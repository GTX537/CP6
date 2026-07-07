# Task 4 报告：权限点接线（9 控制器 + 反射测试 + RoleAction 种子）

## Status
完成。Commit `8a0e17f`（feat/space-wave4-crosscutting）。全量 `dotnet test` 1562 passed / 5 skipped / 0 failed（基线 1559 + 3 反射测试）；build 0 err。

## Implemented
1. 6 个变更型 Space 控制器逐端点贴 `[RequirePermission(menu, action)]`（`using CP6.Core.Auth;`），每控制器头注释加权限约定一行。
2. `CP6.Tests/Space/SpacePermissionAttributeTests.cs`——反射守卫（3 用例）。
3. `docs/seeds/space-roleaction-seed.sql`——Sys_MenuAction 登记 + Sys_RoleAction 授权（逐租户、幂等）。
4. 3 个只读控制器（SpaceLocate/Stock/Advanced）零改动（全 GET，反射测试确认无误贴）。

## 贴点清单（controller → action → 键）
### SpaceMasterController
- CreateSite/UpdateSite/DeleteSite → space-site:add / edit / delete
- CreateFloor/UpdateFloor/DeleteFloor → space-floor:add / edit / delete
- CreateZone/UpdateZone/DeleteZone → space-floor:edit（×3）
- CreateAisle/UpdateAisle/DeleteAisle → space-floor:edit（×3）
- CreateRack/UpdateRack/DeleteRack → space-floor:edit（×3）
- GET（ListSites/ListFloors/ListZones/ListAisles/ListRacks/Scene/Unplaced/Locations）→ 无（仅 [Authorize]）

### LocationPublishController
- PublishFloor(POST) → space-publish:publish
- Deactivate(PUT) → space-publish:deactivate
- Adopt(POST) → space-publish:adopt
- ListEvents(GET) → 无

### CodeRuleController
- CreateRule/UpdateRule/DeleteRule → space-code-rule:add / edit / delete
- GenerateCodes(POST) / GenCode(POST) → space-code-rule:generate（×2）
- Preview(POST) → **豁免**（只读样例合成，不写库，仅 [Authorize]）
- ListRules(GET) / CodePrecheck(GET) → 无

### TemplateController（全归楼层编辑）
- Create(POST) / Update(PUT) / Delete(DELETE) / Clone(POST) → space-floor:edit（×4）
- List(GET) → 无

### ConnectorController（全归楼层编辑）
- Create(POST) / UpsertStop(PUT) / DeleteStop(DELETE) / Update(PUT) / Delete(DELETE) → space-floor:edit（×5）
- ListBySite(GET) → 无

### SceneController（全归楼层编辑）
- SaveScene(POST) / Import(POST) / BindCodes(POST) → space-floor:edit（×3）
- Export(GET) → 无

### 只读控制器（零改动）
- SpaceLocateController / SpaceStockController / SpaceAdvancedController：全部 GET 端点，无特性。

## 反射断言方式说明
- **扫描面**：`typeof(SpaceMasterController).Assembly` → 过滤 `Namespace == "CP6.WebApi.Controllers.Space"` 且 `ControllerBase` 派生且非抽象。守卫用例断言恰好扫到 **9** 个 controller（防命名空间/程序集变动导致空扫空过）。
- **menu/action 读取**：`RequirePermissionAttribute` 的 `_menu`/`_action` 为 **private field**，实例反射不可读。故用 `CustomAttributeData.GetCustomAttributes(method)` 读取特性的**构造参数** `(menu, action)`——实现**白名单逐字校验**（非降级到存在性）。
- **用例①** `EveryMutatingAction_HasRequirePermission_InWhitelist`：每个带 HttpPost/HttpPut/HttpDelete 的 action（豁免项除外）必须带 `[RequirePermission]` 且 `"menu:action"` ∈ 13 键白名单（硬编码，与映射表逐字一致）。
- **用例②** `NoReadOnlyAction_HasRequirePermission`：每个只读端点（GET 且非变更，或豁免的只读 POST）必须**不带** `[RequirePermission]`（防误贴）。
- **豁免清单（显式）**：`CodeRuleController.Preview`——POST 但只读语义（合成样例、不写库），按「不得带特性」校验，与 GET 同待遇。
- **单测无 HTTP 管道**：RequirePermission 作为 `IAsyncAuthorizationFilter` 只在真实管道触发；本反射测试仅检查特性存在性与参数，不执行过滤器；既有直构控制器的服务层单测不受影响（全量绿证实）。

## Files changed
- `CP6.WebApi/Controllers/Space/SpaceMasterController.cs`（+15 特性 + 头注释）
- `CP6.WebApi/Controllers/Space/LocationPublishController.cs`（+3 + 头注释）
- `CP6.WebApi/Controllers/Space/CodeRuleController.cs`（+5 + 头注释，Preview 豁免注释）
- `CP6.WebApi/Controllers/Space/TemplateController.cs`（+4 + 头注释）
- `CP6.WebApi/Controllers/Space/ConnectorController.cs`（+5 + 头注释）
- `CP6.WebApi/Controllers/Space/SceneController.cs`（+3 + 头注释）
- `CP6.Tests/Space/SpacePermissionAttributeTests.cs`（新建，3 用例）
- `docs/seeds/space-roleaction-seed.sql`（新建）

## 种子要点
- **表结构核对**：Sys_MenuAction 列 = Id/MenuId/ActionCode(必)/ActionName(必)/Sort/CreateDate/TenantId（+ Creator/Modifier/ModifyDate 可空，省略）；唯一索引 UX (TenantId,MenuId,ActionCode)。Sys_RoleAction 列序按要求 (Id,RoleId,MenuId,ActionCode,CreateDate,TenantId)、Id=NEWID()。
- **逐租户**：`CROSS JOIN (SELECT Id FROM Sys_Tenants) t`（Sys_Tenants PK=Id[uniqueidentifier]，实体 Sys_Tenant : BaseEntity 确认）。
- **动作定义 13/租户**：902 add/edit/delete；903 add/edit/delete；904 add/edit/delete/generate；905 publish/deactivate/adopt；906 无。
- **幂等**：两表 INSERT 均 NOT EXISTS(TenantId+MenuId+ActionCode)。
- 含验证查询 + 回滚段（仅删本种子 902-905 动作码）+ CP6DB 连接串头注释（照 space-menu-seed-2.sql 骨架）。

## Self-review
- 映射表逐字执行；zone/aisle/rack/scene/template/connector 全变更统一 space-floor:edit，符「避免动作爆炸」裁决。
- **偏差（已文档化）**：CodeRuleController.Preview 是 POST 但只读（合成样例不写库），映射表未列。按测试豁免清单显式登记为只读、不贴特性；控制器头注释与测试注释均说明。这是映射表「唯一权威」下唯一的读写语义与 HTTP 动词不一致点，判定为只读豁免。
- 反射测试用 CustomAttributeData 而非改共享 RequirePermissionAttribute 加公开属性——零涟漪，且实现完整白名单逐字校验（未降级）。
- 种子 MenuId 依赖 902-905 菜单存在（波2/3 已种），头注释已声明前提；本种子只登记动作点与授权，不建菜单。
- 全量测试绿、build 0 err、单 commit、Co-Authored-By 齐备。
- 未触碰 picture/、shots/ 等 session 起始既存的无关未跟踪文件。
