# Space Studio WP6 发布 Warning 明确认领

日期：2026-08-14

分支：`codex/space-publish-warning-ack`

基线：`origin/main@8e999d7c`

## 结论

仓库内的发布 Warning 已从“通用风险勾选”升级为可校验的独立证据：Publish Preview 对选中的 ValidationRun 及其完整 Warning Issue 集生成稳定 SHA-256；用户确认后，Publish Attempt 必须回传同一哈希。缺失确认、证据变化和后台历史重发均失败关闭。

本任务关闭 WP6 的 Warning 认领合同、服务端 fence 与 UI 行为，不代表 WP6 或核心 GA 完成。真实 CP6 WMS、SQL Server、故障恢复、对账、监控告警、运行手册、双仓 Pilot 和五方签字仍是硬门禁。

## 实现边界

- `SpacePublishPreviewDto` 返回 `validationWarningCount` 与条件性 `warningAcknowledgementHash`。
- 哈希输入为 schema 版本、ValidationRun ID 和排序去重后的 Warning Issue ID；持久摘要数量不一致按 `SPACE_VALIDATION_STALE` 失败。
- `CreateSpacePublishAttemptRequest` 条件性接受 `warningAcknowledgementHash`；有 Warning 且缺失时返回 `SPACE_PUBLISH_WARNING_ACKNOWLEDGEMENT_REQUIRED` / 422，哈希变化返回 409。
- 发布编排器在事务前和 Serializable 事务内各校验一次，避免 Preview 与写入之间的集合变化。
- 发布管理页显示 Warning 数量并要求独立复核勾选；既有审批/风险确认仍保留，两者不能互相替代。
- 历史重发遇到 Warning 时停止自动发布，生成的 Ready 版本继续可由操作者进入正式 Publish Preview 完成人工确认。
- OpenAPI、C# 客户端和 TypeScript SDK 已重新生成；`validationWarningCount` 是必有响应字段，哈希在零 Warning 时可为空。

## 自动化与门禁

| 门禁 | 结果 |
|---|---:|
| Warning hash / 422 / 409 策略单测 | 5/5 passed |
| OpenAPI 聚焦 | 42/42 passed |
| 发布管理组件聚焦 | 5/5 passed |
| Space Unit | 506/506 passed |
| CP6.Tests | 2,877 passed / 19 environment skipped |
| Client | 71/71 passed |
| Space Integration | 305 passed / 104 SQL/environment skipped |
| Web Unit | 763/763 passed |
| Vue type-check / production build | passed |
| OpenAPI + C#/TypeScript SDK drift | passed |
| Release solution | 0 warning / 0 error |

本机没有配置 `CP6_TEST_SQLSERVER`，发布 Preview、Orchestrator、WMS 恢复相关 SQL 场景被跳过；这些 skip 没有计入真实环境通过证据。

## 未关闭门禁

- 在真实 SQL Server + CP6 WMS 执行发布绿地、超时自动恢复、部分成功对账、历史重发、同 PublishPlan 幂等重试和旧 Published 连续服务。
- 证明自动恢复不超过 15 分钟、人工对账不超过 4 小时。
- 完成监控、告警、备份恢复、运行手册、安全越权矩阵和演练记录。
- 完成真实 Provider/黄金 CAD、Viewer 性能、两仓各 14 天 Pilot 与产品/QA/WMS/架构/安全签字。
