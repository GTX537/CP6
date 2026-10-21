# W38-01 报工／入库 Owner：单份交付

2026-10-21；COORD本聊天单人；原DEV-1/codex/w38-01，基线`6d97c1a33ce835775364973675b78ee8d1b07ddb`。W37真实合并/清理/席位释放已核实，本轮不重复其交付。[计划](../../../dispatch/20261021-wave38/README.md)、[Owner v1](../../../../design-handbook/remediation/main-production-inbound-owner-v1.md)、[P0 #383](https://github.com/GTX537/CP6/issues/383)。本地Ready to merge Yes；远端采用与席位释放待实际核对。

## 改动与合同

ProductionResultService/BackflushService/InboundService及三个主控制器采用当前原会话Owner，独立动作、完整字段源、可信tenant、自有完整软删除子图、真实订单部门映射及仓库/区域。输入冻结、最终权限和会话检查贯穿COMMIT；序号/库存必须同上下文。读取no-store，错误保留401/403/409/503，新增query/recalculate/backflush仅登记不自动授权。

一次收货的库存、流水、序号、头/明细和计划累计同事务；报工流水、累计、状态及派生工时同事务。已确认提交后才通知。反冲按材料补偿，库存与ActualQty原子提交；保留负库存、成功材料不重复、失败时跳过本次成本归集。实际完成流水产生绑定父complete、原主体/版本/数量的仅进程内委托，下游重查父项，只执行既有自动完成效果，不授独立子动作。未知提交结果不重放。

保留合法精度与Creator；完工/检验后追加数量报工不重复完成桥；手工WM060独立、可选工单、部分/多次收货。收货实际批次和计划外行保持合法，只有明确RefOrderLineNo才要求匹配真实产品/计划行；不复制或读取隐藏计划价格。采购适配器修正非连续PO行号到实际WMS顺序行的映射，回传保持原PO行号；采购整单原子性未在本轮实现。六页面冻结意图、防重复、旧身份/目标/响应隔离、新草稿保护及UNKNOWN阻止重发；确认保存的单号不因读回失败消失。

## 验证来源与失败保留

90项去重相关.NET成功：20项本轮原生PG/实际HTTP、18项既有Owner PG/HTTP及52项兼容/权限测试；17项Vitest（8新+9既有）及完整vue-tsc --build退出0。最终适用集合0失败/跳过。 证据根`D:/CP6-local-test/wave38/foundation/final-validation.json`逐项引用三份实际记录，83项未受最后测试合同调整影响的成功证据复用，最后7项权限接线局部复测；不是90项一次全量重跑，也不是全部PG测试。

|实际记录|结果和归因|
|---|---|
|production-inbound-red-20261021T150547Z|2项真实失败，证实缺Owner读取、收货第二行故障留下首行库存；原步骤在最终PG验证中通过|
|production-inbound-first-20261021T151322Z / corrected-20261021T152308Z|编译失败分别为缺日志扩展using、测试使用不存在ProductName；修正后未掩盖失败|
|production-inbound-behavior-20261021T151603Z|2通过/8失败：共享菜单夹具重复主键，改用已有夹具判存模式|
|production-inbound-fixture-20261021T151823Z|8通过/2失败：BOM缺产品外键、两租户首个计划号相同造成跨租户断言选错对象；修正夹具|
|production-inbound-native-20261021T152353Z|首批10/10通过；后续合同边界修正由最终记录替代对应证据|
|production-inbound-transport-20261021T152717Z|19通过/2失败：新增目录使旧计数陈旧、产品主数据历史全局alternate key造成固定产品夹具冲突；使用独立产品码，不修改模型或租户合同|
|production-inbound-review-fixes-20261021T153028Z|48通过/5失败/1跳过；过宽大小写不敏感筛选误带5项Space外部产物路径失败、1项未启用的Tenancy专属夹具。原结果保留，不计入本轮通过；仅复用其中18项明确Operations Owner和6项权限播种成功证据|
|production-inbound-final-20261021T153430Z|64通过/2失败，20项本轮PG/实际HTTP全通过；两项旧反射规则仍禁止新增query，按新Owner合同调整并局部复查|
|permission-contracts-final-20261021T153729Z|7/7通过；采用Owner的GET必须query，未采用入口历史规则不变|
|production-inbound-pages / typecheck-final|17/17页面行为，完整vue-tsc --build退出0；不冒充浏览器E2E|

20项本轮PG/HTTP覆盖缺Owner、第二库存行失败整笔回滚、原生键/合法精度/Creator/计划累计、完整self含软删除子图、当前动作、仓库区域/字段源/外国tenant、开始中断恢复完成及追加数量、最终权限故障与真实丢失提交确认、实际完成桥受限委托且独立调用拒绝、手工部分收货、真实Program登录/CSRF/query/收货/报工/字段拒绝/503/会话撤销、逐材料库存耗用原子补偿、raw撤权提交先后、原生规格/楞型尺寸反冲、非连续采购行号、计划外行/实际批次/未使用隐藏价格、真实部门映射、工时最终失败回滚及提交后通知。

历史InMemory状态机/工时/反冲/入库/采购适配器及WMS/FIN桥兼容范围单列于final-validation；服务受控主体不冒充身份验证，实际Program夹具跳过全量启动初始化，播种幂等与无自动授权另测。没有全仓回归、SQL Server新增验收、真实DEV/UAT或生产推广。

## 一次作者SPEC/QUALITY与剩余范围

本轮集中检查完整差异、公开接口/DI/控制器、字段用途、完整旧新对象、原生关系/部门/仓库、锁顺序及最终权限、报工/收货/逐材料原子性、受限委托、提交后回调、旧消费者及页面意图。发现并修正：手工收货不能套自动整量约束；完成/检验后追加数量必须保留；尺寸反冲规格/楞型按原生键解析；采购非连续行号必须映射实际入库行；计划来源价格未被消费时不能多要Read；旧反射权限规则跟随正式Owner接线。对应局部复查和适用验证已通过，无本窗剩余合并阻断。未为无关风格扩展范围或启动代理。

短表锁跨租户串行化成本、通知/财务best-effort和无持久幂等明确保留。其他WMS/MES入口（盘点/看板等）、无订单对象部门绑定、采购/财务自身Owner、持久operation/outbox、可靠投递、正式workflow/CRM与真实验收独立。完整WP02-03仍未完成，125目标/247运行状态不提升；不以受控数据替代生产验收。

## 工作区与分支

7处本轮停止的PG生成data回收808394074逻辑字节（0.753GiB），清理时段D盘实际空闲增加812425216字节；另2次编译失败未创建实例。日志/TRX/原失败及ownership保留。清理只在核对绝对边界、ownership/stopped、无进程及无重解析点后进行；未删用户文件、既有数据库或复用缓存。

DEV-1产品和DEV-2 Handoff原固定路径，INTEGRATION-1空闲，零新树/clone/副本/代理。原分支在正常PR、远端采用和状态文档同步后manage finish；候选阶段席位仍占用，不能预称释放。根工作区原未跟踪资料保留。无Actions取消/触发/重跑、workflow改动、现有数据库迁移、真实tenant授权或生产部署。原代理估计4–7小时、故障余量10小时，实际时间在最终finish后核对。
