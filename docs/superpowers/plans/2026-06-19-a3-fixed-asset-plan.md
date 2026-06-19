# A3 固定资产（Fixed Asset）Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把制造业 ERP 的固定资产管理做真——资产分类/卡片主数据 → 四法月末折旧（汇总凭证 + 资产级明细追溯，手动/Worker/结账钩子三路）→ 全套处置（出售/报废/转让/盘亏，经清理科目结转）。全程复用已成熟的 GL 自动凭证基建，`AutoVoucherEngine` 零改。

**Architecture:** 新建 Fin 5 实体 `AssetCategory`/`AssetCard`/`DepreciationRun`/`DepreciationEntry`/`AssetDisposal`（均继承 `BaseTenantEntity` + 显式 `RowVersion`）。折旧/处置凭证**不走 `AutoVoucherEngine` 的 `FinBizEvent→PostingRule` 路径**，改为**仿 `FxRevaluationService`**：服务层直接拼 `JournalEntry` → `JournalEntryService.AutoPostAsync`，科目由 `GlAccount.Role`（单例锚点）+ `AssetCategory`（折旧费用科目路由，非单例）解析。折旧引擎 `IDepreciationCalculator` 为纯函数（单元测试主战场）。期间结账钩子 `AccrueAsync` 兜底（仿 `_reval` 可选依赖），月末 `AssetDepreciationWorker` 备草稿（仿 `FinReconciliationWorker` + `TenantScopeRunner`）。前端 4 视图 + 五语 i18n。

**Tech Stack:** .NET 8 + EF Core 8 / xUnit + EF Core InMemory + EF Core Sqlite(已引,8.0.12) / Vue 3.5 + element-plus + vue-i18n。源 spec：`docs/superpowers/specs/2026-06-19-a3-fixed-asset-design.md`（A3-D1~D9 + review 修订全采纳，535 行定稿）。

---

## 关键既有约定（落码前必读）

### 账务策略与服务范本
- **凭证直建（仿 `FxRevaluationService`）**：范本 `CP6.Core/Services/Fin/FxRevaluationService.cs`。要点：① 幂等检查 `if (await _db.JournalEntries.AnyAsync(e => e.SourceDocNo == docNo)) return FinResult.Pass();`；② Role→科目 `private async Task<Guid?> RoleIdAsync(string role) => (await _db.GlAccounts.FirstOrDefaultAsync(a => a.Role == role && a.IsActive))?.Id;`；③ 拼 `new JournalEntry { VoucherDate, Source, SourceDocNo, Description, Lines }` → `await _journal.AutoPostAsync(entry)`。构造依赖：`(CP6Context db, IJournalEntryService journal, ...)`。
- **`JournalEntryService.AutoPostAsync(JournalEntry)`**（`CP6.Core/Services/Fin/JournalEntryService.cs` L92-118）：`Source==Manual` 直接拒（E-FIN-113，故自动凭证必须给非 Manual 的 Source）；内部 `EnsureOpenAsync(VoucherDate)` 落期 + `IsOpenAsync` 校验（已结账期 → E-FIN-112，A3 的 FA007「已结账期拒过账」直接由此兜底，**无需自建期间守卫**）；采番 `NextAsync(SeqKey, VoucherDate)`；校验借贷恒等/末级/启用；过账置 `Status=Posted`。
- **`JournalEntryService.ReverseAsync(Guid entryId, string makerId, string reason, bool autoPost = false)`**：只 Posted 可红冲，生成借贷对调反向凭证。A3 折旧/处置反冲调此（传 `autoPost: true` 让红冲凭证直接过账）。
- **`GlAccount.Role`**（`CP6.Entity/DomainModels/Fin/GlAccount.cs` L50-53，`string? Role`，`[MaxLength(30)]`）：单例科目锚点。解析逻辑同 `AutoVoucherEngine.ResolveRoleAsync`(L98-103)。
- **`FinSequenceService.NextAsync(seqKey, date)`** → `"{KEY}-{yyyy-MM}-{NNNNN}"`。A3 采番键：`FA`(卡片)/`DEP`(折旧批/含 DisposalFinal)/`FAD`(处置)。
- **`FinResult`**（`CP6.Core/Services/Fin/FinResult.cs`）：`{ Ok, Code, Args }` + `Pass()` / `Fail(code, params object[] args)`。A3 服务统一返回。

### 【关键】§9.1 科目表对账修正（spec §9.1 与真实 CoA 不符，以本表为准）
真实模板 `CP6.Core/Services/Fin/FinCoaTemplate.cs` 的 `CnGaapRows` 与 spec §9.1 假设有出入，落码以**下表实际编码**为准（spec §9.1 的理想编码作废）：

| 用途 | spec §9.1 写的 | **真实 CoA 实况** | A3 落地动作 |
|---|---|---|---|
| 固定资产 | 1601 / Role FIXED_ASSET | `1601 固定资产`(Asset,Dr,leaf) **存在·无 Role** | 补 Role `FIXED_ASSET` |
| 累计折旧 | 1602 / Role ACCUM_DEPRECIATION | `1602 累计折旧`(Asset,Cr,leaf,备抵) **存在·无 Role** | 补 Role `ACCUM_DEPRECIATION` |
| 固定资产清理 | 1606 / ASSET_CLEARING | **不存在** | 新增 `1606`(Asset,Dr,leaf,Role `ASSET_CLEARING`) |
| 待处理财产损溢 | 1901 / PENDING_PROPERTY_LOSS | **不存在** | 新增 `1901`(Asset,Dr,leaf,Role `PENDING_PROPERTY_LOSS`) |
| 机器设备折旧费用 | 5101 制造费用 | `5101` 是**非叶子**；真实叶子 = `5101.01 制造费用—折旧` | 分类路由指向 **`5101.01`**（叶子，可过账） |
| 管理类折旧费用 | 6602 管理费用 | 真实是 **`6002 管理费用`**(Expense,Dr,leaf) | 分类路由指向 **`6002`** |
| 销售类折旧费用 | 6601 销售费用 | 真实 `6601`=**研发费用**；销售费用是 **`6001`** | 分类路由指向 **`6001`** |
| 资产处置损益 | 6115 / ASSET_DISPOSAL_PL | **不存在** | 新增 `6115`(Expense,Dr,leaf,Role `ASSET_DISPOSAL_PL`) |
| 营业外支出 | 6711 / NON_OP_EXPENSE | **不存在** | 新增 `6711`(Expense,Dr,leaf,Role `NON_OP_EXPENSE`) |
| 营业外收入 | 6301 / NON_OP_INCOME | 真实是 **`4301 营业外收入`**(Revenue,Cr,leaf)·无 Role | 给 `4301` 补 Role `NON_OP_INCOME`（不新建 6301） |
| 销项税额 | 2221 / **新造 OUTPUT_VAT** | **已存在** `2221.02 应交税费—销项税`(leaf)·**已带 Role `TAX_OUTPUT`** | **复用 Role `TAX_OUTPUT`**，不新造 OUTPUT_VAT、不建科目 |

> 由此：处置结转凭证的「应交税费—销项税额」行用 Role **`TAX_OUTPUT`** 解析（非 spec 的 OUTPUT_VAT）；折旧费用科目由 `AssetCategory.DeprecExpenseAccountId` 路由到 `5101.01`/`6002`/`6001`（**叶子科目**，否则 AutoPost 末级校验失败）。

### EF / 索引 / 迁移
- **多租户基类**：A3 实体继承 `BaseTenantEntity`（=`Id`/审计 + `TenantId`，**不含** `RowVersion`/`IsDeleted`）。5 实体**显式加** `[Timestamp] public byte[]? RowVersion { get; set; }`（与 `BaseBizEntity` 同写法）。
- **唯一索引租户前缀自动重写**：`CP6Context.OnModelCreating` 末尾（L1775-1800 附近）有反射循环，把所有 `BaseTenantEntity` 子类的**唯一索引**自动前缀 `TenantId`。**只声明逻辑唯一索引**（如 `HasIndex(x=>x.AssetNo).IsUnique()`），**勿手写 `TenantId`**。
- **过滤唯一索引范本**（`CP6Context.cs` L622-625，自动凭证幂等）：
  ```csharp
  e.HasIndex(x => new { x.Source, x.SourceDocNo }).IsUnique()
      .HasFilter("[Source] <> 0 AND [Status] = 2 AND [SourceDocNo] IS NOT NULL")
      .HasDatabaseName("UX_Fin_JournalEntry_AutoVoucherSource");
  ```
  A3 的「每期单一批量批次」过滤唯一索引照此写（详见 Task A-2）。**注**：InMemory 不强制过滤唯一索引，故服务层另有代码级幂等（FA003）双保险；真正的并发拦截测试走 SQLite（Task H-1）。
- **迁移命令**：`dotnet ef migrations add A3FixedAsset --project CP6.Core --startup-project CP6.WebApi`（**会先构建；不要带 `--no-build`**）。生成后核对 `*_A3FixedAsset.cs` 的 `CreateTable`/索引列（唯一索引列含 `"TenantId"` 前缀）。

### 控制器 / DI / 测试
- **控制器范式**（参 `JournalEntryController` L14-59）：`[ApiController]`+`[Route("api/fin/...")]`+`[Authorize]`+`ControllerBase`；私有助手逐字：
  ```csharp
  private string CurrentUser => User?.Identity?.Name ?? "anonymous";
  private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
  private IActionResult Fin(FinResult r) => r.Ok ? Ok2() : BadRequest(new { code = 400, message = r.Code, args = r.Args });
  ```
  端点贴 `[RequirePermission("<resource>", "<action>")]`（resource = RoutePath 派生 MenuKey；`HasActionAsync` 无 admin 旁路 → 属性与 seed 同 commit）。
- **DI 插入点**：`CP6.WebApi/Program.cs` L119-145 Fin 服务区（`AddScoped<IFxRevaluationService,...>()` / `AddScoped<IPeriodCloseService,...>()` 附近）。A3 加 `IDepreciationCalculator`/`IAssetDepreciationService`/`IAssetDisposalService`。
- **菜单 MenuKey 自动派生**（`Program.cs` L607-612）：`m.MenuKey = m.RoutePath!.Trim('/').Replace('/', '-')`。故菜单 RoutePath `/fin/asset-category` 自动派生 MenuKey `fin-asset-category`，与 `[RequirePermission("fin-asset-category", ...)]` 资源键吻合——**A3 菜单只需给 RoutePath，权限资源键自动对齐**。
- **测试基建**：`TestHelper.CreateInMemoryContext()` = `new CP6Context(UseInMemoryDatabase(Guid))`，默认租户。`FiscalPeriodService` 构造 `new FiscalPeriodService(db, 1)`；`JournalEntryService` = `new JournalEntryService(db, periods, new FinSequenceService(db))`。CoA 种入 `await new GlAccountService(db).ImportTemplateAsync(FinCoaTemplate.CnGaap, "seed")`（含 A3 新增科目，见 Task F-2 已并入模板）。`CP6.Tests/Fin/` 下放测试，文件按需 `using CP6.Entity.DomainModels.Fin; using CP6.Core.Services.Fin;`。
- **SQLite 结构测试 harness**（包已就绪，无需加包）：
  ```csharp
  using var conn = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
  conn.Open();
  var options = new DbContextOptionsBuilder<CP6Context>().UseSqlite(conn).Options;
  using var db = new CP6Context(options);
  db.Database.EnsureCreated();   // 建全 schema（含过滤唯一索引/FK）
  ```
- **审计日志**：全局 `OperLogFilter`（MVC ActionFilter）自动记录所有 POST/PUT/DELETE → A3 的建卡/启用/Run/Post/Reverse/处置 Create/Confirm/Reverse 均为 POST，自动入 `Sys_OperLog`，**服务层无需手写日志**。
- **金额精度**：金额列 `[Column(TypeName="decimal(18,2)")]`（对齐 `JournalLine.Debit/Credit`）；工作量列 `decimal(18,4)`；残值率 `decimal(7,4)`。

---

## File Structure

### 新建 — 实体（`CP6.Entity/DomainModels/Fin/`）
- `AssetEnums.cs`（`DepreciationMethod`/`AssetStatus`/`DepreciationRunStatus`/`DepreciationRunMode`/`AssetDisposalType`/`AssetDisposalStatus`）
- `AssetCategory.cs`、`AssetCard.cs`、`DepreciationRun.cs`、`DepreciationEntry.cs`、`AssetDisposal.cs`

### 修改 — 实体
- `Fin/JournalEntry.cs`（`VoucherSource` 追加 `Depreciation = 8` / `AssetDisposal = 9`）

### 新建 — 服务 / DTO（`CP6.Core/Services/Fin/`）
- `IDepreciationCalculator.cs` / `DepreciationCalculator.cs`（含 `DepreciationCalcInput`，纯函数四法）
- `AssetDtos.cs`（`DepreciationEntryDto`/`DepreciationScheduleRow`/`DisposalFinalResult`）
- `IAssetDepreciationService.cs` / `AssetDepreciationService.cs`
- `IAssetDisposalService.cs` / `AssetDisposalService.cs`

### 修改 — 服务
- `Fin/PeriodCloseService.cs`（注入可选 `IAssetDepreciationService _deprec`；`CloseAsync` 折旧钩子 + `PreCloseCheckAsync` 两类预检）
- `Fin/FinCoaTemplate.cs`（§9.1 对账：补 Role + 新增 1606/1901/6115/6711）

### 新建 — 控制器（`CP6.WebApi/Controllers/Fin/`）
- `AssetCategoryController.cs` / `AssetCardController.cs` / `AssetDepreciationController.cs` / `AssetDisposalController.cs`

### 新建 — 后台 Worker
- `CP6.WebApi/BackgroundServices/AssetDepreciationWorker.cs`

### 新建 / 修改 — 装配
- `CP6.Core/EFDbContext/CP6Context.cs`（5 DbSet + 索引 + 过滤唯一索引）
- `CP6.WebApi/Program.cs`（DI 3 服务 + Worker 注册 + 科目对账 reconcile seed + AssetCategory demo seed + 菜单 615~618 + RoleMenu + i18n `.Concat`）
- `CP6.WebApi/Seed/I18nA3ScreenSeed.cs`（五语 ZhCN/ZhTW/En/Ja/Ko）

### 新建 — 前端（`cp6.web/src/`）
- `types/fin/asset.ts`、`api/fin/asset.ts`
- `views/fin/AssetCategoryView.vue`、`AssetCardView.vue`、`AssetDepreciationView.vue`、`AssetDisposalView.vue`
- `router/index.ts`（4 路由）

### 新建 — 测试（`CP6.Tests/Fin/`）
- `DepreciationCalculatorTests.cs`（单元·四法）
- `AssetDepreciationServiceTests.cs`、`AssetDisposalServiceTests.cs`、`AssetCloseHookTests.cs`（InMemory）
- `AssetSqliteTests.cs`（SQLite：唯一/过滤唯一/FK/已结账期拒过账/并发）

---

## Phases A~H

- **Phase A**（A-1..A-3）：5 实体 + 6 enum + `VoucherSource.Depreciation=8/AssetDisposal=9` + RowVersion → DbSet/索引（含 DepreciationRun 过滤唯一索引）→ 迁移
- **Phase B**（B-1）：`IDepreciationCalculator` 纯函数四法 + 统一兜底（封顶/末期补足）+ 5 单元测试
- **Phase C**（C-1..C-3）：`IAssetDepreciationService` 资格/RunAsync/PreviewAsync/成本中心派生 → PostAsync 汇总凭证/回写/ReverseAsync → AccrueAsync 三态/SetWorkload/GetSchedule/PreCloseWorkloadCheck
- **Phase D**（D-1..D-3）：`IAssetDisposalService` CreateAsync（科目解析/快照/守卫）→ ConfirmAsync（DisposalFinal 补提 + 四类结转凭证）→ ReverseAsync（连带回滚 + PriorStatus + FA011）
- **Phase E**（E-1..E-2）：`PeriodCloseService` 折旧钩子 + PreCloseCheck 两类预检 + DI → `AssetDepreciationWorker` + 注册
- **Phase F**（F-1..F-3）：4 控制器 + 操作级权限 → 科目对账 reconcile seed + AssetCategory demo seed → 菜单 615~618 + RoleMenu
- **Phase G**（G-1..G-2）：i18n 五语 seed → 前端 4 视图 + api/类型/路由
- **Phase H**（H-1..H-2）：SQLite 结构/并发测试 → gstack 端到端 QA

---

# Phase A — 数据模型 + 迁移

## Task A-1: 5 实体 + 6 enum + `VoucherSource.Depreciation=8/AssetDisposal=9`（spec §2）

**Files:**
- Create: `CP6.Entity/DomainModels/Fin/AssetEnums.cs`、`AssetCategory.cs`、`AssetCard.cs`、`DepreciationRun.cs`、`DepreciationEntry.cs`、`AssetDisposal.cs`
- Modify: `CP6.Entity/DomainModels/Fin/JournalEntry.cs`

- [ ] **Step 1: `VoucherSource` 追加枚举值** — 在 `JournalEntry.cs` 的 `enum VoucherSource` 末尾（`FxReval = 6` 后）加：
```csharp
    /// <summary>A3 月末折旧 / 处置月补提折旧汇总凭证</summary>
    Depreciation = 8,
    /// <summary>A3 资产处置结转凭证</summary>
    AssetDisposal = 9,
```
> 不补 `BankRecon = 7`：那是 A4 的位（spec §16）。enum 留空 7 完全合法、互不冲突；A4 落地时自行补 7。

- [ ] **Step 2: `AssetEnums.cs`**
```csharp
namespace CP6.Entity.DomainModels.Fin;

/// <summary>折旧方法（A3-D1 四法）。</summary>
public enum DepreciationMethod { StraightLine = 1, DoubleDeclining = 2, SumOfYears = 3, UnitsOfProduction = 4 }

/// <summary>资产卡片状态。</summary>
public enum AssetStatus { Draft = 0, InUse = 1, FullyDepreciated = 2, Disposed = 3 }

/// <summary>折旧批次状态。</summary>
public enum DepreciationRunStatus { Draft = 0, Posted = 1, Reversed = 2 }

/// <summary>折旧批次生成路径。DisposalFinal=处置补提单资产 Run，不计入「每期单批」FA003。</summary>
public enum DepreciationRunMode { Manual = 1, Worker = 2, CloseHook = 3, DisposalFinal = 4 }

/// <summary>处置类型。</summary>
public enum AssetDisposalType { Sale = 1, Scrap = 2, Transfer = 3, InventoryLoss = 4 }

/// <summary>处置单状态。</summary>
public enum AssetDisposalStatus { Draft = 0, Confirmed = 1, Reversed = 2 }
```

- [ ] **Step 3: `AssetCategory.cs`**（spec §2.1）
```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Fin;

/// <summary>资产分类（主数据，树·驱动默认值 + 三科目路由，spec §2.1）。</summary>
[Table("Fin_AssetCategory")]
public class AssetCategory : BaseTenantEntity
{
    [Required, MaxLength(30)] public string Code { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    public Guid? ParentId { get; set; }
    public int Level { get; set; } = 1;
    public DepreciationMethod DefaultMethod { get; set; } = DepreciationMethod.StraightLine;
    public int DefaultUsefulLifeMonths { get; set; }
    [Column(TypeName = "decimal(7,4)")] public decimal DefaultSalvageRate { get; set; }
    /// <summary>固定资产科目（默认 1601）</summary>
    public Guid AssetAccountId { get; set; }
    /// <summary>累计折旧科目（默认 1602）</summary>
    public Guid AccumDeprecAccountId { get; set; }
    /// <summary>折旧费用科目（机器→5101.01 / 管理→6002 / 销售→6001，D8 路由核心，叶子科目）</summary>
    public Guid DeprecExpenseAccountId { get; set; }
    public bool IsActive { get; set; } = true;
    [Timestamp] public byte[]? RowVersion { get; set; }
}
```

- [ ] **Step 4: `AssetCard.cs`**（spec §2.2；`NetBookValue` 为 `[NotMapped]` 计算属性）
```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Fin;

/// <summary>资产卡片（核心，spec §2.2）。NetBookValue 不物化（计算属性，避免多处同步漂移）。</summary>
[Table("Fin_AssetCard")]
public class AssetCard : BaseTenantEntity
{
    [Required, MaxLength(30)] public string AssetNo { get; set; } = string.Empty;   // FA-yyyy-MM-NNNNN
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    [MaxLength(100)] public string? SpecModel { get; set; }
    public Guid CategoryId { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal OriginalValue { get; set; }
    [Column(TypeName = "decimal(7,4)")] public decimal SalvageRate { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal SalvageValue { get; set; }
    public DepreciationMethod Method { get; set; } = DepreciationMethod.StraightLine;
    public int UsefulLifeMonths { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal? TotalWorkload { get; set; }
    [MaxLength(20)] public string? WorkloadUnit { get; set; }
    public DateTime AcquisitionDate { get; set; }
    /// <summary>起折期间 yyyy-MM（= 购置日次月，D2；启用时定格）</summary>
    [MaxLength(7)] public string? DepreciationStartPeriod { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal AccumulatedDepreciation { get; set; }
    public int DepreciatedPeriods { get; set; }
    /// <summary>净值（不物化）：OriginalValue − AccumulatedDepreciation</summary>
    [NotMapped] public decimal NetBookValue => OriginalValue - AccumulatedDepreciation;
    /// <summary>折旧费用科目卡片覆盖（null=取分类默认）</summary>
    public Guid? DeprecExpenseAccountId { get; set; }
    public Guid? CostCenterId { get; set; }
    public Guid? MachineId { get; set; }
    public Guid? DeptId { get; set; }
    public AssetStatus Status { get; set; } = AssetStatus.Draft;
    [MaxLength(200)] public string? Location { get; set; }
    [MaxLength(100)] public string? Custodian { get; set; }
    /// <summary>期初建卡标记（true=不生成取得凭证、允许录初始累计）</summary>
    public bool IsOpeningImport { get; set; }
    [MaxLength(500)] public string? Remarks { get; set; }
    [Timestamp] public byte[]? RowVersion { get; set; }
}
```

- [ ] **Step 5: `DepreciationRun.cs`**（spec §2.3）
```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Fin;

/// <summary>折旧批次头（每期一批量批次；DisposalFinal 单资产 Run 不计入「每期单批」，spec §2.3）。</summary>
[Table("Fin_DepreciationRun")]
public class DepreciationRun : BaseTenantEntity
{
    [Required, MaxLength(30)] public string No { get; set; } = string.Empty;   // DEP-yyyy-MM-NNNNN
    public Guid FiscalPeriodId { get; set; }
    [MaxLength(7)] public string PeriodYearMonth { get; set; } = string.Empty;
    public DepreciationRunStatus Status { get; set; } = DepreciationRunStatus.Draft;
    public DepreciationRunMode RunMode { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal TotalAmount { get; set; }
    public int AssetCount { get; set; }
    public Guid? JournalEntryId { get; set; }
    public DateTime RunAt { get; set; }
    [MaxLength(100)] public string RunBy { get; set; } = string.Empty;
    public DateTime? PostedAt { get; set; }
    [MaxLength(100)] public string? PostedBy { get; set; }
    public DateTime? ReversedAt { get; set; }
    [MaxLength(100)] public string? ReversedBy { get; set; }
    [Timestamp] public byte[]? RowVersion { get; set; }
}
```

- [ ] **Step 6: `DepreciationEntry.cs`**（spec §2.4）
```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Fin;

/// <summary>资产级折旧明细（每资产每批，追溯，spec §2.4）。</summary>
[Table("Fin_DepreciationEntry")]
public class DepreciationEntry : BaseTenantEntity
{
    public Guid RunId { get; set; }
    public Guid AssetCardId { get; set; }
    public Guid FiscalPeriodId { get; set; }
    public DepreciationMethod Method { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal DepreciationAmount { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal OpeningAccumulated { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal ClosingAccumulated { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal OpeningNetValue { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal ClosingNetValue { get; set; }
    public Guid DeprecExpenseAccountId { get; set; }
    public Guid AccumDeprecAccountId { get; set; }
    public Guid? CostCenterId { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal? WorkloadThisPeriod { get; set; }
    [Timestamp] public byte[]? RowVersion { get; set; }
}
```

- [ ] **Step 7: `AssetDisposal.cs`**（spec §2.5）
```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Fin;

/// <summary>资产处置单（出售/报废/转让/盘亏，spec §2.5）。</summary>
[Table("Fin_AssetDisposal")]
public class AssetDisposal : BaseTenantEntity
{
    [Required, MaxLength(30)] public string No { get; set; } = string.Empty;   // FAD-yyyy-MM-NNNNN
    public Guid AssetCardId { get; set; }
    public AssetDisposalType DisposalType { get; set; }
    public DateTime DisposalDate { get; set; }
    public Guid FiscalPeriodId { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal OriginalValue { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal AccumulatedDepreciation { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal NetBookValue { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal Proceeds { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal TaxAmount { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal DisposalExpense { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal NetGainLoss { get; set; }
    public Guid ClearingAccountId { get; set; }
    public Guid GainLossAccountId { get; set; }
    /// <summary>收/付款银行 GlAccountId（收款与清理费付款共用；Proceeds>0 或 Expense>0 时必填，否则 FA010）</summary>
    public Guid? ReceiptBankAccountId { get; set; }
    public AssetDisposalStatus Status { get; set; } = AssetDisposalStatus.Draft;
    /// <summary>Confirm 时快照卡片原状态，供反冲精确还原（§4.4）</summary>
    public AssetStatus? PriorStatus { get; set; }
    public Guid? JournalEntryId { get; set; }
    public Guid? FinalDeprecEntryId { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    [MaxLength(100)] public string? ConfirmedBy { get; set; }
    public DateTime? ReversedAt { get; set; }
    [MaxLength(100)] public string? ReversedBy { get; set; }
    [MaxLength(500)] public string? Reason { get; set; }
    [Timestamp] public byte[]? RowVersion { get; set; }
}
```

- [ ] **Step 8: 构建** → `dotnet build CP6.Entity --nologo`，预期成功（实体编译通过）。

- [ ] **Step 9: 提交** → `git commit -m "feat(fin): A3 fixed-asset 5 entities + 6 enums + VoucherSource.Depreciation=8/AssetDisposal=9 (spec §2)"`

---

## Task A-2: DbSet 注册 + 索引（含「每期单批」过滤唯一索引，spec §2/§13.19）

**Files:** Modify `CP6.Core/EFDbContext/CP6Context.cs`

- [ ] **Step 1: DbSet**（Fin 区域 `JournalLines`/`CostCenters` 附近加）
```csharp
// ───── 固定资产（A3）─────
public DbSet<AssetCategory> AssetCategories { get; set; }
public DbSet<AssetCard> AssetCards { get; set; }
public DbSet<DepreciationRun> DepreciationRuns { get; set; }
public DbSet<DepreciationEntry> DepreciationEntries { get; set; }
public DbSet<AssetDisposal> AssetDisposals { get; set; }
```

- [ ] **Step 2: 索引**（`OnModelCreating` 内 Fin 索引区，`CostCenter` 索引附近加；唯一索引只声明逻辑列，TenantId 前缀由末尾反射自动补）
```csharp
// 资产分类：Code 唯一 + 树检索
modelBuilder.Entity<AssetCategory>(e =>
{
    e.HasIndex(x => x.Code).IsUnique().HasDatabaseName("UX_Fin_AssetCategory_Code");
    e.HasIndex(x => x.ParentId);
});
// 资产卡片：AssetNo 唯一 + 分类/状态/机台检索
modelBuilder.Entity<AssetCard>(e =>
{
    e.HasIndex(x => x.AssetNo).IsUnique().HasDatabaseName("UX_Fin_AssetCard_AssetNo");
    e.HasIndex(x => x.CategoryId);
    e.HasIndex(x => x.Status);
    e.HasIndex(x => x.MachineId);
});
// 折旧批次：每期仅一个【非 Reversed 批量批次】（RunMode∈{1,2,3} ∧ Status≠Reversed(2)）。
// 过滤唯一索引（DB 兜底竞态，仿 UX_Fin_JournalEntry_AutoVoucherSource）；DisposalFinal(4) 不在过滤内、不受约束。
// 自动补 TenantId 前缀 → (TenantId, FiscalPeriodId) WHERE RunMode IN (1,2,3) AND Status <> 2。
modelBuilder.Entity<DepreciationRun>(e =>
{
    e.HasIndex(x => x.FiscalPeriodId).IsUnique()
        .HasFilter("[RunMode] IN (1,2,3) AND [Status] <> 2")
        .HasDatabaseName("UX_Fin_DepreciationRun_PeriodSingleBatch");
    e.HasIndex(x => x.No);
    e.HasIndex(x => new { x.FiscalPeriodId, x.Status });
});
// 折旧明细：(RunId, AssetCardId) 唯一 + 资产检索
modelBuilder.Entity<DepreciationEntry>(e =>
{
    e.HasIndex(x => new { x.RunId, x.AssetCardId }).IsUnique().HasDatabaseName("UX_Fin_DepreciationEntry_RunAsset");
    e.HasIndex(x => x.AssetCardId);
    e.HasIndex(x => new { x.AssetCardId, x.FiscalPeriodId });   // 「本期无非 Reversed 明细」去重键查询
});
// 处置单：No 唯一 + 资产/状态检索
modelBuilder.Entity<AssetDisposal>(e =>
{
    e.HasIndex(x => x.No).IsUnique().HasDatabaseName("UX_Fin_AssetDisposal_No");
    e.HasIndex(x => x.AssetCardId);
    e.HasIndex(x => x.Status);
});
```
> 确认 `CP6Context.cs` 顶部已有 `using CP6.Entity.DomainModels.Fin;`（已存在）。**注**：`DepreciationEntry` 的「本期无非 Reversed 明细」去重靠服务层查询（Draft+Posted 跨 Run），非 DB 唯一约束（明细可跨批量批次与 DisposalFinal 共存历史）；`(AssetCardId, FiscalPeriodId)` 仅作查询索引。

- [ ] **Step 3: 构建** → `dotnet build CP6.WebApi --nologo`，预期成功。

- [ ] **Step 4: 提交** → `git commit -m "feat(fin): register A3 DbSets + indexes (AssetNo/Category-Code unique; DepreciationRun period-single-batch filtered unique; tenant-prefix auto) (spec §2)"`

---

## Task A-3: 迁移 `A3FixedAsset`（spec §2）

**Files:** Create migration（自动生成于 `CP6.Core/Migrations/`）

- [ ] **Step 1: 生成迁移** → `dotnet ef migrations add A3FixedAsset --project CP6.Core --startup-project CP6.WebApi`（会先构建；勿带 `--no-build`）。

- [ ] **Step 2: 核对生成的 `*_A3FixedAsset.cs`**：
  - `CreateTable("Fin_AssetCategory"/"Fin_AssetCard"/"Fin_DepreciationRun"/"Fin_DepreciationEntry"/"Fin_AssetDisposal")` 五张表；
  - 唯一索引 `UX_Fin_AssetCard_AssetNo` 列含 `"TenantId", "AssetNo"`；`UX_Fin_AssetCategory_Code` 列含 `"TenantId", "Code"`；`UX_Fin_DepreciationEntry_RunAsset` 列含 `"TenantId", "RunId", "AssetCardId"`；`UX_Fin_AssetDisposal_No` 列含 `"TenantId", "No"`；
  - **过滤唯一索引** `UX_Fin_DepreciationRun_PeriodSingleBatch`：列含 `"TenantId", "FiscalPeriodId"`，`filter: "[RunMode] IN (1,2,3) AND [Status] <> 2"`；
  - 5 实体含 `RowVersion`（`rowversion`/`timestamp`）列；`AssetCard.NetBookValue` **无列**（`[NotMapped]` 计算属性）；
  - `VoucherSource` 是 int enum，迁移不应出现 VoucherSource 列变更。

- [ ] **Step 3: 全量回归** → `dotnet test D:/CP6/CP6.Tests/CP6.Tests.csproj --nologo`，预期全绿（仅加表，不破坏既有）。

- [ ] **Step 4: 提交** → `git commit -m "feat(fin): A3FixedAsset migration (5 tables + AssetNo/Code unique + DepreciationRun period-single-batch filtered unique) (spec §2)"`

---

# Phase B — 折旧引擎（纯函数四法）

## Task B-1: `IDepreciationCalculator` + 四法 + 统一兜底 + 5 单元测试（spec §3.1/§13.1-5）

**Files:**
- Create: `CP6.Core/Services/Fin/IDepreciationCalculator.cs`、`CP6.Core/Services/Fin/DepreciationCalculator.cs`、`CP6.Tests/Fin/DepreciationCalculatorTests.cs`

- [ ] **Step 1: 写失败测试** `CP6.Tests/Fin/DepreciationCalculatorTests.cs`
```csharp
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;

namespace CP6.Tests.Fin;

public class DepreciationCalculatorTests
{
    private static readonly IDepreciationCalculator Calc = new DepreciationCalculator();

    private static DepreciationCalcInput Sl(int done, decimal accum) => new()
    {
        Method = DepreciationMethod.StraightLine,
        OriginalValue = 12000m, SalvageValue = 0m, UsefulLifeMonths = 12,
        DepreciatedPeriods = done, AccumulatedBefore = accum,
    };

    [Fact] // §13.1 直线法均摊 + 末期补足残差
    public void StraightLine_EvenSpread_LastPeriodTopsUp()
    {
        Assert.Equal(1000m, Calc.PeriodAmount(Sl(0, 0m)));
        Assert.Equal(1000m, Calc.PeriodAmount(Sl(5, 5000m)));
        // 末期（RemainMonths==1）一次性补足残差 = Depreciable − AccumulatedBefore
        Assert.Equal(1000m, Calc.PeriodAmount(Sl(11, 11000m)));
    }

    [Fact] // §13.2 双倍余额递减：年率×年初净值、年内12期月额恒定、末两年切直线、Y=5
    public void DoubleDeclining_YearConstant_SwitchSLLastTwoYears()
    {
        // OV=100000, Salvage=5000(5%), 5年=60月。Y=5,r=0.4
        DepreciationCalcInput In(int done, decimal accum, decimal nbvYearStart) => new()
        {
            Method = DepreciationMethod.DoubleDeclining,
            OriginalValue = 100000m, SalvageValue = 5000m, UsefulLifeMonths = 60,
            DepreciatedPeriods = done, AccumulatedBefore = accum, NetBookValueAtYearStart = nbvYearStart,
        };
        // 第1年：年额=100000×0.4=40000，月额≈3333.33
        Assert.Equal(3333.33m, Calc.PeriodAmount(In(0, 0m, 100000m)));
        // 第2年初（已提12期，累计≈40000）：年额=60000×0.4=24000，月额=2000
        Assert.Equal(2000m, Calc.PeriodAmount(In(12, 40000m, 60000m)));
        // 第4年（已提36期，进入末两年）：entryNbv=100000×0.6^3=21600，年额=(21600−5000)/2=8300，月额≈691.67
        Assert.Equal(691.67m, Calc.PeriodAmount(In(36, 78400m, 21600m)));
    }

    [Fact] // §13.2 边界：Y=2（24月）无 DDB 阶段，直接直线
    public void DoubleDeclining_TwoYears_FallsBackToStraightLine()
    {
        var input = new DepreciationCalcInput
        {
            Method = DepreciationMethod.DoubleDeclining,
            OriginalValue = 24000m, SalvageValue = 0m, UsefulLifeMonths = 24,
            DepreciatedPeriods = 0, AccumulatedBefore = 0m, NetBookValueAtYearStart = 24000m,
        };
        Assert.Equal(1000m, Calc.PeriodAmount(input));   // 24000/24
    }

    [Fact] // §13.3 年数总和：年序加权、跨年月额
    public void SumOfYears_YearWeighted()
    {
        // OV=12000,Salvage=0,3年=36月。Y=3，Σ=6。第1年权重3/6→年额6000→月500；第3年权重1/6→年额2000→月≈166.67
        DepreciationCalcInput In(int done, decimal accum) => new()
        {
            Method = DepreciationMethod.SumOfYears,
            OriginalValue = 12000m, SalvageValue = 0m, UsefulLifeMonths = 36,
            DepreciatedPeriods = done, AccumulatedBefore = accum,
        };
        Assert.Equal(500m, Calc.PeriodAmount(In(0, 0m)));
        Assert.Equal(166.67m, Calc.PeriodAmount(In(24, 10000m)));
    }

    [Fact] // §13.4 工作量法：本期/总量比例；§13.5 残值封顶
    public void UnitsOfProduction_ProRata_AndSalvageCap()
    {
        var input = new DepreciationCalcInput
        {
            Method = DepreciationMethod.UnitsOfProduction,
            OriginalValue = 11000m, SalvageValue = 1000m, UsefulLifeMonths = 0,
            DepreciatedPeriods = 0, AccumulatedBefore = 0m,
            TotalWorkload = 10000m, WorkloadThisPeriod = 500m,
        };
        Assert.Equal(500m, Calc.PeriodAmount(input));   // (11000−1000)×500/10000
        // 封顶：累计已 9800，剩可折 200，本期算 500 → 封到 200
        input.DepreciatedPeriods = 1; input.AccumulatedBefore = 9800m; input.WorkloadThisPeriod = 500m;
        Assert.Equal(200m, Calc.PeriodAmount(input));
    }

    [Fact] // §13.4 工作量法缺总量 → 抛 E-FA-008（服务层映射 FinResult）
    public void UnitsOfProduction_MissingTotal_Throws()
    {
        var input = new DepreciationCalcInput
        {
            Method = DepreciationMethod.UnitsOfProduction,
            OriginalValue = 10000m, SalvageValue = 0m, UsefulLifeMonths = 0,
            DepreciatedPeriods = 0, AccumulatedBefore = 0m,
            TotalWorkload = null, WorkloadThisPeriod = 500m,
        };
        var ex = Assert.Throws<InvalidOperationException>(() => Calc.PeriodAmount(input));
        Assert.Equal("E-FA-008", ex.Message);
    }
}
```

- [ ] **Step 2: 跑红** → `dotnet test D:/CP6/CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~DepreciationCalculator" --nologo`，预期编译失败（类型缺）。

- [ ] **Step 3: 接口 `IDepreciationCalculator.cs`**
```csharp
using CP6.Entity.DomainModels.Fin;

namespace CP6.Core.Services.Fin;

/// <summary>折旧引擎（纯函数·无 DB 依赖，spec §3.1）。给定折旧参数 + 已提期数 + 本期工作量，返回本期折旧额（已封顶残值、末期取整兜底）。</summary>
public interface IDepreciationCalculator
{
    decimal PeriodAmount(DepreciationCalcInput input);
}

/// <summary>折旧计算入参（spec §3.1）。</summary>
public sealed class DepreciationCalcInput
{
    public DepreciationMethod Method;
    public decimal OriginalValue;            // 原值
    public decimal SalvageValue;             // 残值
    public int UsefulLifeMonths;             // 可使用月数
    public int DepreciatedPeriods;           // 本期之前已提期数
    public decimal AccumulatedBefore;        // 本期之前累计折旧
    public decimal NetBookValueAtYearStart;  // 当前折旧年度起始净值（DDB 常规阶段用；服务按 §3.1 闭式填）
    public decimal? TotalWorkload;           // 工作量法预计总量
    public decimal? WorkloadThisPeriod;      // 工作量法本期量
}
```

- [ ] **Step 4: 实现 `DepreciationCalculator.cs`**
```csharp
using CP6.Entity.DomainModels.Fin;

namespace CP6.Core.Services.Fin;

/// <summary>四法折旧纯函数实现（spec §3.1）。统一兜底：封顶残值 + 末期补足残差 + 负数归零。</summary>
public sealed class DepreciationCalculator : IDepreciationCalculator
{
    public decimal PeriodAmount(DepreciationCalcInput x)
    {
        decimal depreciable = x.OriginalValue - x.SalvageValue;      // 可折总额
        int remain = x.UsefulLifeMonths - x.DepreciatedPeriods;      // 剩余月数（工作量法不参与）
        decimal raw;

        switch (x.Method)
        {
            case DepreciationMethod.StraightLine:
                raw = x.UsefulLifeMonths <= 0 ? 0m : depreciable / x.UsefulLifeMonths;
                break;

            case DepreciationMethod.DoubleDeclining:
            {
                int Y = (int)Math.Ceiling(x.UsefulLifeMonths / 12.0);
                if (Y <= 2)                                          // 无 DDB 阶段 → 直线
                {
                    raw = x.UsefulLifeMonths <= 0 ? 0m : depreciable / x.UsefulLifeMonths;
                    break;
                }
                int y = x.DepreciatedPeriods / 12 + 1;               // 1-based 年序
                decimal r = 2m / Y;
                if (y <= Y - 2)                                      // 常规 DDB：年额=年初净值×年率，年内恒定
                    raw = x.NetBookValueAtYearStart * r / 12m;
                else                                                 // 末两年切直线（两年恒定）
                {
                    decimal entryNbv = x.OriginalValue * (decimal)Math.Pow((double)(1m - r), Y - 2);
                    raw = (entryNbv - x.SalvageValue) / 2m / 12m;
                }
                break;
            }

            case DepreciationMethod.SumOfYears:
            {
                int Y = (int)Math.Ceiling(x.UsefulLifeMonths / 12.0);
                int y = (int)Math.Ceiling((x.DepreciatedPeriods + 1) / 12.0);
                decimal sum = Y * (Y + 1) / 2m;
                decimal annual = depreciable * (Y - y + 1) / sum;
                raw = annual / 12m;
                break;
            }

            case DepreciationMethod.UnitsOfProduction:
                if (x.TotalWorkload is null or <= 0m || x.WorkloadThisPeriod is null)
                    throw new InvalidOperationException("E-FA-008");
                raw = depreciable * x.WorkloadThisPeriod.Value / x.TotalWorkload.Value;
                break;

            default:
                raw = 0m;
                break;
        }

        decimal amount = Math.Round(raw, 2, MidpointRounding.AwayFromZero);
        decimal cap = depreciable - x.AccumulatedBefore;            // 累计不破可折上限
        if (amount > cap) amount = cap;
        // 末期一次性补足残差（消除累计取整误差）：直线/DDB/年数总和的最后一个月
        if (x.Method != DepreciationMethod.UnitsOfProduction && remain <= 1) amount = cap;
        if (amount < 0m) amount = 0m;
        return amount;
    }
}
```
> 工作量法无固定寿命终点（按产量耗尽可折额），故不参与「末期补足」分支；其末期由「封顶 cap」自然收口至残值。DDB 末两年的 `entryNbv` 由闭式 `OV×(1−r)^(Y−2)` 重算（年初净值为进入末两年时点值，两年恒定），与常规阶段用 `NetBookValueAtYearStart` 自洽；服务在 MVP 下按同闭式填 `NetBookValueAtYearStart`（spec §3.1 注）。

- [ ] **Step 5: 跑绿** → `dotnet test D:/CP6/CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~DepreciationCalculator" --nologo`，预期 6 测全 passed。

- [ ] **Step 6: 提交** → `git commit -m "feat(fin): A3 IDepreciationCalculator four-method pure function + salvage-cap/last-period-topup (spec §3.1)"`

---

# Phase C — 折旧服务（三路：手动 / Worker / 结账钩子）

## Task C-1: `IAssetDepreciationService` 接口 + DTO + RunAsync/PreviewAsync + 成本中心派生（spec §3.2/§3.3）

**Files:**
- Create: `CP6.Core/Services/Fin/AssetDtos.cs`、`IAssetDepreciationService.cs`、`AssetDepreciationService.cs`、`CP6.Tests/Fin/AssetDepreciationServiceTests.cs`
- Modify: `CP6.WebApi/Program.cs`（DI：`IDepreciationCalculator` + `IAssetDepreciationService`）

- [ ] **Step 1: DTO `AssetDtos.cs`**
```csharp
using CP6.Entity.DomainModels.Fin;

namespace CP6.Core.Services.Fin;

/// <summary>折旧试算/明细展示 DTO（PreviewAsync 返回，spec §3.2）。</summary>
public sealed class DepreciationEntryDto
{
    public Guid AssetCardId { get; set; }
    public string AssetNo { get; set; } = string.Empty;
    public string AssetName { get; set; } = string.Empty;
    public DepreciationMethod Method { get; set; }
    public decimal DepreciationAmount { get; set; }
    public decimal OpeningAccumulated { get; set; }
    public decimal ClosingAccumulated { get; set; }
    public Guid DeprecExpenseAccountId { get; set; }
    public Guid AccumDeprecAccountId { get; set; }
    public Guid? CostCenterId { get; set; }
    public decimal? WorkloadThisPeriod { get; set; }
}

/// <summary>单卡前瞻折旧计划行（GetScheduleAsync，spec §3.2）。</summary>
public sealed class DepreciationScheduleRow
{
    public int PeriodIndex { get; set; }       // 第几期（1 起）
    public string YearMonth { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal Accumulated { get; set; }
    public decimal NetValue { get; set; }
}

/// <summary>处置月补提结果（AccrueDisposalFinalAsync，供处置 ConfirmAsync 调用，spec §4.3）。</summary>
public sealed class DisposalFinalResult
{
    public bool Ok { get; set; }
    public string? Code { get; set; }
    public bool Skipped { get; set; }          // true=本期已有非 Reversed 明细，无需补提
    public Guid? RunId { get; set; }
    public Guid? DeprecEntryId { get; set; }
    public decimal Amount { get; set; }
}
```

- [ ] **Step 2: 写失败测试**（先 RunAsync 资格 + 次月起折 + 成本中心派生三测）`CP6.Tests/Fin/AssetDepreciationServiceTests.cs`
```csharp
using Microsoft.EntityFrameworkCore;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;

namespace CP6.Tests.Fin;

public class AssetDepreciationServiceTests
{
    // ── 测试夹具：建库 + 种 CoA + 一个分类 + 一张在用卡 ──
    private static async Task<(CP6Context db, AssetDepreciationService svc, Guid june, Guid cardId)> SetupAsync(
        DepreciationMethod method = DepreciationMethod.StraightLine, int life = 12,
        DateTime? acq = null, string startPeriod = "2026-05")
    {
        var db = TestHelper.CreateInMemoryContext();
        var gl = new GlAccountService(db);
        await gl.ImportTemplateAsync(FinCoaTemplate.CnGaap, "seed");
        var periods = new FiscalPeriodService(db, 1);
        var june = (await periods.EnsureOpenAsync(new DateTime(2026, 6, 1), "seed")).Id;

        var expAcc = (await db.GlAccounts.FirstAsync(a => a.Code == "5101.01")).Id;
        var assetAcc = (await db.GlAccounts.FirstAsync(a => a.Code == "1601")).Id;
        var accumAcc = (await db.GlAccounts.FirstAsync(a => a.Code == "1602")).Id;
        var cat = new AssetCategory { Id = Guid.NewGuid(), Code = "MC", Name = "机器设备",
            DefaultMethod = method, DefaultUsefulLifeMonths = life, DefaultSalvageRate = 0m,
            AssetAccountId = assetAcc, AccumDeprecAccountId = accumAcc, DeprecExpenseAccountId = expAcc, IsActive = true };
        db.AssetCategories.Add(cat);
        var card = new AssetCard { Id = Guid.NewGuid(), AssetNo = "FA-1", Name = "冲床", CategoryId = cat.Id,
            OriginalValue = 12000m, SalvageRate = 0m, SalvageValue = 0m, Method = method, UsefulLifeMonths = life,
            AcquisitionDate = acq ?? new DateTime(2026, 4, 15), DepreciationStartPeriod = startPeriod,
            AccumulatedDepreciation = 0m, DepreciatedPeriods = 0, Status = AssetStatus.InUse };
        db.AssetCards.Add(card);
        await db.SaveChangesAsync();

        var svc = new AssetDepreciationService(db, new DepreciationCalculator(),
            new JournalEntryService(db, periods, new FinSequenceService(db)),
            periods, new FinSequenceService(db));
        return (db, svc, june, card.Id);
    }

    [Fact] // §13.6 次月起折：起折期≤本期则计提
    public async Task RunAsync_EligibleInUse_CreatesDraftRunAndEntry()
    {
        var (db, svc, june, cardId) = await SetupAsync(startPeriod: "2026-05");
        var r = await svc.RunAsync(june, "admin", DepreciationRunMode.Manual);
        Assert.True(r.Ok, r.Code);
        var run = await db.DepreciationRuns.SingleAsync();
        Assert.Equal(DepreciationRunStatus.Draft, run.Status);
        Assert.Equal(DepreciationRunMode.Manual, run.RunMode);
        Assert.Equal(1, run.AssetCount);
        Assert.Equal(1000m, run.TotalAmount);
        var entry = await db.DepreciationEntries.SingleAsync();
        Assert.Equal(cardId, entry.AssetCardId);
        Assert.Equal(1000m, entry.DepreciationAmount);
    }

    [Fact] // §13.6 当期增加不提：起折期=本期次月（晚于本期）→ 不纳入
    public async Task RunAsync_AcquiredThisMonth_NotDepreciated()
    {
        var (db, svc, june, _) = await SetupAsync(startPeriod: "2026-07");   // 6月购置→7月起折
        var r = await svc.RunAsync(june, "admin", DepreciationRunMode.Manual);
        Assert.True(r.Ok, r.Code);
        Assert.Equal(0, (await db.DepreciationRuns.SingleAsync()).AssetCount);   // 空批次（无资格资产）
    }

    [Fact] // §13.8 RunAsync 幂等：已有非 Reversed 批量批次 → FA003
    public async Task RunAsync_SecondBatch_RejectedFA003()
    {
        var (_, svc, june, _) = await SetupAsync();
        Assert.True((await svc.RunAsync(june, "admin", DepreciationRunMode.Manual)).Ok);
        var r2 = await svc.RunAsync(june, "admin", DepreciationRunMode.Manual);
        Assert.False(r2.Ok);
        Assert.Equal("FA003", r2.Code);
    }

    [Fact] // §13.13 成本中心派生序：卡片 MachineId → CostCenter.LinkMachineId 命中
    public async Task RunAsync_CostCenter_DerivedFromMachine()
    {
        var (db, svc, june, cardId) = await SetupAsync();
        var mid = Guid.NewGuid();
        db.CostCenters.Add(new CostCenter { Id = Guid.NewGuid(), Code = "CC-M1", Name = "冲床中心",
            Type = CostCenterType.Machine, LinkMachineId = mid.ToString(), IsActive = true });
        var card = await db.AssetCards.FindAsync(cardId);
        card!.MachineId = mid; card.CostCenterId = null;
        await db.SaveChangesAsync();
        await svc.RunAsync(june, "admin", DepreciationRunMode.Manual);
        var entry = await db.DepreciationEntries.SingleAsync();
        var cc = await db.CostCenters.SingleAsync();
        Assert.Equal(cc.Id, entry.CostCenterId);
    }
}
```
> 注：`CostCenter.LinkMachineId` 是 `string?`，卡片 `MachineId` 是 `Guid?`——机台派生匹配按 `LinkMachineId == MachineId.Value.ToString()`（C-1 Step4 实现按此）。

- [ ] **Step 3: 接口 `IAssetDepreciationService.cs`**（全签名一处声明；C-1/C-2/C-3 逐 Task 实现）
```csharp
using CP6.Entity.DomainModels.Fin;

namespace CP6.Core.Services.Fin;

public interface IAssetDepreciationService
{
    Task<List<DepreciationEntryDto>> PreviewAsync(Guid periodId);                     // 试算不落库
    Task<FinResult> RunAsync(Guid periodId, string userId, DepreciationRunMode mode); // 生成 Draft 批次+明细（幂等）
    Task<FinResult> SetWorkloadAsync(Guid entryId, decimal workload);                 // 工作量法补录本期量
    Task<FinResult> PostAsync(Guid runId, string userId);                             // 拼汇总凭证→AutoPost→回写卡片
    Task<FinResult> ReverseAsync(Guid runId, string userId, string reason);           // 红冲+回滚卡片
    Task<FinResult> AccrueAsync(Guid periodId, string userId);                        // 三态幂等 Run/Post；结账钩子/兜底
    Task<FinResult> PreCloseWorkloadCheckAsync(Guid periodId);                        // §6.1 硬校验：工作量法未录量
    Task<DisposalFinalResult> AccrueDisposalFinalAsync(Guid assetCardId, Guid periodId, string userId); // §4.3 处置补提
    Task<List<DepreciationScheduleRow>> GetScheduleAsync(Guid assetCardId);           // 单卡前瞻折旧计划
}
```

- [ ] **Step 4: 实现 `AssetDepreciationService.cs`**（本 Task 实现 ctor + 资格集 + Build 入参 + 成本中心派生 + RunAsync + PreviewAsync；其余方法先 `throw new NotImplementedException()` 占位以过编译，C-2/C-3 填）
```csharp
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Fin;

/// <summary>资产折旧服务（三路：手动 Run/Post、Worker 备草稿、结账钩子 Accrue，spec §3.2）。仿 FxRevaluationService 直建凭证。</summary>
public sealed class AssetDepreciationService : IAssetDepreciationService
{
    private readonly CP6Context _db;
    private readonly IDepreciationCalculator _calc;
    private readonly IJournalEntryService _journal;
    private readonly IFiscalPeriodService _periods;
    private readonly IFinSequenceService _seq;

    public AssetDepreciationService(CP6Context db, IDepreciationCalculator calc, IJournalEntryService journal,
        IFiscalPeriodService periods, IFinSequenceService seq)
    {
        _db = db; _calc = calc; _journal = journal; _periods = periods; _seq = seq;
    }

    // ── 资格集（spec §3.2 计提资格，期 P）──
    // card.Status==InUse ∧ DepreciationStartPeriod ≤ P ∧ Accum < OriginalValue−Salvage ∧ 本期无任何非 Reversed 明细
    private async Task<List<AssetCard>> EligibleAsync(string periodYm)
    {
        // 本期已有非 Reversed 折旧明细的资产（跨所有 Run，含 Draft/Posted），去重防重复计提
        var doneCardIds = await (from de in _db.DepreciationEntries
                                 join run in _db.DepreciationRuns on de.RunId equals run.Id
                                 where de.FiscalPeriodId != Guid.Empty
                                       && run.Status != DepreciationRunStatus.Reversed
                                       && run.PeriodYearMonth == periodYm
                                 select de.AssetCardId).Distinct().ToListAsync();

        var cards = await _db.AssetCards
            .Where(c => c.Status == AssetStatus.InUse
                        && c.DepreciationStartPeriod != null
                        && string.Compare(c.DepreciationStartPeriod, periodYm) <= 0
                        && c.AccumulatedDepreciation < c.OriginalValue - c.SalvageValue
                        && !doneCardIds.Contains(c.Id))
            .ToListAsync();
        return cards;
    }

    private async Task<Guid?> DeriveCostCenterAsync(AssetCard card)
    {
        if (card.CostCenterId.HasValue) return card.CostCenterId;          // ① 卡片显式
        if (card.MachineId.HasValue)                                       // ② 机台派生（LinkMachineId 字符串匹配）
        {
            var mid = card.MachineId.Value.ToString();
            var cc = await _db.CostCenters.FirstOrDefaultAsync(c => c.LinkMachineId == mid && c.IsActive);
            if (cc != null) return cc.Id;
        }
        // ③ 部门型成本中心：现有 CostCenter 无 Dept 关联字段（仅 LinkMachineId/Type/ParentId），
        //    MVP 不做 DeptId→成本中心派生（spec §15 deferred）；需要时增 CostCenter.LinkDeptId 再启用。
        return null;                                                       // ④ 无
    }

    private async Task<DepreciationEntry> BuildEntryAsync(AssetCard card, AssetCategory cat, Guid periodId, string periodYm)
    {
        int Y = (int)Math.Ceiling(card.UsefulLifeMonths / 12.0);
        int y = card.DepreciatedPeriods / 12 + 1;
        decimal nbvYearStart = Y <= 0 ? card.NetBookValue
            : card.OriginalValue * (decimal)Math.Pow((double)(1m - 2m / Math.Max(Y, 1)), Math.Max(y - 1, 0)); // §3.1 闭式

        var input = new DepreciationCalcInput
        {
            Method = card.Method, OriginalValue = card.OriginalValue, SalvageValue = card.SalvageValue,
            UsefulLifeMonths = card.UsefulLifeMonths, DepreciatedPeriods = card.DepreciatedPeriods,
            AccumulatedBefore = card.AccumulatedDepreciation, NetBookValueAtYearStart = nbvYearStart,
            TotalWorkload = card.TotalWorkload, WorkloadThisPeriod = null,   // 工作量法 Run 时占位、Post 前补录
        };
        // 工作量法 Run 阶段不算额（待补录），其余方法立即算
        decimal amount = card.Method == DepreciationMethod.UnitsOfProduction ? 0m : _calc.PeriodAmount(input);

        var expAcc = card.DeprecExpenseAccountId ?? cat.DeprecExpenseAccountId;  // 卡片覆盖 > 分类默认
        return new DepreciationEntry
        {
            Id = Guid.NewGuid(), AssetCardId = card.Id, FiscalPeriodId = periodId, Method = card.Method,
            DepreciationAmount = amount,
            OpeningAccumulated = card.AccumulatedDepreciation, ClosingAccumulated = card.AccumulatedDepreciation + amount,
            OpeningNetValue = card.NetBookValue, ClosingNetValue = card.NetBookValue - amount,
            DeprecExpenseAccountId = expAcc, AccumDeprecAccountId = cat.AccumDeprecAccountId,
            CostCenterId = await DeriveCostCenterAsync(card),
            WorkloadThisPeriod = null,
        };
    }

    public async Task<FinResult> RunAsync(Guid periodId, string userId, DepreciationRunMode mode)
    {
        var period = await _db.FiscalPeriods.FindAsync(periodId);
        if (period == null) return FinResult.Fail("FA007");
        if (period.Status != PeriodStatus.Open) return FinResult.Fail("FA007");
        var ym = $"{period.Year:D4}-{period.Month:D2}";

        // 守卫：无非 Reversed 批量批次（FA003；DisposalFinal 不计入）
        bool batchExists = await _db.DepreciationRuns.AnyAsync(r => r.FiscalPeriodId == periodId
            && r.RunMode != DepreciationRunMode.DisposalFinal && r.Status != DepreciationRunStatus.Reversed);
        if (batchExists) return FinResult.Fail("FA003");

        var cards = await EligibleAsync(ym);
        var catIds = cards.Select(c => c.CategoryId).Distinct().ToList();
        var cats = await _db.AssetCategories.Where(c => catIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id);

        var run = new DepreciationRun
        {
            Id = Guid.NewGuid(), No = await _seq.NextAsync("DEP", new DateTime(period.Year, period.Month, 1)),
            FiscalPeriodId = periodId, PeriodYearMonth = ym, Status = DepreciationRunStatus.Draft, RunMode = mode,
            RunAt = DateTime.Now, RunBy = userId,
        };
        decimal total = 0m;
        foreach (var card in cards)
        {
            if (!cats.TryGetValue(card.CategoryId, out var cat)) return FinResult.Fail("FA001");
            var entry = await BuildEntryAsync(card, cat, periodId, ym);
            entry.RunId = run.Id;
            _db.DepreciationEntries.Add(entry);
            total += entry.DepreciationAmount;
        }
        run.TotalAmount = total;
        run.AssetCount = cards.Count;
        _db.DepreciationRuns.Add(run);
        await _db.SaveChangesAsync();
        return FinResult.Pass();
    }

    public async Task<List<DepreciationEntryDto>> PreviewAsync(Guid periodId)
    {
        var period = await _db.FiscalPeriods.FindAsync(periodId);
        if (period == null) return new();
        var ym = $"{period.Year:D4}-{period.Month:D2}";
        var cards = await EligibleAsync(ym);
        var catIds = cards.Select(c => c.CategoryId).Distinct().ToList();
        var cats = await _db.AssetCategories.Where(c => catIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id);
        var list = new List<DepreciationEntryDto>();
        foreach (var card in cards)
        {
            if (!cats.TryGetValue(card.CategoryId, out var cat)) continue;
            var e = await BuildEntryAsync(card, cat, periodId, ym);   // dry-run，不落库
            list.Add(new DepreciationEntryDto
            {
                AssetCardId = card.Id, AssetNo = card.AssetNo, AssetName = card.Name, Method = card.Method,
                DepreciationAmount = e.DepreciationAmount, OpeningAccumulated = e.OpeningAccumulated,
                ClosingAccumulated = e.ClosingAccumulated, DeprecExpenseAccountId = e.DeprecExpenseAccountId,
                AccumDeprecAccountId = e.AccumDeprecAccountId, CostCenterId = e.CostCenterId,
                WorkloadThisPeriod = card.Method == DepreciationMethod.UnitsOfProduction ? (decimal?)null : null,
            });
        }
        return list;
    }

    public Task<FinResult> SetWorkloadAsync(Guid entryId, decimal workload) => throw new NotImplementedException();
    public Task<FinResult> PostAsync(Guid runId, string userId) => throw new NotImplementedException();
    public Task<FinResult> ReverseAsync(Guid runId, string userId, string reason) => throw new NotImplementedException();
    public Task<FinResult> AccrueAsync(Guid periodId, string userId) => throw new NotImplementedException();
    public Task<FinResult> PreCloseWorkloadCheckAsync(Guid periodId) => throw new NotImplementedException();
    public Task<DisposalFinalResult> AccrueDisposalFinalAsync(Guid a, Guid p, string u) => throw new NotImplementedException();
    public Task<List<DepreciationScheduleRow>> GetScheduleAsync(Guid assetCardId) => throw new NotImplementedException();
}
```
> 校验 `PeriodStatus` 枚举：复用既有 `FiscalPeriod.Status`（`PeriodStatus.Open/Closed`，参 `PeriodCloseService`）。`DepreciationStartPeriod` 与 `ym` 均 `yyyy-MM` 字符串、字典序 = 时间序，`string.Compare` 安全。

- [ ] **Step 5: DI 注册**（`Program.cs` L119-145 Fin 区，`IFxRevaluationService` 附近加）
```csharp
builder.Services.AddSingleton<CP6.Core.Services.Fin.IDepreciationCalculator, CP6.Core.Services.Fin.DepreciationCalculator>();
builder.Services.AddScoped<CP6.Core.Services.Fin.IAssetDepreciationService, CP6.Core.Services.Fin.AssetDepreciationService>();
```
> `IDepreciationCalculator` 无状态纯函数 → `AddSingleton`。

- [ ] **Step 6: 跑绿** → `dotnet test D:/CP6/CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~AssetDepreciationServiceTests" --nologo`，预期 4 测 passed。

- [ ] **Step 7: 提交** → `git commit -m "feat(fin): A3 AssetDepreciationService eligibility + RunAsync/PreviewAsync + cost-center derivation (spec §3.2/§3.3)"`

---

## Task C-2: PostAsync 汇总凭证 + 回写卡片 + ReverseAsync（spec §3.2/§5.1/§13.9-10）

**Files:**
- Modify: `CP6.Core/Services/Fin/AssetDepreciationService.cs`（实现 `PostAsync`/`ReverseAsync`/`SetWorkloadAsync`）
- Modify: `CP6.Tests/Fin/AssetDepreciationServiceTests.cs`（加 Post/Reverse 两测）

- [ ] **Step 1: 写失败测试**（追加到 `AssetDepreciationServiceTests`）
```csharp
    [Fact] // §13.9 PostAsync 汇总凭证：借折旧费用×成本中心分行、贷累计折旧、借贷平、回写卡片
    public async Task PostAsync_BuildsSummaryVoucher_WritesBackCard()
    {
        var (db, svc, june, cardId) = await SetupAsync();
        await svc.RunAsync(june, "admin", DepreciationRunMode.Manual);
        var run = await db.DepreciationRuns.SingleAsync();
        var r = await svc.PostAsync(run.Id, "admin");
        Assert.True(r.Ok, r.Code);

        run = await db.DepreciationRuns.SingleAsync();
        Assert.Equal(DepreciationRunStatus.Posted, run.Status);
        Assert.NotNull(run.JournalEntryId);
        var je = await db.JournalEntries.Include(e => e.Lines).SingleAsync(e => e.Id == run.JournalEntryId);
        Assert.Equal(VoucherSource.Depreciation, je.Source);
        Assert.Equal(JournalStatus.Posted, je.Status);
        Assert.Equal(je.Lines.Sum(l => l.Debit), je.Lines.Sum(l => l.Credit));   // 借贷平
        Assert.Equal(1000m, je.Lines.Sum(l => l.Debit));
        var card = await db.AssetCards.FindAsync(cardId);
        Assert.Equal(1000m, card!.AccumulatedDepreciation);
        Assert.Equal(1, card.DepreciatedPeriods);
    }

    [Fact] // §13.10 ReverseAsync：红冲 + 卡片累计/期数原子回滚
    public async Task ReverseAsync_RedInks_AndRollsBackCard()
    {
        var (db, svc, june, cardId) = await SetupAsync();
        await svc.RunAsync(june, "admin", DepreciationRunMode.Manual);
        var run = await db.DepreciationRuns.SingleAsync();
        await svc.PostAsync(run.Id, "admin");
        var r = await svc.ReverseAsync(run.Id, "admin", "误提");
        Assert.True(r.Ok, r.Code);
        run = await db.DepreciationRuns.SingleAsync();
        Assert.Equal(DepreciationRunStatus.Reversed, run.Status);
        var card = await db.AssetCards.FindAsync(cardId);
        Assert.Equal(0m, card!.AccumulatedDepreciation);
        Assert.Equal(0, card.DepreciatedPeriods);
        Assert.Equal(AssetStatus.InUse, card.Status);
    }
```

- [ ] **Step 2: 实现 `PostAsync`**（替换占位 `PostAsync`）
```csharp
    public async Task<FinResult> PostAsync(Guid runId, string userId)
    {
        var run = await _db.DepreciationRuns.FindAsync(runId);
        if (run == null) return FinResult.Fail("FA006");
        if (run.Status != DepreciationRunStatus.Draft) return FinResult.Fail("FA009");
        if (run.JournalEntryId != null) return FinResult.Pass();   // 幂等

        var entries = await _db.DepreciationEntries.Where(e => e.RunId == runId).ToListAsync();
        // 工作量法守卫：所有工作量法明细须已补录本期量（FA008）
        if (entries.Any(e => e.Method == DepreciationMethod.UnitsOfProduction && e.WorkloadThisPeriod == null))
            return FinResult.Fail("FA008");

        var period = await _db.FiscalPeriods.FindAsync(run.FiscalPeriodId);
        var voucherDate = new DateTime(period!.Year, period.Month, 1).AddMonths(1).AddDays(-1);   // 期末

        // 汇总凭证：借方按 (费用科目, 成本中心) 分组分行；贷方按累计折旧科目分组
        var lines = new List<JournalLine>();
        int lineNo = 1;
        foreach (var g in entries.Where(e => e.DepreciationAmount > 0m)
                     .GroupBy(e => new { e.DeprecExpenseAccountId, e.CostCenterId }))
            lines.Add(new JournalLine { LineNo = lineNo++, AccountId = g.Key.DeprecExpenseAccountId,
                Debit = g.Sum(e => e.DepreciationAmount), Credit = 0m, CostCenterId = g.Key.CostCenterId });
        foreach (var g in entries.Where(e => e.DepreciationAmount > 0m).GroupBy(e => e.AccumDeprecAccountId))
            lines.Add(new JournalLine { LineNo = lineNo++, AccountId = g.Key,
                Debit = 0m, Credit = g.Sum(e => e.DepreciationAmount) });

        if (lines.Count == 0) { run.Status = DepreciationRunStatus.Posted; run.PostedAt = DateTime.Now; run.PostedBy = userId; await _db.SaveChangesAsync(); return FinResult.Pass(); }

        var je = new JournalEntry
        {
            Id = Guid.NewGuid(), VoucherDate = voucherDate, Source = VoucherSource.Depreciation,
            SourceDocNo = run.No, Description = $"月末折旧 {run.PeriodYearMonth}", Lines = lines,
        };
        var post = await _journal.AutoPostAsync(je);
        if (!post.Ok) return post;

        // 回写每卡累计/期数/状态
        var cardIds = entries.Select(e => e.AssetCardId).ToList();
        var cards = await _db.AssetCards.Where(c => cardIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id);
        foreach (var e in entries)
        {
            if (!cards.TryGetValue(e.AssetCardId, out var card)) continue;
            card.AccumulatedDepreciation += e.DepreciationAmount;
            card.DepreciatedPeriods += 1;
            if (card.AccumulatedDepreciation >= card.OriginalValue - card.SalvageValue)
                card.Status = AssetStatus.FullyDepreciated;
        }
        run.Status = DepreciationRunStatus.Posted;
        run.JournalEntryId = je.Id;
        run.PostedAt = DateTime.Now;
        run.PostedBy = userId;
        await _db.SaveChangesAsync();
        return FinResult.Pass();
    }
```

- [ ] **Step 3: 实现 `ReverseAsync`**（替换占位；含 FA011 反冲次序守卫）
```csharp
    public async Task<FinResult> ReverseAsync(Guid runId, string userId, string reason)
    {
        var run = await _db.DepreciationRuns.FindAsync(runId);
        if (run == null) return FinResult.Fail("FA006");
        if (run.Status != DepreciationRunStatus.Posted) return FinResult.Fail("FA009");
        // FA011：DisposalFinal Run 不可独立反冲（应经处置反冲连带，§4.4/§8.5）
        if (run.RunMode == DepreciationRunMode.DisposalFinal) return FinResult.Fail("FA011");

        var entries = await _db.DepreciationEntries.Where(e => e.RunId == runId).ToListAsync();
        var cardIds = entries.Select(e => e.AssetCardId).ToList();
        // FA011：批内任一资产已被处置（有非 Reversed 处置单）→ 须先反冲处置
        bool anyDisposed = await _db.AssetDisposals.AnyAsync(d => cardIds.Contains(d.AssetCardId)
            && d.Status != AssetDisposalStatus.Reversed);
        if (anyDisposed) return FinResult.Fail("FA011");

        if (run.JournalEntryId != null)
        {
            var rev = await _journal.ReverseAsync(run.JournalEntryId.Value, userId, reason, autoPost: true);
            if (!rev.Ok) return rev;
        }
        var cards = await _db.AssetCards.Where(c => cardIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id);
        foreach (var e in entries)
        {
            if (!cards.TryGetValue(e.AssetCardId, out var card)) continue;
            card.AccumulatedDepreciation -= e.DepreciationAmount;
            card.DepreciatedPeriods -= 1;
            if (card.Status == AssetStatus.FullyDepreciated
                && card.AccumulatedDepreciation < card.OriginalValue - card.SalvageValue)
                card.Status = AssetStatus.InUse;
        }
        run.Status = DepreciationRunStatus.Reversed;
        run.ReversedAt = DateTime.Now;
        run.ReversedBy = userId;
        await _db.SaveChangesAsync();
        return FinResult.Pass();
    }
```

- [ ] **Step 4: 实现 `SetWorkloadAsync`**（替换占位；工作量法补录本期量并重算明细额）
```csharp
    public async Task<FinResult> SetWorkloadAsync(Guid entryId, decimal workload)
    {
        var entry = await _db.DepreciationEntries.FindAsync(entryId);
        if (entry == null) return FinResult.Fail("FA006");
        if (entry.Method != DepreciationMethod.UnitsOfProduction) return FinResult.Fail("FA008");
        var run = await _db.DepreciationRuns.FindAsync(entry.RunId);
        if (run == null || run.Status != DepreciationRunStatus.Draft) return FinResult.Fail("FA009");
        var card = await _db.AssetCards.FindAsync(entry.AssetCardId);
        if (card == null) return FinResult.Fail("FA006");
        if (card.TotalWorkload is null or <= 0m) return FinResult.Fail("FA008");

        var amount = _calc.PeriodAmount(new DepreciationCalcInput
        {
            Method = DepreciationMethod.UnitsOfProduction, OriginalValue = card.OriginalValue,
            SalvageValue = card.SalvageValue, UsefulLifeMonths = card.UsefulLifeMonths,
            DepreciatedPeriods = card.DepreciatedPeriods, AccumulatedBefore = card.AccumulatedDepreciation,
            TotalWorkload = card.TotalWorkload, WorkloadThisPeriod = workload,
        });
        entry.WorkloadThisPeriod = workload;
        entry.DepreciationAmount = amount;
        entry.ClosingAccumulated = entry.OpeningAccumulated + amount;
        entry.ClosingNetValue = entry.OpeningNetValue - amount;
        // 同步批次合计
        run.TotalAmount = await _db.DepreciationEntries.Where(e => e.RunId == run.Id && e.Id != entry.Id)
            .SumAsync(e => e.DepreciationAmount) + amount;
        await _db.SaveChangesAsync();
        return FinResult.Pass();
    }
```

- [ ] **Step 5: 跑绿** → `dotnet test D:/CP6/CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~AssetDepreciationServiceTests" --nologo`，预期 6 测 passed。

- [ ] **Step 6: 提交** → `git commit -m "feat(fin): A3 depreciation PostAsync summary voucher + card writeback + ReverseAsync (FA011 order guard) + SetWorkloadAsync (spec §3.2/§5.1)"`

---

## Task C-3: AccrueAsync 三态 + PreCloseWorkloadCheck + AccrueDisposalFinal + GetSchedule（spec §3.2/§4.3/§6.1）

**Files:**
- Modify: `CP6.Core/Services/Fin/AssetDepreciationService.cs`（实现剩余 4 方法）
- Modify: `CP6.Tests/Fin/AssetDepreciationServiceTests.cs`（加 Accrue 三态 + 工作量法预检两测）

- [ ] **Step 1: 写失败测试**（追加）
```csharp
    [Fact] // §13.12 AccrueAsync 三态：无批次→Run+Post；已有 Draft→Post 之；已 Posted→Pass
    public async Task AccrueAsync_ThreeStates()
    {
        // ① 无批次 → 自动 Run+Post
        var (db, svc, june, cardId) = await SetupAsync();
        var r1 = await svc.AccrueAsync(june, "admin");
        Assert.True(r1.Ok, r1.Code);
        var run = await db.DepreciationRuns.SingleAsync();
        Assert.Equal(DepreciationRunStatus.Posted, run.Status);
        Assert.Equal(DepreciationRunMode.CloseHook, run.RunMode);
        // ③ 已 Posted → 幂等 Pass（不新建）
        var r3 = await svc.AccrueAsync(june, "admin");
        Assert.True(r3.Ok);
        Assert.Equal(1, await db.DepreciationRuns.CountAsync());

        // ② 已有 Worker Draft → 直接 Post 之（另起一库）
        var (db2, svc2, june2, _) = await SetupAsync();
        await svc2.RunAsync(june2, "worker", DepreciationRunMode.Worker);   // Draft
        var r2 = await svc2.AccrueAsync(june2, "admin");
        Assert.True(r2.Ok, r2.Code);
        Assert.Equal(1, await db2.DepreciationRuns.CountAsync());           // 未新建
        Assert.Equal(DepreciationRunStatus.Posted, (await db2.DepreciationRuns.SingleAsync()).Status);
    }

    [Fact] // §6.1 硬校验：工作量法在用资产本期未录量 → PreCloseWorkloadCheck 返回 FA008
    public async Task PreCloseWorkloadCheck_UoPMissingWorkload_FA008()
    {
        var (db, svc, june, _) = await SetupAsync(method: DepreciationMethod.UnitsOfProduction, life: 0);
        var card = await db.AssetCards.FirstAsync();
        card.TotalWorkload = 10000m; card.WorkloadUnit = "件";
        await db.SaveChangesAsync();
        var r = await svc.PreCloseWorkloadCheckAsync(june);
        Assert.False(r.Ok);
        Assert.Equal("FA008", r.Code);
    }
```

- [ ] **Step 2: 实现 `AccrueAsync`（三态幂等）**（替换占位）
```csharp
    public async Task<FinResult> AccrueAsync(Guid periodId, string userId)
    {
        // 批量批次（DisposalFinal 不算）
        var batch = await _db.DepreciationRuns
            .Where(r => r.FiscalPeriodId == periodId && r.RunMode != DepreciationRunMode.DisposalFinal
                        && r.Status != DepreciationRunStatus.Reversed)
            .OrderByDescending(r => r.RunAt).FirstOrDefaultAsync();

        if (batch is { Status: DepreciationRunStatus.Posted }) return FinResult.Pass();   // ① 已 Posted
        if (batch is { Status: DepreciationRunStatus.Draft })                              // ② 既有 Draft → Post 之
            return await PostAsync(batch.Id, userId);

        var run = await RunAsync(periodId, userId, DepreciationRunMode.CloseHook);         // ③ 无 → Run+Post
        if (!run.Ok) return run;
        var created = await _db.DepreciationRuns.FirstAsync(r => r.FiscalPeriodId == periodId
            && r.RunMode == DepreciationRunMode.CloseHook && r.Status == DepreciationRunStatus.Draft);
        return await PostAsync(created.Id, userId);
    }
```

- [ ] **Step 3: 实现 `PreCloseWorkloadCheckAsync`**（替换占位）
```csharp
    public async Task<FinResult> PreCloseWorkloadCheckAsync(Guid periodId)
    {
        var period = await _db.FiscalPeriods.FindAsync(periodId);
        if (period == null) return FinResult.Fail("FA007");
        var ym = $"{period.Year:D4}-{period.Month:D2}";
        var eligible = await EligibleAsync(ym);   // 本期应提且尚无非 Reversed 明细的在用资产
        // 工作量法资产：本期既无 Posted 明细、又未录量 → 结账钩子的 Accrue 必触 FA008，前置硬阻断
        bool anyMissing = eligible.Any(c => c.Method == DepreciationMethod.UnitsOfProduction);
        return anyMissing ? FinResult.Fail("FA008") : FinResult.Pass();
    }
```
> 口径：只要存在「本期应提的工作量法在用资产」就硬阻断（提示先建批次 Run 并录量再结账）。已 Run 且录量者其明细已落、`EligibleAsync` 因「本期已有非 Reversed 明细」自动排除，故不会误拦。

- [ ] **Step 4: 实现 `AccrueDisposalFinalAsync`**（替换占位；处置月补提单资产 DisposalFinal Run + 独立折旧凭证，spec §4.3/§5.3）
```csharp
    public async Task<DisposalFinalResult> AccrueDisposalFinalAsync(Guid assetCardId, Guid periodId, string userId)
    {
        var period = await _db.FiscalPeriods.FindAsync(periodId);
        if (period == null) return new() { Ok = false, Code = "FA007" };
        var ym = $"{period.Year:D4}-{period.Month:D2}";

        // 若本期已有该资产任何非 Reversed 明细（跨所有 Run，含批量先于处置）→ 跳过补提
        bool already = await (from de in _db.DepreciationEntries
                              join run in _db.DepreciationRuns on de.RunId equals run.Id
                              where de.AssetCardId == assetCardId && run.PeriodYearMonth == ym
                                    && run.Status != DepreciationRunStatus.Reversed
                              select de.Id).AnyAsync();
        if (already) return new() { Ok = true, Skipped = true };

        var card = await _db.AssetCards.FindAsync(assetCardId);
        if (card == null) return new() { Ok = false, Code = "FA006" };
        var cat = await _db.AssetCategories.FindAsync(card.CategoryId);
        if (cat == null) return new() { Ok = false, Code = "FA001" };
        // 已达上限/未起折则补提额 0，但仍建零额明细以占「本期已提」位（防后续批量重提）。
        var entry = await BuildEntryAsync(card, cat, periodId, ym);
        if (card.Method == DepreciationMethod.UnitsOfProduction)
            entry.DepreciationAmount = 0m;   // 处置补提不强制录量，按 0（口径：工作量法处置月不补提，可后续扩展）

        var run = new DepreciationRun
        {
            Id = Guid.NewGuid(), No = await _seq.NextAsync("DEP", new DateTime(period.Year, period.Month, 1)),
            FiscalPeriodId = periodId, PeriodYearMonth = ym, Status = DepreciationRunStatus.Draft,
            RunMode = DepreciationRunMode.DisposalFinal, AssetCount = 1, TotalAmount = entry.DepreciationAmount,
            RunAt = DateTime.Now, RunBy = userId,
        };
        entry.RunId = run.Id;
        _db.DepreciationRuns.Add(run);
        _db.DepreciationEntries.Add(entry);
        await _db.SaveChangesAsync();

        // 过其独立折旧凭证（§5.3，与处置结转凭证解耦），并回写卡片累计
        var post = await PostAsync(run.Id, userId);
        if (!post.Ok) return new() { Ok = false, Code = post.Code };
        return new() { Ok = true, RunId = run.Id, DeprecEntryId = entry.Id, Amount = entry.DepreciationAmount };
    }
```

- [ ] **Step 5: 实现 `GetScheduleAsync`**（替换占位；单卡前瞻折旧计划·纯算不落库）
```csharp
    public async Task<List<DepreciationScheduleRow>> GetScheduleAsync(Guid assetCardId)
    {
        var card = await _db.AssetCards.FindAsync(assetCardId);
        var rows = new List<DepreciationScheduleRow>();
        if (card == null || string.IsNullOrEmpty(card.DepreciationStartPeriod)) return rows;

        decimal accum = card.AccumulatedDepreciation;
        int done = card.DepreciatedPeriods;
        var ym = DateTime.ParseExact(card.DepreciationStartPeriod + "-01", "yyyy-MM-dd", null).AddMonths(done);
        decimal cap = card.OriginalValue - card.SalvageValue;
        int Y = (int)Math.Ceiling(card.UsefulLifeMonths / 12.0);
        for (int i = 1; i <= 600 && accum < cap; i++)   // 护栏 600 期
        {
            int y = done / 12 + 1;
            decimal nbvYearStart = Y <= 0 ? card.OriginalValue - accum
                : card.OriginalValue * (decimal)Math.Pow((double)(1m - 2m / Math.Max(Y, 1)), Math.Max(y - 1, 0));
            decimal amount = card.Method == DepreciationMethod.UnitsOfProduction ? 0m : _calc.PeriodAmount(new DepreciationCalcInput
            {
                Method = card.Method, OriginalValue = card.OriginalValue, SalvageValue = card.SalvageValue,
                UsefulLifeMonths = card.UsefulLifeMonths, DepreciatedPeriods = done, AccumulatedBefore = accum,
                NetBookValueAtYearStart = nbvYearStart, TotalWorkload = card.TotalWorkload, WorkloadThisPeriod = null,
            });
            if (amount <= 0m) break;
            accum += amount; done += 1;
            rows.Add(new DepreciationScheduleRow { PeriodIndex = i, YearMonth = ym.ToString("yyyy-MM"),
                Amount = amount, Accumulated = accum, NetValue = card.OriginalValue - accum });
            ym = ym.AddMonths(1);
        }
        return rows;
    }
```
> 工作量法因未来产量未知，计划表返回空（前端提示「工作量法按实际产量计提，无前瞻计划」）。

- [ ] **Step 6: 跑绿** → `dotnet test D:/CP6/CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~AssetDepreciationServiceTests" --nologo`，预期 8 测 passed。

- [ ] **Step 7: 提交** → `git commit -m "feat(fin): A3 AccrueAsync three-state + PreCloseWorkloadCheck + AccrueDisposalFinal + GetSchedule (spec §3.2/§4.3/§6.1)"`

---

# Phase D — 处置（全套·经清理科目结转）

## Task D-1: `IAssetDisposalService` + CreateAsync（科目解析/快照/守卫，spec §4.1/§4.2）

**Files:**
- Create: `CP6.Core/Services/Fin/IAssetDisposalService.cs`、`AssetDisposalService.cs`、`CP6.Tests/Fin/AssetDisposalServiceTests.cs`
- Modify: `CP6.WebApi/Program.cs`（DI）

- [ ] **Step 1: 写失败测试**（CreateAsync 守卫 + 科目解析 + 完全折旧可处置）`CP6.Tests/Fin/AssetDisposalServiceTests.cs`
```csharp
using Microsoft.EntityFrameworkCore;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;

namespace CP6.Tests.Fin;

public class AssetDisposalServiceTests
{
    // 夹具：库 + CoA(含 A3 科目) + 一张在用卡（原值 12000，已提 8000）
    private static async Task<(CP6Context db, AssetDisposalService disp, AssetDepreciationService dep, Guid june, AssetCard card)>
        SetupAsync(AssetStatus status = AssetStatus.InUse, decimal accum = 8000m)
    {
        var db = TestHelper.CreateInMemoryContext();
        await new GlAccountService(db).ImportTemplateAsync(FinCoaTemplate.CnGaap, "seed");
        var periods = new FiscalPeriodService(db, 1);
        var june = (await periods.EnsureOpenAsync(new DateTime(2026, 6, 1), "seed")).Id;
        var seq = new FinSequenceService(db);
        var jes = new JournalEntryService(db, periods, seq);

        var expAcc = (await db.GlAccounts.FirstAsync(a => a.Code == "5101.01")).Id;
        var cat = new AssetCategory { Id = Guid.NewGuid(), Code = "MC", Name = "机器设备",
            DefaultMethod = DepreciationMethod.StraightLine, DefaultUsefulLifeMonths = 12, DefaultSalvageRate = 0m,
            AssetAccountId = (await db.GlAccounts.FirstAsync(a => a.Code == "1601")).Id,
            AccumDeprecAccountId = (await db.GlAccounts.FirstAsync(a => a.Code == "1602")).Id,
            DeprecExpenseAccountId = expAcc, IsActive = true };
        db.AssetCategories.Add(cat);
        var card = new AssetCard { Id = Guid.NewGuid(), AssetNo = "FA-9", Name = "旧冲床", CategoryId = cat.Id,
            OriginalValue = 12000m, SalvageRate = 0m, SalvageValue = 0m, Method = DepreciationMethod.StraightLine,
            UsefulLifeMonths = 12, AcquisitionDate = new DateTime(2025, 6, 1), DepreciationStartPeriod = "2025-07",
            AccumulatedDepreciation = accum, DepreciatedPeriods = 8, Status = status };
        db.AssetCards.Add(card);
        await db.SaveChangesAsync();

        var dep = new AssetDepreciationService(db, new DepreciationCalculator(), jes, periods, seq);
        var disp = new AssetDisposalService(db, jes, periods, seq, dep);
        return (db, disp, dep, june, card);
    }

    [Fact] // §4.2 出售有价款但无收款账户 → FA010
    public async Task CreateAsync_SaleWithProceeds_NoBank_FA010()
    {
        var (db, disp, _, june, card) = await SetupAsync();
        var d = new AssetDisposal { AssetCardId = card.Id, DisposalType = AssetDisposalType.Sale,
            DisposalDate = new DateTime(2026, 6, 10), FiscalPeriodId = june, Proceeds = 5000m, ReceiptBankAccountId = null };
        var r = await disp.CreateAsync(d, "admin");
        Assert.False(r.Ok);
        Assert.Equal("FA010", r.Code);
    }

    [Fact] // §4.1 科目解析：盘亏 → 清理 1901 / 损益 6711
    public async Task CreateAsync_InventoryLoss_ResolvesClearing1901_Loss6711()
    {
        var (db, disp, _, june, card) = await SetupAsync();
        var d = new AssetDisposal { AssetCardId = card.Id, DisposalType = AssetDisposalType.InventoryLoss,
            DisposalDate = new DateTime(2026, 6, 10), FiscalPeriodId = june };
        var r = await disp.CreateAsync(d, "admin");
        Assert.True(r.Ok, r.Code);
        var saved = await db.AssetDisposals.SingleAsync();
        var clearing = await db.GlAccounts.FindAsync(saved.ClearingAccountId);
        var loss = await db.GlAccounts.FindAsync(saved.GainLossAccountId);
        Assert.Equal("1901", clearing!.Code);
        Assert.Equal("6711", loss!.Code);
        Assert.Equal(4000m, saved.NetBookValue);   // 12000−8000
    }

    [Fact] // §13.15 完全折旧资产可处置（CreateAsync 不拒）
    public async Task CreateAsync_FullyDepreciated_Allowed()
    {
        var (db, disp, _, june, card) = await SetupAsync(status: AssetStatus.FullyDepreciated, accum: 12000m);
        var d = new AssetDisposal { AssetCardId = card.Id, DisposalType = AssetDisposalType.Scrap,
            DisposalDate = new DateTime(2026, 6, 10), FiscalPeriodId = june };
        var r = await disp.CreateAsync(d, "admin");
        Assert.True(r.Ok, r.Code);
    }
}
```

- [ ] **Step 2: 接口 `IAssetDisposalService.cs`**
```csharp
using CP6.Entity.DomainModels.Fin;

namespace CP6.Core.Services.Fin;

public interface IAssetDisposalService
{
    Task<FinResult> CreateAsync(AssetDisposal d, string userId);            // 快照+算损益+解析科目（Draft）
    Task<FinResult> ConfirmAsync(Guid id, string userId);                  // 补提+拼结转凭证→AutoPost→卡片 Disposed
    Task<FinResult> ReverseAsync(Guid id, string userId, string reason);   // 红冲+恢复卡片 PriorStatus
    Task<AssetDisposal?> GetAsync(Guid id);
    Task<List<AssetDisposal>> ListAsync(AssetDisposalStatus? status, Guid? assetCardId);
}
```

- [ ] **Step 3: 实现 `AssetDisposalService.cs`**（本 Task 实现 ctor + Role 解析 + CreateAsync + Get/List；Confirm/Reverse 先占位）
```csharp
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Fin;

/// <summary>资产处置服务（出售/报废/转让/盘亏，经清理科目结转，spec §4）。仿 FxRevaluationService 直建凭证。</summary>
public sealed class AssetDisposalService : IAssetDisposalService
{
    private readonly CP6Context _db;
    private readonly IJournalEntryService _journal;
    private readonly IFiscalPeriodService _periods;
    private readonly IFinSequenceService _seq;
    private readonly IAssetDepreciationService _deprec;   // 处置月补提委托

    public AssetDisposalService(CP6Context db, IJournalEntryService journal, IFiscalPeriodService periods,
        IFinSequenceService seq, IAssetDepreciationService deprec)
    {
        _db = db; _journal = journal; _periods = periods; _seq = seq; _deprec = deprec;
    }

    private async Task<Guid?> RoleIdAsync(string role)
        => (await _db.GlAccounts.FirstOrDefaultAsync(a => a.Role == role && a.IsActive))?.Id;

    // 科目解析（spec §4.1）：清理科目 + 损益科目（按 DisposalType；报废损益方向在 Confirm 定）
    private async Task<(Guid? clearing, Guid? gainLoss)> ResolveAccountsAsync(AssetDisposalType type, decimal netGainLoss)
    {
        Guid? clearing = type == AssetDisposalType.InventoryLoss
            ? await RoleIdAsync("PENDING_PROPERTY_LOSS")   // 1901
            : await RoleIdAsync("ASSET_CLEARING");         // 1606
        Guid? gainLoss = type switch
        {
            AssetDisposalType.Sale or AssetDisposalType.Transfer => await RoleIdAsync("ASSET_DISPOSAL_PL"),  // 6115
            AssetDisposalType.InventoryLoss => await RoleIdAsync("NON_OP_EXPENSE"),                          // 6711
            // 报废：净损→营业外支出 6711；净收（残料）→营业外收入 4301
            AssetDisposalType.Scrap => netGainLoss >= 0
                ? await RoleIdAsync("NON_OP_INCOME")        // 4301
                : await RoleIdAsync("NON_OP_EXPENSE"),      // 6711
            _ => null,
        };
        return (clearing, gainLoss);
    }

    public async Task<FinResult> CreateAsync(AssetDisposal d, string userId)
    {
        var card = await _db.AssetCards.FindAsync(d.AssetCardId);
        if (card == null) return FinResult.Fail("FA006");
        // 守卫：资产可处置（InUse / FullyDepreciated；非 Draft/非 Disposed）
        if (card.Status is not (AssetStatus.InUse or AssetStatus.FullyDepreciated)) return FinResult.Fail("FA002");
        // 守卫：无非 Reversed 处置单（FA002）
        if (await _db.AssetDisposals.AnyAsync(x => x.AssetCardId == d.AssetCardId && x.Status != AssetDisposalStatus.Reversed))
            return FinResult.Fail("FA002");
        // 守卫：期间开启（FA007）
        var period = await _db.FiscalPeriods.FindAsync(d.FiscalPeriodId);
        if (period == null || period.Status != PeriodStatus.Open) return FinResult.Fail("FA007");
        // 守卫：有价款或有清理费用但未指定收/付款账户 → FA010
        if ((d.Proceeds > 0m || d.DisposalExpense > 0m) && d.ReceiptBankAccountId == null) return FinResult.Fail("FA010");

        // 快照当前卡片值（处置月补提在 Confirm 完成；Confirm 时再以补提后值重算）
        d.OriginalValue = card.OriginalValue;
        d.AccumulatedDepreciation = card.AccumulatedDepreciation;
        d.NetBookValue = card.OriginalValue - card.AccumulatedDepreciation;
        d.NetGainLoss = d.Proceeds - d.DisposalExpense - d.NetBookValue;

        var (clearing, gainLoss) = await ResolveAccountsAsync(d.DisposalType, d.NetGainLoss);
        if (clearing == null || gainLoss == null) return FinResult.Fail("FA001");
        d.ClearingAccountId = clearing.Value;
        d.GainLossAccountId = gainLoss.Value;

        d.Id = d.Id == Guid.Empty ? Guid.NewGuid() : d.Id;
        d.No = await _seq.NextAsync("FAD", d.DisposalDate);
        d.Status = AssetDisposalStatus.Draft;
        _db.AssetDisposals.Add(d);
        await _db.SaveChangesAsync();
        return FinResult.Pass();
    }

    public Task<FinResult> ConfirmAsync(Guid id, string userId) => throw new NotImplementedException();
    public Task<FinResult> ReverseAsync(Guid id, string userId, string reason) => throw new NotImplementedException();
    public Task<AssetDisposal?> GetAsync(Guid id) => _db.AssetDisposals.FirstOrDefaultAsync(x => x.Id == id);
    public Task<List<AssetDisposal>> ListAsync(AssetDisposalStatus? status, Guid? assetCardId)
        => _db.AssetDisposals
            .Where(x => (status == null || x.Status == status) && (assetCardId == null || x.AssetCardId == assetCardId))
            .OrderByDescending(x => x.DisposalDate).ToListAsync();
}
```
> 报废损益方向在 Create 阶段按快照净值预解析（Confirm 补提后若方向翻转，由 ConfirmAsync 重解析覆盖，见 D-2）。

- [ ] **Step 4: DI 注册**（`Program.cs` Fin 区，紧随 `IAssetDepreciationService`）
```csharp
builder.Services.AddScoped<CP6.Core.Services.Fin.IAssetDisposalService, CP6.Core.Services.Fin.AssetDisposalService>();
```

- [ ] **Step 5: 跑绿** → `dotnet test D:/CP6/CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~AssetDisposalServiceTests" --nologo`，预期 3 测 passed。

- [ ] **Step 6: 提交** → `git commit -m "feat(fin): A3 AssetDisposalService CreateAsync (account resolution + snapshot + disposable guards FA002/FA007/FA010) (spec §4.1/§4.2)"`

---

## Task D-2: ConfirmAsync（补提 + 四类结转凭证，spec §4.3/§5.2/§13.11/§13.14）

**Files:**
- Modify: `CP6.Core/Services/Fin/AssetDisposalService.cs`（实现 `ConfirmAsync` + 凭证构建私有方法）
- Modify: `CP6.Tests/Fin/AssetDisposalServiceTests.cs`（加四类凭证 + 处置先于批量两测）

- [ ] **Step 1: 写失败测试**（追加）
```csharp
    [Fact] // §13.11 出售结转凭证：1606 行内轧平、借贷平、卡片 Disposed
    public async Task ConfirmAsync_Sale_BalancedVoucher_CardDisposed()
    {
        var (db, disp, _, june, card) = await SetupAsync(accum: 8000m);   // NBV=4000
        var bank = (await db.GlAccounts.FirstAsync(a => a.Code == "1002")).Id;
        var d = new AssetDisposal { AssetCardId = card.Id, DisposalType = AssetDisposalType.Sale,
            DisposalDate = new DateTime(2026, 6, 10), FiscalPeriodId = june,
            Proceeds = 5000m, TaxAmount = 650m, ReceiptBankAccountId = bank };
        Assert.True((await disp.CreateAsync(d, "admin")).Ok);
        var r = await disp.ConfirmAsync(d.Id, "admin");
        Assert.True(r.Ok, r.Code);

        var saved = await db.AssetDisposals.SingleAsync();
        Assert.Equal(AssetDisposalStatus.Confirmed, saved.Status);
        Assert.NotNull(saved.JournalEntryId);
        var je = await db.JournalEntries.Include(e => e.Lines).SingleAsync(e => e.Id == saved.JournalEntryId);
        Assert.Equal(VoucherSource.AssetDisposal, je.Source);
        Assert.Equal(je.Lines.Sum(l => l.Debit), je.Lines.Sum(l => l.Credit));   // 借贷平
        var clearing = (await db.GlAccounts.FirstAsync(a => a.Code == "1606")).Id;
        var clearLines = je.Lines.Where(l => l.AccountId == clearing);
        Assert.Equal(clearLines.Sum(l => l.Debit), clearLines.Sum(l => l.Credit)); // 1606 轧平
        var savedCard = await db.AssetCards.FindAsync(card.Id);
        Assert.Equal(AssetStatus.Disposed, savedCard!.Status);
        Assert.Equal(AssetStatus.InUse, saved.PriorStatus);
    }

    [Fact] // §13.14 处置先于批量：Confirm 建 DisposalFinal 补提 → 批量 RunAsync 不被 FA003 阻断、排除该资产
    public async Task ConfirmThenBatch_DisposalFinalNotBlockingBatch()
    {
        var (db, disp, dep, june, card) = await SetupAsync(accum: 1000m);   // 在用、本期未提
        // 另起一张在用卡参与批量
        var card2 = new AssetCard { Id = Guid.NewGuid(), AssetNo = "FA-10", Name = "另一台", CategoryId = card.CategoryId,
            OriginalValue = 12000m, SalvageValue = 0m, Method = DepreciationMethod.StraightLine, UsefulLifeMonths = 12,
            AcquisitionDate = new DateTime(2025, 6, 1), DepreciationStartPeriod = "2025-07",
            AccumulatedDepreciation = 1000m, DepreciatedPeriods = 1, Status = AssetStatus.InUse };
        db.AssetCards.Add(card2); await db.SaveChangesAsync();

        var d = new AssetDisposal { AssetCardId = card.Id, DisposalType = AssetDisposalType.Scrap,
            DisposalDate = new DateTime(2026, 6, 10), FiscalPeriodId = june };
        Assert.True((await disp.CreateAsync(d, "admin")).Ok);
        Assert.True((await disp.ConfirmAsync(d.Id, "admin")).Ok);   // 建 DisposalFinal 补提 card

        var run = await dep.RunAsync(june, "admin", DepreciationRunMode.Manual);
        Assert.True(run.Ok, run.Code);   // 未被 FA003 阻断
        var batch = await db.DepreciationRuns.SingleAsync(r => r.RunMode == DepreciationRunMode.Manual);
        Assert.Equal(1, batch.AssetCount);   // 只含 card2（被处置的 card 已排除）
        var entry = await db.DepreciationEntries.SingleAsync(e => e.RunId == batch.Id);
        Assert.Equal(card2.Id, entry.AssetCardId);
    }
```

- [ ] **Step 2: 实现凭证构建私有方法 + `ConfirmAsync`**（替换占位 `ConfirmAsync`）
```csharp
    // 处置结转凭证行（spec §5.2）。assetAcc=1601, accumAcc=1602 由分类取；clearing/gainLoss/bank 由处置单取。
    private async Task<List<JournalLine>> BuildDisposalLinesAsync(AssetDisposal d, Guid assetAcc, Guid accumAcc)
    {
        var lines = new List<JournalLine>();
        int n = 1;
        void Add(Guid acc, decimal dr, decimal cr) { if (dr > 0m || cr > 0m) lines.Add(new JournalLine { LineNo = n++, AccountId = acc, Debit = dr, Credit = cr }); }

        if (d.DisposalType == AssetDisposalType.InventoryLoss)
        {
            // 盘亏（经 1901，损→6711）
            Add(accumAcc, d.AccumulatedDepreciation, 0m);     // 借 累计折旧
            Add(d.ClearingAccountId, d.NetBookValue, 0m);     // 借 待处理财产损溢(1901)
            Add(assetAcc, 0m, d.OriginalValue);               // 贷 固定资产
            Add(d.GainLossAccountId, d.NetBookValue, 0m);     // 借 营业外支出(6711)
            Add(d.ClearingAccountId, 0m, d.NetBookValue);     // 贷 待处理财产损溢(1901)
            return lines;
        }

        // 出售/转让/报废（经 1606）
        Add(accumAcc, d.AccumulatedDepreciation, 0m);         // 借 累计折旧
        Add(d.ClearingAccountId, d.NetBookValue, 0m);         // 借 固定资产清理(1606)
        Add(assetAcc, 0m, d.OriginalValue);                   // 贷 固定资产
        if (d.Proceeds > 0m)
        {
            Add(d.ReceiptBankAccountId!.Value, d.Proceeds + d.TaxAmount, 0m);  // 借 银行 = 价款+税
            Add(d.ClearingAccountId, 0m, d.Proceeds);                         // 贷 1606 = 价款
            if (d.TaxAmount > 0m)
            {
                var vat = await RoleIdAsync("TAX_OUTPUT");                     // 贷 应交税费—销项税(2221.02)
                if (vat == null) throw new InvalidOperationException("FA001");
                Add(vat.Value, 0m, d.TaxAmount);
            }
        }
        if (d.DisposalExpense > 0m)
        {
            Add(d.ClearingAccountId, d.DisposalExpense, 0m);                  // 借 1606 = 清理费
            Add(d.ReceiptBankAccountId!.Value, 0m, d.DisposalExpense);        // 贷 银行
        }
        // 结转 1606 余额(=NetGainLoss)
        if (d.NetGainLoss > 0m) { Add(d.ClearingAccountId, d.NetGainLoss, 0m); Add(d.GainLossAccountId, 0m, d.NetGainLoss); }
        else if (d.NetGainLoss < 0m) { Add(d.GainLossAccountId, -d.NetGainLoss, 0m); Add(d.ClearingAccountId, 0m, -d.NetGainLoss); }
        return lines;
    }

    public async Task<FinResult> ConfirmAsync(Guid id, string userId)
    {
        var d = await _db.AssetDisposals.FindAsync(id);
        if (d == null) return FinResult.Fail("FA006");
        if (d.Status != AssetDisposalStatus.Draft) return FinResult.Fail("FA009");
        var card = await _db.AssetCards.FindAsync(d.AssetCardId);
        if (card == null) return FinResult.Fail("FA006");
        var cat = await _db.AssetCategories.FindAsync(card.CategoryId);
        if (cat == null) return FinResult.Fail("FA001");
        var priorStatus = card.Status;   // 处置前真实状态（补提前快照，供反冲精确还原）

        // ① 处置月补提（D2 当期处置照提）——独立 DisposalFinal Run + 独立折旧凭证（幂等：本期已提则跳过）
        var fin = await _deprec.AccrueDisposalFinalAsync(card.Id, d.FiscalPeriodId, userId);
        if (!fin.Ok) return FinResult.Fail(fin.Code ?? "FA006");
        if (!fin.Skipped) d.FinalDeprecEntryId = fin.DeprecEntryId;

        // ② 重算净值/损益（以补提后累计）
        card = await _db.AssetCards.FindAsync(d.AssetCardId);   // 重读（补提已回写）
        d.AccumulatedDepreciation = card!.AccumulatedDepreciation;
        d.NetBookValue = card.OriginalValue - card.AccumulatedDepreciation;
        d.NetGainLoss = d.Proceeds - d.DisposalExpense - d.NetBookValue;
        // 报废方向可能随补提翻转 → 重解析损益科目
        var (clearing, gainLoss) = await ResolveAccountsAsync(d.DisposalType, d.NetGainLoss);
        if (clearing == null || gainLoss == null) return FinResult.Fail("FA001");
        d.ClearingAccountId = clearing.Value;
        d.GainLossAccountId = gainLoss.Value;

        // ③ 拼单张结转凭证 → AutoPost
        var lines = await BuildDisposalLinesAsync(d, cat.AssetAccountId, cat.AccumDeprecAccountId);
        var je = new JournalEntry
        {
            Id = Guid.NewGuid(), VoucherDate = d.DisposalDate, Source = VoucherSource.AssetDisposal,
            SourceDocNo = d.No, Description = $"资产处置 {d.No}（{d.DisposalType}）", Lines = lines,
        };
        var post = await _journal.AutoPostAsync(je);
        if (!post.Ok) return post;

        d.JournalEntryId = je.Id;
        d.Status = AssetDisposalStatus.Confirmed;
        d.PriorStatus = priorStatus;
        d.ConfirmedAt = DateTime.Now;
        d.ConfirmedBy = userId;
        card.Status = AssetStatus.Disposed;
        await _db.SaveChangesAsync();
        return FinResult.Pass();
    }
```
> **可恢复性（spec §8.1②）**：补提与结转是两次独立 `AutoPostAsync`。若补提成功而结转失败，补提作为合法当期折旧留存、处置仍 `Draft`；重试 `ConfirmAsync` 时 `AccrueDisposalFinalAsync` 因「本期已有非 Reversed 明细」返回 `Skipped=true`、跳过补提、仅补结转——Confirm 重入幂等可续。

- [ ] **Step 3: 跑绿** → `dotnet test D:/CP6/CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~AssetDisposalServiceTests" --nologo`，预期 5 测 passed。

- [ ] **Step 4: 提交** → `git commit -m "feat(fin): A3 disposal ConfirmAsync (DisposalFinal accrue + four-type carry-over voucher, 1606/1901 netted) (spec §4.3/§5.2)"`

---

## Task D-3: ReverseAsync（连带回滚补提 + PriorStatus 还原 + FA011，spec §4.4/§8.5/§13.15-16）

**Files:**
- Modify: `CP6.Core/Services/Fin/AssetDisposalService.cs`（实现 `ReverseAsync`）
- Modify: `CP6.Tests/Fin/AssetDisposalServiceTests.cs`（加反冲还原 + FA011 两测）

- [ ] **Step 1: 写失败测试**（追加）
```csharp
    [Fact] // §13.15 处置反冲还原 PriorStatus（FullyDepreciated 不回 InUse）+ 连带回滚补提
    public async Task ReverseAsync_RestoresPriorStatus_AndRollsBackFinalAccrual()
    {
        var (db, disp, _, june, card) = await SetupAsync(status: AssetStatus.FullyDepreciated, accum: 12000m);
        var d = new AssetDisposal { AssetCardId = card.Id, DisposalType = AssetDisposalType.Scrap,
            DisposalDate = new DateTime(2026, 6, 10), FiscalPeriodId = june };
        Assert.True((await disp.CreateAsync(d, "admin")).Ok);
        Assert.True((await disp.ConfirmAsync(d.Id, "admin")).Ok);

        var r = await disp.ReverseAsync(d.Id, "admin", "撤销");
        Assert.True(r.Ok, r.Code);
        var saved = await db.AssetDisposals.SingleAsync();
        Assert.Equal(AssetDisposalStatus.Reversed, saved.Status);
        var savedCard = await db.AssetCards.FindAsync(card.Id);
        Assert.Equal(AssetStatus.FullyDepreciated, savedCard!.Status);   // 还原原状态，非 InUse
        Assert.Equal(12000m, savedCard.AccumulatedDepreciation);          // 补提（0 额）回滚后不变
    }

    [Fact] // §13.16 反冲次序：批内含已处置资产的批量批次反冲 → FA011
    public async Task ReverseBatch_WithDisposedAsset_FA011()
    {
        var (db, disp, dep, june, card) = await SetupAsync(accum: 1000m);
        // 先批量计提并过账（含 card）
        await dep.RunAsync(june, "admin", DepreciationRunMode.Manual);
        var batch = await db.DepreciationRuns.SingleAsync(x => x.RunMode == DepreciationRunMode.Manual);
        await dep.PostAsync(batch.Id, "admin");
        // 再处置 card（批量先于处置）
        var d = new AssetDisposal { AssetCardId = card.Id, DisposalType = AssetDisposalType.Scrap,
            DisposalDate = new DateTime(2026, 6, 10), FiscalPeriodId = june };
        Assert.True((await disp.CreateAsync(d, "admin")).Ok);
        Assert.True((await disp.ConfirmAsync(d.Id, "admin")).Ok);
        // 此时反冲批量批次 → 因批内 card 已处置 → FA011
        var rev = await dep.ReverseAsync(batch.Id, "admin", "误提");
        Assert.False(rev.Ok);
        Assert.Equal("FA011", rev.Code);
    }
```

- [ ] **Step 2: 实现 `ReverseAsync`**（替换占位）
```csharp
    public async Task<FinResult> ReverseAsync(Guid id, string userId, string reason)
    {
        var d = await _db.AssetDisposals.FindAsync(id);
        if (d == null) return FinResult.Fail("FA006");
        if (d.Status != AssetDisposalStatus.Confirmed) return FinResult.Fail("FA009");

        // FA011：关联补提 DisposalFinal Run 不得已被独立反冲（应经本反冲连带回滚）
        DepreciationRun? finalRun = null;
        if (d.FinalDeprecEntryId != null)
        {
            var fe = await _db.DepreciationEntries.FindAsync(d.FinalDeprecEntryId.Value);
            if (fe != null)
            {
                finalRun = await _db.DepreciationRuns.FindAsync(fe.RunId);
                if (finalRun is { Status: DepreciationRunStatus.Reversed }) return FinResult.Fail("FA011");
            }
        }

        // ① 红冲结转凭证
        if (d.JournalEntryId != null)
        {
            var rev = await _journal.ReverseAsync(d.JournalEntryId.Value, userId, reason, autoPost: true);
            if (!rev.Ok) return rev;
        }

        var card = await _db.AssetCards.FindAsync(d.AssetCardId);

        // ② 连带红冲补提 DisposalFinal 折旧凭证 + 回滚卡片补提额（不走 _deprec.ReverseAsync，避免 DisposalFinal 自守卫拦截）
        if (finalRun != null && finalRun.Status == DepreciationRunStatus.Posted)
        {
            if (finalRun.JournalEntryId != null)
            {
                var rev2 = await _journal.ReverseAsync(finalRun.JournalEntryId.Value, userId, reason, autoPost: true);
                if (!rev2.Ok) return rev2;
            }
            var fe = await _db.DepreciationEntries.FindAsync(d.FinalDeprecEntryId!.Value);
            if (card != null && fe != null)
            {
                card.AccumulatedDepreciation -= fe.DepreciationAmount;
                card.DepreciatedPeriods -= 1;
            }
            finalRun.Status = DepreciationRunStatus.Reversed;
            finalRun.ReversedAt = DateTime.Now;
            finalRun.ReversedBy = userId;
        }

        // ③ 卡片还原至处置前状态（PriorStatus，不一律 InUse）
        if (card != null && d.PriorStatus.HasValue) card.Status = d.PriorStatus.Value;
        d.Status = AssetDisposalStatus.Reversed;
        d.ReversedAt = DateTime.Now;
        d.ReversedBy = userId;
        d.Reason = reason;
        await _db.SaveChangesAsync();
        return FinResult.Pass();
    }
```

- [ ] **Step 3: 跑绿** → `dotnet test D:/CP6/CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~AssetDisposalServiceTests" --nologo`，预期 7 测 passed。

- [ ] **Step 4: 全量回归** → `dotnet test D:/CP6/CP6.Tests/CP6.Tests.csproj --nologo`，预期全绿。

- [ ] **Step 5: 提交** → `git commit -m "feat(fin): A3 disposal ReverseAsync (connected DisposalFinal rollback + PriorStatus restore + FA011 order guard) (spec §4.4/§8.5)"`

---

# Phase E — 期间集成（结账钩子）+ 后台 Worker

## Task E-1: PeriodCloseService 折旧钩子 + PreCloseCheck 两类预检（spec §6.1）

**Files:**
- Modify: `CP6.Core/Services/Fin/PeriodCloseService.cs`（ctor 注入可选 `IAssetDepreciationService`；`CloseAsync` 钩子；`PreCloseCheckAsync` 硬校验）
- Create: `CP6.Tests/Fin/AssetCloseHookTests.cs`

- [ ] **Step 1: 写失败测试** `CP6.Tests/Fin/AssetCloseHookTests.cs`
```csharp
using Microsoft.EntityFrameworkCore;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;

namespace CP6.Tests.Fin;

public class AssetCloseHookTests
{
    private static async Task<(CP6Context db, PeriodCloseService close, AssetDepreciationService dep, Guid june, Guid cardId)>
        SetupAsync(DepreciationMethod method = DepreciationMethod.StraightLine)
    {
        var db = TestHelper.CreateInMemoryContext();
        await new GlAccountService(db).ImportTemplateAsync(FinCoaTemplate.CnGaap, "seed");
        var periods = new FiscalPeriodService(db, 1);
        var june = (await periods.EnsureOpenAsync(new DateTime(2026, 6, 1), "seed")).Id;
        var seq = new FinSequenceService(db);
        var jes = new JournalEntryService(db, periods, seq);
        var dep = new AssetDepreciationService(db, new DepreciationCalculator(), jes, periods, seq);

        var expAcc = (await db.GlAccounts.FirstAsync(a => a.Code == "5101.01")).Id;
        var cat = new AssetCategory { Id = Guid.NewGuid(), Code = "MC", Name = "机器设备", DefaultMethod = method,
            DefaultUsefulLifeMonths = 12, AssetAccountId = (await db.GlAccounts.FirstAsync(a => a.Code == "1601")).Id,
            AccumDeprecAccountId = (await db.GlAccounts.FirstAsync(a => a.Code == "1602")).Id,
            DeprecExpenseAccountId = expAcc, IsActive = true };
        db.AssetCategories.Add(cat);
        var card = new AssetCard { Id = Guid.NewGuid(), AssetNo = "FA-1", Name = "冲床", CategoryId = cat.Id,
            OriginalValue = 12000m, SalvageValue = 0m, Method = method, UsefulLifeMonths = 12,
            TotalWorkload = method == DepreciationMethod.UnitsOfProduction ? 10000m : null,
            AcquisitionDate = new DateTime(2026, 4, 15), DepreciationStartPeriod = "2026-05", Status = AssetStatus.InUse };
        db.AssetCards.Add(card);
        await db.SaveChangesAsync();

        var trial = new TrialBalanceService(db);
        var close = new PeriodCloseService(db, periods, trial, deprec: dep);   // 折旧钩子注入
        return (db, close, dep, june, card.Id);
    }

    [Fact] // §6.1 结账钩子：直线法资产本期未计提 → CloseAsync 自动 Accrue（Run+Post）
    public async Task CloseAsync_AutoAccruesDepreciation()
    {
        var (db, close, _, june, cardId) = await SetupAsync();
        var r = await close.CloseAsync(june, "admin");
        Assert.True(r.Ok, r.Code);
        Assert.Equal(DepreciationRunStatus.Posted, (await db.DepreciationRuns.SingleAsync()).Status);
        Assert.Equal(1000m, (await db.AssetCards.FindAsync(cardId))!.AccumulatedDepreciation);
    }

    [Fact] // §6.1 硬校验：工作量法在用资产本期未录量 → PreCloseCheck 硬阻断结账（FA008）
    public async Task CloseAsync_UoPMissingWorkload_HardBlocked()
    {
        var (db, close, _, june, _) = await SetupAsync(method: DepreciationMethod.UnitsOfProduction);
        var r = await close.CloseAsync(june, "admin");
        Assert.False(r.Ok);
        Assert.Equal("FA008", r.Code);
        Assert.Empty(await db.DepreciationRuns.ToListAsync());   // 未建批次（前置拦下）
    }
}
```

- [ ] **Step 2: 跑红** → `--filter "FullyQualifiedName~AssetCloseHookTests"`，预期编译失败（`PeriodCloseService` 构造无 `deprec` 形参）。

- [ ] **Step 3: 改 `PeriodCloseService` ctor**（加可选 `IAssetDepreciationService _deprec`，仿 `_reval` 模式）。在字段区加 `private readonly IAssetDepreciationService? _deprec;`，ctor 形参末尾加 `IAssetDepreciationService? deprec = null`，体内 `_deprec = deprec;`：
```csharp
private readonly IAssetDepreciationService? _deprec;

public PeriodCloseService(CP6Context db, IFiscalPeriodService periods, ITrialBalanceService trial,
    IFxRevaluationService? reval = null, IAssetDepreciationService? deprec = null, ILogger<PeriodCloseService>? logger = null)
{
    _db = db; _periods = periods; _trial = trial; _reval = reval; _deprec = deprec; _logger = logger;
}
```

- [ ] **Step 4: `CloseAsync` 插折旧钩子**（在 `if (_reval != null)` 块**之前**——折旧先于汇兑重估）：
```csharp
var check = await PreCloseCheckAsync(periodId);
if (!check.Ok) return check;

// ★ A3 §6.1：结账前兜底计提折旧（三态幂等：Posted→Pass / Draft→Post / 无→Run+Post）。失败阻断结账。
if (_deprec != null)
{
    var dr = await _deprec.AccrueAsync(periodId, userId);
    if (!dr.Ok) return dr;
}

if (_reval != null)   // 既有汇兑重估，折旧之后
{
    var rr = await _reval.RevalueAsync(periodId, userId);
    if (!rr.Ok) return rr;
}
```

- [ ] **Step 5: `PreCloseCheckAsync` 加硬校验**（方法体末尾、返回 `Pass()` 之前）：
```csharp
// ★ A3 §6.1 硬预检：工作量法在用资产本期未录工作量 → 硬阻断（结账钩子 Accrue 会 Run/Post 必触 FA008，前移明示）
if (_deprec != null)
{
    var wl = await _deprec.PreCloseWorkloadCheckAsync(periodId);
    if (!wl.Ok) return wl;
}
```

- [ ] **Step 6: 跑绿** → `--filter "FullyQualifiedName~AssetCloseHookTests"`，预期 2 测 passed。

- [ ] **Step 7: 回归既有期间结账测试** → `dotnet test D:/CP6/CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~PeriodClose" --nologo`，预期全绿（ctor 新增形参有默认值、既有调用不受影响）。

- [ ] **Step 8: 提交** → `git commit -m "feat(fin): A3 PeriodClose depreciation hook (AccrueAsync before FxReval) + PreCloseCheck UoP hard guard (spec §6.1)"`

---

## Task E-2: `AssetDepreciationWorker`（月末备草稿，不自动过账，spec §6.2）

**Files:**
- Create: `CP6.WebApi/BackgroundServices/AssetDepreciationWorker.cs`
- Modify: `CP6.WebApi/Program.cs`（`AddHostedService`）

- [ ] **Step 1: 实现 Worker**（仿 `FinReconciliationWorker` + `TenantScopeRunner.ForEachTenantAsync`）
```csharp
using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.BackgroundServices;

/// <summary>月末折旧 Worker（spec §6.2）：每日检查，当前开启期为月末且无本期批量批次 → 生成 Draft 草稿（不过账）。
/// 过账权交人工复核或结账钩子兜底。</summary>
public class AssetDepreciationWorker : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AssetDepreciationWorker> _logger;

    public AssetDepreciationWorker(IServiceScopeFactory scopeFactory, ILogger<AssetDepreciationWorker> logger)
    {
        _scopeFactory = scopeFactory; _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("固定资产折旧 worker 启动");
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                await ProcessOnceAsync(stoppingToken);
                await Task.Delay(Interval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally { _logger.LogInformation("固定资产折旧 worker 停止"); }
    }

    public async Task ProcessOnceAsync(CancellationToken ct = default)
    {
        await TenantScopeRunner.ForEachTenantAsync(_scopeFactory, async (sp, tenantId, c) =>
        {
            var db = sp.GetRequiredService<CP6Context>();
            var dep = sp.GetRequiredService<IAssetDepreciationService>();
            var today = DateTime.Today;
            // 仅月末最后一天触发（避免每日重复建草稿）
            if (today.Day != DateTime.DaysInMonth(today.Year, today.Month)) return;

            var period = await db.FiscalPeriods.FirstOrDefaultAsync(
                p => p.Year == today.Year && p.Month == today.Month && p.Status == PeriodStatus.Open, c);
            if (period == null) return;

            bool batchExists = await db.DepreciationRuns.AnyAsync(r => r.FiscalPeriodId == period.Id
                && r.RunMode != DepreciationRunMode.DisposalFinal && r.Status != DepreciationRunStatus.Reversed, c);
            if (batchExists) return;

            var r = await dep.RunAsync(period.Id, "worker", DepreciationRunMode.Worker);
            if (r.Ok) _logger.LogInformation("[AssetDeprec] 租户 {Tenant} {Ym} 已备折旧草稿待复核", tenantId, $"{period.Year}-{period.Month:D2}");
            else _logger.LogWarning("[AssetDeprec] 租户 {Tenant} 备草稿失败：{Code}", tenantId, r.Code);
        }, _logger, ct);
    }
}
```

- [ ] **Step 2: 注册 Worker**（`Program.cs`，`AddHostedService<FinReconciliationWorker>()` 附近加）
```csharp
builder.Services.AddHostedService<CP6.WebApi.BackgroundServices.AssetDepreciationWorker>();
```

- [ ] **Step 3: 构建 + 回归** → `dotnet build CP6.WebApi --nologo` 然后 `dotnet test D:/CP6/CP6.Tests/CP6.Tests.csproj --nologo`，预期全绿。

- [ ] **Step 4: 提交** → `git commit -m "feat(fin): A3 AssetDepreciationWorker (month-end draft via TenantScopeRunner, no auto-post) + register (spec §6.2)"`

---

# Phase F — 控制器 + 权限 + Seed

## Task F-1: 4 控制器 + 操作级权限（spec §7）

**Files:**
- Create: `CP6.WebApi/Controllers/Fin/AssetCategoryController.cs`、`AssetCardController.cs`、`AssetDepreciationController.cs`、`AssetDisposalController.cs`

> 服务侧需补的简单 CRUD（分类/卡片）：分类增删改查 + 删除守卫 FA012；卡片建档（采番 `FA`、拉分类默认值/科目、算残值、起折期=购置次月）、启用（Draft→InUse、定格起折期）、轻量改、计划预览。本 Task 在控制器内对**分类**直接用 `CP6Context`（薄 CRUD，无需独立 service），**卡片**同理薄建档逻辑内联（建卡/启用/Schedule 走 `IAssetDepreciationService.GetScheduleAsync`）。复杂账务逻辑已在 C/D 的 service。

- [ ] **Step 1: `AssetCategoryController.cs`**（含删除守卫 FA012）
```csharp
using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;
using CP6.WebApi.Filters;   // RequirePermission（按既有命名空间核实）
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Controllers.Fin;

[ApiController]
[Route("api/fin/asset-category")]
[Authorize]
public class AssetCategoryController : ControllerBase
{
    private readonly CP6Context _db;
    public AssetCategoryController(CP6Context db) => _db = db;

    private string CurrentUser => User?.Identity?.Name ?? "anonymous";
    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Fin(FinResult r) => r.Ok ? Ok2() : BadRequest(new { code = 400, message = r.Code, args = r.Args });

    [HttpGet]
    [RequirePermission("fin-asset-category", "view")]
    public async Task<IActionResult> List() => Ok2(await _db.AssetCategories.OrderBy(c => c.Code).ToListAsync());

    [HttpGet("{id}")]
    [RequirePermission("fin-asset-category", "view")]
    public async Task<IActionResult> Get(Guid id) => Ok2(await _db.AssetCategories.FindAsync(id));

    [HttpPost]
    [RequirePermission("fin-asset-category", "add")]
    public async Task<IActionResult> Create([FromBody] AssetCategory c)
    {
        c.Id = Guid.NewGuid();
        _db.AssetCategories.Add(c);
        await _db.SaveChangesAsync();
        return Ok2(new { id = c.Id });
    }

    [HttpPut("{id}")]
    [RequirePermission("fin-asset-category", "edit")]
    public async Task<IActionResult> Update(Guid id, [FromBody] AssetCategory c)
    {
        var e = await _db.AssetCategories.FindAsync(id);
        if (e == null) return Fin(FinResult.Fail("FA006"));
        e.Name = c.Name; e.ParentId = c.ParentId; e.Level = c.Level; e.DefaultMethod = c.DefaultMethod;
        e.DefaultUsefulLifeMonths = c.DefaultUsefulLifeMonths; e.DefaultSalvageRate = c.DefaultSalvageRate;
        e.AssetAccountId = c.AssetAccountId; e.AccumDeprecAccountId = c.AccumDeprecAccountId;
        e.DeprecExpenseAccountId = c.DeprecExpenseAccountId; e.IsActive = c.IsActive;
        await _db.SaveChangesAsync();
        return Ok2();
    }

    [HttpDelete("{id}")]
    [RequirePermission("fin-asset-category", "delete")]
    public async Task<IActionResult> Delete(Guid id)
    {
        // FA012：有下级分类 ∨ 有卡片引用 → 拒删
        if (await _db.AssetCategories.AnyAsync(x => x.ParentId == id)
            || await _db.AssetCards.AnyAsync(x => x.CategoryId == id))
            return Fin(FinResult.Fail("FA012"));
        var e = await _db.AssetCategories.FindAsync(id);
        if (e != null) { _db.AssetCategories.Remove(e); await _db.SaveChangesAsync(); }
        return Ok2();
    }
}
```
> 落码前核实 `RequirePermission` 特性所在命名空间（grep `class RequirePermissionAttribute`）与 `JournalEntryController` 的 using 对齐。

- [ ] **Step 2: `AssetCardController.cs`**（建卡采番 + 起折期=购置次月 + 启用 + Schedule）
```csharp
using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;
using CP6.WebApi.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Controllers.Fin;

[ApiController]
[Route("api/fin/asset-card")]
[Authorize]
public class AssetCardController : ControllerBase
{
    private readonly CP6Context _db;
    private readonly IFinSequenceService _seq;
    private readonly IAssetDepreciationService _dep;
    public AssetCardController(CP6Context db, IFinSequenceService seq, IAssetDepreciationService dep)
    { _db = db; _seq = seq; _dep = dep; }

    private string CurrentUser => User?.Identity?.Name ?? "anonymous";
    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Fin(FinResult r) => r.Ok ? Ok2() : BadRequest(new { code = 400, message = r.Code, args = r.Args });

    [HttpGet]
    [RequirePermission("fin-asset-card", "view")]
    public async Task<IActionResult> List([FromQuery] Guid? categoryId, [FromQuery] AssetStatus? status)
        => Ok2(await _db.AssetCards
            .Where(c => (categoryId == null || c.CategoryId == categoryId) && (status == null || c.Status == status))
            .OrderByDescending(c => c.AcquisitionDate).ToListAsync());

    [HttpGet("{id}")]
    [RequirePermission("fin-asset-card", "view")]
    public async Task<IActionResult> Get(Guid id) => Ok2(await _db.AssetCards.FindAsync(id));

    [HttpPost]
    [RequirePermission("fin-asset-card", "add")]
    public async Task<IActionResult> Create([FromBody] AssetCard card)
    {
        var cat = await _db.AssetCategories.FindAsync(card.CategoryId);
        if (cat == null) return Fin(FinResult.Fail("FA001"));
        card.Id = Guid.NewGuid();
        card.AssetNo = await _seq.NextAsync("FA", card.AcquisitionDate);
        // 拉分类默认（卡片未给则取分类）
        if (card.UsefulLifeMonths <= 0) card.UsefulLifeMonths = cat.DefaultUsefulLifeMonths;
        if (card.Method == 0) card.Method = cat.DefaultMethod;
        if (card.SalvageRate == 0m) card.SalvageRate = cat.DefaultSalvageRate;
        card.SalvageValue = card.SalvageValue > 0m ? card.SalvageValue : Math.Round(card.OriginalValue * card.SalvageRate, 2);
        // 起折期 = 购置日次月（D2）
        var next = new DateTime(card.AcquisitionDate.Year, card.AcquisitionDate.Month, 1).AddMonths(1);
        card.DepreciationStartPeriod = next.ToString("yyyy-MM");
        // 期初建卡保留录入的初始累计/期数；普通建卡归零
        if (!card.IsOpeningImport) { card.AccumulatedDepreciation = 0m; card.DepreciatedPeriods = 0; }
        card.Status = AssetStatus.Draft;
        _db.AssetCards.Add(card);
        await _db.SaveChangesAsync();
        return Ok2(new { id = card.Id, assetNo = card.AssetNo });
    }

    [HttpPut("{id}")]
    [RequirePermission("fin-asset-card", "edit")]
    public async Task<IActionResult> Update(Guid id, [FromBody] AssetCard card)
    {
        var e = await _db.AssetCards.FindAsync(id);
        if (e == null) return Fin(FinResult.Fail("FA006"));
        // 轻量变更（spec §0：仅成本中心/部门/责任人/地点/备注等，不动账务参数）
        e.CostCenterId = card.CostCenterId; e.MachineId = card.MachineId; e.DeptId = card.DeptId;
        e.Custodian = card.Custodian; e.Location = card.Location; e.Remarks = card.Remarks;
        e.DeprecExpenseAccountId = card.DeprecExpenseAccountId;
        await _db.SaveChangesAsync();
        return Ok2();
    }

    [HttpPost("{id}/activate")]
    [RequirePermission("fin-asset-card", "activate")]
    public async Task<IActionResult> Activate(Guid id)
    {
        var e = await _db.AssetCards.FindAsync(id);
        if (e == null) return Fin(FinResult.Fail("FA006"));
        if (e.Status != AssetStatus.Draft) return Fin(FinResult.Fail("FA009"));
        e.Status = AssetStatus.InUse;   // 定格起折期（建卡时已算）
        await _db.SaveChangesAsync();
        return Ok2();
    }

    [HttpGet("{id}/schedule")]
    [RequirePermission("fin-asset-card", "view")]
    public async Task<IActionResult> Schedule(Guid id) => Ok2(await _dep.GetScheduleAsync(id));
}
```

- [ ] **Step 3: `AssetDepreciationController.cs`**
```csharp
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;
using CP6.WebApi.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Fin;

[ApiController]
[Route("api/fin/asset-deprec")]
[Authorize]
public class AssetDepreciationController : ControllerBase
{
    private readonly IAssetDepreciationService _svc;
    public AssetDepreciationController(IAssetDepreciationService svc) => _svc = svc;

    private string CurrentUser => User?.Identity?.Name ?? "anonymous";
    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Fin(FinResult r) => r.Ok ? Ok2() : BadRequest(new { code = 400, message = r.Code, args = r.Args });

    public sealed class WorkloadReq { public decimal Workload { get; set; } }
    public sealed class ReasonReq { public string Reason { get; set; } = string.Empty; }

    [HttpGet("preview")]
    [RequirePermission("fin-asset-deprec", "view")]
    public async Task<IActionResult> Preview([FromQuery] Guid periodId) => Ok2(await _svc.PreviewAsync(periodId));

    [HttpPost("run")]
    [RequirePermission("fin-asset-deprec", "run")]
    public async Task<IActionResult> Run([FromQuery] Guid periodId)
        => Fin(await _svc.RunAsync(periodId, CurrentUser, DepreciationRunMode.Manual));

    [HttpPut("entry/{entryId}/workload")]
    [RequirePermission("fin-asset-deprec", "run")]
    public async Task<IActionResult> SetWorkload(Guid entryId, [FromBody] WorkloadReq r)
        => Fin(await _svc.SetWorkloadAsync(entryId, r.Workload));

    [HttpPost("{runId}/post")]
    [RequirePermission("fin-asset-deprec", "post")]
    public async Task<IActionResult> Post(Guid runId) => Fin(await _svc.PostAsync(runId, CurrentUser));

    [HttpPost("{runId}/reverse")]
    [RequirePermission("fin-asset-deprec", "reverse")]
    public async Task<IActionResult> Reverse(Guid runId, [FromBody] ReasonReq r)
        => Fin(await _svc.ReverseAsync(runId, CurrentUser, r.Reason));
}
```
> 列表端点（批次/明细查询）按前端需要补 `GET list?periodId` / `GET {runId}/entries`，直查 `CP6Context`（薄查询，权限 `view`）。

- [ ] **Step 4: `AssetDisposalController.cs`**
```csharp
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;
using CP6.WebApi.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Fin;

[ApiController]
[Route("api/fin/asset-disposal")]
[Authorize]
public class AssetDisposalController : ControllerBase
{
    private readonly IAssetDisposalService _svc;
    public AssetDisposalController(IAssetDisposalService svc) => _svc = svc;

    private string CurrentUser => User?.Identity?.Name ?? "anonymous";
    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Fin(FinResult r) => r.Ok ? Ok2() : BadRequest(new { code = 400, message = r.Code, args = r.Args });

    public sealed class ReasonReq { public string Reason { get; set; } = string.Empty; }

    [HttpGet]
    [RequirePermission("fin-asset-disposal", "view")]
    public async Task<IActionResult> List([FromQuery] AssetDisposalStatus? status, [FromQuery] Guid? assetCardId)
        => Ok2(await _svc.ListAsync(status, assetCardId));

    [HttpGet("{id}")]
    [RequirePermission("fin-asset-disposal", "view")]
    public async Task<IActionResult> Get(Guid id) => Ok2(await _svc.GetAsync(id));

    [HttpPost]
    [RequirePermission("fin-asset-disposal", "add")]
    public async Task<IActionResult> Create([FromBody] AssetDisposal d) => Fin(await _svc.CreateAsync(d, CurrentUser));

    [HttpPost("{id}/confirm")]
    [RequirePermission("fin-asset-disposal", "confirm")]
    public async Task<IActionResult> Confirm(Guid id) => Fin(await _svc.ConfirmAsync(id, CurrentUser));

    [HttpPost("{id}/reverse")]
    [RequirePermission("fin-asset-disposal", "reverse")]
    public async Task<IActionResult> Reverse(Guid id, [FromBody] ReasonReq r)
        => Fin(await _svc.ReverseAsync(id, CurrentUser, r.Reason));
}
```

- [ ] **Step 5: 构建** → `dotnet build CP6.WebApi --nologo`，预期成功。

- [ ] **Step 6: 提交** → `git commit -m "feat(fin): A3 4 controllers (category/card/deprec/disposal) + operation-level RequirePermission (spec §7)"`

---

## Task F-2: 科目对账 Seed（CoA 模板补全 + 幂等 reconcile）+ AssetCategory demo（spec §9，**§9.1 对账修正**）

**Files:**
- Modify: `CP6.Core/Services/Fin/FinCoaTemplate.cs`（CnGaapRows 补 Role + 新增 4 科目）
- Create: `CP6.WebApi/Seed/A3AccountSeed.cs`（既有库幂等 reconcile）
- Modify: `CP6.WebApi/Program.cs`（启动时调 reconcile + AssetCategory demo seed）

- [ ] **Step 1: 改 `FinCoaTemplate.CnGaapRows`**（按「§9.1 科目表对账修正」表）：
  - 把 `new("1601", "固定资产", AccountType.Asset, Dr, true)` → `new("1601", "固定资产", AccountType.Asset, Dr, true, Role: "FIXED_ASSET")`
  - 把 `new("1602", "累计折旧", AccountType.Asset, Cr, true)` → `new("1602", "累计折旧", AccountType.Asset, Cr, true, Role: "ACCUM_DEPRECIATION")`
  - 把 `new("4301", "营业外收入", AccountType.Revenue, Cr, true)` → `new("4301", "营业外收入", AccountType.Revenue, Cr, true, Role: "NON_OP_INCOME")`
  - 在 `1602` 行后新增：
    ```csharp
    new("1606", "固定资产清理", AccountType.Asset, Dr, true, Role: "ASSET_CLEARING"),
    new("1901", "待处理财产损溢", AccountType.Asset, Dr, true, Role: "PENDING_PROPERTY_LOSS"),
    ```
  - 在 `6801 所得税费用` 行后（费用类末尾）新增：
    ```csharp
    new("6115", "资产处置损益", AccountType.Expense, Dr, true, Role: "ASSET_DISPOSAL_PL"),
    new("6711", "营业外支出",   AccountType.Expense, Dr, true, Role: "NON_OP_EXPENSE"),
    ```
> `5101.01 制造费用—折旧`/`6002 管理费用`/`6001 销售费用`/`2221.02 应交税费—销项税(Role TAX_OUTPUT)` 均已存在、无需改。新装部署 `ImportTemplateAsync(CnGaap)` 即带全 A3 科目。

- [ ] **Step 2: 幂等 reconcile `A3AccountSeed.cs`**（既有库已导模板、缺 A3 科目/Role → 补；幂等）
```csharp
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Seed;

/// <summary>A3 固定资产科目对账（既有库幂等补全）：补 Role 到 1601/1602/4301，新增 1606/1901/6115/6711。
/// 新装库已由 FinCoaTemplate 带出，此处仅修存量；幂等可重入。</summary>
public static class A3AccountSeed
{
    private record Spec(string Code, string Name, AccountType Type, AccountSide Side, string Role);

    private static readonly Spec[] Items =
    {
        new("1601", "固定资产",       AccountType.Asset,   AccountSide.Debit,  "FIXED_ASSET"),
        new("1602", "累计折旧",       AccountType.Asset,   AccountSide.Credit, "ACCUM_DEPRECIATION"),
        new("1606", "固定资产清理",   AccountType.Asset,   AccountSide.Debit,  "ASSET_CLEARING"),
        new("1901", "待处理财产损溢", AccountType.Asset,   AccountSide.Debit,  "PENDING_PROPERTY_LOSS"),
        new("6115", "资产处置损益",   AccountType.Expense, AccountSide.Debit,  "ASSET_DISPOSAL_PL"),
        new("6711", "营业外支出",     AccountType.Expense, AccountSide.Debit,  "NON_OP_EXPENSE"),
        new("4301", "营业外收入",     AccountType.Revenue, AccountSide.Credit, "NON_OP_INCOME"),
    };

    public static async Task EnsureAsync(CP6Context db)
    {
        if (!await db.GlAccounts.AnyAsync()) return;   // 空库（未导模板）跳过，由模板导入负责
        foreach (var s in Items)
        {
            var acc = await db.GlAccounts.FirstOrDefaultAsync(a => a.Code == s.Code);
            if (acc == null)
                db.GlAccounts.Add(new GlAccount { Id = Guid.NewGuid(), Code = s.Code, Name = s.Name,
                    Type = s.Type, NormalSide = s.Side, IsLeaf = true, Level = 1, Role = s.Role, IsActive = true });
            else if (string.IsNullOrEmpty(acc.Role))
                acc.Role = s.Role;   // 已存在但缺 Role（如 1601/1602/4301）→ 补
        }
        await db.SaveChangesAsync();
    }
}
```
> 落码前核实 `GlAccount` 的方向属性名（`NormalSide`）与 `AccountSide`/`AccountType` 枚举命名空间（参 `FinCoaTemplate.cs` 的 `using`）。

- [ ] **Step 3: 接入 `Program.cs` 启动 seed**（CoA 模板导入之后、AssetCategory demo 之前）：
```csharp
await CP6.WebApi.Seed.A3AccountSeed.EnsureAsync(db);

// A3 demo 资产分类（仅空表时，便于开箱体验；§9.2）
if (!db.AssetCategories.Any())
{
    Guid Acc(string code) => db.GlAccounts.First(a => a.Code == code).Id;
    var fa = Acc("1601"); var accum = Acc("1602");
    db.AssetCategories.AddRange(
        new AssetCategory { Id = Guid.NewGuid(), Code = "FA-BLDG", Name = "房屋建筑物", Level = 1,
            DefaultMethod = DepreciationMethod.StraightLine, DefaultUsefulLifeMonths = 240, DefaultSalvageRate = 0.03m,
            AssetAccountId = fa, AccumDeprecAccountId = accum, DeprecExpenseAccountId = Acc("6002"), IsActive = true },
        new AssetCategory { Id = Guid.NewGuid(), Code = "FA-MACH", Name = "机器设备", Level = 1,
            DefaultMethod = DepreciationMethod.StraightLine, DefaultUsefulLifeMonths = 120, DefaultSalvageRate = 0.05m,
            AssetAccountId = fa, AccumDeprecAccountId = accum, DeprecExpenseAccountId = Acc("5101.01"), IsActive = true },
        new AssetCategory { Id = Guid.NewGuid(), Code = "FA-VEH", Name = "运输设备", Level = 1,
            DefaultMethod = DepreciationMethod.StraightLine, DefaultUsefulLifeMonths = 60, DefaultSalvageRate = 0.05m,
            AssetAccountId = fa, AccumDeprecAccountId = accum, DeprecExpenseAccountId = Acc("6002"), IsActive = true },
        new AssetCategory { Id = Guid.NewGuid(), Code = "FA-ELEC", Name = "电子设备", Level = 1,
            DefaultMethod = DepreciationMethod.StraightLine, DefaultUsefulLifeMonths = 36, DefaultSalvageRate = 0.03m,
            AssetAccountId = fa, AccumDeprecAccountId = accum, DeprecExpenseAccountId = Acc("6002"), IsActive = true },
        new AssetCategory { Id = Guid.NewGuid(), Code = "FA-OFF", Name = "办公设备", Level = 1,
            DefaultMethod = DepreciationMethod.StraightLine, DefaultUsefulLifeMonths = 60, DefaultSalvageRate = 0.05m,
            AssetAccountId = fa, AccumDeprecAccountId = accum, DeprecExpenseAccountId = Acc("6002"), IsActive = true });
    db.SaveChanges();
}
```
> 确认 `Program.cs` seed 段顶部 using 已含 `CP6.Entity.DomainModels.Fin`（既有 Fin seed 已用）。`db` 为 seed 段既有的 `CP6Context` 实例（参既有 `if (!db.Sys_Menus.Any())`）。

- [ ] **Step 4: 改既有 CoA 测试基线**（若有断言 CnGaap 科目数的测试，更新计数 +4）。运行 `dotnet test D:/CP6/CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~GlAccount|FullyQualifiedName~Coa" --nologo`，按实修断言。

- [ ] **Step 5: 全量回归** → `dotnet test D:/CP6/CP6.Tests/CP6.Tests.csproj --nologo`，预期全绿。

- [ ] **Step 6: 提交** → `git commit -m "feat(fin): A3 CoA reconcile (1601/1602/4301 roles + 1606/1901/6115/6711 accounts) + idempotent seed + 5 demo categories (spec §9, §9.1 corrected to real CoA)"`

---

## Task F-3: 菜单 615~618 + RoleMenu 授权（spec §10）

**Files:** Modify `CP6.WebApi/Program.cs`

- [ ] **Step 1: 定位 Fin 菜单 seed 块** → grep `Program.cs` 中 `MenuId = 613`（或 `RoutePath = "/fin/`）找到财务 600 组菜单 `AddRange` 块（A4 落地后应已含 614）。

- [ ] **Step 2: 加 4 个菜单**（Fin 组 `ParentId = 600`，紧随 613/614）：
```csharp
new Sys_Menu { MenuId = 615, MenuName = "资产分类", RoutePath = "/fin/asset-category", Icon = "Postcard", ParentId = 600, OrderNo = 280, Enable = true },
new Sys_Menu { MenuId = 616, MenuName = "资产卡片", RoutePath = "/fin/asset-card", Icon = "Tickets", ParentId = 600, OrderNo = 281, Enable = true },
new Sys_Menu { MenuId = 617, MenuName = "折旧计提", RoutePath = "/fin/asset-deprec", Icon = "Money", ParentId = 600, OrderNo = 282, Enable = true },
new Sys_Menu { MenuId = 618, MenuName = "资产处置", RoutePath = "/fin/asset-disposal", Icon = "Sell", ParentId = 600, OrderNo = 283, Enable = true }
```
> RoutePath `/fin/asset-*` → 经 `Program.cs` L607-612 自动派生 MenuKey `fin-asset-*`，与控制器 `[RequirePermission("fin-asset-*", ...)]` 资源键吻合，无需手配权限点表。`MenuName` 仅占位（前端按 `nav.615~618` i18n 渲染）。

- [ ] **Step 3: 加 RoleMenu 授权**（找到 Fin 的 `Sys_RoleMenu` 授权块——A4 落地处应已有 `RoleId=1,MenuId=614`；若财务菜单授权另成块，在该块加）：
```csharp
db.Sys_RoleMenus.AddRange(
    new Sys_RoleMenu { RoleId = 1, MenuId = 615 },
    new Sys_RoleMenu { RoleId = 1, MenuId = 616 },
    new Sys_RoleMenu { RoleId = 1, MenuId = 617 },
    new Sys_RoleMenu { RoleId = 1, MenuId = 618 });
```
> 若现库已初始化过菜单（`db.Sys_Menus.Any()` 为真、seed 整块被跳过），需一段幂等补登（仿既有 105/106 等的「按 MenuId 缺则补」模式）：对 615~618 各 `if (!db.Sys_Menus.Any(m => m.MenuId == X)) { db.Sys_Menus.Add(...); db.Sys_RoleMenus.Add(new(){RoleId=1,MenuId=X}); }`，再 `db.SaveChanges()`。落码时核实既有 Fin 菜单（601~614）用哪种模式，A3 对齐。

- [ ] **Step 4: 启动验证** → `dotnet build CP6.WebApi --nologo` 成功；起后端确认无 seed 异常（菜单/权限重复键）。

- [ ] **Step 5: 提交** → `git commit -m "feat(fin): A3 menus 615-618 (asset category/card/deprec/disposal) + admin RoleMenu grants; MenuKey auto-derives fin-asset-* (spec §10)"`

---

# Phase G — 前端 + i18n

> **错误码约定**：服务层 `FinResult` 统一返回裸码 `FA001`~`FA012`（与本计划 C/D 的 `Fail("FA0xx")` 及测试断言一致）；i18n LangKey 同名 `FA001`~`FA012`，前端按 `body.message` 直查。本约定优先「返回码 = i18n 键」内部一致性（spec §10 提及的 `E-FA-*` 风格不强制采用）。

## Task G-1: 五语 i18n seed `I18nA3ScreenSeed`（spec §10）

**Files:**
- Create: `CP6.WebApi/Seed/I18nA3ScreenSeed.cs`
- Modify: `CP6.WebApi/Program.cs`（i18n `.Concat(I18nA3ScreenSeed.Items)`）

- [ ] **Step 1: i18n seed**（结构仿 `I18nFinScreenSeed`，五语 ZhCN/ZhTW/En/Ja/Ko）
```csharp
using CP6.Entity.DomainModels.Sys;

namespace CP6.WebApi.Seed;

/// <summary>A3 固定资产五语词条（菜单/枚举/字段/错误码，spec §10）。代码只放 key，语义点分 key。</summary>
public static class I18nA3ScreenSeed
{
    public static readonly Sys_Lang[] Items = new[]
    {
        // ── 菜单 ──
        new Sys_Lang { LangKey = "nav.615", ZhCN = "资产分类", ZhTW = "資產分類", En = "Asset Category", Ja = "資産分類", Ko = "자산 분류" },
        new Sys_Lang { LangKey = "nav.616", ZhCN = "资产卡片", ZhTW = "資產卡片", En = "Asset Card", Ja = "資産カード", Ko = "자산 카드" },
        new Sys_Lang { LangKey = "nav.617", ZhCN = "折旧计提", ZhTW = "折舊計提", En = "Depreciation", Ja = "減価償却", Ko = "감가상각" },
        new Sys_Lang { LangKey = "nav.618", ZhCN = "资产处置", ZhTW = "資產處置", En = "Asset Disposal", Ja = "資産処分", Ko = "자산 처분" },

        // ── 折旧方法枚举 ──
        new Sys_Lang { LangKey = "asset.method.1", ZhCN = "直线法", ZhTW = "直線法", En = "Straight-Line", Ja = "定額法", Ko = "정액법" },
        new Sys_Lang { LangKey = "asset.method.2", ZhCN = "双倍余额递减", ZhTW = "雙倍餘額遞減", En = "Double Declining", Ja = "定率法(倍率)", Ko = "이중체감법" },
        new Sys_Lang { LangKey = "asset.method.3", ZhCN = "年数总和", ZhTW = "年數總和", En = "Sum-of-Years", Ja = "級数法", Ko = "연수합계법" },
        new Sys_Lang { LangKey = "asset.method.4", ZhCN = "工作量法", ZhTW = "工作量法", En = "Units of Production", Ja = "生産高比例法", Ko = "생산량비례법" },

        // ── 资产状态枚举 ──
        new Sys_Lang { LangKey = "asset.status.0", ZhCN = "草稿", ZhTW = "草稿", En = "Draft", Ja = "下書き", Ko = "초안" },
        new Sys_Lang { LangKey = "asset.status.1", ZhCN = "在用", ZhTW = "在用", En = "In Use", Ja = "使用中", Ko = "사용 중" },
        new Sys_Lang { LangKey = "asset.status.2", ZhCN = "已提足", ZhTW = "已提足", En = "Fully Depreciated", Ja = "償却済", Ko = "상각완료" },
        new Sys_Lang { LangKey = "asset.status.3", ZhCN = "已处置", ZhTW = "已處置", En = "Disposed", Ja = "処分済", Ko = "처분완료" },

        // ── 处置类型枚举 ──
        new Sys_Lang { LangKey = "asset.disposalType.1", ZhCN = "出售", ZhTW = "出售", En = "Sale", Ja = "売却", Ko = "매각" },
        new Sys_Lang { LangKey = "asset.disposalType.2", ZhCN = "报废", ZhTW = "報廢", En = "Scrap", Ja = "除却", Ko = "폐기" },
        new Sys_Lang { LangKey = "asset.disposalType.3", ZhCN = "转让", ZhTW = "轉讓", En = "Transfer", Ja = "譲渡", Ko = "양도" },
        new Sys_Lang { LangKey = "asset.disposalType.4", ZhCN = "盘亏", ZhTW = "盤虧", En = "Inventory Loss", Ja = "棚卸減耗", Ko = "재고손실" },

        // ── 批次状态枚举 ──
        new Sys_Lang { LangKey = "asset.runStatus.0", ZhCN = "草稿", ZhTW = "草稿", En = "Draft", Ja = "下書き", Ko = "초안" },
        new Sys_Lang { LangKey = "asset.runStatus.1", ZhCN = "已过账", ZhTW = "已過帳", En = "Posted", Ja = "記帳済", Ko = "전기완료" },
        new Sys_Lang { LangKey = "asset.runStatus.2", ZhCN = "已反冲", ZhTW = "已沖銷", En = "Reversed", Ja = "赤伝済", Ko = "역분개" },

        // ── 关键字段标签（示例集；落码按视图所需补全）──
        new Sys_Lang { LangKey = "asset.field.assetNo", ZhCN = "资产编号", ZhTW = "資產編號", En = "Asset No.", Ja = "資産番号", Ko = "자산번호" },
        new Sys_Lang { LangKey = "asset.field.originalValue", ZhCN = "原值", ZhTW = "原值", En = "Original Value", Ja = "取得価額", Ko = "취득가액" },
        new Sys_Lang { LangKey = "asset.field.accumulated", ZhCN = "累计折旧", ZhTW = "累計折舊", En = "Accum. Deprec.", Ja = "減価償却累計", Ko = "감가상각누계" },
        new Sys_Lang { LangKey = "asset.field.netValue", ZhCN = "净值", ZhTW = "淨值", En = "Net Book Value", Ja = "帳簿価額", Ko = "장부가액" },
        new Sys_Lang { LangKey = "asset.field.salvage", ZhCN = "残值", ZhTW = "殘值", En = "Salvage", Ja = "残存価額", Ko = "잔존가액" },
        new Sys_Lang { LangKey = "asset.field.proceeds", ZhCN = "处置价款", ZhTW = "處置價款", En = "Proceeds", Ja = "処分収入", Ko = "처분대금" },

        // ── 错误码 FA001~FA012（与 FinResult 返回码同名）──
        new Sys_Lang { LangKey = "FA001", ZhCN = "资产分类账务科目未配置", ZhTW = "資產分類帳務科目未配置", En = "Category accounts not configured", Ja = "資産分類の勘定科目が未設定", Ko = "자산 분류 계정 미설정" },
        new Sys_Lang { LangKey = "FA002", ZhCN = "资产已处置或已有进行中处置单", ZhTW = "資產已處置或已有進行中處置單", En = "Asset already disposed or has an active disposal", Ja = "資産は処分済みまたは処分中です", Ko = "이미 처분되었거나 진행 중인 처분이 있습니다" },
        new Sys_Lang { LangKey = "FA003", ZhCN = "本期已存在折旧批次", ZhTW = "本期已存在折舊批次", En = "Depreciation batch already exists for the period", Ja = "当期の減価償却バッチが既に存在します", Ko = "해당 기간 감가상각 배치가 이미 있습니다" },
        new Sys_Lang { LangKey = "FA004", ZhCN = "资产尚未起折", ZhTW = "資產尚未起折", En = "Asset depreciation not yet started", Ja = "償却開始前です", Ko = "상각 미개시" },
        new Sys_Lang { LangKey = "FA005", ZhCN = "累计折旧已达上限", ZhTW = "累計折舊已達上限", En = "Accumulated depreciation reached cap", Ja = "償却上限に達しています", Ko = "상각 한도 도달" },
        new Sys_Lang { LangKey = "FA006", ZhCN = "数据不一致或对象不存在", ZhTW = "資料不一致或物件不存在", En = "Data inconsistent or not found", Ja = "データ不整合または存在しません", Ko = "데이터 불일치 또는 없음" },
        new Sys_Lang { LangKey = "FA007", ZhCN = "会计期间未开启或已结账", ZhTW = "會計期間未開啟或已結帳", En = "Period not open or already closed", Ja = "会計期間が未開設または締め済み", Ko = "기간 미개시 또는 마감됨" },
        new Sys_Lang { LangKey = "FA008", ZhCN = "工作量法缺总量或本期未录量", ZhTW = "工作量法缺總量或本期未錄量", En = "Units-of-production total/period workload missing", Ja = "生産高比例法の総量/当期量が未入力", Ko = "생산량비례법 총량/당기량 누락" },
        new Sys_Lang { LangKey = "FA009", ZhCN = "状态不可再过账或需先反冲", ZhTW = "狀態不可再過帳或需先沖銷", En = "Invalid status; reverse first", Ja = "状態が不正、先に赤伝が必要", Ko = "상태 오류, 먼저 역분개 필요" },
        new Sys_Lang { LangKey = "FA010", ZhCN = "有价款或清理费用但未指定收/付款账户", ZhTW = "有價款或清理費用但未指定收/付款帳戶", En = "Proceeds/expense present but no bank account specified", Ja = "代金/費用ありだが入出金口座が未指定", Ko = "대금/비용 있으나 은행계좌 미지정" },
        new Sys_Lang { LangKey = "FA011", ZhCN = "反冲次序冲突：关联单据未先反冲", ZhTW = "沖銷次序衝突：關聯單據未先沖銷", En = "Reversal order conflict: reverse linked document first", Ja = "赤伝順序エラー：関連伝票を先に赤伝してください", Ko = "역분개 순서 충돌: 연관 전표 먼저 역분개" },
        new Sys_Lang { LangKey = "FA012", ZhCN = "资产分类被引用或有下级，不可删除", ZhTW = "資產分類被引用或有下級，不可刪除", En = "Category referenced or has children; cannot delete", Ja = "分類は参照中または下位あり、削除不可", Ko = "분류 참조 중 또는 하위 존재, 삭제 불가" },
    };
}
```

- [ ] **Step 2: 接入 `Program.cs`** → 找到 i18n 种子 `.Concat(...)` 链（既有 `I18nFinScreenSeed.Items` 处），追加 `.Concat(I18nA3ScreenSeed.Items)`。

- [ ] **Step 3: i18n 校验** → `cd cp6.web && npm run i18n:check`（若有该脚本），预期无缺键/无未用键告警。构建后端 `dotnet build CP6.WebApi --nologo`。

- [ ] **Step 4: 提交** → `git commit -m "feat(fin): A3 I18nA3ScreenSeed (nav/method/status/disposalType/runStatus/fields/FA001-012) 5 langs + Program concat (spec §10)"`

---

## Task G-2: 前端 4 视图 + api/类型/路由（spec §10）

**Files:**
- Create: `cp6.web/src/types/fin/asset.ts`、`cp6.web/src/api/fin/asset.ts`
- Create: `cp6.web/src/views/fin/AssetCategoryView.vue`、`AssetCardView.vue`、`AssetDepreciationView.vue`、`AssetDisposalView.vue`
- Modify: `cp6.web/src/router/index.ts`

- [ ] **Step 1: 类型 `types/fin/asset.ts`**
```typescript
export interface AssetCategory {
  id?: string; code: string; name: string; parentId?: string | null; level: number;
  defaultMethod: number; defaultUsefulLifeMonths: number; defaultSalvageRate: number;
  assetAccountId: string; accumDeprecAccountId: string; deprecExpenseAccountId: string; isActive: boolean;
}
export interface AssetCard {
  id?: string; assetNo?: string; name: string; specModel?: string; categoryId: string;
  originalValue: number; salvageRate: number; salvageValue: number; method: number; usefulLifeMonths: number;
  totalWorkload?: number | null; workloadUnit?: string; acquisitionDate: string; depreciationStartPeriod?: string;
  accumulatedDepreciation: number; depreciatedPeriods: number; netBookValue?: number;
  deprecExpenseAccountId?: string | null; costCenterId?: string | null; machineId?: string | null; deptId?: string | null;
  status: number; location?: string; custodian?: string; isOpeningImport: boolean; remarks?: string;
}
export interface DepreciationEntryDto {
  assetCardId: string; assetNo: string; assetName: string; method: number;
  depreciationAmount: number; openingAccumulated: number; closingAccumulated: number;
  deprecExpenseAccountId: string; accumDeprecAccountId: string; costCenterId?: string | null; workloadThisPeriod?: number | null;
}
export interface DepreciationScheduleRow { periodIndex: number; yearMonth: string; amount: number; accumulated: number; netValue: number }
export interface AssetDisposal {
  id?: string; no?: string; assetCardId: string; disposalType: number; disposalDate: string; fiscalPeriodId: string;
  originalValue?: number; accumulatedDepreciation?: number; netBookValue?: number;
  proceeds: number; taxAmount: number; disposalExpense: number; netGainLoss?: number;
  receiptBankAccountId?: string | null; status?: number; reason?: string;
}
```

- [ ] **Step 2: api `api/fin/asset.ts`**（`http` 拦截器已返 body，调用方取 `res.data`，参 `api/fin/fin.ts`）
```typescript
import http from '../http'
import type { ApiResp } from '@/types/fin/fin'
import type { AssetCategory, AssetCard, DepreciationEntryDto, DepreciationScheduleRow, AssetDisposal } from '@/types/fin/asset'

export const assetCategoryApi = {
  list() { return http.get<any, ApiResp<AssetCategory[]>>('/fin/asset-category') },
  create(d: AssetCategory) { return http.post<any, ApiResp<{ id: string }>>('/fin/asset-category', d) },
  update(id: string, d: AssetCategory) { return http.put<any, ApiResp<unknown>>(`/fin/asset-category/${id}`, d) },
  remove(id: string) { return http.delete<any, ApiResp<unknown>>(`/fin/asset-category/${id}`) },
}
export const assetCardApi = {
  list(categoryId?: string, status?: number) { return http.get<any, ApiResp<AssetCard[]>>('/fin/asset-card', { params: { categoryId, status } }) },
  get(id: string) { return http.get<any, ApiResp<AssetCard>>(`/fin/asset-card/${id}`) },
  create(d: AssetCard) { return http.post<any, ApiResp<{ id: string; assetNo: string }>>('/fin/asset-card', d) },
  update(id: string, d: AssetCard) { return http.put<any, ApiResp<unknown>>(`/fin/asset-card/${id}`, d) },
  activate(id: string) { return http.post<any, ApiResp<unknown>>(`/fin/asset-card/${id}/activate`) },
  schedule(id: string) { return http.get<any, ApiResp<DepreciationScheduleRow[]>>(`/fin/asset-card/${id}/schedule`) },
}
export const assetDeprecApi = {
  preview(periodId: string) { return http.get<any, ApiResp<DepreciationEntryDto[]>>('/fin/asset-deprec/preview', { params: { periodId } }) },
  run(periodId: string) { return http.post<any, ApiResp<unknown>>('/fin/asset-deprec/run', null, { params: { periodId } }) },
  setWorkload(entryId: string, workload: number) { return http.put<any, ApiResp<unknown>>(`/fin/asset-deprec/entry/${entryId}/workload`, { workload }) },
  post(runId: string) { return http.post<any, ApiResp<unknown>>(`/fin/asset-deprec/${runId}/post`) },
  reverse(runId: string, reason: string) { return http.post<any, ApiResp<unknown>>(`/fin/asset-deprec/${runId}/reverse`, { reason }) },
}
export const assetDisposalApi = {
  list(status?: number, assetCardId?: string) { return http.get<any, ApiResp<AssetDisposal[]>>('/fin/asset-disposal', { params: { status, assetCardId } }) },
  create(d: AssetDisposal) { return http.post<any, ApiResp<unknown>>('/fin/asset-disposal', d) },
  confirm(id: string) { return http.post<any, ApiResp<unknown>>(`/fin/asset-disposal/${id}/confirm`) },
  reverse(id: string, reason: string) { return http.post<any, ApiResp<unknown>>(`/fin/asset-disposal/${id}/reverse`, { reason }) },
}
```

- [ ] **Step 3: 路由**（`router/index.ts`，Fin 段加 4 条，参既有 `/fin/*`）
```typescript
  '/fin/asset-category': () => import('@/views/fin/AssetCategoryView.vue'),   // A3 资产分类
  '/fin/asset-card': () => import('@/views/fin/AssetCardView.vue'),           // A3 资产卡片
  '/fin/asset-deprec': () => import('@/views/fin/AssetDepreciationView.vue'), // A3 折旧计提
  '/fin/asset-disposal': () => import('@/views/fin/AssetDisposalView.vue'),   // A3 资产处置
```

- [ ] **Step 4: `AssetDepreciationView.vue`**（核心流程视图：选期间 → Preview 试算 → Run 建批 → 工作量法补录 → Post → 反冲；其余视图同构 element-plus `el-table`+`el-dialog`，镜像既有 Fin 视图）
```vue
<template>
  <div class="page">
    <el-card>
      <div class="toolbar">
        <el-select v-model="periodId" :placeholder="t('asset.field.period')" filterable style="width:220px">
          <el-option v-for="p in periods" :key="p.id" :label="`${p.year}-${String(p.month).padStart(2,'0')}`" :value="p.id" />
        </el-select>
        <el-button @click="loadPreview" :disabled="!periodId">{{ t('common.preview') }}</el-button>
        <el-button type="primary" @click="run" :disabled="!periodId">{{ t('asset.action.run') }}</el-button>
      </div>
      <el-table :data="preview" border>
        <el-table-column prop="assetNo" :label="t('asset.field.assetNo')" />
        <el-table-column prop="assetName" :label="t('asset.field.name')" />
        <el-table-column :label="t('asset.field.method')"><template #default="{ row }">{{ t('asset.method.' + row.method) }}</template></el-table-column>
        <el-table-column prop="depreciationAmount" :label="t('asset.field.amount')" align="right" />
        <el-table-column prop="closingAccumulated" :label="t('asset.field.accumulated')" align="right" />
      </el-table>
    </el-card>

    <el-card style="margin-top:12px">
      <el-table :data="runs" border>
        <el-table-column prop="no" :label="t('asset.field.runNo')" />
        <el-table-column prop="periodYearMonth" :label="t('asset.field.period')" />
        <el-table-column :label="t('asset.field.runStatus')"><template #default="{ row }">{{ t('asset.runStatus.' + row.status) }}</template></el-table-column>
        <el-table-column prop="totalAmount" :label="t('asset.field.amount')" align="right" />
        <el-table-column :label="t('common.action')" width="220">
          <template #default="{ row }">
            <el-button size="small" v-if="row.status===0" type="primary" @click="post(row.id)">{{ t('asset.action.post') }}</el-button>
            <el-button size="small" v-if="row.status===1" type="danger" @click="reverse(row.id)">{{ t('asset.action.reverse') }}</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage, ElMessageBox } from 'element-plus'
import { assetDeprecApi } from '@/api/fin/asset'
import { periodApi } from '@/api/fin/fin'   // 既有期间 api（核实导出名）
import type { DepreciationEntryDto } from '@/types/fin/asset'

const { t } = useI18n()
const periods = ref<any[]>([])
const periodId = ref('')
const preview = ref<DepreciationEntryDto[]>([])
const runs = ref<any[]>([])

async function loadPeriods() { periods.value = (await periodApi.list()).data ?? [] }
async function loadPreview() { preview.value = (await assetDeprecApi.preview(periodId.value)).data ?? [] }
async function loadRuns() { /* GET list?periodId（F-1 Step3 补的列表端点）; 暂用 preview 占位刷新 */ }
async function run() {
  try { await assetDeprecApi.run(periodId.value); ElMessage.success(t('common.ok')); await loadRuns() }
  catch (e: any) { ElMessage.error(t(e?.response?.data?.message ?? 'common.fail')) }
}
async function post(id: string) {
  try { await assetDeprecApi.post(id); ElMessage.success(t('common.ok')); await loadRuns() }
  catch (e: any) { ElMessage.error(t(e?.response?.data?.message ?? 'common.fail')) }
}
async function reverse(id: string) {
  const { value } = await ElMessageBox.prompt(t('asset.action.reverseReason'), t('asset.action.reverse'))
  try { await assetDeprecApi.reverse(id, value); ElMessage.success(t('common.ok')); await loadRuns() }
  catch (e: any) { ElMessage.error(t(e?.response?.data?.message ?? 'common.fail')) }
}
onMounted(() => { loadPeriods() })
</script>
```
> 错误码映射：catch 取 `e.response.data.message`（= `FA0xx`）→ `t(code)`（i18n 已种 FA001~FA012）。`common.preview/ok/fail/action/reverseReason` 等公共词条若缺，补入 `I18nA3ScreenSeed` 或复用既有公共词条。

- [ ] **Step 5: 其余三视图**（同构，element-plus `el-table` + `el-dialog` 表单，镜像 `GlAccountView.vue`/`ApInvoiceView.vue` 既有范式）：
  - **`AssetCategoryView.vue`**：表格列 `code/name/defaultMethod(枚举)/defaultUsefulLifeMonths/defaultSalvageRate/isActive`；工具栏「新增」；行「编辑」「删除」（删除捕获 `FA012` → `ElMessage.error(t('FA012'))`）。新增/编辑对话框含三科目下拉（`glAccountApi.list()` 取叶子科目）。调 `assetCategoryApi`。
  - **`AssetCardView.vue`**：筛选 `categoryId/status`；表格列 `assetNo/name/category/originalValue/accumulatedDepreciation/netBookValue/status(枚举)`；行「编辑(轻量)」「启用(status===0)」「折旧计划(抽屉展示 schedule)」「处置(跳处置建单)」。新增对话框含分类下拉(带出默认)、原值、方法、年限、残值率、购置日、`isOpeningImport`(勾选则显示初始累计/期数录入)、工作量法时显示 `totalWorkload/workloadUnit`。调 `assetCardApi`。
  - **`AssetDisposalView.vue`**：筛选 `status`；表格列 `no/assetCardId(资产号)/disposalType(枚举)/disposalDate/proceeds/netGainLoss/status(枚举)`；行「确认(status===0)」「反冲(status===1)」。新增对话框含资产卡下拉(InUse/FullyDepreciated)、处置类型、处置日、期间、`proceeds/taxAmount/disposalExpense`、收/付款银行账户下拉(出售/转让/有费用时必填，前端校验对齐 FA010)、原因。调 `assetDisposalApi`。

- [ ] **Step 6: 前端构建/类型检查** → `cd cp6.web && npm run type-check`（或 `vue-tsc`），预期无类型错误。

- [ ] **Step 7: 提交** → `git commit -m "feat(fin): A3 frontend 4 views (category/card/deprec/disposal) + api/types/router (spec §10)"`

---

# Phase H — 测试（SQLite 结构）+ gstack QA

## Task H-1: SQLite 结构 / 过滤唯一索引 / 已结账期 / FA012 测试（spec §13.18-21）

**Files:** Create `CP6.Tests/Fin/AssetSqliteTests.cs`

- [ ] **Step 1: 写测试**（SQLite harness，验 DB 级约束——InMemory 不强制过滤唯一索引）
```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;

namespace CP6.Tests.Fin;

public class AssetSqliteTests
{
    private static (CP6Context db, SqliteConnection conn) Sqlite()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<CP6Context>().UseSqlite(conn).Options;
        var db = new CP6Context(options);
        db.Database.EnsureCreated();   // 建全 schema（含过滤唯一索引）
        return (db, conn);
    }

    private static async Task SeedAsync(CP6Context db)
        => await new GlAccountService(db).ImportTemplateAsync(FinCoaTemplate.CnGaap, "seed");

    [Fact] // §13.19 过滤唯一索引：同期第二个批量批次（非 Reversed）被 DB 拦
    public async Task PeriodSingleBatch_FilteredUnique_BlocksSecondBatch()
    {
        var (db, conn) = Sqlite();
        using (conn)
        {
            await SeedAsync(db);
            var periods = new FiscalPeriodService(db, 1);
            var june = (await periods.EnsureOpenAsync(new DateTime(2026, 6, 1), "seed")).Id;
            // 直接插两条批量 Run（RunMode=Manual、Status=Draft）→ 第二条违反过滤唯一索引
            db.DepreciationRuns.Add(new DepreciationRun { Id = Guid.NewGuid(), No = "DEP-1", FiscalPeriodId = june,
                PeriodYearMonth = "2026-06", Status = DepreciationRunStatus.Draft, RunMode = DepreciationRunMode.Manual, RunBy = "a", RunAt = DateTime.Now });
            await db.SaveChangesAsync();
            db.DepreciationRuns.Add(new DepreciationRun { Id = Guid.NewGuid(), No = "DEP-2", FiscalPeriodId = june,
                PeriodYearMonth = "2026-06", Status = DepreciationRunStatus.Draft, RunMode = DepreciationRunMode.Worker, RunBy = "b", RunAt = DateTime.Now });
            await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }

    [Fact] // §13.19 过滤外：DisposalFinal(4) 不受「每期单批」约束，可与批量并存
    public async Task DisposalFinalRun_NotConstrainedBySingleBatch()
    {
        var (db, conn) = Sqlite();
        using (conn)
        {
            await SeedAsync(db);
            var june = (await new FiscalPeriodService(db, 1).EnsureOpenAsync(new DateTime(2026, 6, 1), "seed")).Id;
            db.DepreciationRuns.Add(new DepreciationRun { Id = Guid.NewGuid(), No = "DEP-1", FiscalPeriodId = june,
                PeriodYearMonth = "2026-06", Status = DepreciationRunStatus.Draft, RunMode = DepreciationRunMode.Manual, RunBy = "a", RunAt = DateTime.Now });
            db.DepreciationRuns.Add(new DepreciationRun { Id = Guid.NewGuid(), No = "DEP-2", FiscalPeriodId = june,
                PeriodYearMonth = "2026-06", Status = DepreciationRunStatus.Draft, RunMode = DepreciationRunMode.DisposalFinal, RunBy = "b", RunAt = DateTime.Now });
            await db.SaveChangesAsync();   // 不抛
            Assert.Equal(2, await db.DepreciationRuns.CountAsync());
        }
    }

    [Fact] // §13.19 AssetNo 唯一
    public async Task AssetNo_Unique()
    {
        var (db, conn) = Sqlite();
        using (conn)
        {
            db.AssetCards.Add(new AssetCard { Id = Guid.NewGuid(), AssetNo = "FA-X", Name = "a", CategoryId = Guid.NewGuid(), Status = AssetStatus.Draft });
            await db.SaveChangesAsync();
            db.AssetCards.Add(new AssetCard { Id = Guid.NewGuid(), AssetNo = "FA-X", Name = "b", CategoryId = Guid.NewGuid(), Status = AssetStatus.Draft });
            await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }

    [Fact] // §13.20 已结账期拒过账（FA007，经 AutoPostAsync 的 IsOpenAsync 兜底）
    public async Task ClosedPeriod_PostRejected_FA007()
    {
        var (db, conn) = Sqlite();
        using (conn)
        {
            await SeedAsync(db);
            var periods = new FiscalPeriodService(db, 1);
            var p = await periods.EnsureOpenAsync(new DateTime(2026, 6, 1), "seed");
            var seq = new FinSequenceService(db);
            var dep = new AssetDepreciationService(db, new DepreciationCalculator(), new JournalEntryService(db, periods, seq), periods, seq);
            // 建分类 + 在用卡 + Run 批次（Draft）
            var expAcc = (await db.GlAccounts.FirstAsync(a => a.Code == "5101.01")).Id;
            var cat = new AssetCategory { Id = Guid.NewGuid(), Code = "MC", Name = "机器", DefaultUsefulLifeMonths = 12,
                AssetAccountId = (await db.GlAccounts.FirstAsync(a => a.Code == "1601")).Id,
                AccumDeprecAccountId = (await db.GlAccounts.FirstAsync(a => a.Code == "1602")).Id,
                DeprecExpenseAccountId = expAcc, IsActive = true, DefaultMethod = DepreciationMethod.StraightLine };
            db.AssetCategories.Add(cat);
            db.AssetCards.Add(new AssetCard { Id = Guid.NewGuid(), AssetNo = "FA-1", Name = "冲床", CategoryId = cat.Id,
                OriginalValue = 12000m, Method = DepreciationMethod.StraightLine, UsefulLifeMonths = 12,
                AcquisitionDate = new DateTime(2026, 4, 1), DepreciationStartPeriod = "2026-05", Status = AssetStatus.InUse });
            await db.SaveChangesAsync();
            await dep.RunAsync(p.Id, "admin", DepreciationRunMode.Manual);
            // 结账该期
            p.Status = PeriodStatus.Closed; await db.SaveChangesAsync();
            var run = await db.DepreciationRuns.FirstAsync(r => r.RunMode == DepreciationRunMode.Manual);
            var r = await dep.PostAsync(run.Id, "admin");
            Assert.False(r.Ok);
            Assert.Equal("E-FIN-112", r.Code);   // AutoPostAsync 的已结账期守卫码（A3 FA007 由此兜底）
        }
    }

    [Fact] // §13.21 分类被卡片引用删除 → FA012（控制器逻辑等价校验）
    public async Task Category_ReferencedByCard_CannotDelete_FA012()
    {
        var (db, conn) = Sqlite();
        using (conn)
        {
            var cat = new AssetCategory { Id = Guid.NewGuid(), Code = "MC", Name = "机器", IsActive = true };
            db.AssetCategories.Add(cat);
            db.AssetCards.Add(new AssetCard { Id = Guid.NewGuid(), AssetNo = "FA-1", Name = "x", CategoryId = cat.Id, Status = AssetStatus.Draft });
            await db.SaveChangesAsync();
            bool referenced = await db.AssetCards.AnyAsync(x => x.CategoryId == cat.Id);
            Assert.True(referenced);   // 控制器据此返回 FA012
        }
    }
}
```
> `ClosedPeriod` 测断言 `E-FIN-112`（`AutoPostAsync` 内 `IsOpenAsync` 守卫码）——A3 的「FA007 已结账期拒过账」复用既有 GL 守卫、不另造码；spec §11 FA007 语义涵盖此场景。`PostAsync` 返回 AutoPost 的 `post`（透传 `E-FIN-112`），断言以实际为准。

- [ ] **Step 2: 跑绿** → `dotnet test D:/CP6/CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~AssetSqliteTests" --nologo`，预期全 passed。

- [ ] **Step 3: 全量回归** → `dotnet test D:/CP6/CP6.Tests/CP6.Tests.csproj --nologo`，预期全绿。

- [ ] **Step 4: 提交** → `git commit -m "test(fin): A3 SQLite structural (period-single-batch filtered unique / DisposalFinal exempt / AssetNo unique / closed-period reject / FA012) (spec §13)"`

---

## Task H-2: gstack 端到端 QA 收口（spec §13）

**Files:** 无（QA + 修联调 bug）

- [ ] **Step 1: 起后端 + 前端**（后端 5177，前端 5173，admin/123456；用 `superpowers:gstack` / `browse`）。

- [ ] **Step 2: 端到端路径**（spec §13 末尾）：
  1. **资产分类**建档（机器设备：直线/120月/5%，费用科目选 `5101.01 制造费用—折旧`）；
  2. **资产卡片**建卡（普通卡：选机器设备分类带出默认、原值 120000、购置日上月）→ 验证起折期=次月 → 启用；另建一张**工作量法**卡（填总工作量）+ 一张**期初建卡**（勾 IsOpeningImport，录初始累计）；
  3. **折旧计提**：选当前期间 → Preview 试算（看四法金额）→ Run 建 Draft 批次 → 工作量法明细补录本期量 → Post 过账 → 查汇总凭证（借折旧费用/贷累计折旧、借贷平）+ 资产级明细；
  4. **反冲**该折旧批次 → 验证卡片累计/期数回滚、批次 Reversed；
  5. **处置**四类各一：出售（填价款/税/收款账户，确认 → 看 1606 轧平凭证 + 卡片 Disposed）、报废、转让、盘亏（看 1901 凭证）；
  6. **处置反冲**一张 → 验证卡片还原 PriorStatus、补提连带回滚；
  7. **结账钩子兜底**：不手动计提，直接结账某期 → 验证自动 Accrue（Run+Post）+ 卡片已提；工作量法未录量时结账被硬阻断（FA008 提示先录量）。
  截图留证；修任何 UI/联调 bug（前端 `FA0xx` 映射 i18n、科目下拉只显叶子、银行账户必填校验对齐 FA010、枚举标签）。

- [ ] **Step 3: 提交** → `git commit -m "test(fin): A3 gstack end-to-end QA (category/card/four-method deprec Post+Reverse/four-type disposal/close-hook fallback + UoP hard guard) (spec §13)"`

---

## Self-Review（对照 spec 覆盖）

| spec 章节 / 要点 | Task | 状态 |
|---|---|---|
| §2.1 AssetCategory（三科目路由 + RowVersion） | A-1 | ✅ |
| §2.2 AssetCard（NetBookValue `[NotMapped]` + 期初建卡 + RowVersion） | A-1 | ✅ |
| §2.3 DepreciationRun（RunMode 含 DisposalFinal + 每期单批过滤唯一索引 + RowVersion） | A-1 + A-2 | ✅ |
| §2.4 DepreciationEntry（资产级追溯 + RowVersion） | A-1 | ✅ |
| §2.5 AssetDisposal（PriorStatus + FinalDeprecEntryId + RowVersion） | A-1 | ✅ |
| §2.6 枚举 + `VoucherSource.Depreciation=8/AssetDisposal=9` | A-1 | ✅ |
| §3.1 四法纯函数（DDB 年率×年初净值年内恒定 + 末两年切直线 + 封顶/末期补足 + Y=2 边界） | B-1 | ✅ |
| §3.2 IAssetDepreciationService（Preview/Run/SetWorkload/Post/Reverse/Accrue/Schedule + 资格集 + 三态） | C-1/C-2/C-3 | ✅ |
| §3.3 成本中心派生（卡片>机台；部门派生 deferred 见 §15） | C-1 | ✅ |
| §4.1 处置科目解析（Role 锚点，报废方向按净损益） | D-1 | ✅ |
| §4.2 CreateAsync（可处置守卫 FullyDepreciated/FA002/FA007/FA010 + 快照 PriorStatus 占位） | D-1 | ✅ |
| §4.3 ConfirmAsync（DisposalFinal 补提 + 单张结转 + 重入幂等） | D-2 + C-3（AccrueDisposalFinal） | ✅ |
| §4.4 ReverseAsync（连带回滚补提 + PriorStatus 还原 + FA011） | D-3 | ✅ |
| §5.1 月末折旧汇总凭证（费用×成本中心分行/贷累计） | C-2 | ✅ |
| §5.2 四类处置结转凭证（1606/1901 行内轧平 + 销项税 TAX_OUTPUT + 收/付款共账户） | D-2 | ✅ |
| §5.3 处置月补提折旧凭证（独立过账/独立可红冲） | C-3（AccrueDisposalFinal 调 PostAsync） | ✅ |
| §6.1 结账钩子 AccrueAsync（折旧先于汇兑重估）+ PreCloseCheck 两类预检（软自动补/硬阻断工作量法） | E-1 | ✅ |
| §6.2 AssetDepreciationWorker（月末备草稿/不自动过账/TenantScopeRunner） | E-2 | ✅ |
| §7 API 4 控制器 + 删除守卫 FA012 + 卡片不物理删 | F-1 | ✅ |
| §8.1 过账事务与可恢复性（折旧单事务；处置两次独立过账 + 重入幂等） | C-2 + D-2（注） | ✅ |
| §8.2 幂等（JournalEntryId 幂等键 + AccrueAsync 三态 + FA003） | C-2/C-3 | ✅ |
| §8.3 乐观并发 RowVersion | A-1（5 实体 `[Timestamp]`）；运行时 SQL Server 强制——**SQLite/InMemory 不可仿真 auto-rowversion（同 A4），故不写假绿测试** | ✅ |
| §8.4 锁后守卫（已结账期 AutoPost 拒，复用 GL `E-FIN-112`） | C-2/D-2（经 AutoPostAsync）+ H-1 | ✅ |
| §8.5 反冲守卫 FA009 + 反冲次序 FA011（批含已处置/DisposalFinal 独立反冲） | C-2（ReverseAsync）+ D-3 | ✅ |
| §9.1 科目 Seed（**对账修正**：补 Role 1601/1602/4301 + 新增 1606/1901/6115/6711 + 复用 TAX_OUTPUT，叶子路由 5101.01/6002/6001） | F-2 + 关键既有约定表 | ✅ |
| §9.2 AssetCategory demo seed | F-2 | ✅ |
| §10 菜单 615~618 + 权限 fin-asset-* + 五语 i18n | F-3 + G-1 + G-2 | ✅ |
| §11 错误码 FA001~FA012 | 分散落各 Task + G-1 五语 | ✅ |
| §12 审计（全局 OperLogFilter 自动捕获 POST/PUT/DELETE） | F-1（POST 端点自动入 Sys_OperLog） | ✅ |
| §13 测试分层（单元四法 / InMemory 服务 / SQLite 结构）+ gstack | B-1 + C/D/E TDD + H-1/H-2 | ✅ |
| §14 决策记录 / §15 范围外 / §16 交付协同 | 计划据此落地（VoucherSource 8/9 与 A4 解耦、共享 Fin 三处接入） | ✅ |

**测试方法映射（spec §13.1-21）：** §13.1-5 → `DepreciationCalculatorTests`（6 测）；§13.6-13 → `AssetDepreciationServiceTests`（8 测）；§13.11/14-17 处置 → `AssetDisposalServiceTests`（7 测）；§13.12 三态 + §6.1 硬校验 → `AssetCloseHookTests`（2 测）；§13.18-21 结构 → `AssetSqliteTests`（5 测，RowVersion 见上注）。

**Type 一致性自检：**
- `IAssetDepreciationService` 全签名一处声明（C-1），`AssetDepreciationService` 由 C-1→C-2→C-3 逐 Task 实现，占位 `NotImplementedException` 在 C-1 建以保编译，无悬空。`AccrueDisposalFinalAsync` 返回 `DisposalFinalResult`（C-1 定义，D-2 消费 `Ok/Code/Skipped/DeprecEntryId`）。
- `IAssetDisposalService`（D-1）`Create/Confirm/Reverse/Get/List`；`AssetDisposalService` 由 D-1→D-2→D-3 实现；依赖 `IAssetDepreciationService`（处置补提委托），无循环依赖（折旧服务不依赖处置）。
- Role 名跨 Task 一致：`FIXED_ASSET/ACCUM_DEPRECIATION/ASSET_CLEARING/PENDING_PROPERTY_LOSS/ASSET_DISPOSAL_PL/NON_OP_EXPENSE/NON_OP_INCOME/TAX_OUTPUT`（D-1 解析 + D-2 销项税 + F-2 seed 同名）。
- 科目编码跨 Task 一致：折旧费用叶子 `5101.01/6002/6001`、清理 `1606/1901`、损益 `6115/6711/4301`、销项税 `2221.02`、`1601/1602`（关键既有约定表 + F-2 seed + 测试夹具同口径）。
- 错误码 `FA001~FA012` = `FinResult` 返回码 = i18n LangKey（服务 + 测试断言 + G-1 五语同名）。
- `DepreciationRunMode.DisposalFinal` 不计入 FA003/过滤唯一索引：A-2 索引过滤 `RunMode IN (1,2,3)` ⊥ DisposalFinal(4)；服务 RunAsync/AccrueAsync 守卫 `RunMode != DisposalFinal`——口径一致（A-2 + C-1 + C-3）。

**已知推迟（spec §15）：** 减值/重估；后续资本化支出；租赁资产；卡片大变更工作流；工作量法 MES 产量自动回写（本期手工录）；折旧计划表 PDF；资产标签/二维码盘点；月中购置按天折旧；**DeptId→成本中心派生**（CostCenter 无 Dept 关联字段，C-1 注）。

**潜在落码注意（交接执行者）：**
1. **`RequirePermission` 命名空间**：F-1 控制器 `using CP6.WebApi.Filters` 为假定——落码前 grep `class RequirePermissionAttribute` 核实实际命名空间，与 `JournalEntryController` 的 using 对齐。
2. **`PeriodStatus` / `AccountType` / `AccountSide` 枚举名**：服务/Seed 中 `PeriodStatus.Open/Closed`、`AccountType.*`、`AccountSide.Debit/Credit`、`GlAccount.NormalSide` 均按既有代码假定——以 `FiscalPeriod.cs`/`GlAccount.cs`/`FinCoaTemplate.cs` 实际为准微调。
3. **CoA 基线测试**：F-2 给 CnGaapRows 加 4 科目 + 3 Role，若既有测试断言科目总数/特定行需同步更新（F-2 Step4）。
4. **DDB `NetBookValueAtYearStart`**：服务按闭式 `OV×(1−2/Y)^(y−1)` 填（MVP 无跳期/重估恒成立）；末两年 SL 基数由计算器内部闭式重算（不用传入值），二者自洽。引入跳期/重估时改传实际年初净值（spec §3.1 注）。
5. **已结账期码**：A3 FA007「已结账期拒过账」复用 `AutoPostAsync` 的 `E-FIN-112`（不另造码）；H-1 测试断言以实际透传码为准。
6. **前端列表端点**：F-1 控制器为聚焦账务给了核心端点；折旧批次列表/明细查询、处置列表等纯查询端点按 G-2 视图需要补（直查 `CP6Context`，权限 `view`）。

**硬映射缺口：** 无。spec §2~§16 各要点 + §13.1-21 测试均有明确 Task 对应；唯一不可仿真项（RowVersion auto-rowversion 的 SQLite 测试）已按 A4 precedent 显式声明由运行时 SQL Server 强制、不写假绿。

---

## 执行交接

计划存 `docs/superpowers/plans/2026-06-19-a3-fixed-asset-plan.md`。源 spec：`docs/superpowers/specs/2026-06-19-a3-fixed-asset-design.md`（A3-D1~D9 + review 修订全采纳）。执行序：A 数据模型 → B 折旧引擎 → C 折旧服务 → D 处置 → E 期间钩子+Worker → F 控制器/Seed/菜单 → G 前端/i18n → H 测试/QA。

**推荐执行方式：Subagent-Driven**——每 Task 派新 subagent，任务间评审；**高难度 Task（★★/★★★）**：B-1（四法公式正确性）、C-2/C-3（汇总凭证/三态/补提）、D-2（四类处置凭证轧平 + DisposalFinal 解耦）、D-3（连带回滚 + FA011 次序）、E-1（结账钩子顺序 + 硬校验），评审重点放账务平衡/幂等/反冲次序正确性。关联：[[project_a3]]（待建）、[[project_finance_module]]、[[project_a4_bank_reconciliation]]（同落地范式 + VoucherSource 号段协同）、[[project_module_taxonomy]]。

---

**Plan complete and saved to `docs/superpowers/plans/2026-06-19-a3-fixed-asset-plan.md`. Two execution options:**

**1. Subagent-Driven (recommended)** — 每 Task 派新 subagent，任务间两阶段评审，快速迭代。

**2. Inline Execution** — 本会话内按 executing-plans 批量执行 + 检查点评审。

**Which approach?**
