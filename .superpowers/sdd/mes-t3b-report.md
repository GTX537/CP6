# M-MES T3b 执行报告：逐租户 MenuAction/RoleAction 权限种子

> 执行于 2026-07-12。分支 `feat/m-mes-crosscutting`。样板 = ErpPermissionSeed（M-ERP T3b）同型复制。

## 三数闭环对账表

| 阶段 | 数 | 依据 |
|---|---|---|
| ① 控制器写端点（[RequirePermission] 贴点） | **28** | `grep RequirePermission CP6.WebApi/Controllers/Mes/*.cs` = 28 行，与真相源 §七「真写 28」精确吻合 |
| ② 去重 (menu-key, action) 元组 | **25** | 28 − 3 归并重复（见下）= 25 |
| ③ 种子元组（MesPermissionSeed.Actions） | **25** | 漏种 0 / 多种 0，测试逐元组集合相等验证 |

**闭环：28 → 25 → 25。** 覆盖 **9** 个有写端点 menu-key（`mes-plan-achievement` 仅 2 只读 POST 豁免→view，未贴点，不入种子，故非 10）。

### 3 处归并（真相源 §五 1/2/3，消解重复）
| 归并键 | 覆盖的两端点 |
|---|---|
| `mes-work-order:add` (302) | Create + ExpandFromOrder |
| `mes-production-result:suspend` (304) | Suspend + Resume |
| `mes-machine:downtime` (310) | RegisterDowntime + CloseDowntime |

（§五归并4「upsert=edit」是单端点内 create/update 合一，不产生重复行，故不计入去重消解。）

### 25 元组 × 锚定 MenuId（锚定表 mes-key-menu-anchor.md）
| menu-key | MenuId | actions | 数 |
|---|---|---|---|
| mes-planning-board | 301 | reschedule, arrange | 2 |
| mes-work-order | 302 | add, edit, del, issue | 4 |
| mes-production-result | 304 | start, suspend, complete, report | 4 |
| mes-quality-inspection | 306 | add, edit | 2 |
| mes-defect | 308 | add, edit, del | 3 |
| mes-machine | 310 | add, edit, del, status, downtime | 5 |
| mes-oee | 311 | recalculate | 1 |
| mes-work-center | 314 | edit, del | 2 |
| mes-process-cost-rate | 315 | edit, del | 2 |
| **合计** | | | **25** |

grep 逐控制器核对（28 贴点）：ProcessCostRate 2 / WorkOrder 5(add×2) / DefectRecord 3 / PlanningBoard 2 / WorkCenter 2 / Machine 6(downtime×2) / QualityInspection 2 / Oee 1 / ProductionResult 5(suspend×2) = 28 ✅。ActionCode 与贴点第二实参逐字一致。

## TDD Evidence

### RED
`dotnet test --filter MesPermissionSeedTests` → 编译失败：
```
error CS0103: The name 'MesPermissionSeed' does not exist in the current context (×6)
```
（测试已写、断言 25/50，但被测类未建 → 红。）

### GREEN
新建 `MesPermissionSeed.cs` + 接入 Program.cs 后：
```
Passed!  - Failed: 0, Passed: 5, Skipped: 0, Total: 5 - CP6.Tests.dll
```
5 测试：25 元组/租户逐元组集合相等（Menu+Role）/ 幂等二次调用零新增(2×25=50) / RoleId=1 且 MenuId 全来自锚定表且 MenuKey mes- 前缀 / 显式 TenantId 两租户各独立行 / 无租户 no-op。

### 全量回归
```
Passed!  - Failed: 0, Passed: 1727, Skipped: 5, Total: 1732 - CP6.Tests.dll
```
= 基线 1722 + 本任务 5 新测试。**基线未跌。**

## 四要件自检（照 ErpPermissionSeed）
- ✅ 逐租户显式 TenantId：`TenantId = tid`（枚举 Sys_Tenants）→ StampTenant 仅盖 Guid.Empty 不覆盖显式值。
- ✅ IgnoreQueryFilters 在查重上：MenuAction/RoleAction 判存均 `.IgnoreQueryFilters().Any(...)`，跨租户可见。
- ✅ 双种 RoleId=1：MenuAction（操作点目录）+ RoleAction（授管理员 RoleId=1）各一份。
- ✅ 测试 oracle 独立：`ExpectedTuples` 为测试内独立硬编码 25 元组，非引用 `MesPermissionSeed.Actions`，防自证假绿。

## Files changed
- `CP6.WebApi/Seed/MesPermissionSeed.cs`（新建，正本）
- `CP6.WebApi/Program.cs`（接入，MesMenuSeed 之后）
- `CP6.Tests/MesPermissionSeedTests.cs`（新建，5 测试，独立 oracle）
- `docs/seeds/mes-permission-seed.sql`（新建，文档留档，头声明 C# 正本，CROSS JOIN Sys_Tenants + NOT EXISTS）

## Concerns
- 无阻断性 concern。ActionName（改期/自动排产/中断/停机记录 等）为 UI 显示名，非权限判定依据（判定只看 ActionCode），措辞可后续微调不影响功能。
- 洁净部署首启依赖 MesMenuSeed（T2）已在回填块之前显式赋 mes- MenuKey——本任务接入点严格置于 MesMenuSeed 之后，满足「锚定菜单行须先在」前提。
- 与 WMS/ERP 同类平台票一致：TenantAdminService 若不复制 RoleAction，新租户 admin 重启前可能 403（重启自愈），非本任务范围。
