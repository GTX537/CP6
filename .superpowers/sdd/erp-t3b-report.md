# M-ERP T3b 执行报告：逐租户 MenuAction/RoleAction 权限种子

- 分支 `feat/m-erp-crosscutting`，commit **c765b81**（已 push）。
- 基线 1689 绿 → **1694 绿**（+5 新测试，0 失败，5 跳过既有）。

## 一、实现内容

新增/改动 4 文件：
1. `CP6.WebApi/Seed/ErpPermissionSeed.cs`（新建）——`EnsureSeeded(CP6Context)`：逐租户幂等播种 30 元组的 `Sys_MenuAction` + `Sys_RoleAction`（RoleId=1）。WmsPermissionSeed 同型复制。
2. `CP6.Tests/ErpPermissionSeedTests.cs`（新建）——5 例断言。
3. `CP6.WebApi/Program.cs`（改）——`ErpMenuSeed.EnsureSeeded` 之后、`WmsPermissionSeed` 之前接入 `ErpPermissionSeed.EnsureSeeded(db)`（第 834 行附近）。
4. `docs/seeds/erp-permission-seed.sql`（新建）——CROSS JOIN Sys_Tenants 风格文档留档，文件头声明 C# 为正本。

## 二、三数闭环对账表

| 阶段 | 数 | 依据 |
|---|---|---|
| 控制器写端点（[RequirePermission] 贴点） | **35** | `grep RequirePermission Controllers/Erp` = 35 行，与真相源 §七「真·写端点 35」吻合 |
| 去重 (menu-key, action) 元组 | **30** | 35 − 5 重复。重复 5 处：estimate-calc:add(16,19)、quotation:add(21,24)、quotation:confirm(25,26)、product:add(28,31)、plate-mold:edit(39,40) |
| 种子元组（Actions[] 长度） | **30** | ErpPermissionSeed.Actions；漏种 0 / 多种 0 |

覆盖 menu-key：**11**（有写端点者）。另 3 键 `erp-order-trace` / `erp-credit-note` / `erp-otd-report` 仅 view 端点（GET-only 或只读 POST 豁免），未贴点即无键可种，故不在本种子——与 14 键总数不矛盾（brief §3 一致）。11 只读 POST 豁免 + AllowAnonymous（estimate-calc:calculate）不入种子。

### 30 元组明细（MenuId 来自锚定表逐字）
- 202 erp-estimate-calc: add/edit/del（3）
- 204 erp-quotation: add/edit/del/confirm/issue（5）
- 206 erp-product: add/edit/del（3）
- 208 erp-order: add/edit/del/cancel（4）
- 209 erp-order-price-correction: correct（1）
- 210 erp-fsc-checklist: issue（1）
- 212 erp-business-partner: add/edit/del（3）
- 213 erp-sheet-unit-price: import/edit（2）
- 215 erp-plate-mold: add/edit/del（3）
- 218 erp-backorder: close/split（2）
- 220 erp-fx-rate: add/edit/del（3）
合计 3+5+3+4+1+1+3+2+3+2+3 = **30**。

ActionCode 与控制器 [RequirePermission] 第二实参逐字核对通过（grep 35 行 vs Actions[]），差一字全链 403 已排除。

## 三、TDD 证据

**RED**（实现前，仅有测试）：`dotnet test --filter ErpPermissionSeedTests`
```
error CS0103: The name 'ErpPermissionSeed' does not exist in the current context  ×6
```
原因：`ErpPermissionSeed` 类尚未创建，测试引用编译失败。

**GREEN**（实现后）：
```
Passed!  - Failed: 0, Passed: 5, Skipped: 0, Total: 5, Duration: 5 s
```

**全量**：
```
Passed!  - Failed: 0, Passed: 1694, Skipped: 5, Total: 1699, Duration: 58 s
```

5 例覆盖：①每租户各得全套 30 且逐元组精确匹配（MenuAction+RoleAction）②幂等（二次调用行数不变，2×30=60）③RoleAction 全挂 RoleId=1 且 MenuId 来自锚定表、菜单行 MenuKey 非 null 且 erp- 前缀 ④显式 TenantId 两租户各独立行 ⑤空 Sys_Tenants → NoOp。

## 四、Self-Review

- 三数闭环 35→30→30 自洽 ✅（写入报告）。
- 四要件齐：逐租户显式 `TenantId=tid` ✅ / `IgnoreQueryFilters()` 查重 ✅ / MenuAction+RoleAction 双种 RoleId=1 ✅ / StampTenant 仅盖 Guid.Empty 不覆盖显式值（WmsPermissionSeed 同型）✅。
- 测试真实：删实现即 RED（CS0103 已证）；幂等二次调用行数不变已断言。
- SQL 文件头声明「正本是 C#」✅。
- 接入位置：紧随 ErpMenuSeed.EnsureSeeded 之后 ✅（菜单锚定行先在，RoleAction 挂 MenuId 有效）。

无 self-review 遗留问题。

## 五、Concerns

- 平台已知票（非本任务范畴）：`TenantAdminService` 新建租户时不复制 RoleAction，新租户 admin 重启前可能 403，重启自愈（启动种子逐租户补齐）——与 M-WMS T3b 同一残项，本任务未扩大也未修复。
- 线上生效需重建 cp6-api 镜像并重启（启动种子跑一遍）——部署动作留待波尾统一执行，与 WMS/ERP T2 同批。
