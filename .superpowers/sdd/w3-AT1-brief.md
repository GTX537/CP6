### Task A-T1: 常量 + 两实体 + DbSet/索引 + 一次迁移 `WfsFlowTrigger`

**Files:**
- Modify: `CP6.Core/Services/Wf/WfStatus.cs`（追加 WfTriggerType）
- Create: `CP6.Entity/DomainModels/Wf/Wf_FlowTrigger.cs`
- Create: `CP6.Entity/DomainModels/Wf/Wf_TriggerFire.cs`
- Modify: `CP6.Core/EFDbContext/CP6Context.cs`（DbSet + 索引）
- Create: 迁移 `CP6.Core/Migrations/<ts>_WfsFlowTrigger.cs`（`dotnet ef` 生成）
- Test: `CP6.Tests/Wf/FlowTriggerModelTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
// CP6.Tests/Wf/FlowTriggerModelTests.cs
using System;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Xunit;

public class FlowTriggerModelTests
{
    [Fact]
    public void WfTriggerType_Constants()
    {
        Assert.Equal(0, WfTriggerType.Timer);
        Assert.Equal(1, WfTriggerType.Event);
        Assert.Equal(2, WfTriggerType.Message);
    }

    [Fact]
    public void Wf_FlowTrigger_Defaults()
    {
        var t = new Wf_FlowTrigger { FlowKey = "fk-demo", TriggerType = WfTriggerType.Timer, StarterUserId = Guid.NewGuid() };
        Assert.Equal("{}", t.ConfigJson);
        Assert.False(t.Enabled);
        Assert.Null(t.EventKey);
        Assert.Null(t.NextDueUtc);
        Assert.Null(t.LastFiredUtc);
        Assert.Null(t.ApiKeyHash);
    }

    [Fact]
    public void Wf_TriggerFire_Defaults()
    {
        var f = new Wf_TriggerFire { TriggerId = Guid.NewGuid(), IdempotencyKey = "k1", FiredUtc = DateTime.UtcNow, Source = WfTriggerType.Event };
        Assert.Null(f.InstanceId);
        Assert.Null(f.Error);
        Assert.Null(f.PayloadHash);
    }
}
```

- [ ] **Step 2: 跑测试验证 FAIL** — `dotnet test CP6.Tests/CP6.Tests.csproj --filter FlowTriggerModelTests`，预期编译失败（类型不存在）。

- [ ] **Step 3: 实现**

`WfStatus.cs` 追加：

```csharp
/// <summary>流程触发器类型（事件触发 start 增量，spec §2.1）。</summary>
public static class WfTriggerType
{
    public const int Timer = 0;
    public const int Event = 1;
    public const int Message = 2;
}
```

`Wf_FlowTrigger.cs`（spec §2.1 逐字 + 映射表④的 MaxLength——索引键列不能 nvarchar(max)）：

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wf;

/// <summary>流程触发器（timer/event/message 三型，spec §2.1）。配置挂流程级（D5），不进设计器 schema。</summary>
[Table("Wf_FlowTrigger")]
public class Wf_FlowTrigger : BaseTenantEntity
{
    /// <summary>目标流程（对齐 SubmitAsync 口径）</summary>
    [MaxLength(200)] public string FlowKey { get; set; } = "";

    /// <summary>WfTriggerType: Timer=0 / Event=1 / Message=2</summary>
    public int TriggerType { get; set; }

    /// <summary>分型配置（spec §2.3）：timer={cron,varsJson} / event={varsMap} / message={varsSchema}</summary>
    [Column(TypeName = "nvarchar(max)")]
    public string ConfigJson { get; set; } = "{}";

    public bool Enabled { get; set; }

    /// <summary>event 专用（提列可索引；格式 "{SourceModule}|{HookName}"；ConfigJson 不再重复存）</summary>
    [MaxLength(200)] public string? EventKey { get; set; }

    /// <summary>名义发起人（D6，必填）——审计与 starter.* 审批人解析都依赖它</summary>
    public Guid StarterUserId { get; set; }

    /// <summary>timer 专用：下次到期（扫描键，UTC）</summary>
    public DateTime? NextDueUtc { get; set; }

    public DateTime? LastFiredUtc { get; set; }

    /// <summary>message 专用：SHA-256 hex（明文只在创建/重置响应显示一次）</summary>
    [MaxLength(64)] public string? ApiKeyHash { get; set; }

    /// <summary>乐观并发（多实例 worker 抢占）</summary>
    [Timestamp] public byte[]? RowVersion { get; set; }
}
```

`Wf_TriggerFire.cs`（spec §2.2 逐字 + MaxLength）：

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wf;

/// <summary>触发流水 = 审计台账 + 幂等闸（D7）。占坑行：InstanceId==null && Error==null。</summary>
[Table("Wf_TriggerFire")]
public class Wf_TriggerFire : BaseTenantEntity
{
    public Guid TriggerId { get; set; }

    /// <summary>复合唯一索引（TenantId,TriggerId,IdempotencyKey）＝幂等闸权威判据；键非空必填，无需 filtered（D7）</summary>
    [MaxLength(200)] public string IdempotencyKey { get; set; } = "";

    public DateTime FiredUtc { get; set; }

    /// <summary>成功发起的流程实例；null=占坑未完成或失败</summary>
    public Guid? InstanceId { get; set; }

    /// <summary>同 WfTriggerType（冗余便查）</summary>
    public int Source { get; set; }

    /// <summary>发起失败原因（结构化码+detail）</summary>
    [MaxLength(1000)] public string? Error { get; set; }

    /// <summary>message/event 负载 SHA-256（审计，不存原文）</summary>
    [MaxLength(64)] public string? PayloadHash { get; set; }
}
```

`CP6Context.cs`：DbSet 两行（放 `Wf_ServiceJobs` 声明同块）：

```csharp
/// <summary>流程触发器配置（事件触发 start 增量，spec §2.1）</summary>
public DbSet<Wf_FlowTrigger> Wf_FlowTriggers { get; set; }
/// <summary>触发流水（审计+幂等闸，spec §2.2）</summary>
public DbSet<Wf_TriggerFire> Wf_TriggerFires { get; set; }
```

`OnModelCreating`（放 `Wf_ServiceJob` 索引块之后，索引照 spec §2.1/§2.2 原文三＋一）：

```csharp
modelBuilder.Entity<Wf_FlowTrigger>(b =>
{
    b.HasIndex(x => new { x.TenantId, x.FlowKey }).HasDatabaseName("IX_Wf_FlowTrigger_Flow");
    // 扫描索引（spec §2.1 原文列序，不含 TenantId——worker 逐租户 scope 下全局过滤补 TenantId 条件）
    b.HasIndex(x => new { x.Enabled, x.TriggerType, x.NextDueUtc }).HasDatabaseName("IX_Wf_FlowTrigger_Scan");
    b.HasIndex(x => new { x.TenantId, x.EventKey }).HasDatabaseName("IX_Wf_FlowTrigger_Event");
});
modelBuilder.Entity<Wf_TriggerFire>(b =>
{
    // 键非空必填 → 无需 filtered（ServiceJob 先例 filtered 是因其键可空，此处不是——D7 原文）
    b.HasIndex(x => new { x.TenantId, x.TriggerId, x.IdempotencyKey })
        .IsUnique().HasDatabaseName("UX_Wf_TriggerFire_Idem");
});
```

- [ ] **Step 4: 跑测试验证 PASS** — `dotnet test ... --filter FlowTriggerModelTests`。

- [ ] **Step 5: 生成迁移**（本波**唯一**一次）：

```bash
dotnet ef migrations add WfsFlowTrigger --project CP6.Core/CP6.Core.csproj --startup-project CP6.WebApi/CP6.WebApi.csproj --context CP6Context
dotnet ef migrations has-pending-model-changes --project CP6.Core/CP6.Core.csproj --startup-project CP6.WebApi/CP6.WebApi.csproj --context CP6Context   # 应 clean
```

检查 Up() 仅建 `Wf_FlowTrigger` + `Wf_TriggerFire` 两表 + 4 索引（`UX_Wf_TriggerFire_Idem` 带 `unique: true` 无 filter），零其他表改动、零回填。**不手写迁移文件**（快照会失同步）。

- [ ] **Step 6: Wf 闸 + commit**

```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf   # 既有照绿
git add -A && git commit -m "feat(wfs-trigger): A-T1 数据模型 Wf_FlowTrigger/Wf_TriggerFire 两表+WfTriggerType+一次迁移 WfsFlowTrigger"
```

---


---
## 附: 共享契约(plan全局, 精确名字)
## 共享契约（所有 Task 用这些**精确**名字，前后一致）

- `WfTriggerType`：`Timer=0 / Event=1 / Message=2`（int 常量，`WfStatus.cs`）。
- 实体字段：`Wf_FlowTrigger { FlowKey, TriggerType, ConfigJson, Enabled, EventKey, StarterUserId, NextDueUtc, LastFiredUtc, ApiKeyHash, RowVersion }`；`Wf_TriggerFire { TriggerId, IdempotencyKey, FiredUtc, InstanceId, Source, Error, PayloadHash }`（均继承 BaseTenantEntity）。
- `TriggerFireResult { bool Success; bool Replayed; Guid? InstanceId; string? Error; static Ok(Guid, bool replayed=false); static Fail(string); }`
- `IFlowTriggerService`（spec §3.1 逐字）：
  - `Task<TriggerFireResult> FireAsync(Wf_FlowTrigger trigger, string? varsJson, int source, string idempotencyKey, CancellationToken ct);`
  - `Task<int> ScanTimersOnceAsync(CancellationToken ct);`（实现类测试重载 `ScanTimersOnceAsync(DateTime nowUtc, CancellationToken ct)`）
- 幂等键口径（spec §2.2）：timer=`$"{trigger.Id}:{dueUtc:O}"`；event=`$"{eventId}:{trigger.Id}"`；message=`Idempotency-Key` 头；手动试发=`$"manual:{Guid.NewGuid():N}"`。
- `WfCronHelper { static bool IsValid(string?); static DateTime? NextUtc(string cron, DateTime afterUtc); static IReadOnlyList<DateTime> PreviewUtc(string cron, DateTime fromUtc, int count); }`
- `IWfTriggerBridgeHook`：
  - `Task<WfTriggerBridgeResult> OnEventAsync(string eventKey, string eventId, string payloadJson, string? userName);`（业务入口，写 outbox 台账）
  - `Task<WfTriggerBridgeResult> ReplayEventAsync(string eventKey, string eventId, string payloadJson, string? userName);`（dispatcher 重放入口，不再写新 outbox 行）
- `WfTriggerBridgeResult { bool Success; int MatchedCount; int FiredCount; string? Message; static Ok(int matched, int fired); static Skipped(string); static Failed(string); }`
- `WfTriggerEventPayload(string EventKey, string EventId, string PayloadJson, string? UserName)`（record，outbox 负载契约）。
- `WfTriggerVarsMapper { static string MapVars(Dictionary<string,string>? varsMap, string payloadJson); static string FilterBySchema(string bodyJson, IReadOnlyList<string>? schema); }`
- `WfApiKeyHelper { static string NewRawKey(); static string HashOf(string raw); static bool Verify(string raw, string? storedHash); }`
- `WfTriggerConfig`：`ParseTimer(string)→WfTimerTriggerConfig{Cron,VarsJson}` / `ParseEvent(string)→WfEventTriggerConfig{VarsMap}` / `ParseMessage(string)→WfMessageTriggerConfig{VarsSchema}`。
- 常量（`FlowTriggerService`）：`RecoveryGrace = TimeSpan.FromMinutes(2)`（补跑宽限）、`BatchSize = 50`、`Trunc` 截 1000。
- 错误码：`E-WF-022`（配置无效：cron/eventKey/varsMap/StarterUserId）/ `E-WF-023`（目标流程不可发起）/ `E-WF-024`（运行时发起失败，写 TriggerFire.Error）。message 端点 401/404/400 走 HTTP 语义不占 E-WF 码。
- FireAsync 撞键语义（spec §3.1 引申，全计划统一）：既有行 `InstanceId != null` → `Ok(instanceId, replayed:true)`（幂等成功非错误）；既有行 `InstanceId == null`（占坑未完成**或**上次失败）→ 补跑第二段（成功回填并清 Error / 失败覆写 Error）。timer 补跑扫描只捡 `Error==null` 的占坑行（spec §3.2 原文）；Error 行的重试机会来自 event outbox 重放与 message 客户端重试。


## 附: 现状锚点(实体/迁移范本行)
| 实体/迁移范本 | `Wf_ServiceJob.cs`：`[Table("Wf_ServiceJob")] : BaseTenantEntity`，字符串用 `[MaxLength(n)]`（无长度=nvarchar(max)），`[Timestamp] byte[]? RowVersion`。`BaseTenantEntity` 提供 `Id/Creator/CreateDate/Modifier/ModifyDate/TenantId`。索引在 `CP6Context.OnModelCreating`（`:733-744` 范本）。迁移命令：`dotnet ef migrations add <名> --project CP6.Core/CP6.Core.csproj --startup-project CP6.WebApi/CP6.WebApi.csproj --context CP6Context`；**不手写迁移文件**。 |
