# W246 原D5身份贯通验收与D5/D6出口审计

W246身份HTTP基线/实际Owner/原生撤权的模拟生产业务验收通过；两种提交顺序、原键重放与撤权后披露拒绝已验证，原D5限定出口核销。

CRM [PR162](https://github.com/GTX537/CP6.CRM/pull/162)源`049f15d5b586fd4ba82e1bd06b352511d0d2b657`→远端main`5f5ab69dab040f14c0d668497157b8c43df89184`完整树采用。[单份产品报告、模拟范围及复现](https://github.com/GTX537/CP6.CRM/blob/049f15d5b586fd4ba82e1bd06b352511d0d2b657/docs/development/evidence/W246-02/DELIVERY.md)。实际环境为隔离本地PostgreSQL18、.NET8原生HTTP；生产业务验收使用模拟输入，生产部署未执行。

## 原D5出口核销

对照[原七天阶段表](../../planning/20261022-seven-day-delivery.md)、[W129原出口矩阵](../W129-01/DELIVERY.md)和[WP02-29限定实施范围](../../task-plans/WP02.md#wp02-29-crm宿主平台合同与观测包采用)，保持原Owner/权限/原子性。本期采用与正式发布资格分别记录，不用新增资格包装替代实际行为，也不把资格未证改成通过。

|原出口|准确已采用实现及证据|
|---|---|
|可信基线/持久投影与持续宿主|[W130](../W130-01/DELIVERY.md)实际Main源/原生PG基线后台、租约/完整采集/发布和停止；本轮从模拟目录实际HTTP重新采集后由原Runtime发布，不用夹具预置publication冒充运行。|
|增量、Inbox、提交后ACK和当前撤权|[W131](../W131-01/DELIVERY.md)原生受信C02接收、原消息同事务/七锁与持久收据、实际Main原字节和未知提交恢复；本轮实际HTTP禁用用户，验证SUCCESS后会话关闭与重复原事件。|
|首个实际Owner/原结果共同事务及竞争|[W102](../../dispatch/20261024-wave102/README.md)、[W103](../../dispatch/20261024-wave103/README.md)、[W114](../../dispatch/20261024-wave114/README.md)、[W117](../W117-01/DELIVERY.md)、[W118](../W118-01/DELIVERY.md)实际配置/Submission/H01原图；本轮业务先持锁时撤权等待，业务提交后撤权生效；撤权先完成时新写入/原重放/读取均401，已提交效果保持。|
|实际宿主采用已接受包、HTTP和观测|[W132](../W132-01/DELIVERY.md)Platform0.10.3实际加载、原生观测/操作端点/独立operator/依赖探针；[W143](../W143-01/DELIVERY.md)当前托管闭包固定清单和启动拒绝。真实运行/发布身份仍Unproven，不提升正式新效果ready。|

原计划与W129要求上述限定采用和最终Owner竞争，发布/环境推广另行。旧阶段remaining把完整process/container/native/JIT及独立发布资格泛化为原D5开工/关闭条件，本次按原要求重新归位到完整WP02-29/发布范围；这些未实现事实保留，未用模拟“证明”其已实现。当前隔离宿主确实运行 accepted package/原Owner，未知正式运行身份仍由原门拒绝；不授予生产发布或取消既有门。

W102/103/114/117/118/130/131/132/133/134/135/143/234 final proof均PASS，相关Main/CRM采用提交本轮重新核对为当前主线祖先。CRM产品src/contracts与依赖基线自W143至当前无变动，历史验证按各自原范围复用，记录wave246-exit-audit.json。本轮正常合并finish后原D5限定出口DONE；完整375卡/125目标/247正式运行验收不提升。

## 原D6审计与下一场景

[W135](../W135-01/DELIVERY.md)已依据冻结原合同核定：本期LeadNo可未分配、公开/H01无附件输入，因此不新增NUM/FILE消费协议；实际事务审计、同意/撤回/必要保留已有采用。公开Bundle/生效选择、浏览器绑定Attempt、提交/受理及原回执已采用；[W133](../W133-01/DELIVERY.md)原生Outbox发布和[W234](../W234-01/DELIVERY.md)持久模拟消费者的回滚/去重/恢复已有真实证据。

政策/日历、IAM/密钥/登记和外部服务均按2026-10-29规则允许明确模拟，旧W135中“必须等企业来源”的文字仅保留历史，不再阻塞。**D6本轮仍PARTIALLY_ADOPTED**：目标宿主规范化/时区与适用传播时限的已有证据尚需逐场景映射，缺项用同契约模拟输入实际执行；不能因为允许模拟就把未运行场景改PASS。D7在原D2/D6余项后做最终相关集成，不以全部125目标替代本期出口。

## 验证、收尾与实际时间

Final local PostgreSQL18/native HTTP 2/2 PASS, zero skipped; acceptance-final-20261101T185625Z. Explicit simulated identity/source/transport/business inputs, actual native runtimes/Owner/locks/commits; scoped W130/131/132/143 and W234 evidence reused, CRM runtime/contracts unchanged since W143. No Actions or deployment.

One complete author SPEC/QUALITY review: original D5/D6 requirements, actual baseline recollection and Owner graph, observed PostgreSQL lock chain, both revocation orderings, exact SUCCESS and original event, no duplicate effects/current disclosure, test isolation and resource disposal. Documentation links, one-time prior-wave archival, unchanged formal task/target/acceptance counts.

独立PG18端口55476确认无客户端后smart停止，HTTP测试退出，数据/日志/cache保留。无新产品依赖、数据库迁移、工作流修改/Actions运行或取消，未执行生产部署。普通文档只检查差异/链接/台账，不另跑产品测试。

本轮正常合并finish后D1/D3/D4/D5原限定出口闭合；D2/D6/D7未关闭。完整WP02-29、正式运行/发布身份与生产部署独立，ops identity仍Unproven且正式新效果ready不提升。4/7仅为阶段数，不是工程完成百分比。 原35–55h总估算已撤回，不以阶段数外推剩余时长。本轮预估45–90分钟，登记至此14.97分钟，最终按Main START至finish计一次，不叠加跨仓子任务。

CRM已合并、准确本地/远端分支已删除且官方finish；Handoff [PR](https://github.com/GTX537/CP6.AI-Handoff/pull/222)已完整采用、准确分支清理且DEV-2释放。Main待正常PR/finish，最终状态以同轮wave246-final-verification.json核销。固定工作目录和用户资料保留。

下一步：继续D6目标宿主规范化/时区和适用传播时限的证据映射与缺项场景；随后完成原D2补货/退货余项和D7最终集成。业务资料及外部输入直接模拟，不等待企业数据，不扩展全125目标。
