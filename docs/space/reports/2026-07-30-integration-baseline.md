# Space V1 受控集成基线报告

- 日期：2026-07-30
- 集成分支：`integration/space-v1-20260730`
- 基线父提交：`dcc1ac9a`
- 初始集成提交：`539d56de`
- 当前代码集成提交：`dca6e19c`
- 候选检查点：`checkpoint/space-candidate-20260730` / `0d25da4d`

## 1. 本轮结论

E00 S01–S04、E01 S01–S06、E07 S01–S04 与 E13 S01–S03 已进入唯一集成基线。E02 S01 的中立实验门禁也已进入基线，但最终技术选型仍受外部数据、授权和环境阻塞，不计作完整签收。各切片均按冻结边界独立实现或由候选重建并经过审查，没有整包合入候选。

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
| E07 S04 | 确定性 500 货架/10,000 库位标准仓、WMS seed、DXF/底图/期望答案、加载器与 6 个固定故障样本 |
| E13 S01 | Provider/确定性端口、Schema v1 强类型契约、租户/Site/别名/数据策略/外部开关门禁、默认 Disabled 与配额失败关闭 |
| E13 S02 | Run/Proposal/Decision/Usage 租户化审计模型、状态机、复合外键、唯一约束、RowVersion 和独立 Migration |
| E13 S03 | Import 6 步/BuildScene 12 步显式处理器、类型过滤认领、租约心跳、取消、检查点复用、单调进度和失败关闭执行器端口 |

## 3. 候选保全边界

`0d25da4d` 保全了 E01 S06、E02 S01 与 E05–E12 等候选来源；其中 S06、E02 S01 的中立实验部分和 E07 S01–S04 已按独立边界重建并集成，E02 的生产转换能力与其余候选仍不是正式实现。检查点包含 542 个文件、约 49 万行新增，已检查凭据、私钥、异常大文件和常见构建产物，未发现真实敏感信息。

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
| `dotnet build CP6.slnx -c Release --no-restore` | E13 S03 合并代码通过；0 errors，7 existing warnings |
| Space UnitTests | 126 passed |
| Space IntegrationTests | 默认门禁 45 passed、33 SQL-gated skipped；本机 SQL 全量启用 71 首轮通过，7 个并行建库/删库超时项串行复跑通过 |
| EF Migration 一致性 | `has-pending-model-changes` 通过，无待迁移模型变更 |
| CP6.Tests | 2680 passed，17 environment-gated skipped |
| CP6.Client.Tests | 71 passed |
| SDK 生成闭环 | drift check、C# build、TypeScript strict compile 通过 |
| S06 范围与格式门禁 | 触及文件格式、提交差异和后续能力污染扫描通过 |
| E02 中立实验工具 | 10 passed；相对适配器参数工作目录回归通过 |
| E02 数据 readiness | 5 个冻结 Seed 完整性通过，50MiB 与 100 万实体压力生成通过；因缺正式 20 文件集、DWG/DXF 矩阵按预期退出 `3` |
| E02 供应商 preflight | ODA/APS 模板均因缺授权包/受控凭据/冻结环境按预期退出 `4`；未读取或序列化 secret 值 |
| Aspose 隔离淘汰复现 | 适配器 build 0 warning / 0 error；25 次中 L5 5/5 崩溃，20 个成功观察均只保留图层 `0` |
| E07 S04 数据与范围门禁 | 两次独立生成 17 个文件差异为 0；干净检出 Manifest 哈希错误为 0；新增 C# 精确格式通过；未混入 S05、E08、E13、Workload 或发布 Saga |
| E13 S01 Provider 门禁 | 三类 Provider 共用同一 SPI；默认租户 Disabled、注册表为空、配额失败关闭；敏感标识不进入 Provider 输入；Provider 契约 18 passed，权限聚焦 17 passed |
| E13 S02 数据模型门禁 | 四表租户过滤/复合外键/RowVersion/追加审计通过；新 SQL 测试真实落库并验证 Current Run 与 Provider 请求去重；E13-S02 新增 16 Unit、4 Integration（含 1 SQL） |
| E13 S03 Worker 处理器门禁 | Import 6 步和 BuildScene 12 步目录固定；类型过滤、复用、取消、租约丢失、宿主停机、硬超时和安全失败分类通过；新增 13 Unit，SQL 聚焦 3/3 passed |
| Frontend type-check | 通过 |
| Frontend unit tests | 86 files，539 tests passed |
| Frontend production build | 通过；保留既有大 chunk 提示 |

默认跳过项是环境门禁，不视为失败，也不记作已通过。2026-07-30 已在提权的本地测试宿主中使用 Windows 集成认证连接 `KOUSQLSERVER`。E13-S03 后 78 个 Space Integration 测试全部实际启动：71 个首轮通过；7 个测试在并行创建、握手或删除独立数据库的压力下超时，随后逐项串行复跑全部通过。所有临时数据库均由测试清理，E13-S03 聚焦 SQL 测试最终 3/3 无跳过通过。

S06 功能提交 `6daf1aeb` 与 no-ff 集成提交 `2ccdff7a` 交付独立 Migration `20260730152005_SpaceE01S06FileSafetyRetention`。E02 实验提交 `fe959066` 与 no-ff 集成提交 `3742fbff` 只增加 solution 外实验项目、文档和 CAD 文件字节稳定属性，不改生产 HTTP、数据库模型或前端产品代码；产品验证沿用 S06 基线。

E07 功能提交 `d06a8bd1` 与 no-ff 集成提交 `6e67a9d1` 交付独立 Migration `20260730161925_SpaceE07S02WmsAdapterLedger`。生产依赖注入仍默认选择 CP6 真实适配器；标准模拟器只能显式解析，不会替换生产数据源。

E07 S04 功能提交 `74577015` 与 no-ff 集成提交 `6d751e0c` 交付数据集版本 `1.0.0`、固定生成器/种子和 10,000 库位验收包。可选 XLSX 不属于第 9 节硬门槛；DWG 缺口继续由 E02-S01 许可转换器决策阻塞并在 Manifest 中明示，不伪造资产。

E13 S01 功能提交 `8f7fc25e` 与 no-ff 集成提交 `ea161975` 交付 Provider/确定性端口、租户策略和失败关闭默认值。权限种子新增 `space:model:generate-ai` 与 `space:model:review-ai`，但权限不会启用 AI；未显式替换租户策略、Provider 注册和原子配额租约前不会发生 Provider 调用。本卡无 Migration、HTTP 或外部网络调用。

E13 S02 功能提交 `cff25a25` 与 no-ff 集成提交 `94822669` 交付 `Space_GenerationRun`、`Space_GenerationProposal`、`Space_ProposalDecision` 和 `Space_AiUsageRecord`，以及 Migration `20260730174231_SpaceE13S02GenerationDataModel`。本卡只建立可审计持久化边界，不包含 ProviderConfig、Worker、CAD IR、输出校验、融合、Apply、预算或 HTTP。

E13 S03 功能提交 `cebd401a` 与 no-ff 集成提交 `dca6e19c` 交付 Import/BuildScene 可恢复处理器控制面。实际 CAD、规则、Provider、校验、融合、几何、Proposal/Issue 和 Usage 步骤仍由后续端口实现；默认执行器稳定返回 `SPACE_JOB_PROCESSOR_UNAVAILABLE`。本卡无 Migration、HTTP、外部网络调用或跨租户宿主循环。

## 6. 下一批固定顺序

1. E02 S01：获得正式黄金集、DWG/DXF 矩阵、ODA/APS 授权材料和 8 vCPU / 32GiB 冻结 Worker 后，运行同环境试验并按 ADR-0001 评分签收。
2. E07 S05：等待 E04 S04，不提前采用或切换。
3. E13 S12：依赖 E13 S01、S03 已满足；交付数据库保护的单租户三并发、日/月预算预留、用量和费用审计，不提前实现 HTTP 或 Provider。
4. E13 S04/S05：等待 E02 S03、CAD IR 最小化和正式供应商证据。

每个子任务必须独立提取、审查、迁移验证、测试和提交。E05–E12 候选不得整包 merge 或 cherry-pick。
