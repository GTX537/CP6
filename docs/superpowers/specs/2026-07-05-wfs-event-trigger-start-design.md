# WFS 事件触发 start · timer-start / IntegrationEvent / message API 设计

> 生成于 2026-07-05（brainstorming 已确认）。上游：内核 spec §11 P3「事件触发 start」；umbrella §9「執行JOB 定时事件」剩余部分（timer **节点**已在 ServiceTask 落地，本增量做「定时/事件**发起**流程」）。
> 落码位：`CP6.Entity/DomainModels/Wf`、`CP6.Core/Services/Wf`、`CP6.WebApi/BackgroundServices`、`cp6.web/src/views/oa/`（流程管理页）。

---

## §0 背景、范围与决策

### §0.1 背景

流程目前只能由用户在信箱手工发起（`StartAsync`/`StartDraftAsync`）。三类自动化场景无解：定时例行审批（月末盘点）、业务事件联动（库位发布→审批）、外部系统发起（第三方对接）。

### §0.2 范围（In / Out）

**In**：`Wf_FlowTrigger`/`Wf_TriggerFire` 两新表（一次迁移）；timer-start（cron）/ event（IntegrationEvent 联动）/ message（外部 REST）三触发器；`IFlowTriggerService` 单一发起出口；流程管理页「触发器」tab；权限点/错误码/五语 i18n/QA harness。

**Out（→ §9 YAGNI）**：真实业务事件源的逐点接入（机制+样例先行，业务接入按需求单独拉动）；触发器级流量控制；BPMN 边界事件（节点级 message/timer 中断）。

### §0.3 锁定决策（用户已拍板 2026-07-05）

| # | 决策 | 依据 |
|---|------|------|
| D1 | 三触发器**一期全做**（timer / event / message） | 用户选项确认 |
| D2 | 三入口收敛**单一出口** `IFlowTriggerService.FireAsync`，统一负责 Enabled 检查→幂等闸→StartAsync→写流水→更新水位 | 幂等/审计集中一处 |
| D3 | timer 周期 = **cron 字符串（NCrontab 库）+ UI 常用预设**；否决自造简化 DSL（月末/工作日很快撞墙） | 设计呈现已确认 |
| D4 | event 触发**加入 BridgeHook 家族**：新 `IWfTriggerBridgeHook`（`BridgeHookBase` 子类），天然获得 IntegrationEvent outbox 重试；否决另造订阅机制 | 对齐既有 `IMesBridgeHook` 等家族模式（`BridgeHookBase.cs:59-78` outbox 写入） |
| D5 | 触发器配置挂**流程级**（管理页 tab），不进设计器 start 节点；否决进 schema——密钥会被卷进 schema 版本化 | 设计呈现已确认 |
| D6 | 自动发起的名义发起人 = 触发器配置的 **StarterUserId（必填）**——审计与审批人解析 starter.* 命名空间都依赖它 | ApproverResolver.cs:24-25 starter 命名空间既有依赖 |
| D7 | `Wf_TriggerFire` 流水三种触发器**统一写**：既是审计台账，也是幂等闸（复合唯一索引 TenantId+TriggerId+IdempotencyKey；键非空必填，无需 filtered——ServiceJob 先例用 filtered 是因其键可空，此处不是） | 幂等/审计集中一处 |

---

## §1 现状锚点（逆向真实，不编造）

- **发起入口**：`FlowEngine` `StartAsync`（信箱手工发起唯一路径）；发起人贯穿 `inst.StarterId`。
- **异步底座先例**：`WfServiceJobScanWorker` + `IWfServiceJobService.ScanOnceAsync`（lease 抢占 + 状态闸 + AttemptCount + 指数退避，`WfServiceJobService.cs:21`）——TriggerWorker 照此模式复制。
- **IntegrationEvent 基建**：`BridgeHookBase.cs:59-78` 失败时写 `IntegrationEvents` outbox 行；`IntegrationEventRetryWorker.cs:69-86` 重放调 `IIntegrationEventDispatcher.DispatchAsync`；`IntegrationEventDispatcher.cs:18-74` 静态路由字典 `RouteKey(source|target|hook)`，`:111-115` 未知路由抛 DISPATCH-404。
- **邮件/通知**：与本增量无关（触发器不发通知，发起后走流程既有通知）。
- **错误码水位**：E-WF-019~021 已被内核 hardening spec 预留，本增量从 **E-WF-022** 起。
- **权限/菜单先例**：OA 模块菜单与 `Permission` 点已有家族（流程管理页存在，tab 追加）。

---

## §2 数据模型（一次迁移 `WfsFlowTrigger`）

### §2.1 `Wf_FlowTrigger`

```csharp
[Table("Wf_FlowTrigger")]
public class Wf_FlowTrigger : BaseTenantEntity
{
    public string FlowKey { get; set; } = "";          // 目标流程（对齐 StartAsync 口径）
    public int TriggerType { get; set; }               // WfTriggerType: Timer=0 / Event=1 / Message=2
    [Column(TypeName = "nvarchar(max)")]
    public string ConfigJson { get; set; } = "{}";     // 分型配置（§2.3）
    public bool Enabled { get; set; }
    public string? EventKey { get; set; }              // event 专用（提列可索引；ConfigJson 里不再重复存）
    public Guid StarterUserId { get; set; }            // 名义发起人（D6，必填）
    public DateTime? NextDueUtc { get; set; }          // timer 专用：下次到期（扫描键）
    public DateTime? LastFiredUtc { get; set; }
    public string? ApiKeyHash { get; set; }            // message 专用：SHA-256（明文只在创建响应显示一次）
    [Timestamp] public byte[]? RowVersion { get; set; }// 乐观并发（多实例 worker 抢占）
}
```

索引：`(TenantId, FlowKey)`；`(Enabled, TriggerType, NextDueUtc)` 扫描索引；`(TenantId, EventKey)`（event 匹配查询——EventKey 提成列正是为可索引，nvarchar(max) 的 ConfigJson 内 JSON 键无法索引）。

### §2.2 `Wf_TriggerFire`（触发流水 = 审计 + 幂等闸）

```csharp
[Table("Wf_TriggerFire")]
public class Wf_TriggerFire : BaseTenantEntity
{
    public Guid TriggerId { get; set; }
    public string IdempotencyKey { get; set; } = "";   // 复合唯一索引（TenantId,TriggerId,IdempotencyKey）＝幂等闸权威判据
    public DateTime FiredUtc { get; set; }
    public Guid? InstanceId { get; set; }              // 成功发起的流程实例；失败为 null
    public int Source { get; set; }                    // 同 WfTriggerType（冗余便查）
    public string? Error { get; set; }                 // 发起失败原因（结构化码+detail）
    public string? PayloadHash { get; set; }           // message/event 负载 SHA-256（审计，不存原文）
}
```

幂等键口径：timer = `"{TriggerId}:{NextDueUtc:O}"`（同一到期时刻只发一次）；event = `"{业务事件Id}:{TriggerId}"`——**业务事件 Id 必须在 hook 入口就确定**（调用方作为必填参数传入，并随 outbox 负载一起持久化供重放复用；不能用 outbox 行 Id——outbox 行只在失败路径才存在，成功路径无键可用，且一事件匹配多触发器的部分成功场景必须按触发器粒度去重）；message = 调用方 `Idempotency-Key` 头（缺省 400）。

### §2.3 ConfigJson 分型

- **timer**：`{ "cron": "0 0 25 * *", "varsJson": "{...}" }`——cron 5 段（NCrontab 标准），varsJson 为发起时的初始流程变量（可空）。
- **event**：`{ "varsMap": { "orderNo": "$.OutboundNo" } }`——eventKey 走实体列（§2.1，格式 `{SourceModule}|{HookName}`，如 `"WMS|OnShipmentConfirmedAsync"`）；varsMap 用点路径/JSONPath 精简子集把事件负载映射进流程变量（复用 `ServiceVarsHelper` 点路径口径，含其已记档限制）。
- **message**：`{ "varsSchema": ["orderNo", "amount"] }`——白名单字段（不在名单的负载键丢弃，防变量注入）。

---

## §3 执行架构

### §3.1 `IFlowTriggerService`（单一出口，D2）

```csharp
public interface IFlowTriggerService
{
    /// <summary>统一发起：Enabled 检查 → 幂等闸（TriggerFire 唯一键，撞键幂等返回既有 InstanceId 不报错）
    /// → 变量构造 → FlowEngine.StartAsync(trigger.StarterUserId) → 写流水 → 更新 NextDue/LastFired。</summary>
    Task<TriggerFireResult> FireAsync(Wf_FlowTrigger trigger, string? varsJson,
                                      int source, string idempotencyKey, CancellationToken ct);
    /// <summary>timer 扫描一轮（worker 复用；lease 语义 = RowVersion 乐观并发 + NextDueUtc 前移即抢占）。</summary>
    Task<int> ScanTimersOnceAsync(CancellationToken ct);
}
```

幂等闸实现：先 INSERT TriggerFire（撞 unique index → 捕获后查既有行返回其 InstanceId，**幂等成功**不是错误）；再 StartAsync；失败回填 Error（流水行保留，供排障）。StartAsync 与流水在同一 SaveChanges 事务（对齐引擎原子接缝铁律）。

### §3.2 timer：`WfTriggerWorker`（BackgroundService）——占坑两段式，既不双发也不丢发

- 照抄 `WfServiceJobScanWorker` 骨架：周期扫 `Enabled && TriggerType==Timer && NextDueUtc <= now`。
- **第一段（抢占+占坑，单事务提交）**：读行→算新 NextDueUtc（NCrontab `GetNextOccurrence`）→ RowVersion 乐观写回 **同时 INSERT 占坑 TriggerFire 行**（幂等键 = `TriggerId:旧NextDueUtc`，InstanceId=null，Error=null）。写回成功者获得发火权（多实例安全）。
- **第二段（完成）**：FireAsync 对占坑行补跑 StartAsync 并回填 InstanceId（成功）或 Error（失败）。
- **崩溃恢复**：两段之间崩溃 → NextDueUtc 已推进（不双发），但占坑行留存 → worker 每轮**另扫未完成流水行**（InstanceId==null && Error==null && FiredUtc 早于宽限期）补跑第二段。两半各自幂等：占坑靠唯一索引，完成靠「已回填则跳过」状态闸。*仅推进 NextDueUtc 不占坑的单段方案有丢发窗口（推进后崩溃 → 本次到期永久丢失，月末盘点静默跳过一个月）——被否决。*
- **misfire 口径**：宕机跨过多个到期点 → 只补发**最近一次**（NextDueUtc 直接推到未来下一个），不追历史——例行审批场景积压补发无意义且危险。（占坑恢复补的是"已抢占未完成"的那一次，与 misfire 跳过历史到期点不冲突。）

### §3.3 event：`WfTriggerBridgeHook`（BridgeHook 家族新成员，D4）

- `IWfTriggerBridgeHook.OnEventAsync(string eventKey, string eventId, string payloadJson, string? userName)`：**eventId 必填**（业务事件标识，幂等键素材，§2.2；随 outbox 负载持久化，重放时原样复用）。查 `Enabled && TriggerType==Event && EventKey==eventKey` 的触发器（可多条，逐条 FireAsync，幂等键 = `"{eventId}:{TriggerId}"`——部分成功后 hook 整体进 outbox 重放时，已发的触发器撞键幂等跳过，未发的补发）。
- 业务模块触发 = 调用该 hook（与调 `IMesBridgeHook` 等同款写法）；失败自动进 outbox。
- `IntegrationEventDispatcher` 加**目标泛化路由**：`target=="WF" && hook=="OnEventAsync"` 时不看 source 直接路由（`DispatchAsync` 加 fallback 分支，`:111` 前插入；DISPATCH-404 语义对其余路由不变）。
- 一期交付**机制 + Echo 样例事件源**（QA harness 用，对齐 ServiceTask EchoConnector 先例）；真实业务接入点（如 Space 发布、WMS 出库）按需求单独拉动，每处一行调用。

### §3.4 message：REST 端点

- `POST /api/oa/flow-triggers/{id}/fire`：`[AllowAnonymous]` + 自定义过滤器校验 `X-Api-Key`（SHA-256 对比 `ApiKeyHash`，常量时间比较）+ `Idempotency-Key` 头必填。
- 负载 = JSON body，按 `varsSchema` 白名单过滤后作为 varsJson。
- 响应：201 `{instanceId}` / 200 幂等重放返回既有 `{instanceId}` / 401 key 无效 / 404 触发器不存在或未启用（存在但停用与不存在不区分）/ 400 缺 Idempotency-Key 或负载超限（上限 64KB）。401 与 404 的区分理论上可探测触发器存在性，但 id 是 GUID、key 是 32 字节随机，枚举不可行——保留区分便于对接排障，不以"防枚举"为设计目标。
- key 生成：创建/重置时服务端生成 32 字节随机，明文**只在响应显示一次**；库中仅存哈希。

---

## §4 管理 UI（流程管理页「触发器」tab）

- 列表：类型/目标流程/启停/NextDue/LastFired/操作。
- 新建/编辑对话框按类型分型表单：timer=cron 输入+常用预设下拉（每日/每周一/每月 25 日/每月末）+ 下次触发时间预览；event=eventKey + varsMap 键值编辑；message=varsSchema 白名单 + key 显示（仅创建时）/重置 key 按钮。
- 手动试发按钮（走 FireAsync，幂等键=手动 GUID，权限同编辑）。
- 流水抽屉：该触发器最近 N 条 TriggerFire（时间/结果/实例链接/错误）。
- 全部走 Design System v1.0 组件（CpTag 等），零硬编码色。

---

## §5 校验与错误码（续 E-WF 序列，019~021 已被 hardening 占用）

| 码 | 场景 | 层 |
|---|---|---|
| **E-WF-022** | 触发器配置无效：cron 解析失败 / eventKey 格式错 / varsMap 点路径非法 / StarterUserId 不存在或停用 | 保存时 + FireAsync 运行时双检（发起人可能在保存后被停用） |
| **E-WF-023** | 目标流程不可发起：FlowKey 无 enabled 流程定义 / 流程被停用 | 保存时 + FireAsync 运行时双检 |
| **E-WF-024** | 发起失败（运行时）：StartAsync 异常包装，写入 TriggerFire.Error | FireAsync |

message 端点的 401/404/400 走 HTTP 语义不占 E-WF 码。

---

## §6 安全 / 多租户

- TenantId 贯穿两表 + 全部查询；worker 扫描跨租户逐行处理（租户上下文按行切换，对齐 ServiceJob worker 口径——plan 时核实其现状写法照抄）。
- message 端点：AllowAnonymous 但 key 绑定单触发器单租户；负载白名单防变量注入；64KB 上限防滥用；**不做全局限流**（YAGNI，反代层职责）。
- 权限点：`OA.FlowTrigger.View/Edit`（管理页）；手动试发归 Edit。
- 审计：TriggerFire 全量流水；触发器增改走既有字段审计基建。

---

## §7 向后兼容

- 纯增量：新表/新 worker/新端点/dispatcher 一个 fallback 分支，既有路由与引擎行为零改动。
- NCrontab 为新依赖（MIT，单包无传递依赖）——plan 阶段核实版本并过依赖审查。

---

## §8 测试策略

- **FireAsync**：幂等撞键返回既有实例、Enabled=false 拒绝、StarterUserId 停用 E-WF-022、StartAsync 失败流水回填。
- **timer**：到期扫描发起、NextDueUtc 前移抢占（并发两 worker 模拟只发一次）、**占坑两段式崩溃恢复（第一段提交后中断 → 补跑扫描回填 InstanceId，不丢发不双发）**、misfire 只补最近一次、cron 边界（月末/闰年）。
- **event**：eventKey 匹配多触发器逐发、varsMap 映射、outbox 失败重试路径（dispatcher fallback 路由）、**部分成功重放去重（3 触发器发 1 个后失败 → 重放仅补 2 个）**、未匹配零动作。
- **message**：key 常量时间校验、幂等头缺失 400、白名单过滤、404 不泄露存在性。
- **QA harness**：gstack 剧本（管理页建三型触发器、cron 预览、手动试发、流水查看、key 重置一次性显示）。
- 基线：后端 1509 → +N 全绿；前端 320 → +N 全绿；EF `has-pending-model-changes` clean（一次迁移）。

---

## §9 YAGNI / 留后

- 真实业务事件源逐点接入（每处一行，按需求拉动）。
- BPMN 边界事件（节点级 message/timer 中断在途实例）。
- 触发器级限流/配额、调用方 IP 白名单。
- timer 工作日/日历口径（与 ServiceTask timer 节点同一留后条目）。
- per-tenant 时区：cron 一期按 app 默认时区解释，字段名不带 Utc 的口径混淆风险在 UI 文案标注时区。

---

## §10 分期 / 任务波次（供 writing-plans 细化）

- **T-A 数据模型 + 服务**：两表迁移 + WfTriggerType 常量 + `IFlowTriggerService.FireAsync`（幂等闸+流水+StartAsync 接缝）。
- **T-B timer**：NCrontab 引入 + ScanTimersOnceAsync + WfTriggerWorker + misfire 口径。
- **T-C event**：IWfTriggerBridgeHook + dispatcher fallback 路由 + varsMap + Echo 样例源。
- **T-D message**：端点 + key 基建（生成/哈希/重置）+ 白名单。
- **T-E 管理 UI**：tab + 分型表单 + 流水抽屉 + 手动试发。
- **T-F 校验 + i18n + QA**：E-WF-022~024 + 权限点/菜单种子 + 五语 seed + gstack harness + DoD。

依赖：T-A → {T-B ‖ T-C ‖ T-D} → T-E → T-F。

---

*生成于 2026-07-05。执行遵守铁律：executor/引擎内写路径三律；E 波紧跟 D 波；零跨模块污染（dispatcher fallback 是唯一 Integration 触点，一个分支）。*
