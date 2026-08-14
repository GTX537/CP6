# CP6 CRM 文档入口

| 文档 | 地位 | 用途 |
| --- | --- | --- |
| [CP6 SaaS V1 公开工程契约](./CP6-SAAS-V1-PUBLIC-CONTRACT.md) | Candidate public synchronization contract | 脱敏同步私有 Frozen 产品与 Accepted R00 的四仓边界、领域/API/事件、安全、发布和 M0 开工规则 |
| [CRM M0 Readiness](./CRM-M0-READINESS.md) | No-Go gate mirror | DEC-000 至 DEC-009、唯一 ProgramOwner 模型、专业证据、分支保护和开工关闭公式 |
| [CRM R00 发布权威镜像](../devops/adr/ADR-CRM-R00-RELEASE-AUTHORITY.md) | Private source Accepted / public mirror Candidate | GHCR/GitHub R2 唯一权威、CandidateLocator、精确对象身份、四仓 Manifest 和回退 |
| [CRM 产品框架](./CRM-PRODUCT-FRAMEWORK.md) | Historical planning baseline | 2026-08-11 至 08-13 的产品规划输入；不再作为新实施权威 |
| [CRM V1 可执行工程规格](./CRM-V1-EXECUTABLE-SPEC.md) | Historical planning baseline | 原三仓可执行计划；最终四仓合同以公开工程契约为准 |
| [CRM V1 Foundation 基线](./CRM-V1-SPEC.md) | Historical Foundation baseline | 2026-08-10 已合入 CP6 单体的 20 表、状态机和菜单/权限迁移源 |

私有 `GTX537/CP6.CRM` 已在合并提交 `07a7bb0b50f33b0cb70c18c14f83be77c725626d` 冻结产品摘要 `e210cb804d5b499e725c0ddeca84bb1157d09eb5304bc3b77b031142db84287b`，并接受 R00 摘要 `64a53dd895aedc20a51288ad0ffdb69f60ddc7c22012c1df83984efba5adbc03`。私有仓库已建立为 Private，但没有业务实现、云资源、Secret、数据库、迁移或部署。

当前公开工程契约仍为 Candidate，M0 继续 No-Go。下一步是由唯一 `ProgramOwner` 批准公开契约的当前摘要并以独立历史证据将同步改为 Complete；随后回写私有聚合记录，再逐项关闭 DEC-001、DEC-003 至 DEC-009、专业证据、Critical/High、分支保护和必需检查。只有 M0 Go 后才解锁 `CRM01` 业务脚手架。
