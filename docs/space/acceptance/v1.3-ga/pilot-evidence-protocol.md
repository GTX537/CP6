# Space Studio V1 双仓 Pilot 证据协议

版本：1.0
适用 Gate：`WP8_TWO_SITE_PILOT_AND_SIGNOFF`

## 1. 目的与边界

本协议把一个绿地仓和一个存量改造仓的现场 Pilot 结果收敛为可追溯、可哈希、可机器拒绝的验收包。它只定义证据结构，不生成现场事实，也不允许用模板、测试 fixture、Mock、合成 Site 或仓库内自动化替代真实 Pilot。

只有以下条件全部成立，最终 Pilot Manifest 才能写成 `conclusion=Pass`：

- 两仓分别连续运行至少 14 个日历日，并且每天都有不可变运行记录。
- 两仓均为零 S1/S2；所有 S3 在签字前关闭。
- 2D、3D、机器对象清单和 WMS 一致性均为 100%。
- 自动恢复不超过 15 分钟，人工对账恢复不超过 240 分钟；没有故障时对应事件数和最大时长都写 `0`。
- 故障期间旧 Published 持续可用，生产 Viewer 只消费 Published，Site 不进入长期双写。
- 每仓保存真实建模时长、人工修改量、恢复、缺陷、开放问题和业务结果证据。
- 每仓客户仓库代表与实施负责人分别实名确认，且确认人与证明对象 `acceptedBy` 一致。

五个内部 GA 签字人继续由 `ga-evidence-index.json/signers` 管理，客户仓库代表和实施负责人不是 GA 审批人。

## 2. 安全的数据边界

- `siteRef` 必须使用 `urn:cp6-space-site:*` 的不透明引用，不写客户名称、地址或仓库编码。
- 原始 DWG/DXF、Excel、库存明细、人员信息、令牌和密码不得进入仓库。
- `tools/test-fixtures` 只允许专项测试显式启用，正式 Pilot Manifest 及其嵌套证明不得引用。
- 仓库内证据使用相对路径并校验实际 SHA-256；受控外部证据只允许 HTTPS 或 `urn:cp6-space-ga-evidence:*`。
- 外部证据对象必须记录真实接受人和 UTC 时间。哈希代表受控存储对象的不可变内容，不是任意占位字符串。

## 3. Manifest 字段

从 `pilot-evidence-template.json` 复制到新的、带日期或版本的文件；不得直接修改模板作为验收结果。

| 字段 | 规则 |
|---|---|
| `schemaVersion` | 固定为 `1` |
| `programId` | 固定为 `CP6_SPACE_STUDIO_V1_CORE_GA` |
| `evidenceClass` | 固定为 `WP8_TWO_SITE_PILOT` |
| `conclusion` | 只有所有条件通过后才写 `Pass` |
| `sites` | 恰好两个，分别为 `Greenfield`、`Retrofit`，`siteRef` 唯一 |
| `runStartDate/runEndDate` | `yyyy-MM-dd`；首尾日都计入，跨度至少 14 天 |
| `continuousRunDays` | 必须与日期跨度完全相同 |
| `dailyRecordCount/dailyRecordDates` | 数量必须与日期跨度相同，日期按时间顺序逐日连续，不允许重复或缺日 |
| `defects` | S1/S2 为零；每个 S3 都有可用绕行，`s3ClosedBeforeSignoff=s3Opened` 且 `s3OpenAtSignoff=0` |
| `metrics` | 建模时长大于零、人工修改量非负、两项一致性均为 100 |
| `recovery` | 有事件时记录真实最大时长；无事件时事件数和最大时长均为零 |
| `boundaries` | `publishedViewerOnly` 和 `noLongTermDualWrite` 均为 `true` |
| `evidence` | 运行日志、指标、缺陷关闭、业务结果、开放问题附录五类证明对象 |
| `confirmations` | 客户仓库代表和实施负责人实名及其独立证明对象 |

证明对象统一格式：

```json
{
  "uri": "urn:cp6-space-ga-evidence:pilot:site-ref:run-log-v1",
  "sha256": "<受控对象的 64 位 SHA-256>",
  "acceptedBy": "<真实人名>",
  "acceptedAtUtc": "2026-08-14T16:00:00Z"
}
```

## 4. 校验与接入 GA 索引

先独立校验最终 Manifest：

```powershell
./tools/Test-SpaceGaPilotEvidence.ps1 `
  -ManifestPath ./docs/space/acceptance/v1.3-ga/evidence/pilot-2026-xx.json
```

再执行以下步骤：

1. 把最终 Manifest 放入仓库安全路径；客户敏感原始证据继续留在受控外部存储。
2. 计算最终 Manifest 自身的 SHA-256。
3. 在 `WP8_TWO_SITE_PILOT_AND_SIGNOFF.verificationManifest` 填入该仓库相对路径。
4. 在该 Gate 的 `acceptedEvidence` 中至少增加一个指向同一 Manifest、哈希匹配、由真实 Gate Owner 接受的证明对象；同时保留必要的外部证明引用。
5. 只有现场条件、两个客户/实施确认和五方内部签字全部完成后，才把 WP8 改为 `Accepted`。
6. 运行 `./tools/Test-SpaceGaEvidence.ps1 -RequireGaReady`。该命令会再次校验 Manifest，不接受模板或测试 fixture。

## 5. 失败处理

- Pilot 中断后重新起算连续 14 天，不拼接两个不连续窗口。
- 结束日期不能在未来；运行、指标、缺陷、业务结果、开放问题和现场确认均不得在该仓 Pilot 结束前预签。
- 出现 S1/S2 时本轮不能签收；修复后按 QA 决定重新执行完整 Pilot 窗口。
- S3 未关闭、恢复超时、一致性不足、旧 Published 不可用、Viewer 读取 Draft 或存在长期双写时，Manifest 保持 `Pending`，不得手改为 `Pass`。
- 证明对象变更必须产生新哈希；不得覆盖旧对象后保留旧 SHA。
