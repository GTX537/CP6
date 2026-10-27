# W179-01 — 原固定审批版本的原子启动

COORD单人继续CP6-7D-20261022，Main基线`dd434bcd6777ef29caf8d00d2a8e73d00adf22fd`；固定DEV-1、Handoff固定DEV-2。采用原OA-APP oa.app/2.0 §6.1 TxE，W176冻结原包、W177原受理、W178可靠运输已交付，不重复。

## 实际改变

- 从不可变StartOutbox与Submission读取原包和已经选定的发布版本。后来发布新版、停用绑定或停用定义，不重新选latest，也不冒充旧Wf_FlowDefVersion。
- 原`engineStartId=SubmissionId`保持不变；实际InstanceId采用固定`oa.app.instance-id/2.0`命名空间，对准确Scope/Owner/Submission的规范字节做SHA-256并取UUIDv8，重试稳定且不同于旧实例命名域。原完整Subject仍由starts→不可变Submission外键关联，不挤入长度/排序规则较窄的legacy BizType/BizId，也不参加旧latest-business回调。旧索引和EF模型不变。
- 原生Scope/authentication锁、原StartOutbox行锁和唯一完成记录串行并发启动；同一显式PostgreSQL事务创建实际Wf_FlowInstance、必要task/token/history/传签读模型/变量快照，保存当前启动回执与审计。引擎SaveChanges只是外层事务的一步；任意后续失败全部回滚。真实commit丢ACK返回START_UNKNOWN，原提交查询和重试只恢复已提交的同一实例。
- 复用现有单档、多档和并行引擎算法、真实Sys_User/Role审批人。当前启用用户/角色、原Scope及租户时钟在适用边界复核；数值条件使用精确求值，不将大数转double。缺角色真实Suspended；start→end没有人工决定时同样Suspended，不造Approved或以发起人冒充缺失审批人。
- 数据库延迟约束要求实例与完整启动证据同时存在，完成记录不可变。原运行表写入先进入认证根锁；已有无关/孤立运行记录造成明确冲突，不能被新提交采用。当前阶段没有TxD写入者，因此数据库和旧办理/转签/撤回/超时/Dispatcher入口均阻止绕过决定协议改写v2实例。
- `GET /internal/oa/app/v2/submissions/{id}`在实际C02服务身份及当前原生登记下查询准确当前启动结果，支持直接alias；原`commands/{id}`仍返回不可变受理ACK。`ApprovalStart.Enabled`默认false，启用还要求已有ApprovalIntake登记；后台使用现有可信租户Scope runner。

## 验证与审查

本轮有效验证109项，其中22项隔离PostgreSQL18/实际HTTP，87项相关引擎/规则测试，0skip。逐测试运行来源与最终源码blob见`D:/CP6-local-test/wave179/foundation/final-validation.json`。未改变的TxS和旧内核成功证据按真实来源复用；不把复用改称整套新跑。没有页面改动，不运行前端全量构建。

覆盖实际原生实例和任务、固定原版本、禁用后继续原意图、并发首次领取、真实commit后丢ACK、保存运行图后故障全回滚、事务中撤权全回滚、缺角色/禁用角色、无审批路径、串签/并行、当前查询/alias/服务撤权、不可变完成、孤立实例/任务拒绝、legacy入口隔离和原索引兼容。

保留真实失败：首轮20项17通过，两个测试故障注入器仅拦reader而未触发nonquery，以及测试复用旧创建草稿命令键；第二轮旧内核成功，新增逐字约束暴露PostgreSQL排序规则冲突，另有测试表名错误。已改为实际NonQuery故障、独立测试命令、准确表名和明确C排序规则原文比较。各次构建/测试均确认句柄终止后才编辑后端；没有因观察超时重复启动。

一次任务级作者SPEC/QUALITY覆盖完整差异：原固定版本/身份、真实引擎复用、外层事务与丢ACK、权限最终检查、数据库写入覆盖、兼容性和未交付边界。没有敏感配置、依赖/工作流变更或生产部署。

## 剩余范围

本轮是实际TxE启动交付。人工TxD的当前权限/SOD、会签决定证据、DecisionOutbox、Owner审批引用投影、Inbox/ApplicationReceipt与实际业务应用仍待采用；v2任务暂不能经旧入口办理。服务任务和subflow在缺对应运行Owner时保留实际token并明确挂起，不调用旧外部执行器，不声称整套工作流已可用。实时通知未作新可靠投递声明。

测试使用隔离DEMO登记、政策、服务密钥/时钟；实际HTTP测试是组件宿主，不是完整Main启用配置或企业环境验收。后台默认停用；正式运营重试/告警与启用资格仍随完整运行链收口。原ROU/BOM/RootWO/Snapshot/Occurrence/SCHED，D2正式Transfer/REP/RMA、D5/D6真实外部来源及D7最终集成全部保留。125目标、247真实验收和完整任务状态不前移。

## 生命周期

W178只归档一次，只关闭`WP12-06.wave178_approval_delivery`生命周期；W179记入`WP12-06.wave179_approval_start`限定范围。七天目标ACTIVE，D1/D4已闭环，其余阶段仍部分采用。

预计120–210分钟；当前Main登记34.94分钟，最终START→finish另存最终核对记录，Handoff重叠不累计。Handoff正常PR：https://github.com/GTX537/CP6.AI-Handoff/pull/155，源码`fa50fe231e0d543385955ebee6fd5f64f3b35152`、远端目标`9c7b92fc6434de11b3b2f12fadda8cd3eec4f402`；已核对准确树采用、删除本地/远端任务分支并释放DEV-2。此提交Main仍待正常PR、远端采用与finish，不预写完成；本机`wave179-final-verification.json`保存最终结果，下一轮准入归档一次。

固定目录复用，用户原件及缓存保留；没有新增源码工作树。普通工作流及依赖内容复用既有分析并核对当前引用；无Actions触发/取消、既有环境迁移、生产部署或保护绕过。
