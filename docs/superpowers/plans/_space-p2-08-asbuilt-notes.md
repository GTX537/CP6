# Space P2/P3 · 08 高级可视化 — as-built 调研笔记(写 Plan 08 前读)

> 本会话(07 收官后)为 Plan 08 预探的 as-built 事实。新窗口写 Plan 08 时直接引用,**不必重跑探查**。设计源=reconcile spec §4 + 丛书 `docs/space/08-advanced-viz.md`(已审)。

## 1. Aisle 中心线几何(拣货路径核心)
- `Space_Aisle.Centerline`(nvarchar(max),默认 `"[]"`)= JSON **`[[x,y],[x,y],…]`**(坐标对数组,**mm 数据空间**)。生成器 `genZoneArray.ts` 写 `[[startX,centerY],[endX,centerY]]`;编辑器 `SnapEngine.ts` 解析 `JSON.parse(a.centerline) as [number,number][]`。
- 前端 `types/space/scene.ts` `AisleVO` 有 `centerline: string`(+`polygon: string`)。
- 后端 `CP6.Entity/DTOs/Space/SpaceMasterDtos.cs` `AisleDto` 有 `Centerline`/`Polygon`。**scene 端点 `/floor/{id}/scene` 返 `aisles[]` 含 centerline** → 前端 PickPathPlanner 建 Dijkstra 图。

## 2. StockTransaction(作业热图源)
- `CP6.Entity/DomainModels/Wms/StockTransaction.cs` : `BaseBizEntity`(租户全局过滤)。字段:`TxnNo`/`TxnType`(IN/OUT/MOVE/ADJ/RSV/UNRSV)/`TxnDateTime`/`LocationCd`(join key)/`ProductCd`/`LotNo`/…。DbSet=`db.StockTransactions`。
- `IWmsWorkloadQuery.GetWorkloadAsync(floorId, from, to)` → 实现:`StockTransactions.Where(TxnDateTime∈[from,to] ∧ LocationCd∈该层Placed编码).GroupBy(LocationCd).Count()` → `WorkloadDto{LocationCode,OpCount}`。可选只计 OUT/PICK(plan 定,默认全 TxnType)。

## 3. 拣货路径源 OutboundOrder/Detail
- `OutboundOrderDetail`:`OutboundNo`/`LineNo`(拣货序)/`ProductCd`/`RequiredQty`/`LocationCd?`。业务键 `(OutboundNo,LineNo)`。
- `IWmsPickTaskQuery.GetPickPathAsync(taskNo)` → `OutboundOrder.Single(OutboundNo==taskNo ∧ Status==Picking(3))`;`OutboundOrderDetail.Where(OutboundNo==taskNo ∧ LocationCd!=null).OrderBy(LineNo)` → `PickStop{Seq=LineNo,LocationCode,Qty=RequiredQty,MaterialNo=ProductCd}`。
- `OutboundOrderStatus`:Draft0/Confirmed1/Allocated2/**Picking3**/Completed4/Cancelled9。

## 4. 拣货 AbsXYZ 解析(后端端点做)
- `Space_Location` 有 `AbsX/AbsY/AbsZ`(int?,mm,发布后不变缓存)。`/pick-path` 端点服务端 join `Space_Location`(FloorId∧Placed∧LocationCode)→ 每 PickStop 补 AbsXYZ 返前端(前端不另查)。

## 5. 07 着色管线复用(作业热图)
- `space-viewer/overlay/StockOverlay.ts` 公开面:`setSnapshot(items,ts)`/`setMode(OverlayMode 'status'|'utilization'|'off')`/`apply()`/`refresh(floorId)`/`getStock(code)`/`startPolling`/`stopPolling`/`dispose`。
- `apply()` 范式:遍历 `_byCode` → `id=viewer.getLocationIdByCode(code)` → `viewer.setInstanceColor(id,hex)` → `viewer.requestRender()`。`stockModel.ts`:`binStatusToHex`/`utilizationToHex(冷蓝→暖红 lerp)`/`locationUtilization`。
- **建议**:08 作业热图用**兄弟类 `WorkloadHeatmap`**(复制 StockOverlay 的 apply→setInstanceColor→requestRender 范式,数据=`{locationCode→opCount}` Map,色=opCount 归一化→冷暖),比给 StockOverlay 加 'workload' 模式(联合类型)更干净。

## 6. viewer 动画/挂点钩子(都已具备)
- `ViewerHandle`:`getSceneRoot():Group`(加路径线/小车/设备 mesh)、`dataToWorld({x,y,z})`、`requestRender()`、`getCurrentFloorId()`、`getLocationCode`/`getLocationIdByCode`、`flyToData`、`focusBox`。
- `core/SceneRoot.ts`:`scale 0.001`(mm→m)+`rotation.x=-π/2`(Z-up→Y-up);`dataToWorld=localToWorld(Vector3(x,y,z))`。**08 一律在数据空间(mm)算,放 mesh 前 `dataToWorld` 转换**。
- `core/Loop.ts`:按需渲染,`markDirty()` 触发一帧;无逐帧回调。**PathAnimator 动画**:自有 `requestAnimationFrame` 每帧更新小车位置 + `viewer.requestRender()`(或 `loop.addThrottledTask(fn,16)` ~60fps,但 Loop 未对外暴露 → 用自有 RAF 最简)。

## 7. 契约位置 + 文件结构
- 3 契约加到 `CP6.Core/Services/Integration/IWmsStockQuery.cs`(同族,**均不存在,新建**):`IWmsPickTaskQuery`/`IWmsWorkloadQuery`/`IWmsDeviceQuery`(+DTO `PickPathDto`/`PickStop`/`WorkloadDto`/`DeviceDto`)。
- WMS 实现新建 `CP6.Core/Services/Wms/`:`WmsPickTaskQuery.cs`/`WmsWorkloadQuery.cs`/`WmsDeviceQuery.cs`(设备桩返空)。DI 注册 `Program.cs`(仿 07 line 374 区)。
- Space 端点 `CP6.WebApi/Controllers/Space/`:新 `SpaceAdvancedController`(或扩 SpaceStockController):`/floor/{id}/pick-path?taskNo=`、`/workload?from=&to=`、`/devices`。`[ApiController][Route("api/space")][Authorize]`+`Ok2{code,message,data}`。
- 前端**新建 `cp6.web/src/space-viewer/advanced/`**(不存在):`PickPathPlanner.ts`(中心线图+Dijkstra/A*)、`PathAnimator.ts`(播放/暂停/步进/调速/重播)、`WorkloadHeatmap.ts`、`DeviceLayer.ts`(占位)。+`api/space/advanced.ts`+`types/space/advanced.ts`。UI 控件挂 `FloorViewer.vue`(无新路由)。

## 8. 08 演示种子缺口(gstack QA 用)
- 拣货路径:一张 `OutboundOrder`(Status=Picking 3)带**多条有序明细**跨真实编码 `A-01-01-01/A-01-01-02/A-01-02-01/A-01-02-02`(LineNo 1..4)。
- 作业热图:若干 `StockTransaction` 行(各库位不同 opCount + 落在查询时间窗内)。
- 拣货路径要"沿巷道"好看需对应 `Space_Aisle.Centerline` 有数据(QA 库当前 aisle 是否有中心线待查;无则退化直连 + W-SPACE-801,可接受)。
- 真实环境:Site QAWH `F31F48C2-81D5-4BA7-AFF1-83DA8D87C2FE` / Floor `5C92E6A8-C4C8-4D91-9DDC-EA9C54B6961F`;TenantId `…A1`;隔离库 `CP6DB_SpaceQA`。

## 坑(07 实测,08 沿用)
- raw SQL `[LineNo]`(LINENO 保留字);sqlcmd `-Q` 传中文注释乱码→种子纯 ASCII;WMS DbSet 复数。
- gstack:el-input v-model 用 `click+type` 非 `fill`;合成 wheel 拉不到 near LOD(盒子像素色难肉眼核→靠 API/InfoCard/单测闭环);viewer 需 `?floorId=`;登录 admin/123456 `POST /api/auth/login`{userName,password},dev Csrf 关。
