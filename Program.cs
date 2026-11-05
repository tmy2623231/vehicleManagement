using System;
using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Serilog;
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
using vehicleManagement.DAL.Repository;
using vehicleManagement.aiServer.BLL;
using vehicleManagement.aiServer.DAL;
using vehicleManagement.aiServer.DAL.Repository;
using Microsoft.SemanticKernel;

// Serilog 日志配置
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()  // 默认级别
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)  // 抑制框架日志
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)  // 抑制 EF Core SQL 日志
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File("logs/log-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7)
    .CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog();

    // 加载外部 AI 配置文件（可选 + 热重载，方便 Docker 挂载后修改）
    builder.Configuration.AddJsonFile("aisettings.json", optional: true, reloadOnChange: true);

    builder.Services.AddRazorPages();
    builder.Services.AddServerSideBlazor();
    builder.Services.AddHttpContextAccessor();

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

    // 通用服务
    builder.Services.AddScoped<FileUploadService>();
    builder.Services.AddSingleton<QrCodeService>();

    // Semantic Kernel 注册（连接 Ollama）
    var ollamaEndpoint = builder.Configuration["AiServer:OllamaEndpoint"] ?? "http://localhost:11434";
    var ollamaModel = builder.Configuration["AiServer:OllamaModel"] ?? "gemma4:e2b";
    var kernelBuilder = Kernel.CreateBuilder();
    kernelBuilder.AddOllamaChatCompletion(ollamaModel, new Uri(ollamaEndpoint));
    builder.Services.AddSingleton(kernelBuilder.Build());

    var app = builder.Build();

    // 确保 data/ 目录存在
    Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, "data"));

    // 确保数据库表已创建 + 种子数据初始化
    using (var scope = app.Services.CreateScope())
    {
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<VehicleDbContext>>();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            await db.Database.EnsureCreatedAsync();
            if (app.Environment.IsDevelopment())
                await SeedDataInitializer.InitializeAsync(db);
        }

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

    // 提供 Folder/ 目录的静态文件访问（上传附件）
    Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, "Folder"));
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(Path.Combine(app.Environment.ContentRootPath, "Folder")),
        RequestPath = "/Folder"
    });

    app.UseRouting();

    app.MapBlazorHub();
    app.MapGet("/health", () => Results.Ok("健康"));
    app.MapFallbackToPage("/_Host");

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "应用程序启动失败");
}   
finally
{
    Log.CloseAndFlush();
}
