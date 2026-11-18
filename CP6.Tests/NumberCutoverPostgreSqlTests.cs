using System.Net;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Pub;
using CP6.Core.Services.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    // 只模拟业务归属方提供的批准、冻结清单和原回执。两个分配器与事务均走实际 PostgreSQL。
    private sealed class PriorNumberSource(NumberCutoverSnapshotV1 snapshot, string writerDatabase) : INumberCutoverSourceV1
    {
        public NumberCutoverSnapshotV1 Snapshot = snapshot;
        public NumberAllocationReceiptV1? Resolved;
        public string ActiveDatabase = writerDatabase;
        public NumberAllocationReceiptV1[] RequiredAllocations = [];
        public Task<NumberWriterApprovalV1?> AcquireWriterAsync(CP6Context db, NumberAllocationScopeV1 scope,
            string cutoverApprovalRef, CancellationToken ct)
        {
            var admitted = db.Database.GetDbConnection().Database == ActiveDatabase && Snapshot.Scope == scope
                && cutoverApprovalRef == Snapshot.ApprovalRef;
            return Task.FromResult<NumberWriterApprovalV1?>(admitted
                ? new(scope, cutoverApprovalRef, "模拟归属方/已核对外部完整分配", RequiredAllocations) : null);
        }
        public Task<NumberCutoverSnapshotV1?> ReadApprovedAsync(CP6Context db, NumberAllocationScopeV1 scope, CancellationToken ct)
            => Task.FromResult<NumberCutoverSnapshotV1?>(Snapshot);
        public Task<NumberAllocationReceiptV1?> ReadOriginalAsync(CP6Context db, NumberAllocationScopeV1 scope,
            string approvalRef, string operationRef, CancellationToken ct)
        {
            Assert.Equal(Snapshot.ApprovalRef, approvalRef);
            Assert.Equal(Snapshot.Scope, scope);
            Assert.Equal("pending", operationRef);
            return Task.FromResult(Resolved);
        }
    }

    private NumberAllocationOwnerV1 CutoverOwner(Harness h, PriorNumberSource source, params IInterceptor[] interceptors)
        => new(() => server.Context(new TenantContext { CurrentTenantId = h.Tenant }, null, interceptors),
            source.Snapshot.Scope, new(new NumberHttp(h.Http), (_, _) => Task.FromResult(h.User), "pub-seq", "add", "query"), source);

    private static NumberAllocationReceiptV1 PriorReceipt(NumberAllocationScopeV1 scope, string key, int ordinal, string text)
        => new(Guid.NewGuid(), scope.TenantId, scope.RuleId, scope.SourceOwner, key, "old-v1", "", ordinal, text, NumberTime);

    private async Task<PriorNumberSource> SeedNumberPredecessor(Harness h, string thirdNumber = "N003")
    {
        await EngineeringAction(h, "pub-seq", "edit");
        await EngineeringAction(h, "pub-seq", "query");
        await EngineeringAction(h, "pub-seq", "add");
        var scope = new NumberAllocationScopeV1(Guid.NewGuid(), h.Tenant, "采购申请测试归属方", "CUTOVER",
            "测试采购业务", "测试法人", null, NumberAllocationOwnerV1.ApprovedPubCutover);
        var rule = new NumberAllocationRuleV1("old-v1", "N", null, 3, 0, "UTC", "");
        h.Db.Pub_DocSequences.Add(new() { Id = scope.RuleId, BizKey = scope.BizKey, Prefix = rule.Prefix,
            SeqLength = 3, CurrentPeriod = "", CurrentValue = 2 });
        await h.Db.SaveChangesAsync();
        return new(new(scope, rule, "test-owner/approved-manifest", NumberAllocationOwnerV1.LegacyPubAllocator,
            [new("", 3)], [new("", 1, "N001", null, "first"), new("", 2, null, "原业务回滚，保留跳号", null),
                new("", 3, thirdNumber, null, "pending")],
            [new(h.User, NumberCommand("first", rule.Version), PriorReceipt(scope, "first", 1, "N001")),
                new(h.User, NumberCommand("pending", rule.Version), null)]), h.Db.Database.GetDbConnection().Database);
    }

    [CurrentPermissionPostgresFact]
    public async Task Number_cutover_requires_approved_complete_source_and_preserves_legacy_on_refusal()
    {
        await using var h = await Seed();
        var source = await SeedNumberPredecessor(h);
        var good = source.Snapshot;
        var owner = CutoverOwner(h, source);
        async Task Refused(string code) => Assert.Equal(code,
            (await Assert.ThrowsAsync<NumberAllocationExceptionV1>(() => owner.AdoptApprovedAsync(good.ApprovalRef))).Message);
        source.Snapshot = good with { OriginalAllocator = "另一个同名域" };
        await Refused("NUMBER_CUTOVER_APPROVAL_REQUIRED");
        source.Snapshot = good with { Slots = good.Slots.Skip(1).ToArray() };
        await Refused("NUMBER_CUTOVER_INCOMPLETE");
        source.Snapshot = good with { Periods = [new("", 1)] };
        await Refused("NUMBER_CUTOVER_INCOMPLETE");
        source.Snapshot = good with { Rule = good.Rule with { ResetCycle = 1 } };
        await Refused("NUMBER_CUTOVER_RULE_MISMATCH");
        Assert.Null(await NumberAllocationOwnerV1.FindRegisteredScopeAsync(h.Db, good.Scope.RuleId));
        Assert.Equal("N003", await new SeqService(h.Db).NextAsync(good.Scope.BizKey));
    }

    [CurrentPermissionPostgresFact]
    public async Task Number_cutover_recovers_original_owner_and_never_reassigns_exposures_or_gaps()
    {
        await using var h = await Seed();
        var source = await SeedNumberPredecessor(h);
        var snapshot = source.Snapshot; var scope = snapshot.Scope; var owner = CutoverOwner(h, source);
        var lost = new NumberLostCommit { Armed = true };
        var uncertain = await Assert.ThrowsAsync<NumberAllocationExceptionV1>(
            () => CutoverOwner(h, source, lost).AdoptApprovedAsync(snapshot.ApprovalRef));
        Assert.Equal("NUMBER_OUTCOME_UNKNOWN", uncertain.Message);
        Assert.True(await owner.AdoptApprovedAsync(snapshot.ApprovalRef));
        Assert.Equal("NUMBER_ALLOCATION_OWNER_REQUIRED", (await Assert.ThrowsAsync<InvalidOperationException>(
            () => new SeqService(h.Db).NextAsync(scope.BizKey))).Message);
        Assert.Equal(snapshot.Operations[0].OriginalReceipt, await owner.AllocateAsync(snapshot.Operations[0].Command));
        Assert.Equal("NUMBER_COMMAND_CONFLICT", (await Assert.ThrowsAsync<NumberAllocationExceptionV1>(
            () => owner.AllocateAsync(snapshot.Operations[0].Command with { BusinessInputIdentity = "另一业务" }))).Message);
        Assert.Equal("NUMBER_PREDECESSOR_OUTCOME_UNKNOWN", (await Assert.ThrowsAsync<NumberAllocationExceptionV1>(
            () => owner.AllocateAsync(snapshot.Operations[1].Command))).Message);
        source.Resolved = PriorReceipt(scope, "pending", 3, "错误外露号");
        Assert.Equal("NUMBER_PREDECESSOR_ORIGINAL_MISMATCH", (await Assert.ThrowsAsync<NumberAllocationExceptionV1>(
            () => owner.ReadAsync("pending"))).Message);
        source.Resolved = PriorReceipt(scope, "pending", 3, "N003");
        Assert.Equal(source.Resolved, await owner.ReadAsync("pending"));
        Assert.Equal(source.Resolved, await owner.AllocateAsync(snapshot.Operations[1].Command));
        Assert.Equal(2, (await owner.ReadManagementAsync()).AllocatedCount);
        var committed = await Task.WhenAll(Enumerable.Range(0, 6).Select(i =>
            CutoverOwner(h, source).AllocateAsync(NumberCommand("new-" + i, snapshot.Rule.Version))));
        Assert.Equal(Enumerable.Range(4, 6), committed.Select(x => x.SequenceValue).Order());
        var command = NumberCommand("lost-new", snapshot.Rule.Version);
        lost.Armed = true;
        Assert.Equal("NUMBER_OUTCOME_UNKNOWN", (await Assert.ThrowsAsync<NumberAllocationExceptionV1>(
            () => CutoverOwner(h, source, lost).AllocateAsync(command))).Message);
        var restored = await owner.ReadAsync(command.OperationRef);
        Assert.Equal(10, restored!.SequenceValue);
        Assert.Equal(restored, await owner.AllocateAsync(command));
        Assert.Equal(9, (await owner.ReadManagementAsync()).AllocatedCount);
    }

    [CurrentPermissionPostgresFact]
    public async Task Number_cutover_existing_external_text_collision_rolls_back_new_counter()
    {
        await using var h = await Seed(); var source = await SeedNumberPredecessor(h, "N004");
        var owner = CutoverOwner(h, source); var snapshot = source.Snapshot;
        await owner.AdoptApprovedAsync(snapshot.ApprovalRef);
        Assert.Equal("NUMBER_PREDECESSOR_HISTORY", (await Assert.ThrowsAsync<NumberAllocationExceptionV1>(
            () => owner.AllocateAsync(NumberCommand("new", snapshot.Rule.Version)))).Message);
        await owner.ChangeRuleAsync(new("change", snapshot.Rule.Version, "v2", "X", null, 3));
        Assert.Equal("X004", (await owner.AllocateAsync(NumberCommand("new", "v2"))).Number);
        Assert.Equal(3, (await owner.ReadManagementAsync()).AllocatedCount);
    }

    [CurrentPermissionPostgresFact]
    public async Task Number_cutover_rechecks_legacy_watermark_after_waiting_for_a_writer()
    {
        await using var h = await Seed(); var source = await SeedNumberPredecessor(h);
        await using var tx = await h.Db.Database.BeginTransactionAsync();
        var legacy = new SeqService(h.Db);
        Assert.Equal("N003", await legacy.NextAsync(source.Snapshot.Scope.BizKey));
        Assert.Equal("N004", await legacy.NextAsync(source.Snapshot.Scope.BizKey));
        var change = CutoverOwner(h, source).AdoptApprovedAsync(source.Snapshot.ApprovalRef);
        try
        {
            await using var observer = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(7));
            while (!await observer.Database.SqlQueryRaw<bool>("""
                SELECT EXISTS(SELECT FROM pg_stat_activity WHERE datname=current_database()
                  AND pid<>pg_backend_pid() AND wait_event_type='Lock' AND query LIKE '%Pub_DocSequence%') AS "Value"
                """).SingleAsync(timeout.Token)) await Task.Delay(30, timeout.Token);
        }
        finally { await tx.CommitAsync(); }
        Assert.Equal("NUMBER_CUTOVER_INCOMPLETE", (await Assert.ThrowsAsync<NumberAllocationExceptionV1>(() => change)).Message);
        Assert.Null(await NumberAllocationOwnerV1.FindRegisteredScopeAsync(h.Db, source.Snapshot.Scope.RuleId));
        Assert.Equal("N005", await legacy.NextAsync(source.Snapshot.Scope.BizKey));
    }

    [CurrentPermissionPostgresFact]
    public async Task Number_cutover_same_business_key_in_two_tenants_keeps_separate_writers_and_immutable_manifests()
    {
        await using var a = await Seed();
        await using var b = await Seed();
        var first = await SeedNumberPredecessor(a);
        var second = await SeedNumberPredecessor(b);
        var firstOwner = CutoverOwner(a, first);
        var secondOwner = CutoverOwner(b, second);
        Assert.Equal(first.Snapshot.Scope.BizKey, second.Snapshot.Scope.BizKey);
        Assert.NotEqual(a.Tenant, b.Tenant);
        Assert.True(await firstOwner.AdoptApprovedAsync(first.Snapshot.ApprovalRef));
        // 另一租户同名旧分配器仍可独立领取 N003。
        Assert.Equal("N003", await new SeqService(b.Db).NextAsync(second.Snapshot.Scope.BizKey));
        Assert.True(await secondOwner.AdoptApprovedAsync(second.Snapshot.ApprovalRef));
        var command = NumberCommand("equivalent-business-key", first.Snapshot.Rule.Version);
        var one = await firstOwner.AllocateAsync(command);
        var two = await secondOwner.AllocateAsync(command);
        Assert.Equal(one.Number, two.Number);
        Assert.NotEqual(one.AllocationId, two.AllocationId);
        Assert.Equal(a.Tenant, one.TenantId);
        Assert.Equal(b.Tenant, two.TenantId);
        Assert.Equal(one, await firstOwner.ReadAsync(command.OperationRef));
        var closed = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => a.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_number_allocation_v1.prior_periods(rule_id,period_key,last_issued)
            VALUES({first.Snapshot.Scope.RuleId},'additional-period',0)
            """));
        Assert.Equal("NUMBER_CUTOVER_MANIFEST_CLOSED", closed.MessageText);
    }

    [CurrentPermissionPostgresFact]
    public async Task Number_native_restore_keeps_originals_readable_and_requires_all_external_receipts_before_issuing()
    {
        await using var h = await Seed();
        var source = await SeedNumberPredecessor(h);
        var snapshot = source.Snapshot;
        var original = CutoverOwner(h, source);
        await original.AdoptApprovedAsync(snapshot.ApprovalRef);
        var lost = NumberCommand("lost-before-backup", snapshot.Rule.Version);
        await Assert.ThrowsAsync<NumberAllocationExceptionV1>(() =>
            CutoverOwner(h, source, new NumberLostCommit { Armed = true }).AllocateAsync(lost));
        var first = Assert.IsType<NumberAllocationReceiptV1>(await original.ReadAsync(lost.OperationRef));
        Assert.Equal("N004", first.Number);

        NumberAllocationOwnerV1 Restored(PrivacyRestoreCandidateFixture backup)
        {
            CP6Context Context()
            {
                var db = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
                db.Database.SetConnectionString(backup.Connection);
                return db;
            }
            return new(Context, snapshot.Scope, new(new NumberHttp(h.Http),
                (_, _) => Task.FromResult(h.User), "pub-seq", "add", "query"), source);
        }
        async Task Refused(NumberAllocationOwnerV1 owner, string code)
        {
            var error = await Assert.ThrowsAsync<NumberAllocationExceptionV1>(() =>
                owner.AllocateAsync(NumberCommand("after-restore", snapshot.Rule.Version)));
            Assert.Equal(code, error.Message);
        }

        // 两份备份来自本测试独占的 PostgreSQL。两次均实际 pg_dump、pg_restore 到不同新库。
        var connection = h.Db.Database.GetConnectionString()!;
        await using var older = await PrivacyRestoreCandidateFixture.Create(connection);
        var later = await original.AllocateAsync(NumberCommand("exposed-after-backup", snapshot.Rule.Version));
        Assert.Equal("N005", later.Number);
        source.RequiredAllocations = [first, later];
        var stale = Restored(older);
        Assert.Equal(first, await stale.AllocateAsync(lost));
        Assert.Equal(snapshot.Operations[0].OriginalReceipt, await stale.ReadAsync("first"));
        await Refused(stale, "NUMBER_RECOVERY_APPROVAL_REQUIRED");
        source.ActiveDatabase = older.Name;
        await Refused(stale, "NUMBER_RECOVERY_INCOMPLETE");
        await Refused(original, "NUMBER_RECOVERY_APPROVAL_REQUIRED");

        await using var newer = await PrivacyRestoreCandidateFixture.Create(connection);
        source.ActiveDatabase = newer.Name;
        var restored = Restored(newer);
        Assert.Equal(later, await restored.ReadAsync(later.OperationRef));
        var next = await restored.AllocateAsync(NumberCommand("after-restore", snapshot.Rule.Version));
        Assert.Equal("N006", next.Number);
        Assert.Equal(next, await restored.ReadAsync(next.OperationRef));
        Assert.Equal(5, (await restored.ReadManagementAsync()).AllocatedCount);
        await Refused(stale, "NUMBER_RECOVERY_APPROVAL_REQUIRED");
    }

    [CurrentPermissionPostgresFact]
    public async Task Number_http_managed_catalog_usage_and_draft_preview_are_authoritative_and_read_only()
    {
        await using var f = await NumberSeed();
        var h = f.H;
        var allocated = await f.Owner.AllocateAsync(NumberCommand());
        h.Db.Pub_DocSequences.Add(new() { BizKey = "LEGACY-LIST", Prefix = "L", ResetCycle = 1,
            CurrentPeriod = "19990101", CurrentValue = 9 });
        await h.Db.SaveChangesAsync();
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h);
        Task<HttpResponseMessage> Send(string path, object? body = null) =>
            host.Send(OperationsRequest(cookies, body == null ? HttpMethod.Get : HttpMethod.Post, path, body));
        using var managed = await Send("api/pub/seq/owner-v1");
        var catalog = await NumberHttpData<JsonElement>(managed);
        Assert.Contains(catalog.GetProperty("rows").EnumerateArray(), row => row.GetProperty("id").GetGuid() == f.Scope.RuleId);
        using var legacy = await Send("api/pub/seq?legacyOnly=true");
        var legacyRows = (await NumberHttpData<JsonElement>(legacy)).GetProperty("rows").EnumerateArray().ToArray();
        Assert.DoesNotContain(legacyRows, row => row.GetProperty("id").GetGuid() == f.Scope.RuleId);
        using var oldPreview = await Send("api/pub/seq/preview/LEGACY-LIST");
        var sample = await NumberHttpData<JsonElement>(oldPreview);
        Assert.True(sample.GetProperty("wouldReset").GetBoolean()); Assert.False(sample.GetProperty("isReservation").GetBoolean());
        using var read = await Send(NumberRulePath(f.Scope.RuleId, "/management"));
        var view = await NumberHttpData<NumberRuleManagementV1>(read);
        Assert.Equal(f.Scope, view.Scope); Assert.Equal(1, view.AllocatedCount);
        Assert.NotNull(view.EffectiveAtUtc);
        var draft = new NumberRuleDraftV1(f.Rule.Version, "草稿", "yyyy", 5);
        using var response = await Send(NumberRulePath(f.Scope.RuleId, "/preview"), draft);
        var preview = await NumberHttpData<NumberAllocationPreviewV1>(response);
        Assert.StartsWith("草稿", preview.Sample); Assert.False(preview.IsReservation);
        using var stale = await Send(NumberRulePath(f.Scope.RuleId, "/preview"), draft with { ExpectedRuleVersion = "旧版本" });
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        Assert.Equal(1, (await f.Owner.ReadManagementAsync()).AllocatedCount);
        Assert.Equal(allocated, await f.Owner.ReadAsync(allocated.OperationRef));
    }
}
