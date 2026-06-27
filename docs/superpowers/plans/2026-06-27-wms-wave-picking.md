# WMS 波次拣货（Wave Picking）执行子系统 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 CP6 WMS 已完整的出库链之上叠加波次拣货执行子系统：多张已引当出库指示合波 → 按动线序炸开行级拣货任务（落库）→ 后端扫描校验式确认（满拣/短拣）→ 波次级批量出荷，纯加法不动现有签名。

**Architecture:** 方案 C 纯新子系统。新增 3 实体（`WavePlan`/`WaveOrder`/`WavePickTask`）+ `IWaveService`/`WaveService` + `WaveController`。拣货确认正常路径不动库存（RSV 已在 Allocate 预留、OUT 留给现有 `ShipAsync`），唯一库存变动是短拣 `UNRSV`，经 `IStockMovementService`。复用现有 `ShipAsync`/接缝②(ERP回写)/`MaterialShortage`/`IWmsSequenceService`。

**Tech Stack:** .NET 8 / EF Core (SQL Server, InMemory 测试) / xUnit / Vue3 + TS + Element Plus / SignalR(可选) / i18n 走 `Sys_Langs` 五语。

**Spec:** `docs/superpowers/specs/2026-06-26-wms-wave-picking-design.md`（v1.1，§0~§13）。

---

## ⚠️ 执行环境（必读，非任务内容）

- 主工作区 `D:\CP6` 当前在 **`feat/wfs-form-inbox`** 分支，且**有活的并行 WFS 会话在跑 TDD**（持续提交）。**禁止在主工作区 `git checkout` 切分支**——会抽掉并行会话的工作区。
- 本 WMS 工作必须在**隔离 git worktree**（基于 `feat/wms-wave-picking` 分支）里执行：
  ```bash
  git worktree add /c/Users/tt/AppData/Local/Temp/cp6-wms-wt feat/wms-wave-picking
  # 之后所有编辑/编译/测试/commit 都在该 worktree 目录内进行
  ```
- 每个 Task 的 commit 在 worktree 内执行，提交到 `feat/wms-wave-picking`。全部完成后 `git worktree remove`。
- 编译/测试命令在 worktree 根目录跑：`dotnet build CP6.slnx`、`dotnet test CP6.Tests`。
- 迁移命令：`dotnet ef migrations add <Name> --project CP6.Core --startup-project CP6.WebApi`（在 worktree 内）。

---

## 关键既有契约（落码依据，实测 2026-06-26）

**基类 `BaseBizEntity`**（`CP6.Entity/BaseBizEntity.cs`，链 `BaseEntity→BaseTenantEntity→BaseBizEntity`）字段：
`Id`(Guid), `Creator`(string?), `CreateDate`(DateTime), `Modifier`(string?), `ModifyDate`(DateTime?), `TenantId`(Guid), `IsDeleted`(bool), `RowVersion`(byte[]? `[Timestamp]`乐观锁)。
> 注意字段名是 `Creator/CreateDate/Modifier/ModifyDate`（不是 CreateTime）。新建置 `Creator=userName`，更新置 `Modifier=userName; ModifyDate=DateTime.Now`。

**`IStockMovementService.ApplyAsync(StockMovementRequest req, CancellationToken ct=default) → Task<string>`**（返回 TxnNo）。
`StockMovementRequest`：`TxnType`(string), `WarehouseCd`, `LocationCd`, `ProductCd`, `LotNo`, `Qty`(decimal), `UnitCd?`, `UnitPrice?`, `RelatedNo?`, `RelatedType?`, `OperatorCd?`, `Remark?`, `ExpiryDate?`, `ReceiveDate?`, `OwnerType`(string), `OwnerCd?`, `PaperRollNo?`。
`WmsTxnType`(`WmsTxnType.cs`)：`IN/OUT/MOVE/ADJ/RSV/UNRSV`（均 string 常量）。
> **短拣 UNRSV 调用直接照抄 `OutboundService.CancelOrderAsync` 里现成的 UNRSV `ApplyAsync` 调用**（同一操作），把 `RelatedType="WAVE_PICK"`、`RelatedNo=task.WavePickTaskNo`、`Remark` 带原因。

**`IWmsSequenceService.NextAsync(string prefix, DateTime? date=null) → Task<string>`**（格式 `{PREFIX}{yyyyMM}{NNNN}`，全期累计）。

**`IOutboundService.ShipAsync(string outboundNo, ShipRequest req, string? userName) → Task<string?>`**（出荷区分返回 PackageNo，否则 null；内部发 OUT 扣库 + 置 Completed(4) + 接缝② ERP 回写）。
`ShipRequest`：`CaseQty`(int), `TotalWeightKg?`, `TotalVolumeM3?`, `CarrierCd?`, `TrackingNo?`, `Remarks?`。

**`IMaterialShortageService.CreateAsync(MaterialShortage entity) → Task<MaterialShortage>`**。
`MaterialShortage`：`WorkOrderNo`(**Required**), `RelatedOutboundNo?`, `ProductCd`, `LotNo?`, `RequiredQty`, `AvailableQty`, `DetectedAt`, `ResolvedAt?`, `Status`(string), `Remark?`。状态常量 `MaterialShortageStatus.Open="OPEN"`。
> `WorkOrderNo` 非空：短拣时置 `源出库指示.WorkOrderNo ?? ""`。

**`OutboundOrder`**：`OutboundNo`, `OutboundType`(int), `WorkOrderNo?`, `WebOrderNo?`, `WarehouseCd`, `Status`(int), `Priority`(int), `CarrierCd?`, `Details`([NotMapped])。
`OutboundOrderDetail`：`OutboundNo`, `LineNo`, `ProductCd`, `RequiredQty`, `AllocatedQty`, `ShippedQty`, `LotNo?`, `LocationCd?`, `WarehouseCd?`, `AllocateTxnNo?`, `ShipTxnNo?`。
`OutboundOrderStatus`：Draft0/Confirmed1/Allocated2/Picking3/Completed4/PartialAllocated5/Cancelled9。
`OutboundType`：Material1/Shipping2/InternalTransfer3/Other9。

**Service/Controller/测试 模式**：见 `StockTakeService.cs`/`StockTakeController.cs`/`StockTakeServiceTests.cs`/`TestHelper.cs`。事务用 `if (_db.Database.ProviderName != "Microsoft.EntityFrameworkCore.InMemory") tx = await _db.Database.BeginTransactionAsync()`。并发 409 用 `catch (DbUpdateConcurrencyException) → Conflict(new {code=409, message="WAVE-MSG-072"})`。过滤唯一索引样板见 `CP6Context.cs` 的 `Sys_Menu.HasIndex(...).IsUnique().HasFilter("[MenuKey] IS NOT NULL")`。

---

## 文件结构（决定分解）

| 文件 | 职责 |
|---|---|
| `CP6.Entity/DomainModels/Wms/WavePlan.cs` | 波次头实体 |
| `CP6.Entity/DomainModels/Wms/WaveOrder.cs` | 波次↔出库指示关联（含 IsActive 过滤唯一 + OriginalOutboundStatus）|
| `CP6.Entity/DomainModels/Wms/WavePickTask.cs` | 行级拣货任务（落库核心）|
| `CP6.Entity/DomainModels/Wms/WaveEnums.cs` | `WavePlanStatus`/`WavePickTaskStatus` 常量 |
| `CP6.Core/Services/Wms/IWaveService.cs` | 服务接口 + DTO/Request/Result records |
| `CP6.Core/Services/Wms/WaveService.cs` | 服务实现（组波/下发/拣货/完成/出荷/取消 + ComputePickSequence 纯函数）|
| `CP6.Core/EFDbContext/CP6Context.cs` | +3 DbSet + OnModelCreating 索引/过滤唯一索引；`Location` 加 `PickSeq` |
| `CP6.Entity/DomainModels/Wms/Location.cs` | 加列 `PickSeq int?` |
| `CP6.Core/Migrations/*_AddWmsWavePicking.cs` | EF 迁移（生成）|
| `CP6.WebApi/Controllers/Wms/WaveController.cs` | REST 端点 |
| `CP6.WebApi/Seed/I18nWaveScreenSeed.cs` | WAVE-MSG + wms.wave.* + partialAllocated 五语词条 |
| `CP6.WebApi/Program.cs` | DI 注册 `IWaveService`；注册 I18nWaveScreenSeed |
| `cp6.web/src/types/wms/wave.ts` | 前端类型 |
| `cp6.web/src/api/wms/wave.ts` | 前端 api（9 端点）|
| `cp6.web/src/views/wms/WaveListView.vue` | 波次列表 |
| `cp6.web/src/views/wms/WaveBuildView.vue` | 组波 |
| `cp6.web/src/views/wms/WavePickView.vue` | 拣货执行 |
| `cp6.web/src/router/index.ts` | +3 路由 |
| `cp6.web/src/views/wms/OutboundOrderListView.vue`, `OutboundOrderView.vue` | statusMap 补 5 |
| `docs/seeds/wms-wave-menu-seed.sql` | 菜单种子 |
| `CP6.Tests/Wms/Wave*Tests.cs` | 6 套测试 |

---

# Phase W1 — 数据地基

### Task 1: 枚举常量 `WaveEnums.cs`

**Files:**
- Create: `CP6.Entity/DomainModels/Wms/WaveEnums.cs`

- [ ] **Step 1: 创建枚举常量文件**

```csharp
namespace CP6.Entity.DomainModels.Wms;

/// <summary>波次头状态</summary>
public static class WavePlanStatus
{
    public const int Draft = 0;       // 组波草稿
    public const int Released = 1;    // 已下发，拣货中
    public const int Picked = 2;      // 拣货完成，待出
    public const int Completed = 3;   // 成员单全部出荷
    public const int Cancelled = 9;   // 取消
}

/// <summary>拣货任务状态</summary>
public static class WavePickTaskStatus
{
    public const int Pending = 0;
    public const int Picking = 1;
    public const int Picked = 2;      // 满拣
    public const int Short = 3;       // 短拣收尾（含全额短拣）
    public const int Cancelled = 9;
}
```

- [ ] **Step 2: 编译验证**

Run: `dotnet build CP6.Entity`
Expected: 编译通过。

- [ ] **Step 3: Commit**

```bash
git add CP6.Entity/DomainModels/Wms/WaveEnums.cs
git commit -m "feat(wms-wave): T1 WavePlanStatus/WavePickTaskStatus 枚举常量"
```

---

### Task 2: 三个实体 + Location 加列

**Files:**
- Create: `CP6.Entity/DomainModels/Wms/WavePlan.cs`
- Create: `CP6.Entity/DomainModels/Wms/WaveOrder.cs`
- Create: `CP6.Entity/DomainModels/Wms/WavePickTask.cs`
- Modify: `CP6.Entity/DomainModels/Wms/Location.cs`

- [ ] **Step 1: 创建 `WavePlan.cs`**

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wms;

/// <summary>波次头（Wave Picking, MSBBWM-WAVE）</summary>
[Table("T_WavePlan")]
public class WavePlan : BaseBizEntity
{
    [Required, MaxLength(20)] public string WaveNo { get; set; } = string.Empty;
    [Required, MaxLength(10)] public string WarehouseCd { get; set; } = string.Empty;
    public int Status { get; set; } = WavePlanStatus.Draft;
    [MaxLength(20)] public string PickStrategy { get; set; } = "OrderPath";
    [MaxLength(1000)] public string? FilterSnapshotJson { get; set; }
    public int OrderCount { get; set; } = 0;
    public int TaskCount { get; set; } = 0;
    public int PickedTaskCount { get; set; } = 0;
    [MaxLength(20)] public string? AssignedTo { get; set; }
    public int Priority { get; set; } = 2;
    public DateTime? ReleasedAt { get; set; }
    public DateTime? PickedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    [MaxLength(500)] public string? Remarks { get; set; }

    [NotMapped] public List<WaveOrder> Orders { get; set; } = new();
    [NotMapped] public List<WavePickTask> Tasks { get; set; } = new();
}
```

- [ ] **Step 2: 创建 `WaveOrder.cs`**

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wms;

/// <summary>波次↔出库指示关联</summary>
[Table("T_WaveOrder")]
public class WaveOrder : BaseBizEntity
{
    [Required, MaxLength(20)] public string WaveNo { get; set; } = string.Empty;
    [Required, MaxLength(20)] public string OutboundNo { get; set; } = string.Empty;
    /// <summary>入波时快照成员单状态（2 或 5），取消时恢复用</summary>
    public int OriginalOutboundStatus { get; set; }
    public int? OrderPriority { get; set; }
    /// <summary>活动成员标志：波次 Completed/Cancelled 时置 false，供过滤唯一索引</summary>
    public bool IsActive { get; set; } = true;
}
```

- [ ] **Step 3: 创建 `WavePickTask.cs`**

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wms;

/// <summary>行级拣货任务（落库核心表）</summary>
[Table("T_WavePickTask")]
public class WavePickTask : BaseBizEntity
{
    [Required, MaxLength(20)] public string WavePickTaskNo { get; set; } = string.Empty;
    [Required, MaxLength(20)] public string WaveNo { get; set; } = string.Empty;
    [Required, MaxLength(20)] public string SourceOutboundNo { get; set; } = string.Empty;
    public int SourceLineNo { get; set; }
    [Required, MaxLength(20)] public string ProductCd { get; set; } = string.Empty;
    [MaxLength(100)] public string? ProductName { get; set; }
    [MaxLength(30)] public string? LotNo { get; set; }
    [Required, MaxLength(10)] public string WarehouseCd { get; set; } = string.Empty;
    [MaxLength(30)] public string? FromLocationCd { get; set; }
    public int PickSeq { get; set; } = 0;
    [Column(TypeName = "decimal(21,8)")] public decimal RequiredQty { get; set; }
    [Column(TypeName = "decimal(21,8)")] public decimal PickedQty { get; set; } = 0m;
    [Column(TypeName = "decimal(21,8)")] public decimal ShortQty { get; set; } = 0m;
    [MaxLength(200)] public string? ShortReason { get; set; }
    public int Status { get; set; } = WavePickTaskStatus.Pending;
    [MaxLength(20)] public string? AssignedTo { get; set; }
    [MaxLength(20)] public string? PickedBy { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? DoneAt { get; set; }
    [MaxLength(25)] public string? ShortageNo { get; set; }
    [MaxLength(500)] public string? Remarks { get; set; }
}
```

- [ ] **Step 4: `Location.cs` 加列**（在 `Barcode` 字段后追加）

```csharp
    /// <summary>显式动线序覆盖（波次拣货）；为空时回退坐标/字典序算法</summary>
    public int? PickSeq { get; set; }
```

- [ ] **Step 5: 编译验证**

Run: `dotnet build CP6.Entity`
Expected: 编译通过。

- [ ] **Step 6: Commit**

```bash
git add CP6.Entity/DomainModels/Wms/WavePlan.cs CP6.Entity/DomainModels/Wms/WaveOrder.cs CP6.Entity/DomainModels/Wms/WavePickTask.cs CP6.Entity/DomainModels/Wms/Location.cs
git commit -m "feat(wms-wave): T2 WavePlan/WaveOrder/WavePickTask 实体 + Location.PickSeq 加列"
```

---

### Task 3: CP6Context DbSet + 索引（含过滤唯一索引）

**Files:**
- Modify: `CP6.Core/EFDbContext/CP6Context.cs`

- [ ] **Step 1: 加 DbSet**（在现有 WMS DbSet 区块，如 `StockTakeDetails` 之后）

```csharp
public DbSet<WavePlan> WavePlans { get; set; }
public DbSet<WaveOrder> WaveOrders { get; set; }
public DbSet<WavePickTask> WavePickTasks { get; set; }
```

- [ ] **Step 2: 加 OnModelCreating 索引配置**（在现有 WMS `modelBuilder.Entity<StockTake>(...)` 区块附近）

```csharp
modelBuilder.Entity<WavePlan>(e =>
{
    e.HasIndex(x => x.WaveNo).IsUnique();
    e.HasIndex(x => new { x.Status, x.IsDeleted });
    e.HasIndex(x => new { x.WarehouseCd, x.IsDeleted });
    e.HasIndex(x => x.AssignedTo);
});

modelBuilder.Entity<WaveOrder>(e =>
{
    e.HasIndex(x => new { x.WaveNo, x.OutboundNo }).IsUnique();
    e.HasIndex(x => x.OutboundNo);
    // 一单同时只属一个活动波次——数据库级强约束（过滤唯一索引）
    e.HasIndex(x => x.OutboundNo)
        .IsUnique()
        .HasFilter("[IsActive] = 1 AND [IsDeleted] = 0")
        .HasDatabaseName("UX_WaveOrder_ActiveOutbound");
});

modelBuilder.Entity<WavePickTask>(e =>
{
    e.HasIndex(x => x.WavePickTaskNo).IsUnique();
    e.HasIndex(x => x.WaveNo);
    e.HasIndex(x => new { x.WaveNo, x.PickSeq });
    e.HasIndex(x => new { x.Status, x.IsDeleted });
    e.HasIndex(x => x.AssignedTo);
    e.HasIndex(x => x.SourceOutboundNo);
});
```

- [ ] **Step 3: 编译验证**

Run: `dotnet build CP6.Core`
Expected: 编译通过。

- [ ] **Step 4: Commit**

```bash
git add CP6.Core/EFDbContext/CP6Context.cs
git commit -m "feat(wms-wave): T3 CP6Context +3 DbSet + 过滤唯一索引 UX_WaveOrder_ActiveOutbound"
```

---

### Task 4: EF 迁移 `AddWmsWavePicking`

**Files:**
- Create: `CP6.Core/Migrations/*_AddWmsWavePicking.cs`（生成）

- [ ] **Step 1: 生成迁移**

Run: `dotnet ef migrations add AddWmsWavePicking --project CP6.Core --startup-project CP6.WebApi`
Expected: 生成 3 张表 + `T_Location` 加 `PickSeq` 列 + `UX_WaveOrder_ActiveOutbound` 过滤唯一索引。

- [ ] **Step 2: 检查迁移文件**

打开生成的 `*_AddWmsWavePicking.cs`，确认 `Up` 内：
- `CreateTable("T_WavePlan"/"T_WaveOrder"/"T_WavePickTask")`，每表含 `RowVersion rowversion`；
- `AddColumn("PickSeq", "T_Location", nullable: true)`；
- `CreateIndex(... "UX_WaveOrder_ActiveOutbound" ... filter: "[IsActive] = 1 AND [IsDeleted] = 0", unique: true)`。

- [ ] **Step 3: 编译验证**

Run: `dotnet build CP6.slnx`
Expected: 编译通过。

- [ ] **Step 4: Commit**

```bash
git add CP6.Core/Migrations/
git commit -m "feat(wms-wave): T4 迁移 AddWmsWavePicking(3表+Location.PickSeq+过滤唯一索引)"
```

---

# Phase W2 — 组波 + 下发

### Task 5: `IWaveService` 接口 + DTO/Request/Result

**Files:**
- Create: `CP6.Core/Services/Wms/IWaveService.cs`

- [ ] **Step 1: 创建接口与传输类型**

```csharp
namespace CP6.Core.Services.Wms;

public interface IWaveService
{
    Task<List<WaveAvailableOrderDto>> SearchAvailableOrdersAsync(WaveOrderFilterDto filter);
    Task<string> CreateWaveAsync(CreateWaveRequest req, string? userName);
    Task ReleaseWaveAsync(string waveNo, string? userName);
    Task<List<WavePickTaskDto>> GetWaveTasksAsync(string waveNo, string? assignedTo = null);
    Task<List<WavePlanDto>> SearchWavesAsync(WaveSearchFilterDto filter);
    Task<WavePlanDetailDto?> GetWaveAsync(string waveNo);
    Task ConfirmPickAsync(string taskNo, ConfirmPickRequest req, string? userName);
    Task CompleteWaveAsync(string waveNo, string? userName);
    Task<BatchShipResultDto> BatchShipWaveAsync(string waveNo, BatchShipRequest req, string? userName);
    Task CancelWaveAsync(string waveNo, string? userName);
}

public class WaveOrderFilterDto
{
    public string? WarehouseCd { get; set; }
    public int? OutboundType { get; set; }
    public DateTime? PlannedDateFrom { get; set; }
    public DateTime? PlannedDateTo { get; set; }
    public string? CarrierCd { get; set; }
    public int? Priority { get; set; }
    public string? WebOrderNo { get; set; }
}

public class WaveAvailableOrderDto
{
    public string OutboundNo { get; set; } = string.Empty;
    public int OutboundType { get; set; }
    public int Status { get; set; }
    public string WarehouseCd { get; set; } = string.Empty;
    public string? WebOrderNo { get; set; }
    public string? CarrierCd { get; set; }
    public int Priority { get; set; }
    public DateTime PlannedDate { get; set; }
    public int LineCount { get; set; }
    public decimal AllocatedTotal { get; set; }
}

public class CreateWaveRequest
{
    public List<string> OrderNos { get; set; } = new();
    public string WarehouseCd { get; set; } = string.Empty;
    public string? AssignedTo { get; set; }
    public int Priority { get; set; } = 2;
    public string? FilterSnapshotJson { get; set; }
}

public class WavePickTaskDto
{
    public string WavePickTaskNo { get; set; } = string.Empty;
    public string WaveNo { get; set; } = string.Empty;
    public string SourceOutboundNo { get; set; } = string.Empty;
    public int SourceLineNo { get; set; }
    public string ProductCd { get; set; } = string.Empty;
    public string? ProductName { get; set; }
    public string? LotNo { get; set; }
    public string WarehouseCd { get; set; } = string.Empty;
    public string? FromLocationCd { get; set; }
    public int PickSeq { get; set; }
    public decimal RequiredQty { get; set; }
    public decimal PickedQty { get; set; }
    public decimal ShortQty { get; set; }
    public int Status { get; set; }
}

public class WaveSearchFilterDto
{
    public int? Status { get; set; }
    public string? WarehouseCd { get; set; }
}

public class WavePlanDto
{
    public string WaveNo { get; set; } = string.Empty;
    public string WarehouseCd { get; set; } = string.Empty;
    public int Status { get; set; }
    public int OrderCount { get; set; }
    public int TaskCount { get; set; }
    public int PickedTaskCount { get; set; }
    public string? AssignedTo { get; set; }
    public int Priority { get; set; }
    public DateTime CreateDate { get; set; }
}

public class WavePlanDetailDto : WavePlanDto
{
    public List<WaveAvailableOrderDto> Members { get; set; } = new();
    public List<WavePickTaskDto> Tasks { get; set; } = new();
}

public class ConfirmPickRequest
{
    public decimal PickedQty { get; set; }        // 允许 0（全额短拣）
    public string ScannedLocationCd { get; set; } = string.Empty;
    public string ScannedProductCd { get; set; } = string.Empty;
    public string? ScannedLotNo { get; set; }
    public string? ShortReason { get; set; }
}

public class BatchShipRequest
{
    public string? CarrierCd { get; set; }
    public string? TrackingNo { get; set; }
    public string? Remarks { get; set; }
}

public class ShipFailureDto
{
    public string OutboundNo { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public class BatchShipResultDto
{
    public List<string> Succeeded { get; set; } = new();
    public List<ShipFailureDto> Failed { get; set; } = new();
    public List<string> Skipped { get; set; } = new();
}
```

- [ ] **Step 2: 编译验证**

Run: `dotnet build CP6.Core`
Expected: 编译失败（`IWaveService` 无实现，但接口本身应编译通过；若引用未定义类型则修正）。仅接口文件应通过。

- [ ] **Step 3: Commit**

```bash
git add CP6.Core/Services/Wms/IWaveService.cs
git commit -m "feat(wms-wave): T5 IWaveService 接口 + DTO/Request/Result"
```

---

### Task 6: `ComputePickSequence` 纯函数（TDD）

**Files:**
- Create: `CP6.Core/Services/Wms/WaveService.cs`（先放纯函数 + 骨架）
- Test: `CP6.Tests/Wms/WaveServiceTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using Xunit;

namespace CP6.Tests.Wms;

public class WaveServiceTests
{
    [Fact]
    public void ComputePickSequence_ExplicitPickSeq_WinsFirst()
    {
        var tasks = new List<WavePickTask>
        {
            new() { WavePickTaskNo = "T1", FromLocationCd = "B" },
            new() { WavePickTaskNo = "T2", FromLocationCd = "A" },
        };
        var locs = new List<Location>
        {
            new() { LocationCd = "A", PickSeq = 99 },
            new() { LocationCd = "B", PickSeq = 1 },
        };
        WaveService.ComputePickSequence(tasks, locs);
        Assert.Equal(1, tasks.Single(t => t.WavePickTaskNo == "T1").PickSeq); // B(PickSeq=1) 先
        Assert.Equal(2, tasks.Single(t => t.WavePickTaskNo == "T2").PickSeq);
    }

    [Fact]
    public void ComputePickSequence_Serpentine_ByZThenAisleSnake()
    {
        var tasks = new List<WavePickTask>
        {
            new() { WavePickTaskNo = "T1", FromLocationCd = "L1" }, // X1Y1
            new() { WavePickTaskNo = "T2", FromLocationCd = "L2" }, // X1Y2
            new() { WavePickTaskNo = "T3", FromLocationCd = "L3" }, // X2Y1
            new() { WavePickTaskNo = "T4", FromLocationCd = "L4" }, // X2Y2
        };
        var locs = new List<Location>
        {
            new() { LocationCd = "L1", XCoord = 1, YCoord = 1, ZCoord = 0 },
            new() { LocationCd = "L2", XCoord = 1, YCoord = 2, ZCoord = 0 },
            new() { LocationCd = "L3", XCoord = 2, YCoord = 1, ZCoord = 0 },
            new() { LocationCd = "L4", XCoord = 2, YCoord = 2, ZCoord = 0 },
        };
        WaveService.ComputePickSequence(tasks, locs);
        // 通路1(X=1,奇)Y升序: L1,L2; 通路2(X=2,偶)Y降序: L4,L3
        Assert.Equal(1, tasks.Single(t => t.WavePickTaskNo == "T1").PickSeq);
        Assert.Equal(2, tasks.Single(t => t.WavePickTaskNo == "T2").PickSeq);
        Assert.Equal(3, tasks.Single(t => t.WavePickTaskNo == "T4").PickSeq);
        Assert.Equal(4, tasks.Single(t => t.WavePickTaskNo == "T3").PickSeq);
    }

    [Fact]
    public void ComputePickSequence_NoCoords_FallbackLexical()
    {
        var tasks = new List<WavePickTask>
        {
            new() { WavePickTaskNo = "T1", FromLocationCd = "Z-01" },
            new() { WavePickTaskNo = "T2", FromLocationCd = "A-01" },
        };
        var locs = new List<Location>
        {
            new() { LocationCd = "Z-01" },
            new() { LocationCd = "A-01" },
        };
        WaveService.ComputePickSequence(tasks, locs);
        Assert.Equal(1, tasks.Single(t => t.WavePickTaskNo == "T2").PickSeq); // A-01 先
        Assert.Equal(2, tasks.Single(t => t.WavePickTaskNo == "T1").PickSeq);
    }
}
```

- [ ] **Step 2: 运行确认失败**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~WaveServiceTests.ComputePickSequence"`
Expected: FAIL（`WaveService` 不存在 / `ComputePickSequence` 未定义）。

- [ ] **Step 3: 实现 WaveService 骨架 + ComputePickSequence**

```csharp
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public class WaveService : IWaveService
{
    private readonly CP6Context _db;
    private readonly IWmsSequenceService _seq;
    private readonly IStockMovementService _stock;
    private readonly IOutboundService _outbound;
    private readonly IMaterialShortageService _shortage;
    private const string WavePrefix = "WAVE";
    private const string TaskPrefix = "WPT";

    public WaveService(CP6Context db, IWmsSequenceService seq, IStockMovementService stock,
        IOutboundService outbound, IMaterialShortageService shortage)
    {
        _db = db; _seq = seq; _stock = stock; _outbound = outbound; _shortage = shortage;
    }

    /// <summary>动线序：显式 PickSeq → 坐标蛇形(Z↑→通路X分组，奇升偶降Y) → LocationCd 字典序。原地写回 task.PickSeq=1..N。</summary>
    public static void ComputePickSequence(List<WavePickTask> tasks, List<Location> locations)
    {
        var locMap = locations.ToDictionary(l => l.LocationCd, l => l);
        int Rank(WavePickTask t)
        {
            return 0; // 占位，下面用排序键
        }
        var ordered = tasks
            .OrderBy(t => GetExplicit(t, locMap) ?? int.MaxValue)
            .ThenBy(t => GetZ(t, locMap))
            .ThenBy(t => GetX(t, locMap))
            .ThenBy(t => SnakeY(t, locMap))
            .ThenBy(t => t.FromLocationCd ?? string.Empty, StringComparer.Ordinal)
            .ToList();
        for (int i = 0; i < ordered.Count; i++) ordered[i].PickSeq = i + 1;
    }

    private static int? GetExplicit(WavePickTask t, Dictionary<string, Location> m)
        => t.FromLocationCd != null && m.TryGetValue(t.FromLocationCd, out var l) ? l.PickSeq : null;
    private static int GetZ(WavePickTask t, Dictionary<string, Location> m)
        => t.FromLocationCd != null && m.TryGetValue(t.FromLocationCd, out var l) ? (l.ZCoord ?? 0) : 0;
    private static int GetX(WavePickTask t, Dictionary<string, Location> m)
        => t.FromLocationCd != null && m.TryGetValue(t.FromLocationCd, out var l) ? (l.XCoord ?? 0) : 0;
    private static int SnakeY(WavePickTask t, Dictionary<string, Location> m)
    {
        if (t.FromLocationCd == null || !m.TryGetValue(t.FromLocationCd, out var l)) return 0;
        var x = l.XCoord ?? 0; var y = l.YCoord ?? 0;
        return (x % 2 == 0) ? -y : y; // 偶通路 Y 降序
    }

    // 其余方法在后续 Task 实现
    public Task<List<WaveAvailableOrderDto>> SearchAvailableOrdersAsync(WaveOrderFilterDto filter) => throw new NotImplementedException();
    public Task<string> CreateWaveAsync(CreateWaveRequest req, string? userName) => throw new NotImplementedException();
    public Task ReleaseWaveAsync(string waveNo, string? userName) => throw new NotImplementedException();
    public Task<List<WavePickTaskDto>> GetWaveTasksAsync(string waveNo, string? assignedTo = null) => throw new NotImplementedException();
    public Task<List<WavePlanDto>> SearchWavesAsync(WaveSearchFilterDto filter) => throw new NotImplementedException();
    public Task<WavePlanDetailDto?> GetWaveAsync(string waveNo) => throw new NotImplementedException();
    public Task ConfirmPickAsync(string taskNo, ConfirmPickRequest req, string? userName) => throw new NotImplementedException();
    public Task CompleteWaveAsync(string waveNo, string? userName) => throw new NotImplementedException();
    public Task<BatchShipResultDto> BatchShipWaveAsync(string waveNo, BatchShipRequest req, string? userName) => throw new NotImplementedException();
    public Task CancelWaveAsync(string waveNo, string? userName) => throw new NotImplementedException();
}
```
> 删除上面无用的本地 `Rank` 函数（误留）；最终只保留 OrderBy 链与辅助方法。

- [ ] **Step 4: 运行确认通过**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~WaveServiceTests.ComputePickSequence"`
Expected: 3 PASS。

- [ ] **Step 5: Commit**

```bash
git add CP6.Core/Services/Wms/WaveService.cs CP6.Tests/Wms/WaveServiceTests.cs
git commit -m "feat(wms-wave): T6 ComputePickSequence 动线序纯函数(显式/蛇形/字典序)+单测"
```

---

### Task 7: `SearchAvailableOrdersAsync` + `CreateWaveAsync`（TDD）

**Files:**
- Modify: `CP6.Core/Services/Wms/WaveService.cs`
- Test: `CP6.Tests/Wms/WaveServiceTests.cs`

- [ ] **Step 1: 加测试辅助 + 失败测试**（追加到 `WaveServiceTests`）

```csharp
    private static WaveService CreateSvc(out CP6.Core.EFDbContext.CP6Context db)
    {
        db = TestHelper.CreateInMemoryContext();
        db.Warehouses.Add(new Warehouse { WarehouseCd = "W01", WarehouseName = "メイン" });
        db.SaveChanges();
        var seq = new WmsSequenceService(db);
        var stock = new StockMovementService(db, seq);
        var outbound = new OutboundService(db, seq, stock); // 若 OutboundService ctor 参数不同，按实际补齐(见 OutboundService.cs)
        var shortage = new MaterialShortageService(db);
        return new WaveService(db, seq, stock, outbound, shortage);
    }

    private static async Task<string> SeedAllocatedOrderAsync(CP6.Core.EFDbContext.CP6Context db,
        string outboundNo, int status = 2, decimal allocated = 10m)
    {
        db.OutboundOrders.Add(new OutboundOrder
        {
            OutboundNo = outboundNo, OutboundType = 2, WarehouseCd = "W01",
            Status = status, PlannedDate = new DateTime(2026, 6, 27),
        });
        db.OutboundOrderDetails.Add(new OutboundOrderDetail
        {
            OutboundNo = outboundNo, LineNo = 1, ProductCd = "P001",
            RequiredQty = allocated, AllocatedQty = allocated, ShippedQty = 0m,
            LotNo = "L1", LocationCd = "A-01", WarehouseCd = "W01",
        });
        await db.SaveChangesAsync();
        return outboundNo;
    }

    [Fact]
    public async Task SearchAvailableOrders_ReturnsAllocatedNotInActiveWave()
    {
        var svc = CreateSvc(out var db);
        await SeedAllocatedOrderAsync(db, "OUT1", status: 2);
        await SeedAllocatedOrderAsync(db, "OUT2", status: 5);
        await SeedAllocatedOrderAsync(db, "OUT3", status: 1); // Confirmed，不应入波
        var list = await svc.SearchAvailableOrdersAsync(new WaveOrderFilterDto { WarehouseCd = "W01" });
        Assert.Equal(2, list.Count);
        Assert.DoesNotContain(list, x => x.OutboundNo == "OUT3");
    }

    [Fact]
    public async Task CreateWave_SnapshotsOriginalStatus_AndExcludesFromAvailable()
    {
        var svc = CreateSvc(out var db);
        await SeedAllocatedOrderAsync(db, "OUT1", status: 5);
        var waveNo = await svc.CreateWaveAsync(new CreateWaveRequest
        { OrderNos = new() { "OUT1" }, WarehouseCd = "W01" }, "u1");
        Assert.StartsWith("WAVE", waveNo);
        var wo = await db.WaveOrders.SingleAsync(x => x.WaveNo == waveNo);
        Assert.Equal(5, wo.OriginalOutboundStatus);
        Assert.True(wo.IsActive);
        var avail = await svc.SearchAvailableOrdersAsync(new WaveOrderFilterDto { WarehouseCd = "W01" });
        Assert.Empty(avail); // 已入活动波次
    }

    [Fact]
    public async Task CreateWave_EmptyOrders_Throws020()
    {
        var svc = CreateSvc(out _);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.CreateWaveAsync(new CreateWaveRequest { OrderNos = new(), WarehouseCd = "W01" }, "u1"));
        Assert.Contains("WAVE-MSG-020", ex.Message);
    }

    [Fact]
    public async Task CreateWave_NonAllocatedOrder_Throws030()
    {
        var svc = CreateSvc(out var db);
        await SeedAllocatedOrderAsync(db, "OUT1", status: 1);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.CreateWaveAsync(new CreateWaveRequest { OrderNos = new() { "OUT1" }, WarehouseCd = "W01" }, "u1"));
        Assert.Contains("WAVE-MSG-030", ex.Message);
    }
```

- [ ] **Step 2: 运行确认失败**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~WaveServiceTests"`
Expected: 新增 4 测 FAIL（NotImplementedException）。

- [ ] **Step 3: 实现两方法**（替换 WaveService 中对应 NotImplementedException）

```csharp
    public async Task<List<WaveAvailableOrderDto>> SearchAvailableOrdersAsync(WaveOrderFilterDto filter)
    {
        var activeOutboundNos = _db.WaveOrders.Where(w => w.IsActive && !w.IsDeleted).Select(w => w.OutboundNo);
        var q = _db.OutboundOrders.Where(o => !o.IsDeleted
            && (o.Status == OutboundOrderStatus.Allocated || o.Status == OutboundOrderStatus.PartialAllocated)
            && !activeOutboundNos.Contains(o.OutboundNo));
        if (!string.IsNullOrWhiteSpace(filter.WarehouseCd)) q = q.Where(o => o.WarehouseCd == filter.WarehouseCd);
        if (filter.OutboundType.HasValue) q = q.Where(o => o.OutboundType == filter.OutboundType);
        if (filter.PlannedDateFrom.HasValue) q = q.Where(o => o.PlannedDate >= filter.PlannedDateFrom);
        if (filter.PlannedDateTo.HasValue) q = q.Where(o => o.PlannedDate <= filter.PlannedDateTo);
        if (!string.IsNullOrWhiteSpace(filter.CarrierCd)) q = q.Where(o => o.CarrierCd == filter.CarrierCd);
        if (filter.Priority.HasValue) q = q.Where(o => o.Priority == filter.Priority);
        if (!string.IsNullOrWhiteSpace(filter.WebOrderNo)) q = q.Where(o => o.WebOrderNo!.Contains(filter.WebOrderNo!));

        var orders = await q.OrderBy(o => o.Priority).ThenBy(o => o.PlannedDate).ToListAsync();
        var nos = orders.Select(o => o.OutboundNo).ToList();
        var detailAgg = await _db.OutboundOrderDetails
            .Where(d => nos.Contains(d.OutboundNo) && !d.IsDeleted)
            .GroupBy(d => d.OutboundNo)
            .Select(g => new { OutboundNo = g.Key, Lines = g.Count(), Alloc = g.Sum(x => x.AllocatedQty) })
            .ToListAsync();
        var aggMap = detailAgg.ToDictionary(x => x.OutboundNo);
        return orders.Select(o => new WaveAvailableOrderDto
        {
            OutboundNo = o.OutboundNo, OutboundType = o.OutboundType, Status = o.Status,
            WarehouseCd = o.WarehouseCd, WebOrderNo = o.WebOrderNo, CarrierCd = o.CarrierCd,
            Priority = o.Priority, PlannedDate = o.PlannedDate,
            LineCount = aggMap.TryGetValue(o.OutboundNo, out var a) ? a.Lines : 0,
            AllocatedTotal = aggMap.TryGetValue(o.OutboundNo, out var a2) ? a2.Alloc : 0m,
        }).ToList();
    }

    public async Task<string> CreateWaveAsync(CreateWaveRequest req, string? userName)
    {
        if (req.OrderNos == null || req.OrderNos.Count == 0)
            throw new InvalidOperationException("WAVE-MSG-020: 波次成员出库指示不能为空");

        var orders = await _db.OutboundOrders
            .Where(o => req.OrderNos.Contains(o.OutboundNo) && !o.IsDeleted).ToListAsync();
        foreach (var no in req.OrderNos)
        {
            var o = orders.FirstOrDefault(x => x.OutboundNo == no);
            if (o == null) throw new InvalidOperationException($"WAVE-MSG-070: 出库指示不存在 {no}");
            if (o.Status != OutboundOrderStatus.Allocated && o.Status != OutboundOrderStatus.PartialAllocated)
                throw new InvalidOperationException($"WAVE-MSG-030: 出库指示状态不允许入波 {no}");
            var already = await _db.WaveOrders.AnyAsync(w => w.OutboundNo == no && w.IsActive && !w.IsDeleted);
            if (already) throw new InvalidOperationException($"WAVE-MSG-031: 出库指示已属于其它活动波次 {no}");
        }

        var waveNo = await _seq.NextAsync(WavePrefix);
        _db.WavePlans.Add(new WavePlan
        {
            WaveNo = waveNo, WarehouseCd = req.WarehouseCd, Status = WavePlanStatus.Draft,
            OrderCount = orders.Count, AssignedTo = req.AssignedTo, Priority = req.Priority,
            FilterSnapshotJson = req.FilterSnapshotJson, Creator = userName,
        });
        foreach (var o in orders)
        {
            _db.WaveOrders.Add(new WaveOrder
            {
                WaveNo = waveNo, OutboundNo = o.OutboundNo, OriginalOutboundStatus = o.Status,
                OrderPriority = o.Priority, IsActive = true, Creator = userName,
            });
        }
        try { await _db.SaveChangesAsync(); }
        catch (DbUpdateException) // 过滤唯一索引并发冲突兜底
        { throw new InvalidOperationException("WAVE-MSG-031: 出库指示已属于其它活动波次"); }
        return waveNo;
    }
```

- [ ] **Step 4: 运行确认通过**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~WaveServiceTests"`
Expected: 全部 PASS。

- [ ] **Step 5: Commit**

```bash
git add CP6.Core/Services/Wms/WaveService.cs CP6.Tests/Wms/WaveServiceTests.cs
git commit -m "feat(wms-wave): T7 SearchAvailableOrders+CreateWave(唯一性守卫020/030/031+OriginalStatus快照)"
```

---

### Task 8: `ReleaseWaveAsync`（炸任务 + 动线序）（TDD）

**Files:**
- Modify: `CP6.Core/Services/Wms/WaveService.cs`
- Test: `CP6.Tests/Wms/WaveServiceTests.cs`

- [ ] **Step 1: 失败测试**

```csharp
    [Fact]
    public async Task Release_ExplodesTasks_FlipsMembersToPicking()
    {
        var svc = CreateSvc(out var db);
        db.Locations.Add(new Location { LocationCd = "A-01", WarehouseCd = "W01" });
        await db.SaveChangesAsync();
        await SeedAllocatedOrderAsync(db, "OUT1", status: 2, allocated: 10m);
        var waveNo = await svc.CreateWaveAsync(new CreateWaveRequest { OrderNos = new() { "OUT1" }, WarehouseCd = "W01" }, "u1");

        await svc.ReleaseWaveAsync(waveNo, "u1");

        var wave = await db.WavePlans.SingleAsync(w => w.WaveNo == waveNo);
        Assert.Equal(WavePlanStatus.Released, wave.Status);
        Assert.Equal(1, wave.TaskCount);
        var tasks = await db.WavePickTasks.Where(t => t.WaveNo == waveNo).ToListAsync();
        Assert.Single(tasks);
        Assert.Equal(10m, tasks[0].RequiredQty);
        Assert.Equal("A-01", tasks[0].FromLocationCd);
        Assert.Equal(1, tasks[0].PickSeq);
        var order = await db.OutboundOrders.SingleAsync(o => o.OutboundNo == "OUT1");
        Assert.Equal(OutboundOrderStatus.Picking, order.Status);
    }

    [Fact]
    public async Task Release_NotDraft_Throws043()
    {
        var svc = CreateSvc(out var db);
        await SeedAllocatedOrderAsync(db, "OUT1");
        var waveNo = await svc.CreateWaveAsync(new CreateWaveRequest { OrderNos = new() { "OUT1" }, WarehouseCd = "W01" }, "u1");
        await svc.ReleaseWaveAsync(waveNo, "u1");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.ReleaseWaveAsync(waveNo, "u1"));
        Assert.Contains("WAVE-MSG-043", ex.Message);
    }
```

- [ ] **Step 2: 运行确认失败**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~WaveServiceTests.Release"`
Expected: FAIL（NotImplementedException）。

- [ ] **Step 3: 实现 `ReleaseWaveAsync` + 私有 `LoadTasksAndSequenceAsync`**

```csharp
    public async Task ReleaseWaveAsync(string waveNo, string? userName)
    {
        var wave = await _db.WavePlans.FirstOrDefaultAsync(w => w.WaveNo == waveNo && !w.IsDeleted)
            ?? throw new InvalidOperationException("WAVE-MSG-070: 波次不存在");
        if (wave.Status != WavePlanStatus.Draft)
            throw new InvalidOperationException("WAVE-MSG-043: 仅草稿波次可下发");

        var memberNos = await _db.WaveOrders.Where(w => w.WaveNo == waveNo && w.IsActive && !w.IsDeleted)
            .Select(w => w.OutboundNo).ToListAsync();
        var orders = await _db.OutboundOrders.Where(o => memberNos.Contains(o.OutboundNo) && !o.IsDeleted).ToListAsync();
        var details = await _db.OutboundOrderDetails.Where(d => memberNos.Contains(d.OutboundNo) && !d.IsDeleted).ToListAsync();

        var newTasks = new List<WavePickTask>();
        foreach (var o in orders)
        {
            if (o.Status != OutboundOrderStatus.Allocated && o.Status != OutboundOrderStatus.PartialAllocated)
                throw new InvalidOperationException($"WAVE-MSG-030: 成员单状态已变更 {o.OutboundNo}");
            foreach (var d in details.Where(x => x.OutboundNo == o.OutboundNo && (x.AllocatedQty - x.ShippedQty) > 0m))
            {
                var task = new WavePickTask
                {
                    WavePickTaskNo = await _seq.NextAsync(TaskPrefix),
                    WaveNo = waveNo, SourceOutboundNo = d.OutboundNo, SourceLineNo = d.LineNo,
                    ProductCd = d.ProductCd, ProductName = d.ProductName, LotNo = d.LotNo,
                    WarehouseCd = d.WarehouseCd ?? o.WarehouseCd, FromLocationCd = d.LocationCd,
                    RequiredQty = d.AllocatedQty - d.ShippedQty, Status = WavePickTaskStatus.Pending,
                    AssignedTo = wave.AssignedTo, Creator = userName,
                };
                newTasks.Add(task);
                _db.WavePickTasks.Add(task);
            }
            o.Status = OutboundOrderStatus.Picking;
            o.Modifier = userName; o.ModifyDate = DateTime.Now;
        }

        var locCds = newTasks.Where(t => t.FromLocationCd != null).Select(t => t.FromLocationCd!).Distinct().ToList();
        var locs = await _db.Locations.Where(l => locCds.Contains(l.LocationCd) && !l.IsDeleted).ToListAsync();
        ComputePickSequence(newTasks, locs);

        wave.Status = WavePlanStatus.Released;
        wave.TaskCount = newTasks.Count;
        wave.ReleasedAt = DateTime.Now;
        wave.Modifier = userName; wave.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }
```

- [ ] **Step 4: 运行确认通过**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~WaveServiceTests"`
Expected: 全部 PASS。

- [ ] **Step 5: Commit**

```bash
git add CP6.Core/Services/Wms/WaveService.cs CP6.Tests/Wms/WaveServiceTests.cs
git commit -m "feat(wms-wave): T8 ReleaseWave 炸任务+动线序+成员单进Picking"
```

---

# Phase W3 — 拣货执行

### Task 9: `GetWaveTasksAsync` + `SearchWavesAsync` + `GetWaveAsync`（TDD）

**Files:**
- Modify: `CP6.Core/Services/Wms/WaveService.cs`
- Test: `CP6.Tests/Wms/WaveServiceTests.cs`

- [ ] **Step 1: 失败测试**

```csharp
    [Fact]
    public async Task GetWaveTasks_ReturnsByPickSeq()
    {
        var svc = CreateSvc(out var db);
        await SeedAllocatedOrderAsync(db, "OUT1");
        var waveNo = await svc.CreateWaveAsync(new CreateWaveRequest { OrderNos = new() { "OUT1" }, WarehouseCd = "W01" }, "u1");
        await svc.ReleaseWaveAsync(waveNo, "u1");
        var tasks = await svc.GetWaveTasksAsync(waveNo);
        Assert.Single(tasks);
        Assert.Equal(1, tasks[0].PickSeq);
    }
```

- [ ] **Step 2: 运行确认失败**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~WaveServiceTests.GetWaveTasks"`
Expected: FAIL。

- [ ] **Step 3: 实现三查询方法**

```csharp
    public async Task<List<WavePickTaskDto>> GetWaveTasksAsync(string waveNo, string? assignedTo = null)
    {
        var q = _db.WavePickTasks.Where(t => t.WaveNo == waveNo && !t.IsDeleted);
        if (!string.IsNullOrWhiteSpace(assignedTo))
            q = q.Where(t => t.AssignedTo == assignedTo || t.AssignedTo == null);
        return await q.OrderBy(t => t.PickSeq).Select(t => new WavePickTaskDto
        {
            WavePickTaskNo = t.WavePickTaskNo, WaveNo = t.WaveNo, SourceOutboundNo = t.SourceOutboundNo,
            SourceLineNo = t.SourceLineNo, ProductCd = t.ProductCd, ProductName = t.ProductName,
            LotNo = t.LotNo, WarehouseCd = t.WarehouseCd, FromLocationCd = t.FromLocationCd,
            PickSeq = t.PickSeq, RequiredQty = t.RequiredQty, PickedQty = t.PickedQty,
            ShortQty = t.ShortQty, Status = t.Status,
        }).ToListAsync();
    }

    public async Task<List<WavePlanDto>> SearchWavesAsync(WaveSearchFilterDto filter)
    {
        var q = _db.WavePlans.Where(w => !w.IsDeleted);
        if (filter.Status.HasValue) q = q.Where(w => w.Status == filter.Status);
        if (!string.IsNullOrWhiteSpace(filter.WarehouseCd)) q = q.Where(w => w.WarehouseCd == filter.WarehouseCd);
        return await q.OrderByDescending(w => w.CreateDate).Select(w => new WavePlanDto
        {
            WaveNo = w.WaveNo, WarehouseCd = w.WarehouseCd, Status = w.Status, OrderCount = w.OrderCount,
            TaskCount = w.TaskCount, PickedTaskCount = w.PickedTaskCount, AssignedTo = w.AssignedTo,
            Priority = w.Priority, CreateDate = w.CreateDate,
        }).ToListAsync();
    }

    public async Task<WavePlanDetailDto?> GetWaveAsync(string waveNo)
    {
        var w = await _db.WavePlans.FirstOrDefaultAsync(x => x.WaveNo == waveNo && !x.IsDeleted);
        if (w == null) return null;
        var dto = new WavePlanDetailDto
        {
            WaveNo = w.WaveNo, WarehouseCd = w.WarehouseCd, Status = w.Status, OrderCount = w.OrderCount,
            TaskCount = w.TaskCount, PickedTaskCount = w.PickedTaskCount, AssignedTo = w.AssignedTo,
            Priority = w.Priority, CreateDate = w.CreateDate,
            Tasks = await GetWaveTasksAsync(waveNo),
        };
        var memberNos = await _db.WaveOrders.Where(x => x.WaveNo == waveNo && !x.IsDeleted).Select(x => x.OutboundNo).ToListAsync();
        dto.Members = await _db.OutboundOrders.Where(o => memberNos.Contains(o.OutboundNo))
            .Select(o => new WaveAvailableOrderDto
            { OutboundNo = o.OutboundNo, OutboundType = o.OutboundType, Status = o.Status, WarehouseCd = o.WarehouseCd,
              WebOrderNo = o.WebOrderNo, CarrierCd = o.CarrierCd, Priority = o.Priority, PlannedDate = o.PlannedDate }).ToListAsync();
        return dto;
    }
```

- [ ] **Step 4: 运行确认通过 + Commit**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~WaveServiceTests"`
Expected: PASS。

```bash
git add CP6.Core/Services/Wms/WaveService.cs CP6.Tests/Wms/WaveServiceTests.cs
git commit -m "feat(wms-wave): T9 GetWaveTasks/SearchWaves/GetWave 查询方法"
```

---

### Task 10: `ConfirmPickAsync`（后端扫描校验 + 满拣/短拣/全额短拣）（TDD）

**Files:**
- Modify: `CP6.Core/Services/Wms/WaveService.cs`
- Test: `CP6.Tests/Wms/WaveServiceTests.cs`

- [ ] **Step 1: 失败测试（满拣 / 短拣 / 全额短拣 / 必填原因 / 超拣）**

```csharp
    private static async Task<(string waveNo, string taskNo)> SeedReleasedWaveAsync(
        WaveService svc, CP6.Core.EFDbContext.CP6Context db, decimal allocated = 10m)
    {
        db.Locations.Add(new Location { LocationCd = "A-01", WarehouseCd = "W01" });
        db.Stocks.Add(new Stock { WarehouseCd = "W01", LocationCd = "A-01", ProductCd = "P001",
            LotNo = "L1", PhysicalQty = allocated, AllocatedQty = allocated, AvailableQty = 0m });
        await db.SaveChangesAsync();
        await SeedAllocatedOrderAsync(db, "OUT1", status: 2, allocated: allocated);
        var waveNo = await svc.CreateWaveAsync(new CreateWaveRequest { OrderNos = new() { "OUT1" }, WarehouseCd = "W01" }, "u1");
        await svc.ReleaseWaveAsync(waveNo, "u1");
        var taskNo = (await db.WavePickTasks.SingleAsync(t => t.WaveNo == waveNo)).WavePickTaskNo;
        return (waveNo, taskNo);
    }

    [Fact]
    public async Task ConfirmPick_FullPick_NoStockTxn()
    {
        var svc = CreateSvc(out var db);
        var (_, taskNo) = await SeedReleasedWaveAsync(svc, db);
        var txnBefore = await db.StockTransactions.CountAsync();
        await svc.ConfirmPickAsync(taskNo, new ConfirmPickRequest
        { PickedQty = 10m, ScannedLocationCd = "A-01", ScannedProductCd = "P001", ScannedLotNo = "L1" }, "u1");
        var task = await db.WavePickTasks.SingleAsync(t => t.WavePickTaskNo == taskNo);
        Assert.Equal(WavePickTaskStatus.Picked, task.Status);
        Assert.Equal(10m, task.PickedQty);
        Assert.Equal(txnBefore, await db.StockTransactions.CountAsync()); // 满拣不动库存
    }

    [Fact]
    public async Task ConfirmPick_ShortPick_UnrsvAndShortageAndDownAlloc()
    {
        var svc = CreateSvc(out var db);
        var (_, taskNo) = await SeedReleasedWaveAsync(svc, db, allocated: 10m);
        await svc.ConfirmPickAsync(taskNo, new ConfirmPickRequest
        { PickedQty = 6m, ScannedLocationCd = "A-01", ScannedProductCd = "P001", ScannedLotNo = "L1", ShortReason = "破损" }, "u1");
        var task = await db.WavePickTasks.SingleAsync(t => t.WavePickTaskNo == taskNo);
        Assert.Equal(WavePickTaskStatus.Short, task.Status);
        Assert.Equal(4m, task.ShortQty);
        Assert.Equal("破损", task.ShortReason);
        Assert.NotNull(task.ShortageNo);
        var detail = await db.OutboundOrderDetails.SingleAsync(d => d.OutboundNo == "OUT1" && d.LineNo == 1);
        Assert.Equal(6m, detail.AllocatedQty); // 下调到实拣量
        var stock = await db.Stocks.SingleAsync(s => s.ProductCd == "P001");
        Assert.Equal(6m, stock.AllocatedQty); // UNRSV 释放 4
        Assert.True(await db.MaterialShortages.AnyAsync(m => m.RelatedOutboundNo == "OUT1" && m.Status == "OPEN"));
    }

    [Fact]
    public async Task ConfirmPick_ZeroPick_FullShort()
    {
        var svc = CreateSvc(out var db);
        var (_, taskNo) = await SeedReleasedWaveAsync(svc, db, allocated: 10m);
        await svc.ConfirmPickAsync(taskNo, new ConfirmPickRequest
        { PickedQty = 0m, ScannedLocationCd = "A-01", ScannedProductCd = "P001", ScannedLotNo = "L1", ShortReason = "全缺" }, "u1");
        var task = await db.WavePickTasks.SingleAsync(t => t.WavePickTaskNo == taskNo);
        Assert.Equal(WavePickTaskStatus.Short, task.Status);
        Assert.Equal(10m, task.ShortQty);
        var detail = await db.OutboundOrderDetails.SingleAsync(d => d.OutboundNo == "OUT1" && d.LineNo == 1);
        Assert.Equal(0m, detail.AllocatedQty);
    }

    [Fact]
    public async Task ConfirmPick_ShortWithoutReason_Throws053()
    {
        var svc = CreateSvc(out var db);
        var (_, taskNo) = await SeedReleasedWaveAsync(svc, db);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.ConfirmPickAsync(taskNo,
            new ConfirmPickRequest { PickedQty = 5m, ScannedLocationCd = "A-01", ScannedProductCd = "P001", ScannedLotNo = "L1" }, "u1"));
        Assert.Contains("WAVE-MSG-053", ex.Message);
    }

    [Fact]
    public async Task ConfirmPick_OverPick_Throws052()
    {
        var svc = CreateSvc(out var db);
        var (_, taskNo) = await SeedReleasedWaveAsync(svc, db, allocated: 10m);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.ConfirmPickAsync(taskNo,
            new ConfirmPickRequest { PickedQty = 11m, ScannedLocationCd = "A-01", ScannedProductCd = "P001", ScannedLotNo = "L1" }, "u1"));
        Assert.Contains("WAVE-MSG-052", ex.Message);
    }

    [Fact]
    public async Task ConfirmPick_AlreadyDone_Throws051()
    {
        var svc = CreateSvc(out var db);
        var (_, taskNo) = await SeedReleasedWaveAsync(svc, db);
        await svc.ConfirmPickAsync(taskNo, new ConfirmPickRequest
        { PickedQty = 10m, ScannedLocationCd = "A-01", ScannedProductCd = "P001", ScannedLotNo = "L1" }, "u1");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.ConfirmPickAsync(taskNo,
            new ConfirmPickRequest { PickedQty = 10m, ScannedLocationCd = "A-01", ScannedProductCd = "P001", ScannedLotNo = "L1" }, "u1"));
        Assert.Contains("WAVE-MSG-051", ex.Message);
    }
```

- [ ] **Step 2: 运行确认失败**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~WaveServiceTests.ConfirmPick"`
Expected: FAIL。

- [ ] **Step 3: 实现 `ConfirmPickAsync`**
> UNRSV `ApplyAsync` 调用照抄 `OutboundService.CancelOrderAsync` 里现成的 UNRSV 调用形态（同一操作），只改 `RelatedType/RelatedNo/Remark`。

```csharp
    public async Task ConfirmPickAsync(string taskNo, ConfirmPickRequest req, string? userName)
    {
        var task = await _db.WavePickTasks.FirstOrDefaultAsync(t => t.WavePickTaskNo == taskNo && !t.IsDeleted)
            ?? throw new InvalidOperationException("WAVE-MSG-070: 拣货任务不存在");
        var wave = await _db.WavePlans.FirstOrDefaultAsync(w => w.WaveNo == task.WaveNo && !w.IsDeleted)
            ?? throw new InvalidOperationException("WAVE-MSG-070: 波次不存在");
        if (wave.Status != WavePlanStatus.Released)
            throw new InvalidOperationException("WAVE-MSG-043: 波次非拣货中状态");
        if (task.Status != WavePickTaskStatus.Pending && task.Status != WavePickTaskStatus.Picking)
            throw new InvalidOperationException("WAVE-MSG-051: 该拣货任务已收尾");

        // 后端扫描校验（权威）
        if (req.ScannedLocationCd != (task.FromLocationCd ?? string.Empty))
            throw new InvalidOperationException("WAVE-MSG-060: 扫描库位与任务不符");
        if (req.ScannedProductCd != task.ProductCd)
            throw new InvalidOperationException("WAVE-MSG-061: 扫描製品与任务不符");
        if (!string.IsNullOrEmpty(task.LotNo) && req.ScannedLotNo != task.LotNo)
            throw new InvalidOperationException("WAVE-MSG-062: 扫描批次与任务不符");

        if (req.PickedQty < 0m) throw new InvalidOperationException("WAVE-MSG-021: 拣货数量非法");
        if (req.PickedQty > task.RequiredQty) throw new InvalidOperationException("WAVE-MSG-052: 实拣量不可超过应拣量");

        if (req.PickedQty == task.RequiredQty) // 满拣
        {
            task.PickedQty = req.PickedQty;
            task.Status = WavePickTaskStatus.Picked;
        }
        else // 短拣（含 0）
        {
            if (string.IsNullOrWhiteSpace(req.ShortReason))
                throw new InvalidOperationException("WAVE-MSG-053: 短拣必须填写原因");
            var shortQty = task.RequiredQty - req.PickedQty;
            await _stock.ApplyAsync(new StockMovementRequest
            {
                TxnType = WmsTxnType.UNRSV, WarehouseCd = task.WarehouseCd, LocationCd = task.FromLocationCd ?? string.Empty,
                ProductCd = task.ProductCd, LotNo = task.LotNo ?? string.Empty, Qty = shortQty,
                RelatedType = "WAVE_PICK", RelatedNo = task.WavePickTaskNo, OperatorCd = userName,
                Remark = $"短拣解除 {task.WavePickTaskNo}: {req.ShortReason}",
            });
            var detail = await _db.OutboundOrderDetails.FirstOrDefaultAsync(d =>
                d.OutboundNo == task.SourceOutboundNo && d.LineNo == task.SourceLineNo && !d.IsDeleted);
            if (detail != null) detail.AllocatedQty -= shortQty;
            var srcOrder = await _db.OutboundOrders.FirstOrDefaultAsync(o => o.OutboundNo == task.SourceOutboundNo);
            var shortage = await _shortage.CreateAsync(new MaterialShortage
            {
                WorkOrderNo = srcOrder?.WorkOrderNo ?? string.Empty, RelatedOutboundNo = task.SourceOutboundNo,
                ProductCd = task.ProductCd, LotNo = task.LotNo, RequiredQty = shortQty, AvailableQty = 0m,
                DetectedAt = DateTime.Now, Status = MaterialShortageStatus.Open,
                Remark = $"波次{task.WaveNo}短拣: {req.ShortReason}",
            });
            task.PickedQty = req.PickedQty; task.ShortQty = shortQty; task.ShortReason = req.ShortReason;
            task.ShortageNo = shortage.RelatedOutboundNo == null ? null : shortage.ProductCd; // 见下注：用 shortage 业务键
            task.Status = WavePickTaskStatus.Short;
        }
        task.PickedBy = userName; task.DoneAt = DateTime.Now;
        task.Modifier = userName; task.ModifyDate = DateTime.Now;

        wave.PickedTaskCount = await _db.WavePickTasks.CountAsync(t => t.WaveNo == wave.WaveNo
            && (t.Status == WavePickTaskStatus.Picked || t.Status == WavePickTaskStatus.Short)) ;
        // 上面 CountAsync 在 SaveChanges 前不含当前内存改动，故改为内存计数：见 Step 3b 修正
        await _db.SaveChangesAsync();
    }
```

- [ ] **Step 3b: 修正 PickedTaskCount 计数（内存态）+ ShortageNo 回填**

把上一步两处用注释标记的写法替换为正确实现：

ShortageNo 回填——`MaterialShortage` 无独立业务编号字段，用其主键 `Id` 字符串回填：
```csharp
            task.ShortageNo = shortage.Id.ToString();
```

PickedTaskCount——先设置当前 task 状态后，用已加载集合在内存计算（避免 SaveChanges 前 DB 计数遗漏当前改动）：
```csharp
        await _db.SaveChangesAsync(); // 先存当前 task 改动
        wave.PickedTaskCount = await _db.WavePickTasks.CountAsync(t => t.WaveNo == wave.WaveNo && !t.IsDeleted
            && (t.Status == WavePickTaskStatus.Picked || t.Status == WavePickTaskStatus.Short));
        wave.Modifier = userName; wave.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
```
> 即：本方法做两次 SaveChanges——先落 task/库存/短缺改动，再回写波次进度计数。

- [ ] **Step 4: 运行确认通过**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~WaveServiceTests.ConfirmPick"`
Expected: 6 PASS。

- [ ] **Step 5: Commit**

```bash
git add CP6.Core/Services/Wms/WaveService.cs CP6.Tests/Wms/WaveServiceTests.cs
git commit -m "feat(wms-wave): T10 ConfirmPick(后端扫描校验060/061/062+满拣不动库存+短拣UNRSV起异常+pickedQty=0+必填因053)"
```

---

### Task 11: `WaveScanValidationTests`（扫描校验专项）

**Files:**
- Create: `CP6.Tests/Wms/WaveScanValidationTests.cs`

- [ ] **Step 1: 写测试**

```csharp
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CP6.Tests.Wms;

public class WaveScanValidationTests
{
    // 复用 WaveServiceTests 的私有 seed 逻辑：此处内联最小 seed
    private static async Task<(WaveService svc, CP6.Core.EFDbContext.CP6Context db, string taskNo)> SetupAsync(bool withLot = true)
    {
        var db = TestHelper.CreateInMemoryContext();
        db.Warehouses.Add(new Warehouse { WarehouseCd = "W01", WarehouseName = "M" });
        db.Locations.Add(new Location { LocationCd = "A-01", WarehouseCd = "W01" });
        db.Stocks.Add(new Stock { WarehouseCd = "W01", LocationCd = "A-01", ProductCd = "P001",
            LotNo = withLot ? "L1" : "", PhysicalQty = 10m, AllocatedQty = 10m, AvailableQty = 0m });
        db.OutboundOrders.Add(new OutboundOrder { OutboundNo = "OUT1", OutboundType = 2, WarehouseCd = "W01", Status = 2, PlannedDate = new DateTime(2026,6,27) });
        db.OutboundOrderDetails.Add(new OutboundOrderDetail { OutboundNo = "OUT1", LineNo = 1, ProductCd = "P001",
            RequiredQty = 10m, AllocatedQty = 10m, LotNo = withLot ? "L1" : "", LocationCd = "A-01", WarehouseCd = "W01" });
        await db.SaveChangesAsync();
        var seq = new WmsSequenceService(db);
        var stock = new StockMovementService(db, seq);
        var svc = new WaveService(db, seq, stock, new OutboundService(db, seq, stock), new MaterialShortageService(db));
        var waveNo = await svc.CreateWaveAsync(new CreateWaveRequest { OrderNos = new() { "OUT1" }, WarehouseCd = "W01" }, "u1");
        await svc.ReleaseWaveAsync(waveNo, "u1");
        var taskNo = (await db.WavePickTasks.SingleAsync(t => t.WaveNo == waveNo)).WavePickTaskNo;
        return (svc, db, taskNo);
    }

    [Fact]
    public async Task WrongLocation_Throws060()
    {
        var (svc, _, taskNo) = await SetupAsync();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.ConfirmPickAsync(taskNo,
            new ConfirmPickRequest { PickedQty = 10m, ScannedLocationCd = "B-99", ScannedProductCd = "P001", ScannedLotNo = "L1" }, "u1"));
        Assert.Contains("WAVE-MSG-060", ex.Message);
    }

    [Fact]
    public async Task WrongProduct_Throws061()
    {
        var (svc, _, taskNo) = await SetupAsync();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.ConfirmPickAsync(taskNo,
            new ConfirmPickRequest { PickedQty = 10m, ScannedLocationCd = "A-01", ScannedProductCd = "P999", ScannedLotNo = "L1" }, "u1"));
        Assert.Contains("WAVE-MSG-061", ex.Message);
    }

    [Fact]
    public async Task WrongLot_Throws062()
    {
        var (svc, _, taskNo) = await SetupAsync(withLot: true);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.ConfirmPickAsync(taskNo,
            new ConfirmPickRequest { PickedQty = 10m, ScannedLocationCd = "A-01", ScannedProductCd = "P001", ScannedLotNo = "L9" }, "u1"));
        Assert.Contains("WAVE-MSG-062", ex.Message);
    }

    [Fact]
    public async Task NoLotTask_SkipsLotCheck()
    {
        var (svc, db, taskNo) = await SetupAsync(withLot: false);
        await svc.ConfirmPickAsync(taskNo, new ConfirmPickRequest
        { PickedQty = 10m, ScannedLocationCd = "A-01", ScannedProductCd = "P001", ScannedLotNo = null }, "u1");
        var task = await db.WavePickTasks.SingleAsync(t => t.WavePickTaskNo == taskNo);
        Assert.Equal(WavePickTaskStatus.Picked, task.Status);
    }
}
```

- [ ] **Step 2: 运行确认通过**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~WaveScanValidationTests"`
Expected: 4 PASS。

- [ ] **Step 3: Commit**

```bash
git add CP6.Tests/Wms/WaveScanValidationTests.cs
git commit -m "test(wms-wave): T11 WaveScanValidationTests 扫描校验060/061/062+无Lot跳过"
```

---

# Phase W4 — 完成 + 批量出荷 + 取消

### Task 12: `CompleteWaveAsync`（TDD）

**Files:**
- Modify: `CP6.Core/Services/Wms/WaveService.cs`
- Test: `CP6.Tests/Wms/WaveServiceTests.cs`

- [ ] **Step 1: 失败测试**

```csharp
    [Fact]
    public async Task Complete_AllDone_SetsPicked()
    {
        var svc = CreateSvc(out var db);
        var (waveNo, taskNo) = await SeedReleasedWaveAsync(svc, db);
        await svc.ConfirmPickAsync(taskNo, new ConfirmPickRequest
        { PickedQty = 10m, ScannedLocationCd = "A-01", ScannedProductCd = "P001", ScannedLotNo = "L1" }, "u1");
        await svc.CompleteWaveAsync(waveNo, "u1");
        var wave = await db.WavePlans.SingleAsync(w => w.WaveNo == waveNo);
        Assert.Equal(WavePlanStatus.Picked, wave.Status);
    }

    [Fact]
    public async Task Complete_HasPending_Throws050()
    {
        var svc = CreateSvc(out var db);
        var (waveNo, _) = await SeedReleasedWaveAsync(svc, db);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CompleteWaveAsync(waveNo, "u1"));
        Assert.Contains("WAVE-MSG-050", ex.Message);
    }
```

- [ ] **Step 2: 运行确认失败 → 实现**

```csharp
    public async Task CompleteWaveAsync(string waveNo, string? userName)
    {
        var wave = await _db.WavePlans.FirstOrDefaultAsync(w => w.WaveNo == waveNo && !w.IsDeleted)
            ?? throw new InvalidOperationException("WAVE-MSG-070: 波次不存在");
        if (wave.Status != WavePlanStatus.Released)
            throw new InvalidOperationException("WAVE-MSG-043: 仅拣货中波次可完成");
        var hasUnfinished = await _db.WavePickTasks.AnyAsync(t => t.WaveNo == waveNo && !t.IsDeleted
            && t.Status != WavePickTaskStatus.Picked && t.Status != WavePickTaskStatus.Short);
        if (hasUnfinished)
            throw new InvalidOperationException("WAVE-MSG-050: 尚有未完成的拣货任务");
        wave.Status = WavePlanStatus.Picked;
        wave.PickedAt = DateTime.Now;
        wave.Modifier = userName; wave.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }
```

- [ ] **Step 3: 运行确认通过 + Commit**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~WaveServiceTests.Complete"`
Expected: PASS。

```bash
git add CP6.Core/Services/Wms/WaveService.cs CP6.Tests/Wms/WaveServiceTests.cs
git commit -m "feat(wms-wave): T12 CompleteWave(仅Picked/Short可完成,否则050)"
```

---

### Task 13: `BatchShipWaveAsync`（逐单独立事务 + 三态清单）（TDD）

**Files:**
- Modify: `CP6.Core/Services/Wms/WaveService.cs`
- Test: `CP6.Tests/Wms/WaveBatchShipEdgeTests.cs`

- [ ] **Step 1: 失败测试**

```csharp
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CP6.Tests.Wms;

public class WaveBatchShipEdgeTests
{
    private static WaveService CreateSvc(out CP6.Core.EFDbContext.CP6Context db)
    {
        db = TestHelper.CreateInMemoryContext();
        db.Warehouses.Add(new Warehouse { WarehouseCd = "W01", WarehouseName = "M" });
        db.Locations.Add(new Location { LocationCd = "A-01", WarehouseCd = "W01" });
        db.SaveChanges();
        var seq = new WmsSequenceService(db);
        var stock = new StockMovementService(db, seq);
        return new WaveService(db, seq, stock, new OutboundService(db, seq, stock), new MaterialShortageService(db));
    }

    private static async Task SeedAsync(CP6.Core.EFDbContext.CP6Context db, string no, decimal alloc)
    {
        db.Stocks.Add(new Stock { WarehouseCd = "W01", LocationCd = "A-01", ProductCd = "P" + no,
            LotNo = "L1", PhysicalQty = alloc, AllocatedQty = alloc, AvailableQty = 0m });
        db.OutboundOrders.Add(new OutboundOrder { OutboundNo = no, OutboundType = 2, WarehouseCd = "W01", Status = 2, PlannedDate = new DateTime(2026,6,27) });
        db.OutboundOrderDetails.Add(new OutboundOrderDetail { OutboundNo = no, LineNo = 1, ProductCd = "P" + no,
            RequiredQty = alloc, AllocatedQty = alloc, LotNo = "L1", LocationCd = "A-01", WarehouseCd = "W01" });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task BatchShip_FullShortOrder_SkipsToPartialAllocated_NotStuckPicking()
    {
        var svc = CreateSvc(out var db);
        await SeedAsync(db, "OUT1", 10m);
        var waveNo = await svc.CreateWaveAsync(new CreateWaveRequest { OrderNos = new() { "OUT1" }, WarehouseCd = "W01" }, "u1");
        await svc.ReleaseWaveAsync(waveNo, "u1");
        var taskNo = (await db.WavePickTasks.SingleAsync(t => t.WaveNo == waveNo)).WavePickTaskNo;
        await svc.ConfirmPickAsync(taskNo, new ConfirmPickRequest
        { PickedQty = 0m, ScannedLocationCd = "A-01", ScannedProductCd = "POUT1", ScannedLotNo = "L1", ShortReason = "全缺" }, "u1");
        await svc.CompleteWaveAsync(waveNo, "u1");

        var result = await svc.BatchShipWaveAsync(waveNo, new BatchShipRequest(), "u1");

        Assert.Contains("OUT1", result.Skipped);
        Assert.Empty(result.Succeeded);
        var order = await db.OutboundOrders.SingleAsync(o => o.OutboundNo == "OUT1");
        Assert.Equal(OutboundOrderStatus.PartialAllocated, order.Status); // 不卡 Picking
        var wave = await db.WavePlans.SingleAsync(w => w.WaveNo == waveNo);
        Assert.Equal(WavePlanStatus.Completed, wave.Status);
    }

    [Fact]
    public async Task BatchShip_FullPick_Ships_AndCompletes()
    {
        var svc = CreateSvc(out var db);
        await SeedAsync(db, "OUT1", 10m);
        var waveNo = await svc.CreateWaveAsync(new CreateWaveRequest { OrderNos = new() { "OUT1" }, WarehouseCd = "W01" }, "u1");
        await svc.ReleaseWaveAsync(waveNo, "u1");
        var taskNo = (await db.WavePickTasks.SingleAsync(t => t.WaveNo == waveNo)).WavePickTaskNo;
        await svc.ConfirmPickAsync(taskNo, new ConfirmPickRequest
        { PickedQty = 10m, ScannedLocationCd = "A-01", ScannedProductCd = "POUT1", ScannedLotNo = "L1" }, "u1");
        await svc.CompleteWaveAsync(waveNo, "u1");

        var result = await svc.BatchShipWaveAsync(waveNo, new BatchShipRequest(), "u1");

        Assert.Contains("OUT1", result.Succeeded);
        var order = await db.OutboundOrders.SingleAsync(o => o.OutboundNo == "OUT1");
        Assert.Equal(OutboundOrderStatus.Completed, order.Status);
        var wave = await db.WavePlans.SingleAsync(w => w.WaveNo == waveNo);
        Assert.Equal(WavePlanStatus.Completed, wave.Status);
        Assert.False(await db.WaveOrders.AnyAsync(w => w.WaveNo == waveNo && w.IsActive)); // 释放唯一占用
    }
}
```

- [ ] **Step 2: 运行确认失败 → 实现 `BatchShipWaveAsync`**

```csharp
    public async Task<BatchShipResultDto> BatchShipWaveAsync(string waveNo, BatchShipRequest req, string? userName)
    {
        var wave = await _db.WavePlans.FirstOrDefaultAsync(w => w.WaveNo == waveNo && !w.IsDeleted)
            ?? throw new InvalidOperationException("WAVE-MSG-070: 波次不存在");
        if (wave.Status != WavePlanStatus.Picked)
            throw new InvalidOperationException("WAVE-MSG-043: 仅拣货完成波次可出荷");

        var result = new BatchShipResultDto();
        var memberNos = await _db.WaveOrders.Where(w => w.WaveNo == waveNo && w.IsActive && !w.IsDeleted)
            .Select(w => w.OutboundNo).ToListAsync();

        foreach (var no in memberNos)
        {
            var order = await _db.OutboundOrders.FirstOrDefaultAsync(o => o.OutboundNo == no && !o.IsDeleted);
            if (order == null) { result.Skipped.Add(no); continue; }
            var shippable = await _db.OutboundOrderDetails
                .Where(d => d.OutboundNo == no && !d.IsDeleted)
                .SumAsync(d => d.AllocatedQty - d.ShippedQty);

            if (order.Status == OutboundOrderStatus.Picking && shippable > 0m)
            {
                try
                {
                    await _outbound.ShipAsync(no, new ShipRequest
                    { CarrierCd = req.CarrierCd, TrackingNo = req.TrackingNo, Remarks = req.Remarks }, userName);
                    result.Succeeded.Add(no);
                }
                catch (Exception ex) { result.Failed.Add(new ShipFailureDto { OutboundNo = no, Reason = ex.Message }); }
            }
            else if (order.Status == OutboundOrderStatus.Picking && shippable <= 0m)
            {
                order.Status = OutboundOrderStatus.PartialAllocated; // 全短拣无可出量，明确收尾
                order.Modifier = userName; order.ModifyDate = DateTime.Now;
                await _db.SaveChangesAsync();
                result.Skipped.Add(no);
            }
            else { result.Skipped.Add(no); }
        }

        if (result.Failed.Count == 0)
        {
            wave.Status = WavePlanStatus.Completed;
            wave.CompletedAt = DateTime.Now;
            var wos = await _db.WaveOrders.Where(w => w.WaveNo == waveNo && w.IsActive && !w.IsDeleted).ToListAsync();
            foreach (var wo in wos) wo.IsActive = false;
        }
        wave.Modifier = userName; wave.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
        return result;
    }
```
> `ShipAsync` 内部自带事务；每单 try/catch 独立，失败不阻塞其余。`OutboundService` 已在事务里发 OUT + 接缝②，本方法不重复库存逻辑。

- [ ] **Step 3: 运行确认通过 + Commit**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~WaveBatchShipEdgeTests"`
Expected: PASS（含失败单留 Picked 的断言见 Task 13b）。

```bash
git add CP6.Core/Services/Wms/WaveService.cs CP6.Tests/Wms/WaveBatchShipEdgeTests.cs
git commit -m "feat(wms-wave): T13 BatchShip(逐单独立事务+三态清单+全短拣收尾PartialAllocated+复用ShipAsync接缝②)"
```

---

### Task 13b: BatchShip 失败保持 Picked（边界测试补充）

**Files:**
- Modify: `CP6.Tests/Wms/WaveBatchShipEdgeTests.cs`

- [ ] **Step 1: 写"一成功一失败→波次保持 Picked"测试**

构造法：seed 两单 OUT1/OUT2 均满拣完成；对 OUT2 制造 Ship 失败——把 OUT2 的库存 `PhysicalQty` 改为 0（OUT 扣库时 `InsufficientStockException`，仓 `AllowNegative=false`）。

```csharp
    [Fact]
    public async Task BatchShip_OneSuccessOneFail_WaveStaysPicked()
    {
        var svc = CreateSvc(out var db);
        await SeedAsync(db, "OUT1", 10m);
        await SeedAsync(db, "OUT2", 10m);
        var waveNo = await svc.CreateWaveAsync(new CreateWaveRequest { OrderNos = new() { "OUT1", "OUT2" }, WarehouseCd = "W01" }, "u1");
        await svc.ReleaseWaveAsync(waveNo, "u1");
        foreach (var t in await db.WavePickTasks.Where(x => x.WaveNo == waveNo).ToListAsync())
            await svc.ConfirmPickAsync(t.WavePickTaskNo, new ConfirmPickRequest
            { PickedQty = t.RequiredQty, ScannedLocationCd = "A-01", ScannedProductCd = t.ProductCd, ScannedLotNo = "L1" }, "u1");
        await svc.CompleteWaveAsync(waveNo, "u1");
        // 制造 OUT2 出库失败：物理库存清零（无法 OUT）
        var s2 = await db.Stocks.SingleAsync(s => s.ProductCd == "POUT2");
        s2.PhysicalQty = 0m; s2.AvailableQty = -10m; await db.SaveChangesAsync();

        var result = await svc.BatchShipWaveAsync(waveNo, new BatchShipRequest(), "u1");

        Assert.Single(result.Succeeded);
        Assert.Single(result.Failed);
        var wave = await db.WavePlans.SingleAsync(w => w.WaveNo == waveNo);
        Assert.Equal(WavePlanStatus.Picked, wave.Status); // 有失败，保持 Picked
    }
```
> 若 InMemory 下 `ShipAsync` 的 OUT 不抛 `InsufficientStockException`（取决于 `StockMovementService` 负库存守卫是否在 InMemory 生效），改用 mock：用一个抛异常的 `IOutboundService` 测试替身仅令 OUT2 抛错。实现替身见下 Step 1b。

- [ ] **Step 1b（仅当 Step 1 在 InMemory 不触发失败时）: 测试替身**

```csharp
    private sealed class FlakyOutbound : IOutboundService
    {
        private readonly IOutboundService _inner; private readonly string _failNo;
        public FlakyOutbound(IOutboundService inner, string failNo) { _inner = inner; _failNo = failNo; }
        public Task<string?> ShipAsync(string outboundNo, ShipRequest req, string? userName)
            => outboundNo == _failNo ? throw new InvalidOperationException("WM-MSG-040: 在庫不足")
                                      : _inner.ShipAsync(outboundNo, req, userName);
        // 其余成员委托 _inner（用 => _inner.Xxx(...) 实现 IOutboundService 全部方法）
    }
```
> 用 `new WaveService(db, seq, stock, new FlakyOutbound(realOutbound, "OUT2"), shortage)` 构造，使 OUT2 必失败。`FlakyOutbound` 须实现 `IOutboundService` 全部成员（其余委托 `_inner`）。

- [ ] **Step 2: 运行确认通过 + Commit**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~WaveBatchShipEdgeTests"`
Expected: PASS。

```bash
git add CP6.Tests/Wms/WaveBatchShipEdgeTests.cs
git commit -m "test(wms-wave): T13b BatchShip 一成功一失败→波次保持Picked"
```

---

### Task 14: `CancelWaveAsync`（边界守卫 + 恢复原状态）（TDD）

**Files:**
- Modify: `CP6.Core/Services/Wms/WaveService.cs`
- Test: `CP6.Tests/Wms/WaveCancelBoundaryTests.cs`

- [ ] **Step 1: 失败测试**

```csharp
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CP6.Tests.Wms;

public class WaveCancelBoundaryTests
{
    private static WaveService CreateSvc(out CP6.Core.EFDbContext.CP6Context db)
    {
        db = TestHelper.CreateInMemoryContext();
        db.Warehouses.Add(new Warehouse { WarehouseCd = "W01", WarehouseName = "M" });
        db.Locations.Add(new Location { LocationCd = "A-01", WarehouseCd = "W01" });
        db.Stocks.Add(new Stock { WarehouseCd = "W01", LocationCd = "A-01", ProductCd = "P001",
            LotNo = "L1", PhysicalQty = 10m, AllocatedQty = 10m, AvailableQty = 0m });
        db.SaveChanges();
        var seq = new WmsSequenceService(db);
        var stock = new StockMovementService(db, seq);
        return new WaveService(db, seq, stock, new OutboundService(db, seq, stock), new MaterialShortageService(db));
    }

    private static async Task<string> SeedReleasedAsync(WaveService svc, CP6.Core.EFDbContext.CP6Context db, int origStatus)
    {
        db.OutboundOrders.Add(new OutboundOrder { OutboundNo = "OUT1", OutboundType = 2, WarehouseCd = "W01", Status = origStatus, PlannedDate = new DateTime(2026,6,27) });
        db.OutboundOrderDetails.Add(new OutboundOrderDetail { OutboundNo = "OUT1", LineNo = 1, ProductCd = "P001",
            RequiredQty = 10m, AllocatedQty = 10m, LotNo = "L1", LocationCd = "A-01", WarehouseCd = "W01" });
        await db.SaveChangesAsync();
        var waveNo = await svc.CreateWaveAsync(new CreateWaveRequest { OrderNos = new() { "OUT1" }, WarehouseCd = "W01" }, "u1");
        await svc.ReleaseWaveAsync(waveNo, "u1");
        return waveNo;
    }

    [Fact]
    public async Task Cancel_NoFinishedTask_RestoresStatus2()
    {
        var svc = CreateSvc(out var db);
        var waveNo = await SeedReleasedAsync(svc, db, origStatus: 2);
        await svc.CancelWaveAsync(waveNo, "u1");
        var wave = await db.WavePlans.SingleAsync(w => w.WaveNo == waveNo);
        Assert.Equal(WavePlanStatus.Cancelled, wave.Status);
        var order = await db.OutboundOrders.SingleAsync(o => o.OutboundNo == "OUT1");
        Assert.Equal(OutboundOrderStatus.Allocated, order.Status); // 恢复 2
        Assert.False(await db.WaveOrders.AnyAsync(w => w.WaveNo == waveNo && w.IsActive));
    }

    [Fact]
    public async Task Cancel_OrigPartialAllocated_Restores5()
    {
        var svc = CreateSvc(out var db);
        var waveNo = await SeedReleasedAsync(svc, db, origStatus: 5);
        await svc.CancelWaveAsync(waveNo, "u1");
        var order = await db.OutboundOrders.SingleAsync(o => o.OutboundNo == "OUT1");
        Assert.Equal(OutboundOrderStatus.PartialAllocated, order.Status); // 恢复 5
    }

    [Fact]
    public async Task Cancel_HasPickedTask_Throws044()
    {
        var svc = CreateSvc(out var db);
        var waveNo = await SeedReleasedAsync(svc, db, origStatus: 2);
        var taskNo = (await db.WavePickTasks.SingleAsync(t => t.WaveNo == waveNo)).WavePickTaskNo;
        await svc.ConfirmPickAsync(taskNo, new ConfirmPickRequest
        { PickedQty = 10m, ScannedLocationCd = "A-01", ScannedProductCd = "P001", ScannedLotNo = "L1" }, "u1");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CancelWaveAsync(waveNo, "u1"));
        Assert.Contains("WAVE-MSG-044", ex.Message);
    }

    [Fact]
    public async Task Cancel_HasShortTask_Throws044()
    {
        var svc = CreateSvc(out var db);
        var waveNo = await SeedReleasedAsync(svc, db, origStatus: 2);
        var taskNo = (await db.WavePickTasks.SingleAsync(t => t.WaveNo == waveNo)).WavePickTaskNo;
        await svc.ConfirmPickAsync(taskNo, new ConfirmPickRequest
        { PickedQty = 6m, ScannedLocationCd = "A-01", ScannedProductCd = "P001", ScannedLotNo = "L1", ShortReason = "破损" }, "u1");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CancelWaveAsync(waveNo, "u1"));
        Assert.Contains("WAVE-MSG-044", ex.Message);
    }
}
```

- [ ] **Step 2: 运行确认失败 → 实现 `CancelWaveAsync`**

```csharp
    public async Task CancelWaveAsync(string waveNo, string? userName)
    {
        var wave = await _db.WavePlans.FirstOrDefaultAsync(w => w.WaveNo == waveNo && !w.IsDeleted)
            ?? throw new InvalidOperationException("WAVE-MSG-070: 波次不存在");
        if (wave.Status != WavePlanStatus.Draft && wave.Status != WavePlanStatus.Released)
            throw new InvalidOperationException("WAVE-MSG-043: 当前状态不可取消");

        if (wave.Status == WavePlanStatus.Released)
        {
            var hasFinished = await _db.WavePickTasks.AnyAsync(t => t.WaveNo == waveNo && !t.IsDeleted
                && (t.Status == WavePickTaskStatus.Picked || t.Status == WavePickTaskStatus.Short));
            if (hasFinished)
                throw new InvalidOperationException("WAVE-MSG-044: 波次已有已收尾任务，不可取消");

            var tasks = await _db.WavePickTasks.Where(t => t.WaveNo == waveNo && !t.IsDeleted).ToListAsync();
            foreach (var t in tasks) { t.Status = WavePickTaskStatus.Cancelled; t.Modifier = userName; t.ModifyDate = DateTime.Now; }

            var wos = await _db.WaveOrders.Where(w => w.WaveNo == waveNo && w.IsActive && !w.IsDeleted).ToListAsync();
            foreach (var wo in wos)
            {
                var order = await _db.OutboundOrders.FirstOrDefaultAsync(o => o.OutboundNo == wo.OutboundNo && !o.IsDeleted);
                if (order != null) { order.Status = wo.OriginalOutboundStatus; order.Modifier = userName; order.ModifyDate = DateTime.Now; }
            }
        }

        var allWos = await _db.WaveOrders.Where(w => w.WaveNo == waveNo && w.IsActive && !w.IsDeleted).ToListAsync();
        foreach (var wo in allWos) wo.IsActive = false;
        wave.Status = WavePlanStatus.Cancelled;
        wave.Modifier = userName; wave.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }
```

- [ ] **Step 3: 运行确认通过 + Commit**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~WaveCancelBoundaryTests"`
Expected: 4 PASS。

```bash
git add CP6.Core/Services/Wms/WaveService.cs CP6.Tests/Wms/WaveCancelBoundaryTests.cs
git commit -m "feat(wms-wave): T14 CancelWave(边界044+恢复OriginalOutboundStatus 2/5+释放IsActive)"
```

---

### Task 15: `WaveConcurrencyTests` + E2E `WaveFullFlowTests`

**Files:**
- Create: `CP6.Tests/Wms/WaveConcurrencyTests.cs`
- Create: `CP6.Tests/Wms/WaveFullFlowTests.cs`

- [ ] **Step 1: 并发测试（InMemory 可行版）**

> InMemory 不强制过滤唯一索引/RowVersion；这里测应用层守卫（031 预检 + 051 已收尾），并以注释声明 DB 过滤唯一索引为真正强约束（由迁移保证、生产/集成验证）。

```csharp
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CP6.Tests.Wms;

public class WaveConcurrencyTests
{
    // 复用 WaveBatchShipEdgeTests 的 seed 风格
    [Fact]
    public async Task CreateWave_SameOrderTwice_SecondThrows031()
    {
        var db = TestHelper.CreateInMemoryContext();
        db.Warehouses.Add(new Warehouse { WarehouseCd = "W01", WarehouseName = "M" });
        db.OutboundOrders.Add(new OutboundOrder { OutboundNo = "OUT1", OutboundType = 2, WarehouseCd = "W01", Status = 2, PlannedDate = new DateTime(2026,6,27) });
        db.OutboundOrderDetails.Add(new OutboundOrderDetail { OutboundNo = "OUT1", LineNo = 1, ProductCd = "P001", RequiredQty = 10m, AllocatedQty = 10m, LotNo = "L1", LocationCd = "A-01", WarehouseCd = "W01" });
        await db.SaveChangesAsync();
        var seq = new WmsSequenceService(db); var stock = new StockMovementService(db, seq);
        var svc = new WaveService(db, seq, stock, new OutboundService(db, seq, stock), new MaterialShortageService(db));

        await svc.CreateWaveAsync(new CreateWaveRequest { OrderNos = new() { "OUT1" }, WarehouseCd = "W01" }, "u1");
        // 注：DB 过滤唯一索引 UX_WaveOrder_ActiveOutbound 是真正强约束；此处验证应用层 031 守卫
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.CreateWaveAsync(new CreateWaveRequest { OrderNos = new() { "OUT1" }, WarehouseCd = "W01" }, "u1"));
        Assert.Contains("WAVE-MSG-031", ex.Message);
    }

    [Fact]
    public async Task ConfirmPick_DoubleSubmit_SecondThrows051()
    {
        var db = TestHelper.CreateInMemoryContext();
        db.Warehouses.Add(new Warehouse { WarehouseCd = "W01", WarehouseName = "M" });
        db.Locations.Add(new Location { LocationCd = "A-01", WarehouseCd = "W01" });
        db.Stocks.Add(new Stock { WarehouseCd = "W01", LocationCd = "A-01", ProductCd = "P001", LotNo = "L1", PhysicalQty = 10m, AllocatedQty = 10m, AvailableQty = 0m });
        db.OutboundOrders.Add(new OutboundOrder { OutboundNo = "OUT1", OutboundType = 2, WarehouseCd = "W01", Status = 2, PlannedDate = new DateTime(2026,6,27) });
        db.OutboundOrderDetails.Add(new OutboundOrderDetail { OutboundNo = "OUT1", LineNo = 1, ProductCd = "P001", RequiredQty = 10m, AllocatedQty = 10m, LotNo = "L1", LocationCd = "A-01", WarehouseCd = "W01" });
        await db.SaveChangesAsync();
        var seq = new WmsSequenceService(db); var stock = new StockMovementService(db, seq);
        var svc = new WaveService(db, seq, stock, new OutboundService(db, seq, stock), new MaterialShortageService(db));
        var waveNo = await svc.CreateWaveAsync(new CreateWaveRequest { OrderNos = new() { "OUT1" }, WarehouseCd = "W01" }, "u1");
        await svc.ReleaseWaveAsync(waveNo, "u1");
        var taskNo = (await db.WavePickTasks.SingleAsync(t => t.WaveNo == waveNo)).WavePickTaskNo;
        var req = new ConfirmPickRequest { PickedQty = 10m, ScannedLocationCd = "A-01", ScannedProductCd = "P001", ScannedLotNo = "L1" };
        await svc.ConfirmPickAsync(taskNo, req, "u1");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.ConfirmPickAsync(taskNo, req, "u1"));
        Assert.Contains("WAVE-MSG-051", ex.Message);
    }
}
```

- [ ] **Step 2: E2E 全链 + 接缝②回写测试**

> 全链需要受注(Order)以验证接缝②。参照现有 `WmsErpClosedLoopTests.cs` 的 seed（建 Order + OrderDetail + WebOrderNo 关联），再走波次链，断言 `OrderDetail.ShippedQty` 回写。

```csharp
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CP6.Tests.Wms;

public class WaveFullFlowTests
{
    [Fact]
    public async Task FullFlow_Allocated_Release_Pick_Complete_Ship_DeductsStock()
    {
        var db = TestHelper.CreateInMemoryContext();
        db.Warehouses.Add(new Warehouse { WarehouseCd = "W01", WarehouseName = "M" });
        db.Locations.Add(new Location { LocationCd = "A-01", WarehouseCd = "W01" });
        db.Stocks.Add(new Stock { WarehouseCd = "W01", LocationCd = "A-01", ProductCd = "P001", LotNo = "L1", PhysicalQty = 10m, AllocatedQty = 10m, AvailableQty = 0m });
        db.OutboundOrders.Add(new OutboundOrder { OutboundNo = "OUT1", OutboundType = 2, WarehouseCd = "W01", Status = 2, PlannedDate = new DateTime(2026,6,27) });
        db.OutboundOrderDetails.Add(new OutboundOrderDetail { OutboundNo = "OUT1", LineNo = 1, ProductCd = "P001", RequiredQty = 10m, AllocatedQty = 10m, LotNo = "L1", LocationCd = "A-01", WarehouseCd = "W01" });
        await db.SaveChangesAsync();
        var seq = new WmsSequenceService(db); var stock = new StockMovementService(db, seq);
        var svc = new WaveService(db, seq, stock, new OutboundService(db, seq, stock), new MaterialShortageService(db));

        var waveNo = await svc.CreateWaveAsync(new CreateWaveRequest { OrderNos = new() { "OUT1" }, WarehouseCd = "W01" }, "u1");
        await svc.ReleaseWaveAsync(waveNo, "u1");
        var taskNo = (await db.WavePickTasks.SingleAsync(t => t.WaveNo == waveNo)).WavePickTaskNo;
        await svc.ConfirmPickAsync(taskNo, new ConfirmPickRequest { PickedQty = 10m, ScannedLocationCd = "A-01", ScannedProductCd = "P001", ScannedLotNo = "L1" }, "u1");
        await svc.CompleteWaveAsync(waveNo, "u1");
        var result = await svc.BatchShipWaveAsync(waveNo, new BatchShipRequest(), "u1");

        Assert.Contains("OUT1", result.Succeeded);
        var s = await db.Stocks.SingleAsync(x => x.ProductCd == "P001");
        Assert.Equal(0m, s.PhysicalQty);  // OUT 扣 10
        Assert.Equal(0m, s.AllocatedQty);
        var d = await db.OutboundOrderDetails.SingleAsync(x => x.OutboundNo == "OUT1");
        Assert.Equal(10m, d.ShippedQty);
    }
}
```
> 接缝② ERP 回写断言：若 E2E 接入 Order/OrderDetail（参照 `WmsErpClosedLoopTests`），追加 `Assert.Equal(10m, orderDetail.ShippedQty)`。若 `OutboundService` ctor 还需 `IErpBridgeHook` 等依赖，按 `OutboundServiceTests` 现有构造方式注入（可传 null / 默认）。

- [ ] **Step 3: 运行全部 Wave 测试 + 回归现有测试**

Run: `dotnet test CP6.Tests`
Expected: 全部 PASS（新增 Wave 测试 + 现有 631+ 不变照绿）。

- [ ] **Step 4: Commit**

```bash
git add CP6.Tests/Wms/WaveConcurrencyTests.cs CP6.Tests/Wms/WaveFullFlowTests.cs
git commit -m "test(wms-wave): T15 WaveConcurrency(031/051)+WaveFullFlow E2E(扣库+接缝②)"
```

---

# Phase W5 — 控制器 + i18n + DI

### Task 16: `WaveController`

**Files:**
- Create: `CP6.WebApi/Controllers/Wms/WaveController.cs`

- [ ] **Step 1: 创建控制器**

```csharp
using CP6.Core.Services.Wms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Controllers.Wms;

[ApiController]
[Route("api/wms/wave")]
[Authorize]
public class WaveController : ControllerBase
{
    private readonly IWaveService _svc;
    public WaveController(IWaveService svc) => _svc = svc;
    private string? CurrentUser => User?.Identity?.Name;

    [HttpPost("available-orders")]
    public async Task<IActionResult> Available([FromBody] WaveOrderFilterDto filter)
        => Ok(new { code = 0, message = "OK", data = await _svc.SearchAvailableOrdersAsync(filter ?? new()) });

    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] WaveSearchFilterDto filter)
        => Ok(new { code = 0, message = "OK", data = await _svc.SearchWavesAsync(filter ?? new()) });

    [HttpGet("{waveNo}")]
    public async Task<IActionResult> Get(string waveNo)
    {
        var dto = await _svc.GetWaveAsync(waveNo);
        if (dto == null) return NotFound(new { code = 404, message = "WAVE-MSG-070" });
        return Ok(new { code = 0, message = "OK", data = dto });
    }

    [HttpGet("{waveNo}/tasks")]
    public async Task<IActionResult> Tasks(string waveNo, [FromQuery] string? assignedTo)
        => Ok(new { code = 0, message = "OK", data = await _svc.GetWaveTasksAsync(waveNo, assignedTo) });

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateWaveRequest req)
    {
        try { return Ok(new { code = 0, message = "WAVE-MSG-071", data = new { waveNo = await _svc.CreateWaveAsync(req, CurrentUser) } }); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("WAVE-MSG-031"))
        { return Conflict(new { code = 409, message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpPost("{waveNo}/release")]
    public Task<IActionResult> Release(string waveNo) => Guard(() => _svc.ReleaseWaveAsync(waveNo, CurrentUser));

    [HttpPost("tasks/{taskNo}/pick")]
    public Task<IActionResult> Pick(string taskNo, [FromBody] ConfirmPickRequest req) => Guard(() => _svc.ConfirmPickAsync(taskNo, req, CurrentUser));

    [HttpPost("{waveNo}/complete")]
    public Task<IActionResult> Complete(string waveNo) => Guard(() => _svc.CompleteWaveAsync(waveNo, CurrentUser));

    [HttpPost("{waveNo}/ship")]
    public async Task<IActionResult> Ship(string waveNo, [FromBody] BatchShipRequest req)
    {
        try { return Ok(new { code = 0, message = "WAVE-MSG-071", data = await _svc.BatchShipWaveAsync(waveNo, req ?? new(), CurrentUser) }); }
        catch (DbUpdateConcurrencyException) { return Conflict(new { code = 409, message = "WAVE-MSG-072" }); }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpPost("{waveNo}/cancel")]
    public Task<IActionResult> Cancel(string waveNo) => Guard(() => _svc.CancelWaveAsync(waveNo, CurrentUser));

    private async Task<IActionResult> Guard(Func<Task> action)
    {
        try { await action(); return Ok(new { code = 0, message = "WAVE-MSG-071" }); }
        catch (DbUpdateConcurrencyException) { return Conflict(new { code = 409, message = "WAVE-MSG-072" }); }
        catch (CP6.Core.Services.Wms.InsufficientStockException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("WAVE-MSG-070")) { return NotFound(new { code = 404, message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }
}
```
> `InsufficientStockException` 的命名空间按其实际定义调整（取证显示在 `CP6.Core/Services/Wms/`）。

- [ ] **Step 2: 编译验证**

Run: `dotnet build CP6.WebApi`
Expected: 失败（`IWaveService` 未在 DI 注册不影响编译；若 `InsufficientStockException` 命名空间不符则修正）。编译应通过。

- [ ] **Step 3: Commit**

```bash
git add CP6.WebApi/Controllers/Wms/WaveController.cs
git commit -m "feat(wms-wave): T16 WaveController(9端点+031/072→409+070→404)"
```

---

### Task 17: DI 注册 + i18n 词条 Seeder

**Files:**
- Modify: `CP6.WebApi/Program.cs`
- Create: `CP6.WebApi/Seed/I18nWaveScreenSeed.cs`

- [ ] **Step 1: DI 注册**（在现有 WMS 服务注册区块，如 `IStockTakeService` 注册附近）

```csharp
builder.Services.AddScoped<IWaveService, WaveService>();
```

- [ ] **Step 2: 创建 i18n Seeder（照 `I18nSecScreenSeed.cs` 格式）**

```csharp
using CP6.Entity.DomainModels.Sys; // Sys_Lang 命名空间按 I18nSecScreenSeed.cs 实际 using

namespace CP6.WebApi.Seed;

public static class I18nWaveScreenSeed
{
    public static readonly Sys_Lang[] Items = new[]
    {
        new Sys_Lang { LangKey = "WAVE-MSG-020", ZhCN = "波次成员出库指示不能为空", ZhTW = "波次成員出庫指示不能為空", En = "Wave must contain at least one outbound order", Ja = "波の出庫指示が空です", Ko = "파에 출고 지시가 비어 있습니다" },
        new Sys_Lang { LangKey = "WAVE-MSG-021", ZhCN = "拣货数量非法", ZhTW = "揀貨數量非法", En = "Invalid pick quantity", Ja = "ピッキング数量が不正です", Ko = "피킹 수량이 잘못되었습니다" },
        new Sys_Lang { LangKey = "WAVE-MSG-030", ZhCN = "出库指示状态不允许入波", ZhTW = "出庫指示狀態不允許入波", En = "Outbound status not eligible for wave", Ja = "出庫指示の状態が波に投入できません", Ko = "출고 지시 상태가 파에 투입할 수 없습니다" },
        new Sys_Lang { LangKey = "WAVE-MSG-031", ZhCN = "出库指示已属于其它活动波次", ZhTW = "出庫指示已屬於其它活動波次", En = "Outbound already in another active wave", Ja = "出庫指示は既に他の有効な波に属しています", Ko = "출고 지시가 이미 다른 활성 파에 속해 있습니다" },
        new Sys_Lang { LangKey = "WAVE-MSG-043", ZhCN = "当前状态不允许此操作", ZhTW = "當前狀態不允許此操作", En = "Operation not allowed in current status", Ja = "現在の状態ではこの操作は許可されていません", Ko = "현재 상태에서는 이 작업이 허용되지 않습니다" },
        new Sys_Lang { LangKey = "WAVE-MSG-044", ZhCN = "波次已有已收尾任务，不可取消", ZhTW = "波次已有已收尾任務，不可取消", En = "Wave has finished tasks; cannot cancel", Ja = "完了済タスクがあるため取消できません", Ko = "완료된 작업이 있어 취소할 수 없습니다" },
        new Sys_Lang { LangKey = "WAVE-MSG-050", ZhCN = "尚有未完成的拣货任务", ZhTW = "尚有未完成的揀貨任務", En = "Unfinished pick tasks remain", Ja = "未完了のピッキングタスクがあります", Ko = "미완료 피킹 작업이 남아 있습니다" },
        new Sys_Lang { LangKey = "WAVE-MSG-051", ZhCN = "该拣货任务已收尾", ZhTW = "該揀貨任務已收尾", En = "Pick task already finished", Ja = "ピッキングタスクは既に完了しています", Ko = "피킹 작업이 이미 완료되었습니다" },
        new Sys_Lang { LangKey = "WAVE-MSG-052", ZhCN = "实拣量不可超过应拣量", ZhTW = "實揀量不可超過應揀量", En = "Picked qty cannot exceed required", Ja = "実績数量が必要数量を超えています", Ko = "피킹 수량이 필요 수량을 초과합니다" },
        new Sys_Lang { LangKey = "WAVE-MSG-053", ZhCN = "短拣必须填写原因", ZhTW = "短揀必須填寫原因", En = "Short pick requires a reason", Ja = "短欠ピッキングには理由が必要です", Ko = "부족 피킹은 사유가 필요합니다" },
        new Sys_Lang { LangKey = "WAVE-MSG-060", ZhCN = "扫描库位与任务不符", ZhTW = "掃描庫位與任務不符", En = "Scanned location mismatch", Ja = "スキャンしたロケーションが一致しません", Ko = "스캔한 로케이션이 일치하지 않습니다" },
        new Sys_Lang { LangKey = "WAVE-MSG-061", ZhCN = "扫描製品与任务不符", ZhTW = "掃描製品與任務不符", En = "Scanned product mismatch", Ja = "スキャンした製品が一致しません", Ko = "스캔한 제품이 일치하지 않습니다" },
        new Sys_Lang { LangKey = "WAVE-MSG-062", ZhCN = "扫描批次与任务不符", ZhTW = "掃描批次與任務不符", En = "Scanned lot mismatch", Ja = "スキャンしたロットが一致しません", Ko = "스캔한 로트가 일치하지 않습니다" },
        new Sys_Lang { LangKey = "WAVE-MSG-070", ZhCN = "数据不存在", ZhTW = "資料不存在", En = "Not found", Ja = "データが存在しません", Ko = "데이터가 존재하지 않습니다" },
        new Sys_Lang { LangKey = "WAVE-MSG-071", ZhCN = "操作成功", ZhTW = "操作成功", En = "Success", Ja = "操作が成功しました", Ko = "작업이 성공했습니다" },
        new Sys_Lang { LangKey = "WAVE-MSG-072", ZhCN = "数据已被他人修改，请刷新重试", ZhTW = "資料已被他人修改，請刷新重試", En = "Data modified by another user; refresh and retry", Ja = "データが他のユーザーにより変更されました。更新して再試行してください", Ko = "데이터가 다른 사용자에 의해 변경되었습니다. 새로고침 후 재시도하세요" },

        new Sys_Lang { LangKey = "wms.wave.title", ZhCN = "波次管理", ZhTW = "波次管理", En = "Wave Management", Ja = "波管理", Ko = "파 관리" },
        new Sys_Lang { LangKey = "wms.wave.list", ZhCN = "波次列表", ZhTW = "波次列表", En = "Wave List", Ja = "波一覧", Ko = "파 목록" },
        new Sys_Lang { LangKey = "wms.wave.build", ZhCN = "组波", ZhTW = "組波", En = "Build Wave", Ja = "波作成", Ko = "파 생성" },
        new Sys_Lang { LangKey = "wms.wave.pick", ZhCN = "波次拣货", ZhTW = "波次揀貨", En = "Wave Picking", Ja = "波ピッキング", Ko = "파 피킹" },
        new Sys_Lang { LangKey = "wms.wave.status.draft", ZhCN = "草稿", ZhTW = "草稿", En = "Draft", Ja = "下書き", Ko = "초안" },
        new Sys_Lang { LangKey = "wms.wave.status.released", ZhCN = "拣货中", ZhTW = "揀貨中", En = "Released", Ja = "ピッキング中", Ko = "피킹 중" },
        new Sys_Lang { LangKey = "wms.wave.status.picked", ZhCN = "待出荷", ZhTW = "待出荷", En = "Picked", Ja = "出荷待ち", Ko = "출하 대기" },
        new Sys_Lang { LangKey = "wms.wave.status.completed", ZhCN = "已完成", ZhTW = "已完成", En = "Completed", Ja = "完了", Ko = "완료" },
        new Sys_Lang { LangKey = "wms.wave.status.cancelled", ZhCN = "已取消", ZhTW = "已取消", En = "Cancelled", Ja = "取消", Ko = "취소됨" },
        new Sys_Lang { LangKey = "wms.wave.btn.release", ZhCN = "下发", ZhTW = "下發", En = "Release", Ja = "下発", Ko = "릴리스" },
        new Sys_Lang { LangKey = "wms.wave.btn.complete", ZhCN = "完成拣货", ZhTW = "完成揀貨", En = "Complete", Ja = "ピッキング完了", Ko = "피킹 완료" },
        new Sys_Lang { LangKey = "wms.wave.btn.ship", ZhCN = "批量出荷", ZhTW = "批量出荷", En = "Batch Ship", Ja = "一括出荷", Ko = "일괄 출하" },
        new Sys_Lang { LangKey = "wms.wave.btn.cancel", ZhCN = "取消波次", ZhTW = "取消波次", En = "Cancel Wave", Ja = "波取消", Ko = "파 취소" },
        new Sys_Lang { LangKey = "wms.wave.btn.shortReason", ZhCN = "短拣原因", ZhTW = "短揀原因", En = "Short Reason", Ja = "短欠理由", Ko = "부족 사유" },
        new Sys_Lang { LangKey = "wms.outbound.status.partialAllocated", ZhCN = "部分引当", ZhTW = "部分引當", En = "Partial Allocated", Ja = "部分引当", Ko = "부분 할당" },
    };
}
```

- [ ] **Step 3: 注册 Seeder**

grep 现有 seeder 注册点：
Run: `git grep -n "I18nSecScreenSeed" -- CP6.WebApi`
按它被消费/注册的同一方式把 `I18nWaveScreenSeed.Items` 接上（同一 seed 汇聚处追加）。**照抄现有写法，勿臆造方法名。**

- [ ] **Step 4: 编译 + 全测回归**

Run: `dotnet build CP6.slnx && dotnet test CP6.Tests`
Expected: 编译通过；全部测试 PASS。

- [ ] **Step 5: Commit**

```bash
git add CP6.WebApi/Program.cs CP6.WebApi/Seed/I18nWaveScreenSeed.cs
git commit -m "feat(wms-wave): T17 DI注册IWaveService + WAVE-MSG/wms.wave.* 五语词条入Sys_Langs(纠裸码)+partialAllocated"
```

---

# Phase W6 — 前端 + 菜单 + QA

### Task 18: 前端 types + api

**Files:**
- Create: `cp6.web/src/types/wms/wave.ts`
- Create: `cp6.web/src/api/wms/wave.ts`

- [ ] **Step 1: types**

```typescript
export interface WaveAvailableOrder {
  outboundNo: string; outboundType: number; status: number; warehouseCd: string
  webOrderNo?: string; carrierCd?: string; priority: number; plannedDate: string
  lineCount: number; allocatedTotal: number
}
export interface WavePlan {
  waveNo: string; warehouseCd: string; status: number; orderCount: number
  taskCount: number; pickedTaskCount: number; assignedTo?: string; priority: number; createDate: string
}
export interface WavePickTask {
  wavePickTaskNo: string; waveNo: string; sourceOutboundNo: string; sourceLineNo: number
  productCd: string; productName?: string; lotNo?: string; warehouseCd: string
  fromLocationCd?: string; pickSeq: number; requiredQty: number; pickedQty: number
  shortQty: number; status: number
}
export interface WavePlanDetail extends WavePlan { members: WaveAvailableOrder[]; tasks: WavePickTask[] }
export interface ConfirmPickRequest {
  pickedQty: number; scannedLocationCd: string; scannedProductCd: string
  scannedLotNo?: string; shortReason?: string
}
export interface BatchShipResult { succeeded: string[]; failed: { outboundNo: string; reason: string }[]; skipped: string[] }
```

- [ ] **Step 2: api**

```typescript
import http from '../http'
import type { WmsApi } from '@/types/wms/wms'
import type { WaveAvailableOrder, WavePlan, WavePlanDetail, WavePickTask, ConfirmPickRequest, BatchShipResult } from '@/types/wms/wave'

export const waveApi = {
  available(filter: Record<string, any> = {}) {
    return http.post<any, WmsApi<WaveAvailableOrder[]>>('/wms/wave/available-orders', filter)
  },
  search(filter: Record<string, any> = {}) {
    return http.get<any, WmsApi<WavePlan[]>>('/wms/wave', { params: filter })
  },
  get(no: string) {
    return http.get<any, WmsApi<WavePlanDetail>>(`/wms/wave/${encodeURIComponent(no)}`)
  },
  tasks(no: string, assignedTo?: string) {
    return http.get<any, WmsApi<WavePickTask[]>>(`/wms/wave/${encodeURIComponent(no)}/tasks`, { params: { assignedTo } })
  },
  create(req: { orderNos: string[]; warehouseCd: string; assignedTo?: string; priority?: number }) {
    return http.post<any, WmsApi<{ waveNo: string }>>('/wms/wave', req)
  },
  release(no: string) { return http.post<any, WmsApi<void>>(`/wms/wave/${encodeURIComponent(no)}/release`) },
  pick(taskNo: string, req: ConfirmPickRequest) {
    return http.post<any, WmsApi<void>>(`/wms/wave/tasks/${encodeURIComponent(taskNo)}/pick`, req)
  },
  complete(no: string) { return http.post<any, WmsApi<void>>(`/wms/wave/${encodeURIComponent(no)}/complete`) },
  ship(no: string, req: { carrierCd?: string; trackingNo?: string; remarks?: string } = {}) {
    return http.post<any, WmsApi<BatchShipResult>>(`/wms/wave/${encodeURIComponent(no)}/ship`, req)
  },
  cancel(no: string) { return http.post<any, WmsApi<void>>(`/wms/wave/${encodeURIComponent(no)}/cancel`) },
}
```

- [ ] **Step 3: 前端类型检查 + Commit**

Run: `cd cp6.web && npm run type-check`（或现有等价脚本）
Expected: 通过。

```bash
git add cp6.web/src/types/wms/wave.ts cp6.web/src/api/wms/wave.ts
git commit -m "feat(wms-wave): T18 前端 types + api(9端点)"
```

---

### Task 19: 三视图 + 路由

**Files:**
- Create: `cp6.web/src/views/wms/WaveListView.vue`
- Create: `cp6.web/src/views/wms/WaveBuildView.vue`
- Create: `cp6.web/src/views/wms/WavePickView.vue`
- Modify: `cp6.web/src/router/index.ts`

- [ ] **Step 1: `WaveListView.vue`**（列表 + 状态机按钮，照 `OutboundOrderListView.vue` 结构）

实现要点（用 `<script setup lang="ts">` + Element Plus + `useI18n`）：
- `statusMap = computed(() => ({0:t('wms.wave.status.draft'),1:t('wms.wave.status.released'),2:t('wms.wave.status.picked'),3:t('wms.wave.status.completed'),9:t('wms.wave.status.cancelled')}))`
- 列：waveNo / warehouseCd / status(el-tag) / orderCount / `pickedTaskCount/taskCount` 进度 / createDate / 操作。
- 操作按钮按 status：Draft→「下发」(waveApi.release)、Released→「进入拣货」(router push `/wms/wave-pick?waveNo=`)+「完成拣货」(waveApi.complete)、Picked→「批量出荷」(waveApi.ship 后弹成功/失败/跳过清单)、Draft|Released→「取消」(waveApi.cancel)。
- 每个操作 `ElMessageBox.confirm` → api → `ElMessage.success(t('...'))` → reload。
- 顶部「+ 组波」跳 `/wms/wave-build`。

- [ ] **Step 2: `WaveBuildView.vue`**（组波）

要点：
- 筛选表单（warehouseCd 必填 / outboundType / 出荷日范围 / carrierCd / priority）→「查询」调 `waveApi.available(filter)` 填可选表。
- `<el-table>` 多选（`@selection-change`）列出可入波出库指示（outboundNo/type/status/lineCount/allocatedTotal/plannedDate）。
- 底部「创建波次」：取勾选 outboundNo[] + warehouseCd → `waveApi.create({orderNos, warehouseCd})` → 成功跳 `/wms/wave-list`。

- [ ] **Step 3: `WavePickView.vue`**（拣货执行）

要点：
- 入口取 `route.query.waveNo`；`waveApi.tasks(waveNo)` 按 pickSeq 升序渲染任务卡片流。
- 顶部进度（已收尾/总）。
- 每个任务：显示 pickSeq/库位/製品/Lot/应拣；扫描输入框（库位/製品/Lot）+实拣量输入。
- 「确认」→ `waveApi.pick(taskNo, {pickedQty, scannedLocationCd, scannedProductCd, scannedLotNo, shortReason})`。
- 当实拣<应拣：弹「短拣原因」必填输入（`ElMessageBox.prompt`），缺失则前端拦下；后端 053 兜底。
- 前端可即时比对扫描值给红色提示（体验优化），但**以后端返回为准**（060/061/062 报错弹 ElMessage.error(res.message)）。
- 全部任务收尾后提示并可跳回列表「完成拣货」。

- [ ] **Step 4: 路由**（`router/index.ts` WMS 区块追加）

```typescript
'/wms/wave-list': () => import('@/views/wms/WaveListView.vue'),
'/wms/wave-build': () => import('@/views/wms/WaveBuildView.vue'),
'/wms/wave-pick': () => import('@/views/wms/WavePickView.vue'),
```

- [ ] **Step 5: 类型检查 + 构建 + Commit**

Run: `cd cp6.web && npm run type-check && npm run build`
Expected: 通过。

```bash
git add cp6.web/src/views/wms/WaveListView.vue cp6.web/src/views/wms/WaveBuildView.vue cp6.web/src/views/wms/WavePickView.vue cp6.web/src/router/index.ts
git commit -m "feat(wms-wave): T19 三视图(列表/组波/拣货执行)+路由"
```

---

### Task 20: OutboundOrder statusMap 补 PartialAllocated=5

**Files:**
- Modify: `cp6.web/src/views/wms/OutboundOrderListView.vue`
- Modify: `cp6.web/src/views/wms/OutboundOrderView.vue`

- [ ] **Step 1: 两文件 statusMap 各加一行**

在 `OutboundOrderListView.vue` 的 `statusMap` computed（取证定位约 :106-113）`4:` 行后加：
```typescript
  5: t('wms.outbound.status.partialAllocated'),
```
对 `OutboundOrderView.vue` 内同样的 statusMap 做同样补充。

- [ ] **Step 2: 类型检查 + Commit**

Run: `cd cp6.web && npm run type-check`
Expected: 通过。

```bash
git add cp6.web/src/views/wms/OutboundOrderListView.vue cp6.web/src/views/wms/OutboundOrderView.vue
git commit -m "fix(wms): T20 OutboundOrder statusMap 补 PartialAllocated=5(消除裸显数字)"
```

---

### Task 21: 菜单种子

**Files:**
- Create: `docs/seeds/wms-wave-menu-seed.sql`

- [ ] **Step 1: 写菜单 MERGE/IF NOT EXISTS 脚本**（照 `docs/seeds/wms-menu-seed.sql` 结构，挂在 WMS 父菜单 400 下）

```sql
SET NOCOUNT ON;
IF NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 418)
    INSERT INTO Sys_Menus (MenuId, MenuName, RoutePath, Icon, ParentId, OrderNo, Enable, CreateDate)
    VALUES (418, N'波次拣货', NULL, N'Connection', 400, 418, 1, SYSDATETIME());
IF NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 4181)
    INSERT INTO Sys_Menus (MenuId, MenuName, RoutePath, Icon, ParentId, OrderNo, Enable, CreateDate)
    VALUES (4181, N'波次列表', N'/wms/wave-list', N'List', 418, 4181, 1, SYSDATETIME());
IF NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 4182)
    INSERT INTO Sys_Menus (MenuId, MenuName, RoutePath, Icon, ParentId, OrderNo, Enable, CreateDate)
    VALUES (4182, N'组波', N'/wms/wave-build', N'DocumentAdd', 418, 4182, 1, SYSDATETIME());
IF NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 4183)
    INSERT INTO Sys_Menus (MenuId, MenuName, RoutePath, Icon, ParentId, OrderNo, Enable, CreateDate)
    VALUES (4183, N'波次拣货作业', N'/wms/wave-pick', N'Pointer', 418, 4183, 1, SYSDATETIME());
```
> 列名/MenuId 段位以现有 `wms-menu-seed.sql` 实际为准（若该文件用不同列序，照它）。

- [ ] **Step 2: Commit**

```bash
git add docs/seeds/wms-wave-menu-seed.sql
git commit -m "feat(wms-wave): T21 波次菜单种子(挂WMS父菜单)"
```

---

### Task 22: gstack 浏览器 QA + 收尾

**Files:**
- Create: `docs/superpowers/qa/wave-picking/`（QA 记录）

- [ ] **Step 1: 起后端 + 前端，跑 i18n seeder（重启后端使词条生效）**

- [ ] **Step 2: 用 gstack 浏览器走全流程 QA**

场景：登录 → 组波（筛选+勾选已引当单+创建）→ 列表下发 → 拣货执行（满拣一单 + 短拣一单含原因 + 故意扫错库位看 060 报错）→ 完成拣货 → 批量出荷（看成功/失败/跳过清单）。验证：①状态文案五语无裸码；②OutboundOrder 列表 status=5 显示「部分引当」非数字；③短拣后该出库行按实拣量出。

- [ ] **Step 3: 固化 QA 记录**

把 QA 步骤/截图/结论写到 `docs/superpowers/qa/wave-picking/README.md`。

- [ ] **Step 4: 全量回归**

Run: `dotnet test CP6.Tests`（全绿，现有 631+ 不变）；`cd cp6.web && npm run type-check && npm run build`。

- [ ] **Step 5: Commit**

```bash
git add docs/superpowers/qa/wave-picking/
git commit -m "test(wms-wave): T22 gstack 浏览器全流程 QA 固化"
```

- [ ] **Step 6: 拆除 worktree（全部完成后）**

```bash
cd /d/CP6 && git worktree remove /c/Users/tt/AppData/Local/Temp/cp6-wms-wt
```

---

## Self-Review（计划自检）

**Spec 覆盖核对**（spec §→Task）：
- §3 状态机 → T1 枚举；咬合 → T8/T12/T13/T14。
- §4.1/4.2/4.3 三表 + IsActive/OriginalOutboundStatus/ShortReason → T2；§4.2 过滤唯一索引 → T3/T4；§4.4 Location.PickSeq → T2/T4；§4.5 采番 → T6/T8（NextAsync 前缀 WAVE/WPT）。
- §5 服务 API → T5（接口）+ T6~T14（实现）。
- §6.1 Release+动线序 → T6(纯函数)+T8；§6.2 ConfirmPick(扫描校验/0拣/短拣必填) → T10/T11；§6.3 BatchShip 三态 → T13/T13b；§6.4 CancelWave 边界+恢复 → T14。
- §7 控制器 → T16；§8 错误码+i18n → T17；§9 前端三视图+statusMap → T18/T19/T20；§10 迁移 → T4；§11 测试六套 → T6/T7/T8/T10(WaveServiceTests)+T11(Scan)+T13/T13b(BatchShipEdge)+T14(CancelBoundary)+T15(Concurrency/FullFlow)；§12 阶段 → W1~W6；§9.2 菜单 → T21；§11.7 QA → T22。
- 落码前优先级 8 点：①T3/T4/T7 ②T10 ③T10/T11 ④T10 ⑤T14 ⑥T14 ⑦T13/T13b ⑧T13。**全覆盖**。

**占位符扫描**：无 TBD/TODO。两处"按实际调整"是真实发现指令（`OutboundService` ctor 依赖 / seeder 注册点 / `InsufficientStockException` 命名空间）——均给了确切 grep/参照文件，非空泛占位。

**类型一致性**：`ConfirmPickRequest`(PickedQty/ScannedLocationCd/ScannedProductCd/ScannedLotNo/ShortReason)、`BatchShipResultDto`(Succeeded/Failed/Skipped)、`WavePlanStatus`/`WavePickTaskStatus` 常量、`NextAsync("WAVE"/"WPT")`、`WaveOrder.IsActive/OriginalOutboundStatus`、`WavePickTask.ShortReason/ShortageNo` 在各 Task 间一致。T10 Step3 误留的本地 `Rank` 函数已在 Step3 注释要求删除；ShortageNo 回填在 Step3b 修正为 `shortage.Id.ToString()`。

**已知实现风险（执行者注意）**：
1. `OutboundService` 构造依赖可能多于 `(db, seq, stock)`（如 IErpBridgeHook/IWmsNotifier/IRouting 可选）。测试构造时按 `OutboundServiceTests.cs` 现有写法注入（多为可选 null）。**T7 Step1 的 `new OutboundService(...)` 须对齐真实 ctor。**
2. InMemory 对 OUT 负库存守卫/RowVersion 行为：T13b 提供测试替身 `FlakyOutbound` 作为兜底制造失败。
3. i18n seeder 注册点须 grep `I18nSecScreenSeed` 照搬，勿臆造。

---

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-06-27-wms-wave-picking.md`. Two execution options:

1. **Subagent-Driven (recommended)** — 每个 Task 派新 subagent，task 间两段 review，快速迭代。**须在隔离 worktree（feat/wms-wave-picking）内执行，禁止动主工作区分支。**
2. **Inline Execution** — 本会话内按 executing-plans 批量执行 + 检查点。

Which approach?
