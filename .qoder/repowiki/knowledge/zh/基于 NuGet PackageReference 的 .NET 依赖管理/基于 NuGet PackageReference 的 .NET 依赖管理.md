---
kind: dependency_management
name: 基于 NuGet PackageReference 的 .NET 依赖管理
category: dependency_management
scope:
    - '**'
source_files:
    - vehicleManagement.csproj
    - Dockerfile
    - .gitignore
    - .dockerignore
---

## 1. 使用的系统/方法

本项目是 Blazor Server (.NET Web SDK) 应用，依赖管理完全通过 **NuGet** 的 `PackageReference` 机制完成，所有第三方库在根目录的 `vehicleManagement.csproj` 中以 `<PackageReference>` 条目声明。没有使用 `packages.config`、`Directory.Packages.props`（中央包管理）、私有 NuGet 源配置文件（仓库中不存在 `nuget.config` / `NuGet.Config`），也没有 vendor 策略或本地包缓存锁定文件。

构建与发布由 Dockerfile 驱动：先 `dotnet restore` 拉取依赖，再 `dotnet publish` 生成自包含部署产物。Docker 镜像基础镜像为 `mcr.microsoft.com/dotnet/aspnet:10.0`，SDK 阶段使用 `mcr.microsoft.com/dotnet/sdk:10.0`，与项目目标框架 `net10.0` 保持一致。

## 2. 关键文件

- `vehicleManagement.csproj` — 唯一的依赖清单，声明全部 NuGet 包及其版本。
- `Dockerfile` — 定义 `dotnet restore` → `dotnet publish` 的依赖拉取与打包流程。
- `.dockerignore` / `.gitignore` — 排除 `bin/`、`obj/`、`data/`、`logs/` 等生成物，避免将还原产物提交到仓库。
- `vehicleManagement.slnx` — 解决方案文件（存在但未见其内容，通常不直接声明包）。

## 3. 架构与约定

- **单一项目、单一清单**：整个仓库只有一个 `.csproj`，所有依赖集中声明，不存在多项目共享包引用的场景。
- **固定版本号**：每个 `<PackageReference>` 都显式指定了精确版本（如 `Microsoft.EntityFrameworkCore.Sqlite` 10.0.9、`Microsoft.SemanticKernel` 1.77.0、`Serilog.AspNetCore` 10.0.0），未使用浮动版本范围（如 `*` 或 `[1.0,2)`）。这使每次构建可复现，但也意味着升级需要人工修改版本号。
- **工具包隔离**：仅用于编译/迁移的工具包（如 `Microsoft.EntityFrameworkCore.Tools`）通过 `<PrivateAssets>all</PrivateAssets>` 标记，不会传递到运行时依赖图中。
- **AI 相关依赖集中在 aiServer 子模块**：Semantic Kernel 及其 Ollama/OpenAI 连接器 (`Microsoft.SemanticKernel.Connectors.Ollama`、`Microsoft.SemanticKernel.Connectors.OpenAI`) 在 csproj 中统一引入，供 `aiServer/BLL/` 下的 `ChatService`、`IntentDetector`、`DataQuerier` 等类使用。
- **容器化恢复**：Dockerfile 明确先 COPY `vehicleManagement.csproj` 再执行 `dotnet restore`，利用 Docker 层缓存加速依赖拉取；后续才 COPY 源码并 publish。

## 4. 约定与约束

- **包来源**：未发现自定义 `nuget.config`，因此默认使用 nuget.org 公共源；也未配置 `GOPRIVATE` 等私有源环境变量（该变量属于 Go，不适用于 .NET）。
- **无锁文件**：仓库中没有 `packages.lock.json` 或 `global-packages-file` 锁定依赖解析结果，`dotnet restore` 的版本解析可能在不同环境产生差异（尽管版本号已固定，仍可能因源索引差异而不同）。
- **无私有注册表**：未检测到任何私有 NuGet 源、Azure Artifacts、GitHub Packages 或公司内网源的配置。
- **Docker 基础镜像版本对齐**：Dockerfile 中的 ASP.NET Runtime 与 SDK 镜像标签均为 `10.0`，与 csproj 的 `<TargetFramework>net10.0</TargetFramework>` 严格对应，避免运行时/SDK 版本不匹配。
- **构建产物不被版本控制**：`.gitignore` 忽略 `bin/`、`obj/`、`data/`、`logs/`，确保依赖还原产物和数据库文件不会进入 Git，保持依赖声明的唯一真实来源是 `vehicleManagement.csproj`。

总结：这是一个极简的 .NET 依赖管理模式——单 csproj + 精确版本号 + 默认 NuGet 源 + Docker 驱动的 restore/publish 流程，没有引入中央包管理、私有源或依赖锁定文件。