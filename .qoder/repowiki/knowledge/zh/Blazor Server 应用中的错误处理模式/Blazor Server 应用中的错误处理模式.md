---
kind: error_handling
name: Blazor Server 应用中的错误处理模式
category: error_handling
scope:
    - '**'
source_files:
    - Program.cs
    - Pages/Error.cshtml
    - BLL/Common/FileUploadService.cs
    - BLL/Common/QrCodeService.cs
    - BLL/Execute/ExecuteService.cs
    - Pages/Execute.razor.cs
    - aiServer/BLL/ChatService.cs
    - aiServer/BLL/DataQuerier.cs
    - aiServer/BLL/MemoryService.cs
---

## 1. 总体方式

仓库是一个 Blazor Server + ASP.NET Core 应用，未定义自定义异常类型或统一的错误码体系。错误处理主要依赖以下手段：
- 启动期全局异常捕获（`Program.cs`）
- ASP.NET Core 开发/生产两套异常页面（`UseDeveloperExceptionPage` / `UseExceptionHandler("/Error")`）
- BLL 层对可恢复失败返回 `(bool Success, string ErrorMessage)` 元组；对参数校验等不可恢复输入抛出 `ArgumentException`
- UI 层用 try/catch 捕获并写入组件级 `errorMessage` / `maintError` / `borrowError` 字段
- Serilog 作为统一日志通道，输出到控制台与按日滚动的文件 `logs/log-.log`

## 2. 关键文件

- `Program.cs` — 应用启动、Serilog 配置、异常中间件注册、数据库初始化
- `Pages/Error.cshtml` — 生产环境默认错误页
- `BLL/Common/FileUploadService.cs` — 上传参数校验抛 `ArgumentException`
- `BLL/Common/QrCodeService.cs` — QR 生成参数校验抛 `ArgumentException`
- `BLL/Execute/ExecuteService.cs` — 保存操作返回 `(Success, ErrorMessage)` 元组
- `Pages/Execute.razor.cs` — UI 侧 try/catch 收集错误信息
- `aiServer/BLL/ChatService.cs`、`aiServer/BLL/DataQuerier.cs`、`aiServer/BLL/MemoryService.cs` — AI 子模块的 try/catch 模式

## 3. 架构与约定

### 3.1 启动期异常
`Program.cs` 将 `WebApplication.CreateBuilder(...)` 到 `app.Run()` 整个流程包裹在 `try { ... } catch (Exception ex) { Log.Fatal(ex, "应用程序启动失败"); } finally { Log.CloseAndFlush(); }` 中，确保启动失败通过 Serilog 记录。

### 3.2 请求级异常中间件
```csharp
if (app.Environment.IsDevelopment())
    app.UseDeveloperExceptionPage();
else
    app.UseExceptionHandler("/Error");
```
开发环境使用内置开发者异常页；生产环境统一跳转到 `/Error` 路由对应的 `Pages/Error.cshtml`。该页面是模板生成的标准错误页，提示切换到 Development 环境以显示详细信息，并警告不应在生产启用开发环境。

### 3.3 BLL 层的两种错误表达
- **参数校验类错误**：直接抛 `ArgumentException`，例如 `FileUploadService.UploadFileAsync` 对空文件、不支持格式、超限大小抛出，消息为中文（"文件不能为空"、"不支持的文件格式"、"文件大小不能超过..."）。`QrCodeService` 也对空内容/Token 抛 `ArgumentException`。
- **业务可恢复失败**：`ExecuteService.SaveRecordAsync` 返回 `(bool Success, string ErrorMessage)` 元组，成功时 `ErrorMessage` 为 null，失败时包含用户可读消息（如 "请选择使用人员"、"保存失败：..."）。调用方（`Execute.razor.cs`）根据 `success` 分支设置 UI 错误字段。

### 3.4 UI 层错误收集
`Execute.razor.cs` 在每个异步操作中使用 try/catch，将 `ex.Message` 拼接到组件字段：
- `errorMessage`（车辆使用）
- `maintError`（维修）
- `borrowError`（借车登记）
同时提供 `ClearErrors()` 方法清空所有错误字段。JS 互操作调用（如 `JS.InvokeVoidAsync("close")`）使用裸 `catch { }` 吞掉异常，避免关闭弹窗失败影响主流程。

### 3.5 AI 子模块
AI 子模块 (`aiServer/BLL/`) 同样采用 try/catch 包裹数据库/外部调用，并在 catch 中记录日志或返回错误状态，未引入自定义异常类型。

## 4. 约定与约束（描述性观察）

- 参数合法性校验通过抛出 `System.ArgumentException` 表达，消息为中文，由调用方 UI 捕获后展示。
- 业务逻辑的可预期失败通过返回值 `(bool Success, string ErrorMessage)` 传递，而非抛异常。
- 所有运行时异常最终被三层捕获：UI 层 try/catch → BLL 层 try/catch → ASP.NET Core 异常中间件 → Serilog。
- 启动期异常不向上冒泡，而是经 `Log.Fatal` 记录后程序退出。
- 没有发现自定义异常类型、错误码枚举、`throw new` 包装器或 `try/finally` 资源清理之外的 recover 机制。
- 没有全局 Blazor ErrorBoundary 或自定义 `IExceptionHandler` 实现——异常处理集中在 `Program.cs` 的中间件和各个组件/服务内部。
- 日志级别通过 Serilog 配置：默认 `Information`，`Microsoft` 与 `Microsoft.EntityFrameworkCore` 降级为 `Warning`，输出模板包含 `{Exception}` 占位符。