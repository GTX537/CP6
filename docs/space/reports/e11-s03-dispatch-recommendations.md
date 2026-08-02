# E11-S03 人员/任务调度建议交付报告

- 状态：已进入 Space 受控集成分支，合并态冒烟通过
- 起始基线：`987f9fcd48b2197ae60b1e13f78785bdb5b8a967`
- 合同提交：`3cf42534`
- 功能提交：`419d3f6c`
- 文档提交：`eea62de0`
- no-ff 集成提交：`cf7bf778`
- 功能分支：`codex/space-e11-s03-dispatch-recommendations`
- Migration：`20260802180049_SpaceE11S03DispatchRecommendations`

## 1. 交付结果

E11-S03 新增两个仅内部主体可访问的运营建议端点：

- `PUT /api/space/operations/v1/sites/{siteId}/dispatch-recommendations/{recommendationId}`
- `GET /api/space/operations/v1/sites/{siteId}/dispatch-recommendations/{recommendationId}`

PUT 生成并保存 `space-dispatch-v1` 不可变证据，GET 回读并重新校验同一证据。调用方提供非空 UUID 作为幂等身份：相同规范化请求返回 `Duplicate`，同一 ID 的不同请求返回 409。两条路径复用
`space:operations:recommendations:generate/read`，并分别记录
`space.operations.dispatch-recommendation.generate/read` 审计；GET 显式启用读审计。

本卡只提供建议，不审批、不分配、不认领、不启动、不修改人员或任务，也不向 WMS、WCS 或 PDA 写入命令。

## 2. 任务与人员事实边界

- 调度任务来自 CP6 当前租户和仓库的真实 `MobileTask`；只把 `Pending` 且未分配的任务纳入建议。保留 TaskId、类型、优先级、ContractVersion、ExecutionVersion、RowVersion、数量和物料证据。
- 首个可行动位置固定优先 `FromLocationCd`，缺失时使用 `ToLocationCd`，并明确标记 `Source` 或 `Destination`；位置必须重新映射到当前 Published/Active 模型，不能按相似编码猜测。
- 人员直接读取 E10 当前态投影；位置与工作状态分别按 5 分钟阈值校验，二者都必须存在且新鲜，工作状态必须为 `Idle`。默认排除 Simulated，只有显式请求才纳入。
- 人员位置必须能解析为当前 Published 活动楼层上的坐标。响应保留来源身份和稳定技术键，不返回姓名、邮箱或 UserId。
- 任务或人员来源不可用、租户/Site/仓库/Published 身份不一致、来源合同越界时均失败关闭。

## 3. 确定性匹配与解释

- 请求可按 TaskType、任务首端楼层/库区、最大几何距离、是否允许跨层、是否包含模拟人员过滤，并返回 1～100 条建议。
- 同层距离只表示 Published 平面锚点之间的欧氏距离，不冒充通道路线、行走时间或 SLA。跨层匹配的距离保持未知；请求距离上限时，未知距离配对失败关闭。
- 候选配对乘积最多 100,000；超限返回 422，并要求缩小任务类型、楼层、库区或距离范围。
- 先用 Hopcroft–Karp 求最大可匹配基数，再在返回额度内做确定性最小成本最大流；优先级、同区、同层、已知距离、距离、TaskId 与 PersonKey 构成稳定顺序，不以贪心牺牲可返回匹配数。
- 每人、每任务在一个建议集中最多出现一次。响应分别报告 examined/eligible task、eligible person/pair、最大可匹配数、返回数和截断状态。
- 排除统计覆盖任务、人员、配对三层，并持久化最多 100 个稳定首因样例；即使没有建议，也能解释原因。

## 4. 不可变证据与 Viewer

`Space_DispatchRecommendation` 保存规范化请求与 SHA-256、Published 版本、任务/人员来源、计数、排除、样例、Assignments、限制、生成者和时间。数据库包含租户复合键、Published 外键、计数/JSON/哈希/非软删除约束与查询索引；`SpaceContext` 拒绝修改或删除已保存证据。GET 反序列化后重新核对计数、rank、Task/Person 唯一性、样例和截断状态，损坏证据不会静默返回。

Viewer 新增默认关闭的 `DSP` 面板，与 KPI、DIAG、PUT 面板互斥。用户必须手动生成；界面显示安全边界、来源时点、任务并发证据、人员位置/工作状态双时点、匹配容量、建议、排除、样例和限制，并可复用 Locator 定位任务首端。失败保留上次成功结果，关闭、切换、卸载或新请求都会使旧响应失效。

新增 42 个简中、繁中、英语、日语、韩语完整词条，i18n 快照从 4,500 增至 4,542。静态缺失仍为既有 909 项，本卡净新增欠账为 0。

## 5. 验证证据

| 门禁 | 结果 |
|---|---|
| 调度引擎聚焦 | 4 passed |
| 调度服务聚焦 | 4 passed |
| CP6 MobileTask 适配器聚焦 | 1 passed |
| WMS Runtime 模拟/采纳映射聚焦 | 1 passed |
| API、权限、审计、合同与五语种种子聚焦 | 4 passed；权限守卫另 19 passed |
| Space Unit Release 全量 | 249 passed / 0 failed / 0 skipped |
| Space Integration Release 默认全集 | 216 passed / 0 failed / 62 SQL 环境门禁 skipped |
| CP6.Tests Release 全量 | 2,752 passed / 0 failed / 17 环境门禁 skipped |
| 前端聚焦 | 2 files / 16 tests passed |
| 前端全量 | 118 files / 653 tests passed |
| 前端严格类型检查与生产构建 | passed；仅既有大 chunk 提示 |
| 完整 solution Release 非增量构建 | 0 errors / 10 条既有 warnings |
| EF pending model | 无待迁移模型变化 |
| Design V1 OpenAPI/C#/TypeScript SDK drift | passed；operations 保持 68，无生成物漂移 |
| TypeScript SDK strict no-emit | passed |
| Git 差异检查 | passed |
| i18n 静态门禁 | 909 项既有欠账；相对基线净新增 0 |
| 合并态冒烟 | 引擎/运行时合同 6/6、服务/适配器 6/6、权限/审计/API/种子 23/23、前端 16/16、类型与 SDK drift passed |

完整 solution 首次使用 `--no-restore` 时，8 个客户端/工具项目因当前工作树缺少
`project.assets.json` 而停止；执行一次 solution restore 后，同一非增量 Release 门禁通过。该现象属于工作树依赖准备，不是源代码失败。

## 6. 明确未做与下一步

本卡不包含 E11-S04 审批与任务适配、E11-S05 执行回执/重试/补偿、E11-S06 效果评估，也不实现技能、资质、班次、工时、人体工学、设备资格、拥堵预测、路线时间或 SLA 猜测。

CAD/E02 外部门禁仍关闭：正式授权黄金集、格式/版本/语义覆盖、供应商 SDK/凭据和冻结 Worker 证据未满足。本卡不绕过这些门禁，也不扩展冻结的 Design v1 HTTP/SDK。功能分支已先以 `eea62de0` 推送远端备份，再以 no-ff 方式合入受控集成；集成远端一致性确认及临时分支/工作树清理完成后，再单独冻结 E11-S04；`main` 保持不变。
