# Task 3 报告：Space 波5 WmsBinConsumer 批量化

## Status
DONE。commit `c6d1b79`（分支 feat/space-wave5，已 push）。全量 1813 passed / 5 skipped（基线 1812 + 本任务新增 1 等价测试）。

## 实现
`CP6.Core/Services/Wms/WmsBinConsumer.cs::ConsumeAsync` 循环前三次预载，替代旧「每 item 三次逐条查询」：

1. **bins 预载（覆盖旧查询①按 Id + ③按锚两处 DbSet 命中面）**：一次
   `WmsBins.Where(idSet.Contains(Id) || (whSet.Contains(WarehouseCd) && codeSet.Contains(LocationCode))).LoadAsync()`，
   再从 `_db.WmsBins.Local` 建 `binsById` 与 `binsByAnchor` 双字典（Local 含刚载入行 + 预先已跟踪行，等价旧 Local-first 优先级）。
2. **库存合计预载（旧查询②）**：收集所有 DEACTIVATE 且 bin 已存在项的锚 `(bin.WarehouseCd, bin.LocationCode)`，一次
   `Stocks.Where(...).GroupBy(...).Sum(PhysicalQty)` 聚成 `stockByAnchor`；循环内 `GetValueOrDefault(anchor, 0m)`（空集=0 等价旧 `SumAsync`）。
3. 循环体三处逐条查询全部改为字典命中：`binsById.TryGetValue`（Id）/`stockByAnchor.GetValueOrDefault`（库存）/`binsByAnchor.TryGetValue`（锚碰撞）。

查询次数：旧 = O(3N)（N=批内 item 数）；新 = 恒定 2 次（bins 一次 Load + 库存一次 GroupBy，无 DEACTIVATE 时 1 次）。

三分支语义（upsert 幂等 Version 判据 / 无 bin 墓碑防乱序复活 / 锚碰撞 REJECTED）、DetachOwnWrites/DeadLetter 路径、结果对象构造全部逐行为不动。

## 批内自碰撞结论（brief 要求的可达性判定）
**旧实现存在「批内新插行影响后续判定」的可达代码路径**——旧两处查询均 `Local.FirstOrDefault(...) ?? DB.FirstOrDefaultAsync(...)`，Local-first 使同批前一 item 刚 Add 的 bin 被后一 item 看到：
- 同 LocationId 双条 → 第二条走版本门 SKIPPED（既有测试 `Upsert_SameBatch_DuplicateLocationId_NoDoubleAdd` 断言此）。
- 同锚不同 LocationId 双条 → 第二条 REJECTED（旧代码注释显式声称覆盖）。

**上游可达性**：生产调用方 `LocationPublishService.PublishFloorAsync` 先跑 `PrecheckAsync`，`DuplicateGroups.Count > 0` 直接抛 `E-SPACE-307`（重复码被闸死），且每条 item 源自不同 PK 的 `Space_Location` 行——故这两条批内自碰撞路径**生产调用方不可达**（RepublishAsync/DeactivateAsync 同理，均由 distinct PK 行构建）。

**但**消费端契约与既有测试要求保留该语义，故预载**不能**仅取批开始快照。处理：循环内每插入新 bin（UPSERT 新建 + DEACTIVATE 墓碑）**同步回填 binsById 与 binsByAnchor**，UPSERT 更新后按当前锚回填——使字典始终反映旧 Local 的实时可见性，判定完全等价。既有 12 用例（含 SameBatch/AnchorCollision）零改动全绿即为证。

## 改动文件
- `CP6.Core/Services/Wms/WmsBinConsumer.cs`（重构 + 注释固化可达性结论）
- `CP6.Tests/WmsBinConsumerTests.cs`（新增 `MixedBatch_TwoUpsertOneDeactivate_EquivalentToPerItem`：UPSERT×2+DEACTIVATE×1 含 REJECTED，双 DB 跑批量路径 vs 逐条独立消费，断言逐项 Status/Success 及最终 bin IsActive/Version 两路径逐一相等）

## 自审
- 三处查询谓词逐一对照旧代码：Id 精确、锚精确 `(WarehouseCd,LocationCode)` pair、库存 `WarehouseCd==bin.WarehouseCd && LocationCd==bin.LocationCode` 求和 → 均保留，预载 Where 用集合超集 + 字典精确键，聚合/命中值不受超集影响。
- DEACTIVATE 库存快照 vs 循环内：循环内从不写库存，快照与逐条查询同值；跨 item「UPSERT 建 bin→同批 DEACTIVATE 同 id」新建 bin 无对应 stock 行（stock 独立表未变），SumAsync=0 与 GetValueOrDefault=0 等价（且此路径生产亦不可达）。
- WmsBin.WarehouseCd/LocationCode 为非空 string，锚字典 key 安全；item.WarehouseCd 在锚检查处已过 null-guard，加 `!`。
- 无 DEACTIVATE 时跳过库存查询；空批 `idSet/whSet` 为空，`Where` 谓词返回空集，`Local` 为空，安全。

## 疑虑
- 理论边角（**非本任务回归、生产不可达**）：若同批内某 UPSERT 把已存在 bin 的 `LocationCode` 改成另一锚值，`binsByAnchor` 旧键会残留指向该 bin。但「发布后码冻结」（LocationCode 恒不变）是既有域不变式，无调用方触发码变更，既有测试亦不涉及；UPSERT 更新后已按当前锚回填新键，主路径无偏差。不视为缺口，仅记录。
