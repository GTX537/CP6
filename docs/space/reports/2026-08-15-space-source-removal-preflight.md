# Space 来源移除引用预检报告

日期：2026-08-15
任务分支：`codex/space-source-removal`

## 结论

详细 Spec LM-FR-005 的仓库纵切已闭环：用户在移除来源前必须先查看服务端引用预检；活动 Draft、任务或设计引用会失败关闭，历史任务、工件、问题和导入审计会明确标记为保留证据。确认移除只软删除当前来源记录，不级联删除物理文件或历史证据。

这不是文件销毁接口。原始文件继续受现有 Retention/Tombstone 流程管理，不能通过 Space Studio 来源列表绕过保留期限、引用检查或对象存储删除审计。

## 权威合同

- `GET /api/space/design/v1/versions/{versionId}/sources/{sourceId}/removal-preview` 返回来源 RowVersion、Version ContentRevision、是否可移除、物理文件保留语义，以及逐类“阻断/保留”引用计数。
- `POST /api/space/design/v1/versions/{versionId}/sources/{sourceId}:remove` 要求 `space:source:upload` 与 `space:model:edit` 两项权限、`Idempotency-Key`、Expected ContentRevision 和 Expected Source RowVersion。
- Apply 在 Serializable 事务中重新读取 Version、Source 和全部活动引用；预检后发生 Revision、RowVersion 或引用变化时零写入，并返回稳定 Conflict/恢复动作。
- 活动扫描/解析、Queued/Running Job、Current Generation Run、Floor Underlay、当前 Design Revision 或当前设计元数据会阻断。
- 终态 Job、Artifact、Issue、CAD Preparation、终态 Generation Run、标定记录和导入 Command Audit 不阻断逻辑移除，但继续持久保留并在预检中展示。
- 工作台“来源”面板区分阻断引用与保留证据；只读/窄屏状态不能提交，确认文案明确物理文件和审计不会被级联删除。

## 自动化证据

- Space Source 领域聚焦：15/15 passed。
- SQL Server LocalDB 真库聚焦：3/3 passed、0 skipped，覆盖无引用来源的软删除/幂等回放/文件保留、活动 Job 阻断、终态 Job 保留后可移除，以及当前 Design Revision 引用阻断。
- Design V1 OpenAPI、权限与外部主体边界：127/127 passed。
- 前端 API 与来源面板：4/4 passed；Vue TypeScript 检查通过。
- OpenAPI 必填字段、C#/TypeScript SDK 生成和 SDK 漂移检查通过；本纵切没有数据库 Schema 或 Migration 变化。
- 全量回归：Space Unit 540/540、Space Integration 真 SQL 447/447（0 skipped）、CP6.Tests 2,932 passed（19 个既有环境门禁 skipped）、Web 170 个文件/862 个测试；production build 与完整 solution Release 0 warning / 0 error，EF 模型无待生成 Migration。

## 未关闭范围

- 本纵切不实现物理文件销毁；真实对象存储的 Retention/Tombstone 演练仍属于生产等价安全与恢复证据。
- LM-FR-010～016、019/019A 仍须继续逐项审计三条路径的剩余差距。
- 真实授权 DWG/DXF/Excel/PDF、两条 Site 批准 Provider、CP6 WMS、双仓 14 天 Pilot 和五方签字仍是核心 GA 硬门槛。

因此 LM-FR-005 的仓库实现完成，但 WP4 继续为 `Partial/Pending`，核心 GA 继续为 72% / `NoGo`。
