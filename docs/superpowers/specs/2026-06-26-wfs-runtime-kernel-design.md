# WFS 运行时内核设计 spec — P1（定稿）

> 源：WFS 通用工作流引擎（OA 深化 / BPM 化）brainstorming 共识（2026-06-26）。已锁 4 项总决策（驱动=真实非审批业务流自动化 / 四类编排能力全要 / **方案 A 分阶段，运行时优先** / 设计器最后 P5），P1 设计八节 §0~§8 全部经用户逐节确认。底座 = 现有 OA 审批引擎（已 on main：`CP6.Core/Services/Wf/` 22 服务 + `CP6.Entity/DomainModels/Wf/` 8 实体 + `cp6.web/src/views/wf/` 16 视图，631+ 测试）。
>
> **本子项目（P1 运行时内核）把"以审批为中心、单活动节点（`CurrentNode`）"的引擎，原地泛化为"token 多活动节点 + 并行网关（split/join）+ `INodeHandler` 插件架构"的 BPM 运行时超集**，并以"631 测试不改照绿"为向后兼容硬验收闸。**P1 只做运行时内核四件事**：①token 化 ②并行网关 ③`INodeHandler` 架构 ④向后兼容。服务任务/定时事件/子流程/设计器分属 P2~P5，本期不做（§0 / §11）。
>
> 命名空间 **`CP6.Core.Services.Wf`**（原地演进，不立新门面）。`IFlowEngine` 对外签名不动（审批调用方零感知），BPM 能力靠更丰富的 `FlowSchema` 浮现。

---

## §0 范围

**做（P1 MVP，四件事）：**

- **① token 化运行时**：新增 `Wf_FlowToken` 独立表（一个实例可同时有多个 Active token）。"实例进行中"判定从"`CurrentNode` 单值"升级为"**存在 Active token**"。`Wf_FlowInstance.CurrentNode` **保留**（单 token 时精确、多 token 时为代表节点），不破坏既有审批 UI / 631 测试。
- **② 并行网关**：`FlowNode.Type` 新增 `parallelSplit`（并行分叉：一入边 N 出边，无条件全激活）/ `parallelJoin`（并行汇聚：N 入边一出边，等齐同批分支才放行）。靠 token 血缘（`ForkId` 认亲 + `ParentTokenId` 嵌套）实现 split→join 等待，嵌套并行天然成立。
- **③ `INodeHandler` 插件架构**：`EnterNodeAsync` 的 `if(end)/if(start)/else approval` 硬编码分支，重构为按 `node.Type` 查 `_handlers[type].OnEnterAsync(ctx)` 的多态分发。5 个 handler：`start`/`approval`/`end`（原样搬今天逻辑 = 兼容载体）+ `parallelSplit`/`parallelJoin`（新增）。未知类型**抛错**（闭合失败不静默直穿）。为 P2~P5 加节点类型留插件位（届时新增 handler + DI 注册即可，引擎不动）。
- **④ 向后兼容**：旧 `FlowSchema`（仅 start/approval/end + 排他边 + 会签 + 退回/加签/委派/超时）必须照跑不挂；**631 Wf 测试不改一行照绿 = 兼容硬验收闸**。在途 Running/Suspended 实例**幂等回填** token 行（§8）。

**不做（YAGNI，留 P2~P5）：**

- **服务任务 / 自动节点**（无人工、调适配器/回调跑完自动流转）—— P2（泛化 `IApprovalCallback`→`IServiceTaskHandler`，本期不碰）。
- **定时 / 事件触发**（timer-start、message/IntegrationEvent 触发起流程或边界事件）—— P3。
- **子流程 / 调用活动**（call-activity）—— P4。
- **BPM 设计器**（流程画布扩网关/并行图元）—— P5。**P1 不动前端**（`FlowDesigner.vue` 仍画审批有向图；并行流程用手写 / 种子 `SchemaJson` 验证运行时）。
- **inclusive（包容网关）**：split 出边选择性激活 / join 等"已激活分支"。**P1 显式不做**（YAGNI 已确认），留 `INodeHandler` 接口位，未来加 `inclusiveSplit`/`inclusiveJoin` handler。**P1 的 `parallelSplit` 忽略出边 `Condition`、无条件全激活**（选择性激活 = inclusive 的语义，归 inclusive）。
- **`IWorkflowEngine` 新门面 / `IFlowEngine` 签名变更**：原地演进 `FlowEngine` + 内部 handler 化，不过早抽象（§9）。
- **跨实例信号 / 补偿 / 事务边界 / 多实例（multi-instance）标记** —— 均留后续。

---

## §1 现状锚点（本会话 2026-06-26 实读核验；行号已坐实，落码前仍建议复读）

> 全部锚点本会话主代理实读 `D:\CP6\CP6.Core\Services\Wf\*` 与 `CP6.Entity\DomainModels\Wf\*` 核验。

- **运行时 = 单活动节点状态机**：`Wf_FlowInstance.CurrentNode`（`string`，单值）= `Wf_FlowInstance.cs:27`；`Status`（`int`）= `:30`，取值见 `FlowInstanceStatus`（`WfStatus.cs:4-11`：`Running=0/Approved=1/Rejected=2/Withdrawn=3/Suspended=4`）。`Wf_FlowInstance : BaseTenantEntity`（`:11`），无 RowVersion / 无并发标记。
- **核心推进 `ActAsync`** = `FlowEngine.cs:57-103`：幂等闸（`task.Status != Pending → return`，`:61`）→ 改任务态 + `AddHistory` → 前加签激活（`:73-74`）→ 取本节点非作废任务计票（`:77-82`，`EvaluateNodeCounts`）→ 未决则 `SaveChanges` 等其他会签人（`:84-88`）→ 已决：`CancelPendingTasks`（`:90`）→ 通过则 `NextNodeAsync(inst, schema, node)`（`:93-95`）/ 否决则 `inst.Status = Rejected`（`:99`）→ `DispatchIfFinishedAsync`（`:101`）→ 最终 `SaveChanges`（`:102`）。
- **进入节点 `EnterNodeAsync`** = `FlowEngine.cs:136-183`：`inst.CurrentNode = node.Id`（`:138`）→ `if end`（置 `Approved` + `AddHistory("end")` + return，`:140-145`）/ `if start`（`NextNodeAsync` 直穿，`:146-150`）/ else **approval**（`BuildRule`→`ResolveAsync`→缺位 `Suspend`→作废上轮遗留→按审批人 `Distinct` 建 `Wf_FlowTask`（带 `Countersign`/`DueAt`）+ `notifier.TodoCreatedAsync`，`:152-182`）。**这就是要 handler 化的 if/else 链**。
- **单边路由 `NextNodeAsync`** = `FlowEngine.cs:203-213`：`foreach edge where From==node.Id` → `ExpressionEvaluator.Evaluate(edge.Condition, inst.VarsJson)` 取**首个为真**的边 → `EnterNodeAsync(target)` 后 **`return`**（只进一个目标）；无匹配则 `inst.Status = Approved` 兜底结束（`:211-212`）。**"只 return 一个目标"= 现引擎无法分叉的命门**（并行网关要从这里突破）。
- **会签三规则 `EvaluateNodeCounts`** = `FlowEngine.cs:118-133`（纯静态：`all`/`veto` = 任一驳回即否、全同意才过；`any` = 任一同意即过、全驳才否）。**P1 不改这个函数**；只把计票口径从"`(InstanceId, NodeId)`"细化到"`(InstanceId, NodeId, TokenId)`"（§7.2）。
- **终态回调 `DispatchIfFinishedAsync`** = `FlowEngine.cs:109-115` → `ApprovalDispatcher.OnInstanceFinishedAsync`（`ApprovalDispatcher.cs:23-42`，按 `inst.BizType` 找 `IApprovalCallback` 同步直调，`IApprovalCallback.cs:13-23`）。**铁律（OA2-D5）**：回调与引擎共享 scoped DbContext，**必须在最终 `SaveChanges` 之前**调用（回调抛异常→流程终态与业务变更一并不落库，原子）。**P1 token 化后此铁律不变**（§4.3 `FinishIfDrained` 内仍在 `SaveChanges` 前 dispatch）。
- **节点/边 DSL** = `FlowSchema.cs`：`FlowNode.Type`（`string`，默认 `"approval"`，注释"start / approval / end"，`:21-22`）+ 审批人/会签/字段权限/超时字段（`:24-45`）；`FlowEdge.From/To/Condition`（`:48-55`，多条件边"按声明序取首个为真"）。**P1 在 `FlowNode.Type` 词汇表加 `parallelSplit`/`parallelJoin` 两值**，不加新字段（split/join 不需要审批人/会签）。
- **高级动作（partial）** = `AdvancedFlow.cs`：`SendBackAsync`（`:77-103`，作废全实例在途待办 + `EnterNodeAsync(target)` 重建）/ `AddSignAsync`（`:16-52`，节点级临时审批人，计入会签）/ `SetDelegateAsync`（`:54-75`）。**P1 退回/加签须 token 感知**（§7.3）。
- **超时扫描 `WfTimeoutService.ScanOnceAsync`** = `WfTimeoutService.cs:42-101`：扫 `DueAt<=now ∧ !TimeoutHandled` 待办；硬动作 `approve`/`reject` **调 `_engine.ActAsync(task.Id, SystemActor, …)`**（`:68`/`:73`，`SystemActor=Guid.Empty`，`:29`），软动作 `remind`/`escalate` 直接改 task 行后末尾统一 `SaveChanges`（`:99`）。**硬动作天然走 ActAsync 的重试路径（§6）；软动作只触达 task 行、不触达 inst RowVersion，无并发冲突**。
- **状态枚举家族** = `WfStatus.cs`：`FlowInstanceStatus`（`:4-11`）/`FlowTaskStatus`（`:14-21`：`Pending=0/Approved=1/Rejected=2/Cancelled=3/Suspended=4`）。两者皆 `static class const int`。**P1 新增 `FlowTokenStatus` 对齐此家族风格**（§2.2）。
- **DI 注册** = `Program.cs:105`：`AddScoped<IFlowEngine, FlowEngine>()`；`IWfNotifier`→`SignalRWfNotifier`（`:107`）。`ApprovalDispatcher` 经构造可选注入（`FlowEngine.cs:21-27`，无回调时退化空集）。**P1 新增 5 个 `INodeHandler` + token 原语；handler 用 DI 注册（§3.4）**。
- **DbSet / 索引 / 迁移**：`CP6Context.cs:373-387` 声明 8 个 Wf DbSet（`Wf_FlowInstances`=`:379`、`Wf_FlowTasks`=`:381`）；`:629-650` 配 Wf 索引（`Wf_FlowTask` 三索引 `:638-643`）。启动期 `Program.cs:584` `db.Database.Migrate()` 自动建表 / 应用迁移；种子块在 `:578-595`（`using scope` 内、`Migrate()` 后）。**P1 加 `DbSet<Wf_FlowToken>` + 索引 + EF 迁移（新表 + 2 新列）+ 回填种子（§8）**。
- **实体基类** = `BaseEntity.cs`（`Guid Id`[Key,Identity] + `Creator/CreateDate/Modifier/ModifyDate`）/ `BaseTenantEntity.cs`（加 `Guid TenantId`，自动全局过滤 + 写入盖章 `StampTenant`）。**8 个 Wf 实体全 `BaseTenantEntity`；`Wf_FlowToken` 同范式继承 `BaseTenantEntity`**（自动纳入租户隔离）。

---

## §2 数据模型

### §2.1 `Wf_FlowToken` 新表（`CP6.Entity/DomainModels/Wf/Wf_FlowToken.cs`，新建）

token = 流程实例内"一个活动执行点"。单路径审批 = 一实例恒一 Active token；并行分叉 = 一实例多 Active token。**仅 5 个业务字段，不加 `ForkSize`**（join 触发靠"按 `ForkId` 数 Active token == join 入边数"，结构化图中 split 出边数 == join 入边数，故无需显式记批量大小）。

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wf;

/// <summary>
/// 流程令牌（WFS P1 运行时内核）。一个 token = 实例内一个活动执行点（停留节点）。
/// 单路径审批：一实例恒一 Active token；并行分叉：一实例多 Active token 并存。
/// 血缘：ParentTokenId 串嵌套层级；ForkId 标同批分叉（parallelJoin 靠"同 ForkId 计数"认亲触发）。
/// "实例进行中" = 存在 Active token（取代旧"CurrentNode 单值"判定）。
/// </summary>
[Table("Wf_FlowToken")]
public class Wf_FlowToken : BaseTenantEntity
{
    /// <summary>所属流程实例 → Wf_FlowInstance.Id</summary>
    public Guid InstanceId { get; set; }

    /// <summary>当前停留节点 Id（= FlowNode.Id）。AdvanceToken 流转时改写。</summary>
    [MaxLength(100)]
    public string NodeId { get; set; } = string.Empty;

    /// <summary>令牌状态：0=Active(活动) 1=Consumed(已消费/正常退场) 2=Cancelled(取消/驳回连坐)。见 FlowTokenStatus。</summary>
    public int Status { get; set; }

    /// <summary>父令牌 Id（嵌套血缘）：分叉时子 token 指向被消费的入 token；汇聚续 token 上弹一层取入 token 之父。根 token=null。</summary>
    public Guid? ParentTokenId { get; set; }

    /// <summary>分叉批次 Id（同一次 parallelSplit 产出的子 token 共享同值）：parallelJoin 靠它认亲计数。根/线性 token=null。</summary>
    public Guid? ForkId { get; set; }
}
```

### §2.2 `FlowTokenStatus`（`CP6.Core/Services/Wf/WfStatus.cs`，加在 `FlowTaskStatus` 后，对齐家族）

```csharp
/// <summary>流程令牌状态（Wf_FlowToken.Status）。Active 才参与流转 / join 计数；非 Active = 重放守卫的 no-op 依据。</summary>
public static class FlowTokenStatus
{
    public const int Active = 0;      // 活动（停泊在 NodeId，等待流转 / join）
    public const int Consumed = 1;    // 已消费（正常退场：到 end、被 split 分叉、被 join 收编）
    public const int Cancelled = 2;   // 取消（驳回 terminate 连坐：全 Active token → Cancelled）
}
```

### §2.3 `Wf_FlowInstance` 加 `RowVersion` + `CurrentNode` 语义升级（`Wf_FlowInstance.cs`）

```csharp
/// <summary>乐观并发标记（WFS P1）：并行分支近同时办结时序列化，避免 join 计数脏读"双 1/2 丢失唤醒"卡死。
/// ActAsync / join 触发 / 超时硬动作在 SaveChanges 前对本实例做一次写触达 → 冲突方抛
/// DbUpdateConcurrencyException 由重试循环重读重算（§6）。</summary>
[Timestamp]
public byte[]? RowVersion { get; set; }
```

- **`CurrentNode` 保留不删**（兼容铁律）：含义升级为"代表节点"——单 token 时 = 该 token 的 NodeId（与今天一致）；多 token（并行中）时 = 末次流转触达的节点（仅供审批 UI 兜底显示，非状态真相）。**状态真相 = `Wf_FlowToken` 集合**。
- **"实例进行中"判定**：从"`Status == Running`（且隐含停在 `CurrentNode`）"细化为"`Status == Running` **且** 存在 `Status==Active` 的 token"。无 Active token 残留 = 已 drained（§4.3）。
- **`Status` 取值不变**（复用 `FlowInstanceStatus`）：`Running`/`Approved`/`Rejected`/`Withdrawn`/`Suspended`。

### §2.4 `Wf_FlowTask` 加 `TokenId`（`Wf_FlowTask.cs`）

```csharp
/// <summary>所属令牌 → Wf_FlowToken.Id（WFS P1）。会签计票按 (InstanceId, NodeId, TokenId) 隔离，
/// 令并行的两条分支即便停在同名节点也互不串台。可空：旧数据 / 回填前为 null（§8 回填补齐）。</summary>
public Guid? TokenId { get; set; }
```

- **可空设计**：旧库既有 task 行无 token（回填种子补齐，§8）；新建 task 必带 `TokenId`（approval handler 建任务时填，§3.3）。

### §2.5 `FlowSchema` 加并行网关节点类型（`FlowSchema.cs`，仅扩词汇表注释 + 不加字段）

`FlowNode.Type` 现注释"start / approval / end"（`:21-22`）→ 改为"start / approval / end / parallelSplit / parallelJoin"。**无新字段**（并行网关不需审批人/会签；条件 P1 忽略，§5）。

```csharp
/// <summary>节点类型：start / approval / end / parallelSplit / parallelJoin。
/// parallelSplit=并行分叉(一入边 N 出边，无条件全激活)；parallelJoin=并行汇聚(N 入边一出边，等齐同批分支放行)。</summary>
public string Type { get; set; } = "approval";
```

### §2.6 DbSet / 索引 / 迁移

- **DbSet**（`CP6Context.cs`，挨着 `:387` 末个 Wf DbSet 后加）：
  ```csharp
  public DbSet<Wf_FlowToken> Wf_FlowTokens { get; set; }
  ```
- **索引**（`CP6Context.cs:629-650` Wf 区，加）：
  ```csharp
  modelBuilder.Entity<Wf_FlowToken>(e =>
  {
      e.HasIndex(x => new { x.InstanceId, x.Status }).HasDatabaseName("IX_Wf_FlowToken_InstanceStatus");  // 列实例 Active token / drained 判定
      e.HasIndex(x => new { x.InstanceId, x.ForkId, x.NodeId }).HasDatabaseName("IX_Wf_FlowToken_Fork");   // parallelJoin 同 ForkId 计数
  });
  ```
- **EF 迁移**（`add-migration WfsRuntimeKernel`）净产物：①新表 `Wf_FlowToken`（6 业务列 + `BaseTenantEntity` 公共列 + 2 索引）②`Wf_FlowInstance` 加 `RowVersion`（`rowversion`/`timestamp`）③`Wf_FlowTask` 加 `TokenId`（`uniqueidentifier NULL`）。SqlServer `[Timestamp]` → `rowversion` 列（DB 自动维护）。
- **InMemory 降级**：InMemory provider 不支持 `rowversion` 并发语义（不抛 `DbUpdateConcurrencyException`）→ 并发测试用 SqlServer/SQLite provider 或单元路径直验重试逻辑（§10），InMemory 测仅验功能正确性（同 #4/#5 既有限制）。

---

## §3 `INodeHandler` 插件架构

### §3.1 接口 + `NodeContext`（`CP6.Core/Services/Wf/INodeHandler.cs`，新建，`internal`）

```csharp
namespace CP6.Core.Services.Wf;

/// <summary>节点处理器（WFS P1 插件架构）。每种 FlowNode.Type 一个实现，封装"token 进入该节点时做什么"。
/// EnterNodeAsync 退化为 _handlers[node.Type].OnEnterAsync(ctx)。新增节点类型(P2~P5)= 加 handler + DI 注册，引擎不动。</summary>
internal interface INodeHandler
{
    /// <summary>节点类型标识，与 FlowNode.Type 对应（start/approval/end/parallelSplit/parallelJoin）。</summary>
    string Type { get; }

    /// <summary>token 进入本节点时的行为：建待办 / 直穿 / 消费 / 分叉 / 汇聚。改库不落盘（调用方统一 SaveChanges）。</summary>
    Task OnEnterAsync(NodeContext ctx);
}

/// <summary>节点处理上下文（WFS P1）。操作对象从"实例"细化为"token"——一个实例可有多 token，
/// 每个 token 独立进节点。Engine 回指引擎以复用 token 原语 / 建待办 / AddHistory 等私有能力。</summary>
internal sealed class NodeContext
{
    public required Wf_FlowInstance Inst { get; init; }
    public required FlowSchema Schema { get; init; }
    public required FlowNode Node { get; init; }
    public required Wf_FlowToken Token { get; init; }   // 当前进入本节点的 token（操作主语）
    public required FlowEngine Engine { get; init; }    // 回指：复用 token 原语 + 私有成员（同 scoped DbContext）
}
```

> **可见性**：`INodeHandler`/`NodeContext`/5 handler/token 原语全 `internal`（对外只露 `IFlowEngine`，§9）。`FlowEngine` 的 token 原语 / 建待办 / `AddHistory` 等私有方法，handler 经 `ctx.Engine` 调用 → 用 `internal` 方法暴露给同程序集 handler（不破 `private` 对外封装）。测试程序集已有 `InternalsVisibleTo`（#4 字段审计已加 `CP6.Core.Tests`，复用）。

### §3.2 `EnterNodeAsync` 退化为多态分发（`FlowEngine.cs:136-183` 重写）

```csharp
private readonly IReadOnlyDictionary<string, INodeHandler> _handlers;   // ctor 注入，Type→handler

// EnterNodeAsync 退化：进节点 = 查 handler 分发。未知类型抛错（闭合失败不静默直穿）。
private async Task EnterNodeAsync(Wf_FlowInstance inst, FlowSchema schema, FlowNode node, Wf_FlowToken token)
{
    inst.CurrentNode = node.Id;   // 兼容：保留代表节点
    var type = (node.Type ?? "approval").Trim().ToLowerInvariant();
    if (!_handlers.TryGetValue(type, out var handler))
        throw new InvalidOperationException($"未知节点类型：{node.Type}（节点 {node.Id}）");
    await handler.OnEnterAsync(new NodeContext { Inst = inst, Schema = schema, Node = node, Token = token, Engine = this });
}
```

- **签名加 `token` 参**：所有 `EnterNodeAsync` 调用点（`SubmitAsync:51`、`NextNodeAsync:209`、`AdvancedFlow.SendBackAsync:101` 等）相应传当前 token（§7）。
- **未知类型抛错**：旧引擎 `else` 兜底当 approval（`:152`）；新引擎显式 5 类，未配类型 = schema 错误，**抛 `InvalidOperationException` 不静默**（防 BPM schema 笔误悄悄退化成审批节点）。

### §3.3 五个 `INodeHandler`（`CP6.Core/Services/Wf/NodeHandlers/`，新建目录）

每个 handler 是从今天 `EnterNodeAsync` if/else 链**原样搬出**的逻辑（start/approval/end）+ 两个新增（split/join）。**搬出 = 兼容载体，行为逐字等价旧代码**。

- **`StartNodeHandler`（Type="start"）**：复用 `:146-150`——沿单边推进。`await ctx.Engine.AdvanceToken(ctx.Token)`（token 不消费，原地流转到 start 的单出边，§4.2）。
- **`ApprovalNodeHandler`（Type="approval"）**：复用 `:152-182`——`BuildRule`→`ResolveAsync`→缺位 `Suspend`→作废上轮遗留→按审批人 `Distinct` 建 `Wf_FlowTask`。**唯一增量**：建任务时 `task.TokenId = ctx.Token.Id`（§2.4），令会签计票按 token 隔离。建完任务后 **token 停泊**（保持 Active，停在本节点，等人办理；不流转）。
- **`EndNodeHandler`（Type="end"）**：**改写** `:140-145`——不再"置 `inst.Status=Approved` + return"，而是 `ctx.Engine.ConsumeToken(ctx.Token)`（该 token 退场）+ `AddHistory("end")` + `ctx.Engine.FinishIfDrained(inst)`（§4.3：无 Active 残留才整体 Approved + dispatch）。**铁律：end 只消费当前 token，不直接判实例通过**——并行分支各自走到 end 时，只有最后一个令实例无 Active 残留者才触发终态。
- **`ParallelSplitNodeHandler`（Type="parallelSplit"）**：**新增**——并行分叉。`ctx.Engine.ConsumeToken(ctx.Token)`（入 token 退场）→ `var forkId = Guid.NewGuid()` → `foreach edge where From==node.Id`（**忽略 `edge.Condition`，无条件全激活**，§5）：`var child = ctx.Engine.SpawnToken(inst, target, parent: ctx.Token.Id, fork: forkId)`（子 token，`ParentTokenId`=入 token、`ForkId`=本批共享）→ `await EnterNodeAsync(inst, schema, target, child)`（子 token 进各分支首节点）。`AddHistory("parallelSplit")`。
- **`ParallelJoinNodeHandler`（Type="parallelJoin"）**：**新增**——并行汇聚。token 流转到 join 节点（NodeId 已被 `AdvanceToken` 设为 join，Active 停泊）后：`var inEdges = schema.Edges.Count(e => e.To == node.Id)`（join 入边数）→ `var arrived = db.Wf_FlowTokens.Count(t => t.InstanceId==inst.Id && t.NodeId==node.Id && t.ForkId==ctx.Token.ForkId && t.Status==Active)`（同 ForkId 已到 join 的 Active token 数，含自身）→ **`if (arrived < inEdges) return`**（未齐，停泊等其余分支，幂等闸=计数本身，§6）→ **齐了：触发汇聚**：消费这批 fork token（全置 Consumed）→ 取血缘上一层（`var entry = db.Wf_FlowTokens.First(...同批任一)`；`parentTok = entry.ParentTokenId 对应的 token`）→ `var cont = ctx.Engine.SpawnToken(inst, node, parent: parentTok?.ParentTokenId, fork: parentTok?.ForkId)`（续 token，**血缘上弹一层**：取入 token 之父的父 / 之父的 fork → 嵌套并行天然复原外层 ForkId）→ `await ctx.Engine.AdvanceToken(cont)`（续 token 沿 join 单出边继续）。`AddHistory("parallelJoin")`。

### §3.4 DI 注册（`Program.cs`，`:105` 附近）

```csharp
// WFS P1：节点处理器（按 Type 多态分发）。新增节点类型(P2~P5)= 加一个 handler 注册，引擎不动。
builder.Services.AddScoped<INodeHandler, StartNodeHandler>();
builder.Services.AddScoped<INodeHandler, ApprovalNodeHandler>();
builder.Services.AddScoped<INodeHandler, EndNodeHandler>();
builder.Services.AddScoped<INodeHandler, ParallelSplitNodeHandler>();
builder.Services.AddScoped<INodeHandler, ParallelJoinNodeHandler>();
```

- **`FlowEngine` ctor 接 `IEnumerable<INodeHandler>`**，构造时 `_handlers = handlers.ToDictionary(h => h.Type, StringComparer.OrdinalIgnoreCase)`。**ctor 须保持现有可选参兼容**（`:21-27` 现 `IWfNotifier? notifier=null, ApprovalDispatcher? dispatcher=null` 供单测裸 `new`）→ handler 集合也给一个"无注入时自建默认 5 handler"的回退（单测 `new FlowEngine(db, approver)` 仍可用）：
  ```csharp
  public FlowEngine(CP6Context db, IApproverResolver approver, IWfNotifier? notifier = null,
                    ApprovalDispatcher? dispatcher = null, IEnumerable<INodeHandler>? handlers = null)
  {
      _db = db; _approver = approver;
      _notifier = notifier ?? new NullWfNotifier();
      _dispatcher = dispatcher ?? new ApprovalDispatcher(Array.Empty<IApprovalCallback>());
      _handlers = (handlers ?? DefaultHandlers()).ToDictionary(h => h.Type, StringComparer.OrdinalIgnoreCase);
  }
  // DefaultHandlers() = new INodeHandler[]{ new StartNodeHandler(), new ApprovalNodeHandler(), new EndNodeHandler(),
  //                                         new ParallelSplitNodeHandler(), new ParallelJoinNodeHandler() };
  // handler 无状态(逻辑全在 ctx.Engine / ctx.* 上)，故可 new 无参；若某 handler 需依赖，改 DI 必经路径。
  ```

---

## §4 token 生命周期：引擎三原语（`FlowEngine.cs`，新增 `internal` 方法）

### §4.1 `SpawnToken` — 生

```csharp
/// <summary>生一个 Active token 停在 node。parent/fork 串血缘（根 token 皆 null）。不落盘（调用方统一 SaveChanges）。</summary>
internal Wf_FlowToken SpawnToken(Wf_FlowInstance inst, FlowNode node, Guid? parent = null, Guid? fork = null)
{
    var tok = new Wf_FlowToken
    {
        Id = Guid.NewGuid(), InstanceId = inst.Id, NodeId = node.Id,
        Status = FlowTokenStatus.Active, ParentTokenId = parent, ForkId = fork,
        Creator = inst.StarterId.ToString(),
    };
    _db.Wf_FlowTokens.Add(tok);   // TenantId 由 StampTenant 自动盖（BaseTenantEntity）
    return tok;
}
```

### §4.2 `AdvanceToken` — 排他流转（**复用 `NextNodeAsync` 取首真边**，单 token 线性 = 零差异）

```csharp
/// <summary>token 排他流转：沿出边取首个条件为真者，改 token.NodeId 后进新节点。不消费（同一 token 移动）。
/// 无匹配出边 → 该 token 消费 + 实例 drained 判定（兜底结束，等价旧 NextNodeAsync:211-212）。</summary>
internal async Task AdvanceToken(Wf_FlowToken token)
{
    var schema = await LoadSchemaAsync(/*inst.FlowKey*/);   // 调用方持 inst/schema 时直接传，省一次反序列化
    var node = FindNode(schema, token.NodeId);
    foreach (var edge in schema.Edges.Where(e => e.From == token.NodeId))
    {
        if (!ExpressionEvaluator.Evaluate(edge.Condition, inst.VarsJson)) continue;
        var target = FindNode(schema, edge.To);
        if (target is not null) { token.NodeId = target.Id; await EnterNodeAsync(inst, schema, target, token); return; }
    }
    // 无后继：消费该 token + drained 判定（替代旧"inst.Status=Approved"直接终态）
    ConsumeToken(token);
    AddHistory(inst.Id, token.NodeId, inst.StarterId, "end", "无后继节点，自动结束");
    FinishIfDrained(inst);
}
```

- **签名实参**：实际实现把 `inst`/`schema` 作参传入（避免重载 `LoadSchemaAsync`），上文 `/*…*/` 仅示意。
- **单 token 线性 = 零差异**：一条审批链上，approval 节点办结 → `AdvanceToken` 把同一 token 移到下一节点 → 行为逐字等价旧 `NextNodeAsync(inst, schema, node)`（只是状态载体从 `CurrentNode` 变 token.NodeId）。**这是 631 测试照绿的关键**。

### §4.3 `ConsumeToken` + `FinishIfDrained` — 灭 + 终态判定

```csharp
/// <summary>消费 token（正常退场：到 end / 被 split 分叉 / 被 join 收编）。带 Active 守卫 → 重放 no-op（§6）。</summary>
internal void ConsumeToken(Wf_FlowToken token)
{
    if (token.Status != FlowTokenStatus.Active) return;   // 重放守卫
    token.Status = FlowTokenStatus.Consumed;
}

/// <summary>驳回连坐：全 Active token → Cancelled（terminate 语义，§7.2）。</summary>
internal void CancelAllActiveTokens(Guid instanceId)
{
    foreach (var t in _db.Wf_FlowTokens.Local.Where(t => t.InstanceId == instanceId && t.Status == FlowTokenStatus.Active))
        t.Status = FlowTokenStatus.Cancelled;
    // 注：须并查 DB + Local（identity-map），下文 §6 用单查 ToList 后改态；此处示意。
}

/// <summary>无 Active token 残留 ⇒ 实例正常通过：置 Approved + 终态分发（须在最终 SaveChanges 前，原子铁律）。</summary>
internal void FinishIfDrained(Wf_FlowInstance inst)
{
    var anyActive = _db.Wf_FlowTokens.Any(t => t.InstanceId == inst.Id && t.Status == FlowTokenStatus.Active);
    if (anyActive) return;                          // 还有分支在跑，不终态
    if (inst.Status != FlowInstanceStatus.Running) return;   // 已被驳回/撤回，不覆盖
    inst.Status = FlowInstanceStatus.Approved;
    // DispatchIfFinishedAsync 由调用方(ActAsync/Submit)在最终 SaveChanges 前调用（沿用 :101 铁律）
}
```

> **drained 判定须并查 DB + EF Local（identity-map）**：刚改态的 token 在 `_db.Wf_FlowTokens.Local`，未存的修改靠 identity-map 反映（同 `ActAsync:77-79` 取 `nodeTasks` 含刚改任务的手法）。实现用"先 `ToList` 本实例全 token 到内存、在内存里数 Active"避免 DB 未提交不可见。

### §4.4 分叉 / 汇聚血缘（§3.3 split/join 已述，此处补"嵌套天然成立"证明）

- **提交起根 token**：`SubmitAsync` 建实例后 `SpawnToken(inst, first, parent: null, fork: null)`（根 token），再 `EnterNodeAsync(inst, schema, first, rootToken)`（§7.1）。
- **一层并行**：split 消费根 token、spawn 2 子 token（`ForkId=F1`、`Parent=根`）。两分支各跑，到 join。join 数 `ForkId==F1 ∧ NodeId==join` 的 Active == 2 → 触发：消费这 2 个、续 token 取"入 token 之父（=根）的父（null）/ fork（null）"→ 续 token `Parent=null, Fork=null`（回到根级线性）。
- **嵌套并行**：外 split 产 `F1` 子 token A、B；A 分支内又有 split 产 `F2` 子 token A1、A2（`Parent=A, Fork=F2`）。内 join 数 `F2` == 2 → 续 token 取"A 之父（=外 split 消费的根/上层 token）的 fork = F1"→ **续 token 回到 `Fork=F1`**，与 B 并列等外 join。外 join 数 `F1`（续 token + B）== 2 → 收编。**血缘上弹一层 = 嵌套自动复原外层批次**，无需显式栈。

---

## §5 并行网关 DSL + 结构化约束

- **节点形态**：`parallelSplit` = 1 入边 / N 出边；`parallelJoin` = N 入边 / 1 出边。N≥2（N=1 退化为直连，schema 校验可警告，非硬错）。
- **split 出边无条件全激活**：P1 **忽略 split 出边的 `Condition` 字段**（即便配了也不求值）。理由：选择性激活 = inclusive 网关语义，YAGNI 不做（§0）。文档 + schema 注释须标明"parallelSplit 出边 Condition 在 P1 被忽略"。
- **结构化约束（写进 spec，落码做轻校验 + 文档强约定）**：
  1. **split 出边数 == 配对 join 入边数**（token 守恒：N 出 → N 到 join → 收编为 1）。
  2. **每分支必达 join**：分支内**不得提前 `end`、不得条件流转裁掉分支**（否则该分支 token 提前消费/走失 → join 永远数不齐 → 死锁）。
  3. **split/join 配对、可嵌套但不交叉**（结构化块嵌套，非任意图）：内层 split 必在同层 join 前闭合。靠 `ForkId`/`ParentTokenId` 血缘保证（§4.4），无需图静态分析。
- **校验落点（P1 轻量）**：运行时 join handler 若发现 `arrived > inEdges`（多于入边数到达，意味非结构化图）→ 记 `AddHistory` 告警但仍按 `arrived >= inEdges` 触发（防卡死优先）。**P5 设计器期再做图静态校验**（结构化块检查）；P1 靠"种子流程结构化 + 文档约定 + 测试覆盖"。

---

## §6 并发 / 幂等

**险情**：两并行分支近同时办结 → join 计数脏读 → "双 1/2 丢失唤醒"卡死（A 读到只有自己 count=1 停泊、B 同样 count=1 停泊，谁都不触发）。

**解法（三层）：**

1. **乐观并发锁（`Wf_FlowInstance.RowVersion`，§2.3）**：`ActAsync` / join 触发 / 超时硬动作在**最终 `SaveChanges` 前对 `inst` 做一次写触达**（`inst.ModifyDate = DateTime.Now` 或 `_db.Entry(inst).Property(x => x.ModifyDate).IsModified = true`），令任何 token 变更都伴随 inst 行 UPDATE → 带 `WHERE RowVersion=@orig`。两分支并发办结 → 一方先提交（RowVersion 递增），另一方 `SaveChanges` 抛 `DbUpdateConcurrencyException`。**关键：必须触达 inst 行，否则只改 token 行不会触发并发校验**（这是序列化两分支的支点）。
2. **重试循环（`ActAsync` 及超时硬动作路径）**：
   ```csharp
   public async Task ActAsync(Guid taskId, Guid actorId, bool approve, string? comment = null)
   {
       for (int attempt = 0; ; attempt++)
       {
           try { await ActOnceAsync(taskId, actorId, approve, comment); return; }
           catch (DbUpdateConcurrencyException) when (attempt < 2)   // 重读重算 ×3（attempt 0/1/2）
           {
               foreach (var e in _db.ChangeTracker.Entries().ToList()) await e.ReloadAsync();   // 丢脏、重读
           }
       }
   }
   ```
   `ActOnceAsync` = 把今天 `ActAsync:57-103` 主体 token 化（§7.2）。败方重读后**重算 join 计数**——此时已能看到对方提交的 token 态 → 正确触发 join（消除丢失唤醒）。
3. **join 幂等闸 = Active fork token 计数本身**：触发即把这批 fork token 置 Consumed；任何重入（重放 / 重试 / 超时再扫）再数同 ForkId Active = 0（或 < 入边数）→ **不二次触发**。无需额外"已触发"标记。
4. **token 原语守卫**：`ConsumeToken` 带 `Status==Active` 检查（§4.3）→ 重放 no-op；`CancelAllActiveTokens` 同理只动 Active。
5. **原子性**：一次 `ActAsync` 内所有 token 变更 + 业务回调（`DispatchIfFinishedAsync`）+ inst 触达**共享一 scoped DbContext、一次 `SaveChanges`**（沿用 OA2-D5 铁律，§1）。回调抛异常 → 全回滚。
6. **超时服务对齐**：`WfTimeoutService` 硬动作 `approve`/`reject` 走 `_engine.ActAsync`（已含重试，§1）→ 天然安全；软动作 `remind`/`escalate` 只改 task 行、不触达 inst RowVersion → 与人工 `ActAsync` 无并发冲突。**铁律：超时硬动作绝不绕过 `ActAsync` 裸改 token**（否则与人工赛跑无序列化）。
7. **InMemory 限制**：InMemory provider 不触发 `DbUpdateConcurrencyException`（§2.6）→ 并发卡死场景须 SqlServer/SQLite provider 测，或单元直验"重读后重算 join 计数能触发"（§10）。

---

## §7 `ActAsync` / `SubmitAsync` / 退回加签 token 化改造

### §7.1 `SubmitAsync`（`FlowEngine.cs:29-55`）

- 建实例后（`:48`）**加 `SpawnToken(inst, first, null, null)`** 得根 token → `EnterNodeAsync(inst, schema, first, rootToken)`（传根 token）→ `DispatchIfFinishedAsync`（极少数"起即终态"如 start→end，end handler 已 `FinishIfDrained` 置 Approved）→ `SaveChanges`。**对外行为不变**（返回 `inst.Id`）。

### §7.2 `ActAsync`（拆 `ActOnceAsync` + token 化，`FlowEngine.cs:57-103`）

- 幂等闸（`:61`）/ 实例运行态闸（`:64`）/ 改任务态 + 历史（`:66-70`）/ 前加签激活（`:73-74`）**不变**。
- **会签计票口径细化**（`:77-82`）：`Where(t => t.InstanceId==inst.Id && t.NodeId==task.NodeId && t.TokenId==task.TokenId && t.Status != Cancelled)`——**加 `TokenId` 维度**，令并行两分支停在同名节点时各自独立计票（嵌套/并行会签隔离）。`EvaluateNodeCounts` 函数本身不改。
- **流转改 token 推进**（`:91-96`）：节点通过后，不再 `NextNodeAsync(inst, schema, node)`，而是 **取该 task 的 token（`var tok = _db.Wf_FlowTokens.First(t => t.Id == task.TokenId)`）→ `await AdvanceToken(tok)`**（沿该分支出边走；若到 join 则 join handler 判等待/触发）。
- **驳回 = terminate**（`:99`）：不止 `inst.Status=Rejected`，**还须 `CancelAllActiveTokens(inst.Id)`**（全 Active token → Cancelled：本分支 + 所有兄弟并行分支连坐取消）→ 兄弟分支的在途待办亦 `CancelPendingTasks`（按 inst 全在途）→ `DispatchIfFinishedAsync`（`OnRejected` 回调**恰一次**）。**铁律：任一 approval 驳回即整实例 terminate**（P1 不做"单分支驳回不影响兄弟"的细粒度，YAGNI）。
- **inst 写触达**（§6）：最终 `SaveChanges` 前 `inst.ModifyDate = DateTime.Now`，令 RowVersion 参与并发。

### §7.3 退回 / 加签（`AdvancedFlow.cs`）

- **`SendBackAsync`（`:77-103`）**：作废全实例在途待办（`:93-97`）后，**亦须把全 Active token → Cancelled**，再 `SpawnToken(inst, target, null, null)` 新根 token + `EnterNodeAsync(inst, schema, target, newToken)`（退回 = 回到单 token 线性，并行分支一并清掉）。**P1 约定：退回只发生在审批节点、不跨并行块退回**（跨并行块退回语义复杂，YAGNI；文档标明）。
- **`AddSignAsync`（`:16-52`）**：加签任务 `addTask.TokenId = task.TokenId`（继承原任务 token，计入同 token 会签）。其余不变。

---

## §8 兼容 / 迁移

### §8.1 旧 schema 照跑（兼容硬验收闸）

- start/approval/end handler = 从今天 `EnterNodeAsync` if/else 链**原样搬出**（§3.3）。旧 `FlowSchema`（仅这 3 类节点 + 排他边 + 会签）经 handler 分发后行为**逐字等价**旧引擎。**631 Wf 审批测试不改一行照绿** = P1 落码的兼容验收闸（任一测试需改即视为兼容破坏，须回退排查）。
- 单 token 线性流程：Submit 起一根 token，沿 approval→approval→end 移动同一 token，end 消费 + drained → Approved。与旧"CurrentNode 单值推进 + end 置 Approved"行为等价。

### §8.2 在途实例回填（关键，幂等 seeding）

现存 Running / Suspended 实例**无 token 行**（迁移前建的）→ 若不回填，`AdvanceToken`/`FinishIfDrained` 找不到 token 会误判。**回填种子**（`Program.cs:578-595` 种子块内、`Migrate()` 后，仿 #5 引导种子幂等范式）：

```csharp
// WFS P1：在途实例 token 回填（幂等，守卫"无 token 才建"）。终态实例跳过。
await CP6.WebApi.Seed.WfTokenBackfillSeed.EnsureAsync(db);
```

`WfTokenBackfillSeed.EnsureAsync` 逻辑：
1. 查 `Wf_FlowInstances.Where(Status == Running || Status == Suspended)`（终态 Approved/Rejected/Withdrawn 跳过）。
2. 对每个实例：`if (!db.Wf_FlowTokens.Any(t => t.InstanceId == inst.Id))`（**守卫：无 token 才建**，幂等）→ `SpawnToken(inst, CurrentNode 对应节点, parent:null, fork:null)`（建一个 Active 根 token，NodeId = `inst.CurrentNode`）。
3. 给该实例 `Pending`/`Suspended` 状态的 `Wf_FlowTask`（`TokenId == null` 的）补 `task.TokenId = 该根 token.Id`。
4. `SaveChanges`。**每次启动跑、幂等**（已回填的有 token → 守卫跳过；新近建的在途实例下次启动补）。

> **限制**：回填假定在途实例皆单活动节点（迁移前引擎本就单 token），故每实例一根 token 正确。迁移后新建的并行实例自带多 token，不经回填。

### §8.3 新列默认值 / 迁移安全

- `Wf_FlowTask.TokenId` 可空 → 旧行 null，回填补齐，新行必填。
- `Wf_FlowInstance.RowVersion` = `rowversion`（DB 自动维护，旧行迁移时自动获初值）。
- 新表 `Wf_FlowToken` 空表起步 + 回填填充。
- 迁移 `Up` 仅 `CreateTable(Wf_FlowToken)` + `AddColumn ×2` + `CreateIndex ×2`，无数据迁移（数据靠启动种子回填，可重入）。

---

## §9 API / 命名 / 读侧

- **对外 `IFlowEngine` 签名不动**（`IFlowEngine.cs:7-34`：`SubmitAsync`/`ActAsync`/`SendBackAsync`/`AddSignAsync`/`SetDelegateAsync` 5 方法原样）。**审批调用方（`ApprovalService`/控制器/前端）零感知**，BPM 能力靠更丰富的 `SchemaJson`（含 parallelSplit/Join 节点）浮现。
- **原地演进 `FlowEngine`，不立 `IWorkflowEngine` 门面**（YAGNI；P2 服务任务若需新对外 API 再评估）。
- **新增内部类型**（全 `internal`，对外封装不变）：`INodeHandler` / `NodeContext` / 5 handler / 3 token 原语（`SpawnToken`/`AdvanceToken`/`ConsumeToken`）+ `FinishIfDrained` / `CancelAllActiveTokens`。
- **`FlowTokenStatus`**（§2.2）对齐 `FlowInstanceStatus`/`FlowTaskStatus` 家族（`static class const int`）。
- **读侧 `GetActiveNodesAsync`（可选，P1 不强求）**：若审批 UI / 调试需列实例当前所有活动节点集，加 `Task<IReadOnlyList<string>> GetActiveNodesAsync(Guid instanceId)`（查 Active token 的 NodeId 集）。**P1 审批 UI 仍用 `CurrentNode` 单值兜底显示**（并行流程 UI 留 P5 设计器期完善），故此方法 P1 可选、不阻断。

---

## §10 测试

**回归闸**：631 Wf 审批测试 **不改一行照绿**（§8.1，兼容硬验收）。

**新增单测（`CP6.Core.Tests`，token 化运行时）：**

- **token 三原语**：`SpawnToken` 建 Active token 串血缘；`AdvanceToken` 单 token 线性 = 行为等价旧 NextNode；`ConsumeToken` Active 守卫（重放 no-op）；`FinishIfDrained` 仅无 Active 残留才 Approved + dispatch 一次。
- **并行 happy path**：split → 双 approval 分支 → join → end。验证：①split 后 2 个 Active token（同 ForkId）②两分支皆批才完成（先批一个：join 停泊不放行；后批：触发）③join 后单续 token（血缘回根）④`OnApproved` 回调**恰一次** ⑤inst 最终 Approved、无 Active 残留。
- **次序无关**：分支 A 先办 vs B 先办，结果一致（join 计数对称）。
- **join 等待**：仅一分支办结时 join 不触发、实例仍 Running、另一分支待办仍在。
- **驳回 terminate**：并行中任一分支 approval 驳回 → 全 Active token → Cancelled（兄弟分支连坐）+ 兄弟在途待办作废 + inst Rejected + `OnRejected` **恰一次**（§7.2）。
- **嵌套分叉**：外 split 内含内 split（A 分支再分叉）。验证内 join 续 token 复原外层 `ForkId`（§4.4），外 join 正确收编、最终单实例 Approved。
- **并行分支内会签隔离**：两分支停同名节点（或各自会签节点）→ 计票按 `(NodeId, TokenId)` 隔离，互不串台（§7.2）。
- **并发卡死防护**（SqlServer/SQLite provider 或单元路径）：模拟两分支近同时办结 → 一方 `DbUpdateConcurrencyException` → 重读重算 → join 正确触发（无丢失唤醒，§6）。InMemory 退化为"重读后重算 join 计数能触发"的逻辑直验。
- **回填种子**：造一个无 token 的在途实例 → `WfTokenBackfillSeed.EnsureAsync` 建根 token + 补 task.TokenId；重跑幂等不重复建（§8.2）。
- **未知节点类型**：schema 含 `Type="foo"` → `EnterNodeAsync` 抛 `InvalidOperationException`（§3.2）。

**gstack 真浏览器 QA（§feedback_coding_skills）**：种一个并行审批流程（split → 2 审批节点 → join → end）`SchemaJson` + 种子数据 → 真浏览器走待办中心：发起 → 两审批人分别在待办看到各自分支任务 → 各自同意 → join 收齐 → 实例通过、终态回调落地。验证旧审批流程（线性）UI 照常。固化 `docs/superpowers/qa/wfs-runtime/`。

**基线**：后端 1189 测 / 1 skip → +N 全绿；前端 P1 不动（type-check/vitest29/build 照绿，无前端改动）。

---

## §11 延后 / YAGNI（P2~P5 与 hardening）

| 项 | 归属 | 备注 |
|---|---|---|
| 服务任务 / 自动节点（`IServiceTaskHandler`） | P2 | 泛化 `IApprovalCallback`；加 `serviceTask` handler |
| 定时 / 事件触发（timer-start、message、IntegrationEvent 边界事件） | P3 | 复用 `WfTimeoutService` + BridgeHook / IntegrationEvent |
| 子流程 / 调用活动（call-activity） | P4 | 父 token 挂起、子实例独立、回填父 token |
| BPM 设计器（网关/并行图元、结构化图静态校验） | P5 | `FlowDesigner.vue` 扩画布；图块结构化检查 |
| inclusive 包容网关（选择性激活 + 已激活分支 join） | hardening | 留 `INodeHandler` 接口位（`inclusiveSplit`/`inclusiveJoin`） |
| 单分支驳回不连坐兄弟（细粒度 terminate） | hardening | P1 = 任一驳回即整实例 terminate |
| 跨并行块退回 / 边界补偿 / 多实例标记（multi-instance） | hardening | P1 退回只在审批节点、不跨并行块 |
| 并行流程审批 UI（多活动节点可视化） | P5 | P1 用 `CurrentNode` 单值兜底；`GetActiveNodesAsync` 可选 |
| `IWorkflowEngine` 新门面 | 视 P2 需要 | P1 原地演进 `FlowEngine`，不过早抽象 |

---

## §12 决策 / 锚点汇总

| # | 决策 | 落点 |
|---|---|---|
| D1 | token 多活动节点取代单 `CurrentNode` | 新表 `Wf_FlowToken`（§2.1）；`CurrentNode` 保留做代表节点兼容（§2.3） |
| D2 | `Wf_FlowToken` 仅 5 业务字段（不加 `ForkSize`） | join 触发靠"同 ForkId Active 数 == join 入边数"（§3.3 / §5） |
| D3 | `INodeHandler` 按 `Type` 多态分发，未知类型抛错 | `EnterNodeAsync` 退化（§3.2）；5 handler（§3.3）；DI（§3.4） |
| D4 | 三 token 原语 spawn / advance / consume + `FinishIfDrained` | §4；`AdvanceToken` 复用 `NextNodeAsync` 取首真边（单 token 零差异） |
| D5 | 并行网关 = 显式 `parallelSplit`/`parallelJoin`；split 忽略出边 Condition | §2.5 / §5；inclusive 不做（YAGNI，留接口位） |
| D6 | join 靠 `ForkId` 认亲、血缘上弹一层 → 嵌套天然成立 | §4.4；§3.3 join handler |
| D7 | 乐观并发 `RowVersion` + `ActAsync` 重试 ×3；join 幂等闸 = 计数本身 | §2.3 / §6；超时硬动作走 `ActAsync` 同路径 |
| D8 | 驳回 = terminate（全 Active token → Cancelled + `OnRejected`×1） | §7.2 |
| D9 | 631 测试不改照绿（兼容硬闸）；在途实例幂等回填 token | §8.1 / §8.2 |
| D10 | 原地演进 `FlowEngine`，`IFlowEngine` 签名不动，不立新门面 | §9；新增类型全 `internal` |

**实读核验文件（本会话 2026-06-26）**：`FlowEngine.cs`（`ActAsync:57-103`/`EnterNodeAsync:136-183`/`NextNodeAsync:203-213`/`EvaluateNodeCounts:118-133`/`DispatchIfFinishedAsync:109-115`/`SubmitAsync:29-55`）、`AdvancedFlow.cs`（`SendBackAsync:77-103`/`AddSignAsync:16-52`）、`FlowSchema.cs`（`FlowNode.Type:21-22`/`FlowEdge:48-55`）、`WfStatus.cs`（`FlowInstanceStatus`/`FlowTaskStatus`）、`IApprovalCallback.cs:13-48`/`ApprovalDispatcher.cs:23-42`、`WfTimeoutService.cs:42-101`、`Wf_FlowInstance.cs:27/30`/`Wf_FlowTask.cs`、`BaseEntity.cs`/`BaseTenantEntity.cs`、`CP6Context.cs:373-387/629-650`、`Program.cs:105/584/578-595`。
