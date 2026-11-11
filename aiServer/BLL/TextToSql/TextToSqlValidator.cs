using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

#nullable enable

namespace vehicleManagement.aiServer.BLL.TextToSql
{
    /// <summary>Text-to-SQL 校验结果。IsValid=false 时 Reason 说明拒绝原因（只写日志，不回显给用户）。</summary>
    public sealed class SqlValidationResult
    {
        private SqlValidationResult(bool isValid, string? sql, string? reason)
        {
            IsValid = isValid;
            Sql = sql;
            Reason = reason;
        }

        public bool IsValid { get; }

        /// <summary>规整并强制附加 LIMIT 后的 SQL（仅 IsValid=true 时有值）。</summary>
        public string? Sql { get; }

        public string? Reason { get; }

        public static SqlValidationResult Ok(string sql) => new(true, sql, null);

        public static SqlValidationResult Reject(string reason) => new(false, null, reason);
    }

    /// <summary>
    /// 模型自主生成的 SQL 的准入闸门（纯字符串校验，不碰数据库）。
    /// 安全边界：只允许单条 SELECT / WITH 查询；拒绝注释、多语句、DDL/DML/PRAGMA/ATTACH；
    /// 表名列名必须落在 <see cref="SqlSchemaWhitelist"/> 内；强制附加 LIMIT（≤200）。
    /// </summary>
    public static class TextToSqlValidator
    {
        /// <summary>行数上限：强制附加 / 夹断到该值。</summary>
        public const int MaxRows = 200;

        private static readonly string[] ForbiddenKeywords =
        {
            "insert", "update", "delete", "drop", "alter", "create", "truncate", "attach", "detach",
            "pragma", "vacuum", "reindex", "begin", "commit", "rollback", "savepoint", "release",
            "upsert", "merge", "grant", "revoke", "exec", "execute", "load_extension", "into", "trigger"
        };

        /// <summary>允许出现的 SQL 关键字 / 函数 / 类型名；其余标识符必须在表列白名单或别名里。</summary>
        private static readonly HashSet<string> AllowedWords = new(StringComparer.OrdinalIgnoreCase)
        {
            // 结构关键字
            "select", "from", "where", "group", "order", "by", "having", "limit", "offset",
            "as", "and", "or", "not", "in", "is", "null", "like", "glob", "between", "exists",
            "case", "when", "then", "else", "end", "distinct", "all", "any", "union", "intersect", "except",
            "join", "inner", "left", "outer", "right", "full", "cross", "natural", "on", "using",
            "asc", "desc", "nulls", "first", "last", "collate", "with", "recursive", "over", "partition",
            "filter", "values", "true", "false",
            // 聚合 / 标量函数
            "count", "sum", "avg", "min", "max", "total", "group_concat",
            "abs", "round", "length", "lower", "upper", "trim", "ltrim", "rtrim",
            "substr", "substring", "replace", "ifnull", "nullif", "coalesce", "cast",
            "date", "time", "datetime", "julianday", "strftime", "unixepoch",
            "typeof", "printf", "format", "instr", "hex", "quote", "random", "char",
            // CAST 的目标类型
            "integer", "int", "real", "text", "blob", "numeric", "decimal", "float", "double",
            "boolean", "varchar"
        };

        private static readonly Regex FirstWordRegex =
            new(@"^\s*(?<word>[A-Za-z_][A-Za-z0-9_]*)\b", RegexOptions.Compiled);

        private static readonly Regex IdentifierRegex =
            new(@"[A-Za-z_][A-Za-z0-9_]*", RegexOptions.Compiled);

        private static readonly Regex StringLiteralRegex =
            new(@"'(?:[^']|'')*'", RegexOptions.Compiled);

        private static readonly Regex AliasRegex =
            new(@"(?is)\b(?:from|join)\s+(?<table>[A-Za-z_][A-Za-z0-9_]*)(?:\s+(?:as\s+)?(?<alias>[A-Za-z_][A-Za-z0-9_]*))?",
                RegexOptions.Compiled);

        /// <summary>SELECT 列表里的 AS 别名（如 COUNT(*) AS total），否则会被当成越界标识符。</summary>
        private static readonly Regex AsAliasRegex =
            new(@"(?is)\bas\s+(?<alias>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);

        private static readonly Regex CteRegex =
            new(@"(?is)(?:\bwith\s+|,\s*)(?<name>[A-Za-z_][A-Za-z0-9_]*)\s+as\s*\(", RegexOptions.Compiled);

        private static readonly Regex LimitRegex =
            new(@"(?is)\blimit\s+(?<n>\d+)", RegexOptions.Compiled);

        public static SqlValidationResult Validate(string? rawSql)
        {
            if (string.IsNullOrWhiteSpace(rawSql))
                return SqlValidationResult.Reject("SQL 为空");

            // 模型偶尔会用 Markdown 代码块包裹
            var sql = rawSql.Replace("```sql", "", StringComparison.OrdinalIgnoreCase)
                            .Replace("```", "", StringComparison.Ordinal)
                            .Trim();

            if (sql.Contains("--", StringComparison.Ordinal)
                || sql.Contains("/*", StringComparison.Ordinal)
                || sql.Contains("*/", StringComparison.Ordinal))
                return SqlValidationResult.Reject("禁止 SQL 注释");

            sql = sql.TrimEnd().TrimEnd(';').TrimEnd();
            if (sql.Length == 0)
                return SqlValidationResult.Reject("SQL 为空");
            if (sql.Contains(';', StringComparison.Ordinal))
                return SqlValidationResult.Reject("只允许单条语句");

            var first = FirstWordRegex.Match(sql);
            if (!first.Success)
                return SqlValidationResult.Reject("无法识别语句类型");
            var firstWord = first.Groups["word"].Value;
            if (!firstWord.Equals("select", StringComparison.OrdinalIgnoreCase)
                && !firstWord.Equals("with", StringComparison.OrdinalIgnoreCase))
                return SqlValidationResult.Reject($"只允许 SELECT / WITH，实际为 {firstWord}");

            // 去掉字符串字面量后再做关键字与标识符扫描，避免把数据当语法
            var stripped = StringLiteralRegex.Replace(sql, "''");

            if (stripped.Contains('?', StringComparison.Ordinal))
                return SqlValidationResult.Reject("禁止参数占位符");

            foreach (var keyword in ForbiddenKeywords)
            {
                if (Regex.IsMatch(stripped, $@"(?i)\b{keyword}\b", RegexOptions.None, TimeSpan.FromSeconds(1)))
                    return SqlValidationResult.Reject($"禁止关键字 {keyword}");
            }

            foreach (var forbidden in SqlSchemaWhitelist.ForbiddenIdentifiers)
            {
                if (Regex.IsMatch(stripped, $@"(?i)\b{Regex.Escape(forbidden)}\b"))
                    return SqlValidationResult.Reject($"禁止访问 {forbidden}");
            }

            var aliases = CollectAliases(stripped);

            foreach (Match match in IdentifierRegex.Matches(stripped))
            {
                var token = match.Value;
                if (AllowedWords.Contains(token)) continue;
                if (SqlSchemaWhitelist.IsTableAllowed(token)) continue;
                if (SqlSchemaWhitelist.IsColumnAllowed(token)) continue;
                if (aliases.Contains(token)) continue;
                return SqlValidationResult.Reject($"未在白名单内的标识符：{token}");
            }

            // LIMIT 必须落在原始 SQL 上（stripped 里的字符串字面量已被置空，不能拿去执行）
            var limited = EnforceLimit(sql);
            return SqlValidationResult.Ok(limited);
        }

        private static HashSet<string> CollectAliases(string sql)
        {
            var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (Match match in AliasRegex.Matches(sql))
            {
                if (!match.Groups["alias"].Success) continue;
                var alias = match.Groups["alias"].Value;
                if (AllowedWords.Contains(alias)) continue;
                aliases.Add(alias);
            }

            foreach (Match match in AsAliasRegex.Matches(sql))
            {
                var alias = match.Groups["alias"].Value;
                if (AllowedWords.Contains(alias)) continue;
                aliases.Add(alias);
            }

            foreach (Match match in CteRegex.Matches(sql))
                aliases.Add(match.Groups["name"].Value);

            return aliases;
        }

        /// <summary>没有 LIMIT 就补一个；有但超过上限就夹断到上限。</summary>
        private static string EnforceLimit(string sql)
        {
            var match = LimitRegex.Match(sql);
            if (!match.Success)
                return sql + $" LIMIT {MaxRows}";

            var current = match.Groups["n"].Value;
            if (!int.TryParse(current, out var value) || value <= 0 || value > MaxRows)
                return sql[..match.Groups["n"].Index] + MaxRows + sql[(match.Groups["n"].Index + current.Length)..];

            return sql;
        }
    }
}
