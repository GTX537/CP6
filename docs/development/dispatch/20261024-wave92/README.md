# W92 仓库／库位独立Owner与名称维护

COORD本聊天单人，沿用writing-plans/executing-plans及精简验证。Main `890cb6427fec6c40cbfd296e9e13bebf0f916c25`；Handoff `567f465bac4180d7d3ebf3bad54627343f80f9e0`；Issue[491](https://github.com/GTX537/CP6/issues/491)。W91 Main489/490、Handoff66、Issue488已全部收尾，15.39min＋独立前置约4min，原精确分支已删、固定席位释放。W89已保存两原分支，跨仓重绑确认仍待答复。

## 已核前置与范围

最新AGENTS/POLICY哈希未变、Main已fetch、官方status两固定树干净且无进程，无重复开放Issue/PR，原仓start不bind/新目录。全文核对当前WarehouseController、Warehouse/Location、两Vue页面及warehouseApi；完整WMS-MASTER-01手册与WP10-01任务卡。原文相关IAM/字段/presence、§6.2初始化、§6.3名称维护、§16逐动作gate已读；不声称重新全文读3886行或全部附件。

旧读只登录直接查表；六旧写缺业务事务当前Owner/对象/字段与最终重核，整实体写允许改其他Owner库存/作业控制，库位创建默认可拣且不隔离。两个页面缺身份撤权清旧及乱序保护，容量技术0冒充无限。原规范明确名称维护可独立于库存/容量/Runtime推进，不能用尚缺正式主数据采用封死独立名称维护。

## 本轮顺序

- [x] 真实PG复现缺Owner的仓库查询、原库位树、未隔离创建，保留红灯证据。
- [x] 新增Warehouse/Location独立当前Owner与版本化观察，真实原UUID部门绑定、可信原会话、仓区/双方源交集、字段MIN及计数分页前过滤。父引用只披露已授权实际父；容量/预占精确原值与typed UNKNOWN分开，Site/lifecycle未知不猜。现有T_Warehouse/T_Location保留，不另造数量账。
- [x] 实现独立名称变更：If-Match真实PG8byte行版本，KEEP/SET/CLEAR按可空性，未知字段整条拒绝；Main journal+私有审计同Owner事务，NO_CHANGE不UPDATE/升行版；同键同意图返回原结果、异义拒绝。原键回查/独立abandon封闭在同一原生锁内，查空为UNKNOWN，晚重试不能复活。原输入/结果不因当前名称改变重写，当前读取范围重核。
- [x] 旧九入口经当前动作/适用对象门要求新读写版本或按实际缺件零效果拒绝，保留非PG历史。query/abandon目录仅定义不自动授角色。新建真实Site/字典/初始化、正式typed规则/生命周期/停止/Space采用另按原合同，不把旧宽PUT当名称维护。
- [x] 两页面接当前读、精确原值/未知、独立改名/原键恢复；身份/权限/筛选/选择/分页立即清空，迟返丢弃，未决原键不重复发新意图。更新唯一实际外部消费者OutboundRouting的仓库选择接口适用性；IAM部门页面加入真实对象类型。
- [ ] 相关原生PG/HTTP/页面和类型检查；一次作者SPEC/QUALITY；Main正常产品PR→Handoff交付/合并/finish→Main同分支最后状态PR/finish→Issue关闭，精确分支清理和席位释放。每次push/PR检查工作流内容与命中过滤，无Actions。

预计60–90min：权限与读取20–30、原结果名称维护20–30、页面/验证/审查/采用收尾20–30；独立前置粗估4min，上一轮重叠阅读不重复算。实际Main start `2026-10-24T01:26:50.033830+00:00`，finish据实校正，不重复计Handoff。复用DEV-1/DEV-2，PG55435隔离、自有进程停止，原数据和失败证据保留。

24/375整卡、125目标0完整交付、247正式运行NOT_RUN不变；完整WMS-MASTER、WP10-01、207AC和真实Site/typed/InitializationFence/保护/Space采用仍UNPROVEN/NOT_RUN。七天持续目标ACTIVE，继续必要D2–D7。不Actions、既有环境迁移或部署。W89仅阻已拒绝的跨仓bind，本轮不重试/绕过。

## 验证完成

19独立.NET（18原生PG、1两租户目录种子）与31Vue通过，无跳过；相关构建及vue-tsc通过，定向补验去重合计50项。一次集中审查及原生竞争/HTTP补验完成；PG停止，待正常采用/收尾。

## 实际采用与最后收尾

Main [PR492](https://github.com/GTX537/CP6/pull/492) source `78213f208946fefa7f24e88c51fb9055d888bf2e`采用远端main `41cbbe6cdbd564557ead95aca7fab2d96bf99a42`，33任务路径逐blob核对；Handoff [PR67](https://github.com/GTX537/CP6.AI-Handoff/pull/67)采用`6a74a70759f9d5e1fc18a5ce662e72a15b692b20`，准确分支删除、固定树干净detached、finish释放。23个产品路径与已测提交逐blob一致，复用有效结果，最后文档diff/台账生成单独检查。

Main同任务最终状态PR/finish及Issue491最后关闭待完成；当前约23.48min，原估60–90min＋独立前置4min，实际按finish不重复计Handoff。最终证明`C:\Users\tt\CP6-planning-reports\20261017-foundation\wave92-final-verification.json`。无Actions、既有库迁移或部署。七天目标ACTIVE，继续剩余独立Owner及D5/D6真实前置，正式MASTER WP10-01仍未采用。
