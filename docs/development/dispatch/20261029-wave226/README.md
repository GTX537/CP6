# W226 原商业退货来源 Implementation Plan

> COORD 在本聊天使用 executing-plans 执行；不派代理、不新开聊天。用户已确认的单分支、固定席位、精简验证及收尾规则优先于通用技能模板。

**Goal:** 在真实发货事务冻结准确原订单商业行，向退货页面提供受当前权限控制的原记录及当前来源。

**Architecture:** ERP提供来源事实，Main WMS保留退货批准与E/A/P占用职责。只在显式正式来源捕获时追加不可变商业记录；其引用进入原履约，敏感正文独立存储并在读取时采用当前订单对象/字段权限。旧发货不回填，不以今天的价格或代码重建历史。

**Tech Stack:** .NET8 / PostgreSQL18 / Vue3 / TypeScript；仅本地相关验证。

预计实际60–100分钟，固定DEV-1/DEV-2复用。W225已闭环，仅归档一次。

- [x] 来源与存储：新增 `CP6.Core/Services/Erp/SalesReturnSourceReaderV1.cs` 与合同、PostgreSQL不可变来源表迁移；修改 `CP6.Core/Services/Wms/OutboundFulfilmentV1.cs`。在现有发货Uow中读取原订单行金额/价格/币种，按实际ShipmentLine→OrderLine身份保存。金额保持十进制字符串、原null不变；商业Ref不含虚构发票或政策。
- [x] 当前授权读取：在 `CP6.Core/Services/Integration/OperationsAccessV1` 添加当前历史订单来源读权限映射；新增 `CP6.WebApi/Controllers/Erp/SalesReturnSourceController.cs`，Program注册Reader。原始正文从已保存事实读取；当前来源按实际原行身份读取，取消状态不自动抹去历史，删除/越权不泄露敏感正文。已校验内部事实直接采用，不重新签名、回算摘要或另设资格层。
- [x] 页面消费：`cp6.web/src/api/wms/rmaFormalApplications.ts` 和 `RmaFormalApplicationsView.vue` 增加原商业来源读取/显示，标明价格及金额是原订单记录，当前变化独立显示，不代表可退额度、贷项或退款批准。只有真实CommercialSnapshotRef时显示读取动作。
- [x] 验证：新增 `CP6.Tests/Erp/SalesReturnSourcePostgreSqlTests.cs`，覆盖实际发货共同事务、同SKU不同原行、精确价格/金额、原记录不随当前价格变化、空值/旧来源不回填、租户/对象/金额字段撤权、写失败全事务回滚及实际HTTP。增加页面针对新读取的测试；复用W224/W225未变化成功结果。命令为本轮 `run_wave226_main.py related FullyQualifiedName~SalesReturnSource_`、`run_wave226_web.py related src/views/wms/RmaFormalApplicationsView.spec.ts` 和 `run_wave226_web.py typecheck`；预期无失败/跳过。
- [ ] 一次完整diff审查；修正仅复查影响部分。同步台账、四份状态及一份交付报告，核对生成视图；正常PR合并、远端采用、准确分支清理、official finish释放席位。

直接依据：WP04-34任务卡、WMS-RMA R3/canonical OriginalFulfilmentRef.CommercialSnapshotRef、ERP-ORD/ERP-CREDIT Owner分责，以及W223–W225实际远端交付。WP04-32完整Sales目录未采用；本轮不以来源快照补足其全部能力。商业可退政策、准确InvoiceLine/Schedule/DeliveryProof、独立批准、额度占用和正式IN仍需真实提供者；完整WP04-34/WP10-22/D2保持未完成。

## 交付快照

[报告](../../evidence/W226-01/DELIVERY.md)。登记27.04分钟；代码/相关验证/集中审查完成，Handoff已释放；Main正常PR/finish为本任务剩余收尾。最终分支及席位状态以wave226-final-verification.json为准。
