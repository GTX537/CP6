# W177 审批接收事务与准确绑定版本

COORD本聊天单人，复用DEV1/DEV2；W176已完整合并和finish，仅归档一次。原七天全部出口保留。

## 范围与依据

依据已接受OA-APP oa.app/2.0 §4/6.1/7.2/16.6，接入W176实际WMS冻结原包：当前受信服务与Owner登记、准确Subject/Policy/授权证据、封闭输入、三层命令/Submission/Intent比较、接收时刻唯一有效绑定、准确Published版本，以及Command/Submission/StartOutbox/Audit同PostgreSQL事务。原键只读恢复不重新选择绑定。沿现有服务认证，不创建假真人或默认企业服务身份。

TxE实例启动、TxD决定证据及Owner Inbox/业务回执继续是必须后续采用，当前不宣称审批可运行上线。注册必须显式，默认不开放；历史业务和未知外部义务保留。任何接收成功仅证明持久待启动。

## 任务与估时

预计120–210分钟：服务身份和Owner证据35–60，接收/规则/版本/持久事务50–90，PG/HTTP专项、集中审查与两仓收尾35–60。Main START到finish计一次，Handoff重叠不相加。

- [x] 当前服务身份及准确Owner冻结证据。
- [x] 接收去重、有效绑定/准确版本、原子StartOutbox与原结果。
- [x] 相关PG/HTTP专项与一次完整SPEC/QUALITY、台账和四状态同步。
- [ ] 正常PR、远端采用、准确分支清理、固定席位释放。

PostgreSQL、精简本地验证、无Actions/既有环境迁移/生产部署。D1/D4闭环，其他阶段和真实验收不提升。工程交付估计约65–70%，阶段闭环2/7只是不同口径；后续不按轮次数/测试数增长。

## 实际交付

[报告](../../evidence/W177-01/DELIVERY.md)，登记46.4分钟；Handoff已释放，Main待正常PR/finish。完整目标不关闭。
