---
kind: frontend_style
name: Blazor Server 前端样式体系：CSS 设计令牌 + 双主题（主应用绿色 / AI Chat 青色）
category: frontend_style
scope:
    - '**'
source_files:
    - wwwroot/css/design-system.css
    - wwwroot/css/site.css
    - wwwroot/css/ai-chat.css
    - Pages/_Host.cshtml
    - Components/Layouts/MainLayout.razor
    - Components/Layouts/MobileLayout.razor
    - vehicleManagement.csproj
---

## 1. 系统与工具

- 技术栈：Blazor Server (.NET 10)，无构建工具链，纯静态 CSS 文件由 `_Host.cshtml` 直接 `<link>` 引入。
- UI 组件库：项目引用了 `Microsoft.FluentUI.AspNetCore.Components`（见 `vehicleManagement.csproj`），但当前页面布局与样式并未使用该库的组件——所有视觉风格均通过手写 CSS 实现。
- 字体：本地 `wwwroot/fonts/Geist-{Regular,Medium,SemiBold,Bold}.ttf` 通过 `@font-face` 注册为 `Geist`；同时通过 Google Fonts 加载 `Plus Jakarta Sans`、`Open Sans`、`Poppins`。
- 图表：使用 Chart.js（`js/chart.umd.min.js`），在 `_Host.cshtml` 中内联初始化全局默认值并暴露 `window.charts` API。

## 2. 关键文件

- `wwwroot/css/design-system.css` — 全局设计令牌与通用 UI Kit（约 1600 行），定义颜色、排版、阴影、圆角、间距、按钮、表格、卡片、徽章、输入框、Tabs、Grid/Flex 辅助类等。
- `wwwroot/css/site.css` — Blazor 特定增强：错误提示条、NavLink active 态、`InputFile`/`select`/`textarea` 统一样式、图表容器响应式、纯 CSS 排行榜条形图、页面淡入动画、移动端布局以及 **Execute 页面** 的专用样式块。
- `wwwroot/css/ai-chat.css` — AI Chat 子系统独立主题，自包含 `--ai-*` 设计令牌，定义聊天侧边栏、消息气泡、思考面板、输入框、欢迎页等。
- `Pages/_Host.cshtml` — 唯一入口 HTML，集中注入三套 CSS 与 Chart.js。
- `Components/Layouts/MainLayout.razor` / `MobileLayout.razor` — 桌面端 sidebar+topbar 布局与移动端 layout 骨架。

## 3. 架构与设计约定

### 3.1 设计令牌（Design Tokens）

`design-system.css` 在 `:root` 下集中声明全部视觉变量，分为若干语义组：

| 分组 | 变量前缀 | 含义 |
|---|---|---|
| 主色 | `--primary-50..950` | 绿色系（#009D5E 为主色 #500） |
| 中性色 | `--gray-50..950` | 灰阶 |
| 语义色 | `--red-*`, `--blue-*`, `--orange-*`, `--sky-*`, `--purple-*`, `--pink-*` | 状态/强调色 |
| 布局 | `--sidebar-width`, `--topbar-height`, `--content-max-width` | 固定尺寸 |
| 排版 | `--font-family`, `--letter-spacing` | 字体族与字距 |
| 阴影 | `--shadow-sm/md/lg` | 三层阴影 |
| 圆角 | `--radius-sm..xl/full` | 6/8/12/16/9999px |
| 过渡 | `--transition` | `all 0.2s ease` |

此外还有一层 `--ds-*` 别名（如 `--ds-primary`, `--ds-bg`, `--ds-fg`, `--ds-border`, `--ds-radius-md`, `--ds-shadow-card`, `--ds-transition`），注释明确说明“Execute page design tokens (aliased from global palette — 使用项目全局绿色主题)”，供 Execute 页面复用同一套绿色主题。

AI Chat 子系统则完全隔离，在 `ai-chat.css` 顶部自声明 `--ai-p` / `--ai-ph` / `--ai-pl` / `--ai-t` / `--ai-bd` / `--ai-bg` / `--ai-rs/rm/rl` / `--ai-s1/s2/s3` 等令牌，主色为青色 `#0891B2`，与主应用的绿色主题互不干扰。

### 3.2 样式组织方式

- 采用 **单文件 CSS + BEM-like class 命名**，没有 SCSS/Less/Sass 预处理，也没有 CSS Modules 或 scoped CSS。
- 通用 UI 元素（`.btn`, `.card`, `.badge`, `.input-field`, `.tabs`, `.grid`, `.stat-card`）集中在 `design-system.css`，作为可复用的原子类。
- 布局结构类（`.app-layout`, `.sidebar`, `.main-wrapper`, `.topbar`, `.main-content`）也在该文件中定义，被 `MainLayout.razor` 直接使用。
- 业务页面级样式放在 `site.css` 中按功能区块划分（Error UI → InputFile → Chart containers → Ranking Bar Charts → Mobile Layout → Execute Page）。Execute 页面的类名以 `exec-*` 前缀区分。
- AI Chat 子系统的样式完全独立于主应用，类名以 `aim-*` 前缀命名，避免与主应用冲突。

### 3.3 响应式策略

- 无断点系统（无 Tailwind 或自定义断点变量），直接在 CSS 中使用 `@media (max-width: ...)` 覆盖：
  - `992px` / `768px` / `480px`：用于图表容器高度与 padding 调整（`site.css`）。
  - `900px` / `640px`：用于 AI Chat 侧边栏折叠与消息气泡宽度（`ai-chat.css`）。
  - `480px`：用于 Execute 表单行堆叠与头部 padding（`site.css`）。
- 移动端布局通过 `MobileLayout.razor` 提供独立的 `.mobile-layout` / `.mobile-content` 容器。

### 3.4 主题与品牌色

- 主应用主色：`--primary-500: #009D5E`，hover 用 `--primary-600: #007E4B`。
- 语义色映射：成功→green (`--primary-*`)，危险→red (`--red-500: #C63435`)，警告→orange (`--orange-500: #F57C23`)，信息→blue (`--blue-500: #148BE7`)，强调→purple/pink。
- AI Chat 主题：主色 `--ai-p: #0891B2`，浅背景 `--ai-pl: #ECFEFF`，文字 `--ai-t: #0F172A`，背景 `--ai-bg: #F8FAFC`。

### 3.5 组件库使用现状

虽然 `vehicleManagement.csproj` 引用了 `Microsoft.FluentUI.AspNetCore.Components` 4.14.3，但在已检查的页面与布局中未发现任何 Fluent UI 组件的使用。所有按钮、卡片、表格、输入框均为手写 CSS 类（`.btn-primary`, `.card`, `.table-wrapper`, `.input-field` 等）。

## 4. 约定与约束

- **设计令牌优先**：颜色、圆角、阴影、字号、间距等视觉属性必须通过 `--*` CSS 变量引用，禁止在组件样式中硬编码十六进制色值（除 fallback 值外，如 `var(--primary-500, #009D5E)`）。
- **Execute 页面主题约束**：`site.css` 中注释明确写明“使用项目全局绿色主题 (--primary-*)，禁止使用任何蓝色”，Execute 页面样式全部走 `--ds-*` 别名。
- **AI Chat 主题隔离**：AI Chat 子系统使用独立的 `--ai-*` 命名空间，不与主应用共享令牌，避免主题污染。
- **Font 加载顺序**：`_Host.cshtml` 先加载 `design-system.css`（注册 Geist），再加载 `site.css` 与 `ai-chat.css`，确保字体可用后再渲染。
- **Chart.js 全局配置**：图表字体统一设为 `Geist`，颜色数组 `chartColorArray` 与 CSS 中的 `--primary-*` / `--blue-*` / `--purple-*` / `--orange-*` / `--sky-*` / `--red-*` / `--pink-*` 一一对应，保持图表与 UI 调色板一致。
- **Blazor NavLink 高亮**：依赖 Blazor 自动添加的 `.active` 类，通过 `.nav-item.active` 选择器统一设置激活态（背景 `--primary-50`，文字 `--primary-600`）。
- **无构建时样式处理**：没有 `tailwind.config.*`、没有 SCSS 编译步骤，所有样式都是浏览器直接加载的原始 CSS，版本号通过 URL 查询参数（`?v=3`、`?v=6`）做缓存失效。