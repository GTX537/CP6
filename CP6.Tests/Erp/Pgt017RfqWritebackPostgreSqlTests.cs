using System.Net;
using CP6.Core.Services.Platform;
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
    public async Task PGT017_rfq_writeback_and_tenant_pause_share_each_price_commit(bool pauseFirst)
    {
        await using var h = await Seed();
        await using var administrator = await TenantManagementSeed();
        var db = h.Db;
        const string resource = "pur-rfq";
        const string action = "writeback";
        await GrantLegacyAuthorityAction(h, resource, action);
        var number = "RFQ" + h.Tenant.ToString("N")[..12];
        var quotation = new Rfq
        {
            RfqNo = number, RfqDate = DateTime.Now, Status = RfqStatus.Selected,
            Lines = [new() { RfqNo = number, LineNo = 1, ItemId = "part", Qty = 3.12345678m }],
            Quotes = [new() { RfqNo = number, LineNo = 1, SupplierId = "fixture", IsSelected = true, QuotedPrice = 12.1234m }]
        };
        db.Rfqs.Add(quotation);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var connection = db.Database.GetConnectionString()!;
        await using var host = await AuthenticationApiProcess.Start(connection,
            IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(h.Tenant));
        var writer = await Login(host, h);
        var manager = await Login(host, administrator);
        using var initial = await host.Send(OperationsRequest(manager, HttpMethod.Get, ManagementPath(h.Tenant)));
        var tenant = await NumberHttpData<TenantManagementSnapshotV1>(initial);
        var pause = new TenantManagementCommandV1("pgt017-rfq-writeback-" + pauseFirst, tenant.Revision, "suspend", null);
        var before = await ReadPauseAuthority(h, administrator.User);
        Assert.Empty(await Prices());
        await using var barrier = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await barrier.OpenAsync();
        await observer.OpenAsync();
        Task<HttpResponseMessage>? writing = null;
        Task<HttpResponseMessage>? pausing = null;
        try
        {
            await using var hold = await barrier.BeginTransactionAsync();
            try
            {
                FenceWait first;
                FenceWait second;
                if (pauseFirst)
                {
                    await using var row = new NpgsqlCommand(
                        """SELECT 1 FROM public."Sys_Tenants" WHERE "Id"=@tenant FOR UPDATE""", barrier, hold);
                    row.Parameters.AddWithValue("tenant", h.Tenant);
                    Assert.Equal(1, await row.ExecuteScalarAsync());
                    pausing = Pause();
                    first = await WaitForFence(observer, barrier.ProcessID, "%UPDATE%Sys_Tenants%", null, pausing);
                    writing = Write();
                    second = await WaitForFence(observer, first.Backend, "%cp6_authentication_v1.acquire()%", null, writing);
                }
                else
                {
                    await using var row = new NpgsqlCommand(
                        """LOCK TABLE public."Pur_SupplierPrice" IN SHARE MODE""", barrier, hold);
                    await row.ExecuteNonQueryAsync();
                    writing = Write();
                    first = await WaitForFence(observer, barrier.ProcessID, "%INSERT%Pur_SupplierPrice%", null, writing);
                    pausing = Pause();
                    second = await WaitForFence(observer, first.Backend, "%cp6_authentication_v1.acquire()%", null, pausing);
                }
                output.WriteLine($"pauseFirst={pauseFirst}, first={first}, second={second}");
                Assert.Empty(await Prices());
                var during = await ReadPauseAuthority(h, administrator.User);
                // The earlier authorized quote scan commits its own acquisition of the shared root.
                Assert.Equal(before.RootRevision + (pauseFirst ? 0 : 1), during.RootRevision);
                Assert.Equal(before with { RootRevision = during.RootRevision }, during);
            }
            finally { await hold.RollbackAsync(); }
        }
        catch
        {
            if (writing != null)
            {
                using var completed = await writing;
                output.WriteLine($"Writeback after releasing barrier: {(int)completed.StatusCode}, prices: {(await Prices()).Length}");
            }
            if (pausing != null)
            {
                using var completed = await pausing;
                output.WriteLine($"Pause after releasing barrier: {(int)completed.StatusCode}");
            }
            throw;
        }

        using var paused = await pausing!;
        var receipt = await NumberHttpData<TenantManagementReceiptV1>(paused);
        CheckPause(before, await ReadPauseAuthority(h, administrator.User), receipt, pause);
        using var result = await writing!;
        var prices = await Prices();
        if (pauseFirst) Assert.Empty(prices);
        else
        {
            var price = Assert.Single(prices);
            Assert.Equal(12.1234m, price.Price);
            Assert.Equal("rfq", price.Source);
        }
        Assert.True(result.StatusCode is HttpStatusCode.OK or HttpStatusCode.Unauthorized,
            await OperationsFailure(result));
        if (pauseFirst) Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
        using var late = await Write();
        Assert.Equal(HttpStatusCode.Unauthorized, late.StatusCode);
        var final = await Prices();
        Assert.Equal(prices.Select(x => (x.Id, x.Price, Convert.ToHexString(x.RowVersion!))),
            final.Select(x => (x.Id, x.Price, Convert.ToHexString(x.RowVersion!))));

        Task<SupplierPrice[]> Prices() => db.SupplierPrices.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync();
        Task<HttpResponseMessage> Write() => host.Send(OperationsRequest(writer, HttpMethod.Post, $"api/pur/rfq/{number}/writeback"));
        Task<HttpResponseMessage> Pause() => host.Send(OperationsRequest(manager, HttpMethod.Post, ManagementPath(h.Tenant, "/commands"), pause));
    }

    [TenantManagementPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PGT017_rfq_writeback_keeps_original_price_boundaries_and_retry_key(bool laterQuoteInvalid)
    {
        await using var h = await Seed();
        var db = h.Db;
        await GrantLegacyAuthorityAction(h, "pur-rfq", "writeback");
        var number = "RFQ" + h.Tenant.ToString("N")[..12];
        var first = new RfqQuote
        {
            RfqNo = number, LineNo = 1, SupplierId = "fixture", IsSelected = true, QuotedPrice = 12.1234m
        };
        var second = new RfqQuote
        {
            RfqNo = number, LineNo = 2, SupplierId = "fixture", IsSelected = true,
            QuotedPrice = laterQuoteInvalid ? -1m : 34.5678m
        };
        db.Rfqs.Add(new()
        {
            RfqNo = number, RfqDate = DateTime.Now, Status = RfqStatus.Selected, Quotes = [first, second],
            Lines = [new() { RfqNo = number, LineNo = 1, ItemId = "one", Qty = 1m },
                new() { RfqNo = number, LineNo = 2, ItemId = "two", Qty = 1m }]
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        await using var host = await AuthenticationApiProcess.Start(db.Database.GetConnectionString()!,
            IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(h.Tenant));
        var writer = await Login(host, h);

        using var applied = await Write();
        var prices = await Prices();
        if (laterQuoteInvalid)
        {
            Assert.Equal(HttpStatusCode.BadRequest, applied.StatusCode);
            Assert.Contains("E-PUR-013", await OperationsFailure(applied));
            Assert.Null(await db.Rfqs.AsNoTracking().Where(x => x.RfqNo == number).Select(x => x.Modifier).SingleAsync());
            Assert.Equal(12.1234m, Assert.Single(prices).Price);
            await db.RfqQuotes.Where(x => x.Id == second.Id).ExecuteUpdateAsync(x => x.SetProperty(q => q.QuotedPrice, 34.5678m));
        }
        else
        {
            Assert.True(applied.StatusCode == HttpStatusCode.OK, await OperationsFailure(applied));
            Assert.Equal(new[] { 12.1234m, 34.5678m }, prices.Select(x => x.Price));
        }
        var original = prices[0].Id;
        using var retry = await Write();
        Assert.True(retry.StatusCode == HttpStatusCode.OK, await OperationsFailure(retry));
        var final = await Prices();
        Assert.Equal(new[] { 12.1234m, 34.5678m }, final.Select(x => x.Price));
        Assert.Equal(original, final[0].Id);
        Assert.All(final, row => Assert.Equal("rfq", row.Source));
        Assert.NotNull(await db.Rfqs.AsNoTracking().Where(x => x.RfqNo == number).Select(x => x.Modifier).SingleAsync());

        Task<SupplierPrice[]> Prices() => db.SupplierPrices.AsNoTracking().OrderBy(x => x.ItemId).ToArrayAsync();
        Task<HttpResponseMessage> Write() => host.Send(OperationsRequest(writer, HttpMethod.Post, $"api/pur/rfq/{number}/writeback"));
    }
}
