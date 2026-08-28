# CP6 Space Studio V1 M0 开工证据协议

开工 Manifest 只登记两类真实外部输入，不登记团队人数、多人签字或客户 Pilot：

- `AUTHORIZED_GOLDEN_CAD_CANDIDATES`：20 份唯一、获授权、已脱敏的 DWG/DXF 候选，L1～L5 各至少四份，并封存候选集哈希。
- `PRIMARY_PROVIDER_AND_ISOLATED_WORKER`：一条 Primary Provider 链的真实许可、安全、保留/删除策略，以及隔离 Worker 或 Owner 批准的 V1 本机受控边界均有证据。

V1 的 `LocalControlledProcess` 允许实名 DeliveryOwner 接受本机受控边界，不把
OS Firewall 出站 Deny 作为阻断项；它仍强制无网络监听、无业务凭据、原始 CAD
逐 Attempt 临时保存并在 `finally` 删除，以及不可变 Release/环境/报告哈希。
生产、公共 SaaS 或远程 Worker 不适用该例外，仍须使用隔离网络、身份、证书和
对应范围的许可/安全批准。

单一 `DeliveryOwner` 可以拥有并接受全部输入；不要求不同姓名。Provider 许可和执行环境授权仍必须真实存在，因为它们是权限事实，不是开发团队人头门禁。

复制 [`kickoff-evidence-template.json`](./kickoff-evidence-template.json) 创建版本化 Manifest。分批完成时使用 `conclusion=InProgress` 并指定 `-InputId`；两类全部完成后使用 `conclusion=Pass`。已经被验收引用的 Manifest 不得原地覆盖。

```powershell
./tools/Test-SpaceGaKickoffEvidence.ps1 `
  -ManifestPath <开工 Manifest> `
  -InputId AUTHORIZED_GOLDEN_CAD_CANDIDATES `
  -ExpectedOwnerName '<DeliveryOwner 实名>'

./tools/Test-SpaceGaKickoffEvidence.ps1 -ManifestPath <完整开工 Manifest>
./tools/Test-SpaceGaEvidence.ps1
```

专项通过只证明外部输入就绪，不自动接受 WP Gate。原始 CAD、凭据和客户敏感信息留在受控系统；仓库只保存不透明引用、SHA-256、非敏感元数据和接受记录。
