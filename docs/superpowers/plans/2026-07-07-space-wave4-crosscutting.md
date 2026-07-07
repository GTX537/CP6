# Space 波4：横切接线（审计 / 错误码 BizException 化 / 权限点 / SignalR SpaceHub）实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 补齐 Space 模块四项横切欠账：字段级审计（11 实体挂 IAuditable）、错误码 BizException 化（56 处 throw + 24 码五语词条 + 36 处 catch 清理）、细粒度权限点（9 控制器 + 种子 + 前端 v-permission）、SignalR SpaceHub（发布事件推送 + events 页自动刷新）。

**Architecture:** 基线 main=a2d1bdc。底座全部现成（CP6Context 审计拦截器 / BizExceptionMiddleware / RequirePermission+PermissionAggregator / SignalR 基建），本波纯接线。两项架构决策（2026-07-07 主控自洽）：① **BizException.cs 文件迁入 CP6.Core 项目但保留 namespace `CP6.WebApi.Localization`**——Core 服务才能抛它，30+ 既有 using/测试零改动（零涟漪迁移，文件头注释说明）；② **SpaceHub 推送走接口注入**：Core 定义 `ISpaceNotifier`，WebApi 实现 `SignalRSpaceNotifier : ISpaceNotifier`（照 `CP6.WebApi/Services/SignalRWmsNotifier.cs` 模式），不用 DeadLetterNotifier 的反射范式。

**Tech Stack:** 同前波。改动面已由 2026-07-07 探查精确盘点（throw 56 处 8 文件 / 去重码 24 / controller catch 36 处 6 文件[另 3 处 409 catch 保留] / 测试 7 文件 32+15 断言）。

## Global Constraints

- **权限约定（本波确立，写进各 controller 头注释）**：变更端点贴 `[RequirePermission(menuKey, action)]`，读端点仅 `[Authorize]`（Fin CostController 同款约定）。键前缀=波2/3 种子已填的 MenuKey（space-site/space-floor/space-code-rule/space-publish/space-events）。
- **权限点映射表（唯一权威）**：
  | 端点域 | MenuKey:action |
  |---|---|
  | SpaceMasterController site C/U/D | space-site:add / edit / delete |
  | SpaceMasterController floor C/U/D | space-floor:add / edit / delete |
  | zone/aisle/rack C/U/D + DeleteAisle/DeleteRack(含 mode) + SceneController 全部变更(scene save/import/bind-codes) + TemplateController C/U/D/clone + ConnectorController 全部变更 | **space-floor:edit**（楼层编辑单一动作，避免动作爆炸） |
  | CodeRuleController rule C/U/D | space-code-rule:add / edit / delete |
  | generate-codes + gen-code | space-code-rule:generate |
  | publish / deactivate / adopt | space-publish:publish / deactivate / adopt |
  | events、全部 GET、SpaceLocate/Stock/Advanced 只读 | 仅 [Authorize] 不贴 |
- BizException 用法：`throw new BizException("E-SPACE-401")`（内联中文消息**删除**，移入词条）；需 409 的用 `new BizException("E-SPACE-009", 409)`。测试断言范式：`var ex = await Assert.ThrowsAsync<BizException>(...); Assert.Equal("E-SPACE-401", ex.Code);`（全库 30+ 先例）。
- **保留 3 处** `LocationPublishController` 的 `catch (DbUpdateConcurrencyException)` 409（EF 直抛，middleware 不管它）；`SpaceMasterService.cs:400` 的服务层并发转译改 `throw new BizException("E-SPACE-009", 409)`（其所在 controller 的普通 catch 删除后由 middleware 出 409）。
- I18nSpaceScreenSeed.cs（C# 种子，24 码五语）照 `CP6.WebApi/Seed/I18nFinScreenSeed.cs:163-171` 逐字格式；Program.cs `:1804` 附近 Concat 链追加一行。错误码属代码资产走 C# 种子；页面词条继续 SQL（既有两个 space-i18n-seed 不动）。
- 审计实体清单（11 个，全部 `CP6.Entity/DomainModels/Space/`）：Space_Site/Floor/Zone/Aisle/Rack/Location/Template/CodeRule/Marker/Connector/ConnectorStop——类声明追加 `, IAuditable`（WmsBin **不挂**：机器写入的消费表，逐发布行审计是噪音）。
- 测试命令与基线：后端 `dotnet test CP6.Tests/CP6.Tests.csproj`（1557 passed/5 skipped）；前端（cp6.web/）type-check 8192 / `npm run test`（364）/ build。
- 提交 `feat(space):`/`refactor(space):` + `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`，每 Task 一个 commit。

---

### Task 1: 审计接线（11 实体 + 行为测试）

**Files:** 11 个 Space 实体（追加 `, IAuditable`，using CP6.Entity 已在基类同 namespace 无需加）；Test: `CP6.Tests/Space/SpaceAuditTests.cs`（新建，照 `CP6.Tests/Sys/FieldAuditCaptureTests.cs` 的桩与断言范式）

- [ ] Step 1: 测试先行——`Space_Site` create→Operation=1 行；update SiteName→Operation=2 且 diff Field/Old/New 断言；（RED：无 IAuditable 时 `Assert.Empty` 反向前提先确认现状零行）
- [ ] Step 2: 11 实体逐个追加接口；跑新测试 GREEN + 全量 1557→1559 级（+2 测试）
- [ ] Step 3: Commit `feat(space): 11 实体接入字段级审计（IAuditable）`

---

### Task 2: BizException 迁 Core + I18nSpaceScreenSeed（零行为变化的基建步）

**Files:**
- Move: `CP6.WebApi/Localization/BizException.cs` → `CP6.Core/Localization/BizException.cs`（**namespace 保持 `CP6.WebApi.Localization` 不变**，文件头加注释「定义于 Core 供服务层抛出；namespace 保留历史值以零涟漪迁移，2026-07-07 波4」）
- Create: `CP6.WebApi/Seed/I18nSpaceScreenSeed.cs`——24 码五语（探查清单为准：E-SPACE-001/002/003/004/006/007/009/301~307/401/402/403/405/407/408/501/502/601 + W-SPACE-404）。**中文消息来源**：throw 站点现有内联消息（有 12 个左右）+ 契约 04 §11 消息表 + 总纲 Spec §16.3 语义（以代码/契约为准）——每码给五语，多义码（如 E-SPACE-002 参数校验族）用概括文案。
- Modify: `CP6.WebApi/Program.cs`（Concat 链 `:1804` 附近加 `.Concat(CP6.WebApi.Seed.I18nSpaceScreenSeed.Items)` + 分段注释）

- [ ] Step 1: 迁移文件（git mv + csproj 无需改[SDK 风格自动包含]），全量编译+测试确认零破坏（1559 级不变）
- [ ] Step 2: 种子 + Concat；`dotnet build CP6.slnx` 0 err；启动期种子逻辑不跑测试（幂等 MERGE 在启动），人工双检 24 码齐全与五语非空
- [ ] Step 3: Commit `refactor(space): BizException 迁 Core（namespace 零涟漪）+ E-SPACE 24 码五语词条种子`

---

### Task 3: Space 服务 BizException 化（56 throws + 36 catch + 7 测试文件）

**Files:**
- Modify: `CP6.Core/Services/Space/` 8 文件（CodeEngineService 10 处/SpaceMasterService 26/SceneService 7/LocationPublishService 5/TemplateService 3/ConnectorService 3/SceneIoService 1/LocationGeometryService 1）——`throw new InvalidOperationException("E-SPACE-xxx[: 中文]")` → `throw new BizException("E-SPACE-xxx")`；`SpaceMasterService:400` → `new BizException("E-SPACE-009", 409)`；同文件补 `using CP6.WebApi.Localization;`
- Modify: `CP6.WebApi/Controllers/Space/` 6 控制器——删 36 处 `catch (InvalidOperationException)`（try 壳一并简化）；**保留** LocationPublishController 3 处 DbUpdateConcurrencyException 409 catch
- Modify: 7 测试文件（SpaceMaster/Scene/LocationPublish/CodeEngine/Template/BindCodes/ConnectorServiceTests）——`ThrowsAsync<InvalidOperationException>` 32 处 → `ThrowsAsync<BizException>`；`StartsWith("E-SPACE-xxx", ex.Message)` 15 处 → `Assert.Equal("E-SPACE-xxx", ex.Code)`；其余码文本引用同步
- **CodePrecheck.Validate 返回的错误码字符串**（E-303/305/306 是返回值非异常）**不动**——它们进 PrecheckErrors 列表由前端展示，非 throw 路径

注意：BizExceptionMiddleware 只在 HTTP 管道生效——单测直接调 Service 断言 BizException 本身，不经中间件，无翻译依赖。前端影响：错误 message 从「E-SPACE-401: 中文」变为词条译文（miss 回退码本身）——波3 各页 catch 逻辑不解析 message 内容（只有 409 分支看 status），无前端改动；**但发布中心 spec 若 mock reject 消息文本需核对**。

- [ ] Step 1: 按文件逐个替换（一文件一编译），controller catch 清理，测试文件更新
- [ ] Step 2: 全量 `dotnet test` 绿（数量不变 1559 级）；前端 `npm run test` 绿（확认无依赖 message 文本的 spec）
- [ ] Step 3: Commit `refactor(space): 错误码 BizException 化——56 throw/36 catch/7 测试文件，中间件统一翻译`

---

### Task 4: 权限点接线（9 控制器 + 反射测试 + RoleAction 种子）

**Files:**
- Modify: `CP6.WebApi/Controllers/Space/` 全部 9 控制器——按 Global Constraints 映射表贴 `[RequirePermission]`（`using CP6.Core.Auth;`），控制器头注释写权限约定一行
- Create: `CP6.Tests/Space/SpacePermissionAttributeTests.cs`——反射断言：9 控制器的全部 POST/PUT/DELETE action（HttpPost/HttpPut/HttpDelete 特性）都带 RequirePermissionAttribute 且 (menu,action) 在映射白名单内；GET action 都不带（防误贴）。豁免清单显式列出（如无）。
- Create: `docs/seeds/space-roleaction-seed.sql`——①`Sys_MenuAction` 登记各菜单可授权动作（902:add/edit/delete；903:add/edit/delete/edit（编辑器域并入 edit）；904:add/edit/delete/generate；905:publish/deactivate/adopt；906 无）幂等 NOT EXISTS，**逐租户**（`CROSS JOIN (SELECT Id FROM Sys_Tenants) t` 或按现有租户循环——先看 Sys_MenuAction 的 TenantId 语义与既有数据惯例，qa 种子 02_seed_roleaction_BC.sql 是显式 TenantId 先例）；②`Sys_RoleAction` 给 RoleId=1 全动作授权，列序 `(Id,RoleId,MenuId,ActionCode,CreateDate,TenantId)`、Id=NEWID()；③验证查询+回滚段+头注释（连接串 CP6DB 套路）。

- [ ] Step 1: 反射测试先行（RED：0 个特性）→ 贴特性 → GREEN
- [ ] Step 2: 种子 SQL 人工双检；全量测试绿（RequirePermission 在单测无 HTTP 管道不触发——控制器单测若有直调 action 的，确认不受影响）
- [ ] Step 3: Commit `feat(space): 细粒度权限点——9 控制器 RequirePermission + MenuAction/RoleAction 种子`

---

### Task 5: 前端 v-permission 接线（4 页按钮）

**Files:** SpaceSiteView/SpaceFloorView/SpaceCodeRuleView/SpacePublishView（+spec 各 1 断言）

规格：按钮贴 `v-permission="'<menuKey>:<action>'"`，键与 Task 4 映射表逐字一致——site 页新建/编辑/削除（add/edit/delete）；floor 页同（编辑器跳转按钮**不贴**——页面级菜单权限已管）；code-rule 页新建/编辑/削除/生码相关（预览只读不贴）；publish 页生成编码（space-code-rule:generate）/发布（space-publish:publish）/停用（deactivate）/采纳（adopt）。指令 fail-open（store 未加载保留元素）——admin 全授权下 UI 无变化。
测试：每页 spec 加 1 断言——mock permission store `loaded=true` 且缺某键时对应按钮从 DOM 移除（v-permission mounted 移除元素；照 directives/permission.ts 行为；store mock 用 pinia testing 或直接 stub usePermissionStore——看既有 spec 有无先例，无则最小 stub）。

- [ ] Step 1: TDD → 实现 → type-check/vitest/build 三件套 → Commit `feat(space): 管理与发布页按钮接入 v-permission`

---

### Task 6: SignalR SpaceHub（发布推送 + events 页自动刷新）

**Files:**
- Create: `CP6.Core/Services/Integration/ISpaceNotifier.cs`——`Task NotifyLocationPublishedAsync(string batchNo, int count, string status)`（+ `NoOpSpaceNotifier` 内联，测试/降级用）
- Create: `CP6.WebApi/Hubs/SpaceHub.cs`（照 WmsHub：OnConnected/Disconnected 日志；无分组——Space 事件低频全播即可，YAGNI）
- Create: `CP6.WebApi/Services/SignalRSpaceNotifier.cs`（照 SignalRWmsNotifier：注入 `IHubContext<SpaceHub>`，`Clients.All.SendAsync("LocationPublished", new { batchNo, count, status })`，try/catch 吞错记日志——推送失败不影响业务）
- Modify: `CP6.Core/Services/Space/LocationPublishService.cs`——ctor 追加 `ISpaceNotifier notifier`（第 7 参）；`PublishFloorAsync` 成功 Commit 后、`DeactivateAsync` 兜底事件后各调一次 notify（**在事务 Commit 之后**，推送不进事务）；测试帮手 MakePublishSvc 加 `new NoOpSpaceNotifier()`
- Modify: `CP6.WebApi/Program.cs`——DI `AddScoped<ISpaceNotifier, SignalRSpaceNotifier>()` + `app.MapHub<CP6.WebApi.Hubs.SpaceHub>("/hubs/space");`（:2524 后）
- Create: `cp6.web/src/utils/spaceHub.ts`（照 wmsHub.ts 单例：withUrl('/hubs/space') 无 accessTokenFactory，cookie 隐式认证）
- Modify: `cp6.web/src/views/space/lifecycle/SpaceEventsView.vue`——onMounted 订阅 `LocationPublished` → `listRef.reload()`（回第 1 页可接受）；onUnmounted 取消订阅（照 IoT 轮询清理先例）
- Test: 后端 `Mock<IHubContext<SpaceHub>>` 链（照 CP6.Tests/DeadLetterNotifierTests.cs:49 范式）验证 SendCoreAsync 参数；LocationPublishServiceTests 加 1 断言（publish 后 notifier 被调——用记录桩）；前端 spec：mock spaceHub 模块，事件回调触发 reload

- [ ] Step 1: TDD → 实现 → 后端全量 + 前端三件套 → Commit `feat(space): SpaceHub 发布推送 + 事件页自动刷新（ISpaceNotifier 接口注入）`

---

### Task 7: 回归 + 真库 QA（波4 DoD）

- [ ] Step 1: 回归门四项（后端 1559 级 / 前端 364+新 / type-check / build）
- [ ] Step 2: 真库（容器 curl/sqlcmd 模式照前波）：执行 space-roleaction-seed.sql → 验证 MenuAction/RoleAction 行数；**403 验证**：建一个无 Space 动作权限的测试角色+用户（或用既有非 admin），调 POST /api/space/site → 403 `{code:403,message:无权限...}`；admin 调 → 200。
- [ ] Step 3: 审计验证：admin 改一个 site 的 SiteName → 查 Sys_FieldAuditLogs 有 Modified 行（EntityName=Space_Site）。
- [ ] Step 4: BizException 验证：无库存前提下停用一个草稿库位（Status=0）→ 400 且 message 为**词条译文**（ja culture 下日文）而非「E-SPACE-004: 中文」原串——验证 middleware+种子链路。409/告警链路有波1-3 证据不重验。
- [ ] Step 5: SpaceHub 冒烟：容器网络内 SignalR 客户端不便——降级为「发布一次 → 检查 cp6-api 日志无推送异常」+ 前端单测已锁订阅逻辑；真浏览器验证并入波5 视觉走查票。清理义务同前波。
- [ ] Step 6: 证据入报告；缺陷则 fix commit。

---

## 自检记录

- **覆盖对照 2026-07-05 横切基准探查**：审计=Task 1（11 实体，拦截器免改）；权限=Task 4/5（RequirePermission+MenuAction/RoleAction 种子+v-permission；MenuKey 波2/3 已备）；错误码=Task 2/3（C# 种子+BizException+catch 清理；E-7xx/8xx 段是 W-SPACE-701/702/801 库存叠加/路径**警告**——属 P2.5 未实现功能的预留码，本波不种[无消费方]，记波5）；SignalR=Task 6（SpaceHub+接口注入）。菜单种子已在波2/3 完成（含 MenuKey），本波无菜单改动。
- **决策留档**：BizException 迁 Core 保留 namespace（零涟漪）；编辑器域变更端点统一 space-floor:edit；WmsBin 不挂审计；SpaceHub 无分组全播（低频 YAGNI）；W-SPACE-404 是 throw 路径要进种子，W-701/702/801 不种。
- **执行顺序**：1→7 串行（3 依赖 2 的迁移；5 依赖 4 的映射表；6 独立但排 5 后；7 收尾）。Task 3 是重灾区（56+36+47 处机械替换）——executor 按文件逐个提交前编译，防大爆炸。
