---
kind: logging_system
name: 基于 Serilog 的 Blazor Server 应用日志系统
category: logging_system
scope:
    - '**'
source_files:
    - Program.cs
    - vehicleManagement.csproj
    - DAL/SeedDataInitializer.cs
    - BLL/Vehicle/VehicleService.cs
    - BLL/UsageRecord/UsageRecordService.cs
    - BLL/Maintenance/MaintenanceRecordService.cs
    - aiServer/BLL/MemoryService.cs
---

## 1. 使用的框架与工具

项目使用 **Serilog**（`Serilog 4.3.1`、`Serilog.AspNetCore 10.0.0`、`Serilog.Sinks.File 7.0.0`）作为统一的日志框架，通过 `Microsoft.Extensions.Logging` 抽象在业务层注入 `ILogger<T>`。没有引入结构化日志字段扩展或第三方 sink（如 Seq、Elastic），仅使用控制台和文件两个输出目标。

## 2. 关键文件

- `Program.cs`：全局 Serilog 初始化、主机集成、启动/关闭生命周期处理。
- `vehicleManagement.csproj`：声明 Serilog 相关 NuGet 包引用。
- `DAL/SeedDataInitializer.cs`：种子数据阶段直接使用静态 `Log.Information/Warning` 输出。
- `BLL/Vehicle/VehicleService.cs`、`BLL/UsageRecord/UsageRecordService.cs`、`BLL/Maintenance/MaintenanceRecordService.cs`、`aiServer/BLL/MemoryService.cs`：通过构造函数注入 `ILogger<T>` 进行业务日志记录。
- `logs/log-*.log`：按日滚动的文件日志输出目录。

## 3. 架构与约定

### 3.1 初始化位置与生命周期

日志配置位于 `Program.cs` 顶部（第 29–37 行），在 `WebApplication.CreateBuilder` 之前执行，确保 ASP.NET Core 管道构建时即可消费日志：

```csharp
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File("logs/log-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7)
    .CreateLogger();
```

随后通过 `builder.Host.UseSerilog()`（第 43 行）将 Serilog 接入 `Microsoft.Extensions.Logging` 管道。应用启动异常由顶层 `try/catch` 捕获并通过 `Log.Fatal(ex, "应用程序启动失败")` 输出；`finally` 块中调用 `Log.CloseAndFlush()` 确保日志刷盘。

### 3.2 日志级别策略

| 来源 | 最低级别 |
|---|---|
| 应用自身 | `Information` |
| `Microsoft.*` 框架日志 | `Warning`（抑制默认信息级框架噪音） |
| `Microsoft.EntityFrameworkCore` | `Warning`（抑制 EF Core SQL 语句日志） |

### 3.3 输出目标（Sink）

- **Console sink**：模板为 `[时间 HH:mm:ss] 级别 消息\n异常`，使用 `{Message:lj}` 保留换行。
- **File sink**：路径 `logs/log-.log`，按天滚动（`RollingInterval.Day`），最多保留 7 个历史文件。仓库中 `logs/` 目录下已存在多个按日期命名的日志文件（如 `log-20260820.log`），印证该策略已在运行中被验证。

### 3.4 业务层使用方式

业务服务通过依赖注入获取 `ILogger<T>`，以类型化 logger 命名空间（例如 `ILogger<VehicleService>`、`ILogger<UsageRecordService>`、`ILogger<MaintenanceRecordService>`、`ILogger<MemoryService>`）。种子初始化阶段则直接调用静态 `Serilog.Log` API（`Log.Information` / `Log.Warning`）。

### 3.5 结构化字段

当前代码未使用 Serilog 的结构化属性语法（如 `{VehicleId}`、`{DepartmentName}` 等上下文字段），日志消息均为纯字符串插值。`SeedDataInitializer.cs` 中的占位符（如 `{DeptCount}部门 {PersonCount}人 {VehicleCount}车`）是 `string.Format` 风格的参数，并非 Serilog 结构化字段。

## 4. 约定与约束

- **统一入口**：所有日志通过 Program.cs 中配置的 Serilog 实例产生，未在业务代码中自行创建 `Logger` 实例。
- **框架噪音抑制**：对 `Microsoft` 与 `Microsoft.EntityFrameworkCore` 命名空间的日志强制降级到 `Warning`，避免控制台被框架内部日志淹没。
- **文件轮转规则**：日志文件按自然日切分，文件名格式固定为 `log-YYYYMMDD.log`，最多保留 7 份历史文件（由 `retainedFileCountLimit: 7` 强制执行）。
- **异常处理**：应用启动阶段的异常统一通过 `Log.Fatal` 记录后退出，正常流程不捕获业务异常并写日志——业务异常应通过 ASP.NET Core 的异常处理中间件（开发环境 `UseDeveloperExceptionPage`，生产环境 `/Error` 页面）处理。
- **无外部 sink**：项目中未引入 Seq、Elasticsearch、Azure Application Insights 等远程 sink，所有日志均落地本地文件系统与标准输出。