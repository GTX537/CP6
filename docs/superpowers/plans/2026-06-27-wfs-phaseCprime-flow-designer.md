# WFS Phase C′（基础版流程设计器）实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 Phase A/B/C 之上，建一套**可视化流程设计器**（自由拖拽画布，基于 `@vue-flow/core`），让业务人员拖节点连边即可编出**能跑的真实审批流**（非手写 JSON），输出 `FlowSchema` → `SaveDef`；覆盖 P1 引擎内核能力（填單/審批/并行 split·join/結束 + 排他条件边 + 并簽 + 节点参数 + 路徑 CC + 现有 5 审批人策略），并落地 §2.7 身份编码 functionID/flowcode（1:1）+ 模板克隆。

**Architecture:** 后端在 `CP6.Core/Services/Wf|Oa/` 加 schema 校验器 + 设计器服务（消费既有 `IFlowDefService.SaveDefAsync`，加 FunctionId/FlowCode 唯一 + 校验 + 克隆），零改引擎执行态。前端引入 `@vue-flow/core` 画布；**可测核心 = `designerModel.ts`（FlowSchema ↔ Vue Flow 图模型转换 + 客户端校验，纯逻辑 vitest）**，画布交互/属性面板走 gstack QA。

**Tech Stack:** .NET 8 / EF Core（SqlServer + InMemory 测试）/ xUnit；Vue 3 + Element Plus + **@vue-flow/core**（新增依赖，本计划唯一新前端库）+ Pinia + vue-i18n（5 语）/ Vite / Vitest。

**配套 spec（落码前必读）：**
- `docs/superpowers/specs/2026-06-26-wfs-form-inbox-unified-design.md`（umbrella；本计划落 §4.8 设计器基础版 + §2.7 functionID/flowcode + W6/W7）
- `docs/superpowers/specs/2026-06-26-wfs-runtime-kernel-design.md`（L0 内核：FlowSchema 节点/边模型）
- Phase B/C plans（信箱 + FlowAdmin 轻量流程管理，已交付）

**设计源对齐（视觉/交互目标，落码前看）：** `D:\CP6\docs\oa\流程编辑器-离线版.html`（台达 Delta「表單流程編輯器」重设计稿，6.6MB 已抽 UI 文本对齐，HTML 本身不入 git）。实证：**拖拽节点到画布新增 + 点击节点/连线编辑属性**（= Vue Flow 画布 + 属性面板，与本计划一致）；工具条 = 保存/復原(undo)/取消復原(redo)/放大/縮小/適應畫面/**自動排版**/**顯示格線**；左侧 **「搜尋狀態」节点列表** + 调色板。**Delta 节点 → CP6 引擎映射（基础版只做引擎能跑的）：**

| Delta 节点 | CP6 引擎 | 基础版 |
|---|---|---|
| 填單 | `start` | ✅ |
| 表單狀態(審批) | `approval` | ✅ |
| 並簽(会签 all) | `approval`+`Countersign=all` | ✅（节点参数，非独立类型） |
| 並行分叉/汇聚 | `parallelSplit`/`parallelJoin` | ✅ |
| 流程結束 | `end` | ✅ |
| 串簽 / 系統動作·數據回寫·WebAPI·JOB / 表單取消·退回(作节点) | — | ❌ roadmap（引擎增量/服务任务/运行时动作，umbrella §9） |

**属性面板对齐 Delta**：状态(节点)=「基本參數」(狀態編號=NodeCode·狀態名稱·節點類型·下一步審核人類型) +「進階參數」(逾時天數·允許退回·逾時提醒·自動跳轉) + 知會人員(CC)；路徑(边)=路徑編號·名稱·類型(無條件/條件)·條件表達式·知會人員。下一步審核人類型映 CP6 5 策略（指定一組=Role / 指定工號N層=DirectManager(Levels) / 指定部門=DeptLeader / 當前直屬主管=DirectManager / 發起人=Starter；表單欄位指定·組合权限类=roadmap）。

---

## Scope Check（两 Part）

- **Part A（T1~T4）= 后端**：FunctionId/FlowCode 列 + FlowNode 画布坐标 + schema 校验器 + 设计器服务（list/load/save/clone）+ 控制器。
- **Part B（T5~T10）= 前端**：Vue Flow 依赖 + schema↔图转换器(vitest) + API/类型 + 画布 + 属性面板 + 设计器壳 + 路由/菜单/i18n/QA。

**不在本计划**：串簽（顺签，引擎增量）/ 系統動作·數據回寫·WebAPI·JOB（服务任务，WFS P2/P3）/ 腳本条件 / 取消·退回作为**可设计节点**（CP6 中是运行时动作非节点类型，归 roadmap）。这些超出「基础版覆盖 P1 内核能力」。

---

## File Structure（先锁分解）

**后端新建：**
- `CP6.Core/Services/Wf/FlowSchemaValidator.cs` — 纯静态校验器（一起点/可达 end/边引用存在节点/审批节点有策略…）
- `CP6.Core/Services/Oa/IDesignerService.cs` / `DesignerService.cs` — list by FunctionId / load / save(校验+唯一+SaveDef) / clone
- `CP6.Core/Services/Oa/DesignerModels.cs` — 设计器 DTO records（FlowDefSummary/SaveFlowRequest/CloneRequest/ValidateResult）
- `CP6.WebApi/Controllers/Oa/DesignerController.cs`
- `CP6.WebApi/Seed/I18nOaDesignerScreenSeed.cs`

**后端修改：**
- `CP6.Entity/DomainModels/Wf/Wf_FlowDef.cs` — 加 `FunctionId` / `FlowCode`
- `CP6.Core/Services/Wf/FlowSchema.cs` — `FlowNode` 加 `X` / `Y`（可空，画布坐标，引擎忽略）
- `CP6.Core/EFDbContext/CP6Context.cs` — `Wf_FlowDef` 过滤唯一索引（FunctionId / FlowCode）
- `CP6.WebApi/Program.cs` — 注册 DesignerService + i18n 合并 + 菜单 738

**后端迁移：** `WfsPhaseCprimeDesigner`（`Wf_FlowDef` 加 FunctionId/FlowCode 两列 + 两过滤唯一索引；`FlowNode.X/Y` 是 SchemaJson 内字段不需列）。

**后端测试（`CP6.Tests/Oa/`）：** `FlowSchemaValidatorTests`、`DesignerServiceTests`、`DesignerModelTests`（列）。

**前端新建：**
- `cp6.web/src/views/oa/designer/designerModel.ts`（+ `designerModel.test.ts`）— schema↔图转换 + 客户端校验（纯逻辑）
- `cp6.web/src/api/oa/designer.ts`、`cp6.web/src/types/oa/designer.ts`
- `cp6.web/src/views/oa/designer/DesignerView.vue`（壳：工具条 + 流程列表 + 画布 + 属性面板）
- `cp6.web/src/views/oa/designer/DesignerCanvas.vue`（Vue Flow 画布 + 调色板拖拽 + 自定义节点）
- `cp6.web/src/views/oa/designer/nodes/{StartNode,ApprovalNode,GatewayNode,EndNode}.vue`（自定义节点渲染）
- `cp6.web/src/views/oa/designer/NodePropertyPanel.vue` / `EdgePropertyPanel.vue`

**前端修改：**
- `cp6.web/package.json` — 加 `@vue-flow/core`（+ `@vue-flow/background`、`@vue-flow/controls`）
- `cp6.web/src/router/index.ts` — viewModules 加 `/oa/designer`
- `CP6.WebApi/Program.cs` / seed — 菜单 738 设计器 + i18n

---

## 通用约定

- **分支/worktree**：续接 **`D:\CP6-oa-core`** 的 **`feat/oa-inbox-core`** 分支（堆叠在 Phase C 之上；若隔离从该分支再切 `feat/oa-flow-designer`）。**绝不碰 `D:\CP6`**（并发 Space 会话）。Bash cwd 每次重置回 `D:/CP6`→须 `cd /d/CP6-oa-core &&` 或 `git -C`/绝对路径；`dotnet` 用显式 csproj 路径；前端 `cd /d/CP6-oa-core/cp6.web &&`（node_modules 已装）。**任何 `Space_*` 文件零碰**。
- **测试基线**：Phase C 末 `dotnet test` = **1251 passed / 1 skip**；前端 vitest **35**。每 Task 末跑相关测试。**零改引擎执行态**：`dotnet test --filter "FullyQualifiedName~Wf"` 任一既有测试转红 = 破坏，回退。
- **测试 DB 工厂**（沿用）：
  ```csharp
  private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
      .UseInMemoryDatabase(Guid.NewGuid().ToString())
      .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
  ```
- **节点类型（锁定 = CP6 引擎真实可执行类型）**：`start`(填單/发起) · `approval`(審批) · `parallelSplit`(并行分叉) · `parallelJoin`(并行汇聚) · `end`(結束)。**取消/退回不是可设计节点**（运行时动作，roadmap）。
- **错误码（沿用 `E-WF-0xx`）**：
  - `E-WF-009` FunctionId 或 FlowCode 租户内重复（违反 1:1）
  - `E-WF-010` schema 校验失败（无起点 / 无法到达 end / 边引用不存在节点 / 审批节点缺策略…，详情逐条回报）
- **commit**：每 Task 末本地 commit（不 push）；提交体尾 `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`。

---

# Part A — 后端

## Task 1：数据模型 — FunctionId/FlowCode + FlowNode 坐标 + 迁移

**Files:**
- Modify: `CP6.Entity/DomainModels/Wf/Wf_FlowDef.cs`、`CP6.Core/Services/Wf/FlowSchema.cs`、`CP6.Core/EFDbContext/CP6Context.cs`
- Test: `CP6.Tests/Oa/DesignerModelTests.cs`

- [ ] **Step 1: 写失败测试** `CP6.Tests/Oa/DesignerModelTests.cs`：
```csharp
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests;

public class DesignerModelTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    [Fact]
    public async Task FlowDef_HasFunctionIdAndFlowCode()
    {
        using var db = NewDb();
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "leave", FlowName = "请假", FormKey = "leave",
            FunctionId = "MSBBPA010", FlowCode = "2887" });
        await db.SaveChangesAsync();
        var got = await db.Wf_FlowDefs.SingleAsync();
        Assert.Equal("MSBBPA010", got.FunctionId);
        Assert.Equal("2887", got.FlowCode);
    }

    [Fact]
    public void FlowNode_HasXYAndCode()
    {
        var n = new FlowNode { Id = "n1", X = 120, Y = 80, Code = "10" };
        Assert.Equal(120, n.X);
        Assert.Equal(80, n.Y);
        Assert.Equal("10", n.Code);   // 状态编号(Delta StateCode)
    }
}
```

- [ ] **Step 2: 跑测试确认失败** — `cd /d/CP6-oa-core && dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~DesignerModelTests"`（FunctionId/FlowCode/X/Y 未定义）。

- [ ] **Step 3: `Wf_FlowDef.cs` 加两列**（类内追加）：
```csharp
/// <summary>功能码（MSBBPA010 式程序/功能标准码，设计器组织主键，umbrella §2.7）。租户内唯一（可空过渡）。</summary>
[MaxLength(50)] public string? FunctionId { get; set; }

/// <summary>流程编号（Delta 式 2887/2889 人面编号，umbrella §2.7）。租户内唯一（可空过渡）。</summary>
[MaxLength(50)] public string? FlowCode { get; set; }
```

- [ ] **Step 4: `FlowSchema.cs` 的 `FlowNode` 加坐标**（类内追加；引擎不读，仅设计器画布回填）：
```csharp
/// <summary>画布 X 坐标（设计器布局，引擎忽略）。</summary>
public double? X { get; set; }
/// <summary>画布 Y 坐标（设计器布局，引擎忽略）。</summary>
public double? Y { get; set; }

/// <summary>状态编号（Delta StateCode / NodeCode，人面业务码；读模型 Wf_FlowFormTo.NodeCode 取此或 Id，引擎执行不依赖）。</summary>
public string? Code { get; set; }
```

- [ ] **Step 5: `CP6Context.cs` 加过滤唯一索引**（`Wf_FlowDef` 配置区；可空列用过滤索引允多 null）：
```csharp
modelBuilder.Entity<Wf_FlowDef>(e =>
{
    e.HasIndex(x => new { x.TenantId, x.FunctionId }).IsUnique()
        .HasFilter("[FunctionId] IS NOT NULL").HasDatabaseName("UX_Wf_FlowDef_Function");
    e.HasIndex(x => new { x.TenantId, x.FlowCode }).IsUnique()
        .HasFilter("[FlowCode] IS NOT NULL").HasDatabaseName("UX_Wf_FlowDef_Code");
});
```
> 若 `Wf_FlowDef` 已有 `modelBuilder.Entity<Wf_FlowDef>` 配置块，把两行 `HasIndex` 并入而非新开块。`Wf_FlowDef` 是否带 `TenantId`：它继承 `BaseTenantEntity`（确认——若实际继承 `BaseEntity` 无 TenantId，则索引去掉 TenantId 改全局唯一，落码核对）。

- [ ] **Step 6: 跑测试确认通过** — PASS（2 例）。

- [ ] **Step 7: 加迁移** — `cd /d/CP6-oa-core && dotnet ef migrations add WfsPhaseCprimeDesigner -p CP6.Core -s CP6.WebApi`。核对 `Up()`：仅 `Wf_FlowDef` 加 `FunctionId`/`FlowCode`（nvarchar(50) NULL）+ 两过滤唯一索引，无其他改动、无 `Space_*`。

- [ ] **Step 8: 兼容回归** — `dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~Wf"` 全绿（仅加列/加 schema 字段）。

- [ ] **Step 9: Commit**
```bash
cd /d/CP6-oa-core && git add CP6.Entity/DomainModels/Wf/Wf_FlowDef.cs CP6.Core/Services/Wf/FlowSchema.cs CP6.Core/EFDbContext/CP6Context.cs CP6.Core/Migrations/ CP6.Tests/Oa/DesignerModelTests.cs && git commit -m "feat(wfs-Cp): T1 Wf_FlowDef FunctionId/FlowCode 过滤唯一 + FlowNode 画布坐标 + 迁移"
```

---

## Task 2：`FlowSchemaValidator`（纯校验器）

**Files:**
- Create: `CP6.Core/Services/Wf/FlowSchemaValidator.cs`
- Test: `CP6.Tests/Oa/FlowSchemaValidatorTests.cs`

> 设计器保存前后端都要校验「这流程能跑」。纯静态函数：输入 `FlowSchema`，输出错误码列表（空=合法）。规则：①恰有一个 `start` 节点；②至少一个 `end`；③每条边的 From/To 都引用存在节点；④从 start 沿边可达某 end；⑤`approval` 节点须有合法 `ApproverStrategy`；⑥`parallelSplit` 至少 2 出边、`parallelJoin` 至少 2 入边。

- [ ] **Step 1: 写失败测试** `CP6.Tests/Oa/FlowSchemaValidatorTests.cs`：
```csharp
using CP6.Core.Services.Wf;

namespace CP6.Tests;

public class FlowSchemaValidatorTests
{
    private static FlowSchema Linear() => new()
    {
        Start = "s",
        Nodes =
        {
            new FlowNode { Id = "s", Type = "start" },
            new FlowNode { Id = "a", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = Guid.NewGuid() },
            new FlowNode { Id = "e", Type = "end" },
        },
        Edges = { new FlowEdge { From = "s", To = "a" }, new FlowEdge { From = "a", To = "e" } },
    };

    [Fact]
    public void Valid_Linear_NoErrors() => Assert.Empty(FlowSchemaValidator.Validate(Linear()));

    [Fact]
    public void MissingStart_Reported()
    {
        var s = Linear(); s.Nodes.RemoveAll(n => n.Type == "start"); s.Start = null;
        s.Edges.RemoveAll(e => e.From == "s");
        Assert.Contains("E-WF-010", FlowSchemaValidator.Validate(s));
    }

    [Fact]
    public void EdgeToUnknownNode_Reported()
    {
        var s = Linear(); s.Edges.Add(new FlowEdge { From = "a", To = "ghost" });
        Assert.Contains("E-WF-010", FlowSchemaValidator.Validate(s));
    }

    [Fact]
    public void ApprovalWithoutStrategy_Reported()
    {
        var s = Linear(); s.Nodes.First(n => n.Id == "a").ApproverStrategy = null;
        Assert.Contains("E-WF-010", FlowSchemaValidator.Validate(s));
    }

    [Fact]
    public void EndUnreachable_Reported()
    {
        var s = Linear(); s.Edges.RemoveAll(e => e.From == "a");   // a 到不了 e
        Assert.Contains("E-WF-010", FlowSchemaValidator.Validate(s));
    }
}
```

- [ ] **Step 2: 跑测试确认失败** — `dotnet test ... --filter "FullyQualifiedName~FlowSchemaValidatorTests"`（`FlowSchemaValidator` 未定义）。

- [ ] **Step 3: 建 `FlowSchemaValidator.cs`**
```csharp
namespace CP6.Core.Services.Wf;

/// <summary>流程 schema 静态校验（设计器保存前后端共用）。返回错误码列表（空=合法）。所有结构性问题统一 E-WF-010。</summary>
public static class FlowSchemaValidator
{
    private static readonly HashSet<string> KnownStrategies =
        new(new[] { "DirectManager", "DeptLeader", "Role", "Specified", "Starter" }, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> Validate(FlowSchema schema)
    {
        var errs = new List<string>();
        if (schema is null || schema.Nodes.Count == 0) { errs.Add("E-WF-010"); return errs; }

        string T(FlowNode n) => (n.Type ?? "approval").Trim().ToLowerInvariant();
        var ids = schema.Nodes.Select(n => n.Id).ToHashSet();

        // ① 恰一 start ② 至少一 end
        if (schema.Nodes.Count(n => T(n) == "start") != 1) errs.Add("E-WF-010");
        if (!schema.Nodes.Any(n => T(n) == "end")) errs.Add("E-WF-010");

        // ③ 边引用存在节点
        foreach (var e in schema.Edges)
            if (!ids.Contains(e.From) || !ids.Contains(e.To)) { errs.Add("E-WF-010"); break; }

        // ⑤ approval 须有合法策略
        foreach (var n in schema.Nodes.Where(n => T(n) == "approval"))
            if (n.ApproverStrategy is null || !KnownStrategies.Contains(n.ApproverStrategy)) { errs.Add("E-WF-010"); break; }

        // ⑥ 并行网关入/出边数
        foreach (var n in schema.Nodes.Where(n => T(n) == "parallelsplit"))
            if (schema.Edges.Count(e => e.From == n.Id) < 2) { errs.Add("E-WF-010"); break; }
        foreach (var n in schema.Nodes.Where(n => T(n) == "paralleljoin"))
            if (schema.Edges.Count(e => e.To == n.Id) < 2) { errs.Add("E-WF-010"); break; }

        // ④ 从 start 可达某 end（BFS）
        var start = schema.Nodes.FirstOrDefault(n => T(n) == "start");
        if (start is not null)
        {
            var adj = schema.Edges.GroupBy(e => e.From).ToDictionary(g => g.Key, g => g.Select(e => e.To).ToList());
            var seen = new HashSet<string> { start.Id };
            var q = new Queue<string>(); q.Enqueue(start.Id);
            bool reachedEnd = false;
            while (q.Count > 0)
            {
                var cur = q.Dequeue();
                var node = schema.Nodes.FirstOrDefault(n => n.Id == cur);
                if (node is not null && T(node) == "end") { reachedEnd = true; break; }
                if (adj.TryGetValue(cur, out var outs))
                    foreach (var to in outs) if (seen.Add(to)) q.Enqueue(to);
            }
            if (!reachedEnd) errs.Add("E-WF-010");
        }

        return errs.Distinct().ToList();
    }
}
```

- [ ] **Step 4: 跑测试确认通过** — PASS（5 例）。

- [ ] **Step 5: Commit**
```bash
cd /d/CP6-oa-core && git add CP6.Core/Services/Wf/FlowSchemaValidator.cs CP6.Tests/Oa/FlowSchemaValidatorTests.cs && git commit -m "feat(wfs-Cp): T2 FlowSchemaValidator 纯校验(起点/可达end/边引用/审批策略/并行网关, E-WF-010)"
```

---

## Task 3：`DesignerService`（list / load / save / clone）

**Files:**
- Create: `CP6.Core/Services/Oa/DesignerModels.cs`、`IDesignerService.cs`、`DesignerService.cs`
- Test: `CP6.Tests/Oa/DesignerServiceTests.cs`

> 消费 `IFlowDefService.SaveDefAsync`（既有 upsert）。Save 前：①`FlowSchemaValidator.Validate` 非空 → `E-WF-010`；②FunctionId/FlowCode 租户内唯一（排除自身 FlowKey）→ `E-WF-009`。Clone：以现有流程为模板生成**独立副本**（新 FlowKey，清空 FunctionId/FlowCode/启用，schema 深拷贝），互不影响（umbrella §4.8）。

- [ ] **Step 1: 写失败测试** `CP6.Tests/Oa/DesignerServiceTests.cs`：
```csharp
using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Text.Json;

namespace CP6.Tests;

public class DesignerServiceTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static IDesignerService Svc(CP6Context db) => new DesignerService(db, new FlowDefService(db));

    private static string ValidSchema() => JsonSerializer.Serialize(new FlowSchema
    {
        Start = "s",
        Nodes = { new FlowNode { Id = "s", Type = "start" },
                  new FlowNode { Id = "a", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = Guid.NewGuid() },
                  new FlowNode { Id = "e", Type = "end" } },
        Edges = { new FlowEdge { From = "s", To = "a" }, new FlowEdge { From = "a", To = "e" } },
    });

    [Fact]
    public async Task Save_Valid_PersistsWithIdentity()
    {
        using var db = NewDb();
        await Svc(db).SaveAsync(new SaveFlowRequest("leave", "请假流程", "leave", "MSBBPA010", "2887", ValidSchema()), "tester");
        var def = await db.Wf_FlowDefs.SingleAsync(d => d.FlowKey == "leave");
        Assert.Equal("MSBBPA010", def.FunctionId);
        Assert.Equal("2887", def.FlowCode);
    }

    [Fact]
    public async Task Save_InvalidSchema_ThrowsE010()
    {
        using var db = NewDb();
        var bad = JsonSerializer.Serialize(new FlowSchema { Nodes = { new FlowNode { Id = "a", Type = "approval" } } }); // 无 start/end/策略
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Svc(db).SaveAsync(new SaveFlowRequest("x", "x", "x", null, null, bad), null));
        Assert.Equal("E-WF-010", ex.Message);
    }

    [Fact]
    public async Task Save_DuplicateFunctionId_ThrowsE009()
    {
        using var db = NewDb();
        await Svc(db).SaveAsync(new SaveFlowRequest("leave", "请假", "leave", "MSBBPA010", "2887", ValidSchema()), null);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Svc(db).SaveAsync(new SaveFlowRequest("expense", "报销", "expense", "MSBBPA010", "2889", ValidSchema()), null));
        Assert.Equal("E-WF-009", ex.Message);     // FunctionId 撞
    }

    [Fact]
    public async Task Clone_ProducesIndependentCopy()
    {
        using var db = NewDb();
        await Svc(db).SaveAsync(new SaveFlowRequest("leave", "请假", "leave", "MSBBPA010", "2887", ValidSchema()), null);
        await Svc(db).CloneAsync(new CloneRequest("leave", "leave_v2", "请假副本"), null);

        var copy = await db.Wf_FlowDefs.SingleAsync(d => d.FlowKey == "leave_v2");
        Assert.Equal("leave", copy.FormKey);          // 同表单
        Assert.Null(copy.FunctionId);                 // 身份码清空（避免撞唯一）
        Assert.Null(copy.FlowCode);
        Assert.False(copy.Enable);                    // 副本默认停用
        Assert.Equal(2, await db.Wf_FlowDefs.CountAsync()); // 两条独立定义
    }
}
```

- [ ] **Step 2: 跑测试确认失败** — `dotnet test ... --filter "FullyQualifiedName~DesignerServiceTests"`。

- [ ] **Step 3: 建 `DesignerModels.cs`**
```csharp
namespace CP6.Core.Services.Oa;

public record SaveFlowRequest(string FlowKey, string FlowName, string FormKey,
    string? FunctionId, string? FlowCode, string SchemaJson);
public record CloneRequest(string SourceFlowKey, string NewFlowKey, string NewFlowName);
public record FlowDefSummary(string FlowKey, string FlowName, string FormKey,
    string? FunctionId, string? FlowCode, int Version, bool Enable);
```

- [ ] **Step 4: 建 `IDesignerService.cs`**
```csharp
namespace CP6.Core.Services.Oa;

/// <summary>流程设计器服务（umbrella §4.8）。校验 + 身份唯一 + upsert（消费 IFlowDefService）+ 模板克隆。</summary>
public interface IDesignerService
{
    Task<IReadOnlyList<FlowDefSummary>> ListAsync(string? functionId = null);  // functionId 非空=按功能筛
    Task<FlowDefSummary?> LoadAsync(string flowKey);                            // 取定义摘要（schema 经 GetDef 取）
    Task SaveAsync(SaveFlowRequest req, string? user);                          // 校验 E-WF-010 + 唯一 E-WF-009 + SaveDef
    Task CloneAsync(CloneRequest req, string? user);                           // 独立副本
}
```

- [ ] **Step 5: 建 `DesignerService.cs`**
```csharp
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CP6.Core.Services.Oa;

public class DesignerService : IDesignerService
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
    private readonly CP6Context _db;
    private readonly IFlowDefService _flowDef;
    public DesignerService(CP6Context db, IFlowDefService flowDef) { _db = db; _flowDef = flowDef; }

    public async Task<IReadOnlyList<FlowDefSummary>> ListAsync(string? functionId = null)
    {
        var q = _db.Wf_FlowDefs.AsQueryable();
        if (!string.IsNullOrWhiteSpace(functionId)) q = q.Where(d => d.FunctionId == functionId);
        return await q.OrderBy(d => d.FunctionId).ThenBy(d => d.FlowKey)
            .Select(d => new FlowDefSummary(d.FlowKey, d.FlowName, d.FormKey, d.FunctionId, d.FlowCode, d.Version, d.Enable))
            .ToListAsync();
    }

    public async Task<FlowDefSummary?> LoadAsync(string flowKey) =>
        await _db.Wf_FlowDefs.Where(d => d.FlowKey == flowKey)
            .Select(d => new FlowDefSummary(d.FlowKey, d.FlowName, d.FormKey, d.FunctionId, d.FlowCode, d.Version, d.Enable))
            .FirstOrDefaultAsync();

    public async Task SaveAsync(SaveFlowRequest req, string? user)
    {
        // ① schema 校验
        var schema = JsonSerializer.Deserialize<FlowSchema>(req.SchemaJson, JsonOpts) ?? new FlowSchema();
        if (FlowSchemaValidator.Validate(schema).Count > 0) throw new InvalidOperationException("E-WF-010");

        // ② 身份码租户内唯一（排除自身 FlowKey）
        if (!string.IsNullOrWhiteSpace(req.FunctionId) &&
            await _db.Wf_FlowDefs.AnyAsync(d => d.FunctionId == req.FunctionId && d.FlowKey != req.FlowKey))
            throw new InvalidOperationException("E-WF-009");
        if (!string.IsNullOrWhiteSpace(req.FlowCode) &&
            await _db.Wf_FlowDefs.AnyAsync(d => d.FlowCode == req.FlowCode && d.FlowKey != req.FlowKey))
            throw new InvalidOperationException("E-WF-009");

        // ③ upsert（SaveDef 升版） + 身份码落库
        await _flowDef.SaveDefAsync(req.FlowKey, req.FlowName, req.FormKey, req.SchemaJson, user);
        var def = await _db.Wf_FlowDefs.FirstAsync(d => d.FlowKey == req.FlowKey);
        def.FunctionId = string.IsNullOrWhiteSpace(req.FunctionId) ? null : req.FunctionId;
        def.FlowCode = string.IsNullOrWhiteSpace(req.FlowCode) ? null : req.FlowCode;
        await _db.SaveChangesAsync();
    }

    public async Task CloneAsync(CloneRequest req, string? user)
    {
        var src = await _flowDef.GetDefAsync(req.SourceFlowKey)
                  ?? throw new InvalidOperationException("E-WF-006");
        if (await _db.Wf_FlowDefs.AnyAsync(d => d.FlowKey == req.NewFlowKey))
            throw new InvalidOperationException("E-WF-009");   // 新 FlowKey 已存在
        // 独立副本：同 schema/FormKey，清身份码 + 停用（避免撞唯一、需重新设定身份与启用）
        await _flowDef.SaveDefAsync(req.NewFlowKey, req.NewFlowName, src.FormKey, src.SchemaJson, user);
        var copy = await _db.Wf_FlowDefs.FirstAsync(d => d.FlowKey == req.NewFlowKey);
        copy.FunctionId = null; copy.FlowCode = null; copy.Enable = false;
        await _db.SaveChangesAsync();
    }
}
```
> `E-WF-006`（流程不存在）Phase B 已用。Clone 的「新 FlowKey 已存在」复用 `E-WF-009`（人面表达「编号/键重复」）。

- [ ] **Step 6: 跑测试确认通过** — PASS（4 例）。

- [ ] **Step 7: Commit**
```bash
cd /d/CP6-oa-core && git add CP6.Core/Services/Oa/DesignerModels.cs CP6.Core/Services/Oa/IDesignerService.cs CP6.Core/Services/Oa/DesignerService.cs CP6.Tests/Oa/DesignerServiceTests.cs && git commit -m "feat(wfs-Cp): T3 DesignerService list/load/save(校验+唯一)/clone(独立副本)"
```

---

## Task 4：`DesignerController` + DI（Part A 收尾）

**Files:**
- Create: `CP6.WebApi/Controllers/Oa/DesignerController.cs`
- Modify: `CP6.WebApi/Program.cs`

> 控制器模式照 Phase B/C（`LocalizedControllerBase`、`Ok2(data)`、`Err(e)→BadRequest{code=400,message}`、`(await _ctx.GetAsync()).UserId` 作 user）。控制器无单测；验收 = 编译 + 全量回归绿 + 装配。

- [ ] **Step 1: 建 `DesignerController.cs`**（`api/oa/designer`，`[Authorize]`）
```csharp
[ApiController]
[Route("api/oa/designer")]
[Authorize]
public class DesignerController : LocalizedControllerBase
{
    private readonly IDesignerService _designer;
    private readonly IFlowDefService _flowDef;
    private readonly ICurrentPermissionContext _ctx;
    public DesignerController(IDesignerService designer, IFlowDefService flowDef, ICurrentPermissionContext ctx)
    { _designer = designer; _flowDef = flowDef; _ctx = ctx; }
    private IActionResult Err(InvalidOperationException e) => BadRequest(new { code = 400, message = e.Message });

    [HttpGet("list")] public async Task<IActionResult> List([FromQuery] string? functionId)
        => Ok2(await _designer.ListAsync(functionId));

    [HttpGet("load/{flowKey}")] public async Task<IActionResult> Load(string flowKey)
    {
        var summary = await _designer.LoadAsync(flowKey);
        if (summary is null) return NotFound(new { code = 404, message = "E-WF-006" });
        var def = await _flowDef.GetDefAsync(flowKey);
        return Ok2(new { summary, schemaJson = def?.SchemaJson ?? "{}" });
    }

    public record SaveReq(string FlowKey, string FlowName, string FormKey, string? FunctionId, string? FlowCode, string SchemaJson);
    [HttpPost("save")] public async Task<IActionResult> Save([FromBody] SaveReq r)
    {
        try
        {
            var user = (await _ctx.GetAsync()).UserId.ToString();
            await _designer.SaveAsync(new SaveFlowRequest(r.FlowKey, r.FlowName, r.FormKey, r.FunctionId, r.FlowCode, r.SchemaJson), user);
            return Ok2(true);
        }
        catch (InvalidOperationException e) { return Err(e); }
    }

    public record CloneReq(string SourceFlowKey, string NewFlowKey, string NewFlowName);
    [HttpPost("clone")] public async Task<IActionResult> Clone([FromBody] CloneReq r)
    {
        try
        {
            var user = (await _ctx.GetAsync()).UserId.ToString();
            await _designer.CloneAsync(new CloneRequest(r.SourceFlowKey, r.NewFlowKey, r.NewFlowName), user);
            return Ok2(true);
        }
        catch (InvalidOperationException e) { return Err(e); }
    }
}
```
> **落码核对**：`LocalizedControllerBase`/`Ok2`/`ICurrentPermissionContext` 命名空间照既有 `Controllers/Oa/InboxController.cs` 抄。

- [ ] **Step 2: Program.cs 注册**（接 4.0e 段后）
```csharp
// 4.0f OA 流程设计器（Phase C′）
builder.Services.AddScoped<CP6.Core.Services.Oa.IDesignerService, CP6.Core.Services.Oa.DesignerService>();
```
> `IFlowDefService`/`FlowDefService` 应已注册（Phase A）；若未注册则补 `AddScoped<IFlowDefService, FlowDefService>()`（落码核对）。

- [ ] **Step 3: 编译 + 全量回归** — `dotnet build CP6.WebApi/CP6.WebApi.csproj` 成功；`dotnet test CP6.Tests/CP6.Tests.csproj` 全绿（1251 + 本计划新增，无回归）。

- [ ] **Step 4: Commit（Part A 收尾）**
```bash
cd /d/CP6-oa-core && git add CP6.WebApi/Controllers/Oa/DesignerController.cs CP6.WebApi/Program.cs && git commit -m "feat(wfs-Cp): T4 DesignerController + DI(Part A 收尾)"
```

---

# Part B — 前端（Vue Flow 画布）

> **通用前端约定**：`import http from '../http'`；`<script setup lang="ts">`；`t()` 文案；类型置 `src/types/oa/`。**Vue Flow** 文档心智：`<VueFlow v-model:nodes v-model:edges>`，自定义节点用 `#node-<type>` 插槽，连边 `@connect`，调色板用 HTML5 drag + `project()` 算落点。

## Task 5：依赖 + `designerModel.ts`（schema↔图转换 + 客户端校验，vitest）

**Files:**
- Modify: `cp6.web/package.json`（装 `@vue-flow/core` `@vue-flow/background` `@vue-flow/controls`）
- Create: `cp6.web/src/views/oa/designer/designerModel.ts` + `designerModel.test.ts`

> **本 Task = C′ 的可测核心**：把「FlowSchema JSON ↔ Vue Flow {nodes,edges}」与客户端基本校验抽成纯函数，单测覆盖；画布只调它。

- [ ] **Step 1: 装依赖** — `cd /d/CP6-oa-core/cp6.web && npm i @vue-flow/core @vue-flow/background @vue-flow/controls`（写入 package.json dependencies）。

- [ ] **Step 2: 写失败测试 `designerModel.test.ts`**
```typescript
import { describe, it, expect } from 'vitest'
import { schemaToGraph, graphToSchema, validateClient, NODE_PALETTE } from './designerModel'

const schema = {
  start: 's',
  nodes: [
    { id: 's', type: 'start', name: '填單', x: 0, y: 0 },
    { id: 'a', type: 'approval', name: '审批', approverStrategy: 'Specified', x: 0, y: 120 },
    { id: 'e', type: 'end', name: '结束', x: 0, y: 240 },
  ],
  edges: [{ from: 's', to: 'a' }, { from: 'a', to: 'e', condition: 'days>3' }],
}

describe('designerModel', () => {
  it('schemaToGraph maps nodes+edges with positions', () => {
    const g = schemaToGraph(schema as any)
    expect(g.nodes).toHaveLength(3)
    expect(g.nodes[0]!.position).toEqual({ x: 0, y: 0 })
    expect(g.nodes[1]!.type).toBe('approval')
    expect(g.edges).toHaveLength(2)
    expect(g.edges[1]!.source).toBe('a')
    expect(g.edges[1]!.target).toBe('e')
  })

  it('graphToSchema is the inverse (roundtrip preserves ids/positions/start)', () => {
    const g = schemaToGraph(schema as any)
    const back = graphToSchema(g.nodes, g.edges)
    expect(back.start).toBe('s')                       // start = type==='start' 节点
    expect(back.nodes.map(n => n.id).sort()).toEqual(['a', 'e', 's'])
    expect(back.nodes.find(n => n.id === 's')!.x).toBe(0)
    expect(back.edges.find(e => e.from === 'a')!.condition).toBe('days>3')
  })

  it('validateClient flags missing start + edge to unknown node', () => {
    expect(validateClient(schema as any)).toEqual([])
    const noStart = { ...schema, nodes: schema.nodes.filter(n => n.type !== 'start') }
    expect(validateClient(noStart as any).length).toBeGreaterThan(0)
    const ghost = { ...schema, edges: [...schema.edges, { from: 'a', to: 'zzz' }] }
    expect(validateClient(ghost as any).length).toBeGreaterThan(0)
  })

  it('NODE_PALETTE lists the 5 engine node types', () => {
    expect(NODE_PALETTE.map(p => p.type).sort())
      .toEqual(['approval', 'end', 'parallelJoin', 'parallelSplit', 'start'])
  })
})
```

- [ ] **Step 3: 跑测试确认失败** — `cd /d/CP6-oa-core/cp6.web && npx vitest run src/views/oa/designer/designerModel.test.ts`。

- [ ] **Step 4: 建 `designerModel.ts`**
```typescript
import type { Node as VFNode, Edge as VFEdge } from '@vue-flow/core'

export interface SchemaNode {
  id: string; type: string; name?: string
  approverStrategy?: string; approverLevels?: number; approverRoleId?: number; approverUserId?: string
  countersign?: string; timeoutHours?: number; timeoutAction?: string; ccUsers?: string[]; ccRoleId?: number
  x?: number; y?: number
}
export interface SchemaEdge { from: string; to: string; condition?: string; ccUsers?: string[] }
export interface FlowSchemaDto { start?: string; nodes: SchemaNode[]; edges: SchemaEdge[] }

export const NODE_PALETTE = [
  { type: 'start',         label: '填單(发起)', color: '#67c23a' },
  { type: 'approval',      label: '審批',       color: '#409eff' },
  { type: 'parallelSplit', label: '并行分叉',   color: '#e6a23c' },
  { type: 'parallelJoin',  label: '并行汇聚',   color: '#e6a23c' },
  { type: 'end',           label: '結束',       color: '#909399' },
] as const

/** FlowSchema → Vue Flow 图（节点带 position + data 全字段；边 source/target + data 条件/CC）。 */
export function schemaToGraph(schema: FlowSchemaDto): { nodes: VFNode[]; edges: VFEdge[] } {
  const nodes: VFNode[] = (schema.nodes ?? []).map((n, i) => ({
    id: n.id,
    type: n.type || 'approval',
    position: { x: n.x ?? 80, y: n.y ?? i * 120 },        // 无坐标→竖排兜底
    data: { ...n },
    label: n.name || n.id,
  }))
  const edges: VFEdge[] = (schema.edges ?? []).map(e => ({
    id: `${e.from}__${e.to}`,
    source: e.from,
    target: e.to,
    data: { condition: e.condition, ccUsers: e.ccUsers },
    label: e.condition || undefined,
  }))
  return { nodes, edges }
}

/** Vue Flow 图 → FlowSchema（start = type==='start' 的节点；回写 x/y）。 */
export function graphToSchema(nodes: VFNode[], edges: VFEdge[]): FlowSchemaDto {
  const sn: SchemaNode[] = nodes.map(n => ({
    ...(n.data as SchemaNode),
    id: n.id,
    type: (n.data as SchemaNode)?.type || n.type || 'approval',
    x: n.position?.x,
    y: n.position?.y,
  }))
  const se: SchemaEdge[] = edges.map(e => ({
    from: e.source, to: e.target,
    condition: (e.data as any)?.condition || undefined,
    ccUsers: (e.data as any)?.ccUsers || undefined,
  }))
  const start = sn.find(n => n.type === 'start')?.id
  return { start, nodes: sn, edges: se }
}

/** 客户端基本校验（后端 FlowSchemaValidator 的轻量镜像；保存前预检）。返回错误文案 key 数组。 */
export function validateClient(schema: FlowSchemaDto): string[] {
  const errs: string[] = []
  const nodes = schema.nodes ?? [], edges = schema.edges ?? []
  const ids = new Set(nodes.map(n => n.id))
  if (nodes.filter(n => n.type === 'start').length !== 1) errs.push('oa.designer.errNoStart')
  if (!nodes.some(n => n.type === 'end')) errs.push('oa.designer.errNoEnd')
  if (edges.some(e => !ids.has(e.from) || !ids.has(e.to))) errs.push('oa.designer.errDanglingEdge')
  if (nodes.some(n => n.type === 'approval' && !n.approverStrategy)) errs.push('oa.designer.errNoStrategy')
  return errs
}
```

- [ ] **Step 5: 跑测试确认通过** — PASS（4 例）；`npx vitest run`（既有 35 + 新 4 = 39 全绿）；`npm run type-check` 绿。

- [ ] **Step 6: Commit**
```bash
cd /d/CP6-oa-core && git add cp6.web/package.json cp6.web/package-lock.json cp6.web/src/views/oa/designer/designerModel.ts cp6.web/src/views/oa/designer/designerModel.test.ts && git commit -m "feat(wfs-Cp): T5 @vue-flow/core 依赖 + designerModel schema↔图转换+客户端校验(vitest)"
```

---

## Task 6：前端 API + TS 类型

**Files:** Create `cp6.web/src/types/oa/designer.ts` + `cp6.web/src/api/oa/designer.ts`

- [ ] **Step 1: `types/oa/designer.ts`**（对齐后端 DTO）
```typescript
export interface FlowDefSummary { flowKey: string; flowName: string; formKey: string;
  functionId?: string; flowCode?: string; version: number; enable: boolean }
export interface SaveFlowBody { flowKey: string; flowName: string; formKey: string;
  functionId?: string; flowCode?: string; schemaJson: string }
export interface LoadFlowResult { summary: FlowDefSummary; schemaJson: string }
```
- [ ] **Step 2: `api/oa/designer.ts`**
```typescript
import http from '../http'
export const designerApi = {
  list:  (functionId?: string) => http.get('/oa/designer/list', { params: { functionId } }),
  load:  (flowKey: string) => http.get(`/oa/designer/load/${flowKey}`),
  save:  (body: import('@/types/oa/designer').SaveFlowBody) => http.post('/oa/designer/save', body),
  clone: (sourceFlowKey: string, newFlowKey: string, newFlowName: string) =>
           http.post('/oa/designer/clone', { sourceFlowKey, newFlowKey, newFlowName }),
}
```
- [ ] **Step 3: type-check 绿；Commit** `git commit -m "feat(wfs-Cp): T6 设计器前端 API + 类型"`

---

## Task 7：`DesignerCanvas.vue`（Vue Flow 画布 + 调色板 + 自定义节点）

**Files:** Create `cp6.web/src/views/oa/designer/DesignerCanvas.vue` + `nodes/{StartNode,ApprovalNode,GatewayNode,EndNode}.vue`

- [ ] **Step 1: 自定义节点组件**（`nodes/*.vue`，4 个）— 各渲染一个带 `Handle`(连接点) 的小卡片：`StartNode`(绿,仅出 Handle)、`ApprovalNode`(蓝,出+入 Handle,显审批人策略摘要)、`GatewayNode`(橙菱形,split/join 共用,按 `data.type` 区分)、`EndNode`(灰,仅入 Handle)。用 `@vue-flow/core` 的 `<Handle type="source|target" :position="Position.Bottom|Top" />`。
- [ ] **Step 2: `DesignerCanvas.vue`**（要点）
  - props `{ modelValue: FlowSchemaDto }`，`emit('update:modelValue', schema)` + `emit('select', {kind:'node'|'edge'|null, id})`。
  - `import { VueFlow, useVueFlow } from '@vue-flow/core'` + `import { Background } from '@vue-flow/background'` + `import { Controls } from '@vue-flow/controls'` + `import '@vue-flow/core/dist/style.css'` + `'@vue-flow/core/dist/theme-default.css'`。
  - 用 `schemaToGraph(props.modelValue)` 初始化 `nodes`/`edges`（`ref`），`<VueFlow v-model:nodes="nodes" v-model:edges="edges">`；注册 `#node-start/#node-approval/#node-parallelSplit/#node-parallelJoin/#node-end` 插槽 → 对应自定义节点。
  - 左侧调色板（`NODE_PALETTE`）：每项 `draggable`，`dragstart` 存 type；画布 `@drop` 用 `project({x,y})` 算坐标 → push 新节点（`id` = `n${Date.now()}` 或递增）。
  - 连边：`@connect="onConnect"` → `addEdges([{ id, source, target }])`。
  - 选中：`@node-click`/`@edge-click` → `emit('select', ...)`；`@pane-click` → 清选。
  - 删除：监听 Delete 键或工具栏「删除选中」→ 移除节点/边。
  - 每次 nodes/edges 变化 → `emit('update:modelValue', graphToSchema(nodes.value, edges.value))`（防抖）。
  - `<Background variant="lines"/>`（网格，对齐「顯示格線」开关） + `<Controls/>`（缩放/适配，对齐「放大/縮小/適應畫面」）。
  - **工具栏（对齐离线版）**：撤销/重做（简单 history 栈，记 nodes/edges 快照）· 自動排版（简单分层：按从 start 的 BFS 层级排 Y、同层排 X，回写 position）· 顯示格線开关。
  - **左侧调色板上方加「搜尋狀態」**：输入框过滤画布节点（按 name/code/id），点结果 → 选中并 `fitView` 定位该节点。
- [ ] **Step 3: type-check 绿**（Vue Flow 类型可能需 `@ts-expect-error` 个别处，尽量按其类型定义）。
- [ ] **Step 4: Commit** `git commit -m "feat(wfs-Cp): T7 DesignerCanvas Vue Flow 画布(调色板拖拽+自定义节点+连边+删除)"`

---

## Task 8：属性面板（`NodePropertyPanel` + `EdgePropertyPanel`）

**Files:** Create `cp6.web/src/views/oa/designer/NodePropertyPanel.vue` / `EdgePropertyPanel.vue`

- [ ] **Step 1: `NodePropertyPanel.vue`** — props `{ node: SchemaNode }` + `emit('update', patch)`。**按离线版分「基本參數 / 進階參數 / 知會人員」三段**（`el-collapse`）：
  - **基本參數**：狀態編號(`Code` `el-input`) · 狀態名稱(`name`) · 節點類型(只读) · **下一步審核人類型**（approval 时 `el-select` 策略 DirectManager/DeptLeader/Role/Specified/Starter + 按策略条件：Specified→`userApi.getList` 选人；Role→角色 id；DirectManager→层数 `el-input-number`）+ 会签 `el-select`(all/any/veto，对齐「並簽」=all)。
  - **進階參數**：逾時天數(`TimeoutHours`，注:存小时,UI 天×24) · 允許退回(`el-switch`) · 逾時提醒/自動跳轉(`TimeoutAction` `el-select`:remind/approve/reject/escalate)。
  - **知會人員(CC)**：多选用户 → `CcUsers`。
  变更即 `emit('update', {...})`（父更新对应节点 data）。
- [ ] **Step 2: `EdgePropertyPanel.vue`** — props `{ edge }` + `emit('update', patch)`（对齐离线版「路徑信息維護」）：路徑類型 `el-radio`(無條件/條件) + 條件表达式 `el-input`(類型=條件时显，占位「如 days>3」) + 知會人員多选(路徑 `CcUsers`)。
- [ ] **Step 3: type-check 绿**。
- [ ] **Step 4: Commit** `git commit -m "feat(wfs-Cp): T8 属性面板(节点:策略/会签/超时/CC; 边:条件/CC)"`

---

## Task 9：`DesignerView.vue`（壳：工具条 + 流程列表 + 画布 + 属性面板）

**Files:** Create `cp6.web/src/views/oa/designer/DesignerView.vue`

- [ ] **Step 1: `DesignerView.vue`**（要点）
  - 顶部工具条：流程列表下拉（`designerApi.list()` → 选一条 `load`）或「新建」；身份字段 `el-input` FunctionId + FlowCode + FlowName + FormKey（FormKey 选既有表单或填）；按钮「校验」「保存」「另存为副本(clone)」。
  - 中间 `<DesignerCanvas v-model="schema" @select="onSelect" />`。
  - 右侧 `<NodePropertyPanel v-if="sel.kind==='node'" :node="selNode" @update="patchNode" />` / `<EdgePropertyPanel v-else-if="sel.kind==='edge'" ... />`。
  - 「校验」→ `validateClient(schema)` 非空 → `ElMessage.error` 列出（i18n 文案）；空 → 「校验通过」。
  - 「保存」→ 先 `validateClient` 预检 → `designerApi.save({ flowKey, flowName, formKey, functionId, flowCode, schemaJson: JSON.stringify(schema) })`；后端 E-WF-009/010 经 http 拦截器 toast。
  - 「另存为副本」→ 弹框填 newFlowKey/newFlowName → `designerApi.clone(...)`。
  - 「新建」→ 给一个含 start+end 的初始 schema 模板。
- [ ] **Step 2: type-check + vitest（39）绿**。
- [ ] **Step 3: Commit** `git commit -m "feat(wfs-Cp): T9 DesignerView 壳(工具条+流程列表+画布+属性面板+校验/保存/克隆)"`

---

## Task 10：路由 + 菜单 738 + i18n 五语 + gstack QA

**Files:** Modify `cp6.web/src/router/index.ts`、`CP6.WebApi/Program.cs`；Create `CP6.WebApi/Seed/I18nOaDesignerScreenSeed.cs`、`docs/superpowers/qa/wfs-form-inbox/phaseCprime/`

- [ ] **Step 1: 路由** — `viewModules` 加 `/oa/designer` → `@/views/oa/designer/DesignerView.vue`。
- [ ] **Step 2: i18n seed `I18nOaDesignerScreenSeed.cs`** — grep 设计器视图全部 `t('oa.designer.*')` 键（`grep -rhoE "t\('oa[^']*'\)" cp6.web/src/views/oa/designer`），5 语全覆盖（含 `oa.designer.errNoStart/errNoEnd/errDanglingEdge/errNoStrategy` 校验文案 + 调色板/工具条/属性面板词）+ `nav.738`(流程设计器)。**避开与 Phase B/C seed 的 LangKey 重复**（读 `I18nOaInboxScreenSeed.cs`/`I18nOaAdvancedScreenSeed.cs`）。接 Program.cs `.Concat(...)`。
- [ ] **Step 3: 菜单 738**（Program.cs，幂等 `if(!Sys_Menus.Any(m=>m.MenuId==738))`，ParentId=740，RoleMenu 授 RoleId=1）：738 流程设计器 `/oa/designer`。
- [ ] **Step 4: 编译 + 全前端回归** — `dotnet build CP6.WebApi`；`cd cp6.web && npm run type-check && npx vitest run && npm run build` 全绿。**注意**：Vue Flow 引入后 `npm run build` 体量↑属正常；若 build 报 Vue Flow CSS/SSR 相关问题，按其文档调（client-only 导入）。
- [ ] **Step 5: gstack 真浏览器 QA**（隔离 DB `CP6DB_OA`，同 Phase B/C README 起后端+前端+i18n:pull）固化 `docs/superpowers/qa/wfs-form-inbox/phaseCprime/`：
  1. 进 `/oa/designer` →「新建」→ 调色板拖出 填單→審批→結束，连边，审批节点设 Specified 审批人 → 校验通过 → 填 FunctionId/FlowCode/FlowName/FormKey → 保存（DB 确 Wf_FlowDef 落 schema + 身份码）。
  2. 重开该流程 `load` → 画布还原（节点位置/属性/边）。
  3. 重复 FunctionId 保存另一流程 → E-WF-009 本地化报错。
  4. 故意删掉 end 保存 → E-WF-010 校验报错。
  5. 「另存为副本」→ 新 FlowKey 独立副本（身份码清空、停用）。
  6. **闭环验证**：用设计器编出的流程，去 FlowAdmin(Phase B 734) 启用它 → 信箱填單发起 → 真能按设计跑审批（设计器产物=能跑的真实流程，非手写 JSON）。
- [ ] **Step 6: Commit** `git commit -m "test(wfs-Cp): T10 路由+菜单738+i18n五语+gstack QA(设计→保存→启用→跑通闭环) 固化"`

---

## Phase C′ 完成定义（DoD）

- [ ] 后端：FunctionId/FlowCode 唯一 + FlowNode 坐标 + `FlowSchemaValidator` + `DesignerService`（save 校验+唯一 / clone 独立副本）+ 控制器，`dotnet test` 全绿（≥1251+新增），引擎执行态零回归。
- [ ] 前端：`@vue-flow/core` 画布 + `designerModel`(vitest) + 属性面板 + 设计器壳；`type-check/vitest/build` 全绿。
- [ ] i18n 五语 + 菜单 738；旧无回归。
- [ ] gstack QA：拖拽设计→校验→保存→load 还原→克隆→**启用后真能跑通**（闭环），固化。
- [ ] 每 Task 本地 commit（不 push）。

**▶️ Phase C′ 之后**：引擎 roadmap（串簽顺签 / 系統動作數據回寫=BridgeHook / WebAPI / JOB 定时 / 腳本条件 / 取消·退回设计节点 / Delta 状态机式高级设计器）= 各自另起 plan，随引擎长（umbrella §9）。

---

## Self-Review（写完自查）

- **spec 覆盖**：§4.8 设计器基础版（节点拖拽 T7 / 路徑条件 T8 / 并行 split·join 节点类型 T5·T7 / 并簽 countersign T8 / 节点参数 T8 / 路徑 CC T8 / 审批人 5 策略 T8）✓ · 输出 SchemaJson→SaveDef（T3/T4）✓ · §2.7 functionID/flowcode 1:1 唯一（T1 过滤唯一 + T3 E-WF-009）✓ · 模板克隆独立副本（T3）✓ · 闭环「能跑的真实流程」（T10 QA 第6步）✓。
- **类型一致**：`SaveFlowRequest(FlowKey,FlowName,FormKey,FunctionId?,FlowCode?,SchemaJson)` / `CloneRequest(SourceFlowKey,NewFlowKey,NewFlowName)` / `FlowDefSummary` / 前端 `schemaToGraph`/`graphToSchema`/`validateClient`/`NODE_PALETTE` 全 Task 间一致；节点类型集（start/approval/parallelSplit/parallelJoin/end）前后端统一。
- **占位扫除**：无 TBD；后端对接点（`Wf_FlowDef` 是否 BaseTenantEntity、`IFlowDefService` 是否已注册、`LocalizedControllerBase`/`Ok2` 命名空间、Vue Flow CSS/build 注意）均标「落码核对」并给验收兜底。
- **风险点**：①Vue Flow 与 Vite/SSR/build 的兼容（T10 Step4 标注按其文档 client-only 导入）；②Vue Flow TS 类型（T7 标注尽量按其类型，个别 `@ts-expect-error`）——画布 UI 主要靠 T10 gstack QA 验收，逻辑核心（转换/校验）已 vitest 锁定。③`HasFilter` 过滤唯一索引仅 SqlServer 生效，InMemory 测试不校验唯一（同既有 RowVersion 限制）——唯一性由 T3 服务层 `AnyAsync` 预检兜底（不只靠 DB 索引）。
