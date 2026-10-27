# W179：审批实例原子启动与固定版本恢复

COORD单人继续CP6-7D-20261022，复用固定DEV1/DEV2。四仓主线、最新AGENTS、台账及空闲席位已核对。W178只归档一次并关闭WP12-06.wave178_approval_delivery生命周期；完整任务及七天出口保留。

## 实施范围与前置

采用已接受OA-APP oa.app/2.0 §6.1 TxE。W176真实Owner冻结原包、W177服务受理与W178可靠投递已合并。本轮以engineStartId=SubmissionId从不可变StartOutbox启动；实际Wf_FlowInstance、必要任务/token/history/读模型、原Submission当前状态和启动完成证据同一PostgreSQL事务。准确固定v2发布版本，不借旧legacy版本或重新选择latest；已受理意图不受后来绑定停用影响。

复用既有引擎节点/审批人解析，缺角色保留Suspended；不以发起人替代缺失审批人或start→end自动批准。隔离保存前通知和legacy业务回调，未交付TxD不能经旧入口提前生效。提供准确原提交当前查询及默认停用的可信租户后台启动。原command受理回执仍不可变。完整TxD决定、Owner应用和原ROU/BOM/WO/SCHED、D2/D5/D6/D7出口保留。

## 任务和实际时间估计

预计120–210分钟：事务启动/引擎固定版本70–120，定向PG/HTTP验证、一次集中审查和合并收尾50–90。实际计Main官方START至finish，重叠Handoff不相加。

- [x] 同事务实例/任务/token/history与启动完成证据。
- [x] 原固定版本、并发/回滚/丢ACK恢复与可信租户后台。
- [x] 原提交当前查询和旧引擎副作用隔离。
- [x] 相关验证、集中审查、台账与四状态同步。
- [ ] 正常PR合并、远端采用核对、准确分支删除和席位释放。

PostgreSQL唯一新增数据库；不触发Actions、既有环境迁移或生产部署。测试夹具不等于真实业务验收，125完整目标/247运行验收保持准确未完成范围。

## 实际交付

[报告](../../evidence/W179-01/DELIVERY.md)，登记34.94分钟；Handoff已释放，Main待正常PR/finish。完整目标不关闭。
