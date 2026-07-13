# Task 6 报告：库位删除时清理 T_WmsBin 墓碑锚

**Status:** COMPLETE — 1824 passed / 5 skipped（基线 1819 + 5 新测），commit `6a32708`（代码+测试）+ 注释收尾 commit。

## 根因与修法

停用位（Status=2）删除此前只删 `Space_Location` 不碰 `T_WmsBin`。停用留下的墓碑 bin
（`IsActive=false`，主键 `Id` = 同 `LocationId`）成孤儿，其 `(WarehouseCd, LocationCode)`
仍占 join 锚。同码新库位再发布时被 `WmsBinConsumer` 的锚碰撞检查（唯一索引→业务拒绝链）判 **REJECTED**。

新增单源 helper（`SpaceMasterService.cs`）：

```csharp
internal static async Task RemoveTombstoneBinsAsync(CP6Context db, List<Guid> locationIds)
// _db.WmsBins.Where(b => locationIds.Contains(b.Id) && !b.IsActive) → RemoveRange
// 不自带 SaveChanges——由调用方与库位删除同一 SaveChanges/同事务提交。
```

- **仅删 `IsActive=false` 墓碑行**；活跃 bin 由 `!IsActive` 过滤天然排除（护栏兜底：活跃 bin 理论
  不可达，因 Status=1 不可删 E-SPACE-408），绝不误清活跃库位目录。
- **签名与 brief 的偏差（已选并说明）**：brief 建议 `private async Task RemoveTombstoneBinsAsync(List<Guid>)`。
  实际改为 `internal static`（入参加 `CP6Context db`），使 `SpaceMasterService` 与 `SceneService`
  两类**共用同一份实现**（brief 允许的「提取 internal static 帮助」选项），避免同构块漂移。SceneService
  以 `SpaceMasterService.RemoveTombstoneBinsAsync(_db, ...)` 调用，注释互指。

## 全仓删除通道清单及接线方式

以 `grep 'Space_Locations\.(Remove|RemoveRange)'` 为真相源，共 4 处库位删除点，全部接线：

| # | 文件:位置 | 通道 | 接线方式 |
|---|-----------|------|----------|
| 1 | `SpaceMasterService.cs` DeleteRackAsync（默认/deactivate 后级联删） | 删 rack 下全部库位 | 删 `children` 后即 `await RemoveTombstoneBinsAsync(_db, children.Select(l=>l.Id).ToList())`，随后同一 `SaveChanges` 提交 |
| 2 | `SceneService.cs` H2 缩格幽灵位清理（`outOfBounds`） | 越界草稿/停用位连带删 | 删除时 ID 入 `deletedLocIds` |
| 3 | `SceneService.cs` Deletes.Locations | 单库位删除（Status=0/2） | 删除时 `e.Id` 入 `deletedLocIds` |
| 4 | `SceneService.cs` Deletes.Racks | 删架级联删库位（`children`） | 删除时 children ID 入 `deletedLocIds` |

SceneService 三通道（2/3/4）累积到一个 `HashSet<Guid> deletedLocIds`，在 `SaveSceneAsync` 唯一
`SaveChanges` **之前**一次性 `await SpaceMasterService.RemoveTombstoneBinsAsync(_db, deletedLocIds.ToList())`
——与库位删除同事务、同 SaveChanges。

**未接线（经核实无需）：`DeleteAisleAsync`** ——巷道删除走 `rack.AisleId = null`（SetNull）语义，
不删任何 `Space_Location`（deactivate 模式亦仅 Status→2 后 SetNull 保留库位），故不产生孤儿 bin。
grep 亦证实其内无 `Space_Locations.Remove`。brief 对它的行号引用系沿用 plan 泛述，实测非库位删除通道。

## 注释收口

- `SpaceMasterService.DeleteRackAsync`：「其码仍占 T_WmsBin 锚……锚清理记后续票」→「波5 已清理：删库位时一并清其 T_WmsBin 墓碑锚（同一 SaveChanges/同事务）」。
- `SceneService` Deletes.Locations：「锚清理机制记后续票，此为拍板时已知代价」→「波5 已清理：……本事务末尾一并清除，同码新库位发布不再被锚碰撞 REJECTED」。

## 消费端不受影响

波3 已有的消费端 DEACTIVATE 墓碑分支（`WmsBinConsumer` H6 乱序防护 / `WmsBinDeactivator` H6 墓碑）
未改动——本任务只管 **Space 侧删除时**的 bin 清理，消费端墓碑落库/幂等语义原样保留。

## 测试（TDD）

新增 5 项（全绿）：
- `DeleteRack_DisabledLocationWithTombstoneBin_RemovesBin` — ① 删 Status=2 带墓碑 bin → bin 消失。
- `DeleteRack_DraftLocationNoBin_DoesNotThrow` — ② 删 Status=0 无 bin → 不炸。
- `DeleteRack_ActiveBin_NotRemoved_Guardrail` — 护栏：活跃 bin 命中不删。
- `DeleteRack_ThenRepublishSameCode_BinRebuiltActive_NotRejected` — 集成（Step 3）：删→同码再建→真 `WmsBinConsumer` publish→UPSERTED（非 REJECTED），bin 重建 `IsActive=true`。
- `SaveScene_DeleteDisabledLocation_RemovesTombstoneBin` — SceneService 删除通道断言。
- 更新既有 `DeleteRack_ModeDeactivate_DeactivatesThenCascades`：墓碑 bin 现随删除清除（`WmsBins.Count==0`），取代旧「bin 独立留存」断言（预期行为变更）。

全量：`dotnet test CP6.Tests/CP6.Tests.csproj` → **1824 passed / 5 skipped**（基线 1819+5）。

---

## 修复节：波5 终审 Important #1（消费端 bin==null 复活守卫）

**问题**：T6 在库位删除时清 `T_WmsBin` 墓碑并硬删 `Space_Location` 行，但没清在途/Failed 的
SPACE→WMS 集成事件。窗口——某库位事件 Failed（`SpaceBridgeHook` 同步消费吞异常落 Failed，
`IntegrationEventRetryWorker` 重试）→期间用户删该停用位（墓碑被 T6 清）→重试到达 `WmsBinConsumer`：
① `bin==null` 的 DEACTIVATE 分支重建墓碑=孤儿锚回归；② 更糟，`bin==null` 的 UPSERT 分支给已删除
库位重建 `IsActive=true` 幻影 bin（复活已删库位）。

**处方（终审方案 a）**：`WmsBinConsumer.ConsumeAsync` 循环前一次性预载「本批中 `binsById` 无命中的
LocationId」的存在性——`Space_Locations.Where(missingIds.Contains).Select(Id)` 成 `HashSet`
（不在循环里逐条 `AnyAsync`，保 T3 批量化纪律）。`bin==null` 两分支（DEACTIVATE 重建墓碑 / UPSERT
重建活跃 bin）先查该集合；库位已删（不在集合）→ 结果记 `SKIPPED`，Reason=「库位已删除，拒绝复活锚
（波5 终审守卫）」。存在性快照循环前取即可（删除不走消费端，批内无 T6 场景）；同批前面 item 新插的
bin 走 `binsById` 命中不进 `bin==null` 分支，守卫不影响批内自碰撞语义。

顺手（终审 Minor #7）：`CP6.Tests/Infra/SqlServerFactAttribute.cs` XML 注释示例 host
`localhost`→`127.0.0.1`（一行）。

**测试**：
- 新增 `WmsBinConsumerTests.Consume_LocationDeleted_NoBin_RefusesToReviveAnchor`——库位无行 + 无 bin，
  一批含 DEACTIVATE + UPSERT 各一 item → 两者 SKIPPED、`WmsBins` 零新行、Success/AllSkipped=true。
- 新增对照 `WmsBinConsumerTests.Consume_LocationExists_NoBin_OriginalSemanticsPreserved`——库位存在 +
  无 bin → 守卫放行，DEACTIVATE 落墓碑（IsActive=false）/ UPSERT 建活跃 bin，原语义不变。
- 既有用例数据补种（断言零改动）：新守卫要求「库位存在」语义的用例补 `Space_Location` 行——
  `WmsBinConsumerTests` 内补种 `SeedLoc` 辅助并接入：`Upsert_NewLocation_CreatesBin`、
  `Upsert_StaleVersion_Skipped_NoWrite`、`Upsert_NewerVersion_UpdatesBin`、
  `Upsert_AnchorCollision_DifferentLocationId_Rejected`（idA+idB 均种，否则 idB 走守卫 SKIPPED 而非锚碰撞
  REJECTED）、`Upsert_SameBatch_DuplicateLocationId_NoDoubleAdd`、`Deactivate_NoBin_CreatesTombstone`、
  `Deactivate_NoBin_NoWarehouseCd_Skipped`、`Deactivate_Tombstone_ThenLateUpsert_Skipped`、
  `Deactivate_WithStock_Rejected`、`MixedBatch_TwoUpsertOneDeactivate_EquivalentToPerItem`（Seed 内种
  idA/idB/idC）、`Deactivate_NoStock_SetsInactive_AndVersion`；`SpaceMasterServiceTests`
  `DeleteRack_ThenRepublishSameCode_BinRebuiltActive_NotRejected` 补种新库位 `newLocId` 的
  `Space_Location`（真实再发布流程会先建新库位行）。
- `Upsert_MissingWarehouseCd_Rejected` 无需补种——UPSERT 缺 WarehouseCd 在 `bin==null` 守卫前即 REJECTED。

**覆盖测试命令与输出**：
```
dotnet test CP6.Tests/CP6.Tests.csproj
Passed!  - Failed: 0, Passed: 1826, Skipped: 5, Total: 1831
```
（基线 1824→1826，新增两用例使上升；skipped 5 不变。）
