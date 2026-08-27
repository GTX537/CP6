# CP6 Space Studio V1 M0 开工证据协议

开工 Manifest 只登记三类真实外部输入，不登记团队人数或多人签字：

- `AUTHORIZED_GOLDEN_CAD_CANDIDATES`：20 份唯一、获授权、已脱敏的 DWG/DXF 候选，L1～L5 各至少四份，并封存候选集哈希。
- `PROVIDER_APPROVALS_AND_ISOLATED_WORKER`：至少两条不同 Provider 链，许可、安全、区域、保留策略和隔离 Worker 边界均有证据。
- `TWO_PILOT_SITES_AND_WMS_WINDOWS`：一个 Greenfield、一个 Retrofit，CP6 WMS 窗口覆盖各自连续 14 天 Pilot。

单一 `DeliveryOwner` 可以拥有并接受全部三类输入；不要求不同姓名。客户、许可或生产访问所需的授权仍必须真实存在，因为它们是权限事实，不是开发团队人头门禁。

复制 [`kickoff-evidence-template.json`](./kickoff-evidence-template.json) 创建版本化 Manifest。分批完成时使用 `conclusion=InProgress` 并指定 `-InputId`；三类全部完成后使用 `conclusion=Pass`。已经被验收引用的 Manifest 不得原地覆盖。

```powershell
./tools/Test-SpaceGaKickoffEvidence.ps1 `
  -ManifestPath <开工 Manifest> `
  -InputId AUTHORIZED_GOLDEN_CAD_CANDIDATES `
  -ExpectedOwnerName '<DeliveryOwner 实名>'

./tools/Test-SpaceGaKickoffEvidence.ps1 -ManifestPath <完整开工 Manifest>
./tools/Test-SpaceGaEvidence.ps1
```

专项通过只证明外部输入就绪，不自动接受 WP Gate。原始 CAD、凭据和客户敏感信息留在受控系统；仓库只保存不透明引用、SHA-256、非敏感元数据和接受记录。
