# CP6 DevOps ADR

本目录保存影响发布权威、Registry、环境推广和运行身份的架构决策。ADR 只记录已经明确的边界；Secret、个人账号、Token、证书私钥和生产连接信息不得写入仓库。

| ADR | 状态 | 决策 |
| --- | --- | --- |
| [ADR-DEVOPS-001](./ADR-DEVOPS-001-RELEASE-AUTHORITY-AND-REGISTRY.md) | Accepted | 当前 CP6 候选继续由 GitHub R2 构建并写入 GHCR；Azure 只能读取同一候选做非权威影子验证 |

新的 Registry、第二套候选清单、版本入口或生产发布权威必须先新增或替代 ADR，不能在普通 YAML 实现票中隐式改变。
