# 终审修复报告 — feat/space-wave1-publish-loop

日期：2026-07-06
基线：1528 passed / 5 skipped → 修复后 1532 passed / 5 skipped（+4 新测试，全绿）
构建：`dotnet build CP6.slnx` → Build succeeded, 0 Errors。

## Finding #1（Critical）— WarehouseCd 长度守卫

**落点**：`CP6.Core/Services/Space/LocationPublishService.cs`
- `ResolveWarehouseCdAsync`（约 :256-273）：解析出 `warehouseCd` 后加长度守卫，`> 10` 抛
  `InvalidOperationException("E-SPACE-405: 站点编码超过 10 字符且未配置 WarehouseCd 映射，无法发布/停用")`。
- **错误码核实**：grep `E-SPACE-405|E-SPACE-407` → 仅出现在总纲 Spec（含义「WarehouseCd 映射缺失」，与本守卫语义一致），
  代码中未被占用 → 用 405，无需改 407。
- **fail-fast 时序**：守卫在 `BuildItemAsync`（发布）/`DeactivateAsync`（停用）内被调；发布路径循环虽先写内存态
  `l.Status=1`，但抛异常在 `_db.SaveChangesAsync()` 之前——内存翻转从不落库，外层发布事务未 Commit 即回滚 →
  库位 Status 不持久化、无 bin、无事件、无孤儿。注释已写明该权衡。
- 显式配置的 `Space_Site.WarehouseCd` 本身受 `MaxLength(10)` 列约束，超长只可能来自 SiteCode 默认回退。

**测试证据**：`LocationPublishServiceTests.Publish_SiteCodeOver10Chars_NoMapping_Throws_E405_NoOrphan`
（SiteCode="SITE0123456" 11 字符 + WarehouseCd=null → 抛 E-SPACE-405；AsNoTracking 读库快照 Status 仍 0；IntegrationEvents 0 行）。

## Finding #2（Important）— 消费端失败时清理自身 tracker 污染

**落点**：`CP6.Core/Services/Wms/WmsBinConsumer.cs`
- `ConsumeAsync` 末尾 `await _db.SaveChangesAsync()` 包 try/catch，失败调 `DetachOwnWrites()` 后 rethrow。
- 新增 `internal void DetachOwnWrites()`：`_db.ChangeTracker.Entries<WmsBin>().Where(State is Added or Modified).ToList()`
  逐个置 `EntityState.Detached`。**只清自己写入的 WmsBin 行，不用 ChangeTracker.Clear()**（避免断开 Worker 正在跟踪的事件行）。
- `internal` + 既有 `InternalsVisibleTo("CP6.Tests")` → 可直接单测。

**可测性结论**：InMemory provider 难以可靠诱发 SaveChanges 持久失败；且 #3 的 Local 双查修复恰好消除了「同批同 PK 双 Add」
这一 InMemory 唯一能抛的诱发路径。故按任务授权，以直接构造 tracker 状态的单测锁定 detach 语义（Add 一个 WmsBin →
调 `DetachOwnWrites()` → 断言 `Entry.State==Detached`），代码走读证明 catch/rethrow 包裹与只清自身 WmsBin 的范围正确。

**测试证据**：`WmsBinConsumerTests.DetachOwnWrites_DetachesAddedWmsBins`。

## Finding #3（Important）— join 锚碰撞转 REJECTED + 批内双 Add 兜底

**落点**：`CP6.Core/Services/Wms/WmsBinConsumer.cs`
- Id 查找（约 :29-33）：`FirstOrDefaultAsync(b => b.Id == item.LocationId)` → **Local 双查**
  `_db.WmsBins.Local.FirstOrDefault(...) ?? await _db.WmsBins.FirstOrDefaultAsync(...)`（采用更稳的 Local 方案，
  能看见同批未保存 Added 实体，顺手兜掉批内同 LocationId 双 Add；注释写明 PK=全局唯一 GUID 跨租户误命中现实无虞的权衡）。
- UPSERT 分支 `bin==null`（约 :95-110）：先按锚补查
  `Local.FirstOrDefault(WarehouseCd==... && LocationCode==...) ?? await FirstOrDefaultAsync(...)`；命中且 `Id != item.LocationId`
  → `REJECTED`（reason 注明锚被占用）+ `result.Success=false` + `continue`——避免 Add 撞唯一索引走异常毒化链。

**测试证据**：
- `WmsBinConsumerTests.Upsert_AnchorCollision_DifferentLocationId_Rejected`（预置 bin A 锚 X，UPSERT B 锚 X → REJECTED + Success=false + WmsBins 仍 1 行）。
- `WmsBinConsumerTests.Upsert_SameBatch_DuplicateLocationId_NoDoubleAdd`（批内同 LocationId 两条 UPSERT → 第二条走版本门 SKIPPED，不抛，行数=1）。

## 回归 / 约束遵守

- `dotnet test CP6.slnx --no-build` → **Passed! Failed: 0, Passed: 1532, Skipped: 5, Total: 1537**。
- 未触碰 BridgeHookBase / CP6Context 等共享基类，全部修改落在本分支创建/修改的文件内。
- H6 墓碑/幂等门语义未改（既有 9+ 消费端测试全绿）；多租户铁律照旧（不写 .Where(TenantId)、不写 TenantId 盖章）。

---

## 波1.5 终审修复（2026-07-06）

五处终审 finding 一次提交修复，全部落在本分支已拥有的文件内（SpaceMasterService / SceneService / WmsBinDeactivator + 对应测试）。

### C1 (Critical) — 删货架 rehome 跨 site 换架打穿 WarehouseCd 锚
- 落点：`CP6.Core/Services/Space/SpaceMasterService.cs` DeleteRackAsync rehome 分支（约 L410）。
- 修法：比照巷道 rehome E-407 先例，加 `target.ZoneId != rack.ZoneId → throw E-SPACE-002: 目标货架与源货架不在同一库区，无法改挂`（同 zone ⇒ 同 floor 同 site，WarehouseCd 锚绝不漂移）。
- 测试：`DeleteRack_ModeRehome_TargetInDifferentZone_Throws`（跨 zone → 抛错，源架/库位原样）。

### I1 (Important) — 两个 rehome 分支非原子，重试黑洞
- 落点：SpaceMasterService.cs DeleteAisleAsync / DeleteRackAsync 的 rehome 分支。
- 修法：各包一层 `IsRelational() ? BeginTransactionAsync() : null` + try/catch(rollback)/finally(dispose)，Commit 在三步（改挂 SaveChanges + Republish + 删源 SaveChanges）全部完成后；RepublishAsync 的 `CurrentTransaction == null` 嵌套守卫自动加入被包裹事务。deactivate 分支保持不包事务（同步 RPC 决策模型，未动）。

### I2 (Important) — mode 白名单 + rehome 在 published=0 时静默毁弃草稿
- 落点：两个 Delete 方法入口 + rehome 判定结构。
- 修法：① 入口处（取 published 之前）`mode is not (null or "deactivate" or "rehome") → throw E-SPACE-002: 未知 mode`。② rehome 从 switch 提出为独立分支，无论 published 数量都走改挂（published>0 才 RepublishAsync）；巷道侧 racks 改挂 targetAisleId（null 合法）。
- 测试：`DeleteRack_ModeRehome_DraftOnly_MovesLocations_NotDeleted`（草稿改挂 target、不删不发事件）、`DeleteRack_UnknownMode_Throws_NoSideEffect`（未知 mode 零副作用）。

### I3 (Important) — 同帧缩格绕过 + 已发布库位级改挂不 republish
- 落点：`CP6.Core/Services/Space/SceneService.cs` SaveSceneAsync Locations 循环 + 新增 `AssertLocationInBoundsAsync` 私有帮手。
- 修法：① `incomingRack = ld.RackId == Guid.Empty ? null : ld.RackId` 归一化。② update 分支 `existing.Status==1 && 落位(RackId/Col/Level/Depth)有变 → throw E-SPACE-004`（回显不触发）。③ update+create 双分支 `incomingRack != null` 时取 rack（同帧缩格经 identity-map 返回新网格），越界 → throw E-SPACE-002；rack 查不到放过（FK 语义兜）。
- 测试：`SaveScene_SameFrameShrinkAndMovePublishedOutOfBounds_Throws_NothingSaved`、`SaveScene_RepointPublishedLocation_Throws004`、`SaveScene_MoveDraftLocationOutOfBounds_Throws002`。
- 既有 H1 测试 `SaveScene_CannotFlipPublishedStatus_OrCodeOrigin` 微调：种子 loc 补 Col/Level/Depth=1 与 DTO 一致（回显），避免误触新 E-004 落位护栏，专测 Status/CodeOrigin 不可覆盖的原语义不变。

### M1 (cheap) — WmsBinDeactivator 版本可回退
- 落点：`CP6.Core/Services/Wms/WmsBinDeactivator.cs` L37。
- 修法：`bin.Version = Math.Max(bin.Version, req.Version);`（版本单调不回退，防陈旧停用重开乱序窗）。墓碑分支为新建行，不受影响。

### 回归结果
- 覆盖测试 `--filter "DeleteAisle_|DeleteRack_|SceneServiceTests"` → **Passed! 29/0/0**。
- 全量 `dotnet test CP6.Tests/CP6.Tests.csproj --no-build` → **Passed! Failed: 0, Passed: 1556, Skipped: 5, Total: 1561**（基线 1550 + 新增 6）。
- 构建 `dotnet build CP6.slnx` → **Build succeeded, 0 Error**。

---

## 波3 终审修复 — feat/space-wave3-lifecycle-ui

日期：2026-07-07
前端基线：363 passed → 修复后 364 passed（+1 新测试，全绿）。
type-check(NODE_OPTIONS=--max-old-space-size=8192 vue-tsc --build) → 0 Errors；`npm run build` → ✓ built。

### #1（Important，必修）— SpacePublishView 预检 stale 响应守卫 + 双发收敛

**落点**：`cp6.web/src/views/space/lifecycle/SpacePublishView.vue`
- `runPrecheck`：加模块级（script setup 作用域，同 CpListPage.load 范式）`let pcSeq = 0`；开头 `const id = ++pcSeq`，
  await 后 `if (id !== pcSeq) return` 才写 `precheck.value`（成功/catch 两路都守卫，finally 仅当 `id === pcSeq` 复位 loading）。
  → 快速切楼层/库区时，慢的旧响应后到不再覆盖新楼层的四卡与 canPublish 三门。
- 双发收敛：新增 `let suppressZoneWatch = false`。floor watcher 重置 `selZoneId=undefined` 前，若确有变更则置
  `suppressZoneWatch=true`（保证 zone watcher 必被触发以复位标志）；zone watcher 开头见标志即复位并 return。
  → 切楼层不再「floor watcher 直跑 + zone 连锁再跑」同参双发；zone 已 undefined（无变更）时 floor watcher 仍独家跑一次，不漏。
- 触发链注释已写入两个 watcher 上方。
- 新测试「乱序预检：慢的旧请求不得覆盖新库区结果」：deferred promise 乱序（楼层级慢 pending / 库区级快先落），
  断言 `precheck` 最终值为后发（库区级 dirtyPrecheck，emptyCodeCount=3）而非过期的 99。照 CpListPage.spec 先例。

### #2（顺手一行）— adopt 成功后重跑预检

**落点**：同文件 `onAdopt` 成功分支，`adoptResult.value = res.data` 后补 `await runPrecheck()`
（采纳新增已发布库位使预检过期；runPrecheck 内部已守卫无楼层场景）。

### #3（顺手清理）— SegmentsEditor 死代码

**落点**：`cp6.web/src/views/space/lifecycle/SegmentsEditor.vue`
- 删 `defineExpose({ seqFieldsEnabled, upperEnabled, fixedValueEnabled })`。grep 全仓 → 三函数仅
  SegmentsEditor.vue（模板消费）与 codeRuleValidate.ts（定义）出现，无测试/其他消费者经 wrapper.vm 引用（测试直接 import 纯函数）→ 零引用，安全删。三函数仍被本组件模板 `:disabled` 消费，import 不悬空。
