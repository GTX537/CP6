# M-OA/WF T5 报告：IAuditable 贴点(Wf_FlowDef/Wf_ApprovalBinding 族)

## 结论
- 目录全量对账：CP6.Entity/DomainModels/Wf 域 **17 个实体**（+1 `WfNotificationType` 静态常量类，非实体不计入）。
- **纳入 5 / 豁免 12 / 目录 17（零漏）**。
- 每实体单行 `IAuditable`（纳入）或 `[审计豁免]` 源码注释（豁免），**零业务字段改动、零迁移**（`git status CP6.Core/Migrations/` = 0）。
- `dotnet test --filter OawfAuditTests` = **21/21 绿**；全量 = **1796 passed / 5 skipped / 0 failed**（= 基线 1775 + 本任务 21，基线未跌）。

## 裁决方法
口径（brief §需求1）：**不可逆治理配置 或 权限授予类 → 纳入；高频运行时流转 / 追加型 / 用户偏好 → 豁免**。
逐实体字段级实查（非按类别套先例）。拦截器 `CP6Context.CaptureFieldAuditBeforeSave` 走 `ChangeTracker.Entries<IAuditable>()` 泛型标记，EntityName=`ClrType.Name`，PK=Id(Guid)→EntityKey，元字段(Id/TenantId/Creator/CreateDate/Modifier/ModifyDate)与 [AuditIgnore]/密钥/RowVersion 由 `BuildChanges` 跳过。

## 全量裁决表（17 实体）

| # | 实体 | 字段级/语义证据 | 裁决 |
|---|------|----------------|------|
| 1 | **Wf_FlowDef** | 流程定义：SchemaJson(节点/边有向图)、FlowKey/FormKey 绑定、Version、Enable、FunctionId/FlowCode。**高危键 oa-designer:* 落库对象**，schema/启停变更影响所有在途流程走向。设计期治理配置。 | **纳入** |
| 2 | **Wf_ApprovalBinding** | 审批绑定单源：BizType→FlowKey 映射、Enable、ConditionJson。改绑/停用直接改变某业务是否走审批、走哪条流程。计划点名。治理配置面。 | **纳入** |
| 3 | **Wf_FormDef** | 表单定义：SchemaJson(字段定义)、Version、Enable、Category。oa-designer 落库对象，schema 改前端渲染+后端 required/类型复核规则。设计期治理配置。 | **纳入** |
| 4 | **Wf_FlowDelegate** | 审批委派：GrantorId→DelegateId、ValidFrom/To、Enable、Scope。委托人把审批权授予代理人——谁把审批权给了谁/何时收回。**权限授予面**（brief 点名 Delegate 类应纳入）。 | **纳入** |
| 5 | **Wf_ApproverMap** | 审批人映射：MapKey/MatchValue→ApproverUserId/ApproverRoleId、OrderNo、Enable。改一行即改某匹配值由谁审批=授予/回收审批权。**权限授予面**（brief 点名 ApproverMap 应纳入）。数据驱动。 | **纳入** |
| 6 | Wf_FlowInstance | 运行时状态载体：CurrentNode/Status/VarsJson 每次推进即变；RowVersion 乐观锁。正确性由 FlowEngine 引擎测试锁定。 | 豁免 |
| 7 | Wf_FlowTask | 高频待办任务：一节点多条(会签)、Status/IsRead/StageIndex/StageRound 幂等流转。引擎测试锁定。 | 豁免 |
| 8 | Wf_FlowToken | 运行时执行点令牌：分叉/合流内核态 Status/StagePlanJson 高频翻转。FlowToken 内核测试锁定。 | 豁免 |
| 9 | Wf_FlowHistory | **仅追加事件日志**：submit/approve/reject… 每动作追加一条、不更新。本身即审批时间线(审计源而非被审计对象)。 | 豁免 |
| 10 | Wf_FlowData | 每关卡不可变表单快照：按 StepSeq 追加"每步变化轨迹"留痕，建后不改。运行时读模型。 | 豁免 |
| 11 | Wf_FlowFormTo | 传签履历台账：运行时读模型，送签建/处理更新(Status/HandledAt 随流转)。引擎测试锁定。 | 豁免 |
| 12 | Wf_FlowCc | 抄送运行时读模型：随流转落行、IsRead/ReadAt 高频翻转。 | 豁免 |
| 13 | Wf_FormData | 表单提交数据：运行时一次提交一行的字段值快照(DataJson)，改版不动旧数据本身即留痕。 | 豁免 |
| 14 | Wf_Notification | 站内通知：运行时随事件追加、IsRead/ReadAt 高频翻转。 | 豁免 |
| 15 | Wf_FormFavorite | 用户个人偏好：填單☆收藏(UserId,FormKey)。 | 豁免 |
| 16 | Wf_InboxPref | 用户个人偏好：信箱显示偏好 PrefsJson 自由结构。 | 豁免 |
| 17 | Wf_ServiceJob | 服务任务异步作业台账：运行时队列 AttemptCount/Status/Lock*/NextAttemptAtUtc 高频翻转 + RowVersion 并发。ServiceJob 扫描/租约测试锁定。 | 豁免 |
| — | WfNotificationType | 静态常量类（`public static class`，无 BaseEntity/无 [Table]/无行）→ **非实体，不计入目录**。 | N/A |

豁免注释已全部落源码（每行 `[审计豁免]` 头注，均标注"OawfAuditTests 负测试坐实零审计行"）。

## TDD 证据（先红后绿）
- **RED**（贴点前跑 OawfAuditTests）：`Failed: 9, Passed: 12, Total: 21` —— 9 个纳入正测试全红（无审计行），12 个豁免负测试已绿。
- **GREEN**（5 实体贴 IAuditable 后）：`Failed: 0, Passed: 21, Total: 21`。
- 纳入正测试真值断言：op1 断言 `EntityName==nameof(...)` + `EntityKey==Id.ToString()`；op2 断言 diff `Field/Old/New` 真值（如 FlowDef.Enable `True→False`、ApprovalBinding.FlowKey `oa-po→oa-po-v2`、FormDef.Version `1→2`、FlowDelegate.Enable 收回 `True→False`、ApproverMap.ApproverRoleId `5→9`）。
- 豁免负测试真实：12 个 `Assert.Empty(db.Sys_FieldAuditLogs.ToList())`，含 Notification create+update(IsRead 翻转)双段。

## 全量回归
`dotnet test`（全量）= **1796 passed / 5 skipped / 0 failed**（1 m 5 s）。基线 1775 未跌，+21 本任务。

## 零迁移证据
`git status --porcelain CP6.Core/Migrations/` = 0 行。IAuditable 为无列标记接口（`interface IAuditable {}`），不映射任何列，EF 模型无 diff。改动面：17 个 Wf 实体文件（5 加接口 + 12 加注释）+ 1 新测试文件。

## Concerns
- 无。IAuditable 为纯标记，零业务逻辑/零 schema 变更；豁免逐条源码注释可追溯；负测试覆盖全部 12 豁免实体。
