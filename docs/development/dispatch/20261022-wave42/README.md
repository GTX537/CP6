# W42-01 真实 START / 七天一期 D2 公共基础

COORD本聊天单人；2026-10-22T05:24:53.552722+00:00；Main基线`02caf602ab8a81d307001484cae52c46f9ff91f5`。W41已合并PR393/394，Handoff17；两分支准确清理/席位实际finish。W37–W41不重复。

限定窗口：无订单对象可信部门绑定：库存、不可变库存流水、盘点、无订单生产指示。最新WMS-RMA-01要求正式WMS-IN-RMA-ADAPTER-1 prepare/commit/query/seal及databaseBinding/MainUow采用证明；五类终端处置仍REQUIRED_FENCED、effectOwner=null，商业/财务效果另属Owner。旧RmaService直接库存/随机贷项不能冒充采用。D2先交付独立可信范围；RMA接线待真实前置，必要时提前D4。

DEV-1 `codex/w42-01`，DEV-2 `codex/w42-handoff`；原已领取窗口修正范围，没有RMA代码改动，不重复START。预估实际2–3小时；数据库只PostgreSQL，新前向迁移仅隔离本地夹具应用。维护复用pub-dept查询/编辑权限、原会话、CAS和不可变原操作；SQL分页/聚合前过滤、当前权限最终复核、审计/撤权并发。无真实数据推断/回填；新建对象、入库及RMA绑定另窗。

Main窗口`docs/development/dispatch/20261022-wave42/README.md`，总计划`docs/development/planning/20261022-seven-day-delivery.md`；roadmap唯一状态源。一次作者集中审查、相关本地验证、正常合并、准确分支清理/manage finish。无新树/代理/Actions/既有环境迁移/生产部署；不提升完整业务125目标/247运行。

## 实施与出口

1. PostgreSQL显式对象绑定、同租户实际对象/健康部门校验、原身份根锁与ABA版本。
2. IAM维护和页面：明确对象选择、CAS/理由/审计/原操作恢复，禁止猜测历史归属。
3. 库存查询/调整、流水查询、盘点全图、无订单生产/看板等现有Owner消费；已关联订单路径保留；未知/禁用/空范围拒绝。
4. 定向PG/HTTP/页面与现有Owner回归；一次作者完整审查，逐项远端采用/分支处置/席位释放。

预计实际2–3h，按真实时间修正。部门范围创建新对象、入库独立对象及RMA适配另留明确前置，不以本批四类通用表冒充已采用。


## 候选验证

100项去重相关.NET通过（12项新增PG/实际HTTP、88项既有Owner/订单绑定），25项Vitest（新增绑定9、既有页面13、原操作恢复3），完整vue-tsc -b通过；所需范围0失败/跳过。一次作者集中审查及定向修正完成；[DELIVERY](../../evidence/WP02-03/standalone-department/DELIVERY.md)。产品和Handoff已采用，原分支最后文档/实际finish见下文。

## 远端采用与原分支收尾

[产品PR #395](https://github.com/GTX537/CP6/pull/395)正常合并，source`c09b2a4977a6c98f8707a8355c75e3be270632ee`、远端main`98d8d00c5ed39c14683ec97d90899edccdfcf32b`，26项任务文件blob一致。[Handoff PR #18](https://github.com/GTX537/CP6.AI-Handoff/pull/18)采用source`3b4377969cd384a7a8df13078298af188e02c885`、目标`567cef9e8c99b1ced784934c58eb18afab2f4822`。准确Handoff本地/远端分支已删除，DEV-2干净detached并释放。

100项去重相关.NET（12新增PG/实际HTTP、88既有Owner/订单绑定）、25项Vitest与完整类型检查通过；原失败保留，所需范围0失败/跳过。合并未改变产品输入，复用真实成功结果；最后文档只查diff/链接/台账。PG/API停止、4处生成data回收463871282字节，原日志和失败保留。本分支Actions0运行，无取消项或触发修改。

此快照 2026-10-22T05:47:39.146153+00:00 仍在原DEV-1/codex/w42-01做最后文档；正常最终PR后manage finish，不提前声明自身采用/释放。准确分支处置、工作区和席位结果及实际耗时见最终PR、实时管理登记与`C:/Users/tt/CP6-planning-reports/20261017-foundation/wave42-final-verification.json`。原估2–3h按真实finish修正。

七天目标ACTIVE，D1完成，D2首批已采用；接续补充或D3设备/OEE/计划入口，先核最新正式合同和共享事务前置，必要时提前D4。RMA适配/MainUow/效果Owner及新对象归属仍未完成；不以包装旧直接库存/贷项实现冒充正式采用。125目标/247运行及整卡不提升。固定路径复用，根用户资料保留，无新树/代理/Actions/既有环境迁移/生产部署，W37–W41不重复。
