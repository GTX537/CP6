using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.Fin;
using CP6.Core.Services.Platform;
using CP6.Entity.DomainModels;
using CP6.Entity.DomainModels.Fin;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private const string FiscalPeriodMenu = "fin-period";
    private const int FiscalFixtureYear = 2026;

    [TenantManagementPostgresTheory]
    [InlineData("close", true)]
    [InlineData("close", false)]
    [InlineData("reopen", true)]
    [InlineData("reopen", false)]
    [InlineData("year-close", true)]
    [InlineData("year-close", false)]
    [InlineData("reopen-year", true)]
    [InlineData("reopen-year", false)]
    public async Task PGT017_fiscal_period_write_and_pause_share_commit_order(string action, bool pauseFirst)
    {
        await using var h = await Seed();
        await using var admin = await TenantManagementSeed();
        await GrantLegacyAuthorityAction(h, FiscalPeriodMenu, action);
        var period = action switch
        {
            "close" => await SeedFiscalMonthAsync(h),
            "reopen" => await SeedFiscalMonthAsync(h, closed: true),
            _ => (await SeedFiscalYearAsync(h, yearClosed: action == "reopen-year")).LastPeriod
        };
        var connection = h.Db.Database.GetConnectionString()!;
        await using var host = await StartFiscalApiAsync(h);
        var actor = await Login(host, h);
        var manager = await Login(host, admin);
        using var read = await host.Send(OperationsRequest(manager, HttpMethod.Get, ManagementPath(h.Tenant)));
        var active = await NumberHttpData<TenantManagementSnapshotV1>(read);
        var suspend = new TenantManagementCommandV1("fiscal-period-pause", active.Revision, "suspend", null);
        var before = await FiscalPeriodWritesAsync(h);
        var authorityBefore = await ReadPauseAuthority(h, admin.User);

        await using var barrier = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await barrier.OpenAsync();
        await observer.OpenAsync();
        Task<HttpResponseMessage> pause;
        Task<HttpResponseMessage> write;
        FenceWait first;
        FenceWait second;
        await using (var hold = await barrier.BeginTransactionAsync())
        {
            using var lockTable = new NpgsqlCommand(pauseFirst
                ? """LOCK TABLE public."Sys_Tenants" IN SHARE MODE"""
                : """LOCK TABLE public."Fin_FiscalPeriod" IN SHARE MODE""", barrier, hold);
            await lockTable.ExecuteNonQueryAsync();
            if (pauseFirst)
            {
                pause = Pause();
                first = await WaitForFence(observer, barrier.ProcessID, "%UPDATE%Sys_Tenants%", null, pause);
                write = Write();
                second = await WaitForFence(observer, first.Backend, "%cp6_authentication_v1.acquire()%", null, write);
                if (action == "close")
                {
                    using var precheck = await host.Send(OperationsRequest(actor, HttpMethod.Get,
                        $"api/fin/period/{period:D}/pre-close-check"));
                    Assert.True(precheck.StatusCode == HttpStatusCode.OK, await OperationsFailure(precheck));
                }
            }
            else
            {
                write = Write();
                // The FX reversal opens the missing next month before the current month's final UPDATE.
                var command = action == "close" ? "%INSERT%Fin_FiscalPeriod%" : "%UPDATE%Fin_FiscalPeriod%";
                first = await WaitForFence(observer, barrier.ProcessID, command, null, write);
                pause = Pause();
                second = await WaitForFence(observer, first.Backend, "%cp6_authentication_v1.acquire()%", null, pause);
            }
            Assert.Equal(before, await FiscalPeriodWritesAsync(h));
            Assert.Equal(authorityBefore, await ReadPauseAuthority(h, admin.User));
            await hold.RollbackAsync();
        }

        using var paused = await pause;
        using var result = await write;
        var receipt = await NumberHttpData<TenantManagementReceiptV1>(paused);
        CheckPause(authorityBefore, await ReadPauseAuthority(h, admin.User), receipt, suspend);
        Assert.True(result.StatusCode == (pauseFirst ? HttpStatusCode.Unauthorized : HttpStatusCode.OK),
            await OperationsFailure(result));
        if (pauseFirst)
        {
            Assert.Equal(before, await FiscalPeriodWritesAsync(h));
            var reactivate = new TenantManagementCommandV1("fiscal-period-resume", receipt.Snapshot.Revision, "reactivate", null);
            using var resumed = await host.Send(OperationsRequest(manager, HttpMethod.Post, ManagementPath(h.Tenant, "/commands"), reactivate));
            Assert.True(resumed.StatusCode == HttpStatusCode.OK, await OperationsFailure(resumed));
            actor = await Login(host, h);
            using var current = await Write();
            Assert.True(current.StatusCode == HttpStatusCode.OK, await OperationsFailure(current));
        }
        else
        {
            var committed = await FiscalPeriodWritesAsync(h);
            using var stale = await Write();
            Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode);
            Assert.Equal(committed, await FiscalPeriodWritesAsync(h));
        }

        Assert.NotEqual(before, await FiscalPeriodWritesAsync(h));
        await AssertFiscalPeriodEffectAsync(h, action, period);
        output.WriteLine(JsonSerializer.Serialize(new { Action = action, PauseFirst = pauseFirst,
            FirstWait = first, RootWait = second, Status = (int)result.StatusCode }));

        Task<HttpResponseMessage> Write() => host.Send(OperationsRequest(actor, HttpMethod.Post, FiscalPeriodRoute(action, period)));
        Task<HttpResponseMessage> Pause() => host.Send(OperationsRequest(manager, HttpMethod.Post,
            ManagementPath(h.Tenant, "/commands"), suspend));
    }

    [TenantManagementPostgresTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PGT017_fiscal_period_year_close_and_role_revocation_share_commit_order(bool revokeFirst)
    {
        await using var h = await Seed();
        await SeedFenceAdministrator(h);
        await GrantLegacyAuthorityAction(h, FiscalPeriodMenu, "year-close");
        var fixture = await SeedFiscalYearAsync(h);
        var connection = h.Db.Database.GetConnectionString()!;
        await using var host = await StartFiscalApiAsync(h);
        var actor = await Login(host, h);
        var manager = await Login(host, "fence-manager", "Fence1!Password", h.Tenant.ToString("N"));
        var revoke = await RevokeActions(host, manager, "year-close");
        var before = await FiscalPeriodWritesAsync(h);
        var authorityBefore = await ReadPolicyWrites(h, revoke.OperationId);

        await using var barrier = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await barrier.OpenAsync();
        await observer.OpenAsync();
        Task<HttpResponseMessage> change;
        Task<HttpResponseMessage> write;
        FenceWait first;
        FenceWait second;
        await using (var hold = await barrier.BeginTransactionAsync())
        {
            using var lockTable = new NpgsqlCommand(revokeFirst
                ? """LOCK TABLE public."Sys_RoleAction" IN SHARE MODE"""
                : """LOCK TABLE public."Fin_FiscalPeriod" IN SHARE MODE""", barrier, hold);
            await lockTable.ExecuteNonQueryAsync();
            if (revokeFirst)
            {
                change = Revoke();
                first = await WaitForFence(observer, barrier.ProcessID, "%DELETE%Sys_RoleAction%", null, change);
                write = Write();
                second = await WaitForFence(observer, first.Backend, "%cp6_authentication_v1.acquire()%", null, write);
            }
            else
            {
                write = Write();
                first = await WaitForFence(observer, barrier.ProcessID, "%UPDATE%Fin_FiscalPeriod%", null, write);
                change = Revoke();
                second = await WaitForFence(observer, first.Backend, "%cp6_authentication_v1.acquire()%", null, change);
            }
            Assert.Equal(before, await FiscalPeriodWritesAsync(h));
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
        Assert.DoesNotContain("year-close", policy.Actions);

        using var result = await write;
        if (revokeFirst)
        {
            Assert.Equal(HttpStatusCode.Forbidden, result.StatusCode);
            Assert.Equal(before, await FiscalPeriodWritesAsync(h));
            await GrantLegacyAuthorityAction(h, FiscalPeriodMenu, "year-close");
            using var restored = await Write();
            Assert.True(restored.StatusCode == HttpStatusCode.OK, await OperationsFailure(restored));
        }
        else
        {
            Assert.True(result.StatusCode == HttpStatusCode.OK, await OperationsFailure(result));
            var committed = await FiscalPeriodWritesAsync(h);
            using var stale = await Write();
            Assert.Equal(HttpStatusCode.Forbidden, stale.StatusCode);
            Assert.Equal(committed, await FiscalPeriodWritesAsync(h));
        }
        await AssertFiscalPeriodEffectAsync(h, "year-close", fixture.LastPeriod);
        output.WriteLine(JsonSerializer.Serialize(new { RevokeFirst = revokeFirst,
            FirstWait = first, RootWait = second, Status = (int)result.StatusCode }));

        Task<HttpResponseMessage> Write() => host.Send(OperationsRequest(actor, HttpMethod.Post, FiscalPeriodRoute("year-close")));
        Task<HttpResponseMessage> Revoke() => host.Send(OperationsRequest(manager, HttpMethod.Put, PolicyWritePath, revoke));
    }

    [TenantManagementPostgresTheory]
    [InlineData("close", "E-FIN-112")]
    [InlineData("year-close", "E-FIN-104")]
    public async Task PGT017_fiscal_period_rejection_rolls_back_the_full_write_set(string action, string code)
    {
        await using var h = await Seed();
        await GrantLegacyAuthorityAction(h, FiscalPeriodMenu, action);
        var monthly = action == "close";
        var period = monthly
            ? await SeedFiscalMonthAsync(h, nextClosed: true)
            : (await SeedFiscalYearAsync(h, retainedLeaf: false)).LastPeriod;
        var before = await FiscalPeriodWritesAsync(h);
        await using var host = await StartFiscalApiAsync(h);
        var actor = await Login(host, h);
        using var rejected = await Write();
        Assert.True(rejected.StatusCode == HttpStatusCode.BadRequest, await OperationsFailure(rejected));
        var reply = await rejected.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, reply.GetProperty("message").GetString());
        if (!monthly)
            Assert.Equal("3104", reply.GetProperty("args")[0].GetString());
        Assert.Equal(before, await FiscalPeriodWritesAsync(h));

        if (monthly)
            Assert.Equal(1, await h.Db.FiscalPeriods.Where(p => p.Year == FiscalFixtureYear && p.Month == 7)
                .ExecuteUpdateAsync(set => set.SetProperty(p => p.Status, PeriodStatus.Open)));
        else
            Assert.Equal(1, await h.Db.GlAccounts.Where(a => a.Role == "RETAINED_EARNINGS")
                .ExecuteUpdateAsync(set => set.SetProperty(a => a.IsLeaf, true)));
        using var allowed = await Write();
        Assert.True(allowed.StatusCode == HttpStatusCode.OK, await OperationsFailure(allowed));
        await AssertFiscalPeriodEffectAsync(h, action, period);
        var scope = monthly ? "2026-06" : "2026-12";
        Assert.Equal(2, await h.Db.FinSequences.Where(s => s.SeqKey == "GL" && s.SeqDate == scope)
            .Select(s => s.CurrentValue).SingleAsync());

        Task<HttpResponseMessage> Write() => host.Send(OperationsRequest(actor, HttpMethod.Post, FiscalPeriodRoute(action, period)));
    }

    [CurrentPermissionPostgresFact]
    public async Task PGT017_fiscal_period_year_close_completes_a_previously_committed_v1()
    {
        await using var h = await Seed();
        await GrantLegacyAuthorityAction(h, FiscalPeriodMenu, "year-close");
        var fixture = await SeedFiscalYearAsync(h, historicalV1: true);
        var original = Assert.Single(await FiscalCarryoversAsync(h));
        Assert.Equal("YC-2026", original.SourceDocNo);

        await using var host = await StartFiscalApiAsync(h);
        var actor = await Login(host, h);
        using var response = await host.Send(OperationsRequest(actor, HttpMethod.Post, FiscalPeriodRoute("year-close")));
        Assert.True(response.StatusCode == HttpStatusCode.OK, await OperationsFailure(response));
        await AssertFiscalPeriodEffectAsync(h, "year-close", fixture.LastPeriod);
        var carryovers = await FiscalCarryoversAsync(h);
        var retained = carryovers.Single(entry => entry.SourceDocNo == "YC-2026");
        Assert.Equal(original.Id, retained.Id);
        Assert.Equal(original.No, retained.No);
        Assert.Equal(original.Status, retained.Status);
        await AssertFiscalBalancesAsync(h, fixture, yearClosed: true);
    }

    [CurrentPermissionPostgresFact]
    public async Task PGT017_fiscal_period_reclose_preserves_posted_history_and_recovers_balances()
    {
        await using var h = await Seed();
        await GrantLegacyAuthorityAction(h, FiscalPeriodMenu, "year-close");
        await GrantLegacyAuthorityAction(h, FiscalPeriodMenu, "reopen-year");
        var fixture = await SeedFiscalYearAsync(h);
        await using var host = await StartFiscalApiAsync(h);
        var actor = await Login(host, h);

        await Run("year-close", yearClosed: true);
        var original = await FiscalCarryoversAsync(h);
        Assert.Equal(new[] { "YC-2026", "YC-2026-RE" }, original.Select(row => row.SourceDocNo));
        await Run("reopen-year", yearClosed: false);
        await Run("year-close", yearClosed: true);
        await Run("reopen-year", yearClosed: false);

        var history = await FiscalCarryoversAsync(h);
        Assert.Equal(6, history.Count);
        Assert.Equal(6, history.Select(row => row.SourceDocNo).Distinct().Count());
        Assert.All(history, row => Assert.Equal(JournalStatus.Posted, row.Status));
        Assert.Contains(history, row => row.SourceDocNo == "YC-2026-REOPEN");
        foreach (var row in original)
            Assert.Contains(history, current => current.Id == row.Id && current.No == row.No && current.SourceDocNo == row.SourceDocNo);

        async Task Run(string action, bool yearClosed)
        {
            using var response = await host.Send(OperationsRequest(actor, HttpMethod.Post, FiscalPeriodRoute(action)));
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{action}: {await OperationsFailure(response)}");
            await AssertFiscalBalancesAsync(h, fixture, yearClosed);
        }
    }

    private static async Task<Guid> SeedFiscalMonthAsync(Harness h, bool closed = false, bool nextClosed = false)
    {
        var db = h.Db;
        var gl = new GlAccountService(db);
        await gl.ImportTemplateAsync(FinCoaTemplate.CnGaap, "fixture");
        var periods = new FiscalPeriodService(db, 1);
        var may = await periods.EnsureOpenAsync(new DateTime(FiscalFixtureYear, 5, 1), "fixture");
        var june = await periods.EnsureOpenAsync(new DateTime(FiscalFixtureYear, 6, 1), "fixture");
        may.Status = PeriodStatus.Closed;
        if (closed)
        {
            june.Status = PeriodStatus.Closed;
            june.ClosedBy = "fixture";
            june.ClosedAt = new DateTime(FiscalFixtureYear, 6, 30);
        }
        if (nextClosed)
        {
            var july = await periods.EnsureOpenAsync(new DateTime(FiscalFixtureYear, 7, 1), "fixture");
            july.Status = PeriodStatus.Closed;
        }
        var category = new AssetCategory
        {
            Id = Guid.NewGuid(), Code = "MC", Name = "Fixture machinery", DefaultMethod = DepreciationMethod.StraightLine,
            DefaultUsefulLifeMonths = 12, AssetAccountId = (await gl.GetByCodeAsync("1601"))!.Id,
            AccumDeprecAccountId = (await gl.GetByCodeAsync("1602"))!.Id, DeprecExpenseAccountId = (await gl.GetByCodeAsync("5101.01"))!.Id
        };
        db.AssetCategories.Add(category);
        db.AssetCards.Add(new()
        {
            AssetNo = "FA-PERIOD", Name = "Fixture asset", CategoryId = category.Id, OriginalValue = 12000m,
            Method = DepreciationMethod.StraightLine, UsefulLifeMonths = 12,
            AcquisitionDate = new DateTime(FiscalFixtureYear, 4, 15), DepreciationStartPeriod = "2026-05", Status = AssetStatus.InUse
        });
        db.FxRates.Add(new FxRate { CurrencyCd = "USD", RateDate = new DateTime(FiscalFixtureYear, 6, 1), Rate = 6.6m });
        db.ApInvoices.Add(new()
        {
            No = "AP-PERIOD", SupplierId = "SUP1", InvoiceDate = new DateTime(FiscalFixtureYear, 6, 10),
            DueDate = new DateTime(FiscalFixtureYear, 7, 10), CurrencyCd = "USD", FxRate = 7m,
            GrossAmount = 5000m, Status = ApInvoiceStatus.Posted
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return june.Id;
    }

    private sealed record FiscalYearFixture(Guid LastPeriod, Guid Revenue, Guid Expense, Guid Profit, Guid Retained);

    private static async Task<FiscalYearFixture> SeedFiscalYearAsync(Harness h, bool yearClosed = false,
        bool retainedLeaf = true, bool historicalV1 = false)
    {
        var db = h.Db;
        var gl = new GlAccountService(db);
        await gl.ImportTemplateAsync(FinCoaTemplate.CnGaap, "fixture");
        var bank = (await gl.GetByCodeAsync("1002"))!.Id;
        var revenue = (await gl.GetByCodeAsync("4001"))!.Id;
        var expense = (await gl.GetByCodeAsync("5001"))!.Id;
        var profit = (await gl.GetByCodeAsync("3103"))!.Id;
        var retained = (await gl.GetByCodeAsync("3104"))!;
        retained.IsLeaf = retainedLeaf;
        await db.SaveChangesAsync();

        var periods = new FiscalPeriodService(db, 1);
        var journal = new JournalEntryService(db, periods, new FinSequenceService(db));
        await Post(1, "PERIOD-REVENUE", VoucherSource.Inventory,
            new() { AccountId = bank, Debit = 1000m }, new() { AccountId = revenue, Credit = 1000m });
        await Post(2, "PERIOD-EXPENSE", VoucherSource.Inventory,
            new() { AccountId = expense, Debit = 600m }, new() { AccountId = bank, Credit = 600m });
        if (historicalV1)
            await Post(12, "YC-2026", VoucherSource.Carryover, new() { AccountId = revenue, Debit = 1000m },
                new() { AccountId = expense, Credit = 600m }, new() { AccountId = profit, Credit = 400m });

        var months = new List<FiscalPeriod>(12);
        for (var month = 1; month <= 12; month++)
            months.Add(await periods.EnsureOpenAsync(FiscalFixtureYear, month, "fixture"));
        foreach (var row in months)
            row.Status = PeriodStatus.Closed;
        await db.SaveChangesAsync();
        if (yearClosed)
        {
            var result = await new PeriodCloseService(db, periods, new TrialBalanceService(db), journal: journal)
                .YearCloseAsync(FiscalFixtureYear, "fixture");
            Assert.True(result.Ok, result.Code);
        }
        db.ChangeTracker.Clear();
        return new(months[^1].Id, revenue, expense, profit, retained.Id);

        async Task Post(int month, string sourceNo, VoucherSource source, params JournalLine[] lines)
        {
            var date = new DateTime(FiscalFixtureYear, month, 15);
            var result = await journal.AutoPostAsync(new()
            {
                VoucherDate = date, Source = source, SourceDocNo = sourceNo, Description = "Fixture voucher", Lines = lines.ToList()
            });
            Assert.True(result.Ok, result.Code);
        }
    }

    private static Task<AuthenticationApiProcess> StartFiscalApiAsync(Harness h) =>
        AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(h.Tenant));

    private static string FiscalPeriodRoute(string action, Guid period = default) =>
        action is "close" or "reopen" ? $"api/fin/period/{period:D}/{action}" : $"api/fin/period/{action}/{FiscalFixtureYear}";

    private static Task<List<JournalEntry>> FiscalCarryoversAsync(Harness h) => h.Db.JournalEntries.AsNoTracking()
        .Where(e => e.Source == VoucherSource.Carryover).OrderBy(e => e.No).ToListAsync();

    private static async Task AssertFiscalPeriodEffectAsync(Harness h, string action, Guid period)
    {
        var db = h.Db;
        if (action is "close" or "reopen")
        {
            var month = await db.FiscalPeriods.AsNoTracking().SingleAsync(p => p.Id == period);
            if (action == "reopen")
            {
                Assert.Equal(PeriodStatus.Open, month.Status);
                Assert.Null(month.ClosedAt);
                Assert.Null(month.ClosedBy);
                return;
            }
            Assert.Equal(PeriodStatus.Closed, month.Status);
            Assert.Equal("price-owner", month.ClosedBy);
            Assert.NotNull(month.ClosedAt);
            Assert.Equal(PeriodStatus.Open, await db.FiscalPeriods.Where(p => p.Year == FiscalFixtureYear && p.Month == 7)
                .Select(p => p.Status).SingleAsync());
            var run = await db.DepreciationRuns.AsNoTracking().SingleAsync();
            Assert.Equal(DepreciationRunStatus.Posted, run.Status);
            Assert.Equal(run.Id, await db.DepreciationEntries.Select(e => e.RunId).SingleAsync());
            Assert.Equal(1000m, await db.AssetCards.Select(c => c.AccumulatedDepreciation).SingleAsync());
            Assert.Equal(2, await db.JournalEntries.CountAsync(e => e.Source == VoucherSource.FxReval && e.Status == JournalStatus.Posted));
            return;
        }

        var year = await db.FiscalPeriods.AsNoTracking().Where(p => p.FiscalYear == FiscalFixtureYear).ToListAsync();
        Assert.Equal(12, year.Count);
        Assert.All(year, row => Assert.Equal(action == "year-close" ? PeriodStatus.YearClosed : PeriodStatus.Closed, row.Status));
        Assert.Equal("price-owner", year.Single(row => row.Id == period).Modifier);
        var carryovers = await FiscalCarryoversAsync(h);
        Assert.Equal(action == "year-close" ? 2 : 3, carryovers.Count);
        Assert.All(carryovers, row => Assert.Equal(JournalStatus.Posted, row.Status));
    }

    private static async Task AssertFiscalBalancesAsync(Harness h, FiscalYearFixture fixture, bool yearClosed)
    {
        var rows = await h.Db.FiscalPeriods.Where(p => p.FiscalYear == FiscalFixtureYear).Select(p => p.Status).ToListAsync();
        Assert.Equal(12, rows.Count);
        Assert.All(rows, status => Assert.Equal(yearClosed ? PeriodStatus.YearClosed : PeriodStatus.Closed, status));
        Assert.Equal(yearClosed ? 0m : -1000m, await Net(fixture.Revenue));
        Assert.Equal(yearClosed ? 0m : 600m, await Net(fixture.Expense));
        Assert.Equal(0m, await Net(fixture.Profit));
        Assert.Equal(yearClosed ? -400m : 0m, await Net(fixture.Retained));

        Task<decimal> Net(Guid account) =>
            (from line in h.Db.JournalLines
             join entry in h.Db.JournalEntries on line.EntryId equals entry.Id
             where entry.Status == JournalStatus.Posted && line.AccountId == account
             select line.Debit - line.Credit).SumAsync();
    }

    private static Task<string> FiscalPeriodWritesAsync(Harness h) => h.Db.Database.SqlQuery<string>($"""
        SELECT jsonb_build_array(
           (SELECT jsonb_agg(to_jsonb(p) ORDER BY p."Id") FROM public."Fin_FiscalPeriod" p WHERE p."TenantId"={h.Tenant}),
           (SELECT jsonb_agg(to_jsonb(s) ORDER BY s."Id") FROM public."Fin_Sequence" s WHERE s."TenantId"={h.Tenant}),
           (SELECT jsonb_agg(to_jsonb(e) ORDER BY e."Id") FROM public."Fin_JournalEntry" e WHERE e."TenantId"={h.Tenant}),
           (SELECT jsonb_agg(to_jsonb(l) ORDER BY l."Id") FROM public."Fin_JournalLine" l WHERE l."TenantId"={h.Tenant}),
           (SELECT jsonb_agg(to_jsonb(r) ORDER BY r."Id") FROM public."Fin_DepreciationRun" r WHERE r."TenantId"={h.Tenant}),
           (SELECT jsonb_agg(to_jsonb(d) ORDER BY d."Id") FROM public."Fin_DepreciationEntry" d WHERE d."TenantId"={h.Tenant}),
           (SELECT jsonb_agg(to_jsonb(a) ORDER BY a."Id") FROM public."Fin_AssetCard" a WHERE a."TenantId"={h.Tenant})
        )::text AS "Value"
        """).SingleAsync();
}
