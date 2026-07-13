### Task 3: WmsBinConsumer 批量化(每 item 3 查→批次 3 查)

**Files:**
- Modify: `CP6.Core\Services\Wms\WmsBinConsumer.cs`(`ConsumeAsync:27-118`)
- Test: 既有 WmsBinConsumer 测试全绿 + 新增混合批(UPSERT×2+DEACTIVATE×1)等价测试

**Interfaces:**
- Consumes: `LocationPublishBatch.Items`(不变)。
- 内部:循环前三次预载——①`var ids = batch.Items.Select(i => i.LocationId)…; var bins = await _db.WmsBins.Where(b => ids.Contains(b.Id)).ToDictionaryAsync(b => b.Id)`;②DEACTIVATE 项的库存合计一次 `GroupBy` 查询(照 `:69-71` 现有谓词,聚合成 `Dictionary<key, decimal>`);③anchor 查询(`:97-100` 谓词)按批次码集合一次载入。循环体改字典命中。

**要点:** 三分支语义(upsert/墓碑/锚碰撞 REJECTED)与幂等 Version 判据逐行为等价;DeadLetter 路径不动。

- [ ] **Step 1: 写混合批等价测试**(2 upsert+1 deactivate,断言 bin 状态/Version/REJECTED 结果与逐条跑一致)
- [ ] **Step 2: 基线绿确认**
- [ ] **Step 3: 重构为三次预载+内存判定**
- [ ] **Step 4: 全量后端绿**
- [ ] **Step 5: Commit + push**(`perf(space): 波5 WMS消费端批量化——每item 3查收敛为批次3查`)

---

