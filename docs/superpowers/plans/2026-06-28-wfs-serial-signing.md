# WFS 串簽(顺签/逐级审批)Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在已完成的 WFS token 内核 + OA 信箱之上,给审批节点加**串簽(顺签/逐级审批)**:节点内有序多档、逐档推进,支持固定顺序/逐级动态(managerChain)/串中带并三形态,退回动作泛化为上一档/发起人/指定节点三目标。

**Architecture:** 方案 1 = 审批节点内多档 + 档位游标。token 停泊审批节点,进节点时 `IApprovalStagePlanner` 把设计期 `FlowNode.Stages` 展平成稳定 `RuntimeApprovalStage[]` 并**冻结**进 `Wf_FlowToken.StagePlanJson`(序列冻结/人选晚绑);某档按其会签模式判定通过 → 同节点同 token 建下档任务,末档过 → `AdvanceToken`。计票维度扩到 `(Inst,Node,Token,StageIndex,StageRound)`,`StageRound` 隔离 prevStage 重入轮次。**铁律:单档节点(`Stages==null`)走与今天逐字等价的 legacy 分支,新行为只在 opt-in 串簽节点。**

**Tech Stack:** .NET 8 / EF Core(SQL Server + InMemory 单测)/ xUnit;Vue 3 + Vue Flow + vitest;pandoc 双格式;gstack headless QA。

**工作树纪律:** 全在隔离 worktree `D:/CP6-wfs-serial` @ `feat/wfs-serial-sign`(off main `a462764`)。**绝不碰** `D:/CP6`(脏分支 `feat/wfs-inbox-core`)/`D:/CP6-space-backend`(Space 会话)。每 Task 全新 general-purpose subagent(sonnet) TDD → `git show` diff 级复核(零 Space/零越界)→ 本地 commit 不 push。**零改引擎执行态硬闸:每 Task 末 `dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~Wf" ` 既有照绿。**

**Spec:** `docs/superpowers/specs/2026-06-28-wfs-serial-signing-design.md`(v1.1,§0~§R 全锁,别重新设计)。

**测试命令基准(worktree 根 `D:/CP6-wfs-serial`):**
- 单测过滤:`dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~SerialSign"`
- Wf 回归闸:`dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~Wf"`
- 全量:`dotnet test CP6.Tests/CP6.Tests.csproj`(基线 1294 passed/1 skip)
- 前端(`cp6.web/`):`npm run type-check` / `npx vitest run <path>` / `npm run build`

---

## 文件结构(决策锁定)

**后端(`CP6.*`):**
- `CP6.Core/Services/Wf/FlowSchema.cs` — 增 `ApprovalStage` 类、`FlowNode.Stages`、`ApprovalStageKinds`/`CountersignModes` 常量。
- `CP6.Core/Services/Wf/IApprovalStagePlanner.cs`(新)— 接口 + `RuntimeApprovalStage` 类。
- `CP6.Core/Services/Wf/ApprovalStagePlanner.cs`(新)— `BuildAsync` 展平实现。
- `CP6.Core/Services/Wf/NodeHandlers/ApprovalNodeHandler.cs` — 档化(legacy 分支 + `EnterStageAsync`)。
- `CP6.Core/Services/Wf/FlowEngine.cs` — 构造加可选 planner;`ActOnceAsync` 档·轮化。
- `CP6.Core/Services/Wf/FlowEngine.Tokens.cs` — 暴露 `internal IApprovalStagePlanner Planner`。
- `CP6.Core/Services/Wf/FlowEngine.ReadModel.cs` — `WriteFormToOnSend` 加 stage 参;`SkipPendingFormTos`/`VoidPendingFormTos`/`UpdateFormToOnHandleAsync` 加 stage 过滤;新 `NextStageRound` 助手。
- `CP6.Core/Services/Wf/AdvancedFlow.cs` — `SendBackAsync` 泛化(`SendBackTarget` 三目标)。
- `CP6.Core/Services/Wf/IFlowEngine.cs` — `SendBackTarget` record + 新重载。
- `CP6.Core/Services/Wf/FlowSchemaValidator.cs` — 串簽档规则(E-WF-011)。
- `CP6.Entity/DomainModels/Wf/Wf_FlowTask.cs` / `Wf_FlowToken.cs` / `Wf_FlowFormTo.cs` — 新列。
- `CP6.Core/Services/Wf/WfStatus.cs` — `FlowFormToStatus.SentBack=7`。
- `CP6.Core/EFDbContext/CP6Context.cs` — 计票索引。
- `CP6.Core/Migrations/<ts>_WfsSerialSign.cs`(生成)。
- `CP6.Core/Services/Oa/ForecastService.cs` — 复用 planner 按档展开。
- DTO:`CP6.Core/Services/Wf/InboxModels` 或 `Services/Oa/*`(待办/详情/timeline/退回目标项,T8 定位)。
- `CP6.WebApi/Program.cs` — DI 注册 planner;i18n seed concat。
- `CP6.Core/.../I18nOaSerialSignScreenSeed.cs`(新)— 五语 seed。

**前端(`cp6.web/src`):**
- `api/oa/*.ts` / `types/oa/*.ts` — 退回目标 + 档字段。
- `views/oa/.../FormDetail*.vue` / `FlowTimeline*.vue` — 退回选择器 + 档·轮分组。
- `views/oa/designer/designerModel.ts` — `stages` 往返 + `validateClient` 镜像。
- `views/oa/designer/NodePropertyPanel.vue` — 「串簽档位」段。

**测试(新建):** `CP6.Tests/Wf/ApprovalStagePlannerTests.cs` / `SerialSignTests.cs` / `SerialSendBackTests.cs` / `SerialForecastTests.cs`;前端 `designerModel.test.ts` 增串簽用例。

---

# P-A 引擎内核

## Task 1: 数据模型 + 常量 + 状态 + 索引 + 迁移

**Files:**
- Modify: `CP6.Core/Services/Wf/FlowSchema.cs`(增 `ApprovalStage`/`FlowNode.Stages`/常量)
- Modify: `CP6.Entity/DomainModels/Wf/Wf_FlowTask.cs:46`(增 `StageIndex`/`StageRound`)
- Modify: `CP6.Entity/DomainModels/Wf/Wf_FlowToken.cs`(增 `StagePlanJson`)
- Modify: `CP6.Entity/DomainModels/Wf/Wf_FlowFormTo.cs`(增 `StageIndex?`/`StageRound?`)
- Modify: `CP6.Core/Services/Wf/WfStatus.cs:43`(增 `SentBack=7`)
- Modify: `CP6.Core/EFDbContext/CP6Context.cs:658`(增计票索引)
- Create: `CP6.Core/Migrations/<ts>_WfsSerialSign.cs`(ef 生成)
- Test: `CP6.Tests/Wf/SerialSignTests.cs`(新建,先放模型默认值测)

- [ ] **Step 1: 写失败测试(模型默认值 + 常量)**

`CP6.Tests/Wf/SerialSignTests.cs`:
```csharp
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;

namespace CP6.Tests.Wf;

public class SerialSignTests
{
    [Fact]
    public void NewColumns_DefaultToZeroOrNull()
    {
        var task = new Wf_FlowTask();
        Assert.Equal(0, task.StageIndex);
        Assert.Equal(0, task.StageRound);

        var token = new Wf_FlowToken();
        Assert.Null(token.StagePlanJson);

        var formto = new Wf_FlowFormTo();
        Assert.Null(formto.StageIndex);
        Assert.Null(formto.StageRound);
    }

    [Fact]
    public void Constants_AndSentBackStatus_Exist()
    {
        Assert.Equal("fixed", ApprovalStageKinds.Fixed);
        Assert.Equal("managerChain", ApprovalStageKinds.ManagerChain);
        Assert.Equal("all", CountersignModes.All);
        Assert.Equal(7, FlowFormToStatus.SentBack);
    }

    [Fact]
    public void FlowNode_Stages_DefaultsNull()
    {
        Assert.Null(new FlowNode().Stages);
        var stage = new ApprovalStage { Kind = "fixed", ApproverStrategy = "Specified", Countersign = "all" };
        Assert.Equal("fixed", stage.Kind);
    }
}
```

- [ ] **Step 2: 跑测确认失败**

Run: `dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~SerialSignTests"`
Expected: 编译失败(`StageIndex`/`StagePlanJson`/`ApprovalStage`/`ApprovalStageKinds`/`SentBack` 未定义)。

- [ ] **Step 3: 加实体列**

`Wf_FlowTask.cs`(在 `TokenId`(L46)之后):
```csharp
    /// <summary>串簽运行档序号(WFS 引擎深化)。默认 0=单档/旧数据,语义不变,无需回填。</summary>
    public int StageIndex { get; set; }

    /// <summary>同一运行档的重入轮次(prevStage 退回后 +1)。计票按 (Inst,Node,Token,StageIndex,StageRound) 隔离,
    /// 杜绝退回上一档后旧轮 Approved 任务串入新轮计票。默认 0。</summary>
    public int StageRound { get; set; }
```

`Wf_FlowToken.cs`(在 `ForkId`(L27)之后):
```csharp
    /// <summary>本 token 当前 approval 节点的冻结运行计划(RuntimeApprovalStage[] JSON)。进多档审批节点时算一次写入;
    /// 单档/非审批节点 = null。推进/退回基于它,不再每次现查 → 杜绝 managerChain 档位漂移。</summary>
    public string? StagePlanJson { get; set; }
```

`Wf_FlowFormTo.cs`(在 `Status`(L27)之后):
```csharp
    /// <summary>串簽运行档序号(timeline/forecast 标号)。旧行 null。</summary>
    public int? StageIndex { get; set; }
    /// <summary>串簽重入轮次。旧行 null。</summary>
    public int? StageRound { get; set; }
```

- [ ] **Step 4: 加常量 + SentBack + Stages + ApprovalStage**

`WfStatus.cs` 的 `FlowFormToStatus`(L43 `Voided=6` 后)加:
```csharp
    public const int SentBack = 7;    // 退回上一档(区别于普通作废 Voided=6)
```

`FlowSchema.cs` 文件末尾(`FlowEdge` 类后)加:
```csharp
/// <summary>串簽档型常量。</summary>
public static class ApprovalStageKinds { public const string Fixed = "fixed"; public const string ManagerChain = "managerChain"; }
/// <summary>会签模式常量。</summary>
public static class CountersignModes { public const string All = "all"; public const string Any = "any"; public const string Veto = "veto"; }

/// <summary>串簽档位(设计期)。一个 approval 节点可挂有序 Stages;空=单档(用节点既有字段)。</summary>
public class ApprovalStage
{
    public string? Name { get; set; }
    public string? Code { get; set; }
    /// <summary>fixed=固定一组审批人;managerChain=沿 ManagerId 链逐级展开。见 ApprovalStageKinds。</summary>
    public string Kind { get; set; } = ApprovalStageKinds.Fixed;
    public string? ApproverStrategy { get; set; }     // fixed:DirectManager/DeptLeader/Role/Specified/Starter
    public int? ApproverLevels { get; set; }          // fixed+DirectManager:取第 N 级主管(本档仍 1 运行档)
    public int? ApproverRoleId { get; set; }
    public Guid? ApproverUserId { get; set; }
    public string Countersign { get; set; } = CountersignModes.All;
    public int? MaxLevels { get; set; }               // managerChain:逐级展开上限(产 N 运行档)
}
```

`FlowSchema.cs` 的 `FlowNode` 类(`Code`(L58)之后)加:
```csharp
    /// <summary>串簽档位序列(有序)。空/缺省=单档,用本节点 ApproverStrategy/Countersign(向后兼容)。</summary>
    public List<ApprovalStage>? Stages { get; set; }
```

- [ ] **Step 5: 跑测确认通过 + 全量编译**

Run: `dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~SerialSignTests"`
Expected: 3 passed。

- [ ] **Step 6: 加计票索引**

`CP6Context.cs` Wf_FlowTask 配置块(L658 `IX_Wf_FlowTask_InstanceNode` 旁)加:
```csharp
            e.HasIndex(x => new { x.InstanceId, x.NodeId, x.TokenId, x.StageIndex, x.StageRound, x.Status })
                .HasDatabaseName("IX_Wf_FlowTask_Tally");   // 串簽档·轮计票
```

- [ ] **Step 7: 生成迁移**

Run(worktree 根):`dotnet ef migrations add WfsSerialSign -p CP6.Core -s CP6.WebApi`
检查生成的 `CP6.Core/Migrations/<ts>_WfsSerialSign.cs`:`Up` 仅 `AddColumn`(Wf_FlowTask.StageIndex/StageRound int default 0、Wf_FlowToken.StagePlanJson nvarchar(max) null、Wf_FlowFormTo.StageIndex/StageRound int null)+ `CreateIndex` IX_Wf_FlowTask_Tally。**无 DropColumn/数据搬迁/Space 表**(diff 复核:`git diff --stat`)。

- [ ] **Step 8: Wf 回归闸 + 提交**

Run: `dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~Wf"`
Expected: 既有全绿 + 3 新测 passed。
```bash
git add CP6.Core/Services/Wf/FlowSchema.cs CP6.Core/Services/Wf/WfStatus.cs \
  CP6.Entity/DomainModels/Wf/Wf_FlowTask.cs CP6.Entity/DomainModels/Wf/Wf_FlowToken.cs \
  CP6.Entity/DomainModels/Wf/Wf_FlowFormTo.cs CP6.Core/EFDbContext/CP6Context.cs \
  CP6.Core/Migrations/ CP6.Tests/Wf/SerialSignTests.cs
git commit -m "feat(wfs-serial): T1 数据模型 ApprovalStage/Stages+StageIndex/StageRound+StagePlanJson+SentBack+索引+迁移"
```

---

## Task 2: IApprovalStagePlanner 展平服务

**Files:**
- Create: `CP6.Core/Services/Wf/IApprovalStagePlanner.cs`(接口 + `RuntimeApprovalStage`)
- Create: `CP6.Core/Services/Wf/ApprovalStagePlanner.cs`(实现)
- Modify: `CP6.Core/Services/Wf/FlowEngine.cs:22-30`(构造加可选 planner)
- Modify: `CP6.Core/Services/Wf/FlowEngine.Tokens.cs:8-11`(暴露 `Planner`)
- Modify: `CP6.WebApi/Program.cs`(DI 注册)
- Test: `CP6.Tests/Wf/ApprovalStagePlannerTests.cs`(新建)

- [ ] **Step 1: 写失败测试**

`CP6.Tests/Wf/ApprovalStagePlannerTests.cs`:
```csharp
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Wf;

public class ApprovalStagePlannerTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static ApprovalStagePlanner Planner(CP6Context db) => new(new ApproverResolver(db));

    private static async Task<Guid> SeedUserChainAsync(CP6Context db, int levels)
    {
        // 造 levels 级管理链:u0(发起人) → u1 → ... → u_levels(链顶)
        Guid? mgr = null;
        Guid topId = Guid.Empty;
        for (int i = levels; i >= 0; i--)
        {
            var id = Guid.NewGuid();
            db.Sys_Users.Add(new Sys_User { Id = id, UserName = $"u{i}", Enable = true, ManagerId = mgr });
            if (i == levels) topId = id;
            mgr = id;
        }
        await db.SaveChangesAsync();
        return mgr!.Value;   // 最后赋的 mgr = u0(发起人)
    }

    [Fact]
    public async Task NoStages_YieldsSingleStageFromNodeFields()
    {
        using var db = NewDb();
        var u = Guid.NewGuid();
        var node = new FlowNode { Id = "n1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = u, Countersign = "any" };
        var plan = await Planner(db).BuildAsync(new Wf_FlowInstance { StarterId = u }, new FlowSchema(), node);
        Assert.Single(plan);
        Assert.Equal(0, plan[0].StageIndex);
        Assert.Equal("any", plan[0].Countersign);
        Assert.Equal(ApproverStrategy.Specified, plan[0].Rule.Strategy);
    }

    [Fact]
    public async Task FixedStages_FlattenInOrderWithIndices()
    {
        using var db = NewDb();
        var u = Guid.NewGuid();
        var node = new FlowNode { Id = "n1", Type = "approval", Stages = new()
        {
            new ApprovalStage { Kind = "fixed", ApproverStrategy = "Specified", ApproverUserId = Guid.NewGuid(), Countersign = "all" },
            new ApprovalStage { Kind = "fixed", ApproverStrategy = "Role", ApproverRoleId = 1, Countersign = "any" },
        }};
        var plan = await Planner(db).BuildAsync(new Wf_FlowInstance { StarterId = u }, new FlowSchema(), node);
        Assert.Equal(2, plan.Count);
        Assert.Equal(0, plan[0].StageIndex);
        Assert.Equal(1, plan[1].StageIndex);
        Assert.Equal("any", plan[1].Countersign);
    }

    [Fact]
    public async Task ManagerChain_ExpandsPerLevel_StopsAtChainEnd()
    {
        using var db = NewDb();
        var starter = await SeedUserChainAsync(db, levels: 3);   // 3 级主管
        var node = new FlowNode { Id = "n1", Type = "approval", Stages = new()
        {
            new ApprovalStage { Kind = "managerChain", MaxLevels = 5, Countersign = "all" },
        }};
        var plan = await Planner(db).BuildAsync(new Wf_FlowInstance { StarterId = starter }, new FlowSchema(), node);
        Assert.Equal(3, plan.Count);   // 链 3 级 → 3 档(MaxLevels=5 未封顶,链断即止)
        Assert.All(plan, s => Assert.Equal(ApproverStrategy.DirectManager, s.Rule.Strategy));
        Assert.Equal(1, plan[0].Rule.Levels);
        Assert.Equal(2, plan[1].Rule.Levels);
        Assert.Equal(3, plan[2].Rule.Levels);
    }

    [Fact]
    public async Task ManagerChain_CapsAtMaxLevels()
    {
        using var db = NewDb();
        var starter = await SeedUserChainAsync(db, levels: 4);
        var node = new FlowNode { Id = "n1", Type = "approval", Stages = new()
        {
            new ApprovalStage { Kind = "managerChain", MaxLevels = 2, Countersign = "all" },
        }};
        var plan = await Planner(db).BuildAsync(new Wf_FlowInstance { StarterId = starter }, new FlowSchema(), node);
        Assert.Equal(2, plan.Count);   // 封顶 2 档
    }

    [Fact]
    public async Task MixedFixed_ManagerChain_Fixed_IndicesContiguous()
    {
        using var db = NewDb();
        var starter = await SeedUserChainAsync(db, levels: 2);
        var node = new FlowNode { Id = "n1", Type = "approval", Stages = new()
        {
            new ApprovalStage { Kind = "fixed", ApproverStrategy = "Specified", ApproverUserId = Guid.NewGuid() },
            new ApprovalStage { Kind = "managerChain", MaxLevels = 5 },   // 2 级 → 2 档
            new ApprovalStage { Kind = "fixed", ApproverStrategy = "Role", ApproverRoleId = 1 },
        }};
        var plan = await Planner(db).BuildAsync(new Wf_FlowInstance { StarterId = starter }, new FlowSchema(), node);
        Assert.Equal(4, plan.Count);   // 1 + 2 + 1
        Assert.Equal(ApproverStrategy.Specified, plan[0].Rule.Strategy);
        Assert.Equal(ApproverStrategy.DirectManager, plan[1].Rule.Strategy);
        Assert.Equal(ApproverStrategy.DirectManager, plan[2].Rule.Strategy);
        Assert.Equal(ApproverStrategy.Role, plan[3].Rule.Strategy);   // 关键:最后 fixed 档 StageIndex=3 不被 managerChain 挤位
        Assert.Equal(3, plan[3].StageIndex);
    }
}
```

- [ ] **Step 2: 跑测确认失败**

Run: `dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~ApprovalStagePlannerTests"`
Expected: 编译失败(`ApprovalStagePlanner`/`RuntimeApprovalStage`/`BuildAsync` 未定义)。

- [ ] **Step 3: 写接口 + RuntimeApprovalStage**

`CP6.Core/Services/Wf/IApprovalStagePlanner.cs`:
```csharp
using CP6.Entity.DomainModels.Wf;

namespace CP6.Core.Services.Wf;

/// <summary>运行期档(展平 ApprovalStage 后的稳定结果)。进 approval 节点时算一次并冻结进 token.StagePlanJson。</summary>
public sealed class RuntimeApprovalStage
{
    public int StageIndex { get; set; }
    public string Kind { get; set; } = ApprovalStageKinds.Fixed;
    public string? StageName { get; set; }
    public string? StageCode { get; set; }
    public ApproverRule Rule { get; set; } = default!;
    public string Countersign { get; set; } = CountersignModes.All;
}

/// <summary>串簽档展平服务。把 node.Stages 展成稳定运行档序列。无 Stages → 单档(节点既有字段),保旧行为等价。
/// managerChain 沿 starter ManagerId 链逐级探,链断或 MaxLevels 止;只定序列/规则/会签,审批人 USER ID 在各档激活时晚解析。</summary>
public interface IApprovalStagePlanner
{
    Task<IReadOnlyList<RuntimeApprovalStage>> BuildAsync(Wf_FlowInstance inst, FlowSchema schema, FlowNode node);
}
```

- [ ] **Step 4: 写实现**

`CP6.Core/Services/Wf/ApprovalStagePlanner.cs`:
```csharp
using CP6.Entity.DomainModels.Wf;

namespace CP6.Core.Services.Wf;

public sealed class ApprovalStagePlanner : IApprovalStagePlanner
{
    private readonly IApproverResolver _approver;
    public ApprovalStagePlanner(IApproverResolver approver) => _approver = approver;

    public async Task<IReadOnlyList<RuntimeApprovalStage>> BuildAsync(Wf_FlowInstance inst, FlowSchema schema, FlowNode node)
    {
        var result = new List<RuntimeApprovalStage>();

        // 单档兼容:无 Stages → 用节点既有字段一档(等价旧 EnterNode approval)
        if (node.Stages is null || node.Stages.Count == 0)
        {
            var rule = FlowEngine.BuildRule(node);   // 既有静态:从节点四字段建 ApproverRule(可能 null=无审批人)
            result.Add(new RuntimeApprovalStage
            {
                StageIndex = 0, Kind = ApprovalStageKinds.Fixed,
                Rule = rule ?? new ApproverRule(ApproverStrategy.Specified, null, null, null),
                Countersign = node.Countersign,
            });
            return result;
        }

        int idx = 0;
        foreach (var st in node.Stages)
        {
            var kind = (st.Kind ?? ApprovalStageKinds.Fixed).Trim();
            if (string.Equals(kind, ApprovalStageKinds.ManagerChain, StringComparison.OrdinalIgnoreCase))
            {
                int max = st.MaxLevels is int m && m >= 1 ? m : 1;
                for (int j = 1; j <= max; j++)
                {
                    // 逐级探链:DirectManager Levels=j 解析得到 → 该级存在;Unres → 链断,停止展开
                    var probe = await _approver.ResolveAsync(
                        new ApproverRule(ApproverStrategy.DirectManager, j, null, null),
                        new ApproverResolveContext { StarterUserId = inst.StarterId });
                    if (!probe.Resolved) break;
                    result.Add(new RuntimeApprovalStage
                    {
                        StageIndex = idx++, Kind = ApprovalStageKinds.ManagerChain,
                        StageName = st.Name, StageCode = st.Code,
                        Rule = new ApproverRule(ApproverStrategy.DirectManager, j, null, null),
                        Countersign = string.IsNullOrWhiteSpace(st.Countersign) ? CountersignModes.All : st.Countersign,
                    });
                }
            }
            else // fixed
            {
                Enum.TryParse<ApproverStrategy>(st.ApproverStrategy, ignoreCase: true, out var strat);
                result.Add(new RuntimeApprovalStage
                {
                    StageIndex = idx++, Kind = ApprovalStageKinds.Fixed,
                    StageName = st.Name, StageCode = st.Code,
                    Rule = new ApproverRule(strat, st.ApproverLevels, st.ApproverRoleId, st.ApproverUserId),
                    Countersign = string.IsNullOrWhiteSpace(st.Countersign) ? CountersignModes.All : st.Countersign,
                });
            }
        }
        return result;
    }
}
```
> 若 `FlowEngine.BuildRule` 当前不是 `internal static`/可见,本步把其可见性提到 `internal static`(它已被 `ApprovalNodeHandler` 以 `FlowEngine.BuildRule(node)` 调用,应已是 internal static — 落码先确认,必要时不改仅复用)。

- [ ] **Step 5: FlowEngine 构造加可选 planner + 暴露**

`FlowEngine.cs:22-30` 构造签名末尾加参 + 字段:
```csharp
    private readonly IApprovalStagePlanner _planner;

    public FlowEngine(CP6Context db, IApproverResolver approver, IWfNotifier? notifier = null,
                      ApprovalDispatcher? dispatcher = null, IEnumerable<INodeHandler>? handlers = null,
                      IApprovalStagePlanner? planner = null)
    {
        _db = db; _approver = approver;
        _notifier = notifier ?? new NullWfNotifier();
        _dispatcher = dispatcher ?? new ApprovalDispatcher(Array.Empty<IApprovalCallback>());
        _handlers = (handlers ?? DefaultHandlers()).ToDictionary(h => h.Type, StringComparer.OrdinalIgnoreCase);
        _planner = planner ?? new ApprovalStagePlanner(_approver);   // 测试 Engine(db) 不传 → 内部 new,保 631 测绿
    }
```
`FlowEngine.Tokens.cs:8-11`(`internal IApproverResolver Approver` 旁)加:
```csharp
    internal IApprovalStagePlanner Planner => _planner;
```

- [ ] **Step 6: DI 注册**

`CP6.WebApi/Program.cs`(Wf 服务注册区,`IApproverResolver` 注册旁)加:
```csharp
builder.Services.AddScoped<IApprovalStagePlanner, ApprovalStagePlanner>();
```

- [ ] **Step 7: 跑测确认通过**

Run: `dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~ApprovalStagePlannerTests"`
Expected: 6 passed。

- [ ] **Step 8: Wf 回归闸 + 提交**

Run: `dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~Wf"`
Expected: 既有全绿(`Engine(db)` 默认 planner 注入,行为不变)+ 新测 passed。
```bash
git add CP6.Core/Services/Wf/IApprovalStagePlanner.cs CP6.Core/Services/Wf/ApprovalStagePlanner.cs \
  CP6.Core/Services/Wf/FlowEngine.cs CP6.Core/Services/Wf/FlowEngine.Tokens.cs \
  CP6.WebApi/Program.cs CP6.Tests/Wf/ApprovalStagePlannerTests.cs
git commit -m "feat(wfs-serial): T2 IApprovalStagePlanner 展平服务(单档兼容/fixed/managerChain逐级/混排不挤位)+DI+可选构造参"
```

---

## Task 3: ApprovalNodeHandler 档化(冻结计划 + EnterStageAsync)

**Files:**
- Modify: `CP6.Core/Services/Wf/NodeHandlers/ApprovalNodeHandler.cs`(OnEnter legacy 分支 + 多档分支 + `EnterStageAsync`)
- Modify: `CP6.Core/Services/Wf/FlowEngine.ReadModel.cs`(`WriteFormToOnSend` 加可选 stage 参 + `NextStageRound` 助手)
- Test: `CP6.Tests/Wf/SerialSignTests.cs`(加多档进入测)

- [ ] **Step 1: 写失败测试(进入即建第 0 档 + 冻结 plan + 空审批人挂起)**

加入 `SerialSignTests.cs`(复用 Engine harness,与 AdvancedFlowTests 同款 `NewDb`/`Engine`):
```csharp
    private static CP6Context Db() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static FlowEngine Eng(CP6Context db) => new(db, new ApproverResolver(db));

    private static async Task SeedSerialFixed3Async(CP6Context db, Guid s1, Guid s2, Guid s3)
    {
        var schema = new FlowSchema { Start = "ap", Nodes =
        {
            new FlowNode { Id = "ap", Type = "approval", Stages = new()
            {
                new ApprovalStage { Kind = "fixed", ApproverStrategy = "Specified", ApproverUserId = s1, Countersign = "all", Name = "档1" },
                new ApprovalStage { Kind = "fixed", ApproverStrategy = "Specified", ApproverUserId = s2, Countersign = "all", Name = "档2" },
                new ApprovalStage { Kind = "fixed", ApproverStrategy = "Specified", ApproverUserId = s3, Countersign = "all", Name = "档3" },
            }},
            new FlowNode { Id = "end", Type = "end" },
        }, Edges = { new FlowEdge { From = "ap", To = "end" } } };
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "serial3", FlowName = "三档串簽",
            FormKey = "t", SchemaJson = JsonSerializer.Serialize(schema), Version = 1, Enable = true });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task SerialEnter_BuildsOnlyStage0Task_FreezesPlan()
    {
        using var db = Db();
        Guid s1 = Guid.NewGuid(), s2 = Guid.NewGuid(), s3 = Guid.NewGuid();
        await SeedSerialFixed3Async(db, s1, s2, s3);
        var instId = await Eng(db).SubmitAsync("serial3", Guid.NewGuid(), "{}");

        var pending = await db.Wf_FlowTasks.Where(t => t.Status == FlowTaskStatus.Pending).ToListAsync();
        Assert.Single(pending);                          // 仅第 0 档建任务
        Assert.Equal(s1, pending[0].AssigneeId);
        Assert.Equal(0, pending[0].StageIndex);
        Assert.Equal(0, pending[0].StageRound);

        var tok = await db.Wf_FlowTokens.SingleAsync(t => t.Status == FlowTokenStatus.Active);
        Assert.False(string.IsNullOrEmpty(tok.StagePlanJson));   // 冻结计划已落
    }

    [Fact]
    public async Task SerialStage_NoApprover_Suspends_NotSkip()
    {
        using var db = Db();
        var schema = new FlowSchema { Start = "ap", Nodes =
        {
            new FlowNode { Id = "ap", Type = "approval", Stages = new()
            {
                new ApprovalStage { Kind = "fixed", ApproverStrategy = "Role", ApproverRoleId = 999, Countersign = "all" }, // 无人角色
            }},
            new FlowNode { Id = "end", Type = "end" },
        }, Edges = { new FlowEdge { From = "ap", To = "end" } } };
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "noappr", FlowName = "x", FormKey = "t",
            SchemaJson = JsonSerializer.Serialize(schema), Version = 1, Enable = true });
        await db.SaveChangesAsync();

        var instId = await Eng(db).SubmitAsync("noappr", Guid.NewGuid(), "{}");
        var inst = await db.Wf_FlowInstances.SingleAsync(i => i.Id == instId);
        Assert.Equal(FlowInstanceStatus.Suspended, inst.Status);   // 不静默跳过
        Assert.Equal(0, await db.Wf_FlowTasks.CountAsync(t => t.Status == FlowTaskStatus.Pending));
    }
```
(文件顶部 using 增 `System.Text.Json`、`Microsoft.EntityFrameworkCore`、`Microsoft.EntityFrameworkCore.Diagnostics`、`CP6.Core.EFDbContext`。)

- [ ] **Step 2: 跑测确认失败**

Run: `dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~SerialSignTests"`
Expected: `SerialEnter_*` 失败(多档全建任务 / StagePlanJson 空)。

- [ ] **Step 3: ReadModel 加 stage 参 + NextStageRound 助手**

`FlowEngine.ReadModel.cs`:`WriteFormToOnSend`(L29)签名末尾加可选参,并落列:
```csharp
    internal void WriteFormToOnSend(Wf_FlowInstance inst, FlowNode node, Wf_FlowToken token,
        Guid expectedHandler, Guid? onBehalfOf, int stepSeq, int? stageIndex = null, int? stageRound = null)
    {
        _db.Wf_FlowFormTos.Add(new Wf_FlowFormTo
        {
            Id = Guid.NewGuid(), InstanceId = inst.Id, TokenId = token.Id, StepSeq = stepSeq,
            NodeId = node.Id, NodeCode = node.Id, NodeName = node.Name,
            ExpectedHandlerId = expectedHandler, OnBehalfOfId = onBehalfOf,
            Status = FlowFormToStatus.Pending, SentAt = DateTime.Now,
            StageIndex = stageIndex, StageRound = stageRound,
        });
    }
```
同文件加助手(仿 `NextStepSeq` 的 Local+DB 取 Max 模式):
```csharp
    /// <summary>本 token·node·stage 的下一个 StageRound = 既存最大轮 +1(无 → 0)。前进档天然 0,prevStage 退回天然 +1。
    /// 仿 NextStepSeq(:12)的 Local+DB 取 Max 模式,杜绝同回合未落盘行漏算。</summary>
    internal int NextStageRound(Guid instanceId, string nodeId, Guid tokenId, int stageIndex)
    {
        int localMax = _db.Wf_FlowTasks.Local
            .Where(t => t.InstanceId == instanceId && t.NodeId == nodeId && t.TokenId == tokenId && t.StageIndex == stageIndex)
            .Select(t => (int?)t.StageRound).Max() ?? -1;
        int dbMax = _db.Wf_FlowTasks
            .Where(t => t.InstanceId == instanceId && t.NodeId == nodeId && t.TokenId == tokenId && t.StageIndex == stageIndex)
            .Select(t => (int?)t.StageRound).Max() ?? -1;
        return Math.Max(localMax, dbMax) + 1;
    }
```

- [ ] **Step 4: ApprovalNodeHandler 档化**

`ApprovalNodeHandler.cs` 重写 `OnEnterAsync`:**单档保留原全文(逐字等价)**,多档走新 `EnterStageAsync`:
```csharp
    public async Task OnEnterAsync(NodeContext ctx)
    {
        var eng = ctx.Engine; var inst = ctx.Inst; var node = ctx.Node;

        // ── 单档(无 Stages):与今天逐字等价,不碰 planner/StagePlanJson ──
        if (node.Stages is null || node.Stages.Count == 0)
        {
            await eng.WriteCcAsync(inst, node.Id, node.CcUsers, node.CcRoleId);
            var rule = FlowEngine.BuildRule(node);
            if (rule is null) { eng.Suspend(inst, node, "节点未配置审批人"); return; }
            var res = await eng.Approver.ResolveAsync(rule, new ApproverResolveContext { StarterUserId = inst.StarterId });
            if (!res.Resolved) { eng.Suspend(inst, node, res.UnresolvedReason ?? "审批人无法解析"); return; }

            var stale = await eng.Db.Wf_FlowTasks
                .Where(t => t.InstanceId == inst.Id && t.NodeId == node.Id && t.Status != FlowTaskStatus.Cancelled)
                .ToListAsync();
            foreach (var t in stale) t.Status = FlowTaskStatus.Cancelled;

            var dueAt = FlowEngine.NodeDueAt(node);
            var step = eng.NextStepSeq(inst.Id);
            eng.WriteSnapshot(inst, node, ctx.Token, step);
            foreach (var uid in res.ApproverIds.Distinct())
            {
                var (assignee, delegatedFrom) = await eng.ResolveActualAssigneeAsync(uid);
                eng.Db.Wf_FlowTasks.Add(new Wf_FlowTask
                {
                    Id = Guid.NewGuid(), InstanceId = inst.Id, NodeId = node.Id, AssigneeId = assignee,
                    Status = FlowTaskStatus.Pending, Countersign = node.Countersign, DueAt = dueAt, TokenId = ctx.Token.Id,
                });
                eng.WriteFormToOnSend(inst, node, ctx.Token, assignee, delegatedFrom, step);
                if (delegatedFrom is Guid g) eng.AddHistory(inst.Id, node.Id, assignee, "delegate", $"代 {g} 审批");
                await eng.Notifier.TodoCreatedAsync(assignee, inst.Id, Guid.Empty, inst.FlowKey);
            }
            return;
        }

        // ── 多档串簽:冻结计划进 token.StagePlanJson,进第 0 档 ──
        await eng.WriteCcAsync(inst, node.Id, node.CcUsers, node.CcRoleId);
        var plan = await eng.Planner.BuildAsync(inst, ctx.Schema, node);
        ctx.Token.StagePlanJson = System.Text.Json.JsonSerializer.Serialize(plan);
        await eng.EnterStageAsync(inst, ctx.Schema, node, ctx.Token, plan, 0);
    }
```
> 注:原代码 `await eng.Notifier.TodoCreatedAsync(assignee, inst.Id, task.Id, ...)` 用 `task.Id`;上面 inline 后改存局部 `task` 再取 `.Id`(落码保持与原一致,勿用 Guid.Empty)。即把 `var task = new Wf_FlowTask{...}; eng.Db.Wf_FlowTasks.Add(task);` 写法照搬原文件,确保单档分支**逐字等价**。

在 `FlowEngine`(放 `FlowEngine.cs` 或新 partial `FlowEngine.Serial.cs`)加 `internal EnterStageAsync`:
```csharp
    /// <summary>进入串簽第 k 档:解析该档审批人建任务(StageRound 自推导)。解析不到 → Suspend(E-WF-013)。</summary>
    internal async Task EnterStageAsync(Wf_FlowInstance inst, FlowSchema schema, FlowNode node,
        Wf_FlowToken token, IReadOnlyList<RuntimeApprovalStage> plan, int k)
    {
        var stage = plan[k];
        var res = await _approver.ResolveAsync(stage.Rule, new ApproverResolveContext { StarterUserId = inst.StarterId });
        if (!res.Resolved) { Suspend(inst, node, "E-WF-013"); return; }

        int round = NextStageRound(inst.Id, node.Id, token.Id, k);
        var dueAt = NodeDueAt(node);
        var step = NextStepSeq(inst.Id);
        WriteSnapshot(inst, node, token, step);
        foreach (var uid in res.ApproverIds.Distinct())
        {
            var (assignee, delegatedFrom) = await ResolveActualAssigneeAsync(uid);
            var task = new Wf_FlowTask
            {
                Id = Guid.NewGuid(), InstanceId = inst.Id, NodeId = node.Id, AssigneeId = assignee,
                Status = FlowTaskStatus.Pending, Countersign = stage.Countersign, DueAt = dueAt,
                TokenId = token.Id, StageIndex = k, StageRound = round,
            };
            _db.Wf_FlowTasks.Add(task);
            WriteFormToOnSend(inst, node, token, assignee, delegatedFrom, step, stageIndex: k, stageRound: round);
            if (delegatedFrom is Guid g) AddHistory(inst.Id, node.Id, assignee, "delegate", $"代 {g} 审批");
            await _notifier.TodoCreatedAsync(assignee, inst.Id, task.Id, inst.FlowKey);
        }
    }
```

- [ ] **Step 5: 跑测确认通过**

Run: `dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~SerialSignTests"`
Expected: 全 passed(进入仅建第 0 档 + StagePlanJson 落 + 空审批人 Suspend)。

- [ ] **Step 6: Wf 回归闸 + 提交**

Run: `dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~Wf"`
Expected: 既有全绿(单档分支逐字等价)+ 新测 passed。
```bash
git add CP6.Core/Services/Wf/NodeHandlers/ApprovalNodeHandler.cs CP6.Core/Services/Wf/FlowEngine*.cs \
  CP6.Tests/Wf/SerialSignTests.cs
git commit -m "feat(wfs-serial): T3 ApprovalNodeHandler 档化(单档逐字等价分支+多档冻结计划+EnterStageAsync+空审批人Suspend E-WF-013)"
```

---

## Task 4: ActOnceAsync 档·轮化(逐档推进)

**Files:**
- Modify: `CP6.Core/Services/Wf/FlowEngine.cs:163-190`(tally 加 stage 维度 + passed 推档/推节点)
- Modify: `CP6.Core/Services/Wf/FlowEngine.ReadModel.cs:101`(`SkipPendingFormTos` 加 stage 过滤)
- Test: `CP6.Tests/Wf/SerialSignTests.cs`(加推进/串中带并/防漂移测)

- [ ] **Step 1: 写失败测试**

加入 `SerialSignTests.cs`:
```csharp
    [Fact]
    public async Task SerialFixed3_AdvancesStageByStage_ThenEnds()
    {
        using var db = Db();
        Guid s1 = Guid.NewGuid(), s2 = Guid.NewGuid(), s3 = Guid.NewGuid();
        await SeedSerialFixed3Async(db, s1, s2, s3);
        var instId = await Eng(db).SubmitAsync("serial3", Guid.NewGuid(), "{}");

        // 档0 过 → 建档1
        var t0 = await db.Wf_FlowTasks.SingleAsync(t => t.StageIndex == 0 && t.Status == FlowTaskStatus.Pending);
        await Eng(db).ActAsync(t0.Id, s1, true);
        var t1 = await db.Wf_FlowTasks.SingleAsync(t => t.StageIndex == 1 && t.Status == FlowTaskStatus.Pending);
        Assert.Equal(s2, t1.AssigneeId);
        Assert.Equal(FlowInstanceStatus.Running, (await db.Wf_FlowInstances.SingleAsync(i => i.Id == instId)).Status);

        // 档1 过 → 建档2
        await Eng(db).ActAsync(t1.Id, s2, true);
        var t2 = await db.Wf_FlowTasks.SingleAsync(t => t.StageIndex == 2 && t.Status == FlowTaskStatus.Pending);
        Assert.Equal(s3, t2.AssigneeId);

        // 档2 过 → AdvanceToken → end → Approved
        await Eng(db).ActAsync(t2.Id, s3, true);
        var inst = await db.Wf_FlowInstances.SingleAsync(i => i.Id == instId);
        Assert.Equal(FlowInstanceStatus.Approved, inst.Status);
        Assert.Equal(0, await db.Wf_FlowTokens.CountAsync(t => t.InstanceId == instId && t.Status == FlowTokenStatus.Active));
    }

    [Fact]
    public async Task SerialStage_WithParallelCountersign_Any_PassesOnFirstApprove()
    {
        using var db = Db();
        Guid x = Guid.NewGuid(), y = Guid.NewGuid();
        var schema = new FlowSchema { Start = "ap", Nodes =
        {
            new FlowNode { Id = "ap", Type = "approval", Stages = new()
            {
                new ApprovalStage { Kind = "fixed", ApproverStrategy = "Role", ApproverRoleId = 5, Countersign = "any" },
            }},
            new FlowNode { Id = "end", Type = "end" },
        }, Edges = { new FlowEdge { From = "ap", To = "end" } } };
        db.Sys_Users.Add(new() { Id = x, UserName = "x", Enable = true, RoleId = 5 });
        db.Sys_Users.Add(new() { Id = y, UserName = "y", Enable = true, RoleId = 5 });
        db.Wf_FlowDefs.Add(new() { Id = Guid.NewGuid(), FlowKey = "csany", FlowName = "x", FormKey = "t",
            SchemaJson = JsonSerializer.Serialize(schema), Version = 1, Enable = true });
        await db.SaveChangesAsync();

        var instId = await Eng(db).SubmitAsync("csany", Guid.NewGuid(), "{}");
        Assert.Equal(2, await db.Wf_FlowTasks.CountAsync(t => t.StageIndex == 0 && t.Status == FlowTaskStatus.Pending));
        var one = await db.Wf_FlowTasks.FirstAsync(t => t.Status == FlowTaskStatus.Pending);
        await Eng(db).ActAsync(one.Id, one.AssigneeId, true);   // any:一人过即档过
        Assert.Equal(FlowInstanceStatus.Approved, (await db.Wf_FlowInstances.SingleAsync(i => i.Id == instId)).Status);
    }

    [Fact]
    public async Task ManagerChainPlan_FrozenAtEntry_OrgChangeDoesNotAddStage()
    {
        using var db = Db();
        // 发起人 u0 → u1(链顶,仅 1 级)
        Guid u0 = Guid.NewGuid(), u1 = Guid.NewGuid();
        db.Sys_Users.Add(new() { Id = u1, UserName = "u1", Enable = true, ManagerId = null });
        db.Sys_Users.Add(new() { Id = u0, UserName = "u0", Enable = true, ManagerId = u1 });
        var schema = new FlowSchema { Start = "ap", Nodes =
        {
            new FlowNode { Id = "ap", Type = "approval", Stages = new()
            {
                new ApprovalStage { Kind = "managerChain", MaxLevels = 5, Countersign = "all" },
            }},
            new FlowNode { Id = "end", Type = "end" },
        }, Edges = { new(){From="ap",To="end"} } };
        db.Wf_FlowDefs.Add(new() { Id = Guid.NewGuid(), FlowKey = "mc", FlowName = "x", FormKey = "t",
            SchemaJson = JsonSerializer.Serialize(schema), Version = 1, Enable = true });
        await db.SaveChangesAsync();

        var instId = await Eng(db).SubmitAsync("mc", u0, "{}");   // 进节点冻结:链 1 级 → plan 1 档
        var t0 = await db.Wf_FlowTasks.SingleAsync(t => t.StageIndex == 0 && t.Status == FlowTaskStatus.Pending);

        // 冻结后改组织:给 u1 加上级 u2(链变 2 级)
        var u2 = Guid.NewGuid();
        db.Sys_Users.Add(new() { Id = u2, UserName = "u2", Enable = true, ManagerId = null });
        var u1row = await db.Sys_Users.SingleAsync(u => u.Id == u1);
        u1row.ManagerId = u2;
        await db.SaveChangesAsync();

        // 档0(u1)过 → 冻结 plan 仅 1 档 → 不因链增长出档1 → 直接 AdvanceToken → end → Approved
        await Eng(db).ActAsync(t0.Id, u1, true);
        var inst = await db.Wf_FlowInstances.SingleAsync(i => i.Id == instId);
        Assert.Equal(FlowInstanceStatus.Approved, inst.Status);
        Assert.Equal(0, await db.Wf_FlowTasks.CountAsync(t => t.StageIndex == 1));   // 无第 1 档(防漂移)
    }
```

- [ ] **Step 2: 跑测确认失败**

Run: `dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~SerialSignTests"`
Expected: `SerialFixed3_*` 失败(档0过后未建档1,而是直接 AdvanceToken 去 end)。

- [ ] **Step 3: ActOnceAsync 档·轮化**

`FlowEngine.cs` 的 `ActOnceAsync` 计票查询(L163-166)加 stage 过滤:
```csharp
        var nodeTasks = await _db.Wf_FlowTasks
            .Where(t => t.InstanceId == inst.Id && t.NodeId == task.NodeId
                        && t.TokenId == task.TokenId
                        && t.StageIndex == task.StageIndex && t.StageRound == task.StageRound   // ★ 档·轮隔离
                        && t.Status != FlowTaskStatus.Cancelled)
            .ToListAsync();
```
`passed` 分支(L184-190)改为"先推档,无档才推节点":
```csharp
        if (passed)
        {
            SkipPendingFormTos(inst.Id, task.NodeId, task.TokenId, task.StageIndex, task.StageRound);
            var schema = await LoadSchemaAsync(inst.FlowKey);
            var tok = await _db.Wf_FlowTokens.FirstOrDefaultAsync(t => t.Id == task.TokenId);
            if (tok is not null)
            {
                var plan = string.IsNullOrEmpty(tok.StagePlanJson)
                    ? null
                    : JsonSerializer.Deserialize<List<RuntimeApprovalStage>>(tok.StagePlanJson, JsonOpts);
                int k1 = task.StageIndex + 1;
                if (plan is not null && k1 < plan.Count)
                {
                    var node = FindNode(schema, task.NodeId)!;
                    await EnterStageAsync(inst, schema, node, tok, plan, k1);   // 建下档,token 仍停本节点
                }
                else
                {
                    await AdvanceToken(inst, schema, tok);                      // 末档/单档 → 去下一节点
                }
            }
        }
```
> `JsonOpts` 复用 `FlowEngine.cs:14` 既有静态。`FindNode` 已是私有助手。

- [ ] **Step 4: SkipPendingFormTos 加 stage 过滤**

`FlowEngine.ReadModel.cs:101` 签名 + 两处 Where 加 stage 维度:
```csharp
    internal void SkipPendingFormTos(Guid instanceId, string nodeId, Guid? tokenId, int? stageIndex = null, int? stageRound = null)
    {
        bool Match(Wf_FlowFormTo f) => f.InstanceId == instanceId && f.NodeId == nodeId && f.TokenId == tokenId
            && f.Status == FlowFormToStatus.Pending
            && (stageIndex == null || f.StageIndex == stageIndex)
            && (stageRound == null || f.StageRound == stageRound);
        foreach (var f in _db.Wf_FlowFormTos.Local.Where(Match).ToList()) f.Status = FlowFormToStatus.Skipped;
        var localIds = _db.Wf_FlowFormTos.Local.Where(f => f.InstanceId == instanceId).Select(f => f.Id).ToHashSet();
        foreach (var f in _db.Wf_FlowFormTos
            .Where(f => f.InstanceId == instanceId && f.NodeId == nodeId && f.TokenId == tokenId
                        && f.Status == FlowFormToStatus.Pending && !localIds.Contains(f.Id)
                        && (stageIndex == null || f.StageIndex == stageIndex)
                        && (stageRound == null || f.StageRound == stageRound)).ToList())
            f.Status = FlowFormToStatus.Skipped;
    }
```
> 单档调用方(`ActOnceAsync` 既有 + 上面 passed 分支)传 stageIndex/stageRound;旧其他调用(若有)默认 null = 全节点(行为不变)。

- [ ] **Step 5: 跑测确认通过**

Run: `dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~SerialSignTests"`
Expected: 全 passed。

- [ ] **Step 6: Wf 回归闸 + 提交**

Run: `dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~Wf"`
Expected: 既有全绿(单档 task.StageIndex/StageRound=0,tally 行为不变)+ 新测 passed。
```bash
git add CP6.Core/Services/Wf/FlowEngine.cs CP6.Core/Services/Wf/FlowEngine.ReadModel.cs CP6.Tests/Wf/SerialSignTests.cs
git commit -m "feat(wfs-serial): T4 ActOnceAsync 档·轮化(tally+StageIndex/Round隔离+passed逐档推进/末档AdvanceToken)+SkipPendingFormTos档过滤"
```

---

# P-B 退回泛化

## Task 5: SendBackTarget 三目标骨架 + node 收紧校验

**Files:**
- Modify: `CP6.Core/Services/Wf/IFlowEngine.cs:27`(加 `SendBackTarget` record + 新重载签名)
- Modify: `CP6.Core/Services/Wf/AdvancedFlow.cs:100`(拆分:旧重载转发 node + node 收紧校验 BFS)
- Test: `CP6.Tests/Wf/SerialSendBackTests.cs`(新建,node 收紧 + 既有等价)

- [ ] **Step 1: 写失败测试(node 收紧:退非上游/自身 → E-WF-012;上游照常)**

`CP6.Tests/Wf/SerialSendBackTests.cs`:
```csharp
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Wf;

public class SerialSendBackTests
{
    private static CP6Context Db() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static FlowEngine Eng(CP6Context db) => new(db, new ApproverResolver(db));

    // 线性 n1(A)→n2(B)→n3(C)→end
    private static async Task SeedLinear3(CP6Context db, Guid a, Guid b, Guid c)
    {
        var schema = new FlowSchema { Start = "n1", Nodes =
        {
            new FlowNode { Id = "n1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = a },
            new FlowNode { Id = "n2", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = b },
            new FlowNode { Id = "n3", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = c },
            new FlowNode { Id = "end", Type = "end" },
        }, Edges = { new(){From="n1",To="n2"}, new(){From="n2",To="n3"}, new(){From="n3",To="end"} } };
        db.Wf_FlowDefs.Add(new() { Id = Guid.NewGuid(), FlowKey = "lin3", FlowName = "x", FormKey = "t",
            SchemaJson = JsonSerializer.Serialize(schema), Version = 1, Enable = true });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task SendBackNode_ToUpstream_Works()
    {
        using var db = Db();
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid();
        await SeedLinear3(db, a, b, c);
        var instId = await Eng(db).SubmitAsync("lin3", Guid.NewGuid(), "{}");
        var t1 = await db.Wf_FlowTasks.SingleAsync(t => t.NodeId == "n1");
        await Eng(db).ActAsync(t1.Id, a, true);
        var t2 = await db.Wf_FlowTasks.SingleAsync(t => t.NodeId == "n2" && t.Status == FlowTaskStatus.Pending);

        await Eng(db).SendBackAsync(t2.Id, b, new SendBackTarget("node", "n1"));   // 退到上游 n1
        Assert.Equal("n1", (await db.Wf_FlowInstances.SingleAsync(i => i.Id == instId)).CurrentNode);
    }

    [Fact]
    public async Task SendBackNode_ToDownstreamOrSelf_Throws_E_WF_012()
    {
        using var db = Db();
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid();
        await SeedLinear3(db, a, b, c);
        await Eng(db).SubmitAsync("lin3", Guid.NewGuid(), "{}");
        var t1 = await db.Wf_FlowTasks.SingleAsync(t => t.NodeId == "n1");
        await Eng(db).ActAsync(t1.Id, a, true);
        var t2 = await db.Wf_FlowTasks.SingleAsync(t => t.NodeId == "n2" && t.Status == FlowTaskStatus.Pending);

        // 退到下游 n3(非上游) / 自身 n2 → E-WF-012
        var e1 = await Assert.ThrowsAsync<InvalidOperationException>(() => Eng(db).SendBackAsync(t2.Id, b, new SendBackTarget("node", "n3")));
        Assert.Contains("E-WF-012", e1.Message);
        var e2 = await Assert.ThrowsAsync<InvalidOperationException>(() => Eng(db).SendBackAsync(t2.Id, b, new SendBackTarget("node", "n2")));
        Assert.Contains("E-WF-012", e2.Message);
    }
}
```

- [ ] **Step 2: 跑测确认失败**

Run: `dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~SerialSendBackTests"`
Expected: 编译失败(`SendBackTarget` 未定义)。

- [ ] **Step 3: 加 SendBackTarget + 新重载签名**

`IFlowEngine.cs`(`SendBackAsync`(L27)旁)加:
```csharp
    /// <summary>退回落点。Kind: prevStage(同节点上一档)/starter(退回发起人重填)/node(退回指定上游节点)。</summary>
    public sealed record SendBackTarget(string Kind, string? NodeId = null);

    /// <summary>退回(泛化三目标)。详见 spec §4.2。</summary>
    Task SendBackAsync(Guid taskId, Guid actorId, SendBackTarget target, string? comment = null);
```

- [ ] **Step 4: AdvancedFlow 拆分 + node 收紧**

`AdvancedFlow.cs`:把原 `SendBackAsync(taskId,actor,targetNodeId,comment)` 改为转发,新增三目标分发 + node 上游可达校验:
```csharp
    // 旧重载保留 → 转发 node(既有调用方零感知)
    public Task SendBackAsync(Guid taskId, Guid actorId, string targetNodeId, string? comment = null)
        => SendBackAsync(taskId, actorId, new SendBackTarget("node", targetNodeId), comment);

    public async Task SendBackAsync(Guid taskId, Guid actorId, SendBackTarget target, string? comment = null)
    {
        var task = await _db.Wf_FlowTasks.FirstOrDefaultAsync(t => t.Id == taskId)
                   ?? throw new InvalidOperationException("任务不存在");
        if (task.Status != FlowTaskStatus.Pending) return;   // 幂等闸门
        var inst = await _db.Wf_FlowInstances.FirstOrDefaultAsync(i => i.Id == task.InstanceId);
        if (inst is null || inst.Status != FlowInstanceStatus.Running) return;
        var schema = await LoadSchemaAsync(inst.FlowKey);

        switch ((target.Kind ?? "node").Trim().ToLowerInvariant())
        {
            case "node":      await SendBackToNodeAsync(inst, schema, task, actorId, target.NodeId, comment); break;
            case "prevstage": await SendBackToPrevStageAsync(inst, schema, task, actorId, comment); break;   // T6
            case "starter":   await SendBackToStarterAsync(inst, task, actorId, comment); break;             // T6
            default: throw new InvalidOperationException("E-WF-012");
        }
        await _db.SaveChangesAsync();
    }

    private async Task SendBackToNodeAsync(Wf_FlowInstance inst, FlowSchema schema, Wf_FlowTask task,
        Guid actorId, string? targetNodeId, string? comment)
    {
        var target = FindNode(schema, targetNodeId ?? "") ?? throw new InvalidOperationException("E-WF-012");
        // 收紧:Type∈approval/start、非 end、非当前节点、上游可达(BFS 反向)、非跨并行块
        if (IsType(target, "end") || target.Id == task.NodeId) throw new InvalidOperationException("E-WF-012");
        var tt = (target.Type ?? "approval").Trim().ToLowerInvariant();
        if (tt != "approval" && tt != "start") throw new InvalidOperationException("E-WF-012");
        if (!IsUpstreamReachable(schema, target.Id, task.NodeId)) throw new InvalidOperationException("E-WF-012");
        if (CrossesParallelBlock(schema, target.Id, task.NodeId)) throw new InvalidOperationException("E-WF-012");

        var live = await _db.Wf_FlowTasks
            .Where(t => t.InstanceId == inst.Id && (t.Status == FlowTaskStatus.Pending || t.Status == FlowTaskStatus.Suspended))
            .ToListAsync();
        foreach (var t in live) t.Status = FlowTaskStatus.Cancelled;
        AddHistory(inst.Id, task.NodeId, actorId, "sendback", comment ?? $"退回至 {target.Id}");
        CancelAllActiveTokens(inst.Id);
        VoidPendingFormTos(inst.Id);
        var sbToken = SpawnToken(inst, target, parent: null, fork: null);
        await EnterNodeAsync(inst, schema, target, sbToken);
    }
```
加两个纯静态图助手(同文件或 `FlowEngine` 私有):
```csharp
    /// <summary>target 能否沿边正向到达 current(即 target 是 current 的上游)。</summary>
    private static bool IsUpstreamReachable(FlowSchema schema, string targetId, string currentId)
    {
        var seen = new HashSet<string> { targetId };
        var q = new Queue<string>(); q.Enqueue(targetId);
        while (q.Count > 0)
        {
            var cur = q.Dequeue();
            foreach (var e in schema.Edges.Where(e => e.From == cur))
                if (e.To == currentId) return true;
                else if (seen.Add(e.To)) q.Enqueue(e.To);
        }
        return false;
    }
    /// <summary>v1:target→current 路径上若经过 parallelSplit/parallelJoin 则视为跨并行块,禁止。</summary>
    private static bool CrossesParallelBlock(FlowSchema schema, string targetId, string currentId)
    {
        // 简化:任一并行网关存在于 target 可达且能达 current 的中间集 → 跨块。MVP 保守:图含并行网关即按节点类型判中间路径。
        var between = NodesBetween(schema, targetId, currentId);
        return between.Any(id => schema.Nodes.Any(n => n.Id == id &&
            ((n.Type ?? "").Equals("parallelSplit", StringComparison.OrdinalIgnoreCase) ||
             (n.Type ?? "").Equals("parallelJoin", StringComparison.OrdinalIgnoreCase))));
    }
    private static HashSet<string> NodesBetween(FlowSchema schema, string fromId, string toId)
    {
        // target 正向可达集 ∩ 能反向到达 current 的集(粗略中间节点集)
        var fwd = new HashSet<string>(); var q = new Queue<string>(); q.Enqueue(fromId);
        while (q.Count > 0) { var c = q.Dequeue(); foreach (var e in schema.Edges.Where(e => e.From == c)) if (fwd.Add(e.To)) q.Enqueue(e.To); }
        fwd.Remove(toId);
        return fwd;
    }
```
> `IsType` 已是既有私有助手(`SendBackAsync` 原体用过)。若 `FindNode`/`IsType` 不在 `AdvancedFlow.cs` 可见域,它们是 `FlowEngine` partial 私有成员,本 partial 直接可用。

- [ ] **Step 5: 跑测确认通过 + Wf 回归闸**

Run: `dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~SerialSendBackTests"` → passed。
Run: `dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~Wf"` → 既有 `AdvancedFlowTests`(退到上游 n1 / nope / end)全绿(上游 n1 通过收紧校验;nope=FindNode null→E-WF-012;end→E-WF-012,断言类型仍 `InvalidOperationException`,兼容)。

- [ ] **Step 6: 提交**

```bash
git add CP6.Core/Services/Wf/IFlowEngine.cs CP6.Core/Services/Wf/AdvancedFlow.cs CP6.Tests/Wf/SerialSendBackTests.cs
git commit -m "feat(wfs-serial): T5 SendBackTarget 三目标骨架+旧重载转发node+node收紧校验(上游可达/Type/非跨并行块 E-WF-012)"
```

---

## Task 6: prevStage 状态机 + starter 退回 + Void 档过滤

**Files:**
- Modify: `CP6.Core/Services/Wf/AdvancedFlow.cs`(`SendBackToPrevStageAsync` + `SendBackToStarterAsync`)
- Modify: `CP6.Core/Services/Wf/FlowEngine.ReadModel.cs:142`(`VoidPendingFormTos` 加可选 stage 过滤 + prevStage 用 SentBack)
- Test: `CP6.Tests/Wf/SerialSendBackTests.cs`(prevStage 多轮隔离 + starter)

- [ ] **Step 1: 写失败测试(重复 prevStage 多轮计票隔离 R12 + 第0档 prevStage 报错 + starter)**

加入 `SerialSendBackTests.cs`(复用 SerialSignTests 的 `SeedSerialFixed3Async` 同款种子,这里内联一份 helper 或提取到共享):
```csharp
    private static async Task SeedSerial3(CP6Context db, Guid s1, Guid s2, Guid s3)
    {
        var schema = new FlowSchema { Start = "ap", Nodes =
        {
            new FlowNode { Id = "ap", Type = "approval", Stages = new()
            {
                new ApprovalStage { Kind="fixed", ApproverStrategy="Specified", ApproverUserId=s1, Countersign="all", Name="档1" },
                new ApprovalStage { Kind="fixed", ApproverStrategy="Specified", ApproverUserId=s2, Countersign="all", Name="档2" },
                new ApprovalStage { Kind="fixed", ApproverStrategy="Specified", ApproverUserId=s3, Countersign="all", Name="档3" },
            }},
            new FlowNode { Id = "end", Type = "end" },
        }, Edges = { new(){From="ap",To="end"} } };
        db.Wf_FlowDefs.Add(new() { Id=Guid.NewGuid(), FlowKey="ser3", FlowName="x", FormKey="t",
            SchemaJson=JsonSerializer.Serialize(schema), Version=1, Enable=true });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task PrevStage_RepeatedRounds_TallyIsolated()
    {
        using var db = Db();
        Guid s1=Guid.NewGuid(), s2=Guid.NewGuid(), s3=Guid.NewGuid();
        await SeedSerial3(db, s1, s2, s3);
        var instId = await Eng(db).SubmitAsync("ser3", Guid.NewGuid(), "{}");

        // 档0过 → 档1过 → 档2
        var t0 = await db.Wf_FlowTasks.SingleAsync(t => t.StageIndex==0 && t.Status==FlowTaskStatus.Pending);
        await Eng(db).ActAsync(t0.Id, s1, true);
        var t1 = await db.Wf_FlowTasks.SingleAsync(t => t.StageIndex==1 && t.Status==FlowTaskStatus.Pending);
        await Eng(db).ActAsync(t1.Id, s2, true);
        var t2 = await db.Wf_FlowTasks.SingleAsync(t => t.StageIndex==2 && t.Status==FlowTaskStatus.Pending);

        // 档2 退回上一档(档1)→ 档1 第2轮(StageRound=1)
        await Eng(db).SendBackAsync(t2.Id, s3, new SendBackTarget("prevStage"), "再核");
        Assert.Equal(FlowTaskStatus.Cancelled, (await db.Wf_FlowTasks.SingleAsync(t => t.Id==t2.Id)).Status);  // 当前档 cancelled
        var t1r1 = await db.Wf_FlowTasks.SingleAsync(t => t.StageIndex==1 && t.Status==FlowTaskStatus.Pending);
        Assert.Equal(1, t1r1.StageRound);   // 新轮
        Assert.NotEqual(t1.Id, t1r1.Id);

        // 档1 第2轮再过 → 档2 第1轮(新)再生成。关键:计票只看当前轮,旧轮 Approved t1 不串入
        await Eng(db).ActAsync(t1r1.Id, s2, true);
        var t2b = await db.Wf_FlowTasks.SingleAsync(t => t.StageIndex==2 && t.Status==FlowTaskStatus.Pending);
        Assert.Equal(FlowInstanceStatus.Running, (await db.Wf_FlowInstances.SingleAsync(i=>i.Id==instId)).Status);

        // 档2 再退档1 → 档1 第3轮 → 再过 → 档2 第... ;最后档2过 → Approved
        await Eng(db).SendBackAsync(t2b.Id, s3, new SendBackTarget("prevStage"));
        var t1r2 = await db.Wf_FlowTasks.SingleAsync(t => t.StageIndex==1 && t.Status==FlowTaskStatus.Pending);
        Assert.Equal(2, t1r2.StageRound);
        await Eng(db).ActAsync(t1r2.Id, s2, true);
        var t2c = await db.Wf_FlowTasks.SingleAsync(t => t.StageIndex==2 && t.Status==FlowTaskStatus.Pending);
        await Eng(db).ActAsync(t2c.Id, s3, true);
        Assert.Equal(FlowInstanceStatus.Approved, (await db.Wf_FlowInstances.SingleAsync(i=>i.Id==instId)).Status);

        // 档1 历史各轮 Approved 留痕(轮 0/1/2 各一)
        Assert.Equal(3, await db.Wf_FlowTasks.CountAsync(t => t.StageIndex==1 && t.Status==FlowTaskStatus.Approved));
    }

    [Fact]
    public async Task PrevStage_AtStage0_Throws_E_WF_012()
    {
        using var db = Db();
        Guid s1=Guid.NewGuid(), s2=Guid.NewGuid(), s3=Guid.NewGuid();
        await SeedSerial3(db, s1, s2, s3);
        await Eng(db).SubmitAsync("ser3", Guid.NewGuid(), "{}");
        var t0 = await db.Wf_FlowTasks.SingleAsync(t => t.StageIndex==0 && t.Status==FlowTaskStatus.Pending);
        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => Eng(db).SendBackAsync(t0.Id, s1, new SendBackTarget("prevStage")));
        Assert.Contains("E-WF-012", e.Message);
    }

    [Fact]
    public async Task SendBackStarter_ReturnsToDraft_TerminatesTokens()
    {
        using var db = Db();
        Guid s1=Guid.NewGuid(), s2=Guid.NewGuid(), s3=Guid.NewGuid();
        await SeedSerial3(db, s1, s2, s3);
        var starter = Guid.NewGuid();
        var instId = await Eng(db).SubmitAsync("ser3", starter, "{}");
        var t0 = await db.Wf_FlowTasks.SingleAsync(t => t.StageIndex==0 && t.Status==FlowTaskStatus.Pending);

        await Eng(db).SendBackAsync(t0.Id, s1, new SendBackTarget("starter"), "请补充");
        var inst = await db.Wf_FlowInstances.SingleAsync(i => i.Id==instId);
        Assert.Equal(FlowInstanceStatus.Draft, inst.Status);
        Assert.Equal(0, await db.Wf_FlowTokens.CountAsync(t => t.InstanceId==instId && t.Status==FlowTokenStatus.Active));
        Assert.Equal(0, await db.Wf_FlowFormTos.CountAsync(f => f.InstanceId==instId && f.Status==FlowFormToStatus.Pending));

        // 发起人重提 → 从头(档0)重跑
        await Eng(db).StartDraftAsync(instId, starter);
        Assert.Equal(FlowInstanceStatus.Running, (await db.Wf_FlowInstances.SingleAsync(i=>i.Id==instId)).Status);
        Assert.Equal(1, await db.Wf_FlowTasks.CountAsync(t => t.StageIndex==0 && t.Status==FlowTaskStatus.Pending && t.AssigneeId==s1));
    }
```

- [ ] **Step 2: 跑测确认失败**

Run: `dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~SerialSendBackTests"`
Expected: prevStage/starter 测失败(方法体未实现,`SendBackToPrevStageAsync`/`SendBackToStarterAsync` 抛 NotImplemented 或缺失)。

- [ ] **Step 3: VoidPendingFormTos 加可选 stage 过滤 + SentBack 变体**

`FlowEngine.ReadModel.cs:142` 改为可选 stage 过滤,并加一个 prevStage 专用置 SentBack 的助手:
```csharp
    internal void VoidPendingFormTos(Guid instanceId, string? nodeId = null, Guid? tokenId = null,
        int? stageIndex = null, int? stageRound = null, int newStatus = FlowFormToStatus.Voided)
    {
        bool Match(Wf_FlowFormTo f) => f.InstanceId == instanceId && f.Status == FlowFormToStatus.Pending
            && (nodeId == null || f.NodeId == nodeId) && (tokenId == null || f.TokenId == tokenId)
            && (stageIndex == null || f.StageIndex == stageIndex) && (stageRound == null || f.StageRound == stageRound);
        foreach (var f in _db.Wf_FlowFormTos.Local.Where(Match).ToList()) f.Status = newStatus;
        var localIds = _db.Wf_FlowFormTos.Local.Where(f => f.InstanceId == instanceId).Select(f => f.Id).ToHashSet();
        foreach (var f in _db.Wf_FlowFormTos
            .Where(f => f.InstanceId == instanceId && f.Status == FlowFormToStatus.Pending && !localIds.Contains(f.Id)
                        && (nodeId == null || f.NodeId == nodeId) && (tokenId == null || f.TokenId == tokenId)
                        && (stageIndex == null || f.StageIndex == stageIndex) && (stageRound == null || f.StageRound == stageRound)).ToList())
            f.Status = newStatus;
    }
```
> 既有调用 `VoidPendingFormTos(inst.Id)` 不变(全实例 Voided)。

- [ ] **Step 4: prevStage + starter 实现**

`AdvancedFlow.cs` 加:
```csharp
    private async Task SendBackToPrevStageAsync(Wf_FlowInstance inst, FlowSchema schema, Wf_FlowTask task,
        Guid actorId, string? comment)
    {
        if (task.StageIndex <= 0) throw new InvalidOperationException("E-WF-012");   // 第 0 档无上一档
        var node = FindNode(schema, task.NodeId) ?? throw new InvalidOperationException("E-WF-012");

        // ① 本档本轮全部在途/挂起任务 → Cancelled
        var cur = await _db.Wf_FlowTasks.Where(t => t.InstanceId == inst.Id && t.NodeId == task.NodeId
            && t.TokenId == task.TokenId && t.StageIndex == task.StageIndex && t.StageRound == task.StageRound
            && (t.Status == FlowTaskStatus.Pending || t.Status == FlowTaskStatus.Suspended)).ToListAsync();
        foreach (var t in cur) t.Status = FlowTaskStatus.Cancelled;
        // ② 本档本轮 Pending 履历 → SentBack(非 Voided)
        VoidPendingFormTos(inst.Id, task.NodeId, task.TokenId, task.StageIndex, task.StageRound, FlowFormToStatus.SentBack);
        AddHistory(inst.Id, task.NodeId, actorId, "sendback", comment ?? $"退回上一档(档{task.StageIndex}→{task.StageIndex - 1})");

        // ③ 读冻结计划,重建上一档(StageRound 自推导 +1),token 不 terminate
        var tok = await _db.Wf_FlowTokens.FirstAsync(t => t.Id == task.TokenId);
        var plan = JsonSerializer.Deserialize<List<RuntimeApprovalStage>>(tok.StagePlanJson!, JsonOpts)!;
        await EnterStageAsync(inst, schema, node, tok, plan, task.StageIndex - 1);
    }

    private async Task SendBackToStarterAsync(Wf_FlowInstance inst, Wf_FlowTask task, Guid actorId, string? comment)
    {
        var live = await _db.Wf_FlowTasks.Where(t => t.InstanceId == inst.Id
            && (t.Status == FlowTaskStatus.Pending || t.Status == FlowTaskStatus.Suspended)).ToListAsync();
        foreach (var t in live) t.Status = FlowTaskStatus.Cancelled;
        AddHistory(inst.Id, task.NodeId, actorId, "sendback", comment ?? "退回发起人重填");
        CancelAllActiveTokens(inst.Id);
        VoidPendingFormTos(inst.Id);                              // 全实例 Pending → Voided
        inst.Status = FlowInstanceStatus.Draft;                  // 回草稿,发起人改后 StartDraftAsync 从头跑
        inst.ModifyDate = DateTime.Now;
    }
```
> `JsonOpts` 是 `FlowEngine.cs:14` 私有静态,本 partial 可用。`EnterStageAsync` 是 T3 加的 `FlowEngine` internal 方法,本 partial 可直接调。

- [ ] **Step 5: 跑测确认通过 + Wf 回归闸**

Run: `dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~SerialSendBackTests"` → 全 passed(尤其 `PrevStage_RepeatedRounds_TallyIsolated` 验旧轮不串台)。
Run: `dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~Wf"` → 既有全绿。

- [ ] **Step 6: 提交**

```bash
git add CP6.Core/Services/Wf/AdvancedFlow.cs CP6.Core/Services/Wf/FlowEngine.ReadModel.cs CP6.Tests/Wf/SerialSendBackTests.cs
git commit -m "feat(wfs-serial): T6 prevStage 状态机(当前档Cancelled+FormTo SentBack+上档StageRound+1冻结计划重建)+starter回Draft+VoidPendingFormTos档过滤;重复退回多轮计票隔离(R12)"
```

---

# P-C 读模型 / forecast / 信箱

## Task 7: ForecastService 按档展开(复用 planner)

**Files:**
- Modify: `CP6.Core/Services/Oa/ForecastService.cs:54-85`(approval 分支复用 `IApprovalStagePlanner`)
- Modify: `CP6.Core/Services/Oa/InboxModels.cs:40`(`ForecastStep` 位置记录尾加 `StageIndex?/StageName?`)
- Modify: `ForecastService` 构造注入 `IApprovalStagePlanner`
- Test: `CP6.Tests/Oa/ForecastServiceTests.cs`(加串簽展开)

- [ ] **Step 1: 写失败测试**

加入 `CP6.Tests/Oa/ForecastServiceTests.cs`(复用其既有 harness):
```csharp
    [Fact]
    public async Task Forecast_ExpandsSerialStages()
    {
        using var db = NewDb();   // 沿用文件既有 helper
        Guid s1=Guid.NewGuid(), s2=Guid.NewGuid();
        var schema = new FlowSchema { Start="ap", Nodes =
        {
            new FlowNode { Id="ap", Type="approval", Stages = new()
            {
                new ApprovalStage { Kind="fixed", ApproverStrategy="Specified", ApproverUserId=s1, Name="档1" },
                new ApprovalStage { Kind="fixed", ApproverStrategy="Specified", ApproverUserId=s2, Name="档2" },
            }},
            new FlowNode { Id="end", Type="end" },
        }, Edges = { new(){From="ap",To="end"} } };
        db.Wf_FlowDefs.Add(new() { Id=Guid.NewGuid(), FlowKey="fs", FlowName="x", FormKey="t",
            SchemaJson=JsonSerializer.Serialize(schema), Version=1, Enable=true });
        await db.SaveChangesAsync();

        var planner = new ApprovalStagePlanner(new ApproverResolver(db));
        var svc = new ForecastService(db, new ApproverResolver(db), planner);
        var res = await svc.ForecastAsync("fs", "{}", Guid.NewGuid());

        var approvalSteps = res.Steps.Where(s => s.Type == "approval").ToList();
        Assert.Equal(2, approvalSteps.Count);            // 一节点展成 2 档
        Assert.Equal(0, approvalSteps[0].StageIndex);
        Assert.Equal("档1", approvalSteps[0].StageName);
        Assert.Equal(1, approvalSteps[1].StageIndex);
    }
```

- [ ] **Step 2: 跑测确认失败**

Run: `dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~ForecastServiceTests"`
Expected: 编译失败(`ForecastService` 构造无 planner 参 / `ForecastStep.StageIndex` 缺失)。

- [ ] **Step 3: ForecastStep 增字段**

`InboxModels.cs:40` 的 `ForecastStep` 位置记录尾加两个可选参(保既有位置构造兼容):
```csharp
public record ForecastStep(string NodeId, string? NodeName, string Type, IReadOnlyList<string> Approvers,
    bool Resolved, string? Note, int? StageIndex = null, string? StageName = null);
```
> 既有 `ForecastService` 里 `new ForecastStep(node.Id, node.Name, "end", ..., true, null)` 等位置构造不受影响(新参有默认值)。

- [ ] **Step 4: 构造注入 planner + approval 分支展开**

`ForecastService.cs` 构造加 `IApprovalStagePlanner`:
```csharp
    private readonly IApprovalStagePlanner _planner;
    public ForecastService(CP6Context db, IApproverResolver approver, IApprovalStagePlanner planner)
    { _db = db; _approver = approver; _planner = planner; }
```
`default`(approval)分支(L54-59)改为:
```csharp
                default: // approval
                    var plan = await _planner.BuildAsync(new Wf_FlowInstance { StarterId = starterId }, schema, node);
                    foreach (var rs in plan)
                    {
                        var (names, resolved) = await ResolveRuleNamesAsync(rs.Rule, starterId);
                        steps.Add(new ForecastStep(node.Id, rs.StageName ?? node.Name, "approval", names, resolved,
                            resolved ? null : "审批人到达时解析", rs.StageIndex, rs.StageName));
                    }
                    cursor = NextNodeId(schema, cursor, varsJson);
                    break;
```
加 `ResolveRuleNamesAsync`(把既有 `ResolveApproverNamesAsync` 泛化为按 `ApproverRule`):
```csharp
    private async Task<(IReadOnlyList<string> Names, bool Resolved)> ResolveRuleNamesAsync(ApproverRule rule, Guid starterId)
    {
        try
        {
            var res = await _approver.ResolveAsync(rule, new ApproverResolveContext { StarterUserId = starterId });
            if (!res.Resolved) return (Array.Empty<string>(), false);
            var names = await OaUserNames.ResolveAsync(_db, res.ApproverIds);
            return (res.ApproverIds.Select(id => names.GetValueOrDefault(id, id.ToString())).ToList(), true);
        }
        catch { return (Array.Empty<string>(), false); }
    }
```
> DI:`ForecastService` 在 Program.cs 已注册;新增构造参 `IApprovalStagePlanner` 由 T2 的 DI 注册自动注入(生产无忧)。
> **⚠️ 连带破点(必修)**:测试里手动 `new ForecastService(db, new ApproverResolver(db))` 的 `Forecast(db)` 助手会因构造签名变化编译失败 —— 须同步改 **`CP6.Tests/Oa/InboxServiceTests.cs:19` 附近**、**`CP6.Tests/Oa/QueryServiceTests.cs:19` 附近**、`ForecastServiceTests.cs` 的 `Forecast(db)` 助手为 `new ForecastService(db, new ApproverResolver(db), new ApprovalStagePlanner(new ApproverResolver(db)))`。改完跑 `--filter "FullyQualifiedName~Oa"` 全绿。

- [ ] **Step 5: 跑测 + Wf/Oa 回归 + 提交**

Run: `dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~ForecastServiceTests"` → passed。
Run: `dotnet test CP6.Tests/CP6.Tests.csproj` → 全量基线不降(+新测)。
```bash
git add CP6.Core/Services/Oa/ForecastService.cs CP6.Core/Services/Oa/IForecastService.cs CP6.Tests/Oa/ForecastServiceTests.cs
git commit -m "feat(wfs-serial): T7 ForecastService 复用 IApprovalStagePlanner 按档展开(一节点 N 步 forecast)+ForecastStep stageIndex/stageName"
```

---

## Task 8: DTO 档字段(待办/timeline)

**Files:**
- Modify: `CP6.Core/Services/Oa/InboxModels.cs:6-8`(`InboxPendingItem` 位置记录尾加档字段)、`:28-30`(`TimelineRow` 尾加 `StageIndex?/StageRound?`)
- Modify: `CP6.Core/Services/Oa/InboxService.cs:31`(`new InboxPendingItem(...)` 投影补字段)+ timeline 投影处(grep `new TimelineRow(`)
- Test: `CP6.Tests/Oa/SerialInboxDtoTests.cs`(新建)

- [ ] **Step 1: 写失败测试**

`CP6.Tests/Oa/SerialInboxDtoTests.cs`(参照 `InboxServiceTests.cs` harness:`new InboxService(db, Engine(db), Forecast(db))`,Engine/Forecast 助手照搬该文件,Forecast 须带 planner):
```csharp
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Oa;

public class SerialInboxDtoTests
{
    private static CP6Context Db() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static FlowEngine Engine(CP6Context db) => new(db, new ApproverResolver(db));
    private static ForecastService Forecast(CP6Context db) =>
        new(db, new ApproverResolver(db), new ApprovalStagePlanner(new ApproverResolver(db)));
    private static IInboxService Inbox(CP6Context db) => new InboxService(db, Engine(db), Forecast(db));

    [Fact]
    public async Task PendingItem_CarriesStageFields_AndCanSendBackPrevStage()
    {
        using var db = Db();
        Guid s1 = Guid.NewGuid(), s2 = Guid.NewGuid();
        var schema = new FlowSchema { Start = "ap", Nodes =
        {
            new FlowNode { Id = "ap", Type = "approval", Stages = new()
            {
                new ApprovalStage { Kind="fixed", ApproverStrategy="Specified", ApproverUserId=s1, Countersign="all", Name="档1" },
                new ApprovalStage { Kind="fixed", ApproverStrategy="Specified", ApproverUserId=s2, Countersign="all", Name="档2" },
            }},
            new FlowNode { Id = "end", Type = "end" },
        }, Edges = { new(){From="ap",To="end"} } };
        db.Wf_FlowDefs.Add(new() { Id=Guid.NewGuid(), FlowKey="ser2", FlowName="x", FormKey="t",
            SchemaJson=JsonSerializer.Serialize(schema), Version=1, Enable=true });
        await db.SaveChangesAsync();

        var instId = await Engine(db).SubmitAsync("ser2", Guid.NewGuid(), "{}");
        // 档0 是 s1:CanSendBackPrevStage=false
        var p0 = (await Inbox(db).PendingAsync(s1)).Single(i => i.InstanceId == instId);
        Assert.Equal(0, p0.StageIndex);
        Assert.False(p0.CanSendBackPrevStage);

        // 档0 过 → 档1(s2):StageIndex=1、CanSendBackPrevStage=true、StageName=档2
        var t0 = await db.Wf_FlowTasks.SingleAsync(t => t.StageIndex == 0 && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(t0.Id, s1, true);
        var p1 = (await Inbox(db).PendingAsync(s2)).Single(i => i.InstanceId == instId);
        Assert.Equal(1, p1.StageIndex);
        Assert.True(p1.CanSendBackPrevStage);
        Assert.Equal("档2", p1.StageName);
    }
}
```

- [ ] **Step 2: 跑测确认失败**

Run: `dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~SerialInboxDtoTests"`
Expected: 编译失败(`InboxPendingItem` 无 `StageIndex`/`CanSendBackPrevStage`)。

- [ ] **Step 3: 位置记录尾加可选字段**

`InboxModels.cs` `InboxPendingItem`(L6-8)尾加:
```csharp
public record InboxPendingItem(Guid TaskId, Guid InstanceId, Guid? TokenId, string FlowKey, string? FlowName,
    string NodeId, string? NodeName, Guid StarterId, string StarterName, string? BizType, string? BizId,
    bool IsRead, DateTime SentAt,
    int StageIndex = 0, int StageRound = 0, string? StageName = null, string? StageCode = null, bool CanSendBackPrevStage = false);
```
`TimelineRow`(L28-30)尾加 `int? StageIndex = null, int? StageRound = null`。

- [ ] **Step 4: 投影补字段**

`InboxService.cs:31` 的 `new InboxPendingItem(...)`:在末尾按命名实参补
```csharp
            StageIndex: x.StageIndex, StageRound: x.StageRound,
            StageName: StageNameFromPlan(x), StageCode: StageCodeFromPlan(x),
            CanSendBackPrevStage: x.StageIndex > 0
```
其中 `x` 为投影源(Wf_FlowTask 或其匿名投影,落码按实际变量名)。`StageName/StageCode` 取法:从该 task 的 token `StagePlanJson` 反序列化 `List<RuntimeApprovalStage>` 按 `StageIndex` 取 `StageName/StageCode`(token 已在查询范围则内存取;否则一次性按 instance 批量载 token 字典)。timeline 投影 `new TimelineRow(...)` 尾加 `f.StageIndex, f.StageRound`(f=Wf_FlowFormTo)。

- [ ] **Step 5: 跑测 + Oa/全量回归 + 提交**

Run: `dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~SerialInboxDtoTests"` → passed。
Run: `dotnet test CP6.Tests/CP6.Tests.csproj` → 全量绿。
```bash
git add CP6.Core/Services/Oa/InboxModels.cs CP6.Core/Services/Oa/InboxService.cs CP6.Tests/Oa/SerialInboxDtoTests.cs
git commit -m "feat(wfs-serial): T8 InboxPendingItem/TimelineRow 增档字段(StageIndex/StageRound/StageName/StageCode/CanSendBackPrevStage)+投影装配"
```

---

## Task 9: 前端 信箱(退回选择器 + timeline 档·轮分组)

**Files:**
- Modify: `cp6.web/src/api/oa/*.ts` / `types/oa/*.ts`(退回 target + 档字段类型)
- Modify: `cp6.web/src/views/oa/.../FormDetail*.vue`(退回下拉:上一档/发起人/指定节点)
- Modify: `cp6.web/src/views/oa/.../FlowTimeline*.vue`(同节点多档·多轮分组「第 K 档·第 R 轮」)
- Test: 既有前端 vitest(若有信箱 model 纯逻辑)+ type-check/build

- [ ] **Step 1: 类型 + API**

`types/oa` 待办/详情项加 `stageIndex/stageRound/stageName/stageCode/canSendBackPrevStage`;退回 API body 加 `target: { kind: 'prevStage'|'starter'|'node', nodeId?: string }`。

- [ ] **Step 2: FormDetail 退回选择器**

操作条「退回」改为带子菜单/下拉:`canSendBackPrevStage` 时显「退回上一档」(`kind:'prevStage'`)、恒显「退回发起人」(`kind:'starter'`)、「退回到…」列上游节点(`kind:'node', nodeId`)。提交调退回 API。

- [ ] **Step 3: FlowTimeline 档·轮分组**

渲染 `Wf_FlowFormTo` 项时,同 `nodeId` 按 `(stageIndex,stageRound)` 分组,标题「第 {stageIndex+1} 档 · 第 {stageRound+1} 轮 · {stageName}」;`Status==7(SentBack)` 显「已退回」标签。

- [ ] **Step 4: 校验闸**

Run(`cp6.web/`):`npm run type-check` → 0 err;`npx vitest run`(若有信箱 model 测)→ pass;`npm run build` → 绿。

- [ ] **Step 5: 提交**

```bash
git add cp6.web/src/api/oa cp6.web/src/types/oa cp6.web/src/views/oa
git commit -m "feat(wfs-serial): T9 前端信箱 退回选择器(上一档/发起人/指定节点)+timeline 档·轮分组+SentBack 标签"
```

---

# P-D 设计器

## Task 10: designerModel stages 往返 + validateClient 镜像

**Files:**
- Modify: `cp6.web/src/views/oa/designer/designerModel.ts`(`SchemaNode.stages` + 透传 + `validateClient` 镜像)
- Test: `cp6.web/src/views/oa/designer/designerModel.test.ts`(加串簽往返 + 校验用例)

- [ ] **Step 1: 写失败 vitest**

加入 `designerModel.test.ts`:
```ts
it('round-trips serial stages', () => {
  const schema: FlowSchemaDto = { start: 'ap', nodes: [
    { id: 'ap', type: 'approval', stages: [
      { kind: 'fixed', approverStrategy: 'Specified', countersign: 'all', name: '档1' },
      { kind: 'managerChain', maxLevels: 2, countersign: 'all', name: '逐级' },
    ] },
    { id: 'end', type: 'end' },
  ], edges: [{ from: 'ap', to: 'end' }] }
  const g = schemaToGraph(schema)
  const back = graphToSchema(g.nodes, g.edges)
  expect(back.nodes.find(n => n.id === 'ap')!.stages).toHaveLength(2)
  expect(back.nodes.find(n => n.id === 'ap')!.stages![1].maxLevels).toBe(2)
})

it('validateClient flags invalid stage', () => {
  const schema: FlowSchemaDto = { start: 'ap', nodes: [
    { id: 'start', type: 'start' },
    { id: 'ap', type: 'approval', stages: [{ kind: 'managerChain' /* 缺 maxLevels */, countersign: 'all' }] },
    { id: 'end', type: 'end' },
  ], edges: [{ from: 'start', to: 'ap' }, { from: 'ap', to: 'end' }] }
  expect(validateClient(schema)).toContain('oa.designer.errStageInvalid')
})
```

- [ ] **Step 2: 跑测确认失败**

Run(`cp6.web/`):`npx vitest run src/views/oa/designer/designerModel.test.ts`
Expected: FAIL(`stages` 未透传 / 校验无该规则)。

- [ ] **Step 3: SchemaNode 增 stages + ApprovalStage 类型**

`designerModel.ts` `SchemaNode`(L3-11)加 + 新接口:
```ts
export interface ApprovalStageDto {
  name?: string; code?: string; kind: 'fixed' | 'managerChain'
  approverStrategy?: string; approverLevels?: number; approverRoleId?: number; approverUserId?: string
  countersign?: 'all' | 'any' | 'veto'; maxLevels?: number
}
// SchemaNode 增:
  stages?: ApprovalStageDto[]
```
`schemaToGraph`/`graphToSchema` 经 `...n`/`...(n.data)` 已透传对象字段 → `stages` 自动随带(vitest 验证)。

- [ ] **Step 4: validateClient 加镜像规则**

`validateClient`(L61)末尾加:
```ts
  for (const n of nodes) {
    if (n.type === 'approval' && n.stages && n.stages.length) {
      for (const s of n.stages) {
        const ok = s.kind === 'managerChain'
          ? (s.maxLevels ?? 0) >= 1
          : !!s.approverStrategy
        const cs = !s.countersign || ['all','any','veto'].includes(s.countersign)
        if (!ok || !cs) { errs.push('oa.designer.errStageInvalid'); break }
      }
    }
  }
```

- [ ] **Step 5: 跑测确认通过 + type-check**

Run: `npx vitest run src/views/oa/designer/designerModel.test.ts` → pass;`npm run type-check` → 0 err。

- [ ] **Step 6: 提交**

```bash
git add cp6.web/src/views/oa/designer/designerModel.ts cp6.web/src/views/oa/designer/designerModel.test.ts
git commit -m "feat(wfs-serial): T10 designerModel stages 往返透传 + validateClient 镜像档校验(errStageInvalid)"
```

---

## Task 11: 设计器「串簽档位」面板 + 后端校验

**Files:**
- Modify: `cp6.web/src/views/oa/designer/NodePropertyPanel.vue`(「串簽档位」段)
- Modify: `CP6.Core/Services/Wf/FlowSchemaValidator.cs:26-27`(串簽档规则 E-WF-011)
- Test: `CP6.Tests/Wf/SerialSignTests.cs`(校验规则单测)+ 前端 build

- [ ] **Step 1: 写失败测试(后端校验)**

加入 `SerialSignTests.cs`:
```csharp
    [Fact]
    public void Validator_FlagsBadStage_E_WF_011()
    {
        var schema = new FlowSchema { Nodes =
        {
            new FlowNode { Id="start", Type="start" },
            new FlowNode { Id="ap", Type="approval", Stages = new()
            {
                new ApprovalStage { Kind="managerChain" /* 缺 MaxLevels */, Countersign="all" },
            }},
            new FlowNode { Id="end", Type="end" },
        }, Edges = { new(){From="start",To="ap"}, new(){From="ap",To="end"} } };
        Assert.Contains("E-WF-011", FlowSchemaValidator.Validate(schema));
    }

    [Fact]
    public void Validator_GoodSerialStages_NoStageError()
    {
        var schema = new FlowSchema { Nodes =
        {
            new FlowNode { Id="start", Type="start" },
            new FlowNode { Id="ap", Type="approval", Stages = new()
            {
                new ApprovalStage { Kind="fixed", ApproverStrategy="Specified", Countersign="all" },
                new ApprovalStage { Kind="managerChain", MaxLevels=2, Countersign="any" },
            }},
            new FlowNode { Id="end", Type="end" },
        }, Edges = { new(){From="start",To="ap"}, new(){From="ap",To="end"} } };
        Assert.DoesNotContain("E-WF-011", FlowSchemaValidator.Validate(schema));
    }
```

- [ ] **Step 2: 跑测确认失败**

Run: `dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~SerialSignTests"` → `Validator_*` 失败。

- [ ] **Step 3: FlowSchemaValidator 加串簽规则**

`FlowSchemaValidator.cs` 规则⑤(L26-27)后加:
```csharp
        // ⑦ 串簽档配置(E-WF-011):有 Stages 时每档合法
        foreach (var n in schema.Nodes.Where(n => T(n) == "approval" && n.Stages is { Count: > 0 }))
        {
            foreach (var st in n.Stages!)
            {
                var kind = (st.Kind ?? "fixed").Trim().ToLowerInvariant();
                bool ruleOk = kind == "managerchain"
                    ? st.MaxLevels is int ml && ml >= 1
                    : st.ApproverStrategy is not null && KnownStrategies.Contains(st.ApproverStrategy);
                var cs = (st.Countersign ?? "all").Trim().ToLowerInvariant();
                bool csOk = cs is "all" or "any" or "veto";
                if (!ruleOk || !csOk) { errs.Add("E-WF-011"); break; }
            }
        }
```
> 注:规则⑤(单档须策略)只在 `Stages` 为空时判 —— 把 L26-27 的 foreach 条件加 `&& (n.Stages is null || n.Stages.Count == 0)`,避免串簽节点(节点级 ApproverStrategy 为空)误报 E-WF-010。

- [ ] **Step 4: NodePropertyPanel「串簽档位」段**

`NodePropertyPanel.vue` 审批节点属性加一段(`el-collapse-item` 标题「串簽档位」):
- `el-switch` 启用串簽(切换写/清 `node.stages`);
- 启用后 `el-table`/卡片列表渲染 `node.stages`,每行:档型 `el-radio`(固定/逐级)、固定时审批人策略+角色/用户(复用 `TransferDialog`/属性面板既有 `userApi/roleApi` 远程搜索)、逐级时 `el-input-number` MaxLevels、会签 `el-select`(all/any/veto)、档名 `el-input`;
- 行操作:上移/下移/删除 + 底部「+ 加档」;
- `ApproverLevels`(固定 DirectManager)与 `MaxLevels`(逐级)各带 `el-tooltip` 文案区分(spec R7)。
- 修改即 emit patchNode(写回 `node.stages`)。

- [ ] **Step 5: 跑测 + 前端 build**

Run: `dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~SerialSignTests"` → passed。
Run(`cp6.web/`):`npm run type-check && npm run build` → 绿。

- [ ] **Step 6: 提交**

```bash
git add CP6.Core/Services/Wf/FlowSchemaValidator.cs cp6.web/src/views/oa/designer/NodePropertyPanel.vue CP6.Tests/Wf/SerialSignTests.cs
git commit -m "feat(wfs-serial): T11 设计器串簽档位面板(增删改排序+固定/逐级+会签+MaxLevels tooltip)+FlowSchemaValidator 档规则 E-WF-011"
```

---

## Task 12: i18n 五语 seed + concat

**Files:**
- Create: `CP6.Core/.../I18nOaSerialSignScreenSeed.cs`(参照 `I18nOaDesignerScreenSeed` 静态 `Sys_Lang[]`)
- Modify: `CP6.WebApi/Program.cs`(concat 链带去重)
- Test: i18n check(前端)+ 启动装配

- [ ] **Step 1: grep 新键 + 去重核验**

Run(`cp6.web/`):`grep -rhoE "t\('oa\.(designer\.stage|detail\.sendback)[^']*'\)" src | sort -u`(收集本批新增 `oa.designer.stage.*` / 退回选择器键);与既有 `I18nOaInboxScreenSeed`/`I18nOaAdvancedScreenSeed`/`I18nOaDesignerScreenSeed`/`I18nOaNotify*` 比对去重(LangKey 不得撞)。

- [ ] **Step 2: 写 seed(五语)**

`I18nOaSerialSignScreenSeed.cs`,静态 `Items = new Sys_Lang[] { ... }`,每键 `LangKey/ZhCN/ZhTW/En/Ja/Ko`:`oa.designer.stage.enable/add/kind.fixed/kind.managerChain/maxLevels/countersign/name/...` + `oa.detail.sendback.prevStage/starter/node` + 3 错误码裸键 `E-WF-011/E-WF-012/E-WF-013`(五语文案,参照既有 `E-WF-00x` 风格)+ `oa.designer.errStageInvalid`。

- [ ] **Step 3: Program.cs concat**

Program.cs i18n seed concat 链加 `.Concat(I18nOaSerialSignScreenSeed.Items)`(带既有去重 by LangKey)。

- [ ] **Step 4: 校验**

启动后端(隔离库)`npm run i18n:pull && npm run gen-types && npm run i18n:check`(或与 T13 live QA 合并跑;若不起后端,至少 `npm run type-check` 用 `t()` 运行时键不触发 keys.generated 重生,过)。Run: `dotnet test CP6.Tests/CP6.Tests.csproj` 全量绿(seed 不破测)。

- [ ] **Step 5: 提交**

```bash
git add CP6.Core/ CP6.WebApi/Program.cs
git commit -m "feat(wfs-serial): T12 i18n 五语 seed(oa.designer.stage.*+退回选择器+E-WF-011/012/013)+Program.cs concat 去重"
```

---

# P-E gstack QA

## Task 13: gstack 真浏览器 QA 固化

**Files:**
- Create: `docs/superpowers/qa/wfs-serial-signing/README.md`(剧本)
- Create: `docs/superpowers/qa/wfs-serial-signing/seed.sql`(串簽流程定义 + 用户管理链)
- Create: scratchpad QA 脚本(HTTP e2e + 浏览器)

- [ ] **Step 1: 准备隔离库 + seed**

隔离库 `CP6DB_OA`(`localhost\KOUSQLSERVER`,与 Space 的 CP6DB 物理隔离)。seed:
- 串簽流程 `serial-demo`:approval 节点 Stages=[固定(admin)、逐级 managerChain MaxLevels=2、固定(总经理)] → end;
- 用户管理链(发起人 u0 → u1 → u2),总经理用户。
- 沿 phaseB/C QA harness 模式(`SET QUOTED_IDENTIFIER ON` 对带筛选索引表 DML)。

- [ ] **Step 2: HTTP e2e 剧本(PS5.1,ASCII)**

- 设计串簽流程保存(校验过,E-WF-011 反例)→ 发起 → 档0(admin)审 → 档1(逐级第1级)审 → 档2(逐级第2级)审 → 档3(总经理)审 → Approved;
- 退回上一档:档2 退回 → 档1 第2轮 → 再审;
- 退回发起人:档0 退回 → 实例 Draft → 重提从头;
- 驳回:某档驳回 → 整单 Rejected;
- 断言 DB:`Wf_FlowTask.StageIndex/StageRound`、`Wf_FlowFormTo`(SentBack/Skipped/Approved)、`Wf_FlowToken.StagePlanJson` 落、Token Consumed。
- (PS5.1 Invoke-RestMethod 400 错误体从 `Exception.Response.GetResponseStream()` 读;取 id `$r.data.data.id`。)

- [ ] **Step 3: 真浏览器(gstack headless)**

- 设计器:开审批节点属性 → 启用串簽 → 加 3 档(固定/逐级/固定)→ 保存 → load 往返档保留;
- 信箱:逐档审批 → timeline 显「第 K 档·第 R 轮」→ 退回上一档 → 再审;
- **主管变更场景(R13)**:发起后改 u1 的 ManagerId(链 A→B 改 A→C→D)→ 断言**序列冻结**(档数不因链增而变)、后续固定档(总经理)不错位;
- i18n 全解析(串簽面板/退回选择器/错误码)。

- [ ] **Step 4: 固化 + 提交**

固化 README + seed.sql + 脚本到 `docs/superpowers/qa/wfs-serial-signing/`。
```bash
git add docs/superpowers/qa/wfs-serial-signing/
git commit -m "test(wfs-serial): T13 gstack 真浏览器 QA 固化(串簽全链+退回三目标+主管变更冻结 R13)"
```

- [ ] **Step 5: 全量终检**

Run: `dotnet test CP6.Tests/CP6.Tests.csproj`(全量绿)+ `cp6.web` type-check/vitest/build 绿 + 整支 `git log --oneline a462764..HEAD` 零 Space 污染核验。

---

## 收尾(用户在场)

- **push**(会话权限拦 git push,须用户自跑):`! git -C D:/CP6-wfs-serial push -u origin feat/wfs-serial-sign`。
- **合并 main**:沿 OA 模式(快进或 PR)。
- **更新记忆** `project_current_focus.md` / `project_wfs_bpm.md`:串簽全栈完成。

---

## Self-Review(写计划后自检,见下方对话)
