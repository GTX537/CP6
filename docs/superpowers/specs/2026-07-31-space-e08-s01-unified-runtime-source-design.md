# E08-S01 统一运行态数据源设计

- 状态：已确认，待实施
- 日期：2026-07-31
- 目标卡：E08-S01
- 前置：E07-S01～S03；E07-S05 的身份绑定作为已集成能力使用

## 1. 背景与当前缺口

E07 已经提供只读底层合同 `ISpaceWmsRuntimeSource`，生产
`Cp6SpaceWmsAdapter` 与 `StandardSpaceWmsSimulator` 也已经使用相同的
`SpaceWmsInventoryResult` 和 `SpaceWmsTaskResult`。该层解决了“不同 WMS
来源如何返回同形记录”的问题，但它仍以 WMS Logical ID 为查询边界，不知道当前
Published 空间版本、站点数据范围、设计库位身份或 E07-S05 的采纳绑定。

现有 Viewer API 直接依赖旧 CP6 查询服务和 Published 物化表，范围校验、来源语义及
Space/WMS 身份没有统一收口。因此 E08-S01 不重造底层适配器，而是在其上增加一个
面向 3D 运行态消费者的只读服务纵切。

## 2. 目标

1. 生产 WMS 与标准模拟器通过同一个运行态服务返回完全相同的公开库存/任务 DTO。
2. 运行态查询只覆盖调用租户有权读取的站点当前 Published 版本及其中 Active 库位。
3. 同时保留 Space 几何身份和原始 WMS 身份，明确暴露编码漂移。
4. 支持标准仓最多 10,000 个库位，并通过固定批量查询避免 N+1 WMS 调用。
5. 任何响应都明确声明 `Real`、`Simulated` 或 `Unavailable` 来源；空数据不能隐藏来源状态。
6. 为 E08-S02～S04 提供稳定的后端 API 和测试边界。

## 3. 非目标

- 不实现来源接收时间、延迟、时钟偏移、最近成功或最近失败展示；这些属于 E08-S02。
- 不实现按物料、批次或容器定位结果体验；这些属于 E08-S03。
- 不实现任务路径、优化顺序或工作量展示；这些属于 E08-S04。
- 不实现 10,000 库位前端渲染性能门槛；这些属于 E08-S05。
- 不修改 Design Revision、Published 物化数据、WMS 数据或 E07-S05 采纳台账。
- 不替换旧 Viewer API；后续卡片迁移消费者后再决定其兼容退场方式。
- 不新增数据表或 EF Core Migration。

## 4. 方案选择

### 4.1 采用：Published 运行态服务层

新增应用服务接口、公开合同、基础设施实现和两个只读 API。服务统一负责范围解析、
身份映射、分块访问、合同防御与稳定排序，控制器只负责 HTTP 绑定和权限声明。

该方案保持 `ISpaceWmsRuntimeSource` 的读写隔离，同时让后续库存定位、任务路径和来源
新鲜度能力都依赖同一个可信边界。

### 4.2 未采用：控制器直接调用 WMS 来源

该方案文件较少，但会把 Published 范围校验、E07-S05 身份映射和错误语义分散到多个
端点，后续 E08-S03/S04 会重复同一套逻辑。

### 4.3 未采用：库存与任务聚合为单一快照

一次调用看似简单，但库存和任务的筛选、刷新频率和失败方式不同。聚合会放大响应并使
一类来源故障阻断另一类数据，不适合 10,000 库位基线。

## 5. 架构与组件

### 5.1 底层来源合同

保留现有 `ISpaceWmsRuntimeSource`：

- `QueryInventoryAsync(SpaceWmsInventoryQuery)`
- `QueryTasksAsync(SpaceWmsTaskQuery)`
- 稳定的 Runtime Adapter ID、Data Source ID 和 Data Source Kind

生产适配器与模拟器继续负责把各自内部实体转换成相同的 WMS 层记录。运行态服务只能
获得只读来源合同，不能获得 `ISpaceWmsAdapter` 的预检、发布或写入能力。

### 5.2 应用服务

新增 `ISpaceWmsRuntimeService`：

- `QueryInventoryAsync(siteId, locationLogicalIds?, cancellationToken)`
- `QueryTasksAsync(siteId, locationLogicalIds?, cancellationToken)`

接口只使用站点与 Space Location Logical ID。WMS 上下文、Published 版本和身份转换由
实现内部完成，调用方不能绕过范围解析直接传入 Warehouse Code 或 WMS Logical ID。

### 5.3 基础设施实现

新增 `SpaceWmsRuntimeService`，依赖：

- `SpaceContext`：读取当前 Published 版本、场景库位和 E07-S05 绑定；
- `ISpaceExecutionContext`：强制存在租户和操作者上下文；
- `ISpaceDesignAccessEvaluator`：执行站点只读数据范围；
- `ISpaceWarehouseResolver`：把站点解析为 WMS Warehouse Code；
- `ISpaceWmsRuntimeSource`：访问已选择的真实或模拟只读来源。

服务不持久化任何状态。

### 5.4 HTTP 层

使用独立控制器承载运行态端点，避免继续扩大现有
`SpaceDesignV1Controller`：

- `GET /api/space/design/v1/sites/{siteId}/runtime/inventory`
- `GET /api/space/design/v1/sites/{siteId}/runtime/tasks`

两个端点接受重复的 `locationLogicalId` 查询参数；未提供时表示查询当前 Published
版本内全部 Active 库位。端点要求 `space:model:read`，不应用会删除来源字段的动态字段
过滤。

## 6. 公开响应合同

### 6.1 来源

`SpaceWmsRuntimeSourceDto` 包含：

- `kind`
- `dataSourceId`
- `observedAtUtc`
- `isSimulated`
- `isAvailable`

`kind` 使用稳定字符串 `Real`、`Simulated` 或 `Unavailable`。E08-S01 只冻结适配器
已能可靠提供的字段；E08-S02 再增加接收时间、延迟及健康历史。

### 6.2 库存

`SpaceWmsRuntimeInventoryResponse` 包含站点 ID、Published Version ID、Warehouse Code、
来源和项目集合。每个项目包含：

- Space Location Logical ID 与 WMS Logical ID；
- Space Location Code、WMS Location Code 与 `codeMatches`；
- Floor Logical ID、Floor Code、Floor Name、Floor Level；
- Physical Quantity、Allocated Quantity；
- Material Number、Lot Number、Container Number、Owner ID。

### 6.3 任务

`SpaceWmsRuntimeTaskResponse` 使用相同的顶层上下文。每个项目包含：

- Task ID、Task Type、Status、Sequence Number；
- Space Location Logical ID 与 WMS Logical ID；
- Space Location Code、WMS Location Code 与 `codeMatches`；
- 楼层、区域、货架身份；
- 可用的 X/Y/Z 毫米空间锚点；
- Quantity 与 Material Number。

空间层级与锚点由 Published 场景补充，WMS 来源不负责理解设计几何。

## 7. 查询与身份映射流程

1. 要求有效执行上下文，校验 `siteId`，执行站点只读数据范围。
2. 解析站点对应 Warehouse Code 和当前 Published Version；没有 Published Version 时
   使用现有领域错误语义拒绝请求。
3. 从当前 Published 版本加载 Active 库位。若请求指定 Location Logical ID，则每个 ID
   必须属于该集合；任何无效、停用或非 Published ID 都整体拒绝，且不访问 WMS。
4. 加载 E07-S05 已绑定记录。普通库位以 Space Logical ID 作为 WMS Logical ID；采纳库位
   使用绑定记录中的原始 WMS Logical ID。
5. 对映射后的 WMS ID 去重，最多允许 10,000 个，每 500 个为一批调用底层来源。
6. 每批响应先做来源和项目合同校验，再将 WMS ID 映射回 Space 几何身份。
7. 库存按 Space Location Code、Material、Lot、Container 稳定排序；任务按 Task ID、
   Sequence Number、Location Logical ID 稳定排序。

映射必须是一对一。重复 WMS Logical ID、同一 Space Location 的冲突绑定或无法反向
解析的返回项目都视为合同违规，不能择一继续。

## 8. 分块快照与来源规则

- 每次逻辑查询声明的 Data Source Kind 和 Data Source ID 必须固定。
- 所有分块返回的来源身份必须与服务声明一致；中途变化时失败关闭。
- `observedAtUtc` 必须是有效 UTC 时间。多分块响应使用各分块中最早的时间，保守表示
  整体快照的新鲜度。
- 空位置范围不调用 WMS，但仍使用已声明的来源身份生成空响应。
- 任一分块返回 `Unavailable` 时，整个逻辑响应返回该来源和空项目集合，不能混入前面
  已取得的部分数据。

## 9. 错误处理

| 场景 | 行为 |
|---|---|
| 空 Site ID、空 Location ID、超过 10,000 个 ID | HTTP 400 参数错误 Problem Details |
| 站点越权 | 现有拒绝优先访问错误 |
| 站点模型不存在 | HTTP 404 `SPACE_MODEL_NOT_FOUND` |
| 没有当前 Published 版本 | HTTP 409 `SPACE_VERSION_STATE_INVALID` |
| 请求库位不在 Published/Active 范围 | HTTP 404 `SPACE_LOGICAL_ID_NOT_FOUND`；调用 WMS 前失败 |
| 来源声明为 `Unavailable` | 200；来源 `isAvailable=false`；项目为空 |
| WMS 超时、连接或适配器执行异常 | 可重试 `SPACE_WMS_UNAVAILABLE` |
| 来源身份变化、额外 ID、空编码、非法任务身份/顺序、映射冲突 | `SPACE_WMS_RUNTIME_CONTRACT_VIOLATION`，失败关闭 |
| 请求取消 | 原样传播取消，不改写为 WMS 不可用 |

`Unavailable` 与真实空库存是两个不同的成功响应，消费者不得通过项目数量推断来源状态。

## 10. 依赖注入

生产 DI 默认将 `ISpaceWmsRuntimeSource` 映射到已注册的 `ISpaceWmsAdapter`，当前即
`Cp6SpaceWmsAdapter`。标准模拟器只能被测试或显式配置选中，不能因注册顺序静默覆盖
生产来源。

注册 `ISpaceWmsRuntimeService` 为 Scoped；服务及其控制器不直接解析具体适配器类型。

## 11. 测试与验收

### 11.1 底层合同

- CP6 WMS 与模拟器对相同库存/任务输入返回相同记录结构和来源字段。
- Real、Simulated、Unavailable 三类来源均可序列化为稳定字符串和派生标志。

### 11.2 服务测试

- 租户上下文、站点数据范围及 Published-only/Active-only 规则。
- 全量查询、指定库位查询、空 Published 位置集合和 10,000 上限。
- 501 个及 10,000 个库位按 500 正确分块；不得产生逐库位 WMS 调用。
- 普通身份、E07-S05 绑定身份、编码一致与编码漂移。
- 库存和任务映射、空间层级补充及稳定排序。
- Unavailable 返回、传输失败映射、取消传播。
- 分块来源变化、范围外返回 ID、无效字段和身份冲突失败关闭。

### 11.3 API 与组合测试

- 两条路由、重复查询参数、`space:model:read` 权限和 Problem Details。
- OpenAPI 包含完整公开 DTO，来源字段不会被字段策略移除。
- 生产 DI 选择 CP6 WMS；测试 DI 可显式选择标准模拟器。
- 运行现有 Space Unit、Integration、权限/OpenAPI 回归集。

## 12. 完成标准

E08-S01 在以下条件全部满足时完成：

1. 两种来源经同一服务和 API 返回同构库存/任务 DTO。
2. 服务只查询授权站点的当前 Published Active 库位，且采纳身份可双向映射。
3. 10,000 库位以内使用 500 大小分块，无 N+1 调用。
4. 来源、不可用和合同违规语义通过自动测试锁定。
5. API、权限、OpenAPI、DI 和既有 Space 回归通过。
6. 无数据库模型变化和 Migration。

## 13. 后续边界

- E08-S02 在来源 DTO 上增加接收时间、延迟、时钟偏移和健康历史，并在 Viewer 展示。
- E08-S03 基于库存服务实现物料/批次/容器定位结果语义。
- E08-S04 基于任务服务实现任务路径、跨层/跨区和工作量验收。
- E08-S05 对统一 API 与 Viewer 的 10,000 库位性能建立正式门槛。
