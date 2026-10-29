# RMA-DISPOSITION/2：独立退货处置与库存采用

本合同是历史 WMS-RMA-01 R3 中五个 `REQUIRED_FENCED` 处置目标的可执行后继。原归档不改写；v2 Owner 回执不冒充旧适配器已经采用。既有 `DispositionProposal`、`StockExecutionManifest`、`StockPostingPlanRequest`、`StockCommandResponse` 仍使用原合同形状和数量精度。

## 五个执行入口

| 处置 | 独立权限资源 | 实际结果 Owner | 库存动作 |
|---|---|---|---|
| RESALE_STOCK | wms-return-resale | Main.Wms.ReturnResale.v2 | MOVE，所选准确数量独立 QC 放行到可售库位 |
| REPAIR | mes-return-repair | Main.Mes.ReturnRepairTransfer.v2 | MOVE，内部返修位置转移，保留质量状态 |
| SCRAP | quality-return-scrap | Main.Quality.ReturnScrap.v2 | ADJUSTMENT / ADJUST / LOSS |
| SUPPLIER_RETURN | purchasing-return-shipment | Main.Purchasing.ReturnShipment.v2 | SHIP 到已批准供应商交接 |
| RETURN_TO_CUSTOMER | wms-customer-return-shipment | Main.Wms.CustomerReturnShipment.v2 | SHIP 到已批准客户交接 |

适配器身份分别为 `RMA-DSP-RESALE-MOVE-2`、`RMA-DSP-REPAIR-MOVE-2`、`RMA-DSP-SCRAP-ADJUST-LOSS-2`、`RMA-DSP-SUPPLIER-RETURN-SHIP-2`、`RMA-DSP-CUSTOMER-RETURN-SHIP-2`。五个类固定各自身份，通过同一受类型约束的 Stock 参与者执行，不能由客户端提交 Owner、库存动作或效果回执。

返修仅转移实物位置，不改变产品、技术身份、所有权或原始用途；报废不隐式释放其他预留；退供应商不使用库存调整代替发运。商业补偿、财务结算、完整返修生产和外部物流执行由对应后续任务负责。

## 模拟来源与实际验收

用户已允许生产验收使用模拟数据。`ReturnDispositionSimulation.Enabled=true` 与 `CasesJson` 配置明确的租户、Site、质量政策、处置方式、版本、业务选项名称 Label、允许的质量状态、业务批准、权限依据和证据；MOVE 指定真实本地库位，SHIP 指定模拟交易方及外部交接凭据。配置从服务端注入，浏览器只能选择当前返回的选项。业务批准与外部交接资料可以模拟，库存、业务回执、原操作记录和事务必须实际执行。

`ReturnQualitySimulation` 提供检验政策；实际质量判定由独立 Quality Owner 签署，RMA 必须采用当前判定。`ACCEPTED` 可申请可售，`REJECTED` 仅在相应模拟业务政策允许时选择其他处置；`HOLD` / `PENDING` 不提供执行选项。政策版本或质量结果变化后，旧准备不能执行。

缺少企业数据不是前置阻塞。禁用模拟配置时不产生隐式批准；后续对接其他批准来源时采用明确的新来源合同，不猜测交易方、库位或审批结果。

## HTTP 与状态

根路径：`/api/wms/rma/v2/dispositions/{kind}`。

| 方法 / 子路径 | 行为 |
|---|---|
| GET applications/{application}/context | 当前可选质量范围、批准选项、已准备/执行/封存记录和能力 |
| POST applications/{application}/works | 提交 proposalRef、selectionRef、optionRef、ranges、reason，冻结准备并占用范围 |
| POST applications/{application}/works/{work}/commit | 以 expectedWorkRef、reason 执行真实库存与 Owner 效果 |
| POST applications/{application}/works/{work}/seal | 以 expectedWorkRef、reason 将未执行准备封存为无效果 |
| GET applications/{application}/works/{work} | 当前受权的工作记录 |
| GET applications/{application}/works/{work}/outcome | PENDING / FOUND / SEALED_NO_EFFECT |
| POST commands/query | 以 applicationId、commandId 查询原命令结果 |

写命令必须带 GUID `Idempotency-Key`。未知属性拒绝；数量使用最多 8 位小数的十进制字符串，范围为半开区间，客户端不能先转为浮点数。准备记录单一处置、单一实收行和一个质量分区，允许该分区内多个不连续范围；多个处置可分别推进。

工作状态只允许 `PREPARED → APPLIED` 或 `PREPARED → SEALED_NO_EFFECT`。只有持久 `SEALED_NO_EFFECT` 允许推断无库存效果并释放范围。PREPARED、查询未观察到和请求超时不释放范围；APPLIED 永久保留已消费范围，更正必须通过后续明确的更正合同。

## 权限与一致性

每个入口独立 `query / prepare / post / seal` 权限；写入同时要求 RMA 对应的 `disposition-prepare / disposition-apply / disposition-seal` 权限。当前 RMA、实收入库、Quality、处置 Owner、库位和库存范围均参与授权。可售另要求 Stock QC 的 `return-release` 权限。迁移登记菜单与动作，不自动赋予角色。恢复原结果只需要当前读取权限，但仍检查当前租户、Actor、对象和来源范围，不能借原键绕过撤权。

五个 Owner 在现有 PostgreSQL 业务/权限锁及 RMA 质量锁内串行占用准确实收范围。原根保留 RMA 到 Stock 根坐标映射；无原根的真实实收使用本次生成的根，并按准确实收段映射。不同 Owner 不得重复占用同一实收范围。无法归属的后续旧库存扣减会使根位置不再可信，拒绝继续该来源，待明确更正处理。

准备冻结真实源/目标库存快照；执行再次检查快照、当前质量观察、批准版本、库位可用性、可售属性、容量、召回与自由数量。只消费未分配数量，MOVE / SHIP / LOSS 不扣减既有 `AllocatedQty`。目标接收保留所有权、单位、价格、有效期和召回等原有元数据，不合并不兼容库存身份。

可售放行由 Stock QC 保存准确质量判定、根范围、数量和目的地的独立回执；仅该次进入目标的数量为 Passed，混合来源池不整体变为 Passed。返修不自动放行。

库存流水、根流水对应关系、QC 放行、处置回执、不可变工作终态、outbox 与原命令结果在同一 PostgreSQL 事务提交。任何一步失败全部回滚。提交确认丢失时查询原键；重派完全相同请求返回原结果，改变内容返回绑定冲突，不再移动库存。

`cp6_return_disposition_v2` 保存上述数据，tenant_id 贯穿主键/查询/外键；准备内容与终态回执不可变，只前向迁移。此轮 outbox 为真实待派发事实；外部发送和消费确认不由“已入 outbox”代替。

## 页面与后续范围

RMA 页面提供五种处置选择、准确质量范围、业务选项、准备/执行/无效果关闭和质量过期提示。原请求保存在按租户/Actor 分隔的私有 IndexedDB，用 Web Lock 防止同身份重复派发；先查询再重试，身份/应用切换后丢弃过期显示，保留原恢复记录。

本轮完成五种终端处置的独立 Owner、实际 Stock 参与及页面恢复。实收 VOID/冲正、已执行处置更正与 SX 采用、商业补偿/财务、完整 NCR/返修和外部派发仍继续实施，不能据本轮交付关闭完整 WP10-22 或七天目标。生产部署及既有环境迁移状态独立记录。
