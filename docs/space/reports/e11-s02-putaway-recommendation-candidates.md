# E11-S02 上架/库位推荐候选交付报告

- 状态：已进入 Space 受控集成分支
- 起始基线：`3577463017ed9783128740fa198a83b0ec21fe63`
- 合同提交：`3ccd2936`
- 功能提交：`644293f1`
- 文档提交：`034a1b1b`
- no-ff 集成提交：`a2b47826`
- 功能分支：`codex/space-e11-s02-putaway-recommendations`
- Migration：`20260802172258_SpaceE11S02PutawayRecommendations`

## 1. 交付结果

E11-S02 新增两个仅内部主体可访问的运营推荐端点：

- `PUT /api/space/operations/v1/sites/{siteId}/putaway-recommendations/{recommendationId}`
- `GET /api/space/operations/v1/sites/{siteId}/putaway-recommendations/{recommendationId}`

PUT 生成并持久化不可变推荐证据，GET 回读同一证据。调用方 UUID 提供幂等身份；
相同规范化请求返回 `Duplicate`，同一 ID 的不同请求返回 409。权限分别为
`space:operations:recommendations:generate` 和
`space:operations:recommendations:read`，两条路径均有显式失败关闭审计。

推荐定义固定为 `space-putaway-v1`。本卡不预留库位、不移动库存、不创建任务，
也不向 WMS、WCS 或 PDA 写入任何命令。

## 2. 候选与解释口径

- 当前 Published/Active 模型是空间身份与尺寸权威，当前 WMS 库存和活动任务是运行事实
  权威；Site、Warehouse、PublishedVersion 和来源身份不一致均失败关闭。
- 单次最多考察 10,000 个活动库位，最多返回 50 个候选和 100 个首因排除样例。
- 每个库位只记录一个稳定首因：元数据缺失、范围外、活动任务、库存数量无效、
  WMS/Space 编码不一致、不兼容库存、尺寸不足、承载未知或承载不足。
- 精确合并要求请求显式提供货主和批次，且库位每条正库存的数量、编码、物料、货主、
  批次全部有效并完全匹配；否则只推荐无正库存库位。
- 排序固定为精确合并、匹配库存同区域、同楼层、Published 货架锚点二维距离、层号、
  库位代码和库位 ID。距离只表示几何近似，不宣称通道或步行距离。
- 入库数量保留为请求证据，但不推导体积、重量或容量；只有调用方显式提供尺寸或承载
  要求时才按 Published 字段过滤。

## 3. 不可变证据与 Viewer

`Space_PutawayRecommendation` 保存规范化请求与 SHA-256、生成者/时间、Published
版本、独立库存/任务来源、候选、排除计数与样例、限制说明和截断状态。数据库含租户
复合外键、计数/JSON/哈希/非软删除检查约束；`SpaceContext` 拒绝修改或删除已保存推荐，
GET 反序列化后重新核对计数、样例和 rank，损坏证据不会静默返回。

Viewer 新增默认折叠的 `PUT` 面板，只有用户提交表单才生成记录；可限定当前楼层，
展示来源时点、候选解释、排除统计/样例和限制，并复用 Locator 定位。PUT 与 KPI/DIAG
面板互斥；关闭、切换或卸载会使旧响应失效，刷新失败保留上次成功结果。

新增 42 个五语种子，其中 41 个进入新快照、1 个复用既有公共键。i18n 快照由 4,459
增至 4,500；静态检查的历史缺失由 911 降至 909，本卡没有增加欠账。

## 4. 验证证据

| 门禁 | 结果 |
|---|---|
| 推荐引擎聚焦 | 5 passed |
| 推荐服务聚焦 | 6 passed |
| 权限、审计、HTTP 契约与种子聚焦 | 33 passed |
| Space Unit Release 全量 | 245 passed / 0 failed / 0 skipped |
| Space Integration 默认全集 | 211 passed / 0 failed / 62 SQL 环境门禁 skipped |
| CP6.Tests Release 全量 | 2748 passed / 0 failed / 17 环境门禁 skipped |
| 前端聚焦 | 2 files / 14 tests passed |
| 前端全量 | 117 files / 648 tests passed |
| 前端严格类型检查与生产构建 | passed；仅既有大 chunk 提示 |
| 完整 solution Release 非增量构建 | 0 errors / 10 条既有 warnings |
| EF pending model | 无待迁移模型变化 |
| Design V1 OpenAPI/C#/TypeScript SDK drift | passed；operations 保持 68，无生成物漂移 |
| TypeScript SDK strict no-emit | passed |
| Git 差异检查 | passed |
| i18n 静态门禁 | 909 项既有欠账；比 911 基线减少 2，净新增 0 |
| 合并态冒烟 | 引擎 5/5、服务 6/6、权限/审计/契约/种子 34/34、前端 14/14、两套 TypeScript strict no-emit 与 SDK drift passed |

首次把四组全量测试并行运行时，既有
`Worker_backfill_reloader_cannot_overwrite_another_workers_future_claim` 在高负载下发生一次
31 秒取消；该测试隔离复跑通过，随后主测试集串行完整复跑 2748/2748 通过，因此不构成
E11-S02 回归。

合并态首次并行启动三组 .NET 测试时，两个进程同时编译共享 `obj` 目录造成 SourceLink/
编译产物文件锁；服务组仍为 6/6。将引擎与合同组串行重跑后分别为 5/5 和 34/34，
证明该现象是测试编排资源争用，不是源码或合并回归。

## 5. 当前路线与明确不做

本卡不包含 E11-S03 人员/任务调度建议、E11-S04 审批与任务适配、E11-S05 执行回执
和补偿、E11-S06 效果评估，也不绕过 CAD、外部 AI Provider 或发布治理闸门。

E02/CAD 主链仍等待正式授权黄金集、格式/版本/语义覆盖、供应商 SDK/凭据和冻结 Worker
证据。功能分支完成远端备份及受控集成冒烟后，下一步应单独冻结 E11-S03 合同；任何
审批或执行写回仍须等待 E11-S04 以后另行验收。
