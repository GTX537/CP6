# F-T2 报告：波⑤ 引擎基建六件套 gstack QA harness 三件套（只写不跑）+ DoD 自查

- **分支**：`feat/wfs-engine-infra`　**Commit**：见文末（docs-only，零代码/零迁移改动）
- **交付**：`docs/superpowers/qa/wfs-engine-infra/{README.md, seed.sql, qa_infra.ps1}`
- **状态**：三处 `STATUS: written, not run` 声明齐（README §顶 / seed.sql §头 / qa_infra.ps1 `.NOTES`）；零执行痕迹（无截图、无运行日志、无 PASS/FAIL 输出捕获）。
- **口径**：结构照抄波④ `wfs-inbox-ux` + 波③ `wfs-flow-trigger`（Approved 先例）。剧本 8 条按可测面拆分：ps1 覆盖 2/3/6/7/8（纯 HTTP 确定性）；DB/worker 演练覆盖 1/3-runtime/4/5；浏览器走查覆盖 1/2/3/6/7/8。

## 一、剧本 ↔ 代码 cross-check（file:line 实引，非 plan 文本）

| 剧本面 | 断言/路径 | 代码锚点（实读核实） |
|---|---|---|
| 2 年历导入 | `POST /api/oa/work-calendar/import-jp` → `data.inserted`；`GET ?year=` → `data.{year,isEmpty,items}` | `WorkCalendarController.cs:29-36`（List）/`:57-60`（ImportJp，`Ok2(new{inserted=...})`）；权限 `[RequirePermission("oa-work-calendar","Calendar.Edit")]` `:40/49/58` |
| 2 空态前提 | A1 boot 已植 35 假日 → 非空；清表看空态 | A-T2 报告：Program.cs 默认租户 `JapaneseHolidaySeed.For(DefaultTenant)` 幂等 `(TenantId,Date)`；README §3.1 给清表 SQL |
| 3 E-WF-027 静态 | designer save 无 IsError 边 → 400 含 `E-WF-027`；有边 → 200 | `FlowSchemaValidator.cs:132-134`（`TimeoutAction=="errorEdge"` 无 IsError 出边→加码）；`DesignerController.cs:52-63` save，`:31` `Err`=`{code:400,message:e.Message}`（**不拆**，裸码） |
| 3 errorEdge 运行时 | 超时→节点作废+沿错误边路由+`timeoutError` 注入 | `WfTimeoutService.cs:93`（`case "erroredge"`，小写！`:59` `.Trim().ToLowerInvariant()`）→ `IFlowEngine.TimeoutAdvanceErrorEdgeAsync`（B-T1 `FlowEngine.Tokens.cs`）；扫描面 `:46`（`DueAt<=now && !TimeoutHandled && Status==Pending`）；worker `WfTimeoutScanWorker.cs:12`（1min）/`:36`（`ScanOnceAsync(DateTime.Now,...)`） |
| 3 来源放宽 | approval/subFlow 为合法错误边来源 | `FlowSchemaValidator.cs:128-130`（`ErrorEdgeSourceTypes.Contains(T(n))`）；常量 `:21-23`=`{serviceTask,approval,subFlow}` OrdinalIgnoreCase |
| 4 三既有超时零回归 | remind/approve/reject/escalate 不变 | `WfTimeoutService.cs:63-93`（四 case 全小写，`erroredge` 插在 escalate 与 default 间）；单测 `Timeout_Reject_ByteEquivalent_NoRegression`（B-T1） |
| 5 清理 worker | 终态删/在途留/占坑永不清/老化计数 | C-T1 报告：`WfCleanupService`（`Wf_ServiceJob` 删 `Status∈{2,3,4}&&CompletedAtUtc<cutoff`；`Wf_TriggerFire` 占坑 `InstanceId==null&&Error==null` 永不清 + 老化计数）；worker 每日 03:00 UTC；`ServiceJobStatus` `WfStatus.cs:51-55`（Succeeded=2/Running=1/Cancelled=4） |
| 6 连接器全流程 | create→`{id}`；list `hasAuth` 掩码 `authJson` 恒 null；`TimeoutSec>=lease`→400；403 | `WfConnectorController.cs:22`（`[Route("api/oa/wf-connector")]`）/`:46-55`（GET 掩码）/`:57-64`（create，`Connector.Edit`）；`WfConnectorView.AuthJson=>null` `WfConnectorService.cs:34`，`HasAuth` `:32`；`SaveReq` 字段 `name/displayName/baseUrl/authJson/timeoutSec/enabled` `:10-19` |
| 6 E-WF-028 呈现 | message=`"E-WF-028"`（纯码）+ `detail` 后缀 | `WfConnectorController.cs:36-43`（`Err` 按 `\|` 拆：message=码，detail=诊断）；服务抛 `"E-WF-028\|timeoutGteLease:.."`（D-T1 `WfConnectorService.ValidateLease`；lease=`WfServiceJobService.LeaseDuration`=300） |
| 6 403 文案 | 非 RoleId=1 → 403 含 `Connector.Edit` | `RequirePermissionAttribute`（fail-closed，无 admin bypass）；权限 seed `WorkCalendarConnectorPermissionSeed` 授 RoleId=1（F-T1） |
| 7 节点 HTTP 覆盖 | `serviceTimeoutSec>=lease`→400；method∉{GET,POST,PUT,DELETE}→400；PUT+5→200 | 静态 `FlowSchemaValidator.cs:115-124`（⑧b：timeout `>0&&<=3600`、method 值域 E-WF-028）；保存侧 `DesignerService.SaveAsync`（`ServiceTimeoutSec>=租约`→抛裸 `"E-WF-028"`，E-T1 报告）；节点字段 `FlowSchema.cs:81-82`（`ServiceHttpMethod`/`ServiceTimeoutSec`）；webApi 必填 `ServiceConnectorName`+`ServicePath` `:76-77`（避 E-WF-016 抢先，`Validator:106`） |
| 8 租户时区 E-WF-028 | 不可解析→400 含 `E-WF-028`；Asia/Tokyo→200 | `TenantController.cs:15`（`[Route("api/platform/tenant")]`）/`:17`（`[RequirePlatformAdmin]`）/`:59-70`（Update，catch→`BizException(ex.Message)`）/`:104`（`UpdateTenantRequest{...,TimeZoneId=null}`）；服务 `TenantAdminService.UpdateAsync` 非空 `FindSystemTimeZoneById` 失败抛 `"E-WF-028"`（E-T2 报告） |
| flow submit/act | `POST /api/wf/flow/submit {flowKey,varsJson}`；`POST /api/wf/task/{id}/act` | `FlowController.cs:53-64`（submit，`oa-form-catalog:submit`）/`:66-77`（act，`oa-inbox:approve`）/`:89-90`（record 形状） |

## 二、偏离声明（brief vs 实落，逐条给依据）

1. **连接器走 API 创建、不 raw-seed**（硬要求已明示）：`Wf_Connector.AuthJsonEncrypted` 是 DataProtection 密文（purpose `Wfs.Connector.Auth`），raw INSERT 的伪串执行期 `Unprotect` 必败。故 ps1 经 `POST /api/oa/wf-connector` 创建（唯一走「即写即加密＋掩码读」契约的路径）。同波③ message 触发器经 API 捕获一次性 key 先例。
2. **workdays timer 节点 / errorEdge approval 节点在浏览器现建，不入 seed**：brief 剧本 1/3 原文即「真浏览器：设计器建 timer 节点 / 建带 errorEdge 的 approval」。故 seed 只 raw 一条**已合法**的 `qa-inf-erroredge`（供运行时 DueAt 回拨演练），E-WF-027 负例由 ps1 POST 非法 schema 触发。降低 seed 出 serviceKind JSON 大小写错的风险。
3. **ps1 只覆盖可 HTTP 确定性断言的 5 条**（2/3-静态/6/7/8）；1（workdays DueAt）、3-runtime（超时路由）、4（三动作回归）、5（清理 worker）落 README DB/worker 演练。理由：这些需 worker 定时触发 + SQL 时间回拨，纯 HTTP 无法确定性驱动——同波③ timer misfire 剧本先例（SQL 回拨 + 等扫描）。
4. **scenario 2 ps1 断言放宽为「inserted=35 或 0」**：A1 boot 已有 35 假日，首次 import 幂等返 0；真空态需先清表（README §3.1 给 SQL）。诚实反映幂等语义，不伪造 35。
5. **scenario 8 需 platform admin**：`TenantController` 是 `[RequirePlatformAdmin]`，故 seed 专置 `qa_inf_padmin`（`IsPlatformAdmin=1`）驱动，ps1 用它登录；tz 改完回拨为空（留 QA 库中性）。

## 三、DoD 自查结论（照 plan Global Constraints）

README §8 给全量对照表。F-T2 系 docs-only，代码级闸由各前置任务报告佐证、主控全量闸复跑；harness 自身贡献＝末行「gstack QA harness 齐（8 剧本）」。**结论**：三件套完整、与实码逐条 cross-check（§一表 file:line）；唯一带生产动作的未决项＝**DataProtection 密钥环持久化（D-T0）**——已在 README §2.3 + 本报告 watch items 标注为部署闸前必落项。其余后端 2110 绿/5 skip（F-T1）、前端 463 绿（E-T2）、EF 恰一次迁移 `WfsInfra`、五语 seed 齐、零硬编码色、JP 35 日幂等，均在前置任务边界已绿。

## 四、watch items（复核/live QA 须知）

1. **🔴 DataProtection 密钥环（D-T0）**：Dev 单实例默认位置可跑；**生产上线前必配 `DataProtection:KeyPath`（`PersistKeysToFileSystem` 共享卷）+ `SetApplicationName`**，否则容器重建/多实例后既有连接器密文解不开（同 SSO ClientSecret 换机隐患 `runbook.md:112`）。live QA 期间勿中途重建容器。
2. **oa-designer:edit / oa-form-catalog:submit / oa-inbox:approve 授权假设**：ps1 designer/submit/act 端点依赖 RoleId=1 已被既有 OA seed 授这三键（非本波种）。若 QA 库这些键缺失，相关剧本会 403——README §2.1 已注明「须 role 1」，属既有 OA 权限面，非本波欠账。
3. **erpEcho 租户优先无 HTTP 断言面**：D-T2 未暴露合并目录端点，租户优先由单测 `Catalog_MergesBothSources`/`Resolve_TenantRowPreferred` + 真实外呼佐证（README §6.2），非 ps1 断言。
4. **节点覆盖上线线**：ps1 只证保存侧 E-WF-028 值域；PUT/5s 真达下游由 `NodeHttpOverrideTests` 捕获式 handler + live 真外呼佐证（README §6.3）。
5. **common.edit/cancel/save 跨模块缺 seed**（F-T1 concern 承接）：DB 驱动 i18n 下回退裸 key，连接器 dialog 沿此既有模式；记为全局 `common.*` seed 后置票，非本波。
6. **live QA 8 剧本用户在场**：隔离库 CP6DB_OA 起后端+前端 → ps1（2/3/6/7/8）+ DB/worker（1/3-runtime/4/5）+ gstack 真浏览器（1/2/3/6/7/8）。抓 bug 当场 TDD 修入 `CP6.Tests/Wf/**` 或 `cp6.web` vitest。
