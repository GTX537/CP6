### Task A-T3: InclusiveSplit/InclusiveJoin 两 handler + 注册（第 7/8 个）

> 依赖 A-T2（join 放行走 GatewayJoinHelper）。

**Files:**
- Create: `CP6.Core/Services/Wf/NodeHandlers/InclusiveSplitNodeHandler.cs`
- Create: `CP6.Core/Services/Wf/NodeHandlers/InclusiveJoinNodeHandler.cs`
- Modify: `CP6.Core/Services/Wf/FlowEngine.cs`（`DefaultHandlers()` :38-43 加 2 项，注释「五 handler」改「八 handler」）
- Modify: `CP6.WebApi/Program.cs`（:113 `ParallelJoinNodeHandler` 注册行后加 2 行 DI）
- Test: `CP6.Tests/Wf/InclusiveGatewayTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
// CP6.Tests/Wf/InclusiveGatewayTests.cs
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Wf;

/// <summary>inclusive 网关行为（hardening spec §3.1/§3.2/§9）。构造模式沿 ParallelGatewayTests。</summary>
public class InclusiveGatewayTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static FlowEngine Engine(CP6Context db) => new(db, new ApproverResolver(db));

    // s → isplit → ( a["goA > 0"], b["goB > 0"], c["goC > 0"], d[default 无条件] ) → ijoin → end
    private static FlowSchema IncSchema(Guid ua, Guid ub, Guid uc, Guid ud) => new()
    {
        Start = "s",
        Nodes =
        {
            new FlowNode { Id = "s", Type = "start" },
            new FlowNode { Id = "isplit", Type = "inclusiveSplit" },
            new FlowNode { Id = "a", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = ua },
            new FlowNode { Id = "b", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = ub },
            new FlowNode { Id = "c", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = uc },
            new FlowNode { Id = "d", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = ud },
            new FlowNode { Id = "ijoin", Type = "inclusiveJoin" },
            new FlowNode { Id = "end", Type = "end" },
        },
        Edges =
        {
            new FlowEdge { From = "s", To = "isplit" },
            new FlowEdge { From = "isplit", To = "a", Condition = "goA > 0" },
            new FlowEdge { From = "isplit", To = "b", Condition = "goB > 0" },
            new FlowEdge { From = "isplit", To = "c", Condition = "goC > 0" },
            new FlowEdge { From = "isplit", To = "d" },                          // default 兜底边
            new FlowEdge { From = "a", To = "ijoin" }, new FlowEdge { From = "b", To = "ijoin" },
            new FlowEdge { From = "c", To = "ijoin" }, new FlowEdge { From = "d", To = "ijoin" },
            new FlowEdge { From = "ijoin", To = "end" },
        },
    };

    private static async Task<Guid> SeedAndSubmitAsync(CP6Context db, Guid ua, Guid ub, Guid uc, Guid ud, string vars)
    {
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "inc", FlowName = "inc", FormKey = "f",
            SchemaJson = JsonSerializer.Serialize(IncSchema(ua, ub, uc, ud)), Version = 1, Enable = true });
        await db.SaveChangesAsync();
        return await Engine(db).SubmitAsync("inc", Guid.NewGuid(), vars);
    }

    [Fact]
    public async Task TwoOfThreeTrue_SpawnsOnlyTrueBranches_DefaultNotWalked()
    {
        using var db = NewDb();
        Guid ua = Guid.NewGuid(), ub = Guid.NewGuid(), uc = Guid.NewGuid(), ud = Guid.NewGuid();
        await SeedAndSubmitAsync(db, ua, ub, uc, ud, "{\"goA\":1,\"goB\":1,\"goC\":0}");

        Assert.Equal(2, await db.Wf_FlowTokens.CountAsync(t => t.Status == FlowTokenStatus.Active));
        Assert.True(await db.Wf_FlowTasks.AnyAsync(t => t.AssigneeId == ua && t.Status == FlowTaskStatus.Pending));
        Assert.True(await db.Wf_FlowTasks.AnyAsync(t => t.AssigneeId == ub && t.Status == FlowTaskStatus.Pending));
        Assert.False(await db.Wf_FlowTasks.AnyAsync(t => t.AssigneeId == uc));   // 假边不走
        Assert.False(await db.Wf_FlowTasks.AnyAsync(t => t.AssigneeId == ud));   // ★ 有真边时 default 不走
        Assert.Equal(1, await db.Wf_FlowHistories.CountAsync(h => h.Action == "inclusiveSplit"));

        // 只等实际激活的两支：a、b 都过 → 放行到 end
        var ta = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ua && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(ta.Id, ua, approve: true);
        Assert.Equal(FlowInstanceStatus.Running, (await db.Wf_FlowInstances.SingleAsync()).Status);
        var tb = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ub && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(tb.Id, ub, approve: true);
        Assert.Equal(FlowInstanceStatus.Approved, (await db.Wf_FlowInstances.SingleAsync()).Status);
    }

    [Fact]
    public async Task AllConditionsTrue_AllThreeWalk_DefaultStillNotWalked()
    {
        using var db = NewDb();
        Guid ua = Guid.NewGuid(), ub = Guid.NewGuid(), uc = Guid.NewGuid(), ud = Guid.NewGuid();
        await SeedAndSubmitAsync(db, ua, ub, uc, ud, "{\"goA\":1,\"goB\":1,\"goC\":1}");

        Assert.Equal(3, await db.Wf_FlowTokens.CountAsync(t => t.Status == FlowTokenStatus.Active));
        Assert.False(await db.Wf_FlowTasks.AnyAsync(t => t.AssigneeId == ud));   // ★ 全真条件边时 default 不走
    }

    [Fact]
    public async Task AllFalse_OnlyDefaultWalks_SingleBranchJoinReleases()
    {
        using var db = NewDb();
        Guid ua = Guid.NewGuid(), ub = Guid.NewGuid(), uc = Guid.NewGuid(), ud = Guid.NewGuid();
        await SeedAndSubmitAsync(db, ua, ub, uc, ud, "{\"goA\":0,\"goB\":0,\"goC\":0}");

        Assert.Equal(1, await db.Wf_FlowTokens.CountAsync(t => t.Status == FlowTokenStatus.Active));
        var td = await db.Wf_FlowTasks.SingleAsync(t => t.Status == FlowTaskStatus.Pending);
        Assert.Equal(ud, td.AssigneeId);                              // ★ 全假仅 default 兜底

        await Engine(db).ActAsync(td.Id, ud, approve: true);          // 单支到场即齐（活支==1）
        Assert.Equal(FlowInstanceStatus.Approved, (await db.Wf_FlowInstances.SingleAsync()).Status);
    }

    // 嵌套：s → psplit → ( isplit⊂parallel 支 , p2 支 ) → pjoin → end；inclusive 内嵌 a/b + default d
    [Fact]
    public async Task InclusiveInsideParallel_OuterJoinWaitsInclusiveSubtree()
    {
        using var db = NewDb();
        Guid ua = Guid.NewGuid(), ud = Guid.NewGuid(), up = Guid.NewGuid();
        var schema = new FlowSchema
        {
            Start = "s",
            Nodes =
            {
                new FlowNode { Id = "s", Type = "start" },
                new FlowNode { Id = "psplit", Type = "parallelSplit" },
                new FlowNode { Id = "isplit", Type = "inclusiveSplit" },
                new FlowNode { Id = "a", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = ua },
                new FlowNode { Id = "d", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = ud },
                new FlowNode { Id = "ijoin", Type = "inclusiveJoin" },
                new FlowNode { Id = "p2", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = up },
                new FlowNode { Id = "pjoin", Type = "parallelJoin" },
                new FlowNode { Id = "end", Type = "end" },
            },
            Edges =
            {
                new FlowEdge { From = "s", To = "psplit" },
                new FlowEdge { From = "psplit", To = "isplit" }, new FlowEdge { From = "psplit", To = "p2" },
                new FlowEdge { From = "isplit", To = "a", Condition = "goA > 0" },
                new FlowEdge { From = "isplit", To = "d" },
                new FlowEdge { From = "a", To = "ijoin" }, new FlowEdge { From = "d", To = "ijoin" },
                new FlowEdge { From = "ijoin", To = "pjoin" },
                new FlowEdge { From = "p2", To = "pjoin" },
                new FlowEdge { From = "pjoin", To = "end" },
            },
        };
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "mix", FlowName = "mix", FormKey = "f",
            SchemaJson = JsonSerializer.Serialize(schema), Version = 1, Enable = true });
        await db.SaveChangesAsync();
        await Engine(db).SubmitAsync("mix", Guid.NewGuid(), "{\"goA\":1}");

        // p2 先过 → 外层 pjoin 必须等 inclusive 子树（血缘感知）
        var tp = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == up && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(tp.Id, up, approve: true);
        Assert.Equal(FlowInstanceStatus.Running, (await db.Wf_FlowInstances.SingleAsync()).Status);

        var ta = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ua && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(ta.Id, ua, approve: true);   // inclusive 活支只有 a → ijoin 放行 → pjoin 齐
        Assert.Equal(FlowInstanceStatus.Approved, (await db.Wf_FlowInstances.SingleAsync()).Status);
        Assert.False(await db.Wf_FlowTasks.AnyAsync(t => t.AssigneeId == ud));   // default 未走
    }

    // 嵌套反向：s → isplit → ( psplit⊂inclusive 支 → (p1,p2) → pj → ijoin , d[default] → ijoin ) → end
    [Fact]
    public async Task ParallelInsideInclusive_InclusiveJoinWaitsParallelSubtree()
    {
        using var db = NewDb();
        Guid u1 = Guid.NewGuid(), u2 = Guid.NewGuid(), ud = Guid.NewGuid();
        var schema = new FlowSchema
        {
            Start = "s",
            Nodes =
            {
                new FlowNode { Id = "s", Type = "start" },
                new FlowNode { Id = "isplit", Type = "inclusiveSplit" },
                new FlowNode { Id = "psplit", Type = "parallelSplit" },
                new FlowNode { Id = "p1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = u1 },
                new FlowNode { Id = "p2", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = u2 },
                new FlowNode { Id = "pj", Type = "parallelJoin" },
                new FlowNode { Id = "d", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = ud },
                new FlowNode { Id = "ijoin", Type = "inclusiveJoin" },
                new FlowNode { Id = "end", Type = "end" },
            },
            Edges =
            {
                new FlowEdge { From = "s", To = "isplit" },
                new FlowEdge { From = "isplit", To = "psplit", Condition = "goP > 0" },
                new FlowEdge { From = "isplit", To = "d" },
                new FlowEdge { From = "psplit", To = "p1" }, new FlowEdge { From = "psplit", To = "p2" },
                new FlowEdge { From = "p1", To = "pj" }, new FlowEdge { From = "p2", To = "pj" },
                new FlowEdge { From = "pj", To = "ijoin" },
                new FlowEdge { From = "d", To = "ijoin" },
                new FlowEdge { From = "ijoin", To = "end" },
            },
        };
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "pin", FlowName = "pin", FormKey = "f",
            SchemaJson = JsonSerializer.Serialize(schema), Version = 1, Enable = true });
        await db.SaveChangesAsync();
        await Engine(db).SubmitAsync("pin", Guid.NewGuid(), "{\"goP\":1}");   // 真边=psplit 支，default 不走

        Assert.False(await db.Wf_FlowTasks.AnyAsync(t => t.AssigneeId == ud));
        var t1 = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == u1 && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(t1.Id, u1, approve: true);   // p1 过 → pj 停泊；ijoin 活支子树在途，必须等
        Assert.Equal(FlowInstanceStatus.Running, (await db.Wf_FlowInstances.SingleAsync()).Status);

        var t2 = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == u2 && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(t2.Id, u2, approve: true);   // pj 齐 → 上弹 → ijoin 齐（活支==1）→ end
        Assert.Equal(FlowInstanceStatus.Approved, (await db.Wf_FlowInstances.SingleAsync()).Status);
        Assert.False(await db.Wf_FlowTokens.AnyAsync(t => t.Status == FlowTokenStatus.Active));
    }
}
```

- [ ] **Step 2: 跑验证 FAIL** — `--filter InclusiveGatewayTests`。预期「未知节点类型：inclusiveSplit」异常（`EnterNodeAsync :273` 抛）。

- [ ] **Step 3: 实现** — `InclusiveSplitNodeHandler.cs` 全文：

```csharp
using CP6.Entity.DomainModels.Wf;

namespace CP6.Core.Services.Wf;

/// <summary>包容分叉网关（hardening spec §3.1，第 7 个 handler）：对全部条件出边求值取真边集 T；
/// T 非空 → 激活 T（default 不走）；T 空 → 激活唯一 default 兜底边。每激活边各生一枚同 ForkId 子 token
/// （与 parallelSplit 完全相同的血缘机制）。default 边 = 唯一无条件出边（E-WF-020 校验保证存在且唯一），
/// 不是恒真必走边。激活集为空 = 校验漏网属 bug → 抛引擎异常，不静默。</summary>
internal sealed class InclusiveSplitNodeHandler : INodeHandler
{
    public string Type => "inclusiveSplit";

    public async Task OnEnterAsync(NodeContext ctx)
    {
        var eng = ctx.Engine; var inst = ctx.Inst; var schema = ctx.Schema; var node = ctx.Node;
        eng.ConsumeToken(ctx.Token);                       // 入 token 退场

        var outs = schema.Edges.Where(e => e.From == node.Id && e.IsError != true).ToList();
        var condEdges = outs.Where(e => !string.IsNullOrWhiteSpace(e.Condition)).ToList();
        var defaults  = outs.Where(e =>  string.IsNullOrWhiteSpace(e.Condition)).ToList();

        // 注意不能对全边直接调 Evaluate：空表达式在 ExpressionEvaluator 里恒真，必须先分组
        var truthy = condEdges.Where(e => ExpressionEvaluator.Evaluate(e.Condition, inst.VarsJson)).ToList();
        var active = truthy.Count > 0 ? truthy : defaults.Take(1).ToList();

        var forkId = Guid.NewGuid();
        var activated = new List<string>();
        foreach (var edge in active)
        {
            var target = FlowEngine.FindNode(schema, edge.To);
            if (target is null) continue;
            var child = eng.SpawnToken(inst, target, parent: ctx.Token.Id, fork: forkId);
            activated.Add(edge.To);
            await eng.EnterNodeAsync(inst, schema, target, child);
        }
        if (activated.Count == 0)   // 防御式兜底：校验漏网（无 default 且全假 / 激活边目标缺失）
            throw new InvalidOperationException($"E-WF-020: inclusiveSplit {node.Id} 无可激活出边");
        eng.AddHistory(inst.Id, node.Id, inst.StarterId, "inclusiveSplit",
            "activated: " + string.Join(",", activated));
    }
}
```

`InclusiveJoinNodeHandler.cs` 全文：

```csharp
using CP6.Entity.DomainModels.Wf;

namespace CP6.Core.Services.Wf;

/// <summary>包容汇聚网关（hardening spec §3.2，第 8 个 handler）：与 parallelJoin 同构，共用
/// <see cref="GatewayJoinHelper"/> 动态计票（活支==实际激活边数，只等真走的分支——inclusive join 标准解）。
/// D3：独立节点类型，不与 parallelJoin 合并。</summary>
internal sealed class InclusiveJoinNodeHandler : INodeHandler
{
    public string Type => "inclusiveJoin";
    public Task OnEnterAsync(NodeContext ctx) => GatewayJoinHelper.TryReleaseAsync(ctx, "inclusiveJoin");
}
```

`FlowEngine.cs` `DefaultHandlers()`（:38-43）改为（注释同步）：

```csharp
    // ★ T5：start/approval/end + parallelSplit/parallelJoin；A-T6：serviceTask；
    // ★ hardening A-T3：inclusiveSplit/inclusiveJoin（第 7/8 个，spec D3）。
    private static IEnumerable<INodeHandler> DefaultHandlers() => new INodeHandler[]
    {
        new StartNodeHandler(), new ApprovalNodeHandler(), new EndNodeHandler(),
        new ParallelSplitNodeHandler(), new ParallelJoinNodeHandler(),
        new ServiceTaskNodeHandler(Array.Empty<IServiceTaskExecutor>()),
        new InclusiveSplitNodeHandler(), new InclusiveJoinNodeHandler(),
    };
```

`Program.cs` :114（ServiceTaskNodeHandler 注册行）后加：

```csharp
builder.Services.AddScoped<CP6.Core.Services.Wf.INodeHandler, CP6.Core.Services.Wf.InclusiveSplitNodeHandler>();  // hardening A-T3 节点处理器：包容分叉
builder.Services.AddScoped<CP6.Core.Services.Wf.INodeHandler, CP6.Core.Services.Wf.InclusiveJoinNodeHandler>();   // hardening A-T3 节点处理器：包容汇聚
```

- [ ] **Step 4: 跑验证 PASS** — `--filter InclusiveGatewayTests` 全绿；`dotnet build CP6.WebApi/CP6.WebApi.csproj` 过。
- [ ] **Step 5: Wf 闸 + commit**

```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf
git add -A && git commit -m "feat(wfs-hardening): A-T3 InclusiveSplit/Join两handler+DefaultHandlers/DI注册(字典第7/8个)"
```

## 落码纪律 / Global Constraints（每个 Task 都遵守）

- **基线锁定**：后端 `dotnet test CP6.Tests/CP6.Tests.csproj` = **1509 通过（5 skip=SQLite 既知）** → 本波只增不减、全绿；前端 `npm run test`（vitest）= **320 全绿** → +N 全绿；`npm run type-check`（package.json 既有命令，含大堆内存参数）+ `npm run build` 全过。
- **EF clean（本波零迁移）**：只改 SchemaJson POCO（`FlowNode.OnBranchReject`）+ 常量（`FlowTokenStatus.Pruned`），**不加实体列、不生成迁移**。每波末跑 `dotnet ef migrations has-pending-model-changes --project CP6.Core/CP6.Core.csproj --startup-project CP6.WebApi/CP6.WebApi.csproj --context CP6Context` 必须 clean。
- **27 个既有 Wf 不变量测试一个断言不许改**（`CP6.Tests/Wf/**` 既有文件只增不改；`ParallelGatewayTests` 4+1 个并行语义测试是动态计票的等价性铁闸）。唯一例外见 D-T1：前端 `designerModel.test.ts:45` 的 palette 类型清单断言随 palette 扩展同步更新（前端测试不在 27 不变量范围，且该断言就是「palette 全集清单」本身）。
- **默认路径行为与现状全等**：无 inclusive 节点 + 无 onBranchReject 配置的流程，token 状态序列 / 任务 / FormTo / 通知 bit 级等价（cascade 一行不改、动态计票旧场景回归锁定、线性退回走既有全清场分支逐字保留）。
- **引擎内写路径三律（黄金模板铁律）**：① 先校验后写（一切结构化拒绝在任何状态突变之前抛出）；② 幂等（join 计数重入安全、已办任务再办 no-op）；③ handler/引擎内部方法**绝不自行 SaveChanges**（统一由 ActAsync/SendBackAsync 等外壳收口，剪枝/退回全部改动随既有 SaveChanges 落库）。
- **E 波紧跟 D 波不留窗口**：D-T2 合入后立即执行 E-T1/E-T2，不允许「有 UI 无 i18n/无 QA」的中间态过夜。
- **零跨模块污染**：不碰 `cp6.web/src/views/space/**`、`Services/*Space*`、Space 迁移/DbSet。每 Task 完成 `git show --stat` 复核。
- **零硬编码色**：前端新增视觉全部走 Design System token（`var(--cp-warn)` 家族等），沿 `DesignerCanvas.vue` `.dot-*` / `GatewayNode.vue` 既有 token 用法。
- **五语 i18n**：ja / zh-CN / zh-TW / en / ko，新 UI 文案全 `t()` 运行时键，键值入 `I18nOa*ScreenSeed` 家族新 seed。
- **隔离 worktree**：建议 `git worktree add C:/CP6-wfs-hardening -b feat/wfs-kernel-hardening main`（off `fb90d75`），不污染 `C:\CP6` 工作区。
- **subagent-driven TDD**：每 Task 全新编码子代理（模型按 model-policy：Opus 4.8）→ 主代理 `git show` diff 复核 → 本地 commit **不 push**。节奏：先写失败测试 → 跑验证 FAIL → 最小实现 → 跑验证 PASS → commit。提交信息 `feat(wfs-hardening): <Task 号> 中文摘要`。

---

## 侦察结论（spec §5.3 各核实项，已实读代码定案 —— 执行者照此实现，不再二次侦察）

| 核实项 | 结论 |
|---|---|
| §4.1 **split 节点定位机制** | `Wf_FlowToken`（`CP6.Entity/DomainModels/Wf/Wf_FlowToken.cs`）**没有** split nodeId 列，只有 `ParentTokenId`/`ForkId`/`StagePlanJson`。选定 **ParentTokenId 上溯**：分叉时 `ParallelSplitNodeHandler.cs:21` 以 `parent: ctx.Token.Id` 生子 token，而 ctx.Token 在被消费前 NodeId 已被 `AdvanceToken`（`FlowEngine.Tokens.cs:102`）置为 split 节点 id、`ConsumeToken` 不改 NodeId ⇒ **`ForkParent(t)`（Id==t.ParentTokenId 的 token）.NodeId 恒等于生成 t.ForkId 批次的 split 节点 id**。join 续 token 的「上弹一层」血缘（`ParallelJoinNodeHandler.cs:25-27`）保证该不变量对每层 fork 成立。零迁移。 |
| §5.3 **SendBackToNodeAsync 重生血缘现状** | `AdvancedFlow.cs:141` — `SpawnToken(inst, target, parent: null, fork: null)`，**血缘归零**（根 token）。SameBranch 规则必须改为携带剥离层血缘 `(parent: strip.ParentTokenId, fork: strip.ForkId)`；BeforeSplit/线性流保留归零重生（现状逐字）。 |
| §5.3 **CancelAllActiveTokens 过滤重载** | `FlowEngine.Tokens.cs:40` 现为全实例清场（含 B-T3 的 Pending Wf_ServiceJob 清场）、无过滤参数。不改它；**新增 `CancelTokenSubtree(Guid instanceId, Guid rootTokenId)`**（C-T2）：按 ParentTokenId 闭包算子树，子树内 Active token→Cancelled、Pending/Suspended 任务→Cancelled、Pending FormTo→Voided、Pending ServiceJob→Cancelled（镜像既有 Local ∪ DB localIds-exclusion 惯用法）。**子树闭包正确性论证**：join 续 token 血缘「上弹一层」（parent=祖父），故任何仍在途的分支延续 token 会重新挂在剥离层同级 —— 作用域分析（C-T1）从 current token 血缘出发选剥离层时选到的正是该延续 token 本身，ParentTokenId 后代闭包捕获全部需清场的活 token（C-T3 嵌套测试锁定）。 |
| §5.3 **StageRound 递增与局部退回相容性** | `NextStageRound`（`FlowEngine.ReadModel.cs:53`）按 `(instanceId, nodeId, tokenId, stageIndex)` 键控取 Max+1。SameBranch 重生的是**新 tokenId** ⇒ 新 token 串簽轮次从 0 起，与现状全清场重生同构，**天然相容，零改**。prevStage 退回不换 token（`AdvancedFlow.cs:161-163`），轮次 +1 语义不受本波影响。 |
| §5.3 **剥离层判定与 fork 栈共用血缘辅助** | 新 `TokenLineage` 静态类（A-T1）：`AncestorChain` / `CrossesFork` / `ForkParent` / `ForkStack`。剪枝递归上弹（B-T2/B-T3）与退回剥离层解析（C-T1）共用，单一口径。token 快照沿用 `ParallelJoinNodeHandler.AllTokens` 的 Local ∪ DB 身份映射去重口径，抽为 `FlowEngine.SnapshotTokens`（A-T1）。 |
| **计票退化护栏（计划期新发现）** | 旧静态计票在「join 被 ForkId==null 的线性 token 进入」的怪异 schema 下按入边数计（等不齐永停）；朴素动态判据会立即放行 ⇒ 行为漂移。定案：`GatewayJoinHelper` 对 **ForkId==null 保留旧静态入边计票路径**，bit 级等价（A-T2 专测锁定）。 |
| **剪后补放行 ≠ 全剪光（计划期新发现）** | 剪枝后 join 若齐批放行，同批 token 全部 Consumed、续 token 属上层批次 ⇒ 「无 Active 穿过本批次」也成立，若不加判别会误判全剪光递归驳回。定案：补放行探测中**检测到任一停泊 token 重入后变为 Consumed（即 join 已放行）则立即返回**，不再走全剪光检查（B-T2 `Prune_JoinBackfill_*` 锁定）。 |

**发现的 spec 与代码现状出入（不改 spec，按下述口径落码）**：
1. spec §5.2 称 BeforeSplit 为「现行为」——实际现状是 `CrossesParallelBlock`（`AdvancedFlow.cs:196`）对一切跨网关退回直接拒绝 E-WF-012，并非允许后全清场。本波 BeforeSplit = **放开该禁令后套用既有全清场机制**（行为上是新放开的能力，机制逐字复用现状代码块）。既有测试无「跨并行块拒绝」断言（已 grep 核实），无不变量冲突。
2. spec §4.2.2 通知联动信箱 spec 偏好矩阵——`PersistentWfNotifier` 现按 `NotificationPrefs` 强类型字段开关，无 BranchPruned 键。本波 `BranchPrunedAsync` **不查偏好开关**（等价信箱 spec「缺键默认 true」三态坍缩），偏好矩阵接管由信箱 spec 落地时统一改造。
3. spec §1 的 FlowEngine 行号锚点有 ±10 行漂移（办理实际在 `ActOnceAsync :136`、会签计票 `:169-177`、驳回连坐 `:221-226`），语义描述全部核实无误。

---

## File Structure（创建/修改清单，每文件一职责）

**后端 `CP6.Core/Services/Wf`**
- Modify `WfStatus.cs` — `FlowTokenStatus` 加 `Pruned = 3`。
- Modify `FlowSchema.cs` — `FlowNode` 加 `public string? OnBranchReject { get; set; }`。
- Create `TokenLineage.cs` — 血缘辅助纯函数（AncestorChain/CrossesFork/ForkParent/ForkStack）。
- Create `FlowGraph.cs` — schema 图论辅助（ReachableFrom/BfsDepths/NearestCommonJoin/BranchDomain）。
- Modify `FlowEngine.Tokens.cs` — 新增 `SnapshotTokens` / `CancelTokenSubtree` / `CancelPendingTasksOfToken` / `CancelPendingServiceJobsOfToken`。
- Create `NodeHandlers/GatewayJoinHelper.cs` — 双 join 共享动态计票放行（D4）。
- Modify `NodeHandlers/ParallelJoinNodeHandler.cs` — 委托 GatewayJoinHelper（删私有 AllTokens/静态计票）。
- Create `NodeHandlers/InclusiveSplitNodeHandler.cs` — 第 7 个 handler。
- Create `NodeHandlers/InclusiveJoinNodeHandler.cs` — 第 8 个 handler。
- Modify `FlowEngine.cs` — `DefaultHandlers()` 加 2 handler；`ActOnceAsync` 驳回分支加 prune 分流（cascade 一行不改）。
- Create `FlowEngine.Prune.cs` — partial：`TryPruneBranchAsync`/`PruneTokenAsync`/`ReleaseOrCollapseAsync`/`FindSplitNode`/`IsPrune`/`IsJoinType`。
- Create `SendBackScopeAnalyzer.cs` — `SendBackScope` 枚举 + `Analyze` 纯函数。
- Modify `AdvancedFlow.cs` — `SendBackToNodeAsync` 三规则接线（删 `CrossesParallelBlock`/`NodesBetween`）。
- Modify `FlowSchemaValidator.cs` — E-WF-020 / E-WF-021 规则。
- Modify `IWfNotifier.cs` — 加 `BranchPrunedAsync` + `NullWfNotifier` 实现。

**后端其他**
- Modify `CP6.Entity/DomainModels/Wf/WfNotificationType.cs` — 加 `BranchPruned = 5`。
- Modify `CP6.WebApi/Services/PersistentWfNotifier.cs` — `BranchPrunedAsync` 三渠道实现。
- Modify `CP6.WebApi/Services/SignalRWfNotifier.cs` — `BranchPrunedAsync` no-op（接口补全）。
- Modify `CP6.WebApi/Program.cs` — DI 注册 2 个新 INodeHandler（:113 后）；i18n concat（:1819 链尾）。
- Create `CP6.WebApi/Seed/I18nOaKernelHardeningScreenSeed.cs` — 五语 12 键。

**前端 `cp6.web/src`**
- Modify `views/oa/designer/designerModel.ts` — `SchemaNode.onBranchReject` + palette 2 入口 + validateClient E-WF-020/021 镜像。
- Modify `views/oa/designer/designerModel.test.ts` — :45 palette 类型清单断言补 2 类型。
- Create `views/oa/designer/designerModel.hardening.test.ts` — 新 vitest。
- Create `views/oa/designer/nodes/InclusiveGatewayNode.vue` — 空心圆菱形节点（BPMN 惯例）。
- Modify `views/oa/designer/DesignerCanvas.vue` — 注册 2 节点模板 + palette 空心 dot 样式。
- Modify `views/oa/designer/NodePropertyPanel.vue` — 「分支驳回策略」段（parallelSplit/inclusiveSplit）。
- Modify `types/oa/notification.ts` — `NotificationType.BranchPruned = 5` 镜像。

**测试 / QA**
- Create `CP6.Tests/Wf/TokenLineageTests.cs` / `DynamicJoinCountTests.cs` / `InclusiveGatewayTests.cs` / `InclusiveValidatorTests.cs` / `BranchPruneTests.cs` / `BranchPruneNestedTests.cs` / `SendBackScopeTests.cs` / `TokenSubtreeCancelTests.cs` / `SendBackThreeRuleTests.cs`。
- Modify `CP6.Tests/Oa/NotificationEngineHookTests.cs` — `CountingNotifier` 补 `BranchPrunedAsync` 计数。
- Create `docs/superpowers/qa/wfs-kernel-hardening/{README.md,seed.sql,qa_kernel_hardening.ps1}` — gstack harness。

---

## 共享契约（所有 Task 用这些**精确**名字与签名，前后一致，不许漂移）

```csharp
// WfStatus.cs
public const int Pruned = 3;                                  // FlowTokenStatus 新常量

// FlowSchema.cs / FlowNode
public string? OnBranchReject { get; set; }                   // null/"cascade"=连坐(现状)；"prune"=剪枝

// TokenLineage.cs（internal static class TokenLineage）
public static IEnumerable<Wf_FlowToken> AncestorChain(IReadOnlyList<Wf_FlowToken> all, Wf_FlowToken t);
public static bool CrossesFork(IReadOnlyList<Wf_FlowToken> all, Wf_FlowToken t, Guid forkId);
public static Wf_FlowToken? ForkParent(IReadOnlyList<Wf_FlowToken> all, Wf_FlowToken t);
public static List<(Wf_FlowToken BranchToken, Guid ForkId, string SplitNodeId)> ForkStack(
    IReadOnlyList<Wf_FlowToken> all, Wf_FlowToken t);          // 内→外

// FlowGraph.cs（internal static class FlowGraph）
public static FlowNode? NearestCommonJoin(FlowSchema schema, FlowNode split);
public static HashSet<string> ReachableFrom(FlowSchema schema, string startId);
public static HashSet<string> BranchDomain(FlowSchema schema, string edgeTargetId, string pairedJoinId);

// FlowEngine.Tokens.cs（partial FlowEngine）
internal IReadOnlyList<Wf_FlowToken> SnapshotTokens(Guid instanceId);          // Local ∪ DB 去重快照
internal void CancelTokenSubtree(Guid instanceId, Guid rootTokenId);           // 剥离层子树清场

// NodeHandlers/GatewayJoinHelper.cs
internal static class GatewayJoinHelper { public static Task TryReleaseAsync(NodeContext ctx, string historyAction); }

// FlowEngine.Prune.cs（partial FlowEngine）
internal Task<bool> TryPruneBranchAsync(Wf_FlowInstance inst, FlowSchema schema, Wf_FlowToken token, Guid actorId, string? comment);
internal static FlowNode? FindSplitNode(FlowSchema schema, IReadOnlyList<Wf_FlowToken> all, Wf_FlowToken token);
internal static bool IsPrune(FlowNode split);

// SendBackScopeAnalyzer.cs
public enum SendBackScope { SameBranch, BeforeSplit, SiblingBranch }
public static (SendBackScope Scope, Wf_FlowToken? StripToken) Analyze(
    FlowSchema schema, IReadOnlyList<Wf_FlowToken> all, Wf_FlowToken current,
    string currentNodeId, string targetNodeId);                // 结构不可判定 → throw E-WF-012

// IWfNotifier.cs / WfNotificationType.cs
Task BranchPrunedAsync(Guid starterId, Guid instanceId, string flowKey, string nodeId, string? comment);
public const int BranchPruned = 5;
```

- 节点类型串：`"inclusiveSplit"` / `"inclusiveJoin"`（比较一律 OrdinalIgnoreCase，对齐 `EnterNodeAsync` 的 ToLowerInvariant 分发）。
- 履历 action：`"inclusiveSplit"` / `"inclusiveJoin"` / `"branchPruned"`；退回沿用 `"sendback"`。
- 错误码：**E-WF-019**（运行时，退回兄弟支拒绝）/ **E-WF-020**（静态，inclusive default 边）/ **E-WF-021**（静态，配对 + onBranchReject 值域/落点）。
- 前端 i18n 键：`oa.designer.gw.inclusiveSplit|inclusiveJoin|branchReject|branchReject.cascade|branchReject.prune|branchRejectHint`、`oa.designer.errInclusiveDefault|errInclusivePair|errBranchReject`、`E-WF-019|E-WF-020|E-WF-021`（12 键五语）。

---

