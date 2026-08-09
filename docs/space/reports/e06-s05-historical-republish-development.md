# E06-S05 历史版本重新发布回退开发报告

日期：2026-08-08
状态：已 no-ff 集成
集成基线：`bf9c07d8c193a09e5f9064f54126e36207a9e694`
功能分支：`codex/space-e06-s05-historical-republish`
功能提交：`ea16ce679f90ed17f37a37b930e68c528b4375a0`
no-ff 集成提交：`85e039f8adae7ee6b61e5112670244c86c16f7e6`

## 1. 本卡边界

本切片交付 E06-S05：允许把同一模型中已被取代的生产版本作为历史来源，克隆成一个全新的生产候选版本，按当前校验规则重新验证，再通过 E06-S03/S04 的发布队列生成新的 PublishPlan、PublishAttempt、WMS 写入和运行态激活动作。历史版本、历史发布计划、历史尝试和原审计链始终只读，不通过修改旧记录伪造“回退”。

本卡没有实现 E06-S06 发布管理 UI。生产等价 WMS 演练、正式 CAD Provider/授权黄金集、E03-S05 权威 Match Artifact 写入链及 Beta/GA 跨职能证据仍是独立缺口。

## 2. 不可变历史与新发布血缘

- 新增 `Space_HistoricalRepublish`，冻结 Tenant、模型、历史版本、发起时 Published 版本、目标版本、Job、请求人、原因、审批引用、幂等键与请求哈希。
- 仅同一模型内 `Production + Superseded + 已有 ContentHash` 的历史版本可作为来源；当前 Published 版本必须与请求前置条件一致，且仓库不能已有活动草稿或发布槽位。
- 目标版本是新的 `Production` 版本，`BasedOnVersionId` 指向历史版本，`CloneOperationId` 指向重新发布操作；快照复制保留 LogicalId 和内容，重新分配数据库行标识。
- 历史重新发布证据禁止删除，关键身份、来源、请求与绑定字段在保存层禁止改写；Migration Down 继续采用前向修复并 `THROW 51022` 失败关闭。

## 3. 持久化作业与当前规则校验

- `POST /api/space/design/v1/versions/{historicalVersionId}/republish` 使用 `space:model:rollback`、`Idempotency-Key` 和期望 Published 版本创建操作、初始化目标版本及 `HistoricalRepublish` Job；同键同请求稳定重放，同键异请求冲突。
- 后台作业按 `CloneHistoricalSnapshot → ValidateHistoricalSnapshot → QueuePublish` 三步执行，处理器版本为 `space-historical-republish-v1`，步骤超时为 30 分钟，并复用既有租约、心跳、退避和人工干预账本。
- 克隆完成后必须使用当前验证规则和当前 WMS 能力重新校验。阻断、过期、能力变化或 Published 指针变化均失败关闭，不会进入发布队列，也不会影响现有生产版本。
- 校验通过后重新生成权威预览和 PlanHash，再创建新的不可变 PublishPlan、PublishAttempt 与 Publish Job；失败或重试继续沿用 E06-S04 的恢复与对账机制。

## 4. 权限、审计与执行身份

- 新增 `space:model:rollback` 写权限；查询操作继续使用模型读取权限，并保持 Tenant、Site 与数据范围检查。
- 新发布尝试保留原始回退申请人为 `RequestedBy`，允许受信后台 Worker 以系统执行身份运行；运行态激活会再次把请求参数与持久化 Attempt、Plan、目标版本、基准版本、原始申请人和 PlanHash 对齐，防止内部参数替换。
- 新尝试在普通 `Queued` 事件后追加 `HistoricalRepublishQueued`，记录 RepublishId、历史/当前/目标版本、ValidationRun 与原因，并继续参与不可变哈希审计链。
- 原历史版本的发布计划、发布尝试和审计事件不新增、不更新、不删除；重新发布的全部事件只属于新尝试。

## 5. 数据库、API 与兼容性

- Migration `20260807170204_SpaceE06S05HistoricalRepublish` 以强 Tenant 复合外键绑定模型、四类版本引用、Job、ValidationRun 和 PublishAttempt，并提供幂等部署脚本。
- 新增查询 `GET /api/space/design/v1/republishes/{republishId}`，返回克隆、校验、排队及后续发布尝试状态，支持 UI 后续轮询。
- OpenAPI 操作数由 108 增至 110；C# 与 TypeScript SDK 已重新生成，客户端表面哈希已更新且漂移检查通过。
- 客户端表面更新脚本补齐最小启动配置、短 Web 超时和环境恢复，确保独立生成检查不依赖开发机已有应用配置。

## 6. 验证证据

| 门禁 | 结果 |
|---|---|
| Space Unit 全量 | 462/462 passed |
| CP6.Tests 全量 | 2803 passed / 17 environment-gated skipped / 0 failed |
| 默认 Space Integration 全量 | 261 passed / 94 SQL-gated skipped / 0 failed |
| 发布编排真实 SQL 回归 | 3/3 passed |
| E06-S05 历史重新发布真实 SQL 主路径 | 1/1 passed |
| 完整 `CP6.slnx` Release（含 Desktop/Android 双架构 AOT） | 0 warning / 0 error |
| EF pending model changes | none |
| OpenAPI/C#/TypeScript SDK drift | passed |
| C# SDK、TypeScript 类型检查与 Web 生产构建 | passed |
| `git diff --check` | passed |

真实 SQL 用例连续发布 v2、v3，再以 v2 创建新的回退操作和 v4：错误的当前版本前置条件不会产生操作或改变指针；同一请求幂等重放、异请求冲突；后台 Worker 与原请求人身份不同仍保留原始申请人；v4 完成后成为 Published，v2/v3 保持 Superseded；v2 原发布计划和审计链逐项不变，新尝试拥有独立计划、Attempt 和 `HistoricalRepublishQueued` 审计证据。

完整构建首次并行执行触发 Android 工具链的多架构 AOT 内部状态冲突；清理移动端中间产物后以单进程完整编译，两套架构均成功，随后整套解决方案无增量复核为 0 warning / 0 error。该现象未涉及本卡业务代码或生成契约。

## 7. 尚未完成与下一步

1. E06-S06：发布管理 UI，完整呈现预检、差异、审批、队列进度、失败原因、人工对账及历史回退入口。
2. 在生产等价 WMS 环境完成真实外部写入、超时、断点恢复、告警和运维演练。
3. E03-S05 仍依赖权威 Match Artifact/CAD 链；正式 CAD Provider、组织有权使用的 DWG/DXF 黄金集和性能证据尚未签收。
4. Beta/GA 的跨职能验收、容量、SLO、灾备和发布证据仍需后续切片完成。

本卡是 E06-S05 开发闭环，不是完整 E06、Beta 或 GA 发布签收。下一张可独立推进 E06-S06。
