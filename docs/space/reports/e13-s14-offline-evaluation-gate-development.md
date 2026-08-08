# E13-S14 离线评估与阈值校准开发报告

- 状态：Engineering slice validated；正式黄金集签收待外部证据
- 日期：2026-08-08
- 起始集成基线：`6c99b0fe`
- 功能分支：`codex/space-e13-s14-offline-evaluation`
- 功能提交：`e69b3bca`
- 目标分支：`integration/space-v1-20260730`

## 1. 交付结论

本切片交付可复用的 E13-S14 离线质量评估器、阈值校准器、正式数据证据门禁和命令行入口。
它消费版本化数据清单、稳定预期对象、最终融合提案和人工操作计数，输出目标覆盖率、整体语义准确率、
高置信度精确率、95% Wilson 下界、人工操作下降率、分层结果、校准决策、发布门禁与防篡改 SHA-256。

这不是 E13-S14 的正式产品签收。仓库现有 `development-v2.0.0` 是 20 份合成 DXF
`DevelopmentSeed`，可用于验证评估器，但 `countsTowardReleaseGate=false`，没有组织授权、独立标注、
正式 Provider 输出和人工操作实测。实现会稳定报告 `releaseEligible=false`，不能通过修改一个布尔值把开发
数据冒充黄金集。

## 2. 数据集与证据门禁

规范化清单使用 `SpaceAiEvaluationManifestV1`。可评估数据必须满足：

- 恰好 20 个唯一资产，L1～L5 每类至少 4 个；SourceFile 和 64 位小写 SHA-256 均唯一；
- 单位固定 `Millimeter`、坐标固定 `FloorLocal-ZUp`，每个样本声明的预期目标数与实际答案一致；
- 每个样本都有正数人工基线操作和非负 AI 辅助操作计数；
- 预期 ID 全局唯一，预期 MatchKey 在样本内唯一；ProposalId 全局唯一；
- 关系只能引用同一样本内另一个真实 MatchKey，自引用、跨样本和不存在的目标失败关闭。

正式发布还必须同时满足：

- Purpose 为 `FormalRelease` 且显式计入发布门禁；Calibration/Validation/ReleaseHoldout 精确为
  10/5/5；
- 包级和每资产授权、每资产脱敏证据、应用完整提交 SHA、Parser/Provider/Model、映射、规则和标准答案
  版本齐全；
- 两名独立标注者加 QA 仲裁证据、ISO 日期、不可变标记和通过的 hash-sealed 完整性审计齐全。

数量、分布和证据缺口会进入失败关闭报告；重复身份、歧义匹配或非法关系等无法可信计算的输入直接拒绝。
任何正式证据缺失都会关闭发布资格和高置信度批量入口。
本切片没有读取客户 CAD、调用 Provider、写 Draft、创建数据库表或放宽既有安全策略。

## 3. 匹配与质量口径

评估器按 `SampleId + MatchKey` 一对一匹配，而不是使用数据库运行时 GUID 或数组顺序。一个预期目标
只有在最终提案的对象类型、声明的全部关键属性和精确逻辑关系都正确时才计为正确。额外属性允许存在；
额外、缺失或错误关系不正确。相同 MatchKey 的重复提案只有一个可命中，其余全部进入 False Positive，
不能通过重复猜测提高覆盖率。

指标与需求口径一致：

| 指标 | 计算 | 正式阈值 |
|---|---|---:|
| 目标覆盖率 | 正确提案 / 预期目标 | ≥ 80% |
| 整体语义准确率 | 正确提案 / 全部自动提案 | ≥ 90% |
| 高置信度精确率 | 正确高置信度提案 / 全部高置信度提案 | ≥ 95% |
| 高置信度 Wilson 下界 | 95% Wilson score lower bound | ≥ 90% |
| 人工操作下降率 | `1 - AI 辅助操作 / 纯人工操作` | ≥ 70% |

CAD 几何仍由 E02 已有确定性 IR、单位/坐标确认和黄金数据容差负责。E13 Provider 不生成最终几何，
因此本评估器只在已绑定稳定来源的最终融合提案上评价语义类型、关键属性和关系，避免重复或混淆边界。

## 4. 阈值校准与 Holdout 隔离

默认高置信度分数阈值是 0.90，但分数本身不代表精确率。校准器只读取 Calibration 样本，按可观察
置信度候选从低到高计算 Precision 和 Wilson 下界，选择第一个同时满足 95% / 90% 的阈值。没有候选
达标时返回 `CALIBRATION_THRESHOLD_UNAVAILABLE` 并关闭快捷入口。

Validation 和 ReleaseHoldout 永远不参与阈值选择，只在冻结阈值后作为 out-of-sample 组验证。单元测试
证明改变 Holdout 的错误提案不会改变校准结果。完美但样本过少时 Wilson 下界仍不足，不能以 100% 表面
准确率绕过统计样本量。正式 ReleaseEligible 同时要求结构、证据、校准、整体指标和 out-of-sample
高置信度指标全部通过。

## 5. 可运行入口与结果完整性

`CP6.Space.CadExperiment` 新增：

```powershell
dotnet run --project tools\CP6.Space.CadExperiment -c Release -- `
  evaluate-ai-offline `
  --input tmp\e13-s14\evaluation-request.json `
  --output tmp\e13-s14\evaluation-report.json `
  --require-release-eligible
```

不带 `--require-release-eligible` 时，结构有效的开发评估可以返回 0 并写报告；带该参数而不具备正式
资格时返回 4；结构无效返回 3。报告按稳定顺序生成规范 JSON 和 SHA-256，输入数组倒序不会改变哈希，
修改报告正文后再次序列化会被拒绝。

`SpaceAiEvaluationProposalAdapter` 把现有 `WarehouseDraftProposalV1` 的 SourceKey、最终字段、置信度
和关系目标转换为规范评估提案，不另造一套 AI 输出模型。

## 6. 验证证据

| 门禁 | 结果 |
|---|---|
| E13-S14 核心单元 | 11/11 passed |
| 离线评估命令入口 | 1/1 passed |
| CAD Experiment 全量 | 26/26 passed |
| Space Unit 全量 | 482/482 passed |
| 默认 Space Integration | 275 passed / 94 SQL-gated skipped / 0 failed |
| CP6.Tests | 2811 passed / 17 environment-gated skipped / 0 failed |
| 完整 solution Release（含 Desktop/Android AOT） | 0 warning / 0 error |
| C# whitespace / `git diff --check` | passed |

核心场景覆盖正式全绿、DevelopmentSeed 永不发布、重复提案、错误类型/属性、最低合格阈值、Holdout
泄漏隔离、小样本 Wilson 关闭、正式证据缺失、输入顺序确定性、报告防篡改、Draft 适配和关系引用完整性。

## 7. 剩余边界

E13-S14 还不能标记正式完成。需要数据/QA 提供 20 份获授权、脱敏且不可变的 DWG/DXF 黄金资产，按
10/5/5 和 L1～L5 完成两人独立标注及仲裁；需要冻结真实 Parser/Provider/Model/规则/映射版本，生成
最终融合提案，独立记录纯人工与 AI 辅助操作，并由完整性审计产生证据哈希。届时使用本命令运行正式
门禁，保存原始请求、报告和审批签字。

在正式 S14 通过前，E13-S15 的性能/影子运行/试点、依赖 S15 的 S18，以及 S19 独立发布证据仍不能
提前签收。生产外部 Provider 和批量 High Accept 继续默认关闭。
