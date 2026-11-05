# 车辆管理系统 · 内置本地大模型 AI 助手

C# / .NET 10 Blazor Server 车辆（公车）管理系统，业务侧覆盖车辆、部门、人员、地点、使用记录、维修记录；
在同一套后端里嵌入了一个 **只用本地 Ollama 推理** 的自然语言查询助手——业务人员问「目前有哪些车在用」「最近一周有哪些使用记录」，系统查真实数据库并把结果交给本地小模型组织成自然语言回答。

> 简历关联：[田茂屹 · 求职意向 AI Agent 工程师](https://gitea.tianmaoyi.cn:4443/maoyi/tianmaoyi-resume)
> 本仓库对应其中的「车辆管理系统 · AI 智能助手」自研项目。

---

## 1. 技术栈

| 分类 | 选型 | 版本（取自 `vehicleManagement.csproj`） |
| --- | --- | --- |
| 运行时 / Web | .NET / ASP.NET Core Blazor Server（`Microsoft.NET.Sdk.Web`） | `net10.0` |
| UI 组件 | Microsoft Fluent UI Blazor Components | `4.14.3` |
| ORM / 存储 | EF Core + SQLite（双库：业务库 + AI 库） | `Microsoft.EntityFrameworkCore.Sqlite 10.0.9` |
| LLM 编排 | Semantic Kernel + Ollama Chat Completion Connector | `Microsoft.SemanticKernel 1.77.0` / `...Connectors.Ollama 1.77.0-alpha` |
| 日志 | Serilog（Console + 按天滚动文件，保留 7 份） | `Serilog.AspNetCore 10.0.0` |
| 导出 | DocumentFormat.OpenXml（车辆信息 xlsx 导出） | `3.5.1` |
| 二维码 | QRCoder（一车一码，司机扫码执行出车） | `1.8.0` |
| 前端图表 | Chart.js（本机静态资源，不引 CDN） | `wwwroot/js/chart.umd.min.js` |
| 容器 | 多阶段 Dockerfile + HEALTHCHECK | `mcr.microsoft.com/dotnet/{sdk,aspnet}:10.0` |

## 2. 功能模块

| 路由 | 页面 |
| --- | --- |
| `/` | 仪表盘：车辆总览、部门车辆分布、近 6 个月用车趋势 |
| `/vehicles` | 车辆台账（增删改查、二维码、附件） |
| `/departments` `/personnel` `/locations` | 部门 / 人员 / 地点主数据 |
| `/usage-records` | 使用记录（出车、里程、到达地点、用途） |
| `/borrow/{VehicleId:int}` | 出车申请 |
| `/maintenance/{VehicleId:int}` `/maintenance-records` | 维修登记与维修记录 |
| `/execute/{Token}` | 司机扫码执行页（按车辆二维码 Token 定位单据） |
| `/ai-chat` | **AI 助手**：多会话、记忆面板、移动端侧边栏 |
| `/health` | 容器健康检查探针 |

分层：`DAL`（EF Core 模型 / 仓储）→ `BLL`（业务服务）→ `Pages` + `Components`（Blazor UI）→ `aiServer`（AI 子系统，独立命名空间与独立数据库）。

---

## 3. 本地 AI 接入（重点）

### 3.1 部署形态：推理不出环境

模型跑在本地 / 内网的 Ollama 上，应用侧只有一个 HTTP 出口，没有云端 API Key，业务数据不需要脱敏出网。

```
Blazor Server (vehicleManagement)
   │  Semantic Kernel IChatCompletionService
   ▼
Ollama REST  http://<host>:11434  →  本地小模型（Gemma / Qwen 等 4B 级）
   ▲
   └ 同一进程内另有 SQLite：vehicleManagement.db（业务） / aiServer.db（会话·消息·记忆）
```

### 3.2 配置的三级取值

| 优先级 | 来源 | 位置 |
| --- | --- | --- |
| 1 | 环境变量 `AiServer__OllamaEndpoint` / `AiServer__OllamaModel` | Docker 部署注入 |
| 2 | `aisettings.json`（**可选 + 热重载**，改完不用重启） | `Program.cs:46` |
| 3 | 代码兜底 `http://localhost:11434` | `Program.cs:98` |

```csharp
// Program.cs:46 —— 外部配置文件，optional 保证缺文件也能起，reloadOnChange 支持挂载后热改
builder.Configuration.AddJsonFile("aisettings.json", optional: true, reloadOnChange: true);

// Program.cs:98-102 —— 唯一一处模型接入点，Ollama 走 SK 官方 connector
var ollamaEndpoint = builder.Configuration["AiServer:OllamaEndpoint"] ?? "http://localhost:11434";
var ollamaModel    = builder.Configuration["AiServer:OllamaModel"]    ?? "gemma4:e2b";
var kernelBuilder  = Kernel.CreateBuilder();
kernelBuilder.AddOllamaChatCompletion(ollamaModel, new Uri(ollamaEndpoint));
builder.Services.AddSingleton(kernelBuilder.Build());   // Kernel 无状态，注册为单例
```

`Kernel` 以单例注入，`ChatService` 通过 `kernel.GetRequiredService<IChatCompletionService>()` 拿到推理接口（`ChatService.cs:41`）——上层的意图识别、查询、提示词全部与具体模型解耦，换模型只改配置。

### 3.3 一次问答的完整链路

`ChatService.SendMessageAsync()`（`ChatService.cs:94-139`）：

```
用户输入
  │
  ├ 1. 落库 user 消息 + 自动起会话标题        ChatService.cs:96-107
  ├ 2. 意图识别（规则，不调模型）              IntentDetector.Detect()          → aiServer/BLL/IntentDetector.cs:17
  ├ 3. 按意图查业务库，格式化成 LLM 可读文本    DataQuerier.QueryAsync()         → aiServer/BLL/DataQuerier.cs:48
  ├ 4. 组装 ChatHistory（4 层拼接）            ChatService.BuildChatHistory()   → aiServer/BLL/ChatService.cs:161
  ├ 5. 单次调用 Ollama，异常本地兜底           ChatService.CallLlmAsync()       → aiServer/BLL/ChatService.cs:188
  ├ 6. 落库 assistant 消息                     ChatService.cs:122-129
  └ 7. 异步（非关键路径）抽取本轮记忆           MemoryService.ExtractAndStoreAsync → aiServer/BLL/MemoryService.cs:45
```

关键点：**模型只被调用一次**。取数不靠模型生成 SQL，也不靠模型决定调用哪个工具。

### 3.4 意图路由表

`UserIntent`（`aiServer/BLL/Enums/UserIntent.cs`）共 7 类，`IntentDetector` 按「统计 → 使用记录 → 在用车辆 → 车辆 → 部门 → 人员 → 车牌正则 → 闲聊」的**固定优先级**做关键词匹配（`IntentDetector.cs:17-111`），命中即路由到 `DataQuerier` 的一个白名单方法：

| 意图 | 触发词（节选） | 落到哪 | 取数内容 |
| --- | --- | --- | --- |
| `VehicleStats` | 统计、汇总、多少辆、占比、分布、趋势、哪个部门、最多 | `QueryVehicleStatsAsync` | 总数 / 在用 / 维修 / 退役 + 各部门车辆分布 + 近 6 个月趋势 |
| `UsageRecordQuery` | 使用记录、出车、用车、行程 | `QueryUsageRecordsAsync` | 时间·车牌·驾驶员·部门·用途（支持中文时间范围） |
| `ActiveVehicleQuery` | 在用车辆、当前可用、有哪些车、车辆列表 | `QueryActiveVehiclesAsync` | 全部在用车辆明细 |
| `VehicleQuery` | 车辆、车牌、品牌、型号、状态、公里，或裸车牌 `京A12345` | `QueryVehiclesAsync` | 关键词过滤后的车辆台账 |
| `DepartmentQuery` | 部门、单位、科室 | `QueryDepartmentsAsync` | 部门 + （暗示问人时）交叉带出各部门人员 |
| `PersonnelQuery` | 人员、员工、司机、姓名、电话 | `QueryPersonnelAsync` | 人员 + （暗示问车时）交叉带出其用车记录 |
| `GeneralChat` | 你好、谢谢、帮助 | 不查库 | 纯提示词问答 |

两个容易踩的实现细节，代码里做了显式处理：

- **优先级冲突**：`统计`类词先判，否则「哪个部门用车最多」会被 `用车` 抢去走使用记录查询；`在用车辆`先于`车辆`，否则「有哪些车在用」会被截成车辆模糊搜索（`IntentDetector.cs:21-58`）。
- **残留关键词清洗**：去掉触发词和停用词后剩下的片段还要过 `IsMeaningfulKeyword()`，用噪声字表（辆/在/有/哪/的/了…）判定它是不是真搜索词，是就走 `VehicleQuery` 过滤，不是才落到 `ActiveVehicleQuery`（`IntentDetector.cs:116-127`）。

### 3.5 为什么是「规则路由 + 结果注入」，而不是 Text-to-SQL / 模型选工具

这是本项目最主要的架构取舍，也是它和「套壳聊天框」的区别：

1. **4B 级本地模型写不稳 SQL**。多表 JOIN、`GROUP BY`、时间函数一旦写错就是静默错答，而且 SQL 错误面直接暴露到业务库。
2. **把取数收敛成 C# 方法白名单**。`DataQuerier` 的每个分支都调用已有的 `VehicleService` / `UsageRecordService` / `DashboardService`，参数只有 `keyword` 和分页，**没有任何字符串拼进 SQL**——注入面为零，权限面等于业务层权限。
3. **模型的工作被限定为「读结构化文本 + 说人话」**。`DataQuerier` 输出的不是 JSON 而是给人看的定长文本（`【车辆查询结果】`、`- 车牌号：… | 状态：在用 | 所属部门：…`），小模型对这种格式的遵循度明显高于嵌套 JSON。
4. **答案可复现、可单测**。同一条业务查询在 UI 和 AI 两条路径上走的是同一个 Service，结果必然一致；意图规则可脱离模型单测。

诚实说明它的代价：意图识别的召回率由关键词表决定，**模型不参与工具选择**，问法超出词表就退化成 `GeneralChat`（不查库）。这是当前实现的明确边界，不是模型能力问题。

### 3.6 上下文工程：`ChatHistory` 的 4 层拼接

`ChatService.BuildChatHistory()`（`ChatService.cs:161-183`）：

```csharp
var chatHistory = new ChatHistory(SystemPrompts.Main);                       // ① 角色 + 库结构 + 8 条硬规则
if (!string.IsNullOrEmpty(dataContext))
    chatHistory.AddSystemMessage(SystemPrompts.BuildDataInjectionPrompt(dataContext)); // ② 本轮真实查询结果
var memoryContext = await _memoryService.BuildMemoryContextAsync(currentContent);
if (!string.IsNullOrEmpty(memoryContext))
    chatHistory.AddSystemMessage(memoryContext);                             // ③ 历史记忆 Top5
var recentMessages = await _messageRepo.GetRecentBySessionIdAsync(sessionId, MaxRecentMessages);
foreach (var msg in recentMessages) { /* user / assistant 交替 */ }           // ④ 最近 20 条对话
```

- **① 系统提示词**（`SystemPrompts.cs:11-28`）里直接内联了五张表的字段语义和枚举取值（`Status: 在用Active/维修Maintenance/退役Retired`），让模型能看懂 `DataQuerier` 输出的字段名，不必再猜 schema。硬规则中第 2/3/4 条是针对小模型幻觉的：**没数据就照实说没数据、不许编造、禁用「可能/大概/估计」**；第 7 条专门纠正「哪个部门用车最多」应按部门车辆分布回答。
- **② 数据注入**统一加一句前置约束（`SystemPrompts.cs:33-36`），把「以下是真实数据库查询结果」和结果文本一起作为 system message 送入。
- **④ 历史窗口**固定 `MaxRecentMessages = 20`（`ChatService.cs:27`）：4B 模型上下文有限，长会话靠窗口滚动而不是无限堆叠。
- **③ 记忆**见下节，与「会话历史」是两个正交机制——历史解决「刚才说了什么」，记忆解决「用户是谁、习惯什么」。

### 3.7 记忆子系统：纯规则抽取 + 关键词回检索

`MemoryService`（`aiServer/BLL/MemoryService.cs`）没有用向量库，选型理由是单机 SQLite 场景下它足够且可解释：

- **抽取**（`MemoryService.cs:45-113`）四路规则：车牌正则（`MemoryService.cs:21`）、26 个车辆品牌词表、用户偏好词（喜欢/常用/习惯/经常/倾向于…，命中就取前后 100 字片段）、以及「≤20 字短句且三路都没中」时的通用兜底记忆。
- **写入**用 `UpsertMemoryAsync(key, …)` 按 key 幂等更新（`vehicle_plate_京A12345` 这种 key 不会产生重复行），偏好与短句走 `DateTime.Now.Ticks` 追加。
- **回检索**（`MemoryService.cs:118-153`）：从当前提问里抽车牌 / 品牌 / 连续中文词，最多 5 个关键词做 `LIKE` 搜索，按 Id 去重、按时间倒序取 Top5，注入为 system message。
- **隔离失败**：整段抽取包在 `try/catch` 里，且通过 `_ = Task.Run(...)` 挂在主流程之外（`ChatService.cs:132-136`）——记忆写失败绝不影响用户拿到回答。
- **可观测**：`/ai-chat` 侧边栏「记忆」面板直接列出 `ChatMemories` 表内容并可单条删除，用户能看到模型「记住了我什么」。

### 3.8 中文时间表达 → 查询窗口

`DataQuerier.TryParseTimeRange()`（`DataQuerier.cs:272-330`）把中文相对时间压成天数：先正则匹配「N天 / N个月」，再走词表——今天/昨天/前天、一周/两周/本周、上个月/两个月/三个月、一季度/半年/三个季度、去年/今年/两年，最后是「最近 / 近期」默认 7 天。这让「最近一周有哪些使用记录」直接变成 `GetRecentAsync(7, …)`，不需要模型做日期推理。

### 3.9 兜底与可运维性

| 失败点 | 行为 |
| --- | --- |
| Ollama 不可达 / 返回空 | `CallLlmAsync` `try/catch` 返回「AI 服务暂时不可用：{原因}」，页面不崩（`ChatService.cs:188-199`） |
| 业务查询抛异常 | `DataQuerier.QueryAsync` 捕获后返回「查询数据库时发生错误：{原因}」，仍然走模型正常作答（`DataQuerier.cs:63-66`） |
| 查询结果为空 | 每个分支返回显式文案（「当前没有匹配的车辆数据」），配合提示词第 2 条堵住幻觉 |
| 记忆抽取抛异常 | 只记 `LogWarning`，不影响对话（`MemoryService.cs:109-112`） |
| 并发写库 | 两个 `DbContext` 都注册为 `AddDbContextFactory`（`Program.cs:53-58`），避开 Blazor Server 单 Circuit 共享 DbContext 的并发冲突 |
| 日志噪声 | Serilog 把 `Microsoft` / `Microsoft.EntityFrameworkCore` 压到 Warning（`Program.cs:31-32`），否则 SK 的 HTTP 与 EF 的 SQL 会淹没业务日志 |

### 3.10 数据隔离

业务库 `data/vehicleManagement.db` 与 AI 库 `data/aiServer.db` 分离：`ChatSessions` / `ChatMessages` / `ChatMemories` 全在 AI 库，清对话记录不会动业务数据，反向亦然。两个库都在启动时 `EnsureCreatedAsync()`（`Program.cs:110-125`），**仓库不提交任何数据库文件与运行日志**（见 `.gitignore`）。

---

## 4. 快速开始

```bash
# 1) 起一个本地模型（tag 以你本地 ollama list 的结果为准）
ollama pull gemma3:4b
ollama serve            # 默认监听 http://localhost:11434

# 2) 配置应用（不配也能跑，默认就是 localhost:11434）
cp aisettings.example.json aisettings.json
#   编辑 OllamaModel 为你实际拉取的 tag

# 3) 运行
dotnet restore
dotnet run              # Development 环境会自动建库并写入种子数据 → http://localhost:5000
```

打开 `/ai-chat`，新建会话，试这几个内置引导问题（`AiChat.razor:257-263`）：

> 目前有哪些车辆在用？ · 帮我查一下各部门的车辆分布 · 最近一周有哪些使用记录？ · 如何添加新的使用记录？

**先验通 Ollama 再接应用**，可以省掉一大半排查时间：

```bash
curl http://localhost:11434/api/chat -d '{
  "model": "gemma3:4b",
  "messages": [{"role": "user", "content": "你好"}]
}'
```

连不上时的顺序：`/health` 是否 200 → 容器/进程能否解析到 Ollama 主机（Linux 容器里 `localhost` 指容器自身，要用宿主机 IP 或 `host.docker.internal`）→ `aisettings.json` 是否被环境变量覆盖 → `OllamaModel` 的 tag 是否与 `ollama list` 完全一致。

## 5. Docker 部署

```bash
docker build -t vehicle-management .
docker run -d -p 5000:80 \
  -e AiServer__OllamaEndpoint=http://192.168.1.10:11434 \
  -e AiServer__OllamaModel=gemma3:4b \
  -v /data/vm/db:/app/data \
  -v /data/vm/logs:/app/logs \
  -v /data/vm/attachments:/app/Folder \
  vehicle-management
```

多阶段构建（`Dockerfile`）只在最终镜像装 `curl` 供 `HEALTHCHECK` 使用；`data / logs / Folder` 均为挂载卷。`Dockerfile` 里的 AI 端点改为 `ARG` 默认 `localhost`，**部署时必须显式 `--build-arg` 或 `-e` 传入真实 Ollama 地址**——容器内的 `localhost` 不是宿主机。注意 `.dockerignore` 不排除 `aisettings.json`，若该文件存在于构建上下文中会被 `COPY . .` 打进镜像，生产部署建议只走环境变量。

## 6. 目录结构

```
vehicleManagement/
├── Program.cs              启动装配：配置三级取值、DI、建库、中间件
├── DAL/                    VehicleDbContext · 7 个实体 · 6 个仓储 · SeedDataInitializer · ChinaTime
├── BLL/                    业务服务：Vehicle / Department / Personnel / Location / UsageRecord
│                           / Dashboard / Execute / Maintenance / Export / Common(附件·二维码)
├── Pages/                  Blazor 页面（台账、记录、申请、扫码执行）
├── Components/             MainLayout / MobileLayout / NavMenu / Pagination
├── aiServer/               ★ AI 子系统
│   ├── BLL/                ChatService · IntentDetector · DataQuerier · MemoryService
│   │   ├── Prompts/        SystemPrompts
│   │   └── Enums/          UserIntent
│   ├── DAL/                AiServerDbContext · ChatSession/Message/Memory + 三个仓储
│   └── Pages/AiChat.razor  /ai-chat 会话 UI + 记忆面板
├── wwwroot/                设计令牌 CSS、Chart.js、字体（全部本地，无 CDN）
├── aisettings.example.json AI 配置模板（真实 aisettings.json 不入库）
└── Dockerfile / .dockerignore / .gitignore
```

## 7. 已知限制

- **意图召回受词表约束**：`IntentDetector` 是关键词 + 正则的规则匹配，问法超纲会落到 `GeneralChat` 不查库；扩展靠加词表，不靠模型。
- **无流式输出**：`SendMessageAsync` 一次性返回完整回答，长回答期间只有「思考中」态，`GetStreamingChatMessageContentsAsync` 尚未接入。
- **思考过程面板是空挂的**：`ChatMessage.ThinkingContent`（`aiServer/DAL/Model/ChatMessage.cs:37`）字段与 UI 折叠区已就位，但没有任何代码写入该字段。
- **记忆检索是 `LIKE`，不是语义检索**：换个说法召回不到相关记忆；换 embedding + 向量库是可预期的下一步。
- **无鉴权**：系统按内网单工具设计，页面和 `/execute/{Token}` 都没有登录与权限模型，公网直接暴露不合适。
- **模型名与 Ollama 官方 tag 未必一致**：仓库配置里的 `gemma4` / `gemma4:e2b` 是本地自定义 tag，克隆后请按自己 `ollama list` 的结果改 `aisettings.json`。

## 8. 许可与说明

个人自研项目，代码用于技术展示，未附带开源许可证；数据、日志与上传附件均不入库。
