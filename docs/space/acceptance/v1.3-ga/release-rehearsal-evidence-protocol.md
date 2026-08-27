# Space Studio V1 Core GA 发布演练协议

适用 Gate：`WP8_RELEASE_REHEARSAL_AND_SIGNOFF`

Core GA 只要求一次可复现、内容哈希固定的受控发布演练，不再要求两个客户现场或连续 14 天 Pilot。现场推广属于 GA 后的运营增强，不阻断单人开发的首版产品结案。

演练必须绑定应用完整 Git Commit、已冻结的 Source Set、Golden Dataset 和 Worker Environment，并在受控 SQL Server、CP6 WMS 与 Published-only Viewer 边界内完成。以下结果全部为真才能写 `conclusion=Pass`：

- DWG/DXF 端到端和 CAD、Excel、手工三条建模路径通过；
- Publish/WMS、Published/Draft 隔离、恢复和安全负向通过；
- 重试没有重复 Location、事件或外部写入；
- 自动恢复不超过 15 分钟，人工恢复不超过 240 分钟，故障期间旧 Published 持续可用；
- 开放 S1、S2 和 Blocking S3 均为 0；
- 执行、Publish/WMS、Viewer、恢复和安全五类证据均由同一真实 `DeliveryOwner` 接受。

从 [`release-rehearsal-evidence-template.json`](./release-rehearsal-evidence-template.json) 复制一个版本化 Manifest；模板、测试 fixture、Mock、原始 CAD、Secret 或仓库外绝对路径不能作为正式证据。外部证据使用 HTTPS 或 `urn:cp6-space-ga-evidence:*`，仓库内证据使用相对路径和实际 SHA-256。

```powershell
./tools/Test-SpaceGaReleaseRehearsalEvidence.ps1 `
  -ManifestPath <发布演练 Manifest>

./tools/Test-SpaceGaEvidence.ps1 -RequireGaReady
```

通过本协议不代表已执行生产部署。生产部署仍按 Release/CD 的候选、环境审批和部署门禁独立执行。
