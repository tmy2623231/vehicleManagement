---
kind: design
name: Repository UpdateAsync 使用 CurrentValues.SetValues 并仅对纯读方法加 AsNoTracking
source: session
category: adr
---

# Repository UpdateAsync 使用 CurrentValues.SetValues 并仅对纯读方法加 AsNoTracking

_来源：0797e3e → 8600653 提交周期内记录的编码计划——内容为规划时意图，实现可能滞后或有出入。_

**状态：** accepted

## 背景
EF Core 的 `db.X.Update(detached)` 会把 `Include` 进来的导航属性（如 `Department`、`UsageRecords`）整片标记 Modified，用陈旧快照反向覆盖部门表；同时全表加载后在内存中 `.Where(IsActive)` 筛选导致删除人员/部门时把整库拉进内存。

## 决策驱动
- 避免导航属性被静默覆盖
- 避免全表加载到内存
- 保持现有「不抛异常」返回契约

## 备选方案
- **继续使用 db.Update(patch)** _（已否决）_ — 优点：改动最小；缺点：会连带标记 Include 的导航属性，造成静默数据回退
- **对所有查询加 AsNoTracking** _（已否决）_ — 优点：减少跟踪开销；缺点：下游「读-改-写」路径的 SetValues 找不到原实体，变成静默不更新
- **SetValues + 仅在纯读方法加 AsNoTracking** — 优点：只覆盖标量列与 FK，导航属性不动；纯读查询零跟踪开销

## 决策
5 个 Repository 的 `UpdateAsync` 改用 `db.Entry(existing).CurrentValues.SetValues(patch)`；同时在 `VehicleRepository.GetAllAsync`、`DepartmentRepository.GetAllAsync`、`PersonnelRepository.GetAllAsync/GetByDepartmentIdAsync`、`LocationRepository.GetAllAsync/GetByCategoryAsync`、`UsageRecordRepository.GetAllAsync/GetLastRecordByVehicleIdAsync`、`MaintenanceRecordRepository.GetAllAsync`、`ChatSessionRepository.GetAllAsync`、`ChatMessageRepository.GetBySessionIdAsync/GetRecentBySessionIdAsync`、`ChatMemoryRepository.GetAllAsync/SearchAsync/GetByKeyAsync` 等纯读方法加 `AsNoTracking()`，并在每个 `GetByIdAsync` 上方注释禁止误加。

## 影响
人员编辑不再静默覆盖 Department 导航属性；纯读查询不再占用 EF 跟踪缓存。代价是 `SetValues` 不做乐观并发校验，且新增索引需同时维护 `VehicleDbContext.OnModelCreating` 与 `SchemaBootstrapper.Ddl` 两处。