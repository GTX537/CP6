# CRM V1 M0 开工就绪与 No-Go 清单

- 状态：**NO-GO**
- 截止基线：`main@57f0199ab014ea5d8b09939c0421de6f771943f3`
- 更新日期：2026-08-13
- 规范来源：[CRM V1 可执行工程规格](./CRM-V1-EXECUTABLE-SPEC.md)
- 发布决策：[ADR-CRM-R00](../devops/adr/ADR-CRM-R00-RELEASE-AUTHORITY.md)

## 1. 当前结论

T1 已进入 `main`，R00 已把 GHCR/GitHub R2 唯一权威和 Azure 非权威边界写成 Proposed ADR；但所有 named approval、DEC-CRM-002–007、真实 Pilot/Observation 输入、Azure SQL/Emergency Intake 环境证据仍未完成。因此 M0 明确为 **NO-GO**。

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

每项输入必须有 Owner、backup、target date、内容 digest、非 Secret 的不可变 evidence URI、Approver、决定和 ApprovedAtUtc。Secret、连接串、Token、私钥、Cookie、原始 PII 与生产数据不得写入 Git 或本清单。

## 3. M0 决策登记册

| ID | Owner | 必需输出 | 当前状态 | 退出条件 |
| --- | --- | --- | --- | --- |
| DEC-CRM-001 | Release Owner | GHCR/R2 权威、Azure 边界、System Manifest 与等价矩阵 | Pending | [R00 ADR](../devops/adr/ADR-CRM-R00-RELEASE-AUTHORITY.md) 获全部必需批准 |
| DEC-CRM-002 | Platform Owner | 私有 NuGet 源、source mapping、签名、保留与撤回 | Pending | 包源 ADR + 消费/撤回演练获批 |
| DEC-CRM-003 | Product Owner | 定位、角色、V1/VNext、KPI | Pending | 产品框架逐项签收并引用固定 digest |
| DEC-CRM-004 | Data Owner | 20 表列合同、转换、时区与保留 | Pending | migration map、列合同、golden vectors 获批 |
| DEC-CRM-005 | ERP Owner | BP/Quotation/Order 契约、幂等和错误码 | Pending | OpenAPI/Event Schema 由生产者和消费者签收 |
| DEC-CRM-006 | Security Owner | RS256/JWKS、租户、PII、DataScope 与威胁模型 | Pending | 安全 ADR、负向测试计划和例外规则获批 |
| DEC-CRM-007 | SRE Owner | SLO、容量、告警、错误预算与故障演练 | Pending | 负载配置、Dashboard/Alert 和恢复计划获批 |

`DEC-CRM-008` 是 T-7/T-1/生产 Go/No-Go 与采用关闭决策，不是 M0 关闭项；它必须在后续每次推广和 Epic 关闭前重新批准。

## 4. Named 责任人与开工输入

实名与 backup 账号保存在访问受控的项目系统记录中；本清单只记录其不可变引用和状态。不得由实现者代选、猜测或以团队群组冒充唯一负责人。

| 角色 | 责任 | M0 硬门禁 | 当前状态/受控记录 |
| --- | --- | --- | --- |
| Sponsor | 目标、预算、跨部门阻断升级 | 是 | Pending / Required |
| Product Owner | 产品范围、KPI、采用门禁 | 是 | Pending / Required |
| Sales Operations Owner | Lead 口径、SLA、cohort 与 Observation | 是 | Pending / Required |
| System Architect | 三仓边界、同步/异步与回退 | 是 | Pending / Required |
| Platform Owner | DEC-CRM-002、P01–P10、合同和 System Manifest | 是 | Pending / Required |
| Security Owner | 身份、租户、PII、供应链与威胁门禁 | 是 | Pending / Required |
| ERP Owner | DEC-CRM-005、C03 与真实隔离 ERP UAT | 是 | Pending / Required |
| Data Owner | DEC-CRM-004、迁移源、列合同、对账和保留 | 是 | Pending / Required |
| DBA Owner | Azure SQL、恢复副本和 migration 运维 | 是 | Pending / Required |
| SRE Owner | DEC-CRM-007、SLO、容量、连续性与 Runbook | 是 | Pending / Required |
| Release Owner | DEC-CRM-001、候选、推广与 Go/No-Go | 是 | Pending / Required |
| CRM Engineering Owner | CRM01–CRM12 交付与依赖 | 否；CRM01 前阻断 | Pending / Required before CRM01 |
| Product/Design Owner | Pilot IA、公开站点和高保真门禁 | 否；CRM04/CRM09 前阻断 | Pending / Required before CRM04/CRM09 |
| QA Owner | 测试矩阵、证据格式和失败关闭 | 否；M1/Pilot 前阻断 | Pending / Required before M1/Pilot |

## 5. Azure SQL 与 Emergency Intake 合同确认

本节只冻结必须由 SRE/DBA/Security/Release Owner 绑定到真实资源和演练证据的合同，不表示资源已创建或平台能力已验收。

| 合同项 | 锁定值 | 当前状态/证据 |
| --- | --- | --- |
| 生产数据库 | Azure SQL Database General Purpose vCore standard-series、zone redundant | Pending：订阅/区域/SKU/容量/网络引用 |
| 自动备份 | GZRS，PITR 保留 35 天 | Pending：Policy 与恢复点证据 |
| 单节点/AZ 故障 | 已提交数据 RPO=0 目标；外部探针发现至 readiness + 管理读写 + 公开回执 + 两租户冒烟 RTO ≤30 分钟 | Pending：季度故障演练 |
| 逻辑损坏/PITR | 可选恢复点距声明时间 ≤10 分钟；同规模季度恢复 RTO ≤4 小时 | Pending：季度实测；这是内部门禁，不得表述为 Azure 平台保证 |
| Emergency Intake 存储 | Azure Storage ZRS 私有 Blob 保存不可变加密 envelope，Queue 仅保存指针 | Pending：资源与不可变/私网证据 |
| 访问与加密 | private endpoint、workload identity、Key Vault envelope encryption、轮换 HMAC | Pending：身份/权限/轮换/篡改证据 |
| Envelope | 原 attempt id、tenant/site/form、隐私版本、ReceivedAtUtc、payload hash 与密文 | Pending：Schema 与负向测试 |
| 容量 | 实测四小时峰值 +25%；80% 告警，95% 后中性 503 | Pending：容量模型与压测 |
| 保留与恢复 | 7 天；不可人工编辑；按原 attempt 幂等导入并重新风险判断；Blob/Queue/CRM 数量与 hash 100% 对账后删除 | Pending：恢复演练 |
| 权威/SLO | spool 永不成为 CRM/ERP 权威或第二浏览器写路径；期间错误和延迟仍计入 99.9% 与 Adoption | Pending：路由、告警和证据查询 |

## 6. Observation、Pilot cohort 与任务清单

### 6.1 Observation Gate

| 输入 | 固定最小值 | 当前状态 |
| --- | --- | --- |
| 定性观察 | 3 人、15 条真实 Lead，使用冻结观察脚本 | Pending |
| 脱敏定量基线 | 8 人、2 个部门、100 个事件、10 个工作日 | Pending |
| 输出 | 摩擦排序、当前基线、不可事后修改的 Pilot task manifest | Pending |

Observation 数据必须脱敏，PII 不得进入录屏标题、分析事件、截图、日志或证据包。若样本、时长、部门或事件量不足，不能由访谈印象补齐。

### 6.2 Pilot 开工输入

| 输入 | 必需内容 | 当前状态 |
| --- | --- | --- |
| Cohort | 8–12 名销售、2 部门、至少 2 名主管、named backup | Pending |
| Task manifest | ≥120 个固定任务且每人 ≥10，预标 normal/reject/recovery | Pending |
| 数据与环境 | 两租户、真实 Dapr/Kafka、真实 C03 handler、隔离 ERP SQL、PII 安全数据集 | Pending |
| 评价规则 | 正常无引导完成率、时长、拒绝/恢复、正确性、隔离和 defect severity 口径 | Pending |
| 证据位置 | 加密、版本化、Object Lock 的不可变 URI 与 digest | Pending |

## 7. M0 关闭公式与硬停止条件

只有以下表达式为真时，Release Owner 才能把状态改为 `GO / Approved`：

```text
T1_in_latest_main
AND DEC-CRM-001..007 == Approved
AND all_M0_hard_gate_named_roles == Approved
AND Azure_SQL_Emergency_Intake_contract == Approved
AND Observation_Gate == Approved
AND Pilot_cohort_and_task_manifest == Approved
AND no_expired_or_unreadable_evidence
AND no_unapproved_scope_exception
```

当前 No-Go 原因：

1. R00 仍是 Proposed，所有 named approver 记录缺失；
2. DEC-CRM-002–007 尚无批准证据；
3. Sponsor 与全部 M0 硬门禁角色的实名/backup 责任记录未冻结；
4. Azure SQL/Emergency Intake 尚未绑定真实环境、容量和演练证据；
5. Observation、Pilot cohort 和固定任务 manifest 均未完成；

因此 M0 保持 No-Go。P01 runner/合同是 M0 关闭后的独立上游交付；在 M0 Approved 与 P01 可消费两项同时满足前，`CRM01-S01`、CRM 仓创建和业务实现保持锁定。

## 8. 更新和审计流程

1. Owner 提交固定输入、内容 digest 和不可变 evidence URI；
2. Reviewer 验证角色、范围、数据保护、证据可读性与到期日；
3. Approver 在受控项目系统记录决定和 UTC 时间；
4. 文档任务只更新状态与引用，不改写历史批准；内容变化时生成新 digest 并使旧批准 Expired；
5. Release Owner 重算第 7 节公式。任一项失败即保持 No-Go；
6. M0 获批后仍需先取得可消费的 P01 runner/合同，才可由独立 `CRM01-S01` 任务创建私有 CRM 仓。
