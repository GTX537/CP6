# E13 Generation Run 建模 Web 入口报告

- 状态：功能分支验证完成，待受控集成
- 日期：2026-08-08
- 起始集成基线：`3d3f923c`
- 功能分支：`codex/space-generation-run-web-entry`
- 功能提交：`52bb3a29`
- 目标分支：`integration/space-v1-20260730`

## 1. 交付结论

建模编辑器现已接入统一 `CreateGenerationRun`：具备 `space:model:generate-ai` 的内部用户，可以从当前 Draft 的已确认 DWG/DXF 来源启动 RuleOnly Generation Run。界面明确说明该操作不调用外部 AI Provider，也不会自动写入 Draft；创建成功后直接进入同一任务的排队、进度、审核、决策与原子 Apply 面板。

本切片同时修复了 E13-S11 旧恢复界面仍调用已移除 recovery 合同的问题。Failed/Stale Run 现在使用同一个公开创建入口，提交当前 Draft `If-Match`、ContentRevision、旧 Run RowVersion 和 BasedOn 血缘，服务端继续执行统一幂等、并发和 replacement Run 门禁。

## 2. 用户流程

- 编辑器只在可编辑 Draft 显示“规则生成”，并继续由 `space:model:generate-ai` 控制；
- 启动面板读取当前 Version 与 Source，只展示已完成映射且处于 PreviewReady/Imported 的 DWG/DXF 来源；
- 单一合格来源自动选择，多来源要求用户显式选择，并展示来源状态、单位和内容指纹；
- 创建请求冻结 Source、Mapping、当前 ContentRevision、Draft RowVersion 和稳定 Idempotency-Key；
- 409/422 后丢弃旧幂等键并重新读取权威版本/来源，避免用陈旧输入盲重试；
- Queued/Preparing/Inferring/Validating 状态只轮询 Run，不提前请求尚未生成的 Review/Proposal；
- 到达 AwaitingReview 后再载入审核数据，并容忍 Run 先终态、Review 稍后物化的短暂 404；
- 恢复后路由切换到新 Run，但仍停留在同一决策面板，不丢失用户上下文。

浏览器侧的 Source 筛选只是便利提示。服务端仍重新校验 Tenant、Site、Draft、RowVersion、ContentRevision、Source 类型/状态、Clean file、SourceHash、坐标、Floor、Mapping 和固定 Preview Artifact/SHA；前端不能绕过生产失败关闭。

## 3. API 与合同修正

`SpaceAiGenerationRunDto` 新增公开冻结字段：

- `sourceId`；
- `mappingProfileVersionId`；
- `rackGenerationProfileVersionId`。

Run 详情因此足以安全构造 BasedOn replacement 请求，不再依赖浏览器记忆旧表单。OpenAPI、C# SDK 与 TypeScript SDK 已重新生成；Web 包装层显式保留 Mapping/Rack Profile 的 nullable 语义，并同时发送 `If-Match` 与 `Idempotency-Key`。

未新增路由、数据库表、列或 Migration；未启用 Provider、Secret、外部网络、AI Usage、High Accept 或 Draft 自动写入。

## 4. 验证证据

| 门禁 | 结果 |
|---|---|
| 新建/恢复/排队前端聚焦 | 3 files / 11 tests passed |
| 前端全量 | 133 files / 710 tests passed |
| 前端 type-check + production build | passed |
| OpenAPI 与 AI 审计安全合同 | 31/31 passed |
| Space Unit 全量 | 484/484 passed |
| 默认 Space Integration | 283 passed / 94 SQL-gated skipped / 0 failed |
| CP6.Tests | 2812 passed / 17 environment-gated skipped / 0 failed |
| C# SDK Release | 0 warning / 0 error |
| TypeScript SDK strict | passed |
| OpenAPI/C#/TypeScript SDK drift | passed |
| 完整 solution Release（含 Desktop/Android AOT） | 0 warning / 0 error |
| C# whitespace / `git diff --check` | passed |

## 5. 剩余边界

- 权威 RackGenerationProfile 版本存储/读取尚未实现，因此 Web 首建固定提交 `null`，服务端继续拒绝未经验证的 GUID；
- 无人工锁定时的确定性 Zone/Aisle/Rack 父关系推导仍可能产生 Blocking；
- 不同 SourceHash locked facts 的几何匹配和人工确认尚未实现；
- 真实 Provider-backed BuildScene、供应商/法务/网络/预算证据仍失败关闭；
- 正式 CAD 签收仍需用户有权使用的 DWG/DXF、组织黄金集、真实大文件/故障/性能和人工验收证据。

因此，本报告只证明内部用户已能通过建模 Web UI 启动和跟踪权威 RuleOnly Generation Run，并安全恢复 Failed/Stale Run；不证明外部 AI、正式 CAD 或 GA 签收。
