using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Fin;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace CP6.Tests.DatabaseCompatibility;

[Collection(CoreBusinessRelationalCollection.Name)]
public sealed class FinanceRelationalTests(CoreBusinessRelationalFixture fixture, ITestOutputHelper output)
{
    // Retains the native SQL test's complete cross-line stale-write, fresh retry and stale-delete assertions.
    [CoreBusinessFact]
    public async Task Stale_version_token_rejects_second_writer_on_a_different_line()
    {
        var seed = await SeedBudgetAsync();
        await using var firstWriter = fixture.CreateContext(seed.Tenant);
        await using var secondWriter = fixture.CreateContext(seed.Tenant);
        var firstToken = (await firstWriter.BudgetVersions.AsNoTracking().SingleAsync(v => v.Id == seed.Version)).RowVersion!;
        var staleSecondToken = (await secondWriter.BudgetVersions.AsNoTracking().SingleAsync(v => v.Id == seed.Version)).RowVersion!;
        Assert.Equal(8, firstToken.Length);
        Assert.Equal(firstToken, staleSecondToken);

        var firstResult = await new BudgetLineService(firstWriter).UpsertLineAsync(new()
        {
            VersionId = seed.Version, AccountId = seed.FirstAccount, AnnualAmount = 1200m,
            SpreadMode = "even", VersionRowVersion = firstToken
        });
        Assert.True(firstResult.Ok, firstResult.Code);
        var staleResult = await new BudgetLineService(secondWriter).UpsertLineAsync(new()
        {
            VersionId = seed.Version, AccountId = seed.SecondAccount, AnnualAmount = 600m,
            SpreadMode = "even", VersionRowVersion = staleSecondToken
        });
        Assert.False(staleResult.Ok);
        Assert.Equal("E-A5-CONCURRENCY-001", staleResult.Code);
        await using (var assertion = fixture.CreateContext(seed.Tenant))
        {
            var lines = await assertion.BudgetLines.AsNoTracking().Where(l => l.VersionId == seed.Version).ToListAsync();
            Assert.Single(lines);
            Assert.Equal(seed.FirstAccount, lines[0].AccountId);
            Assert.Equal(12, await assertion.BudgetLinePeriods.CountAsync());
        }

        await using var retryWriter = fixture.CreateContext(seed.Tenant);
        var freshToken = (await retryWriter.BudgetVersions.AsNoTracking().SingleAsync(v => v.Id == seed.Version)).RowVersion!;
        Assert.NotEqual(staleSecondToken, freshToken);
        var retryResult = await new BudgetLineService(retryWriter).UpsertLineAsync(new()
        {
            VersionId = seed.Version, AccountId = seed.SecondAccount, AnnualAmount = 600m,
            SpreadMode = "even", VersionRowVersion = freshToken
        });
        Assert.True(retryResult.Ok, retryResult.Code);

        await using var deleteSnapshot = fixture.CreateContext(seed.Tenant);
        var deleteVersionToken = (await deleteSnapshot.BudgetVersions.AsNoTracking().SingleAsync(v => v.Id == seed.Version)).RowVersion!;
        var firstLine = await deleteSnapshot.BudgetLines.AsNoTracking()
            .SingleAsync(l => l.VersionId == seed.Version && l.AccountId == seed.FirstAccount);
        await using var competingWriter = fixture.CreateContext(seed.Tenant);
        var competingVersionToken = (await competingWriter.BudgetVersions.AsNoTracking().SingleAsync(v => v.Id == seed.Version)).RowVersion!;
        var secondLine = await competingWriter.BudgetLines.AsNoTracking()
            .SingleAsync(l => l.VersionId == seed.Version && l.AccountId == seed.SecondAccount);
        var competingResult = await new BudgetLineService(competingWriter).UpsertLineAsync(new()
        {
            VersionId = seed.Version, AccountId = seed.SecondAccount, AnnualAmount = 700m,
            SpreadMode = "even", RowVersion = secondLine.RowVersion, VersionRowVersion = competingVersionToken
        });
        Assert.True(competingResult.Ok, competingResult.Code);
        await using var staleDeleteWriter = fixture.CreateContext(seed.Tenant);
        var staleDelete = await new BudgetLineService(staleDeleteWriter).DeleteLineAsync(
            firstLine.Id, firstLine.RowVersion, deleteVersionToken);
        Assert.False(staleDelete.Ok);
        Assert.Equal("E-A5-CONCURRENCY-001", staleDelete.Code);
        await using var deleteAssertion = fixture.CreateContext(seed.Tenant);
        Assert.True(await deleteAssertion.BudgetLines.AsNoTracking().AnyAsync(l => l.Id == firstLine.Id));
        Assert.Equal(24, await deleteAssertion.BudgetLinePeriods.CountAsync());
        Assert.Equal(1900m, await deleteAssertion.BudgetLinePeriods.SumAsync(p => p.Amount));
    }

    [CoreBusinessFact]
    public async Task Decimal_annual_amount_and_last_period_remainder_round_trip_through_native_storage()
    {
        var seed = await SeedBudgetAsync();
        await using (var db = fixture.CreateContext(seed.Tenant))
        {
            var token = (await db.BudgetVersions.SingleAsync(v => v.Id == seed.Version)).RowVersion;
            var result = await new BudgetLineService(db).UpsertLineAsync(new()
            {
                VersionId = seed.Version, AccountId = seed.FirstAccount, AnnualAmount = 1000.01m,
                SpreadMode = "even", VersionRowVersion = token
            });
            Assert.True(result.Ok, result.Code);
        }
        await using var fresh = fixture.CreateContext(seed.Tenant);
        var line = await fresh.BudgetLines.AsNoTracking().SingleAsync(l => l.VersionId == seed.Version);
        var periods = await fresh.BudgetLinePeriods.AsNoTracking().Where(p => p.BudgetLineId == line.Id)
            .OrderBy(p => p.PeriodNo).ToArrayAsync();
        Assert.Equal(1000.01m, line.AnnualAmount);
        Assert.Equal(12, periods.Length);
        Assert.All(periods.Take(11), p => Assert.Equal(83.33m, p.Amount));
        Assert.Equal(83.38m, periods[11].Amount);
        Assert.Equal(1000.01m, await fresh.BudgetLinePeriods.Where(p => p.BudgetLineId == line.Id).SumAsync(p => p.Amount));
        Assert.Equal(Guid.Empty, line.CostCenterKey);
        Assert.Equal("", line.CostObjectTypeKey);
        Assert.Equal("", line.CostObjectIdKey);
    }

    [CoreBusinessFact]
    public Task PostAsync_BlockExceeded_Rejected() => CheckPostingAsync(500.01m, shouldPost: false);

    [CoreBusinessFact]
    public Task PostAsync_WithinBudget_Posts() => CheckPostingAsync(80.01m, shouldPost: true);

    private async Task CheckPostingAsync(decimal amount, bool shouldPost)
    {
        var seed = await SeedBudgetAsync(active: true);
        Guid entryId;
        await using (var db = fixture.CreateContext(seed.Tenant))
        {
            entryId = await CreatePendingAsync(db, seed, "maker", amount);
            var result = await Journal(db).PostAsync(entryId, "checker");
            Assert.Equal(shouldPost, result.Ok);
            if (!shouldPost) Assert.Equal("E-A5-BUDGET-EXCEEDED", result.Code);
        }
        await using var fresh = fixture.CreateContext(seed.Tenant);
        var entry = await fresh.JournalEntries.AsNoTracking().Include(e => e.Lines).SingleAsync(e => e.Id == entryId);
        Assert.Equal(shouldPost ? JournalStatus.Posted : JournalStatus.PendingReview, entry.Status);
        Assert.Equal(shouldPost ? "checker" : null, entry.CheckerId);
        Assert.Equal(2, entry.Lines.Count);
        Assert.Equal(amount, entry.Lines.Sum(l => l.Debit));
        Assert.Equal(amount, entry.Lines.Sum(l => l.Credit));
        Assert.Equal(1200.12m, await fresh.BudgetLinePeriods.SumAsync(p => p.Amount));
    }

    [CoreBusinessFact]
    public async Task SelfApproval_MakerEqualsChecker_RollsBack_workflow_task_and_voucher()
    {
        var seed = await SeedBudgetAsync(active: true);
        var approver = Guid.NewGuid();
        var starter = Guid.NewGuid();
        var bindingId = Guid.NewGuid();
        var flowKey = "wp4-fin-" + Guid.NewGuid().ToString("N");
        Guid entryId = default, instanceId = default, taskId = default;
        Guid[] historyBefore = [];
        byte[] instanceToken = [];
        var bindingSaved = false;
        try
        {
            await using (var db = fixture.CreateContext(seed.Tenant))
            {
                db.Sys_Users.AddRange(
                    new Sys_User { Id = approver, UserName = "wp4-" + approver.ToString("N"), Password = "test-only" },
                    new Sys_User { Id = starter, UserName = "wp4-" + starter.ToString("N"), Password = "test-only" });
                var schema = new FlowSchema
                {
                    Nodes =
                    {
                        new FlowNode { Id = "approve", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = approver },
                        new FlowNode { Id = "end", Type = "end" }
                    },
                    Edges = { new FlowEdge { From = "approve", To = "end" } }
                };
                var head = new Wf_FlowDef
                {
                    Id = Guid.NewGuid(), FlowKey = flowKey, FlowName = "WP4 journal approval", FormKey = "FinJournalPost",
                    SchemaJson = JsonSerializer.Serialize(schema), Version = 1, Enable = true
                };
                db.Wf_FlowDefs.Add(head);
                db.Wf_FlowDefVersions.Add(new()
                {
                    Id = Guid.NewGuid(), FlowDefId = head.Id, Version = 1, Status = WfDefinitionVersionStatus.Published,
                    FlowNameSnapshot = head.FlowName, SchemaJson = head.SchemaJson
                });
                db.Wf_ApprovalBindings.Add(new()
                {
                    Id = bindingId, BizType = "FinJournalPost", FlowKey = flowKey, Enable = true
                });
                await db.SaveChangesAsync();
                bindingSaved = true;
                entryId = await CreatePendingAsync(db, seed, approver.ToString(), 80.01m);
                var journal = Journal(db);
                var dispatcher = new ApprovalDispatcher([new JournalApprovalCallback(journal)]);
                var engine = new FlowEngine(db, new ApproverResolver(db), notifier: null, dispatcher: dispatcher);
                await new ApprovalService(db, engine).SubmitAsync("FinJournalPost", entryId.ToString(), starter);
                var task = await db.Wf_FlowTasks.SingleAsync(t => t.Status == FlowTaskStatus.Pending);
                taskId = task.Id;
                instanceId = task.InstanceId;
                instanceToken = (await db.Wf_FlowInstances.AsNoTracking().SingleAsync(i => i.Id == instanceId)).RowVersion!;
                historyBefore = await db.Wf_FlowHistories.Where(h => h.InstanceId == instanceId).Select(h => h.Id).ToArrayAsync();
                var error = await Assert.ThrowsAsync<InvalidOperationException>(() => engine.ActAsync(taskId, approver, approve: true));
                Assert.Contains("E-FIN-111", error.Message, StringComparison.Ordinal);
            }
            await using var fresh = fixture.CreateContext(seed.Tenant);
            var entry = await fresh.JournalEntries.SingleAsync(e => e.Id == entryId);
            Assert.Equal(JournalStatus.PendingReview, entry.Status);
            Assert.Null(entry.CheckerId);
            var instance = await fresh.Wf_FlowInstances.SingleAsync(i => i.Id == instanceId);
            Assert.Equal(FlowInstanceStatus.Running, instance.Status);
            Assert.Equal(instanceToken, instance.RowVersion);
            Assert.Equal(FlowTaskStatus.Pending, (await fresh.Wf_FlowTasks.SingleAsync(t => t.Id == taskId)).Status);
            Assert.Equal(1, await fresh.Wf_FlowTasks.CountAsync(t => t.Status == FlowTaskStatus.Pending));
            Assert.Equal(historyBefore.Order().ToArray(),
                (await fresh.Wf_FlowHistories.Where(h => h.InstanceId == instanceId).Select(h => h.Id).ToArrayAsync()).Order().ToArray());
        }
        finally
        {
            // Remove only this case's approval registration; keep its tenant's financial/workflow evidence.
            if (bindingSaved)
            {
                await using var cleanup = fixture.CreateContext(seed.Tenant);
                await cleanup.Wf_ApprovalBindings.Where(b => b.Id == bindingId).ExecuteDeleteAsync();
            }
        }
    }

    private async Task<BudgetSeed> SeedBudgetAsync(bool active = false)
    {
        output.WriteLine(fixture.SetupSummary);
        var tenant = Guid.NewGuid();
        await using var db = fixture.CreateContext(tenant);
        db.Sys_Tenants.Add(new() { Id = tenant, TenantCode = "wp4-fin-" + tenant.ToString("N"), TenantName = "WP4 finance", Enable = true });
        // The final Core model prefixes these financial unique indexes with TenantId.
        const int year = 2027;
        var first = Account(AccountType.Expense);
        var second = Account(active ? AccountType.Asset : AccountType.Expense);
        var budget = new Budget { Id = Guid.NewGuid(), No = "WP4-" + Guid.NewGuid().ToString("N")[..20], Name = "WP4 budget", FiscalYear = year, IsActive = true };
        var version = new BudgetVersion
        {
            BudgetId = budget.Id, VersionNo = 1, Name = "WP4 version", IsActive = active,
            Status = active ? BudgetVersionStatus.Approved : BudgetVersionStatus.Draft,
            DefaultControlMode = BudgetControlMode.Block, DefaultControlBasis = BudgetControlBasis.Period
        };
        db.AddRange(first, second, budget, version);
        await db.SaveChangesAsync();
        if (active)
        {
            var line = new BudgetLine { VersionId = version.Id, AccountId = first.Id, AnnualAmount = 1200.12m };
            line.NormalizeKeys();
            db.BudgetLines.Add(line);
            await db.SaveChangesAsync();
            for (var period = 1; period <= 12; period++)
                db.BudgetLinePeriods.Add(new() { BudgetLineId = line.Id, PeriodNo = period, Amount = 100.01m });
            await db.SaveChangesAsync();
        }
        return new(tenant, version.Id, first.Id, second.Id, year);
    }

    private static GlAccount Account(AccountType type) => new()
    {
        Code = "WP4" + Guid.NewGuid().ToString("N")[..20], Name = "WP4 account", Type = type,
        NormalSide = AccountSide.Debit, IsLeaf = true, IsActive = true
    };

    private static JournalEntryService Journal(CP6Context db) => new(db, new FiscalPeriodService(db, 1), new FinSequenceService(db));

    private static async Task<Guid> CreatePendingAsync(CP6Context db, BudgetSeed seed, string maker, decimal amount)
    {
        var journal = Journal(db);
        var id = await journal.CreateDraftAsync(new()
        {
            VoucherDate = new DateTime(seed.Year, 2, 15), Source = VoucherSource.Manual, Description = "WP4 budget posting",
            Lines =
            {
                new JournalLine { AccountId = seed.FirstAccount, Debit = amount },
                new JournalLine { AccountId = seed.SecondAccount, Credit = amount }
            }
        }, maker);
        var result = await journal.SubmitForReviewAsync(id);
        Assert.True(result.Ok, result.Code);
        return id;
    }

    private sealed record BudgetSeed(Guid Tenant, Guid Version, Guid FirstAccount, Guid SecondAccount, int Year);
}
