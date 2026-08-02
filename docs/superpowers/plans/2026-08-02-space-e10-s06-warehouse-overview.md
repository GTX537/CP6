# Space E10-S06 仓库 KPI、利用率与 ABC 实现计划

## 目标

在当前 Published/Active Space 模型和 E08 统一 WMS 运行源之上提供一个可解释的仓库运营快照，并在 3D Viewer 中展示 KPI 与 ABC 空间叠加。所有指标必须返回来源、观察时间、分析窗口和计算口径；缺少容量或历史事实时返回不可用，不允许浏览器猜测。

## API

- 新增 `GET /api/space/design/v1/sites/{siteId}/runtime/overview?abcWindowDays=90`。
- 权限沿用 `space:model:read`；执行上下文、租户、站点访问和当前 Published 版本检查沿用统一运行态服务。
- `abcWindowDays` 允许 1～365，默认 90；窗口为请求捕获日前完整的 N 个 CP6 WMS 交易自然日，半开区间 `[fromDateInclusive,toDateExclusive)`。
- 响应包含模型、库存、作业、异常、ABC 摘要、楼层明细和库位 ABC 映射；库存、作业、ABC 分别保留 WMS 来源和时间证据，允许显式部分可用。

## 指标口径

### 模型与面积

- 只统计当前 Published 版本中 `Active` 的楼层、库区、货架和库位。
- 楼层面积由 `BoundaryJson` 的毫米坐标多边形用鞋带公式计算；边界缺失或形状不可计算时该层面积为 `null`。
- 全站面积仅在所有活跃楼层均有可计算边界时返回，否则为 `null`，并返回面积覆盖楼层数和缺失数。
- 货架占地为 `Width × Depth` 的总和；货架占地率仅在全站面积可用且大于零时返回。该值是建模占地率，不是 WMS 容量利用率。

### 库存与利用率

- WMS 仍是货主、SKU、批次、容器、数量的唯一事实源；Design Revision 不保存运行事实。
- 占用库位定义为当前查询中至少一条 `PhysicalQuantity > 0` 的活跃库位。
- 库位占用率固定为 `occupiedLocationCount / activeLocationCount × 100`，返回分子、分母、百分比和方法名。
- 当前统一源没有标准容量数值/单位，因此 `capacityUtilizationPercent` 必须为 `null`，状态为 `Unavailable`，原因固定为 `WMS_LOCATION_CAPACITY_NOT_AVAILABLE`；不得以 BinStatus 或数量粗估冒充容量利用率。
- 库存摘要返回记录数、占用库位数以及不同货主、SKU、批次、容器数；不跨不同计量单位汇总库存数量。

### 作业与异常

- 作业量使用统一 WMS 运行源当前活跃任务，返回不同任务数和任务停靠行数；不把当前任务数称为历史吞吐量。
- 异常返回当前设备活动告警总数/严重告警数、WMS/Space 编码不一致库位数、超额分配库存行数、缺失面积楼层数和无 ABC 历史 SKU 数。

### ABC

- ABC 事实来自 WMS 正数 `OUT` 库存交易；零数、负数和非 OUT 交易不进入口径。
- 先按 SKU 聚合出库次数和出库量，再按出库量降序、SKU 序号升序确定稳定顺序。
- 排名依据为“该 SKU 之前的累计出库量占比”：前序累计 `<80%` 为 A，`<95%` 为 B，其余为 C；因此最高贡献 SKU 始终为 A。当前库存 SKU 在窗口内无正数 OUT 时为 `Unclassified`。
- 百分比保留两位小数；响应明确返回 `OutboundQuantityPreviousCumulativeShare` 方法、80/95 阈值、窗口和交易时间基准。
- 一个库位含多等级 SKU 时按 A → B → C → Unclassified 优先级着色；响应保留该库位完整 SKU 等级事实，避免只凭颜色推断。

## Viewer

- 新增仓库总览面板，显示快照完整性、来源时间、面积、库位占用率、库存、活跃作业、异常、ABC 分布和楼层明细。
- 支持 1～365 天 ABC 窗口刷新；旧请求、卸载后的响应不得覆盖新状态。
- ABC 空间叠加颜色固定为 A 红、B 橙、C 蓝、Unclassified 深灰，并跨库存轮询和楼层切换保持。
- ABC、E10-S05 库存空间筛选和作业热图互斥；关闭 ABC 后恢复之前的库存覆盖模式。
- 数据源不可用、部分可用和真实零值必须有不同显示；刷新失败保留最后一次成功快照。

## 适配器与模拟器

- `ISpaceWmsRuntimeSource` 增加只读 ABC 聚合查询，返回来源及窗口内按 SKU 聚合的正数 OUT 事实；运行服务统一负责阈值排名和空间映射。
- CP6 适配器从 `T_StockTransaction` 按当前仓库和窗口查询，不绕过租户上下文。
- 标准模拟器增加可清理的出库移动种子；标准数据加载器为 100 个固定 SKU 生成稳定的模拟移动，保证无真实仓数据也能验收 ABC。
- 适配器返回空、重复 SKU、非正聚合、越界时间窗、非法来源或过大集合时按合同失败关闭；传输异常返回可重试 503。

## 明确不做

- 不持久化 KPI 快照，不增加数据库 Migration。
- 不实现库存价值、周转率、历史吞吐、趋势、预测、诊断、建议或自动调度。
- 不把原始数量跨计量单位求和，不实现容量主数据，不修改 WMS 库存或任务。
- 不开放外部 Portal 字段策略，不接入 MQTT/OPC UA，不推进仍受阻的 CAD/E06 链。

## 验收门禁

- 运行态服务、CP6 适配器、标准模拟器、真实 SQL、权限、OpenAPI/SDK 和前端聚焦测试。
- Space Unit、默认 Space Integration、CP6.Tests、前端全量、Vue 类型检查、生产构建、完整 solution 非增量构建。
- EF 无待迁移变化，C#/TypeScript SDK 无漂移，生成 TypeScript 严格 no-emit，`git diff --check` 通过。
