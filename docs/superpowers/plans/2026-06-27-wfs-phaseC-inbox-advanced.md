# WFS Phase C（信箱进阶）实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 Phase A（token 内核+读模型）/ Phase B（信箱核心）之上，加 OA 信箱进阶五能力：**代理 act-as**（租户内轻量身份切换，履历双记）+ **转交**（引擎新动作）+ **填單表单库**（机能分类+☆收藏+常用）+ **表單查詢**（多条件搜索）+ **設定**（代理人维护+显示偏好）。

**Architecture:** 三层中的 L2 增量。后端服务全部置 `CP6.Core/Services/Oa/`（消费 `Wf` 引擎），唯一对 `Wf` 引擎的增量是给 `AdvancedFlow` 加 `TransferAsync` + 给办理路径加 `ActAsAsync`（onBehalfOf 线索，复用既有 `UpdateFormToOnHandleAsync`，对既有 `ActAsync` 行为零影响）。新前端置 `cp6.web/src/views/oa/`，act-as 仿 `stores/platform.ts` 的 sessionStorage 态机但**租户内轻量**（无 jti 黑名单/token 重签，每动作服务端 `AssertActiveGrant` 校验）。

**Tech Stack:** .NET 8 / EF Core（SqlServer 运行期 + InMemory 测试）/ xUnit（`CP6.Tests`）；Vue 3 + Element Plus + Pinia + vue-i18n（5 语）/ Vite / Vitest。后端启动项目 `CP6.WebApi`，DbContext + 迁移在 `CP6.Core`。

**配套 spec（落码前必读）：**
- `docs/superpowers/specs/2026-06-26-wfs-form-inbox-unified-design.md`（umbrella；本计划落 §4.4 代理 + §4.5 转交 + §2.5 应用支撑表 + 阶段 C）
- `docs/superpowers/plans/2026-06-27-wfs-phaseB-inbox-core.md`（Phase B，已交付，本计划依赖其信箱服务/视图）

---

## Scope Check（本计划含两 Part）

- **Part A（T1~T8）= 后端**：数据模型 + 引擎转交/act-as + 五服务（Delegate/Catalog+Favorite/Query/Pref）+ 控制器（含 act-as 接入）。独立可测、可交付。
- **Part B（T9~T16）= 前端**：API/类型 + act-as 态机 + 转交 + 填單库 + 起草发起 + 表單查詢 + 設定 + 路由/菜单/i18n + gstack QA。

**不在本计划**：C′ 完整流程设计器（umbrella §4.8 / §5，另起 plan）。审批人解析高级策略（JSON 组/数据/字段驱动）= 引擎 roadmap。

---

## File Structure（先锁分解）

**后端新建（`CP6.Core/Services/Oa/`）：**
- `OaAdvancedModels.cs` — Phase C DTO records（GrantInfo/DelegateItem/CatalogNode/FormCard/QueryFilter/QueryResultItem/InboxPrefDto）
- `IDelegateService.cs` / `DelegateService.cs` — act-as 授权（我能代理谁/谁能代理我）+ `AssertActiveGrantAsync` + 代理人 CRUD
- `ICatalogService.cs` / `CatalogService.cs` — 填單表单库（分类树 + 收藏 + 常用）
- `IFavoriteService.cs` / `FavoriteService.cs` — 收藏增删列
- `IPrefService.cs` / `PrefService.cs` — 显示偏好读写

**后端修改：**
- `CP6.Entity/DomainModels/Wf/Wf_FormFavorite.cs`（新）/ `Wf_InboxPref.cs`（新）
- `CP6.Entity/DomainModels/Wf/Wf_FormDef.cs` — 加 `Category` / `SubCategory`
- `CP6.Core/EFDbContext/CP6Context.cs` — 2 新 DbSet + 唯一索引
- `CP6.Core/Services/Wf/IFlowEngine.cs` — 加 `TransferAsync` + `ActAsAsync`
- `CP6.Core/Services/Wf/AdvancedFlow.cs` — 实现 `TransferAsync`
- `CP6.Core/Services/Wf/FlowEngine.cs` — `ActAsAsync` + 办理路径透传 `onBehalfOf`
- `CP6.Core/Services/Wf/FlowEngine.ReadModel.cs` — `UpdateFormToOnHandleAsync` 加 `onBehalfOf` 参（设 OnBehalfOfId）；加转交读模型双行助手
- `CP6.Core/Services/Oa/IInboxService.cs` / `InboxService.cs` — 加 `QueryAsync` + `ActBatchAsAsync`（act-as 批量）
- `CP6.WebApi/Program.cs` — 注册 5 Oa 服务 + i18n 种子合并 + 菜单 735/736/737

**后端控制器新建（`CP6.WebApi/Controllers/Oa/`）：**
- `DelegateController.cs`（my-grants / settings CRUD）/ `CatalogController.cs` / `QueryController.cs` / `PrefController.cs`；转交端点加入 `InboxController`。

**后端迁移：** `WfsPhaseCAppSupport`（FormFavorite/InboxPref 两表 + FormDef 两列）。

**后端测试（`CP6.Tests/Oa/`）：** `TransferServiceTests`、`ActAsServiceTests`、`DelegateServiceTests`、`CatalogServiceTests`、`QueryServiceTests`、`PrefServiceTests`、`PhaseCModelTests`。

**前端新建：**
- `cp6.web/src/api/oa/{delegate,transfer,catalog,query,pref}.ts`、`cp6.web/src/types/oa/advanced.ts`
- `cp6.web/src/stores/oaActingAs.ts`（sessionStorage 态机）
- `cp6.web/src/views/oa/inbox/TransferDialog.vue`
- `cp6.web/src/views/oa/catalog/FormCatalog.vue` / `FormInitiate.vue`
- `cp6.web/src/views/oa/query/FormQuery.vue`
- `cp6.web/src/views/oa/settings/InboxSettings.vue`
- `cp6.web/src/components/oa/ActingAsBanner.vue`

**前端修改：**
- `cp6.web/src/router/index.ts` — viewModules 加 `/oa/form-catalog`、`/oa/form-search`、`/oa/settings`
- `cp6.web/src/api/http.ts` — 请求拦截器注入 `X-Acting-As` 头（读 oaActingAs store）
- `cp6.web/src/views/oa/inbox/InboxView.vue` — 头像区代理切换入口 + 顶部 `ActingAsBanner`
- `CP6.WebApi/Seed/I18nOaAdvancedScreenSeed.cs`（新）— 五语词条 + nav.735/736/737

---

## 通用约定

- **分支/worktree**：本计划在 **`D:\CP6-oa-core`**（worktree）的 **`feat/oa-inbox-core`** 分支上**续接 Phase B**（Phase C 提交直接堆叠在 Phase B 之上；若希望隔离，从该分支再切 `feat/oa-inbox-advanced`）。**绝不碰 `D:\CP6`**（并发 Space 会话）。Bash cwd 每次重置回 `D:/CP6`，须 `cd /d/CP6-oa-core &&` 前缀或 `git -C`/绝对路径；`dotnet` 用显式 csproj 路径 `/d/CP6-oa-core/CP6.Tests/CP6.Tests.csproj`；前端 `cd /d/CP6-oa-core/cp6.web &&`（node_modules 已装）。**任何 `Space_*` 文件零碰**。
- **测试基线**：Phase B 末 `dotnet test` = **1237 passed / 1 skip**。每 Task 末跑相关测试；触动引擎的 Task 必跑 `--filter "FullyQualifiedName~Wf"` 兼容回归（**任一既有测试转红 = 兼容破坏，回退排查**）。
- **测试 DB 工厂 + 引擎装配**（沿用 Phase B）：
  ```csharp
  private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
      .UseInMemoryDatabase(Guid.NewGuid().ToString())
      .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
  private static FlowEngine Engine(CP6Context db) => new(db, new ApproverResolver(db));
  ```
  > `FlowEngine` 的 `IWfNotifier` 参可选（null → `NullWfNotifier` 无操作），测试两参构造即可。
- **错误码（沿用 §4.6 `E-WF-0xx`，服务抛 `InvalidOperationException("E-WF-0xx")` → 控制器 catch 转 BadRequest）**：
  - `E-WF-001` act-as 无有效授权（伪造代理 / 授权过期/停用）
  - `E-WF-002` 转交目标非法（非待办任务 / 目标用户不存在或非同租户 / 转交给自己）
  - （收藏/偏好幂等，不设错误码）
- **act-as 授权方向（锁定）**：`Wf_FlowDelegate{GrantorId=委托人（被代理人）, DelegateId=代理人}`。**我（me）能 act-as X ⟺ 存在 active `Wf_FlowDelegate{GrantorId=X, DelegateId=me}`**。`AssertActiveGrantAsync(delegateId, grantorId)` 校验之；active = `Enable && ValidFrom<=now && ValidTo>=now`。
- **act-as 接缝（锁定）**：前端切为 X 身份 → sessionStorage 存 `actingAs=X` → http 拦截器对 `/oa/*` 请求注入头 `X-Acting-As: <X>`。控制器 `EffectiveUserAsync()`：若头存在 → `AssertActiveGrantAsync(me, X)`（失败 E-WF-001）→ 返回 X 作有效用户；否则返回 me。**读**用有效用户查询；**写**（办理/转交）记 `ActualHandlerId=me、OnBehalfOfId=X`（X 非空时）。
- **commit**：每 Task 末本地 commit（不 push）。提交体尾行 `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`。

---

# Part A — 后端

## Task 1：数据模型 — Wf_FormFavorite + Wf_InboxPref + FormDef 分类列 + 迁移

**Files:**
- Create: `CP6.Entity/DomainModels/Wf/Wf_FormFavorite.cs`、`CP6.Entity/DomainModels/Wf/Wf_InboxPref.cs`
- Modify: `CP6.Entity/DomainModels/Wf/Wf_FormDef.cs`、`CP6.Core/EFDbContext/CP6Context.cs`
- Test: `CP6.Tests/Oa/PhaseCModelTests.cs`

- [ ] **Step 1: 写失败测试** `CP6.Tests/Oa/PhaseCModelTests.cs`：
```csharp
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests;

public class PhaseCModelTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    [Fact]
    public async Task Favorite_And_Pref_Persist()
    {
        using var db = NewDb();
        var u = Guid.NewGuid();
        db.Wf_FormFavorites.Add(new Wf_FormFavorite { Id = Guid.NewGuid(), UserId = u, FormKey = "leave" });
        db.Wf_InboxPrefs.Add(new Wf_InboxPref { Id = Guid.NewGuid(), UserId = u, PrefsJson = """{"pageSize":20}""" });
        await db.SaveChangesAsync();

        Assert.Equal("leave", (await db.Wf_FormFavorites.SingleAsync()).FormKey);
        Assert.Equal("""{"pageSize":20}""", (await db.Wf_InboxPrefs.SingleAsync()).PrefsJson);
    }

    [Fact]
    public async Task FormDef_HasCategoryColumns()
    {
        using var db = NewDb();
        db.Wf_FormDefs.Add(new Wf_FormDef { Id = Guid.NewGuid(), FormKey = "leave", FormName = "请假",
            FormKey2Category = null, Category = "人事", SubCategory = "假勤" });
        await db.SaveChangesAsync();
        var got = await db.Wf_FormDefs.SingleAsync();
        Assert.Equal("人事", got.Category);
        Assert.Equal("假勤", got.SubCategory);
    }
}
```
> 删除测试里 `FormKey2Category = null,` 这行（它是故意的编译哨兵——确认你在用真实属性名；正式实现时 `Wf_FormDef` 只加 `Category`/`SubCategory` 两属性，测试里去掉该哨兵行）。

- [ ] **Step 2: 跑测试确认失败** — `cd /d/CP6-oa-core && dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~PhaseCModelTests"`（编译失败：实体/DbSet/列未定义；去掉哨兵行后仍因新类型未定义而失败）。

- [ ] **Step 3: 建 `Wf_FormFavorite.cs`**
```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wf;

/// <summary>填單☆收藏（信箱 L2，umbrella §2.5）。唯一 (TenantId,UserId,FormKey)。</summary>
[Table("Wf_FormFavorite")]
public class Wf_FormFavorite : BaseTenantEntity
{
    public Guid UserId { get; set; }
    [MaxLength(100)] public string FormKey { get; set; } = string.Empty;
}
```

- [ ] **Step 4: 建 `Wf_InboxPref.cs`**
```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wf;

/// <summary>信箱显示偏好（umbrella §2.5）。每用户一行，PrefsJson 自由结构。唯一 (TenantId,UserId)。</summary>
[Table("Wf_InboxPref")]
public class Wf_InboxPref : BaseTenantEntity
{
    public Guid UserId { get; set; }
    [Column(TypeName = "nvarchar(max)")] public string PrefsJson { get; set; } = "{}";
}
```

- [ ] **Step 5: `Wf_FormDef.cs` 加分类列**（类内追加）：
```csharp
/// <summary>填單分类（机能大类，umbrella §2.5）。</summary>
[MaxLength(100)] public string? Category { get; set; }

/// <summary>填單子分类。</summary>
[MaxLength(100)] public string? SubCategory { get; set; }
```

- [ ] **Step 6: `CP6Context.cs` 加 DbSet + 唯一索引**
  - DbSet 区（挨现有 `Wf_*` DbSet 后）：
    ```csharp
    public DbSet<Wf_FormFavorite> Wf_FormFavorites { get; set; }
    public DbSet<Wf_InboxPref> Wf_InboxPrefs { get; set; }
    ```
  - 索引配置区（仿现有 `modelBuilder.Entity<Wf_*>` 块）：
    ```csharp
    modelBuilder.Entity<Wf_FormFavorite>(e =>
        e.HasIndex(x => new { x.TenantId, x.UserId, x.FormKey }).IsUnique().HasDatabaseName("UX_Wf_FormFavorite"));
    modelBuilder.Entity<Wf_InboxPref>(e =>
        e.HasIndex(x => new { x.TenantId, x.UserId }).IsUnique().HasDatabaseName("UX_Wf_InboxPref_User"));
    ```

- [ ] **Step 7: 跑测试确认通过** — 去掉哨兵行后 `dotnet test ... --filter "FullyQualifiedName~PhaseCModelTests"` PASS（2 例）。

- [ ] **Step 8: 加迁移** — `cd /d/CP6-oa-core && dotnet ef migrations add WfsPhaseCAppSupport -p CP6.Core -s CP6.WebApi`。打开生成文件核对 `Up()`：仅建 `Wf_FormFavorite`/`Wf_InboxPref` 两表（含唯一索引）+ `Wf_FormDef` 加 `Category`/`SubCategory` 两列，无其他表改动、无 `Space_*`。

- [ ] **Step 9: 兼容回归** — `dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~Wf"` 全绿。

- [ ] **Step 10: Commit**
```bash
cd /d/CP6-oa-core && git add CP6.Entity/DomainModels/Wf/Wf_FormFavorite.cs CP6.Entity/DomainModels/Wf/Wf_InboxPref.cs CP6.Entity/DomainModels/Wf/Wf_FormDef.cs CP6.Core/EFDbContext/CP6Context.cs CP6.Core/Migrations/ CP6.Tests/Oa/PhaseCModelTests.cs && git commit -m "feat(wfs-C): T1 数据模型 FormFavorite/InboxPref 新表 + FormDef 分类列 + 迁移"
```

---

## Task 2：引擎 `TransferAsync`（转交，AdvancedFlow）

**Files:**
- Modify: `CP6.Core/Services/Wf/IFlowEngine.cs`、`CP6.Core/Services/Wf/AdvancedFlow.cs`、`CP6.Core/Services/Wf/FlowEngine.ReadModel.cs`
- Test: `CP6.Tests/Oa/TransferServiceTests.cs`

> 转交 = 把待办**移交**给同租户他人（umbrella §4.5）：原 task `AssigneeId` 改为 `toUserId`（保 TokenId/NodeId，不流转、不改计票）；履历**原行 Transferred + 受让人新 Pending 行**；`AddHistory("transfer")` + 通知。与加签（增加并存审批人）区别明确。

- [ ] **Step 1: 写失败测试** `CP6.Tests/Oa/TransferServiceTests.cs`：
```csharp
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Text.Json;

namespace CP6.Tests;

public class TransferServiceTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static FlowEngine Engine(CP6Context db) => new(db, new ApproverResolver(db));

    private static async Task<(Guid inst, Guid task)> SeedAsync(CP6Context db, Guid starter, Guid approver, Guid receiver)
    {
        db.Sys_Users.AddRange(
            new Sys_User { Id = starter, UserName = "s", Password = "x" },
            new Sys_User { Id = approver, UserName = "a", NickName = "原审批王", Password = "x" },
            new Sys_User { Id = receiver, UserName = "r", NickName = "受让赵", Password = "x" });
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "leave", FlowName = "请假", FormKey = "leave",
            SchemaJson = JsonSerializer.Serialize(new FlowSchema {
                Nodes = { new FlowNode { Id = "n1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = approver },
                          new FlowNode { Id = "end", Type = "end" } },
                Edges = { new FlowEdge { From = "n1", To = "end" } } }),
            Version = 1, Enable = true });
        await db.SaveChangesAsync();
        var inst = await Engine(db).SubmitAsync("leave", starter, "{}");
        var task = await db.Wf_FlowTasks.Where(t => t.Status == FlowTaskStatus.Pending).Select(t => t.Id).SingleAsync();
        return (inst, task);
    }

    [Fact]
    public async Task Transfer_Reassigns_AndWritesDualFormToRows()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var approver = Guid.NewGuid(); var receiver = Guid.NewGuid();
        var (inst, task) = await SeedAsync(db, starter, approver, receiver);

        await Engine(db).TransferAsync(task, approver, receiver, "我出差，转给你");

        // 任务被改派（同一条 task）
        var t = await db.Wf_FlowTasks.SingleAsync(x => x.Id == task);
        Assert.Equal(receiver, t.AssigneeId);
        Assert.Equal(FlowTaskStatus.Pending, t.Status);
        // 履历：原审批人行 Transferred + 受让人新 Pending 行
        var rows = await db.Wf_FlowFormTos.Where(f => f.InstanceId == inst).ToListAsync();
        Assert.Contains(rows, r => r.ExpectedHandlerId == approver && r.Status == FlowFormToStatus.Transferred && r.ActualHandlerId == approver);
        Assert.Contains(rows, r => r.ExpectedHandlerId == receiver && r.Status == FlowFormToStatus.Pending);
    }

    [Fact]
    public async Task Transfer_NotPendingTask_Throws()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var approver = Guid.NewGuid(); var receiver = Guid.NewGuid();
        var (inst, task) = await SeedAsync(db, starter, approver, receiver);
        await Engine(db).ActAsync(task, approver, approve: true, "先批了");   // 任务已办

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Engine(db).TransferAsync(task, approver, receiver, null));
        Assert.Equal("E-WF-002", ex.Message);
    }

    [Fact]
    public async Task Transfer_TargetNotExist_Throws()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var approver = Guid.NewGuid(); var receiver = Guid.NewGuid();
        var (inst, task) = await SeedAsync(db, starter, approver, receiver);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Engine(db).TransferAsync(task, approver, Guid.NewGuid(), null));   // 目标不存在
        Assert.Equal("E-WF-002", ex.Message);
    }
}
```

- [ ] **Step 2: 跑测试确认失败** — `dotnet test ... --filter "FullyQualifiedName~TransferServiceTests"`（`TransferAsync` 未定义）。

- [ ] **Step 3: `IFlowEngine` 加方法**
```csharp
/// <summary>转交（umbrella §4.5）：把 Pending 待办改派给同租户 toUserId（保 TokenId/NodeId，不流转）。
/// 履历原行 Transferred + 受让人新 Pending 行 + AddHistory("transfer") + 通知。非待办/目标非法 → E-WF-002。</summary>
Task TransferAsync(Guid taskId, Guid actorId, Guid toUserId, string? comment = null);
```

- [ ] **Step 4: `FlowEngine.ReadModel.cs` 加转交读模型助手**

先读现有该文件：`UpdateFormToOnHandleAsync`（约 L52-67，办结时按 `(InstanceId,NodeId,TokenId,ExpectedHandlerId,Pending)` 匹配单行并置 Status/ActualHandlerId/HandledAt/Comment）+ **送签时插入 Pending 行的助手**（OnEnter 时建 `Wf_FlowFormTo` 的方法，找到它的名字与签名）。据此加：
```csharp
/// <summary>转交读模型：原行 Transferred(实办=转出人)，受让人新起 Pending 行（同 Token/Node，StepSeq 递增）。</summary>
internal async Task TransferFormToAsync(Guid instanceId, string nodeId, Guid? tokenId,
    Guid fromUserId, Guid toUserId, string? comment)
{
    var src = await _db.Wf_FlowFormTos.FirstOrDefaultAsync(f =>
        f.InstanceId == instanceId && f.NodeId == nodeId && f.TokenId == tokenId &&
        f.ExpectedHandlerId == fromUserId && f.Status == FlowFormToStatus.Pending);
    if (src is not null)
    {
        src.Status = FlowFormToStatus.Transferred;
        src.ActualHandlerId = fromUserId;
        src.HandledAt = DateTime.Now;
        src.Comment = comment;
    }
    _db.Wf_FlowFormTos.Add(new Wf_FlowFormTo
    {
        Id = Guid.NewGuid(), InstanceId = instanceId, TokenId = tokenId,
        StepSeq = await NextStepSeqAsync(instanceId),     // 复用既有 StepSeq 递增助手；若名不同照实改
        FromNodeId = src?.FromNodeId, NodeId = nodeId, NodeCode = src?.NodeCode, NodeName = src?.NodeName,
        ExpectedHandlerId = toUserId, Status = FlowFormToStatus.Pending, SentAt = DateTime.Now,
    });
}
```
> **落码核对**：`NextStepSeqAsync`/`StepSeq` 递增方式照 ReadModel.cs 既有送签插入逻辑（Phase A 已有 `Max(StepSeq)+1` 口径）；`NodeCode/NodeName` 从源行快照复制。若 ReadModel.cs 已有"插入 Pending 行"公用方法，直接复用它建受让人行，仅本助手负责把源行置 Transferred。

- [ ] **Step 5: `AdvancedFlow.cs` 实现 `TransferAsync`**（紧随 `AddSignAsync`/`SendBackAsync` 同类内，仿其装载/校验/通知骨架）
```csharp
public async Task TransferAsync(Guid taskId, Guid actorId, Guid toUserId, string? comment = null)
{
    var task = await _db.Wf_FlowTasks.FirstOrDefaultAsync(t => t.Id == taskId)
               ?? throw new InvalidOperationException("E-WF-002");
    if (task.Status != FlowTaskStatus.Pending) throw new InvalidOperationException("E-WF-002");

    var inst = await _db.Wf_FlowInstances.FirstOrDefaultAsync(i => i.Id == task.InstanceId);
    if (inst is null || inst.Status != FlowInstanceStatus.Running) throw new InvalidOperationException("E-WF-002");

    // 目标须同租户真实用户、且非当前处理人（同租户由全局过滤器保证；查不到=越租户/不存在）
    var toExists = await _db.Sys_Users.AnyAsync(u => u.Id == toUserId);
    if (!toExists || toUserId == task.AssigneeId) throw new InvalidOperationException("E-WF-002");

    var fromUserId = task.AssigneeId;
    await TransferFormToAsync(inst.Id, task.NodeId, task.TokenId, fromUserId, toUserId, comment);

    task.AssigneeId = toUserId;          // 改派（保 TokenId/NodeId/Countersign 不变）
    AddHistory(inst.Id, task.NodeId, actorId, "transfer", comment);
    await _notifier.TodoCreatedAsync(toUserId, inst.Id, task.Id, inst.FlowKey);
    await _db.SaveChangesAsync();
}
```
> `actorId` = 发起转交者（应为 `task.AssigneeId` 或其代理；P1 不在引擎层强校验 actor==assignee——由控制器 `EffectiveUserAsync` 决定有效用户，act-as 时 actorId 仍传有效用户；履历"实办=转出人"记 `fromUserId`）。`_db`/`_notifier`/`AddHistory` 均为 `AdvancedFlow`/`FlowEngine` partial 既有成员。

- [ ] **Step 6: 跑测试确认通过** — `dotnet test ... --filter "FullyQualifiedName~TransferServiceTests"`（3 例）。

- [ ] **Step 7: 兼容回归** — `dotnet test ... --filter "FullyQualifiedName~Wf"` 全绿（仅加新方法 + 读模型新助手，既有路径零改）。

- [ ] **Step 8: Commit**
```bash
cd /d/CP6-oa-core && git add CP6.Core/Services/Wf/IFlowEngine.cs CP6.Core/Services/Wf/AdvancedFlow.cs CP6.Core/Services/Wf/FlowEngine.ReadModel.cs CP6.Tests/Oa/TransferServiceTests.cs && git commit -m "feat(wfs-C): T2 引擎 TransferAsync 转交(改派+履历双行+通知, E-WF-002)"
```

---

## Task 3：引擎 `ActAsAsync`（act-as 办理 onBehalfOf 双记）

**Files:**
- Modify: `CP6.Core/Services/Wf/IFlowEngine.cs`、`CP6.Core/Services/Wf/FlowEngine.cs`、`CP6.Core/Services/Wf/FlowEngine.ReadModel.cs`
- Test: `CP6.Tests/Oa/ActAsServiceTests.cs`

> act-as 办理（umbrella §4.4 / §3）：代理人 me 办理被代理人 X 的待办——办理逻辑与 `ActAsync` 等价（推进/计票），但履历 `ActualHandlerId=me、OnBehalfOfId=X`（双记可追溯）。**`OnBehalfOfId` 列 Phase A 已存在**；本 Task 仅给办理路径透传 `onBehalfOf`，对既有 `ActAsync`（onBehalfOf=null）行为零影响。

- [ ] **Step 1: 写失败测试** `CP6.Tests/Oa/ActAsServiceTests.cs`：
```csharp
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Text.Json;

namespace CP6.Tests;

public class ActAsServiceTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static FlowEngine Engine(CP6Context db) => new(db, new ApproverResolver(db));

    private static async Task<Guid> SeedAsync(CP6Context db, Guid starter, Guid grantor)
    {
        db.Sys_Users.AddRange(
            new Sys_User { Id = starter, UserName = "s", Password = "x" },
            new Sys_User { Id = grantor, UserName = "g", NickName = "被代理X", Password = "x" });
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "leave", FlowName = "请假", FormKey = "leave",
            SchemaJson = JsonSerializer.Serialize(new FlowSchema {
                Nodes = { new FlowNode { Id = "n1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = grantor },
                          new FlowNode { Id = "end", Type = "end" } },
                Edges = { new FlowEdge { From = "n1", To = "end" } } }),
            Version = 1, Enable = true });
        await db.SaveChangesAsync();
        await Engine(db).SubmitAsync("leave", starter, "{}");
        return (await db.Wf_FlowTasks.Where(t => t.Status == FlowTaskStatus.Pending).Select(t => t.Id).SingleAsync());
    }

    [Fact]
    public async Task ActAs_RecordsActualHandler_AndOnBehalfOf()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var grantor = Guid.NewGuid(); var me = Guid.NewGuid();
        var task = await SeedAsync(db, starter, grantor);

        // me 代 grantor 批准（task 归属 grantor）
        await Engine(db).ActAsAsync(task, actorId: me, onBehalfOf: grantor, approve: true, "代签同意");

        var t = await db.Wf_FlowTasks.SingleAsync(x => x.Id == task);
        Assert.Equal(FlowTaskStatus.Approved, t.Status);            // 任务正常办结
        var row = await db.Wf_FlowFormTos.SingleAsync(f => f.ExpectedHandlerId == grantor);
        Assert.Equal(FlowFormToStatus.Approved, row.Status);
        Assert.Equal(me, row.ActualHandlerId);                     // 实办=代理人本人
        Assert.Equal(grantor, row.OnBehalfOfId);                   // 代谁签=被代理人
    }

    [Fact]
    public async Task ActAs_NullOnBehalf_EquivalentToActAsync()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var grantor = Guid.NewGuid();
        var task = await SeedAsync(db, starter, grantor);

        await Engine(db).ActAsAsync(task, actorId: grantor, onBehalfOf: null, approve: true, null);
        var row = await db.Wf_FlowFormTos.SingleAsync(f => f.ExpectedHandlerId == grantor);
        Assert.Equal(grantor, row.ActualHandlerId);
        Assert.Null(row.OnBehalfOfId);
    }
}
```

- [ ] **Step 2: 跑测试确认失败** — `dotnet test ... --filter "FullyQualifiedName~ActAsServiceTests"`（`ActAsAsync` 未定义）。

- [ ] **Step 3: `FlowEngine.ReadModel.cs` — `UpdateFormToOnHandleAsync` 加 `onBehalfOf` 参**

读现有签名（约 L52-67）。改为接受可空 `onBehalfOf`：用 `expected = onBehalfOf ?? actorId` 匹配 Pending 行（act-as 时匹配 X 的行）；设 `ActualHandlerId = actorId`、`OnBehalfOfId = onBehalfOf`：
```csharp
internal async Task UpdateFormToOnHandleAsync(Guid instanceId, string nodeId, Guid? tokenId,
    Guid actorId, bool approve, string? comment, Guid? onBehalfOf = null)   // ← 末加可选参
{
    var expected = onBehalfOf ?? actorId;
    var row = await _db.Wf_FlowFormTos.FirstOrDefaultAsync(f =>
        f.InstanceId == instanceId && f.NodeId == nodeId && f.TokenId == tokenId &&
        f.ExpectedHandlerId == expected && f.Status == FlowFormToStatus.Pending);
    if (row is null) return;
    row.Status = approve ? FlowFormToStatus.Approved : FlowFormToStatus.Rejected;
    row.ActualHandlerId = actorId;
    row.OnBehalfOfId = onBehalfOf;
    row.HandledAt = DateTime.Now;
    row.Comment = comment;
}
```
> **所有既有调用点**（onBehalfOf 缺省=null）行为不变。**落码核对**：以 ReadModel.cs 真实字段/匹配条件为准微调，保持"既有调用零改"。

- [ ] **Step 4: `FlowEngine.cs` — 办理内核透传 `onBehalfOf` + 公开 `ActAsAsync`**

读 `ActAsync`/`ActOnceAsync`（约 L113-181）。给内核办理方法加可选 `onBehalfOf`（默认 null），在调用 `UpdateFormToOnHandleAsync` 处把它传下去；办理所用的"关卡处理人/计票口径"以**任务 `task.AssigneeId`**（act-as 时 = X）为准，与 onBehalfOf 一致，故无需额外改计票。新增公开方法：
```csharp
/// <summary>act-as 办理：actorId(代理人) 代 onBehalfOf(被代理人) 办理其待办；履历 ActualHandler=actorId、OnBehalfOf=onBehalfOf。
/// onBehalfOf=null 时行为同 ActAsync。授权由控制器 AssertActiveGrant 把关（引擎不查委派）。</summary>
public Task ActAsAsync(Guid taskId, Guid actorId, Guid? onBehalfOf, bool approve, string? comment = null)
    => ActOnceAsync(taskId, /*acting assignee*/ onBehalfOf ?? actorId, approve, comment, onBehalfOf);
```
> **关键**：`ActOnceAsync` 既有第二参（原 `actorId`）语义是"办理这条任务的处理人"——它被用于幂等/计票/履历匹配。act-as 时这个"处理人"应是 **X（onBehalfOf）**（任务归属 X），而"实际点击人"me 只进 `ActualHandlerId`。因此把 `onBehalfOf ?? actorId` 作 ActOnceAsync 的处理人参，并额外把 `onBehalfOf` 透传到 `UpdateFormToOnHandleAsync`。**读 ActOnceAsync 实现确认**：若其内部并不直接用第二参做履历 ActualHandler、而是有独立链路，按实际把 `actualClicker=actorId`/`onBehalfOf` 正确落到读模型即可（以 Step 1 两个断言为验收）。既有 `ActAsync` 签名/行为不动。

- [ ] **Step 5: 跑测试确认通过** — `dotnet test ... --filter "FullyQualifiedName~ActAsServiceTests"`（2 例）。

- [ ] **Step 6: 兼容回归** — `dotnet test ... --filter "FullyQualifiedName~Wf"` 全绿（既有 `ActAsync` 路径 onBehalfOf=null，零行为变化）。

- [ ] **Step 7: Commit**
```bash
cd /d/CP6-oa-core && git add CP6.Core/Services/Wf/IFlowEngine.cs CP6.Core/Services/Wf/FlowEngine.cs CP6.Core/Services/Wf/FlowEngine.ReadModel.cs CP6.Tests/Oa/ActAsServiceTests.cs && git commit -m "feat(wfs-C): T3 引擎 ActAsAsync act-as办理 onBehalfOf 履历双记(既有ActAsync零改)"
```

---

## Task 4：`DelegateService`（act-as 授权 + 代理人 CRUD）

**Files:**
- Create: `CP6.Core/Services/Oa/OaAdvancedModels.cs`、`CP6.Core/Services/Oa/IDelegateService.cs`、`CP6.Core/Services/Oa/DelegateService.cs`
- Test: `CP6.Tests/Oa/DelegateServiceTests.cs`

> 授权方向：`Wf_FlowDelegate{GrantorId=委托人, DelegateId=代理人}`。me 能 act-as X ⟺ active `{GrantorId=X, DelegateId=me}`。`MyGrantsAsync(me)` 返回"我能代理谁(ICanActAs)/谁能代理我(CanActForMe)"；`AssertActiveGrantAsync(me, X)` 失败抛 E-WF-001。代理人 CRUD = 维护"我授出的委派"（GrantorId=me）。

- [ ] **Step 1: 写失败测试** `CP6.Tests/Oa/DelegateServiceTests.cs`：
```csharp
using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests;

public class DelegateServiceTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static IDelegateService Svc(CP6Context db) => new DelegateService(db);

    [Fact]
    public async Task MyGrants_ResolvesBothDirections()
    {
        using var db = NewDb();
        var me = Guid.NewGuid(); var x = Guid.NewGuid(); var y = Guid.NewGuid();
        db.Sys_Users.AddRange(
            new Sys_User { Id = x, UserName = "x", NickName = "X 经理", Password = "p" },
            new Sys_User { Id = y, UserName = "y", NickName = "Y 同事", Password = "p" });
        // X 授我；我授 Y
        db.Wf_FlowDelegates.AddRange(
            new Wf_FlowDelegate { Id = Guid.NewGuid(), GrantorId = x, DelegateId = me, Enable = true,
                ValidFrom = DateTime.Now.AddDays(-1), ValidTo = DateTime.Now.AddDays(1) },
            new Wf_FlowDelegate { Id = Guid.NewGuid(), GrantorId = me, DelegateId = y, Enable = true,
                ValidFrom = DateTime.Now.AddDays(-1), ValidTo = DateTime.Now.AddDays(1) });
        await db.SaveChangesAsync();

        var g = await Svc(db).MyGrantsAsync(me);
        Assert.Contains(g.ICanActAs, u => u.UserId == x && u.UserName == "X 经理");
        Assert.Contains(g.CanActForMe, u => u.UserId == y);
    }

    [Fact]
    public async Task AssertActiveGrant_OkWhenActive_ThrowsWhenMissingOrExpired()
    {
        using var db = NewDb();
        var me = Guid.NewGuid(); var x = Guid.NewGuid();
        db.Wf_FlowDelegates.Add(new Wf_FlowDelegate { Id = Guid.NewGuid(), GrantorId = x, DelegateId = me, Enable = true,
            ValidFrom = DateTime.Now.AddDays(-1), ValidTo = DateTime.Now.AddDays(1) });
        await db.SaveChangesAsync();

        await Svc(db).AssertActiveGrantAsync(me, x);   // 不抛

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Svc(db).AssertActiveGrantAsync(me, Guid.NewGuid()));
        Assert.Equal("E-WF-001", ex.Message);
    }

    [Fact]
    public async Task AddAndRemove_MyDelegate()
    {
        using var db = NewDb();
        var me = Guid.NewGuid(); var y = Guid.NewGuid();
        var id = await Svc(db).AddDelegateAsync(me, y, DateTime.Now, DateTime.Now.AddDays(7), null, "休假代理");
        Assert.Single(await Svc(db).ListMyDelegatesAsync(me));
        await Svc(db).RemoveDelegateAsync(me, id);
        Assert.Empty(await Svc(db).ListMyDelegatesAsync(me));
    }
}
```

- [ ] **Step 2: 跑测试确认失败** — `dotnet test ... --filter "FullyQualifiedName~DelegateServiceTests"`。

- [ ] **Step 3: 建 `OaAdvancedModels.cs`**
```csharp
namespace CP6.Core.Services.Oa;

// ── act-as 授权 ──
public record GrantUser(Guid UserId, string UserName);
public record MyGrants(IReadOnlyList<GrantUser> ICanActAs, IReadOnlyList<GrantUser> CanActForMe);
public record DelegateItem(Guid Id, Guid GrantorId, Guid DelegateId, string DelegateName,
    DateTime ValidFrom, DateTime ValidTo, bool Enable, string? Scope, string? Remark);

// ── 填單表单库 ──
public record FormCard(string FormKey, string FormName, string? Category, string? SubCategory, bool Favorite);
public record CatalogNode(string Category, IReadOnlyList<CatalogSub> Subs);
public record CatalogSub(string SubCategory, IReadOnlyList<FormCard> Forms);

// ── 表單查詢 ──
public record FormQueryFilter(Guid? StarterId, Guid? HandlerId, string? FlowKey, string? Keyword,
    int? Status, DateTime? From, DateTime? To);
public record FormQueryItem(Guid InstanceId, string FlowKey, string? FlowName, Guid StarterId, string StarterName,
    int Status, string CurrentNode, DateTime CreateDate);
```

- [ ] **Step 4: 建 `IDelegateService.cs`**
```csharp
namespace CP6.Core.Services.Oa;

/// <summary>代理 act-as 授权（umbrella §4.4）。复用 Wf_FlowDelegate。</summary>
public interface IDelegateService
{
    Task<MyGrants> MyGrantsAsync(Guid userId);                                  // 我能代理谁 / 谁能代理我
    Task AssertActiveGrantAsync(Guid delegateId, Guid grantorId);              // 校验 me 可 act-as grantor，否则 E-WF-001
    Task<IReadOnlyList<DelegateItem>> ListMyDelegatesAsync(Guid grantorId);    // 我授出的委派（設定页）
    Task<Guid> AddDelegateAsync(Guid grantorId, Guid delegateId, DateTime from, DateTime to, string? scope, string? remark);
    Task RemoveDelegateAsync(Guid grantorId, Guid id);                         // 仅能删自己授出的
}
```

- [ ] **Step 5: 建 `DelegateService.cs`**
```csharp
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

public class DelegateService : IDelegateService
{
    private readonly CP6Context _db;
    public DelegateService(CP6Context db) { _db = db; }

    private IQueryable<Wf_FlowDelegate> Active() => _db.Wf_FlowDelegates
        .Where(d => d.Enable && d.ValidFrom <= DateTime.Now && d.ValidTo >= DateTime.Now);

    public async Task<MyGrants> MyGrantsAsync(Guid userId)
    {
        var canActAsIds = await Active().Where(d => d.DelegateId == userId).Select(d => d.GrantorId).Distinct().ToListAsync();
        var actForMeIds = await Active().Where(d => d.GrantorId == userId).Select(d => d.DelegateId).Distinct().ToListAsync();
        var names = await OaUserNames.ResolveAsync(_db, canActAsIds.Concat(actForMeIds));
        GrantUser U(Guid id) => new(id, names.GetValueOrDefault(id, id.ToString()));
        return new MyGrants(canActAsIds.Select(U).ToList(), actForMeIds.Select(U).ToList());
    }

    public async Task AssertActiveGrantAsync(Guid delegateId, Guid grantorId)
    {
        var ok = await Active().AnyAsync(d => d.DelegateId == delegateId && d.GrantorId == grantorId);
        if (!ok) throw new InvalidOperationException("E-WF-001");
    }

    public async Task<IReadOnlyList<DelegateItem>> ListMyDelegatesAsync(Guid grantorId)
    {
        var rows = await _db.Wf_FlowDelegates.Where(d => d.GrantorId == grantorId)
            .OrderByDescending(d => d.CreateDate).ToListAsync();
        var names = await OaUserNames.ResolveAsync(_db, rows.Select(r => r.DelegateId));
        return rows.Select(d => new DelegateItem(d.Id, d.GrantorId, d.DelegateId,
            names.GetValueOrDefault(d.DelegateId, d.DelegateId.ToString()),
            d.ValidFrom, d.ValidTo, d.Enable, d.Scope, d.Remark)).ToList();
    }

    public async Task<Guid> AddDelegateAsync(Guid grantorId, Guid delegateId, DateTime from, DateTime to, string? scope, string? remark)
    {
        var d = new Wf_FlowDelegate { Id = Guid.NewGuid(), GrantorId = grantorId, DelegateId = delegateId,
            ValidFrom = from, ValidTo = to, Enable = true, Scope = scope, Remark = remark, Creator = grantorId.ToString() };
        _db.Wf_FlowDelegates.Add(d);
        await _db.SaveChangesAsync();
        return d.Id;
    }

    public async Task RemoveDelegateAsync(Guid grantorId, Guid id)
    {
        var d = await _db.Wf_FlowDelegates.FirstOrDefaultAsync(x => x.Id == id && x.GrantorId == grantorId);
        if (d is null) return;   // 幂等 / 仅能删自己授出的
        _db.Wf_FlowDelegates.Remove(d);
        await _db.SaveChangesAsync();
    }
}
```

- [ ] **Step 6: 跑测试确认通过** — `dotnet test ... --filter "FullyQualifiedName~DelegateServiceTests"`（3 例）。

- [ ] **Step 7: Commit**
```bash
cd /d/CP6-oa-core && git add CP6.Core/Services/Oa/OaAdvancedModels.cs CP6.Core/Services/Oa/IDelegateService.cs CP6.Core/Services/Oa/DelegateService.cs CP6.Tests/Oa/DelegateServiceTests.cs && git commit -m "feat(wfs-C): T4 DelegateService act-as授权(MyGrants/AssertActiveGrant/代理人CRUD, E-WF-001)"
```

---

## Task 5：`CatalogService` + `FavoriteService`（填單表单库：分类 + 收藏）

**Files:**
- Create: `CP6.Core/Services/Oa/IFavoriteService.cs`、`FavoriteService.cs`、`ICatalogService.cs`、`CatalogService.cs`
- Test: `CP6.Tests/Oa/CatalogServiceTests.cs`

> 填單库（umbrella §4.2 FormCatalog）：按 `Wf_FormDef.Category→SubCategory` 组装表单卡片树，标注当前用户 ☆收藏（`Wf_FormFavorite`）；收藏增删幂等（唯一约束）。常用 = 收藏的快捷集。

- [ ] **Step 1: 写失败测试** `CP6.Tests/Oa/CatalogServiceTests.cs`：
```csharp
using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests;

public class CatalogServiceTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static IFavoriteService Fav(CP6Context db) => new FavoriteService(db);
    private static ICatalogService Cat(CP6Context db) => new CatalogService(db, Fav(db));

    private static async Task SeedFormsAsync(CP6Context db)
    {
        db.Wf_FormDefs.AddRange(
            new Wf_FormDef { Id = Guid.NewGuid(), FormKey = "leave", FormName = "请假单", Category = "人事", SubCategory = "假勤", Enable = true },
            new Wf_FormDef { Id = Guid.NewGuid(), FormKey = "expense", FormName = "报销单", Category = "财务", SubCategory = "费用", Enable = true },
            new Wf_FormDef { Id = Guid.NewGuid(), FormKey = "off", FormName = "停用单", Category = "人事", SubCategory = "假勤", Enable = false }); // 停用不入库
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Catalog_GroupsByCategory_FlagsFavorite()
    {
        using var db = NewDb();
        var me = Guid.NewGuid();
        await SeedFormsAsync(db);
        await Fav(db).AddAsync(me, "leave");

        var tree = await Cat(db).CatalogAsync(me);
        Assert.Equal(2, tree.Count);                                   // 人事 / 财务
        var hr = tree.Single(n => n.Category == "人事");
        var card = hr.Subs.Single().Forms.Single(f => f.FormKey == "leave");
        Assert.True(card.Favorite);
        Assert.DoesNotContain(tree.SelectMany(n => n.Subs).SelectMany(s => s.Forms), f => f.FormKey == "off"); // 停用排除
    }

    [Fact]
    public async Task Favorite_AddIdempotent_AndRemove()
    {
        using var db = NewDb();
        var me = Guid.NewGuid();
        await SeedFormsAsync(db);
        await Fav(db).AddAsync(me, "leave");
        await Fav(db).AddAsync(me, "leave");                           // 幂等：不重复
        Assert.Single(await Fav(db).ListAsync(me));
        await Fav(db).RemoveAsync(me, "leave");
        Assert.Empty(await Fav(db).ListAsync(me));
    }
}
```

- [ ] **Step 2: 跑测试确认失败** — `dotnet test ... --filter "FullyQualifiedName~CatalogServiceTests"`。

- [ ] **Step 3: 建 `IFavoriteService.cs` + `FavoriteService.cs`**
```csharp
namespace CP6.Core.Services.Oa;

public interface IFavoriteService
{
    Task AddAsync(Guid userId, string formKey);        // 幂等
    Task RemoveAsync(Guid userId, string formKey);
    Task<IReadOnlyList<string>> ListAsync(Guid userId); // 收藏的 FormKey
}
```
```csharp
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

public class FavoriteService : IFavoriteService
{
    private readonly CP6Context _db;
    public FavoriteService(CP6Context db) { _db = db; }

    public async Task AddAsync(Guid userId, string formKey)
    {
        if (await _db.Wf_FormFavorites.AnyAsync(f => f.UserId == userId && f.FormKey == formKey)) return; // 幂等
        _db.Wf_FormFavorites.Add(new Wf_FormFavorite { Id = Guid.NewGuid(), UserId = userId, FormKey = formKey });
        await _db.SaveChangesAsync();
    }

    public async Task RemoveAsync(Guid userId, string formKey)
    {
        var f = await _db.Wf_FormFavorites.FirstOrDefaultAsync(x => x.UserId == userId && x.FormKey == formKey);
        if (f is null) return;
        _db.Wf_FormFavorites.Remove(f);
        await _db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<string>> ListAsync(Guid userId) =>
        await _db.Wf_FormFavorites.Where(f => f.UserId == userId).Select(f => f.FormKey).ToListAsync();
}
```

- [ ] **Step 4: 建 `ICatalogService.cs` + `CatalogService.cs`**
```csharp
namespace CP6.Core.Services.Oa;

public interface ICatalogService
{
    Task<IReadOnlyList<CatalogNode>> CatalogAsync(Guid userId);   // 分类树 + 收藏标注
}
```
```csharp
using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

public class CatalogService : ICatalogService
{
    private readonly CP6Context _db;
    private readonly IFavoriteService _fav;
    public CatalogService(CP6Context db, IFavoriteService fav) { _db = db; _fav = fav; }

    public async Task<IReadOnlyList<CatalogNode>> CatalogAsync(Guid userId)
    {
        var favs = (await _fav.ListAsync(userId)).ToHashSet();
        var defs = await _db.Wf_FormDefs.Where(d => d.Enable)
            .Select(d => new { d.FormKey, d.FormName, d.Category, d.SubCategory }).ToListAsync();
        return defs
            .GroupBy(d => d.Category ?? "未分类")
            .OrderBy(g => g.Key)
            .Select(catGrp => new CatalogNode(catGrp.Key,
                catGrp.GroupBy(d => d.SubCategory ?? "其他").OrderBy(s => s.Key)
                      .Select(subGrp => new CatalogSub(subGrp.Key,
                          subGrp.OrderBy(d => d.FormName)
                                .Select(d => new FormCard(d.FormKey, d.FormName, d.Category, d.SubCategory, favs.Contains(d.FormKey)))
                                .ToList()))
                      .ToList()))
            .ToList();
    }
}
```

- [ ] **Step 5: 跑测试确认通过** — `dotnet test ... --filter "FullyQualifiedName~CatalogServiceTests"`（2 例）。

- [ ] **Step 6: Commit**
```bash
cd /d/CP6-oa-core && git add CP6.Core/Services/Oa/IFavoriteService.cs CP6.Core/Services/Oa/FavoriteService.cs CP6.Core/Services/Oa/ICatalogService.cs CP6.Core/Services/Oa/CatalogService.cs CP6.Tests/Oa/CatalogServiceTests.cs && git commit -m "feat(wfs-C): T5 Catalog+Favorite 填單表单库(分类树+☆收藏幂等)"
```

---

## Task 6：`InboxService.QueryAsync`（表單查詢多条件）

**Files:**
- Modify: `CP6.Core/Services/Oa/IInboxService.cs`、`CP6.Core/Services/Oa/InboxService.cs`
- Test: `CP6.Tests/Oa/QueryServiceTests.cs`

> 表單查詢（umbrella §4.2 FormQuery）：按 发起人/处理人/流程类型/状态/日期区间/关键词 多条件过滤实例。空条件=不限。

- [ ] **Step 1: 写失败测试** `CP6.Tests/Oa/QueryServiceTests.cs`：
```csharp
using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Text.Json;

namespace CP6.Tests;

public class QueryServiceTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static FlowEngine Engine(CP6Context db) => new(db, new ApproverResolver(db));
    private static IForecastService Forecast(CP6Context db) => new ForecastService(db, new ApproverResolver(db));
    private static IInboxService Inbox(CP6Context db) => new InboxService(db, Engine(db), Forecast(db));

    [Fact]
    public async Task Query_FiltersByStarterAndFlowKey()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var approver = Guid.NewGuid();
        db.Sys_Users.AddRange(
            new Sys_User { Id = starter, UserName = "s", NickName = "发起李", Password = "x" },
            new Sys_User { Id = approver, UserName = "a", Password = "x" });
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "leave", FlowName = "请假", FormKey = "leave",
            SchemaJson = JsonSerializer.Serialize(new FlowSchema {
                Nodes = { new FlowNode { Id = "n1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = approver },
                          new FlowNode { Id = "end", Type = "end" } },
                Edges = { new FlowEdge { From = "n1", To = "end" } } }),
            Version = 1, Enable = true });
        await db.SaveChangesAsync();
        await Engine(db).SubmitAsync("leave", starter, "{}");

        var hit = await Inbox(db).QueryAsync(new FormQueryFilter(starter, null, "leave", null, null, null, null));
        var item = Assert.Single(hit);
        Assert.Equal("发起李", item.StarterName);
        Assert.Equal("leave", item.FlowKey);

        var miss = await Inbox(db).QueryAsync(new FormQueryFilter(Guid.NewGuid(), null, null, null, null, null, null));
        Assert.Empty(miss);
    }
}
```

- [ ] **Step 2: 跑测试确认失败** — `dotnet test ... --filter "FullyQualifiedName~QueryServiceTests"`。

- [ ] **Step 3: `IInboxService` 加方法**
```csharp
    // ── 表單查詢（Phase C）──
    Task<IReadOnlyList<FormQueryItem>> QueryAsync(FormQueryFilter filter);
```

- [ ] **Step 4: `InboxService` 实现**
```csharp
    public async Task<IReadOnlyList<FormQueryItem>> QueryAsync(FormQueryFilter f)
    {
        var q = _db.Wf_FlowInstances.AsQueryable();
        if (f.StarterId is { } s) q = q.Where(i => i.StarterId == s);
        if (!string.IsNullOrWhiteSpace(f.FlowKey)) q = q.Where(i => i.FlowKey == f.FlowKey);
        if (f.Status is { } st) q = q.Where(i => i.Status == st);
        if (f.From is { } fr) q = q.Where(i => i.CreateDate >= fr);
        if (f.To is { } to) q = q.Where(i => i.CreateDate <= to);
        if (f.HandlerId is { } h)   // 处理人：我办过/正办该实例
            q = q.Where(i => _db.Wf_FlowFormTos.Any(ft => ft.InstanceId == i.Id
                && (ft.ExpectedHandlerId == h || ft.ActualHandlerId == h)));
        if (!string.IsNullOrWhiteSpace(f.Keyword))
            q = q.Where(i => i.FlowKey.Contains(f.Keyword!) || (i.BizId != null && i.BizId.Contains(f.Keyword!)));

        var rows = await (from i in q
                          join d in _db.Wf_FlowDefs on i.FlowKey equals d.FlowKey into dd
                          from d in dd.DefaultIfEmpty()
                          join u in _db.Sys_Users on i.StarterId equals u.Id into uu
                          from u in uu.DefaultIfEmpty()
                          orderby i.CreateDate descending
                          select new { i, FlowName = d == null ? null : d.FlowName, Starter = u }).Take(500).ToListAsync();
        return rows.Select(x => new FormQueryItem(x.i.Id, x.i.FlowKey, x.FlowName, x.i.StarterId,
            Name(x.Starter), x.i.Status, x.i.CurrentNode, x.i.CreateDate)).ToList();
    }
```
> `Name(Sys_User?)` 私有助手 Phase B T6 已加（NickName ?? UserName）。`Take(500)` 防爆量；后续可分页。

- [ ] **Step 5: 跑测试确认通过** — `dotnet test ... --filter "FullyQualifiedName~QueryServiceTests"`（1 例）+ `--filter "FullyQualifiedName~InboxServiceTests"`（Phase B 10 例不回归）。

- [ ] **Step 6: Commit**
```bash
cd /d/CP6-oa-core && git add CP6.Core/Services/Oa/IInboxService.cs CP6.Core/Services/Oa/InboxService.cs CP6.Tests/Oa/QueryServiceTests.cs && git commit -m "feat(wfs-C): T6 InboxService.QueryAsync 表單查詢多条件(发起/处理人/类型/状态/日期/关键词)"
```

---

## Task 7：`PrefService`（显示偏好）

**Files:**
- Create: `CP6.Core/Services/Oa/IPrefService.cs`、`PrefService.cs`
- Test: `CP6.Tests/Oa/PrefServiceTests.cs`

> 显示偏好（umbrella §2.5 Wf_InboxPref）：每用户一行 PrefsJson（分页数/隐藏取消单/主旨概要…）。Get 无则返回默认空对象；Save upsert。

- [ ] **Step 1: 写失败测试** `CP6.Tests/Oa/PrefServiceTests.cs`：
```csharp
using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests;

public class PrefServiceTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static IPrefService Svc(CP6Context db) => new PrefService(db);

    [Fact]
    public async Task Get_DefaultsEmpty_Save_Upserts()
    {
        using var db = NewDb();
        var me = Guid.NewGuid();
        Assert.Equal("{}", await Svc(db).GetAsync(me));            // 无则默认 {}
        await Svc(db).SaveAsync(me, """{"pageSize":50}""");
        Assert.Equal("""{"pageSize":50}""", await Svc(db).GetAsync(me));
        await Svc(db).SaveAsync(me, """{"pageSize":20}""");        // upsert 覆盖（不重复行）
        Assert.Equal("""{"pageSize":20}""", await Svc(db).GetAsync(me));
        Assert.Equal(1, await db.Wf_InboxPrefs.CountAsync(p => p.UserId == me));
    }
}
```

- [ ] **Step 2: 跑测试确认失败** — `dotnet test ... --filter "FullyQualifiedName~PrefServiceTests"`。

- [ ] **Step 3: 建 `IPrefService.cs` + `PrefService.cs`**
```csharp
namespace CP6.Core.Services.Oa;

public interface IPrefService
{
    Task<string> GetAsync(Guid userId);          // 无则 "{}"
    Task SaveAsync(Guid userId, string prefsJson); // upsert
}
```
```csharp
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

public class PrefService : IPrefService
{
    private readonly CP6Context _db;
    public PrefService(CP6Context db) { _db = db; }

    public async Task<string> GetAsync(Guid userId) =>
        (await _db.Wf_InboxPrefs.FirstOrDefaultAsync(p => p.UserId == userId))?.PrefsJson ?? "{}";

    public async Task SaveAsync(Guid userId, string prefsJson)
    {
        var p = await _db.Wf_InboxPrefs.FirstOrDefaultAsync(x => x.UserId == userId);
        if (p is null)
            _db.Wf_InboxPrefs.Add(new Wf_InboxPref { Id = Guid.NewGuid(), UserId = userId, PrefsJson = prefsJson ?? "{}" });
        else { p.PrefsJson = prefsJson ?? "{}"; p.ModifyDate = DateTime.Now; }
        await _db.SaveChangesAsync();
    }
}
```

- [ ] **Step 4: 跑测试确认通过** — PASS（1 例）。

- [ ] **Step 5: Commit**
```bash
cd /d/CP6-oa-core && git add CP6.Core/Services/Oa/IPrefService.cs CP6.Core/Services/Oa/PrefService.cs CP6.Tests/Oa/PrefServiceTests.cs && git commit -m "feat(wfs-C): T7 PrefService 显示偏好(upsert/默认空)"
```

---

## Task 8：控制器 + DI（转交 / act-as 接入 / delegate / catalog / query / pref）

**Files:**
- Modify: `CP6.WebApi/Controllers/Oa/InboxController.cs`（加 transfer + act-as 有效用户）
- Create: `CP6.WebApi/Controllers/Oa/DelegateController.cs`、`CatalogController.cs`、`QueryController.cs`、`PrefController.cs`
- Modify: `CP6.WebApi/Program.cs`（DI）

> 控制器模式照 Phase B（`LocalizedControllerBase`、`(await _ctx.GetAsync()).UserId`、`Ok2(data)`、`Err(e)→BadRequest{code=400,message}`）。**act-as 接缝**：在 OA 控制器加 `EffectiveUserAsync()`——读请求头 `X-Acting-As`，非空则 `AssertActiveGrantAsync(me, X)` 后返 X，否则返 me；写动作另记 onBehalfOf。控制器无单测（服务层已测），验收 = 编译 + 全量回归绿 + 装配。

- [ ] **Step 1: `InboxController` 加 act-as 有效用户 + 写动作 act-as + 转交端点**

注入 `IDelegateService _delegate` + `IFlowEngine _engine`。加私有助手 + 改写写动作：
```csharp
// 读请求头 X-Acting-As：非空→校验授权→返回被代理人；否则返回本人。返回 (effective, onBehalfOf)
private async Task<(Guid effective, Guid? onBehalfOf)> EffectiveAsync()
{
    var me = (await _ctx.GetAsync()).UserId;
    var hdr = Request.Headers["X-Acting-As"].ToString();
    if (Guid.TryParse(hdr, out var x) && x != Guid.Empty && x != me)
    {
        await _delegate.AssertActiveGrantAsync(me, x);   // 失败抛 E-WF-001
        return (x, x);
    }
    return (me, null);
}
```
- **读端点**（pending/pending-cc/running/done/stats/detail/draft-list）：把 `MeAsync()` 换成 `(await EffectiveAsync()).effective`，并 try/catch(InvalidOperationException)→Err（act-as 校验可能抛 E-WF-001）。
- **批量办理** `POST batch`：
  ```csharp
  [HttpPost("batch")]
  public async Task<IActionResult> Batch([FromBody] BatchReq r)
  {
      try
      {
          var me = (await _ctx.GetAsync()).UserId;
          var (eff, onBehalf) = await EffectiveAsync();
          return Ok2(await _inbox.ActBatchAsAsync(me, onBehalf, r.TaskIds, r.Approve, r.Comment));
      }
      catch (InvalidOperationException e) { return Err(e); }
  }
  ```
- **转交** `POST transfer` body `{ Guid TaskId, Guid ToUserId, string? Comment }`：
  ```csharp
  public record TransferReq(Guid TaskId, Guid ToUserId, string? Comment);
  [HttpPost("transfer")]
  public async Task<IActionResult> Transfer([FromBody] TransferReq r)
  {
      try
      {
          var (eff, _) = await EffectiveAsync();   // 转出人=有效用户（act-as 时=被代理人）
          await _engine.TransferAsync(r.TaskId, eff, r.ToUserId, r.Comment);
          return Ok2(true);
      }
      catch (InvalidOperationException e) { return Err(e); }
  }
  ```

- [ ] **Step 2: `InboxService` 加 `ActBatchAsAsync`**（IInboxService + InboxService）
```csharp
    // 接口
    Task<IReadOnlyList<BatchActResultItem>> ActBatchAsAsync(Guid actorId, Guid? onBehalfOf, IReadOnlyList<Guid> taskIds, bool approve, string? comment = null);
```
```csharp
    // 实现：与 ActBatchAsync 同骨架，逐条 _engine.ActAsAsync(taskId, actorId, onBehalfOf, approve, comment)；
    // 校验任务归属用 (onBehalfOf ?? actorId)
    public async Task<IReadOnlyList<BatchActResultItem>> ActBatchAsAsync(
        Guid actorId, Guid? onBehalfOf, IReadOnlyList<Guid> taskIds, bool approve, string? comment = null)
    {
        var owner = onBehalfOf ?? actorId;
        var results = new List<BatchActResultItem>();
        foreach (var taskId in taskIds.Distinct())
        {
            var t = await _db.Wf_FlowTasks.FirstOrDefaultAsync(x => x.Id == taskId);
            if (t is null || t.AssigneeId != owner || t.Status != FlowTaskStatus.Pending)
            { results.Add(new BatchActResultItem(taskId, false, "E-WF-004")); continue; }
            try { await _engine.ActAsAsync(taskId, actorId, onBehalfOf, approve, comment); results.Add(new(taskId, true, null)); }
            catch (InvalidOperationException e) { results.Add(new(taskId, false, e.Message)); }
        }
        return results;
    }
```
> 既有 `ActBatchAsync`（Phase B T7）保留不动（onBehalfOf=null 等价路径，旧 InboxController 若仍调它也照常）；新控制器走 `ActBatchAsAsync`。

- [ ] **Step 3: 建 `DelegateController.cs`**（`api/oa/delegate`，`[Authorize]`）
  - `GET my-grants` → `MyGrantsAsync(me)`。
  - `GET list` → `ListMyDelegatesAsync(me)`。
  - `POST add` body `{ Guid DelegateId, DateTime ValidFrom, DateTime ValidTo, string? Scope, string? Remark }` → `AddDelegateAsync(me, ...)`（try/catch）。
  - `POST remove` body `{ Guid Id }` → `RemoveDelegateAsync(me, Id)`。

- [ ] **Step 4: 建 `CatalogController.cs`**（`api/oa/catalog`）
  - `GET tree` → `CatalogAsync(me)`。
  - `POST favorite` body `{ string FormKey, bool On }` → On ? `AddAsync(me,FormKey)` : `RemoveAsync(me,FormKey)`。

- [ ] **Step 5: 建 `QueryController.cs`**（`api/oa/query`）
  - `POST search` body = `FormQueryFilter`（用 record 接收或逐字段）→ `_inbox.QueryAsync(filter)`（try/catch）。

- [ ] **Step 6: 建 `PrefController.cs`**（`api/oa/pref`）
  - `GET get` → `{ prefsJson = GetAsync(me) }`。
  - `POST save` body `{ string PrefsJson }` → `SaveAsync(me, PrefsJson)`。

- [ ] **Step 7: Program.cs 注册 5 服务**（接 4.0d 段后）
```csharp
// 4.0e OA 信箱进阶（Phase C）
builder.Services.AddScoped<CP6.Core.Services.Oa.IDelegateService, CP6.Core.Services.Oa.DelegateService>();
builder.Services.AddScoped<CP6.Core.Services.Oa.IFavoriteService, CP6.Core.Services.Oa.FavoriteService>();
builder.Services.AddScoped<CP6.Core.Services.Oa.ICatalogService, CP6.Core.Services.Oa.CatalogService>();
builder.Services.AddScoped<CP6.Core.Services.Oa.IPrefService, CP6.Core.Services.Oa.PrefService>();
```

- [ ] **Step 8: 编译 + 全量回归** — `cd /d/CP6-oa-core && dotnet build CP6.WebApi/CP6.WebApi.csproj` 成功；`dotnet test CP6.Tests/CP6.Tests.csproj` 全绿（Phase B 1237 + Phase C 新增，无回归）。

- [ ] **Step 9: Commit（Part A 收尾）**
```bash
cd /d/CP6-oa-core && git add CP6.WebApi/Controllers/Oa/ CP6.Core/Services/Oa/IInboxService.cs CP6.Core/Services/Oa/InboxService.cs CP6.WebApi/Program.cs && git commit -m "feat(wfs-C): T8 控制器(transfer/act-as接入/delegate/catalog/query/pref)+DI(Part A 收尾)"
```

---

# Part B — 前端

> **通用前端约定**（沿用 Phase B）：`import http from '../http'`；列表 `el-table`；视图用 `t()`；类型置 `src/types/oa/`；纯逻辑抽 `*.ts` 走 vitest。**act-as**：`stores/oaActingAs.ts`（sessionStorage）+ http 拦截器注头 `X-Acting-As`。

## Task 9：前端 API 层 + TS 类型（Phase C）

**Files:** Create `cp6.web/src/types/oa/advanced.ts` + `cp6.web/src/api/oa/{delegate,transfer,catalog,query,pref}.ts`

- [ ] **Step 1: `types/oa/advanced.ts`**（对齐后端 DTO，camelCase）
```typescript
export interface GrantUser { userId: string; userName: string }
export interface MyGrants { iCanActAs: GrantUser[]; canActForMe: GrantUser[] }
export interface DelegateItem { id: string; grantorId: string; delegateId: string; delegateName: string;
  validFrom: string; validTo: string; enable: boolean; scope?: string; remark?: string }
export interface FormCard { formKey: string; formName: string; category?: string; subCategory?: string; favorite: boolean }
export interface CatalogSub { subCategory: string; forms: FormCard[] }
export interface CatalogNode { category: string; subs: CatalogSub[] }
export interface FormQueryFilter { starterId?: string; handlerId?: string; flowKey?: string; keyword?: string;
  status?: number; from?: string; to?: string }
export interface FormQueryItem { instanceId: string; flowKey: string; flowName?: string; starterId: string;
  starterName: string; status: number; currentNode: string; createDate: string }
```

- [ ] **Step 2: API 模块**
  - `api/oa/delegate.ts`：`myGrants() / list() / add(body) / remove(id)`。
  - `api/oa/transfer.ts`：`transfer(taskId, toUserId, comment) => http.post('/oa/inbox/transfer', { taskId, toUserId, comment })`。
  - `api/oa/catalog.ts`：`tree() => http.get('/oa/catalog/tree')`、`favorite(formKey, on) => http.post('/oa/catalog/favorite', { formKey, on })`。
  - `api/oa/query.ts`：`search(filter) => http.post('/oa/query/search', filter)`。
  - `api/oa/pref.ts`：`get() => http.get('/oa/pref/get')`、`save(prefsJson) => http.post('/oa/pref/save', { prefsJson })`。
- [ ] **Step 3: type-check** — `cd /d/CP6-oa-core/cp6.web && npm run type-check` 绿。
- [ ] **Step 4: Commit** `git commit -m "feat(wfs-C): T9 前端 OA 进阶 API + TS 类型"`

---

## Task 10：act-as 态机 store + http 头注入 + 头像入口 + 横幅

**Files:** Create `cp6.web/src/stores/oaActingAs.ts`、`cp6.web/src/components/oa/ActingAsBanner.vue`；Modify `cp6.web/src/api/http.ts`、`cp6.web/src/views/oa/inbox/InboxView.vue`；Test `cp6.web/src/stores/oaActingAs.test.ts`

> 仿 `stores/platform.ts` 的 sessionStorage 态机（**per-tab 隔离**），但租户内轻量：仅存 `{ userId, userName }`，无 token 重签/jti。

- [ ] **Step 1: 写失败测试 `oaActingAs.test.ts`**（vitest，纯 store 逻辑）
```typescript
import { describe, it, expect, beforeEach } from 'vitest'
import { setActingAs, getActingAs, clearActingAs } from './oaActingAs'

describe('oaActingAs', () => {
  beforeEach(() => sessionStorage.clear())
  it('set/get/clear roundtrip', () => {
    expect(getActingAs()).toBeNull()
    setActingAs({ userId: 'u1', userName: 'X 经理' })
    expect(getActingAs()?.userId).toBe('u1')
    clearActingAs()
    expect(getActingAs()).toBeNull()
  })
})
```
> 若 vitest 环境无 `sessionStorage`，在测试顶部加 jsdom 环境注释 `// @vitest-environment jsdom`（项目既有前端测试已配 jsdom 则免）。

- [ ] **Step 2: 跑测试确认失败** — `cd /d/CP6-oa-core/cp6.web && npx vitest run src/stores/oaActingAs.test.ts`。
- [ ] **Step 3: 建 `oaActingAs.ts`**（纯函数 + 可选 Pinia store 包装）
```typescript
const KEY = 'cp6_oa_acting_as'
export interface ActingAs { userId: string; userName: string }
export function setActingAs(a: ActingAs) { sessionStorage.setItem(KEY, JSON.stringify(a)) }
export function getActingAs(): ActingAs | null {
  const s = sessionStorage.getItem(KEY); if (!s) return null
  try { return JSON.parse(s) as ActingAs } catch { return null }
}
export function clearActingAs() { sessionStorage.removeItem(KEY) }
```
- [ ] **Step 4: 跑测试确认通过**；`npx vitest run`（既有 34 全绿，+1）。
- [ ] **Step 5: `http.ts` 请求拦截器注头** — 在请求拦截器内（仅对 `/oa/` 路径）：`const a = getActingAs(); if (a && config.url?.includes('/oa/')) config.headers['X-Acting-As'] = a.userId`。读 `http.ts` 既有拦截器结构，最小插入。
- [ ] **Step 6: `ActingAsBanner.vue`** — `getActingAs()` 非空时显「正以 {{userName}} 身份处理 · 切回本人」横幅，点击 `clearActingAs()` + 刷新当前路由/重载列表。
- [ ] **Step 7: `InboxView.vue` 接入** — 顶部加 `<ActingAsBanner/>`；头像/工具区加「代理身份」下拉：调 `delegateApi.myGrants()` 列 `iCanActAs`，选一人 → `setActingAs({userId,userName})` + 重载列表；「切回本人」→ `clearActingAs()`。
- [ ] **Step 8: type-check + vitest 绿**。
- [ ] **Step 9: Commit** `git commit -m "feat(wfs-C): T10 act-as 态机(sessionStorage)+X-Acting-As头注入+头像入口+横幅"`

---

## Task 11：转交对话框（FormDetail / 未處理接入 TransferAsync）

**Files:** Create `cp6.web/src/views/oa/inbox/TransferDialog.vue`；Modify `FormDetail.vue`（+转交按钮）

- [ ] **Step 1: `TransferDialog.vue`** — props `{ taskId }`，`el-dialog`：用户选择器（`el-select` 远程搜同租户用户——复用既有用户下拉 API；若无则 `el-input` 填 userId 占位）+ 意见 `el-input`；确认 → `transferApi.transfer(taskId, toUserId, comment)` → 成功 `ElMessage` + `emit('done')`（父刷新/关详情）。
- [ ] **Step 2: `FormDetail.vue` 接入** — 底部操作条（有 `myTaskId` 时）加「转交」按钮 → 打开 `TransferDialog :task-id="myTaskId"`，`@done` 后重载详情 + `emit('done')`。
- [ ] **Step 3: type-check 绿**。
- [ ] **Step 4: Commit** `git commit -m "feat(wfs-C): T11 转交对话框(FormDetail 接入 transfer)"`

---

## Task 12：`FormCatalog` 填單表单库视图

**Files:** Create `cp6.web/src/views/oa/catalog/FormCatalog.vue`

- [ ] **Step 1: `FormCatalog.vue`** — `catalogApi.tree()` → `CatalogNode[]`：左/上「常用」区（favorite 卡片）+ 机能大类 `el-collapse`/`el-tabs` → 子类 → 表单卡片（`el-card`：名称 + ☆收藏切换 `catalogApi.favorite(formKey,!fav)` 乐观更新 + 「填寫」按钮 → `router.push('/oa/form-initiate?formKey=xxx')` 或打开 `FormInitiate`）。
- [ ] **Step 2: type-check 绿**。
- [ ] **Step 3: Commit** `git commit -m "feat(wfs-C): T12 FormCatalog 填單表单库(分类树+☆收藏+常用)"`

---

## Task 13：`FormInitiate` 起草发起（DynamicForm 可填 + 预览 + 存草稿/提交）

**Files:** Create `cp6.web/src/views/oa/catalog/FormInitiate.vue`

> 选定表单 → 取其 FormDef schema（`formApi.getDef(formKey)`，`@/api/wf/form`）+ 绑定 1:1 流程（`flowAdminApi`/约定 FormKey→FlowKey）→ `DynamicForm` 可填（非 readonly）→ **提交前预览审批节点**（`forecastApi.preview(flowKey, varsJson)`）→ 存草稿（`draftApi.save(flowKey, varsJson)`）或提交（save 后 `draftApi.submit(id)`）。

- [ ] **Step 1: `FormInitiate.vue`** — props/query `formKey`：
  - 载入 FormDef schema（复用 `@/api/wf/form` 的 `getDef`）→ `DynamicForm :schema :mask`(可编辑，无 readonly mask) `v-model="model"`。
  - 「预览流程」按钮 → `forecastApi.preview(flowKey, JSON.stringify(model))` → 复用 `FlowTimeline`（仅 forecast 段）展示预计审批人。
  - 「存暫存」→ `draftApi.save(flowKey, JSON.stringify(model))`；「提交」→ `formRef.validate()` 通过后 `draftApi.save` 再 `draftApi.submit(id)` → `ElMessage` + 跳信箱。
  - flowKey 来源：约定每表单专属流程，`flowAdminApi.get`/列表按 FormKey 找启用流程的 FlowKey（无启用流程 → 提示「该表单未配置启用流程」）。
- [ ] **Step 2: type-check 绿**。
- [ ] **Step 3: Commit** `git commit -m "feat(wfs-C): T13 FormInitiate 起草发起(DynamicForm可填+forecast预览+存草稿/提交)"`

---

## Task 14：`FormQuery` 表單查詢视图

**Files:** Create `cp6.web/src/views/oa/query/FormQuery.vue`

- [ ] **Step 1: `FormQuery.vue`** — 条件区：发起人/处理人（用户下拉，可空）+ 流程类型（`flowAdminApi.list()` 下拉）+ 状态（`el-select`：进行中/通过/驳回/撤回/挂起）+ 日期区间（`el-date-picker type="daterange"`）+ 关键词 `el-input`。查询 → `queryApi.search(filter)` → `FormQueryItem[]` `el-table`（状态用 `instanceStatusType/Text`，复用 inboxModel）→ 行点击打开详情抽屉（复用 `FormDetail`）。
- [ ] **Step 2: type-check 绿**。
- [ ] **Step 3: Commit** `git commit -m "feat(wfs-C): T14 FormQuery 表單查詢(多条件+结果表+详情)"`

---

## Task 15：`InboxSettings` 設定（代理人管理 + 显示偏好）

**Files:** Create `cp6.web/src/views/oa/settings/InboxSettings.vue`

- [ ] **Step 1: `InboxSettings.vue`** — `el-tabs`：
  - **代理人設定**：`delegateApi.list()` → `el-table`（代理人/有效期/范围/备注/删）；「新增」`el-dialog`（选代理人 + `el-date-picker` 有效期 + 备注）→ `delegateApi.add(...)`；删 → `ElMessageBox.confirm` → `delegateApi.remove(id)`。
  - **显示偏好**：`prefApi.get()` → 表单（分页数 `el-input-number` / 隐藏取消单 `el-switch` / 主旨概要 `el-switch`）→ 「保存」`prefApi.save(JSON.stringify(prefs))`。
- [ ] **Step 2: type-check 绿**。
- [ ] **Step 3: Commit** `git commit -m "feat(wfs-C): T15 InboxSettings 設定(代理人管理+显示偏好)"`

---

## Task 16：路由 + 菜单 735/736/737 + i18n 五语 + gstack QA

**Files:** Modify `cp6.web/src/router/index.ts`、`CP6.WebApi/Program.cs`；Create `CP6.WebApi/Seed/I18nOaAdvancedScreenSeed.cs`、`docs/superpowers/qa/wfs-form-inbox/phaseC/`

- [ ] **Step 1: 路由** — `viewModules` 加 `/oa/form-catalog`→`FormCatalog.vue`、`/oa/form-search`→`FormQuery.vue`、`/oa/settings`→`InboxSettings.vue`（`/oa/form-initiate` 可作 standalone 或 catalog 内对话框）。
- [ ] **Step 2: i18n seed `I18nOaAdvancedScreenSeed.cs`** — grep 所有新视图的 `t('oa.*')` 键（`grep -rhoE "t\('oa[^']*'\)" cp6.web/src/views/oa/{catalog,query,settings} cp6.web/src/components/oa cp6.web/src/views/oa/inbox/TransferDialog.vue`），5 语全覆盖；加 `nav.735`(填單)/`nav.736`(表單查詢)/`nav.737`(設定) + act-as 横幅词 + 转交词 + `E-WF-001`/`E-WF-002`（Phase B seed 若已含可跳过，避免 LangKey 重复）。接 Program.cs `.Concat(I18nOaAdvancedScreenSeed.Items)`。
- [ ] **Step 3: 菜单 735/736/737**（Program.cs，幂等 `if(!Sys_Menus.Any(m=>m.MenuId==735))`，ParentId=740，RoleMenu 授 RoleId=1）：735 填單`/oa/form-catalog`、736 表單查詢`/oa/form-search`、737 設定`/oa/settings`。
- [ ] **Step 4: 编译 + 全前端回归** — `dotnet build CP6.WebApi`；`cd cp6.web && npm run type-check && npx vitest run && npm run build` 全绿。
- [ ] **Step 5: gstack 真浏览器 QA**（隔离 DB `CP6DB_OA`，同 Phase B README 方式起后端+前端+i18n:pull）固化 `docs/superpowers/qa/wfs-form-inbox/phaseC/`：
  1. 設定页加一条「X→me」委派；切「代理身份=X」→ 横幅显示 → 未處理显 X 的待办 → 代办批准 → 详情时间线显「me（代 X 签）」（DB 确 ActualHandlerId=me/OnBehalfOfId=X）。
  2. 转交：未處理选一待办 → 转交给他人 → 受让人未處理出现该单（DB 确 task.AssigneeId 改 + 履历原行 Transferred + 新 Pending）。
  3. 填單：FormCatalog 分类浏览 + ☆收藏 → 填寫 → forecast 预览 → 提交 → 信箱在途出现。
  4. 表單查詢：多条件查 → 结果 → 详情。
- [ ] **Step 6: Commit** `git commit -m "test(wfs-C): T16 路由+菜单735/736/737+i18n五语+gstack QA 固化"`

---

## Phase C 完成定义（DoD）

- [ ] 后端：引擎 `TransferAsync`+`ActAsAsync`（既有 `ActAsync`/Wf 测试零回归）+ 五服务（Delegate/Catalog/Favorite/Query/Pref）+ 控制器（act-as 接入）全装配，`dotnet test` 全绿（≥1237+新增）。
- [ ] 前端：act-as 态机 + 转交 + 填單库 + 起草发起 + 表單查詢 + 設定；`type-check/vitest/build` 全绿。
- [ ] i18n 五语 seed + 菜单 735/736/737；旧无回归。
- [ ] gstack 真浏览器 QA：act-as 切换全流程 + 转交 + 填单发起 + 查询，固化。
- [ ] 每 Task 本地 commit（不 push）。

**▶️ Phase C 之后**：`C′ 基础版流程设计器`（umbrella §4.8/§5）= 下一阶段另起 plan；引擎 roadmap（串簽/系統動作/WebAPI/JOB/高级审批人策略）随后。

---

## Self-Review（写完自查）

- **spec 覆盖**：代理 act-as（T3/T4/T8/T10）✓ · 转交（T2/T8/T11）✓ · 填單表单库分类+收藏（T1/T5/T12/T13）✓ · 表單查詢（T6/T8/T14）✓ · 設定 代理人+偏好（T1/T4/T7/T8/T15）✓ · §3 履历双记（T3 OnBehalfOf / T2 转交双行）✓ · §2.5 三表/列（T1）✓ · §4.6 错误码 E-WF-001/002（T4/T2 + i18n T16）✓。
- **类型一致**：`ActAsAsync(taskId,actorId,onBehalfOf,approve,comment)` / `TransferAsync(taskId,actorId,toUserId,comment)` / `AssertActiveGrantAsync(delegateId,grantorId)` / DTO（MyGrants/GrantUser/DelegateItem/FormCard/CatalogNode/FormQueryFilter/FormQueryItem）全 Task 间一致。
- **占位扫除**：无 TBD；引擎内部对接点（`UpdateFormToOnHandleAsync` 签名、`ActOnceAsync` 第二参语义、`NextStepSeq`/送签插入助手名）均标「落码核对」并给验收断言兜底（仿 Phase B T2 成功经验）。
- **风险点**：T3（act-as 办理透传）最微妙——已用两条断言（ActualHandlerId=me / OnBehalfOfId=X）+ 兼容回归双闸把关；执行时实现者须实读 `ActOnceAsync`/`UpdateFormToOnHandleAsync` 再落码，不盲抄。
