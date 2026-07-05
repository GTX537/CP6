# WFS 引擎基建与多租户深化（六件套）设计

> 生成于 2026-07-05（brainstorming 已确认，WFS 深化三期 Spec B）。上游：ServiceTask spec §13 与波③ spec §9 的留后条目兑现——工作日/日历延时、错误边放宽 approval、终态 job 清理、per-tenant 连接器、per-node HTTP 覆盖、租户时区。
> 落码位：`CP6.Entity/DomainModels/{Sys,Wf}`、`CP6.Core/Services/Wf`、`CP6.WebApi`、`cp6.web/src/views/oa`。

---

## §0 背景、范围与决策

### §0.1 范围（In / Out）

**In**：W-1 基建波＝①工作日历（新表+管理页+日本法定假日 seed+`workdays` 延时模式）②approval 超时错误边（TimeoutAction 第四动作）③终态 job/流水清理 worker；W-2 多租户波＝④per-tenant 连接器（Wf_Connector 表+DataProtection 加密）⑤per-node HTTP method/timeout 覆盖⑥租户时区（Sys_Tenant.TimeZoneId）。

**Out（→ §9 YAGNI）**：多国假日日历（首切日本）；连接器凭证轮转/密钥版本化；cron 的工作日语义（触发器 cron 保持正交）；approval 错误边扩展到挂起（Suspended 保持人工指派闭环——用户拍板）。

### §0.2 锁定决策（用户已拍板 2026-07-05）

| # | 决策 | 依据 |
|---|------|------|
| D1 | 工作日历 = **新表 + 年历勾选管理页 + 日本法定假日 seed（2026–2027）**；周六日默认非工作日，例外表双向反转（假日/调休补班） | 用户选项确认；调休补班需业务自助 |
| D2 | approval 错误边触发源 = **仅超时**（`TimeoutAction="errorEdge"` 第四动作）；驳回不走错误边（已有剪枝/连坐语义，避免两套机制打架）；挂起保持人工指派 | 用户选项确认 |
| D3 | 终态 job **硬删**（非归档）：流程履历权威在 FlowHistory/FormTo，`Wf_ServiceJob`/`Wf_TriggerFire` 是运维台账；保留期 app 配置默认 180 天 | 设计呈现已确认 |
| D4 | 连接器凭证加密 = **ASP.NET DataProtection**（`IDataProtector`，密钥环随应用管理，零自造密码学） | 平台标准做法 |
| D5 | 连接器解析顺序 = **租户表优先 → app 级注册兜底**（EchoConnector 等既有注册零改动） | 向后兼容铁律 |
| D6 | 三表改动合并**一次迁移 `WfsInfra`**（Sys_WorkCalendar + Wf_Connector + Sys_Tenant.TimeZoneId 列） | 迁移最小化惯例 |

---

## §1 现状锚点（逆向真实，不编造）

- **timer 延时**：`ComputeDueUtc(node, varsJson)`（ServiceTask spec §4.7）现支持 `duration/untilDate/untilExpr` 三模式（`ServiceDelayMode/ServiceDelayValue`）。
- **超时动作**：`WfTimeoutService.cs:58-77` `switch(action)`：`remind`（软：催办+顺延）/ `reject`（硬：自动驳回）/ `escalate`（硬：升级换人）——第四 case 的插点。
- **错误路由**：`AdvanceAlongErrorEdge`（ServiceTask P-A 落地）；错误边来源校验现仅 serviceTask（ServiceTask spec §13 留后条目「放宽到 approval」即本 spec ②）。
- **worker 骨架**：`WfServiceJobScanWorker` + `TenantScopeRunner.ForEachTenantAsync`（逐租户 CreateScope + ITenantContext.CurrentTenantId 赋值切换，波③ plan 侦察已核实）——清理 worker 照抄。
- **连接器**：`IWfConnector { Name, DisplayName, CallAsync }` + 波①票 3 追加的 `MaxCallDuration`；app 级 DI 注册（`Program.cs:136-138` 一带）；`WebApiExecutor` 按 Name 解析。
- **租户实体**：`Sys_Tenant.cs` 存在（`CP6.Entity/DomainModels/Sys/`）。
- **假日/日历**：仓库零假日/日历表（三期侦察 grep 确认），净新增。
- **错误码水位**：子流程 spec 用 025/026，本 spec 从 **E-WF-027** 起。

---

## §2 W-1 ① 工作日历 + `workdays` 延时模式

### §2.1 数据（迁移 `WfsInfra` 之一）

```csharp
[Table("Sys_WorkCalendar")]
public class Sys_WorkCalendar : BaseTenantEntity
{
    public DateTime Date { get; set; }        // 例外日（date 粒度，unique(TenantId,Date)）
    public bool IsWorkday { get; set; }       // true=补班（周末却上班）；false=假日（工作日却休）
    public string? Note { get; set; }         // "元日" / "振替休日" / "臨時休業" 等
}
```

规则：`IsWorkday(date) = 例外表命中 ? 行.IsWorkday : (周一~周五)`。**日本法定假日 seed**：2026–2027 两年（元日/成人の日/建国記念の日/天皇誕生日/春分/昭和の日/憲法記念日/みどりの日/こどもの日/海の日/山の日/敬老の日/秋分/スポーツの日/文化の日/勤労感謝の日 + 振替休日 + **国民の休日**（祝日法「挟まれ日」规则，如 2026-09-22——plan 终审已裁决纳入，日历事实正确性优先），seed 幂等（(TenantId,Date) 去重），植入默认租户。
**非默认租户防静默缺失**：无例外行的租户 workdays 按纯周末算，元旦审批照发没人会发现——管理页年历**空态提示**「本租户未维护假日日历」+「导入日本法定假日」按钮（复用 seed 逻辑，写当前租户）。

### §2.2 `WorkdayCalculator`（`CP6.Core/Services/Wf`，纯查询服务）

```csharp
public interface IWorkdayCalculator
{
    /// <summary>date 起顺延 n 个工作日（n≥1；当天不算；跳过非工作日）。date 按租户时区解释（§7）。</summary>
    Task<DateTime> AddWorkdaysAsync(DateTime dateLocal, int n, CancellationToken ct);
    Task<bool> IsWorkdayAsync(DateTime dateLocal, CancellationToken ct);
}
```

防御：连续 366 天无工作日（例外表被灌满）→ 抛异常快速失败，不死循环。

### §2.3 timer 第四延时模式 `workdays`

`ServiceDelayMode` 值域加 `"workdays"`：`ServiceDelayValue` = 整数字符串（如 `"3"`）。`ComputeDueUtc` 扩展：按租户时区取当日 → `AddWorkdaysAsync` → 当日**营业开始时刻 09:00**（app 配置 `Wfs:WorkdayFireHour` 默认 9）转回 UTC。校验：值非正整数 → 既有 E-WF-016 家族口径。设计器 timer 面板延时模式 radio 加第四项。**触发器 cron 不掺工作日语义**（正交，Out）。

---

## §3 W-1 ② approval 超时错误边

1. `WfTimeoutService.cs` switch 加 `case "errorEdge"`（硬动作：置 Handled）：作废该节点在途待办（对齐 reject 分支的清理口径）→ 错误变量注入（`timeoutError`: nodeId/dueAt）→ `AdvanceAlongErrorEdge(token)` 沿 IsError 边路由。token 在并行支内时后续行为由血缘/剪枝既有机制管辖，零新增。
2. 错误边来源校验放宽：FlowSchemaValidator「IsError 边只许出自 serviceTask」的规则放宽为「serviceTask / approval / subFlow」（subFlow 由 Spec A 落，条件写成类型集合一处维护）。
3. **E-WF-027**：`TimeoutAction="errorEdge"` 的 approval 节点必须有 IsError 出边（静态）；同时保留反向规则——非上述类型节点带 IsError 出边仍报错。
4. 设计器：approval 面板超时动作下拉加「超时走失败边」选项 + validateClient 镜像 E-WF-027。

---

## §4 W-1 ③ 终态 job/流水清理

`WfServiceJobCleanupWorker`（BackgroundService，每日 03:00 一轮，模式照抄 ScanWorker + TenantScopeRunner）：

- 删 `Wf_ServiceJob`：Status ∈ {Succeeded, Failed, Cancelled} 且 `CompletedAtUtc < now - 保留期`。
- 删 `Wf_TriggerFire`：`FiredUtc < now - 保留期` 且（`InstanceId != null` 或 `Error != null`）——**未完成占坑行永不清**（timer 补跑依据）。
- 保留期：`Wfs:CleanupRetentionDays` 默认 180；`<=0` = 禁用清理。
- 分批删（每批 500，防长事务/锁表），每轮记 OperLog 一行（删除计数）。
- **幂等窗口契约（spec 评审补）**：`Wf_TriggerFire` 既是审计也是幂等闸——清理即意味着 **message 端点的幂等保证窗口 = 保留期**（调用方拿 180 天前的 Idempotency-Key 重放会重复起单）；此契约写进 message 端点文档与波③ spec §3.4 呼应。timer/event 键含到期时刻/事件 Id 不会自然复现，不受影响。
- **老化占坑告警**：`InstanceId` 与 `Error` 均空且超龄（> `Wfs:StaleReservationAlertDays` 默认 7 天）的占坑行永不清但**每轮 OperLog 记计数**——这是补跑 worker 持续失败的信号，非静默黑洞。

---

## §5 W-2 ④ per-tenant 连接器（`Wf_Connector` 表）

### §5.1 数据（迁移 `WfsInfra` 之二）

```csharp
[Table("Wf_Connector")]
public class Wf_Connector : BaseTenantEntity
{
    public string Name { get; set; } = "";        // 解析键，unique(TenantId,Name)
    public string DisplayName { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    [Column(TypeName = "nvarchar(max)")]
    public string? AuthJsonEncrypted { get; set; } // DataProtection 密文：{type:"apiKey|basic|bearer", ...}
    public int TimeoutSec { get; set; } = 30;
    public bool Enabled { get; set; }
    [Timestamp] public byte[]? RowVersion { get; set; }
}
```

### §5.2 行为

- 加密：`IDataProtectionProvider.CreateProtector("Wfs.Connector.Auth")`；管理页保存时加密、执行时解密；**读接口永不回显明文**（返回 `hasAuth: true` 掩码）。
- **运维前提（plan 必核实项）**：DataProtection **密钥环必须持久化到共享存储**（现有部署的 `PersistKeysTo*` 配置现状）——否则换机/重建容器/多实例部署会使全部凭证密文不可解、所有租户连接器瘫痪。若现状未配置，密钥环持久化作为本波前置任务落地。
- 解析（D5）：`WebApiExecutor` 按 Name 解析时**先查租户表**（Enabled 行 → 包装成动态 `DbWfConnector : IWfConnector`，HttpClient 走 `IHttpClientFactory`，超时=TimeoutSec）→ 未命中回落 app 级注册字典。目录端点（C-T3）同口径合并两源（租户行优先去重）。
- **E-WF-028**：保存时校验 `TimeoutSec*1s ≥ 租约 LeaseDuration` 拒绝（波①票 3 启动护栏的保存时前移；app 级连接器仍靠启动护栏）。
- 管理页：连接器 tab（列表/新建/编辑/启停；凭证输入即写不回显）。权限点沿波③ MenuAction 口径（`oa-flow-admin` 家族）。

## §6 W-2 ⑤ per-node HTTP 覆盖 + ⑥ 租户时区

- **⑤**：`FlowNode` 加 `ServiceHttpMethod?`（GET/POST/PUT/DELETE，默认连接器口径）/ `ServiceTimeoutSec?`（POCO 零迁移）。`WebApiExecutor`：节点覆盖优先 → 连接器默认。节点 TimeoutSec 同受 E-WF-028 口径校验（静态：值域+上限；租约比对在保存时）。面板 webApi 段加两个可选输入。
- **⑥**：`Sys_Tenant` 加 `TimeZoneId?`（IANA/Windows id，`TimeZoneInfo.FindSystemTimeZoneById` 可解析；迁移 `WfsInfra` 之三）。消费点：timer `untilDate`/`workdays` 与触发器 cron 的本地时刻解释——统一经新 `ITenantClock.GetTenantTimeZone()`（缺省 app 默认 `Wfs:DefaultTimeZone`，再缺省服务器时区）。租户管理页加时区下拉。**存量行为保持**：TimeZoneId null 时与现状完全一致。
  - **时区变更自愈口径**：改时区**不批量重算**既有触发器的 NextDueUtc——下次发火后按新时区重算即自愈（最多一次旧时区发火，管理页保存时提示此口径）。
  - **DST 口径**：cron 本地时刻落在 DST 跳过区间 → 取下一有效瞬间；落在重复区间 → 取首次出现。日本无 DST，但字段不限日本，口径写死防歧义。

---

## §7 校验与错误码汇总

| 码 | 规则 | 层 |
|---|---|---|
| **E-WF-027** | TimeoutAction=errorEdge 的节点必须有 IsError 出边；IsError 边来源 ∈ {serviceTask, approval, subFlow} | 静态 + validateClient 镜像 |
| **E-WF-028** | 连接器/节点 TimeoutSec ≥ 租约 → 拒绝保存；TimeZoneId 不可解析 → 拒绝保存 | 保存时（服务层） |

`workdays` 模式值校验并入既有 E-WF-016 家族（serviceTask 配置无效）。

---

## §8 测试策略

- **日历**：例外反转矩阵（假日/补班/普通周末/普通工作日）、AddWorkdays 跨周末+假日+振替、366 天防死循环、seed 幂等。
- **workdays 模式**：ComputeDueUtc 四模式矩阵 + 租户时区换算（东京 vs UTC）+ FireHour 落点。
- **超时错误边**：errorEdge 动作路由 + 待办作废 + 无边配置被 E-WF-027 拦 + 三既有动作零回归。
- **清理**：终态删/在途留/占坑行永不清/保留期=0 禁用/分批。
- **连接器**：租户行优先 app 兜底、密文往返（保存加密/执行解密/读端点掩码）、E-WF-028、目录合并去重、节点覆盖优先级。
- **时区**：null 与现状全等回归、东京租户 untilDate/cron 解释。
- **QA harness**：年历勾选→timer 3 工作日实算、连接器 tab 全流程（凭证不回显）、approval 超时走错边实况。
- 基线全绿；EF 迁移恰一次 `WfsInfra`。

---

## §9 YAGNI / 留后

- 多国假日集（首切日本；表结构天然支持其他租户自维护）。
- 连接器凭证轮转/密钥版本化/审计明细（DataProtection 密钥环随平台）。
- cron 工作日语义、per-node 重试策略覆盖、response-map 覆盖。
- 挂起（Suspended）走错误边（用户拍板保持人工指派闭环）。

---

## §10 分期 / 任务波次（供 writing-plans 细化）

- **I-A 迁移 + 日历**：`WfsInfra` 迁移（三表改动）+ WorkdayCalculator + 假日 seed + workdays 模式 + 面板。
- **I-B 超时错误边**：第四动作 + 来源放宽 + E-WF-027 + 面板。
- **I-C 清理 worker**。
- **I-D 连接器租户化**：Wf_Connector + DataProtection + 解析合并 + E-WF-028 + 管理 tab。
- **I-E 节点覆盖 + 时区**：POCO 双字段 + ITenantClock + 消费点接线 + 租户管理页时区。
- **I-F i18n + QA**：五语 seed（估 ~30 键）+ harness + DoD。

依赖：I-A → {I-B ‖ I-C ‖ I-D} → I-E（时区消费 I-A 的 workdays）→ I-F。与子流程 spec 的接缝：E-WF-027 的来源集合含 subFlow（先落地者写全集合常量，后落地者只加测试）。

---

*生成于 2026-07-05。执行遵守铁律：worker 照抄 TenantScopeRunner 口径；引擎内写路径三律；E 波紧跟 D 波；零跨模块污染。*
