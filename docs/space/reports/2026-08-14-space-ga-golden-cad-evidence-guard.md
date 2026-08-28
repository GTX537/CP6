# Space Studio V1 正式黄金 CAD 证据门禁

> 当前口径：本文的双 Provider 描述是 2026-08-14 的历史规则。
> 2026-08-27 起只要求一个通过 `cad-provider-adr-0001-v2` 的 Primary，
> Backup 为可选增强；20 份黄金集与质量门槛继续保留。

日期：2026-08-14

范围：核心 GA / WP7 真实黄金 CAD 与双 Provider 正式证据

结论：WP7 已具备结构化证据协议、复制模板、失败关闭校验器和总 GA 组合门禁。该任务只关闭未来验收的证据断链与误报风险，不表示真实黄金 CAD、主备 Provider 或冻结 Worker 已交付；WP7 继续为 `ExternalExecution/Pending`，核心 GA 继续为 72% / No-Go。

## 复用而非重造

- 语义覆盖率、准确率、高置信度精确率、Wilson 下界、人工操作下降和 `releaseEligible` 继续由现有 `SpaceAiOfflineEvaluator` 计算。
- Provider 80 分硬门槛与 Primary/Backup 排名继续由 `qualify-providers` 和 ADR-0001 决定。
- 新 `Test-SpaceGaGoldenCadEvidence.ps1` 验证正式包装层：真实授权数据、冻结身份、两份报告和性能证据是否属于同一条可追踪链。

## 失败关闭规则

- 数据集必须恰好 20 份、唯一 Source SHA、10/5/5、L1～L5 每类至少 4 份，并同时包含 DWG/DXF。
- 每份样本使用不透明 URN，记录真实字节数、`ApprovedCustomerDerived` 授权、脱敏证明、两个不同标注人和独立 QA 仲裁；标注证明接受人必须等于 QA 仲裁人。
- Release Holdout 禁止调参；数据集冻结、Holdout 冻结、应用 Commit、Parser、Mapping、Rule、Expected Answer、Worker 环境和完整性审计均被绑定。
- `sourceSetSha256` 从排序后的 Sample Ref + Source SHA 确定性重算，Primary/Backup 必须使用该同一集合、同一 Golden Dataset 和同一 Worker。
- 两个 Provider 必须身份不同、资格分≥80、`releaseEligible=true`，评估证明哈希必须绑定声明的 Report SHA。
- Overall 和 Out-of-sample 都需覆盖率≥80%、准确率≥90%、高置信度精确率≥95%、Wilson 下界≥90%、人工操作下降≥70%；Holdout 未报告 Blocking 遗漏为 0。
- 每条 Provider 至少 5 次 50 MiB 到审查和 5 次受训用户到 Ready 观察；nearest-rank P95 分别≤15/60 分钟，并绑定同一冻结 Worker。
- 正式模式拒绝模板、测试 fixture、原始 CAD 仓库路径、未来/过早接受证据和不受控 URI。

## 与总 GA 索引联动

WP7 标记 Accepted 时，总校验器要求：

1. `AUTHORIZED_GOLDEN_CAD_CANDIDATES` 和 `PROVIDER_APPROVALS_AND_ISOLATED_WORKER` 已 Complete。
2. WP3 主备 Provider Gate 已 Accepted。
3. `verificationManifest` 是仓库内非模板、非 fixture 的 JSON。
4. WP7 `acceptedEvidence` 对该 Manifest 自身哈希进行证明。
5. Manifest 再次通过黄金 CAD 专项校验器。

## 自动化

- 黄金 CAD 专项：31/31。覆盖正式形状正向、正式模式拒绝 fixture、空模板稳定语义失败、样本数/唯一性、10/5/5、布局、DWG/DXF、授权、Holdout 泄漏、标注/仲裁、Source Set Hash、主备身份/分数/排序/同一标准 CAD/基线/报告哈希、五项质量指标、Holdout Blocking、50 MiB、两项 P95、过早证据和缺失证明。
- 总 GA 证明链：29/29。新增 WP7 缺 Manifest、外部/WP3 前置缺失、有效组合、Manifest 未证明、语义无效和空模板六类场景。
- PowerShell/JSON/YAML 解析门禁随 CI 执行；当前权威索引保持 `NoGo`、5 个 Pending Inputs、9 个 Pending Gates、5 个 Pending Signers。
- `-RequireGaReady` 仍以退出码 2 失败，证明本任务没有用结构工具替代真实 Provider、黄金 CAD 或批准。

## 外部执行仍缺失

业务/QA 仍需提供 20 份获授权真实 CAD、双人标注和 QA 仲裁；法务/安全/平台仍需交付两条获批 Provider 与冻结 Worker；Provider 工程与 QA 仍需在同一 Source Set/环境运行正式评估和性能采样。原始客户 CAD 不进入仓库，只提交安全摘要、哈希和受控证明引用。
