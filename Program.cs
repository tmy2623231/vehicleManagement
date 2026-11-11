using System;
using System.IO;
using System.Net.Http;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using vehicleManagement.BLL.Auth;
using vehicleManagement.BLL.Common;
using vehicleManagement.BLL.Vehicle;
using vehicleManagement.BLL.Department;
using vehicleManagement.BLL.Personnel;
using vehicleManagement.BLL.Location;
using vehicleManagement.BLL.UsageRecord;
using vehicleManagement.BLL.Dashboard;
using vehicleManagement.BLL.Execute;
using vehicleManagement.BLL.Maintenance;
using vehicleManagement.BLL.Export;
using vehicleManagement.DAL;
using vehicleManagement.DAL.Model;
using vehicleManagement.DAL.Repository;
using vehicleManagement.aiServer.BLL;
using vehicleManagement.aiServer.BLL.TextToSql;
using vehicleManagement.aiServer.DAL;
using vehicleManagement.aiServer.DAL.Repository;
using Microsoft.Extensions.Primitives;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using OpenAI;
using System.ClientModel;
using System.ClientModel.Primitives;

// Serilog 日志配置：时间戳走 ChinaTimestamp 属性，避免容器 UTC 时差 8 小时
const string consoleTemplate = "[{ChinaTimestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}";
const string fileTemplate = "{ChinaTimestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}";

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()  // 默认级别
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)  // 抑制框架日志
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)  // 抑制 EF Core SQL 日志
    .Enrich.With(new ChinaTimeEnricher())
    .WriteTo.Console(outputTemplate: consoleTemplate)
    .WriteTo.File("logs/log-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7,
        outputTemplate: fileTemplate)
    .CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog();

    // 加载外部 AI 配置文件（可选 + 热重载，方便 Docker 挂载后修改）
    builder.Configuration.AddJsonFile("aisettings.json", optional: true, reloadOnChange: true);
    // 重申环境变量优先级（JSON 文件源后注册会盖掉 Docker ENV）
    builder.Configuration.AddEnvironmentVariables();

    // AI 接入配置绑定：全部集中在 aisettings.json 的 AiServer 节。
    // 取值优先级：环境变量（AiServer__BaseUrl 等）> aisettings.json > AiServerOptions 代码默认值。
    var aiOptions = new AiServerOptions();
    builder.Configuration.GetSection(AiServerOptions.SectionName).Bind(aiOptions);
    builder.Services.AddSingleton(aiOptions);

    // 热重载：aisettings.json 改动（AddJsonFile reloadOnChange: true）后把新值重绑到同一个实例。
    // 运行期每次调用都会读取的键（TextToSqlEnabled / TimeoutSeconds / MaxTokens / Temperature）改完即生效；
    // 连接器参数（Provider / BaseUrl / ApiKey / ModelId / ChatCompletionPath）在启动时已固化进 Kernel，需重启。
    var lastAiSummary = aiOptions.DescribeForLog();
    ChangeToken.OnChange(
        ((IConfigurationRoot)builder.Configuration).GetReloadToken,
        () =>
        {
            builder.Configuration.GetSection(AiServerOptions.SectionName).Bind(aiOptions);
            // 文件监听会连发多次变更，值没变就不重复打日志
            var summary = aiOptions.DescribeForLog();
            if (summary == lastAiSummary) return;
            lastAiSummary = summary;
            Log.Information("AI 配置已热重载：{Summary}", summary);
        });

    builder.Services.AddRazorPages();
    builder.Services.AddServerSideBlazor();

    // Cookie 身份认证；DataProtection 密钥落盘（否则重启即换 → Cookie 全废）
    var keysDir = Path.Combine(builder.Environment.ContentRootPath, "data", "keys");
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(keysDir))
        .SetApplicationName("vehicleManagement");

    builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
        .AddCookie(options =>
        {
            options.LoginPath = "/login";
            options.AccessDeniedPath = "/denied";          // 已登录但无权 → 403 页，不回登录页
            options.Cookie.Name = "vm.auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;    // 扫码跨应用跳转需兼容
            options.ExpireTimeSpan = TimeSpan.FromDays(30); // 长期免登
            options.SlidingExpiration = true;
        });
    builder.Services.AddAuthorization();
    builder.Services.AddCascadingAuthenticationState();     // AuthorizeRouteView 必需
    builder.Services.AddScoped<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
    builder.Services.AddScoped<AuthService>();
    builder.Services.AddScoped<AccountService>();
    builder.Services.AddScoped<ICurrentUser, CurrentUser>();
    builder.Services.AddScoped<AuditService>();

    // SQLite 数据库配置（使用 DbContextFactory 避免 Blazor Server 并发冲突）
    builder.Services.AddDbContextFactory<VehicleDbContext>(options =>
        options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

    // AI 服务独立数据库上下文
    builder.Services.AddDbContextFactory<AiServerDbContext>(options =>
        options.UseSqlite(builder.Configuration.GetConnectionString("AiServerConnection")));

    // 仓储层（DAL）
    builder.Services.AddScoped<VehicleRepository>();
    builder.Services.AddScoped<DepartmentRepository>();
    builder.Services.AddScoped<PersonnelRepository>();
    builder.Services.AddScoped<LocationRepository>();
    builder.Services.AddScoped<UsageRecordRepository>();

    // 维修与导出
    builder.Services.AddScoped<MaintenanceRecordRepository>();

    // AI 服务仓储层
    builder.Services.AddScoped<ChatSessionRepository>();
    builder.Services.AddScoped<ChatMessageRepository>();
    builder.Services.AddScoped<ChatMemoryRepository>();

    // 业务逻辑层（BLL）
    builder.Services.AddScoped<VehicleService>();
    builder.Services.AddScoped<DepartmentService>();
    builder.Services.AddScoped<PersonnelService>();
    builder.Services.AddScoped<LocationService>();
    builder.Services.AddScoped<UsageRecordService>();
    builder.Services.AddScoped<DashboardService>();
    builder.Services.AddScoped<ExecuteService>();

    // 维修与导出
    builder.Services.AddScoped<MaintenanceRecordService>();
    builder.Services.AddScoped<VehicleExportService>();

    // AI 服务业务逻辑层
    builder.Services.AddScoped<MemoryService>();
    builder.Services.AddScoped<DataQuerier>();
    builder.Services.AddScoped<ChatService>();

    // AI 副路径：模型自主 Text-to-SQL（开关 AiServer:TextToSqlEnabled，默认关闭）
    builder.Services.AddScoped<TextToSqlService>();

    // 通用服务
    builder.Services.AddScoped<FileUploadService>();
    builder.Services.AddSingleton<QrCodeService>();

    // Semantic Kernel 注册（默认 OpenAI 兼容连接器；Endpoint 可指向任意中转网关）
    // Provider 可切换：改 aisettings.json 的 AiServer:Provider 即可回退 Ollama，不用改代码。
    var kernelBuilder = Kernel.CreateBuilder();
    if (!aiOptions.IsConfigured)
    {
        // 缺 BaseUrl / ApiKey / ModelId：不抛未捕获异常，降级为「AI 不可用，业务页面不受影响」
        kernelBuilder.Services.AddSingleton<IChatCompletionService>(new UnavailableChatCompletionService());
        Log.Warning("AI 服务未完成配置（缺 {Missing}），/ai-chat 将提示服务不可用，业务页面不受影响。"
                    + "请在 aisettings.json 或环境变量 AiServer__Xxx 中补齐。",
            string.Join(" / ", aiOptions.MissingKeys));
    }
    else if (!Uri.TryCreate(aiOptions.NormalizedBaseUrl, UriKind.Absolute, out var aiEndpoint))
    {
        kernelBuilder.Services.AddSingleton<IChatCompletionService>(new UnavailableChatCompletionService());
        Log.Warning("AiServer:BaseUrl 不是合法的绝对地址（{BaseUrl}），AI 已降级为不可用。",
            aiOptions.NormalizedBaseUrl);
    }
    else if (aiOptions.IsOllama)
    {
        kernelBuilder.AddOllamaChatCompletion(aiOptions.ModelId.Trim(), aiEndpoint);
        Log.Information("AI 连接器：Ollama，{Summary}", aiOptions.DescribeForLog());
    }
    else
    {
        // ChatCompletionPath 与默认值不同时，挂一个改写路由的处理器来适配中转差异
        HttpMessageHandler aiHandler =
            string.Equals(aiOptions.ResolvedChatCompletionPath, AiServerOptions.DefaultChatCompletionPath,
                StringComparison.OrdinalIgnoreCase)
                ? new HttpClientHandler()
                : new ChatCompletionPathHandler(aiOptions.ResolvedChatCompletionPath);

        var aiHttpClient = new HttpClient(aiHandler)
        {
            Timeout = TimeSpan.FromSeconds(aiOptions.EffectiveTimeoutSeconds)
        };

        var openAiClient = new OpenAIClient(
            new ApiKeyCredential(aiOptions.ApiKey.Trim()),
            new OpenAIClientOptions
            {
                Endpoint = aiEndpoint,
                Transport = new HttpClientPipelineTransport(aiHttpClient)
            });

        kernelBuilder.AddOpenAIChatCompletion(aiOptions.ModelId.Trim(), openAiClient);
        Log.Information("AI 连接器：OpenAI 兼容，{Summary}", aiOptions.DescribeForLog());
    }

    builder.Services.AddSingleton(kernelBuilder.Build());

    var app = builder.Build();

    // 确保 data/ 目录存在
    Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, "data"));
    app.Logger.LogInformation("DataProtection 密钥目录：{Dir}", Path.GetFullPath(keysDir));

    // 确保数据库表已创建 + 种子数据初始化
    using (var scope = app.Services.CreateScope())
    {
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<VehicleDbContext>>();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            await db.Database.EnsureCreatedAsync();

            // 幂等补齐新增表：EnsureCreatedAsync 对已存在的库是 no-op，AppUsers/AuditLogs 只能靠这里建
            await SchemaBootstrapper.ApplyAsync(db, app.Logger);

            // 业务必需的参考数据：任何环境都执行，但只在完全空库时补占位（不会污染已有业务数据）
            await SeedDataInitializer.EnsureReferenceDataAsync(db);

            // 演示数据：必须同时满足 Development + 显式配置开关，默认关闭（避免开发库与生产库数据漂移）
            if (app.Environment.IsDevelopment() && app.Configuration.GetValue("Seed:DemoEnabled", false))
                await SeedDataInitializer.SeedDemoDataAsync(db);
        }

        // 账号初始化：必须在 SchemaBootstrapper 之后（AppUsers 表已存在），且幂等可重复执行
        await UserSeeder.EnsureSeedUsersAsync(
            scope.ServiceProvider.GetRequiredService<IDbContextFactory<VehicleDbContext>>(),
            scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>(),
            app.Logger);

        var aiDbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AiServerDbContext>>();
        await using (var aiDb = await aiDbFactory.CreateDbContextAsync())
        {
            await aiDb.Database.EnsureCreatedAsync();
        }
    }

    if (app.Environment.IsDevelopment())
        app.UseDeveloperExceptionPage();
    else
        app.UseExceptionHandler("/Error");

    app.UseStaticFiles();

    // 上传附件收权：不再用 UseStaticFiles 公开映射 Folder/，改为带认证的端点（P1-3）
    var folderRoot = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "Folder"));
    Directory.CreateDirectory(folderRoot);

    app.UseRouting();

    // 认证/授权必须夹在 UseRouting 之后、MapBlazorHub 之前，否则 Circuit 拿不到 ClaimsPrincipal
    app.UseAuthentication();
    app.UseAuthorization();

    // 未登录整页访问：服务端直接 302 到登录页（扫码链路走 /m/login）。
    // 不在组件里 NavigateTo —— 预渲染阶段那样会抛 NavigationException。
    app.Use(async (ctx, next) =>
    {
        var path = ctx.Request.Path.Value ?? "/";
        var isPageRequest = HttpMethods.IsGet(ctx.Request.Method) || HttpMethods.IsHead(ctx.Request.Method);

        if (!isPageRequest || ctx.User.Identity?.IsAuthenticated == true || IsPublicPath(path))
        {
            await next();
            return;
        }

        var loginPath = IsScanFlow(path) ? "/m/login" : "/login";
        var returnUrl = Uri.EscapeDataString($"{ctx.Request.Path}{ctx.Request.QueryString}");
        ctx.Response.Redirect($"{loginPath}?returnUrl={returnUrl}");
    });

    // 免登录可达：两套登录页、403 页、健康检查、Blazor 自身资源
    static bool IsPublicPath(string path)
        => path.Equals("/login", StringComparison.OrdinalIgnoreCase)
           || path.Equals("/m/login", StringComparison.OrdinalIgnoreCase)
           || path.Equals("/denied", StringComparison.OrdinalIgnoreCase)
           || path.Equals("/health", StringComparison.OrdinalIgnoreCase)
           || path.Equals("/Error", StringComparison.OrdinalIgnoreCase)
           || path.StartsWith("/_blazor", StringComparison.OrdinalIgnoreCase)
           || path.StartsWith("/_framework", StringComparison.OrdinalIgnoreCase);

    // 扫码/登记链路：登录页也必须用移动端那一套
    static bool IsScanFlow(string path)
        => path.StartsWith("/scan", StringComparison.OrdinalIgnoreCase)
           || path.StartsWith("/execute/", StringComparison.OrdinalIgnoreCase);

    // 退出登录：只保留整页 POST（电路内跳转不刷新认证状态）；back=mobile 时回移动端登录页
    app.MapPost("/auth/logout", async (HttpContext ctx) =>
    {
        await ctx.SignOutAsync();
        var target = ctx.Request.Query["back"] == "mobile" ? "/m/login" : "/login";
        return Results.Redirect(target);
    });

    app.MapRazorPages();
    app.MapBlazorHub();
    app.MapGet("/health", () => Results.Ok("健康"));

    app.MapGet("/Folder/{**path}", (string path) =>
    {
        var root = folderRoot;
        var full = Path.GetFullPath(Path.Combine(root, path));
        // 路径穿越兜底：解析后的绝对路径必须仍在根目录内
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !File.Exists(full))
            return Results.NotFound();

        var provider = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
        if (!provider.TryGetContentType(full, out var contentType))
            contentType = "application/octet-stream";
        return Results.File(full, contentType, enableRangeProcessing: true);
    }).RequireAuthorization();

    app.MapFallbackToPage("/_Host");

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "应用程序启动失败");
    // 静默退出时退出码为 0，容器编排会当成正常结束而不重启 —— 必须显式失败
    Environment.ExitCode = 1;
}   
finally
{
    Log.CloseAndFlush();
}
