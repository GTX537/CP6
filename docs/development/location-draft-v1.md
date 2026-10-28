# 原生库位隔离草稿 v1

Owner `Main.Wms.LocationDraft.v1`，Journal `LOCATION-DRAFT/1`。本窗口落实 WMS-MASTER-01 v1.1.1 §6.2/6.2.1 的受限创建路径；完整 MASTER、规则、启用及源采纳合同未关闭。

## 适用范围与当前依据

只支持 W206 已有真实物理 Site 关联、仍受数据库业务使用屏障保护的 DRAFT 仓库。旧仓库没有可信 Site 关联时返回422 INITIALIZATION_FENCE_UNSUPPORTED，不猜 BaseCd，不修改历史库位。非空条码保留合法50 UTF16输入，但因真实扫描别名政策来源尚未采用返回 LOCATION_BARCODE_SOURCE_UNAVAILABLE；不是把合同改为永久只允许null。

初始化能力固定为 `Main.Wms.LocationInitialization.DraftWarehouse.v1`。这是当前 Main 构建、本地 PostgreSQL 验证覆盖的实际消费范围；不证明未知旧宿主、任意直接SQL读取者或生产部署已经升级。启用新模式前仍必须核对所有实际宿主采用。

## 原子创建与身份

`/api/wms/warehouses/{warehouseId}/location-drafts-v1`：GET分页列表、GET `/{id}`、GET `/scope`、GET `/creation-context`、POST创建、GET `/operations/{uuid}`、POST `/operations/{uuid}/close`。闭合DTO包含operationId、authorityVersion、departmentId、expectedPolicyVersion、expectedHierarchyRevision、locationCode、locationName?、必传显式null/Guid的parentLocationId、必传1–5的locationLevel、barcode?。正文不允许租户、仓库/Site覆盖、初始化状态、容量/预占或规则。

当前原始会话的 wms-location add/query、字段权、部门范围1–5、仓库query及WMS整仓范围、Site query共同参与。新增部门绑定票据只绑定本事务新分配ID，不是已有对象重绑定。最终当前授权及前驱变化使本体/绑定/结构版本/Journal/Audit全部回滚。原回执按当前query和原actor披露，add撤销后仍可恢复，未知结果不能当作无效果或重新创建；关闭与创建串行。

编码NFC/Trim、保留显示大小写，原生30 UTF16；canonical NFC+InvariantUpper上限300，在Tenant+WarehouseId内唯一，原生 PostgreSQL 相等性另核同仓碰撞。跨仓同码合法。旧非隔离行继续保留原租户内编码唯一，新所有行还受Tenant+WarehouseCd+LocationCd唯一索引约束。已存在/已删除锚仍参与。全表或双库没有重编码。

层级采用 W207 当前显式策略与两个真实版本，原生表和层级控制行锁定后再次核查。父必须有当前读取权、同仓的正式草稿稳定ID；DRAFT父可组织新草稿，不授予使用资格。ParentId与旧ParentLocationCd同事务保存，原生触发器推进HierarchyRevision；更换策略必须检查包含隔离草稿的完整实际树。独立结构预检仍不授予创建/初始化能力。

## 隔离范围

新增原生布尔 IsInitializationQuarantined=true，旧行默认false只表示历史路径，不赋予已核生命周期。正常 CP6Context Location 查询同时保留Tenant条件并排除隔离行。实际旧读取与选择包括 Mobile/MobileTaskV2、Lpn、Replenish、Slotting、WmsStockQuery、WmsRoleScope，以及 Inventory/Reports/Kitting/RMA/生产入库/工具等 OperationsAccess 路径。当前源码未发现绕过此过滤的业务原生Location SQL读取；新Owner、层级兼容检查及名称Owner的明确例外均保留显式Tenant及当前对象/部门/区域权限，且不返回可消费数量。

原生仓库使用触发器持续保护 Stock/StockTransaction、MobileTask/Reservation、LPN/Serial、WmsBin、WCS、入出库等含仓库身份/码的真实表。唯一新增例外是同事务已插入正式初始化元数据的原生库位；其元数据受延迟外键及真实Owner Journal约束，不能靠一个请求布尔值绕过。

油墨、扫码别名及无仓库列的入库/退货明细另有原生库位码保护。旧消费者的码属于原全租户唯一的非隔离命名空间；已有真实旧同码对象可继续使用，不误伤跨仓旧业务。仅命中隔离草稿而没有该旧身份时禁止写入；既有悬空码阻止新草稿采用。扫码别名 LOCATION TargetKey同样保护。Space_Location仍属于独立源目录，不能被当作本域L；其业务落点WmsBin继续受仓库屏障约束。

运行时核查当前模型过滤、真实控制触发器、仓库消费者列/触发参数、封闭的27表库位字段清单以及无仓库列消费者保护。缺失、停用或新增未登记消费者时无能力且零正式创建。新增消费范围必须独立核实并更新能力版本，不能自动登记未知消费者为已合格。

## 初始化事实与独立读取

新对象固定DRAFT/MANUAL、业务版/控制版1/1、QUARANTINED、MANUAL_CREATE及NEW_TECHNICAL_PLACEHOLDER。IsBlocked=true、IsPickable=false；Area/允许品类/坐标/条码null；旧容量/预占技术0。安全读取明确TECHNICAL_PLACEHOLDER、typed容量UNKNOWN、预占UNKNOWN/null、Selector0/CurrentRule null，AdmissionGrant=false。不伪造Space来源、空库存/空预占或旧不限容量。

数据库禁止清隔离、删除/搬移/改码、改容量/预占/状态；名称维护复用既有MASTER-NAME/1同事务回执，实际变化只升业务版，无变化保留版本。正常旧主数据数量观察排除这些新草稿，专用页显示准确未核定状态。

## 页面与后续

`/wms/warehouse/{id}/location-drafts`从仓库草稿列表进入。显式选择层级与根/有权草稿父，只显示当前页可见且符合政策的候选，并说明分页范围不足以证明无其他节点。当前版本再次核对；浏览器origin+tenant+actor+warehouse下只保留原UUID，Web Locks互斥，不重发UNKNOWN；身份/路由变化、401/403/404及晚响应清理私有显示。

READY_TYPED、激活、规则、实际库存/预占Owner、完整生命周期保护、Space映射与正式Transfer效果仍待各自真实前置。此轮不清旧余额、不开放作业，不修改W205固定九源profile，也不执行既有环境迁移或Actions。
