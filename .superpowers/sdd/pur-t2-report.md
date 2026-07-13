# M-PUR T2 报告：贴点 + 菜单锚定 + 逐租户种子（一体）

分支 `feat/m-pur-crosscutting`，base 2ba4fd8。真相源 `docs/seeds/pur-permission-keys.md` 逐字落地，未改真相源正文，无 BLOCKED。

## 实现清单（按 brief 需求）

### 需求1 · 贴点（14 新键，既有 10 未动）
构造参数顺序 = 同目录既贴控制器 `[RequirePermission("menu-key","action")]`：
- **PurchaseRequestController**（+`using CP6.Core.Auth;`）：`pur-pr` add / submit / convert（convert 高危）。
- **RfqController**（+using）：`pur-rfq` add / invite / quote / rank / select / writeback / convert（convert 高危；rank 看似只读实为写，按真相源 §四不豁免）。
- **SubcontractController**（+using）：`pur-subcontract` consign / issue / cost（issue/cost 高危）+ reconcile 归 `view`（只读 POST 豁免，真相源 §四，附行内注释）。
- 既有 4 控制器 10 贴点（PurchaseOrder/SupplierPrice/GoodsReceipt/ThreeWayMatch）未触碰。

### 需求2 · 菜单锚定（硬前置#1）
`Program.cs` 菜单插入块对 705/706/707 各行**显式赋值** `MenuKey="pur-rfq"/"pur-pr"/"pur-subcontract"`——首启一遍即就位，不依赖 :922 全局回填（后者早于 Pur 菜单插入执行）。701–704 局部回填块（:1513，`MenuId 701..704`，在插入后同 pass 执行）保留不动，现状正确。708 GET-only 未赋键（真相源承载 0 action 键，无需）。

### 需求3 · 逐租户种子（硬前置#2）
新建 `CP6.WebApi/Seed/PurPermissionSeed.cs`，照 `WmsPermissionSeed` 模式：运行时枚举 `Sys_Tenants` 全 Id（不硬编码租户数）、显式 `TenantId=tid`、`IgnoreQueryFilters()` 幂等判存。一次覆盖**全 24 键 = 既有 10 + 新 14**（MenuAction + admin RoleAction，RoleId=1），锚定 MenuId 701–707。
`Program.cs` 移除原仅默认租户的内联 `purActions` 块，改在 Pur 菜单 + 705/706/707 MenuKey 就位之后调用 `PurPermissionSeed.EnsureSeeded(db)`。无重复种子（旧块已删；新 seed IgnoreQueryFilters 判存对既存行幂等）。

### 需求4 · 测试
`CP6.Tests/PurPermissionSeedTests.cs`，6 用例：每租户 24 元组（MenuAction+RoleAction 精确集合匹配）、幂等二跑不翻倍（2×24=48）、RoleAction 全挂 RoleId=1 + 锚定 MenuId + MenuKey 非 null/pur- 前缀/无下划线、逐租户显式 TenantId、无租户 no-op、oracle 自洽（24 元组/7 menu-key/无重复）。`ExpectedTuples` 独立硬编码誊自真相源 §一/§二，非引用生产常量。

## TDD 证据

**RED**（临时注释掉 seed 内 `(705,"convert")` 一键）：
```
dotnet test CP6.Tests --filter FullyQualifiedName~PurPermissionSeedTests
Failed! - Failed: 2, Passed: 4, Total: 6
  EnsureSeeded_SeedsExactly24TuplesPerTenant... Assert.Equal() Expected: 24 Actual: 23
  EnsureSeeded_IsIdempotent... Assert.Equal() Expected: 48 Actual: 46
```
oracle 捕获缺键 → 证明误删/误改会红。

**GREEN**（恢复该键后）：
```
dotnet test CP6.Tests --filter FullyQualifiedName~PurPermissionSeedTests
Passed! - Failed: 0, Passed: 6, Total: 6
```

## 全量结果
```
dotnet test CP6.Tests/CP6.Tests.csproj
Passed! - Failed: 0, Passed: 1802, Skipped: 5, Total: 1807
```
基线 1796 + 新增 6 = 1802 绿。5 skip 为既存结构性/SQLite 跳过，非本任务引入。WebApi 构建 0 warning/0 error。

## 文件变更
- 新增 `CP6.WebApi/Seed/PurPermissionSeed.cs`
- 新增 `CP6.Tests/PurPermissionSeedTests.cs`
- 改 `CP6.WebApi/Controllers/Pur/PurchaseRequestController.cs`（using + 3 attr）
- 改 `CP6.WebApi/Controllers/Pur/RfqController.cs`（using + 7 attr）
- 改 `CP6.WebApi/Controllers/Pur/SubcontractController.cs`（using + 4 attr）
- 改 `CP6.WebApi/Program.cs`（705/706/707 MenuKey 显式赋值；内联 purActions 块 → PurPermissionSeed 调用）

commits（均已 push）：
- `aa274fd` feat(pur): 逐租户 PurPermissionSeed（全24键）+ 单测
- `5fe9a77` feat(pur): 裸控制器贴 RequirePermission(14) + 705/706/707 MenuKey + 接线 PurPermissionSeed

## 自审
- 24 键逐字核对：控制器 grep（10 既有 + 14 新）↔ seed Actions ↔ 测试 oracle ↔ 真相源 §一 四方一致，连字符、无下划线、无 typo。
- MenuKey：705/706/707 首启就位（显式）；701–704 现状不动；无回填时序依赖。
- seed：逐租户、幂等、24 键、无重复；旧默认租户块已删。
- 既有 10 贴点、菜单 701–704 未破坏；全量绿；测试输出干净。

## Concerns
1. **既有已部署 DB 自愈路径**：老库中 705/706/707 已插入且 MenuKey=null，本次代码在下次启动时不会重跑插入块（`if(!Any(705))` 为 false），但 `:922` 全局回填（每启动扫 `MenuKey==null && RoutePath!=null`）会将其补为 pur-rfq/pur-pr/pur-subcontract。故显式赋值修的是**洁净首启**，老库靠 :922 二次自愈——两条路径都覆盖，无残口。部署冒烟建议实证 705/706/707 MenuKey 非 null。
2. **reconcile 归 view**：该端点已贴 `[RequirePermission("pur-subcontract","view")]`（非豁免清单条目），T3 反射测试「有 attr 或在豁免清单」判定应通过；若 T3 另设「只读 POST 豁免清单」需知悉本端点走 attr-view 而非清单豁免。
3. **本任务不含 T3 范围**（反射 fail-closed 测试、403 集成用例）——未触碰。
