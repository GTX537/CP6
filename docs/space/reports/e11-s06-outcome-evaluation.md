# E11-S06 优化效果评估与收益看板交付报告

- 状态：功能分支全量门禁完成，等待远端备份与 Space 受控集成
- 起始基线：`f7a6576b2a8bc38a8819a534548a841ed9b24ba3`
- 合同提交：`46884878fca3bea6fe3036ee924f6724c0196463`
- 功能提交：`f10b4b54`
- 功能分支：`codex/space-e11-s06-outcome-evaluation`
- Migration：无；两个 EF Context 均无待迁移模型变化

## 1. 交付结果

E11-S06 在 E11-S03～S05 的不可变建议、审批选择、分派回执和当前执行证据之上，新增只读批次效果评估。内部运营端点为 `GET /api/space/sites/{siteId}/dispatch-recommendations/{recommendationId}/approval-requests/{approvalRequestId}/evaluation`，只组合既有权威读服务与指定 Published 版本的几何锚点，不写建议、审批、任务、库存、订单或新的评估快照。

评估返回推荐→选择→分派回执→开始→完成/关注/补偿漏斗、选择/分派/开始/完成率、审批与分派耗时，以及分派到开始、执行、分派到完成的样本数和平均时长。建议、审批和执行身份、选择项、回执、逐任务身份及执行聚合任一不一致时，以稳定 `SPACE_DISPATCH_EVALUATION_EVIDENCE_INVALID` 失败关闭，不输出看似完整的收益结果。

## 2. 计划几何比较与收益边界

- 计划距离只比较同一获批队列：任务按 `TaskId` 稳定排序，人员按 `SourceId + ExternalId` 稳定排序形成反事实基线；推荐配对使用 E11-S03 已持久化的同层几何距离。
- 只有至少 2 个选择项、同一 Published 版本位置锚点完整、所有配对同层且满足原始最大距离约束时才返回比较；否则整项 `Unavailable`，不做部分样本或选择性结论。
- 正差值为 `Improved`，零为 `Neutral`，负差值为 `Regressed`；回退结果不会被包装成收益。
- 实际路线节省固定不可用，因为没有任务关联的路线轨迹；吞吐提升固定不可用，因为没有可比历史控制窗口；货币收益固定不可用，因为没有工时/设备成本与归因基线。
- 无效或不完整时间样本被排除，并通过稳定 limitation code 明示；不会以零值填充或猜测。

## 3. 权限、隐私与 Viewer

评估读取沿用 `space:operations:dispatch:read`，审计动作固定为 `space.operations.dispatch-evaluation.read`，审计资源为 `DispatchOutcomeEvaluation`。端点只接受内部执行上下文，并复用建议、审批、执行服务的 Tenant、Site 与 WMS 范围检查。

返回值只含批次聚合、稳定外部任务/人员证据的统计结果、Published 几何比较和限制码；不返回人员姓名、邮箱、内部 `UserId`、`AssignedTo` 或逐任务收益明细。

Viewer 调度面板新增效果看板、手动刷新、来源时点、样本量、计划几何改善/持平/回退、不可用原因及收益声明边界。提交新审批、生成新建议、关闭面板或卸载页面都会使旧评估响应失效；刷新失败保留上次成功结果并明确提示。

新增 28 行五语言种子，全部 28 个唯一键进入生成式快照，快照从 4,587 增至 4,615 个唯一键。i18n 静态门禁仍报告 908 个既有缺失项，本卡净新增缺失为 0。本机 SQL Server 当前不可达，因此快照由同一后端 seed 确定性重建；五语言完整性与唯一性由测试验证。

## 4. 验证证据

| 门禁 | 结果 |
|---|---|
| 纯评估引擎聚焦 | 9 passed |
| 真实 `SpaceContext` 组合服务聚焦 | 2 passed |
| 权限、合同、种子聚焦 | 7 passed |
| 前端 API 与效果面板聚焦 | 2 files / 23 tests passed |
| Space Unit Release 全量 | 258 passed / 0 failed |
| Space Integration Release 默认全集 | 232 passed / 0 failed / 62 SQL 环境门禁 skipped |
| CP6.Tests Release 全量 | 2,759 passed / 0 failed / 17 环境门禁 skipped |
| 前端全量 | 118 files / 660 tests passed |
| 前端严格类型检查与生产构建 | passed；仅既有大 chunk 提示 |
| 完整 solution Release 非增量构建 | exit 0；10 条既有 warning |
| EF pending model | `CP6Context`、`SpaceContext` 均无待迁移变化 |
| Design V1 SDK drift | passed；冻结的 Design v1 表面无变化 |
| TypeScript SDK strict no-emit | passed（项目锁定编译器） |
| 原生客户端 OpenAPI surface | 哈希更新为 `14C69780...5593` 并复验同步 |
| i18n 静态门禁 | 908 项既有欠账；本卡无新增 |
| Git 差异检查 | passed |

默认测试集未连接 SQL Server，因此 62 项 SQL 集成门禁按既有约定跳过；这不是通过结果。本卡没有数据库模型变化，Published 几何组合已用真实 `SpaceContext` 的内存持久化路径验证。部署前仍需在具备 SQL Server 的发布环境执行完整真实 SQL 门禁和备份/回滚演练。

## 5. 明确未做与下一步

本卡不采集路线轨迹，不建立历史控制组、成本模型或财务归因，不新增评估持久化表、后台 Worker、自动重试、自动调度或自动执行，也不把计划欧氏几何距离冒充真实通道路线、吞吐或货币收益。E12 历史仿真不在本卡范围内。

E11-S01～S06 至此均有完成证据。下一张可独立冻结 E12-S01“与生产版本隔离的方案分支”合同；CAD/E02 的正式授权黄金集、格式/版本/语义覆盖、供应商 SDK/凭据及冻结 Worker 证据仍未满足，E03-S04、E04-S05、E06 与 E13 后续依赖卡不得绕过这些外部门禁。

## 6. 远端备份与资源清理

待完成：功能 tip 远端备份、no-ff 合入 `integration/space-v1-20260730`、合并态复验、项目状态更新、祖先关系核验，以及功能工作树和本地/远端临时分支删除。
