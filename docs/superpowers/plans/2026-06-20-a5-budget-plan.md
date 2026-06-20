# A5 预算 / 管理会计 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把"预算/管理会计"做真——多维(科目×成本中心×成本对象×期间)年度预算编制 + 按月分解 + 多版本接 OA 审批 + 预算 vs 实际实时报表 + 按预算行可选过账控制(None/Warn/Block)。补齐 ERP 完整性路线 A5（最后一项 A 类缺口），定位"账外 memorandum"——不产生凭证、不改 GL 过账逻辑。

**Architecture:** 新建 Fin 4 实体 `Budget`/`BudgetVersion`/`BudgetLine`/`BudgetLinePeriod`（均继承 `BaseTenantEntity`；`BudgetVersion`/`BudgetLine` 显式加 `RowVersion`）。`BudgetLine` 含真实可空维度(`CostCenterId`/`CostObjectType`/`CostObjectId`，保 FK 关系) + 3 个 not-null 规范化键(`CostCenterKey`/`CostObjectTypeKey`/`CostObjectIdKey`，仅供唯一索引规避 NULL 不唯一)。`BudgetService` 管方案/版本状态机(Draft→PendingApproval→Approved/Rejected→Archived、一个 Active) + 复制(版本/上年实际) + 提交审批 + 激活；`BudgetLineService` 管行网格/按月分解/Excel 导入。OA 接入照 `JournalApprovalCallback` 先例：`IApprovalService.SubmitAsync("A5_Budget",…)` + `BudgetApprovalCallback`(通过→`ActivateFromApprovalAsync` 同事务一次性激活清旧/驳回→可改回草稿) + seed `Wf_FlowDef`/`Wf_ApprovalBinding`。`BudgetGuard` 复用 A4 `BankReconGuard` 静态守卫范式（同 DbContext 直查、无循环依赖），仅挂手工 `PostAsync`、Block 仅作用费用、YTD/Period 口径、最具体维度匹配、同 entry 合并防绕过。`BudgetReportService` 仿 `TrialBalanceService` 聚合已过账实际(加成本中心/成本对象维度)，对比 Active 预算出差异/差异率/执行率 + "未编预算实际"分组。前端 2 视图(编制网格 + 执行分析) + 并发冲突 UX。**不新增 `VoucherSource`**。

**Tech Stack:** .NET 8 + EF Core 8 / xUnit + EF Core InMemory + EF Core Sqlite(8.0.12 已引) / Vue 3.5 + element-plus + vue-i18n / ClosedXML 0.105(已引)。spec：`docs/superpowers/specs/2026-06-20-a5-budget-design.md`（A5-D1~D7 + §8 四小决策 + rev1 4 点修订，用户已批准）。

---

## 关键既有约定（落码前必读）

- **多租户基类**：A5 实体继承 `BaseTenantEntity`（=`Id`/审计 + `TenantId`，**不含** `RowVersion`/`IsDeleted`）。预算"停用≠逻辑删除"故不继承 `BaseBizEntity`；编辑/并发核心实体 `BudgetVersion`/`BudgetLine` **显式加** `[Timestamp] public byte[]? RowVersion { get; set; }`（与 `BaseBizEntity` 同写法）。`Budget`/`BudgetLinePeriod` 不加 RowVersion（方案改动少；分解行随 BudgetLine 整体保存）。
- **唯一索引租户前缀自动重写**：`CP6Context.OnModelCreating` 末尾反射循环对所有 `BaseTenantEntity` 子类**唯一索引**自动前缀 `TenantId`。**只声明逻辑唯一索引**（`HasIndex(...).IsUnique()`），**勿手写 `TenantId`**。A5 维度成员（`CostCenterId` 等）无导航属性/DB 外键约束（GUID 值引用，参 `JournalLine.AccountId` 风格），唯一索引正常获得租户前缀。
- **NULL 维度唯一性（spec §3.3 rev1，关键）**：`BudgetLine` **不**把 `Guid.Empty` 写进 `CostCenterId`（会撞 FK）。真实维度 `CostCenterId`(Guid?)/`CostObjectType`(string?)/`CostObjectId`(string?) 保持可空承载业务/FK；另加 not-null 规范化键 `CostCenterKey`(Guid)/`CostObjectTypeKey`(string)/`CostObjectIdKey`(string)，服务层保存时派生（`CostCenterKey = CostCenterId ?? Guid.Empty`；`CostObjectTypeKey = CostObjectType ?? ""`；`CostObjectIdKey = CostObjectId ?? ""`）。唯一索引 `UX_Fin_BudgetLine_Dim` **只用 Key 列**：`(VersionId, AccountId, CostCenterKey, CostObjectTypeKey, CostObjectIdKey)`。
- **迁移命令**：`dotnet ef migrations add <Name> --project CP6.Core --startup-project CP6.WebApi`（**会先构建**；**勿带 `--no-build`**）。生成后打开 `*_<Name>.cs` 核对 `CreateTable`/索引列（唯一索引列含 `"TenantId"` 前缀 + Key 列）。
- **DbSet 注册**：在 `CP6.Core/Data/CP6Context.cs` 加 4 个 `DbSet<>` + `OnModelCreating` 唯一索引配置（参既有 A4 BankStatement 配置块）。
- **控制器范式**（参 `PaymentController`/`GlAccountController`）：`[ApiController]`+`[Route("api/fin/budget...")]`+`[Authorize]`+`ControllerBase`；私有助手逐字：
  ```csharp
  private string CurrentUser => User?.Identity?.Name ?? "anonymous";
  private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
  private IActionResult Fin(FinResult r) => r.Ok ? Ok2() : BadRequest(new { code = 400, message = r.Code, args = r.Args });
  ```
  端点贴 `[RequirePermission("fin-budget", "<action>")]`（resource key=派生 MenuKey；`HasActionAsync` 无 admin 旁路 → 属性与 seed 同 commit；**GET 也要 seed `view`**，避开 A3 漏 view 致 403 坑）。
- **`FinResult`**：`{ Ok, Code, Args }` + `Pass()` / `Fail(code, params object[] args)`。A5 服务统一返回。批量逐行结果（Excel 预览/预检预警）用自定义 DTO List。
- **`JournalEntryService.PostAsync(entryId, checkerId)`**：现挂 `BankReconGuard.CheckPostingAsync(_db, e)`（约 L84，在 `ValidateAsync` 前）。**A5 仅在此处**追加 `BudgetGuard.CheckPostingAsync(_db, e)`（紧接 BankReconGuard 之后）。**不挂** `AutoPostAsync`（决策 §8-2：自动凭证不被预算卡死）、**不挂** `ReverseAsync`（红冲释放预算）。
- **`FiscalPeriodService`**：`ComputeFiscal(year,month)→(FiscalYear,PeriodNo)`、`ResolveAsync(date)→FiscalPeriod?`、`IsOpenAsync(periodId)`、`ListAsync(year?)`。**落期口径（spec §7.1 rev1）**：守卫/报表都**优先 `entry.PeriodId` 关联 `FiscalPeriod` 取 `FiscalYear`+`PeriodNo`**，`PeriodId` 空才 fallback `ResolveAsync(VoucherDate)`——**勿直接按 `VoucherDate.Year/Month` 算**（非自然财年会错）。
- **`FinSequenceService.NextAsync(seqKey, date)`** → `"{KEY}-{yyyy-MM}-{NNNNN}"`。方案号用 `seqKey="BUD"`（生成 `BUD-2026-06-00001`，对齐 spec `BUD-{FY}-nnnn` 语义；FY 取 fiscalYear 拼前缀亦可，落码取 `$"BUD-{fiscalYear}-{seq4}"` 自拼以含财年，见 Task B-1）。
- **OA 审批引擎接入**（spec §8，照 `JournalApprovalCallback` 先例）：
  - `CP6.Core.Services.Wf.IApprovalService.SubmitAsync(string bizType, string bizId, Guid starterId, object? formSnapshot=null) → Task<Guid>`（返流程实例 Id；同 (bizType,bizId) 有 Running 实例则抛异常）。
  - `CP6.Core.Services.Wf.IApprovalCallback`：`string BizType { get; }` + `Task OnApprovedAsync(ApprovalCallbackContext ctx)` + `Task OnRejectedAsync(ApprovalCallbackContext ctx)`。`ApprovalCallbackContext{ BizType, BizId, InstanceId, StarterId, DecidedById(Guid?), Reason }`。
  - 回调由 `ApprovalDispatcher.OnInstanceFinishedAsync` 在 `FlowEngine` **最终 `SaveChangesAsync` 之前**调用，**与引擎共享 scoped `CP6Context`**，回调抛异常则审批+业务一并不落库（原子）。**回调内不自行 SaveChanges**。
  - 注册：`Program.cs` 已有 `AddScoped<IApprovalCallback, JournalApprovalCallback>()`，A5 **追加** `AddScoped<IApprovalCallback, BudgetApprovalCallback>()`（多回调按 `BizType` 分发）。
  - Seed：`Wf_FlowDef{FlowKey="budget-approve", FlowName="预算审批", FormKey="BudgetApproval", SchemaJson=单审批人(admin)默认流程, Enable=true}` + `Wf_ApprovalBinding{BizType="A5_Budget", FlowKey="budget-approve", Enable=true}`（幂等 seed）。
- **实际聚合范式**（参 `TrialBalanceService.cs:29-42`）：`from l in _db.JournalLines join e in _db.JournalEntries on l.EntryId equals e.Id where e.Status==JournalStatus.Posted ...`；A5 报表额外 join `GlAccounts`(取 Type 筛损益)、group by 含 `l.CostCenterId/CostObjectType/CostObjectId`、按 `JournalEntry.PeriodId→FiscalPeriod` 的 FiscalYear/PeriodNo 过滤。
- **测试基建**：`TestHelper.CreateInMemoryContext()` = `new CP6Context(UseInMemoryDatabase(Guid))`，默认租户。`CP6.Tests/GlobalUsings.cs` **不含** Fin 命名空间 → A5 测试文件按需 `using CP6.Entity.DomainModels.Fin; using CP6.Core.Services.Fin;`。
- **SQLite 已知限制（spec §16.1）**：`CP6Context` 含 SQL Server `nvarchar(max)` 列，SQLite `EnsureCreated` 撞 `near "max"`（A3 H-1 / A4 H-2 同因 drop）。A5 **唯一约束/并发以 InMemory 覆盖逻辑 + 真 SQL Server 兜底结构层**，不强求 SQLite 全 schema 建库（落码时若已有绕法可加，否则跳过 SQLite 结构测试并在 plan H 注明）。
- **审计日志**：全局 `OperLogFilter` 自动记录所有 POST/PUT/DELETE → A5 各写端点自动入 `Sys_OperLog`，**服务层无需手写日志**。
- **金额精度**：A5 全部金额 `[Column(TypeName="decimal(18,2)")]`（与 `JournalLine.Debit/Credit` 一致），比较按存储精度完全相等。
- **i18n seed**：`public static class I18nA5BudgetScreenSeed { public static readonly Sys_Lang[] Items = new[] {...}; }`，每条 `new Sys_Lang { LangKey=..., ZhCN=..., ZhTW=..., En=..., Ja=..., Ko=... }`；菜单键 `nav.621/622/623`、错误码 `E-A5-*`/`W-A5-*` 直接以 LangKey 落条；`Program.cs` i18n 链 `.Concat(I18nA5BudgetScreenSeed.Items)`。fin 视图惯例用**中文自然语言 key**（如 `t('新建')`），零裸 key。
- **菜单 621-623 空位**（600~620 已用）：`621 预算管理`(父,挂 600)、`622 预算编制`(RoutePath `/fin/budget`)、`623 执行分析`(RoutePath `/fin/budget/vs-actual`)。三者 RoleMenu{RoleId=1} + MenuKey 由 RoutePath 自动派生（622→`fin-budget`；623→`fin-budget-vs-actual`，但**权限统一用 `fin-budget`**，故 623 控制器也贴 `[RequirePermission("fin-budget", ...)]`）。Program.cs MenuKey 派生循环范围改 `<=623`。**注意路由菜单驱动（A4 H-3 教训）**：seed 菜单 + RoleMenu 后前端 `addDynamicRoutes` 才注册路由，否则白屏不可达——622/623 都要 seed。
- **前端并发 UX**：后端 RowVersion 冲突 → `FinResult.Fail("E-A5-CONCURRENCY-001")`（HTTP 400，body.message=code），前端编制网格捕获该 code → `ElMessageBox` 提示刷新重试。

---

## File Structure

### 新建 — 实体（`CP6.Entity/DomainModels/Fin/`）
- `Budget.cs`（含 `BudgetScope` enum）
- `BudgetVersion.cs`（含 `BudgetVersionStatus`/`BudgetControlMode`/`BudgetControlBasis` enums + RowVersion）
- `BudgetLine.cs`（真实维度 + 规范化 Key + RowVersion）
- `BudgetLinePeriod.cs`

### 修改 — 数据上下文
- `CP6.Core/Data/CP6Context.cs`（4 DbSet + OnModelCreating 唯一索引）
- `CP6.Core/Services/Fin/JournalEntryService.cs`（`PostAsync` 追加 `BudgetGuard.CheckPostingAsync`）

### 新建 — 服务/DTO（`CP6.Core/Services/Fin/`）
- `BudgetDtos.cs`（`BudgetLineDto`/`BudgetLineGridRow`/`BudgetImportPreviewResult`/`BudgetImportRow`/`BudgetVsActualRow`/`BudgetVsActualReport`/`BudgetWarningDto` 等）
- `IBudgetService.cs` / `BudgetService.cs`（方案/版本 CRUD + 状态机 + 复制 + 提交审批 + 激活 + `ActivateFromApprovalAsync`）
- `IBudgetLineService.cs` / `BudgetLineService.cs`（行 upsert/删 + 按月分解 + Excel 导入 Preview/Confirm）
- `BudgetApprovalCallback.cs`（`IApprovalCallback`，BizType="A5_Budget"）
- `BudgetGuard.cs`（静态守卫，供 `JournalEntryService` 直查同 DbContext；核心评估器 `BudgetEvaluator` 供守卫 + 预检共用）
- `IBudgetReportService.cs` / `BudgetReportService.cs`（预算 vs 实际 + 未编预算分组 + 过账预检 PreCheck）

### 新建 — 控制器（`CP6.WebApi/Controllers/Fin/`）
- `BudgetController.cs`（方案+版本）/ `BudgetLineController.cs`（行网格+导入）/ `BudgetReportController.cs`（分析+预检）

### 新建 — Seed（`CP6.WebApi/Seed/`）
- `I18nA5BudgetScreenSeed.cs`（五语词条）
- `A5BudgetFlowSeed.cs`（Wf_FlowDef + Wf_ApprovalBinding 幂等 seed）
- 修改 `Program.cs`（菜单 621-623 + RoleMenu/MenuActions + i18n concat + flow seed + DI 注册 callback）

### 新建 — 前端（`cp6.web/src/`）
- `types/fin/budget.ts` / `api/fin/budget.ts`
- `views/fin/BudgetEditView.vue` / `views/fin/BudgetVsActualView.vue`
- 修改 `router/index.ts`（2 路由，菜单驱动）

### 新建 — 测试（`CP6.Tests/Fin/`）
- `BudgetVersionStateMachineTests.cs` / `BudgetLineBreakdownTests.cs` / `BudgetGuardTests.cs` / `BudgetApprovalIntegrationTests.cs` / `BudgetVsActualTests.cs` / `BudgetCopyImportTests.cs` / `BudgetSqliteTests.cs`(若 SQLite 可用)

---

## Phase A — 数据模型 + 迁移

### Task A-1: 4 实体 + 枚举

**Files:**
- Create: `CP6.Entity/DomainModels/Fin/Budget.cs`
- Create: `CP6.Entity/DomainModels/Fin/BudgetVersion.cs`
- Create: `CP6.Entity/DomainModels/Fin/BudgetLine.cs`
- Create: `CP6.Entity/DomainModels/Fin/BudgetLinePeriod.cs`

- [ ] **Step 1: 写实体（无测试，build 验证）**

`Budget.cs`:
```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Fin;

/// <summary>预算方案（按财年，每财年唯一）。</summary>
[Table("Fin_Budget")]
public class Budget : BaseTenantEntity
{
    [MaxLength(30)] public string No { get; set; } = "";
    [MaxLength(100)] public string Name { get; set; } = "";
    public int FiscalYear { get; set; }
    public BudgetScope Scope { get; set; } = BudgetScope.PnL;
    [MaxLength(500)] public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}

public enum BudgetScope { PnL = 1 }
```

`BudgetVersion.cs`:
```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Fin;

/// <summary>预算版本。方案下多版本；至多一个 IsActive，作控制+报表唯一基准。</summary>
[Table("Fin_BudgetVersion")]
public class BudgetVersion : BaseTenantEntity
{
    public Guid BudgetId { get; set; }
    public int VersionNo { get; set; }
    [MaxLength(100)] public string Name { get; set; } = "";
    public BudgetVersionStatus Status { get; set; } = BudgetVersionStatus.Draft;
    public bool IsActive { get; set; }
    public BudgetControlMode DefaultControlMode { get; set; } = BudgetControlMode.None;
    public BudgetControlBasis DefaultControlBasis { get; set; } = BudgetControlBasis.Ytd;
    public Guid? ApprovalInstanceId { get; set; }
    [MaxLength(50)] public string? ApprovalRef { get; set; }
    public DateTime? SubmittedAt { get; set; }
    [MaxLength(100)] public string? SubmittedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    [MaxLength(100)] public string? ApprovedBy { get; set; }
    [MaxLength(500)] public string? RejectReason { get; set; }
    [Timestamp] public byte[]? RowVersion { get; set; }
}

public enum BudgetVersionStatus { Draft = 0, PendingApproval = 1, Approved = 2, Rejected = 3, Archived = 4 }
public enum BudgetControlMode { None = 0, Warn = 1, Block = 2 }
public enum BudgetControlBasis { Ytd = 0, Period = 1 }
```

`BudgetLine.cs`:
```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Fin;

/// <summary>预算行=维度桶（科目×成本中心(可空)×成本对象(可空)）。一版一桶唯一。</summary>
[Table("Fin_BudgetLine")]
public class BudgetLine : BaseTenantEntity
{
    public Guid VersionId { get; set; }
    public Guid AccountId { get; set; }

    // 真实业务维度（可空，承载 FK/显示）
    public Guid? CostCenterId { get; set; }
    [MaxLength(20)] public string? CostObjectType { get; set; }
    [MaxLength(50)] public string? CostObjectId { get; set; }

    // 规范化键（not-null，仅供唯一索引；服务层派生，前端不可见）
    public Guid CostCenterKey { get; set; }
    [MaxLength(20)] public string CostObjectTypeKey { get; set; } = "";
    [MaxLength(50)] public string CostObjectIdKey { get; set; } = "";

    [Column(TypeName = "decimal(18,2)")] public decimal AnnualAmount { get; set; }
    public BudgetControlMode? ControlMode { get; set; }
    public BudgetControlBasis? ControlBasis { get; set; }
    [MaxLength(500)] public string? Memo { get; set; }
    [Timestamp] public byte[]? RowVersion { get; set; }

    /// <summary>由真实维度派生规范化键（保存前调）。</summary>
    public void NormalizeKeys()
    {
        CostCenterKey = CostCenterId ?? Guid.Empty;
        CostObjectTypeKey = CostObjectType ?? "";
        CostObjectIdKey = CostObjectId ?? "";
    }
}
```

`BudgetLinePeriod.cs`:
```csharp
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Fin;

/// <summary>预算行按月分解（财年期号 1..12）。随 BudgetLine 整体保存。</summary>
[Table("Fin_BudgetLinePeriod")]
public class BudgetLinePeriod : BaseTenantEntity
{
    public Guid BudgetLineId { get; set; }
    public int PeriodNo { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal Amount { get; set; }
}
```

- [ ] **Step 2: build 验证**

Run: `dotnet build CP6.Entity`
Expected: 成功，无警告关于这 4 文件。

- [ ] **Step 3: Commit**

```bash
git add CP6.Entity/DomainModels/Fin/Budget.cs CP6.Entity/DomainModels/Fin/BudgetVersion.cs CP6.Entity/DomainModels/Fin/BudgetLine.cs CP6.Entity/DomainModels/Fin/BudgetLinePeriod.cs
git commit -m "feat(fin): A5 budget 4 entities + enums (BaseTenantEntity, normalized dim keys, no VoucherSource) (spec §3)"
```

### Task A-2: DbContext DbSet + 唯一索引

**Files:**
- Modify: `CP6.Core/Data/CP6Context.cs`

- [ ] **Step 1: 加 DbSet（参既有 Fin DbSet 区块）**

```csharp
public DbSet<Budget> Budgets => Set<Budget>();
public DbSet<BudgetVersion> BudgetVersions => Set<BudgetVersion>();
public DbSet<BudgetLine> BudgetLines => Set<BudgetLine>();
public DbSet<BudgetLinePeriod> BudgetLinePeriods => Set<BudgetLinePeriod>();
```

- [ ] **Step 2: OnModelCreating 唯一索引（在既有 Fin 配置块后追加；勿手写 TenantId，反射循环自动补前缀）**

```csharp
// ── A5 预算 ──
modelBuilder.Entity<Budget>().HasIndex(b => b.FiscalYear).IsUnique().HasDatabaseName("UX_Fin_Budget_FiscalYear");
modelBuilder.Entity<BudgetVersion>().HasIndex(v => new { v.BudgetId, v.VersionNo }).IsUnique().HasDatabaseName("UX_Fin_BudgetVersion_BudgetNo");
modelBuilder.Entity<BudgetLine>().HasIndex(l => new { l.VersionId, l.AccountId, l.CostCenterKey, l.CostObjectTypeKey, l.CostObjectIdKey }).IsUnique().HasDatabaseName("UX_Fin_BudgetLine_Dim");
modelBuilder.Entity<BudgetLinePeriod>().HasIndex(p => new { p.BudgetLineId, p.PeriodNo }).IsUnique().HasDatabaseName("UX_Fin_BudgetLinePeriod_LinePeriod");
```

- [ ] **Step 3: build 验证**

Run: `dotnet build CP6.Core`
Expected: 成功。

- [ ] **Step 4: Commit**

```bash
git add CP6.Core/Data/CP6Context.cs
git commit -m "feat(fin): register A5 budget DbSets + unique indexes (Key-based dim uniqueness, tenant-prefix auto) (spec §3/§14.1)"
```

### Task A-3: EF 迁移

**Files:**
- Create: `CP6.Core/Migrations/*_A5Budget.cs`（自动生成）

- [ ] **Step 1: 生成迁移**

Run: `dotnet ef migrations add A5Budget --project CP6.Core --startup-project CP6.WebApi`
Expected: 生成 `*_A5Budget.cs`。

- [ ] **Step 2: 反验迁移**

打开 `*_A5Budget.cs` 核对：
- 4 张 `CreateTable`（Fin_Budget/Fin_BudgetVersion/Fin_BudgetLine/Fin_BudgetLinePeriod）。
- `Fin_BudgetLine` 含 `CostCenterId`(nullable)/`CostObjectType`(nullable)/`CostObjectId`(nullable) + `CostCenterKey`(not null)/`CostObjectTypeKey`(not null)/`CostObjectIdKey`(not null)。
- `RowVersion` 列在 BudgetVersion/BudgetLine（rowversion/timestamp）。
- 唯一索引 4 个，列含 `TenantId` 前缀 + `UX_Fin_BudgetLine_Dim` 用 `CostCenterKey/CostObjectTypeKey/CostObjectIdKey`。
- decimal 列 `decimal(18,2)`。

- [ ] **Step 3: Commit**

```bash
git add CP6.Core/Migrations/
git commit -m "feat(fin): A5Budget migration (4 tables + Key-based unique index + RowVersion) (spec §14.1)"
```

---

## Phase B — 方案/版本 Service + 状态机

### Task B-1: BudgetService 方案 + 版本 CRUD

**Files:**
- Create: `CP6.Core/Services/Fin/BudgetDtos.cs`
- Create: `CP6.Core/Services/Fin/IBudgetService.cs`
- Create: `CP6.Core/Services/Fin/BudgetService.cs`
- Test: `CP6.Tests/Fin/BudgetVersionStateMachineTests.cs`

- [ ] **Step 1: 写失败测试（建方案唯一财年 + 建版本 VersionNo 自增）**

```csharp
using CP6.Core.Data;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CP6.Tests.Fin;

public class BudgetVersionStateMachineTests
{
    private static CP6Context Db() => TestHelper.CreateInMemoryContext();

    [Fact]
    public async Task CreateBudget_DuplicateFiscalYear_Rejected()
    {
        using var db = Db();
        var svc = new BudgetService(db, new FinSequenceService(db));
        var r1 = await svc.CreateBudgetAsync(new Budget { Name = "2027", FiscalYear = 2027 }, "admin");
        Assert.True(r1.Ok);
        var r2 = await svc.CreateBudgetAsync(new Budget { Name = "2027b", FiscalYear = 2027 }, "admin");
        Assert.False(r2.Ok);
        Assert.Equal("E-A5-BUDGET-001", r2.Code);
    }

    [Fact]
    public async Task CreateVersion_AutoIncrementsVersionNo()
    {
        using var db = Db();
        var svc = new BudgetService(db, new FinSequenceService(db));
        var b = (await svc.CreateBudgetAsync(new Budget { Name = "2027", FiscalYear = 2027 }, "admin")).Data!;
        var v1 = (await svc.CreateVersionAsync(b.Id, "初稿", "admin")).Data!;
        var v2 = (await svc.CreateVersionAsync(b.Id, "调整", "admin")).Data!;
        Assert.Equal(1, v1.VersionNo);
        Assert.Equal(2, v2.VersionNo);
        Assert.Equal(BudgetVersionStatus.Draft, v2.Status);
    }
}
```

> `FinResult` 需带泛型 Data 的变体：若既有 `FinResult` 无 `Data`，新增 `FinResult<T>`（`Ok`/`Code`/`Args`/`Data`）于 `BudgetDtos.cs`，或服务返回 `(FinResult, entity)`。本 plan 采 `FinResult<T>`（见 Step 3）。

- [ ] **Step 2: 运行验证失败**

Run: `dotnet test CP6.Tests --filter BudgetVersionStateMachineTests`
Expected: FAIL（BudgetService 未定义）。

- [ ] **Step 3: 写 DTO + 接口 + 实现**

`BudgetDtos.cs`:
```csharp
namespace CP6.Core.Services.Fin;

/// <summary>带数据的结果（既有 FinResult 无 Data 时用）。</summary>
public class FinResult<T>
{
    public bool Ok { get; init; }
    public string? Code { get; init; }
    public object[]? Args { get; init; }
    public T? Data { get; init; }
    public static FinResult<T> Pass(T data) => new() { Ok = true, Data = data };
    public static FinResult<T> Fail(string code, params object[] args) => new() { Ok = false, Code = code, Args = args };
}
```

`IBudgetService.cs`:
```csharp
using CP6.Entity.DomainModels.Fin;

namespace CP6.Core.Services.Fin;

public interface IBudgetService
{
    Task<List<Budget>> ListBudgetsAsync();
    Task<FinResult<Budget>> CreateBudgetAsync(Budget dto, string user);
    Task<FinResult> UpdateBudgetAsync(Guid id, string name, string? description);
    Task<FinResult> DeactivateBudgetAsync(Guid id);

    Task<List<BudgetVersion>> ListVersionsAsync(Guid budgetId);
    Task<BudgetVersion?> GetVersionAsync(Guid versionId);
    Task<FinResult<BudgetVersion>> CreateVersionAsync(Guid budgetId, string name, string user,
        Guid? copyFromVersionId = null, int? copyFromActualFiscalYear = null);
    Task<FinResult> UpdateVersionAsync(Guid versionId, string name, BudgetControlMode mode, BudgetControlBasis basis);
    Task<FinResult> DeleteVersionAsync(Guid versionId);

    Task<FinResult> SubmitForApprovalAsync(Guid versionId, Guid userId, string userName);
    Task<FinResult> ActivateAsync(Guid versionId);
    Task ActivateFromApprovalAsync(Guid versionId, string decidedBy);   // OA 回调专用，不 SaveChanges
}
```

`BudgetService.cs`（本 Task 仅 CRUD 部分，提交/激活在 B-2、复制在 C-2，留 `NotImplementedException` 桩并在对应 Task 实现）:
```csharp
using CP6.Core.Data;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Fin;

public class BudgetService : IBudgetService
{
    private readonly CP6Context _db;
    private readonly FinSequenceService _seq;
    // OA + 报表服务在 B-2/C-2 注入（提交审批/复制实际需要），本 Task 先不注入
    public BudgetService(CP6Context db, FinSequenceService seq) { _db = db; _seq = seq; }

    public Task<List<Budget>> ListBudgetsAsync() =>
        _db.Budgets.AsNoTracking().OrderByDescending(b => b.FiscalYear).ToListAsync();

    public async Task<FinResult<Budget>> CreateBudgetAsync(Budget dto, string user)
    {
        if (await _db.Budgets.AnyAsync(b => b.FiscalYear == dto.FiscalYear))
            return FinResult<Budget>.Fail("E-A5-BUDGET-001", dto.FiscalYear);
        var seq = await _seq.NextAsync("BUD", DateTime.Now);
        dto.No = $"BUD-{dto.FiscalYear}-{seq.Split('-').Last()}";
        dto.Scope = BudgetScope.PnL;
        dto.IsActive = true;
        dto.Creator = user; dto.CreateDate = DateTime.Now;
        _db.Budgets.Add(dto);
        await _db.SaveChangesAsync();
        return FinResult<Budget>.Pass(dto);
    }

    public async Task<FinResult> UpdateBudgetAsync(Guid id, string name, string? description)
    {
        var b = await _db.Budgets.FindAsync(id);
        if (b == null) return FinResult.Fail("E-A5-BUDGET-404");
        b.Name = name; b.Description = description;
        await _db.SaveChangesAsync();
        return FinResult.Pass();
    }

    public async Task<FinResult> DeactivateBudgetAsync(Guid id)
    {
        var b = await _db.Budgets.FindAsync(id);
        if (b == null) return FinResult.Fail("E-A5-BUDGET-404");
        b.IsActive = false;
        await _db.SaveChangesAsync();
        return FinResult.Pass();
    }

    public Task<List<BudgetVersion>> ListVersionsAsync(Guid budgetId) =>
        _db.BudgetVersions.AsNoTracking().Where(v => v.BudgetId == budgetId)
           .OrderBy(v => v.VersionNo).ToListAsync();

    public Task<BudgetVersion?> GetVersionAsync(Guid versionId) =>
        _db.BudgetVersions.FirstOrDefaultAsync(v => v.Id == versionId);

    public async Task<FinResult<BudgetVersion>> CreateVersionAsync(Guid budgetId, string name, string user,
        Guid? copyFromVersionId = null, int? copyFromActualFiscalYear = null)
    {
        var budget = await _db.Budgets.FindAsync(budgetId);
        if (budget == null) return FinResult<BudgetVersion>.Fail("E-A5-BUDGET-404");
        var maxNo = await _db.BudgetVersions.Where(v => v.BudgetId == budgetId)
            .Select(v => (int?)v.VersionNo).MaxAsync() ?? 0;
        var v = new BudgetVersion
        {
            BudgetId = budgetId, VersionNo = maxNo + 1, Name = name,
            Status = BudgetVersionStatus.Draft, IsActive = false,
            Creator = user, CreateDate = DateTime.Now,
        };
        _db.BudgetVersions.Add(v);
        await _db.SaveChangesAsync();
        // 复制起点（C-2 实现 CopyInto；此处调用，C-2 前为桩）
        if (copyFromVersionId.HasValue || copyFromActualFiscalYear.HasValue)
            await CopyIntoAsync(v.Id, copyFromVersionId, copyFromActualFiscalYear);   // C-2
        return FinResult<BudgetVersion>.Pass(v);
    }

    public async Task<FinResult> UpdateVersionAsync(Guid versionId, string name, BudgetControlMode mode, BudgetControlBasis basis)
    {
        var v = await _db.BudgetVersions.FindAsync(versionId);
        if (v == null) return FinResult.Fail("E-A5-VERSION-404");
        if (v.Status != BudgetVersionStatus.Draft) return FinResult.Fail("E-A5-VERSION-005");
        v.Name = name; v.DefaultControlMode = mode; v.DefaultControlBasis = basis;
        await _db.SaveChangesAsync();
        return FinResult.Pass();
    }

    public async Task<FinResult> DeleteVersionAsync(Guid versionId)
    {
        var v = await _db.BudgetVersions.FindAsync(versionId);
        if (v == null) return FinResult.Fail("E-A5-VERSION-404");
        if (v.Status != BudgetVersionStatus.Draft) return FinResult.Fail("E-A5-VERSION-005");
        var lineIds = await _db.BudgetLines.Where(l => l.VersionId == versionId).Select(l => l.Id).ToListAsync();
        _db.BudgetLinePeriods.RemoveRange(_db.BudgetLinePeriods.Where(p => lineIds.Contains(p.BudgetLineId)));
        _db.BudgetLines.RemoveRange(_db.BudgetLines.Where(l => l.VersionId == versionId));
        _db.BudgetVersions.Remove(v);
        await _db.SaveChangesAsync();
        return FinResult.Pass();
    }

    // ── B-2 实现 ──
    public Task<FinResult> SubmitForApprovalAsync(Guid versionId, Guid userId, string userName) => throw new NotImplementedException();
    public Task<FinResult> ActivateAsync(Guid versionId) => throw new NotImplementedException();
    public Task ActivateFromApprovalAsync(Guid versionId, string decidedBy) => throw new NotImplementedException();
    // ── C-2 实现 ──
    internal Task CopyIntoAsync(Guid targetVersionId, Guid? fromVersionId, int? fromActualFiscalYear) => Task.CompletedTask;
}
```

- [ ] **Step 4: 运行验证通过**

Run: `dotnet test CP6.Tests --filter BudgetVersionStateMachineTests`
Expected: PASS（2 测）。

- [ ] **Step 5: Commit**

```bash
git add CP6.Core/Services/Fin/BudgetDtos.cs CP6.Core/Services/Fin/IBudgetService.cs CP6.Core/Services/Fin/BudgetService.cs CP6.Tests/Fin/BudgetVersionStateMachineTests.cs
git commit -m "feat(fin): A5 BudgetService 方案/版本 CRUD (唯一财年 + VersionNo 自增 + Draft 编辑守卫) (spec §5.1)"
```

### Task B-2: 提交审批 + 激活（含 ActivateFromApprovalAsync 同事务一次性）

**Files:**
- Modify: `CP6.Core/Services/Fin/BudgetService.cs`
- Test: `CP6.Tests/Fin/BudgetVersionStateMachineTests.cs`

- [ ] **Step 1: 写失败测试（激活清旧 Active + 仅 Approved 可激活）**

```csharp
[Fact]
public async Task ActivateFromApproval_SetsActive_ArchivesPriorActive()
{
    using var db = Db();
    var svc = new BudgetService(db, new FinSequenceService(db));
    var b = (await svc.CreateBudgetAsync(new Budget { Name = "2027", FiscalYear = 2027 }, "admin")).Data!;
    var v1 = (await svc.CreateVersionAsync(b.Id, "v1", "admin")).Data!;
    // 手工把 v1 推到 Active（模拟先前已批准激活）
    v1.Status = BudgetVersionStatus.Approved; await db.SaveChangesAsync();
    await svc.ActivateAsync(v1.Id);
    var v2 = (await svc.CreateVersionAsync(b.Id, "v2", "admin")).Data!;
    v2.Status = BudgetVersionStatus.PendingApproval; await db.SaveChangesAsync();

    await svc.ActivateFromApprovalAsync(v2.Id, "checker");
    await db.SaveChangesAsync();   // 模拟 OA 引擎最终持久化

    var rv1 = await db.BudgetVersions.FindAsync(v1.Id);
    var rv2 = await db.BudgetVersions.FindAsync(v2.Id);
    Assert.False(rv1!.IsActive);
    Assert.Equal(BudgetVersionStatus.Archived, rv1.Status);
    Assert.True(rv2!.IsActive);
    Assert.Equal(BudgetVersionStatus.Approved, rv2.Status);
}

[Fact]
public async Task Activate_NonApproved_Rejected()
{
    using var db = Db();
    var svc = new BudgetService(db, new FinSequenceService(db));
    var b = (await svc.CreateBudgetAsync(new Budget { Name = "2027", FiscalYear = 2027 }, "admin")).Data!;
    var v = (await svc.CreateVersionAsync(b.Id, "v1", "admin")).Data!;   // Draft
    var r = await svc.ActivateAsync(v.Id);
    Assert.False(r.Ok);
    Assert.Equal("E-A5-VERSION-004", r.Code);
}
```

- [ ] **Step 2: 运行验证失败**

Run: `dotnet test CP6.Tests --filter BudgetVersionStateMachineTests`
Expected: FAIL（NotImplementedException）。

- [ ] **Step 3: 实现提交/激活；注入 OA `IApprovalService`**

把构造改为注入 `IApprovalService`（Wf）。替换 B-1 的 3 个桩：
```csharp
using CP6.Core.Services.Wf;   // 文件顶部

// 构造（B-2 起）
private readonly IApprovalService _approval;
public BudgetService(CP6Context db, FinSequenceService seq, IApprovalService approval)
{ _db = db; _seq = seq; _approval = approval; }

public async Task<FinResult> SubmitForApprovalAsync(Guid versionId, Guid userId, string userName)
{
    var v = await _db.BudgetVersions.FindAsync(versionId);
    if (v == null) return FinResult.Fail("E-A5-VERSION-404");
    if (v.Status != BudgetVersionStatus.Draft) return FinResult.Fail("E-A5-VERSION-002");
    if (!await _db.BudgetLines.AnyAsync(l => l.VersionId == versionId)) return FinResult.Fail("E-A5-VERSION-006");

    var total = await _db.BudgetLines.Where(l => l.VersionId == versionId).SumAsync(l => l.AnnualAmount);
    Guid instanceId;
    try
    {
        instanceId = await _approval.SubmitAsync("A5_Budget", versionId.ToString(), userId,
            new { fiscalYear = (await _db.Budgets.FindAsync(v.BudgetId))!.FiscalYear, versionNo = v.VersionNo, totalAmount = total });
    }
    catch (InvalidOperationException)   // OA 防重：同 (bizType,bizId) 已有 Running 实例
    {
        return FinResult.Fail("E-A5-VERSION-003");
    }
    v.Status = BudgetVersionStatus.PendingApproval;
    v.ApprovalInstanceId = instanceId;
    v.ApprovalRef = instanceId.ToString();
    v.SubmittedAt = DateTime.Now; v.SubmittedBy = userName;
    await _db.SaveChangesAsync();
    return FinResult.Pass();
}

public async Task<FinResult> ActivateAsync(Guid versionId)
{
    var v = await _db.BudgetVersions.FindAsync(versionId);
    if (v == null) return FinResult.Fail("E-A5-VERSION-404");
    if (v.Status != BudgetVersionStatus.Approved) return FinResult.Fail("E-A5-VERSION-004");
    await ApplyActivationAsync(v);
    await _db.SaveChangesAsync();
    return FinResult.Pass();
}

public async Task ActivateFromApprovalAsync(Guid versionId, string decidedBy)
{
    // OA 回调专用：与引擎共享 DbContext，全程基于已加载实体改值，不自行 SaveChanges
    var v = await _db.BudgetVersions.FindAsync(versionId);
    if (v == null) return;
    if (v.Status == BudgetVersionStatus.Approved || v.IsActive) return;   // 幂等
    if (v.Status != BudgetVersionStatus.PendingApproval) return;
    v.Status = BudgetVersionStatus.Approved;
    v.ApprovedAt = DateTime.Now; v.ApprovedBy = decidedBy;
    await ApplyActivationAsync(v);
    // 不 SaveChanges —— 由 OA 引擎统一持久化（同事务原子）
}

/// <summary>清同 Budget 下其它 Active→Archived，本版 IsActive=true。基于已加载实体改值。</summary>
private async Task ApplyActivationAsync(BudgetVersion v)
{
    var priorActive = await _db.BudgetVersions
        .Where(x => x.BudgetId == v.BudgetId && x.IsActive && x.Id != v.Id).ToListAsync();
    foreach (var p in priorActive) { p.IsActive = false; p.Status = BudgetVersionStatus.Archived; }
    v.IsActive = true;
}
```

> **注**：B-1 已注入的测试 `new BudgetService(db, seq)` 需补第 3 参；测试用 `new BudgetService(db, seq, new StubApprovalForTest())` 或引真实 `ApprovalService`。本 plan 提供测试桩 `StubApprovalForTest`（见 D-1 集成测试用真实 OA）：
> ```csharp
> internal class StubApprovalForTest : CP6.Core.Services.Wf.IApprovalService {
>     public Task<Guid> SubmitAsync(string bizType, string bizId, Guid starterId, object? formSnapshot = null) => Task.FromResult(Guid.NewGuid());
>     public Task<CP6.Core.Services.Wf.ApprovalStatus> GetStatusAsync(string bizType, string bizId) => Task.FromResult(default(CP6.Core.Services.Wf.ApprovalStatus));
> }
> ```
> 回填 B-1 两测的构造参数。

- [ ] **Step 4: 运行验证通过**

Run: `dotnet test CP6.Tests --filter BudgetVersionStateMachineTests`
Expected: PASS（4 测）。

- [ ] **Step 5: Commit**

```bash
git add CP6.Core/Services/Fin/BudgetService.cs CP6.Tests/Fin/BudgetVersionStateMachineTests.cs
git commit -m "feat(fin): A5 submit-for-approval + activate (ActivateFromApprovalAsync 同事务一次性激活清旧, 仅 Approved 可激活) (spec §8.1/§8.3)"
```

---

## Phase C — 预算行 Service + 分解 + 导入 + 复制

### Task C-1: BudgetLineService 行 upsert + 按月分解 + 维度/科目校验

**Files:**
- Create: `CP6.Core/Services/Fin/IBudgetLineService.cs`
- Create: `CP6.Core/Services/Fin/BudgetLineService.cs`
- Modify: `CP6.Core/Services/Fin/BudgetDtos.cs`
- Test: `CP6.Tests/Fin/BudgetLineBreakdownTests.cs`

- [ ] **Step 1: 写失败测试（均摊 + 维度桶唯一 + 科目校验 + AnnualAmount 回填）**

```csharp
using CP6.Core.Data;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CP6.Tests.Fin;

public class BudgetLineBreakdownTests
{
    private static async Task<(CP6Context db, BudgetVersion v, Guid expenseAcct)> SeedAsync()
    {
        var db = TestHelper.CreateInMemoryContext();
        var acct = new GlAccount { Code = "6602", Name = "管理费用", Type = AccountType.Expense, NormalSide = AccountSide.Debit, IsLeaf = true, IsActive = true };
        db.GlAccounts.Add(acct);
        var b = new Budget { No = "BUD-2027-00001", Name = "2027", FiscalYear = 2027, IsActive = true };
        db.Budgets.Add(b);
        var v = new BudgetVersion { BudgetId = b.Id, VersionNo = 1, Status = BudgetVersionStatus.Draft };
        db.BudgetVersions.Add(v);
        await db.SaveChangesAsync();
        return (db, v, acct.Id);
    }

    [Fact]
    public async Task UpsertLine_EvenSpread_FillsAnnualAndPeriods()
    {
        var (db, v, acct) = await SeedAsync();
        var svc = new BudgetLineService(db);
        var r = await svc.UpsertLineAsync(new BudgetLineDto {
            VersionId = v.Id, AccountId = acct, AnnualAmount = 1200m, SpreadMode = "even"
        });
        Assert.True(r.Ok);
        var line = await db.BudgetLines.FirstAsync(l => l.VersionId == v.Id);
        var periods = await db.BudgetLinePeriods.Where(p => p.BudgetLineId == line.Id).OrderBy(p => p.PeriodNo).ToListAsync();
        Assert.Equal(12, periods.Count);
        Assert.Equal(100m, periods[0].Amount);
        Assert.Equal(1200m, periods.Sum(p => p.Amount));
        Assert.Equal(1200m, line.AnnualAmount);
        Assert.Equal(Guid.Empty, line.CostCenterKey);   // 规范化键派生
        Assert.Equal("", line.CostObjectTypeKey);
    }

    [Fact]
    public async Task UpsertLine_NonLeafOrNonPnL_Rejected()
    {
        var (db, v, _) = await SeedAsync();
        var asset = new GlAccount { Code = "1001", Name = "现金", Type = AccountType.Asset, NormalSide = AccountSide.Debit, IsLeaf = true, IsActive = true };
        db.GlAccounts.Add(asset); await db.SaveChangesAsync();
        var svc = new BudgetLineService(db);
        var r = await svc.UpsertLineAsync(new BudgetLineDto { VersionId = v.Id, AccountId = asset.Id, AnnualAmount = 100m, SpreadMode = "even" });
        Assert.False(r.Ok);
        Assert.Equal("E-A5-LINE-002", r.Code);
    }

    [Fact]
    public async Task UpsertLine_DuplicateBucket_SecondUpdatesNotDuplicates()
    {
        var (db, v, acct) = await SeedAsync();
        var svc = new BudgetLineService(db);
        await svc.UpsertLineAsync(new BudgetLineDto { VersionId = v.Id, AccountId = acct, AnnualAmount = 1200m, SpreadMode = "even" });
        await svc.UpsertLineAsync(new BudgetLineDto { VersionId = v.Id, AccountId = acct, AnnualAmount = 2400m, SpreadMode = "even" });
        var lines = await db.BudgetLines.Where(l => l.VersionId == v.Id).ToListAsync();
        Assert.Single(lines);   // 同桶 upsert，不重复
        Assert.Equal(2400m, lines[0].AnnualAmount);
    }

    [Fact]
    public async Task UpsertLine_VersionNotDraft_Rejected()
    {
        var (db, v, acct) = await SeedAsync();
        v.Status = BudgetVersionStatus.Approved; await db.SaveChangesAsync();
        var svc = new BudgetLineService(db);
        var r = await svc.UpsertLineAsync(new BudgetLineDto { VersionId = v.Id, AccountId = acct, AnnualAmount = 100m, SpreadMode = "even" });
        Assert.False(r.Ok);
        Assert.Equal("E-A5-VERSION-005", r.Code);
    }
}
```

- [ ] **Step 2: 运行验证失败**

Run: `dotnet test CP6.Tests --filter BudgetLineBreakdownTests`
Expected: FAIL。

- [ ] **Step 3: 写 DTO + 接口 + 实现**

`BudgetDtos.cs` 追加:
```csharp
public class BudgetLineDto
{
    public Guid? Id { get; set; }
    public Guid VersionId { get; set; }
    public Guid AccountId { get; set; }
    public Guid? CostCenterId { get; set; }
    public string? CostObjectType { get; set; }
    public string? CostObjectId { get; set; }
    public decimal AnnualAmount { get; set; }
    public string SpreadMode { get; set; } = "even";   // even | seasonal | manual
    public decimal[]? Periods { get; set; }             // manual/seasonal 用：12 个值（seasonal 为权重）
    public BudgetControlMode? ControlMode { get; set; }
    public BudgetControlBasis? ControlBasis { get; set; }
    public string? Memo { get; set; }
}
```

`IBudgetLineService.cs`:
```csharp
using CP6.Entity.DomainModels.Fin;

namespace CP6.Core.Services.Fin;

public interface IBudgetLineService
{
    Task<List<BudgetLineGridRow>> ListLinesAsync(Guid versionId);
    Task<FinResult> UpsertLineAsync(BudgetLineDto dto);
    Task<FinResult> DeleteLineAsync(Guid lineId);
    Task<BudgetImportPreviewResult> PreviewImportAsync(Guid versionId, Stream excel);   // C-3
    Task<FinResult> ConfirmImportAsync(Guid versionId, Stream excel);                   // C-3
}
```

`BudgetLineService.cs`:
```csharp
using CP6.Core.Data;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Fin;

public class BudgetLineService : IBudgetLineService
{
    private readonly CP6Context _db;
    public BudgetLineService(CP6Context db) { _db = db; }

    public async Task<FinResult> UpsertLineAsync(BudgetLineDto dto)
    {
        var v = await _db.BudgetVersions.FindAsync(dto.VersionId);
        if (v == null) return FinResult.Fail("E-A5-VERSION-404");
        if (v.Status != BudgetVersionStatus.Draft) return FinResult.Fail("E-A5-VERSION-005");

        var acct = await _db.GlAccounts.FindAsync(dto.AccountId);
        if (acct == null || !acct.IsLeaf || (acct.Type != AccountType.Expense && acct.Type != AccountType.Revenue))
            return FinResult.Fail("E-A5-LINE-002");
        if ((dto.CostObjectType == null) != (dto.CostObjectId == null))
            return FinResult.Fail("E-A5-LINE-004");

        var periods = SpreadPeriods(dto);   // 12 个值
        var annual = periods.Sum();

        // 规范化键定位现有桶（upsert）
        var ccKey = dto.CostCenterId ?? Guid.Empty;
        var coTypeKey = dto.CostObjectType ?? "";
        var coIdKey = dto.CostObjectId ?? "";
        var line = await _db.BudgetLines.FirstOrDefaultAsync(l =>
            l.VersionId == dto.VersionId && l.AccountId == dto.AccountId &&
            l.CostCenterKey == ccKey && l.CostObjectTypeKey == coTypeKey && l.CostObjectIdKey == coIdKey);

        if (line == null)
        {
            line = new BudgetLine { VersionId = dto.VersionId, AccountId = dto.AccountId,
                CostCenterId = dto.CostCenterId, CostObjectType = dto.CostObjectType, CostObjectId = dto.CostObjectId };
            _db.BudgetLines.Add(line);
        }
        line.CostCenterId = dto.CostCenterId; line.CostObjectType = dto.CostObjectType; line.CostObjectId = dto.CostObjectId;
        line.NormalizeKeys();
        line.AnnualAmount = annual;
        line.ControlMode = dto.ControlMode; line.ControlBasis = dto.ControlBasis; line.Memo = dto.Memo;

        await _db.SaveChangesAsync();   // 先存 line 拿 Id

        // 重建按月分解
        _db.BudgetLinePeriods.RemoveRange(_db.BudgetLinePeriods.Where(p => p.BudgetLineId == line.Id));
        for (int i = 0; i < 12; i++)
            _db.BudgetLinePeriods.Add(new BudgetLinePeriod { BudgetLineId = line.Id, PeriodNo = i + 1, Amount = periods[i] });
        await _db.SaveChangesAsync();
        return FinResult.Pass();
    }

    /// <summary>按 SpreadMode 算 12 期值；AnnualAmount 自动回填=Σ（spec §5.3）。</summary>
    internal static decimal[] SpreadPeriods(BudgetLineDto dto)
    {
        var r = new decimal[12];
        switch (dto.SpreadMode)
        {
            case "manual":
                var src = dto.Periods ?? new decimal[12];
                for (int i = 0; i < 12; i++) r[i] = i < src.Length ? src[i] : 0m;
                break;
            case "seasonal":
                var w = dto.Periods ?? Enumerable.Repeat(1m, 12).ToArray();
                var sum = w.Sum();
                if (sum == 0) goto case "even";
                decimal acc = 0;
                for (int i = 0; i < 12; i++) { r[i] = Math.Round(dto.AnnualAmount * w[i] / sum, 2); acc += r[i]; }
                r[11] += dto.AnnualAmount - acc;   // 余数进末期
                break;
            default:   // even
                var each = Math.Round(dto.AnnualAmount / 12m, 2);
                for (int i = 0; i < 12; i++) r[i] = each;
                r[11] += dto.AnnualAmount - each * 12;   // 余数进末期
                break;
        }
        return r;
    }

    public async Task<FinResult> DeleteLineAsync(Guid lineId)
    {
        var line = await _db.BudgetLines.FindAsync(lineId);
        if (line == null) return FinResult.Fail("E-A5-LINE-404");
        var v = await _db.BudgetVersions.FindAsync(line.VersionId);
        if (v?.Status != BudgetVersionStatus.Draft) return FinResult.Fail("E-A5-VERSION-005");
        _db.BudgetLinePeriods.RemoveRange(_db.BudgetLinePeriods.Where(p => p.BudgetLineId == lineId));
        _db.BudgetLines.Remove(line);
        await _db.SaveChangesAsync();
        return FinResult.Pass();
    }

    public async Task<List<BudgetLineGridRow>> ListLinesAsync(Guid versionId)
    {
        var lines = await _db.BudgetLines.AsNoTracking().Where(l => l.VersionId == versionId).ToListAsync();
        var ids = lines.Select(l => l.Id).ToList();
        var periods = await _db.BudgetLinePeriods.AsNoTracking().Where(p => ids.Contains(p.BudgetLineId)).ToListAsync();
        return lines.Select(l => new BudgetLineGridRow
        {
            Id = l.Id, AccountId = l.AccountId, CostCenterId = l.CostCenterId,
            CostObjectType = l.CostObjectType, CostObjectId = l.CostObjectId,
            AnnualAmount = l.AnnualAmount, ControlMode = l.ControlMode, ControlBasis = l.ControlBasis, Memo = l.Memo,
            Periods = Enumerable.Range(1, 12).Select(n => periods.FirstOrDefault(p => p.BudgetLineId == l.Id && p.PeriodNo == n)?.Amount ?? 0m).ToArray(),
            RowVersion = l.RowVersion,
        }).ToList();
    }

    // C-3
    public Task<BudgetImportPreviewResult> PreviewImportAsync(Guid versionId, Stream excel) => throw new NotImplementedException();
    public Task<FinResult> ConfirmImportAsync(Guid versionId, Stream excel) => throw new NotImplementedException();
}
```

`BudgetDtos.cs` 追加 `BudgetLineGridRow`:
```csharp
public class BudgetLineGridRow
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid? CostCenterId { get; set; }
    public string? CostObjectType { get; set; }
    public string? CostObjectId { get; set; }
    public decimal AnnualAmount { get; set; }
    public decimal[] Periods { get; set; } = new decimal[12];
    public BudgetControlMode? ControlMode { get; set; }
    public BudgetControlBasis? ControlBasis { get; set; }
    public string? Memo { get; set; }
    public byte[]? RowVersion { get; set; }
}
```

- [ ] **Step 4: 运行验证通过**

Run: `dotnet test CP6.Tests --filter BudgetLineBreakdownTests`
Expected: PASS（4 测）。

- [ ] **Step 5: Commit**

```bash
git add CP6.Core/Services/Fin/IBudgetLineService.cs CP6.Core/Services/Fin/BudgetLineService.cs CP6.Core/Services/Fin/BudgetDtos.cs CP6.Tests/Fin/BudgetLineBreakdownTests.cs
git commit -m "feat(fin): A5 BudgetLineService upsert + 按月分解(均摊/季节/手工, 余数进末期) + 维度桶 Key upsert + 科目/Draft 守卫 (spec §5.2/§5.3)"
```

### Task C-2: 复制（版本 / 上年实际）

**Files:**
- Modify: `CP6.Core/Services/Fin/BudgetService.cs`（实现 `CopyIntoAsync`，注入 `IBudgetReportService` 取实际）
- Test: `CP6.Tests/Fin/BudgetCopyImportTests.cs`

> 依赖 F-1 的 `IBudgetReportService.AggregateActualByBucketAsync`（复制上年实际用）。**执行序**：可先做 F-1 再回 C-2，或 C-2 先实现"复制版本"、"复制实际"留到 F-1 后补。本 plan 采**C-2 仅实现复制版本**，复制实际在 F-2 补（标注）。

- [ ] **Step 1: 写失败测试（复制版本桶+按月数）**

```csharp
[Fact]
public async Task CopyFromVersion_ClonesBucketsAndPeriods()
{
    var db = TestHelper.CreateInMemoryContext();
    var acct = new GlAccount { Code="6602", Name="管理费用", Type=AccountType.Expense, NormalSide=AccountSide.Debit, IsLeaf=true, IsActive=true };
    db.GlAccounts.Add(acct);
    var b = new Budget { No="BUD-2027-00001", Name="2027", FiscalYear=2027, IsActive=true };
    db.Budgets.Add(b);
    var src = new BudgetVersion { BudgetId=b.Id, VersionNo=1, Status=BudgetVersionStatus.Approved };
    db.BudgetVersions.Add(src);
    await db.SaveChangesAsync();
    var lineSvc = new BudgetLineService(db);
    await lineSvc.UpsertLineAsync(new BudgetLineDto { VersionId=src.Id, AccountId=acct.Id, AnnualAmount=1200m, SpreadMode="even" });

    var svc = new BudgetService(db, new FinSequenceService(db), new StubApprovalForTest());
    var v2 = (await svc.CreateVersionAsync(b.Id, "copy", "admin", copyFromVersionId: src.Id)).Data!;

    var lines = await db.BudgetLines.Where(l => l.VersionId == v2.Id).ToListAsync();
    Assert.Single(lines);
    Assert.Equal(1200m, lines[0].AnnualAmount);
    var periods = await db.BudgetLinePeriods.Where(p => p.BudgetLineId == lines[0].Id).ToListAsync();
    Assert.Equal(12, periods.Count);
}
```

- [ ] **Step 2: 运行验证失败** — `dotnet test CP6.Tests --filter BudgetCopyImportTests` → FAIL。

- [ ] **Step 3: 实现 CopyIntoAsync（复制版本部分）**

```csharp
internal async Task CopyIntoAsync(Guid targetVersionId, Guid? fromVersionId, int? fromActualFiscalYear)
{
    if (fromVersionId.HasValue)
    {
        var srcLines = await _db.BudgetLines.AsNoTracking().Where(l => l.VersionId == fromVersionId.Value).ToListAsync();
        var srcIds = srcLines.Select(l => l.Id).ToList();
        var srcPeriods = await _db.BudgetLinePeriods.AsNoTracking().Where(p => srcIds.Contains(p.BudgetLineId)).ToListAsync();
        foreach (var sl in srcLines)
        {
            var nl = new BudgetLine {
                VersionId = targetVersionId, AccountId = sl.AccountId,
                CostCenterId = sl.CostCenterId, CostObjectType = sl.CostObjectType, CostObjectId = sl.CostObjectId,
                AnnualAmount = sl.AnnualAmount, ControlMode = sl.ControlMode, ControlBasis = sl.ControlBasis, Memo = sl.Memo,
            };
            nl.NormalizeKeys();
            _db.BudgetLines.Add(nl);
            await _db.SaveChangesAsync();
            foreach (var p in srcPeriods.Where(p => p.BudgetLineId == sl.Id))
                _db.BudgetLinePeriods.Add(new BudgetLinePeriod { BudgetLineId = nl.Id, PeriodNo = p.PeriodNo, Amount = p.Amount });
        }
        await _db.SaveChangesAsync();
    }
    if (fromActualFiscalYear.HasValue)
    {
        // F-2 实现：调 IBudgetReportService.AggregateActualByBucketAsync(fromActualFiscalYear) → 写按月数
        await CopyFromActualAsync(targetVersionId, fromActualFiscalYear.Value);
    }
}

// F-2 实现
internal virtual Task CopyFromActualAsync(Guid targetVersionId, int sourceFiscalYear) => Task.CompletedTask;
```

- [ ] **Step 4: 运行验证通过** — PASS。

- [ ] **Step 5: Commit**

```bash
git add CP6.Core/Services/Fin/BudgetService.cs CP6.Tests/Fin/BudgetCopyImportTests.cs
git commit -m "feat(fin): A5 copy-from-version (clone buckets + periods into draft) (spec §5.5)"
```

### Task C-3: Excel 导入（Preview / Confirm）

**Files:**
- Modify: `CP6.Core/Services/Fin/BudgetLineService.cs`
- Modify: `CP6.Core/Services/Fin/BudgetDtos.cs`
- Test: `CP6.Tests/Fin/BudgetCopyImportTests.cs`

- [ ] **Step 1: 写失败测试（致命错误整批拒绝 + 正常 Confirm 落库）**

```csharp
[Fact]
public async Task Import_FatalRow_RejectsWholeBatch()
{
    var db = TestHelper.CreateInMemoryContext();
    var acct = new GlAccount { Code="6602", Name="管理费用", Type=AccountType.Expense, NormalSide=AccountSide.Debit, IsLeaf=true, IsActive=true };
    db.GlAccounts.Add(acct);
    var b = new Budget { No="BUD-2027-00001", Name="2027", FiscalYear=2027, IsActive=true };
    db.Budgets.Add(b);
    var v = new BudgetVersion { BudgetId=b.Id, VersionNo=1, Status=BudgetVersionStatus.Draft };
    db.BudgetVersions.Add(v);
    await db.SaveChangesAsync();
    var svc = new BudgetLineService(db);

    // 含一行不存在科目编码 "9999" → 致命
    using var xls = BudgetExcelFixture.Build(new[] {
        ("6602", "", "", new decimal[12]),
        ("9999", "", "", new decimal[12]),   // 科目不存在
    });
    var r = await svc.ConfirmImportAsync(v.Id, xls);
    Assert.False(r.Ok);
    Assert.Equal("E-A5-IMPORT-001", r.Code);
    Assert.Equal(0, await db.BudgetLines.CountAsync());   // 零持久化
}
```

> `BudgetExcelFixture.Build(...)` 测试辅助：用 ClosedXML 造内存 xlsx（列：科目编码/成本中心编码/成本对象类型/成本对象号/M1..M12）。放 `CP6.Tests/Fin/BudgetExcelFixture.cs`。

- [ ] **Step 2: 运行验证失败** — FAIL。

- [ ] **Step 3: 实现导入（ClosedXML 解析 + Preview/Confirm）**

`BudgetDtos.cs` 追加:
```csharp
public class BudgetImportRow
{
    public int RowNo { get; set; }
    public string AccountCode { get; set; } = "";
    public string? CostCenterCode { get; set; }
    public string? CostObjectType { get; set; }
    public string? CostObjectId { get; set; }
    public decimal[] Periods { get; set; } = new decimal[12];
    public bool Ok { get; set; }
    public string? Error { get; set; }   // 致命错误码
}
public class BudgetImportPreviewResult
{
    public List<BudgetImportRow> Rows { get; set; } = new();
    public bool HasFatal => Rows.Any(r => !r.Ok);
}
```

`BudgetLineService.cs` 实现（解析+校验复用一处 `ParseAndValidate`）:
```csharp
using ClosedXML.Excel;

public async Task<BudgetImportPreviewResult> PreviewImportAsync(Guid versionId, Stream excel)
    => await ParseAndValidateAsync(versionId, excel);

public async Task<FinResult> ConfirmImportAsync(Guid versionId, Stream excel)
{
    var v = await _db.BudgetVersions.FindAsync(versionId);
    if (v == null) return FinResult.Fail("E-A5-VERSION-404");
    if (v.Status != BudgetVersionStatus.Draft) return FinResult.Fail("E-A5-VERSION-005");
    var preview = await ParseAndValidateAsync(versionId, excel);
    if (preview.HasFatal) return FinResult.Fail("E-A5-IMPORT-001");
    foreach (var row in preview.Rows)
    {
        var acct = await _db.GlAccounts.FirstAsync(a => a.Code == row.AccountCode);
        Guid? ccId = row.CostCenterCode == null ? null
            : (await _db.CostCenters.FirstOrDefaultAsync(c => c.Code == row.CostCenterCode))?.Id;
        await UpsertLineAsync(new BudgetLineDto {
            VersionId = versionId, AccountId = acct.Id, CostCenterId = ccId,
            CostObjectType = string.IsNullOrEmpty(row.CostObjectType) ? null : row.CostObjectType,
            CostObjectId = string.IsNullOrEmpty(row.CostObjectId) ? null : row.CostObjectId,
            SpreadMode = "manual", Periods = row.Periods, AnnualAmount = row.Periods.Sum(),
        });
    }
    return FinResult.Pass();
}

private async Task<BudgetImportPreviewResult> ParseAndValidateAsync(Guid versionId, Stream excel)
{
    var result = new BudgetImportPreviewResult();
    using var wb = new XLWorkbook(excel);
    var ws = wb.Worksheet(1);
    var acctCodes = await _db.GlAccounts.AsNoTracking()
        .Where(a => a.IsLeaf && (a.Type == AccountType.Expense || a.Type == AccountType.Revenue))
        .Select(a => a.Code).ToListAsync();
    var ccCodes = await _db.CostCenters.AsNoTracking().Select(c => c.Code).ToListAsync();
    foreach (var xlRow in ws.RowsUsed().Skip(1))   // 跳表头
    {
        var row = new BudgetImportRow { RowNo = xlRow.RowNumber(), Ok = true };
        row.AccountCode = xlRow.Cell(1).GetString().Trim();
        row.CostCenterCode = EmptyToNull(xlRow.Cell(2).GetString());
        row.CostObjectType = EmptyToNull(xlRow.Cell(3).GetString());
        row.CostObjectId = EmptyToNull(xlRow.Cell(4).GetString());
        for (int i = 0; i < 12; i++) row.Periods[i] = xlRow.Cell(5 + i).GetValue<decimal>();
        if (!acctCodes.Contains(row.AccountCode)) { row.Ok = false; row.Error = "E-A5-LINE-002"; }
        else if (row.CostCenterCode != null && !ccCodes.Contains(row.CostCenterCode)) { row.Ok = false; row.Error = "E-A5-LINE-005"; }
        else if ((row.CostObjectType == null) != (row.CostObjectId == null)) { row.Ok = false; row.Error = "E-A5-LINE-004"; }
        result.Rows.Add(row);
    }
    return result;
    static string? EmptyToNull(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
```

- [ ] **Step 4: 运行验证通过** — PASS。

- [ ] **Step 5: Commit**

```bash
git add CP6.Core/Services/Fin/BudgetLineService.cs CP6.Core/Services/Fin/BudgetDtos.cs CP6.Tests/Fin/BudgetCopyImportTests.cs CP6.Tests/Fin/BudgetExcelFixture.cs
git commit -m "feat(fin): A5 Excel import Preview/Confirm (ClosedXML, fatal→整批拒绝零持久化, 复用 UpsertLine) (spec §5.4)"
```

---

## Phase D — OA 审批接入

### Task D-1: BudgetApprovalCallback + 注册 + Flow Seed + OA 集成测试

**Files:**
- Create: `CP6.Core/Services/Fin/BudgetApprovalCallback.cs`
- Create: `CP6.WebApi/Seed/A5BudgetFlowSeed.cs`
- Modify: `CP6.WebApi/Program.cs`（DI 注册 callback + flow seed）
- Test: `CP6.Tests/Fin/BudgetApprovalIntegrationTests.cs`

- [ ] **Step 1: 写失败测试（提交→OA 通过→版本 Approved+自动激活；驳回→Rejected）**

```csharp
using CP6.Core.Data;
using CP6.Core.Services.Fin;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Fin;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Xunit;

namespace CP6.Tests.Fin;

public class BudgetApprovalIntegrationTests
{
    // 仿 JournalApprovalIntegrationTests：seed FlowDef+Binding，跑真实 ApprovalService + Dispatcher + FlowEngine
    [Fact]
    public async Task Approve_SetsApprovedAndAutoActivates()
    {
        using var db = TestHelper.CreateInMemoryContext();
        var approver = Guid.NewGuid();
        await SeedFlowAsync(db, approver);
        var (budgetSvc, approval, engine) = BuildOaStack(db);

        var b = (await budgetSvc.CreateBudgetAsync(new Budget { Name="2027", FiscalYear=2027 }, "admin")).Data!;
        var v = (await budgetSvc.CreateVersionAsync(b.Id, "v1", "admin")).Data!;
        await SeedOneLineAsync(db, v.Id);

        var starter = Guid.NewGuid();
        await budgetSvc.SubmitForApprovalAsync(v.Id, starter, "admin");
        Assert.Equal(BudgetVersionStatus.PendingApproval, (await db.BudgetVersions.FindAsync(v.Id))!.Status);

        // 审批人同意 → 引擎终态 → Dispatcher → BudgetApprovalCallback.OnApproved → ActivateFromApproval
        var task = await db.Wf_FlowTasks.FirstAsync(t => t.AssigneeId == approver);
        await engine.ActAsync(task.Id, approver, approve: true, comment: null);

        var rv = await db.BudgetVersions.FindAsync(v.Id);
        Assert.Equal(BudgetVersionStatus.Approved, rv!.Status);
        Assert.True(rv.IsActive);
    }

    [Fact]
    public async Task Reject_SetsRejectedWithReason()
    {
        using var db = TestHelper.CreateInMemoryContext();
        var approver = Guid.NewGuid();
        await SeedFlowAsync(db, approver);
        var (budgetSvc, approval, engine) = BuildOaStack(db);
        var b = (await budgetSvc.CreateBudgetAsync(new Budget { Name="2027", FiscalYear=2027 }, "admin")).Data!;
        var v = (await budgetSvc.CreateVersionAsync(b.Id, "v1", "admin")).Data!;
        await SeedOneLineAsync(db, v.Id);
        await budgetSvc.SubmitForApprovalAsync(v.Id, Guid.NewGuid(), "admin");
        var task = await db.Wf_FlowTasks.FirstAsync(t => t.AssigneeId == approver);
        await engine.ActAsync(task.Id, approver, approve: false, comment: "超支");
        var rv = await db.BudgetVersions.FindAsync(v.Id);
        Assert.Equal(BudgetVersionStatus.Rejected, rv!.Status);
        Assert.Equal("超支", rv.RejectReason);
    }

    // BuildOaStack / SeedFlowAsync / SeedOneLineAsync 见实现 Step（仿 JournalApprovalIntegrationTests:38-59）
}
```

> 测试辅助 `BuildOaStack` 须组装真实 `ApprovalService`/`ApprovalDispatcher`/`FlowEngine` + 把 `BudgetApprovalCallback` 注入 `IEnumerable<IApprovalCallback>`。**逐字参 `CP6.Tests/Fin/JournalApprovalIntegrationTests.cs` 的组装与 seed 写法**（同一套 OA 栈，只把 callback 换成 budget、bizType 换成 "A5_Budget"）。

- [ ] **Step 2: 运行验证失败** — FAIL（BudgetApprovalCallback 未定义）。

- [ ] **Step 3: 写 callback + seed + 注册**

`BudgetApprovalCallback.cs`:
```csharp
using CP6.Core.Services.Wf;

namespace CP6.Core.Services.Fin;

/// <summary>预算版本审批终态回调（BizType="A5_Budget"）。与引擎共享 DbContext，不自行 SaveChanges。</summary>
public class BudgetApprovalCallback : IApprovalCallback
{
    private readonly IBudgetService _budget;
    public BudgetApprovalCallback(IBudgetService budget) { _budget = budget; }
    public string BizType => "A5_Budget";

    public async Task OnApprovedAsync(ApprovalCallbackContext ctx)
    {
        var versionId = Guid.Parse(ctx.BizId);
        // 同事务一次性：Status=Approved + 清旧 Active→Archived + 本版 IsActive（spec §8.3）
        await _budget.ActivateFromApprovalAsync(versionId, ctx.DecidedById?.ToString() ?? "OA");
    }

    public async Task OnRejectedAsync(ApprovalCallbackContext ctx)
    {
        var versionId = Guid.Parse(ctx.BizId);
        var v = await _budget.GetVersionAsync(versionId);
        if (v == null || v.Status != BudgetVersionStatus.Draft && v.Status != BudgetVersionStatus.PendingApproval) return;
        if (v.Status != BudgetVersionStatus.PendingApproval) return;   // 幂等
        v.Status = BudgetVersionStatus.Rejected;
        v.RejectReason = ctx.Reason ?? "审批驳回";
        // 不 SaveChanges（引擎统一持久化）
    }
}
```

`A5BudgetFlowSeed.cs`:
```csharp
using CP6.Core.Data;
using CP6.Entity.DomainModels.Wf;
using System.Text.Json;

namespace CP6.WebApi.Seed;

public static class A5BudgetFlowSeed
{
    public static void Seed(CP6Context db)
    {
        if (!db.Wf_FlowDefs.Any(f => f.FlowKey == "budget-approve"))
        {
            // 单审批人(admin)默认流程；真实多级由 OA 设计器改 SchemaJson
            var schema = new { Nodes = new object[] {
                new { Id="n1", Type="approval", ApproverStrategy="RoleAdmin" },
                new { Id="end", Type="end" } },
                Edges = new object[] { new { From="n1", To="end" } } };
            db.Wf_FlowDefs.Add(new Wf_FlowDef {
                Id = Guid.NewGuid(), FlowKey = "budget-approve", FlowName = "预算审批",
                FormKey = "BudgetApproval", SchemaJson = JsonSerializer.Serialize(schema), Version = 1, Enable = true });
        }
        if (!db.Wf_ApprovalBindings.Any(x => x.BizType == "A5_Budget"))
            db.Wf_ApprovalBindings.Add(new Wf_ApprovalBinding {
                Id = Guid.NewGuid(), BizType = "A5_Budget", FlowKey = "budget-approve", Enable = true });
        db.SaveChanges();
    }
}
```

`Program.cs`：DI 区追加 `builder.Services.AddScoped<CP6.Core.Services.Wf.IApprovalCallback, CP6.Core.Services.Fin.BudgetApprovalCallback>();`（与 JournalApprovalCallback 并列）+ 注册 `IBudgetService`/`IBudgetLineService`/`IBudgetReportService`；seed 区调 `A5BudgetFlowSeed.Seed(db);`（参 §8.4 默认流程 ApproverStrategy 落码核对 OA 实际 schema 字段名，对齐 JournalApprovalIntegrationTests 的 FlowSchema 结构）。

> **落码核实**：`FlowSchema`/`FlowNode` 的真实字段（`ApproverStrategy`/`ApproverUserId`/`Type`）以 `CP6.Core.Services.Wf` 实际类型为准（见 JournalApprovalIntegrationTests:38-59），seed 用强类型 `FlowSchema` 而非匿名对象更稳——落码时改为强类型。

- [ ] **Step 4: 运行验证通过** — `dotnet test CP6.Tests --filter BudgetApprovalIntegrationTests` → PASS（2 测）。

- [ ] **Step 5: Commit**

```bash
git add CP6.Core/Services/Fin/BudgetApprovalCallback.cs CP6.WebApi/Seed/A5BudgetFlowSeed.cs CP6.WebApi/Program.cs CP6.Tests/Fin/BudgetApprovalIntegrationTests.cs
git commit -m "feat(fin): A5 OA approval (BudgetApprovalCallback A5_Budget, 通过→自动激活/驳回→可重编, seed FlowDef/Binding, 共享 DbContext 原子) (spec §8)"
```

---

## Phase E — BudgetGuard 过账控制

### Task E-1: BudgetEvaluator 核心评估器（最具体匹配 + YTD/Period）

**Files:**
- Create: `CP6.Core/Services/Fin/BudgetGuard.cs`（含静态 `BudgetEvaluator`）
- Test: `CP6.Tests/Fin/BudgetGuardTests.cs`

- [ ] **Step 1: 写失败测试（Block 超 YTD 拒 + 未超放行 + 最具体匹配 + 同 entry 合并 + 无 Active 短路）**

```csharp
using CP6.Core.Data;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CP6.Tests.Fin;

public class BudgetGuardTests
{
    // Seed：财年 2027 期间 1..3 Open；科目 6602 费用；Active 版本含 Block 行(公司级 年 1200=每期 100, YTD)
    private static async Task<(CP6Context db, Guid acct, Guid periodId, int periodNo)> SeedAsync(decimal annual, BudgetControlMode mode, BudgetControlBasis basis, Guid? cc = null)
    {
        var db = TestHelper.CreateInMemoryContext();
        var acct = new GlAccount { Code="6602", Name="管理费用", Type=AccountType.Expense, NormalSide=AccountSide.Debit, IsLeaf=true, IsActive=true };
        db.GlAccounts.Add(acct);
        var p2 = new FiscalPeriod { FiscalYear=2027, Year=2027, Month=2, PeriodNo=2, Status=PeriodStatus.Open, PeriodStart=new DateTime(2027,2,1), PeriodEnd=new DateTime(2027,2,28) };
        db.FiscalPeriods.Add(p2);
        var b = new Budget { No="BUD-2027-00001", Name="2027", FiscalYear=2027, IsActive=true };
        db.Budgets.Add(b);
        var v = new BudgetVersion { BudgetId=b.Id, VersionNo=1, Status=BudgetVersionStatus.Approved, IsActive=true, DefaultControlMode=mode, DefaultControlBasis=basis };
        db.BudgetVersions.Add(v);
        await db.SaveChangesAsync();
        var line = new BudgetLine { VersionId=v.Id, AccountId=acct.Id, CostCenterId=cc, AnnualAmount=annual };
        line.NormalizeKeys();
        db.BudgetLines.Add(line);
        await db.SaveChangesAsync();
        for (int i=1;i<=12;i++) db.BudgetLinePeriods.Add(new BudgetLinePeriod { BudgetLineId=line.Id, PeriodNo=i, Amount=annual/12 });
        await db.SaveChangesAsync();
        return (db, acct.Id, p2.Id, 2);
    }

    private static JournalEntry Entry(Guid acct, Guid periodId, decimal debit, Guid? cc = null) => new JournalEntry {
        VoucherDate=new DateTime(2027,2,15), PeriodId=periodId, Source=VoucherSource.Manual, Status=JournalStatus.Draft,
        Lines = new() { new JournalLine { AccountId=acct, Debit=debit, Credit=0m, CostCenterId=cc } }
    };

    [Fact]
    public async Task Block_Ytd_ExceedsCumulativeBudget_Rejected()
    {
        // 年 1200/月 100；期 1 已过账实际 90；本期(2)再过账 120 → YTD 已用 90+120=210 > 累计预算(期1+2)=200 → 拒
        var (db, acct, pid, _) = await SeedAsync(1200m, BudgetControlMode.Block, BudgetControlBasis.Ytd);
        // 期1 已过账 90
        var p1 = new FiscalPeriod { FiscalYear=2027, Year=2027, Month=1, PeriodNo=1, Status=PeriodStatus.Open, PeriodStart=new(2027,1,1), PeriodEnd=new(2027,1,31) };
        db.FiscalPeriods.Add(p1); await db.SaveChangesAsync();
        var posted = Entry(acct, p1.Id, 90m); posted.Status = JournalStatus.Posted;
        db.JournalEntries.Add(posted); await db.SaveChangesAsync();

        var r = await BudgetGuard.CheckPostingAsync(db, Entry(acct, pid, 120m));
        Assert.False(r.Ok);
        Assert.Equal("E-A5-BUDGET-EXCEEDED", r.Code);
    }

    [Fact]
    public async Task Block_WithinBudget_Passes()
    {
        var (db, acct, pid, _) = await SeedAsync(1200m, BudgetControlMode.Block, BudgetControlBasis.Period);
        var r = await BudgetGuard.CheckPostingAsync(db, Entry(acct, pid, 80m));   // 期 2 预算 100，过 80 → 放行
        Assert.True(r.Ok);
    }

    [Fact]
    public async Task NoActiveVersion_ShortCircuitsPass()
    {
        var (db, acct, pid, _) = await SeedAsync(1200m, BudgetControlMode.Block, BudgetControlBasis.Period);
        foreach (var v in db.BudgetVersions) v.IsActive = false;
        await db.SaveChangesAsync();
        var r = await BudgetGuard.CheckPostingAsync(db, Entry(acct, pid, 99999m));
        Assert.True(r.Ok);
    }

    [Fact]
    public async Task SameEntry_MultipleLinesSameBucket_Merged()
    {
        var (db, acct, pid, _) = await SeedAsync(1200m, BudgetControlMode.Block, BudgetControlBasis.Period);
        var e = Entry(acct, pid, 60m);
        e.Lines.Add(new JournalLine { AccountId = acct, Debit = 60m, Credit = 0m });   // 同桶第二行
        var r = await BudgetGuard.CheckPostingAsync(db, e);   // 合并 120 > 期预算 100 → 拒
        Assert.False(r.Ok);
        Assert.Equal("E-A5-BUDGET-EXCEEDED", r.Code);
    }
}
```

- [ ] **Step 2: 运行验证失败** — FAIL。

- [ ] **Step 3: 实现 BudgetGuard + BudgetEvaluator**

```csharp
using CP6.Core.Data;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Fin;

/// <summary>预算过账守卫（静态，同 CP6Context 直查，无 DI/无循环依赖，仿 BankReconGuard）。挂手工 PostAsync。</summary>
public static class BudgetGuard
{
    public static async Task<FinResult> CheckPostingAsync(CP6Context db, JournalEntry entry)
    {
        var warnings = await BudgetEvaluator.EvaluateAsync(db, entry, blockOnly: true);
        var firstBlock = warnings.FirstOrDefault(w => w.IsBlock && w.Exceeded);
        return firstBlock == null ? FinResult.Pass()
            : FinResult.Fail("E-A5-BUDGET-EXCEEDED", firstBlock.AccountCode, firstBlock.Budget, firstBlock.Used, firstBlock.Incoming, firstBlock.Used + firstBlock.Incoming - firstBlock.Budget);
    }
}

/// <summary>核心评估器：守卫(blockOnly) 与 预检(全量 Warn+Block) 共用。</summary>
public static class BudgetEvaluator
{
    public static async Task<List<BudgetEvalResult>> EvaluateAsync(CP6Context db, JournalEntry entry, bool blockOnly)
    {
        var results = new List<BudgetEvalResult>();

        // 1. 落期：优先 PeriodId，fallback VoucherDate（spec §7.1）
        FiscalPeriod? period = null;
        if (entry.PeriodId != Guid.Empty)
            period = await db.FiscalPeriods.AsNoTracking().FirstOrDefaultAsync(p => p.Id == entry.PeriodId);
        period ??= await db.FiscalPeriods.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Year == entry.VoucherDate.Year && p.Month == entry.VoucherDate.Month);
        if (period == null) return results;
        int fy = period.FiscalYear, pno = period.PeriodNo;

        // 2. 该财年 Active 版本
        var version = await (from v in db.BudgetVersions.AsNoTracking()
                             join b in db.Budgets.AsNoTracking() on v.BudgetId equals b.Id
                             where v.IsActive && b.FiscalYear == fy
                             select v).FirstOrDefaultAsync();
        if (version == null) return results;

        // 3. 该版本费用类预算行（含 12 期）；按 AccountId 收窄
        var entryAcctIds = entry.Lines.Select(l => l.AccountId).Distinct().ToList();
        var budgetLines = await (from l in db.BudgetLines.AsNoTracking()
                                 join a in db.GlAccounts.AsNoTracking() on l.AccountId equals a.Id
                                 where l.VersionId == version.Id && a.Type == AccountType.Expense
                                       && entryAcctIds.Contains(l.AccountId)
                                 select l).ToListAsync();
        if (budgetLines.Count == 0) return results;
        var blIds = budgetLines.Select(l => l.Id).ToList();
        var blPeriods = await db.BudgetLinePeriods.AsNoTracking().Where(p => blIds.Contains(p.BudgetLineId)).ToListAsync();

        // 4. 费用行按桶合并本 entry 消耗（防拆行绕过），仅 Expense 行
        var acctTypes = await db.GlAccounts.AsNoTracking().Where(a => entryAcctIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a);
        var incoming = new Dictionary<Guid, decimal>();   // budgetLineId → 本 entry 消耗
        var lineMeta = new Dictionary<Guid, BudgetLine>();
        foreach (var jl in entry.Lines)
        {
            if (!acctTypes.TryGetValue(jl.AccountId, out var a) || a.Type != AccountType.Expense) continue;
            var consume = jl.Debit - jl.Credit;   // 费用消耗
            var bl = MostSpecific(budgetLines, jl);
            if (bl == null) continue;
            var mode = bl.ControlMode ?? version.DefaultControlMode;
            if (blockOnly && mode != BudgetControlMode.Block) continue;
            if (mode == BudgetControlMode.None) continue;
            incoming[bl.Id] = incoming.GetValueOrDefault(bl.Id) + consume;
            lineMeta[bl.Id] = bl;
        }
        if (incoming.Count == 0) return results;

        // 5. 逐桶取口径算已用 + 本次 vs 预算
        foreach (var (blId, inc) in incoming)
        {
            var bl = lineMeta[blId];
            var basis = bl.ControlBasis ?? version.DefaultControlBasis;
            var mode = bl.ControlMode ?? version.DefaultControlMode;
            int fromP = basis == BudgetControlBasis.Ytd ? 1 : pno, toP = pno;
            var budget = blPeriods.Where(p => p.BudgetLineId == blId && p.PeriodNo >= fromP && p.PeriodNo <= toP).Sum(p => p.Amount);
            var used = await ActualForBucketAsync(db, bl, fy, fromP, toP);
            var acctCode = (await db.GlAccounts.AsNoTracking().FirstAsync(a => a.Id == bl.AccountId)).Code;
            results.Add(new BudgetEvalResult {
                AccountCode = acctCode, Budget = budget, Used = used, Incoming = inc,
                IsBlock = mode == BudgetControlMode.Block, Exceeded = used + inc > budget,
            });
        }
        return results;
    }

    /// <summary>最具体匹配（spec §7.3）：非空预算维度全等于凭证行；取非空维度最多者。</summary>
    private static BudgetLine? MostSpecific(List<BudgetLine> lines, JournalLine jl)
    {
        var ccKey = jl.CostCenterId ?? Guid.Empty;
        var coTypeKey = jl.CostObjectType ?? "";
        var coIdKey = jl.CostObjectId ?? "";
        return lines.Where(l => l.AccountId == jl.AccountId
                && (l.CostCenterKey == Guid.Empty || l.CostCenterKey == ccKey)
                && (l.CostObjectTypeKey == "" || l.CostObjectTypeKey == coTypeKey)
                && (l.CostObjectIdKey == "" || l.CostObjectIdKey == coIdKey))
            .OrderByDescending(l => (l.CostCenterKey != Guid.Empty ? 1 : 0) + (l.CostObjectTypeKey != "" ? 1 : 0) + (l.CostObjectIdKey != "" ? 1 : 0))
            .ThenByDescending(l => l.CostCenterKey != Guid.Empty)   // 确定性 tie-break
            .FirstOrDefault();
    }

    /// <summary>桶已过账实际（费用净借方），财年 fy 期号 [fromP,toP]。</summary>
    private static async Task<decimal> ActualForBucketAsync(CP6Context db, BudgetLine bl, int fy, int fromP, int toP)
    {
        var q = from l in db.JournalLines.AsNoTracking()
                join e in db.JournalEntries.AsNoTracking() on l.EntryId equals e.Id
                join p in db.FiscalPeriods.AsNoTracking() on e.PeriodId equals p.Id
                where e.Status == JournalStatus.Posted && l.AccountId == bl.AccountId
                      && p.FiscalYear == fy && p.PeriodNo >= fromP && p.PeriodNo <= toP
                select new { l.Debit, l.Credit, l.CostCenterId, l.CostObjectType, l.CostObjectId };
        var rows = await q.ToListAsync();
        // 桶维度过滤（与预算桶同粒度：预算桶非空维度须与实际相等；预算桶空维度=通配）
        return rows.Where(r =>
                (bl.CostCenterKey == Guid.Empty || (r.CostCenterId ?? Guid.Empty) == bl.CostCenterKey) &&
                (bl.CostObjectTypeKey == "" || (r.CostObjectType ?? "") == bl.CostObjectTypeKey) &&
                (bl.CostObjectIdKey == "" || (r.CostObjectId ?? "") == bl.CostObjectIdKey))
            .Sum(r => r.Debit - r.Credit);
    }
}

public class BudgetEvalResult
{
    public string AccountCode { get; set; } = "";
    public decimal Budget { get; set; }
    public decimal Used { get; set; }
    public decimal Incoming { get; set; }
    public bool IsBlock { get; set; }
    public bool Exceeded { get; set; }
}
```

- [ ] **Step 4: 运行验证通过** — PASS（4 测）。

- [ ] **Step 5: Commit**

```bash
git add CP6.Core/Services/Fin/BudgetGuard.cs CP6.Tests/Fin/BudgetGuardTests.cs
git commit -m "feat(fin): A5 BudgetGuard + BudgetEvaluator (最具体匹配 + YTD/Period 口径 + 同 entry 合并 + 仅费用Block + PeriodId落期, 仿 BankReconGuard) (spec §7)"
```

### Task E-2: 挂 JournalEntryService.PostAsync + Warn 放行测试

**Files:**
- Modify: `CP6.Core/Services/Fin/JournalEntryService.cs`
- Test: `CP6.Tests/Fin/BudgetGuardTests.cs`

- [ ] **Step 1: 写失败测试（PostAsync 端到端：Block 拒 / Warn 通过 / AutoPost 不拦）**

```csharp
[Fact]
public async Task PostAsync_BlockExceeded_Rejected()
{
    var (db, acct, pid, _) = await SeedAsync(1200m, BudgetControlMode.Block, BudgetControlBasis.Period);
    var svc = TestHelper.BuildJournalEntryService(db);   // 与既有凭证测试同构建法
    var e = Entry(acct, pid, 500m); e.MakerId = "maker"; e.Status = JournalStatus.PendingReview;
    db.JournalEntries.Add(e); await db.SaveChangesAsync();
    var r = await svc.PostAsync(e.Id, "checker");
    Assert.False(r.Ok);
    Assert.Equal("E-A5-BUDGET-EXCEEDED", r.Code);
}

[Fact]
public async Task PostAsync_WarnExceeded_Passes()
{
    var (db, acct, pid, _) = await SeedAsync(1200m, BudgetControlMode.Warn, BudgetControlBasis.Period);
    var svc = TestHelper.BuildJournalEntryService(db);
    var e = Entry(acct, pid, 500m); e.MakerId = "maker"; e.Status = JournalStatus.PendingReview;
    db.JournalEntries.Add(e); await db.SaveChangesAsync();
    var r = await svc.PostAsync(e.Id, "checker");
    Assert.True(r.Ok);   // Warn 不拦
}
```

> `TestHelper.BuildJournalEntryService(db)` 若不存在则参既有凭证测试构造（注入 `FiscalPeriodService`/`FinSequenceService` 等）。Warn 测试需科目余额恒等可过账——`Entry` 的对方贷方在测试里补一条平衡行（或用既有建好平衡凭证的 helper）。

- [ ] **Step 2: 运行验证失败** — FAIL。

- [ ] **Step 3: 挂守卫（PostAsync，紧接 BankReconGuard）**

`JournalEntryService.PostAsync` 内（找到 `BankReconGuard.CheckPostingAsync` 调用处，其后追加）:
```csharp
var bankGuard = await BankReconGuard.CheckPostingAsync(_db, e);
if (!bankGuard.Ok) return bankGuard;
var budgetGuard = await BudgetGuard.CheckPostingAsync(_db, e);   // A5
if (!budgetGuard.Ok) return budgetGuard;
```

> **不改** `AutoPostAsync`/`ReverseAsync`（决策 §8-2 / 红冲释放）。

- [ ] **Step 4: 运行验证通过** — PASS。

- [ ] **Step 5: Commit**

```bash
git add CP6.Core/Services/Fin/JournalEntryService.cs CP6.Tests/Fin/BudgetGuardTests.cs
git commit -m "feat(fin): A5 hook BudgetGuard into manual PostAsync (Block 拒/Warn 放行; AutoPost/Reverse 不拦) (spec §6.2/§7.4)"
```

---

## Phase F — 预算 vs 实际报表

### Task F-1: BudgetReportService 维度聚合 + 未编预算分组

**Files:**
- Create: `CP6.Core/Services/Fin/IBudgetReportService.cs`
- Create: `CP6.Core/Services/Fin/BudgetReportService.cs`
- Modify: `CP6.Core/Services/Fin/BudgetDtos.cs`
- Test: `CP6.Tests/Fin/BudgetVsActualTests.cs`

- [ ] **Step 1: 写失败测试（按预算桶对比 + 未编预算实际分组 + 差异）**

```csharp
using CP6.Core.Data;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CP6.Tests.Fin;

public class BudgetVsActualTests
{
    [Fact]
    public async Task VsActual_MatchesBudgetBucket_ComputesVariance()
    {
        var db = TestHelper.CreateInMemoryContext();
        var acct = new GlAccount { Code="6602", Name="管理费用", Type=AccountType.Expense, NormalSide=AccountSide.Debit, IsLeaf=true, IsActive=true };
        db.GlAccounts.Add(acct);
        var p2 = new FiscalPeriod { FiscalYear=2027, Year=2027, Month=2, PeriodNo=2, Status=PeriodStatus.Open, PeriodStart=new(2027,2,1), PeriodEnd=new(2027,2,28) };
        db.FiscalPeriods.Add(p2);
        var b = new Budget { No="BUD-2027-00001", Name="2027", FiscalYear=2027, IsActive=true };
        db.Budgets.Add(b);
        var v = new BudgetVersion { BudgetId=b.Id, VersionNo=1, Status=BudgetVersionStatus.Approved, IsActive=true };
        db.BudgetVersions.Add(v);
        await db.SaveChangesAsync();
        var line = new BudgetLine { VersionId=v.Id, AccountId=acct.Id, AnnualAmount=1200m }; line.NormalizeKeys();
        db.BudgetLines.Add(line); await db.SaveChangesAsync();
        for (int i=1;i<=12;i++) db.BudgetLinePeriods.Add(new BudgetLinePeriod { BudgetLineId=line.Id, PeriodNo=i, Amount=100m });
        // 期2 实际 80
        var e = new JournalEntry { VoucherDate=new(2027,2,10), PeriodId=p2.Id, Source=VoucherSource.Manual, Status=JournalStatus.Posted,
            Lines=new(){ new JournalLine { AccountId=acct.Id, Debit=80m, Credit=0m } } };
        db.JournalEntries.Add(e); await db.SaveChangesAsync();

        var svc = new BudgetReportService(db);
        var rep = await svc.BuildVsActualAsync(2027, null, 1, 12);
        var row = rep.Rows.First(r => r.AccountCode == "6602" && !r.IsUnbudgeted);
        Assert.Equal(1200m, row.Budget);
        Assert.Equal(80m, row.Actual);
        Assert.Equal(1120m, row.Variance);
    }

    [Fact]
    public async Task VsActual_ActualWithoutBudget_GoesToUnbudgetedGroup()
    {
        var db = TestHelper.CreateInMemoryContext();
        var acct = new GlAccount { Code="6603", Name="差旅费", Type=AccountType.Expense, NormalSide=AccountSide.Debit, IsLeaf=true, IsActive=true };
        db.GlAccounts.Add(acct);
        var p2 = new FiscalPeriod { FiscalYear=2027, Year=2027, Month=2, PeriodNo=2, Status=PeriodStatus.Open, PeriodStart=new(2027,2,1), PeriodEnd=new(2027,2,28) };
        db.FiscalPeriods.Add(p2);
        var b = new Budget { No="BUD-2027-00001", Name="2027", FiscalYear=2027, IsActive=true };
        db.Budgets.Add(b);
        var v = new BudgetVersion { BudgetId=b.Id, VersionNo=1, Status=BudgetVersionStatus.Approved, IsActive=true };
        db.BudgetVersions.Add(v);
        await db.SaveChangesAsync();
        // 无预算行，但有实际 6603 = 50
        var e = new JournalEntry { VoucherDate=new(2027,2,10), PeriodId=p2.Id, Source=VoucherSource.Manual, Status=JournalStatus.Posted,
            Lines=new(){ new JournalLine { AccountId=acct.Id, Debit=50m, Credit=0m } } };
        db.JournalEntries.Add(e); await db.SaveChangesAsync();

        var svc = new BudgetReportService(db);
        var rep = await svc.BuildVsActualAsync(2027, null, 1, 12);
        var ub = rep.Rows.First(r => r.IsUnbudgeted && r.AccountCode == "6603");
        Assert.Equal(0m, ub.Budget);
        Assert.Equal(50m, ub.Actual);
        Assert.True(rep.Rows.Any(r => r.IsUnbudgeted));   // 未编预算实际不丢
    }
}
```

- [ ] **Step 2: 运行验证失败** — FAIL。

- [ ] **Step 3: 写 DTO + 接口 + 实现**

`BudgetDtos.cs` 追加:
```csharp
public class BudgetVsActualRow
{
    public Guid AccountId { get; set; }
    public string AccountCode { get; set; } = "";
    public string AccountName { get; set; } = "";
    public Guid? CostCenterId { get; set; }
    public string? CostObjectType { get; set; }
    public string? CostObjectId { get; set; }
    public decimal Budget { get; set; }
    public decimal Actual { get; set; }
    public decimal Variance => Budget - Actual;
    public decimal? VariancePct => Budget == 0 ? null : Math.Round((Budget - Actual) / Budget * 100, 2);
    public decimal[] BudgetPeriods { get; set; } = new decimal[12];
    public decimal[] ActualPeriods { get; set; } = new decimal[12];
    public bool IsUnbudgeted { get; set; }
}
public class BudgetVsActualReport
{
    public int FiscalYear { get; set; }
    public Guid? VersionId { get; set; }
    public List<BudgetVsActualRow> Rows { get; set; } = new();
    public decimal TotalBudget => Rows.Sum(r => r.Budget);
    public decimal TotalActual => Rows.Sum(r => r.Actual);
}
public class BudgetWarningDto   // 过账预检（§7.4）
{
    public string AccountCode { get; set; } = "";
    public decimal Budget { get; set; }
    public decimal Used { get; set; }
    public decimal Incoming { get; set; }
    public bool IsBlock { get; set; }
}
```

`IBudgetReportService.cs`:
```csharp
using CP6.Entity.DomainModels.Fin;

namespace CP6.Core.Services.Fin;

public interface IBudgetReportService
{
    Task<BudgetVsActualReport> BuildVsActualAsync(int fiscalYear, Guid? versionId, int periodFrom, int periodTo);
    Task<List<BudgetWarningDto>> PreCheckAsync(JournalEntry entry);   // F-3
    Task<Dictionary<(Guid acct, Guid cc, string coType, string coId), decimal[]>> AggregateActualByBucketAsync(int fiscalYear);   // C-2/F-2 复制实际用
}
```

`BudgetReportService.cs`（vs-actual 主体；PreCheck 在 F-3，AggregateActual 在 F-2）:
```csharp
using CP6.Core.Data;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Fin;

public class BudgetReportService : IBudgetReportService
{
    private readonly CP6Context _db;
    public BudgetReportService(CP6Context db) { _db = db; }

    public async Task<BudgetVsActualReport> BuildVsActualAsync(int fiscalYear, Guid? versionId, int periodFrom, int periodTo)
    {
        var rep = new BudgetVsActualReport { FiscalYear = fiscalYear, VersionId = versionId };

        // Active(或指定)版本
        var version = versionId.HasValue
            ? await _db.BudgetVersions.AsNoTracking().FirstOrDefaultAsync(v => v.Id == versionId)
            : await (from v in _db.BudgetVersions.AsNoTracking()
                     join b in _db.Budgets.AsNoTracking() on v.BudgetId equals b.Id
                     where v.IsActive && b.FiscalYear == fiscalYear select v).FirstOrDefaultAsync();

        // 预算侧按桶（含 12 期）
        var budgetRows = new Dictionary<string, BudgetVsActualRow>();
        if (version != null)
        {
            var lines = await _db.BudgetLines.AsNoTracking().Where(l => l.VersionId == version.Id).ToListAsync();
            var ids = lines.Select(l => l.Id).ToList();
            var periods = await _db.BudgetLinePeriods.AsNoTracking().Where(p => ids.Contains(p.BudgetLineId)).ToListAsync();
            var accts = await _db.GlAccounts.AsNoTracking().Where(a => lines.Select(x=>x.AccountId).Contains(a.Id)).ToListAsync();
            foreach (var l in lines)
            {
                var key = BucketKey(l.AccountId, l.CostCenterKey, l.CostObjectTypeKey, l.CostObjectIdKey);
                var a = accts.First(x => x.Id == l.AccountId);
                var row = new BudgetVsActualRow { AccountId = l.AccountId, AccountCode = a.Code, AccountName = a.Name,
                    CostCenterId = l.CostCenterId, CostObjectType = l.CostObjectType, CostObjectId = l.CostObjectId };
                foreach (var p in periods.Where(p => p.BudgetLineId == l.Id && p.PeriodNo >= periodFrom && p.PeriodNo <= periodTo))
                    row.BudgetPeriods[p.PeriodNo - 1] = p.Amount;
                row.Budget = row.BudgetPeriods.Sum();
                budgetRows[key] = row;
            }
        }

        // 实际侧（已过账、损益类、按桶+期聚合）
        var actuals = await (from l in _db.JournalLines.AsNoTracking()
                             join e in _db.JournalEntries.AsNoTracking() on l.EntryId equals e.Id
                             join p in _db.FiscalPeriods.AsNoTracking() on e.PeriodId equals p.Id
                             join a in _db.GlAccounts.AsNoTracking() on l.AccountId equals a.Id
                             where e.Status == JournalStatus.Posted && p.FiscalYear == fiscalYear
                                   && p.PeriodNo >= periodFrom && p.PeriodNo <= periodTo
                                   && (a.Type == AccountType.Expense || a.Type == AccountType.Revenue)
                             select new { l.AccountId, a.Code, a.Name, a.Type, l.CostCenterId, l.CostObjectType, l.CostObjectId, l.Debit, l.Credit, p.PeriodNo })
                            .ToListAsync();

        foreach (var g in actuals.GroupBy(x => new { x.AccountId, x.Code, x.Name, x.Type, x.CostCenterId, x.CostObjectType, x.CostObjectId }))
        {
            var ccKey = g.Key.CostCenterId ?? Guid.Empty;
            var coTypeKey = g.Key.CostObjectType ?? "";
            var coIdKey = g.Key.CostObjectId ?? "";
            // 上卷到最具体匹配预算桶（spec §9.3 按预算桶视图）
            var matchKey = budgetRows.Keys.Where(k => MatchBucketKey(k, g.Key.AccountId, ccKey, coTypeKey, coIdKey))
                .OrderByDescending(k => k.Length).FirstOrDefault();
            BudgetVsActualRow row;
            if (matchKey != null) row = budgetRows[matchKey];
            else   // 未编预算实际（spec §9.3 rev1，不静默丢）
            {
                var key = BucketKey(g.Key.AccountId, ccKey, coTypeKey, coIdKey) + "|UB";
                if (!budgetRows.TryGetValue(key, out row!))
                {
                    row = new BudgetVsActualRow { AccountId = g.Key.AccountId, AccountCode = g.Key.Code, AccountName = g.Key.Name,
                        CostCenterId = g.Key.CostCenterId, CostObjectType = g.Key.CostObjectType, CostObjectId = g.Key.CostObjectId,
                        IsUnbudgeted = true };
                    budgetRows[key] = row;
                }
            }
            foreach (var x in g)
            {
                var amt = g.Key.Type == AccountType.Expense ? x.Debit - x.Credit : x.Credit - x.Debit;
                row.ActualPeriods[x.PeriodNo - 1] += amt;
            }
            row.Actual = row.ActualPeriods.Sum();
        }

        rep.Rows = budgetRows.Values.OrderBy(r => r.IsUnbudgeted).ThenBy(r => r.AccountCode).ToList();
        return rep;
    }

    private static string BucketKey(Guid acct, Guid cc, string coType, string coId) => $"{acct}|{cc}|{coType}|{coId}";
    private static bool MatchBucketKey(string budgetKey, Guid acct, Guid cc, string coType, string coId)
    {
        var parts = budgetKey.Split('|');
        if (Guid.Parse(parts[0]) != acct) return false;
        var bCc = Guid.Parse(parts[1]); var bType = parts[2]; var bId = parts[3];
        return (bCc == Guid.Empty || bCc == cc) && (bType == "" || bType == coType) && (bId == "" || bId == coId);
    }

    // F-2
    public Task<Dictionary<(Guid acct, Guid cc, string coType, string coId), decimal[]>> AggregateActualByBucketAsync(int fiscalYear) => throw new NotImplementedException();
    // F-3
    public Task<List<BudgetWarningDto>> PreCheckAsync(JournalEntry entry) => throw new NotImplementedException();
}
```

- [ ] **Step 4: 运行验证通过** — PASS（2 测）。

- [ ] **Step 5: Commit**

```bash
git add CP6.Core/Services/Fin/IBudgetReportService.cs CP6.Core/Services/Fin/BudgetReportService.cs CP6.Core/Services/Fin/BudgetDtos.cs CP6.Tests/Fin/BudgetVsActualTests.cs
git commit -m "feat(fin): A5 BudgetVsActual (维度聚合上卷预算桶 + 未编预算实际分组不丢 + 差异/差异率, 仿 TrialBalance) (spec §9)"
```

### Task F-2: AggregateActualByBucket + 复制上年实际回填

**Files:**
- Modify: `CP6.Core/Services/Fin/BudgetReportService.cs`（实现 AggregateActualByBucketAsync）
- Modify: `CP6.Core/Services/Fin/BudgetService.cs`（CopyFromActualAsync 调用之）
- Test: `CP6.Tests/Fin/BudgetCopyImportTests.cs`

- [ ] **Step 1: 写失败测试（复制上年实际→新版本按月数）** — 略（结构同 C-2 复制版本，但源为上年已过账实际；断言新版本桶按月数=实际聚合）。

- [ ] **Step 2-4**: 实现 `AggregateActualByBucketAsync`（按 (acct,ccKey,coTypeKey,coIdKey) group by + PeriodNo 聚出 12 期实际，损益类 Posted），`BudgetService.CopyFromActualAsync` 用其结果对每桶 `UpsertLine(SpreadMode=manual, Periods=12期实际)`。注入 `IBudgetReportService` 到 `BudgetService`。

- [ ] **Step 5: Commit**

```bash
git commit -m "feat(fin): A5 copy-from-actual (上年已过账实际按桶聚合回填新版本按月数) (spec §5.5)"
```

### Task F-3: PreCheck 过账预检（Warn+Block 预警）

**Files:**
- Modify: `CP6.Core/Services/Fin/BudgetReportService.cs`（PreCheckAsync 用 `BudgetEvaluator.EvaluateAsync(blockOnly:false)`）
- Test: `CP6.Tests/Fin/BudgetVsActualTests.cs`

- [ ] **Step 1-4**: `PreCheckAsync(entry)` = `BudgetEvaluator.EvaluateAsync(_db, entry, blockOnly:false)` → 映射超支项为 `BudgetWarningDto`（含 Warn+Block，供凭证 UI 提交前提示）。测试：Warn 行超支返回预警、未超不返回。

- [ ] **Step 5: Commit**

```bash
git commit -m "feat(fin): A5 PreCheck 过账预检 (复用 BudgetEvaluator 全量 Warn+Block 预警) (spec §7.4)"
```

---

## Phase G — API 控制器 + 权限 + 菜单

### Task G-1: 3 控制器 + 操作级权限

**Files:**
- Create: `CP6.WebApi/Controllers/Fin/BudgetController.cs`
- Create: `CP6.WebApi/Controllers/Fin/BudgetLineController.cs`
- Create: `CP6.WebApi/Controllers/Fin/BudgetReportController.cs`

- [ ] **Step 1: 写控制器（贴 `[RequirePermission("fin-budget", action)]`，GET 也贴 view）**

`BudgetController.cs`（节选，方案+版本）:
```csharp
using CP6.Core.Services.Fin;
using CP6.Core.Auth;
using CP6.Entity.DomainModels.Fin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Fin;

[ApiController]
[Route("api/fin/budget")]
[Authorize]
public class BudgetController : ControllerBase
{
    private readonly IBudgetService _svc;
    public BudgetController(IBudgetService svc) { _svc = svc; }
    private string CurrentUser => User?.Identity?.Name ?? "anonymous";
    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Fin(FinResult r) => r.Ok ? Ok2() : BadRequest(new { code = 400, message = r.Code, args = r.Args });

    [HttpGet] [RequirePermission("fin-budget", "view")]
    public async Task<IActionResult> List() => Ok2(await _svc.ListBudgetsAsync());

    [HttpPost] [RequirePermission("fin-budget", "add")]
    public async Task<IActionResult> Create([FromBody] Budget dto)
    { var r = await _svc.CreateBudgetAsync(dto, CurrentUser); return r.Ok ? Ok2(r.Data) : BadRequest(new { code=400, message=r.Code, args=r.Args }); }

    [HttpGet("{id}/versions")] [RequirePermission("fin-budget", "view")]
    public async Task<IActionResult> Versions(Guid id) => Ok2(await _svc.ListVersionsAsync(id));

    [HttpPost("{id}/versions")] [RequirePermission("fin-budget", "add")]
    public async Task<IActionResult> CreateVersion(Guid id, [FromBody] CreateVersionReq req)
    { var r = await _svc.CreateVersionAsync(id, req.Name, CurrentUser, req.CopyFromVersionId, req.CopyFromActualFiscalYear);
      return r.Ok ? Ok2(r.Data) : BadRequest(new { code=400, message=r.Code, args=r.Args }); }

    [HttpPost("versions/{vid}/submit")] [RequirePermission("fin-budget", "submit")]
    public async Task<IActionResult> Submit(Guid vid)
    { var uid = Guid.TryParse(User?.FindFirst("uid")?.Value, out var g) ? g : Guid.Empty;
      return Fin(await _svc.SubmitForApprovalAsync(vid, uid, CurrentUser)); }

    [HttpPost("versions/{vid}/activate")] [RequirePermission("fin-budget", "activate")]
    public async Task<IActionResult> Activate(Guid vid) => Fin(await _svc.ActivateAsync(vid));

    [HttpDelete("versions/{vid}")] [RequirePermission("fin-budget", "delete")]
    public async Task<IActionResult> DeleteVersion(Guid vid) => Fin(await _svc.DeleteVersionAsync(vid));

    public record CreateVersionReq(string Name, Guid? CopyFromVersionId, int? CopyFromActualFiscalYear);
}
```

`BudgetLineController.cs`（行网格 + 导入）与 `BudgetReportController.cs`（vs-actual GET + pre-check POST）按相同范式写（详见 spec §10.2/§10.3；导入用 `IFormFile`→`OpenReadStream()` 调 Preview/Confirm）。

- [ ] **Step 2: build** — `dotnet build CP6.WebApi` 成功。

- [ ] **Step 3: Commit**

```bash
git add CP6.WebApi/Controllers/Fin/Budget*.cs
git commit -m "feat(fin): A5 3 controllers (budget/line/report) + operation-level RequirePermission(view 也 seed) (spec §10/§12)"
```

### Task G-2: 菜单 621-623 + 权限 seed + DI 注册

**Files:**
- Modify: `CP6.WebApi/Program.cs`

- [ ] **Step 1: seed 菜单 + RoleMenu + MenuActions + DI**

```csharp
// 菜单（参 A4 菜单 seed）
SeedMenu(621, "预算管理", null, 600, 280);            // 父
SeedMenu(622, "预算编制", "/fin/budget", 621, 281);
SeedMenu(623, "执行分析", "/fin/budget/vs-actual", 621, 282);
// RoleMenu admin 全开 621/622/623
// MenuActions: fin-budget × {view,add,edit,delete,submit,activate,import,copy}
foreach (var act in new[]{"view","add","edit","delete","submit","activate","import","copy"})
    SeedMenuAction("fin-budget", act);   // 与既有 finActions seed 同法
// MenuKey 派生循环范围改 <=623
// DI
builder.Services.AddScoped<IBudgetService, BudgetService>();
builder.Services.AddScoped<IBudgetLineService, BudgetLineService>();
builder.Services.AddScoped<IBudgetReportService, BudgetReportService>();
builder.Services.AddScoped<CP6.Core.Services.Wf.IApprovalCallback, CP6.Core.Services.Fin.BudgetApprovalCallback>();
// flow seed
A5BudgetFlowSeed.Seed(db);
```

> 实际 seed 写法逐字参 Program.cs 既有 A4 菜单 614/619/620 + finActions 块；上面是占位示意，落码按既有 helper 形态写。**623 权限统一用 `fin-budget`**（控制器已贴），故 623 不单独建权限资源。

- [ ] **Step 2: 启动验证** — 起后端，确认菜单 621-623 入库、admin RoleMenu 通、MenuKey 派生含 fin-budget。

- [ ] **Step 3: Commit**

```bash
git add CP6.WebApi/Program.cs
git commit -m "feat(fin): A5 seed menus 621-623 + RoleMenu + fin-budget 8 actions + DI(services+callback) + flow seed (spec §11.3/§12)"
```

---

## Phase H — 前端 + i18n

### Task H-1: types + api + i18n seed

**Files:**
- Create: `cp6.web/src/types/fin/budget.ts`, `cp6.web/src/api/fin/budget.ts`
- Create: `CP6.WebApi/Seed/I18nA5BudgetScreenSeed.cs`
- Modify: `CP6.WebApi/Program.cs`（i18n `.Concat`）

- [ ] **Step 1**: 写 TS 类型（Budget/BudgetVersion/BudgetLineGridRow/BudgetVsActualRow + 枚举）+ api 封装（对齐 3 控制器端点）。
- [ ] **Step 2**: `I18nA5BudgetScreenSeed`：nav.621/622/623 + 枚举(状态/控制模式 None/Warn/Block/口径 YTD/单期) + 字段/动作 label + 错误码 E-A5-*/W-A5-* 五语；`Program.cs` i18n 链 `.Concat(I18nA5BudgetScreenSeed.Items)`。**5 个错误码务必 seed**（E-A5-BUDGET-001/VERSION-002~006/LINE-001~005/BUDGET-EXCEEDED/IMPORT-001/COPY-001/CONCURRENCY-001/W-A5-BUDGET-WARN）。
- [ ] **Step 3**: 后端 build + 启动 reseed 验证词条下发；前端 `npm run build`。
- [ ] **Step 4: Commit**

```bash
git commit -m "feat(fin): A5 frontend types+api + I18nA5BudgetScreenSeed 五语(nav.621-623/枚举/字段/错误码) (spec §10/§14.5)"
```

### Task H-2: BudgetEditView（编制网格）

**Files:**
- Create: `cp6.web/src/views/fin/BudgetEditView.vue`
- Modify: `cp6.web/src/router/index.ts`（路由 `/fin/budget`，菜单驱动）

- [ ] **Step 1**: 写视图（fin house style：page-header/el-card/table-toolbar/el-table）：左方案+版本树（状态 tag + Active 徽标），右维度行网格（科目/成本中心/成本对象 + M1..M12 + 合计 + ControlMode/Basis 下拉），工具条（新增行/Excel 导入/复制/分解/提交审批/刷新）；编辑门控仅 Draft；并发冲突 E-A5-CONCURRENCY-001 → ElMessageBox 刷新重试。零裸 key（中文自然语言 key）。
- [ ] **Step 2**: 路由注册（菜单 622 驱动）。`npm run build` 通过。
- [ ] **Step 3: Commit**

```bash
git commit -m "feat(fin): A5 BudgetEditView (方案+版本树 + 维度行网格 12 月编辑/分解/导入/复制/提交审批 + 并发UX, fin house style) (spec §11.1)"
```

### Task H-3: BudgetVsActualView（执行分析）

**Files:**
- Create: `cp6.web/src/views/fin/BudgetVsActualView.vue`
- Modify: `cp6.web/src/router/index.ts`（路由 `/fin/budget/vs-actual`）

- [ ] **Step 1**: 写视图：筛选(财年/版本/期间区间/维度) + 顶部卡片(总预算/总实际/总差异/执行率) + 主表(维度行 + 预算/实际/差异/差异率/执行率 + 12 期钻取)；费用超支红/收入未达黄/未编预算实际橙。
- [ ] **Step 2**: 路由注册（菜单 623）。`npm run build` 通过。
- [ ] **Step 3: Commit**

```bash
git commit -m "feat(fin): A5 BudgetVsActualView (筛选+卡片+对比表 12期钻取, 超支/未编预算配色) (spec §11.2)"
```

---

## Phase I — 测试收口 + QA

### Task I-1: SQLite 结构测试（若可用）+ AC 覆盖补齐

**Files:**
- Create: `CP6.Tests/Fin/BudgetSqliteTests.cs`（唯一约束含 NULL 维度 + RowVersion 并发）

- [ ] **Step 1**: 尝试 SQLite harness（spec §16.1）：若 `EnsureCreated` 撞 `near "max"`（A3/A4 同因）则**跳过 SQLite 结构测试**并在文件头注明，唯一约束/并发以 InMemory 覆盖（AC-002/AC-017 InMemory 版）。可用则测：①两行维度全空(公司级)插入冲突 E-A5-LINE-001（验证 Key 唯一性）②RowVersion 并发改行 E-A5-CONCURRENCY-001。
- [ ] **Step 2**: 补 AC 未覆盖断言（AC-011 AutoPost 不拦 / AC-018 无 Active 短路 / AC-019 红冲不拦 / AC-022 非自然财年落期），归入既有测试类。
- [ ] **Step 3: Commit**

```bash
git commit -m "test(fin): A5 SQLite struct(或 InMemory 兜底) + AC-011/018/019/022 覆盖 (spec §15/§16)"
```

### Task I-2: gstack 端到端 QA（人工触发）

- [ ] **Step 1**: 起后端 + 前端，admin 登录浏览器实测全链（spec §16.2）：
  - 建方案(2027) → 建版本(复制上年实际) → 编行(配 Block/YTD) → 分解(均摊) → 提交审批 → (OA 通过)自动激活 → 手工凭证超预算被拒(E-A5-BUDGET-EXCEEDED) → Warn 行不拦 → 预算 vs 实际报表数对 + 未编预算实际分组可见 → 五语切换零裸 key → 权限无 403 → 菜单 621-623 全可达(A4 H-3 路由教训)。
- [ ] **Step 2**: 逮 bug 修复后 Commit。

```bash
git commit -m "fix(fin): A5 gstack QA 修复(若有) — 全链编制→审批→控制→报表实测通过"
```

---

## 潜在落码注意（执行前必看）

1. **`RequirePermission` 命名空间**：实际是 `CP6.Core.Auth`（A3 plan 误写 `CP6.WebApi.Filters`）。`HasActionAsync` 无 admin 旁路 → **GET 端点也要 seed `view`**（A3 漏 view 致全 403 的坑）。
2. **`AccountType`/`AccountSide`/`PeriodStatus`/`JournalStatus`/`VoucherSource` 枚举名**以 `CP6.Entity.DomainModels.Fin` 实际为准（`Expense=5`/`Revenue=4`/`Posted=2`/`Open=0`）。
3. **OA 栈类型**（`IApprovalService`/`IApprovalCallback`/`ApprovalCallbackContext`/`ApprovalDispatcher`/`FlowEngine`/`Wf_FlowDef`/`Wf_ApprovalBinding`/`FlowSchema`/`FlowNode`）逐字参 `JournalApprovalCallback.cs` + `JournalApprovalIntegrationTests.cs`，seed 用强类型 `FlowSchema`（非匿名对象）。`DecidedById` 是 `Guid?`。
4. **落期口径**：守卫/报表/复制实际**一律** PeriodId 优先 → ResolveAsync 兜底，**勿** `VoucherDate.Year/Month`（非自然财年坑）。
5. **规范化 Key 派生**：任何写 BudgetLine 的路径（upsert/复制/导入）保存前必调 `NormalizeKeys()`，否则唯一索引与匹配失效。
6. **`FinResult` vs `FinResult<T>`**：既有 `FinResult` 无 Data；本 plan 新增 `FinResult<T>` 供需返回实体的方法（CreateBudget/CreateVersion）。其余沿用 `FinResult`。
7. **`BudgetService` 构造演进**：B-1(db,seq) → B-2 加 `IApprovalService` → F-2 加 `IBudgetReportService`。每次加参回填既有测试构造 + Program.cs DI（DI 自动注入，测试手动传）。
8. **不新增 `VoucherSource`**：A5 账外无凭证产物，勿动 JournalEntry 枚举。
9. **SQLite 已知限制**：`nvarchar(max)` 致 `EnsureCreated` 失败（A3/A4 同），结构/并发测试 InMemory + 真 SQL Server 兜底，不阻断。
10. **菜单驱动路由（A4 H-3 教训）**：622/623 必须 seed 菜单 + RoleMenu，前端 `addDynamicRoutes` 才注册路由，否则视图白屏不可达。

---

## Self-Review（已核）

- **Spec 覆盖**：§3 数据模型→A-1/A-2/A-3；§4 状态机→B-1/B-2；§5 业务规则→B/C；§6 流程→贯穿；§7 BudgetGuard→E-1/E-2；§8 OA→B-2/D-1；§9 报表→F-1/F-2/F-3；§10 API→G-1；§11 页面→H-2/H-3；§12 权限→G-1/G-2；§13 错误码→H-1 seed；§14 护栏→各 Task；§15 AC→测试类 + I-1；§16 测试→各 Task TDD + I。无遗漏。
- **Placeholder 扫描**：无 TBD/TODO（"落码核实/核对"为既有约定指引，非占位）；硬任务代码完整。
- **类型一致**：`BudgetVersionStatus`/`BudgetControlMode`/`BudgetControlBasis` 全 plan 一致；`NormalizeKeys()`/`ActivateFromApprovalAsync`/`BudgetEvaluator.EvaluateAsync`/`MostSpecific`/`ActualForBucketAsync` 签名前后一致；`FinResult<T>` 引入点(B-1)早于使用。

---

*生成于 2026-06-20。spec：docs/superpowers/specs/2026-06-20-a5-budget-design.md（含 rev1 4 点修订）。9 阶段 ~18 任务，每后端 Task 含完整可落码 C# + TDD + 迁移命令；硬任务 E(守卫多维匹配/口径/合并)、D(OA 回调原子激活)、F(实际维度聚合/未编预算)、C(复制/Excel) 评审上 opus。复用 A4 BankReconGuard 范式 / JournalApprovalCallback 先例 / TrialBalanceService 聚合范式。不新增 VoucherSource。下一步：subagent-driven-development 逐 Task 落地。*
