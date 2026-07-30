# Space V1 受控集成基线报告

- 日期：2026-07-30
- 集成分支：`integration/space-v1-20260730`
- 基线父提交：`dcc1ac9a`
- 初始集成提交：`539d56de`
- 当前集成提交：`2ccdff7a`
- 候选检查点：`checkpoint/space-candidate-20260730` / `0d25da4d`

## 1. 本轮结论

E00 S01–S04 与 E01 S01–S06 已进入唯一集成基线。E01 S04、S05、S06 均由候选重建为经过审查的最小切片，并未整包合入候选。其余候选实现已被安全保全，但不计入正式完成度。

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

## 3. 候选保全边界

`0d25da4d` 保全了 E01 S06、E02 S01 与 E05–E12 等候选来源；其中 S06 已按独立边界重建并集成，剩余候选仍不是正式实现。检查点包含 542 个文件、约 49 万行新增，已检查凭据、私钥、异常大文件和常见构建产物，未发现真实敏感信息。

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
| `dotnet build CP6.slnx -c Release` | S06 合并态通过；0 errors，10 existing warnings |
| Space UnitTests | 合并态 52 passed |
| Space IntegrationTests | 合并态 17 passed，29 SQL-gated skipped；本机强制补跑在业务断言前被 TLS/SSPI/Guest 执行身份认证阻断 |
| EF Migration 一致性 | `has-pending-model-changes` 通过，无待迁移模型变更 |
| CP6.Tests | S06 功能态全量 2674 passed，17 environment-gated skipped |
| SDK 生成闭环 | drift check、C# build、TypeScript strict compile 通过 |
| S06 范围与格式门禁 | 触及文件格式、提交差异和后续能力污染扫描通过 |
| Frontend type-check | 通过 |
| Frontend unit tests | 86 files，539 tests passed |
| Frontend production build | 通过；保留既有大 chunk 提示 |

跳过项是环境门禁，不视为失败，也不记作已通过。2026-07-30 已尝试以项目现有 Windows 集成认证连接本机 SQL Server；运行在建库和业务断言前被加密协商、SSPI 及自动化执行身份认证阻断。没有测试宿主进程残留。获得可认证的隔离 SQL 测试连接后，仍需补跑 29 个 Space SQL 集成测试，其中 5 个是 S06 新增的真实 SQL Server 测试。

S06 功能提交 `6daf1aeb` 与 no-ff 集成提交 `2ccdff7a` 交付独立 Migration `20260730152005_SpaceE01S06FileSafetyRetention`。S06 未新增 HTTP 路由，也未改前端产品代码；前端沿用本报告初始集成态的验证结果。

## 6. 下一批固定顺序

1. E02 S01：CAD 选择试验。
2. E07：WMS 契约、CP6 适配器和标准模拟器。
3. E13：按冻结批次完成 Provider 技术/授权证据。

每个子任务必须独立提取、审查、迁移验证、测试和提交。E05–E12 候选不得整包 merge 或 cherry-pick。
