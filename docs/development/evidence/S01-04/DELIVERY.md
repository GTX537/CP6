# S01-04 库存数量、金额与比例交付

- 执行对话 B；仓库 `GTX537/CP6`；分支 `codex/s01-04-inventory-values-20261012`。
- 实际工作区 `D:/CP6-worktrees/s01-04-20261012`；实现及验证源码基线 `8569b669fed56ddc7b53b7bb24666c7fb93d544a`，开工时已 fetch 并核对 origin/main。
- 准入为 READY/CONFIRMED，依赖 S01-02 已 DONE。创建时基线为 `d7a210256783e45291a5e724ea068ed4fc165ecc`；当前差异只含其后的协调资料。S01-05 已按 PR185 的回执闭环，未复用其旧编辑窗口。
- START：Handoff `d574f0f874b152544d83b6db3fa87ae147e8ca17` / `handoffs/CP6-DEV-20261012/tasks/S01-04/messages/20261013T002324Z-start/MESSAGE.md`；固定 SHA 远端字节回读相同。

## 实现

`InventoryDecimal` 分开采用原生 Decimal18、Qty12 和独立的 NonNegativeQty12 scalar；整数位无新增上限，原始词法与归一化 `BigInteger + scale` 分离。负零、尾零原样保存，原始精度验证先于数值归一化。字段专属 JSON converter 要求 string，输出仍校验目标域。

不可变 `InventoryDecimalValue` 提供精确加减、乘法和比较，中间值不舍入或截断；最终输出经过目标 profile。`InventoryRatio` 提供任意精度有符号分子/正分母、gcd 约分及精确除法余数。除法向零截断，余数与分子同号或零；余数以约分后的分母为单位，恒等式有自动化断言。

本次只提供内部比例算术，完整 Rational/basisRef、MAVG 字符串和状态对象 codec 明确留待后续。原生 Rational 的正分子要求与 MAVG 的 `00` / `-0` 合法词法、null/ACTIVE/EMPTY 约束均在使用说明中保留，没有据内部规范化修改它们。

所有代码在 `CP6.Entity/Contracts/FinInventory/`；测试仅新增指定 `InventoryDecimalTests.cs`。没有修改共享 Wire、两个 csproj、包/锁、共用输入、其他测试、总台账/状态页、现有 StockFinBridge、数据库或前端。使用说明见 [README](../../../../CP6.Entity/Contracts/FinInventory/README.md)。

## 验证

执行命令（RED/GREEN 各附独立 TRX 输出参数）：

```powershell
dotnet test eng/contracts/exact-values-tests/CP6.ExactValues.Tests.csproj --filter FullyQualifiedName~InventoryDecimalTests
```

| 执行 | 结果 | 证据 |
| --- | --- | --- |
| RED | 正常编译并发现 132 项，129 失败 / 3 通过 / 0 跳过，退出 1；失败来自显式未实现桩 | [日志](red.log)、[TRX](red/inventory-red.trx) |
| GREEN | 132 通过 / 0 失败 / 0 跳过，退出 0 | [日志](green.log)、[TRX](green/inventory-green.trx) |

SDK 10.0.302；目标 net8.0；TRX 中实测运行时 .NET 8.0.29。首次命令恢复现有依赖，后续报告 all projects up-to-date，未修改包或锁。没有保存 RED 构建产物或桩源码快照；RED 绑定以实际原始日志/TRX 及不变测试文件为限，不把 GREEN 产物哈希追溯到 RED。

固定包的 9 项全部真实执行，包含接受/拒绝及数值系数、scale、originalWire；132 项中其余 123 项覆盖 >90/200 位整数、18/12 边界、错误 token、目标域误用、原始 UTF8、严格载体及比例约分/符号/零/余数。实际 ID 和全部 test name/outcome、输入/配置/依赖/源码/构建产物哈希见 [verification.json](verification.json)。复用 S01-02 旧 77/77 仅说明前置来源，本任务新增载体断言已实际执行。

一次完整任务差异审查见 [REVIEW.md](REVIEW.md)。成功验证后仅追加说明和证据，未修改被验证源码或测试；COORD 后续如仅合并或追加共享状态，按身份核对复用该结果，不把后续 SHA 说成新执行。

## 未覆盖与集成

本地合同单测不证明真实成本 Owner、库存计价、完整 Rational/MAVG 对象、C#/TS/PG 联合比对、持久化、权限/事务、旧账迁移或生产部署。共用输入包、247 完整业务场景及历史缺口保持各自原状态；不把局部结果扩为业务 PASS。

交付由 COORD 串行同步 roadmap/生成视图/四状态页，正常保护合并并核对远端 main 包含关系后才可 DONE。本报告不代替主线合并回执。

Actions 没有任务归属的排队/执行运行需要取消；没有手动触发、重跑或修改触发配置。推送前核对实际候选和受保护主线的工作流事件；不创建发布 tag，不执行数据库、迁移、包发布或部署。
