using System.Data;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Entity.DomainModels.Common;
using Dapper;
using Microsoft.EntityFrameworkCore;

namespace CP6.DatabaseCompatibility.RuntimeProbe;

internal static class OrderNumberGates
{
    private static readonly DateTime Date = new(2026, 10, 3);
    public static async Task<string> ImmediateAsync(RuntimeFixture fixture)
    {
        await fixture.ClearOrderAsync();
        try
        {
            await using var core = fixture.Core();
            var result = await DocNumber.NextAsync(core, "ord", Date);
            ProbeAssert.Require(result.Seq == 1 && result.No == "ORD2026100001", "Original ORD format and first number must be retained.");
            ProbeAssert.Require(await fixture.ReadOrderAsync() == 1, "ORD must reserve immediately without caller SaveChanges; a separate connection must observe1.");
            ProbeAssert.Require(!core.ChangeTracker.Entries<DocSequence>().Any(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted), "Immediate ORD allocation must not leave pending tracked counter edits.");
            return "Actual DocNumber reserved1 without SaveChanges; separate connection read1 and no pending manual counter changes.";
        }
        finally { await fixture.ClearOrderAsync(); }
    }
    public static async Task<string> ConcurrentFirstCreationAsync(RuntimeFixture fixture)
    {
        await fixture.ClearOrderAsync();
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<int>? first = null;
        Task<int>? second = null;
        try
        {
            var bothReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var ready = 0;
            async Task<int> Allocate()
            {
                await using var core = fixture.Core();
                await core.Database.OpenConnectionAsync(budget.Token);
                if (Interlocked.Increment(ref ready) == 2) bothReady.SetResult();
                await barrier.Task.WaitAsync(budget.Token);
                return (await DocNumber.NextAsync(core, "ORD", Date)).Seq;
            }
            first = Allocate(); second = Allocate();
            await bothReady.Task.WaitAsync(TimeSpan.FromSeconds(10), budget.Token);
            barrier.SetResult();
            var results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(20));
            ProbeAssert.Require(results.Order().SequenceEqual(new[] { 1, 2 }) && await fixture.ReadOrderAsync() == 2, "Two ready physical sessions creating an absent ORD must allocate distinct1/2 and persist2.");
            return "Two actual open sessions crossed a shared start barrier with ORD absent; distinct1/2 and committed2.";
        }
        finally
        {
            budget.Cancel();
            barrier.TrySetCanceled();
            // Wait for allocation and context disposal before deleting its fixture.
            try { await Task.WhenAll(new[] { first, second }.OfType<Task<int>>()); }
            catch { /* Retain the primary case failure, after both workers have ended. */ }
            await fixture.ClearOrderAsync();
        }
    }
    public static async Task<string> RollbackAsync(RuntimeFixture fixture)
    {
        await fixture.ClearOrderAsync();
        try
        {
            await using var core = fixture.Core();
            await using (var transaction = await core.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted))
            {
                ProbeAssert.Require((await DocNumber.NextAsync(core, "ORD", Date)).Seq == 1 && (await DocNumber.NextAsync(core, "ORD", Date)).Seq == 2, "The caller transaction must observe consecutive reserved numbers.");
                await transaction.RollbackAsync();
            }
            ProbeAssert.Require(await fixture.ReadOrderAsync() is null, "Caller rollback must undo counter creation and both increments.");
            await using var fresh = fixture.Core();
            ProbeAssert.Require((await DocNumber.NextAsync(fresh, "ORD", Date)).Seq == 1 && await fixture.ReadOrderAsync() == 1, "New transaction after rollback must see absent counter and reserve1.");
            return "Caller-owned transaction reserved1/2 then rollback removed both; fresh allocation independently committed1.";
        }
        finally { await fixture.ClearOrderAsync(); }
    }
    public static async Task<string> GlobalScopeAsync(RuntimeFixture fixture)
    {
        await fixture.ClearOrderAsync();
        try
        {
            await using var first = fixture.Core(Guid.Parse("14bfaf3d-b8aa-4423-bfc8-ea2e55478c5d"));
            await using var second = fixture.Core(Guid.Parse("f49d16a1-de1c-49ef-8159-93190f6d88e0"));
            var one = await DocNumber.NextAsync(first, "ORD", Date);
            var two = await DocNumber.NextAsync(second, "ORD", new(2026, 11, 1));
            ProbeAssert.Require(one.No == "ORD2026100001" && two.No == "ORD2026110002" && await fixture.ReadOrderAsync() == 2, "ORD must remain one global never-reset FuncCode counter; date changes only the format prefix.");
            return "Different tenant contexts and months shared the original global1/2 stream; date formatting did not reset it. Business tenant isolation is separate.";
        }
        finally { await fixture.ClearOrderAsync(); }
    }
    public static async Task<string> PendingManualChangeAsync(RuntimeFixture fixture)
    {
        await fixture.ClearOrderAsync();
        try
        {
            await using var core = fixture.Core();
            core.DocSequences.Add(new() { FuncCode = "ORD", LastSeq = 77 });
            try { await DocNumber.NextAsync(core, "ORD", Date); throw new ProbeAssertionException("Pending manual ORD change must be rejected."); }
            catch (InvalidOperationException exception) when (exception.Message == "ORD_SEQUENCE_HAS_PENDING_MANUAL_CHANGE") { }
            ProbeAssert.Require(await fixture.ReadOrderAsync() is null, "Rejected manual counter changes must not write native rows.");
            return "Actual pending manual ORD row rejected with original error; no native counter created.";
        }
        finally { await fixture.ClearOrderAsync(); }
    }

    public static async Task<string> ExistingRowConcurrencyAsync(RuntimeFixture fixture)
    {
        const string code = "ORD";
        await fixture.VerifyOwnerAsync();
        await RequireAbsentCodeAsync(fixture, code);
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bothReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = 0;
        var sessions = new int[2];
        Task<int>? first = null;
        Task<int>? second = null;
        try
        {
            await using (var seed = fixture.Core())
                ProbeAssert.Require((await DocNumber.NextAsync(seed, code, Date)).Seq == 1,
                    "The existing-row concurrency fixture must first reserve and commit native ORD counter1.");
            ProbeAssert.Require(await ReadCodeAsync(fixture, code, budget.Token) == 1,
                "A separate connection must observe the committed ORD seed before concurrent allocation starts.");

            async Task<int> Allocate(int index)
            {
                await using var core = fixture.Core();
                await core.Database.OpenConnectionAsync(budget.Token);
                sessions[index] = await core.Database.GetDbConnection().QuerySingleAsync<int>(new CommandDefinition(
                    fixture.IsPostgreSql ? "SELECT pg_backend_pid()" : "SELECT @@SPID", commandTimeout: 15,
                    cancellationToken: budget.Token));
                if (Interlocked.Increment(ref ready) == 2) bothReady.TrySetResult();
                await start.Task.WaitAsync(budget.Token);
                return (await DocNumber.NextAsync(core, code, Date)).Seq;
            }

            first = Allocate(0);
            second = Allocate(1);
            await bothReady.Task.WaitAsync(TimeSpan.FromSeconds(10), budget.Token);
            ProbeAssert.Require(sessions[0] > 0 && sessions[1] > 0 && sessions[0] != sessions[1],
                "The existing-row allocation barrier must hold two distinct actual physical database sessions.");
            start.SetResult();
            var allocated = await Task.WhenAll(first, second);
            ProbeAssert.Require(allocated.Order().SequenceEqual(new[] { 2, 3 }) &&
                await ReadCodeAsync(fixture, code, budget.Token) == 3,
                "Two physical sessions starting with committed ORD1 must reserve distinct2/3 and persist native3.");
            return "Committed ORD1 before a real two-session start barrier; actual sessions reserved distinct2/3 and a separate connection observed committed3.";
        }
        finally
        {
            budget.Cancel();
            start.TrySetCanceled();
            // Observe both workers before cleanup; an allocation must never outlive its fixture.
            if (first is not null || second is not null)
            {
                try { await Task.WhenAll(new[] { first, second }.OfType<Task<int>>()); }
                catch { /* The primary case failure is retained; cleanup still waits for worker disposal. */ }
            }
            await ClearCodeAsync(fixture, code);
        }
    }

    public static async Task<string> PendingModifiedAndDeletedAsync(RuntimeFixture fixture)
    {
        const string code = "ORD";
        await fixture.VerifyOwnerAsync();
        await RequireAbsentCodeAsync(fixture, code);
        try
        {
            await using (var seed = fixture.Core())
                ProbeAssert.Require((await DocNumber.NextAsync(seed, code, Date)).Seq == 1,
                    "The pending-state fixture must first save native ORD counter1.");
            ProbeAssert.Require(await ReadCodeAsync(fixture, code) == 1,
                "The pending-state fixture's saved ORD1 must be visible to a separate connection.");

            await using (var modified = fixture.Core())
            {
                var row = await modified.DocSequences.SingleAsync(value => value.FuncCode == code);
                row.LastSeq = 77;
                modified.Entry(row).Property(value => value.LastSeq).IsModified = true;
                ProbeAssert.Require(modified.Entry(row).State == EntityState.Modified,
                    "The modified-state fixture must contain a previously saved ORD row tracked as Modified.");
                await RequirePendingRejectedAsync(modified);
                ProbeAssert.Require(modified.Entry(row).State == EntityState.Modified && row.LastSeq == 77 &&
                    await ReadCodeAsync(fixture, code) == 1,
                    "Rejecting a tracked Modified ORD must preserve the pending caller state and native saved counter1.");
            }

            await using (var deleted = fixture.Core())
            {
                var row = await deleted.DocSequences.SingleAsync(value => value.FuncCode == code);
                deleted.DocSequences.Remove(row);
                ProbeAssert.Require(deleted.Entry(row).State == EntityState.Deleted,
                    "The deleted-state fixture must contain a previously saved ORD row tracked as Deleted.");
                await RequirePendingRejectedAsync(deleted);
                ProbeAssert.Require(deleted.Entry(row).State == EntityState.Deleted &&
                    await ReadCodeAsync(fixture, code) == 1,
                    "Rejecting a tracked Deleted ORD must preserve the pending caller state and native saved counter1.");
            }
            return "Actual saved ORD1 was separately tracked Modified and Deleted; both calls retained ORD_SEQUENCE_HAS_PENDING_MANUAL_CHANGE and left the committed counter1 unchanged.";
        }
        finally { await ClearCodeAsync(fixture, code); }
    }

    public static async Task<string> NonOrderBatchingAsync(RuntimeFixture fixture)
    {
        const string code = "QZV";
        await fixture.VerifyOwnerAsync();
        await RequireAbsentCodeAsync(fixture, code);
        try
        {
            await using var core = fixture.Core();
            var first = await DocNumber.NextAsync(core, "qzv", Date);
            var row = core.DocSequences.Local.Single(value => value.FuncCode == code);
            ProbeAssert.Require(first.Seq == 1 && first.No == "QZV2026100001" && row.LastSeq == 1 &&
                core.Entry(row).State == EntityState.Added && await ReadCodeAsync(fixture, code) is null,
                "A non-ORD first allocation must remain a caller-tracked Added row invisible to a separate native connection before SaveChanges.");

            var second = await DocNumber.NextAsync(core, code, Date);
            ProbeAssert.Require(second.Seq == 2 && second.No == "QZV2026100002" && row.LastSeq == 2 &&
                ReferenceEquals(row, core.DocSequences.Local.Single(value => value.FuncCode == code)) &&
                core.ChangeTracker.Entries<DocSequence>().Count(entry => entry.Entity.FuncCode == code) == 1 &&
                core.Entry(row).State == EntityState.Added && await ReadCodeAsync(fixture, code) is null,
                "Two non-ORD calls in one context must reuse the same single Local counter row, allocate1/2, and remain invisible before caller save.");

            await core.SaveChangesAsync();
            ProbeAssert.Require(core.Entry(row).State == EntityState.Unchanged &&
                await ReadCodeAsync(fixture, code) == 2,
                "The caller's SaveChanges must commit the original non-ORD batching counter2 to a separate native connection.");
            return "Actual QZV calls returned original formatted1/2 on the same single Local row; separate native reads saw no row before SaveChanges and committed2 afterwards. No cross-context non-ORD concurrency guarantee is implied.";
        }
        finally { await ClearCodeAsync(fixture, code); }
    }

    private static async Task RequirePendingRejectedAsync(CP6Context core)
    {
        try
        {
            await DocNumber.NextAsync(core, "ord", Date);
            throw new ProbeAssertionException("An ORD allocation with tracked Modified or Deleted counter state must be rejected.");
        }
        catch (InvalidOperationException exception) when (exception.Message == "ORD_SEQUENCE_HAS_PENDING_MANUAL_CHANGE") { }
    }

    private static async Task RequireAbsentCodeAsync(RuntimeFixture fixture, string code)
        => ProbeAssert.Require(await ReadCodeAsync(fixture, code) is null,
            "The new counter fixture code must be absent before its case; existing rows must not be overwritten.");

    private static async Task<int?> ReadCodeAsync(RuntimeFixture fixture, string code,
        CancellationToken cancellationToken = default)
    {
        await using var connection = fixture.Connection();
        await connection.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
            $"SELECT {fixture.Column("LastSeq")} FROM {fixture.SequenceTable} WHERE {fixture.Column("FuncCode")}=@code",
            new { code }, commandTimeout: 15, cancellationToken: cancellationToken));
    }

    private static async Task ClearCodeAsync(RuntimeFixture fixture, string code)
    {
        await using var connection = fixture.Connection();
        await connection.OpenAsync();
        await connection.ExecuteAsync(new CommandDefinition(
            $"DELETE FROM {fixture.SequenceTable} WHERE {fixture.Column("FuncCode")}=@code", new { code }, commandTimeout: 15));
        ProbeAssert.Require(await ReadCodeAsync(fixture, code) is null,
            "Cleanup must leave only the exact case counter code absent.");
    }
}
