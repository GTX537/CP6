# WFS 引擎深化 · 服务任务节点(Service Task)设计

> 版本 v1.1 · 2026-06-29(v1.0 → v1.1 = 用户 review 护栏强化,见 §0.4)
> 范围:WFS 引擎深化第三增量(前两增量「串簽」「审批人解析高级策略」已收官上 main `0541bd9`)。
> 隔离 worktree:`D:/CP6-wfs-approver` @ `feat/wfs-approver-resolve`(off main `0541bd9`)。
> 落码纪律:subagent-driven TDD,每 Task 全新 general-purpose[sonnet] → 控制器级 diff 复核(零 Space)→ 本地 commit 不 push;零改引擎执行态硬闸 `dotnet test --filter Wf`;gstack QA(隔离库 `CP6DB_OA`)。

---

## §0 背景、范围与决策

### §0.1 背景

CP6 的 WFS 工作流引擎(P1 token 内核)目前节点类型只有 `start / approval / end / parallelSplit / parallelJoin` —— 全是「人工审批 / 网关」语义,节点本身不会**跑自动化**。Delta 风格的 BPM 引擎里,流程节点可以执行系统动作:数据回写、调用 WebAPI、定时/等待、调子流程。这是引擎从「纯人工审批」到「真正 BPM」的最大一步跃迁。

内核 spec(`2026-06-26-wfs-runtime-kernel-design.md` §11)早已给出 roadmap:

> | 服务任务 / 自动节点(`IServiceTaskHandler`) | P2 | 泛化 `IApprovalCallback`;加 `serviceTask` handler |
> | 定时 / 事件触发(timer-start、message、IntegrationEvent 边界事件) | P3 | 复用 `WfTimeoutService` + BridgeHook / IntegrationEvent |

本设计落地该 roadmap 的首切。

### §0.2 范围(In / Out)

**In(本增量首切)**:
- **数据回写(dataWriteback)**:节点到达时把数据回写到 CP6 各模块(复用既有跨模块服务,包成命名 executor)。
- **执行 WebAPI(webApi)**:经**注册式连接器**调用内/外部 HTTP API。
- **JOB 定时(timer)**:**两种语义都要**——① 纯等待(等一段时长 / 等到某日期再推进);② 到点执行动作(到期时执行回写 / WebAPI 再推进)。

**Out(留后,§13)**:子流程(P4)、per-tenant 连接器配置表、工作日/日历口径延时、错误边来源放宽到非服务任务节点、事件触发 start、终态 job 清理后台任务、per-node HTTP method/timeout/response-map 覆盖。

### §0.3 锁定决策(D1~D11)

| # | 决策 | 取舍理由 |
|---|---|---|
| **D1** | 首切三类 = dataWriteback + webApi + timer;子流程留后 | 一个完整闭环又不过大 |
| **D2** | timer = 纯等待 **和** 到点执行动作 两者都要;timer 是「调度包装器」,可选挂一个动作 | 统一为「DueAt + 可选动作」一套机制 |
| **D3** | 失败语义 = 重试 N 次 → 有错误边走错误边,否则挂起(Suspend) | 对齐 Delta「30 呼叫ERP重試中」+「1001 表單退回」;复用引擎既有 Suspend |
| **D4** | webApi 绑定 = 注册式连接器(服务端预置 baseURL/认证/密钥/method/response-map,设计器仅选连接器+路径+参数模板) | 多租户 SaaS 安全:无 SSRF、密钥不进流程定义 |
| **D5** | 架构 = 方案 A:**单一 `serviceTask` 节点类型** + 单一 `IServiceTaskExecutor` 注册表 + 共享异步底座 | 重试/错误边/停泊-恢复 plumbing 集中一处;executor 注册表落地 §11 泛化;异步底座未来可扩子流程 |
| **D6** | sync = 内联乐观一击(快/原子),失败转异步重试;async/timer = 直接停泊入队 | 本地回写走快路径且与流程态同事务原子;失败自动降级异步重试不阻塞请求 |
| **D7** | 异步底座 = 轮询扫描 `WfServiceJobScanWorker`(复用 `WfTimeoutScanWorker` 模式),非 Kafka/outbox | 租户隔离、at-least-once、自包含;Kafka dev flaky/可选 |
| **D8** | 错误边 = `FlowEdge.IsError` 标记;普通 `AdvanceToken` 跳过它,仅服务任务重试耗尽走 | 成功路径绝不误走错误边(关键不变量) |
| **D9** | 连接器密钥**绝不进 SchemaJson**;首切 app 级配置注册 | 流程定义可导出/克隆/明文存,密钥泄露不可接受 |
| **D10** | `IServiceTaskExecutor` 契约要求**幂等**(async at-least-once);抢占 lease + RowVersion + 唯一约束 + 恢复幂等 多层降重 | 异步执行与恢复跨事务,崩溃可能重投(见 §0.4 P0-2/3/4/5) |
| **D11** | `FlowNode`/`FlowEdge` 是 SchemaJson POCO 加字段 = **零迁移**;唯一新迁移 = `Wf_ServiceJob` 表 | 与审批人增量同款手法;新表纯加法零回填 |

### §0.4 v1.0 → v1.1 review 护栏强化(必读)

用户 review 抓出「异步服务任务必踩坑」,本版全部纳入:

| # | 护栏 | 落点 |
|---|---|---|
| **P0-1** | `RetryCount/MaxRetries` 改 `AttemptCount/MaxAttempts`(消 off-by-one);节点 `ServiceMaxRetries`→job `MaxAttempts=retries+1` | §2.1 / §2.3 / §3.4 / §4.2 |
| **P0-2** | `ResumeServiceTokenAsync` **幂等**:token 非 Active 或 `NodeId!=job.NodeId` 则不二次推进 | §4.4 |
| **P0-3** | 同 token 同 node **只能有一个活跃 job**:filtered unique index + EnqueueServiceJob 先查 | §2.3 / §3.4 |
| **P0-4** | reaper 从 `ModifyDate` 改 **lease 模型**(`LockedBy/LockedAtUtc/LockExpiresAtUtc`) | §2.3 / §4.2 / §4.6 |
| **P0-5** | 实例撤回/驳回:Pending→Cancelled;**Running job 恢复前必检查实例/token 状态**,不满足则 Cancelled 不恢复 | §4.2 / §4.8 |
| **P0-6** | 时间全 **UTC 存储**(`*Utc` 字段);`untilDate` 按租户时区(无则 app 默认)解释转 UTC | §2.3 / §4.7 |
| **P1-1** | `ActionRefJson` 固定结构(serviceKind/actionKind/timer/connector/path/params) | §3.5 |
| **P1-2** | `ServiceTaskContext` 补 `JobId/AttemptNo/ActorId/NowUtc`;连接器发 `Idempotency-Key: wf-service-job-{jobId}` | §3.1 / §3.3 |
| **P1-3** | `OutputVars` 合并规则写死(top-level object / 保留前缀 / 大小写 / 仅 JSON 值 / history) | §3.6 |
| **P1-4** | 错误边路由写标准错误变量 `wf.serviceError{...}` | §4.3 |
| **P1-5** | WebAPI:连接器自己决定 method/headers/timeout/response-map;流程定义仅传 path+params | §3.3 / §13 |
| **P1-6** | 服务目录按 executor `Kind`/`VisibleInDesigner` 过滤,webApi executor 不混进回写动作 | §3.1 / §5.5 |
| **P2-1** | sync 原子前提显式化(同 scoped DbContext / 不提前 SaveChanges / 不外部 HTTP / 不开独立事务) | §4.5 |
| **P2-2** | `ServiceParamsJson` 表达式语法明确(`$.var` / `$wf.ctx` / 字面量) | §3.6 |
| **P2-3** | 校验:非 end 的 serviceTask 须 ≥1 非错误出边 | §6.1 |
| **P2-4** | 终态 job 加 `CompletedAtUtc` + 保留策略(清理任务留后) | §2.3 / §13 |

---

## §1 现状锚点(逆向真实,不编造)

| 组件 | 位置 | 关键事实 |
|---|---|---|
| `INodeHandler` | `CP6.Core/Services/Wf/INodeHandler.cs:8-12` | `string Type {get;}` + `Task OnEnterAsync(NodeContext ctx)` |
| `NodeContext` | `INodeHandler.cs:15-22` | `Inst / Schema / Node / Token / Engine` |
| 5 个 handler | `CP6.Core/Services/Wf/NodeHandlers/*.cs` | start/approval/end/parallelSplit/parallelJoin |
| handler 分发 | `FlowEngine.cs:265-272` | 按 `node.Type.ToLowerInvariant()` 查 `_handlers` 字典;未知类型抛 |
| handler DI | `Program.cs:108-112` | `AddScoped<INodeHandler, XxxNodeHandler>()` ×5 |
| token 原语 | `FlowEngine.Tokens.cs` | `SpawnToken`(15-25)/ `AdvanceToken`(69-84)/ `ConsumeToken`(28-32)/ `FinishIfDrained`(49-54)/ `CancelAllActiveTokens`(37-46) |
| `AdvanceToken` | `FlowEngine.Tokens.cs:69-84` | 沿首条 `ExpressionEvaluator.Evaluate(edge.Condition, vars)` 为真的出边推进;无后继→Consume+FinishIfDrained |
| `Suspend` | `FlowEngine.cs:293-297` | `inst.Status=Suspended` + 历史 |
| 乐观并发重试 | `FlowEngine.cs:103-114` | `ActAsync` 包 `ActOnceAsync`,`DbUpdateConcurrencyException`→全 reload→重试(0/1/2) |
| `FlowInstanceStatus` | `WfStatus.cs:4-12` | Running=0/Approved=1/Rejected=2/Withdrawn=3/Suspended=4/Draft=5 |
| `FlowTokenStatus` | `WfStatus.cs` | Active/Consumed/Cancelled |
| `IApprovalCallback` | `IApprovalCallback.cs:13-23` | `BizType` + `OnApprovedAsync` + `OnRejectedAsync`;注释「应幂等」 |
| `ApprovalDispatcher` | `ApprovalDispatcher.cs:23-42` | 按 `inst.BizType` 找回调,**`SaveChanges` 前**调用(原子) |
| `IFinBridgeHook` | `IFinBridgeHook.cs:10-20` | 跨模块回写既有样板 |
| `FlowNode`(POCO) | `FlowSchema.cs:16-69` | Type 默认 "approval";已有审批/会签/超时/抄送/坐标/串簽/高级审批人字段 |
| `FlowEdge`(POCO) | `FlowSchema.cs:71-81` | From/To/Condition/CcUsers |
| `FlowSchemaValidator` | `FlowSchemaValidator.cs` | 纯静态校验,统一 E-WF-010 |
| `NODE_PALETTE`(前端) | `designerModel.ts:49-55` | 5 类型调色板 |
| `IWfTimeoutService` | `WfTimeoutService.cs:9-12` | `Task<int> ScanOnceAsync(DateTime now, CancellationToken ct)`(注入时间=可测) |
| `WfTimeoutScanWorker` | `BackgroundServices/WfTimeoutScanWorker.cs:10-49` | `BackgroundService`,每 1min,`TenantScopeRunner.ForEachTenantAsync`,`SystemActor=Guid.Empty` |
| Wf 测试面 | `CP6.Tests/Wf/*.cs` | `--filter Wf` 硬闸,须字节等价 |

> 注:既有引擎多处用 `DateTime.Now`(本地时)。`Wf_ServiceJob` 本表**内部一律 UTC**(字段带 `Utc`,worker 传 `DateTime.UtcNow`,自洽);不改既有 `WfTimeoutService` 的本地时用法(超出本增量范围)。

---

## §2 数据模型

### §2.1 `FlowNode` 加服务任务配置字段(POCO,零迁移,全可空向后兼容)

```csharp
public string? ServiceKind { get; set; }            // "dataWriteback" | "webApi" | "timer";null=非服务任务
public string? ServiceMode { get; set; }            // "sync" | "async";dataWriteback 默认 sync;webApi 默认 async;timer 恒 async

// 动作绑定:dataWriteback 用 ActionName;webApi 用 Connector+Path+Params;timer 可选挂动作(复用这些字段)
public string? ServiceActionName { get; set; }       // dataWriteback / timer 动作:注册执行器键
public string? ServiceConnectorName { get; set; }    // webApi / timer 动作:注册连接器键
public string? ServicePath { get; set; }             // webApi:相对 baseURL 的路径模板(可含 $.var)
public string? ServiceParamsJson { get; set; }       // 参数模板:JSON,键→表达式(§3.6 语法)

// timer 专属
public string? ServiceDelayMode { get; set; }        // "duration" | "untilDate" | "untilExpr"
public string? ServiceDelayValue { get; set; }       // "3d"/"PT2H" | "2026-07-01" | 表达式

// 重试策略(设计器口径:重试次数;映射到 job)
public int? ServiceMaxRetries { get; set; }          // 默认 3(= 首次后再重试 3 次);job.MaxAttempts = retries + 1
public int? ServiceRetryBackoffSec { get; set; }     // 默认 30,指数退避基数
```

> **method/headers/timeout/response-mapping 不在节点上**(D4/P1-5):由连接器自己决定;流程定义只传 path+params。per-node 覆盖 = 留后(§13)。
> **timer 语义统一**:timer 必有 `ServiceDelay*`(决定 DueAt);若同时有 ActionName/ConnectorName 则到点执行该动作再推进,否则纯等待直接推进。timer 不需要独立 executor。

### §2.2 `FlowEdge` 加错误边标记(POCO,零迁移)

```csharp
public bool? IsError { get; set; }   // true=失败出边;普通 AdvanceToken 跳过它,仅服务任务重试耗尽时走
```

### §2.3 新表 `Wf_ServiceJob`(异步停泊任务台账,唯一新迁移 `WfsServiceTask`)

EF 实体(`CP6.Entity/DomainModels/Wf/Wf_ServiceJob.cs`,继承 `BaseTenantEntity`):

```csharp
public Guid Id { get; set; }                   // PK
// TenantId 来自 BaseTenantEntity
public Guid InstanceId { get; set; }           // FK→Wf_FlowInstance
public Guid TokenId { get; set; }              // 要恢复的停泊 token
public string NodeId { get; set; }             // 服务任务节点 Id
public string Kind { get; set; }               // dataWriteback/webApi/timer
public string? ActionRefJson { get; set; }     // 固化动作绑定快照(§3.5),防流程定义漂移

public DateTime DueAtUtc { get; set; }         // async 动作=入队时刻(UTC);timer=未来到期时刻(UTC)
public int Status { get; set; }                // ServiceJobStatus(§2.4)

// 尝试计数(P0-1):统一 sync 失败降级 / async 首执 / timer 到期执行
public int AttemptCount { get; set; }          // 已实际执行次数
public int MaxAttempts { get; set; }           // 最大总尝试次数(= node.ServiceMaxRetries + 1,默认 4)
public DateTime NextAttemptAtUtc { get; set; } // 退避后下次可执行时刻(扫描门控,UTC)

// 租约(P0-4):替代 ModifyDate 式 reaper
public string? LockedBy { get; set; }          // workerId
public DateTime? LockedAtUtc { get; set; }
public DateTime? LockExpiresAtUtc { get; set; }

public string? LastError { get; set; }         // 最后失败原因(截断 ≤1000 字)
public DateTime? CompletedAtUtc { get; set; }  // 终态完成时刻(P2-4,排查/保留用)
public DateTime CreateDate { get; set; }
public DateTime? ModifyDate { get; set; }
[Timestamp] public byte[]? RowVersion { get; set; }   // 乐观并发,防多 worker 抢同一 job
```

索引(`CP6Context.OnModelCreating`):
- `IX_Wf_ServiceJob_Scan = (TenantId, Status, NextAttemptAtUtc)` —— 扫描器查待执行。
- `IX_Wf_ServiceJob_Instance = (TenantId, InstanceId)` —— 终止清理 / 实例维度查询。
- `UX_Wf_ServiceJob_LiveToken = (TenantId, TokenId, NodeId)` **filtered unique** `WHERE Status IN (0,1)`(P0-3)——同 token 同 node 只能一个活跃 job。
  ```csharp
  builder.HasIndex(x => new { x.TenantId, x.TokenId, x.NodeId })
         .IsUnique().HasFilter("[Status] IN (0, 1)");
  ```
  > SQLite 测试 `HasFilter` 语法不通用 → 仍靠 `EnqueueServiceJob` 代码级先查兜底(§3.4)。

DbSet:`public DbSet<Wf_ServiceJob> Wf_ServiceJobs { get; set; }`。

### §2.4 枚举/常量(`WfStatus.cs`,沿用 `static class + const int`)

```csharp
public static class ServiceJobStatus {
    public const int Pending=0; public const int Running=1;
    public const int Succeeded=2; public const int Failed=3; public const int Cancelled=4;
}
public static class ServiceKind {
    public const string DataWriteback="dataWriteback"; public const string WebApi="webApi"; public const string Timer="timer";
}
public static class ServiceMode { public const string Sync="sync"; public const string Async="async"; }
```

---

## §3 执行架构

### §3.1 `IServiceTaskExecutor` 契约(§11「泛化 `IApprovalCallback`」落地)

```csharp
// CP6.Core/Services/Wf/IServiceTaskExecutor.cs
public interface IServiceTaskExecutor
{
    string Key { get; }                  // webApi→"webApi";dataWriteback→动作名
    string Kind { get; }                 // "dataWriteback" | "webApi" | "internal"(P1-6:服务目录据此过滤)
    bool VisibleInDesigner { get; }      // dataWriteback 动作=true;WebApiExecutor=false(不当回写动作暴露)
    string DisplayName { get; }          // 设计器目录显示(可 i18n 键)
    /// <summary>执行。实现**必须幂等**(async at-least-once,崩溃可能重投;用 ctx.JobId 作幂等键)。</summary>
    Task<ServiceTaskResult> ExecuteAsync(ServiceTaskContext ctx);
}

public sealed class ServiceTaskContext
{
    public required Guid InstanceId { get; init; }
    public required Guid TokenId { get; init; }
    public required string NodeId { get; init; }
    public required Guid StarterId { get; init; }
    public required Guid JobId { get; init; }       // P1-2:幂等键来源(async);sync 内联用 Guid.Empty
    public required int AttemptNo { get; init; }     // 第几次执行(1-based)
    public required Guid ActorId { get; init; }      // SystemActor(async/timer) 或发起人(sync)
    public required DateTime NowUtc { get; init; }
    public string? VarsJson { get; init; }           // 表单数据,供参数模板求值
    public string? ActionRefJson { get; init; }      // 固化动作绑定快照(§3.5)
    // executor 通过注入服务(DB/HttpClient)干活,不直接持有 FlowEngine
}

public sealed class ServiceTaskResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public Dictionary<string, object?>? OutputVars { get; init; }   // §3.6 合并规则
    public static ServiceTaskResult Ok(Dictionary<string,object?>? outputVars = null) => new(){Success=true, OutputVars=outputVars};
    public static ServiceTaskResult Fail(string error) => new(){Success=false, Error=error};
}
```

### §3.2 单一注册表 + 按键解析

引擎/服务持有 `IEnumerable<IServiceTaskExecutor>` → `ToDictionary(e => e.Key, OrdinalIgnoreCase)`。解析键(`ResolveExecutorKey(actionRef)`):

| serviceKind / actionKind | 解析键 | executor |
|---|---|---|
| webApi | `"webApi"` | 单一 `WebApiExecutor`(处理所有连接器) |
| dataWriteback | `actionName` | 命名回写动作 executor(每个一实现) |
| timer 无动作(actionKind="none") | —(null) | 无 executor,纯推进 |
| timer 有动作 | 按 actionKind 同上 | 复用 webApi / dataWriteback executor |

### §3.3 连接器注册(WebAPI 安全底座)

```csharp
// CP6.Core/Services/Wf/IWfConnector.cs
public interface IWfConnector
{
    string Name { get; }          // 设计器下拉用
    string DisplayName { get; }
    /// <summary>按路径+参数模板调用。连接器自己决定 baseURL/认证/HTTP method/headers/timeout/response→OutputVars 映射(D4/P1-5)。
    /// 实现应把 ctx.JobId 作幂等键发出:Idempotency-Key: wf-service-job-{JobId}。</summary>
    Task<ServiceTaskResult> CallAsync(string pathTemplate, string? paramsJson, ServiceTaskContext ctx);
}
```

- DI 注册、按 `Name` 索引;`WebApiExecutor.ExecuteAsync` 从 `ActionRefJson` 取 connector/path/params → 找 `IWfConnector` → `CallAsync`。
- **密钥服务端配置**(appsettings / secret store),**绝不进 SchemaJson**(D9)。
- 首切连接器 **app 级 DI/配置注册**;per-tenant 连接器 = 留后(§13)。

### §3.4 `ServiceTaskNodeHandler`(`Type="serviceTask"`,第 6 个 handler)

DI 注册(`Program.cs` 追加)+ `DefaultHandlers()`(`FlowEngine.cs:34` 追加)。

```
OnEnterAsync(ctx):
  node=ctx.Node; inst=ctx.Inst; token=ctx.Token
  kind=node.ServiceKind
  mode = (kind==timer) ? async
         : (node.ServiceMode ?? (kind==webApi ? async : sync))   // dataWriteback 默认 sync;webApi 默认 async
  maxAttempts = (node.ServiceMaxRetries ?? 3) + 1

  if mode == sync:                                  // 内联乐观一击(快/原子),失败降级异步
     r = await Resolve(node).ExecuteAsync(BuildCtx(node, token, jobId:Guid.Empty, attemptNo:1, actor:inst.StarterId))
     if r.Success:
        MergeOutputVars(inst, r.OutputVars)         // §3.6
        await ctx.Engine.AdvanceToken(inst, schema, token)   // 原子(SaveChanges 前),沿成功边(跳过 IsError)
     else:
        EnqueueServiceJob(node, token, dueAtUtc:utcnow, attemptCount:1, maxAttempts,
                          nextAttemptAtUtc: utcnow + backoff)   // sync 那一击算 attempt 1;token 停泊不 advance
  else (async / timer):
     dueAtUtc = (kind==timer) ? ComputeDueUtc(node, inst.VarsJson) : utcnow
     EnqueueServiceJob(node, token, dueAtUtc, attemptCount:0, maxAttempts,
                       nextAttemptAtUtc: dueAtUtc, Status:Pending)   // token 停泊(像 ApprovalNodeHandler 等 ActAsync)
```

- **`EnqueueServiceJob` 防重(P0-3)**:先查同 `(TenantId,TokenId,NodeId)` 是否已有 Pending/Running job,有则**不重复创建**(配合 filtered unique index)。
- `ActionRefJson` = `SnapshotActionRef(node)`(§3.5 结构),固化防漂移。
- handler 不写 SaveChanges(沿用引擎在 Submit/ActOnce 边界统一保存)。

### §3.5 `ActionRefJson` 固定结构(P1-1)

```jsonc
// timer 到点调 webApi
{ "serviceKind":"timer", "actionKind":"webApi",
  "timer":{ "delayMode":"duration", "delayValue":"PT2H" },
  "connectorName":"erpEcho", "path":"/api/order/{orderId}", "paramsJson":"{...}" }
// 纯等待 timer
{ "serviceKind":"timer", "actionKind":"none",
  "timer":{ "delayMode":"untilDate", "delayValue":"2026-07-01" } }
// 直接 dataWriteback / webApi(无 timer)
{ "serviceKind":"dataWriteback", "actionKind":"dataWriteback", "actionName":"poConfirm", "paramsJson":"{...}" }
{ "serviceKind":"webApi", "actionKind":"webApi", "connectorName":"erpEcho", "path":"/x", "paramsJson":"{...}" }
```
> worker 解析 executor 只看 `actionKind`(none/dataWriteback/webApi),不靠猜 `Kind`。

### §3.6 参数模板语法 + OutputVars 合并规则(P2-2 / P1-3)

**`ServiceParamsJson` / `ServicePath` 模板语法**(前后端共用一套,务必一致):
```jsonc
{
  "orderId":   "$.orderId",       // $. 前缀 = 取 inst.VarsJson 顶层/点路径
  "lineNo":    "$.detail.lineNo",
  "approvedBy":"$wf.actorId",     // $wf. 前缀 = 引擎上下文(actorId/jobId/instanceId/nowUtc)
  "channel":   "固定值"            // 其余 = 字面量
}
```
- 路径解析:顶层 + 点路径(JSONPath-lite,不引入完整 JSONPath 依赖);缺失键 → null。
- `path` 模板里 `{orderId}` 占位等价 `$.orderId`(便于 URL 写法)。

**`OutputVars` 合并规则**(executor 返回值并回 `inst.VarsJson`):
- 仅允许合并 **top-level JSON object**。
- **禁止覆盖保留前缀** `wf.*` / `sys.*` / `_internal.*`(违者跳过+记 warning)。
- key **大小写敏感**(与 VarsJson 一致)。
- value 仅允许 JSON primitive / object / array(不接受任意 .NET 对象直接序列化)。
- 合并前**记 history**:哪些 key 新增/覆盖(`AddHistory` 一条 `serviceVars`)。

---

## §4 异步底座与错误处理

### §4.1 `IWfServiceJobService` + `WfServiceJobScanWorker`

```csharp
public interface IWfServiceJobService
{
    /// <summary>扫一遍到期待执行 job(含 reaper),逐条执行+恢复/重试/路由。返回处理条数。nowUtc 注入可测。</summary>
    Task<int> ScanOnceAsync(DateTime nowUtc, string workerId, CancellationToken ct = default);
}
```
Worker(`CP6.WebApi/BackgroundServices/WfServiceJobScanWorker.cs`,克隆 `WfTimeoutScanWorker`):`BackgroundService`,~20s 间隔,`TenantScopeRunner.ForEachTenantAsync` 逐租户 → `ScanOnceAsync(DateTime.UtcNow, workerId, ct)`;`workerId` = 进程/实例标识(lease 用)。

### §4.2 `ScanOnceAsync` 主循环

```
// ① reaper 先行(P0-4):回收过期租约
foreach j in Wf_ServiceJobs.Where(Status==Running && LockExpiresAtUtc < nowUtc):
    j.Status=Pending; j.AttemptCount++; j.LockedBy=null; j.LockedAtUtc=null; j.LockExpiresAtUtc=null
SaveChanges

// ② 取到期 Pending
jobs = Wf_ServiceJobs.Where(Status==Pending && NextAttemptAtUtc<=nowUtc).OrderBy(NextAttemptAtUtc).Take(50)
foreach job:
  // 抢占(lease)
  job.Status=Running; job.LockedBy=workerId; job.LockedAtUtc=nowUtc; job.LockExpiresAtUtc=nowUtc+LeaseDuration
  try SaveChanges  catch DbUpdateConcurrencyException: continue   // 别人领了(RowVersion 守)

  // 执行前状态闸(P0-5):实例/ token 还活着?
  if !InstanceRunning(job.InstanceId) || !TokenActiveAt(job.TokenId, job.NodeId):
     job.Status=Cancelled; job.CompletedAtUtc=nowUtc; SaveChanges; continue

  // 执行
  job.AttemptCount++
  if job 无动作(actionKind=="none"): r = Ok()
  else: ex = Resolve(job.ActionRefJson)
        if ex==null: r = Fail("E-WF-018 动作/连接器未注册")
        else: try r = await ex.ExecuteAsync(BuildCtxFromJob(job, attemptNo:job.AttemptCount, actor:SystemActor, nowUtc))
              catch e: r = Fail(e.Message)

  if r.Success:
     resumed = await engine.ResumeServiceTokenAsync(job.InstanceId, job.TokenId, job.NodeId, r.OutputVars)  // 幂等(§4.4)
     job.Status=Succeeded; job.CompletedAtUtc=nowUtc; SaveChanges
  else:
     if job.AttemptCount < job.MaxAttempts:
        job.Status=Pending; job.NextAttemptAtUtc = nowUtc + Backoff*2^(AttemptCount-1); job.LastError=r.Error
        job.LockedBy=null; job.LockExpiresAtUtc=null
     else:
        job.Status=Failed; job.LastError=r.Error; job.CompletedAtUtc=nowUtc
        await engine.FailServiceTokenAsync(job.InstanceId, job.TokenId, job.NodeId, r.Error)   // §4.3
     SaveChanges
```

> **崩溃窗口闭合(P0-2)**:若 `ResumeServiceTokenAsync` 后、`Status=Succeeded` 前 worker 崩溃 → job 留 Running → reaper 到期重投 → 执行前状态闸发现 token 已离开该 node → 标 Cancelled 不重复执行。即便闸漏过,`ResumeServiceTokenAsync` 幂等也不会二次推进。executor 幂等(`JobId` 键)兜底外部副作用。

### §4.3 错误路由(重试耗尽,D3 / P1-4)

`engine.FailServiceTokenAsync(instId, tokenId, nodeId, reason)`:
- **先写标准错误变量**进 `inst.VarsJson`(下游人工节点据此展示原因):
  ```jsonc
  { "wf": { "serviceError": { "nodeId":"...", "jobId":"...", "kind":"webApi", "message":"...", "failedAtUtc":"..." } } }
  ```
- 节点有 `IsError` 出边 → `AdvanceAlongErrorEdge(token)`(沿错误边推进)。
- 否则 → `Suspend(inst, node, "服务任务失败:"+reason)`。

### §4.4 引擎新方法(`FlowEngine.Tokens.cs` / `FlowEngine.cs`)

| 方法 | 职责 |
|---|---|
| `ResumeServiceTokenAsync(instId, tokenId, nodeId, outputVars?)` | **幂等(P0-2)**:重载 inst/schema/token;若 token 非 Active 或 `token.NodeId!=nodeId` → 直接返回(已恢复/已离开,不二次推进);否则合并 outputVars→`AdvanceToken`→`DispatchIfFinished`→SaveChanges;包乐观并发重试×3 |
| `FailServiceTokenAsync(instId, tokenId, nodeId, reason)` | §4.3;同样幂等(token 已离开则 no-op)+ 重试×3 |
| `AdvanceAlongErrorEdge(inst, schema, token)` | 选 `IsError==true` 的出边推进;无错误边则 Suspend |
| `AdvanceToken` 改动 | **跳过 `IsError==true` 的边**:`Where(e => e.From==token.NodeId && e.IsError != true)`。既有边 `IsError==null`→`null!=true` 为真→字节等价 |

### §4.5 原子性 / 幂等(D6 / D10 / P2-1)

- **sync 快乐路径原子的前提**(显式):executor 用同一 scoped `DbContext`、不提前 `SaveChanges`、不调用外部 HTTP、不开独立事务 → 业务写入与流程写入同一数据库事务提交。**故 sync 仅推荐本库短事务 dataWriteback;webApi 默认 async**,除非连接器明确声明可 sync。
- **async 路径**:执行与恢复跨事务 → at-least-once。`IServiceTaskExecutor` 契约要求幂等(`ctx.JobId` 作 `Idempotency-Key`)。抢占 lease + RowVersion + live-token 唯一约束 + 恢复幂等 = 多层降重。

### §4.6 lease-based reaper(P0-4)

见 §4.2 ①:只回收 `Status==Running && LockExpiresAtUtc < nowUtc` 的 job(过期租约=worker 崩溃)。`LeaseDuration` 可配(默认如 5min,且应 > 单次 executor 预期耗时;长任务可由连接器侧自行约束超时)。不再依赖 `ModifyDate`。

### §4.7 timer 到期计算 `ComputeDueUtc(node, varsJson)`(P0-6)

- `duration`:`nowUtc + ParseDuration(ServiceDelayValue)`("3d" / ISO-8601 "PT2H")。
- `untilDate`:把用户输入(如 `2026-07-01`)按**租户时区(无则 app 默认时区)**解释 → 转 UTC。
- `untilExpr`:对 `varsJson` 求值 → 日期 → 转 UTC。
- 一律返回 **UTC** 存 `DueAtUtc`。工作日/日历口径 = 留后(§13)。

### §4.8 实例终止时清理(P0-5)

撤回/驳回走 `CancelAllActiveTokens(instanceId)` 时:
- 该实例 `Status==Pending` 的 `Wf_ServiceJob` → `Cancelled`(+ `CompletedAtUtc`)。
- `Status==Running` 的 job 不强杀(可能正在跑外部调用);由 worker 的**执行前/恢复前状态闸**(§4.2 / §4.4)发现实例已终止 → 标 Cancelled,不执行 executor / 不恢复 token。

---

## §5 设计器与前端(`cp6.web/src/views/oa/designer/`)

### §5.1 调色板(`designerModel.ts` NODE_PALETTE)

加 3 个友好入口,都创建 `serviceTask` 节点、预置 `ServiceKind`:
```typescript
{ type:'serviceTask', kind:'dataWriteback', label:'数据回写',  color:'#9c27b0' },
{ type:'serviceTask', kind:'webApi',        label:'WebAPI调用', color:'#00bcd4' },
{ type:'serviceTask', kind:'timer',         label:'定时·等待',  color:'#795548' },
```

### §5.2 自定义节点组件

新增 serviceTask 自定义 Vue Flow 节点(仿 Start/Approval/Gateway/End,带 Handle),按 kind 显示标签/图标。

### §5.3 NodePropertyPanel 服务任务段(按 `ServiceKind` 切换)

- **dataWriteback**:动作(下拉,服务目录)+ mode(sync/async)+ 参数模板 + 重试次数/退避。
- **webApi**:连接器(下拉)+ 路径模板 + 参数模板 + mode + 重试次数/退避。
- **timer**:延时模式(duration/untilDate/untilExpr)+ 延时值 + 可选动作(无/回写动作/webApi 连接器)+ 重试次数/退避。

### §5.4 EdgePropertyPanel 错误边:加「失败边(IsError)」复选框。

### §5.5 服务目录端点(P1-6)

`GET /api/oa/designer/service-catalog` → `{ actions:[{name,label}], connectors:[{name,label}] }`:
- actions = `IServiceTaskExecutor.Where(e => e.Kind=="dataWriteback" && e.VisibleInDesigner)`(**过滤掉 WebApiExecutor**)。
- connectors = 全部 `IWfConnector`。
- 下拉只从注册项选(不自由填键)。控制器在 `DesignerController` 加 action(`LocalizedControllerBase`/`Ok2`)。

### §5.6 designerModel round-trip

`schemaToGraph`/`graphToSchema` round-trip 新 `Service*` 字段 + `FlowEdge.IsError`;`validateClient` 镜像后端(`errServiceConfig`)。

---

## §6 校验(分两层)

### §6.1 `FlowSchemaValidator`(纯静态)

serviceTask 新规则:
- `ServiceKind ∈ {dataWriteback,webApi,timer}`,否则 **E-WF-016**。
- dataWriteback:`ServiceActionName` 必填;webApi:`ServiceConnectorName`+`ServicePath` 必填;timer:`ServiceDelayMode`+`ServiceDelayValue` 必填(缺则 E-WF-016)。
- `ServiceMode ∈ {sync,async}`(timer 规整为 async)。
- **非 end 的 serviceTask 须 ≥1 条非错误出边(P2-3)**——服务任务成功后应有后继;若要终止须显式接 end 节点(缺成功出边 → E-WF-016)。
- 错误边(**E-WF-017**):一节点 ≤1 条 `IsError` 出边;`IsError` 边仅允许出自 serviceTask 节点。

### §6.2 `DesignerService.save`(有 DI)

额外校验引用的 `ServiceActionName`(在 dataWriteback executor 目录)/`ServiceConnectorName`(在连接器目录)**确实已注册** → 否则 **E-WF-018**。

---

## §7 错误码(续 E-WF 序列,上一个用到 015)

| 码 | 含义 | 抛处 |
|---|---|---|
| `E-WF-016` | 服务任务配置不完整/非法(kind/动作/连接器/路径/延时/成功出边缺失) | `FlowSchemaValidator` |
| `E-WF-017` | 错误边非法(>1 错误边 / 错误边来源非服务任务) | `FlowSchemaValidator` |
| `E-WF-018` | 引用的回写动作/连接器未注册 | `DesignerService.save`(设计期);运行期动作失踪时 Suspend 原因带此码 |

运行期服务任务失败挂起复用既有 `Suspend`(不新增码)。

---

## §8 i18n

新 seed `I18nOaServiceTaskScreenSeed`(五语,仿 `I18nOaApproverScreenSeed`):设计器面板标签(kind/mode/重试/退避/延时/连接器/路径/参数/错误边)+ 错误消息 E-WF-016/017/018。concat 进 `Program.cs` i18n seed 链(带去重)。无新菜单(设计器在菜单 738)。

---

## §9 安全与多租户

- 连接器密钥服务端配置,**绝不进 SchemaJson**(D9);流程定义可导出/克隆/明文存。
- WebAPI 仅经注册连接器(D4),无开放 URL → 无 SSRF。
- `Wf_ServiceJob` 带 `TenantId`,扫描器逐租户 scope(租户隔离)。
- 服务目录端点只读已注册项名/标签,不泄露 baseURL/密钥。

---

## §10 向后兼容铁律

- `FlowNode`/`FlowEdge` 加 POCO 字段,既有流程无 `ServiceKind`/`IsError==null` → 行为不变。
- `EnterNodeAsync` 按 `node.Type` 分发,只有新 `Type="serviceTask"` 走新 handler,既有 5 类型零触碰。
- `AdvanceToken` 加 `IsError != true` 过滤 → 既有边字节等价。
- `Wf_ServiceJob` 纯新表零回填。
- **硬闸**:`dotnet test --filter Wf` 既有测试字节等价。

---

## §11 测试策略

**新测**(`CP6.Tests/Wf/`):
- `ServiceTaskHandlerTests`:sync 成功 advance / sync 失败入队(AttemptCount=1)+停泊 / async 入队 / timer 算 DueAtUtc。
- `ServiceJobScanTests`:worker 到期执行→恢复;失败退避重试(AttemptCount<MaxAttempts);耗尽→错误边;耗尽→挂起;lease 抢占并发(RowVersion);**lease reaper 只回收过期租约**;成功置 Succeeded+CompletedAtUtc。
- `ErrorEdgeRoutingTests`:`AdvanceToken` 跳过 IsError 边;`AdvanceAlongErrorEdge` 选它。
- `ServiceConnectorTests`:`WebApiExecutor` 解析连接器+模板(假 `IWfConnector`)+ `OutputVars` 合并规则(保留前缀拦截/top-level only)。
- `FlowSchemaValidator`/`DesignerService` 配置校验(016/017/018 + 非 end 须成功出边)。
- 前端 vitest:designerModel round-trip(Service* + IsError 存活)+ validateClient 镜像 + 参数模板语法。

**review 加测 6 条(P0 高价值)**:
1. **重复入队保护**:同 token 重复进 `ServiceTaskNodeHandler` 只生成一个 Pending/Running job。
2. **恢复幂等**:成功 advance 后再调 `ResumeServiceTokenAsync` 不二次推进。
3. **撤回后 worker 返回**:Running 期间实例撤回,executor 返回成功后**不**恢复 token(执行前/恢复前状态闸)。
4. **reaper 不误杀未过期 lease**:`Running && LockExpiresAtUtc > nowUtc` 不重置。
5. **timer untilDate 租户时区**:输入 `2026-07-01` 按租户时区转 UTC,`DueAtUtc` 正确。
6. **error edge 写错误变量**:耗尽走错误边后,下游节点能读到 `wf.serviceError`。

**可测时钟**:`ScanOnceAsync(DateTime nowUtc, ...)` 注入时间 → timer/退避/lease 确定性测;SQLite + trigger 测 RowVersion 并发(沿用 `SqliteCP6Context.HasTrigger`);filtered unique index SQLite 不支持 → 靠 EnqueueServiceJob 代码级先查测。

**gstack QA**(末期,隔离库 `CP6DB_OA`):设计含三类 serviceTask 的流(假 echo 连接器 + 样例回写动作 + 短 timer),端到端:sync 回写 advance / webApi async worker 恢复 / timer 等待后 advance / 失败→重试→错误边或挂起;真浏览器验三调色板 + 按 kind 属性面板 + 错误边复选。

---

## §12 分期 / 任务波次(供 writing-plans 细化)

- **P-A 引擎内核**:数据模型(FlowNode/Edge POCO + Wf_ServiceJob 表 + 迁移 + 枚举 + filtered unique index)→ `IServiceTaskExecutor`/`Context`/`Result`(含 JobId/AttemptNo/ActorId/NowUtc)+ 注册表解析 + ActionRefJson 结构 → `ServiceTaskNodeHandler`(sync/async 分支 + 防重入队 + 停泊)→ 引擎方法(ResumeServiceTokenAsync 幂等 / FailServiceTokenAsync + 错误变量 / AdvanceAlongErrorEdge + AdvanceToken 跳错误边)+ 参数模板/OutputVars 合并。
- **P-B 异步底座**:`IWfServiceJobService.ScanOnceAsync`(reaper + lease 抢占 + 状态闸 + AttemptCount + 退避重试 + 错误路由)+ `WfServiceJobScanWorker` + DI + 撤回清理接缝(§4.8)。
- **P-C 连接器 + 执行器**:`IWfConnector` + `WebApiExecutor`(Kind=webApi/VisibleInDesigner=false)+ 1 样例 dataWriteback executor + app 级连接器注册 + 服务目录端点(过滤)。
- **P-D 设计器**:NODE_PALETTE 3 入口 + serviceTask 自定义节点 + NodePropertyPanel 按 kind + EdgePropertyPanel 错误边 + designerModel round-trip + validateClient + 服务目录拉取。
- **P-E 校验 + 错误码 + i18n + QA**:FlowSchemaValidator/DesignerService 规则 + E-WF-016/017/018 + `I18nOaServiceTaskScreenSeed` + gstack QA harness。

---

## §13 YAGNI / 留后

- **子流程(subprocess,P4)**:嵌套实例生命周期;本增量异步底座(Wf_ServiceJob + worker)已铺路。
- **per-tenant 连接器配置**:首切 app 级;后续 `Wf_Connector` 租户表 + 加密密钥列。
- **per-node HTTP method/timeout/response-map 覆盖**:首切由连接器决定;后续可加节点级覆盖字段。
- **工作日/日历口径延时**("3 个工作日"):首切日历时长 + 绝对日期 + 表达式。
- **错误边来源放宽**:首切仅 serviceTask;后续放宽到 approval 等(超时/异常统一错误路由)。
- **终态 job 保留/清理**:`CompletedAtUtc` 已记录;Succeeded/Failed/Cancelled 保留 ~180 天或随实例归档,清理后台任务留后。
- **事件触发 start**(timer-start / message / IntegrationEvent 边界事件,内核 spec §11 P3)。
- **租户时区基础设施**:`untilDate` 首切按 app 默认时区(无 per-tenant tz 时);tenant-tz 设置落地后字段名带 `Utc` 零返工。
