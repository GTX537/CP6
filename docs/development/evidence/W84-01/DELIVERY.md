# W84-01：批次追溯与召回标记独立Owner

Issue [471](https://github.com/GTX537/CP6/issues/471)，COORD本聊天单人。Main基线`0e0eec34d63efbc3dcd93e6c2582874452705944`，任务`BUG-471-LOT-TRACE-OWNER`，固定DEV-1；Handoff固定DEV-2。开工22:35:48Z，预计35–55min＋独立前置约2min，最终按finish计，不重复累计Handoff；[范围与前置](../../dispatch/20261023-wave84/README.md)。W37–W83不重复。

## 行为与合同

PostgreSQL批次追溯接入当前OperationsOwner原会话、独立query/recall、原生权限/业务围栏和最终重核。自身与源wms-stock:query的真实库存、交易范围取交集，再计算汇总。scope1核实际Creator，2–4用stock/stock-transaction实际UUID部门绑定，各自独立；5仍有可信租户和Wms仓库区位约束。空集合也核Owner。`LOT-TRACE-OWNER/1`返回具名、最小DTO，数量及跨行合计以精确字符串/BigInteger表达，保留21,8和合计超过单行上限的真实值。没有价格、整实体或额外actor输出。

OperatorCd/Remark/CustomerName/SupplierName执行当前MIN字段读取决定。流向客户要求自身/源实际出库对象权限交集，交易TxnNo与明细ShipTxnNo以及实际RelatedNo/Type、产品、批次、仓库、库位相符。来源供应商要求自身/源收货以及有来源时的入库计划权限，明细StockTxnNo与真实库存交易、收货单号及产品批次库位相符。ReceiptNo与InboundNo区分；无入库计划的直接收货不伪造供应商。可选来源缺动作权限仅省略其关联投影，其他不可用仍关闭。已授权库存流水中的原始单号引用仍属该流水范围。稳定排序和实际交易Id去重避免明细关联放大数量。

库存读取返回包含实际全部可见Stock Id/RowVersion的摘要，域含租户、产品、批次与版本名；它只是并发版本，不是授权。召回命令在首个await前冻结。原生围栏内要求该可信租户产品/批次全部非删除库存均在当前自身/源库存授权范围，否则403且零部分写入；完整集合摘要或任何RowVersion变化409。锁防止原生并发插删/写入越过最终核验。只修改RecallFlag、真实Modifier和ModifyDate，数量/分配/价格/QC/流水不变。最终对象及权限重核失败回滚；提交确认丢失503 OPERATIONS_OUTCOME_UNKNOWN，无自动重发。旧PG无版本写409 ROW_VERSION_REQUIRED，旧读取409 OWNER_READ_VERSION_REQUIRED；非PG历史行为保留。

实际既有OutboundService分配验证：召回标记使后续分配抛库存不足，分配量仍0；持当前版本解除后重新分配成功。这不等于撤销已存在分配、实物召回执行或全部出库拦截验收。页面明确仅当前权限内批次观察及标记，未实现BOM递归/多跳供应链或正式召回业务。

集中审查发现原库存空LotNo是合法无批次键，已保留并在真实HTTP读/写与页面验证；前后空格也不擅自trim成另一个键。生产Program登记范围/字段，PG迁移和新租户启动只添加query目录、不自动授角色。HTTP统一Owner错误与no-store。页面数量不经Number，结果匹配具名版本/覆盖声明/产品/批次/方向；身份epoch、乱序、失败和当前查询权限丢失清数据。召回使用已显示快照及版本，确认期间筛选/身份/动作变化停止提交，重复拒绝，未知无重试；成功重读当前状态。UI为实际SFC运行用例，不声称现场浏览器验收。

## 验证与一次集中审查

23独立.NET用例（17原生PG、5既有非PG行为、1两租户启动种子）及12Vue用例通过，无跳过；相关构建和vue-tsc通过，去重35项。一次作者完整SPEC/QUALITY覆盖入口、真实来源/部门/字段、精确数量与合法键、全批次权限与CAS、原生并发、最终失败/未知、真实Program/login/HTTP、分配消费者、页面及两种目录入口；问题修复后仅复验相关部分，无剩余阻断。

- `owner-red-20261023T223727Z.json`：3真实PG红灯，缺Owner读取和召回原先未拒绝。
- `owner-boundaries-20261023T224024Z.json`：编译失败、无测试/PG启动；夹具误用不存在的Shipped枚举，修正为实际Completed。失败保留。
- `owner-native-20261023T224147Z.json`：20项9通过11失败；本任务字段声明未按公共验证器要求排序，造成503。修正本资源expected字段顺序，未放宽公共权限验证器。该批5历史非PG、1两租户启动种子成功证据仍适用。
- `owner-reviewed-20261023T224334Z.json`：17PG中16通过1失败；实际分配按原合同抛InsufficientStockException，修正用例预期。包括双连接真实锁等待/一个版本胜出、库存集合增删和行版本变化、tenant/self/部门2–4/仓库/源权限、字段与真实关联、回滚/未知。
- `owner-final-20261023T224604Z.json`：3/3PG定向复验通过，实际分配拒绝/解除成功、真实HTTP含合法空批次及撤权、非法输入和目录无自动授权。覆盖最后产品修改，未变成功证据复用。
- `lot-ui-compatible.json`：12/12通过；`lot-types-result.json`记录exit0。类型检查旧调用结果在上下文截断中缺退出码，补一次有记录的检查；未声称空日志本身等于成功。
- 4个实际自有PG夹具串行55435，ownership/stopped齐且无PID；编译失败批次不虚计PG。数据、TRX和红灯保留。已有构建警告保留。无全仓回归、Actions、既有环境迁移、镜像或生产部署。

## 采用与剩余范围

候选进入正常Main产品PR，核远端blob后交换Handoff DELIVERY并合并/finish，再同任务Main状态PR/finish。核对前不提前声明分支删除或席位释放，最终耗时以生命周期证明和后继登记为准。

24/375整卡、125目标0完整交付、247正式运行NOT_RUN保持；完整WP02-03、库存/财务、正式召回执行及CRM真实Site/Form Owner/公开受理不关闭。七天目标ACTIVE，继续剩余实际前置。W51指定停止data删除曾被自动审批以blocked by policy拒绝，继续原地保留，不重试。

## 实际采用与最后收尾

Main [PR472](https://github.com/GTX537/CP6/pull/472) source `9607b710b6b926dbeb7735482f474cd2083b127f`采用远端main `4cbf720a21c783cd16fc0d8ca84857494463c2a1`，26任务路径逐blob核对；Handoff [PR60](https://github.com/GTX537/CP6.AI-Handoff/pull/60)采用`081c35e68b10f1b0fa42b4315cef84195c196d8c`，准确分支删除、固定树干净detached、finish释放。16个产品路径与已测提交逐blob一致，复用有效结果，最后文档diff/台账生成单独检查。

Main同任务最终状态PR/finish及Issue471最后关闭待完成；当前约17.80min，原估35–55min＋独立前置2min，实际按finish不重复计Handoff。最终证明`C:\Users\tt\CP6-planning-reports\20261017-foundation\wave84-final-verification.json`。无Actions、既有库迁移或部署。七天目标ACTIVE，继续剩余独立Owner及D5/D6真实前置，正式召回执行/Finance仍未采用。
