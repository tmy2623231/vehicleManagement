---
kind: design
name: 审计调用点从 7 处补齐至 18 处，通过构造参数注入 AuditService
source: session
category: adr
---

# 审计调用点从 7 处补齐至 18 处，通过构造参数注入 AuditService

_来源：0797e3e → 8600653 提交周期内记录的编码计划——内容为规划时意图，实现可能滞后或有出入。_

**状态：** accepted

## 背景
`AuditService` 已就绪（独立 DbContext 写、失败只记 Error 不阻断业务），但只有登录、跨部门借用、账号管理、人员删除等 7 处调用了它，车辆/维修/位置/部门/使用记录的增删改缺失审计。

## 决策驱动
- 关键实体变更可追溯
- 审计失败不阻断业务
- 不引入 SaveChangesInterceptor 或 HttpContextAccessor

## 备选方案
- **SaveChangesInterceptor 全局拦截** _（已否决）_ — 优点：一处接入；缺点：Circuit 下 `IHttpContextAccessor` 恒为 null，难以区分操作人
- **在每个 Service 的关键方法显式调用 AuditService** — 优点：Action 文案精确、失败不影响业务、DI 按类型注入无需改 Program.cs

## 决策
向 `VehicleService`、`UsageRecordService`、`MaintenanceRecordService`、`LocationService`、`DepartmentService` 新增 `AuditService` 构造参数，在 Create/Update/Delete/ToggleBorrowed 等方法中补 11 处调用，使总数达到 18 处。

## 影响
测试工厂构造这些 Service 时需多传一个 `AuditService`；审计写入失败仅记 Error 日志，不会让业务回滚。