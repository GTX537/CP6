### Task 1: 对账漂移扫描 Worker(Space.Status=1 ∧ T_WmsBin.IsActive=false)

**Files:**
- Create: `CP6.Core\Services\Space\SpaceBinDriftScanner.cs`
- Create: `CP6.WebApi\BackgroundServices\SpaceBinReconciliationWorker.cs`
- Modify: `CP6.WebApi\Program.cs`(照 `:503` FinReconciliationWorker 注册处,紧邻加一行)
- Test: `CP6.Tests\Space\SpaceBinDriftScannerTests.cs`

**Interfaces:**
- Produces: `SpaceBinDriftScanner.ScanAsync(CP6Context db, CancellationToken ct)` → `Task<List<SpaceBinDrift>>`;`record SpaceBinDrift(Guid LocationId, string? LocationCode, long BinVersion)`。
- Worker 纯壳:照 `CP6.WebApi\BackgroundServices\FinReconciliationWorker.cs`(启动延迟 1min + 每 24h,`TenantScopeRunner.ForEachTenantAsync`,只读,漂移逐条 `LogError`,`ProcessOnceAsync` 公开可测)。

**要点:** 两表以**主键等值 join**(`WmsBin.Id == Space_Location.Id`,跨系统同一 GUID)。漂移=已发布库位(Status=1, IsDeleted=0)对应 bin 存在且 IsActive=false。**只读不自愈**(对账 job 语义,与 FinReconciliationWorker 一致)。

- [ ] **Step 1: 写失败测试**(InMemory context;三例:①Status=1+bin.IsActive=false→命中 ②Status=1+bin.IsActive=true→不命中 ③Status=2+bin.IsActive=false→不命中):

```csharp
[Fact]
public async Task Scan_PublishedLocationWithInactiveBin_Reported()
{
    using var db = TestDb.Create(); // 照仓内既有 InMemory 先例
    var id = Guid.NewGuid();
    db.Space_Locations.Add(new Space_Location { Id = id, Status = 1, LocationCode = "A-01-01" });
    db.WmsBins.Add(new WmsBin { Id = id, LocationCode = "A-01-01", WarehouseCd = "W1", IsActive = false });
    await db.SaveChangesAsync();

    var drifts = await SpaceBinDriftScanner.ScanAsync(db, default);

    Assert.Single(drifts);
    Assert.Equal(id, drifts[0].LocationId);
}
```

- [ ] **Step 2: 跑测试确认红**(`dotnet test --filter SpaceBinDriftScannerTests`)
- [ ] **Step 3: 最小实现**:

```csharp
public static class SpaceBinDriftScanner
{
    public record SpaceBinDrift(Guid LocationId, string? LocationCode, long BinVersion);

    public static async Task<List<SpaceBinDrift>> ScanAsync(CP6Context db, CancellationToken ct)
        => await db.Space_Locations
            .Where(l => l.Status == 1 && !l.IsDeleted)
            .Join(db.WmsBins.Where(b => !b.IsActive),
                  l => l.Id, b => b.Id,
                  (l, b) => new SpaceBinDrift(l.Id, l.LocationCode, b.Version))
            .ToListAsync(ct);
}
```

Worker(照 FinReconciliationWorker 全文逐字同构,把勾稽逻辑换成调 `ScanAsync` 后 `foreach (var d in drifts) _logger.LogError("[SpaceBinDrift] 已发布库位 {LocationId}({Code}) 对应 WMS bin 处于停用态(version={V})——发布/停用链路漂移,需人工核查", …)`),并在 Program.cs FinReconciliationWorker 注册行旁 `builder.Services.AddHostedService<SpaceBinReconciliationWorker>();`。

- [ ] **Step 4: 跑测试确认绿 + 全量后端绿**
- [ ] **Step 5: Commit + push**(`feat(space): 波5 对账漂移扫描worker(Status=1∧bin停用,只读告警)`)

---

