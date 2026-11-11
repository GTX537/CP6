using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using CP6.Entity.DomainModels;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private async Task<Harness> LocationPublishSeed(bool otherReady = true)
    {
        var h = await HierarchySeed();
        var db = h.Db;
        var menu = await db.Sys_Menus.SingleOrDefaultAsync(m => m.MenuKey == "space-publish");
        if (menu is null)
        {
            menu = new() { MenuId = 905, MenuKey = "space-publish", MenuName = "SIM Publish", RoutePath = "/space/publish" };
            db.Sys_Menus.Add(menu);
            await db.SaveChangesAsync();
        }
        if (!await db.Sys_MenuActions.AnyAsync(a => a.MenuId == menu.MenuId && a.ActionCode == "publish"))
            db.Sys_MenuActions.Add(new() { MenuId = menu.MenuId, ActionCode = "publish", ActionName = "Publish" });
        db.Sys_RoleActions.Add(new() { RoleId = 72, MenuId = menu.MenuId, ActionCode = "publish" });
        var zone = await db.Space_Zones.AsNoTracking().SingleAsync(x => x.ZoneCode == "OTHER");
        var rack = Guid.NewGuid();
        db.Space_Racks.Add(new() { Id = rack, FloorId = zone.FloorId, ZoneId = zone.Id,
            RackCode = "R-OTHER", Cols = 1, Levels = 1, DepthCount = 1, CellW = 1000, CellH = 1000, CellD = 800 });
        var other = await db.Space_Locations.SingleAsync(x => x.Col == 2);
        other.RackId = rack;
        other.Col = 1;
        other.LocationCode = otherReady ? "OTHER-1" : null;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return h;
    }

    private static Task<Guid> PublishZone(Harness h) => h.Db.Space_Zones.Where(x => x.ZoneCode == "A").Select(x => x.Id).SingleAsync();
    private static string PublishPath(Guid floor) => $"api/space/floor/{floor}/publish";
    private static async Task<int> PublishedCount(HttpResponseMessage response)
        => (await SpaceCodeHttpData<JsonElement>(response)).GetProperty("published").GetInt32();

    private static async Task<FenceWait?> ObservePublishWait(NpgsqlConnection observer, int blocker, Task<HttpResponseMessage> request)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await using var command = new NpgsqlCommand("""
            SELECT pid,usename,wait_event,clock_timestamp() FROM pg_stat_activity
            WHERE datname=current_database() AND wait_event_type='Lock' AND @blocker=ANY(pg_blocking_pids(pid))
            """, observer);
        command.Parameters.AddWithValue("blocker", blocker);
        while (!request.IsCompleted)
        {
            await using (var reader = await command.ExecuteReaderAsync(deadline.Token))
                if (await reader.ReadAsync(deadline.Token))
                    return new(reader.GetInt32(0), blocker, reader.GetString(1), reader.GetString(2), reader.GetFieldValue<DateTimeOffset>(3));
            await Task.Delay(35, deadline.Token);
        }
        return null;
    }

    [CurrentPermissionPostgresFact]
    public async Task LegacyPublish_current_rule_precedes_the_release_decision()
    {
        await using var h = await LocationPublishSeed();
        var connection = h.Db.Database.GetConnectionString()!;
        await using var host = await AuthenticationApiProcess.Start(connection);
        var actor = await Login(host, h);
        var floor = await SceneDeltaFloor(h);
        var before = await SceneDeltaRows(h);
        var seq = await SceneDeltaSequence(h);
        var stock = await PalletStockRows(h);
        await using var control = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await control.OpenAsync();
        await observer.OpenAsync();
        Task<HttpResponseMessage> pending;
        FenceWait? wait;
        await using (var tx = await control.BeginTransactionAsync())
        {
            await using var change = new NpgsqlCommand("""
                UPDATE public."Space_CodeRule" SET "Segments"='[]'
                WHERE "TenantId"=@tenant AND "ScopeId"=@floor AND "ScopeType"=1
                """, control, tx);
            change.Parameters.AddWithValue("tenant", h.Tenant);
            change.Parameters.AddWithValue("floor", floor);
            Assert.Equal(1, await change.ExecuteNonQueryAsync());
            pending = host.Send(OperationsRequest(actor, HttpMethod.Post, PublishPath(floor), new { }));
            wait = await ObservePublishWait(observer, control.ProcessID, pending);
            await tx.CommitAsync();
        }
        using var result = await pending;
        var body = await result.Content.ReadAsStringAsync();
        var after = await SceneDeltaRows(h);
        var afterSeq = await SceneDeltaSequence(h);
        var stockAfter = await PalletStockRows(h);
        await DeviceEvidence("legacy-publish-rule.json", new { Status = (int)result.StatusCode, Wait = wait,
            Gate = body.Contains("E-SPACE-307", StringComparison.Ordinal), Before = before, After = after,
            SequenceBefore = seq, SequenceAfter = afterSeq, StockBefore = stock, StockAfter = stockAfter });
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Contains("E-SPACE-307", body);
        Assert.NotNull(wait);
        Assert.Equal(before, after);
        Assert.Equal(seq, afterSeq);
        Assert.Equal(stock, stockAfter);
    }

    [CurrentPermissionPostgresFact]
    public async Task LegacyPublish_signed_publish_waits_for_the_current_authority()
    {
        await using var h = await LocationPublishSeed();
        var connection = h.Db.Database.GetConnectionString()!;
        await using var host = await AuthenticationApiProcess.Start(connection);
        var actor = await Login(host, h);
        var floor = await SceneDeltaFloor(h);
        var zone = await PublishZone(h);
        var before = await SceneDeltaRows(h);
        var seq = await SceneDeltaSequence(h);
        await using var control = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await control.OpenAsync();
        await observer.OpenAsync();
        Task<HttpResponseMessage> pending;
        FenceWait? wait;
        string whileHeld;
        await using (var tx = await control.BeginTransactionAsync())
        {
            await using var hold = new NpgsqlCommand("SELECT cp6_authentication_v1.acquire()", control, tx);
            await hold.ExecuteNonQueryAsync();
            pending = host.Send(OperationsRequest(actor, HttpMethod.Post, PublishPath(floor), new { zoneId = zone }));
            wait = await ObservePublishWait(observer, control.ProcessID, pending);
            whileHeld = await SceneDeltaRows(h);
            await tx.CommitAsync();
        }
        using var result = await pending;
        Assert.Equal(1, await PublishedCount(result));
        var receipt = await AssertLegacyPublished(h, 1, seq);
        await DeviceEvidence("legacy-publish-authority.json", new { Status = (int)result.StatusCode, Wait = wait,
            Before = before, WhileHeld = whileHeld, Receipt = receipt });
        Assert.NotNull(wait);
        Assert.Equal(before, whileHeld);
    }

    private static async Task<string> AssertLegacyPublished(Harness h, int count, int seq)
    {
        var rows = await h.Db.Space_Locations.AsNoTracking().Where(x => x.Id != Guid.Empty && x.LocationCode != "FROZEN" && x.Status == 1).ToArrayAsync();
        var bins = await h.Db.WmsBins.AsNoTracking().ToDictionaryAsync(x => x.Id);
        var evt = await h.Db.IntegrationEvents.AsNoTracking().SingleAsync(x => x.SourceModule == "SPACE" && x.TargetModule == "WMS");
        Assert.Equal(count, rows.Length);
        Assert.Equal(count, bins.Count);
        foreach (var row in rows)
        {
            Assert.Equal(1, row.Version);
            var bin = bins[row.Id];
            Assert.True(bin.IsActive);
            Assert.Equal("SIM", bin.WarehouseCd);
            Assert.Equal(row.LocationCode, bin.LocationCode);
            Assert.Equal(row.Version, bin.Version);
        }
        Assert.Equal(IntegrationEventStatus.Success, evt.Status);
        Assert.Equal(h.Tenant, evt.TenantId);
        Assert.StartsWith("LPUB-", evt.SourceNo);
        Assert.Equal(seq + 1, await SceneDeltaSequence(h));
        return evt.SourceNo;
    }

    private sealed class PublishHub : IDisposable
    {
        private readonly ClientWebSocket _socket = new();
        private readonly Queue<string> _frames = new();

        public static async Task<PublishHub> Connect(AuthenticationApiProcess host)
        {
            var hub = new PublishHub();
            try
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                var uri = new UriBuilder(host.Origin) { Scheme = "ws", Path = "/hubs/space" }.Uri;
                await hub._socket.ConnectAsync(uri, deadline.Token);
                var handshake = Encoding.UTF8.GetBytes("{\"protocol\":\"json\",\"version\":1}\u001e");
                await hub._socket.SendAsync(new ArraySegment<byte>(handshake), WebSocketMessageType.Text, true, deadline.Token);
                var response = await hub.Frame(deadline.Token);
                Assert.False(response.TryGetProperty("error", out _), response.GetRawText());
                return hub;
            }
            catch { hub.Dispose(); throw; }
        }

        public async Task<JsonElement> ReadNotice(CancellationToken ct)
        {
            while (true)
            {
                var frame = await Frame(ct);
                if (frame.TryGetProperty("target", out var target) && target.GetString() == "LocationPublished")
                    return frame.GetProperty("arguments")[0];
            }
        }

        private async Task<JsonElement> Frame(CancellationToken ct)
        {
            while (_frames.Count == 0)
            {
                using var message = new MemoryStream();
                var buffer = new byte[1024];
                WebSocketReceiveResult part;
                do
                {
                    part = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    Assert.Equal(WebSocketMessageType.Text, part.MessageType);
                    message.Write(buffer, 0, part.Count);
                } while (!part.EndOfMessage);
                foreach (var frame in Encoding.UTF8.GetString(message.ToArray()).Split('\u001e', StringSplitOptions.RemoveEmptyEntries))
                    _frames.Enqueue(frame);
            }
            return JsonSerializer.Deserialize<JsonElement>(_frames.Dequeue());
        }

        public void Dispose() => _socket.Dispose();
    }
}
