# CP6 CRM 文档入口

| 文档 | 地位 | 用途 |
| --- | --- | --- |
| [CRM 产品框架](./CRM-PRODUCT-FRAMEWORK.md) | Approved normative product baseline | 产品定位、角色、渠道、旅程、V1/VNext、IA/UX、设计/采用门禁、KPI 和上线口径 |
| [CRM V1 可执行工程规格](./CRM-V1-EXECUTABLE-SPEC.md) | Approved implementation-planning baseline | 三仓架构、领域/数据/API/事件、安全、迁移、测试、发布权威、任务依赖和 DoD |
| [CRM M0 开工就绪清单](./CRM-M0-READINESS.md) | NO-GO | DEC-CRM-001–007、named Owner、Azure SQL/Emergency Intake、Observation 与 Pilot 输入 |
| [ADR-CRM-R00 发布权威](../devops/adr/ADR-CRM-R00-RELEASE-AUTHORITY.md) | Proposed | GHCR/GitHub R2 唯一权威、Azure 边界、System Release Manifest、回退和等价缺口 |
| [CRM V1 Foundation 基线](./CRM-V1-SPEC.md) | Historical foundation baseline | 2026-08-10 已实现的 20 表、状态机和菜单/权限范围 |

两份 Approved 文档冻结的是可拆票实施的产品和工程规划，不是开工 Owner、Pilot、UAT 或生产批准。当前只完成 Foundation 代码和规范 T1；API、Next.js、独立 CRM 数据库、Dapr/Kafka/YARP、RS256/JWKS、ERP 异步闭环、数据搬迁、采用门禁和生产发布均不能仅凭这些文档标记为完成。V1 仍是包装/制造行业售前工作台，不包含软件商城、产品订阅或登录后客户产品中心。

R00 文档票已把 CRM V1 锁定的 GHCR/GitHub R2 唯一候选权威、Azure 非权威边界和系统级回退写入 Proposed ADR，并建立 M0 就绪清单。当前 M0 明确为 No-Go：DEC-CRM-001 仍缺对固定 `decisionPayloadSha256` 的 named approval，DEC-CRM-002–007、八类 M0 硬角色、Pilot cohort/task manifest、Observation Gate 和 Azure SQL/Emergency Intake 开工合同均未关闭。真实云资源、Dapr/Kafka/C03/隔离 ERP 和运行演练属于 M2/M6/CRM12，不是 M0 前置。`GTX537/CP6.CRM` 不在此阶段提前建立，只在 M0 全部 Approved 且后续 P01 runner/合同可消费后由独立 `CRM01-S01` 创建。
