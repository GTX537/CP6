# W178-01 — 审批 Owner 原包可靠投递与恢复

COORD单人，CP6-7D-20261022持续目标。Main基线`bcfb6504aeb41dde5f0d563d392679e79353f4c6`，固定DEV-1；Handoff固定DEV-2。采用原OA-APP oa.app/2.0 §6.1/7.2/16.6，W176真实冻结原包及W177服务受理已交付。

## 实际改变

- 原Owner Submission/Outbox/用户操作回执仍不可变。新增PostgreSQL投递状态、不可变Attempt/Result；claim和租约代际先提交，网络在事务/锁外，接收回执与当前epoch CAS另一个事务提交。数据库外键、约束触发器保证当前attempt及完成回执同时存在，完成记录不可删除/改写。
- 每次首次发送及恢复均先GET原command；只有明确404才用同CommandId/SubmissionId/OutboxId及原bytes POST。未确认结果不生成新提交。超时、取消、进程停止或commit ACK丢失保留原请求；租约到期可重领，旧epoch不能覆盖新结果。
- 实际C02 client_credentials获取短期CP6.Services token，走HTTPS固定来源及固定接收路径。禁止重定向；body/queryRef不能指定地址。当前注册服务/原租户、环境、Owner登记与接收回执准确Subject/Policy/版本复核。原投递首次目标和身份不可随配置更换；仅配置Version允许显式恢复同身份请求。
- 403/422等记录BLOCKED，不盲重试；修复当前资格后由运维明确提升同路线Version或有效Owner登记修订，再查询同原command。409/异形回执保持CONFLICT，无强制通过；未知canonical Owner别名不能生成第二个业务意图。
- 当前审批条件API另返delivery，页面分别展示待投递、结果未知、暂停、冲突、受理确认。用户原命令回执保持历史DISPATCH_PENDING；RECEIVED只表示审批端已受理，不等于起实例、人工批准、义务关闭或报废。

## 本地验证与复用

本轮28项后端有效结果（27 PostgreSQL18/实际HTTP及1精确数值unit）和6项Vue测试通过，0skip；vue-tsc及实际Vite构建通过。明确复用本轮已成功且相关代码未改的19项Owner/intake检查和8项投递检查，最后仅复跑改变的真实HTTPS链，不把它们改称全部新跑。

证据目录：`D:/CP6-local-test/wave178/foundation/`；`final-validation.json`按测试列出实际运行来源并绑定最终源码。主要运行为`delivery-behaviors-20261027T140549Z.json`、`delivery-reviewed-20261027T141221Z.json`和最终`delivery-https-*`记录；UI为`approval-delivery-ui.json`，构建为`build-reviewed-execution.json`。

覆盖接收成功丢ACK后原查询、提交/政策权限撤销后的只读恢复、两连接抢占、重启/租约到期、旧epoch迟到、失效服务阻止本地确认、版本明确恢复BLOCKED、不可变/原子回执、异形/跨对象ACK、未知canonical别名和原路线不可漂移。实际HTTPS链使用产品C02控制器、原生服务Token记录、RSA签名、SDK bearer验证及PG受理，丢弃真实201网络响应后只GET恢复，没有第二次POST。正式默认客户端拒绝未信任的测试证书；测试客户端仅准确pin本次临时证书。

保留失败：首次编译为测试IdentityEventValidator命名空间错误；第二次26项25通过、1项HTTP测试元数据HTTPS配置失败；第三次9项8通过、1项HTTP issuer不满足C02 HTTPS合同。修复测试为真实HTTPS，未放宽生产TLS/身份合同。相关运行终止后才编辑后端；没有观察超时另开重复运行。

一次任务级完整SPEC/QUALITY审查覆盖全部功能/测试/迁移/台账diff；发现并修正原请求在配置变更时的地址/身份漂移，添加针对性PG验证。页面复用现有控件和原键恢复；本轮未另做真实浏览器与PG联合验收。

## 配置与恢复边界

`ToolApprovalDelivery`默认Enabled=false。启用需明确Version、DestinationOrigin及每租户Clients(TenantId/ClientId/ClientSecret)。Secret从宿主秘密来源提供，不写台账、原包、路由证据或日志。它必须匹配已有CrmOidc服务登记的hash；ApprovalIntake映射、Scope环境、原生Owner登记、当前启用租户及实际Token存储均须成立。REAL用于实际来源；DEMO只能在Development并与Owner登记一致。

运输检查使用隔离的临时RSA/TLS证书、登记、政策和接收时钟；浏览器登录协作者为不应调用的strict mocks。它证明此传输/恢复实现，不是企业资料、完整Main启用宿主或生产验收。原TxE实例/任务/token/history、TxD决定/DecisionOutbox、Owner Inbox/ApplicationReceipt，以及ROU/BOM/WO/SCHED和D2/D5/D6/D7仍需完成。

## 交付与生命周期

W177仅归档一次，只关闭WP12-06.wave177_approval_intake生命周期。W178记入WP12-06.wave178_approval_delivery限定范围；完整任务、125目标、247真实验收不前移。七天目标ACTIVE，D1/D4已闭环，其余阶段保持部分采用。

预计120–210分钟；本报告时Main登记已32.6分钟，最终START→finish在本机最终核对记录；Handoff重叠不累计。Handoff正常PR：https://github.com/GTX537/CP6.AI-Handoff/pull/154，源码`a1d95685e70855c96fe0c6f68187a46bc9832e7e`，远端目标`eb3dc58e521442c4f77f83ed89a1bf2f7b5191cf`；已核对准确树采用、删除本地/远端任务分支并释放DEV-2。Main此提交仍待正常PR、远端采用及finish，不能预写已完成；本机`wave178-final-verification.json`保存最终结果，下一轮准入再归档一次。

工作区复用固定路径，保留根用户原件和缓存；不创建额外工作树。普通工作流与依赖内容复用已核对结论，无Actions触发/取消项或配置变更；无既有环境迁移、生产部署、保护绕过或共享历史重写。
