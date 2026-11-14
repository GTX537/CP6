using System.Net;
using System.Text.Json;
using CP6.Entity.DomainModels.Pur;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [TenantManagementPostgresTheory]
    [InlineData("add", true)]
    [InlineData("invite", true)]
    [InlineData("quote", true)]
    [InlineData("rank", true)]
    [InlineData("select", true)]
    [InlineData("quote", false)]
    public async Task PGT017_rfq_mutations_share_the_current_role_revocation_fence(string action, bool revokeFirst)
    {
        await using var h = await Seed();
        await SeedFenceAdministrator(h);
        var db = h.Db;
        const string resource = "pur-rfq";
        await GrantLegacyAuthorityAction(h, resource, action);
        var suffix = h.Tenant.ToString("N")[..12];
        var date = new DateTime(2026, 11, 14);
        var pr = "PR" + suffix;
        var rfq = "RFQ" + suffix;
        var item = "item" + suffix;
        var existingSupplier = "A" + suffix;
        var invitedSupplier = "B" + suffix;
        const decimal quantity = 3.12345678m;
        const decimal price = 12.1234m;
        db.PurchaseRequests.Add(new PurchaseRequest
        {
            PrNo = pr, RequestDate = date,
            Lines = [new() { PrNo = pr, LineNo = 1, ItemId = item, Qty = quantity }]
        });
        db.Rfqs.Add(new Rfq
        {
            RfqNo = rfq, RfqDate = date,
            Lines = [new() { RfqNo = rfq, LineNo = 1, ItemId = item, Qty = quantity }],
            Suppliers = [new() { RfqNo = rfq, SupplierId = existingSupplier, InviteStatus = RfqInviteStatus.Invited }],
            Quotes = [new() { RfqNo = rfq, SupplierId = existingSupplier, LineNo = 1, QuotedPrice = 10m }]
        });
        db.BusinessPartners.Add(new() { BpCd = invitedSupplier, BpName = "fixture supplier", SupplierFlg = true });
        db.Pub_DocSequences.Add(new() { BizKey = "RFQ", Prefix = "RFQ" + suffix, SeqLength = 4 });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var (path, payload) = action switch
        {
            "add" => ($"from-pr/{pr}", (object?)null),
            "invite" => ($"{rfq}/suppliers", new[] { invitedSupplier }),
            "quote" => ($"{rfq}/quote", new { supplierId = existingSupplier, lines = new[] { new { lineNo = 1, quotedPrice = price } } }),
            "rank" => ($"{rfq}/rank", new { asOf = date }),
            "select" => ($"{rfq}/select", new[] { new { lineNo = 1, supplierId = existingSupplier } }),
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };
        var connection = db.Database.GetConnectionString()!;
        await using var host = await AuthenticationApiProcess.Start(connection,
            IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(h.Tenant));
        var writer = await Login(host, h);
        var manager = await Login(host, "fence-manager", "Fence1!Password", h.Tenant.ToString("N"));
        var revoke = await RevokeActions(host, manager, action);
        var before = await Effects();
        var authority = await ReadPolicyWrites(h, revoke.OperationId);
        await using var barrier = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await barrier.OpenAsync();
        await observer.OpenAsync();
        Task<HttpResponseMessage> change;
        Task<HttpResponseMessage> write;
        await using (var hold = await barrier.BeginTransactionAsync())
        {
            if (revokeFirst)
            {
                await using var table = new NpgsqlCommand("""LOCK TABLE public."Sys_RoleAction" IN SHARE MODE""", barrier, hold);
                await table.ExecuteNonQueryAsync();
                change = Revoke();
                var iam = await WaitForFence(observer, barrier.ProcessID, "%DELETE%Sys_RoleAction%", null, change);
                write = Write();
                await WaitForFence(observer, iam.Backend, "%cp6_authentication_v1.acquire()%", null, write);
            }
            else
            {
                await using var table = new NpgsqlCommand("""LOCK TABLE public."Pur_RfqQuote" IN SHARE MODE""", barrier, hold);
                await table.ExecuteNonQueryAsync();
                write = Write();
                var buyer = await WaitForFence(observer, barrier.ProcessID, "%UPDATE%Pur_RfqQuote%", null, write);
                change = Revoke();
                await WaitForFence(observer, buyer.Backend, "%cp6_authentication_v1.acquire()%", null, change);
            }
            Assert.Equal(before, await Effects());
            Assert.Equal(authority, await ReadPolicyWrites(h, revoke.OperationId));
            await hold.RollbackAsync();
        }

        using var changed = await change;
        Assert.True(changed.StatusCode == HttpStatusCode.OK, await OperationsFailure(changed));
        var after = await ReadPolicyWrites(h, revoke.OperationId);
        Assert.NotEqual(authority.RoleRevision, after.RoleRevision);
        Assert.Equal(authority.Generation + 1, after.Generation);
        Assert.Equal(authority.Snapshot + 1, after.Snapshot);
        Assert.Equal(authority.Outbox + 1, after.Outbox);
        Assert.Equal(1, after.CurrentOutbox);
        Assert.Equal(1, after.Audits);
        Assert.Equal(1, after.Receipts);

        using var result = await write;
        if (revokeFirst)
        {
            Assert.Equal(HttpStatusCode.Forbidden, result.StatusCode);
            Assert.Equal(before, await Effects());
            await GrantLegacyAuthorityAction(h, resource, action);
            using var allowed = await Write();
            Assert.True(allowed.StatusCode == HttpStatusCode.OK, await OperationsFailure(allowed));
        }
        else
        {
            Assert.True(result.StatusCode == HttpStatusCode.OK, await OperationsFailure(result));
            var committed = await Effects();
            using var stale = await Write();
            Assert.Equal(HttpStatusCode.Forbidden, stale.StatusCode);
            Assert.Equal(committed, await Effects());
        }
        Assert.NotEqual(before, await Effects());

        switch (action)
        {
            case "add":
                var created = await db.Rfqs.AsNoTracking().SingleAsync(x => x.SourcePrNo == pr);
                var line = await db.RfqLines.AsNoTracking().SingleAsync(x => x.RfqNo == created.RfqNo);
                Assert.Equal(quantity, line.Qty);
                Assert.Equal(pr, line.SourcePrNo);
                Assert.Equal(1, await db.Pub_DocSequences.Where(x => x.BizKey == "RFQ").Select(x => x.CurrentValue).SingleAsync());
                break;
            case "invite":
                var invitation = await db.RfqSuppliers.AsNoTracking().SingleAsync(x => x.SupplierId == invitedSupplier);
                Assert.Equal(RfqInviteStatus.Invited, invitation.InviteStatus);
                break;
            case "quote":
                Assert.Equal(price, (await db.RfqQuotes.AsNoTracking().SingleAsync()).QuotedPrice);
                Assert.Equal(RfqInviteStatus.Quoted, (await db.RfqSuppliers.AsNoTracking().SingleAsync()).InviteStatus);
                break;
            case "rank":
                Assert.Equal(1, (await db.RfqQuotes.AsNoTracking().SingleAsync()).Rank);
                break;
            case "select":
                Assert.True((await db.RfqQuotes.AsNoTracking().SingleAsync()).IsSelected);
                Assert.Equal(RfqStatus.Selected, (await db.Rfqs.AsNoTracking().SingleAsync()).Status);
                break;
        }

        Task<HttpResponseMessage> Revoke() => host.Send(OperationsRequest(manager, HttpMethod.Put, PolicyWritePath, revoke));
        Task<HttpResponseMessage> Write() => host.Send(OperationsRequest(writer, HttpMethod.Post, $"api/pur/rfq/{path}", payload));
        async Task<string> Effects() => JsonSerializer.Serialize(new
        {
            Head = await db.Rfqs.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Lines = await db.RfqLines.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Invitations = await db.RfqSuppliers.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Quotes = await db.RfqQuotes.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Number = await db.Pub_DocSequences.AsNoTracking().Where(x => x.BizKey == "RFQ").SingleAsync()
        });
    }
}
