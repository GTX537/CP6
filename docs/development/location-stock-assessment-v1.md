# 库位当前库存事实评估 v1

W215 在准确规则候选上接入独立 Stock 的当前读取权限。入口是 `POST /api/wms/locations/{location}/rules-v1/{proposal}/stock-assessment`，正文仅 `proposal:{owner,id,version,digest}`，携当前候选 `If-Match`。租户、人员、仓库、读取时刻和覆盖状态全部由服务端确定。

该入口调用真实 PostgreSQL `T_Stock`，与规则、库位及仓库授权处于同一 `OperationsOwnerV1` 事务。现有本地表 SHARE 锁防止读库存过程中其他写事务改变已核定的本地范围；独立 Stock 权限和各来源权限在最终提交前再次核对。它不安装跨事务作业保护，不是08E的持久 `ImpactAssessment` 或应用回执。

## 读取和覆盖

- 规则全部相关字段须可读，规则/库位/仓库当前范围须可读，另须独立 `wms-stock:query`。Stock 的创建人、部门及仓库/库区授权继续生效。原数据的价格、客户、货权联系人等无关字段不投影。
- 通过实际仓库/库位代码与当前租户定位原库存，按稳定 StockId 排序，最多返回100行。每行提供实际 StockId、产品/批次、原 UnitCd、PhysicalQty/AllocatedQty/AvailableQty 和原并发版本。数量为固定8位小数字符串，保留 decimal(21,8) 边界。
- `COMPLETE_CURRENT_ROWS` 仅证明当前有权观察该库位完整原生活动库存集合且结果未截断。没有返回行表示该本地活动集合为空，不证明现场物理为空、未收货/预留/任务义务为空。
- `VISIBLE_ROWS_ONLY`：创建人/部门或库区权限不能覆盖完整库位。即使结果为空，也不比较或披露隐藏对象数量。
- `TRUNCATED`：可见记录超过100行。不能拼接不同读取时刻的页面作为原子完整证据。
- `ARCHIVED_BALANCE_UNRESOLVED`：具备完整库位权限时发现归档库存还有非零物理、分配或可用余额。只报告未核清，不暴露归档数量/对象，也不擅自清账。

`ObservedAt` 来自数据库本次实际读取，供时间定位；不是 W212 的认可时钟证据，不证明误差、有效期或时间许可。`SourceOwner=Main.Wms.NativeStockObservation.v1` 标识本读取路径，不替代外部Owner登记、签署、原件或完整覆盖证明。结果不持久化；再次读取会得到当时事实，而非历史重放。

## 容量边界

所有响应固定 `Qualification=CURRENT_NATIVE_STOCK_ONLY`、`OccupancyCoverage=UNKNOWN`、`CapacityQuantity=null`、`AdmissionGrant=false`、`RequiresFinalEffectRecheck=true`。Allocated 属于 Physical，不相加；原单位即使文本相同，也不能据此宣称准确指标、原生单位版本或容量换算已证明。不同产品/单位不合计；负值或物理/分配/可用关系不一致会明确列为待核事项，不修改原记录。

完整占用仍需实际Source/Root/Claim/未收义务、非重叠份额、准确度量及委托归属；规则生效还需完整Owner保护和最终写入复核。初始化隔离、旧 `ReservedCapacityQty`、空查询、净和0、W213已登记份额或W214内部意图均不能代替这些证明。本入口不更新 Selector、候选状态、库位控制、库存或原始操作台账。

## 页面与验证

库位规则候选详情提供“读取当前库存依据”，绑定已保存候选的原件和ETag。切换候选、身份、权限或禁用页面会清除观察；迟到响应须仍匹配当前租户/人员/候选，且覆盖和数量格式一致。无权和错误不显示历史敏感结果。

实际本地 PostgreSQL／Program HTTP、精确数量/独立权限/范围/归档/截断/最终撤权及相关Vue验证见 [W215交付报告](evidence/W215-01/DELIVERY.md)。该证据不等同完整业务或生产环境验收。
