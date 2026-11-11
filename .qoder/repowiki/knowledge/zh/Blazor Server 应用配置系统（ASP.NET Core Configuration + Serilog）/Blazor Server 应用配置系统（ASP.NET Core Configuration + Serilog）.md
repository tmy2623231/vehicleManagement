---
kind: configuration_system
name: Blazor Server 应用配置系统（ASP.NET Core Configuration + Serilog）
category: configuration_system
scope:
    - '**'
source_files:
    - Program.cs
    - appsettings.json
    - appsettings.Development.json
    - aisettings.example.json
    - vehicleManagement.csproj
---

## 1. 使用的系统与框架

- **ASP.NET Core `IConfiguration`**：通过 `WebApplication.CreateBuilder(args)` 构建的默认配置源，支持 JSON 配置文件、环境变量与命令行参数。
- **Serilog**：在 `Program.cs` 中直接构造 `LoggerConfiguration`，使用 `Serilog.AspNetCore` 集成到宿主，并通过 `builder.Host.UseSerilog()` 注入。
- **EF Core + SQLite**：连接字符串通过 `IConfiguration.GetConnectionString` 读取，数据库文件位于 `data/` 目录。
- **Semantic Kernel + Ollama**：AI 端点与模型名通过独立的 `aisettings.json` 提供，并启用热重载。

## 2. 关键文件

| 文件 | 作用 |
|---|---|
| `Program.cs` | 唯一启动入口，装配所有服务、配置源、中间件与数据库初始化 |
| `appsettings.json` | 主配置文件，定义 `AllowedHosts` 与两个 `ConnectionStrings` |
| `appsettings.Development.json` | 开发环境覆盖，仅开启 `DetailedErrors` |
| `aisettings.example.json` | AI 子系统的可选配置模板（`AiServer:OllamaEndpoint` / `OllamaModel`） |
| `vehicleManagement.csproj` | 声明依赖（Serilog、EF Core、SemanticKernel 等），无自定义 MSBuild 配置项 |
| `Properties/launchSettings.json` | VS/IIS Express 启动设置（未展开） |

## 3. 架构与约定

### 3.1 配置加载顺序

`Program.cs` 中显式追加了一个外部 JSON 配置源：

```csharp
builder.Configuration.AddJsonFile("aisettings.json", optional: true, reloadOnChange: true);
```

因此最终配置键的优先级为（从高到低）：
1. 环境变量 / 命令行参数（由 ASP.NET Core 默认注入）
2. `aisettings.json`（可选，运行时可热重载）
3. `appsettings.json`（默认包含）
4. `appsettings.{Environment}.json`（如 Development）
5. 默认值（代码中的 `?? "http://localhost:11434"` 等）

### 3.2 配置分层

- **基础设施层**：`appsettings.json` 中的 `ConnectionStrings.DefaultConnection` 与 `ConnectionStrings.AiServerConnection` 分别指向 `data/vehicleManagement.db` 与 `data/aiServer.db`。
- **AI 子系统层**：`aisettings.json`（示例见 `aisettings.example.json`）提供 `AiServer:OllamaEndpoint` 与 `AiServer:OllamaModel`，默认值在 `Program.cs` 第 98–99 行回退。
- **运行期开关**：通过 `app.Environment.IsDevelopment()` 控制是否执行种子数据初始化 (`SeedDataInitializer.InitializeAsync`) 与开发者异常页。

### 3.3 配置消费方式

- 连接字符串：`builder.Configuration.GetConnectionString("DefaultConnection")` / `"AiServerConnection"`。
- 标量配置：`builder.Configuration["AiServer:OllamaEndpoint"]` 与 `builder.Configuration["AiServer:OllamaModel"]`。
- 没有使用强类型配置对象（如 `IOptions<T>`），全部通过键路径直接访问。

### 3.4 日志配置

`Program.cs` 在 `WebApplication.CreateBuilder` 之前即创建全局 `Log.Logger`：

- 最低级别：`Information`
- 覆盖：`Microsoft` 与 `Microsoft.EntityFrameworkCore` 降级为 `Warning`
- 输出：控制台 + `logs/log-.log`（按日滚动，保留 7 份）
- 通过 `builder.Host.UseSerilog()` 注入到 DI 容器

### 3.5 资源目录约定

- 数据库文件：`data/`（启动时 `Directory.CreateDirectory` 确保存在）
- 上传附件：`Folder/`（通过 `PhysicalFileProvider` 映射到 `/Folder` URL）
- 静态资源：`wwwroot/`（标准 Blazor Server 静态文件管线）

## 4. 约定与约束

- **连接字符串命名**：必须命名为 `DefaultConnection` 和 `AiServerConnection`，否则 `GetConnectionString` 返回空导致 EF Core 启动失败（由 `UseSqlite(...)` 调用处隐式强制）。
- **AI 配置键路径**：必须遵循 `AiServer:OllamaEndpoint` 与 `AiServer:OllamaModel` 两级结构，对应 `aisettings.example.json` 的键布局；缺失时回退到硬编码默认值，不会报错。
- **`aisettings.json` 是可选的**：`optional: true` 意味着该文件不存在时应用仍可启动；但 `reloadOnChange: true` 使其内容变更后可被 ASP.NET Core 配置系统重新加载。
- **环境区分**：通过 `app.Environment.IsDevelopment()` 分支决定是否执行种子数据与开发者异常页，生产环境不会自动播种。
- **无 `.env` 或 YAML/TOML 支持**：仓库中没有 `.env` 文件，也没有引入 `YamlDotNet`、`Tomlet` 等库；配置来源仅限于 JSON 与 ASP.NET Core 默认的环境变量/命令行注入。
- **无集中配置类**：整个仓库未发现任何 `IOptions<T>`、`Configure<T>` 或强类型配置 POCO，所有配置以字符串键形式直接消费。