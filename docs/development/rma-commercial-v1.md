# RMA 商业请求与财务观察 v1

`WMS-RMA.COMMERCIAL/1` 是本期独立 RMA Owner 的商业接线。客户端提交已批准的退货份额与处理意图，服务端冻结份额、原发运/发票/版本、当前授权及实际实收引用。请求先提交至 PostgreSQL，再通过配置的外部 ERP/Finance Owner 处理；RMA 不写外部账本或把退货实收直接当作贷项。

## 请求和恢复

入口 `/api/wms/rma/v1/commercial`：

- `GET applications/{application}/context`：当前可见申请、授权、商业请求与独立动作能力。
- `POST applications/{application}/requests`：`Idempotency-Key` UUID；输入 `expectedRmaHeadRef`、`expectedAuthorizationRef`、`returnShareRefs`、`requestedOutcome`、`reasonCode`。本版本按所选完整已授权份额冻结；CREDIT/REFUND 要求可信来源提供发票行。其他字段（金额、批准或入账回执）不能由浏览器提供。
- `POST commands/query`：`applicationId` 与原 `commandId`，返回原创建结果；同键异体拒绝，当前读权限仍有效。未观察到不代表无效果。
- `POST applications/{application}/requests/{request}/dispatch`：把已持久化的原请求交给外部 Owner，重派始终使用同一租户/请求键。
- `POST applications/{application}/requests/{request}/refresh`：只查询原外部请求，不提交新商业效果。

`commercial-query`、`commercial-request`、`commercial-dispatch` 是 wms-rma 的独立动作；迁移仅登记动作，不自动授权角色。所有读取依赖当前租户、对象/原发运/仓区及字段范围。写入与当前权限、申请授权、范围占用、原操作结果在同一事务。外部派发前再核当前有效授权和未VOID范围。应用来源已经合法、内部类型已经保证的条件直接复用，不新增资格回执层。

每一原发运行坐标的商业请求占用跨申请、跨授权版本保留；响应未知或授权撤回不能释放并换键重复申请。后续商业纠错通过外部 Owner 的不可变后继结果表达，不原地改写旧结果。本期不提供任意释放已请求商业范围的接口。

## 外部 Owner 接口

配置 `RmaCommercialOwner:BaseUrl`，以末尾 `/` 的服务根为准；非本机地址要求 HTTPS 和 `BearerToken`（从部署密钥配置提供，不能写入仓库）。默认未配置立即返回明确错误。外部服务接受 `POST requests` / `POST queries`，请求体 `RmaCommercialOwnerRequestV1`，头 `Idempotency-Key = tenantId(N):requestId(N)`；必须按该租户/原键保存和查询唯一结果。请求包含完整可信作用域，不从调用人字符串猜测部门或客户。

返回 `RmaCommercialObservationV1` 的 tenantId/requestRef 必须准确匹配，状态 FOUND/STALE_HEAD/NOT_OBSERVED/UNKNOWN/PENDING/REQUIRED_FENCED，证据模式 REAL/SIMULATED/UNKNOWN。FOUND 携带原归档 `WMS-RMA-01_CANONICAL-CONTRACT.json` 的 `CommercialOutcomeRef`；其他状态不携带新商业结果。`FinancialOutcomeRef` 可独立观察，不能用商业获批推断入账或退款到账。

外部边界一次校验：CREDIT 发票/claim/range/数量、net+tax=gross 与货币一致；换货/补发引用新需求；RETURN_ONLY/REJECTED 独占结果；退款授权引用同 bundle 贷项、总退款不超过贷项且不再次占数量。金额沿用30位整数/8位小数字符串，使用 BigInteger 精确核对，不转浮点或缩窄为 decimal。数量与半开范围沿用原 Owner 的13位整数/8位小数合同。

财务 AR_CREDIT/REFUND_CASH 分别关联实际观察的贷项/退款授权；INVENTORY_VALUE/COGS 必须关联实际 RMA 实收引用。金额未知保持 null；POSTED 必须有结果、入账凭据和 COMPLETE 覆盖。部分轴仍待处理时不能标总计 COMPLETE。商业有后继版本时旧财务记录继续保留并标 `financialSourceCurrent=false`，页面明确提示仍需核对。

外部超时、网络错误或非成功HTTP返回 UNKNOWN；已持久化请求和范围保留。查询未知时保留最后确认结果，页面明确当前观察未知。慢查询不能覆盖更新的本地观察。原结果同身份不得改写；合法后继必须指向前一原结果。外部调用不包在数据库提交中，外部已生效但本地未观察到时按原请求恢复。

## 验收边界

按2026-10-29用户决定，生产业务验收使用明确模拟的发票、商业批准、ERP结果、财务政策/入账凭据和外部HTTP服务。RMA权限、冻结请求、范围占用、PostgreSQL原操作/不可变历史、实际实收来源、HTTP接线及页面恢复实际执行。模拟外部服务把原请求和结果持久化到隔离文件，重启后按原键查询；不把它称为真实企业财务过账。

本版本不替换原归档合同，不宣称全量 ERP-CREDIT/FIN-AR/FIN-INV/GL 或完整 WP10/11 已实现。完整外部生产服务部署、H30所有消费者、部分份额的新申请分配/释放与正式财务账本属于各自后续任务。业务验收与部署状态分开，未执行生产部署或既有数据库迁移。
