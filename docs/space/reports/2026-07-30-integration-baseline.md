# Space V1 受控集成基线报告

- 日期：2026-07-30
- 集成分支：`integration/space-v1-20260730`
- 基线父提交：`dcc1ac9a`
- 初始集成提交：`539d56de`
- 当前代码集成提交：`6e67a9d1`
- 候选检查点：`checkpoint/space-candidate-20260730` / `0d25da4d`

## 1. 本轮结论

E00 S01–S04、E01 S01–S06 与 E07 S01–S03 已进入唯一集成基线。E02 S01 的中立实验门禁也已进入基线，但最终技术选型仍受外部数据、授权和环境阻塞，不计作完整签收。各切片均由候选重建并经过独立审查，没有整包合入候选。

## 2. 已集成范围

| 范围 | 结果 |
|---|---|
| E00 S01 | 当前事实清单与可重复盘点工具 |
| E00 S02 | Legacy / Design V1 兼容护栏 |
| E00 S03 | Space 数据源契约 |
| E00 S04 | 审计、可观测性、重试与死信收口 |
| E01 S01 | Model / Version 持久化基线 |
| E01 S02 | Source / File 血缘与上传生命周期 |
| E01 S03 | Job / Step / Attempt / Artifact Ledger |
| E01 S04 | Published→Draft Clone、八类快照、幂等预留、租约围栏与失败清理 |
| E01 S05 | Design API v1、Problem Details、RBAC/cutover/cursor/幂等边界、OpenAPI 与生成 SDK |
| E01 S06 | 失败关闭文件扫描、隔离 Worker 契约、扫描 Job 原子终态、引用感知保留清理与对象删除补偿 |
| E02 S01（Partial） | 中立数据审计、压力生成、适配器运行证据、ODA/APS preflight、隔离供应商淘汰复现；不含生产 CAD 适配器 |
| E07 S01–S03 | WMS 能力合同、CP6 真实适配器、持久化幂等账本、标准模拟器、库存/任务查询与故障注入 |

## 3. 候选保全边界

`0d25da4d` 保全了 E01 S06、E02 S01 与 E05–E12 等候选来源；其中 S06、E02 S01 的中立实验部分和 E07 S01–S03 已按独立边界重建并集成，E02 的生产转换能力与其余候选仍不是正式实现。检查点包含 542 个文件、约 49 万行新增，已检查凭据、私钥、异常大文件和常见构建产物，未发现真实敏感信息。

该检查点的用途是防止工作丢失和提供提取来源，不是可直接合并的交付单元。共享 Domain、DbContext、Migration、API 和前端文件必须按子任务重新切片。

## 4. 合并决策

合并产生两个冲突：

1. `CP6.Core/EFDbContext/CP6Context.cs`
2. `CP6.slnx`

解决结果：

- `SaveChanges` / `SaveChangesAsync` 同时执行 Definition 不可变、WMS 序列追踪不可降级和 Space 审计追加写保护。
- 解决方案保留既有 `CP6.Mobile`，并加入 Contracts、Domain、Application、Infrastructure、UnitTests、IntegrationTests 六个 Space 项目。

## 5. 集成态验证

| 检查 | 结果 |
|---|---|
| `dotnet build CP6.slnx -c Release` | E07 功能/合并等价树通过；0 errors，7 existing test warnings |
| Space UnitTests | 73 passed |
| Space IntegrationTests | 35 passed，30 SQL-gated skipped；本机强制补跑在业务断言前被 TLS/SSPI/Guest 执行身份认证阻断 |
| EF Migration 一致性 | `has-pending-model-changes` 通过，无待迁移模型变更 |
| CP6.Tests | 2674 passed，17 environment-gated skipped |
| CP6.Client.Tests | 71 passed |
| SDK 生成闭环 | drift check、C# build、TypeScript strict compile 通过 |
| S06 范围与格式门禁 | 触及文件格式、提交差异和后续能力污染扫描通过 |
| E02 中立实验工具 | 10 passed；相对适配器参数工作目录回归通过 |
| E02 数据 readiness | 5 个冻结 Seed 完整性通过，50MiB 与 100 万实体压力生成通过；因缺正式 20 文件集、DWG/DXF 矩阵按预期退出 `3` |
| E02 供应商 preflight | ODA/APS 模板均因缺授权包/受控凭据/冻结环境按预期退出 `4`；未读取或序列化 secret 值 |
| Aspose 隔离淘汰复现 | 适配器 build 0 warning / 0 error；25 次中 L5 5/5 崩溃，20 个成功观察均只保留图层 `0` |
| E07 精确格式与范围门禁 | 新增 C# 文件 whitespace verify 通过；未混入 S04/S05、E08、E13、Workload 或发布 Saga |
| Frontend type-check | 通过 |
| Frontend unit tests | 86 files，539 tests passed |
| Frontend production build | 通过；保留既有大 chunk 提示 |

跳过项是环境门禁，不视为失败，也不记作已通过。2026-07-30 已尝试以项目现有 Windows 集成认证连接本机 SQL Server；运行在建库和业务断言前被加密协商、SSPI 及自动化执行身份认证阻断。没有测试宿主进程残留。获得可认证的隔离 SQL 测试连接后，仍需补跑 30 个 Space SQL 集成测试，其中 5 个是 S06 新增测试，1 个是 E07 新增的迁移/事务合同测试。

S06 功能提交 `6daf1aeb` 与 no-ff 集成提交 `2ccdff7a` 交付独立 Migration `20260730152005_SpaceE01S06FileSafetyRetention`。E02 实验提交 `fe959066` 与 no-ff 集成提交 `3742fbff` 只增加 solution 外实验项目、文档和 CAD 文件字节稳定属性，不改生产 HTTP、数据库模型或前端产品代码；产品验证沿用 S06 基线。

E07 功能提交 `d06a8bd1` 与 no-ff 集成提交 `6e67a9d1` 交付独立 Migration `20260730161925_SpaceE07S02WmsAdapterLedger`。生产依赖注入仍默认选择 CP6 真实适配器；标准模拟器只能显式解析，不会替换生产数据源。

## 6. 下一批固定顺序

1. E02 S01：获得正式黄金集、DWG/DXF 矩阵、ODA/APS 授权材料和 8 vCPU / 32GiB 冻结 Worker 后，运行同环境试验并按 ADR-0001 评分签收。
2. E07 S04：构建可确定性重建的 500 货架、10,000 库位、库存、任务和异常标准数据包；S05 等待 E04 S04。
3. E13：按冻结批次完成 Provider 技术/授权证据。

每个子任务必须独立提取、审查、迁移验证、测试和提交。E05–E12 候选不得整包 merge 或 cherry-pick。
