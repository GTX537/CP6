# CP6 SaaS M0 Readiness 公开镜像

<!-- crm-m0-status: No-Go -->

- Gate ID：`CP6-SAAS-M0`
- 状态：**NO-GO**
- 产品源：私有 `CP6-SAAS-V1` / `e210cb804d5b499e725c0ddeca84bb1157d09eb5304bc3b77b031142db84287b` / Frozen
- 发布源：私有 `CP6-SAAS-R00` / `64a53dd895aedc20a51288ad0ffdb69f60ddc7c22012c1df83984efba5adbc03` / Accepted
- 公开合同：[CP6 SaaS V1 公开工程契约](./CP6-SAAS-V1-PUBLIC-CONTRACT.md)
- 更新日期：2026-08-26

## 1. M0 的边界

M0 是“可以开始四仓基础实施”的合同门禁，不是云资源、Pilot、候选或上线门禁。M0 只冻结责任人、拓扑、账户/容量/身份、连续性目标、测试计划、证据格式、cohort 和固定任务 manifest。

M0 前不要求也不授权创建云资源、Secret、Service Connection、生产账号、候选、Tag、迁移或部署。真实资源、私网、恢复点、容量负载与季度恢复演练分别在 M2/M5/M6/CRM12 签署。

## 2. 人类批准与证据 DRI

`all_M0_human_approver_role_ids` 的唯一集合是 `{ ProgramOwner }`。ProgramOwner 必须批准当前决策摘要；公开镜像不复制个人审批身份。

专业角色负责评审或提供证据，但不构成独立人类签字门禁：SalesOperations 负责 Pilot/SLA/采用；Security/PrivacyLegal 负责身份、授权、租户/PII 与风险；FinanceCommerce 负责套餐、税务、支付与对账；ERP 负责 CP6 ERP、ExternalEvidence 与 C03；Data 负责 20 表迁移；SRE/Release 负责 SLO、连续性、候选与回退；Architecture 负责四仓、区域、数据库和跨服务合同。

Platform、DBA、CRM/Portal Engineering、Product Design、Mobile 和 QA 是对应 M1/M2/M4/M5/M6 的 DRI/reviewer。所有专业证据、自动化检查和真实环境门禁必须通过，ProgramOwner 不得豁免。

## 3. 决策清单

| ID | 决策/输入 | M0 要求 | 当前 |
| --- | --- | --- | --- |
| DEC-000 | 产品冻结摘要 | ProgramOwner 批准同一 digest；状态达到 Frozen | Approved |
| DEC-001 | 四仓边界、双区域和租户数据库拓扑 | Architecture 提供证据，ProgramOwner 批准合同与 owner | Pending |
| DEC-002 | R00 GHCR/GitHub R2 唯一候选权威 | Architecture/Release/Security 提供证据，ProgramOwner 批准 payload digest | Approved |
| DEC-003 | 身份/授权/Entitlement/支持访问 | Security/PrivacyLegal 提供合同与风险证据，ProgramOwner 接受 | Pending |
| DEC-004 | Commerce/账单/区域 PSP | FinanceCommerce 提供税务/对账责任证据，ProgramOwner 接受 | Pending |
| DEC-005 | CP6 ERP 与 ExternalEvidence | ERP 提供权威边界、C03 和验收数据计划，ProgramOwner 接受 | Pending |
| DEC-006 | SQL/备份/Emergency Intake 连续性 | SRE 提供拓扑、RPO/RTO、DRI、测试计划和 evidence schema，ProgramOwner 接受 | Pending |
| DEC-007 | 20 表迁移 | Data 提供 source inventory、映射、恢复副本和 cutover 计划，ProgramOwner 接受 | Pending |
| DEC-008 | Pilot/Adoption | SalesOperations 提供 cohort、固定 task manifest、评价口径与 canonical SQL/evidence 合同，ProgramOwner 接受 | Pending |
| DEC-009 | SLO/候选/发布职责 | SRE/Release 提供错误预算、候选、回退与签署流程，ProgramOwner 接受 | Pending |

## 4. 连续性与 Pilot 合同

M0 冻结但不创建区域/订阅/资源命名、组织数据库目录、elastic pool、托管身份、区域驻留和恢复合同。AZ 目标为已提交事务 RPO 0、RTO `≤15 min`；逻辑损坏 PITR 恢复点目标 `≤10 min`，季度实测恢复门禁 `≤4 h`。

Emergency Intake 只能是同源 BFF 后的受控加密 spool，保留 attempt/tenant/site/form/config/privacy/calendar/SLA anchor，不可人工改写，限时保留，恢复后幂等导入并 100% 对账；它永不成为 CRM/ERP 权威。

M0 的 Pilot 输入只包括固定 cohort、角色/部门范围、至少 120 个版本化任务、评价/失败整改口径、UTC 窗口、canonical SQL 和证据合同。真实 Dapr/Kafka、C03、隔离 ERP SQL、两租户环境、运行事件与性能 smoke 属于 M2，不参与 M0 关闭。

## 5. 关闭公式

```text
M0_GO =
  product_freeze_status == Frozen
  AND public_contract_sync == Complete
  AND approved_human_role_ids == { ProgramOwner }
  AND DEC_001_through_DEC_009 == Approved
  AND all_required_specialist_evidence == Passed
  AND R00_status == Accepted
  AND no_open_Critical_or_High == true
  AND branch_protection_and_required_checks == Enforced
```

任何条件缺失即 `NO-GO`。ProgramOwner 不得豁免专业证据、自动化、真实环境、租户/PII、迁移、性能、采用、发布完整性、Critical/High 清零、分支保护或必需检查。

## 6. 当前 No-Go 原因

- 产品摘要已 Frozen，R00 已 Accepted；这不满足其余 M0 条件。
- 本公开工程契约已由 ProgramOwner 对精确摘要批准并达到 Complete；这只关闭公开同步条件。
- DEC-001、DEC-003 至 DEC-009 和对应专业证据仍 Pending，SQL 容量模型与真实 Pilot cohort 均未冻结。
- 公开仓库已执行严格分支保护；私有 `CP6.CRM` 仍因 GitHub 账户方案限制无法启用 required checks，尚未形成可验证的强制证据。

这些 No-Go 原因不要求提前创建真实云资源或 Pilot 环境。`CP6.CRM` 仓库已存在也不解锁业务实现。
