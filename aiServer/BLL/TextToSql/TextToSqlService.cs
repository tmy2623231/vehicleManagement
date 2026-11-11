using System;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using vehicleManagement.aiServer.BLL.Prompts;
using vehicleManagement.DAL;

#nullable enable

namespace vehicleManagement.aiServer.BLL.TextToSql
{
    /// <summary>
    /// Text-to-SQL 副路径：模型在给定的表白名单内生成只读 SQL，经严格校验后在只读连接上执行。
    /// 任何一步不通过都返回 null（静默回退主路径），SQL 细节只进日志、绝不回显给用户。
    /// </summary>
    public class TextToSqlService
    {
        /// <summary>SQL 执行超时（秒）：即使 LLM 生成了全表扫描也不会把对话拖死。</summary>
        public const int CommandTimeoutSeconds = 30;

        private const int MaxCellLength = 80;

        private readonly IDbContextFactory<VehicleDbContext> _dbFactory;
        private readonly Kernel _kernel;
        private readonly AiServerOptions _options;
        private readonly ILogger<TextToSqlService> _logger;

        public TextToSqlService(
            IDbContextFactory<VehicleDbContext> dbFactory,
            Kernel kernel,
            AiServerOptions options,
            ILogger<TextToSqlService> logger)
        {
            _dbFactory = dbFactory;
            _kernel = kernel;
            _options = options;
            _logger = logger;
        }

        /// <summary>
        /// 尝试用模型自主生成的 SQL 回答数据类问题。
        /// 返回 null 表示「没拿到可用数据」，调用方保持主路径行为不变。
        /// </summary>
        public async Task<string?> TryQueryAsync(string question, CancellationToken cancellationToken = default)
        {
            // 开关关闭 / AI 未配置：与改动前完全一致
            if (!_options.TextToSqlEnabled || !_options.IsConfigured)
                return null;

            string? generated = null;
            try
            {
                generated = await GenerateSqlAsync(question, cancellationToken);

                var validation = TextToSqlValidator.Validate(generated);
                if (!validation.IsValid)
                {
                    _logger.LogWarning("Text-to-SQL 被拒绝（{Reason}），回退主路径。模型原始输出：{Sql}",
                        validation.Reason, generated);
                    return null;
                }

                var safeSql = validation.Sql!;
                var (text, rowCount) = await ExecuteReadOnlyAsync(safeSql, cancellationToken);
                _logger.LogInformation("Text-to-SQL 执行完成：行数={RowCount}，SQL={Sql}", rowCount, safeSql);

                // 没查到数据时不注入上下文，让主路径的「未找到数据」表述生效
                return rowCount == 0 ? null : text;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Text-to-SQL 超时（{Seconds}s），回退主路径", _options.EffectiveTimeoutSeconds);
                return null;
            }
            catch (Exception ex)
            {
                // 静默回退：异常细节只进日志，页面不报错
                _logger.LogWarning(ex, "Text-to-SQL 失败，回退主路径；SQL={Sql}", generated);
                return null;
            }
        }

        private async Task<string?> GenerateSqlAsync(string question, CancellationToken cancellationToken)
        {
            var chat = _kernel.GetRequiredService<IChatCompletionService>();

            var history = new ChatHistory(SystemPrompts.BuildTextToSqlPrompt(SqlSchemaWhitelist.DescribeForPrompt()));
            history.AddUserMessage(question);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(_options.EffectiveTimeoutSeconds));

            var response = await chat.GetChatMessageContentAsync(history, cancellationToken: cts.Token);
            return response.Content;
        }

        private async Task<(string Text, int RowCount)> ExecuteReadOnlyAsync(string sql, CancellationToken cancellationToken)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            var rawConnectionString = db.Database.GetConnectionString();
            if (string.IsNullOrWhiteSpace(rawConnectionString))
                throw new InvalidOperationException("缺少 DefaultConnection 连接串，无法执行 Text-to-SQL");

            await using var connection = await OpenReadOnlyAsync(rawConnectionString, cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = CommandTimeoutSeconds;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            var columnCount = reader.FieldCount;
            var headers = new string[columnCount];
            for (var i = 0; i < columnCount; i++)
                headers[i] = reader.GetName(i);

            // 提示词约定的「与数据无关」哨兵结果，不作为数据上下文注入
            if (columnCount == 1 && headers[0].Equals("no_data", StringComparison.OrdinalIgnoreCase))
                return (string.Empty, 0);

            var sb = new StringBuilder();
            sb.AppendLine("【模型自主生成的 SQL 查询结果】");
            sb.AppendLine(string.Join(" | ", headers));

            var rows = 0;
            while (rows < TextToSqlValidator.MaxRows && await reader.ReadAsync(cancellationToken))
            {
                var values = new string[columnCount];
                for (var i = 0; i < columnCount; i++)
                {
                    var value = reader.IsDBNull(i)
                        ? "-"
                        : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture) ?? "-";
                    values[i] = value.Length > MaxCellLength ? value[..MaxCellLength] + "…" : value;
                }

                sb.AppendLine(string.Join(" | ", values));
                rows++;
            }

            sb.AppendLine($"(共 {rows} 行，安全上限 {TextToSqlValidator.MaxRows} 行)");
            return (sb.ToString(), rows);
        }

        /// <summary>
        /// 打开只读连接。优先 ReadOnly；WAL 库在部分环境下只读打开会失败，
        /// 则退回普通连接并立刻 PRAGMA query_only=1 —— 会话级仍然只读，写语句直接被 SQLite 拒绝。
        /// </summary>
        private static async Task<SqliteConnection> OpenReadOnlyAsync(
            string rawConnectionString, CancellationToken cancellationToken)
        {
            var readOnlyBuilder = new SqliteConnectionStringBuilder(rawConnectionString)
            {
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            };

            var connection = new SqliteConnection(readOnlyBuilder.ToString());
            try
            {
                await connection.OpenAsync(cancellationToken);
                return connection;
            }
            catch (SqliteException)
            {
                await connection.DisposeAsync();

                var fallbackBuilder = new SqliteConnectionStringBuilder(rawConnectionString)
                {
                    Mode = SqliteOpenMode.ReadWrite,
                    Pooling = false
                };

                var fallback = new SqliteConnection(fallbackBuilder.ToString());
                await fallback.OpenAsync(cancellationToken);

                await using var pragma = fallback.CreateCommand();
                pragma.CommandText = "PRAGMA query_only = 1;";
                await pragma.ExecuteNonQueryAsync(cancellationToken);

                return fallback;
            }
        }
    }
}
