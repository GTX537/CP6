# W32-01 受注单价订正 Owner 接线：单份交付

2026-10-20，COORD当前聊天独立，codex/w32-01，基线ad4313f8ce0aea9840189d015c0ddadf6b03551e。[计划](../../../dispatch/20261020-wave32/README.md)，[版本化 Owner 映射](../../../../design-handbook/remediation/main-order-price-correction-owner-v1.md)，[P0 #365](https://github.com/GTX537/CP6/issues/365)。限定范围本地可合并：Yes；候选时尚未宣称远端采用/席位释放。

## 实际变化

实际 PostgreSQL 的 OrderService.BatchUpdatePriceAsync 现进入 Main ERP 的 OrderPriceCorrectionOwnerV1，生产 DI 接好。内部入口没有 Owner 时拒绝，不因旧构造器或空请求回退旧写入。历史非 PG 路径保留原范围，不增加 SQL Server 实现/验收。

Main.Order.PriceCorrection.Write.v1 精确绑定两个价格输入、特价/暂定价、同单传播效果与派生 Amount；Amount 的 Write 依赖 UnitPrice 同用途。复用 W31 完整封存字段来源和真实存行 MIN→默认。映射只批准这个代码 Owner 的写适配器，不从客户端接收 OwnerApproved，也不声称外部业务签名。Read/Export/Download、其他受注写入口仍未全覆盖。

新鲜 READ COMMITTED 事务先取得与 ordinary SQL 权限/用户/session writer 共用的 authority 根，随后锁头及所有同单明细（含已删除行，阻止中途复活绕过传播集合）。在根内重查实际原会话/认证版本、当前启用角色、精确 correct 操作及仍存在的定义、tenant/对象范围和字段权限；保存后与提交前再查。该根持有到 COMMIT，避免入口检查后被撤权仍提交。tenant 时间边界也在 SQL 返回后和提交前比较，不把查询启动时的时钟参数当永久有效。

Scope 1 沿用数据库 Creator/UserName 比较，Scope 5 仍限当前tenant；2/3/4和默认2因 ERP 部门码没有可信 IAM DeptId 映射，明确503 OWNER_SCOPE_MAPPING_UNAVAILABLE。不填假映射或偷偷改成全租户。客户端传入 userName 不作为修改者或授权身份；原真实登录下的模拟身份也不获得平台权限捷径。

#365 原先的 ExecuteUpdate 独立落库及保存前外部调用已移出新 PG 路径。最后非 null 套价按每单输入顺序传播到实际受影响行，全部检查对象/字段；由最终价格重算金额并设置审批申请状态。未勾选而实际改价的行也保存意图，防止价格/金额/审批状态分离。保持 decimal(21,8)、原价选择公式、合法大小写/尾空格业务键、缺失行 ConflictedKeys；有效 RowVersion 不会因自己的套价批量写入产生假冲突。真正冲突或非法字段变化整批回滚，绝不悄悄改回原值。

必要前向迁移20261020104000_OrderPriceCorrectionIntentV1仅安装在自有隔离实例。内部 cp6_order_price_v1.workflow_intents 保存最小改价意图与tenant/actor/映射/来源版本，和业务及原字段审计同事务。它不是POWER EGG完整46项报文，也无自动投递器。原主机NoOp日志桩不再冒充外部成功；返回新增 workflowDeliveryState=AwaitingAdapter，页面显示保存申请、等待外部发送。UpdatedCount仍计选中输入，WfRequestedCount为实际变化行意图数，可含传播行。

提交或回滚确认不明返回503 PRICE_CORRECTION_OUTCOME_UNKNOWN、retryable=false；无自动重放。页面未知/网络失败保留草稿并锁住本页再次提交，清空查询不解除该锁；401/409/权限失败有明确提示。确认期间只允许一次冻结输入；身份变更清掉旧数据并拒发旧草稿。原通用HTTP写请求不重放规则保持。业务仍没有operationId/恢复API，不能把本页防重试称为跨重载幂等恢复。

## 实际验证

证据根 D:/CP6-local-test/wave32/foundation。最终 **49项相关.NET/原生PG/实际宿主测试通过，9项Vitest 4.1.11通过，类型检查通过；0最终失败、0跳过**。多次重叠执行不累加。

|执行|真实结果|
|---|---|
|owner-red-20261020T102904Z|2项均按原步骤失败：只读仍写；过期RowVersion抛冲突后两行套价仍已变为9。原失败保留。|
|owner-initial-20261020T103454Z|原2项回归通过。|
|owner-native-20261020T104528Z|20项通过，包含相关原算价回归；随后只因明确时间/动作定义/明细复活风险补检查。|
|owner-http-20261020T104833Z|3项实际Program+登录Cookie/CSRF测试通过；不是伪造Controller允许。|
|owner-final-20261020T105312Z|最终49项全部通过，含本次改动及相关字段算法/可信来源/旧算价。|
|owner-web-red|旧页面5项失败，包含未知/错误状态缺失及重复确认等待；保留日志。|
|owner-web-final / owner-web-delivery|先8项通过；补确认跨身份边界后最终9项通过。新页6项+原HTTP写恢复3项，不把前一次8项再累计。|
|type-check-delivery|最终vue-tsc --build退出0；没有整仓前端构建。|

PG用正式迁移和真实触发器。覆盖精度、合法业务键、RowVersion、缺失行、可信修改者、隐藏/只读及派生依赖、跨tenant、本人范围传播拒绝、未知部门范围、孤立动作授权拒绝；自有旧事务/脏上下文不被提交或清空；ordinary SQL撤权先完成则拒绝，业务持锁时撤权实际等待且下一意图拒绝；软删行复活实际等待；保存后来源故障/会话拒绝/到期/取消回滚所有业务和意图；真实commit成功后丢回执只报UNKNOWN且仅一份意图。

实际子进程采用本次编译的WebApi入口和登录，验证缺CSRF拒绝、字段403/范围503/允许200原envelope、等待根期间撤动作权403和认证epoch变化401。服务级会话回调是明确受控夹具，HTTP测试使用真实原会话；不混淆两者。组件中的数据库/账号都是隔离测试数据，不冒充真实DEV或生产验收。未跑新浏览器E2E；页面行为由组件及实际HTTP合同验证。

W27全认证/跨Tab底座、W29 writer/版本和W30封存目录的未变范围复用原交付；本窗只重测受影响消费。没有全仓回归、SQL Server测试、远程Actions或降低检查。

## 集中作者 SPEC/QUALITY

一次任务级作者审查，非独立代理审查。核对完整差异、原PA090与SYS-IAM合同、字段用途及Owner边界、真实原认证链、root/头/明细锁序、传播行授权、原生比较/decimal、回滚和UNKNOWN、内部存储迁移、没有外部副作用、前端不重放/跨身份保护。审查中的时间到期、孤立动作和软删行复活风险已按影响定向修正并验证；无本窗剩余阻断。

## 资源与剩余范围

自有PG55435及实际API子进程均已停止。5处本轮生成data核ownership、停止标记、PID/进程、绝对路径与reparse后回收；原日志、TRX、失败、ownership保留。逻辑回收570849903字节（0.532GiB）；回收时段D盘实际空闲增加573251584字节（0.534GiB），不混为同一个统计值。

DEV-1/DEV-2原席位复用，INTEGRATION-1空闲，无新树/clone/全副本/代理。产品PR、Handoff及本任务同分支文档收尾后调用manage finish；实际分支/席位结论随后追加，候选不提前宣称释放。

仍待：ERP部门与IAM映射、Order其余读/写/导出、业务operation恢复、POWER EGG真实投递及报文、CRM跨库Owner采用与真实业务验收。本窗不建立Redis/跨库分布式事务，不自动推广既有环境。完整WP02-03、125目标/247真实运行保持；无Actions、真实租户批准或生产部署。

## 远端采用与本任务收尾

|项目|实际核对结果|分支和固定席位|
|---|---|---|
|W32-01产品|[PR #366](https://github.com/GTX537/CP6/pull/366)已合并；source`85a3475a3da3e653f64c9d1752335380c362459c`，remote main`f39351c22ac62def3a6502f43529141b4b0fc10d`，改动文件blob一致；#365已按原步骤回归及远端采用后关闭|本文件继续在原codex/w32-01、DEV-1完成最后文档PR；合并后调用同一任务finish|
|W32-HANDOFF|[PR #8](https://github.com/GTX537/CP6.AI-Handoff/pull/8)已合入默认目标；source`0021ddac034d880175bc24d428b41bf611c609e5`，目标`09d3f559bb031c735f2a69ac6b2e0b0f6c75eff9`|codex/w32-handoff本地/远端已删除；DEV-2干净detached，已释放|

本轮限定的单价订正Owner映射、实际业务写入接线和最终撤权门已集成。49项相关.NET/原生PG/实际Program、9项Vitest4.1.11及最终类型检查通过，最终0失败/跳过；一次作者SPEC/QUALITY无本窗阻断。此最后文档未改变产品/依赖/环境，直接复用原验证，只查台账结构、差异和链接。

自有PG/API已停止；五处自有生成data已安全回收，逻辑570849903字节（0.532GiB），清理时段D盘实际空闲增加573251584字节（0.534GiB）。日志、TRX及初始失败保留；不将磁盘其他活动算作本任务逻辑回收。

本提交如实记录写入时点：Main仍持有W32-01席位，仅为本提交的正常PR/finish；不能由提交自身提前证明未来合并或释放。最终实际“分支处置、工作区、席位”查既有管理登记`D:/CP6/.git/cp6-workspaces/state.json`、最后PR收尾评论及`C:/Users/tt/CP6-planning-reports/20261017-foundation/wave32-final-verification.json`。不另开W32-CLOSE任务或分支。INTEGRATION-1空闲，零新树/clone/整仓副本/代理。

仍待ERP部门与IAM可信映射、Order其余读/写/导出、跨重载operation恢复、POWER EGG正式报文/真实投递、CRM Owner采用及真实业务验收。完整WP02-03、125目标/247真实运行不提升，不重开冻结方向。无Actions、既有环境迁移、真实tenant审批或生产部署。原估计本代理3–4.5小时/故障余量6小时；最终耗时在真实finish后报告，不等待满24小时。
