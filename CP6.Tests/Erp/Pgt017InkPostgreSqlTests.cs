using System.Net;
using System.Text.Json;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [TenantManagementPostgresTheory]
    [InlineData("create", true)]
    [InlineData("open", true)]
    [InlineData("mix", true)]
    [InlineData("match", true)]
    [InlineData("mix", false)]
    public async Task PGT017_ink_commands_hold_current_authority_through_commit(string operation, bool revokeFirst)
    {
        await using var h = await Seed();
        await SeedFenceAdministrator(h);
        var db = h.Db;
        const string menu = "wms-ink";
        var permission = operation is "create" or "match" ? "add" : operation;
        if (!await db.Sys_Menus.AnyAsync(row => row.MenuKey == menu))
        {
            db.Sys_Menus.Add(new() { MenuId = 444, MenuKey = menu, MenuName = "ink management" });
            await db.SaveChangesAsync();
        }
        await GrantLegacyAuthorityAction(h, menu, permission);

        const string parentA = "PGT017-INK-A";
        const string parentB = "PGT017-INK-B";
        const decimal consumedA = 1.12345678m;
        const decimal consumedB = 2.23456789m;
        var expiry = new DateTime(2026, 12, 1);
        db.InkLots.AddRange(
            new InkLot { InkLotNo = parentA, ColorCode = "BLUE", Quantity = 4m, ExpiryDate = expiry },
            new InkLot { InkLotNo = parentB, ColorCode = "WHITE", Quantity = 6m, ExpiryDate = expiry.AddDays(15) });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var connection = db.Database.GetConnectionString()!;
        await using var host = await AuthenticationApiProcess.Start(connection,
            IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(h.Tenant));
        var writer = await Login(host, h);
        var manager = await Login(host, "fence-manager", "Fence1!Password", h.Tenant.ToString("N"));
        var revoke = await RevokeActions(host, manager, permission);
        var before = await InkWrites();
        var authorityBefore = await ReadPolicyWrites(h, revoke.OperationId);
        await using var barrier = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await barrier.OpenAsync();
        await observer.OpenAsync();

        Task<HttpResponseMessage> change;
        Task<HttpResponseMessage> write;
        FenceWait firstWait;
        FenceWait rootWait;
        await using (var hold = await barrier.BeginTransactionAsync())
        {
            if (revokeFirst)
            {
                await using var lockActions = new NpgsqlCommand(
                    """LOCK TABLE public."Sys_RoleAction" IN SHARE MODE""", barrier, hold);
                await lockActions.ExecuteNonQueryAsync();
                change = Revoke();
                firstWait = await WaitForFence(observer, barrier.ProcessID, "%DELETE%Sys_RoleAction%", null, change);
                write = Write();
                rootWait = await WaitForFence(observer, firstWait.Backend, "%cp6_authentication_v1.acquire()%", null, write);
            }
            else
            {
                await using var lockLots = new NpgsqlCommand(
                    """LOCK TABLE public."T_InkLot" IN SHARE MODE""", barrier, hold);
                await lockLots.ExecuteNonQueryAsync();
                write = Write();
                firstWait = await WaitForFence(observer, barrier.ProcessID, "%UPDATE%T_InkLot%", null, write);
                change = Revoke();
                rootWait = await WaitForFence(observer, firstWait.Backend, "%cp6_authentication_v1.acquire()%", null, change);
            }
            Assert.Equal(before, await InkWrites());
            Assert.Equal(authorityBefore, await ReadPolicyWrites(h, revoke.OperationId));
            await hold.RollbackAsync();
        }

        using var changed = await change;
        Assert.True(changed.StatusCode == HttpStatusCode.OK, await OperationsFailure(changed));
        var policy = await ReadPolicyWrites(h, revoke.OperationId);
        Assert.NotEqual(authorityBefore.RoleRevision, policy.RoleRevision);
        Assert.Equal(authorityBefore.Generation + 1, policy.Generation);
        Assert.Equal(authorityBefore.Snapshot + 1, policy.Snapshot);
        Assert.Equal(authorityBefore.Outbox + 1, policy.Outbox);
        Assert.Equal(1, policy.CurrentOutbox);
        Assert.Equal(1, policy.Audits);
        Assert.Equal(1, policy.Receipts);

        using var result = await write;
        if (revokeFirst)
        {
            Assert.Equal(HttpStatusCode.Forbidden, result.StatusCode);
            Assert.Contains("IAM_FORBIDDEN", await OperationsFailure(result));
            Assert.Equal(before, await InkWrites());
            await GrantLegacyAuthorityAction(h, menu, permission);
            using var allowed = await Write();
            Assert.True(allowed.StatusCode == HttpStatusCode.OK, await OperationsFailure(allowed));
            if (operation == "mix")
            {
                var committed = await InkWrites();
                // PostgreSQL rejects the new lot's color after the sequence save has flushed the parents.
                using var rejected = await Write(new string('X', 31));
                Assert.Equal(HttpStatusCode.ServiceUnavailable, rejected.StatusCode);
                Assert.Equal(committed, await InkWrites());
            }
        }
        else
        {
            Assert.True(result.StatusCode == HttpStatusCode.OK, await OperationsFailure(result));
            var committed = await InkWrites();
            using var stale = await Write();
            Assert.Equal(HttpStatusCode.Forbidden, stale.StatusCode);
            Assert.Equal(committed, await InkWrites());
        }

        var lots = await db.InkLots.AsNoTracking().Where(row => row.TenantId == h.Tenant).ToListAsync();
        var lotA = lots.Single(row => row.InkLotNo == parentA);
        var lotB = lots.Single(row => row.InkLotNo == parentB);
        var matches = await db.InkColorMatchHistories.AsNoTracking().Where(row => row.TenantId == h.Tenant).ToListAsync();
        var sequence = await db.WmsSequences.AsNoTracking().Where(row => row.TenantId == h.Tenant &&
            (row.Prefix == "INK" || row.Prefix == "ICM")).ToListAsync();
        Assert.Equal(operation == "mix" ? 4m - consumedA : 4m, lotA.Quantity);
        Assert.Equal(operation == "mix" ? 6m - consumedB : 6m, lotB.Quantity);
        Assert.Equal(operation == "open" ? InkOpenStatus.Opened : InkOpenStatus.Unopened, lotA.OpenStatus);
        Assert.Equal(operation == "open" ? expiry.AddDays(2) : expiry, lotA.ExpiryDate);
        Assert.Equal(operation == "match" ? 1 : 0, matches.Count);
        Assert.Equal(operation is "create" or "mix" ? 3 : 2, lots.Count);
        if (operation == "open") Assert.Empty(sequence);
        else Assert.Equal(1, Assert.Single(sequence).NextNo);
        if (operation is "create" or "mix")
        {
            var newLot = lots.Single(row => row.InkLotNo != parentA && row.InkLotNo != parentB);
            Assert.StartsWith("INK", newLot.InkLotNo);
            Assert.Equal("INK", sequence.Single().Prefix);
            Assert.Equal(expiry, newLot.ExpiryDate);
            Assert.Equal(operation == "mix" ? consumedA + consumedB : consumedA, newLot.Quantity);
            Assert.Equal(operation == "mix" ? parentA : null, newLot.ParentLotNoA);
            Assert.Equal(operation == "mix" ? parentB : null, newLot.ParentLotNoB);
        }
        if (operation == "match")
        {
            var match = Assert.Single(matches);
            Assert.StartsWith("ICM", match.MatchNo);
            Assert.Equal("ICM", sequence.Single().Prefix);
            Assert.Equal(consumedA, match.ConsumedQty);
            Assert.Equal("fixture-operator", match.OperatorCd);
        }
        output.WriteLine(JsonSerializer.Serialize(new { Operation = operation, RevokeFirst = revokeFirst,
            FirstWait = firstWait, RootWait = rootWait, Status = (int)result.StatusCode, LotCount = lots.Count }));

        Task<HttpResponseMessage> Revoke() => host.Send(OperationsRequest(manager, HttpMethod.Put, PolicyWritePath, revoke));

        Task<HttpResponseMessage> Write(string mixedColor = "PALE-BLUE")
        {
            (string Path, object Body) request = operation switch
            {
                "create" => ("lots", new InkLotDto { ColorCode = "BLACK", Quantity = consumedA, ExpiryDate = expiry }),
                "open" => ($"lots/{parentA}/open", new { NewExpiryDate = expiry.AddDays(2) }),
                "mix" => ("lots/mix", new MixInkRequest
                {
                    ParentLotNoA = parentA, ParentQtyA = consumedA, ParentLotNoB = parentB,
                    ParentQtyB = consumedB, NewColorCode = mixedColor
                }),
                "match" => ("matches", new InkColorMatchDto
                {
                    CustomerCd = "fixture-customer", ColorCode = "PALE-BLUE", ConsumedQty = consumedA,
                    OperatorCd = "fixture-operator"
                }),
                _ => throw new ArgumentOutOfRangeException(nameof(operation))
            };
            return host.Send(OperationsRequest(writer, HttpMethod.Post, $"api/wms/ink/{request.Path}", request.Body));
        }

        Task<string> InkWrites() => db.Database.SqlQuery<string>($"""
            SELECT jsonb_build_array(
              (SELECT jsonb_agg(to_jsonb(l) ORDER BY l."Id") FROM public."T_InkLot" l WHERE l."TenantId"={h.Tenant}),
              (SELECT jsonb_agg(to_jsonb(m) ORDER BY m."Id") FROM public."T_InkColorMatchHistory" m WHERE m."TenantId"={h.Tenant}),
              (SELECT jsonb_agg(to_jsonb(s) ORDER BY s."Id") FROM public."T_WmsSequence" s
                WHERE s."TenantId"={h.Tenant} AND s."Prefix" IN ('INK','ICM'))
            )::text AS "Value"
            """).SingleAsync();
    }
}
