# CP6 Space Studio V1 核心 GA 证据索引

本目录是核心 GA 的唯一汇总入口。它把“代码已完成”“环境证据已接受”和“GA 已签字”分成三个状态，避免把 Mock、skipped 测试或仓库实现冒充生产验收。

当前结论固定为 `NoGo`。原因不是仓库主链不可用，而是实名 Owner、真实主备 Provider、20 份授权黄金 CAD、真实 SQL/WMS/Published Viewer、两仓各 14 天 Pilot 和五方签字尚未齐全。

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
```

第一条校验索引结构、路径和状态自洽；第二条是正式 GA 门禁，当前应以退出码 `2` 失败。任何人不得通过删除 Blocking Gate、降低门槛或把合成证据标成 Accepted 来消除该失败。

## 状态口径

| 字段 | 含义 |
|---|---|
| `implementationStatus=Complete` | 仓库代码、合同和自动化已完成；不代表生产验收 |
| `acceptanceStatus=Accepted` | 冻结环境的真实证据已由实名 Owner 接受 |
| `externalInputs.status=Complete` | 授权、人员、Site、窗口或基础设施已真实交付 |
| `signers.status=Signed` | 具有审批权的实名角色已签署 |
| `declaredStatus=GaReady` | 所有 Blocking Gate、外部输入和五方签字均通过 |

10–12 周是依赖按时交付时的计划窗口，不是自动倒计时。本索引中的里程碑从实名 kickoff 日期起算；外部输入延期时顺延 GA 日期，不削减门槛。
