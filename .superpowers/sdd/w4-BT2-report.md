# Task B-T2 报告：批量转单端点 + 权限点 seed

**STATUS: DONE** ｜ commit `6bfcbfb`（已 push feat/wfs-inbox-ux）
测试：全量 `dotnet test CP6.slnx` = 2016 通过 / 5 跳过（== 基线，零回归）。

## 交付内容
1. **`CP6.WebApi/Controllers/Oa/InboxController.cs`**（+32 行）
   - 两个 POST 端点，同贴 `[RequirePermission("oa-inbox","batch-transfer")]`（C8：preview 同权限点）：
     - `POST /api/oa/inbox/batch-transfer` → `_inbox.BatchTransferAsync(me, from, to, comment, filter)`，操作者=登录本人（管理动作不走 act-as）。
     - `POST /api/oa/inbox/batch-transfer/preview` → `_inbox.BatchTransferPreviewAsync(from, filter)`。
   - DTO：`BatchTransferFilterReq` / `BatchTransferReq` + `ToFilter` 映射至 B-T1 的 `BatchTransferFilter` record。
   - `using CP6.Core.Auth;` 已在文件头（无需新增）。

2. **`CP6.WebApi/Seed/InboxBatchTransferPermissionSeed.cs`**（新建，79 行）
   - 照 **FlowTriggerPermissionSeed sibling 模式**（波③ 交接注记指定），非 brief 的 Program.cs 内联 RoleId=1 范本。
   - 逐租户幂等：枚举 `Sys_Tenants` → 每租户显式 `TenantId=tid` 插 `Sys_MenuAction`(733,batch-transfer,批量改派) + `Sys_RoleAction`(RoleId=1,733,batch-transfer)。
   - 幂等守卫用 `IgnoreQueryFilters()`（跨租户可见，防默认租户作用域误判重复插）→ 重启不重复。
   - 不做 MenuKey 回填：733 `MenuKey="oa-inbox"` 已由 `OawfMenuSeed`（M-OA/WF 波，锚定行显式赋值）落地。

3. **`CP6.WebApi/Program.cs`**（+4 行）
   - 在 `OawfPermissionSeed.EnsureSeeded` / `FlowTriggerPermissionSeed.EnsureSeeded` **之后**接入 `InboxBatchTransferPermissionSeed.EnsureSeeded(db)`（锚定菜单 733 须先在）。

4. **`CP6.Tests/OawfPermissionAttributeTests.cs`**（守卫重基线，仅词表/计数常量/注释，断言逻辑零弱化）
   - `ActionVocabulary` 加 `"batch-transfer"`（否则视为 typo 报红）。

## 守卫重基线数字（实跑 4/4 绿确认，非推测）
- `taggedCount` 断言 **37 → 39**（+2：batch-transfer 与 preview 两个带 RequirePermission 的 POST）。
- 非GET 端点总数注释 **39 → 41**（41 = 39 贴点 + 2 只读 POST 豁免）。
- `exemptHit` = 2（不变）；controller count = 17（不变）。
- 断言谓词（IsMutating / fail-closed 核心闸 / 豁免防腐 / 键约定）逐字未动，仅常量与文档注释调整。

## seed 幂等证据
- 判存条件 `(TenantId,MenuId,ActionCode)` / `(TenantId,RoleId,MenuId,ActionCode)` 四元组守卫 + `IgnoreQueryFilters()` 跨租户可见，与 FlowTriggerPermissionSeed / OawfPermissionSeed 同型，重启零重复插。
- 「贴点⊆种子」互锁：InboxController 新 action 集 {batch-transfer} == 种子登记集，无孤儿键、无漏种。

## 闸门
1. ✅ `dotnet test CP6.slnx` 全绿（含重基线守卫）：2016/5。
2. ✅ `dotnet ef migrations has-pending-model-changes`：No changes（零迁移，引擎零 diff）。
3. ✅ `git show --stat HEAD`：仅 brief 4 文件（控制器/Program.cs/新种子/守卫），外科式 add，`.superpowers/sdd/*` 既有改动未混入本 commit。

## Concerns
- 无阻塞项。控制器为薄壳（业务逻辑在 B-T1 服务，已有服务测试承载 + E-T2 QA harness e2e 承接）；未新增控制器单测符合 brief「控制器薄壳走 build」口径。
- 部署注意：新种子须随镜像重建生效；线上验证 RoleAction(733,batch-transfer) 逐租户就位可留待部署阶段（本波不部署）。
