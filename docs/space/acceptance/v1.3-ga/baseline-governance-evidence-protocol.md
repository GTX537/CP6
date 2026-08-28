# Space Studio V1 Core GA WP0 基线与治理证据协议

适用 Gate：`WP0_BASELINE_AND_GOVERNANCE`

WP0 只证明交付基线和责任边界已经真实建立，不重复执行功能或发布验收。单人交付只要求
一名实名 `DeliveryOwner`；不要求第二人、独立复核、角色配额或签字法定人数。

正式 Manifest 必须同时证明：

- 精确 `main` Commit 已存在，并且是当前验收分支的祖先；
- DeliveryOwner、Kickoff、目标 GA 日期与总索引一致；
- 两类外部输入均有实名 Owner、正式 Manifest 和内容 SHA-256；
- WP3、WP4、WP7 已接受，证明 Primary、三路径和黄金 CAD 基线可复用；
- 合并后总 GA 与证据门禁通过，工作区干净；
- 未执行生产部署，也未把 WP0 描述成最终 GA 签署。

从 [`baseline-governance-evidence-template.json`](./baseline-governance-evidence-template.json)
复制版本化 Manifest，然后执行：

```powershell
./tools/Test-SpaceGaBaselineGovernanceEvidence.ps1 `
  -ManifestPath <WP0 正式 Manifest> `
  -ExpectedOwnerName BUBAO.GAO `
  -ExpectedKickoffDate 2026-08-27 `
  -ExpectedTargetGaDate 2026-09-27
```

通过 WP0 不代表剩余功能 Gate、发布演练、生产部署或最终签署完成。
