# OA P0 验收标准与 CLI 证据清单

日期：2026-07-23  
适用计划：`docs/superpowers/plans/2026-07-23-oa-p0-foundation.md`  
原则：没有自动化证据的“看起来能用”不算通过。  

## 1. 验收结论规则

P0 只有三种结论：

- **PASS**：所有 P0-AC 条目通过，迁移演练通过，安全负向用例通过，完整 CLI 验证返回 0。
- **CONDITIONAL**：功能已完成，但仅剩已登记的非 OA 全局基线债务；不得用于生产发布。
- **FAIL**：任一数据一致性、安全、版本 pin、迁移或 PUR_PR 闭环条目失败。

以下项目没有 waiver：

- Published 不可变。
- v1 在途实例不被 v2 改写。
- SFS 原子提交。
- 无关用户不可读取。
- hidden/readonly 服务端强制。
- PUR_PR 无绑定不得自动放行。
- 重复提交不得产生第二 active instance。

## 2. CLI 入口

### 2.1 阶段验证

```powershell
powershell -ExecutionPolicy Bypass -File scripts/verify-oa-p0.ps1 -Stage Definitions
powershell -ExecutionPolicy Bypass -File scripts/verify-oa-p0.ps1 -Stage Submission
powershell -ExecutionPolicy Bypass -File scripts/verify-oa-p0.ps1 -Stage Draft
powershell -ExecutionPolicy Bypass -File scripts/verify-oa-p0.ps1 -Stage Access
powershell -ExecutionPolicy Bypass -File scripts/verify-oa-p0.ps1 -Stage PurPr
```

### 2.2 完整验证

```powershell
powershell -ExecutionPolicy Bypass -File scripts/verify-oa-p0.ps1 -Stage All -IncludeE2E
```

期望：

- 退出码 0。
- 每条命令显示耗时和通过数。
- 不打印密钥、连接串、Cookie、JWT、DataJson、VarsJson。

## 3. 全局门槛

| ID | 验收标准 | 自动化证据 |
|---|---|---|
| P0-AC-G01 | 后端全量 0 failed，既有 2225 passed 不下降 | `dotnet test CP6.Tests\CP6.Tests.csproj --no-restore --nologo` |
| P0-AC-G02 | OA/PUR 新增单元与集成测试全部通过 | `dotnet test --filter "FullyQualifiedName~Oa|FullyQualifiedName~Wf|FullyQualifiedName~Pur"` |
| P0-AC-G03 | Vitest 无 failed、无 unhandled errors | `bun run test` |
| P0-AC-G04 | TypeScript 0 errors | `bun run type-check` |
| P0-AC-G05 | 生产构建成功 | `bun run build`、`dotnet build CP6.slnx --no-restore` |
| P0-AC-G06 | EF 模型没有未生成迁移的变更 | `dotnet ef migrations has-pending-model-changes --project CP6.Core --startup-project CP6.WebApi` |
| P0-AC-G07 | idempotent SQL 只含预期 expand/backfill | 人工审查 `dotnet ef migrations script --idempotent` 输出 |
| P0-AC-G08 | 冻结规格与实现错误码、路由、状态一致 | contract tests + spec link check |
| P0-AC-G09 | filtered unique index、rowversion、并发 race 在真实 SQL Server 通过 | `verify-oa-p0.ps1 -Stage SqlServer` |

## 4. P0-1 定义版本化

| ID | Given / When / Then | 证据 |
|---|---|---|
| P0-AC-V01 | 已发布 Flow v1 并启动实例；发布 v2 后；v1 实例继续读取 v1 全部节点、边和审批人 | `FlowDefinitionPinTests` |
| P0-AC-V02 | v2 已发布；新启动实例；FlowDefVersionId 指向 v2 | `FlowDefinitionPinTests` |
| P0-AC-V03 | 已发布 Form v1 并提交数据；发布 v2 后；旧数据仍按 v1 schema 返回 | `FormDefinitionPinTests` |
| P0-AC-V04 | 尝试通过 service 或直接 EF 更新/删除 Published；操作被拒绝且数据库不变 | `DefinitionImmutabilityTests` |
| P0-AC-V05 | 只保存 Draft 未 Publish；新实例仍使用旧 Published | `DefinitionVersionServiceTests` |
| P0-AC-V06 | 两管理员并发保存同一 Draft；后写返回 409 | SQLite concurrency test |
| P0-AC-V07 | Disable Flow；新启动失败，在途实例仍可完成 | engine integration test |
| P0-AC-V08 | 启动纯业务 WFS；FormDefVersionId/FormDataId 允许为空 | business flow test |
| P0-AC-V09 | 独立 SFS 表单无绑定；可发布和提交 | submission integration test |
| P0-AC-V10 | 父 v1 pin 子 v1；子 v2 发布后；父 v1 仍启动子 v1 | `SubFlowDefinitionPinTests` |
| P0-AC-V11 | binding 或 publish 引用不存在/不兼容字段；fail-closed | compatibility tests |
| P0-AC-V12 | 存量无法精确 pin；API 明确 `legacy-fallback`，不返回伪造版本 | legacy test |
| P0-AC-V13 | 流程事务失败时无外部通知；重试只产生一个 outbox；提交后投递失败可重试且不改变流程 | `WfNotificationOutboxTests` |

## 5. P0-2 SFS 权威提交

| ID | Given / When / Then | 证据 |
|---|---|---|
| P0-AC-S01 | FormInitiate 正式提交；网络只调用新 submission endpoint | Vitest request assertion |
| P0-AC-S02 | 任意成功提交；生成准确 FormDefVersionId 的 FormData | service integration test |
| P0-AC-S03 | 有 binding；FormData 与 pinned FlowInstance 同时存在 | transaction test |
| P0-AC-S04 | 无 binding；FormData 成功，FlowInstanceId 为空 | standalone test |
| P0-AC-S05 | 客户端伪造 compute 值；落库是服务端计算值 | compute test |
| P0-AC-S06 | 缺 required、类型错误、超长、pattern 错误、未知字段；全部拒绝 | validation theory tests |
| P0-AC-S07 | HTTP 请求试图指定 FlowKey/BizType/BizId/version；契约不接受或忽略且无越权影响 | controller contract test |
| P0-AC-S08 | 同 SubmissionKey + 同 payload 重试；返回原结果且数据库各一条 | idempotency test |
| P0-AC-S09 | 同 SubmissionKey + 不同 payload；返回 E-WF-044 | idempotency hash test |
| P0-AC-S10 | 流程停用或引擎失败；FormData/instance/task/history 均不残留 | rollback test |
| P0-AC-S11 | 调用旧通用 flow/approval submit；返回 410/不存在 | API integration test |

## 6. P0-3 Draft

| ID | Given / When / Then | 证据 |
|---|---|---|
| P0-AC-D01 | 新建草稿；只产生 Wf_FormDraft，不产生 instance/token/task/history | draft integration test |
| P0-AC-D02 | 列表/详情；返回完整 DataJson、pinned version、RowVersion | DTO test |
| P0-AC-D03 | 重新打开；DynamicForm 使用 pinned schema，值完整 | Vitest |
| P0-AC-D04 | 两窗口保存；后写返回 409，先写不丢 | concurrency test |
| P0-AC-D05 | 发布新版后打开旧草稿；显示 stale 且禁止直接提交 | service + UI test |
| P0-AC-D06 | rebase 同名兼容字段；值保留 | rebase unit test |
| P0-AC-D07 | rebase 遇删除字段有值；未确认时拒绝 | rebase unit test |
| P0-AC-D08 | 提交失败；Draft 仍 Active | transaction test |
| P0-AC-D09 | 提交成功；Draft=Submitted，FormData/instance 一致；重试返回原结果 | integration test |
| P0-AC-D10 | legacy draft backfill 重跑；数量不增长且异常逐条计数 | migration test |

## 7. P0-4 访问与字段安全

参与角色：

```text
starter
current expected/actual handler
historical expected/actual/on-behalf-of handler
cc recipient
unrelated user
actor acting-as effective user
```

每个读取端点都必须覆盖以上矩阵。

| ID | Given / When / Then | 证据 |
|---|---|---|
| P0-AC-A01 | unrelated user 请求 Inbox detail；403/404，无 DTO 数据 | authorization test |
| P0-AC-A02 | unrelated user 请求 Flow instance、Approval detail、status；全部拒绝 | read-surface theory test |
| P0-AC-A03 | participant 查询；只返回参与实例，total 不泄露全租户数量 | paging integration test |
| P0-AC-A04 | API response；不含完整 EF entity、VarsJson、raw business snapshot | serialization contract test |
| P0-AC-A05 | hidden 字段；schema/data/timeline snapshots 均不存在字段名和值 | projection test |
| P0-AC-A06 | current handler patch edit 字段；成功并同步 FormData/VarsJson | decision test |
| P0-AC-A07 | patch readonly/hidden/unknown 字段；403，数据库不变 | negative decision tests |
| P0-AC-A08 | historical handler/cc；可读合法字段但不能办理 | role matrix test |
| P0-AC-A09 | acting-as 无 grant、过期 grant、effective user 非参与者；全部拒绝 | acting-as tests |
| P0-AC-A10 | 两窗口提交字段补丁；后写 409，不覆盖 | concurrency test |
| P0-AC-A11 | 当前节点含 edit 字段并调用 batch；拒绝绕过 | batch test |
| P0-AC-A12 | legacy 缺 schema；最小暴露，不返回全部数据 | legacy projection test |
| P0-AC-A13 | 用户可见子实例但不可见父实例，或反之；无权 parent/child link 完全省略 | subflow link authorization test |
| P0-AC-A14 | decision 遇并发后重试；完整重读、RowVersion 校验、patch、compute、act 被重新执行，字段补丁不丢 | decision retry integration test |
| P0-AC-A15 | Pending merged list 和 Stats 在大数据集上执行 SQL 分页/聚合，不 materialize 全租户列表 | SQL query/log assertion |

## 8. P0-5 PUR_PR

| ID | Given / When / Then | 证据 |
|---|---|---|
| P0-AC-P01 | PR 页面点击送审；请求只有 prNo | Vitest + network assertion |
| P0-AC-P02 | 客户端修改金额/行数；服务端 snapshot 仍来自数据库 | service test |
| P0-AC-P03 | binding 缺失/停用；送审失败，PR=Draft | integration test |
| P0-AC-P04 | totalEstimatedAmount 命中条件规则；启动正确 Published version | routing test |
| P0-AC-P05 | 成功送审；PR=Submitted，ApprovalRef 指向唯一实例 | integration test |
| P0-AC-P06 | 并发双击；仅一个 active instance | SQL unique race test |
| P0-AC-P07 | Inbox 点击 PUR_PR；跳到 `/pur/pr?prNo=...`，刷新可恢复 | Playwright |
| P0-AC-P08 | 当前审批人在 PR 页面看见 ApprovalPanel 并办理 | Playwright |
| P0-AC-P09 | Approve；PR=Approved，轨迹完整 | E2E + DB assert |
| P0-AC-P10 | Reject；PR=Draft，驳回意见留在 OA 轨迹 | E2E + DB assert |
| P0-AC-P11 | callback 抛错；流程终态和 PR 状态都未提交 | rollback integration test |
| P0-AC-P12 | 无 pur-pr 查看权限；知道 prNo 仍不能读 | controller authorization test |
| P0-AC-P13 | 非实例参与者；知道 instanceId/ApprovalRef 仍不能读轨迹 | panel authorization test |
| P0-AC-P14 | 有菜单 submit 权限但无目标 PR 数据范围；知道 prNo 仍不能送审 | business submit authorization test |
| P0-AC-P15 | PR 重新送审后旧 instance 延迟 callback；回调被拒绝，新 ApprovalRef/状态不变 | stale callback correlation test |

## 9. 迁移验收

### 9.1 Preflight

```powershell
dotnet run --project CP6.WebApi -- --oa-p0-preflight
```

必须输出计数：

- flowDefs / formDefs
- running / suspended / terminal / legacy draft instances
- orphan flow/form keys
- unpinnable instances/form data
- invalid subflow refs
- duplicate active `(TenantId,BizType,BizId)`，active 精确定义为 Status IN (0,4)

有无法安全迁移的 Running/Suspended 数据时退出码非零。

### 9.2 Backfill

```powershell
dotnet run --project CP6.WebApi -- --oa-p0-backfill
dotnet run --project CP6.WebApi -- --oa-p0-backfill
```

第二次运行：

- inserted=0。
- existing/skipped 数量与第一次结果一致。
- 不产生重复 version、binding、dependency、draft。

### 9.3 数据断言

- 每个可迁移 FlowDef 至少一条 Published version。
- 每个可迁移 FormDef 至少一条 Published version。
- 每个新 Running/Suspended instance 的 FlowDefVersionId 非空。
- 新 SFS instance 的 FormDefVersionId/FormDataId 非空。
- 纯业务 instance 的 FormDefVersionId/FormDataId 为空是合法的。
- `(TenantId,BizType,BizId)` active instance 不重复。
- SubmissionKey 在租户内不重复。
- 存量 null SubmissionKey 可多行共存；非 null 使用 filtered unique index。
- legacy draft copy 数与 source draft 数一致。

### 9.4 SQL Server 并发

以下测试必须连接真实 SQL Server，SQLite/InMemory 不能替代：

- 相同 SubmissionKey 并发插入。
- 相同 `(TenantId,BizType,BizId)` Running/Suspended 并发启动。
- Draft RowVersion 两窗口保存。
- FormData RowVersion 两窗口 decision。
- expand migration 在多条 null SubmissionKey 存量数据上成功。

## 10. E2E 场景

### E2E-1：版本 pin

1. 发布请假 Flow/Form v1。
2. 用户 A 提交，实例停在审批节点。
3. 修改审批人/路径并发布 v2。
4. 用户 B 提交。
5. A 实例仍按 v1；B 实例按 v2。

### E2E-2：Draft

1. 保存草稿。
2. 刷新并重新打开，值不变。
3. 第二窗口修改并保存。
4. 第一窗口再保存收到 409。
5. 发布表单 v2，旧草稿显示 stale。
6. rebase 并提交，草稿变 Submitted。

### E2E-3：字段权限攻击

1. 当前节点隐藏 `salary`、只读 `requester`、可编辑 `amount`。
2. UI 只显示合法字段。
3. 直接 HTTP patch 三个字段。
4. hidden/readonly 被拒绝且数据库不变。
5. amount 合法修改后办理成功。

### E2E-4：PUR_PR 黄金路径

1. 创建 PR。
2. 送审并连续双击。
3. 只有一个 instance，PR=Submitted。
4. 审批人从 Inbox 深链进入 PR 页面。
5. 在 ApprovalPanel 办理。
6. Approved/Rejected 分别回写正确状态。
7. unrelated user 用直链访问被拒绝。

## 11. 发布签字

| 角色 | 必须确认 |
|---|---|
| Backend | migration、事务、unique race、callback rollback |
| Frontend | DynamicForm、ApprovalPanel、deep link、错误恢复 |
| Security | participant matrix、hidden/readonly、旧入口关闭 |
| QA | CLI All + E2E 退出码 0 |
| Product | PUR_PR 从送审到终态回写可独立完成 |

最终证据至少包括：

- CLI 汇总日志。
- 测试结果文件。
- migration preflight/backfill 计数。
- idempotent SQL 审查记录。
- 四条 E2E 的截图或 trace。

## 12. 当前基线阻塞项

在 2026-07-23 计划生成时：

1. Vitest 499 个断言通过，但存在 15 个 `ElSelect` 递归更新未处理异常。
2. 全局 type-check 存在 `CpListPage.vue` 泛型传播错误。

这些问题不是 OA P0 可以忽略的生产门槛。实施阶段可以先用“无新增 OA 错误”推进专项任务，但 F3 与最终 PASS 必须让完整命令归零；若选择在独立工作项修复，则本 P0 只能得到 CONDITIONAL，不能标记生产可用。

## 13. 2026-07-23 implementation evidence

Status: **CONDITIONAL / not release-accepted**. T3 and T6 are now complete.
Every locally executable release gate is green, but T1 and T7 remain unchecked:
the required production-sized masked-staging migration/rollback and functional
pin drill could not be represented by a new empty local database.

- Focused OA/WF/PUR coverage passed 782, failed 0, skipped 1; the skipped test
  was the separately gated SQL Server race. The task-level
  `FormSubmission` suite passed 6/6 and the non-SQL PUR/ApprovalPanel filter
  passed 22/22. The full backend suite passed 2,275, failed 0, skipped 6.
- `PurchaseRequestApprovalSqlServerTests` ran on SQL Server 2022 and passed
  1/1 with no skip. Its uniquely named `CP6PurP0_<guid>` database was removed;
  the post-test database count was zero.
- The isolated migration drill used
  `CP6OaP0Drill_20260723212624_69129a5d`. EF applied 114 migrations through
  `20260724000423_OaP0DraftAccess`; preflight was safe with every count zero.
  Both backfills reported expected/inserted/skipped/errors = 0 for every
  category, so the second run was idempotent. The exact drill database was
  verified and dropped. This proves the empty-database path only, not the
  production-sized masked-staging requirement.
- `has-pending-model-changes` reported no drift. The idempotent forward script
  remains `artifacts/oa-p0-forward.sql`, SHA-256
  `74FDC48092B3CE0E0F451B4E31DC5472E73DC84BC51819B5FBB07C2FE992BFE0`.
- Frontend release gates are clean: 79/79 Vitest files and 506/506 assertions
  passed with zero unhandled errors; `bun run type-check`, `bun run build`, and
  `dotnet build CP6.slnx --no-restore --nologo` all exited zero.
- Current-source Playwright ran against API `http://localhost:5177`, Vite
  `http://localhost:5173`, and isolated database
  `CP6OaP0E2E_20260723212925_63a2abe4`. Auth setup plus both PUR business
  scenarios passed 3/3: P07/P08/P09 approve callback and P10 reject-to-Draft
  with timeline comment. API root PID 2456 and Vite root PID 22280 (including
  their recorded descendants) were stopped, both ports were released, and the
  exact database was dropped.
- `verify-oa-p0.ps1 -Stage All`, with the SQL connection supplied only through
  process-local environment state, passed all seven recorded commands and
  included `backend-sqlserver` with `environmentBlocked=false`. Its JSON
  summary contains no connection secret.
- Diagnostic failure traces/screenshots are retained under
  `tmp/oa-p0-e2e-*-failure*-20260723212925`; current-source API/Vite stdout,
  stderr, and PID records are retained under `tmp/oa-p0-e2e-*-20260723212925.*`.

### 13.1 Historical-backup continuation

Status remains **CONDITIONAL / not release-accepted**. This continuation
replaces the empty-database-only limitation with a successful local historical
backup drill and real-SQL functional pin/feature rollback evidence. It does
not satisfy the production-sized masked-staging or actual previous-binary
rollback requirements, so T1/T7 remain unchecked.

- Six canonical backups were restored and counted sequentially; every exact
  inspection database and copied backup was cleaned before the next restore.
  `artifacts/oa-p0-backup-inventory-20260723.json` contains only filenames,
  sizes, backup finish times, original database names, migration heads, and
  aggregate WF/OA/PUR counts.
- Selected source:
  `CP6DB-local-sync-source-20260721-062913.bak`, 4,149,248 bytes, finish
  `2026-07-21T10:37:53Z`, original database `CP6DB`, head
  `20260720035903_SpaceAnalyticsControlTower`. Counts: 20 WF tables / 49 rows,
  15 PUR tables / 35 rows, 6 flow defs, 3 form defs, 4 instances,
  3 purchase requests, 4 lines, and 0 form-data rows. The source is neither
  production-sized nor proven masked.
- Passing isolated database:
  `CP6OaP0Stage_20260724024204_320e6b97`. Restore 1,121 ms; expand 10,065 ms
  through `20260724000423_OaP0DraftAccess`.
- Preflight: 6 flow defs, 3 form defs, 1 Running, 0 Suspended, 3 terminal,
  0 legacy drafts; orphan flow/form, unpinnable active/form data, invalid
  subflow, duplicate active business key, and invalid legacy draft counts were
  all zero.
- Backfill 1 inserted flow versions 6, form versions 3, flow pins 4,
  bindings 3, and zero in the remaining categories; all error counts were
  zero. Backfill 2 inserted zero in every category and all errors remained
  zero.
- Real SQL pin/rollback test passed 1/1: the old instance retained v1 after
  v2 publish, a new instance pinned v2, the new-entry head was disabled, and
  the pinned v1 instance completed. Count-only assertions recorded 2
  published versions, 2 pinned instances, 2 distinct pins, 1 completed v1,
  1 new v2, and 1 disabled legacy-readable head.
- Compatibility rollback retained four legacy `Wf_FlowDef` read columns, all
  five expanded tables, the version/pin rows, and the expanded migration head.
  No schema downgrade ran. No previous application binary was available;
  therefore a full old-binary rollback rehearsal is explicitly not claimed.
- Real SQL gate passed 2, failed 0, skipped 0. Focused OA/WF/PUR passed 782,
  failed 0, skipped 2 (the two SQL-gated tests separately passed on SQL).
  Full backend passed 2,275, failed 0, skipped 7. Script helper tests passed
  12/12. `has-pending-model-changes`, backend/frontend builds, Vitest,
  type-check, and all seven `verify-oa-p0.ps1 -Stage All` commands exited zero.
- Cleanup: exact database dropped without single-user mode and verified
  absent; exact copied backup removed and verified absent; canonical source
  verified unchanged. `CP6DB` was never targeted or altered. Retained evidence:
  `artifacts/oa-p0-staging-drill-20260723.json`,
  `artifacts/oa-p0-focused-20260723.trx`, and
  `artifacts/oa-p0-full-backend-20260723.trx`.

Remaining blockers for PASS: production-sized masked-staging evidence, an
actual previous compatible application binary plus operator rollback
rehearsal, and still-unclaimed non-PUR live E2E/release sign-off work.
