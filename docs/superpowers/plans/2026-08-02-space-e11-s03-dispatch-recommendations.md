# Space E11-S03 人员/任务调度建议与约束实现计划

状态：功能完成，待受控集成
起始基线：`987f9fcd48b2197ae60b1e13f78785bdb5b8a967`
功能分支：`codex/space-e11-s03-dispatch-recommendations`

## 1. 为什么现在做

E10-S01～S06 已提供人员当前位置/工作状态、Published 空间身份与统一 WMS 运行来源，
E11-S01/S02 已冻结运营诊断和“建议只写不可变证据、不执行”的边界，因此 E11-S03
依赖已满足。CAD 授权黄金集、供应商 SDK/凭据与冻结 Worker 仍阻塞 E02/E03/E13/E06
主链；本卡不绕过这些闸门。

旧 checkpoint 只作为需求证据，不整体合并。它依赖当前代码已不存在的
`SpacePersonCurrentState.TaskExternalId/PersonKey`，并把出库单站点当作可派发任务，
无法验证真实任务分配状态；它的全局贪心匹配还可能在存在完整匹配时漏配。本实现按
当前 E10 人员模型和 CP6 `MobileTask` 事实重建合同。

## 2. HTTP、授权、审计与幂等

- 新增内部资源：
  - `PUT /api/space/operations/v1/sites/{siteId}/dispatch-recommendations/{recommendationId}`
  - `GET /api/space/operations/v1/sites/{siteId}/dispatch-recommendations/{recommendationId}`
- PUT/GET 分别复用 `space:operations:recommendations:generate/read`；审计动作固定为
  `space.operations.dispatch-recommendation.generate/read`，GET 设置 `AuditRead=true`。
- 外部 Portal 主体在读取人员、任务或模型前失败关闭。调用方 UUID 非空；相同 ID 与
  相同规范化请求返回 `Duplicate`，不同请求返回 409。
- 定义版本固定 `space-dispatch-v1`。PUT 只保存不可变建议证据，不批准、不分配、不领取、
  不启动、不修改人员状态，也不向 WMS/WCS/PDA 写命令。

## 3. 真实任务来源

- 在统一 WMS 适配器边界新增内部 dispatch-task 查询，不扩展冻结的 Design v1 HTTP/SDK。
- CP6 生产适配器读取当前租户/仓库的非删除 `MobileTask`，保留 TaskNo、TaskType、Status、
  AssignedTo、Priority、ContractVersion、ExecutionVersion、RowVersion、数量、物料和首个
  可行动位置。首个可行动位置固定优先 `FromLocationCd`，缺失时使用 `ToLocationCd`，并
  返回 `Source`/`Destination` 角色；不从任务类型猜测另一套顺序。
- 只把 `Pending` 且 `AssignedTo` 为空的任务视为可建议。InProgress、Paused、Exception、
  PartiallyCompleted、终态、已分配、字段无效、位置未映射当前 Published 或编码不一致均
  按稳定首因排除。
- Space 运行服务把 WMS 位置编码重新绑定到当前 Published/Active 采纳身份；Site、仓库、
  Published 版本、适配器来源和任务位置越界失败关闭或显式排除，不按相似编码猜测。
- 单次最多 10,000 个活动任务；来源不可用返回 503，来源合同违例返回 502。

## 4. 人员资格与时点

- 直接读取当前租户、请求 Site 的 `SpacePersonnelCurrentState`，最多 10,000 人；超过上限
  422 失败关闭。
- 位置和工作状态分别以 E10 既有 5 分钟阈值校验，二者都必须存在且新鲜；工作状态必须
  严格为 `Idle`。
- 默认排除 Simulated 人员；只有请求显式 `IncludeSimulatedPersonnel=true` 才纳入，并在
  来源与限制中标识，后续审批/执行仍必须拒绝或重新验证。
- 人员位置必须能解析到当前 Published 活动楼层，且具有楼层 XYZ，或从当前活动库位的
  货架锚点解析二维位置。楼层/库位不一致、旧版本库位和缺失坐标不推断。
- 返回内部 `SourceId + PersonExternalId` 以形成可行动建议，但不返回姓名、邮箱或 UserId；
  `PersonKey` 是租户/Site/来源身份的稳定 SHA-256 技术键，不宣称匿名化。

## 5. 请求、配对约束与确定性匹配

- 请求支持可选 TaskType、任务首站 Floor/Zone、默认禁止跨层、显式允许跨层、可选最大
  几何距离、是否纳入模拟人员及 1～100 个返回建议。
- TaskType trim 后大写；decimal 用 invariant `G29` 参与哈希。Floor/Zone 必须属于当前
  Published，Zone 与指定 Floor 不一致直接拒绝。
- 默认跨层配对拒绝。显式允许跨层时距离为未知，不虚构楼梯/电梯/垂直路线；若请求最大
  距离，未知距离也失败关闭。
- 同层距离只用人员当前二维点到任务首个可行动库位 Published 货架锚点的欧氏距离，
  单位米、三位小数；不宣称通道路线、行走时间或 SLA。
- 候选边最多 100,000；超限 422，要求缩小任务楼层/区域/类型或距离范围，避免 10k×10k
  无界笛卡尔积。
- 先用 Hopcroft–Karp 计算最大可匹配数，再在最多 100 个返回额度内做确定性最小成本
  最大流。成本顺序固定为任务 Priority、同 Zone、同 Floor、已知距离、距离、TaskId、
  PersonKey；不会用简单贪心牺牲可返回匹配数量。
- 每人/每任务在一个建议集中最多出现一次。响应分别给出 examined/eligible task、eligible
  person、eligible pair、matchable assignment、returned 数及截断状态。

## 6. 解释、证据与持久化

- 排除计数覆盖任务、人员和配对三层；持久化最多 100 个稳定排除样例，样例含 Subject、
  TaskId/PersonKey、位置范围和原因码，确保无结果可解释。
- Assignment 返回任务并发证据（ContractVersion、ExecutionVersion、RowVersion）、Priority、
  首个位置、人员来源身份、人员位置/工作状态时点、同层/同区、几何距离和规则命中。
- 新增 `Space_DispatchRecommendation` 与当前日期 Migration，保存规范化请求/SHA-256、
  Published 版本、任务/人员来源、计数、排除、样例、Assignments、限制、生成者/时间。
- JSON 采用显式 UTF-8 上限；数据库约束覆盖计数、结果、JSON、哈希和非软删除，建立
  租户/ID 候选键、Published 复合外键及 Site/生成时间索引。
- `SpaceContext` 阻止修改/删除；GET 反序列化后重新核对计数、rank、唯一 Task/Person、
  样例和截断，损坏证据失败关闭。

## 7. Viewer 与本地化

- Viewer 新增默认关闭的 `DSP` 面板；用户手动生成，不在加载时持久化记录。
- 表单支持任务类型、当前楼层范围、跨层、最大距离、模拟人员和建议数；显式展示
  “不批准、不派发、不修改任务”。
- 展示任务→人员、Priority、位置/距离、来源时点、排除统计/样例、限制和截断；点击任务
  或样例复用 Locator。
- DSP 与 KPI/DIAG/PUT 面板互斥。关闭、切换、卸载和新请求使旧响应失效；失败保留上次
  成功结果。
- 新增简中、繁中、英语、日语、韩语 seed 并同步快照；i18n 909 项基线不得净增加。

## 8. 门禁与交付

- 纯引擎覆盖最大基数反例、Priority/距离稳定成本、跨层/未知距离、任务/人员/配对首因、
  100 建议/100 样例/100k 边界。
- 服务覆盖内部/租户/Site、幂等冲突、任务/人员来源、Published 映射、各自新鲜度、模拟
  边界、证据损坏和不可变持久化。
- CP6 适配器测试覆盖 MobileTask 分配/状态/优先级/并发证据和租户/仓库过滤。
- API/权限/审计/种子、前端 API/面板/Viewer 互斥与并发失效均需自动化。
- 运行 Space Unit、默认 Integration、环境可用时真实 SQL、CP6.Tests、前端全量、两套
  TypeScript strict no-emit、生产构建、solution Release、EF pending、SDK drift、i18n
  差异和 `git diff --check`。
- 功能分支先推远端备份；门禁通过后 no-ff 合入
  `integration/space-v1-20260730`，合并态冒烟通过再推集成并删除已合并临时分支/工作树。
  `main` 在 Space 整体发布边界批准前不改动。

## 9. 明确不做

- E11-S04 审批、任务转换、分配/领取/启动或 WMS/WCS/PDA 写回。
- E11-S05 执行回执、重试、补偿或状态机。
- E11-S06 效果评估、基线对照或收益看板。
- 技能、资质、班次、工时、设备资格、人体工学、通道拥堵预测、路线时间或 SLA 猜测。
- 姓名/邮箱/UserId、外部 Portal、Design v1 HTTP/SDK 扩展、CAD/AI Provider 闸门绕过。
- 候选 checkpoint 整包合并或用旧报告替代当前基线验收。
