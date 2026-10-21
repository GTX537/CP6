# W36-01 订单取消库存／制造级联Owner：单份交付

2026-10-21，COORD本聊天独立；原DEV-1/codex/w36-01，基线`f54402d0703e3b15fa67036358b1ce383930cad9`。[计划](../../../dispatch/20261021-wave36/README.md)、[Owner v1](../../../../design-handbook/remediation/main-order-cancel-owner-v1.md)、[P0 #377](https://github.com/GTX537/CP6/issues/377)。候选本地Ready to merge Yes；远端采用、分支处置与席位释放随后同任务核对。

## 实际改动

原生PG的Order.Cancel通过当前原会话、可信租户/部门、完整订单对象与独立cancel动作；复用共同权限根、完整字段源及最终来源/主体/期限校验直至COMMIT。取消不改价格或金额，不从Read推导Write，也不要求无关价格写权限。旧公开Bridge PG入口不能绕过Owner；现有SQL Server历史路径保留。

数据库原生键决定同租户关联制造/出库集合，保留Creator/Id、decimal精度及原业务状态机。冲突关联整笔拒绝。短事务表写锁固定软引用集合，真实库存键汇总与行锁防止不足、重复释放；每张出库单保存点使库存、流水、序号、明细/头一起成功或回滚，恢复EF跟踪避免失败对象再次保存。其依赖制造项只有关联出库全部确认取消才继续；已消费、头状态与占用矛盾拒绝伪成功。保留NeedsDecision、PartiallyCancelled及既取消项幂等。

最终权限失效整笔回滚，不降为子项成功；COMMIT或回滚确认未知返回ORDER_CANCEL_OUTCOME_UNKNOWN，不自动重放。原财务桥/通知仅在确认提交后best-effort，失败不把已提交业务误报成可重试失败。前端从首请求防重复，冻结订单/原因/身份，换单/跨标签身份变化使旧响应失效；二次确认沿用同一意图，Rejected不发成功事件，未知/无响应阻止本弹窗重提并提示刷新核对。

## 实际验证

证据根`D:/CP6-local-test/wave36/foundation`。最终119项相关.NET（含原生PG、实际Program HTTP及既有取消/制造/库存服务回归）、18项Vitest4.1.11、类型检查通过；最终0失败/跳过。119项并非全部PG，重叠结果不累加。

|证据|结果/真实范围|
|---|---|
|cancel-red-20261021T120829Z|新增夹具类型/枚举编译错误；未执行行为测试，随后修正，不算产品缺陷|
|cancel-red-fixed-fixture-20261021T121031Z|两项实际产品失败：无cancel动作仍写；第二行故障时第一行库存释放已提交。原失败保留|
|cancel-initial-20261021T121426Z|上述原故障修正后2/2通过|
|cancel-boundaries-20261021T121828Z|2通过/6失败，新增多租户夹具复用全局WorkOrderNo违反已有唯一约束；改为唯一夹具键，不改生产约束，不算6个产品缺陷|
|cancel-native-http-20261021T122107Z|10/10通过、0跳过，原生事务/范围/最终撤权/UNKNOWN及实际Program Cookie/CSRF|
|cancel-review-boundary-red-20261021T122338Z|1项真实失败：头Confirmed而明细仍占用被误报全部Cancelled；专项修正后纳入最终整套|
|cancel-delivery-20261021T122514Z|119/119通过、0跳过：OrderPriceCorrectionPostgreSqlTests、OrderServiceCancelTests、OrderCancelBridgeHookTests、WorkOrderServiceCancelTests、OutboundCancelFinReverseTests、StockMovementServiceTests、ErpPermissionAttributeTests|
|cancel-web-red-results.json|4失败；其中重复请求夹具等待导致超时，修正夹具后保留下面明确断言红灯|
|cancel-web-red-assertions-results.json|4项实际断言失败：重复首请求、旧目标/身份响应、二次确认原因、未知结果重复提交；随后修正|
|cancel-web-initial-results.json|4/4通过|
|cancel-web-related-results.json|最终18/18通过，0失败/跳过：4新增取消用例及14既有Order页面用例|
|type-check.json|vue-tsc --build退出0|

12项新增PG用例覆盖实际动作拒绝、完整对象/删除兄弟/部门及冲突子关联、失败出库全量回滚和制造依赖、原生大小写/尾空格与精度、已知部分结果显式续办、最终源失败全回滚、旧事务拒绝/提交确认丢失、聚合占用不足、实际HTTP登录/CSRF及隐藏价格/已撤用户、提交后桥可见性与失败、真实raw撤权writer被锁并在随后拒绝、raw关联写等待及重复调用仅释放一次、矛盾库存/制造状态。受控服务actor不冒充真实身份验收；raw关联写等待不证明所有未来独立子项创建已接Owner。未新增浏览器E2E/真实DEV/UAT验收。

## 一次作者SPEC/QUALITY与限制

集中审查完整任务差异、当前权限/字段及可信映射、原生关联/精度、表锁和库存行锁、保存点/EF恢复、制造依赖、最终撤权/UNKNOWN、提交后副作用和前端意图/旧响应。审查发现的头/数量矛盾已局部修正并纳入119项最终验证；无剩余本窗阻断。验证后仅补XML合同注释/文档，不重复相同业务测试。未删除失败测试、跳过门禁或启用额外代理。

关联表锁短暂串行化这些表的写入（包括其他租户），是当前软引用布局的成本。只证明本次提交时集合一致，独立WMS/MES创建/编辑入口仍需自己的Owner。提交后财务桥与通知保持既有best-effort，未实现持久outbox或完整财务验收；未知结果只拒绝自动重试，不宣称跨重载operation持久恢复。其他读取/计算/追踪、外部workflow/CRM和真实业务验收独立；完整WP02-03与125目标/247运行不提升。

## 资源与收尾

6处本轮自有PG生成data回收689069947逻辑字节（0.642GiB）；清理时段D盘实际空闲增加692285440字节（0.645GiB）。逐个核对ownership、停止标记、进程、绝对路径和重解析点后正常清理；日志/TRX/原失败及必要资料保留。未清其他数据库、缓存或历史目录。

原DEV-1产品、DEV-2 Handoff，INTEGRATION-1空闲，四仓合计2开发+1集成；零新树/clone/完整副本/代理。原任务分支正常PR及最后采用文档后manage finish，候选阶段不提前声称释放。代理原估计150–270分钟，实际耗时在真实收尾后核对。无Actions、既有环境迁移、真实租户授权或生产部署。

## 远端采用及原任务分支收尾

产品[PR #378](https://github.com/GTX537/CP6/pull/378)正常合并，source`a6e58a19df1de2ce1c97c3701f3da8a697c54814`、远端main`d8c9f549e9733ee6c76b68f5120064826e7c8bfd`，任务改动blob一致；#377原故障回归及远端采用后关闭。[Handoff PR #12](https://github.com/GTX537/CP6.AI-Handoff/pull/12)合入默认目标，source`ea5af396bdb7efdd713e949cba18fd036436dea8`、目标`c9f647bdf118b9513fc16043a88a8a5454ba8392`；资料分支已定向删除，DEV-2干净detached并释放。

119相关.NET（含原生PG/实际Program HTTP）、18前端及类型检查通过，最终0失败/跳过；一次作者审查及发现问题的局部复查无剩余本窗阻断。最后文档不改产品/依赖/环境，复用上述证据，仅查差异、链接及台账。自有PG/API停止；6处自有生成data逻辑回收689069947字节（0.642GiB），清理时段D盘实际空闲增加692285440字节（0.645GiB）；日志/TRX/原失败保留。

本提交仍使用原codex/w36-01、DEV-1，最后正常PR之后调用manage finish。当前提交不能提前证明自己的未来合并和释放；实际结果以管理登记`D:/CP6/.git/cp6-workspaces/state.json`、最后PR收尾评论及`C:/Users/tt/CP6-planning-reports/20261017-foundation/wave36-final-verification.json`为准。INTEGRATION-1空闲；零新树/clone/完整副本/额外收尾分支/代理。

本取消Owner窗口已完成；独立WMS/MES入口、其他读取/计算/追踪、持久operation/通知恢复、正式外部workflow/CRM和真实DEV/UAT仍独立。表锁跨租户串行化成本及财务/通知best-effort边界保持；完整WP02-03、125目标/247运行不提升。无Actions、既有环境迁移、真实tenant批准或生产部署。代理原估计150–270分钟，实际耗时在真实finish后核对。
