# CP6 CRM 文档入口

| 文档 | 地位 | 用途 |
| --- | --- | --- |
| [CRM 产品框架](./CRM-PRODUCT-FRAMEWORK.md) | Approved normative product baseline | 产品定位、角色、渠道、旅程、V1/VNext、IA/UX、设计/采用门禁、KPI 和上线口径 |
| [CRM V1 可执行工程规格](./CRM-V1-EXECUTABLE-SPEC.md) | Approved implementation-planning baseline | 三仓架构、领域/数据/API/事件、安全、迁移、测试、发布权威、任务依赖和 DoD |
| [CRM V1 Foundation 基线](./CRM-V1-SPEC.md) | Historical foundation baseline | 2026-08-10 已实现的 20 表、状态机和菜单/权限范围 |

两份 Approved 文档冻结的是可拆票实施的产品和工程规划，不是开工 Owner、Pilot、UAT 或生产批准。当前只完成 Foundation 代码和规范 T1；API、Next.js、独立 CRM 数据库、Dapr/Kafka/YARP、RS256/JWKS、ERP 异步闭环、数据搬迁、采用门禁和生产发布均不能仅凭这些文档标记为完成。

下一张单任务票据是 M0/R00：在 DevOps ADR 中记录 CRM V1 已锁定的 GHCR/GitHub R2 唯一候选权威、Azure 非权威边界和回退；Sponsor、各 named Owner、Pilot cohort 与 Observation Gate 缺失时自动 No-Go。之后才按工程 Spec 的 Pilot 分阶段依赖创建三个仓库的实施票据。
