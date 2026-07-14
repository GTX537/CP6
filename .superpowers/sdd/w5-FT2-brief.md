## Task F-T2: gstack QA harness（只写不跑）+ DoD 自查

**Files:** Create `docs/superpowers/qa/wfs-engine-infra/{README.md, seed.sql, qa_infra.ps1}`

- [ ] **Step 1: 写 harness**（结构照 ServiceTask E-T3 先例；隔离库 `CP6DB_OA` 真 SQL Server，只写不跑服务器）。剧本 8 条：
  1. **年历勾选→timer 3 工作日实算**（真浏览器）：年历页→勾一天为假日→设计器建 timer 节点 `workdays=3`→试跑→DueAt 落 3 个工作日后 09:00（跨假日/振替验算）。
  2. **年历空态导入**：新租户年历页显示空态提示+「导入日本法定假日」按钮→点击→35 行入库→日历渲染假日态。
  3. **approval 超时走失败边实况**：建带 `TimeoutAction=errorEdge`+IsError 边的 approval→触发超时→原待办作废、token 进失败边节点、`timeoutError` 变量注入；无 IsError 边保存→设计器报 E-WF-027（抽 2 语验 i18n）。
  4. **三既有超时动作零回归**：remind/approve/reject/escalate 各跑一遍确认不变。
  5. **清理 worker**：seed 超龄终态 job + 占坑 `Wf_TriggerFire` + 老化占坑→触发清理→终态删、在途/占坑留、OperLog 记删除+老化计数。
  6. **连接器 tab 全流程**：建连接器（凭证输入）→列表 `HasAuth` 掩码不回显→刷新后仍掩码→执行服务任务解密成功；`TimeoutSec<租约` 保存→E-WF-028；租户连接器与 app EchoConnector 同名→租户优先。
  7. **节点 HTTP 覆盖**：serviceTask webApi 节点填 method=PUT/timeout=5→执行按节点值。
  8. **租户时区**：租户设 `Asia/Tokyo`→timer `untilDate`/workdays 按东京时刻解释；改时区不批量重算提示；`TimeZoneId` 填乱码→E-WF-028。
  - seed.sql：OA 单数表名、`SET QUOTED_IDENTIFIER ON`；seed enabled 流程 + QA 用户 + 一个连接器。ps1：连接器 CRUD + E-WF-028 e2e（ASCII 数据）。
- [ ] **Step 2: commit** — `git add -A && git commit -m "test(wfs-infra): F-T2 gstack QA harness(8剧本+seed+e2e脚本，只写不跑)"`
- [ ] **Step 3: 末期 live QA（用户在场）** — 隔离库 `CP6DB_OA` 起后端+前端→跑 ps1+gstack 真浏览器过 8 剧本。抓 bug 当场 TDD 修。

---

## DoD / 验收

- [ ] 后端 `dotnet test CP6.Tests/CP6.Tests.csproj` 全绿（1509[5 skip] → +N）；**既有 Wf 测试字节等价**（三既有超时动作/ComputeDueUtc 三模式/连接器 app 兜底零回归）。
- [ ] 前端 `npm run test`（320 → +N）/ `npm run type-check` / `npm run build` 全绿。
- [ ] EF `dotnet ef migrations has-pending-model-changes` clean；**本波恰一次迁移 `WfsInfra`**（两新表 + 一新列，零其他改动）。
- [ ] **零跨模块污染**：`git diff --stat fb90d75..HEAD` 仅落在 `{Sys,Wf}` 实体 / `Services/{Wf,Sys}` / WebApi(Program/BackgroundServices/Controllers/Oa+Sys/Seed) / `cp6.web/src/{api,views}/oa`；无 Space/WMS/MES/FIN/PUR 业务文件。
- [ ] spec §8 测试矩阵全覆盖（见下表）；E-WF-027/028 各有静态+服务层专测。
- [ ] 五语 seed 齐（ZhCN/ZhTW/En/Ja/Ko）、LangKey 无重复；权限点 `Calendar.View/Edit` + `Connector.View/Edit` seed + RoleId=1 授权。
- [ ] 零硬编码色（CpTag tone / Design System token）。
- [ ] **日本假日 seed 35 日期**（2026×18 + 2027×17，含振替休日与 2026-09-22 国民の休日），seed 幂等（(TenantId,Date) 去重）。
- [ ] **DataProtection 密钥环持久化已落地**（D-T0；生产配 `DataProtection:KeyPath`），runbook:112 隐患修复说明补齐。
- [ ] gstack QA harness 齐（8 剧本）+ live QA 全过（用户在场，隔离库 CP6DB_OA）。

### 覆盖核对（spec §8 → 测试 → 任务）

| spec §8 条目 | 测试 | 任务 |
|---|---|---|
| 例外反转矩阵（假日/补班/普通周末/普通工作日） | `IsWorkday_ExceptionReversalMatrix` | A-T2 |
| AddWorkdays 跨周末+假日+振替 | `AddWorkdays_SkipsWeekendsHolidaysAndSubstitute` | A-T2 |
| 366 天防死循环 | `AddWorkdays_366ConsecutiveNonWorkdays_FailsFast_NoInfiniteLoop` | A-T2 |
| seed 幂等 | `ImportJapaneseHolidays_Idempotent_35Rows` + `Items_Cover2026And2027_35Dates_AllDistinct` | A-T2/A-T4 |
| ComputeDueUtc 四模式 + 东京 tz + FireHour 落点 | `ComputeWorkdaysDue_LandsOnFireHour_ServerLocalToUtc` / `ComputeDueUtc_ExistingThreeModes_ByteEquivalent` / `WorkdaysTokyoTimeZoneTests` | A-T3/E-T2 |
| errorEdge 路由 + 待办作废 + 三既有动作零回归 | `Timeout_ErrorEdge_VoidsPendingTask_RoutesAlongErrorEdge` / `Timeout_Reject_ByteEquivalent_NoRegression` | B-T1 |
| 无 IsError 边配置被 E-WF-027 拦 + 来源集合放宽 | `ApprovalTimeoutErrorEdge_WithoutErrorEdge_E027` / `SubFlowErrorEdge_NowAllowed_NoE017` / `StartErrorEdge_StillRejected_E017` | B-T1 |
| 清理：终态删/在途留/占坑永不清/保留期=0/分批/老化告警 | `Cleanup_DeletesTerminalOlderThanRetention_KeepsRunningAndRecent` / `Cleanup_RetentionZero_Disabled_NothingDeleted` / `Cleanup_Batches_DeletesAllOverMultiplePasses` / （波③表就绪后）占坑+老化计数 | C-T1 |
| 连接器：租户优先 app 兜底 + 密文往返 + 掩码 + E-WF-028 + 目录合并去重 | `Resolve_TenantRowPreferred_*` / `Resolve_FallsBackToApp_*` / `Save_EncryptsAuth_ExecuteDecrypts_ReadMasks` / `Save_TimeoutBelowLease_E028_Rejected` / `Catalog_MergesBothSources_TenantRowDedups` | D-T1 |
| 节点覆盖优先级 + E-WF-028 值域 | `NodeHttpOverrideTests` | E-T1 |
| 时区 null 全等回归 + 东京 untilDate/workdays + DST 跳变口径 | `NullTimeZoneId_FallsBackToServerLocal_Regression` / `TokyoTimeZoneId_Resolves` / DST 定点 | E-T2 |
| QA harness（年历实算/连接器全流程/超时错边实况） | 剧本 1~8 | F-T2 |

### 执行顺序与依赖（spec §10）

**I-A（A-T1 → A-T2 → A-T3 → A-T4）→ { I-B（B-T1 → B-T2）‖ I-C（C-T1）‖ I-D（D-T0 → D-T1 → D-T2）} → I-E（E-T1 → E-T2）→ I-F（F-T1 → F-T2）**

- A-T1 一次落全部三处 schema + 唯一迁移 `WfsInfra`；A-T2~A-T4 只消费该 schema。
- I-B/I-C/I-D 三波仅依赖 I-A 契约，可三线并行（各自 worktree 或串行皆可，合并后跑全量闸）。**D-T0 是 D-T1 硬前置**。
- I-E 依赖 I-A（workdays 计算接线点）+ I-D（连接器节点覆盖）；E-T2 依赖 E-T1。
- I-F 依赖全部；F-T2 live QA 用户在场。
- 共 **14 个任务**。每任务收口：`--filter Wf`（或 `Wf|Sys`）既有全绿 + commit **不 push**。

---

*生成于 2026-07-05，由 spec `2026-07-05-wfs-engine-infra-design.md`（唯一权威）细化。执行铁律：worker 照抄 `TenantScopeRunner` 口径；errorEdge 节点级清场不连坐；一次迁移 `WfsInfra`；错误边来源集合单一常量（本波写全集含 subFlow，子流程 spec 只加测试）；E 波紧跟 D 波；零跨模块污染；零硬编码色。DataProtection 密钥环持久化（D-T0）是连接器加密硬前置。*
