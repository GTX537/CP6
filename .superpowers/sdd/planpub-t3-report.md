# M-PLAN/PUB T3 报告：反射 fail-closed 测试 + 403 拒绝用例

分支 `feat/m-planpub-crosscutting`。纯测试任务，**零生产代码改动**（RED 反向验证的临时注释已还原，`git status --short CP6.WebApi/` 空）。真相源 `docs/seeds/planpub-permission-keys.md` 未动，T2 交付物（11 贴点 / 731-732 MenuKey / PlanPubPermissionSeed）未动，无 BLOCKED。

## 必读完成（按 brief 顺序）
1. 真相源 §一(14 行端点表)/§二(4 menu-key)/§三(高危 3)/§四(4a view **贴点**非旁路 + 4b Attachment 3 组件豁免)/§五(裁定)/§六(命门+HttpPut 跨波票)/§七(计数) 全读。
2. 前波先例：`OawfPermissionAttributeTests`（commit 1e75f38，**双命名空间 + 非空豁免表**，本波最贴样板）+ `PurPermissionAttributeTests`（HttpPut/豁免口径）精读，结构照抄按 Plan/Pub 实况调整。
3. 403 先例：`PurHighRiskDenialTests` / `pur-t3-report.md`「403 口径」段——照其已裁定的「无权限身份 → 真实聚合链 → 403」口径落地。
4. T2 报告：`planpub-t2-report.md`（11 贴点逐字核对、Attachment 三端点零触碰交 T3 豁免表）。

## 接口事实自查（brief 要求项）
- **基类扫描口径据实**：grep 逐类核实——Plan.MrpController / Plan.ItemPlanningPolicyController / Pub.CodeGenController **直接 `: ControllerBase`**；Pub.SeqController / Pub.AttachmentController 经 `LocalizedControllerBase`（**abstract**，位于 `CP6.WebApi.Controllers` 命名空间——不在 Plan/Pub 扫描面且被 `!t.IsAbstract` 排除；**零 [HttpXxx] 声明**，仅暴露 Localizer）继承 ControllerBase。各级基类均无端点声明 → 写端点均为子类手写 → `BindingFlags.DeclaredOnly` 不漏扫。注释与源一致（无 MES 那类失实抄袭），并注明未来共享基类挂 [HttpXxx] 需改策略。
- **IsMutating 谓词含 HttpPut**：SeqController.Update=`[HttpPut]`（本波唯一 PUT，pub-seq:edit）经 `HttpPut_Endpoint_IsScannedAndGuarded` 用例**显式钉死**（确为 HttpPut + 被 IsMutating 认定 + 已贴 (pub-seq,edit)）。**HttpPatch 未含**——全仓五波谓词均未含 PATCH（已立跨波 sweep 票），本波不扩；另加 `NoPatchEndpoints_InScope` 诚实自检钉死本波扫描面 PATCH 端点==0，证明不扩不损完备性（注释注明该票防误读为遗漏）。

## 实现清单

### 需求1 · 反射 fail-closed 测试 —— `CP6.Tests/PlanPubPermissionAttributeTests.cs`（7 用例，双命名空间 Plan∪Pub）
- **discovery 守卫** `PlanPubControllers_AreDiscovered`：Plan(2)+Pub(3)=**5**（防单侧空扫假绿）。
- **fail-closed 核心闸** `EveryMutatingAction_IsGuarded_WithConventionalKeyOrExemption`：每个非 GET 端点**要么**带 [RequirePermission]、**要么**在**组件豁免清单**内（Attachment upload/delete/rebind，§四.4b/§五.4）；贴点精确 == **11**、豁免命中 == **3**（14 = 11 + 3 收口）。menu 匹配 `^(plan|pub)-[a-z0-9-]+$` + ∈ 4 键白名单；action 逐词落 `ActionVocabulary`（9 词：run/convert/save/confirm/ignore/add/delete/edit/**view**——view 是 CodeGen.PreviewInline 贴点，走核心闸非旁路）。既贴又豁免 = 语义冲突报错。
- **键面 oracle 双向相等** `ResourceKeys_MatchIndependentOracle_Exactly`：11 收集集 ↔ 测试内独立 `ExpectedResourceKeys`（11，誊自真相源 §一/§七，零引用生产常量）双向 Except 相等 + 计数 11 + 资源键 1:1 无重复 + 前缀 ∈ 4 键白名单 + 零下划线。
- **组件豁免防腐** `ComponentExemptions_AreAllStillUntaggedMutatingEndpoints`：3 条豁免逐条实存 + 确为变更端点 + 当前未贴键（防清单陈旧遮蔽真·写端点丢键）。
- **HttpPut 显式覆盖** `HttpPut_Endpoint_IsScannedAndGuarded`（见上「接口事实自查」）。
- **PATCH 诚实自检** `NoPatchEndpoints_InScope`：扫描面 PATCH 端点==0。
- **只读 GET 误贴防护** `NoReadOnlyGetAction_HasRequirePermission`。

### 需求2 · 403 拒绝用例 —— `CP6.Tests/PlanPub/PlanPubHighRiskDenialTests.cs`（2 用例）
- 覆盖真相源 §三**高危 3 键全部**：`plan-mrp:run`(MrpController.Run)/`plan-mrp:convert`(MrpController.Convert)/`pub-codegen:save`(CodeGenController.Save)。
- `UnauthorizedUser_Is403_OnEveryHighRiskEndpoint`：走**真实后端聚合链**（PermissionAggregator→CurrentPermissionContext→PermissionService，InMemory DB），登录用户 "u" **仅授 pub-seq:add** 一个良性非高危键 → 对每个高危键经生产 `RequirePermissionAttribute.OnAuthorizationAsync` 请求 → 断言 `StatusCodes.Status403Forbidden`。外加**反射交叉核验**：每个 (控制器.方法) 确携该 (menu,action)，端点改名/改键则 403 oracle 亦破。
- `BenignGrantedAction_PassesChain`：正控——有 pub-seq:add 的请求放行（证明链非全盘拒绝假绿）。

## TDD 证据

**RED**（临时注释 `MrpController.Convert` 的 `[RequirePermission("plan-mrp","convert")]`，一个高危键，同时命中三闸）：
```
dotnet test --filter "...PlanPubPermissionAttributeTests|...PlanPubHighRiskDenialTests"
Failed! - Failed: 3, Passed: 6, Total: 9
  EveryMutatingAction_IsGuarded_WithConventionalKeyOrExemption [FAIL]
    MrpController.Convert：变更端点缺 [RequirePermission] 且不在组件豁免清单   （taggedCount 11→10）
  ResourceKeys_MatchIndependentOracle_Exactly [FAIL]
    oracle 有但源码缺（漏贴/改键）: plan-mrp:convert
  UnauthorizedUser_Is403_OnEveryHighRiskEndpoint [FAIL]
    MrpController.Convert：生产端点 [RequirePermission] = 无，与高危 oracle (plan-mrp,convert) 不符
```
即 brief 要求的「移除任一贴点 → 双重失败」：反射计数闸 + oracle 闸 + 403 交叉核验**三闸同破**，皆有牙。

**GREEN**（还原该贴点后）：
```
dotnet test --filter "...PlanPubPermissionAttributeTests|...PlanPubHighRiskDenialTests"
Passed! - Failed: 0, Passed: 9, Skipped: 0, Total: 9
```
还原后 `git status --short CP6.WebApi/` 空——最终交付零生产改动。

## 403 用例口径与依据
口径 = **已登录但无该操作权的用户 → 真实聚合链 → 生产 RequirePermissionAttribute → 403**，逐字照 `PurHighRiskDenialTests` / `PermissionChainIntegrationTests` / `FourGranularityIntegrationTests`（授一无关键 pub-seq:add 放行 + 目标高危键 403）。**「无认证 401」取舍**：本仓 [Authorize] 认证层（401）在 HTTP 传输层，进程内 RequirePermission 过滤器不经它；且 `CurrentPermissionContext.GetAsync` 对无 Identity.Name 会 throw "未登录"（非返回 401）。故按 brief 明列的可行口径「无权限身份 → 403」落地，与既有先例断言口径（`ObjectResult.StatusCode==403`）一致，非静默缩水，无偏离先例。

## 全量结果
```
dotnet test CP6.Tests/CP6.Tests.csproj
Passed! - Failed: 0, Passed: 2190, Skipped: 5, Total: 2195, Duration: 1m53s
```
基线 2181 + 新增 9（反射 7 + 403 2）= **2190 绿 / 5 skip**。5 skip 为既存结构性跳过，非本任务引入。构建 0 error；本任务零新增 xUnit 警告（既有 4 处 PendingCookie/BudgetVsActual/InboxService/InboundService 警告 pre-existing，非本 diff）。

## 文件变更
- 新增 `CP6.Tests/PlanPubPermissionAttributeTests.cs`（反射 fail-closed，7 用例）
- 新增 `CP6.Tests/PlanPub/PlanPubHighRiskDenialTests.cs`（403 高危拒绝，2 用例）

## 自审
- **oracle 独立**：`ExpectedResourceKeys`(11)/`ActionVocabulary`(9)/`MenuKeyWhitelist`(4)/`ComponentExemptions`(3)/`HighRiskKeys`(3) 全为测试内字面量，零引用 PlanPubPermissionSeed.Actions 或控制器常量。
- **计数精确 11 + 豁免 3 + 反向验证**：taggedCount==11 与 oracle==11 双闸，exemptHit==3，RED 实证移除即三破。14 非GET端点 = 11 贴点 + 3 组件豁免，与真相源 §七逐字吻合。
- **豁免表非空且防腐**：Attachment 3 端点显式登记（附逐条依据注释），`ComponentExemptions_AreAllStillUntaggedMutatingEndpoints` 防陈旧；PreviewInline 走 view 贴点不进豁免表。
- **HttpPut 覆盖 + PATCH 诚实标注**：唯一 PUT 显式钉死，PATCH 扫描面==0 自检 + 跨波 sweep 票注释。
- **基类口径据实**：grep 核实 3+2 继承结构，注释与源一致。
- **403 覆盖 3 高危键**：断言口径与先例一致 + 交叉核验绑真实端点。
- 测试输出干净。

## Concerns
1. **403 为进程内链路测**（非 HTTP e2e）：与本仓既有 403 先例（Pur/OA/MES 各波）同构，不经 Kestrel/[Authorize]；线上 401/403 由部署冒烟另证（沿用本项目历波「反射闸 + 部署冒烟」分工）。既定口径而非本任务缺口。
2. **跨波 sweep 票（PATCH 不在五波 IsMutating 谓词）**：本波不扩，已在测试注释 + `NoPatchEndpoints_InScope` 自检钉死本波零 PATCH 端点，无实质暴露面；票据仍归跨波 sweep（M-PUR 波已立）。
3. **Attachment 组件豁免 follow-up**（真相源 §五.4/§六）：upload/delete/rebind 现仅 `[Authorize]` 登录闸 + `EnforceBizPermission` 门控 download/preview（未覆盖写端点，delete 尤为高危形态）。本波按登录闸豁免留票，非本任务范畴。
4. **plan-mrp:convert 走 P1 桩**（真相源 §六）：convert 端点当前委托 `PlanToPrServiceStub`（不实写 PurchaseRequests），闸门已先落地——桩替换为真实实现时键覆盖不变。非本任务缺口。
无其它 concern。反射闸此后锁死 Plan/Pub 权限面，可交 fable 终审。
