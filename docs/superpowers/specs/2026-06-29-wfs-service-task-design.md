# WFS 引擎深化 · 服务任务节点(Service Task)设计

> 版本 v1.0 · 2026-06-29
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

**Out(留后,§14)**:
- 子流程(subprocess,P4)。
- per-tenant 连接器配置(首切连接器为 app 级配置注册)。
- 工作日/日历口径延时("3 个工作日")。
- 错误边来源放宽到非服务任务节点。
- 事件触发 start(timer-start / message start / IntegrationEvent 边界事件)。

### §0.3 锁定决策(D1~D11)

| # | 决策 | 取舍理由 |
|---|---|---|
| **D1** | 首切三类 = dataWriteback + webApi + timer;子流程留后 | 一个完整闭环又不过大;子流程需嵌套实例生命周期,复杂度上台阶 |
| **D2** | timer = 纯等待 **和** 到点执行动作 两者都要;timer 是「调度包装器」,可选挂一个动作 | 用户业务两种都用;统一为「DueAt + 可选动作」一套机制 |
| **D3** | 失败语义 = 重试 N 次 → 有错误边走错误边,否则挂起(Suspend) | 用户选「组合」;对齐 Delta「30 呼叫ERP重試中」+「1001 表單退回」;复用引擎既有 Suspend |
| **D4** | webApi 绑定 = 注册式连接器(服务端预置 baseURL/认证/密钥,设计器选连接器+路径/参数模板) | 多租户 SaaS 安全:无 SSRF、密钥不进流程定义;低码+安全平衡点 |
| **D5** | 架构 = 方案 A:**单一 `serviceTask` 节点类型** + 单一 `IServiceTaskExecutor` 注册表 + 共享异步底座 | 重试/错误边/停泊-恢复 plumbing 集中一处;调色板靠设计器层做显式;executor 注册表直接落地 §11 泛化;异步底座未来可扩子流程 |
| **D6** | sync = 内联乐观一击(快/原子),失败转异步重试;async/timer = 直接停泊入队 | 本地回写走快路径且与流程态同事务原子;失败自动降级异步重试不阻塞用户请求;timer 天生异步 |
| **D7** | 异步底座 = 轮询扫描 `WfServiceJobScanWorker`(复用 `WfTimeoutScanWorker` 模式),非 Kafka/outbox | 租户隔离、at-least-once、自包含;repo 里 Kafka 在 dev 标记 flaky/可选,避免基础设施耦合 |
| **D8** | 错误边 = `FlowEdge.IsError` 标记;普通 `AdvanceToken` 跳过它,仅服务任务重试耗尽走 | 成功路径绝不误走错误边(关键不变量) |
| **D9** | 连接器密钥**绝不进 SchemaJson**;首切 app 级配置注册 | 流程定义可导出/克隆/明文存,密钥泄露不可接受 |
| **D10** | `IServiceTaskExecutor` 契约要求**幂等**(async at-least-once);抢占(Running)+ RowVersion + reaper 降重 | 异步执行与恢复跨事务,崩溃可能重投 |
| **D11** | `FlowNode`/`FlowEdge` 是 SchemaJson POCO 加字段 = **零迁移**;唯一新迁移 = `Wf_ServiceJob` 表 | 与审批人增量同款手法;新表纯加法零回填 |

---

## §1 现状锚点(逆向真实,不编造)

| 组件 | 位置 | 关键事实 |
|---|---|---|
| `INodeHandler` | `CP6.Core/Services/Wf/INodeHandler.cs:8-12` | `string Type {get;}` + `Task OnEnterAsync(NodeContext ctx)` |
| `NodeContext` | `INodeHandler.cs:15-22` | `Inst / Schema / Node / Token / Engine`(token 中心 + 引擎反引用) |
| 5 个 handler | `CP6.Core/Services/Wf/NodeHandlers/*.cs` | start/approval/end/parallelSplit/parallelJoin |
| handler 分发 | `FlowEngine.cs:265-272` | 按 `node.Type.ToLowerInvariant()` 查 `_handlers` 字典;未知类型抛 `InvalidOperationException` |
| handler DI 注册 | `Program.cs:108-112` | `AddScoped<INodeHandler, XxxNodeHandler>()` ×5 |
| token 原语 | `FlowEngine.Tokens.cs` | `SpawnToken`(15-25)/ `AdvanceToken`(69-84)/ `ConsumeToken`(28-32)/ `FinishIfDrained`(49-54)/ `CancelAllActiveTokens`(37-46) |
| `AdvanceToken` | `FlowEngine.Tokens.cs:69-84` | 沿首条 `ExpressionEvaluator.Evaluate(edge.Condition, vars)` 为真的出边推进;无后继→Consume+FinishIfDrained |
| `Suspend` | `FlowEngine.cs:293-297` | `inst.Status=Suspended` + 历史 |
| 乐观并发重试 | `FlowEngine.cs:103-114` | `ActAsync` 包 `ActOnceAsync`,`DbUpdateConcurrencyException` → 全 reload → 重试(attempt 0/1/2) |
| `FlowInstanceStatus` | `WfStatus.cs:4-12` | Running=0/Approved=1/Rejected=2/Withdrawn=3/Suspended=4/Draft=5 |
| `FlowTokenStatus` | `WfStatus.cs` | Active/Consumed/Cancelled |
| `IApprovalCallback` | `IApprovalCallback.cs:13-23` | `BizType` + `OnApprovedAsync(ctx)` + `OnRejectedAsync(ctx)`;注释「应幂等」 |
| `ApprovalDispatcher` | `ApprovalDispatcher.cs:23-42` | 按 `inst.BizType` 找回调,**`SaveChanges` 前**调用(原子) |
| `IFinBridgeHook` | `IFinBridgeHook.cs:10-20` | 跨模块回写既有样板(出货→开票/工单→成本) |
| `FlowNode`(POCO) | `FlowSchema.cs:16-69` | Type 默认 "approval";已有审批/会签/超时/抄送/坐标/串簽/高级审批人字段 |
| `FlowEdge`(POCO) | `FlowSchema.cs:71-81` | From/To/Condition/CcUsers |
| `FlowSchemaValidator` | `FlowSchemaValidator.cs` | 纯静态:一 start/≥一 end/边引用存在/start BFS 可达 end/网关入出边数;统一 E-WF-010 |
| `NODE_PALETTE`(前端) | `cp6.web/src/views/oa/designer/designerModel.ts:49-55` | 5 类型调色板 |
| `IWfTimeoutService` | `WfTimeoutService.cs:9-12` | `Task<int> ScanOnceAsync(DateTime now, CancellationToken ct)`(注入时间=可测) |
| `WfTimeoutScanWorker` | `CP6.WebApi/BackgroundServices/WfTimeoutScanWorker.cs:10-49` | `BackgroundService`,每 1min,`TenantScopeRunner.ForEachTenantAsync`,`SystemActor=Guid.Empty` |
| Wf 测试面 | `CP6.Tests/Wf/*.cs` | `--filter Wf` 硬闸,须字节等价 |

---

## §2 数据模型

### §2.1 `FlowNode` 加服务任务配置字段(POCO,`FlowSchema.cs`,零迁移,全可空向后兼容)

```csharp
// 服务任务(ServiceKind!=null 时本节点 Type 应="serviceTask")
public string? ServiceKind { get; set; }            // "dataWriteback" | "webApi" | "timer";null=非服务任务
public string? ServiceMode { get; set; }            // "sync" | "async";timer 恒按 async 处理

// 动作绑定:dataWriteback 用 ActionName;webApi 用 Connector+Path+Params;timer 可选挂动作(复用这些字段)
public string? ServiceActionName { get; set; }       // dataWriteback / timer 动作:注册执行器键
public string? ServiceConnectorName { get; set; }    // webApi / timer 动作:注册连接器键
public string? ServicePath { get; set; }             // webApi:相对 baseURL 的路径模板(可含 {var} 占位)
public string? ServiceParamsJson { get; set; }       // 参数/载荷模板:JSON,键→表达式(取表单 vars),executor 求值

// timer 专属
public string? ServiceDelayMode { get; set; }        // "duration" | "untilDate" | "untilExpr"
public string? ServiceDelayValue { get; set; }       // "3d"/"PT2H" | "2026-07-01" | 表达式(对 vars 求 DueAt)

// 重试策略(async 路径生效)
public int? ServiceMaxRetries { get; set; }          // 默认 3
public int? ServiceRetryBackoffSec { get; set; }     // 默认 30,指数退避
```

> **timer 语义统一**:timer 节点必有 `ServiceDelay*`(决定 DueAt);若同时有 `ServiceActionName` 或 `ServiceConnectorName` 则到点执行该动作再推进(到点执行动作),否则纯等待直接推进。timer 不需要独立 executor —— 它复用 dataWriteback / webApi 的 executor 在到期时执行。

### §2.2 `FlowEdge` 加错误边标记(POCO,`FlowSchema.cs`,零迁移)

```csharp
public bool? IsError { get; set; }   // true=失败出边;普通 AdvanceToken 跳过它,仅服务任务重试耗尽时走
```

### §2.3 新表 `Wf_ServiceJob`(异步停泊任务台账,唯一新迁移 `WfsServiceTask`)

EF 实体(`CP6.Entity/DomainModels/Wf/Wf_ServiceJob.cs`,继承 `BaseTenantEntity`):

```csharp
public Guid Id { get; set; }                  // PK
// TenantId 来自 BaseTenantEntity
public Guid InstanceId { get; set; }          // FK→Wf_FlowInstance    [IX]
public Guid TokenId { get; set; }             // 要恢复的停泊 token
public string NodeId { get; set; }            // 服务任务节点 Id
public string Kind { get; set; }              // dataWriteback/webApi/timer
public string? ActionRefJson { get; set; }    // 固化的动作绑定快照(actionName/connector/path/params),防流程定义漂移
public DateTime DueAt { get; set; }           // async 动作=入队时刻;timer=未来到期时刻
public int Status { get; set; }               // ServiceJobStatus(见 §2.4)
public int RetryCount { get; set; }           // 已重试次数
public int MaxRetries { get; set; }
public DateTime NextAttemptAt { get; set; }   // 退避后下次可执行时刻(扫描门控)
public string? LastError { get; set; }        // 最后失败原因(截断,如 ≤1000 字)
public DateTime CreateDate { get; set; }
public DateTime? ModifyDate { get; set; }
[Timestamp] public byte[]? RowVersion { get; set; }   // 乐观并发,防多 worker 抢同一 job
```

索引(`CP6Context.OnModelCreating`):
- `IX_Wf_ServiceJob_Scan = (TenantId, Status, NextAttemptAt)` —— 扫描器查待执行。
- `IX_Wf_ServiceJob_Instance = (InstanceId)` —— 终止清理 / 实例维度查询。

DbSet:`public DbSet<Wf_ServiceJob> Wf_ServiceJobs { get; set; }`。

### §2.4 枚举/常量(`WfStatus.cs` 同处,沿用 `static class + const int` 风格)

```csharp
public static class ServiceJobStatus
{
    public const int Pending   = 0;   // 待执行(NextAttemptAt 门控)
    public const int Running   = 1;   // 已抢占执行中
    public const int Succeeded = 2;   // 成功终态
    public const int Failed    = 3;   // 重试耗尽终态(已路由错误边/挂起)
    public const int Cancelled = 4;   // 实例撤回/驳回时清理
}
public static class ServiceKind
{
    public const string DataWriteback = "dataWriteback";
    public const string WebApi        = "webApi";
    public const string Timer         = "timer";
}
public static class ServiceMode
{
    public const string Sync  = "sync";
    public const string Async = "async";
}
```

---

## §3 执行架构

### §3.1 `IServiceTaskExecutor` 契约(§11 的「泛化 `IApprovalCallback`」落地)

```csharp
// CP6.Core/Services/Wf/IServiceTaskExecutor.cs
public interface IServiceTaskExecutor
{
    /// <summary>注册键。webApi→"webApi";dataWriteback→动作名(每命名回写动作一个实现)。</summary>
    string Key { get; }
    /// <summary>显示名(设计器目录下拉用,可为 i18n 键)。</summary>
    string DisplayName { get; }
    /// <summary>执行。实现**必须幂等**(async at-least-once,崩溃可能重投)。</summary>
    Task<ServiceTaskResult> ExecuteAsync(ServiceTaskContext ctx);
}

public sealed class ServiceTaskContext
{
    public required Guid InstanceId { get; init; }
    public required Guid TokenId { get; init; }
    public required string NodeId { get; init; }
    public required Guid StarterId { get; init; }
    public string? VarsJson { get; init; }        // 表单数据,供参数模板求值
    public string? ActionRefJson { get; init; }   // 固化动作绑定快照
    // executor 通过注入服务(DB/HttpClient)干活,不直接持有 FlowEngine(职责隔离)
}

public sealed class ServiceTaskResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public Dictionary<string, object?>? OutputVars { get; init; }  // 可选:合并回 inst.VarsJson
    public static ServiceTaskResult Ok(Dictionary<string, object?>? outputVars = null) => ...;
    public static ServiceTaskResult Fail(string error) => ...;
}
```

### §3.2 单一注册表 + 按键解析

引擎/handler 持有 `IEnumerable<IServiceTaskExecutor>` → `ToDictionary(e => e.Key, OrdinalIgnoreCase)`。解析规则(`ResolveExecutorKey(node)`):

| ServiceKind | 解析键 | executor |
|---|---|---|
| webApi | `"webApi"` | 单一 `WebApiExecutor`(处理所有连接器) |
| dataWriteback | `ServiceActionName` | 命名回写动作 executor(每个一实现,开发者注册) |
| timer(无动作) | —(null) | 无 executor,纯推进 |
| timer(有动作) | dataWriteback→ActionName / webApi→"webApi" | 复用对应 executor |

### §3.3 连接器注册(WebAPI 安全底座)

```csharp
// CP6.Core/Services/Wf/IWfConnector.cs
public interface IWfConnector
{
    string Name { get; }          // 设计器下拉用
    string DisplayName { get; }
    /// <summary>按路径模板+参数模板调用(实现内部:IHttpClientFactory + 预置 baseURL/认证)。</summary>
    Task<ServiceTaskResult> CallAsync(string pathTemplate, string? paramsJson, ServiceTaskContext ctx);
}
```

- DI 注册、按 `Name` 索引;`WebApiExecutor.ExecuteAsync` 从 `ActionRefJson` 取 connectorName/path/params → 找 `IWfConnector` → `CallAsync`。
- 实现内部:求值 `path`/`params` 模板(取 `ctx.VarsJson`)→ HTTP 调用 → 解析响应映射为 `OutputVars`。
- **密钥(API key/token)服务端配置**(appsettings / secret store),**绝不进 SchemaJson**(D9)。
- 首切连接器为 **app 级 DI/配置注册**;per-tenant 连接器 = YAGNI 留后(§14)。

### §3.4 `ServiceTaskNodeHandler`(`Type="serviceTask"`,第 6 个 handler)

DI 注册(`Program.cs`,追加一行)+ `DefaultHandlers()`(`FlowEngine.cs:34`,追加)。

```
OnEnterAsync(ctx):
  node = ctx.Node; inst = ctx.Inst; token = ctx.Token
  kind = node.ServiceKind
  mode = (kind==timer) ? async
         : (node.ServiceMode ?? (kind==webApi ? async : sync))   // dataWriteback 默认 sync;webApi 默认 async;timer 恒 async

  if mode == sync:
     r = await ResolveExecutor(node).ExecuteAsync(BuildCtx(node, token))
     if r.Success:
        MergeOutputVars(inst, r.OutputVars)
        await ctx.Engine.AdvanceToken(inst, schema, token)        // 原子(SaveChanges 前),沿成功边(跳过 IsError)
     else:
        EnqueueServiceJob(inst, token, node, dueAt: now, retryCount: 1,
                          nextAttemptAt: now + backoff)            // 失败降级异步重试;token 停泊不 advance
  else (async / timer):
     dueAt = (kind==timer) ? ComputeDue(node, inst.VarsJson) : now
     EnqueueServiceJob(inst, token, node, dueAt, retryCount: 0, nextAttemptAt: dueAt, Status: Pending)
     // token 保持 Active 停泊(像 ApprovalNodeHandler 等 ActAsync),由 worker 到点恢复
```

- **停泊** = token 留在服务任务节点 Active,不 advance。
- `EnqueueServiceJob`:`_db.Wf_ServiceJobs.Add(new Wf_ServiceJob{ ... ActionRefJson = SnapshotActionRef(node) ... })`,**固化动作绑定快照**(防流程定义之后被改导致漂移)。
- `MergeOutputVars`:把 `OutputVars` 合并进 `inst.VarsJson`(JSON 合并;键冲突以输出为准)。
- handler 本身不写 SaveChanges(沿用引擎在 Submit/ActOnce 边界统一保存)。

---

## §4 异步底座与错误处理

### §4.1 `IWfServiceJobService` + `WfServiceJobScanWorker`

服务接口(`CP6.Core/Services/Wf/WfServiceJobService.cs`,仿 `IWfTimeoutService`):
```csharp
public interface IWfServiceJobService
{
    /// <summary>扫一遍到期待执行 job,逐条执行+恢复/重试/路由。返回处理条数。</summary>
    Task<int> ScanOnceAsync(DateTime now, CancellationToken ct = default);
}
```

Worker(`CP6.WebApi/BackgroundServices/WfServiceJobScanWorker.cs`,克隆 `WfTimeoutScanWorker`):
- `BackgroundService`,间隔 ~20s(async webApi 要尽快;timer 远期靠 `NextAttemptAt` 门控,不会空跑)。
- `TenantScopeRunner.ForEachTenantAsync` 逐租户 scope → `IWfServiceJobService.ScanOnceAsync(DateTime.Now, ct)`。
- `SystemActor = Guid.Empty`(任何 actor 标记的操作用之,如历史)。

### §4.2 `ScanOnceAsync` 主循环

```
jobs = Wf_ServiceJobs.Where(Status==Pending && NextAttemptAt<=now).OrderBy(NextAttemptAt).Take(50)
foreach job:
  ① 抢占:job.Status = Running; SaveChanges
     catch DbUpdateConcurrencyException: continue   // 别人领了/重叠扫描,跳过(RowVersion 守)
  ② 执行:
     if job 无动作(timer 纯等待):  r = ServiceTaskResult.Ok()
     else: executor = Resolve(job.Kind, job.ActionRefJson)
           if executor==null:  r = Fail("E-WF-018 动作/连接器未注册:"+key)
           else: try r = await executor.ExecuteAsync(BuildCtxFromJob(job))
                 catch ex: r = Fail(ex.Message)
  ③ 成功:await engine.ResumeServiceTokenAsync(job.InstanceId, job.TokenId, r.OutputVars)
          job.Status = Succeeded; SaveChanges
  ④ 失败:job.RetryCount++; job.LastError = r.Error
          if job.RetryCount <= job.MaxRetries:
              job.Status = Pending
              job.NextAttemptAt = now + Backoff * 2^(RetryCount-1)   // 指数退避
          else (耗尽):
              job.Status = Failed
              await engine.FailServiceTokenAsync(job.InstanceId, job.TokenId, job.NodeId, r.Error)
          SaveChanges
```

### §4.3 错误路由(重试耗尽,D3)

`engine.FailServiceTokenAsync(instanceId, tokenId, nodeId, reason)`:
- 加载 instance + schema,找停泊 token。
- 若节点有 `IsError==true` 出边 → `AdvanceAlongErrorEdge(token)`(沿错误边推进:转人工补救/退回)。
- 否则 → `Suspend(inst, node, "服务任务失败:" + reason)`(实例置 Suspended,管理页可重试/改派)。

### §4.4 引擎新方法(`FlowEngine.Tokens.cs` / `FlowEngine.cs`)

| 方法 | 职责 |
|---|---|
| `ResumeServiceTokenAsync(instId, tokenId, outputVars?)` | 重载 instance/schema → 合并 outputVars 进 VarsJson → 找 token → `AdvanceToken` → `DispatchIfFinished` → SaveChanges;包乐观并发重试×3(仿 `ActAsync`) |
| `FailServiceTokenAsync(instId, tokenId, nodeId, reason)` | §4.3 错误路由;同样包重试×3 |
| `AdvanceAlongErrorEdge(inst, schema, token)` | 选 `IsError==true` 的出边推进;无错误边则 Suspend |
| `AdvanceToken` 改动 | **跳过 `IsError==true` 的边**(关键不变量 D8:成功路径绝不走错误边)。改动极小:`Where(e => e.From==token.NodeId && e.IsError != true)` |

> `AdvanceToken` 的这处 `IsError != true` 过滤对既有流程**字节等价**(既有边 `IsError==null`,`null != true` 为真,行为不变)。

### §4.5 原子性 / 幂等(D6/D10)

- **sync 快乐路径**:executor + AdvanceToken + SaveChanges 同一事务 = 原子(与现有 `IApprovalCallback` 终态回调同款;回写抛异常则流程态与业务一起回滚)。
- **async 路径**:执行与恢复是独立事务 → **at-least-once**。`IServiceTaskExecutor` 契约**要求幂等**(接口注释强制);webApi 连接器可用 `job.Id` 作幂等键。抢占(Running)+ RowVersion 把重复降到最低。

### §4.6 崩溃兜底 reaper

Running 状态超过阈值(如 5 分钟,可配)的 job = worker 崩溃留下的僵尸 → 扫描时重置 `Status=Pending` + `RetryCount++`(防死循环)重投。实现:`ScanOnceAsync` 开头先扫一遍 `Status==Running && ModifyDate < now-阈值` 重置。

### §4.7 timer 到期计算 `ComputeDue(node, varsJson)`

- `duration`:`now + ParseDuration(ServiceDelayValue)`(支持 "3d" / ISO-8601 "PT2H" 等)。
- `untilDate`:解析 `ServiceDelayValue` 为绝对日期/时刻。
- `untilExpr`:对 `varsJson` 求值表达式 → 日期(复用 `ExpressionEvaluator` 思路,可能需扩日期返回)。
- **工作日/日历口径**("3 个工作日") = 留后(§14);首切先做日历时长 + 绝对日期 + 表达式。

### §4.8 实例终止时清理(D 撤回/驳回)

撤回/驳回走 `CancelAllActiveTokens(instanceId)` 时,**同步把该实例 `Status==Pending` 的 `Wf_ServiceJob` 置 `Cancelled`**(挂进现有撤回/取消路径),避免 worker 唤醒死实例的停泊 token。

---

## §5 设计器与前端(`cp6.web/src/views/oa/designer/`)

### §5.1 调色板(`designerModel.ts` NODE_PALETTE)

加 3 个友好入口,都创建 `serviceTask` 节点、预置不同 `ServiceKind`:
```typescript
{ type: 'serviceTask', kind: 'dataWriteback', label: '数据回写', color: '#9c27b0' },
{ type: 'serviceTask', kind: 'webApi',        label: 'WebAPI调用', color: '#00bcd4' },
{ type: 'serviceTask', kind: 'timer',         label: '定时·等待',  color: '#795548' },
```
(调色板预置 kind;落到画布生成 `serviceTask` 节点带该 kind)

### §5.2 自定义节点组件

新增 serviceTask Vue Flow 自定义节点(仿 Start/Approval/Gateway/End,带 Handle),按 kind 显示标签/图标。

### §5.3 NodePropertyPanel 服务任务段

按 `ServiceKind` 切换字段:
- **dataWriteback**:动作(下拉,来自服务端目录)+ mode(sync/async)+ 参数模板(textarea JSON)+ 重试(maxRetries/backoff)。
- **webApi**:连接器(下拉)+ 路径模板(input)+ 参数模板(textarea)+ mode + 重试。
- **timer**:延时模式(radio duration/untilDate/untilExpr)+ 延时值 + 可选动作(无/回写动作/webApi 连接器)+ 重试。

### §5.4 EdgePropertyPanel 错误边

加「失败边(IsError)」复选框。

### §5.5 服务目录端点

`GET /api/oa/designer/service-catalog` → `{ actions: [{name,label}], connectors: [{name,label}] }`,从服务端注册的 `IServiceTaskExecutor`(dataWriteback 类)/ `IWfConnector` 列出。**下拉只从注册项选**(不让自由填动作键 = 安全+可发现)。控制器:`DesignerController` 加一个 action(沿用 `LocalizedControllerBase` / `Ok2`)。

### §5.6 designerModel round-trip

`schemaToGraph` / `graphToSchema` round-trip 新 `Service*` 字段 + `FlowEdge.IsError`;`validateClient` 镜像后端校验(新 `errServiceConfig` 等)。

---

## §6 校验(分两层)

### §6.1 `FlowSchemaValidator`(纯静态,无 DI)

serviceTask 节点新规则:
- `ServiceKind ∈ {dataWriteback, webApi, timer}`,否则 **E-WF-016**。
- dataWriteback:`ServiceActionName` 必填(E-WF-016)。
- webApi:`ServiceConnectorName` + `ServicePath` 必填(E-WF-016)。
- timer:`ServiceDelayMode` + `ServiceDelayValue` 必填(E-WF-016)。
- `ServiceMode ∈ {sync, async}`(timer 可由校验器规整为 async)。
- 错误边(**E-WF-017**):一节点 ≤1 条 `IsError` 出边;有错误边须 ≥1 条非错误出边(成功路径);`IsError` 边仅允许出自 serviceTask 节点。

### §6.2 `DesignerService.save`(有 DI)

额外校验引用的 `ServiceActionName`/`ServiceConnectorName` **确实已注册**(查 executor/connector 目录)→ 否则 **E-WF-018**。

---

## §7 错误码(续 E-WF 序列,上一个用到 015)

| 码 | 含义 | 抛处 |
|---|---|---|
| `E-WF-016` | 服务任务配置不完整/非法(kind/动作/连接器/路径/延时缺失或非法) | `FlowSchemaValidator` |
| `E-WF-017` | 错误边非法(>1 错误边 / 仅错误边无成功边 / 错误边来源非服务任务) | `FlowSchemaValidator` |
| `E-WF-018` | 引用的回写动作/连接器未注册 | `DesignerService.save`(设计期);运行期动作失踪时 Suspend 原因带此码 |

运行期服务任务失败挂起复用既有 `Suspend`(不新增码)。

---

## §8 i18n

新 seed `I18nOaServiceTaskScreenSeed`(五语,仿 `I18nOaApproverScreenSeed`):设计器面板标签(kind/mode/retry/backoff/delay/connector/path/params/错误边)+ 错误消息 E-WF-016/017/018。concat 进 `Program.cs` i18n seed 链(带去重)。**无新菜单**(设计器在菜单 738)。

---

## §9 安全与多租户

- 连接器密钥服务端配置,**绝不进 SchemaJson**(D9);流程定义可导出/克隆/明文存。
- WebAPI 仅经注册连接器(D4),无开放 URL → 无 SSRF。
- `Wf_ServiceJob` 带 `TenantId`(`BaseTenantEntity`),扫描器逐租户 scope(租户隔离)。
- 服务目录端点只读已注册项名/标签,不泄露 baseURL/密钥。

---

## §10 向后兼容铁律

- `FlowNode`/`FlowEdge` 加 POCO 字段,既有流程无 `ServiceKind`/`IsError==null` → 行为不变。
- `EnterNodeAsync` 按 `node.Type` 分发,只有新 `Type="serviceTask"` 走新 handler,既有 5 类型零触碰。
- `AdvanceToken` 加 `IsError != true` 过滤 → 既有边 `null != true` 为真,字节等价。
- `Wf_ServiceJob` 纯新表零回填。
- **硬闸**:`dotnet test --filter Wf` 既有测试字节等价。

---

## §11 测试策略

**新测**(`CP6.Tests/Wf/`):
- `ServiceTaskHandlerTests`:sync 成功 advance / sync 失败入队+停泊 / async 入队+停泊 / timer 算 DueAt。
- `ServiceJobScanTests`:worker 到期执行→恢复 token;失败退避重试;耗尽→错误边;耗尽→挂起(无错误边);抢占并发(RowVersion);reaper 重置僵尸;撤回清理 Pending job。
- `ErrorEdgeRoutingTests`:`AdvanceToken` 跳过 IsError 边;`AdvanceAlongErrorEdge` 选它。
- `ServiceConnectorTests`:`WebApiExecutor` 解析连接器+模板(假 `IWfConnector`)+ `OutputVars` 合并。
- `FlowSchemaValidator` / `DesignerService` 配置校验(016/017/018)。
- 前端 vitest:designerModel round-trip(Service* + IsError 存活)+ validateClient 镜像。

**可测时钟**:`ScanOnceAsync(DateTime now)` 注入时间(仿 `WfTimeoutService`)→ timer/退避确定性测;SQLite + trigger 测 RowVersion 并发(沿用 `SqliteCP6Context.HasTrigger`)。

**gstack QA**(末期,隔离库 `CP6DB_OA`):设计含三类 serviceTask 的流(假 echo 连接器 + 样例回写动作 + 短 timer),端到端:sync 回写 advance / webApi async worker 恢复 / timer 等待后 advance / 失败→重试→错误边或挂起;真浏览器验三调色板 + 按 kind 属性面板 + 错误边复选。

---

## §12 分期 / 任务波次(供 writing-plans 细化)

- **P-A 引擎内核**:数据模型(FlowNode/Edge POCO 字段 + Wf_ServiceJob 表 + 迁移 + 枚举)→ `IServiceTaskExecutor`/`ServiceTaskContext`/`Result` + 注册表解析 → `ServiceTaskNodeHandler`(sync/async 分支 + 停泊/入队)→ 引擎新方法(Resume/Fail/AdvanceAlongErrorEdge + AdvanceToken 跳错误边)。
- **P-B 异步底座**:`IWfServiceJobService.ScanOnceAsync`(抢占/执行/恢复/退避重试/错误路由/reaper)+ `WfServiceJobScanWorker` + DI + 撤回清理接缝。
- **P-C 连接器 + 执行器**:`IWfConnector` + `WebApiExecutor` + 1 样例 dataWriteback executor + app 级连接器注册 + 服务目录端点。
- **P-D 设计器**:NODE_PALETTE 3 入口 + serviceTask 自定义节点 + NodePropertyPanel 按 kind + EdgePropertyPanel 错误边 + designerModel round-trip + validateClient + 服务目录拉取。
- **P-E 校验 + 错误码 + i18n + QA**:FlowSchemaValidator/DesignerService 规则 + E-WF-016/017/018 + `I18nOaServiceTaskScreenSeed` + gstack QA harness。

---

## §13 YAGNI / 留后

- **子流程(subprocess,P4)**:嵌套实例生命周期,本增量异步底座(Wf_ServiceJob + worker)已为其铺路。
- **per-tenant 连接器配置**:首切 app 级;后续可加 `Wf_Connector` 租户表 + 加密密钥列。
- **工作日/日历口径延时**("3 个工作日"):首切日历时长 + 绝对日期 + 表达式。
- **错误边来源放宽**:首切仅 serviceTask;后续可放宽到 approval 等(超时/异常统一错误路由)。
- **事件触发 start**(timer-start / message / IntegrationEvent 边界事件,内核 spec §11 P3)。
