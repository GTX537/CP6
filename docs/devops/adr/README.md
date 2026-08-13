# CP6 DevOps ADR 索引

本目录保存会影响候选身份、发布权威、环境推广和恢复边界的架构决策。ADR 只记录决策与批准状态，不替代流水线运行证据、资源侧审批或生产 Go/No-Go。带 `decisionPayloadSha256` 的 ADR 必须把不可变决策正文与可变状态/批准镜像分开；批准绑定规范化决策摘要，不能绑定一个随后会因状态更新而变化的完整文档 blob。

## 状态语义

| 状态 | 含义 |
| --- | --- |
| Proposed | 决策内容已写明，仍缺一个或多个 named approver 的有效批准 |
| Accepted | 所有必需 approver 已批准，证据 URI、时间和适用范围均完整 |
| Superseded | 已由后续 Accepted ADR 取代；保留历史，不删除 |
| Rejected | 决策未获批准，不得实施 |

任何 `Pending`、过期、摘要不匹配或无法读取的批准证据都按未批准处理。ADR 不得保存 Secret、Token、Cookie、私钥、原始 PII 或可变“latest”证据链接；对象存储证据必须固定精确对象版本与内容摘要。

## 决策记录

| ADR | 状态 | 范围 |
| --- | --- | --- |
| [ADR-CRM-R00：CRM V1 发布权威与 Registry](./ADR-CRM-R00-RELEASE-AUTHORITY.md) | Proposed | GHCR/GitHub R2 唯一权威、Azure 边界、三仓 System Release Manifest、回退与等价缺口 |
