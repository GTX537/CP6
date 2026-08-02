# Space E11-S01 运营诊断口径实现计划

状态：功能验证完成，待受控集成  
起始基线：`b6770e7943a7daf3d313e91d695f78ef808ec6f3`  
功能分支：`codex/space-e11-s01-operational-diagnostics`

## 1. 为什么现在做

E03-S01～S03、E13-S16 和 E10-S01～S06 已完成。E02-S01 于 2026-08-02
重新执行严格审计和 ODA/APS preflight：工具测试 10/10，Development Seed
完整性及两项容量资产通过，但正式 20 份授权黄金集、DWG/DXF 版本矩阵、法务、
SDK/凭据和冻结 Worker 仍缺失，退出码保持 `3/4/4`。因此 E02-S02 及其下游
MVP 链不能合法启动。

E11-S01 只依赖已完成的 E10。它作为 CAD 外部闸门等待期间的独立卡推进，
不得改变 E02/E03/E13/E06 的优先级，也不得把诊断扩展成建议或自动执行。

## 2. 冻结 HTTP 与授权合同

- 新增内部只读端点：
  `GET /api/space/operations/v1/sites/{siteId}/diagnostics?fromUtc=&toUtc=`。
- 新增权限：`space:operations:diagnostics:read`，仅通过现有 Space 管理员种子授予；
  外部 Portal 主体即使误获权限也必须在站点访问和运行态查询前拒绝。
- GET 使用 `space.operations.diagnostics.read` 强制审计；审计写入失败时失败关闭。
- 请求不接受 tenant、Published version、来源身份、人员身份或阈值覆盖。
- 时间窗是 UTC 半开区间 `[fromUtc,toUtc)`，`fromUtc < toUtc <= now`，最长
  24 小时且必须位于现有 30 天人员轨迹保留窗口内。
- 单次最多读取 100,000 条人员位置证据；超过返回 422
  `SPACE_OPERATIONS_DIAGNOSTICS_EVIDENCE_LIMIT`。
- 响应口径版本固定为 `space-operations-diagnostics-v1`；即时计算，不持久化，
  不新增 Migration。

## 3. 人员证据与隐私边界

- 只读取 E10-S01 的 `PositionObserved` 事件；只纳入 `Real` 来源，模拟事件
  单独计为排除数，不与真实结果混合。
- 人员分组键只在进程内由 `SourceId + PersonExternalId` 构成，响应不返回人员 ID、
  外部人员号、UserId 或可逆匿名键。
- 事件必须属于请求 Site，且楼层/库位仍属于当前 Published/Active 模型；旧版本或
  无法映射的事件计为排除证据并打断连续轨迹，不映射到相似编码。
- 响应返回来源、事件/人员数、首末观察和接收时间、模拟/模型外排除数及局限说明。
- 当前人员事件没有任务身份，因此本卡不伪造“任务路径”或任务计数。

## 4. 诊断定义

### 4.1 路径距离

- 只比较同一内部人员键、同一楼层、时间严格递增且相邻不超过 300 秒的连续位置。
- 两端有 XY 时按毫米二维欧氏距离；同一 LocationLogicalId 且缺坐标时只能确认
  0 位移；跨层、超时、不同无坐标库位或模型外点均为未知段。
- 分别返回已知段、未知段和已观察距离，不插值、不补楼层间直线。

### 4.2 折返

- 仅使用两个连续、可比较且各至少 1,000 mm 的向量段。
- 转向夹角 `>=150°` 计为折返；最多返回 100 条稳定排序证据。
- 证据可以包含受审计保护的时间、楼层、库位和转折坐标，但不含人员身份。

### 4.3 停留

- 同一人员在同一 Published/Active 库位、同一楼层的连续观测，间隔 `>0` 且
  `<=300s`，合并为一个 observed-presence episode。
- episode 至少 300 秒才计为停留；位置/楼层变化、模型外点或时间断裂立即结束。
- 返回总 episode、人数、库位、总/最大停留时间及最多 100 个热点。

### 4.4 拥堵

- 拥堵只表示同一库位 observed-presence 半开区间的重叠，不等于物理碰撞、
  通道堵塞或传感器覆盖区外的人群密度。
- 至少两个不同内部人员键同时存在才形成热点；按事件扫描计算峰值人数和重叠秒数，
  不用采样点数量替代并发人数；最多返回 100 个热点。

### 4.5 容量与占用压力

- WMS 只提供当前正物理库存位置，和人员历史窗口不是同一时点；来源时间必须独立展示。
- 返回当前 Published/Active 库位数、正库存去重占用库位数和全仓/分层占用率。
- `<85%` 为 Normal，`>=85%` 为 Watch，`>=95%` 为 Critical；该状态命名为
  `LocationOccupancyPressure`，不是容量利用率。
- 因没有库位容量主数据，`CapacityUtilizationPercent` 固定为 `null`，状态
  `Unavailable`，原因 `WMS_LOCATION_CAPACITY_NOT_AVAILABLE`；不得从库存数量、
  货架体积、面积或占用率反推容量。
- WMS 不可用时人员诊断仍返回，容量/占用字段为空并保留明确不可用原因；WMS 合同
  越界、Site/Published/Location 不一致仍整体失败关闭。

## 5. Viewer

- Viewer 工具栏新增 `DIAG` 开关，默认最近 8 小时，支持 1/8/24 小时重算。
- 面板展示路径覆盖质量、折返、停留、observed co-presence、库位占用压力、
  真正容量不可用状态、人员/WMS 各自来源时间和限制。
- 停留/拥堵/折返/分层占用项可通过现有 Locator 定位；无库位编码时不猜测。
- 较旧并发请求、关闭面板和组件卸载后的响应不得覆盖新状态；失败保留最后成功结果。
- 本卡只读，不改变库存、ABC、空间筛选、作业热图或设备/人员图层的颜色权威。

## 6. 门禁

- 纯计算引擎：路径已知/未知、跨层/超时、折返阈值、停留边界、半开区间拥堵、
  稳定排序与 100 条截断。
- 服务：UTC/保留期/24 小时/100,000 上限、内部主体、站点范围、Published 模型、
  Real-only、模型外证据、WMS 可用/不可用/越界和零库位。
- API/权限/种子/审计：稳定路由、参数表面、管理员幂等授权、外部拒绝和 Problem Details。
- 前端：API 参数、空/加载/失败/最后成功、容量不可用、窗口切换、热点定位和旧响应保护。
- Space Unit、默认 Integration、CP6.Tests、前端全量、TypeScript、生产构建、solution
  Release、EF pending model、SDK drift 与 `git diff --check`。

## 7. 明确不做

- E11-S02～S06 的推荐、调度、审批、WMS/WCS/PDA 写入、回执、补偿和收益评估。
- 速度/连续轨迹插值、楼层间距离、真实通道堵塞推断、体积/重量/托盘容量。
- 外部 Portal、历史诊断存储、趋势或预测。
- CAD、AI Provider、E02/E03/E13/E06 主链的任何依赖绕过。
