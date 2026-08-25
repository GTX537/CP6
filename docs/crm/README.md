# CP6 CRM 文档入口

| 文档 | 地位 | 用途 |
| --- | --- | --- |
| [CP6 SaaS V1 公开工程契约](./CP6-SAAS-V1-PUBLIC-CONTRACT.md) | Complete public synchronization contract | 脱敏同步私有 Frozen 产品与 Accepted R00 的四仓边界、领域/API/事件、安全、发布和 M0 开工规则 |
| [CRM V1 产品需求文档](./CRM-V1-PRD.md) | Draft for Product Review | 对齐 Frozen SaaS V1，定义前端效果、后端逻辑、状态/权限/失败语义、验收和升级边界 |
| [公开产品对比与业务决策基线](./CRM-COMPETITIVE-ANALYSIS.md) | Product research baseline | 对比 9 个公开 CRM 产品，归纳市场分型、CRM 业务主链、CP6 取舍、V1/VNext 和商业验证假设 |
| [CRM M0 Readiness](./CRM-M0-READINESS.md) | No-Go gate mirror | DEC-000 至 DEC-009、唯一 ProgramOwner 模型、专业证据、分支保护和开工关闭公式 |
| [CRM R00 发布权威镜像](../devops/adr/ADR-CRM-R00-RELEASE-AUTHORITY.md) | Private source Accepted / public mirror Complete | GHCR/GitHub R2 唯一权威、CandidateLocator、精确对象身份、四仓 Manifest 和回退 |
| [CRM 产品框架](./CRM-PRODUCT-FRAMEWORK.md) | Historical planning baseline | 2026-08-11 至 08-13 的产品规划输入；不再作为新实施权威 |
| [CRM V1 可执行工程规格](./CRM-V1-EXECUTABLE-SPEC.md) | Historical planning baseline | 原三仓可执行计划；最终四仓合同以公开工程契约为准 |
| [CRM V1 Foundation 基线](./CRM-V1-SPEC.md) | Historical Foundation baseline | 2026-08-10 已合入 CP6 单体的 20 表、状态机和菜单/权限迁移源 |

私有 `GTX537/CP6.CRM` 已在合并提交 `07a7bb0b50f33b0cb70c18c14f83be77c725626d` 冻结产品摘要 `e210cb804d5b499e725c0ddeca84bb1157d09eb5304bc3b77b031142db84287b`，并接受 R00 摘要 `64a53dd895aedc20a51288ad0ffdb69f60ddc7c22012c1df83984efba5adbc03`。私有仓库已建立为 Private，但没有业务实现、云资源、Secret、数据库、迁移或部署。

当前公开工程契约已由唯一 `ProgramOwner` 对摘要 `8950c63c9ed37d01a8c39c4e7df9267e69596057340eb48fbd668049eeca06d9` 批准并同步为 Complete，审批证据固定在 [append-only 历史记录](./approvals/history/2026-08-26-cp6-saas-v1-public-contract-program-owner.json)。M0 继续 No-Go；下一步是回写私有聚合记录，再逐项关闭 DEC-001、DEC-003 至 DEC-009、专业证据、Critical/High、分支保护和必需检查。只有 M0 Go 后才解锁 `CRM01` 业务脚手架。

当前 PRD 是对 Frozen SaaS V1 与已完成公开合同的产品对齐草案；在产品评审形成不可变证据前，不构成开工、Pilot、UAT 或生产批准。当前 `main` 仍只有 Foundation 的 20 表、状态机、迁移、6 个禁用菜单和 22 个动作；没有 CRM Controller、独立 CRM API、Next.js/React Native 客户端或可用 CRM 路由。
