---
kind: design
name: 分页收敛为 PagedResult.ClampPageSize/ClampPage 并强制唯一列兜底排序
source: session
category: adr
---

# 分页收敛为 PagedResult.ClampPageSize/ClampPage 并强制唯一列兜底排序

_来源：0797e3e → 8600653 提交周期内记录的编码计划——内容为规划时意图，实现可能滞后或有出入。_

**状态：** accepted

## 背景
7 处 `GetPagedAsync` 各自抄一份 11 行样板，未限制 `pageSize`，且当 `CreatedAt` 落在同一毫秒时 SQLite 的 `LIMIT/OFFSET` 翻页会出现重复或漏行。

## 决策驱动
- 防止 pageSize=100000 把整库拉进内存
- 消除 7 处重复分页样板
- 保证 SQLite 翻页稳定有序

## 备选方案
- **各 Service 自行 Clamp** _（已否决）_ — 优点：无需引入新类；缺点：7 处重复实现，边界行为不一致
- **统一静态工具类 + ThenBy(Id) 兜底** — 优点：单一来源、页大小硬上限 100、空结果回第 1 页、等值排序键稳定

## 决策
在 `BLL/Common/PagedResult.cs` 新增非泛型静态类 `PagedResult`，提供 `MaxPageSize=100`、`ClampPageSize`、`ClampPage`、`Of<T>`；7 个分页方法尾部统一为 5 行调用，并在每个 `OrderBy` 分支末尾追加 `ThenBy(v => v.Id)` 作为唯一列兜底。

## 影响
所有列表页翻页不再重复/漏行，恶意大 pageSize 被截断为 100。代价是新增了一个共享工具类，且 `CreatedAt` 相同记录的分页顺序依赖主键 Id。