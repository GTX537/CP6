### Task F-T3: gstack QA harness（只写不跑）+ DoD 自查

**Files:**
- Create: `docs/superpowers/qa/wfs-flow-trigger/README.md`（剧本）
- Create: `docs/superpowers/qa/wfs-flow-trigger/seed.sql`
- Create: `docs/superpowers/qa/wfs-flow-trigger/qa_flow_trigger.ps1`（HTTP e2e，ASCII 数据）

- [ ] **Step 1: 写 harness**（结构照 `docs/superpowers/qa/wfs-service-task/` E-T3 先例：README 剧本 + seed.sql + ps1；隔离库 `CP6DB_OA` 真 SQL Server，harness 只写不跑服务器）。剧本 8 条：
  1. **管理页建三型触发器**（真浏览器）：触发器 tab → 分别建 timer（预设「每日 9 点」+ cron 预览显示 5 个未来时刻）/ event（eventKey=`QA|OnEchoAsync` + varsMap）/ message（varsSchema=`orderNo,amount`，**创建后弹出明文 key 且仅此一次**——刷新后不可再见）。
  2. **手动试发**：timer 触发器「试发」→ toast 带 instanceId → 流水抽屉出现 1 行成功（实例链接可点）。
  3. **timer 短周期发起**：seed 一个 `*/1 * * * *` 每分钟触发器 → 等 ≤90s → 流水自动 +1、NextDue 前移、实例落信箱。
  4. **event 联动**：POST `/api/oa/wf-trigger-echo/fire`（Echo 样例源）body `{eventKey:"QA|OnEchoAsync",eventId:"QA-EV-1",payloadJson:"{\"OutboundNo\":\"OB-1\"}"}` → 实例发起且 VarsJson 含 varsMap 映射值；**同 eventId 重发** → FiredCount 含幂等跳过，实例不增。
  5. **message e2e**（ps1）：`POST /api/oa/flow-triggers/{id}/fire` 三头齐 → 201 {instanceId}；同 Idempotency-Key 重放 → 200 同 instanceId；错 key → 401；停用后 → 404（与不存在 GUID 的 404 响应体逐字段一致）；缺 Idempotency-Key → 400；body 65KB → 400；白名单外字段不入 VarsJson。
  6. **key 重置**：重置 → 新明文一次性显示 → 旧 key 打端点 401、新 key 201。
  7. **保存校验**：cron 填 `not a cron` 保存 → 400 E-WF-022 文案（五语抽 2 语验 i18n）；FlowKey 填停用流程 → E-WF-023。
  8. **流水抽屉**：查看 message 触发器流水 → 时间/结果/实例链接/错误列齐（含一条人为失败：发起人停用后试发 → Error 显示 E-WF-022）。
  - seed.sql：OA 单数表名、`SET QUOTED_IDENTIFIER ON`；seed 一个 enabled 流程（复用 ServiceTask harness 的 FlowDef 模式）+ QA 发起人。
- [ ] **Step 2: commit** — `git add -A && git commit -m "test(wfs-trigger): F-T3 gstack QA harness(8 剧本+seed+e2e 脚本)"`
- [ ] **Step 3: 末期 live QA（用户在场）** — 隔离库 `CP6DB_OA` 起后端 + 前端 → 跑 ps1 e2e + gstack 真浏览器过 8 剧本。**抓 bug 当场 TDD 修**。

---

## DoD / 验收

- [ ] 后端 `dotnet test CP6.Tests/CP6.Tests.csproj` 全绿（1509[5 skip] → +N）；**既有 Wf/Integration 测试字节等价**（引擎零改动、dispatcher 既有路由零改动）。
- [ ] 前端 `npm run test`（320 → +N）/ `npm run type-check` / `npm run build` 全绿。
- [ ] EF `dotnet ef migrations has-pending-model-changes` clean；**本波恰一次迁移 `WfsFlowTrigger`**（两表四索引，零其他改动）。
- [ ] **零跨模块污染**：`git diff --stat fb90d75..HEAD` 中 Integration 目录仅 `IntegrationEventDispatcher.cs`（1 注入+1 fallback 分支）与新增 `IWfTriggerBridgeHook.cs`；无 Space/WMS/MES/FIN 业务文件。
- [ ] spec §8 测试矩阵全覆盖（见下表）；E-WF-022/023/024 保存+运行时双检各有专测。
- [ ] 五语 seed 齐（ZhCN/ZhTW/En/Ja/Ko）、LangKey 无重复；权限点 FlowTrigger.View/Edit seed + RoleId=1 授权。
- [ ] 零硬编码色（CpTag tone / Design System token）。
- [ ] gstack QA harness 齐（8 剧本）+ live QA 全过（用户在场，隔离库 CP6DB_OA）。

### 覆盖核对（spec §8 → 测试 → 任务）

| spec §8 条目 | 测试 | 任务 |
|---|---|---|
| FireAsync 幂等撞键返回既有实例 | `Fire_SameKey_Replays_ExistingInstance_NoSecondInstance` | A-T2 |
| Enabled=false 拒绝 | `Fire_Disabled_Rejected_NoFireRow` | A-T2 |
| StarterUserId 停用 E-WF-022 | `Fire_StarterDisabled_EWF022_ErrorBackfilled` + `Starter_MissingOrDisabled_EWF022` | A-T2 / F-T1 |
| StartAsync 失败流水回填 | `Fire_SubmitThrows_EWF024_ErrorBackfilled_RowKept` | A-T2 |
| timer 到期扫描发起 | `DueTimer_Fires_AdvancesNextDue_WritesFire` | B-T2 |
| NextDueUtc 前移抢占（并发两 worker 只发一次） | `TwoWorkers_SameDue_FiresExactlyOnce` | B-T2 |
| **占坑两段式崩溃恢复（不丢发不双发）** | `CrashBetweenPhases_RecoveryBackfills_NoLoss_NoDouble` + `RecoveryGrace_NotYetElapsed_SlotUntouched` | B-T2 |
| misfire 只补最近一次 | `Misfire_MultipleMissedDue_OnlyLatestFired` | B-T2 |
| cron 边界（月末/闰年） | `NextUtc_Day31_SkipsShortMonths` / `NextUtc_Feb29_OnlyLeapYear` | B-T1 |
| event eventKey 匹配多触发器逐发 | `OnEvent_MatchesMany_FiresEach_WithPerTriggerKey` | C-T1 |
| varsMap 映射 | `MapVars_*` + `OnEvent_VarsMap_Applied` | C-T1 |
| outbox 失败重试路径（dispatcher fallback 路由） | `Dispatch_TargetWF_OnEventAsync_RoutesToReplay_AnySource` | C-T2 |
| **部分成功重放去重（3 发 1 失败→重放仅补 2）** | `OnEvent_PartialFail_OutboxFailed_ReplayTopsUpOnlyMissing` | C-T1 |
| 未匹配零动作 | `OnEvent_NoMatch_ZeroAction_SkippedRow` | C-T1 |
| message key 常量时间校验 | `Verify_*`（等长闸+FixedTimeEquals）+ `Filter_WrongKey_401` | D-T1 / D-T2 |
| 幂等头缺失 400 | `Filter_MissingIdempotencyKey_400` | D-T2 |
| 白名单过滤 | `FilterBySchema_*` + `Fire_FirstCall_201_WithInstanceId_SchemaFiltered` | C-T1 / D-T2 |
| **404 不泄露存在性（停用=不存在）** | `Filter_DisabledTrigger_404_SameShapeAsUnknown` | D-T2 |
| 64KB 上限 | `Fire_OversizeBody_400` | D-T2 |
| 幂等重放 200 既有实例 | `Fire_SameIdempotencyKey_200_SameInstance` | D-T2 |
| QA harness（三型/预览/试发/流水/key 一次性） | 剧本 1~8 | F-T3 |

### 执行顺序与依赖（spec §10）

**T-A（A-T1 → A-T2）→ { T-B（B-T1 → B-T2 → B-T3）‖ T-C（C-T1 → C-T2）‖ T-D（D-T1 → D-T2）} → T-E（E-T1 → E-T2）→ T-F（F-T1 → F-T2 → F-T3）**

- T-B/T-C/T-D 三波仅依赖 T-A 契约，可三线并行（并行时各自独立 worktree 或串行执行皆可，合并后跑全量闸）。
- E-T1 依赖 B-T1（WfCronHelper 初始 NextDueUtc/预览）+ D-T1（key 生成）；E-T2 依赖 E-T1。
- F-T1 依赖 E-T1（Validator 挂点已在）；F-T2 依赖 E-T2（键面定稿）；F-T3 依赖全部。
- 共 **14 个任务**。每任务收口：`--filter Wf` 既有全绿（C-T2 跑全量）+ commit 不 push。

---

*生成于 2026-07-05，由 spec `2026-07-05-wfs-event-trigger-start-design.md`（唯一权威）细化。执行铁律：引擎零改动；E-WF-022~024 双检；零跨模块污染（dispatcher fallback 唯一 Integration 触点）；占坑两段式与幂等闸的语义以本计划「共享契约」末条为准，与 spec §3.1/§3.2 一致。*






