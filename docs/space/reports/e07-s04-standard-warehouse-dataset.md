# E07-S04 标准 10,000 库位仓数据集交付报告

- 状态：完成，待合入 Space 集成分支
- 工作分支：`codex/space-e07-s04-standard-dataset`
- 数据集版本：`1.0.0`
- 生成器版本：`space-standard-warehouse-generator-v1`
- 固定随机种子：`cp6-space-standard-warehouse-seed-v1`
- 内容哈希：`d1891a4b334b70aae3d0926896127f6e73add4e4b7bc9bd7001a861302241e53`
- Manifest 哈希：`5fbed5b0777d645e00e3608a5a832874f540162f2e6c3be3a046e615920bf6a1`

## 1. 冻结协议闭环

本卡按 `docs/space/acceptance/01-golden-dataset-protocol.md` 第 9 节实现标准仓：

| 冻结要求 | 实现结果 |
|---|---:|
| Floor | 2 |
| Zone | 7 |
| Aisle | 20 |
| Rack | 500 |
| 每货架库位 | 20 |
| Location | 精确 10,000 |
| SKU | 100 |
| 库存记录 | 5,000 |
| 拣货任务 | 100，合计 200 行 |
| 批次、容器、货主 | 每条库存记录均具备 |
| 2D / 3D / WMS 共模 | 同一应用数据集生成 DXF、期望答案、底图和 WMS seed |
| 固定生成器与种子 | 写入 Manifest，不使用未固定随机数 |

数据集固定包含 25 个跨楼层任务、25 个跨 Zone 任务和 50 个 Zone
内任务。所有 `LogicalId` 都由固定种子与业务键经 SHA-256 推导，重新生成
不会依赖机器时间、进程随机源或数据库自增值。

## 2. 交付资产

验收包位于
`CP6.Tests/TestData/Space/Acceptance/v1.0.0`，共 17 个文件、约 10.1 MiB：

- `manifest.json`：数据集身份、计数、生成器、种子、内容哈希、逐文件
  SHA-256 和 readiness；
- `warehouse-standard.dxf`：509 个闭合轮廓、20 条巷道线和 10,000 个库位点；
- `expected-elements.jsonl`：10,529 个层级与几何期望元素；
- `expected-locations.csv`：10,000 个唯一 LogicalId 和库位编码；
- `wms-seed.json`：SKU、库存、批次、容器、货主与拣货任务；
- `floor-1.png`、`floor-2.png`、`floor-maps.pdf`：由同一模型生成的底图；
- `metadata.json`、`LICENSE.md`、`README.md`；
- 6 个固定故障样本：未知图层、重复编码、越界坐标、必填列缺失、
  损坏 CAD 和 WMS 超时。

生成器只接受新的或空的输出目录，防止旧文件被静默写入 Manifest。
两次独立生成均得到 17 个文件，逐文件路径、长度和 SHA-256 差异为 0。

## 3. WMS 模拟器装载

`ISpaceStandardWarehouseDatasetLoader` 把 10,000 个库位按模拟器声明的
1,000 条批次上限装载为 10 个原子批次，然后注入库存和任务：

- 上下文仓库编码必须与标准数据集一致，否则以
  `SPACE_STANDARD_DATASET_WAREHOUSE_MISMATCH` 失败关闭；
- 任一能力、预检、批次、取消或 seed 步骤失败时重置该租户、站点、仓库
  隔离域，不保留半装载状态；
- 重复装载重建相同库位目录和状态；
- 查询结果持续标记为 `STANDARD_WMS_SIMULATOR/Simulated`，不得冒充真实 WMS。

生产 `ISpaceWmsAdapter` 仍解析为 CP6 真实适配器；本卡没有改变 E07-S05
采用与切换决策。

## 4. 边界与外部阻塞

第 9 节对 E07-S04 的硬要求是 `expected-locations.csv`、`wms-seed.json`、
生成器版本、固定种子和标准仓计数，不要求 XLSX。因此本卡没有引入候选
分支中的可选工作簿，也没有绕过已批准的工作簿运行时约束。

`warehouse-standard.dwg` 仍受 E02-S01 许可转换器决策阻塞。验收包只提供
真实生成的 DXF，并在 Manifest 中以
`BlockedByE02S01LicensedConverterDecision` 和
`missingRequiredArtifacts` 明示 DWG 缺口，不伪造 DWG。该缺口影响正式
黄金资产和 E02 签收，不改变 DevelopmentSeed 类 E07-S04 标准仓的完成状态。

## 5. 验证证据

| 检查 | 结果 |
|---|---:|
| 生成器 Release build | 通过，0 warning / 0 error |
| 全解决方案 Release build | 通过，0 warning / 0 error |
| Space UnitTests | 79 passed |
| Space IntegrationTests | 40 passed / 30 SQL-gated skipped |
| 标准仓验收包专项 | 6 passed |
| CP6.Tests 全量 | 2,680 passed / 17 environment-gated skipped |
| CP6.Client.Tests | 71 passed |
| 两次独立生成 | 17 vs 17 files，0 differences |
| 本卡 C# 精确格式检查 | 通过 |

30 个 Space SQL Server 测试因当前机器没有可认证的隔离 SQL 环境而跳过，
不记作通过；E07-S04 的生成器、内存模拟器装载、清单和资产验证均不依赖
这些跳过项。
