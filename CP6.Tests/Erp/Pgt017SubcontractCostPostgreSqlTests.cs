using System.Net;
using System.Text.Json;
using CP6.Core.Services.Fin;
using CP6.Core.Services.Platform;
using CP6.Entity.DomainModels.Fin;
using CP6.Entity.DomainModels.Pur;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [TenantManagementPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PGT017_subcontract_finance_and_pause_share_the_native_commit_order(bool pauseFirst)
    {
        await using var h = await Seed();
        await using var admin = await TenantManagementSeed();
        var fixture = await SeedPurchaseMatch(h);
        var po = await h.Db.PurchaseOrders.SingleAsync(row => row.Id == fixture.Po);
        po.Type = 2;
        h.Db.PoConsignMaterials.Add(new PoConsignMaterial
        {
            PoNo = po.PoNo, LineNo = 1, ConsignItemId = "W639-CONSIGNED",
            ConsignQty = 1.25m, ConsignUnitCost = 4.55m, IssuedQty = 1.25m, WmsIssueNo = "W639-SIMULATED-ISSUE",
        });
        var period = await new FiscalPeriodService(h.Db, 1).EnsureOpenAsync(DateTime.Today, "fixture");
        await h.Db.SaveChangesAsync();
        var fg = await h.Db.GlAccounts.AsNoTracking().Where(row => row.Role == "FG").Select(row => row.Id).SingleAsync();
        await GrantLegacyAuthorityAction(h, "pur-subcontract", "cost");
        await using var host = await StartPurchaseMatchApi(h);
        var actor = await Login(host, h);
        var manager = await Login(host, admin);
        var path = $"/api/pur/subcontract/{fixture.PoNo}/1/finished-cost";
        using var tenantResult = await host.Send(OperationsRequest(manager, HttpMethod.Get, ManagementPath(h.Tenant)));
        var active = await NumberHttpData<TenantManagementSnapshotV1>(tenantResult);
        var pause = new TenantManagementCommandV1("subcontract-cost-pause", active.Revision, "suspend", null);
        var authority = await ReadPauseAuthority(h, admin.User);

        var connection = h.Db.Database.GetConnectionString()!;
        await using var observer = new NpgsqlConnection(connection);
        await observer.OpenAsync();
        var before = await ReadPurchaseMatch(observer, h.Tenant);
        var consign = await ReadConsign();
        Assert.Empty(before.Rows("journals"));
        var race = await ObserveAssetRace(connection, observer, "Fin_JournalEntry", "INSERT", "Sys_Tenants", "UPDATE",
            pauseFirst, Write,
            () => host.Send(OperationsRequest(manager, HttpMethod.Post, ManagementPath(h.Tenant, "/commands"), pause)),
            async () =>
            {
                Assert.Equal(before, await ReadPurchaseMatch(observer, h.Tenant));
                Assert.Equal(authority, await ReadPauseAuthority(h, admin.User));
                Assert.Equal(consign, await ReadConsign());
            });

        using var paused = race.Authority;
        using var business = race.Business;
        var receipt = await NumberHttpData<TenantManagementReceiptV1>(paused);
        CheckPause(authority, await ReadPauseAuthority(h, admin.User), receipt, pause);
        var afterRace = await ReadPurchaseMatch(observer, h.Tenant);
        JsonElement committed;
        if (pauseFirst)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, business.StatusCode);
            Assert.Equal(before, afterRace);
            await ResumeAssetTenant(host, manager, h.Tenant, receipt);
            actor = await Login(host, h);
            using var resumed = await Write();
            committed = await ReadApSuccessData(resumed);
        }
        else
        {
            committed = await ReadApSuccessData(business);
            Assert.Single(afterRace.Rows("journals"));
            using var stale = await Write();
            Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode);
            Assert.Equal(afterRace, await ReadPurchaseMatch(observer, h.Tenant));
            await ResumeAssetTenant(host, manager, h.Tenant, receipt);
            actor = await Login(host, h);
        }

        var final = await ReadPurchaseMatch(observer, h.Tenant);
        CheckPurchaseTables(before, final, "po", "po-lines", "receipts", "receipt-lines", "matches",
            "match-lines", "ap", "ap-lines", "periods", "pub-numbers");
        Assert.Equal(consign, await ReadConsign());
        Assert.Equal(30.85m, committed.GetProperty("processingFee").GetDecimal());
        Assert.Equal(5.6875m, committed.GetProperty("consignCost").GetDecimal());
        Assert.Equal(36.5375m, committed.GetProperty("finishedCost").GetDecimal());
        var journal = Assert.Single(final.Rows("journals"));
        Assert.Equal(committed.GetProperty("costVoucherNo").GetString(), journal.GetProperty("No").GetString());
        Assert.Equal($"SC-{fixture.PoNo}-1", journal.GetProperty("SourceDocNo").GetString());
        Assert.Equal((int)VoucherSource.Cost, AssetInt(journal, "Source"));
        Assert.Equal((int)JournalStatus.Posted, AssetInt(journal, "Status"));
        Assert.Equal(period.Id, AssetId(journal, "PeriodId"));
        var lines = final.Rows("journal-lines");
        Assert.Equal(2, lines.Length);
        Assert.All(lines, line => Assert.Equal(AssetId(journal), AssetId(line, "EntryId")));
        var debit = Assert.Single(lines, line => AssetId(line, "AccountId") == fg);
        var credit = Assert.Single(lines, line => AssetId(line, "AccountId") == fixture.Inventory);
        Assert.Equal((36.54m, 0m), (AssetDecimal(debit, "Debit"), AssetDecimal(debit, "Credit")));
        Assert.Equal((0m, 36.54m), (AssetDecimal(credit, "Debit"), AssetDecimal(credit, "Credit")));
        var number = Assert.Single(final.Rows("fin-numbers"));
        Assert.Equal("GL", number.GetProperty("SeqKey").GetString());
        Assert.Equal(1, AssetInt(number, "CurrentValue"));

        using var replay = await Write();
        Assert.Equal(committed.GetRawText(), (await ReadApSuccessData(replay)).GetRawText());
        Assert.Equal(final, await ReadPurchaseMatch(observer, h.Tenant));
        output.WriteLine(JsonSerializer.Serialize(new
        {
            h.Tenant, pauseFirst, race.FirstWait, race.RootWait, Business = (int)business.StatusCode,
            Before = PurchaseState(before), DuringPause = PurchaseState(afterRace),
            Original = committed, Final = PurchaseState(final),
        }));

        Task<HttpResponseMessage> Write() =>
            host.Send(OperationsRequest(actor, HttpMethod.Post, path, new { FinishedQty = PurchaseQty }));

        async Task<string> ReadConsign()
        {
            await using var query = new NpgsqlCommand("""
                SELECT jsonb_agg(to_jsonb(c) ORDER BY c."Id")::text
                FROM public."Pur_PoConsignMaterial" c WHERE c."TenantId"=@tenant
                """, observer);
            query.Parameters.AddWithValue("tenant", h.Tenant);
            return Assert.IsType<string>(await query.ExecuteScalarAsync());
        }
    }
}
