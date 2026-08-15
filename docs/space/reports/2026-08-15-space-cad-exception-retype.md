# Space Studio CAD 异常对象改类型

日期：2026-08-15

需求：`LM-FR-017`

证据类别：RepositoryImplementation

接受结论：不构成 GA 接受证据

## 结论

Space Studio 现在允许用户在属性检查器中把一个通用 CAD 异常对象改为受支持的另一种语义类型。改型不创建新对象、不改变 LogicalId，也不绕过 Design V1：写入继续通过带编辑租约、Floor Revision、Content Revision/Hash 和幂等标识的原子命令批，命令记录保留前后值。

资产实例不允许借此改变领域类型；未知或空类型在写入前失败。保存后的撤销和重做使用同一命令合同提交补偿批次，场景刷新后 2D/3D 消费同一元素类型。

## LM-FR-017 覆盖矩阵

| 能力 | 当前状态 | 证据边界 |
|---|---|---|
| 改类型 | 已实现 | 本报告中的领域、SQL、契约、前端与 E2E 自动化 |
| 删除 | 已有实现 | 既有 `DeleteObject`、批量删除与补偿命令链 |
| 合并 | 未实现 | 需要独立 typed command、几何/属性冲突规则、身份和撤销合同 |
| 拆分 | 未实现 | 需要独立 typed command、新 LogicalId 分配、属性继承和撤销合同 |
| 重画 | 未实现 | 需要画布绘制交互、几何校验、原子替换和撤销合同 |

因此 WP4 的仓库实现状态为 `Partial`，不是 `Complete`。该校正不改变冻结的整体 72% 基线，也不改变所有 GA Gate 的 `Pending` 接受状态。

## 实现边界

- 契约：`SpaceUpdateElementPropertiesDto.ElementType` 为向后兼容的可选字段；未传时保持现有类型。
- 领域：仅 `SpaceElementRevision` 可改型，并统一规范为 `SpaceElementTypes` 的规范值。
- 服务：目标存在性、资产边界和类型白名单在原子写入前校验。
- 前端：属性面板只提供领域支持列表；保存与撤销快照都携带语义类型。
- 生成物：OpenAPI、C# Client 与 TypeScript SDK 已由权威生成脚本同步。
- 数据库：没有 Schema 变化，不需要迁移。

## 可复现门禁

| 门禁 | 结果 |
|---|---:|
| Space Domain 聚焦单测 | 53 passed / 0 failed |
| Space Unit 全量回归 | 526 passed / 0 failed |
| Design V1 OpenAPI 测试 | 44 passed / 0 failed |
| SQL Server LocalDB 命令批测试 | 1 passed / 0 failed / 0 skipped |
| 前端属性/API 聚焦测试 | 7 passed / 0 failed |
| 前端 Vitest 全量回归 | 780 passed / 0 failed |
| Vue TypeScript 检查 | passed |
| Space Studio 改型、撤销、重做 Playwright | 1 passed / 0 failed |
| Space Studio Playwright 全量回归 | 15 passed / 0 failed |
| OpenAPI/C#/TypeScript SDK 漂移检查 | passed |

真 SQL 用例验证类型从 `Column` 更新为 `Door`、响应场景同步、同一批次幂等重放，以及审计 `AfterJson` 包含新类型。Playwright 用例验证属性面板保存 `Door`、撤销恢复 `Column`、重做再次写入 `Door`。

## 后续任务

1. 异常对象合并。
2. 异常对象拆分。
3. 异常对象画布重画。
4. 三项闭环后重新执行详细 Spec 的逐条 LM-FR 实现审计，再决定 WP4 是否可恢复为 `Complete`。

授权真实 CAD、双 Provider、黄金集和双仓 Pilot 仍是独立外部门禁，本报告不能进入 `acceptedEvidence`。
