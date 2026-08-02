# Space E11-S02 上架/库位推荐候选与解释实现计划

状态：执行中
起始基线：`3577463017ed9783128740fa198a83b0ec21fe63`
功能分支：`codex/space-e11-s02-putaway-recommendations`

## 1. 为什么现在做

E10-S01～S06 与 E11-S01 已完成，E11-S02 所需的当前 Published 空间模型、
统一 WMS 库存/活动任务来源和运营诊断边界均已具备。CAD 授权黄金集、SDK、
冻结 Worker 等外部闸门仍阻塞 E02/E03/E13/E06 主链，因此本卡作为已解锁的
独立运营卡推进，不绕过 CAD 闸门，也不扩展到审批或执行。

旧 candidate checkpoint 只作为需求证据。其来源字段已过时，并且“保留有限排除
示例”的报告与 DTO 不一致；本实现按当前集成架构重建合同，不整体合并 checkpoint。

## 2. 冻结 HTTP、授权与幂等合同

- 新增内部端点：
  - `PUT /api/space/operations/v1/sites/{siteId}/putaway-recommendations/{recommendationId}`
  - `GET /api/space/operations/v1/sites/{siteId}/putaway-recommendations/{recommendationId}`
- PUT 需要 `space:operations:recommendations:generate`，GET 需要
  `space:operations:recommendations:read`；两者都使用 Problem Details、显式强制审计，
  外部 Portal 主体在访问模型或运行态前失败关闭。
- 调用方提供非空 UUID `recommendationId`。相同 ID 与相同规范化请求返回
  `Duplicate`；相同 ID 与不同请求返回 409
  `SPACE_PUTAWAY_RECOMMENDATION_CONFLICT`。
- 请求在哈希前规范化：标识 trim 后使用不区分大小写的规范形式，decimal 使用
  invariant `G29` 表示，避免仅大小写、尾随空格或小数 scale 不同破坏幂等语义。
- 推荐定义版本固定为 `space-putaway-v1`。生成只写不可变推荐证据，不预留库位、
  不改变库存、不创建任务，也不向 WMS/WCS/PDA 写命令。

## 3. 请求与输入边界

- 必填：物料代码、正数入库数量。
- 可选：货主、批次、楼层、区域、宽/高/深、最大承载要求。
- `AllowExactStockConsolidation` 默认 true；`MaximumCandidates` 为 1～50。
- 楼层/区域必须属于生成时的当前 Published/Active 模型；区域若与指定楼层不一致
  直接拒绝，不做相似编码映射。
- 单次最多考察 10,000 个 Active 库位，排除样例最多返回并持久化 100 条；候选、
  排除和来源均稳定排序并有截断标记。
- 入库数量只作为业务证据，不从数量猜测体积、重量或占用容量。只有调用方明确
  提供尺寸/承载要求时才使用 Published 元数据做硬过滤。

## 4. 来源一致性与失败关闭

- 库存与活动任务必须来自同一 Site、Warehouse、PublishedVersion，以及同一当前
  `Kind + AdapterId + DataSourceId + IsSimulated` 来源身份；各自观察/接收时间独立保留。
- 任一来源不可用时返回 503；来源合同、未知库位、版本越界或模型关系不一致时
  返回 502；生成期间 Published 上下文改变时返回 409 并要求重试。
- 库存或任务引用的每个 LocationLogicalId 必须仍属于当前 Active Published 模型；
  不把旧版本或编码相似的运行态证据映射到新库位。
- 模拟来源不会冒充真实来源，响应原样标识并增加限制代码；后续审批/执行必须重新验证。

## 5. 候选资格、首因排除与排序

每个 Active 库位按稳定顺序只记录一个首个排除原因：

1. Published 几何/层级/编码元数据缺失或不一致；
2. 位于请求楼层或区域范围之外；
3. 生成时点存在活动任务；
4. 库存数量无效（负数、分配为负或分配大于物理量）；
5. WMS 与 Space 库位代码不一致；
6. 正库存与请求身份不兼容；
7. 尺寸不足；
8. 请求承载但 Published 最大承载未知；
9. 最大承载不足。

精确合并只有在请求明确提供物料、货主和批次，且库位全部正库存逐行满足代码一致、
数量有效和物料/货主/批次完全匹配时才成立；否则只允许无正库存库位。每种排除返回
计数，并持久化最多 100 个包含库位/楼层/区域和原因码的首因样例，确保“为什么没选”
可验证，而非只给总数。

排序固定为：精确合并；与匹配库存同区域；同楼层；Published 货架锚点二维几何距离；
层号、库位代码、库位 ID。距离仅是几何近似，不宣称通道路径或步行距离。每个候选
返回稳定 rank、category、规则命中、当前数量和空间标识。

## 6. 不可变证据与迁移

- 新增 `Space_PutawayRecommendation` 与当前日期迁移，保存规范化请求、SHA-256、
  Published 版本、来源、候选、排除计数/样例、限制、生成者和生成时间。
- 数据库约束覆盖计数关系、哈希、JSON 合法性与非软删除；建立租户/ID 候选键、
  Published 版本复合外键及 Site/生成时间索引。
- `SpaceContext` 对已持久化推荐的修改或删除失败关闭；服务反序列化时重新校验候选数、
  排除数和样例数，损坏证据不得静默返回。
- JSON 写入设定显式 UTF-8 大小上限，避免可控字段造成无界证据膨胀。

## 7. Viewer

- Viewer 工具栏新增 `PUT` 面板，默认折叠且只在用户点击后生成，不在页面加载时
  创建持久化记录。
- 输入物料、货主、批次、数量、可选尺寸/承载和当前楼层范围；清楚提示生成不会
  预留、移动库存或创建任务。
- 面板展示来源时点、候选解释、排除统计/样例、限制与截断状态；点击候选或排除样例
  复用 Locator，并在需要时切换到相应楼层。
- 较旧并发请求、关闭面板和组件卸载后的响应不得覆盖新状态；失败保留上次成功结果。
  PUT 面板与 KPI/DIAG 面板互斥，避免遮挡和状态混淆。
- 新增五语 seed；缺失键基线不得净增加。

## 8. 门禁与交付

- 纯引擎：精确合并、空库位、首因排除、尺寸/承载、活动任务、几何排序、稳定
  tie-break、候选 50 与排除样例 100 截断。
- 服务：请求规范化/幂等冲突、内部主体、站点范围、Published/来源一致性、不可用和
  合同越界、不可变持久化及损坏证据失败关闭。
- API/权限/种子/审计：稳定路由、参数表面、管理员幂等授权、强制 GET 审计、
  外部拒绝和非 Design v1 合同计数不漂移。
- 前端：API、表单边界、手动生成、最后成功结果、并发失效、候选/排除定位和面板互斥。
- 运行 Space Unit、默认 Integration、真实 SQL（环境可用时）、CP6.Tests、前端全量、
  TypeScript、生产构建、solution Release、EF pending model、SDK drift 和
  `git diff --check`。
- 功能分支先推远端备份；门禁通过后 no-ff 合入受控 Space 集成分支并再次冒烟，
  再删除已合并的本地/远端功能分支与工作树。`main` 在 Space 发布边界批准前不改动。

## 9. 明确不做

- E11-S03 人员/任务调度建议。
- E11-S04 审批和 WMS/WCS/PDA 任务适配。
- E11-S05 执行、回执、重试或补偿。
- E11-S06 效果评估或收益看板。
- 库位锁定、容量推导、危险品/温控/存储类别猜测、路线距离或自动执行。
- 外部 Portal 授权、冻结 `/api/space/design/v1` 合同或生成 SDK 的扩展。
- CAD、AI Provider 与 E02/E03/E13/E06 外部依赖的任何绕过。
