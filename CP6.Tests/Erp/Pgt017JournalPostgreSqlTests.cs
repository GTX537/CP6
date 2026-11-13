using System.Net;
using System.Text.Json;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Fin;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [TenantManagementPostgresTheory]
    [InlineData("add", true)]
    [InlineData("submit", true)]
    [InlineData("post", true)]
    [InlineData("reject", true)]
    [InlineData("reverse", true)]
    [InlineData("post", false)]
    public async Task PGT017_manual_journal_commands_hold_current_authority_through_commit(string action, bool revokeFirst)
    {
        await using var h = await Seed();
        await SeedFenceAdministrator(h);
        var db = h.Db;
        const string menu = "fin-journal";
        if (!await db.Sys_Menus.AnyAsync(row => row.MenuKey == menu))
        {
            db.Sys_Menus.Add(new() { MenuId = 602, MenuKey = menu, MenuName = "manual journal" });
            await db.SaveChangesAsync();
        }
        await GrantLegacyAuthorityAction(h, menu, action);

        var debit = new GlAccount { Id = Guid.NewGuid(), Code = "J1", Name = "debit", Type = AccountType.Asset };
        var credit = new GlAccount { Id = Guid.NewGuid(), Code = "J2", Name = "credit", Type = AccountType.Liability };
        var date = new DateTime(2026, 11, 13);
        var period = new FiscalPeriod { Id = Guid.NewGuid(), Year = 2026, Month = 11, FiscalYear = 2026, PeriodNo = 11,
            PeriodStart = new(2026, 11, 1), PeriodEnd = new(2026, 11, 30) };
        var fixture = new JournalEntry
        {
            Id = Guid.NewGuid(), VoucherDate = date, PeriodId = period.Id, No = "PGT017-" + h.Tenant.ToString("N")[..12],
            MakerId = "original-maker",
            Status = action switch
            {
                "reverse" => JournalStatus.Posted,
                "post" or "reject" => JournalStatus.PendingReview,
                _ => JournalStatus.Draft
            },
            Lines = Lines()
        };
        db.GlAccounts.AddRange(debit, credit);
        db.FiscalPeriods.Add(period);
        db.JournalEntries.Add(fixture);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var connection = db.Database.GetConnectionString()!;
        await using var host = await AuthenticationApiProcess.Start(connection,
            IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(h.Tenant));
        var writer = await Login(host, h);
        var manager = await Login(host, "fence-manager", "Fence1!Password", h.Tenant.ToString("N"));
        var revoke = await RevokeActions(host, manager, action);
        var draftId = Guid.NewGuid();
        var before = await JournalWrites();
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
                await using var lockEntry = new NpgsqlCommand(
                    """LOCK TABLE public."Fin_JournalEntry" IN SHARE MODE""", barrier, hold);
                await lockEntry.ExecuteNonQueryAsync();
                write = Write();
                firstWait = await WaitForFence(observer, barrier.ProcessID, "%UPDATE%Fin_JournalEntry%", null, write);
                change = Revoke();
                rootWait = await WaitForFence(observer, firstWait.Backend, "%cp6_authentication_v1.acquire()%", null, change);
            }
            Assert.Equal(before, await JournalWrites());
            Assert.Equal(authorityBefore, await ReadPolicyWrites(h, revoke.OperationId));
            await hold.RollbackAsync();
        }

        using var changed = await change;
        Assert.True(changed.StatusCode == HttpStatusCode.OK, await OperationsFailure(changed));
        var policy = await ReadPolicyWrites(h, revoke.OperationId);
        Assert.Equal(authorityBefore.Generation + 1, policy.Generation);
        Assert.Equal(authorityBefore.Snapshot + 1, policy.Snapshot);
        Assert.Equal(authorityBefore.Outbox + 1, policy.Outbox);
        Assert.Equal(1, policy.Audits);
        Assert.Equal(1, policy.Receipts);
        using var result = await write;
        if (revokeFirst)
        {
            Assert.Equal(HttpStatusCode.Forbidden, result.StatusCode);
            Assert.Equal(before, await JournalWrites());
            await GrantLegacyAuthorityAction(h, menu, action);
            using var allowed = await Write();
            Assert.True(allowed.StatusCode == HttpStatusCode.OK, await OperationsFailure(allowed));
        }
        else
        {
            Assert.True(result.StatusCode == HttpStatusCode.OK, await OperationsFailure(result));
            var committed = await JournalWrites();
            using var stale = await Write();
            Assert.Equal(HttpStatusCode.Forbidden, stale.StatusCode);
            Assert.Equal(committed, await JournalWrites());
        }

        var id = action == "add" ? draftId : fixture.Id;
        var entry = await db.JournalEntries.AsNoTracking().Include(row => row.Lines).SingleAsync(row => row.Id == id);
        Assert.Equal(action switch
        {
            "submit" => JournalStatus.PendingReview,
            "post" => JournalStatus.Posted,
            "reject" => JournalStatus.Rejected,
            "reverse" => JournalStatus.Reversed,
            _ => JournalStatus.Draft
        }, entry.Status);
        if (action == "add")
        {
            Assert.Equal("price-owner", entry.MakerId);
            Assert.Equal(h.Tenant, entry.TenantId);
            Assert.All(entry.Lines, line => Assert.Equal(h.Tenant, line.TenantId));
            Assert.StartsWith("GL-2026-11-", entry.No);
            var committed = await JournalWrites();
            using var conflict = await host.Send(OperationsRequest(writer, HttpMethod.Post, "api/fin/journal",
                new { Id = draftId, VoucherDate = date.AddMonths(1), Lines = Lines() }));
            Assert.False(conflict.IsSuccessStatusCode);
            Assert.Equal(committed, await JournalWrites());
        }
        if (action == "post") Assert.Equal("price-owner", entry.CheckerId);
        if (action == "reject") Assert.Equal("fixture review", entry.RejectReason);
        if (action == "reverse")
        {
            var reversal = await db.JournalEntries.AsNoTracking().Include(row => row.Lines)
                .SingleAsync(row => row.ReverseOfId == id);
            Assert.Equal(reversal.Id, entry.ReversedById);
            Assert.Equal(JournalStatus.PendingReview, reversal.Status);
            Assert.Equal(17.23m, reversal.Lines.Single(line => line.AccountId == debit.Id).Credit);
        }
        Assert.Equal(17.23m, entry.Lines.Single(line => line.AccountId == debit.Id).Debit);
        Assert.Equal(1.123456m, entry.Lines.Single(line => line.AccountId == debit.Id).FxRate);
        output.WriteLine(JsonSerializer.Serialize(new { Action = action, RevokeFirst = revokeFirst,
            FirstWait = firstWait, RootWait = rootWait, Status = (int)result.StatusCode, FinalStatus = entry.Status }));

        List<JournalLine> Lines() =>
        [
            new() { AccountId = debit.Id, LineNo = 1, Debit = 17.23m, FxRate = 1.123456m },
            new() { AccountId = credit.Id, LineNo = 2, Credit = 17.23m }
        ];
        Task<HttpResponseMessage> Revoke() => host.Send(OperationsRequest(manager, HttpMethod.Put, PolicyWritePath, revoke));
        Task<HttpResponseMessage> Write() => host.Send(action == "add"
            ? OperationsRequest(writer, HttpMethod.Post, "api/fin/journal", new
                { Id = draftId, TenantId = h.Tenant, VoucherDate = date, Lines = Lines() })
            : OperationsRequest(writer, HttpMethod.Post, $"api/fin/journal/{fixture.Id}/{action}",
                action is "reject" or "reverse" ? new { Reason = "fixture review" } : null));
        Task<string> JournalWrites() => db.Database.SqlQuery<string>($"""
            SELECT jsonb_build_array(
              (SELECT jsonb_agg(to_jsonb(e) ORDER BY e."Id") FROM public."Fin_JournalEntry" e WHERE e."TenantId"={h.Tenant}),
              (SELECT jsonb_agg(to_jsonb(l) ORDER BY l."Id") FROM public."Fin_JournalLine" l WHERE l."TenantId"={h.Tenant}),
              (SELECT jsonb_agg(to_jsonb(s) ORDER BY s."Id") FROM public."Fin_Sequence" s WHERE s."TenantId"={h.Tenant}),
              (SELECT jsonb_agg(to_jsonb(p) ORDER BY p."Id") FROM public."Fin_FiscalPeriod" p WHERE p."TenantId"={h.Tenant})
            )::text AS "Value"
            """).SingleAsync();
    }
}
