using System.Net;
using System.Text.Json;
using CP6.Entity.DomainModels;
using CP6.Entity.DTOs.Space;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private async Task<Harness> LocationDeactivateSeed(bool existingBin = true)
    {
        var h = await HierarchySeed();
        var db = h.Db;
        var menu = await db.Sys_Menus.SingleOrDefaultAsync(x => x.MenuKey == "space-publish");
        if (menu is null)
        {
            menu = new() { MenuId = 905, MenuKey = "space-publish", MenuName = "SIM Publish", RoutePath = "/space/publish" };
            db.Sys_Menus.Add(menu);
            await db.SaveChangesAsync();
        }
        if (!await db.Sys_MenuActions.AnyAsync(x => x.MenuId == menu.MenuId && x.ActionCode == "deactivate"))
            db.Sys_MenuActions.Add(new() { MenuId = menu.MenuId, ActionCode = "deactivate", ActionName = "Deactivate" });
        db.Sys_RoleActions.Add(new() { RoleId = 72, MenuId = menu.MenuId, ActionCode = "deactivate" });
        var location = await db.Space_Locations.SingleAsync(x => x.LocationCode == "FROZEN");
        location.Version = 3;
        if (existingBin)
            db.WmsBins.Add(new() { Id = location.Id, LocationCode = "FROZEN", WarehouseCd = "SIM", Version = 3, IsActive = true });
        db.Stocks.AddRange(
            new() { WarehouseCd = "SIM", LocationCd = "FROZEN", ProductCd = "SIM-ITEM", LotNo = "LOCAL", PhysicalQty = 0m },
            new() { WarehouseCd = "W1", LocationCd = "FROZEN", ProductCd = "SIM-ITEM", LotNo = "OTHER", PhysicalQty = 2m, AvailableQty = 2m });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return h;
    }

    private static string DeactivatePath(Guid location) => $"api/space/location/{location}/deactivate";
    private sealed record Deactivated(int Status, string? LocationCode, long LocationVersion, long BinVersion,
        bool BinActive, string WarehouseCd, string EventStatus, string Batch, int SequenceBefore, int SequenceAfter);

    private static async Task<Deactivated> DeactivationReceipt(Harness h, Guid location, int seq)
    {
        var row = await h.Db.Space_Locations.AsNoTracking().SingleAsync(x => x.Id == location);
        var bin = await h.Db.WmsBins.AsNoTracking().SingleAsync(x => x.Id == location);
        var evt = await h.Db.IntegrationEvents.AsNoTracking().SingleAsync(x => x.SourceModule == "SPACE" && x.TargetModule == "WMS");
        var after = await SceneDeltaSequence(h);
        Assert.Equal(2, row.Status);
        Assert.Equal(4, row.Version);
        Assert.Equal("FROZEN", row.LocationCode);
        Assert.False(bin.IsActive);
        Assert.Equal(row.Version, bin.Version);
        Assert.Equal(row.LocationCode, bin.LocationCode);
        Assert.Equal("SIM", bin.WarehouseCd);
        // The synchronous participant already saved this version. Replay must be an idempotent skip.
        Assert.Equal(IntegrationEventStatus.Skipped, evt.Status);
        var payload = JsonSerializer.Deserialize<LocationPublishBatch>(evt.PayloadJson)!;
        var item = Assert.Single(payload.Items);
        Assert.Equal("DEACTIVATE", item.Op);
        Assert.Equal(location, item.LocationId);
        Assert.Equal(row.Version, item.Version);
        Assert.Equal(h.Tenant, evt.TenantId);
        Assert.Equal(seq + 1, after);
        return new(row.Status, row.LocationCode, row.Version, bin.Version, bin.IsActive, bin.WarehouseCd, evt.Status, evt.SourceNo, seq, after);
    }

    [CurrentPermissionPostgresFact]
    public async Task LegacyDeactivate_waits_for_current_same_warehouse_stock()
    {
        await using var h = await LocationDeactivateSeed();
        var connection = h.Db.Database.GetConnectionString()!;
        await using var host = await AuthenticationApiProcess.Start(connection);
        var actor = await Login(host, h);
        var location = await SceneIngressLocation(h, "FROZEN");
        var seq = await SceneDeltaSequence(h);
        var before = await SceneDeltaRows(h);
        await using var control = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await control.OpenAsync();
        await observer.OpenAsync();
        Task<HttpResponseMessage> pending;
        FenceWait? wait;
        await using (var tx = await control.BeginTransactionAsync())
        {
            await using var receive = new NpgsqlCommand("""
                UPDATE public."T_Stock" SET "PhysicalQty"=3.125, "AvailableQty"=3.125
                WHERE "TenantId"=@tenant AND "WarehouseCd"='SIM' AND "LocationCd"='FROZEN'
                """, control, tx);
            receive.Parameters.AddWithValue("tenant", h.Tenant);
            Assert.Equal(1, await receive.ExecuteNonQueryAsync());
            pending = host.Send(OperationsRequest(actor, HttpMethod.Put, DeactivatePath(location)));
            wait = await ObservePublishWait(observer, control.ProcessID, pending);
            await tx.CommitAsync();
        }
        using var response = await pending;
        var body = await response.Content.ReadAsStringAsync();
        var stock = await h.Db.Stocks.AsNoTracking().Where(x => x.WarehouseCd == "SIM" && x.LocationCd == "FROZEN").Select(x => x.PhysicalQty).SingleAsync();
        var after = await SceneDeltaRows(h);
        var afterSeq = await SceneDeltaSequence(h);
        var bin = await h.Db.WmsBins.AsNoTracking().SingleAsync(x => x.Id == location);
        await DeviceEvidence("legacy-deactivate-stock.json", new { Status = (int)response.StatusCode, Wait = wait, Quantity = stock,
            Gate = body.Contains("E-SPACE-401", StringComparison.Ordinal), Before = before, After = after,
            SequenceBefore = seq, SequenceAfter = afterSeq, BinActive = bin.IsActive });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("E-SPACE-401", body);
        Assert.NotNull(wait);
        Assert.Equal(3.125m, stock);
        Assert.True(bin.IsActive);
        Assert.Equal(before, after);
        Assert.Equal(seq, afterSeq);
    }

    [TenantManagementPostgresTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LegacyDeactivate_updates_real_projection_or_creates_its_tombstone(bool existingBin)
    {
        await using var h = await LocationDeactivateSeed(existingBin);
        await using var other = await LocationDeactivateSeed();
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var actor = await Login(host, h);
        using var hub = await PublishHub.Connect(host);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var location = await SceneIngressLocation(h, "FROZEN");
        var seq = await SceneDeltaSequence(h);
        var stock = await PalletStockRows(h);
        var otherBefore = await SceneDeltaRows(other);
        var notification = hub.ReadNotice(deadline.Token);
        using var result = await host.Send(OperationsRequest(actor, HttpMethod.Put, DeactivatePath(location)));
        Assert.True(result.StatusCode == HttpStatusCode.OK, await OperationsFailure(result));
        var receipt = await DeactivationReceipt(h, location, seq);
        var notice = await notification;
        AssertPublishNotice(notice, receipt.Batch, 1);
        var after = await SceneDeltaRows(h);
        using var repeated = await host.Send(OperationsRequest(actor, HttpMethod.Put, DeactivatePath(location)));
        Assert.Equal(HttpStatusCode.BadRequest, repeated.StatusCode);
        Assert.Contains("E-SPACE-004", await repeated.Content.ReadAsStringAsync());
        using var noNotice = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => hub.ReadNotice(noNotice.Token));
        var stockAfter = await PalletStockRows(h);
        var otherAfter = await SceneDeltaRows(other);
        Assert.Equal(otherBefore, otherAfter);
        Assert.Equal(stock, stockAfter);
        Assert.Equal(after, await SceneDeltaRows(h));
        Assert.Equal(seq + 1, await SceneDeltaSequence(h));
        await DeviceEvidence("legacy-deactivate-" + (existingBin ? "existing" : "tombstone") + ".json", new
        {
            Status = (int)result.StatusCode, ExistingBin = existingBin, Receipt = receipt, Notice = notice,
            Repeated = (int)repeated.StatusCode, NoRepeatedNotice = true, StockBefore = stock, StockAfter = stockAfter,
            OtherBefore = otherBefore, OtherAfter = otherAfter
        });
    }
}
