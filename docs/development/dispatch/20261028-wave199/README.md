# W199：完整候选评审包与持久 OA 送审

COORD 单人继续 CP6-7D-20261022 的 D3 实际前置。W198 Main PR621 / Handoff PR174 已远端采用、准确分支清理及席位释放；仅归档一次，复用 DEV-1 / DEV-2。

## 任务与实际执行预估

1. 完整候选集合、准确评审包、独立摘要及 RoundBinding、当前源和政策准入：60–90分钟。
2. 同事务 InReview/原件/原操作/送审 Outbox、原生 OA 接收与可靠原包重试、受控页面：55–90分钟。
3. 定向 PG18、实际 HTTP/UI、一次 SPEC/QUALITY 审查、台账和四状态、正常 PR 与 finish：35–60分钟。

预计150–240分钟，沿用原持续目标。按实际耗时记录，不将本地案例或轮次数算为目标百分比。

## 固定来源与前置

依据 WP05-19、PLM-ITER-01、原 ITER PROC009、OA R2闭合合同及共享消费映射 ITER-OA-APPLICATION/1 和原 schema/mapping 资产。W198原生候选、W197迭代与批准REQ输入、现有OA持久接收/FLOW启动/原包投递作为实际基础。源身份、摘要及原技术字节不改写。

冻结完整成员集合，成员按原ID UTF-8字节排序，不取首项或前1000项；Aggregate/Package/Policy/Technical/Subject摘要分开。原生本地Round为UUID；合法原生Ref原样保存，外域别名未注册时明确拒绝，不截短或伪造新Owner Ref。Snapshot保原256KiB、body1MiB和ID列表1000项上限。

新发起要求当前InDesign、非暂停、完整已批准输入、当前产品场地/候选源/字段/成员权限和合法政策；Package+Aggregate+Binding+Round+InReview+Outbox+原结果+审计同事务。原结果未知只查原键；发送在业务锁外。OA接收/启动不代表Owner批准采用。

本轮交付送审链；后续 WP05-20 决定Inbox/独立问题关闭/应用回执/撤回竞争，工程冻结及WO/SCHED仍保留。完整WP05-19的外域兼容与正式验收不因原生链交付自动关闭。保留D2/D3/D5/D6/D7原出口与125目标/247验收，PostgreSQL唯一新增数据库，无Actions或既有环境迁移/生产部署。

## 实际交付

[报告](../../evidence/W199-01/DELIVERY.md)，登记72.6分钟；Handoff已释放，Main待正常PR/finish。完整目标不关闭。
