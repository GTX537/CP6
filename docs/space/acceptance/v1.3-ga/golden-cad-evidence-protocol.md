# Space Studio V1 正式黄金 CAD 证据协议

版本：1.0

适用 Gate：`WP7_GOLDEN_CAD_FORMAL_EVIDENCE`

## 1. 目的与权威关系

本协议把已有黄金数据协议、E13-S14 离线评估报告、ADR-0001 Provider 资格报告和性能记录组合成一个可失败关闭的 WP7 Manifest。它不替代现有评估算法：覆盖率、准确率、高置信度精确率、Wilson 下界和人工操作下降仍由 `SpaceAiOfflineEvaluator` 计算；本协议验证正式数据、两个 Provider、冻结基线和报告之间没有断链。

模板、测试 fixture、`DevelopmentSeed`、合成 CAD 或人工改写指标不得计入正式证据。只有真实授权的 20 份 DWG/DXF、双人标注和 QA 仲裁、冻结后的主备 Provider 输出及现场性能记录可以生成 `conclusion=Pass`。

## 2. 数据与安全边界

- 原始 DWG/DXF、客户名称、仓库编码、库存、人员信息、Secret 和 Provider 凭据不得进入仓库。
- 每份样本使用 `urn:cp6-space-golden-cad:*` 不透明引用，只登记格式、字节数和 SHA-256。
- 原始资产、标注答案和 Provider 输出可保存在受控外部存储；Manifest 使用 HTTPS 或 `urn:cp6-space-ga-evidence:*`、内容哈希、真实接受人和 UTC 时间引用。
- 仓库内证明只能使用相对路径，实际内容 SHA-256 必须匹配；正式模式拒绝 `tools/test-fixtures`。
- `sourceSetSha256` 按 `sampleRef` 序排序后，对每行 `<sampleRef>:<lowercase sourceSha256>` 以 UTF-8 和 LF 拼接计算，确保两条 Provider 链声明的是同一组文件。

## 3. 数据集门禁

最终 Manifest 必须满足：

- 恰好 20 个唯一样本和唯一 Source SHA；`Calibration/Validation/ReleaseHoldout=10/5/5`。
- L1～L5 每类至少 4 份；正式集合同时包含真实 DWG 和 DXF。
- `license=ApprovedCustomerDerived`，每份都有授权、脱敏和标注证明。
- 两名标注人相互不同，QA 仲裁人也必须独立；标注证明 `acceptedBy` 必须等于 QA 仲裁人。
- Release Holdout 的 `usedForTuning=false`；冻结时间不能在未来。
- 数据集绑定应用 Commit、Parser、Mapping Profile、Rule Set、Expected Answer 和冻结 Worker 环境，并通过不可变完整性审计。

## 4. 主备 Provider 与指标

`providers` 恰好包含一个 Primary 和一个 Backup，二者 Provider Key/Version 不同且：

- 资格分均不低于 80，`releaseEligible=true`；Primary 资格分必须严格高于 Backup，落实“合格者最高分为主、第二名为备”。
- `goldenDatasetSha256`、`evaluatedSourceSetSha256`、`frozenWorkerEnvironmentSha256` 与数据集完全一致。
- `evaluationEvidence.sha256` 与 `evaluationReportSha256` 一致；资格、评估和性能证明都不能早于 Holdout 冻结。
- Overall 和 Validation+Holdout Out-of-sample 指标都满足：覆盖率≥80%、准确率≥90%、高置信度精确率≥95%、Wilson 95% 下界≥90%、人工操作下降≥70%。
- Release Holdout 的未报告 Blocking 遗漏数为 0。
- 使用至少 50 MiB 的同一标准 CAD 和同一冻结 Worker；每条 Provider 至少保存 5 次到可审查结果和 5 次受训用户到首次 Ready 观察，nearest-rank P95 分别≤15 分钟和≤60 分钟。

Provider 评分和主备选择继续由 `qualify-providers` 生成，不能在本 Manifest 中自行修改分数或角色。离线评估继续使用：

```powershell
dotnet run --project tools/CP6.Space.CadExperiment -c Release -- `
  evaluate-ai-offline `
  --input <受控评估请求> `
  --output <受控评估报告> `
  --require-release-eligible
```

## 5. 校验与 GA 索引接入

从 `golden-cad-evidence-template.json` 复制到新的版本化文件，补齐 20 份样本和两条 Provider 后执行：

```powershell
./tools/Test-SpaceGaGoldenCadEvidence.ps1 `
  -ManifestPath ./docs/space/acceptance/v1.3-ga/evidence/golden-cad-2026-xx.json
```

校验通过后：

1. 计算最终 Manifest 自身 SHA-256。
2. 在 `WP7_GOLDEN_CAD_FORMAL_EVIDENCE.verificationManifest` 写入仓库相对路径。
3. 在该 Gate 的 `acceptedEvidence` 中加入指向同一 Manifest、哈希匹配、由实名 Owner 接受的证明对象，并保留受控外部原始报告引用。
4. 把 Gate 改为 `Accepted` 前，确保授权黄金集和 Provider/隔离 Worker 两类外部输入均已 `Complete`。
5. 运行 `./tools/Test-SpaceGaEvidence.ps1 -RequireGaReady`。总门禁会再次调用 WP7 校验器；模板或测试 fixture 永远不能充当正式验收。

## 6. 失败与重跑

- Holdout 泄漏、数据或答案变化必须提升数据集版本、重新冻结并重跑两条 Provider。
- Provider Version、Config、Worker 环境、Parser、Mapping、Rule 或应用 Commit 任一变化，都必须重新生成对应报告和证据哈希。
- 任一 Provider 未达门槛、主备未使用同一 Source Set、50 MiB/Ready P95 超标或 Holdout 出现未报告 Blocking 遗漏时保持 `Pending`。
- 只允许追加新版本，不覆盖已用于签字的 Manifest 或受控报告。
