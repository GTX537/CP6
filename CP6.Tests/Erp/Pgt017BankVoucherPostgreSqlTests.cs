using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.Platform;
using CP6.Entity.DomainModels.Fin;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private const string BankReconMenu = "fin-bank-reconciliation";
    private const string BankVoucherAction = "generate-voucher";
    private const decimal BankVoucherAmount = 46.81m;

    [TenantManagementPostgresTheory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task PGT017_bank_voucher_orders_pause_and_revocation(bool pause, bool authorityFirst)
    {
        await using var h = await Seed();
        await using var platform = pause ? await TenantManagementSeed() : null;
        if (!pause) await SeedFenceAdministrator(h);
        var fixture = await SeedBankVoucher(h, pause && !authorityFirst ? 2 : 1);
        var connection = h.Db.Database.GetConnectionString()!;
        await using var host = await AuthenticationApiProcess.Start(connection,
            IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(h.Tenant));
        var writer = await Login(host, h);
        var manager = pause ? await Login(host, platform!)
            : await Login(host, "fence-manager", "Fence1!Password", h.Tenant.ToString("N"));
        TenantManagementCommandV1? suspend = null;
        if (pause)
        {
            using var read = await host.Send(OperationsRequest(manager, HttpMethod.Get, ManagementPath(h.Tenant)));
            var snapshot = await NumberHttpData<TenantManagementSnapshotV1>(read);
            suspend = new("bank-voucher-pause", snapshot.Revision, "suspend", null);
        }
        var revoke = pause ? null : await RevokeActions(host, manager, BankVoucherAction);
        var beforePause = pause ? await ReadPauseAuthority(h, platform!.User) : null;
        var beforePolicy = pause ? null : await ReadPolicyWrites(h, revoke!.OperationId);
        var before = await ReadVoucherEffects(h, fixture, fixture.LineIds[0]);
        Assert.Equal(new BankVoucherEffects(null, null, BankLineMatchStatus.Unmatched, 0, 0, 0, 0, 0), before);

        await using var barrier = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await barrier.OpenAsync();
        await observer.OpenAsync();
        Task<HttpResponseMessage> change;
        Task<HttpResponseMessage> post;
        await using (var hold = await barrier.BeginTransactionAsync())
        {
            var blocked = authorityFirst
                ? pause ? """LOCK TABLE public."Sys_Tenants" IN SHARE MODE"""
                        : """LOCK TABLE public."Sys_RoleAction" IN SHARE MODE"""
                : """LOCK TABLE public."Fin_JournalEntry" IN SHARE MODE""";
            using var command = new NpgsqlCommand(blocked, barrier, hold);
            await command.ExecuteNonQueryAsync();
            if (authorityFirst)
            {
                change = Change();
                var authority = await WaitForFence(observer, barrier.ProcessID,
                    pause ? "%UPDATE%Sys_Tenants%" : "%DELETE%Sys_RoleAction%", null, change);
                post = Generate();
                var pending = await WaitForFence(observer, authority.Backend, "%cp6_authentication_v1.acquire()%", null, post);
                output.WriteLine($"Authority {authority.Backend}; bank voucher {pending.Backend}; {pending.WaitEvent}");
            }
            else
            {
                post = Generate();
                var voucher = await WaitForFence(observer, barrier.ProcessID, "%INSERT%Fin_JournalEntry%", null, post);
                change = Change();
                var pending = await WaitForFence(observer, voucher.Backend, "%cp6_authentication_v1.acquire()%", null, change);
                output.WriteLine($"Bank voucher {voucher.Backend}; authority {pending.Backend}; {pending.WaitEvent}");
            }
            Assert.Equal(before, await ReadVoucherEffects(h, fixture, fixture.LineIds[0]));
            if (fixture.LineIds.Length == 2)
                Assert.Equal(before, await ReadVoucherEffects(h, fixture, fixture.LineIds[1]));
            if (pause)
                Assert.Equal(beforePause, await ReadPauseAuthority(h, platform!.User));
            else
                Assert.Equal(beforePolicy, await ReadPolicyWrites(h, revoke!.OperationId));
            await hold.RollbackAsync();
        }

        using var changed = await change;
        Assert.True(changed.StatusCode == HttpStatusCode.OK, await OperationsFailure(changed));
        if (pause)
            CheckPause(beforePause!, await ReadPauseAuthority(h, platform!.User),
                await NumberHttpData<TenantManagementReceiptV1>(changed), suspend!);
        else
        {
            var policy = await ReadPolicyWrites(h, revoke!.OperationId);
            Assert.Equal(beforePolicy!.Generation + 1, policy.Generation);
            Assert.Equal(beforePolicy.Snapshot + 1, policy.Snapshot);
            Assert.Equal(beforePolicy.Outbox + 1, policy.Outbox);
            Assert.Equal(1, policy.Audits);
            Assert.Equal(1, policy.Receipts);
        }
        using var response = await post;
        var denied = pause ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
        Assert.True(response.StatusCode == (authorityFirst ? denied : HttpStatusCode.OK), await OperationsFailure(response));
        var committed = await ReadVoucherEffects(h, fixture, fixture.LineIds[0]);
        if (authorityFirst)
            Assert.Equal(before, committed);
        else
        {
            var rows = await VoucherResults(response);
            Assert.Equal(fixture.LineIds.Length, rows.Length);
            Assert.Equal(committed.Entry, rows[0].GetProperty("journalEntryId").GetGuid());
            await CheckPostedVoucher(h, fixture, committed);
            if (rows.Length == 2)
            {
                Assert.False(rows[1].GetProperty("ok").GetBoolean());
                Assert.Equal("REAUTH_REQUIRED", rows[1].GetProperty("code").GetString());
                var untouched = await ReadVoucherEffects(h, fixture, fixture.LineIds[1]);
                Assert.Equal(committed with { Entry = null, Group = null, Status = BankLineMatchStatus.Unmatched }, untouched);
            }
        }
        using var late = await Generate();
        Assert.Equal(denied, late.StatusCode);
        Assert.Equal(committed, await ReadVoucherEffects(h, fixture, fixture.LineIds[0]));

        Task<HttpResponseMessage> Change() => pause
            ? host.Send(OperationsRequest(manager, HttpMethod.Post, ManagementPath(h.Tenant, "/commands"), suspend))
            : host.Send(OperationsRequest(manager, HttpMethod.Put, PolicyWritePath, revoke));
        Task<HttpResponseMessage> Generate() => host.Send(BankVoucherRequest(writer, fixture, fixture.LineIds));
    }

    [CurrentPermissionPostgresFact]
    public async Task PGT017_bank_voucher_preserves_per_line_commits_and_failed_number()
    {
        await using var h = await Seed();
        var fixture = await SeedBankVoucher(h, lineCount: 2);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,
            IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(h.Tenant));
        var actor = await Login(host, h);
        using var response = await host.Send(BankVoucherRequest(actor, fixture, fixture.LineIds, missingCounter: true));
        Assert.True(response.StatusCode == HttpStatusCode.OK, await OperationsFailure(response));
        var failed = await VoucherResults(response);
        Assert.Equal(2, failed.Length);
        Assert.All(failed, row => Assert.False(row.GetProperty("ok").GetBoolean()));
        var before = await ReadVoucherEffects(h, fixture, fixture.LineIds[0]);
        Assert.Equal(0, before.Sequence);

        using var mixed = await host.Send(BankVoucherRequest(actor, fixture,
            [fixture.LineIds[0], Guid.NewGuid(), fixture.LineIds[1]]));
        Assert.True(mixed.StatusCode == HttpStatusCode.OK, await OperationsFailure(mixed));
        var rows = await VoucherResults(mixed);
        Assert.Equal(3, rows.Length);
        Assert.True(rows[0].GetProperty("ok").GetBoolean());
        Assert.Equal("E-A4-MATCH-004", rows[1].GetProperty("code").GetString());
        Assert.True(rows[2].GetProperty("ok").GetBoolean());
        var committed = await ReadVoucherEffects(h, fixture, fixture.LineIds[0]);
        Assert.Equal((2, 2, 2, 2, 2), (committed.Entries, committed.Groups, committed.Links, committed.Sequence, committed.Audits));
        using var duplicate = await host.Send(BankVoucherRequest(actor, fixture, fixture.LineIds));
        Assert.True(duplicate.StatusCode == HttpStatusCode.OK, await OperationsFailure(duplicate));
        Assert.All(await VoucherResults(duplicate), row => Assert.Equal("E-A4-BANKONLY-DUP", row.GetProperty("code").GetString()));
        Assert.Equal(committed, await ReadVoucherEffects(h, fixture, fixture.LineIds[0]));
    }

    private static async Task<BankVoucherFixture> SeedBankVoucher(Harness h, int lineCount = 1)
    {
        await GrantLegacyAuthorityAction(h, BankReconMenu, BankVoucherAction);
        var bank = new GlAccount { Id = Guid.NewGuid(), Code = "1002", Name = "fixture bank", IsLeaf = true, IsActive = true };
        var fee = new GlAccount { Id = Guid.NewGuid(), Code = "6603", Name = "fixture fee", IsLeaf = true, IsActive = true };
        var date = new DateTime(2026, 11, 13);
        var period = new FiscalPeriod { Id = Guid.NewGuid(), Year = 2026, Month = 11, FiscalYear = 2026, PeriodNo = 11,
            PeriodStart = new(2026, 11, 1), PeriodEnd = new(2026, 11, 30) };
        var account = new BankAccount { Id = Guid.NewGuid(), Code = "FIX-BK", Name = "fixture account",
            GlAccountId = bank.Id, IsActive = true };
        var statement = new BankStatement { Id = Guid.NewGuid(), No = "BK-" + h.Tenant.ToString("N")[..16],
            BankAccountId = account.Id, FiscalPeriodId = period.Id, PeriodStart = period.PeriodStart, PeriodEnd = period.PeriodEnd,
            Status = BankStatementStatus.Open };
        var lines = Enumerable.Range(1, lineCount).Select(i => new BankStatementLine { Id = Guid.NewGuid(),
            StatementId = statement.Id, LineNo = i, TxnDate = date, Direction = BankLineDirection.Withdrawal,
            Amount = BankVoucherAmount, Source = BankLineSource.Imported, Description = "fixture bank charge",
            Category = BankLineCategory.BankCharge }).ToArray();
        foreach (var line in lines) line.RecomputeSigned();
        h.Db.GlAccounts.AddRange(bank, fee);
        h.Db.FiscalPeriods.Add(period);
        h.Db.BankAccounts.Add(account);
        h.Db.BankStatements.Add(statement);
        h.Db.BankStatementLines.AddRange(lines);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        return new(statement.Id, statement.No, bank.Id, fee.Id, lines.Select(line => line.Id).ToArray());
    }

    private static async Task<JsonElement[]> VoucherResults(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").EnumerateArray().ToArray();

    private static Task<BankVoucherEffects> ReadVoucherEffects(Harness h, BankVoucherFixture fixture, Guid lineId)
        => h.Db.BankStatementLines.Where(line => line.Id == lineId)
            .Select(line => new BankVoucherEffects(line.GeneratedJournalEntryId, line.MatchGroupId, line.MatchStatus,
                h.Db.JournalEntries.Count(j => j.Source == VoucherSource.BankRecon),
                h.Db.BankReconMatches.Count(m => m.StatementId == fixture.Statement),
                h.Db.BankReconJournalLinks.Count(link => h.Db.BankReconMatches.Any(m => m.Id == link.MatchGroupId && m.StatementId == fixture.Statement)),
                h.Db.FinSequences.Sum(s => s.CurrentValue),
                h.Db.Sys_OperLogs.Count(log => log.Action == "GenerateBankVoucherLine"))).SingleAsync();

    private static async Task CheckPostedVoucher(Harness h, BankVoucherFixture fixture, BankVoucherEffects saved)
    {
        Assert.NotNull(saved.Entry);
        Assert.NotNull(saved.Group);
        Assert.Equal((BankLineMatchStatus.Matched, 1, 1, 1, 1, 1),
            (saved.Status, saved.Entries, saved.Groups, saved.Links, saved.Sequence, saved.Audits));
        var entry = await h.Db.JournalEntries.AsNoTracking().Include(x => x.Lines).SingleAsync(x => x.Id == saved.Entry);
        Assert.Equal(JournalStatus.Posted, entry.Status);
        Assert.Equal($"BKR-LINE-{fixture.LineIds[0]:N}", entry.SourceDocNo);
        Assert.Contains(fixture.Number, entry.Description);
        Assert.Contains(entry.Lines, line => line.AccountId == fixture.BankGl && line.Credit == BankVoucherAmount && line.Debit == 0);
        Assert.Contains(entry.Lines, line => line.AccountId == fixture.FeeGl && line.Debit == BankVoucherAmount && line.Credit == 0);
        var link = await h.Db.BankReconJournalLinks.AsNoTracking().SingleAsync(x => x.MatchGroupId == saved.Group);
        Assert.Equal((saved.Entry.Value, -BankVoucherAmount), (link.JournalEntryId, link.BankSignedAmount));
    }

    private static HttpRequestMessage BankVoucherRequest(Dictionary<string, string> actor, BankVoucherFixture fixture, Guid[] lines,
        bool missingCounter = false)
        => OperationsRequest(actor, HttpMethod.Post, $"/api/fin/bank-recon/{fixture.Statement:D}/generate-voucher",
            new { LineIds = lines, CounterAccountId = missingCounter ? Guid.NewGuid() : fixture.FeeGl });

    private sealed record BankVoucherFixture(Guid Statement, string Number, Guid BankGl, Guid FeeGl, Guid[] LineIds);
    private sealed record BankVoucherEffects(Guid? Entry, Guid? Group, BankLineMatchStatus Status, int Entries, int Groups,
        int Links, int Sequence, int Audits);
}
