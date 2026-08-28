# CP6 Space Studio V1 核心 GA 证据索引

本目录是核心 GA 的唯一汇总入口。当前采用单人交付：一名真实 `DeliveryOwner` 可以兼任产品、开发、QA、UX、架构、安全和 WMS 联调职责，也可以自验收并完成最终签署。不要求不同人员、独立复核、多人配额或签字法定人数。

门禁只看结果证据：自动化结果、受控 SQL/WMS、Published-only Viewer、性能样本、恢复演练、授权 CAD 和 Primary Provider 输出。角色模拟、Mock、skipped 测试或口头说明不能替代这些证据。

当前仍为 `NoGo` / 72%。20 份 `ApprovedOriginalWork` 黄金 CAD、正式 AutoCAD Primary/Worker、业务质量、50 MiB/Ready 性能，以及 CAD / Excel–CAD / 手工底图与空白画布三路径证据均已接受；WP3、WP4、WP7 已 Complete/Accepted。仍未完成的是 WP0～WP2、WP5、WP6、WP8 的结果接受及唯一 DeliveryOwner 最终签署。独立 Backup Provider 与双仓 14 天 Pilot 已改为 GA 后增强项。

## 最小流程

1. 在 [`ga-evidence-index.json`](./ga-evidence-index.json) 填写同一位真实 `DeliveryOwner`、日期和各项 Owner；同一个人可以出现在所有 Owner/接受人字段。
2. 外部输入绑定对应的结构化 Manifest；原创或客户派生黄金 CAD 候选使用 [`authorized-golden-cad-candidates-v1.0.0.json`](./authorized-golden-cad-candidates-v1.0.0.json)，Primary Provider/Worker 使用 [`kickoff-evidence-template.json`](./kickoff-evidence-template.json)。
3. Gate 只在真实结果通过后改为 `Accepted`，并记录证据 URI、SHA-256、接受人和 UTC 时间。
4. 所有 Gate、外部输入和唯一 `DeliveryOwner` 签署完成后，才可改为 `GaReady` / 100%。

本地 `00001`～`00005` 仅用于权限和角色切换测试，不能冒充真实 `DeliveryOwner`；详见 [`development-personnel-seed.md`](./development-personnel-seed.md)。

## 校验

```powershell
./tools/Test-SpaceGaEvidence.ps1
./tools/Test-SpaceGaEvidence.ps1 -RequireGaReady
./tools/Test-SpaceGaKickoffEvidence.ps1 -ManifestPath <开工 Manifest>
./tools/Test-SpaceGaGoldenCadCandidates.ps1
./tools/Test-SpaceGaGoldenCadEvidence.ps1 -ManifestPath <黄金 CAD Manifest>
./tools/Test-SpaceGaThreePathEvidence.ps1 -ManifestPath <三路径 Manifest>
./tools/Test-SpaceGaReleaseRehearsalEvidence.ps1 -ManifestPath <发布演练 Manifest>
```

正式模式拒绝模板、测试 fixture、原始客户 CAD、越界路径、哈希不一致、未来时间和占位人名。`ApprovedOriginalWork` 必须是真实原创 CAD，由实际作者授权和复核；不得虚构客户来源。证据对象统一为：

```json
{
  "uri": "docs/space/acceptance/v1.3-ga/evidence/wp1-report.json",
  "sha256": "<64 hex>",
  "acceptedBy": "<DeliveryOwner real name>",
  "acceptedAtUtc": "2026-08-26T12:00:00Z"
}
```

仓库内证据使用相对路径并重算哈希；外部受控证据使用无用户信息的 HTTPS URI 或 `urn:cp6-space-ga-evidence:*`。原始 `.dwg`/`.dxf` 不进入仓库。
