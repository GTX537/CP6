# W96 库存调整／移动页面与原操作恢复

COORD本聊天单人，Main `cf6fbd214c638d917f4cf617ee3a6d876ee77ea5`，Handoff `c7ca6825c3bd65bb45d25b84b5143a69515a599f`。W95 Main501/502、Handoff70和Issue500已完整收尾，实际19.07min＋独立前置约5min；两准确分支删除、三个固定席位空闲。W89跨仓确认待答复，不重试bind。

## 前置和任务

官方status、最新AGENTS/POLICY、主线及Handoff已核对。W44库存原操作v1和W50同事务Outbox已采用；当前stockApi提供命令和查询／close，但实际页面无调用。本次是WP02-10/D4的实际客户端采用，不制造BUG或改写既有v1规范输入。

- [ ] 实际库存页面接入有权限的ADJ／同仓MOVE表单；精确数量以十进制文本输入及HTTP传送，使用既有动作、租户／actor和业务键合同。
- [ ] 当前服务端scope分区、发送前持久原键、跨标签独占；无存储或安全锁则不发写请求。仅保存公共操作引用，不保存库存或价格草稿。
- [ ] 刷新／身份改变／撤权／超时／UNKNOWN保留原键；原键查询或显式close。仅匹配终态可释放，不自动重试或换键，不以close表示撤销已提交库存。
- [ ] 实际PostgreSQL HTTP精度／原结果／同事务Outbox与定向Vue恢复、竞态、授权、类型验证；一次集中SPEC/QUALITY，正常Main/Handoff合并与准确分支／席位收尾。

预计实际60–90min：页面和持久恢复25–40、定向验证15–25、审查／合并／收尾20–25。正式START `2026-10-24T03:14:39.959147+00:00`，独立前置约6min，finish计时且不重复Handoff。DEV-1/DEV-2原仓固定路径复用，PostgreSQL隔离55435；无Actions、既有库迁移、生产部署。

数量仍按既有Stock存储／Owner合同，不能把此页面当作正式TRANSFER Root/Claim/Stage/SourceDemand、REP/RMA、财务或外部业务ACK验收。24/375整卡、125目标0完整、247正式运行NOT_RUN不提升；七天全目标ACTIVE，D5/D6真实前置继续。

## 实施后前置调整与验证

1项原生PostgreSQL实际HTTP端到端＋28项去重Vue/client检查通过，vue-tsc通过；另1项MOVE属性诊断红灯留为Issue503，未计通过。实际只采用ADJ页面及原键恢复；新增MOVE写入口因确认CUSTOMER→SELF缺陷撤回，保留Issue503待独立修复后继续。原计划MOVE项不勾完成。其余本轮适用验证及一次集中审查通过，PG停止。用户已明确授权官方重绑并继续W89；本轮收尾后优先恢复，不再等待确认。

## 实际采用与最后收尾

Main [PR504](https://github.com/GTX537/CP6/pull/504) source `c1d05fe9140afc240e1a1289a09c963083fe6433`采用远端main `87c491b36cf2694f017764a0f6ebe1c43c635f04`，19任务路径一致；Handoff [PR71](https://github.com/GTX537/CP6.AI-Handoff/pull/71)采用`262dd3e197db4a68a9a7714f536c2daaa2366805`，准确分支删除、固定树干净detached、finish释放。8个产品路径与已测提交逐blob一致，复用原成功证据；最后文档diff和台账生成独立检查。

Main同任务最终状态PR/finish待完成，当前约13.05min，原估60–90min＋前置6min，最终以`C:\Users\tt\CP6-planning-reports\20261017-foundation\wave96-final-verification.json`为准，不重复Handoff。Issue503保持Open，新增MOVE页面未采用；用户已授权官方重绑CRM并继续W89，W96全收尾后优先恢复。无Actions、既有库迁移或部署，七天全目标ACTIVE。
