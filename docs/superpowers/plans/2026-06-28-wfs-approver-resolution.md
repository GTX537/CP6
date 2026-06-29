# 审批人解析高级策略 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 扩 `IApproverResolver` 闭 umbrella §1.5.3 四缺口：表单字段指定(FormField)/数据驱动映射(DataMap)/混源组(Group)/角色+条件(When 门控 + Filter 候选过滤)，并全栈贯通(设计器/维护页/表单控件/forecast/i18n/QA)。

**Architecture:** 核心接缝=把实例 `inst.VarsJson` 注入 `ApproverResolveContext`(可选字段)。新增 3 个 `ApproverStrategy` 枚举 + 递归 `ApproverRule`(扁平合并组) + 新表 `Wf_ApproverMap`。设计期走方案 X(节点/档保留扁平叶字段，仅 Group 用嵌套 Members)，对已上 main 的串簽路径零扰动。向后兼容铁律：无新配置→与今天逐字等价。

**Tech Stack:** .NET 8 / EF Core(SQL Server, InMemory 测试) / xUnit / Vue 3 + element-plus + Vue Flow / vitest。

**Spec:** `docs/superpowers/specs/2026-06-28-wfs-approver-resolution-design.md`(§0~§13，决策 D1~D9)。

---

## 工作纪律(每个 Task 必读)

- **worktree**：全部改动只在 `D:/CP6-wfs-approver`(分支 `feat/wfs-approver-resolve`，off `main` f90a138)。**绝不碰** `D:/CP6`(脏分支 feat/wfs-inbox-core) 与 `D:/CP6-space-backend`(Space 会话)。
- **Bash cwd 每次重置回 `D:/CP6`** → 所有命令先 `cd /d/CP6-wfs-approver &&`，或用绝对路径 / `git -C D:/CP6-wfs-approver`。
- **TDD**：先写失败测试→跑红→最小实现→跑绿→本地 commit(**不 push**，会话权限拦)。
- **硬闸(每 Task 末跑)**：
  - 引擎字节等价 = `cd /d/CP6-wfs-approver && dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~Wf"` 全绿(≈80 engine 测，基线绿)。
  - 本 Task 新测按类名跑：`--filter "FullyQualifiedName~<TestClass>"`。
  - 波次边界 / 前端前跑全量：`dotnet test CP6.Tests/CP6.Tests.csproj`(基线 1320 passed/1 skip)。
- **复核**：commit 后 `git -C D:/CP6-wfs-approver show --stat HEAD` 核验**零 Space 文件**(无 `CP6.Core/Services/Space*`、`cp6.web/src/views/space*`、`Space*` 迁移)、零越界。
- **i18n**：视图用 `t()`(运行时键，免重生 keys.generated)；新键经后端 seed，**不跑 `i18n:pull`**(与 Space 共用 CP6DB，延到 live QA)。
- **commit message**：`feat(wfs-approver): T<n> …` 末加 `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`。

---

## 文件结构(决策锁定)

**后端 CP6.Entity**
- 创建 `CP6.Entity/DomainModels/Wf/Wf_ApproverMap.cs` — 映射表实体。

**后端 CP6.Core**
- 改 `Services/Wf/IApproverResolver.cs` — 枚举 +3 / `ApproverRule` 递归扩 / `ApproverResolveContext.VarsJson`。
- 改 `Services/Wf/ApproverResolver.cs` — When 门控 + FormField/DataMap/Group + Filter + 变量命名空间。
- 改 `Services/Wf/FlowSchema.cs` — `ApproverSpec` + FlowNode/ApprovalStage 新字段。
- 改 `Services/Wf/FlowEngine.cs` — `BuildRule` 映射新字段 + `MapSpec`。
- 改 `Services/Wf/ApprovalStagePlanner.cs` — fixed 档建富 rule + managerChain probe 传 VarsJson。
- 改 `Services/Wf/FlowEngine.Serial.cs` — `EnterStageAsync` 传 VarsJson。
- 改 `Services/Wf/NodeHandlers/ApprovalNodeHandler.cs` — 单档传 VarsJson。
- 改 `Services/Wf/FlowSchemaValidator.cs` — 新策略 + E-WF-014。
- 改 `Services/Oa/ForecastService.cs` — planner/ResolveRuleNamesAsync 传 varsJson。
- 创建 `Services/Wf/IApproverMapService.cs` + `ApproverMapService.cs` — CRUD + E-WF-015。
- 改 `EFDbContext/CP6Context.cs` — DbSet + 索引。
- 创建迁移 `Migrations/<ts>_WfsApproverMap.cs`(EF 工具生成)。

**后端 CP6.WebApi**
- 创建 `Controllers/Oa/ApproverMapController.cs`。
- 创建 `Seed/I18nOaApproverScreenSeed.cs`。
- 改 `Program.cs` — DI(IApproverMapService) + i18n concat + 菜单 seed。

**前端 cp6.web**
- 改 `src/views/oa/designer/designerModel.ts` + `designerModel.test.ts` — ApproverSpec + 新字段 + round-trip + validateClient。
- 改 `src/views/oa/designer/NodePropertyPanel.vue` — 新策略编辑。
- 创建 `src/api/oa/approverMap.ts` + 类型。
- 创建 `src/views/oa/admin/ApproverMapView.vue` — 维护页。
- 改 `src/views/wf/DynamicForm.vue` — user 字段升级真选择器。
- 改 `src/router/index.ts`(或 OA 路由) — 维护页路由。

**测试**
- 创建 `CP6.Tests/ApproverResolverAdvancedTests.cs`(namespace `CP6.Tests`)。
- 创建 `CP6.Tests/Wf/ApproverMapServiceTests.cs`(namespace `CP6.Tests.Wf`)。
- 改 `CP6.Tests/Oa/FlowSchemaValidatorTests.cs`、`CP6.Tests/Oa/ForecastServiceTests.cs`。
- 改 `cp6.web/src/views/oa/designer/designerModel.test.ts`。

**QA**
- 创建 `docs/superpowers/qa/wfs-approver-resolution/`(README + seed.sql + scripts)。

---

# P-A 引擎内核

## Task 1：`Wf_ApproverMap` 实体 + DbSet + 索引 + 迁移

**Files:**
- Create: `CP6.Entity/DomainModels/Wf/Wf_ApproverMap.cs`
- Modify: `CP6.Core/EFDbContext/CP6Context.cs`(DbSet 区 ~L401 后 + 索引区 ~L700 后)
- Create: `CP6.Core/Migrations/<ts>_WfsApproverMap.cs`(EF 生成)
- Test: `CP6.Tests/Wf/ApproverMapServiceTests.cs`(本 Task 先放持久化冒烟测，T7 续扩)

- [ ] **Step 1：写失败测试(实体可持久化 + 索引字段)**

创建 `CP6.Tests/Wf/ApproverMapServiceTests.cs`：
```csharp
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Wf;

public class ApproverMapServiceTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .Options);

    [Fact]
    public async Task ApproverMap_Persists_AndQueriesByKeyAndValue()
    {
        using var db = NewDb();
        db.Wf_ApproverMaps.Add(new Wf_ApproverMap
        {
            Id = Guid.NewGuid(), MapKey = "cc", MatchValue = "A100",
            ApproverUserId = Guid.NewGuid(), Enable = true,
        });
        await db.SaveChangesAsync();

        var row = await db.Wf_ApproverMaps.FirstOrDefaultAsync(m => m.MapKey == "cc" && m.MatchValue == "A100" && m.Enable);
        Assert.NotNull(row);
        Assert.NotNull(row!.ApproverUserId);
    }
}
```

- [ ] **Step 2：跑红**

Run: `cd /d/CP6-wfs-approver && dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~ApproverMapServiceTests"`
Expected: FAIL — `Wf_ApproverMap` / `Wf_ApproverMaps` 不存在(编译错误)。

- [ ] **Step 3：建实体**

创建 `CP6.Entity/DomainModels/Wf/Wf_ApproverMap.cs`：
```csharp
using System.ComponentModel.DataAnnotations;

namespace CP6.Entity.DomainModels.Wf;

/// <summary>审批人映射表(②b Menu 数据驱动)。一条=某命名映射下某匹配值对应一个审批目标(用户或角色)。
/// 同 (MapKey,MatchValue) 可多行(多审批人→会签组)。租户隔离走 BaseTenantEntity 全局过滤器。</summary>
public class Wf_ApproverMap : BaseTenantEntity
{
    [MaxLength(100)] public string MapKey { get; set; } = "";
    [MaxLength(200)] public string MatchValue { get; set; } = "";
    public Guid? ApproverUserId { get; set; }
    public int? ApproverRoleId { get; set; }
    public int OrderNo { get; set; }
    public bool Enable { get; set; } = true;
}
```
> 确认 `BaseTenantEntity` 命名空间：与 `Wf_FlowInstance`(`CP6.Entity.DomainModels.Wf`) 同处可直接用(无需 using)。

- [ ] **Step 4：注册 DbSet + 索引**

`CP6.Core/EFDbContext/CP6Context.cs`，DbSet 区(紧接 L401 `Wf_Notifications` 后)：
```csharp
    public DbSet<Wf_ApproverMap> Wf_ApproverMaps { get; set; }
```
OnModelCreating 索引区(紧接 L700 `Wf_Notification` 索引后)：
```csharp
        modelBuilder.Entity<Wf_ApproverMap>(e =>
            e.HasIndex(x => new { x.TenantId, x.MapKey, x.MatchValue }).HasDatabaseName("IX_Wf_ApproverMap_Lookup"));
```

- [ ] **Step 5：跑绿(InMemory 自建表)**

Run: `cd /d/CP6-wfs-approver && dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~ApproverMapServiceTests"`
Expected: PASS。

- [ ] **Step 6：生成迁移**

Run: `cd /d/CP6-wfs-approver && dotnet ef migrations add WfsApproverMap --project CP6.Core --startup-project CP6.WebApi`
Expected: 生成 `CP6.Core/Migrations/<ts>_WfsApproverMap.cs`。**打开核验 Up()**：仅 `CreateTable("Wf_ApproverMap"…)` + `CreateIndex("IX_Wf_ApproverMap_Lookup"…)`，**无任何其他表/列改动**(若混入别的 pending 改动→说明 main 漂移，停下报告)。

- [ ] **Step 7：引擎闸 + 复核 + commit**

Run: `cd /d/CP6-wfs-approver && dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~Wf"`
Expected: PASS(零回归)。
```bash
git -C D:/CP6-wfs-approver add CP6.Entity CP6.Core
git -C D:/CP6-wfs-approver show --stat HEAD  # 核验前确认无 Space
git -C D:/CP6-wfs-approver commit -m "feat(wfs-approver): T1 Wf_ApproverMap 实体+DbSet+索引+迁移WfsApproverMap

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 2：`ApproverStrategy` 枚举 +3 / `ApproverRule` 递归扩 / `ApproverResolveContext.VarsJson`

**Files:**
- Modify: `CP6.Core/Services/Wf/IApproverResolver.cs`
- Test: `CP6.Tests/ApproverResolverAdvancedTests.cs`(创建)

- [ ] **Step 1：写失败测试(构造富 rule + 既有 5 策略不回归)**

创建 `CP6.Tests/ApproverResolverAdvancedTests.cs`：
```csharp
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests;

public class ApproverResolverAdvancedTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .Options);

    [Fact]
    public void RichRule_ConstructsWithOptionalMembers()
    {
        var leaf = new ApproverRule(ApproverStrategy.Starter, null, null, null) { When = "amount > 10" };
        var grp = new ApproverRule(ApproverStrategy.Group, null, null, null) { Members = new[] { leaf } };
        Assert.Equal("amount > 10", grp.Members!.Single().When);
        Assert.Equal(ApproverStrategy.Group, grp.Strategy);
    }

    [Fact]
    public async Task ExistingStrategies_StillWork_WithVarsJsonNull()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid();
        var res = await new ApproverResolver(db).ResolveAsync(
            new ApproverRule(ApproverStrategy.Starter, null, null, null),
            new ApproverResolveContext { StarterUserId = starter, VarsJson = null });
        Assert.Equal(starter, res.ApproverIds.Single());
    }
}
```

- [ ] **Step 2：跑红**

Run: `cd /d/CP6-wfs-approver && dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~ApproverResolverAdvancedTests"`
Expected: FAIL — `ApproverStrategy.Group` / `Members` / `When` / `VarsJson` 不存在(编译错误)。

- [ ] **Step 3：扩枚举 + record + context**

`CP6.Core/Services/Wf/IApproverResolver.cs`：枚举末尾(L17 `Starter,` 后)加：
```csharp
    /// <summary>表单字段指定(③ Delta 02)：VarsJson[FieldName] → UserId(s)。</summary>
    FormField,
    /// <summary>数据驱动(②b Delta 17)：VarsJson[FieldName] 匹配值 → 查 Wf_ApproverMap(MapKey)。</summary>
    DataMap,
    /// <summary>JSON 组(① Delta 15)：Members 各自解析 → 去重扁平合并。</summary>
    Group,
```
`ApproverRule`(L21) 改为带 init 成员的 record：
```csharp
public record ApproverRule(ApproverStrategy Strategy, int? Levels, int? RoleId, Guid? SpecifiedUserId)
{
    /// <summary>FormField:取审批人的字段名;DataMap:取匹配值的字段名。</summary>
    public string? FieldName { get; init; }
    /// <summary>DataMap:命名映射(Wf_ApproverMap.MapKey)。</summary>
    public string? MapKey { get; init; }
    /// <summary>门控(②a):对表单 vars 求值,假则本规则不产审批人。</summary>
    public string? When { get; init; }
    /// <summary>候选过滤(②a):对每个候选人属性求值,留通过者。</summary>
    public string? Filter { get; init; }
    /// <summary>Group(①):成员规则(扁平,均为叶规则)。</summary>
    public IReadOnlyList<ApproverRule>? Members { get; init; }
}
```
`ApproverResolveContext`(L24) 加：
```csharp
    /// <summary>实例表单数据 JSON(②③① 求值/取字段用)。null=无表单上下文(如 Role→CC 解析)。</summary>
    public string? VarsJson { get; set; }
```

- [ ] **Step 4：跑绿**

Run: `cd /d/CP6-wfs-approver && dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~ApproverResolverAdvancedTests"`
Expected: PASS。

- [ ] **Step 5：引擎闸 + commit**

Run: `cd /d/CP6-wfs-approver && dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~Wf"` → PASS。
```bash
git -C D:/CP6-wfs-approver add CP6.Core CP6.Tests
git -C D:/CP6-wfs-approver commit -m "feat(wfs-approver): T2 枚举+3(FormField/DataMap/Group)+ApproverRule递归扩+Context.VarsJson

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 3：解析器 `FormField` 策略(③)

**Files:**
- Modify: `CP6.Core/Services/Wf/ApproverResolver.cs`
- Test: `CP6.Tests/ApproverResolverAdvancedTests.cs`

- [ ] **Step 1：写失败测试(单值/数组多值/缺字段/无效)**

追加到 `ApproverResolverAdvancedTests`：
```csharp
    [Fact]
    public async Task FormField_SingleGuid_ResolvesEnabledUser()
    {
        using var db = NewDb();
        var u = Guid.NewGuid();
        db.Sys_Users.Add(new Sys_User { Id = u, UserName = "u", Password = "x", Enable = true });
        await db.SaveChangesAsync();

        var res = await new ApproverResolver(db).ResolveAsync(
            new ApproverRule(ApproverStrategy.FormField, null, null, null) { FieldName = "approver" },
            new ApproverResolveContext { StarterUserId = Guid.NewGuid(), VarsJson = $"{{\"approver\":\"{u}\"}}" });
        Assert.Equal(u, res.ApproverIds.Single());
    }

    [Fact]
    public async Task FormField_ArrayOfGuids_ResolvesGroup_ExcludesDisabled()
    {
        using var db = NewDb();
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        db.Sys_Users.AddRange(
            new Sys_User { Id = a, UserName = "a", Password = "x", Enable = true },
            new Sys_User { Id = b, UserName = "b", Password = "x", Enable = false });
        await db.SaveChangesAsync();

        var res = await new ApproverResolver(db).ResolveAsync(
            new ApproverRule(ApproverStrategy.FormField, null, null, null) { FieldName = "approvers" },
            new ApproverResolveContext { VarsJson = $"{{\"approvers\":[\"{a}\",\"{b}\"]}}" });
        Assert.Equal(a, res.ApproverIds.Single());   // b 停用排除
    }

    [Fact]
    public async Task FormField_MissingOrInvalid_Unresolved()
    {
        using var db = NewDb();
        var res1 = await new ApproverResolver(db).ResolveAsync(
            new ApproverRule(ApproverStrategy.FormField, null, null, null) { FieldName = "approver" },
            new ApproverResolveContext { VarsJson = "{}" });
        Assert.False(res1.Resolved);

        var res2 = await new ApproverResolver(db).ResolveAsync(
            new ApproverRule(ApproverStrategy.FormField, null, null, null) { FieldName = "approver" },
            new ApproverResolveContext { VarsJson = "{\"approver\":\"not-a-guid\"}" });
        Assert.False(res2.Resolved);
    }
```

- [ ] **Step 2：跑红**

Run: `dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~ApproverResolverAdvancedTests"`(前缀 `cd /d/CP6-wfs-approver &&`)
Expected: FAIL — FormField 未实现(走 `_ => Unres("未知审批人策略")`)。

- [ ] **Step 3：实现 FormField 分支 + 读字段助手**

`ApproverResolver.cs`：`using System.Text.Json;` 顶部加。switch(L17-27) 加分支：
```csharp
        ApproverStrategy.FormField     => FormFieldAsync(rule, ctx),
```
新方法(class 内)：
```csharp
    /// <summary>③:从 VarsJson 读 FieldName 取 UserId(单值或数组);过滤存在且启用的用户。</summary>
    private async Task<ApproverResolveResult> FormFieldAsync(ApproverRule rule, ApproverResolveContext ctx)
    {
        if (string.IsNullOrWhiteSpace(rule.FieldName)) return ApproverResolveResult.Unres("未配置表单字段名");
        var ids = ReadGuidsFromField(ctx.VarsJson, rule.FieldName);
        if (ids.Count == 0) return ApproverResolveResult.Unres("表单字段未指定有效审批人");
        var valid = await _db.Sys_Users.Where(u => ids.Contains(u.Id) && u.Enable).Select(u => u.Id).ToListAsync();
        return valid.Count > 0 ? ApproverResolveResult.Ok(valid.ToArray()) : ApproverResolveResult.Unres("表单字段指定的用户无效或停用");
    }

    /// <summary>从 VarsJson 读字段(String 单值 / Array 多值),逐个 Guid.TryParse。
    /// 注:不走 ExpressionEvaluator.ParseVars(它把数组降为 null)。</summary>
    private static List<Guid> ReadGuidsFromField(string? varsJson, string fieldName)
    {
        var result = new List<Guid>();
        if (string.IsNullOrWhiteSpace(varsJson)) return result;
        try
        {
            using var doc = JsonDocument.Parse(varsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return result;
            if (!doc.RootElement.TryGetProperty(fieldName, out var el)) return result;
            void TryAdd(JsonElement v) { if (v.ValueKind == JsonValueKind.String && Guid.TryParse(v.GetString(), out var g)) result.Add(g); }
            if (el.ValueKind == JsonValueKind.Array) foreach (var item in el.EnumerateArray()) TryAdd(item);
            else TryAdd(el);
        }
        catch { /* 解析失败 → 空 */ }
        return result;
    }
```

- [ ] **Step 4：跑绿**

Run: `dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~ApproverResolverAdvancedTests"` → PASS。

- [ ] **Step 5：引擎闸 + commit**

`dotnet test … --filter "FullyQualifiedName~Wf"` → PASS。
```bash
git -C D:/CP6-wfs-approver add CP6.Core CP6.Tests
git -C D:/CP6-wfs-approver commit -m "feat(wfs-approver): T3 解析器 FormField 策略(VarsJson 取 UserId,数组绕 ParseVars)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 4：解析器 `DataMap` 策略(②b)

**Files:**
- Modify: `CP6.Core/Services/Wf/ApproverResolver.cs`
- Test: `CP6.Tests/ApproverResolverAdvancedTests.cs`

- [ ] **Step 1：写失败测试(命中用户/角色展开/未命中)**

追加：
```csharp
    [Fact]
    public async Task DataMap_MatchValue_ResolvesUserAndExpandsRole()
    {
        using var db = NewDb();
        var user = Guid.NewGuid(); var roleUser = Guid.NewGuid();
        db.Sys_Users.AddRange(
            new Sys_User { Id = user, UserName = "u", Password = "x", Enable = true },
            new Sys_User { Id = roleUser, UserName = "r", Password = "x", RoleId = 9, Enable = true });
        db.Wf_ApproverMaps.AddRange(
            new Wf_ApproverMap { Id = Guid.NewGuid(), MapKey = "cc", MatchValue = "A100", ApproverUserId = user, Enable = true },
            new Wf_ApproverMap { Id = Guid.NewGuid(), MapKey = "cc", MatchValue = "A100", ApproverRoleId = 9, Enable = true });
        await db.SaveChangesAsync();

        var res = await new ApproverResolver(db).ResolveAsync(
            new ApproverRule(ApproverStrategy.DataMap, null, null, null) { MapKey = "cc", FieldName = "costCenter" },
            new ApproverResolveContext { VarsJson = "{\"costCenter\":\"A100\"}" });
        Assert.Contains(user, res.ApproverIds);
        Assert.Contains(roleUser, res.ApproverIds);
    }

    [Fact]
    public async Task DataMap_NoMatch_Unresolved()
    {
        using var db = NewDb();
        var res = await new ApproverResolver(db).ResolveAsync(
            new ApproverRule(ApproverStrategy.DataMap, null, null, null) { MapKey = "cc", FieldName = "costCenter" },
            new ApproverResolveContext { VarsJson = "{\"costCenter\":\"ZZZ\"}" });
        Assert.False(res.Resolved);
    }
```

- [ ] **Step 2：跑红** — `--filter "FullyQualifiedName~ApproverResolverAdvancedTests"` → FAIL(DataMap 未实现)。

- [ ] **Step 3：实现 DataMap 分支**

switch 加 `ApproverStrategy.DataMap => DataMapAsync(rule, ctx),`。新方法：
```csharp
    /// <summary>②b:取 FieldName 标量值 → 查 Wf_ApproverMap(MapKey+MatchValue+Enable);收用户 + 展开角色。</summary>
    private async Task<ApproverResolveResult> DataMapAsync(ApproverRule rule, ApproverResolveContext ctx)
    {
        if (string.IsNullOrWhiteSpace(rule.MapKey) || string.IsNullOrWhiteSpace(rule.FieldName))
            return ApproverResolveResult.Unres("未配置映射键或匹配字段");
        var matchValue = ReadScalarString(ctx.VarsJson, rule.FieldName);
        if (matchValue is null) return ApproverResolveResult.Unres("表单匹配字段为空");

        var rows = await _db.Wf_ApproverMaps
            .Where(m => m.MapKey == rule.MapKey && m.MatchValue == matchValue && m.Enable).ToListAsync();
        if (rows.Count == 0) return ApproverResolveResult.Unres($"映射 {rule.MapKey}/{matchValue} 无审批人");

        var ids = new List<Guid>();
        ids.AddRange(rows.Where(r => r.ApproverUserId is Guid).Select(r => r.ApproverUserId!.Value));
        foreach (var rid in rows.Where(r => r.ApproverRoleId is int).Select(r => r.ApproverRoleId!.Value).Distinct())
        {
            var roleRes = await RoleAsync(new ApproverRule(ApproverStrategy.Role, null, rid, null));
            if (roleRes.Resolved) ids.AddRange(roleRes.ApproverIds);
        }
        var valid = await _db.Sys_Users.Where(u => ids.Contains(u.Id) && u.Enable).Select(u => u.Id).ToListAsync();
        return valid.Count > 0 ? ApproverResolveResult.Ok(valid.Distinct().ToArray()) : ApproverResolveResult.Unres("映射审批人无效或停用");
    }

    /// <summary>从 VarsJson 读字段标量值的字符串形式(数字/字符串/布尔);数组/对象/缺失→null。</summary>
    private static string? ReadScalarString(string? varsJson, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(varsJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(varsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (!doc.RootElement.TryGetProperty(fieldName, out var el)) return null;
            return el.ValueKind switch
            {
                JsonValueKind.String => el.GetString(),
                JsonValueKind.Number => el.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null,
            };
        }
        catch { return null; }
    }
```

- [ ] **Step 4：跑绿** → PASS。
- [ ] **Step 5：引擎闸 + commit**

`--filter "FullyQualifiedName~Wf"` → PASS。
```bash
git -C D:/CP6-wfs-approver add CP6.Core CP6.Tests
git -C D:/CP6-wfs-approver commit -m "feat(wfs-approver): T4 解析器 DataMap 策略(Wf_ApproverMap 查映射+角色展开)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 5：解析器 `Group` 策略(①，扁平合并)

**Files:**
- Modify: `CP6.Core/Services/Wf/ApproverResolver.cs`
- Test: `CP6.Tests/ApproverResolverAdvancedTests.cs`

- [ ] **Step 1：写失败测试(混源合并去重/部分成员缺位仍成立/全空)**

追加：
```csharp
    [Fact]
    public async Task Group_MergesMembers_Distinct_PartialMissingStillResolves()
    {
        using var db = NewDb();
        var mgr = Guid.NewGuid(); var low = Guid.NewGuid();
        db.Sys_Users.AddRange(
            new Sys_User { Id = mgr, UserName = "m", Password = "x", Enable = true },
            new Sys_User { Id = low, UserName = "l", Password = "x", ManagerId = mgr, Enable = true });
        await db.SaveChangesAsync();

        var rule = new ApproverRule(ApproverStrategy.Group, null, null, null)
        {
            Members = new[]
            {
                new ApproverRule(ApproverStrategy.DirectManager, 1, null, null),       // → mgr
                new ApproverRule(ApproverStrategy.Specified, null, null, mgr),         // → mgr(重复,去重)
                new ApproverRule(ApproverStrategy.DeptLeader, null, null, null),       // → 无部门,缺位(静默不贡献)
            }
        };
        var res = await new ApproverResolver(db).ResolveAsync(rule,
            new ApproverResolveContext { StarterUserId = low });
        Assert.Equal(mgr, res.ApproverIds.Single());   // 合并去重 = {mgr}
    }

    [Fact]
    public async Task Group_AllMembersMissing_Unresolved()
    {
        using var db = NewDb();
        var u = Guid.NewGuid();
        db.Sys_Users.Add(new Sys_User { Id = u, UserName = "u", Password = "x", Enable = true });
        await db.SaveChangesAsync();
        var rule = new ApproverRule(ApproverStrategy.Group, null, null, null)
        { Members = new[] { new ApproverRule(ApproverStrategy.DirectManager, 1, null, null) } };  // u 无主管
        var res = await new ApproverResolver(db).ResolveAsync(rule, new ApproverResolveContext { StarterUserId = u });
        Assert.False(res.Resolved);
    }
```

- [ ] **Step 2：跑红** → FAIL(Group 未实现)。

- [ ] **Step 3：实现 Group 分支(递归)**

switch 加 `ApproverStrategy.Group => GroupAsync(rule, ctx),`。新方法：
```csharp
    /// <summary>①:成员规则各自递归解析 → 去重扁平合并;成员缺位静默不贡献;全空 → Unres。</summary>
    private async Task<ApproverResolveResult> GroupAsync(ApproverRule rule, ApproverResolveContext ctx)
    {
        if (rule.Members is null || rule.Members.Count == 0) return ApproverResolveResult.Unres("JSON 组无成员");
        var ids = new List<Guid>();
        foreach (var m in rule.Members)
        {
            var r = await ResolveAsync(m, ctx);   // 成员自身 When/Filter 在此生效(T6 后)
            if (r.Resolved) ids.AddRange(r.ApproverIds);
        }
        return ids.Count > 0 ? ApproverResolveResult.Ok(ids.Distinct().ToArray()) : ApproverResolveResult.Unres("JSON 组无任何成员解析出审批人");
    }
```

- [ ] **Step 4：跑绿** → PASS。
- [ ] **Step 5：引擎闸 + commit**
```bash
git -C D:/CP6-wfs-approver add CP6.Core CP6.Tests
git -C D:/CP6-wfs-approver commit -m "feat(wfs-approver): T5 解析器 Group 策略(成员递归解析+去重扁平合并)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 6：`When` 门控 + `Filter` 候选过滤 + 变量命名空间(②a)

**Files:**
- Modify: `CP6.Core/Services/Wf/ApproverResolver.cs`
- Test: `CP6.Tests/ApproverResolverAdvancedTests.cs`

- [ ] **Step 1：写失败测试(When 真假/Filter 同部门/全排除)**

追加：
```csharp
    [Fact]
    public async Task When_GatesRule_FalseYieldsUnresolved()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid();
        var ruleTrue = new ApproverRule(ApproverStrategy.Starter, null, null, null) { When = "amount > 10" };
        var ruleFalse = new ApproverRule(ApproverStrategy.Starter, null, null, null) { When = "amount > 1000" };
        var ctx = new ApproverResolveContext { StarterUserId = starter, VarsJson = "{\"amount\":100}" };
        Assert.True((await new ApproverResolver(db).ResolveAsync(ruleTrue, ctx)).Resolved);
        Assert.False((await new ApproverResolver(db).ResolveAsync(ruleFalse, ctx)).Resolved);
    }

    [Fact]
    public async Task Filter_KeepsSameDeptCandidates()
    {
        using var db = NewDb();
        var dept = Guid.NewGuid();
        var starter = Guid.NewGuid();
        var same = Guid.NewGuid(); var other = Guid.NewGuid();
        db.Sys_Users.AddRange(
            new Sys_User { Id = starter, UserName = "s", Password = "x", DeptId = dept, RoleId = 7, Enable = true },
            new Sys_User { Id = same, UserName = "same", Password = "x", DeptId = dept, RoleId = 7, Enable = true },
            new Sys_User { Id = other, UserName = "other", Password = "x", DeptId = Guid.NewGuid(), RoleId = 7, Enable = true });
        await db.SaveChangesAsync();

        var rule = new ApproverRule(ApproverStrategy.Role, null, 7, null) { Filter = "user.deptId == starter.deptId" };
        var res = await new ApproverResolver(db).ResolveAsync(rule,
            new ApproverResolveContext { StarterUserId = starter, VarsJson = "{}" });
        Assert.Contains(same, res.ApproverIds);
        Assert.Contains(starter, res.ApproverIds);   // starter 同部门也留
        Assert.DoesNotContain(other, res.ApproverIds);
    }

    [Fact]
    public async Task Filter_AllExcluded_Unresolved()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var u = Guid.NewGuid();
        db.Sys_Users.AddRange(
            new Sys_User { Id = starter, UserName = "s", Password = "x", DeptId = Guid.NewGuid(), RoleId = 7, Enable = true },
            new Sys_User { Id = u, UserName = "u", Password = "x", DeptId = Guid.NewGuid(), RoleId = 7, Enable = true });
        await db.SaveChangesAsync();
        var rule = new ApproverRule(ApproverStrategy.Role, null, 7, null) { Filter = "user.deptId == starter.deptId" };
        var res = await new ApproverResolver(db).ResolveAsync(rule,
            new ApproverResolveContext { StarterUserId = starter, VarsJson = "{}" });
        // starter 自己也是 role 7,但 starter.deptId==starter.deptId 真 → starter 留;u 排除
        Assert.Equal(starter, res.ApproverIds.Single());
    }
```

- [ ] **Step 2：跑红** → FAIL(When/Filter 未实现)。

- [ ] **Step 3：重构 ResolveAsync 为「门控→分发→过滤」主流程**

把 `ResolveAsync` 的 switch 表达式改为方法体(保留所有分支调用)：
```csharp
    public async Task<ApproverResolveResult> ResolveAsync(ApproverRule rule, ApproverResolveContext ctx)
    {
        // ②a 门控:When 假 → 本规则不产审批人
        if (!string.IsNullOrWhiteSpace(rule.When) && !ExpressionEvaluator.Evaluate(rule.When, ctx.VarsJson))
            return ApproverResolveResult.Unres("条件不满足(When)");

        var baseRes = rule.Strategy switch
        {
            ApproverStrategy.DirectManager => await DirectManagerAsync(rule, ctx),
            ApproverStrategy.DeptLeader    => await DeptLeaderAsync(ctx),
            ApproverStrategy.Role          => await RoleAsync(rule),
            ApproverStrategy.Specified     => rule.SpecifiedUserId is Guid u
                                                ? ApproverResolveResult.Ok(u) : ApproverResolveResult.Unres("未指定审批人"),
            ApproverStrategy.Starter       => ApproverResolveResult.Ok(ctx.StarterUserId),
            ApproverStrategy.FormField     => await FormFieldAsync(rule, ctx),
            ApproverStrategy.DataMap       => await DataMapAsync(rule, ctx),
            ApproverStrategy.Group         => await GroupAsync(rule, ctx),
            _ => ApproverResolveResult.Unres("未知审批人策略"),
        };
        if (!baseRes.Resolved) return baseRes;

        // ②a 候选过滤:Filter 逐候选求值,留通过者
        if (!string.IsNullOrWhiteSpace(rule.Filter))
            return await ApplyFilterAsync(rule.Filter!, baseRes.ApproverIds, ctx);

        return baseRes;
    }
```
> 删除原 switch 表达式(L17-27)。`using` 顶部确保 `using System.Text.Json;`(T3 已加)。`ExpressionEvaluator` 同命名空间，无需 using。

新增过滤 + 命名空间助手：
```csharp
    /// <summary>②a 候选过滤:对每个候选载入 Sys_User 行,建 starter.* / user.* + 表单字段 变量字典求值。</summary>
    private async Task<ApproverResolveResult> ApplyFilterAsync(string filter, List<Guid> candidateIds, ApproverResolveContext ctx)
    {
        var users = await _db.Sys_Users.Where(u => candidateIds.Contains(u.Id)).ToListAsync();
        var formVars = ExpressionEvaluator.ParseVars(ctx.VarsJson);
        var starter = await _db.Sys_Users.FirstOrDefaultAsync(u => u.Id == ctx.StarterUserId);
        var kept = new List<Guid>();
        foreach (var u in users)
        {
            var vars = new Dictionary<string, object?>(formVars, StringComparer.Ordinal);
            AddUserNamespace(vars, "starter", starter);
            AddUserNamespace(vars, "user", u);
            if (ExpressionEvaluator.Evaluate(filter, vars)) kept.Add(u.Id);
        }
        return kept.Count > 0 ? ApproverResolveResult.Ok(kept.ToArray()) : ApproverResolveResult.Unres("无候选人满足过滤条件");
    }

    /// <summary>把用户属性写进变量字典(ns.id/deptId/managerId/roleId/userName/enable);GUID 串化、int→double。</summary>
    private static void AddUserNamespace(Dictionary<string, object?> vars, string ns, Sys_User? u)
    {
        if (u is null) return;
        vars[$"{ns}.id"]        = u.Id.ToString();
        vars[$"{ns}.deptId"]    = u.DeptId?.ToString() ?? "";
        vars[$"{ns}.managerId"] = u.ManagerId?.ToString() ?? "";
        vars[$"{ns}.roleId"]    = u.RoleId is int r ? (double)r : (object?)null;
        vars[$"{ns}.userName"]  = u.UserName;
        vars[$"{ns}.enable"]    = u.Enable;
    }
```
> `Sys_User` 已 using(`CP6.Entity.DomainModels.Sys`，文件顶部 L2 已有)。

- [ ] **Step 4：跑绿** → PASS(含 T3/T4/T5 旧测仍绿)。
- [ ] **Step 5：引擎闸 + commit**

`--filter "FullyQualifiedName~Wf"` → PASS。
```bash
git -C D:/CP6-wfs-approver add CP6.Core CP6.Tests
git -C D:/CP6-wfs-approver commit -m "feat(wfs-approver): T6 When 门控+Filter 候选过滤+starter./user. 变量命名空间

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 7：`IApproverMapService` CRUD + E-WF-015

**Files:**
- Create: `CP6.Core/Services/Wf/IApproverMapService.cs`, `CP6.Core/Services/Wf/ApproverMapService.cs`
- Test: `CP6.Tests/Wf/ApproverMapServiceTests.cs`(扩)

- [ ] **Step 1：写失败测试(CRUD + 重复 E-WF-015 + 双空 E-WF-015)**

追加到 `ApproverMapServiceTests`：
```csharp
    [Fact]
    public async Task Create_Then_List_ByKey()
    {
        using var db = NewDb();
        var svc = new ApproverMapService(db);
        await svc.CreateAsync("cc", "A100", Guid.NewGuid(), null);
        var rows = await svc.ListAsync("cc");
        Assert.Single(rows);
    }

    [Fact]
    public async Task Create_DuplicateSameTarget_Throws_EWF015()
    {
        using var db = NewDb();
        var svc = new ApproverMapService(db);
        var uid = Guid.NewGuid();
        await svc.CreateAsync("cc", "A100", uid, null);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CreateAsync("cc", "A100", uid, null));
        Assert.Contains("E-WF-015", ex.Message);
    }

    [Fact]
    public async Task Create_BothTargetsNull_Throws_EWF015()
    {
        using var db = NewDb();
        var svc = new ApproverMapService(db);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CreateAsync("cc", "A100", null, null));
        Assert.Contains("E-WF-015", ex.Message);
    }
```

- [ ] **Step 2：跑红** → FAIL(`ApproverMapService` 不存在)。

- [ ] **Step 3：建接口 + 实现**

`CP6.Core/Services/Wf/IApproverMapService.cs`：
```csharp
using CP6.Entity.DomainModels.Wf;

namespace CP6.Core.Services.Wf;

public interface IApproverMapService
{
    Task<IReadOnlyList<Wf_ApproverMap>> ListAsync(string? mapKey);
    Task<IReadOnlyList<string>> DistinctKeysAsync();
    Task<Wf_ApproverMap> CreateAsync(string mapKey, string matchValue, Guid? approverUserId, int? approverRoleId, int orderNo = 0);
    Task UpdateAsync(Guid id, string matchValue, Guid? approverUserId, int? approverRoleId, int orderNo, bool enable);
    Task DeleteAsync(Guid id);
}
```
`CP6.Core/Services/Wf/ApproverMapService.cs`：
```csharp
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

/// <summary>审批人映射维护(②b)。租户隔离走全局过滤器。校验非法 → InvalidOperationException("E-WF-015")(控制器 catch 转 BizException)。</summary>
public class ApproverMapService : IApproverMapService
{
    private readonly CP6Context _db;
    public ApproverMapService(CP6Context db) => _db = db;

    public async Task<IReadOnlyList<Wf_ApproverMap>> ListAsync(string? mapKey)
    {
        var q = _db.Wf_ApproverMaps.AsQueryable();
        if (!string.IsNullOrWhiteSpace(mapKey)) q = q.Where(m => m.MapKey == mapKey);
        return await q.OrderBy(m => m.MapKey).ThenBy(m => m.MatchValue).ThenBy(m => m.OrderNo).ToListAsync();
    }

    public async Task<IReadOnlyList<string>> DistinctKeysAsync()
        => await _db.Wf_ApproverMaps.Select(m => m.MapKey).Distinct().OrderBy(k => k).ToListAsync();

    public async Task<Wf_ApproverMap> CreateAsync(string mapKey, string matchValue, Guid? approverUserId, int? approverRoleId, int orderNo = 0)
    {
        Validate(mapKey, matchValue, approverUserId, approverRoleId);
        await AssertNoDuplicateAsync(mapKey, matchValue, approverUserId, approverRoleId, null);
        var row = new Wf_ApproverMap
        {
            Id = Guid.NewGuid(), MapKey = mapKey.Trim(), MatchValue = matchValue.Trim(),
            ApproverUserId = approverUserId, ApproverRoleId = approverRoleId, OrderNo = orderNo, Enable = true,
        };
        _db.Wf_ApproverMaps.Add(row);
        await _db.SaveChangesAsync();
        return row;
    }

    public async Task UpdateAsync(Guid id, string matchValue, Guid? approverUserId, int? approverRoleId, int orderNo, bool enable)
    {
        var row = await _db.Wf_ApproverMaps.FirstOrDefaultAsync(m => m.Id == id)
                  ?? throw new InvalidOperationException("E-WF-015");
        Validate(row.MapKey, matchValue, approverUserId, approverRoleId);
        await AssertNoDuplicateAsync(row.MapKey, matchValue, approverUserId, approverRoleId, id);
        row.MatchValue = matchValue.Trim(); row.ApproverUserId = approverUserId;
        row.ApproverRoleId = approverRoleId; row.OrderNo = orderNo; row.Enable = enable;
        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        var row = await _db.Wf_ApproverMaps.FirstOrDefaultAsync(m => m.Id == id);
        if (row is null) return;
        _db.Wf_ApproverMaps.Remove(row);
        await _db.SaveChangesAsync();
    }

    private static void Validate(string mapKey, string matchValue, Guid? uid, int? rid)
    {
        if (string.IsNullOrWhiteSpace(mapKey) || string.IsNullOrWhiteSpace(matchValue)) throw new InvalidOperationException("E-WF-015");
        if (uid is null && rid is null) throw new InvalidOperationException("E-WF-015");   // 双目标皆空
    }

    private async Task AssertNoDuplicateAsync(string mapKey, string matchValue, Guid? uid, int? rid, Guid? excludeId)
    {
        var exists = await _db.Wf_ApproverMaps.AnyAsync(m =>
            m.MapKey == mapKey && m.MatchValue == matchValue &&
            m.ApproverUserId == uid && m.ApproverRoleId == rid &&
            (excludeId == null || m.Id != excludeId));
        if (exists) throw new InvalidOperationException("E-WF-015");
    }
}
```

- [ ] **Step 4：跑绿** → PASS。
- [ ] **Step 5：引擎闸 + commit**
```bash
git -C D:/CP6-wfs-approver add CP6.Core CP6.Tests
git -C D:/CP6-wfs-approver commit -m "feat(wfs-approver): T7 IApproverMapService CRUD+E-WF-015(重复/双空)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

# P-B 接线 + 校验

## Task 8：设计期 `ApproverSpec` + 节点/档新字段 + `BuildRule`/planner 映射

**Files:**
- Modify: `CP6.Core/Services/Wf/FlowSchema.cs`, `CP6.Core/Services/Wf/FlowEngine.cs`(BuildRule L342), `CP6.Core/Services/Wf/ApprovalStagePlanner.cs`(fixed 档 L66-75)
- Test: `CP6.Tests/ApproverResolverAdvancedTests.cs`(BuildRule 映射测，需访问 internal → 已有 `InternalsVisibleTo` CP6.Tests？若无则用反射/或测 planner 输出)

> **核对**：`FlowEngine.BuildRule` 是 `internal static`。确认 `CP6.Core` 是否 `[InternalsVisibleTo("CP6.Tests")]`(字段审计 T3 曾加)。若可见 → 直接断言 BuildRule；否则改为断言 `ApprovalStagePlanner.BuildAsync`(public)对单档节点产出的 `RuntimeApprovalStage.Rule` 含新字段。本计划用 **planner 路径**(public，稳妥)。

- [ ] **Step 1：写失败测试(节点配 Group → planner 单档产出富 rule)**

追加到 `ApproverResolverAdvancedTests`：
```csharp
    [Fact]
    public async Task Planner_SingleStageNode_WithGroupSpec_BuildsRichRule()
    {
        using var db = NewDb();
        var node = new FlowNode
        {
            Id = "n1", Type = "approval", ApproverStrategy = "Group",
            ApproverMembers = new List<ApproverSpec>
            {
                new() { Strategy = "Starter" },
                new() { Strategy = "Specified", ApproverUserId = Guid.NewGuid() },
            },
        };
        var plan = await new ApprovalStagePlanner(new ApproverResolver(db))
            .BuildAsync(new Wf_FlowInstance { StarterId = Guid.NewGuid() }, new FlowSchema(), node);
        var rule = plan.Single().Rule;
        Assert.Equal(ApproverStrategy.Group, rule.Strategy);
        Assert.Equal(2, rule.Members!.Count);
    }

    [Fact]
    public async Task Planner_SingleStageNode_WithFieldAndWhen_BuildsRichRule()
    {
        using var db = NewDb();
        var node = new FlowNode
        {
            Id = "n1", Type = "approval", ApproverStrategy = "FormField",
            ApproverFieldName = "approver", ApproverWhen = "amount > 10", ApproverFilter = "user.enable == true",
        };
        var plan = await new ApprovalStagePlanner(new ApproverResolver(db))
            .BuildAsync(new Wf_FlowInstance { StarterId = Guid.NewGuid() }, new FlowSchema(), node);
        var rule = plan.Single().Rule;
        Assert.Equal("approver", rule.FieldName);
        Assert.Equal("amount > 10", rule.When);
        Assert.Equal("user.enable == true", rule.Filter);
    }
```
> 测试需 `using CP6.Entity.DomainModels.Wf;`(Wf_FlowInstance)。`FlowNode`/`FlowSchema`/`ApproverSpec` 在 `CP6.Core.Services.Wf`(已 using)。

- [ ] **Step 2：跑红** → FAIL(`ApproverMembers`/`ApproverFieldName`/`ApproverWhen`/`ApproverFilter`/`ApproverSpec` 不存在)。

- [ ] **Step 3：扩 FlowSchema**

`FlowSchema.cs`：`FlowNode` 类内(L61 `Stages` 后)加：
```csharp
    // ── 高级审批人策略(②③①)。缺省 null=不启用,走原扁平 4 字段路径(向后兼容) ──
    public string? ApproverFieldName { get; set; }   // FormField/DataMap:字段名
    public string? ApproverMapKey { get; set; }      // DataMap:映射键
    public string? ApproverWhen { get; set; }        // ②a 门控
    public string? ApproverFilter { get; set; }      // ②a 候选过滤
    public List<ApproverSpec>? ApproverMembers { get; set; }   // Group 成员
```
`ApprovalStage` 类内(L93 `MaxLevels` 后)加同 5 字段。
文件末尾(L94 后)加 `ApproverSpec`：
```csharp
/// <summary>审批人设计期叶规则(Group 成员用)。对齐 ApproverRule 叶子部分,无 Members(扁平合并)。</summary>
public class ApproverSpec
{
    public string? Strategy { get; set; }
    public int? ApproverLevels { get; set; }
    public int? ApproverRoleId { get; set; }
    public Guid? ApproverUserId { get; set; }
    public string? FieldName { get; set; }
    public string? MapKey { get; set; }
    public string? When { get; set; }
    public string? Filter { get; set; }
}
```

- [ ] **Step 4：BuildRule 映射新字段 + MapSpec**

`FlowEngine.cs` L342 `BuildRule`：
```csharp
    internal static ApproverRule? BuildRule(FlowNode n)
    {
        if (string.IsNullOrWhiteSpace(n.ApproverStrategy)) return null;
        if (!Enum.TryParse<ApproverStrategy>(n.ApproverStrategy, ignoreCase: true, out var strat)) return null;
        return new ApproverRule(strat, n.ApproverLevels, n.ApproverRoleId, n.ApproverUserId)
        {
            FieldName = n.ApproverFieldName,
            MapKey    = n.ApproverMapKey,
            When      = n.ApproverWhen,
            Filter    = n.ApproverFilter,
            Members   = n.ApproverMembers?.Select(MapSpec).ToList(),
        };
    }

    /// <summary>设计期叶 spec → 运行期叶 ApproverRule(无 Members)。</summary>
    internal static ApproverRule MapSpec(ApproverSpec s)
    {
        Enum.TryParse<ApproverStrategy>(s.Strategy, ignoreCase: true, out var strat);
        return new ApproverRule(strat, s.ApproverLevels, s.ApproverRoleId, s.ApproverUserId)
        {
            FieldName = s.FieldName, MapKey = s.MapKey, When = s.When, Filter = s.Filter,
        };
    }
```

- [ ] **Step 5：planner fixed 档 + 单档兼容映射新字段**

`ApprovalStagePlanner.cs`：单档兼容分支(L17-24)已调 `FlowEngine.BuildRule(node)` → 自动带新字段，**无需改**。fixed 档分支(L66-75)改 `Rule` 构造：
```csharp
            else // fixed
            {
                Enum.TryParse<ApproverStrategy>(st.ApproverStrategy, ignoreCase: true, out var strat);
                result.Add(new RuntimeApprovalStage
                {
                    StageIndex = idx++, Kind = ApprovalStageKinds.Fixed,
                    StageName = st.Name, StageCode = st.Code,
                    Rule = new ApproverRule(strat, st.ApproverLevels, st.ApproverRoleId, st.ApproverUserId)
                    {
                        FieldName = st.ApproverFieldName, MapKey = st.ApproverMapKey,
                        When = st.ApproverWhen, Filter = st.ApproverFilter,
                        Members = st.ApproverMembers?.Select(FlowEngine.MapSpec).ToList(),
                    },
                    Countersign = string.IsNullOrWhiteSpace(st.Countersign) ? CountersignModes.All : st.Countersign,
                });
            }
```

- [ ] **Step 6：跑绿** → PASS。
- [ ] **Step 7：引擎闸 + commit**

`--filter "FullyQualifiedName~Wf"` → PASS(单档 BuildRule 全 null 路径=字节等价)。
```bash
git -C D:/CP6-wfs-approver add CP6.Core CP6.Tests
git -C D:/CP6-wfs-approver commit -m "feat(wfs-approver): T8 设计期 ApproverSpec+节点/档新字段+BuildRule/MapSpec/planner 映射

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 9：四调用点接线 `inst.VarsJson` → context

**Files:**
- Modify: `CP6.Core/Services/Wf/NodeHandlers/ApprovalNodeHandler.cs:24`, `CP6.Core/Services/Wf/FlowEngine.Serial.cs:12`, `CP6.Core/Services/Wf/ApprovalStagePlanner.cs:40`, `CP6.Core/Services/Oa/ForecastService.cs:58,83`
- Test: `CP6.Tests/Oa/ForecastServiceTests.cs`(扩，验 forecast 带 vars 解析 FormField)

- [ ] **Step 1：写失败测试(forecast 用 vars 前解析 FormField 显具体人)**

参考 `CP6.Tests/Oa/ForecastServiceTests.cs` 既有夹具风格(它如何建 Wf_FlowDef + schema + 调 ForecastAsync)。新增：
```csharp
    [Fact]
    public async Task Forecast_FormFieldNode_ResolvesNamedApprover_FromVars()
    {
        using var db = NewDb();   // 复用该测试类既有 NewDb 助手
        var approver = Guid.NewGuid();
        db.Sys_Users.Add(new Sys_User { Id = approver, UserName = "boss", NickName = "Boss", Password = "x", Enable = true });
        var schema = "{\"start\":\"s\",\"nodes\":[" +
            "{\"id\":\"s\",\"type\":\"start\"}," +
            "{\"id\":\"a\",\"type\":\"approval\",\"approverStrategy\":\"FormField\",\"approverFieldName\":\"approver\"}," +
            "{\"id\":\"e\",\"type\":\"end\"}]," +
            "\"edges\":[{\"from\":\"s\",\"to\":\"a\"},{\"from\":\"a\",\"to\":\"e\"}]}";
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "ff", FlowName = "ff", FormKey = "f", SchemaJson = schema, Enable = true });
        await db.SaveChangesAsync();

        var svc = new ForecastService(db, new ApproverResolver(db), new ApprovalStagePlanner(new ApproverResolver(db)));
        var res = await svc.ForecastAsync("ff", $"{{\"approver\":\"{approver}\"}}", Guid.NewGuid());
        var step = res.Steps.First(s => s.NodeId == "a");
        Assert.True(step.Resolved);
        Assert.Contains("Boss", step.ApproverNames);   // 字段名按实际 DTO 属性名(核对 ForecastStep)
    }
```
> **落码前核对** `ForecastStep` DTO 实际属性名(`InboxModels.cs`)：`ApproverNames`/`Names`/`Resolved` 的真名，按真名改断言。schema JSON 的字段名(`approverStrategy`/`approverFieldName`)须与 `FlowNode` 的 `JsonSerializerDefaults.Web` camelCase 反序列化一致(ForecastService L11 `PropertyNameCaseInsensitive=true`，camelCase OK)。

- [ ] **Step 2：跑红** → FAIL(forecast 未把 vars 传进解析 → `Resolved=false` 占位)。

- [ ] **Step 3：接线四处**

`ApprovalNodeHandler.cs:24`：
```csharp
            var res = await eng.Approver.ResolveAsync(rule, new ApproverResolveContext { StarterUserId = inst.StarterId, VarsJson = inst.VarsJson });
```
`FlowEngine.Serial.cs:12`：
```csharp
        var res = await _approver.ResolveAsync(stage.Rule, new ApproverResolveContext { StarterUserId = inst.StarterId, VarsJson = inst.VarsJson });
```
`ApprovalStagePlanner.cs:38-40`(managerChain probe)：
```csharp
                    var probe = await _approver.ResolveAsync(
                        new ApproverRule(ApproverStrategy.DirectManager, j, null, null),
                        new ApproverResolveContext { StarterUserId = inst.StarterId, VarsJson = inst.VarsJson });
```
`ForecastService.cs:58`：
```csharp
                    var plan = await _planner.BuildAsync(new Wf_FlowInstance { StarterId = starterId, VarsJson = varsJson }, schema, node);
```
`ForecastService.cs:79-89`(`ResolveRuleNamesAsync` 加 varsJson 参 + 调用点传)：
```csharp
    private async Task<(IReadOnlyList<string> Names, bool Resolved)> ResolveRuleNamesAsync(ApproverRule rule, Guid starterId, string varsJson)
    {
        try
        {
            var res = await _approver.ResolveAsync(rule, new ApproverResolveContext { StarterUserId = starterId, VarsJson = varsJson });
            if (!res.Resolved) return (Array.Empty<string>(), false);
            var names = await OaUserNames.ResolveAsync(_db, res.ApproverIds);
            return (res.ApproverIds.Select(id => names.GetValueOrDefault(id, id.ToString())).ToList(), true);
        }
        catch { return (Array.Empty<string>(), false); }
    }
```
调用点(L60-61 附近)改为 `await ResolveRuleNamesAsync(rs.Rule, starterId, varsJson)`。

- [ ] **Step 4：跑绿** → PASS。
- [ ] **Step 5：引擎闸 + commit**

`--filter "FullyQualifiedName~Wf"` → PASS。
```bash
git -C D:/CP6-wfs-approver add CP6.Core CP6.Tests
git -C D:/CP6-wfs-approver commit -m "feat(wfs-approver): T9 四调用点接线 inst.VarsJson→ApproverResolveContext(handler/serial/planner/forecast)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 10：`FlowSchemaValidator` 新策略 + E-WF-014

**Files:**
- Modify: `CP6.Core/Services/Wf/FlowSchemaValidator.cs`
- Test: `CP6.Tests/Oa/FlowSchemaValidatorTests.cs`(扩)

> **落码前读** `FlowSchemaValidator.cs` 全文 + 既有 `FlowSchemaValidatorTests.cs` 风格(它如何造 FlowSchema 调 Validate、断言错误码列表)。

- [ ] **Step 1：写失败测试(FormField 缺字段/DataMap 缺键/Group 空 → E-WF-014;合法 → 无错)**

追加到 `FlowSchemaValidatorTests`(按既有 Validate API 签名)：
```csharp
    [Fact]
    public void FormFieldNode_MissingFieldName_EWF014()
    {
        var schema = new FlowSchema {
            Start = "s",
            Nodes = { new() { Id = "s", Type = "start" },
                      new() { Id = "a", Type = "approval", ApproverStrategy = "FormField" },   // 缺 ApproverFieldName
                      new() { Id = "e", Type = "end" } },
            Edges = { new() { From = "s", To = "a" }, new() { From = "a", To = "e" } } };
        Assert.Contains("E-WF-014", FlowSchemaValidator.Validate(schema));
    }

    [Fact]
    public void GroupNode_EmptyMembers_EWF014()
    {
        var schema = new FlowSchema {
            Start = "s",
            Nodes = { new() { Id = "s", Type = "start" },
                      new() { Id = "a", Type = "approval", ApproverStrategy = "Group" },        // 缺 Members
                      new() { Id = "e", Type = "end" } },
            Edges = { new() { From = "s", To = "a" }, new() { From = "a", To = "e" } } };
        Assert.Contains("E-WF-014", FlowSchemaValidator.Validate(schema));
    }

    [Fact]
    public void DataMapNode_Valid_NoError()
    {
        var schema = new FlowSchema {
            Start = "s",
            Nodes = { new() { Id = "s", Type = "start" },
                      new() { Id = "a", Type = "approval", ApproverStrategy = "DataMap", ApproverMapKey = "cc", ApproverFieldName = "costCenter" },
                      new() { Id = "e", Type = "end" } },
            Edges = { new() { From = "s", To = "a" }, new() { From = "a", To = "e" } } };
        Assert.DoesNotContain("E-WF-014", FlowSchemaValidator.Validate(schema));
    }
```
> 核对 `Validate` 真实签名(可能返 `List<string>`/`IEnumerable<string>`)与 `FlowNode` 集合初始化语法，按真实调整。

- [ ] **Step 2：跑红** → FAIL(新策略不在 `KnownStrategies` → 误报 E-WF-010 而非 E-WF-014，或合法 DataMap 误报)。

- [ ] **Step 3：扩 KnownStrategies + 加 E-WF-014 配置完整性校验**

`FlowSchemaValidator.cs`：`KnownStrategies`(L6) 加 `"FormField","DataMap","Group"`。approval 节点校验段(L27 附近)：策略合法后，按策略补配置完整性校验：
```csharp
            // 新策略配置完整性(E-WF-014)
            if (n.ApproverStrategy == "FormField" && string.IsNullOrWhiteSpace(n.ApproverFieldName)) { errs.Add("E-WF-014"); }
            if (n.ApproverStrategy == "DataMap" && (string.IsNullOrWhiteSpace(n.ApproverMapKey) || string.IsNullOrWhiteSpace(n.ApproverFieldName))) { errs.Add("E-WF-014"); }
            if (n.ApproverStrategy == "Group" && (n.ApproverMembers is null || n.ApproverMembers.Count == 0)) { errs.Add("E-WF-014"); }
```
> 串簽档(L38 附近)同理：fixed 档若 `ApproverStrategy` ∈ 新策略，校验对应 stage 字段(`st.ApproverFieldName`/`st.ApproverMapKey`/`st.ApproverMembers`)，违 → `E-WF-014`(沿用既有档校验 break 风格，避免改动 E-WF-011 语义)。保持既有控制流，仅**追加** E-WF-014 分支。

- [ ] **Step 4：跑绿** → PASS。
- [ ] **Step 5：引擎闸 + 全量 + commit**

`--filter "FullyQualifiedName~Wf"` → PASS；波次边界跑全量 `dotnet test CP6.Tests/CP6.Tests.csproj` → 基线绿 + 本波新测。
```bash
git -C D:/CP6-wfs-approver add CP6.Core CP6.Tests
git -C D:/CP6-wfs-approver commit -m "feat(wfs-approver): T10 FlowSchemaValidator 新策略+E-WF-014 配置完整性校验

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

# P-C 设计器

## Task 11：`designerModel.ts` 新字段 + round-trip + validateClient(vitest)

**Files:**
- Modify: `cp6.web/src/views/oa/designer/designerModel.ts`, `cp6.web/src/views/oa/designer/designerModel.test.ts`

> 前端首用须 `cd /d/CP6-wfs-approver/cp6.web && npm ci`(若 node_modules 缺)。测试 `npm run test`(vitest)，类型 `npm run check`(vue-tsc)。

- [ ] **Step 1：写失败 vitest(新字段 round-trip + Group 校验)**

`designerModel.test.ts` 追加：
```ts
import { describe, it, expect } from 'vitest'
import { schemaToGraph, graphToSchema, validateClient, type FlowSchemaDto } from './designerModel'

describe('approver advanced strategies', () => {
  it('round-trips formField/dataMap/group fields', () => {
    const schema: FlowSchemaDto = {
      start: 's',
      nodes: [
        { id: 's', type: 'start' },
        { id: 'a', type: 'approval', approverStrategy: 'Group',
          approverWhen: 'amount > 10', approverFilter: 'user.enable == true',
          approverMembers: [{ strategy: 'Starter' }, { strategy: 'Specified', approverUserId: 'u1' }] },
        { id: 'e', type: 'end' },
      ],
      edges: [{ from: 's', to: 'a' }, { from: 'a', to: 'e' }],
    }
    const { nodes, edges } = schemaToGraph(schema)
    const back = graphToSchema(nodes, edges)
    const a = back.nodes.find(n => n.id === 'a')!
    expect(a.approverMembers?.length).toBe(2)
    expect(a.approverWhen).toBe('amount > 10')
  })

  it('flags group node with empty members', () => {
    const schema: FlowSchemaDto = {
      start: 's',
      nodes: [{ id: 's', type: 'start' }, { id: 'a', type: 'approval', approverStrategy: 'Group' }, { id: 'e', type: 'end' }],
      edges: [{ from: 's', to: 'a' }, { from: 'a', to: 'e' }],
    }
    expect(validateClient(schema)).toContain('oa.designer.errApproverConfig')
  })
})
```

- [ ] **Step 2：跑红** — `cd /d/CP6-wfs-approver/cp6.web && npm run test -- designerModel` → FAIL(字段/校验缺)。

- [ ] **Step 3：扩 designerModel.ts**

`ApprovalStageDto`(L3) + `SchemaNode`(L15) 各加：
```ts
  approverFieldName?: string
  approverMapKey?: string
  approverWhen?: string
  approverFilter?: string
  approverMembers?: ApproverSpecDto[]
```
新增类型(文件顶部 import 后)：
```ts
export interface ApproverSpecDto {
  strategy?: string
  approverLevels?: number
  approverRoleId?: number
  approverUserId?: string
  fieldName?: string
  mapKey?: string
  when?: string
  filter?: string
}
```
> `schemaToGraph`/`graphToSchema` 用 `{ ...n }` 浅拷贝 → 新标量字段自动 round-trip；`approverMembers` 数组随浅拷贝传递(设计器编辑会整体替换数组，无需深拷贝)。`validateClient`(L74) 在 approval 节点校验段后加：
```ts
  for (const n of nodes) {
    if (n.type !== 'approval') continue
    if (n.approverStrategy === 'FormField' && !n.approverFieldName) errs.push('oa.designer.errApproverConfig')
    if (n.approverStrategy === 'DataMap' && (!n.approverMapKey || !n.approverFieldName)) errs.push('oa.designer.errApproverConfig')
    if (n.approverStrategy === 'Group' && !(n.approverMembers?.length)) errs.push('oa.designer.errApproverConfig')
  }
```

- [ ] **Step 4：跑绿 + 类型** — `npm run test -- designerModel` PASS；`npm run check` 0 错。
- [ ] **Step 5：commit**
```bash
git -C D:/CP6-wfs-approver add cp6.web/src/views/oa/designer/designerModel.ts cp6.web/src/views/oa/designer/designerModel.test.ts
git -C D:/CP6-wfs-approver commit -m "feat(wfs-approver): T11 designerModel 新策略字段+round-trip+validateClient(errApproverConfig)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 12：`NodePropertyPanel.vue` 新策略编辑(单档 + 档)

**Files:**
- Modify: `cp6.web/src/views/oa/designer/NodePropertyPanel.vue`

> 该文件单档审批段 L174-252、档段 L318-392。本 Task **只加**新策略下拉项 + 条件区，**不动**既有 5 策略控件。

- [ ] **Step 1：策略下拉加 3 项(单档 L176-182 + 档 L320-326)**

两处 `<el-select v-model="…approverStrategy">` 末尾(Starter option 后)加：
```vue
                <el-option value="FormField" :label="t('oa.designer.strategy.formField')" />
                <el-option value="DataMap"   :label="t('oa.designer.strategy.dataMap')" />
                <el-option value="Group"     :label="t('oa.designer.strategy.group')" />
```
> 档段 Group/FormField/DataMap：fixed 档支持 FormField/DataMap(单值/映射)；Group 主要用于单档节点。档段加 FormField/DataMap option，Group 仅单档段加(档内 Group 罕见，YAGNI)。

- [ ] **Step 2：单档新策略条件控件(L243 Countersign 前插)**

```vue
            <!-- FormField → 选 user 型表单字段(降级文本) -->
            <el-form-item v-if="local.approverStrategy === 'FormField'" :label="t('oa.designer.approverField')">
              <el-input v-model="local.approverFieldName" :placeholder="t('oa.designer.approverFieldHint')" clearable />
            </el-form-item>
            <!-- DataMap → 映射键 + 匹配字段 -->
            <template v-if="local.approverStrategy === 'DataMap'">
              <el-form-item :label="t('oa.designer.approverMapKey')">
                <el-input v-model="local.approverMapKey" clearable />
              </el-form-item>
              <el-form-item :label="t('oa.designer.approverField')">
                <el-input v-model="local.approverFieldName" clearable />
              </el-form-item>
            </template>
            <!-- Group → 成员行增删 -->
            <template v-if="local.approverStrategy === 'Group'">
              <div v-for="(m, i) in (local.approverMembers ?? [])" :key="i" class="stage-card">
                <div class="stage-card-header">
                  <span class="stage-index-label">{{ t('oa.designer.member') }} {{ i + 1 }}</span>
                  <el-button link type="danger" size="small" @click="removeMember(i)">{{ t('oa.designer.stage.remove') }}</el-button>
                </div>
                <el-select v-model="m.strategy" style="width:100%" clearable>
                  <el-option value="DirectManager" :label="t('oa.designer.strategy.directManager')" />
                  <el-option value="DeptLeader" :label="t('oa.designer.strategy.deptLeader')" />
                  <el-option value="Role" :label="t('oa.designer.strategy.role')" />
                  <el-option value="Specified" :label="t('oa.designer.strategy.specified')" />
                  <el-option value="Starter" :label="t('oa.designer.strategy.starter')" />
                  <el-option value="FormField" :label="t('oa.designer.strategy.formField')" />
                  <el-option value="DataMap" :label="t('oa.designer.strategy.dataMap')" />
                </el-select>
                <el-input v-if="m.strategy === 'Specified'" v-model="m.approverUserId" :placeholder="t('oa.designer.approverUser')" style="margin-top:4px" clearable />
                <el-input-number v-if="m.strategy === 'DirectManager'" v-model="m.approverLevels" :min="1" :max="10" style="width:100%;margin-top:4px" />
                <el-input v-if="m.strategy === 'Role'" v-model.number="m.approverRoleId" :placeholder="t('oa.designer.approverRole')" style="margin-top:4px" clearable />
                <template v-if="m.strategy === 'FormField'"><el-input v-model="m.fieldName" :placeholder="t('oa.designer.approverField')" style="margin-top:4px" clearable /></template>
                <template v-if="m.strategy === 'DataMap'">
                  <el-input v-model="m.mapKey" :placeholder="t('oa.designer.approverMapKey')" style="margin-top:4px" clearable />
                  <el-input v-model="m.fieldName" :placeholder="t('oa.designer.approverField')" style="margin-top:4px" clearable />
                </template>
              </div>
              <el-button style="width:100%;margin-top:4px" @click="addMember">{{ t('oa.designer.addMember') }}</el-button>
            </template>
```

- [ ] **Step 3：When/Filter 输入(進階段 L432 collapse-item 内加)**

```vue
          <el-form-item v-if="isApproval" :label="t('oa.designer.approverWhen')">
            <el-input v-model="local.approverWhen" type="textarea" :rows="2" :placeholder="t('oa.designer.approverWhenHint')" />
          </el-form-item>
          <el-form-item v-if="isApproval" :label="t('oa.designer.approverFilter')">
            <el-input v-model="local.approverFilter" type="textarea" :rows="2" :placeholder="t('oa.designer.approverFilterHint')" />
          </el-form-item>
```

- [ ] **Step 4：成员增删方法(script 内 addStage 附近)**

```ts
function addMember() {
  if (!local.value.approverMembers) local.value.approverMembers = []
  local.value.approverMembers.push({ strategy: 'Starter' })
}
function removeMember(i: number) {
  local.value.approverMembers?.splice(i, 1)
  if (!local.value.approverMembers?.length) local.value.approverMembers = undefined
}
```
> `cloneNode`(L13) 加 `approverMembers: n.approverMembers ? n.approverMembers.map(m => ({ ...m })) : undefined`，避免共享引用。`SchemaNode` 已含新字段(T11)。

- [ ] **Step 5：类型 + 构建** — `cd /d/CP6-wfs-approver/cp6.web && npm run check` 0 错；`npm run build` 绿。
- [ ] **Step 6：commit**
```bash
git -C D:/CP6-wfs-approver add cp6.web/src/views/oa/designer/NodePropertyPanel.vue
git -C D:/CP6-wfs-approver commit -m "feat(wfs-approver): T12 设计器面板 FormField/DataMap/Group 编辑+When/Filter 输入

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

# P-D 维护页 + 表单控件

## Task 13：`ApproverMapController` + DI

**Files:**
- Create: `CP6.WebApi/Controllers/Oa/ApproverMapController.cs`
- Modify: `CP6.WebApi/Program.cs`(DI，L104 串簽 planner 注册后)

- [ ] **Step 1：建控制器(照 InboxController 模式)**

`CP6.WebApi/Controllers/Oa/ApproverMapController.cs`：
```csharp
using CP6.Core.Services.Wf;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Oa;

/// <summary>审批人映射维护(②b Menu 数据驱动)。/api/oa/approver-map。</summary>
[ApiController]
[Route("api/oa/approver-map")]
[Authorize]
public class ApproverMapController : LocalizedControllerBase
{
    private readonly IApproverMapService _svc;
    public ApproverMapController(IApproverMapService svc) => _svc = svc;

    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Err(InvalidOperationException e) => BadRequest(new { code = 400, message = e.Message });

    public record CreateReq(string MapKey, string MatchValue, Guid? ApproverUserId, int? ApproverRoleId, int OrderNo);
    public record UpdateReq(string MatchValue, Guid? ApproverUserId, int? ApproverRoleId, int OrderNo, bool Enable);

    [HttpGet("list")]
    public async Task<IActionResult> List([FromQuery] string? mapKey) => Ok2(await _svc.ListAsync(mapKey));

    [HttpGet("keys")]
    public async Task<IActionResult> Keys() => Ok2(await _svc.DistinctKeysAsync());

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateReq r)
    {
        try { return Ok2(await _svc.CreateAsync(r.MapKey, r.MatchValue, r.ApproverUserId, r.ApproverRoleId, r.OrderNo)); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateReq r)
    {
        try { await _svc.UpdateAsync(id, r.MatchValue, r.ApproverUserId, r.ApproverRoleId, r.OrderNo, r.Enable); return Ok2(); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id) { await _svc.DeleteAsync(id); return Ok2(); }
}
```

- [ ] **Step 2：DI 注册**

`Program.cs` L104(`IApprovalStagePlanner` 注册后)加：
```csharp
builder.Services.AddScoped<CP6.Core.Services.Wf.IApproverMapService, CP6.Core.Services.Wf.ApproverMapService>(); // ②b 审批人映射维护
```

- [ ] **Step 3：编译 + 引擎闸 + commit**

Run: `cd /d/CP6-wfs-approver && dotnet build CP6.WebApi/CP6.WebApi.csproj` → 绿；`dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~Wf"` → PASS。
```bash
git -C D:/CP6-wfs-approver add CP6.WebApi
git -C D:/CP6-wfs-approver commit -m "feat(wfs-approver): T13 ApproverMapController(api/oa/approver-map)+DI

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 14：前端 API + 类型 + `ApproverMapView.vue` 维护页

**Files:**
- Create: `cp6.web/src/api/oa/approverMap.ts`, `cp6.web/src/views/oa/admin/ApproverMapView.vue`

> 参考既有 `cp6.web/src/api/oa/*.ts`(如 inbox.ts)的 `http` import 与封装风格；参考一个既有 OA 表格视图(如 `FormCatalog`/转交对话框)的 element-plus 表格 + 远程用户搜风格。

- [ ] **Step 1：API + 类型**

`cp6.web/src/api/oa/approverMap.ts`：
```ts
import http from '../http'

export interface ApproverMap {
  id: string; mapKey: string; matchValue: string
  approverUserId?: string | null; approverRoleId?: number | null
  orderNo: number; enable: boolean
}

export const approverMapApi = {
  list: (mapKey?: string) => http.get('/oa/approver-map/list', { params: { mapKey } }),
  keys: () => http.get('/oa/approver-map/keys'),
  create: (body: Partial<ApproverMap>) => http.post('/oa/approver-map', body),
  update: (id: string, body: Partial<ApproverMap>) => http.put(`/oa/approver-map/${id}`, body),
  remove: (id: string) => http.delete(`/oa/approver-map/${id}`),
}
```
> 核对 `http` 路径前缀(`@/api/http` 还是 `../http`)与既有 OA api 一致；端点是否含 `/api` 前缀由 http baseURL 决定(参 inbox.ts)。

- [ ] **Step 2：维护页(MapKey 选择/新建 + 行表格 CRUD + 远程用户/角色)**

`cp6.web/src/views/oa/admin/ApproverMapView.vue`(骨架，按既有视图补全样式)：
```vue
<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import { approverMapApi, type ApproverMap } from '@/api/oa/approverMap'
import { userApi } from '@/api/sys/user'
import { roleApi } from '@/api/sys/role'

const { t } = useI18n()
const keys = ref<string[]>([])
const curKey = ref<string>('')
const rows = ref<ApproverMap[]>([])
const userOpts = ref<{ label: string; value: string }[]>([])
const roleOpts = ref<{ label: string; value: number }[]>([])

async function loadKeys() { keys.value = (await approverMapApi.keys() as any).data ?? [] }
async function loadRows() { rows.value = (await approverMapApi.list(curKey.value || undefined) as any).data ?? [] }
async function searchUsers(kw: string) {
  if (!kw) { userOpts.value = []; return }
  const res = await userApi.getList({ page: 1, pageSize: 20, keyword: kw }) as any
  userOpts.value = (res.rows ?? []).map((u: any) => ({ label: u.nickName || u.userName, value: String(u.id) }))
}
async function loadRoles() {
  const res = await roleApi.getAll() as any
  const list: any[] = Array.isArray(res) ? res : (res.rows ?? [])
  roleOpts.value = list.map((r: any) => ({ label: r.roleName ?? r.name, value: Number(r.roleId ?? r.id) }))
}
function addRow() { rows.value.push({ id: '', mapKey: curKey.value, matchValue: '', approverUserId: null, approverRoleId: null, orderNo: 0, enable: true }) }
async function save(r: ApproverMap) {
  try {
    if (r.id) await approverMapApi.update(r.id, r)
    else await approverMapApi.create(r)
    ElMessage.success(t('common.saveSuccess'))
    await loadKeys(); await loadRows()
  } catch { /* http 拦截器已 toast E-WF-015 译文 */ }
}
async function del(r: ApproverMap) { if (r.id) { await approverMapApi.remove(r.id); await loadRows() } else { rows.value = rows.value.filter(x => x !== r) } }

onMounted(async () => { await loadKeys(); await loadRoles(); await loadRows() })
</script>

<template>
  <div style="padding:12px">
    <div style="margin-bottom:8px;display:flex;gap:8px;align-items:center">
      <el-select v-model="curKey" filterable allow-create clearable :placeholder="t('oa.approverMap.key')" style="width:240px" @change="loadRows">
        <el-option v-for="k in keys" :key="k" :label="k" :value="k" />
      </el-select>
      <el-button type="primary" @click="addRow">{{ t('oa.approverMap.addRow') }}</el-button>
    </div>
    <el-table :data="rows" border size="small">
      <el-table-column :label="t('oa.approverMap.matchValue')">
        <template #default="{ row }"><el-input v-model="row.matchValue" /></template>
      </el-table-column>
      <el-table-column :label="t('oa.approverMap.approverUser')">
        <template #default="{ row }">
          <el-select v-model="row.approverUserId" filterable remote :remote-method="searchUsers" clearable style="width:100%">
            <el-option v-for="u in userOpts" :key="u.value" :label="u.label" :value="u.value" />
          </el-select>
        </template>
      </el-table-column>
      <el-table-column :label="t('oa.approverMap.approverRole')">
        <template #default="{ row }">
          <el-select v-model="row.approverRoleId" clearable style="width:100%">
            <el-option v-for="r in roleOpts" :key="r.value" :label="r.label" :value="r.value" />
          </el-select>
        </template>
      </el-table-column>
      <el-table-column :label="t('oa.approverMap.enable')" width="80">
        <template #default="{ row }"><el-switch v-model="row.enable" /></template>
      </el-table-column>
      <el-table-column :label="t('common.operation')" width="140">
        <template #default="{ row }">
          <el-button link type="primary" size="small" @click="save(row)">{{ t('common.save') }}</el-button>
          <el-button link type="danger" size="small" @click="del(row)">{{ t('common.delete') }}</el-button>
        </template>
      </el-table-column>
    </el-table>
  </div>
</template>
```
> 核对 `userApi`/`roleApi` 真实导出名与既有 NodePropertyPanel 用法一致；`common.*` 词条若缺，T17 补。

- [ ] **Step 3：类型 + 构建** — `npm run check` 0 错；`npm run build` 绿。
- [ ] **Step 4：commit**
```bash
git -C D:/CP6-wfs-approver add cp6.web/src/api/oa/approverMap.ts cp6.web/src/views/oa/admin/ApproverMapView.vue
git -C D:/CP6-wfs-approver commit -m "feat(wfs-approver): T14 approverMap API+类型+ApproverMapView 维护页

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 15：路由 + 菜单 seed(维护页)

**Files:**
- Modify: `cp6.web/src/router/index.ts`(或 OA 路由表), `CP6.WebApi/Program.cs`(菜单 seed，照 C′ 设计器菜单 738 模式)

> **落码前**：grep `Program.cs` 现有 OA 菜单 seed(MenuId 733~738 + parent 740)，取**下一空位 MenuId**(739 若空)；grep 路由 `oa/designer` 注册位置照搬。

- [ ] **Step 1：路由注册**

OA 路由表加(照 `/oa/designer` 风格)：
```ts
{ path: '/oa/approver-map', name: 'oa-approver-map', component: () => import('@/views/oa/admin/ApproverMapView.vue') },
```

- [ ] **Step 2：菜单 seed(Program.cs，幂等守卫块外，授 RoleId=1)**

照 C′ 设计器菜单 738 那段，加 MenuId=739(确认空位) under ParentId=740：
```csharp
// OA 审批人映射维护(②b)
if (!db.Sys_Menus.Any(m => m.Id == 739))
    db.Sys_Menus.Add(new Sys_Menu { Id = 739, ParentId = 740, MenuName = "approverMap", RoutePath = "/oa/approver-map", /* 照 738 补齐其余字段 */ });
// RoleMenu 授 RoleId=1(照既有幂等模式)
```
> 严格照搬既有 738 的 `Sys_Menu` 全字段(Icon/OrderNo/MenuType 等)与 RoleMenu 幂等写法，勿臆造字段。

- [ ] **Step 3：构建 + commit**

`npm run build` 绿；`dotnet build CP6.WebApi/CP6.WebApi.csproj` 绿。
```bash
git -C D:/CP6-wfs-approver add cp6.web/src/router CP6.WebApi/Program.cs
git -C D:/CP6-wfs-approver commit -m "feat(wfs-approver): T15 维护页路由 /oa/approver-map+菜单 739

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 16：`DynamicForm` `user` 字段升级真选择器

**Files:**
- Modify: `cp6.web/src/views/wf/DynamicForm.vue`

- [ ] **Step 1：`user` 字段分支(L63 v-else 前插)**

```vue
      <el-select
        v-else-if="f.type === 'user'"
        v-model="model[f.name]"
        :multiple="!!f.multiple"
        filterable remote
        :remote-method="(kw: string) => searchUsers(f.name, kw)"
        :disabled="readonly(f)"
        :placeholder="f.placeholder"
        clearable
        style="width: 100%"
      >
        <el-option v-for="o in (userOpts[f.name] ?? [])" :key="o.value" :label="o.label" :value="o.value" />
      </el-select>
```

- [ ] **Step 2：远程搜 + `isText` 调整**

script(setup) 加：
```ts
import { userApi } from '@/api/sys/user'
const userOpts = ref<Record<string, { label: string; value: string }[]>>({})
async function searchUsers(field: string, kw: string) {
  if (!kw) { userOpts.value[field] = []; return }
  const res = await userApi.getList({ page: 1, pageSize: 20, keyword: kw }) as any
  userOpts.value[field] = (res.rows ?? []).map((u: any) => ({ label: u.nickName || u.userName, value: String(u.id) }))
}
```
`isText`(L109-111) 移除 `'user'`：`return ['input', 'dept'].includes(f.type)`(`user` 不再纯文本；`dept` 维持现状)。
> readonly 模式：`el-select :disabled` 已处理；若既有 readonly mask 走 `isText` 文本展示，须确保 `user` 只读时仍能显示昵称(选项已加载或回显)。若回显复杂，readonly 时降级显示原值文本即可(保持既有 mask 行为，加 `user` 到只读文本分支)。核对 `buildFieldMask`/readonly 渲染后决定。

- [ ] **Step 3：类型 + 构建 + 既有 vitest** — `npm run check` 0；`npm run test`(DynamicForm 相关若有)绿；`npm run build` 绿。
- [ ] **Step 4：commit**
```bash
git -C D:/CP6-wfs-approver add cp6.web/src/views/wf/DynamicForm.vue
git -C D:/CP6-wfs-approver commit -m "feat(wfs-approver): T16 DynamicForm user 字段升级远程选择器(存 GUID,多选→会签组)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

# P-E i18n + QA

## Task 17：i18n 五语 seed + Program.cs concat

**Files:**
- Create: `CP6.WebApi/Seed/I18nOaApproverScreenSeed.cs`
- Modify: `CP6.WebApi/Program.cs`(concat 链 L1778 串簽 seed 后)

> 照 `CP6.WebApi/Seed/I18nOaSerialSignScreenSeed.cs` 结构(静态 `Sys_Lang[] Items`，属性 `LangKey/ZhCN/ZhTW/En/Ja/Ko`)。**grep 全部新增 `t('…')` / `:label` 键**(designer.strategy.formField/dataMap/group、approverField/approverFieldHint、approverMapKey、approverWhen/Hint、approverFilter/Hint、member/addMember、errApproverConfig、oa.approverMap.*、nav.739、E-WF-014/015)，**去重避开 Phase B/C/串簽 seed 已有键**(如 common.* 若已存在勿重复)。

- [ ] **Step 1：建 seed 文件**

```csharp
using CP6.Entity.DomainModels.Sys;

namespace CP6.WebApi.Seed;

/// <summary>审批人解析高级策略画面词条(②③①)：oa.designer.strategy.*/oa.approverMap.*/nav.739/E-WF-014/015。
/// 去重避开 I18nOaInbox/Advanced/Designer/SerialSign seed 已有键。</summary>
public static class I18nOaApproverScreenSeed
{
    public static readonly Sys_Lang[] Items =
    {
        new() { LangKey = "oa.designer.strategy.formField", ZhCN = "表单字段指定", ZhTW = "表單欄位指定", En = "Form Field", Ja = "フォーム項目指定", Ko = "양식 필드 지정" },
        new() { LangKey = "oa.designer.strategy.dataMap",   ZhCN = "数据映射",     ZhTW = "資料映射",     En = "Data Map",   Ja = "データマップ",     Ko = "데이터 매핑" },
        new() { LangKey = "oa.designer.strategy.group",     ZhCN = "混合组",       ZhTW = "混合組",       En = "Group",      Ja = "混合グループ",     Ko = "혼합 그룹" },
        new() { LangKey = "oa.designer.approverField",      ZhCN = "审批人字段",   ZhTW = "審批人欄位",   En = "Approver Field", Ja = "承認者項目", Ko = "승인자 필드" },
        new() { LangKey = "oa.designer.approverFieldHint",  ZhCN = "表单中存审批人 UserId 的字段名", ZhTW = "表單中存審批人 UserId 的欄位名", En = "Form field holding approver UserId", Ja = "承認者UserIdを保持するフォーム項目", Ko = "승인자 UserId를 담는 양식 필드" },
        new() { LangKey = "oa.designer.approverMapKey",     ZhCN = "映射键",       ZhTW = "映射鍵",       En = "Map Key",    Ja = "マップキー",       Ko = "매핑 키" },
        new() { LangKey = "oa.designer.approverWhen",       ZhCN = "适用条件",     ZhTW = "適用條件",     En = "When (condition)", Ja = "適用条件", Ko = "적용 조건" },
        new() { LangKey = "oa.designer.approverWhenHint",   ZhCN = "对表单字段求值,为真才采用本规则。如 amount > 10000", ZhTW = "對表單欄位求值,為真才採用本規則。如 amount > 10000", En = "Evaluated over form fields; rule applies only if true. e.g. amount > 10000", Ja = "フォーム項目で評価し真なら適用。例 amount > 10000", Ko = "양식 필드로 평가, 참일 때만 적용. 예 amount > 10000" },
        new() { LangKey = "oa.designer.approverFilter",     ZhCN = "候选过滤",     ZhTW = "候選過濾",     En = "Candidate Filter", Ja = "候補フィルタ", Ko = "후보 필터" },
        new() { LangKey = "oa.designer.approverFilterHint", ZhCN = "逐候选求值,保留通过者。可用 user.deptId/starter.deptId 等", ZhTW = "逐候選求值,保留通過者。可用 user.deptId/starter.deptId 等", En = "Per-candidate filter; keep those passing. e.g. user.deptId == starter.deptId", Ja = "候補ごとに評価し通過者を残す。例 user.deptId == starter.deptId", Ko = "후보별 평가, 통과자만 유지. 예 user.deptId == starter.deptId" },
        new() { LangKey = "oa.designer.member",             ZhCN = "成员",         ZhTW = "成員",         En = "Member",     Ja = "メンバー",         Ko = "구성원" },
        new() { LangKey = "oa.designer.addMember",          ZhCN = "加成员",       ZhTW = "加成員",       En = "Add Member", Ja = "メンバー追加",     Ko = "구성원 추가" },
        new() { LangKey = "oa.designer.errApproverConfig",  ZhCN = "审批人高级配置不完整", ZhTW = "審批人進階配置不完整", En = "Advanced approver config incomplete", Ja = "承認者の詳細設定が不完全です", Ko = "고급 승인자 구성이 불완전합니다" },
        new() { LangKey = "oa.approverMap.key",         ZhCN = "映射键",   ZhTW = "映射鍵",   En = "Map Key",      Ja = "マップキー",   Ko = "매핑 키" },
        new() { LangKey = "oa.approverMap.matchValue",  ZhCN = "匹配值",   ZhTW = "匹配值",   En = "Match Value",  Ja = "一致値",       Ko = "일치 값" },
        new() { LangKey = "oa.approverMap.approverUser",ZhCN = "审批用户", ZhTW = "審批用戶", En = "Approver User",Ja = "承認ユーザー", Ko = "승인 사용자" },
        new() { LangKey = "oa.approverMap.approverRole",ZhCN = "审批角色", ZhTW = "審批角色", En = "Approver Role",Ja = "承認ロール",   Ko = "승인 역할" },
        new() { LangKey = "oa.approverMap.enable",      ZhCN = "启用",     ZhTW = "啟用",     En = "Enable",       Ja = "有効",         Ko = "사용" },
        new() { LangKey = "oa.approverMap.addRow",      ZhCN = "新增映射", ZhTW = "新增映射", En = "Add Mapping",  Ja = "マッピング追加",Ko = "매핑 추가" },
        new() { LangKey = "nav.739",                    ZhCN = "审批人映射", ZhTW = "審批人映射", En = "Approver Mapping", Ja = "承認者マッピング", Ko = "승인자 매핑" },
        new() { LangKey = "E-WF-014", ZhCN = "审批人高级配置非法", ZhTW = "審批人進階配置非法", En = "Invalid advanced approver config", Ja = "承認者の詳細設定が不正です", Ko = "고급 승인자 구성이 잘못되었습니다" },
        new() { LangKey = "E-WF-015", ZhCN = "审批人映射重复或非法", ZhTW = "審批人映射重複或非法", En = "Duplicate or invalid approver mapping", Ja = "承認者マッピングが重複または不正です", Ko = "승인자 매핑이 중복되거나 잘못되었습니다" },
    };
}
```
> **`common.*`/`oa.designer.approverUser`/`oa.designer.approverRole`/`oa.designer.userHint` 等键多半已存在**(串簽/Phase C seed) → **勿重复加**(撞 `UX_Sys_Lang_Tenant_Key`)。落码时 grep 确认每个键唯一后再保留。

- [ ] **Step 2：Program.cs concat**

L1778(`I18nOaSerialSignScreenSeed` 那行)后加：
```csharp
            .Concat(CP6.WebApi.Seed.I18nOaApproverScreenSeed.Items)  // 审批人解析高级策略 oa.designer.strategy.*/oa.approverMap.*/nav.739/E-WF-014/015
```

- [ ] **Step 3：编译 + commit**

`dotnet build CP6.WebApi/CP6.WebApi.csproj` → 绿。
```bash
git -C D:/CP6-wfs-approver add CP6.WebApi/Seed/I18nOaApproverScreenSeed.cs CP6.WebApi/Program.cs
git -C D:/CP6-wfs-approver commit -m "feat(wfs-approver): T17 i18n 五语 seed(高级策略/映射维护/E-WF-014·015/nav.739)+concat

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 18：全量回归 + gstack QA harness

**Files:**
- Create: `docs/superpowers/qa/wfs-approver-resolution/README.md`, `seed.sql`, `qa_approver.ps1`

- [ ] **Step 1：全量后端回归**

Run: `cd /d/CP6-wfs-approver && dotnet test CP6.Tests/CP6.Tests.csproj`
Expected: 全绿(基线 1320 + 本计划新增解析器/映射/校验/forecast 测)。

- [ ] **Step 2：前端全量**

Run: `cd /d/CP6-wfs-approver/cp6.web && npm run check && npm run test && npm run build`
Expected: type-check 0 / vitest 绿(含 designerModel 新测) / build 绿。

- [ ] **Step 3：写 QA harness(只写不跑服务器，避并行会话冲突)**

`docs/superpowers/qa/wfs-approver-resolution/README.md` 写 7 剧本(隔离库 `CP6DB_OA`，端口避开 Space 与串簽：后端 5179 / 前端 5181)：
1. 设计含 FormField 节点的流程 → 保存校验通过。
2. 维护页 → 种 `cc/A100→user1`、`cc/A100→role9`(映射 CRUD + E-WF-015 重复拦截)。
3. 填單(DynamicForm `user` 选择器选审批人)→ 提交 → FormField 节点指派该人。
4. DataMap 节点：填 costCenter=A100 → 指派 user1 + role9 全员。
5. When 门控：amount=50000 触发额外审批档；amount=100 跳过。
6. Filter：Role 节点 + `user.deptId == starter.deptId` → 仅同部门人收待办。
7. Group 节点：直属+指定混源 → 合并去重 → 整组会签。
+ forecast：FormInitiate 填單后预览显具体审批人名(FormField/DataMap)。

`seed.sql`(OA 表单数单数表名 `Wf_FormDef`/`Wf_FlowDef`，`SET QUOTED_IDENTIFIER ON`)+ `qa_approver.ps1`(HTTP e2e，ASCII 数据，400 错误体从 `Exception.Response.GetResponseStream()` 读)。

- [ ] **Step 4：commit(harness)**
```bash
git -C D:/CP6-wfs-approver add docs/superpowers/qa/wfs-approver-resolution
git -C D:/CP6-wfs-approver commit -m "test(wfs-approver): T18 gstack QA harness(7 剧本+seed.sql+e2e 脚本)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

- [ ] **Step 5：live gstack QA(用户在场)**

隔离库 `CP6DB_OA` 起后端 5179 + 前端 5181 → gstack 真浏览器跑 7 剧本 + forecast。**抓修缺陷按 TDD 回归**。坑(沿用)：browse headless 钉 chromium 1208(缺则装 headless-shell 后复制 1223→1208)；Vue Flow 节点选中须 dispatch pointerdown/mousedown；el-table 行须 dispatch td.cell。

---

## 自检(写完计划对照 spec)

- **Spec 覆盖**：③ FormField=T3；②a When/Filter=T6；②b DataMap=T4+T7(表/服务)+T1(表);① Group=T5;接缝 VarsJson=T9;设计期承载方案 X=T8;设计器=T11/T12;维护页=T13/T14/T15;表单控件=T16;forecast=T9;校验 E-WF-014=T10;E-WF-015=T7;i18n=T17;QA=T18。**全 §0~§13 有对应 Task**。
- **占位扫描**：无 TBD/TODO；每代码步含完整代码。少数"落码前核对真实 DTO 属性名/既有 seed 键去重"是**审慎核对指令**(非占位)——因这些真名须在目标文件实读确认，计划已给出核对位置。
- **类型一致**：`ApproverRule`{FieldName/MapKey/When/Filter/Members}、`ApproverResolveContext.VarsJson`、`Wf_ApproverMap`{MapKey/MatchValue/ApproverUserId/ApproverRoleId/OrderNo/Enable}、`ApproverSpec`{Strategy/ApproverLevels/ApproverRoleId/ApproverUserId/FieldName/MapKey/When/Filter}、FlowNode/ApprovalStage 新 5 字段(ApproverFieldName/ApproverMapKey/ApproverWhen/ApproverFilter/ApproverMembers)、前端 `ApproverSpecDto`{strategy/approverLevels/approverRoleId/approverUserId/fieldName/mapKey/when/filter} —— 跨 Task 命名一致。
- **错误码**：E-WF-014(配置)/E-WF-015(映射) 全程一致；运行期缺位走既有 E-WF-013(不新增)。
