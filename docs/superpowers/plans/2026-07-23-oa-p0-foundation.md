# OA P0 基础闭环执行计划

日期：2026-07-23  
状态：Ready for CLI execution  
规格状态：Frozen  
执行方式：Codex CLI 单工作区串行实施  

> **For CLI agents:** 按任务顺序执行。每个任务先补失败测试，再写实现，再跑任务级验证。不得覆盖、还原或提交工作区中已有的用户改动；不得使用 `git reset --hard`、`git checkout --`、`git clean` 或 `git add -A`。

## 1. 目标

本计划把 OA 从“功能演示”推进到可以承载真实业务审批的 P0 基础闭环：

1. Flow/Form 已发布定义不可变，运行实例锁定版本。
2. SFS 保存、校验、落库、起流只有一条服务端权威链。
3. 草稿拥有独立、可恢复、可并发控制的生命周期。
4. 实例、查询、状态、表单字段全部由服务端授权。
5. `PUR_PR` 成为第一条业务页送审、业务页办理、终态回写的黄金路径。

## 2. 冻结规格

- `docs/superpowers/specs/2026-07-23-oa-p0-foundation-epic.md`
- `docs/superpowers/specs/2026-07-23-oa-p0-01-definition-versioning.md`
- `docs/superpowers/specs/2026-07-23-oa-p0-02-authoritative-submission.md`
- `docs/superpowers/specs/2026-07-23-oa-p0-03-draft-lifecycle.md`
- `docs/superpowers/specs/2026-07-23-oa-p0-04-access-and-field-security.md`
- `docs/superpowers/specs/2026-07-23-oa-p0-05-pur-pr-approval-pilot.md`

实施中如发现必须改变 D1–D6，停止编码，先修改规格并重新确认。实现细节可以调整，但不得改变数据真相、信任边界、版本不可变和参与者授权原则。

## 3. 已记录基线

2026-07-23 在当前工作区执行：

| 检查 | 结果 | 处理原则 |
|---|---|---|
| `dotnet test CP6.Tests\CP6.Tests.csproj --no-restore --nologo` | 2225 passed，5 skipped，0 failed | P0 不得造成任何后端回归 |
| `bun run test` | 75 files / 499 assertions passed，但 15 个 `ElSelect` 递归更新未处理异常使命令失败 | 记为既有基线债务；P0 不得新增，发布前必须归零 |
| `bun run type-check` | 既有 `CpListPage.vue` 泛型传播导致多个模块类型错误 | P0 新增/修改文件必须零类型错误；全局发布闸仍要求最终归零 |

当前工作区已有未提交改动，且与 P0 有重叠：

- `CP6.Core/Services/Wf/FlowEngine.Tokens.cs`
- `CP6.Core/Services/Wf/FlowSchema.cs`
- `cp6.web/src/views/oa/designer/**`
- `docs/superpowers/specs/2026-07-07-oa-approval-decoupling-design.md`

CLI 必须在这些文件的现状上增量集成，不能用 HEAD 版本覆盖。

## 4. 全局约束

### 4.1 数据与事务

- `Wf_FlowDef`、`Wf_FormDef` 是稳定头表；不可变内容进入版本表。
- `Wf_FormData` 是 SFS 正式数据真相；`Wf_FormDraft` 是草稿真相；业务表是客制业务真相。
- `Wf_FlowInstance.VarsJson` 只作为流程条件运行副本，不再作为可直接返回的业务数据源。
- SFS 正式提交、草稿提交、任务字段补丁、PUR_PR 送审都必须是短事务，事务内不进行外部网络调用。
- 所有提交/送审入口必须有数据库唯一约束兜底，应用层 `AnyAsync` 不能作为最终防重。
- SQL Server `rowversion` 用于 Draft/FormData 并发控制；测试中用 SQLite 或应用管理并发标记覆盖非 SQL Server 行为。
- 流程事务内只写持久化通知/outbox；SignalR、邮件等外部副作用必须在提交后投递，并以持久化去重键保证重试安全。

### 4.2 安全

- 浏览器不得指定可信 `FlowKey`、`BizType`、`BizId`、定义版本或业务快照。
- 实例读取授权和任务办理授权是两条独立检查，均需通过。
- `hidden` 字段从 schema、data、snapshot DTO 中物理移除，不只是前端隐藏。
- `readonly`/未授权字段的 HTTP patch 返回 403，数据库保持不变。
- 通用 `/api/wf/flow/submit`、`/api/wf/approval/submit` 不再作为浏览器入口。

### 4.3 兼容

- 本轮采用 expand → backfill → switch-read/write → contract-later。
- 旧头表的 `SchemaJson`、`Version`、`FormKey` 暂留一个发布周期，只作为迁移兼容字段。
- 存量无法准确恢复历史版本的记录必须显式标记 `legacy-fallback`，不能伪装成准确 pin。
- 不迁移运行中的实例到新版本。

### 4.4 质量

- 每个任务包含单元或集成测试；安全边界必须有反向测试。
- EF 迁移生成后必须核对，任何与任务无关的模型漂移都停止实施。
- 前端使用现有 Vue 3、Element Plus、Vitest、Playwright 和 Cp* token，不引入新状态管理框架。
- 后端沿用 .NET 8、EF Core 8、xUnit，不升级框架版本。

## 5. 目标架构

### 5.1 定义、提交与运行

```text
Wf_FormDef ──1:N── Wf_FormDefVersion
    │                   │
    │             Wf_FormFlowBinding
    │                   │
    ▼                   ▼
Wf_FormDraft      Wf_FormData ──────┐
                         │            │
                         └──── Wf_FlowInstance
                                   │
Wf_FlowDef ──1:N── Wf_FlowDefVersion
                         │
                         └── Wf_FlowDefVersionDependency
                                  └── pinned SubFlow version
```

### 5.2 SFS 正式提交

```text
POST /api/oa/forms/{formKey}/submissions
        │
        ├─ authenticated user + Idempotency-Key
        ├─ load latest Published FormDefVersion
        ├─ normalize + recompute + validate on server
        ├─ insert Wf_FormData
        ├─ optional active Wf_FormFlowBinding
        ├─ create pinned Wf_FlowInstance
        └─ commit once / rollback all
```

### 5.3 任务字段补丁与办理

```text
POST /api/oa/tasks/{taskId}/decision
        │
        ├─ acting-as grant validation
        ├─ task ownership validation
        ├─ instance participant validation
        ├─ current node read/edit mask
        ├─ FormData rowversion validation
        ├─ whitelist dataPatch
        ├─ server recompute + validate
        ├─ update FormData + VarsJson
        ├─ approve/reject task
        └─ one transaction
```

### 5.4 PUR_PR

```text
PR business page ──POST prNo only──> PurchaseRequestService
                                      │
                                      ├─ load PR + lines
                                      ├─ build trusted snapshot v1
                                      ├─ resolve binding fail-closed
                                      ├─ create unique active instance
                                      └─ PR = Submitted

Inbox row ──detailRoute──> /pur/pr?prNo=...
                                │
                                └─ ApprovalPanel ──decision──> WFS
                                                             │
                                                             └─ callback updates PR
```

## 6. 复用现有能力

| 现有能力 | 复用方式 |
|---|---|
| `FlowEngine` token、会签、并行、包容、退回、超时、服务任务、子流程 | 不重写状态机，只替换 schema 解析来源和启动入口 |
| `Wf_FlowFormTo`、`Wf_FlowData`、`Wf_FlowCc` | 继续作为参与者和时间线来源，但输出前进行字段裁剪 |
| `FormService.RecomputeAndValidate` | 抽出为可复用的服务端规范化/校验核心 |
| `ApprovalDispatcher` + `IApprovalCallback` | 继续保证流程终态与业务回调共享 scoped DbContext |
| `CurrentPermissionContext`、委派/acting-as | 继续解析真实用户与有效用户，补实例参与关系校验 |
| `PurchaseRequestService`、`PrApprovalCallback` | 收紧可信输入与事务顺序，不新建第二套采购审批 |
| Vue `DynamicForm`、Inbox 页面、PrView | 改接新 DTO 和权威端点，不重做 UI 框架 |

## 7. 依赖与执行顺序

| 阶段 | 任务 | 依赖 |
|---|---|---|
| A | A0 基线守卫；A1 数据模型；A2 迁移与回填 | 无 |
| B | B1 定义版本服务；B2 发布与兼容校验；B3 运行 pin；B4 子流程 pin；B5 通知 outbox | A |
| C | C1 SFS 提交；C2 提交 API 收口；C3 草稿服务；C4 草稿 UI | B |
| D | D1 实例访问服务；D2 DTO/字段投影；D3 decision；D4 查询/状态收口 | B；D3 依赖 C1 |
| E | E1 PUR_PR 可信送审；E2 ApprovalPanel；E3 深链与 PR UI；E4 回调/并发 | B、D |
| F | F1 迁移演练；F2 E2E；F3 全量验收与文档 | A–E |

由于当前工作区已有重叠的 WFS/设计器改动，采用单 CLI 会话串行实施，不使用并行 worktree。

## 8. 实施任务

### Task A0：基线守卫与验收脚本骨架

**Files**

- Create: `scripts/verify-oa-p0.ps1`
- Create: `docs/superpowers/plans/2026-07-23-oa-p0-acceptance.md`

**Steps**

- [ ] 在脚本中提供 `-Stage`, `-IncludeE2E`, `-ConnectionString` 参数。
- [ ] 默认只运行无外部依赖检查：后端测试、OA/PUR 前端测试、type-check 增量检查、迁移漂移检查。
- [ ] `-IncludeE2E` 才启动/连接 API 与前端并跑 Playwright。
- [ ] 所有命令保留原始退出码，任何失败最终返回非零。
- [ ] 输出机器可读汇总：阶段、命令、耗时、通过/失败、已知基线差异。
- [ ] 脚本不得包含或打印连接串、令牌、Cookie。

**Verify**

```powershell
powershell -ExecutionPolicy Bypass -File scripts/verify-oa-p0.ps1 -Stage Baseline
```

**Exit**

- 后端基线可重现。
- 前端既有未处理异常和全局 type-check 债务被标为 baseline blocker，不被误报为 P0 新回归。

### Task A1：新增 P0 数据模型与索引

**Create**

- `CP6.Entity/DomainModels/Wf/Wf_FlowDefVersion.cs`
- `CP6.Entity/DomainModels/Wf/Wf_FormDefVersion.cs`
- `CP6.Entity/DomainModels/Wf/Wf_FlowDefVersionDependency.cs`
- `CP6.Entity/DomainModels/Wf/Wf_FormFlowBinding.cs`
- `CP6.Entity/DomainModels/Wf/Wf_FormDraft.cs`

**Modify**

- `CP6.Entity/DomainModels/Wf/Wf_FlowDef.cs`
- `CP6.Entity/DomainModels/Wf/Wf_FormDef.cs`
- `CP6.Entity/DomainModels/Wf/Wf_FormData.cs`
- `CP6.Entity/DomainModels/Wf/Wf_FlowInstance.cs`
- `CP6.Entity/DomainModels/Wf/Wf_ApprovalBinding.cs`
- `CP6.Core/EFDbContext/CP6Context.cs`

**Steps**

- [ ] 按 P0-1/P0-3 精确建立实体和最大长度。
- [ ] Published/Draft 使用明确状态常量，不使用散落 magic number。
- [ ] Version 表唯一键：`(TenantId, DefId, Version)`。
- [ ] 每个定义最多一个 Draft，使用 SQL Server filtered unique index。
- [ ] `Wf_FormFlowBinding` 每表单最多一个 active binding。
- [ ] `Wf_FormData` 增加 version pin、submission key/hash、提交人/时间和 RowVersion；唯一索引必须是 `(TenantId, SubmissionKey) WHERE SubmissionKey IS NOT NULL`，允许存量 null 共存。
- [ ] `Wf_FlowInstance` 增加 FlowDefVersionId、FormDefVersionId、FormDataId。
- [ ] `Wf_FormDraft` 增加 owner、version pin、状态、rebase、legacy、RowVersion。
- [ ] `Wf_ApprovalBinding` 增加 `DetailRoute`；active business instance 唯一闸精确定义为 `(TenantId,BizType,BizId) WHERE BizType IS NOT NULL AND BizId IS NOT NULL AND Status IN (0,4)`。
- [ ] 为参与者查询建立租户前导索引：FlowFormTo 的 ExpectedHandlerId、ActualHandlerId、OnBehalfOfId 分别到 InstanceId，FlowCc 的 RecipientId 到 InstanceId；同时建立 SubmissionKey、Draft owner/status、version dependency 索引。

**Tests**

- Create: `CP6.Tests/Oa/OaP0ModelTests.cs`
- Assert table names、列、外键、两个 filtered unique index 的精确 filter SQL、RowVersion、参与者索引和租户过滤。
- SQLite 生成脚本测试不代替 SQL Server filter 文本断言。

**Exit**

- 模型测试通过。
- 还未改变任何生产读写路径。

### Task A2：expand 迁移、只读预检与幂等回填

**Files**

- Create: `CP6.Core/Migrations/*_OaP0FoundationExpand.cs`
- Create: `CP6.Core/Services/Oa/IOaP0MigrationService.cs`
- Create: `CP6.Core/Services/Oa/OaP0MigrationService.cs`
- Modify: `CP6.WebApi/Program.cs`
- Test: `CP6.Tests/Oa/OaP0MigrationTests.cs`

**Steps**

- [ ] 迁移只新增表、列、索引和外键；不删除遗留列。
- [ ] `--oa-p0-preflight` 只读输出：孤儿 FormKey、孤儿 FlowKey、无法 pin 的实例、遗留 Draft、无法映射的 FormData、无效 SubFlowKey，以及按 `Status IN (0,4)` 计算的重复 active `(TenantId,BizType,BizId)`。
- [ ] 预检存在无法安全迁移的 Running/Suspended 实例时返回非零。
- [ ] `--oa-p0-backfill` 在事务内幂等执行：
  - 当前 FlowDef/FormDef 复制为 Published version。
  - 从旧 `FlowDef.FormKey` 生成 FormFlowBinding。
  - 可确定的实例和 FormData 回填 version pin。
  - 旧 Draft 实例复制到 Wf_FormDraft，保留 LegacyFlowInstanceId。
  - 解析 SubFlowKey 并建立 version dependency。
- [ ] 每一类回填输出 expected/inserted/skipped/error 数量，不输出 DataJson/VarsJson。
- [ ] 重跑 backfill 数量不增长。
- [ ] 生成并审查 idempotent SQL 脚本；不得自动对生产库执行 `database update`。
- [ ] 在真实 SQL Server 容器/测试库运行 expand + backfill；不能只用 SQLite/模型元数据推断 filtered index、rowversion 和并发行为。

**Verify**

```powershell
dotnet ef migrations script --idempotent --project CP6.Core --startup-project CP6.WebApi
dotnet test CP6.Tests\CP6.Tests.csproj --filter "FullyQualifiedName~OaP0Migration"
```

**Exit**

- 空库、典型存量库、孤儿数据三类测试通过。
- 失败回填整批回滚。

### Task B1：Flow/Form 定义版本仓储与不可变守卫

**Files**

- Create: `CP6.Core/Services/Wf/IDefinitionVersionResolver.cs`
- Create: `CP6.Core/Services/Wf/DefinitionVersionResolver.cs`
- Create: `CP6.Core/EFDbContext/DefinitionImmutabilityInterceptor.cs`
- Modify: `CP6.Core/Services/Wf/FlowDefService.cs`
- Modify: `CP6.Core/Services/Wf/FormService.cs`
- Modify: `CP6.Core/Services/Wf/IFlowDefService.cs`
- Modify: `CP6.Core/Services/Wf/IFormService.cs`
- Modify: `CP6.WebApi/Program.cs`

**Steps**

- [ ] 保存只创建/更新 Draft version。
- [ ] Publish 校验 RowVersion，把 Draft 切为 Published，并保证旧 Published 不变。
- [ ] Disable 只影响新启动，不影响 pinned 实例。
- [ ] resolver 提供 `latest published`、`by version id`、`legacy fallback` 三种显式结果。
- [ ] interceptor 拒绝 Published version 的 update/delete，包括绕过 service 的 EF 写入。
- [ ] 旧接口暂时返回 latest Published；写接口兼容映射为 Draft save，不再覆盖 Published。

**Tests**

- Create: `CP6.Tests/Wf/DefinitionVersionServiceTests.cs`
- Create: `CP6.Tests/Wf/DefinitionImmutabilityTests.cs`
- 覆盖 publish、并发保存、停用、legacy fallback、绕过 service 修改/删除。

### Task B2：发布 API、设计器语义与 Form/Flow 兼容校验

**Files**

- Create: `CP6.Core/Services/Wf/IFlowFormCompatibilityValidator.cs`
- Create: `CP6.Core/Services/Wf/FlowFormCompatibilityValidator.cs`
- Modify: `CP6.WebApi/Controllers/Wf/FlowController.cs`
- Modify: `CP6.WebApi/Controllers/Wf/FormController.cs`
- Modify: `CP6.WebApi/Controllers/Oa/DesignerController.cs`
- Modify: `cp6.web/src/api/oa/designer.ts`
- Modify: `cp6.web/src/views/oa/designer/DesignerView.vue`
- Test: `CP6.Tests/Oa/FlowFormCompatibilityValidatorTests.cs`
- Test: `cp6.web/src/views/oa/designer/definitionPublish.spec.ts`

**Steps**

- [ ] 增加显式 Save Draft / Publish / Disable API。
- [ ] 发布与启用 binding 时双向校验 fieldPerms、ApproverFieldName、静态字段引用。
- [ ] fail-closed 返回冻结规格定义的错误码。
- [ ] 设计器保留当前未提交的视觉与 Priority 改动，增量增加 Draft/Published 状态和发布动作。
- [ ] 发布成功前 UI 不显示“已生效”。

**Exit**

- 保存 Draft 不影响新实例。
- Publish 后才成为 latest Published。

### Task B3：所有运行路径改为 version pin

**Files**

- Modify: `CP6.Core/Services/Wf/FlowEngine.cs`
- Modify: `CP6.Core/Services/Wf/FlowEngine.Tokens.cs`
- Modify: `CP6.Core/Services/Wf/FlowEngine.SubFlow.cs`
- Modify: `CP6.Core/Services/Wf/FlowTriggerService.cs`
- Modify: `CP6.Core/Services/Wf/FlowTriggerAdminService.cs`
- Modify: `CP6.Core/Services/Wf/WfTimeoutService.cs`
- Modify: `CP6.Core/Services/Wf/WfServiceJobService.cs`
- Modify: forecast/admin/read-model schema loaders found by `rg "Wf_FlowDefs|SchemaJson|FlowKey" CP6.Core/Services`

**Steps**

- [ ] 新实例启动前解析 latest Published 并写 FlowDefVersionId。
- [ ] runtime schema loader 只按 FlowDefVersionId 读取。
- [ ] pin 为空只允许显式 legacy fallback；新建 Running/Suspended 实例 pin 为空立即失败。
- [ ] timeout、resume、forecast、sendback、trigger、service job 全部复用同一 resolver。
- [ ] `FlowKey` 保留为检索冗余字段，不再决定运行 schema。
- [ ] 保留当前 `FlowEngine.Tokens.cs` 的边 Priority 行为。

**Tests**

- Create: `CP6.Tests/Wf/FlowDefinitionPinTests.cs`
- 扩展既有 FlowEngine、timeout、subflow、trigger 测试。
- 必测：v1 在途 + v2 发布；disable 后在途继续；legacy fallback；新实例空 pin 快速失败。

### Task B4：SubFlow 版本依赖锁定

**Files**

- Modify: `CP6.Core/Services/Wf/FlowDefService.cs`
- Modify: `CP6.Core/Services/Wf/FlowEngine.SubFlow.cs`
- Modify: `CP6.Core/Services/Wf/SubFlowRefValidator.cs`
- Test: `CP6.Tests/Wf/SubFlowDefinitionPinTests.cs`

**Steps**

- [ ] 父流程发布时把每个 SubFlowKey 解析为当时 latest Published target version。
- [ ] dependency 行是父 FlowDefVersion 的不可变组成部分。
- [ ] 新父实例启动时校验依赖 target 仍可启动。
- [ ] 已在途父实例不因子定义 disable 或发布 v2 改变既定版本。

### Task B5：通知 outbox 与提交后投递

**Files**

- Modify: `CP6.Entity/DomainModels/Wf/Wf_Notification.cs`
- Modify: `CP6.Core/Services/Oa/NotificationService.cs`
- Modify: `CP6.WebApi/Services/PersistentWfNotifier.cs`
- Create: `CP6.WebApi/BackgroundServices/WfNotificationDispatchWorker.cs`
- Modify: `CP6.WebApi/Program.cs`
- Test: `CP6.Tests/Oa/WfNotificationOutboxTests.cs`

**Steps**

- [ ] 引擎事务内只幂等写 `Wf_Notification`/outbox 行，不调用 SignalR 或邮件。
- [ ] outbox 使用稳定去重键，例如 `(TenantId,EventType,InstanceId,TaskId,RecipientId)`，重复引擎重试不产生第二事件。
- [ ] 提交后 worker claim Pending 行，分别投递 SignalR/邮件；失败记录 attempts/next-attempt/last-error，不回滚流程。
- [ ] SignalR 已发而 worker 崩溃允许至少一次语义，但前端按 notification id 去重；邮件使用同一事件 id 作为 provider 幂等键，provider 不支持时保留可审计重复风险。
- [ ] 测试 SaveChanges/并发失败时不产生外部通知、重试只有一个 outbox、投递失败不改变流程终态。

### Task C1：SFS 权威提交服务

**Files**

- Create: `CP6.Core/Services/Wf/IFormSubmissionService.cs`
- Create: `CP6.Core/Services/Wf/FormSubmissionService.cs`
- Create: `CP6.Core/Services/Wf/FormSubmissionModels.cs`
- Refactor: `CP6.Core/Services/Wf/FormService.cs`
- Modify: `CP6.WebApi/Program.cs`
- Test: `CP6.Tests/Wf/FormSubmissionServiceTests.cs`
- Test: `CP6.Tests/Wf/FormSubmissionSqliteTests.cs`

**Steps**

- [ ] canonicalize 原始输入并计算 request hash。
- [ ] 校验 Idempotency-Key 长度、字符和租户内唯一性。
- [ ] 从 latest Published FormDefVersion 规范化、compute、校验未知字段与规则。
- [ ] 插入 Wf_FormData；有 binding 时创建 pinned instance，无 binding 时只提交表单。
- [ ] 显式事务包住 FormData、FlowInstance、token、task、history。
- [ ] unique race 时回读已存在结果；相同 hash 返回原结果，不同 hash 返回 E-WF-044。
- [ ] 流程停用、无 Published、校验失败、引擎失败全部不留孤儿 FormData。

### Task C2：SFS 提交 API 与通用入口收口

**Files**

- Create: `CP6.WebApi/Controllers/Oa/FormSubmissionController.cs`
- Modify: `CP6.WebApi/Controllers/Wf/FlowController.cs`
- Modify: `CP6.WebApi/Controllers/Wf/FormController.cs`
- Modify: `CP6.WebApi/Controllers/Wf/ApprovalController.cs`
- Modify: `cp6.web/src/api/wf/form.ts`
- Modify: `cp6.web/src/api/wf/flow.ts`
- Modify: `cp6.web/src/views/oa/catalog/FormInitiate.vue`
- Modify: `cp6.web/src/views/oa/catalog/FormInitiate.submit.spec.ts`

**Steps**

- [ ] 新增 `POST /api/oa/forms/{formKey}/submissions`。
- [ ] 请求体只有 data 和可选 draftId；版本/流程/业务标识来自服务端。
- [ ] FormInitiate 每次用户动作生成稳定 Idempotency-Key，重试复用。
- [ ] `/api/wf/flow/submit` 和 `/api/wf/approval/submit` 对浏览器调用返回 410 或移除路由。
- [ ] 旧 `/api/wf/form/data` 明确 deprecated 并委托权威服务，不能保留第二套逻辑。

### Task C3：独立 Draft 服务、rebase 与提交

**Files**

- Modify: `CP6.Core/Services/Oa/IDraftService.cs`
- Rewrite: `CP6.Core/Services/Oa/DraftService.cs`
- Modify: `CP6.WebApi/Controllers/Oa/DraftController.cs`
- Test: `CP6.Tests/Oa/DraftServiceTests.cs`
- Test: `CP6.Tests/Oa/DraftSubmissionTests.cs`

**Steps**

- [ ] Draft API 按 FormKey/FormDefVersionId 保存，不再接受 FlowKey。
- [ ] 新建、更新、详情、列表返回 Draft DTO 和 RowVersion。
- [ ] owner 和 active 状态统一在服务端校验。
- [ ] stale draft 禁止直接提交。
- [ ] rebase 保留同名兼容字段；删除字段值必须由调用方显式确认。
- [ ] 提交调用 C1，并在同一事务把 Draft 标记 Submitted。
- [ ] 提交失败保持 Active；成功重试返回原 FormData/FlowInstance。
- [ ] 旧 Draft FlowInstance 只读保留一个周期，不再被运行引擎启动。

### Task C4：Draft 产品路径

**Files**

- Modify: `cp6.web/src/views/oa/inbox/InboxDraft.vue`
- Modify: `cp6.web/src/views/oa/catalog/FormInitiate.vue`
- Create/Modify: OA draft API/types
- Test: `cp6.web/src/views/oa/inbox/InboxDraft.spec.ts`
- Test: `cp6.web/src/views/oa/catalog/FormDraftLifecycle.spec.ts`

**Steps**

- [ ] 正常编辑路径只使用 DynamicForm。
- [ ] 列表显示标题、表单、版本、更新时间、stale 状态。
- [ ] 重新打开使用 pinned schema 和完整 DataJson。
- [ ] 两窗口冲突显示“草稿已被更新”，不静默覆盖。
- [ ] rebase 对删除值给出明确确认。

### Task D1：统一实例访问服务

**Files**

- Create: `CP6.Core/Services/Oa/IOaInstanceAccessService.cs`
- Create: `CP6.Core/Services/Oa/OaInstanceAccessService.cs`
- Modify: `CP6.WebApi/Program.cs`
- Test: `CP6.Tests/Oa/OaInstanceAccessServiceTests.cs`

**Steps**

- [ ] 参与者集合包含 starter、当前/历史 ExpectedHandler、ActualHandler、OnBehalfOf、CC recipient。
- [ ] acting-as 先验证有效委派，再以 effective user 检查参与关系。
- [ ] 提供可组合 `IQueryable<Guid>`，查询先 scope 再 filter/page。
- [ ] 普通详情不因拥有菜单权限而自动放宽。
- [ ] 管理员排障留给专用 FlowOps，不旁路普通详情。

### Task D2：显式 DTO 与字段投影

**Files**

- Create: `CP6.Core/Services/Oa/IFormFieldProjectionService.cs`
- Create: `CP6.Core/Services/Oa/FormFieldProjectionService.cs`
- Modify: `CP6.Core/Services/Oa/InboxModels.cs`
- Modify: `CP6.Core/Services/Oa/InboxService.cs`
- Modify: `CP6.WebApi/Controllers/Oa/InboxController.cs`
- Modify: `CP6.Core/Services/Wf/FlowDefService.cs`
- Test: `CP6.Tests/Oa/FormFieldProjectionTests.cs`
- Test: `CP6.Tests/Oa/InboxDetailAuthorizationTests.cs`

**Steps**

- [ ] `DetailAsync` 必须接收 viewer/effective user。
- [ ] detail、forecast、表单 schema 和 snapshots 必须按实例的 FlowDefVersionId/FormDefVersionId 解析，不得从头表或 FlowKey 取 latest。
- [ ] API 不返回 EF entity、VarsJson 或完整 raw snapshot。
- [ ] 按当前办理人、发起人、历史办理人、抄送人计算 read/edit mask。
- [ ] hidden 字段从 schema、data、每步 snapshot 一并移除。
- [ ] 缺 schema 的 legacy fallback 采用最小暴露，不能默认全字段。
- [ ] 旧 `/api/wf/flow/instance/{id}` 改为授权 DTO 或移除。
- [ ] parent/child SubFlow 链接逐条做独立参与者授权；无权限的链接完全省略，不泄露 instanceId、名称或状态。

### Task D3：字段补丁与任务 decision

**Files**

- Create: `CP6.Core/Services/Oa/ITaskDecisionService.cs`
- Create: `CP6.Core/Services/Oa/TaskDecisionService.cs`
- Create: `CP6.WebApi/Controllers/Oa/TaskDecisionController.cs`
- Modify: `CP6.Core/Services/Wf/FlowEngine.cs`
- Modify: `CP6.Core/Services/Oa/InboxService.cs`
- Test: `CP6.Tests/Oa/TaskDecisionServiceTests.cs`
- Test: `CP6.Tests/Oa/TaskDecisionConcurrencyTests.cs`

**Steps**

- [ ] 新增 `POST /api/oa/tasks/{taskId}/decision`。
- [ ] dataPatch 只允许当前节点 edit mask 中字段。
- [ ] 对补丁后的完整数据重新 compute/validate。
- [ ] 检查 FormData RowVersion，冲突返回 409/E-WF-049。
- [ ] FormData.DataJson 与实例 VarsJson 同步后再办理。
- [ ] patch、task、token、history、callback 同一事务。
- [ ] `TaskDecisionService` 拥有整个乐观并发重试边界；每次重试都从数据库重新读取 task/instance/FormData，重新校验 RowVersion、重新投影 patch、重新 compute/validate，再调用无内部重试的单次引擎办理内核。
- [ ] 禁止让现有 `FlowEngine.ActAsync` 的“Reload 所有 tracked entity”只重试办理动作而丢弃调用方已暂存的 FormData/VarsJson patch。
- [ ] 有 edit 字段的任务禁止从 batch 跳过字段规则；无 edit 字段的任务可继续 batch。

### Task D4：查询、状态与所有读取入口收口

**Files**

- Modify: `CP6.Core/Services/Oa/InboxService.cs`
- Modify: `CP6.WebApi/Controllers/Oa/InboxController.cs`
- Modify: `CP6.WebApi/Controllers/Wf/ApprovalController.cs`
- Modify: `cp6.web/src/views/oa/query/FormQuery.vue`
- Test: `CP6.Tests/Oa/OaReadSurfaceAuthorizationTests.cs`
- Test: `CP6.Tests/Oa/InboxQueryPagingTests.cs`

**Steps**

- [ ] FormQuery 从 participant scope 开始，不从全租户实例开始。
- [ ] 服务端分页，移除 `Take(500)`。
- [ ] Pending merged grouping、排序、分页全部下推 SQL，不能全量 materialize 后分组。
- [ ] Stats 使用独立 `CountAsync`/聚合查询，不得调用 PendingAsync 拉取完整列表。
- [ ] total 只统计授权集合。
- [ ] approval status/detail 也走统一访问服务。
- [ ] 所有直接 URL 负向测试返回 403/404，且不泄露对象是否存在的额外字段。

### Task E1：PUR_PR 可信送审与唯一 active instance

**Files**

- Modify: `CP6.Core/Services/Pur/Contracts/IApprovalService.cs`
- Modify: `CP6.Core/Services/Pur/Contracts/ApprovalServiceAdapter.cs`
- Modify: `CP6.Core/Services/Pur/IPurchaseRequestService.cs`
- Modify: `CP6.Core/Services/Pur/PurchaseRequestService.cs`
- Modify: `CP6.WebApi/Controllers/Pur/PurchaseRequestController.cs`
- Modify: `CP6.Core/Services/Wf/ApprovalService.cs`
- Test: `CP6.Tests/Pur/PurchaseRequestApprovalP0Tests.cs`

**Steps**

- [ ] controller 从 CurrentPermissionContext 取得 actorId，不用 username 反查或 Guid.Empty。
- [ ] controller permission 之外，PurchaseRequestService 必须校验 actor 对目标 PR 的业务数据范围/送审资格；仅知道 prNo 不能送审他人无权单据。
- [ ] 服务端加载 PR + lines，构造 snapshot v1。
- [ ] 无/停用 binding fail-closed，删除自动放行。
- [ ] 条件选流从可信 snapshot 求值并 pin Published version。
- [ ] PurchaseRequestService 在修改 PR 前显式开启 ambient transaction；先把 PR 置 Submitted/设置本次 ApprovalRef 关联，再调用 `_approval.SubmitAsync`，且 FlowEngine 的 SaveChanges 必须参与同一 transaction，最后由业务服务 commit。
- [ ] 即时终态 callback 在同一 transaction 中能看到 PR 的合法 Submitted 前态；任一 PR save、WFS、callback 失败全部回滚。
- [ ] filtered unique index 与异常翻译保证双击只产生一个 active instance。
- [ ] 送审失败时 PR 仍为 Draft。

### Task E2：授权聚合 ApprovalPanel 后端

**Files**

- Create: `CP6.Core/Services/Oa/IApprovalPanelService.cs`
- Create: `CP6.Core/Services/Oa/ApprovalPanelService.cs`
- Create: `CP6.WebApi/Controllers/Oa/ApprovalController.cs`
- Modify: `CP6.WebApi/Program.cs`
- Test: `CP6.Tests/Oa/ApprovalPanelServiceTests.cs`

**Steps**

- [ ] 聚合接口按 `(bizType,bizId)` 定位最新实例。
- [ ] 业务读取权限和实例参与者权限都通过才返回。
- [ ] 返回 status、timeline、current task、actions、detailRoute，不返回 VarsJson。
- [ ] action 权限来自真实 task ownership/acting-as，不由客户端声明。

### Task E3：ApprovalPanel、深链与 PR 页面

**Files**

- Create: `cp6.web/src/types/oa/approval.ts`
- Create: `cp6.web/src/api/oa/approval.ts`
- Create: `cp6.web/src/composables/useApproval.ts`
- Create: `cp6.web/src/components/approval/ApprovalPanel.vue`
- Modify: `cp6.web/src/views/pur/PrView.vue`
- Modify: Inbox row/navigation files under `cp6.web/src/views/oa/inbox`
- Modify: router query restoration as needed
- Test: `cp6.web/src/components/approval/ApprovalPanel.spec.ts`
- Test: `cp6.web/src/views/pur/PrApproval.spec.ts`

**Steps**

- [ ] PR 详情 URL 用 `?prNo=` 可刷新恢复。
- [ ] 送审请求只传 prNo。
- [ ] Inbox 的 `detailRoute` 只允许站内相对路由和 `{bizId}` 占位符。
- [ ] ApprovalPanel 统一展示状态、轨迹、当前任务和办理动作。
- [ ] FormDetail 也复用同一办理模型，避免两套动作逻辑。
- [ ] 慢请求、重复点击、409、403、500 都有明确可恢复 UI。

### Task E4：回调原子性与采购权限

**Files**

- Modify: `CP6.Core/Services/Pur/PurApprovalCallback.cs`
- Modify: PR query/service permission path
- Test: `CP6.Tests/Pur/PurApprovalIntegrationTests.cs`
- Test: `CP6.Tests/Pur/PurApprovalAuthorizationTests.cs`

**Steps**

- [ ] Approved → PR Approved；Rejected → PR Draft；重复 callback 无副作用。
- [ ] callback 必须用 `ctx.InstanceId` 与 PR 当前 `ApprovalRef` 做关联；同一 instance 的精确重放可 no-op，旧 instance 的延迟回调或不匹配前态必须抛错并回滚 WFS，不能影响重新送审后的 PR。
- [ ] callback 抛错时流程终态和 PR 状态一起回滚。
- [ ] 无 `pur-pr` 查看权限不能读取 PR。
- [ ] 仅知道 prNo、instanceId 或 ApprovalRef 不能越权读取。

### Task F1：迁移演练与回滚包

**Files**

- Create: `docs/oa/oa-p0-migration-runbook.md`
- Generate: reviewed idempotent forward SQL
- Generate: reviewed rollback SQL to pre-P0 migration

**Steps**

- [ ] 复制生产规模统计到脱敏 staging。
- [ ] 运行 preflight，记录各类数量。
- [ ] 备份后执行 expand migration 和 backfill。
- [ ] 重跑 backfill 验证幂等。
- [ ] 用 v1 在途实例完成一次审批，再发布 v2 验证 pin。
- [ ] 回滚演练只回滚应用读写路径；含新数据后不盲目 drop 新表。

### Task F2：端到端验收

**Files**

- Create: `cp6.web/e2e/oa-p0-sfs.spec.ts`
- Create: `cp6.web/e2e/oa-p0-access.spec.ts`
- Create: `cp6.web/e2e/oa-p0-pur-pr.spec.ts`

**Critical flows**

- [ ] SFS standalone submit。
- [ ] SFS bound submit + duplicate retry。
- [ ] Draft save/reopen/conflict/stale/rebase/submit。
- [ ] v1 instance survives v2 publish。
- [ ] unrelated user cannot detail/query/status。
- [ ] hidden/readonly/edit field behavior。
- [ ] PUR_PR submit → inbox deep link → ApprovalPanel → approve/reject callback。

### Task F3：最终质量闸

- [ ] `scripts/verify-oa-p0.ps1 -Stage All -IncludeE2E` 返回 0。
- [ ] 后端全量至少 2225 passed，0 failed；新增测试全部通过。
- [ ] Vitest 无 failed 且无 unhandled errors。
- [ ] `bun run type-check` 0 errors。
- [ ] `bun run build` 成功。
- [ ] idempotent migration SQL 经人工审查，无意外 Drop/Alter destructive 操作。
- [ ] 安全负向矩阵全部通过。
- [ ] `-Stage SqlServer` 在真实 SQL Server 上通过 SubmissionKey race、active business unique race、Draft/FormData rowversion conflict、expand/backfill。
- [ ] 文档、错误码、五语词条和接入模板完成。

## 9. 测试覆盖图

```text
CODE PATHS                                      USER FLOWS
[GAP→TEST] version publish                      [GAP→E2E] v1 running + publish v2 + finish v1
  ├─ draft save / rowversion
  ├─ compatibility fail-closed
  ├─ published immutable
  └─ latest published switch

[GAP→TEST] SFS submission                       [GAP→E2E] standalone / bound submit
  ├─ normalize + compute + validate
  ├─ idempotency same/different hash
  ├─ transaction rollback
  └─ pinned instance

[GAP→TEST] Draft                                [GAP→E2E] reopen / conflict / stale / rebase
  ├─ owner
  ├─ rowversion
  ├─ removed field confirmation
  └─ submit state transition

[GAP→TEST] Access + field projection            [GAP→E2E] direct URL attack
  ├─ starter/handler/history/cc
  ├─ acting-as
  ├─ hidden removed
  ├─ readonly rejected
  ├─ edit patch + whole-operation retry
  └─ subflow links independently authorized

[GAP→TEST] PUR_PR                               [GAP→E2E] PR → inbox → PR → decision → callback
  ├─ trusted snapshot
  ├─ binding fail-closed
  ├─ unique active instance
  ├─ callback instance correlation + rollback
  └─ business + participant authorization

[GAP→TEST] notification outbox
  ├─ no external delivery before commit
  ├─ engine retry creates one outbox event
  └─ worker failure retries without changing flow state
```

每个 `[GAP]` 在相应任务退出前必须变为单元/集成/E2E 测试。安全路径不能只靠手工验收。

## 10. 生产失败模式

| 失败模式 | 防护 | 用户结果 |
|---|---|---|
| 两个管理员同时保存 Draft definition | RowVersion + 409 | 后写者刷新，不覆盖 |
| 发布后仍从 FlowKey 取最新 schema | version resolver 单入口 + runtime tests | v1 在途继续按 v1 |
| 用户双击 SFS 提交 | SubmissionKey unique + hash | 返回第一次结果 |
| 两请求竞争 PUR_PR 送审 | active business unique index | 一个成功，一个返回已送审 |
| FormData 已写而流程启动失败 | 外层数据库事务 | 两者一起回滚 |
| callback 失败 | callback 在最终 SaveChanges/commit 前 | 流程与 PR 一起回滚 |
| 旧实例延迟 callback 到达 | ApprovalRef/InstanceId 关联校验 | 拒绝旧回调，不影响新一轮审批 |
| 办理并发重试 | decision 重跑完整 read-validate-patch-act 单元 | 不丢字段补丁 |
| SaveChanges 失败但已实时通知 | DB outbox 提交后投递 | 不出现幽灵/重复通知 |
| hidden 字段出现在历史 snapshot | snapshot 同投影器裁剪 | API 不返回字段 |
| batch 绕过 edit 字段 | batch 检测当前 node edit mask | 拒绝并引导单条办理 |
| migration 遇孤儿 Running 实例 | preflight 非零退出 | 部署停止 |
| legacy 无法恢复精确 schema | `legacy-fallback` + 最小暴露 | 明确降级，不假装准确 |

静默失败零容忍：任何无测试、无错误处理且用户看不到原因的路径都阻塞 F3。

## 11. 发布与回滚

### 11.1 发布顺序

1. 部署支持新旧结构的应用版本，但新入口先关闭。
2. 执行只读 preflight。
3. 执行 expand migration。
4. 执行 backfill 并核对数量。
5. 开启 definition pin 和新 SFS/Draft 读写。
6. 开启授权 DTO 和 decision。
7. 仅对 `PUR_PR` 开启业务深链与 ApprovalPanel。
8. 观察一轮真实审批后关闭通用浏览器起审入口。

### 11.2 回滚

- 功能回滚优先关闭新入口，恢复兼容读取；不删除已产生的新版本、FormData、Draft。
- 回滚应用时保证旧应用不会误读新状态；必要时停止写入而不是双写。
- 数据库 downgrade 只在确认新表无新数据时执行；否则保留 expand schema。
- 任何跨版本实例迁移不在本计划内。

## 12. NOT in scope

- SFS 子表、附件、布局设计器、查询导出。
- 流程实例跨版本迁移。
- FlowOps 管理员运维控制台。
- Connector 配置版本化；服务任务仍在进入节点时固化 ActionRefJson。
- `PUR_PO`、预算、财务凭证等第二批业务页面换装。
- 组织树重建；OA 只消费 PUB/IAM 的用户、部门、主管、负责人和角色。
- 修复与 OA 无关的全局 `CpListPage` 类型债务；但生产发布前全局 type-check 仍必须恢复为零。

## 13. Implementation Tasks

- [ ] **T1 (P1)** A0–A2：验收脚本、P0 数据模型、expand 迁移、preflight/backfill。小型历史备份演练已通过；生产规模脱敏 staging 证据仍缺，保持未勾选。
- [x] **T2 (P1)** B1–B5：定义版本、不可变发布、runtime/subflow pin、通知 outbox。
- [x] **T3 (P1)** C1–C2：SFS 权威提交与旧入口收口。
- [x] **T4 (P1)** C3–C4：独立 Draft 生命周期与产品 UI。
- [x] **T5 (P1)** D1–D4：实例参与者授权、字段投影、decision、查询收口。
- [x] **T6 (P1)** E1–E4：PUR_PR 可信送审、ApprovalPanel、深链、callback。
- [ ] **T7 (P1)** F1–F3：迁移演练、E2E、完整验收与接入文档。当前源码兼容回滚已通过；生产规模脱敏 staging、真实旧应用 binary 回滚及其完整签字仍缺，保持未勾选。

## GSTACK REVIEW REPORT

| Review | Trigger | Why | Runs | Status | Findings |
|---|---|---|---:|---|---|
| CEO Review | `/plan-ceo-review` | Scope & strategy | 0 | — | Frozen P0 scope used |
| Codex Review | `codex exec` | Independent CLI plan review | 2 | CLEAR | First run found 12 blockers; second run returned `READY` |
| Eng Review | `/plan-eng-review` | Architecture & tests | 1 | CLEAR | All 12 findings were resolved in the plan and acceptance matrix |
| Design Review | `/plan-design-review` | UI/UX gaps | 0 | — | UI behavior covered by acceptance |
| DX Review | `/plan-devex-review` | Developer experience | 0 | — | CLI runner included |

- **CODEX REVIEW:** transaction ownership, notification outbox, full-operation retry, filtered uniqueness, preflight duplicate detection, callback correlation, business data-scope authorization, pinned-version reads, subflow authorization, SQL-side paging/statistics, participant indexes, and real SQL Server race tests are now explicit plan items.
- **CROSS-MODEL AGREEMENT:** frozen specs, engineering review, and the final CLI review agree on the P0 boundary and implementation order.
- **UNRESOLVED:** 0 product/architecture decisions; D1–D6 frozen; 0 critical engineering gaps.
- **VERDICT:** ENG + CODEX CLEARED — ready to implement.

## 14. 2026-07-23 historical-backup continuation evidence

Status: **CONDITIONAL / not release-accepted**. The local T1/T7 historical
backup drill is green, but T1 and T7 remain unchecked. A 4.1 MB local backup is
not the required production-sized masked staging input, and no actual previous
application binary was available for a full rollback rehearsal.

- All six canonical backup candidates were restored one at a time into exact
  `CP6OaP0Stage_*` databases, inspected with count-only queries, and cleaned
  before the next candidate. The comparison is
  `artifacts/oa-p0-backup-inventory-20260723.json`; it makes no masking or
  production-size claim.
- Selected `CP6DB-local-sync-source-20260721-062913.bak`: 4,149,248 bytes,
  finish `2026-07-21T10:37:53Z`, original database `CP6DB`, head
  `20260720035903_SpaceAnalyticsControlTower`. It was the newest source with
  WF definitions, OA runtime rows, and PUR rows: 20 WF tables / 49 rows,
  15 PUR tables / 35 rows, 6 flow defs, 3 form defs, 4 instances,
  3 purchase requests, and 4 lines. `Wf_FormData` had zero rows.
- The passing drill used
  `CP6OaP0Stage_20260724024204_320e6b97`. Restore took 1,121 ms; expand to
  `20260724000423_OaP0DraftAccess` took 10,065 ms. Read-only preflight reported
  6 flows, 3 forms, 1 Running, 0 Suspended, 3 terminal, and zero orphan,
  unpinnable, invalid-subflow, invalid-draft, or duplicate-active blockers.
- First backfill inserted 6 flow versions, 3 form versions, 4 flow pins, and
  3 bindings with zero errors. The second run inserted zero in all seven
  categories and reported zero errors.
- A real SQL synthetic pin drill on the restored historical database proved
  one v1 instance stayed pinned after v2 publication, one new instance pinned
  v2, and v1 completed after the entry head was disabled. Aggregate evidence:
  2 published versions, 2 pinned instances, 2 distinct pins, 1 completed v1,
  1 new v2, and 1 disabled legacy-readable head.
- Feature/application rollback compatibility preserved the legacy
  `Wf_FlowDef` read columns, all five expanded tables, version rows, pins, and
  the migration head. It did not run a destructive database downgrade.
  `actualPreviousApplicationBinaryAvailable=false`, so this is not a full
  old-binary rehearsal.
- Real SQL passed 2/2 with no skip. Focused OA/WF/PUR passed 782, failed 0,
  skipped 2 SQL-gated tests; those same two tests passed in the real SQL gate.
  Full backend passed 2,275, failed 0, skipped 7. Script safety tests passed
  12/12. `has-pending-model-changes` and every one of the seven
  `verify-oa-p0.ps1 -Stage All` commands exited zero.
- The exact drill database and copied backup were removed and verified absent;
  the selected canonical backup was verified unchanged. No database named
  `CP6DB` was targeted or altered. Retained count-only evidence:
  `artifacts/oa-p0-staging-drill-20260723.json`; TRX:
  `artifacts/oa-p0-focused-20260723.trx` and
  `artifacts/oa-p0-full-backend-20260723.trx`.

Remaining acceptance inputs/work: a production-sized masked backup in staging,
an actual compatible previous application binary and operator rollback
rehearsal, and the still-unclaimed non-PUR live E2E/sign-off portions of F2/F3.
