# Main 调拨请求原生入口 v1

W235，服务 `Main.Wms.TransferRequest.v1`，命令合同 `Main.Wms.TransferRequest.commands.v1`。这是补货 DIRECT_MOVE 的原生请求前置；完整 TF v1.1 的路由、ApprovalView、外部 SourceDemand 与 Stage 协议继续按[原设计](../design-handbook/modules/WMS-TRANSFER-01.md)实施。本入口单独版本化，不宣称替代完整 TF v1.1，也不改变已接受合同的合法输入或 Owner 身份。

## 已实现业务

请求状态为 `DRAFT → SUBMITTED → APPROVED / REJECTED`；`DRAFT / SUBMITTED → WITHDRAWN`。批准和拒绝必须由当前具有 `wms-transfer-request:approve` 权限、对象及双库位范围的另一人执行，禁止创建人、最后内容作者或送审人自审。批准原件固定请求版本、完整内容摘要、实际审批人和时间；内建策略版本为 `MAIN_TRANSFER_INDEPENDENT_APPROVAL_V1`。这不是外部工作流终审回执。

每个创建人在租户内的 `draftKey` 唯一。操作键、理由以及输入只在入口校验和冻结；相同操作键/输入返回保存原结果，改变输入返回冲突。编辑无变化时不增加内容版本或状态修订；改变内容只允许草稿，以稳定 `clientLineKey` 保留行身份。明细按 UTF-8 字节序规范排序，数组重排不会制造新内容。历史修订不可修改。

每行实际采用原生库存产品目录、货权/用途、商务与工程产品、技术身份和 UOM。数量使用正的固定八位小数字符串，最大 `9999999999999.99999999`；采用现有整数有理数换算，不做浮点计算或有损舍入。产品和单位来源在同一事务内按不同引用缓存；提交和批准时重新核对会变化的当前业务条件。

创建时登记本地 `WMS_TRANSFER` SourceDemand 和 `[0, quantity)` 双轴绑定，使用已存在的 `TransferDemandBudgetV1`。编辑在一个事务内关闭旧绑定/需求，再登记新内容版本的需求/绑定；撤回或拒绝关闭全部绑定及需求。请求、不可变历史、待发布事件、部门映射及原操作日志共同提交。失败全部回滚，提交响应丢失保留 UNKNOWN 语义，经原操作查询恢复。

仅完成请求批准时，返回 `executionState=NOT_DISPATCHED`。请求批准不写 Stock、Movement、Claim 或容量。W236 已增加[原生执行阶段](transfer-native-stages-v1.md)，阶段执行后才根据实际 Stock 双腿回执更新移动状态。`cp6_transfer_request_v1.events` 保存原子待发布原文；尚未实现该事件的运输消费者，不能据此声称消息已投递。

## 入口

根路径 `/api/wms/transfer/native-requests-v1`；身份与 CSRF 沿用宿主。输入是封闭 JSON 对象，响应 `{code:0,data:...}` 且 `no-store`。

| 方法与相对路径 | 行为 |
|---|---|
| GET `context` | 当前身份、可选部门、产品、单位和已登记站点的库位 |
| GET `?page=1` | 每页最多50条可见请求，不返回隐藏请求详情 |
| POST 根路径 | `operationId,draftKey,departmentId,lines,reason` 创建草稿 |
| GET `{id}` | 当前请求和状态允许的动作；动作仍需当前权限 |
| POST `{id}/edit` | `operationId,expectedRevision,etag,lines,reason` |
| POST `{id}/decisions/{operation}` | `SUBMIT/APPROVE/REJECT/WITHDRAW`；`operationId,expectedRevision,etag,contentDigest,reason` |
| GET `{id}/revisions/{revision}` | 原历史快照，仍要求当前对象和库位读取范围 |
| GET `operations/{id}` | `UNKNOWN/COMMITTED/CLOSED` 原操作状态 |
| POST `operations/{id}/close` | 原子关闭尚未提交操作；已提交者返回原结果 |

明细字段见 `TransferRequestLineInputV1`：`clientLineKey,productCatalogueRef,inputUomRef,inputQuantity,sourceWarehouseId,sourceLocationId,destinationWarehouseId,destinationLocationId`。最大500行、2MiB。更新使用正文中的准确 revision 和强 ETag；这里不声称实现 TF v1.1 的 `If-Match` wire。

独立部门映射类型 `transfer-request` 已加入 IAM 管理入口。本人范围采用创建人 UUID；部门、本部门子树、自定义部门和全部范围复用现有规则。源和目标库位分别受当前 WMS 仓库/区域授权约束。

## 环境与模拟数据验收

沿用服务端 `TransferEnvironment` 和原生来源登记。`TransferRequests:WarehouseSites` 是显式的 `{warehouseId,siteId}` 数组；仓库属于当前租户，站点必须为本机当前原生 PhysicalSite 来源。本轮没有已存在的正式仓库→站点关系可直接采用，因此将这个关系放在服务端配置边界，不从 `BaseCd`、名称或调用人推断，也不接受客户端覆盖。

生产业务验收允许构造这些业务数据和配置。`TransferRequestPostgreSqlTests` 创建明确模拟的租户、两个独立用户、产品/UOM/库存身份、站点、仓库和库位；实际执行 Owner、PostgreSQL 迁移/事务、权限、HTTP 登录/CSRF和审批。无需真实企业资料。测试环境是本地隔离 PostgreSQL 18 / .NET 宿主，不等同于生产部署。

W236 已完成原生库存池 Stage/Stock 同事务移动与阶段恢复。后续仍须完成已有目标中的 SourceDemand 共享义务采用、正式审批适配、完整 Root/Slice/Claim 与 typed 容量协议、补货派发及取消/恢复。这些内容不能由原生请求或阶段的局部通过结果关闭 WP10-10/WP10-31 全任务。
