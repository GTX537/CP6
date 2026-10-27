# W171：Scope 授权门与本域撤权写入参与

COORD 单人继续原 CP6-7D-20261022。固定 DEV-1/DEV-2；W170 已合并、准确分支清理、席位释放，本轮只归档一次并关闭 WP12-01.wave170_explicit_drafts。

## 范围和准入

落实 WP02-10 的公共前置：Main PostgreSQL 内真实 ScopeAuthorizationFence（租户 + 受信部署环境 + 权限域），持久 epoch/rowversion 与唯一 WF_AUTH 映射；受当前 IAM 管理权限控制的登记入口。现有身份、角色、动作、资源/字段范围、部门、有效委派和审批人映射写入在实际 SQL 变更前锁同一 fence 并推进 epoch，包括直接 SQL 与 ABA。新增显式事务端口验证 Scope、映射、writer 覆盖、当前版本，供后续 Workflow/业务 Owner 采用。

依据 accepted OA-APP §15.1 与 S3-COMMON R1 §5.1。现有 AuthenticationCompletionGate 是外层串行锁，统一锁序为 native authentication root → Scope fences（稳定排序）→ command/business objects；它不冒充 Scope fence。新数据库 schema 无默认生产环境、无自动企业登记，不从请求体采信 Scope。注册和操作原结果、审计在同一事务；缺配置/映射/writer 覆盖关闭准入。登记的 native writer profile 仅陈述已存在来源，未实现 SOD/正式资源授权与 DEF/FORM/TASK 消费仍须各自采用，不能标记完整 WF_AUTH 业务就绪。

只运行隔离 PostgreSQL 18 本地验证，不迁移既有环境，不启动 Actions 或部署，不改保护、不新增授权。W170 原有设计器、完整发布/审批决策/Owner 应用本轮不提前计完成。

## 任务与实际执行估计

预计 90–150 分钟：Scope 存储/锁/写入覆盖 35–55；受控管理 API、原结果与定向 PostgreSQL/HTTP/并发验证 35–65；一次 SPEC/QUALITY、台账与四状态、两仓合并及生命周期 20–30。实测以官方 Main START→finish 为准，Handoff 重叠不重复计时。

- [x] 持久 Scope/域映射、epoch、事务锁与当前 writer 联动。
- [x] 管理登记和恢复接口、真实 PG/HTTP 撤权并发及故障验证。
- [x] 集中审查、交付报告、台账和四状态同步。
- [ ] 正常两仓合并、远端核对、任务分支清理和席位释放。

七天 D2/D3/D5/D6/D7 全部出口保持；375 卡、125 完整目标、247 真实验收不以公共前置关闭。

## 实际交付

[报告](../../evidence/W171-01/DELIVERY.md)，登记33.93分钟；Handoff已释放，Main待正常PR/finish。完整目标不关闭。
