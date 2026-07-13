### Task 6: 库位删除时清理 T_WmsBin 墓碑锚

**Files:**
- Modify: `CP6.Core\Services\Space\SpaceMasterService.cs`(`DeleteRackAsync:415-501`、`DeleteAisleAsync:260-334` 中删 `Space_Locations` 处;另 grep 全仓其余 `Space_Locations.Remove` 删除通道——含 SceneService 保存删除分支,一并接线)
- Test: `CP6.Tests\Space\`

**Interfaces:**
- Produces: `SpaceMasterService` 内 `private async Task RemoveTombstoneBinsAsync(List<Guid> locationIds)`:`_db.WmsBins.Where(b => locationIds.Contains(b.Id) && !b.IsActive)` → `RemoveRange`。**仅删 IsActive=false 的墓碑行**(活跃 bin 理论不可达——Status=1 不可删 E-SPACE-408;护栏兜底:命中活跃 bin 时不删并保留)。SceneService 若在别类,提取为 `internal static` 帮助或复制同构块并注释互指。

**要点:** 与库位删除**同一事务/同一 SaveChanges**;删除后同码再发布不再 REJECTED(锚释放)。顺带更新 `:495-499` 注释(「锚清理记后续票」→「波5 已清理」)。

- [ ] **Step 1: 失败测试×2**(①删 Status=2 库位(带 IsActive=false bin)→bin 行消失 ②删 Status=0 库位(无 bin)→不炸)
- [ ] **Step 2: 红 → 实现(所有删除通道接线)→ 绿 → 全量绿**
- [ ] **Step 3: 追加集成断言**:同码重发布不再碰撞(构造 删→再建同码→publish→bin 重建 IsActive=true)
- [ ] **Step 4: Commit + push**(`feat(space): 波5 库位删除清理T_WmsBin墓碑锚——同码再发布不再REJECTED`)

---

