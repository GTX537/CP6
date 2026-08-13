# CRM V1 M0 开工就绪与 No-Go 清单

- 状态：**NO-GO**
- 截止基线：`main@57f0199ab014ea5d8b09939c0421de6f771943f3`
- 更新日期：2026-08-13
- 规范来源：[CRM V1 可执行工程规格](./CRM-V1-EXECUTABLE-SPEC.md)
- 发布决策：[ADR-CRM-R00](../devops/adr/ADR-CRM-R00-RELEASE-AUTHORITY.md)

## 1. 当前结论

T1 已进入 `main`，R00 已把 GHCR/GitHub R2 唯一权威和 Azure 非权威边界写成 Proposed ADR；但所有 named approval、DEC-CRM-002–007、Observation 证据、Pilot cohort/task manifest，以及 Azure SQL/Emergency Intake 开工合同仍未完成。因此 M0 明确为 **NO-GO**。

在本清单变为 `GO / Approved` 且 P01 runner/合同可消费前：

- 不创建 `GTX537/CP6.CRM` 空仓；
- 不启动 `CRM01-S01`，不写 CRM 业务代码或数据库 migration；
- 不创建云资源、Secret、Service Connection、候选、Tag 或部署；
- 只允许继续准备受控批准记录、观察材料、合同证据和可审阅任务卡。

## 2. 状态与证据规则

| 状态 | 判定 |
| --- | --- |
| Pending | 尚未提交、证据不可读取、审批角色不匹配或内容未冻结 |
| Ready for approval | 输入完整且可复核，尚缺最后批准 |
| Approved | named approver 对固定内容 digest 作出有效批准并记录 UTC 时间 |
| Rejected | 输入被拒绝；对应下游保持锁定 |
| Expired | 批准超过适用期或内容 digest 已变化，等同 Pending |

每项输入必须有 Owner、backup、target date、内容 digest、非 Secret 的精确 evidence object identity（URI/对象版本/内容摘要）、Approver、决定和 ApprovedAtUtc。Secret、连接串、Token、私钥、Cookie、原始 PII 与生产数据不得写入 Git 或本清单。

## 3. M0 决策登记册

| ID | Owner | 必需输出 | 当前状态 | 退出条件 |
| --- | --- | --- | --- | --- |
| DEC-CRM-001 | Release Owner | GHCR/R2 权威、Azure 边界、System Manifest 与等价矩阵 | Pending | [R00 ADR](../devops/adr/ADR-CRM-R00-RELEASE-AUTHORITY.md) 的固定 `decisionPayloadSha256` 获全部必需批准，且权威状态记录为 Accepted |
| DEC-CRM-002 | Platform Owner | 私有 NuGet 源、source mapping、签名、保留与撤回 | Pending | 包源 ADR + 消费/撤回演练获批 |
| DEC-CRM-003 | Product Owner | 定位、角色、V1/VNext、KPI | Pending | 产品框架逐项签收并引用固定 digest |
| DEC-CRM-004 | Data Owner | 20 表列合同、转换、时区与保留 | Pending | migration map、列合同、golden vectors 获批 |
| DEC-CRM-005 | ERP Owner | BP/Quotation/Order 契约、幂等和错误码 | Pending | OpenAPI/Event Schema 由生产者和消费者签收 |
| DEC-CRM-006 | Security Owner | RS256/JWKS、租户、PII、DataScope 与威胁模型 | Pending | 安全 ADR、负向测试计划和例外规则获批 |
| DEC-CRM-007 | SRE Owner | SLO、容量、告警、错误预算与故障演练计划 | Pending | 负载配置、Dashboard/Alert 和恢复计划获批 |

`DEC-CRM-008` 是 T-7/T-1/生产 Go/No-Go 与采用关闭决策，不是 M0 关闭项；它必须在后续每次推广和 Epic 关闭前重新批准。

## 4. Named 责任人与开工输入

实名与 backup 账号保存在访问受控的项目系统记录中；本清单只记录其不可变引用和状态。不得由实现者代选、猜测或以团队群组冒充唯一负责人。

`M0_named_roles` 的唯一集合固定为：`Sponsor`、`ProductOwner`、`SalesOperationsOwner`、`SecurityOwner`、`ERPOwner`、`DataOwner`、`SREOwner`、`ReleaseOwner`。其他角色仍按下表参与 R00、DEC 或后续里程碑，但不是该公式中的独立角色项。

| 角色 | 责任 | M0 硬门禁 | 当前状态/受控记录 |
| --- | --- | --- | --- |
| Sponsor | 目标、预算、跨部门阻断升级 | 是 | Pending / Required |
| Product Owner | 产品范围、KPI、采用门禁 | 是 | Pending / Required |
| Sales Operations Owner | Lead 口径、SLA、cohort 与 Observation | 是 | Pending / Required |
| System Architect | 三仓边界、同步/异步与回退 | 否；通过 R00/DEC-CRM-001 阻断 | Pending / Required before ADR Accepted |
| Platform Owner | DEC-CRM-002、P01–P10、合同和 System Manifest | 否；通过 DEC-CRM-002/P01 阻断 | Pending / Required before DEC-CRM-002/P01 |
| Security Owner | 身份、租户、PII、供应链与威胁门禁 | 是 | Pending / Required |
| ERP Owner | DEC-CRM-005、C03 与真实隔离 ERP UAT | 是 | Pending / Required |
| Data Owner | DEC-CRM-004、迁移源、列合同、对账和保留 | 是 | Pending / Required |
| DBA Owner | Azure SQL、恢复副本和 migration 运维 | 否；资源创建/CRM11 前阻断 | Pending / Required before resource creation/CRM11 |
| SRE Owner | DEC-CRM-007、SLO、容量、连续性与 Runbook | 是 | Pending / Required |
| Release Owner | DEC-CRM-001、候选、推广与 Go/No-Go | 是 | Pending / Required |
| CRM Engineering Owner | CRM01–CRM12 交付与依赖 | 否；CRM01 前阻断 | Pending / Required before CRM01 |
| Product/Design Owner | Pilot IA、公开站点和高保真门禁 | 否；CRM04/CRM09 前阻断 | Pending / Required before CRM04/CRM09 |
| QA Owner | 测试矩阵、证据格式和失败关闭 | 否；M1/Pilot 前阻断 | Pending / Required before M1/Pilot |

## 5. Azure SQL 与 Emergency Intake 合同确认

本节只批准 M0 开工所需的拓扑、账户归属、容量模型、身份、连续性目标、DRI、测试计划和证据格式，不表示资源已创建或平台能力已验收。真实资源、Policy、私网、恢复点、容量压测和故障/恢复演练证据分别在 M2、M6/CRM12 或生产门禁失败关闭；它们不参与 M0 关闭公式。

| 合同项 | 锁定值 | M0 批准证据 | 后续运行证据门禁 |
| --- | --- | --- | --- |
| 生产数据库 | Azure SQL Database General Purpose vCore standard-series、zone redundant | Pending：订阅/区域/SKU/容量/网络拓扑、预算与 DRI 决策记录；不要求实例已创建 | P08/CRM12：真实资源、网络和 readiness 证据 |
| 自动备份 | GZRS，PITR 保留 35 天 | Pending：Policy 目标、Owner、验证步骤和证据 Schema | CRM12：Policy、恢复点与同规模恢复证据 |
| 单节点/AZ 故障 | 已提交数据 RPO=0 目标；外部探针发现至 readiness + 管理读写 + 公开回执 + 两租户冒烟 RTO ≤30 分钟 | Pending：故障模型、探针、计时边界、Runbook、DRI 和演练计划 | CRM12/生产：季度故障演练 |
| 逻辑损坏/PITR | 可选恢复点距声明时间 ≤10 分钟；同规模季度恢复 RTO ≤4 小时 | Pending：恢复点口径、计时边界、失败处理、DRI 和证据格式；不得表述为 Azure 平台保证 | CRM12/生产：同规模季度实测 |
| Emergency Intake 存储 | Azure Storage ZRS 私有 Blob 保存不可变加密 envelope，Queue 仅保存指针 | Pending：账户命名/归属、容量、网络拓扑、保留和 DRI 合同 | M2/CRM12：真实资源、不可变和私网证据 |
| 访问与加密 | private endpoint、workload identity、Key Vault envelope encryption、轮换 HMAC | Pending：身份/权限模型、密钥轮换合同、威胁用例和证据格式 | M2/CRM12：真实身份、权限、轮换和篡改证据 |
| Envelope | 原 attempt id、tenant/site/form、隐私版本、ReceivedAtUtc、payload hash 与密文 | Pending：Schema、版本策略、负向测试计划和 Owner | M2：实现后的 Schema/负向测试证据 |
| 容量 | 四小时峰值模型 +25%；80% 告警，95% 后中性 503 | Pending：容量假设、流量来源、阈值、Owner 和压测方案 | M2/CRM12：真实环境容量压测 |
| 保留与恢复 | 7 天；不可人工编辑；按原 attempt 幂等导入并重新风险判断；Blob/Queue/CRM 数量与 hash 100% 对账后删除 | Pending：恢复算法、对账查询、删除条件、Runbook 和证据格式 | CRM12：端到端恢复演练 |
| 权威/SLO | spool 永不成为 CRM/ERP 权威或第二浏览器写路径；期间错误和延迟仍计入 99.9% 与 Adoption | Pending：路由/告警/证据查询合同、DRI 和失败处理 | M2/CRM12/生产：真实路由、告警和证据查询 |

## 6. Observation、Pilot cohort 与任务清单

### 6.1 Observation Gate

| 输入 | 固定最小值 | 当前状态 |
| --- | --- | --- |
| 定性观察 | 3 人、15 条真实 Lead，使用冻结观察脚本 | Pending |
| 脱敏定量基线 | 8 人、2 个部门、100 个事件、10 个工作日 | Pending |
| 输出 | 摩擦排序、当前基线、不可事后修改的 Pilot task manifest | Pending |

Observation 数据必须脱敏，PII 不得进入录屏标题、分析事件、截图、日志或证据包。若样本、时长、部门或事件量不足，不能由访谈印象补齐。

### 6.2 M0 冻结的 Pilot 输入

| 输入 | 必需内容 | 当前状态 |
| --- | --- | --- |
| Cohort | 8–12 名销售、2 部门、至少 2 名主管、named backup | Pending |
| Task manifest | ≥120 个固定任务且每人 ≥10，预标 normal/reject/recovery | Pending |
| 评价规则 | 正常无引导完成率、时长、拒绝/恢复、正确性、隔离和 defect severity 口径 | Pending |
| 证据合同 | 加密、版本化、Object Lock、精确对象版本、内容 digest、保留和访问规则 | Pending |

本节只冻结参与者、任务和评价/证据合同。它不要求 M0 前存在 C03 handler、Dapr/Kafka、隔离 ERP SQL 或 Pilot 运行结果。

### 6.3 M2 Pilot 运行门禁

| 输入 | 必需内容 | 时点 |
| --- | --- | --- |
| 数据与环境 | 两租户、真实 Dapr/Kafka、真实 C03 handler、隔离 ERP SQL、PII 安全数据集 | M1 交付后、Pilot 执行前 |
| 运行证据 | ≥120 个固定任务的实际结果、拒绝/恢复、隔离、性能 Smoke 与缺陷状态 | M2 Pilot 签收 |
| 证据对象 | 精确对象版本、内容 digest、保留策略和受控访问可验证 | M2 Pilot 签收 |

上述任一运行门禁失败时不得签收 M2，但不追溯否定已按合同关闭的 M0。

## 7. M0 关闭公式与硬停止条件

只有以下表达式为真时，Release Owner 才能把状态改为 `GO / Approved`：

```text
T1_in_latest_main
AND DEC-CRM-001..007 == Approved
AND M0_named_roles[Sponsor,ProductOwner,SalesOperationsOwner,SecurityOwner,ERPOwner,DataOwner,SREOwner,ReleaseOwner] == Approved
AND Azure_SQL_Emergency_Intake_contract == Approved
AND Observation_Gate == Approved
AND Pilot_cohort_task_evaluation_evidence_contract == Approved
AND no_expired_or_unreadable_evidence
AND no_unapproved_scope_exception
```

当前 No-Go 原因：

1. R00 仍是 Proposed，所有 named approver 记录缺失；
2. DEC-CRM-002–007 尚无批准证据；
3. 八类 M0 硬门禁角色的实名/backup 责任记录未冻结；
4. Azure SQL/Emergency Intake 的拓扑、账户/容量/身份、连续性、DRI、测试计划和证据格式合同尚未批准；
5. Observation、Pilot cohort、固定任务 manifest、评价规则和证据合同均未完成；

因此 M0 保持 No-Go。P01 runner/合同是 M0 关闭后的独立上游交付；在 M0 Approved 与 P01 可消费两项同时满足前，`CRM01-S01`、CRM 仓创建和业务实现保持锁定。

## 8. 更新和审计流程

1. Owner 提交固定输入、内容 digest 和精确 evidence object identity；
2. Reviewer 验证角色、范围、数据保护、证据可读性与到期日；
3. Approver 在受控项目系统记录决定和 UTC 时间；
4. 文档任务只更新状态与引用，不改写历史批准；内容变化时生成新 digest 并使旧批准 Expired；
5. Release Owner 重算第 7 节公式。任一项失败即保持 No-Go；
6. M0 获批后仍需先取得可消费的 P01 runner/合同，才可由独立 `CRM01-S01` 任务创建私有 CRM 仓。
