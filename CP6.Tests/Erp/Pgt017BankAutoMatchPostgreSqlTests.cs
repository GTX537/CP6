using System.Net;
using CP6.Core.Services.Platform;
using CP6.Entity.DomainModels.Fin;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [TenantManagementPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PGT017_bank_auto_match_orders_each_group_with_tenant_pause(bool pauseFirst)
    {
        await using var h = await Seed();
        await using var admin = await TenantManagementSeed();
        var fixture = await SeedBankMatch(h);
        var db = h.Db;
        var connection = db.Database.GetConnectionString()!;
        await using var host = await AuthenticationApiProcess.Start(connection,
            IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(h.Tenant));
        var manager = await Login(host, admin);
        var actor = await Login(host, h);
        using var snapshot = await host.Send(OperationsRequest(manager, HttpMethod.Get, ManagementPath(h.Tenant)));
        var current = await NumberHttpData<TenantManagementSnapshotV1>(snapshot);
        var suspend = new TenantManagementCommandV1("bank-match-pause", current.Revision, "suspend", null);
        var authorityBefore = await ReadPauseAuthority(h, admin.User);
        var before = await ReadMatches();
        Assert.Equal(0, before.Groups);
        await using var barrier = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await barrier.OpenAsync();
        await observer.OpenAsync();
        Task<HttpResponseMessage> pause;
        Task<HttpResponseMessage> write;
        await using (var hold = await barrier.BeginTransactionAsync())
        {
            var table = pauseFirst ? """LOCK TABLE public."Sys_Tenants" IN SHARE MODE"""
                : """LOCK TABLE public."Fin_BankReconMatch" IN SHARE MODE""";
            using var lockTable = new NpgsqlCommand(table, barrier, hold);
            await lockTable.ExecuteNonQueryAsync();
            if (pauseFirst)
            {
                pause = Pause();
                var management = await WaitForFence(observer, barrier.ProcessID, "%UPDATE%Sys_Tenants%", null, pause);
                write = AutoMatch();
                var waiting = await WaitForFence(observer, management.Backend, "%cp6_authentication_v1.acquire()%", null, write);
                output.WriteLine($"Pause {management.Backend}; bank match {waiting.Backend}; {waiting.WaitEvent}");
            }
            else
            {
                write = AutoMatch();
                var group = await WaitForFence(observer, barrier.ProcessID, "%INSERT%Fin_BankReconMatch%", null, write);
                pause = Pause();
                var waiting = await WaitForFence(observer, group.Backend, "%cp6_authentication_v1.acquire()%", null, pause);
                output.WriteLine($"Bank group {group.Backend}; pause {waiting.Backend}; {waiting.WaitEvent}");
            }
            Assert.Equal(before, await ReadMatches());
            var observedAuthority = await ReadPauseAuthority(h, admin.User);
            // The authorized candidate scan can commit before the first match transaction.
            Assert.Equal(authorityBefore, observedAuthority with { RootRevision = authorityBefore.RootRevision });
            await hold.RollbackAsync();
        }
        using var paused = await pause;
        using var result = await write;
        var receipt = await NumberHttpData<TenantManagementReceiptV1>(paused);
        CheckPause(authorityBefore, await ReadPauseAuthority(h, admin.User), receipt, suspend);
        Assert.True(result.StatusCode == HttpStatusCode.Unauthorized, await OperationsFailure(result));
        var saved = await ReadMatches();
        Assert.Equal(pauseFirst ? before : new BankMatchEffects(1, 1, 1, 0), saved);
        using var oldSession = await AutoMatch();
        Assert.Equal(HttpStatusCode.Unauthorized, oldSession.StatusCode);
        Assert.Equal(saved, await ReadMatches());

        var reopen = new TenantManagementCommandV1("bank-match-reactivate", receipt.Snapshot.Revision, "reactivate", null);
        using var reopened = await host.Send(OperationsRequest(manager, HttpMethod.Post, ManagementPath(h.Tenant, "/commands"), reopen));
        Assert.True(reopened.StatusCode == HttpStatusCode.OK, await OperationsFailure(reopened));
        actor = await Login(host, h);
        using var completed = await AutoMatch();
        Assert.True(completed.StatusCode == HttpStatusCode.OK, await OperationsFailure(completed));
        Assert.Equal(new BankMatchEffects(2, 2, 2, 0), await ReadMatches());
        using var repeat = await AutoMatch();
        Assert.True(repeat.StatusCode == HttpStatusCode.OK, await OperationsFailure(repeat));
        Assert.Equal(new BankMatchEffects(2, 2, 2, 0), await ReadMatches());

        Task<HttpResponseMessage> Pause() =>
            host.Send(OperationsRequest(manager, HttpMethod.Post, ManagementPath(h.Tenant, "/commands"), suspend));
        Task<HttpResponseMessage> AutoMatch() =>
            host.Send(OperationsRequest(actor, HttpMethod.Post, $"/api/fin/bank-recon/{fixture.Statement:D}/auto-match"));
        async Task<BankMatchEffects> ReadMatches() => new(
            await db.BankReconMatches.CountAsync(x => x.StatementId == fixture.Statement),
            await db.BankReconJournalLinks.CountAsync(),
            await db.BankStatementLines.CountAsync(x => x.StatementId == fixture.Statement && x.MatchStatus == BankLineMatchStatus.Matched),
            await db.FinSequences.SumAsync(x => x.CurrentValue));
    }

    private static async Task<BankVoucherFixture> SeedBankMatch(Harness h)
    {
        var fixture = await SeedBankVoucher(h, lineCount: 2);
        await GrantLegacyAuthorityAction(h, BankReconMenu, "match");
        var db = h.Db;
        var period = await db.FiscalPeriods.SingleAsync();
        var amounts = new[] { BankVoucherAmount, 123.45m };
        var other = await db.BankStatementLines.SingleAsync(x => x.Id == fixture.LineIds[1]);
        other.Amount = amounts[1];
        other.RecomputeSigned();
        var date = new DateTime(2026, 11, 12);
        for (var i = 0; i < amounts.Length; i++)
        {
            var amount = amounts[i];
            db.JournalEntries.Add(new()
            {
                No = $"FIX-MATCH-{i}", VoucherDate = date, PeriodId = period.Id,
                Source = VoucherSource.Manual, Status = JournalStatus.Posted,
                Lines =
                [
                    new() { LineNo = 1, AccountId = fixture.BankGl, Credit = amount },
                    new() { LineNo = 2, AccountId = fixture.FeeGl, Debit = amount }
                ]
            });
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return fixture;
    }

    private sealed record BankMatchEffects(int Groups, int Links, int Matched, int Sequence);
}
