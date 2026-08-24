# CP6 CRM 文档入口

| 文档 | 地位 | 用途 |
| --- | --- | --- |
| [CRM V1 产品需求文档](./CRM-V1-PRD.md) | Draft for Product Review | 对齐 2026-08-14 Frozen SaaS V1，定义前端效果、后端逻辑、状态/权限/失败语义、验收和升级边界 |
| [CRM 产品框架](./CRM-PRODUCT-FRAMEWORK.md) | Approved normative product baseline | 产品定位、角色、渠道、旅程、V1/VNext、IA/UX、设计/采用门禁、KPI 和上线口径 |
| [CRM V1 可执行工程规格](./CRM-V1-EXECUTABLE-SPEC.md) | Approved implementation-planning baseline | 三仓架构、领域/数据/API/事件、安全、迁移、测试、发布权威、任务依赖和 DoD |
| [CRM V1 Foundation 基线](./CRM-V1-SPEC.md) | Historical foundation baseline | 2026-08-10 已实现的 20 表、状态机和菜单/权限范围 |

当前公开仓的两份 Approved 文档记录 2026-08-13 三仓规划；2026-08-14 私有 `GTX537/CP6.CRM` 的 Frozen SaaS V1 已扩展为 CP6、Platform、CRM、Portal 四仓边界。上表 PRD 是两代范围的产品对齐草案，不在评审和 Public Contract Sync 完成前覆盖旧 Approved 文档，也不构成开工、Pilot、UAT 或生产批准。遇到范围冲突时必须先关闭公开同步差异，不得选择性实施。

当前 `main` 只完成 Foundation 的 20 表、状态机、迁移、6 个禁用菜单和 22 个动作；没有 CRM Controller、独立 CRM API、Next.js/React Native 客户端或可用 CRM 路由。私有 `GTX537/CP6.CRM` 已存在，但目前只有产品、系统和交付文档，不能视为应用代码已开工。

下一张治理票仍是 M0/R00：记录 GHCR/GitHub R2 唯一候选权威、Azure 非权威边界和系统级回退，并冻结 Azure SQL/Emergency Intake 合同。Sponsor、named Owner、Pilot cohort、Observation Gate、Public Contract Sync 或非豁免硬门禁缺失时保持 No-Go。
