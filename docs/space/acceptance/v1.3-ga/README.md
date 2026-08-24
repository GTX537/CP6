# CP6 Space Studio V1 核心 GA 证据索引

本目录是核心 GA 的唯一汇总入口。它把“代码已完成”“环境证据已接受”和“GA 已签字”分成三个状态，避免把 Mock、skipped 测试或仓库实现冒充生产验收。

当前结论固定为 `NoGo`。原因不是仓库主链不可用，而是实名 Owner、真实主备 Provider、20 份授权黄金 CAD、真实 SQL/WMS/Published Viewer、两仓各 14 天 Pilot 和五方签字尚未齐全。

## 单人开发人员种子

当前单人开发阶段可以使用 [`development-personnel-seed.json`](./development-personnel-seed.json) 中的 `00001`～`00005` 做本地角色切换、任务归属和权限测试。它们全部标记为 `DevelopmentSeed`、`simulated=true`、`productionAccess=false`、`formalGaEligible=false`；详细边界见 [`development-personnel-seed.md`](./development-personnel-seed.md)。

这些编号不是实名人员，不能关闭 `CORE_TEAM_ALLOCATION`，不能作为任何 Owner、证据接受人、Pilot 确认人或五方 GA 签字人。正式人名校验器会拒绝纯数字和开发/测试身份；因此建立开发人员种子不会改变 72% / `NoGo` 状态。

## 使用方法

1. 在 [`ga-evidence-index.json`](./ga-evidence-index.json) 中填写真实 `ownerName`、`kickoffDate` 和 `targetGaDate`；不得填写角色名、团队名或 `TBD` 冒充实名。
2. 外部输入交付后，把对应 `status` 从 `Pending` 改为 `Complete`，并附带可追溯证据。
3. 代码完成只更新 `implementationStatus`。只有 QA/业务接受了真实环境证据，才把 `acceptanceStatus` 改为 `Accepted`。
4. 已接受证据必须记录仓库内相对路径或受控证据 URI、SHA-256、接受人和 UTC 时间；原始客户 CAD 不得进入仓库。
5. 五个签字角色必须全部实名并标记 `Signed`。只有所有 Blocking Gate、外部输入和签字同时通过，才允许把 `declaredStatus` 改为 `GaReady`、整体进度记为 100%。

## 校验命令

```powershell
./tools/Test-SpaceGaEvidence.ps1
./tools/Test-SpaceGaEvidence.ps1 -RequireGaReady
./tools/Test-SpaceGaKickoffEvidence.ps1 `
  -ManifestPath <最终或增量开工 Manifest 路径> `
  -InputId <五类外部输入之一>
./tools/Test-SpaceGaPilotEvidence.ps1 `
  -ManifestPath <最终双仓 Pilot Manifest 路径>
./tools/Test-SpaceGaGoldenCadEvidence.ps1 `
  -ManifestPath <最终黄金 CAD Manifest 路径>
```

第一条校验索引结构、路径和状态自洽；第二条是正式 GA 门禁，当前应以退出码 `2` 失败。任何人不得通过删除 Blocking Gate、降低门槛或把合成证据标成 Accepted 来消除该失败。

任何 `externalInputs.status=Complete` 都必须按 [`kickoff-evidence-protocol.md`](./kickoff-evidence-protocol.md) 绑定结构化开工 Manifest，并由该输入的 `evidence` 证明 Manifest 自身哈希。专项校验器按输入 ID 复核实名签字人、2+2+1 团队、20 份授权 CAD 候选、至少两条 Provider 审批与隔离 Worker、Greenfield/Retrofit 双仓和 CP6 WMS 窗口；总校验器还会核对分区 Owner 与索引 Owner、签字人登记与总索引逐角色一致。空模板或一份泛化说明不能关闭外部输入。

WP8 不能只附一份泛化签字说明。标记 `Accepted` 前必须先完成五个内部角色签字，再按 [`pilot-evidence-protocol.md`](./pilot-evidence-protocol.md) 生成最终结构化 Manifest，在 Gate 的 `verificationManifest` 中登记其仓库相对路径，并由 `acceptedEvidence` 对该 Manifest 自身的内容哈希进行证明。总校验器会调用 Pilot 校验器复核双仓类型、连续 14 天、每日记录、缺陷、恢复 SLO、一致性、Published-only/双写边界和两类现场确认；空白模板与测试 fixture 永远不能作为正式证据。

WP7 同样不能用一份汇总文档代替原始质量链。按 [`golden-cad-evidence-protocol.md`](./golden-cad-evidence-protocol.md) 生成最终 Manifest，登记到 WP7 的 `verificationManifest` 并证明其内容哈希。总校验器会复核授权 20 份、10/5/5、L1～L5、DWG/DXF、双人标注/QA 仲裁、Primary/Backup 同 Source Set 和 Worker、主 Provider 严格高分、两者使用同一 50 MiB 标准 CAD、两份 release-eligible 评估、质量/Wilson/人工操作/Holdout Blocking 与 50 MiB/Ready P95；黄金 CAD 和 Provider/Worker 外部输入或 WP3 验收未完成时，WP7 不能 Accepted。

## 证据证明对象

`signers[].evidence`、`externalInputs[].evidence` 和 `gates[].acceptedEvidence` 使用同一结构：

```json
{
  "uri": "docs/space/acceptance/v1.3-ga/evidence/wp1-report.json",
  "sha256": "<64 hex>",
  "acceptedBy": "<real person name>",
  "acceptedAtUtc": "2026-08-14T12:00:00Z"
}
```

- 仓库内证据只能使用仓库根目录相对路径。校验器会重算文件 SHA-256；不存在、越界或哈希不一致均失败。
- 受客户数据边界限制的证据可使用无用户信息的 HTTPS URI，或 `urn:cp6-space-ga-evidence:*` 受控引用；由接受人记录受控存储中对象内容的 SHA-256。
- `acceptedBy` 必须是真实人名，不接受 `TBD/Pending/待定`；`acceptedAtUtc` 必须为以 `Z` 结尾的 ISO-8601 UTC 时间，不允许未来时间。
- `signers[].evidence.acceptedBy` 必须与该角色登记的 `name` 一致；不能由另一人代替签字人证明其签署。
- 原始 `.dwg`/`.dxf` 不能作为仓库内证据；只提交授权登记、脱敏指标、哈希和接受记录。

开发者可运行 `./tools/Test-SpaceGaEvidence.Tests.ps1` 覆盖本地哈希、受控 URI、不存在路径、不安全 scheme、原始 CAD、UTC 和占位接受人等正反向场景。

## 状态口径

| 字段 | 含义 |
|---|---|
| `implementationStatus=Complete` | 仓库代码、合同和自动化已完成；不代表生产验收 |
| `acceptanceStatus=Accepted` | 冻结环境的真实证据已由实名 Owner 接受 |
| `externalInputs.status=Complete` | 授权、人员、Site、窗口或基础设施已真实交付 |
| `signers.status=Signed` | 具有审批权的实名角色已签署 |
| `declaredStatus=GaReady` | 所有 Blocking Gate、外部输入和五方签字均通过 |

10–12 周是依赖按时交付时的计划窗口，不是自动倒计时。本索引中的里程碑从实名 kickoff 日期起算；外部输入延期时顺延 GA 日期，不削减门槛。
