# WP02-03 卡核销：当前授权与撤权最终门

结论：**卡 DONE**。依据 2026-11-17 用户决定，以 [task-definitions.json](../../../task-definitions.json) 的本卡定义核销；不更改已接受设计、原 AC、依赖或目标完成口径。分支 `codex/wp02-03`，固定 DEV-1，基线 Main `fc8b5a51da6f6fc8a8791a1f4bd7feb5fd3abb0d`。本 PR 只改治理、归属和状态记录，业务代码保持原状。

## 开头固定的卡出口

本卡 M，上限 4h，强制拆卡点 6h。直接前置 WP02-02 已 DONE，原卡核销和实际远端采用见 W674/W675/W676/W677。W699 的 PR #1236 已正常合并、准确分支删除和席位释放；本轮没有重复执行 W699。

1. 用户/部门/角色/委派 CAS 与权威版本；认证完成和 Owner 最终写入共用撤权 fence。
2. 全字段注册表先 MIN 既有授权再应用默认，query/write/export/download 分别判定。
3. 正向：合法主体获得当前授权，原结果披露按当前读权。反向：未知字段、撤权/写入竞争、过期委派不产生新效果，不能默认放宽权限。

## 原 AC 对照及证据核销

原断言来自 [SYS-IAM-01 原 AC](../../../assessments/20261011/catalogs/foundation.json)。下表核销其落在本卡定义中的责任；不把所有后续业务 DTO、CRM 消费或全链验收冒充本卡已经完成，也不把原目标的 NOT_RUN 批量改成 PASS。

| 本卡责任与映射原 AC | 已采用事实和实际证据 | 核销 |
|---|---|---|
| 用户分配、当前角色、版本/原结果；AC-IA01-01/02/04/06 | [用户分配 W28](../iam-user-assignments/DELIVERY.md)：真实 PostgreSQL/Program HTTP 主附角色、同租户引用、CAS/ABA、writer 根后当前权限、身份 Outbox/审计/回执同提交、提交确认丢失后原操作回查；[一致读 W18](../consistent-permission-read/DELIVERY.md) 单语句读取有效角色、动作/范围/字段，不读旧 cache 作允许 | 满足 |
| 部门树和范围的当前权威；AC-IA02-01～06 | [组织管理 W28](../organization-administration/DELIVERY.md)：实际子树原子移动、祖先健康、跨租户/循环/旧路径拒绝、CAS/ABA/412、审计回执失败回滚、历史审批不改写；原职责和 scope 使用当前来源 | 满足 |
| 角色/资源字段保存及旧语义；AC-IA03-03/04/07/08 | [角色 W29](../role-administration/DELIVERY.md)、[菜单动作 W30](../menu-action-administration/DELIVERY.md)、[一致读 W18](../consistent-permission-read/DELIVERY.md)：持久版本和有效角色，Scope MAX/custom4 保留原义；新字段协议与 legacy no-op/掩码/恢复原值明确区分，不混作同一合同 | 满足 |
| 完整字段域、MIN 后默认、四用途及依赖；AC-IA03-01/02/05/06/10 | [确定性决定](../field-decisions/DELIVERY.md)、[真实字段源 W31](../trusted-field-source/DELIVERY.md)：完整封存域、实际原行 MIN、缺项最后物化 1；未知字段/非法 Access/缺映射/缺依赖拒绝；独立 Read/Write/Export/Download 交集。[订单实际四用途 W34](../order-read-owner/DELIVERY.md)、[写入](../order-write-owner/DELIVERY.md)和 [W678–W685 当前消费索引](../../../authorization/current-main/README.md)提供已采用入口的实际证据 | 满足本卡共同决定和已采用映射；具体业务卡继续验证其新 DTO，不新增业务范围 |
| 认证完成/原会话与最终撤权门；AC-IA01-04、AC-IA03-09、AC-IA04-03 | [认证完成 W26](../authentication-entry-completion/DELIVERY.md)、[会话连续性](../authentication-session-continuity/DELIVERY.md)、[PGT-017/W645](../../W645-01/DELIVERY.md)：认证和真实 Owner/后台/CRM 共同门、真实锁序、暂停/撤权先胜拒绝、业务先提交保留原结果；不把 RR 快照或缓存 TTL 当最终门 | 满足 |
| 委派当前版本、期限和职责交集；本卡反向验收 | [W277](../../W277-01/DELIVERY.md)、[W305](../../W305-01/DELIVERY.md)：准确 grant、版本 CAS、半开 UTC 期限、单一完整授权和双方当前权限；真实 `TaskDelegationPostgreSqlTests` 验证过期 grant 选择/最终使用拒绝；原角色/职责不被代理身份替换 | 满足 |
| 当前读权披露原结果、输出不得默认放宽 | [W34](../order-read-owner/DELIVERY.md)、[W679](../../W679-01/DELIVERY.md)、[W682](../../W682-01/DELIVERY.md)、[W685](../../W685-01/DELIVERY.md)、[W699](../../W699-01/DELIVERY.md)：原结果不等于仍有披露权，字段、来源、对象及精确用途决定在当前入口执行；资格或业务撤销不改写已提交事实 | 满足 |

当前 `AuthorizationCatalogV1` 的封存、`CurrentFieldPermissionSourceReaderV1`、`FieldPermissionEvaluatorV1`、`CurrentPermissionContext`、`OperationsOwnerV1` 和相应测试已对照上述真实报告核实。82 资源/114 字段/2,141 MVC descriptor 是导航规模，不是通过项数；完整域/未知映射关闭由通用边界保证。尚未实现的新业务消费者仍受其业务卡的原 AC 约束，不能以 WP02-03 DONE 获得默认授权。

SYS-IAM-01 目标仍未关闭：其跨 CRM 消费、代表 Owner 事务及 WP14-08 原全链验收按所属卡继续。Space 专用 S5/61字段/78DTO 组合仍由 WP13-04 实际采用。保持原目标原 AC 状态和全部必需修订场景，不以本卡核销外推目标 DONE。

## 财务交付归回原卡

| 对应卡 | 本轮归属的已交付部分 | 继续保留的原业务出口 |
|---|---|---|
| WP11-39 | W689–W699 中现行成本政策/费率用版、专业资格、中心、标准 Basis/单据、完整 COST-POLICY-1 和原生政策来源 | 剩余 R2 费率/Basis 命令、维护与完整业务采用，严格按原卡验收 |
| WP11-14 | W699 原生会计政策和双方来源合同；W689 现行结转的保存版本绑定仅作旧实现背景 | GL 来源接收、完整 R-GL02、OA 应用、规则/映射/矩阵/期间及真正过账 |
| WP11-40 | W686–W689 的现行归集、完工反冲后采用和原计算快照，W693 标准依据、W698 质量规范引用 | MAT/EXEC/OUTPUT 完整原件及实际质量处置；规范批准不等于产出通过 |
| WP11-41 | 可复用 W689 已保存成本/费率版本作为输入；未交付完整 R2 分配 | 原分配方法、精度/残差、成本对象及相应原 AC 全部保留 |
| WP11-42 | W687/W688 的现行完工财务收尾与重启恢复、W689 已保存版结转绑定 | 账套级 GL 结转、C45 和正式恢复全部保留；不冒称原生 GL 已过账 |
| WP11-04 | W690–W692 的本地中心版本、专业资格、同事务制造采用 | 完整 FIN-MASTER/FM15 通用协议及其原控制/采用范围 |

六卡都记录为局部已交付，状态仍为原 PLANNED；前置图不变，未经前置 DONE 不继续实现。历史报告和源码不动，原窗口证据作为历史来源保留；WP02-03 的当前 remaining_scope 清空，不再承担这些业务出口。归属记录不把未执行的业务 AC 转成 PASS。

## 验证、审查及执行规则

本轮无产品、依赖、数据库或接口改动；相关权限/事务/精度专项结果按各原报告的真实 SHA、环境和输入复用，不称重新运行。实际执行仅有原报告/源码对照、直接前置及远端提交核对、文档链接、台账结构和生成视图一致性检查。PGT 27/27 与原询盘 60/60 未重跑，原失败日志未改写。任务级集中审查核对范围归属、原 AC 不缩减、依赖未变、财务代码未变和报告事实。

[新执行计划](../../../planning/20261117-card-execution.md)与 AGENTS 同 PR 保存：单卡串行、4h/8h 和 150% 拆卡、严格前置、一卡一 PR、24h 有效时间检查点及 2,503.55h 总预算。当前只关闭 WP02-03；其他卡逐张独立 PR，不在此 PR 顺手关闭。没有冻结模块、删除设计或降级目标出口。

开工前工具识别三个历史 WP02-03 分支；逐个核实精确 tip 均已在远端 main，无开放 PR、占用或活动依赖后，按既有授权条件删除准确本卡引用，未重写任何提交。正常 start 建立唯一 `codex/wp02-03`。本次 PR 正常合并后，用官方 finish 核对远端采用、定向处理分支、检查干净工作区并释放 DEV-1；实际合并与释放以 PR/finish 为准。

Actions 触发/取消/配置变更和生产部署均为零；工作流内容及触发依赖未变，只有版本 tag 可触发 R2 候选。下一卡按用户阶段 0 序列为 WP02-10，在本卡远端 DONE 后再开工。

本卡有效工作区间：2026-11-17T11:43:54.964043+00:00 至 2026-11-17T11:56:25.010116+00:00，核销时计 0.208h；未到强制拆卡点，新增拆卡 0。本计划累计关闭 1 卡、0 目标；合并/席位收尾时间在下一次检查点接续计入，不重复计历史 W699。
