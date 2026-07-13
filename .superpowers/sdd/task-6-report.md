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
