# W176：版模处置审批冻结与原键恢复

COORD单人继续CP6-7D-20261022，复用固定DEV1/DEV2。W175仅归档一次、关闭WP12-06.wave175_approval_binding限定字段，完整任务和七天全部出口保留。

## 前置与实施范围

四仓主线/AGENTS、最新台账、工作区、重复任务/PR和Actions已核对。采用已接受WMS-TOOL-01报废冻结意图及OA-APP oa.app/2.0 §6.1/7.2/16.6：业务Owner本地原子冻结准确Subject/Policy、快照、发起授权、原Submission/OwnerOutbox/Command/Audit。现有W169申请不是审批，W175绑定不是业务政策。

实现独立有权处置审批政策及实际Owner冻结提交、永久意图去重、原命令恢复、同事务待投递原包、当前权限/来源/Site/义务最终检查和页面入口。政策不默认种入，不以夹具为真实批准。HTTP201仅DISPATCH_PENDING；本轮不宣称TxS/TxE/TxD、已启动审批或最终报废。Stock/Finance/作业等未知义务保留；后续引擎与双向持久回执仍是原目标必需工作。

## 任务与实际耗时估计

预计90–150分钟：服务与持久化45–75，页面20–30，定向PG/HTTP/Vue验证、一次集中审查和合并收尾25–45。实际计Main官方START→finish，重叠Handoff不相加。

- [x] 政策Owner、准确冻结Submission及Outbox/原命令同事务。
- [x] 处置页面提交/当前读取/原键恢复。
- [x] 定向本地验证、集中审查、台账及四状态同步。
- [ ] 正常PR合并、远端核对、准确分支清理及席位释放。

PostgreSQL唯一新增数据库；Actions/既有环境迁移/生产部署未执行。D2/D3/D5/D6/D7、125完整目标及247真实验收保持未完成范围。

## 实际交付

[报告](../../evidence/W176-01/DELIVERY.md)，登记25.68分钟；Handoff已释放，Main待正常PR/finish。完整目标不关闭。
