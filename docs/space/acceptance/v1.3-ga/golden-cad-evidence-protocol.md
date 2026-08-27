# Space Studio V1 正式黄金 CAD 证据协议

适用 Gate：`WP7_GOLDEN_CAD_FORMAL_EVIDENCE`

正式集合必须包含 20 份唯一、真实授权并脱敏的 DWG/DXF，按 `Calibration/Validation/ReleaseHoldout=10/5/5` 冻结，覆盖 L1～L5。每份样本由一名真实 `reviewedBy` 完成可追溯人工复核；该人可以是 `DeliveryOwner`，不要求第二标注人或独立 QA 仲裁。

以下结果门禁保持不变：

- 数据集绑定应用 Commit、Parser、Mapping Profile、Rule Set、Expected Answer 和冻结 Worker，并通过完整性审计。
- Primary/Backup 是两条不同 Provider 链，使用相同 Source Set、黄金集、Worker 和 50 MiB 标准 CAD；两者资格分均不低于 80，且主链严格高于备链。
- Overall 与 Out-of-sample：覆盖率≥80%、准确率≥90%、高置信度精确率≥95%、Wilson 下界≥90%、人工操作下降≥70%。
- Holdout 未报告 Blocking 遗漏为 0；每条 Provider 至少 5 次性能观察，P95 到可审查≤15 分钟、受训用户首次 Ready≤60 分钟。

原始 CAD、客户身份、Secret 和 Provider 凭据不得进入仓库。复制 [`golden-cad-evidence-template.json`](./golden-cad-evidence-template.json)，补齐证据后运行：

```powershell
./tools/Test-SpaceGaGoldenCadEvidence.ps1 -ManifestPath <黄金 CAD Manifest>
./tools/Test-SpaceGaEvidence.ps1 -RequireGaReady
```

WP7 接受时必须绑定最终 Manifest 自身的仓库相对路径和 SHA-256。数据、答案、Provider、配置、Worker、Parser、规则或应用 Commit 变化时创建新版本并重跑，不覆盖旧证据。
