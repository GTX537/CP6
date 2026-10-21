# W35-01 普通订单写入Owner：单份交付

2026-10-21，COORD本聊天独立；原DEV-1/codex/w35-01，基线`3bdc4253ea484eaa0a7de105f4e6003c0eb480ca`。[计划](../../../dispatch/20261021-wave35/README.md)、[Owner v1](../../../../design-handbook/remediation/main-order-write-owner-v1.md)、[P0 #374](https://github.com/GTX537/CP6/issues/374)。候选本地Ready to merge Yes，远端采用/分支处置/席位释放随后同任务核对。

## 实际改动

Create/Update/Delete内部PG Owner重新消费原会话、真实add/edit/del、完整当前字段源、可信租户和部门/对象范围；创建/替换独立UnitPrice/Amount Write，删除独立动作/全对象且不改变金额值。旧对象与写后持久化对象都核范围；共同根、完整订单图锁、READ COMMITTED和最终来源/身份/到期检查贯穿COMMIT。所有子表与ORD序号同事务，最终源失败/并发冲突整笔回滚；提交确认丢失UNKNOWN不重放。

修改保留原业务行号的Id、Creator/CreateDate、mc/审批/出荷状态和原生业务键/完整小数精度；全量删除再插入在同一事务，原合法空手配号不生成超长替代值。创建桥接后移到已确认提交之后；best-effort失败记录日志，不误报创建失败。C03内部来源路径维持独立合同，不宣称其新验收完成。

真实MVC不再把Owner拒绝转成400；非法DELETE版本400不忽略，真正冲突409。前端从授信/确认前就防重复，冻结目标/输入、核身份/代次；保存前新编辑中止旧提交，已提交后新编辑保留草稿并绑定订单号/版本，旧删除响应不清空新草稿。只有edit权限也能正确显示修改按钮。

## 实际验证

证据根`D:/CP6-local-test/wave35/foundation`；最终69项相关.NET（含原生PG/实际Program HTTP）、14项Vitest4.1.11与类型检查通过，0最终失败/跳过；重叠结果不累加。

|证据|结果/真实范围|
|---|---|
|ordinary-write-red-20261021T063835Z|两项真实产品失败：无del动作服务仍删除；过时头版本抛冲突但两个子行已被删除。原记录保留|
|ordinary-write-initial-20261021T064839Z|原两项故障修正后2/2通过|
|ordinary-write-boundaries-20261021T065208Z|7通过/3失败；发现真实MVC400包装、原长键空手配号补值超限，后者也使最终源故障测试尚未走到第二次读取。均修正后执行下列整套相关验证|
|ordinary-write-native-http-20261021T070039Z|69/69通过、0跳过：整个OrderPriceCorrectionPostgreSqlTests（含读写Owner/部门/撤权）、OrderReadbackTests、OrderCalcTests、ErpPermissionAttributeTests|
|order-write-web-red-results.json|8通过/3预期新增失败：授信前重复保存、删除确认跨身份、过时删除清新草稿；随后修正|
|order-write-draft-red-results.json|11通过/2预期新增失败：等待期间新草稿被旧保存覆盖；局部修正|
|order-write-permission-red-results.json|定向1项预期失败，其余13项因选择器未运行；不能称全套通过。仅加key仍失败的order-write-final保留；改由Vue条件渲染|
|order-write-delivery-results.json|最终完整页面14/14通过，0失败/跳过，含真实权限store/指令与edit-only切换|
|type-check-delivery.json|最终vue-tsc --build退出0；初次新增测试空值类型错误及中间记录保留|

PG实际覆盖嵌套字段写拒绝、真实动作目录缺失、外租户/完整旧self范围/删除兄弟、写后部门迁移拒绝、健康树失效、全表原子回滚、原生键/子材料及decimal、原Creator/Id/出荷保留、输入冻结、提交后桥接可见且失败不重创、旧事务拒绝/最终源故障/丢失提交确认、真实raw撤权writer等待读取COMMIT后下一写拒绝。实际Program登录Cookie/CSRF、字段403、并发409、撤销身份401。服务actor回调是受控夹具；没有新增浏览器E2E或真实DEV/UAT验收。

## 一次作者SPEC/QUALITY及范围

已集中审查本任务完整源码/测试差异、SYS-IAM完整域与Owner映射、原生范围/锁/事务/子图身份、最终撤权、受信C03路径、已提交桥接/回读语义及前端草稿/权限。审查发现的草稿覆盖和edit-only按钮恢复已局部修正并定向复查；无本窗剩余阻断。没有移除失败测试、跳过门禁或新建审查代理；未变后端证据不因随后前端/文档变化重复运行。

取消级联仍未采用：OrderCancelBridgeHook逐项调用OutboundService/WorkOrderService并捕获异常，需要先核库存/制造效果、各自事务与部分失败恢复，再接Owner。其他查询/计算/追踪、业务operation持久恢复、正式POWER EGG、CRM持久Owner和真实业务验收独立；完整WP02-03及125目标/247运行不提升。

## 资源与收尾

4处本轮自有PG data经ownership/停止标记/进程/绝对路径/重解析点核对后正常回收，逻辑458600284字节（0.427GiB），清理时段D盘实际空闲增加460750848字节（0.429GiB）。日志/TRX/原失败/必要私有资料保留。未清其他数据库或历史目录。

四仓合计2开发+1集成，原DEV-1产品/DEV-2 Handoff，INTEGRATION-1空闲。零新树/clone/完整副本/代理。原任务分支完成正常PR和最后采用文档后manage finish定向处置分支、释放固定席位；候选阶段不预先声称释放。代理原估计180–300分钟，实际耗时在真实收尾后核对。无Actions、既有环境迁移、真实租户授权或生产部署。
