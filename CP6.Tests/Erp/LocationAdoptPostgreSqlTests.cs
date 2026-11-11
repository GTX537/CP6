using System.Net;
using System.Text.Json;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private const string AdoptPath = "api/space/location/adopt";

    private async Task<Harness> LocationAdoptSeed()
    {
        var h = await SpaceCodeSeed();
        var db = h.Db;
        var menu = await db.Sys_Menus.SingleOrDefaultAsync(x => x.MenuKey == "space-publish");
        if (menu is null)
        {
            menu = new() { MenuId = 905, MenuKey = "space-publish", MenuName = "SIM Publish", RoutePath = "/space/publish" };
            db.Sys_Menus.Add(menu);
            await db.SaveChangesAsync();
        }
        if (!await db.Sys_MenuActions.AnyAsync(x => x.MenuId == menu.MenuId && x.ActionCode == "adopt"))
            db.Sys_MenuActions.Add(new() { MenuId = menu.MenuId, ActionCode = "adopt", ActionName = "Adopt" });
        db.Sys_RoleActions.Add(new() { RoleId = 72, MenuId = menu.MenuId, ActionCode = "adopt" });
        db.Space_Locations.Add(new() { Id = Guid.NewGuid(), LocationCode = "RESERVED", CodeOrigin = 2, Status = 2, IsDeleted = true });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return h;
    }

    private static object AdoptInput(params string[] codes) => new { items = codes.Select(code => new { code }) };
    private sealed record AdoptReceipt(int Imported, string[] Skipped);
    private static async Task<AdoptReceipt> AdoptResponse(HttpResponseMessage response)
    {
        var data = await SpaceCodeHttpData<JsonElement>(response);
        return new(data.GetProperty("imported").GetInt32(), data.GetProperty("skipped").EnumerateArray().Select(x => x.GetString()!).ToArray());
    }
    private static Task<string[]> AdoptedCodes(Harness h) => h.Db.Space_Locations.AsNoTracking()
        .Where(x => x.CodeOrigin == 2 && !x.IsDeleted).Select(x => x.LocationCode!).OrderBy(x => x).ToArrayAsync();
    private static async Task<string> AdoptSecondaryState(Harness h) => JsonSerializer.Serialize(new
    {
        Bins = await h.Db.WmsBins.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
        Events = await h.Db.IntegrationEvents.AsNoTracking().Where(x => x.SourceModule == "SPACE").OrderBy(x => x.Id).ToArrayAsync(),
        LPB = await SceneDeltaSequence(h),
        Stock = await PalletStockRows(h)
    });

    [CurrentPermissionPostgresFact]
    public async Task LegacyAdopt_concurrent_occupied_code_is_skipped_without_losing_the_rest()
    {
        await using var h = await LocationAdoptSeed();
        var connection = h.Db.Database.GetConnectionString()!;
        await using var host = await AuthenticationApiProcess.Start(connection);
        var actor = await Login(host, h);
        var before = await AdoptSecondaryState(h);
        await using var control = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await control.OpenAsync();
        await observer.OpenAsync();
        await using var setup = new NpgsqlCommand($$"""
            CREATE FUNCTION public.cp6_test_adopt_insert_gate() RETURNS trigger LANGUAGE plpgsql AS $gate$
            BEGIN PERFORM pg_advisory_xact_lock(927601); RETURN NEW; END $gate$;
            CREATE TRIGGER cp6_test_adopt_insert_gate BEFORE INSERT ON public."Space_Location"
            FOR EACH ROW WHEN (NEW."TenantId"='{{h.Tenant}}' AND NEW."LocationCode"='ADOPT-RACE')
            EXECUTE FUNCTION public.cp6_test_adopt_insert_gate();
            """, control);
        await setup.ExecuteNonQueryAsync();
        Task<HttpResponseMessage> first, second;
        FenceWait firstWait, secondWait;
        try
        {
            await using (var tx = await control.BeginTransactionAsync())
            {
                await using var hold = new NpgsqlCommand("SELECT pg_advisory_xact_lock(927601)", control, tx);
                await hold.ExecuteNonQueryAsync();
                first = Send("ADOPT-RACE");
                firstWait = await WaitForFence(observer, control.ProcessID, "%INSERT%Space_Location%", null, first);
                second = Send("ADOPT-RACE", "ADOPT-SECOND", "ADOPT-SECOND");
                secondWait = await SerialNativeWait(observer, firstWait.Backend, control.ProcessID, "legacy-adopt-second", second);
                await tx.RollbackAsync();
            }
            using var won = await first;
            using var later = await second;
            var one = await AdoptResponse(won);
            var two = later.IsSuccessStatusCode ? await AdoptResponse(later) : null;
            var codes = await AdoptedCodes(h);
            var after = await AdoptSecondaryState(h);
            await DeviceEvidence("legacy-adopt-race.json", new { First = (int)won.StatusCode, Second = (int)later.StatusCode,
                FirstReceipt = one, SecondReceipt = two, firstWait, secondWait, Codes = codes, Before = before, After = after });
            Assert.Equal(1, one.Imported);
            Assert.Empty(one.Skipped);
            Assert.Equal(HttpStatusCode.OK, later.StatusCode);
            Assert.Equal(1, two!.Imported);
            Assert.Equal(new[] { "ADOPT-RACE", "ADOPT-SECOND" }, two.Skipped);
            Assert.Equal(new[] { "ADOPT-RACE", "ADOPT-SECOND" }, codes);
            Assert.Equal(firstWait.Backend, secondWait.Blocker);
            Assert.Equal(before, after);
        }
        finally
        {
            await using var cleanup = new NpgsqlCommand("""
                DROP TRIGGER cp6_test_adopt_insert_gate ON public."Space_Location";
                DROP FUNCTION public.cp6_test_adopt_insert_gate();
                """, control);
            await cleanup.ExecuteNonQueryAsync();
        }
        Task<HttpResponseMessage> Send(params string[] codes) => host.Send(OperationsRequest(actor, HttpMethod.Post, AdoptPath, AdoptInput(codes)));
    }

    [CurrentPermissionPostgresFact]
    public async Task LegacyAdopt_preserves_ordered_skip_receipts_and_creates_only_shadow_locations()
    {
        await using var h = await LocationAdoptSeed();
        await using var other = await LocationAdoptSeed();
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var actor = await Login(host, h);
        var otherActor = await Login(host, other);
        using var hub = await PublishHub.Connect(host);
        var before = await AdoptSecondaryState(h);
        var otherBefore = await AdoptSecondaryState(other);
        var items = new[] { "OLD-1", "RESERVED", "ADOPT-A", "ADOPT-B", "ADOPT-A" };
        using var first = await Send();
        var one = await AdoptResponse(first);
        Assert.Equal(2, one.Imported);
        Assert.Equal(new[] { "OLD-1", "RESERVED", "ADOPT-A" }, one.Skipped);
        using var repeat = await Send();
        var again = await AdoptResponse(repeat);
        Assert.Equal(0, again.Imported);
        Assert.Equal(items, again.Skipped);
        using var cross = await host.Send(OperationsRequest(otherActor, HttpMethod.Post, AdoptPath, AdoptInput("ADOPT-A")));
        var theirs = await AdoptResponse(cross);
        Assert.Equal(1, theirs.Imported);
        Assert.Empty(theirs.Skipped);
        var rows = await h.Db.Space_Locations.AsNoTracking().Where(x => x.CodeOrigin == 2 && !x.IsDeleted).ToArrayAsync();
        Assert.Equal(new[] { "ADOPT-A", "ADOPT-B" }, rows.Select(x => x.LocationCode).OrderBy(x => x));
        Assert.All(rows, row =>
        {
            Assert.Equal(h.Tenant, row.TenantId);
            Assert.Equal(1, row.Status);
            Assert.False(row.Placed);
            Assert.Null(row.RackId);
            Assert.Null(row.FloorId);
            Assert.Equal(0, row.Version);
        });
        Assert.True(await h.Db.Space_Locations.AnyAsync(x => x.LocationCode == "RESERVED" && x.IsDeleted));
        Assert.Equal(new[] { "ADOPT-A" }, await AdoptedCodes(other));
        var after = await AdoptSecondaryState(h);
        var otherAfter = await AdoptSecondaryState(other);
        Assert.Equal(before, after);
        Assert.Equal(otherBefore, otherAfter);
        using var noNotice = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => hub.ReadNotice(noNotice.Token));
        await DeviceEvidence("legacy-adopt-normal.json", new { First = (int)first.StatusCode, One = one, Again = again, Theirs = theirs,
            Before = before, After = after, OtherBefore = otherBefore, OtherAfter = otherAfter, NoNotice = true });
        Task<HttpResponseMessage> Send() => host.Send(OperationsRequest(actor, HttpMethod.Post, AdoptPath, AdoptInput(items)));
    }

    [CurrentPermissionPostgresFact]
    public async Task LegacyAdopt_deferred_commit_is_explicit_and_no_partial_batch_is_observed()
    {
        await using var h = await LocationAdoptSeed();
        var connection = h.Db.Database.GetConnectionString()!;
        await using var host = await AuthenticationApiProcess.Start(connection);
        var actor = await Login(host, h);
        var before = await SceneDeltaRows(h);
        var secondary = await AdoptSecondaryState(h);
        await using var control = new NpgsqlConnection(connection);
        await control.OpenAsync();
        await using var install = new NpgsqlCommand($$"""
            CREATE FUNCTION public.cp6_test_adopt_commit_failure() RETURNS trigger LANGUAGE plpgsql AS $fault$
            BEGIN RAISE EXCEPTION 'simulated legacy adoption commit failure'; END $fault$;
            CREATE CONSTRAINT TRIGGER cp6_test_adopt_commit_failure AFTER INSERT ON public."Space_Location"
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW WHEN (NEW."TenantId"='{{h.Tenant}}' AND NEW."LocationCode"='ADOPT-FAULT')
            EXECUTE FUNCTION public.cp6_test_adopt_commit_failure();
            """, control);
        await install.ExecuteNonQueryAsync();
        int status;
        string after;
        bool unknown;
        try
        {
            using var response = await Send();
            status = (int)response.StatusCode;
            unknown = (await response.Content.ReadAsStringAsync()).Contains("SPACE_ADOPT_OUTCOME_UNKNOWN", StringComparison.Ordinal);
            after = await SceneDeltaRows(h);
            Assert.Equal(before, after);
            Assert.Equal(secondary, await AdoptSecondaryState(h));
        }
        finally
        {
            await using var cleanup = new NpgsqlCommand("""
                DROP TRIGGER cp6_test_adopt_commit_failure ON public."Space_Location";
                DROP FUNCTION public.cp6_test_adopt_commit_failure();
                """, control);
            await cleanup.ExecuteNonQueryAsync();
        }
        using var retry = await Send();
        var recovered = await AdoptResponse(retry);
        Assert.Equal(2, recovered.Imported);
        Assert.Empty(recovered.Skipped);
        var secondaryAfter = await AdoptSecondaryState(h);
        Assert.Equal(secondary, secondaryAfter);
        await DeviceEvidence("legacy-adopt-commit.json", new { Status = status, Unknown = unknown, Before = before, After = after,
            SecondaryBefore = secondary, SecondaryAfter = secondaryAfter, Recovered = recovered });
        Assert.Equal((int)HttpStatusCode.ServiceUnavailable, status);
        Assert.True(unknown);
        Task<HttpResponseMessage> Send() => host.Send(OperationsRequest(actor, HttpMethod.Post, AdoptPath, AdoptInput("ADOPT-FIRST", "ADOPT-FAULT")));
    }
}
