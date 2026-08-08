# E07-S05 存量 WMS 采纳与绑定设计

## 目标

在版本化 Space Design 工作区内完成存量 WMS 库位的拉取、放置、绑定、
未绑定识别和差异对账闭环。WMS 既有码和外部身份保持不变；库存、任务等
运行态数据不得写入 Design Revision。

## 方案选择

采用“站点级 WMS 采纳账本 + 版本化几何绑定”：

- `Space_WmsAdoption` 保存最近一次 WMS 目录观测、外部身份、外部版本、
  状态哈希和稳定的 Space `LocationLogicalId` 绑定。
- `Space_LocationRevision` 继续作为版本内几何与业务编码权威；绑定只把
  WMS 既有码写入 Draft 中的库位修订，并标记为 `Adopted/Bound`。
- 库存和任务继续通过 `ISpaceWmsRuntimeSource` 读取，不进入采纳账本或
  Design Revision。

未采用的方案：

1. 继续扩展旧 `Space_Location` 采纳接口：会绕过版本工作区，并把旧
   Published 物化表误当作设计真相。
2. 把完整 WMS 快照复制进每个 ModelVersion：会产生大量重复数据，且
   容易把运行态和设计态混合。

## 领域模型

`SpaceWmsAdoption` 以
`TenantId + SiteId + AdapterId + WmsLogicalId` 唯一标识一个 WMS 库位，
记录：

- 外部位置 ID、WMS 编码、启停状态、外部版本和状态哈希；
- 最近观测时间；
- `Unbound / Bound / Diverged / MissingInWms` 状态；
- 绑定的稳定 `LocationLogicalId`、绑定时编码和时间；
- SQL Server `rowversion`，防止用户基于过期目录覆盖新观测。

`SpaceLocationRevision.BindAdoptedLocationCode` 只允许首次绑定或同码重放；
已绑定库位不得被另一个 WMS 编码替换。

## 工作流

### 拉取

1. 校验执行上下文、Tenant、Site 数据范围、`space:integration:manage`。
2. 解析 Site 对应 WarehouseCode。
3. 检查适配器能力和健康状态，拉取完整位置目录。
4. 在 Serializable 事务内 upsert 采纳账本；本次未出现的旧项标记为
   `MissingInWms`。
5. 计算差异并同步当前 Draft 的 WMS 采纳问题。

适配器失败或来源不可用时返回可重试的
`SPACE_WMS_UNAVAILABLE`，不修改本地目录。

### 绑定既有几何

把一条或一批 WMS 采纳项绑定到当前 Draft 已存在的未绑定库位修订：

- 目标库位必须属于当前版本、具有 Rack/Floor 几何且为 Active；
- 同一 WMS 项、同一外部编码和同一 Space LogicalId 都只能绑定一次；
- 同一批次先完整校验，再在单事务中原子提交；
- 成功后 LocationCode 保持 WMS 既有码，`CodeOrigin=Adopted`，
  `ExternalBindingState=Bound`。

### 放置

当货架格口尚无 Location Revision 时，用户可把 WMS 项直接放到指定
`Rack + Column + Level + Depth`。服务从货架层规格推导尺寸，以 WMS
LogicalId 作为稳定 LocationLogicalId 创建 `Adopted/Bound` 修订。

### 对账

列表实时组合采纳账本与目标版本 Location Revision，输出：

- `SPACE_WMS_LOCATION_UNBOUND`：WMS 有、尚未绑定几何；
- `SPACE_WMS_BINDING_GEOMETRY_MISSING`：已有绑定，但目标版本缺少几何；
- `SPACE_WMS_BINDING_CODE_MISMATCH`：WMS 码、绑定时码和版本内码不一致；
- `SPACE_WMS_LOCATION_CODE_DUPLICATE`：同适配器目录重复编码；
- `SPACE_WMS_LOCATION_MISSING`：Space 已采纳，但最新 WMS 目录不存在。

未绑定为 Warning；重复、缺失几何、编码漂移和 WMS 缺失为 Blocking。
刷新、绑定和放置后同步 `Space_ModelIssue`，已闭合差异自动 Resolve。

## API

路由位于 `/api/space/design/v1/versions/{versionId}/wms-adoption`：

- `POST /refresh`
- `GET /locations?status=&differenceCode=&limit=&cursor=`
- `POST /locations/{adoptionId}/bind`
- `POST /bindings:batch`
- `POST /locations/{adoptionId}/place`

读取使用 `space:model:read`，刷新使用 `space:integration:manage`，绑定与
放置同时要求 `space:model:edit`。所有错误使用 RFC Problem Details 和
稳定 `SPACE_*` 错误码。

## 编辑器

Design V1 楼层编辑器新增 WMS 采纳面板：

- 刷新 WMS 目录并展示总数、未绑定数和差异数；
- 按状态/差异过滤；
- 选中货架后，把 WMS 项放到下一个空格口；
- 将选中货架内按列、层、深排序的未绑定几何与 WMS 项批量自动配对；
- 展示未绑定、缺失、重复和编码漂移，不把模拟来源标成真实 WMS。

前端只调用 Design V1 API；旧 `/api/space/location/adopt` 和旧
`BindCodesDialog` 保留兼容，但不作为本卡的新链路。

## 并发与恢复

- 目录刷新使用 Serializable 事务和唯一索引。
- 写命令携带采纳账本 `rowversion`；过期提交返回
  `SPACE_CONCURRENCY_CONFLICT`。
- 批量绑定最多 1,000 项，任何一项失败整批不写入。
- 重复刷新幂等更新同一账本，不创建重复采纳项。
- Migration 回滚只删除采纳账本；执行前应导出账本审计副本。

## 测试

- Domain：观测、漂移、缺失、同码重放和换码拒绝。
- Application/Infrastructure：拉取幂等、跨租户/跨 Site、WMS 不可用、
  单项/批量绑定、放置、重复位置、过期 rowversion、问题开闭环。
- SQL Server：迁移、唯一索引、rowversion 和原子批量。
- API/OpenAPI：路由、权限、Problem Details 和 DTO schema。
- Frontend：API 参数、差异过滤、自动配对、空格口放置和只读版本禁用。
- 回归：Space Unit、Integration、真实 SQL 门禁、前端 test/type-check/build、
  全解决方案 build 和 EF pending-model 检查。

