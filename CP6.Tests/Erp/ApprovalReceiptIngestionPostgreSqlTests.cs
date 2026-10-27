using System.Text;
using CP6.Core.Services.Common;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private sealed record ReceiptFixture(ApprovalReceiptIngestionV2 Owner, IntakeAuthority Authority,
        ApprovalApplicationReceiptV2[] Receipts, FactFixture Fact);
    private static ApprovalReceiptIngestionV2 ReceiptReceiver(Harness h, IApprovalServiceAuthorityV2 authority) => new(h.Db, ScopeFence(h), authority);
    private async Task<ReceiptFixture> ReceiptSetup(Harness h, int count = 3)
    {
        var f = await FactSetup(h); await f.Owner.ReceiveAsync(f.Payload); var tool = await ApplicationTool(h);
        var receipts = new List<ApprovalApplicationReceiptV2>();
        for (var i = 0; i < count; i++) receipts.Add((await ApplicationAttempt(h, tool)).Receipt);
        var principal = await h.Db.Database.SqlQuery<Guid>($"SELECT principal AS \"Value\" FROM cp6_approval_intake_v2.submissions WHERE tenant_id={h.Tenant}").SingleAsync();
        var authority = new IntakeAuthority(f.Authority.Identity with { Principal = principal, Client = "fixture-service" });
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_approval_receipt_v2.registrations(environment_id,tenant_id,principal_id,owner,revision,enabled,can_receive,can_query,evidence_mode)
            VALUES({ScopeEnvironment},{h.Tenant},{principal},'Main.Wms.ToolDisposition.v1',{Guid.NewGuid()},true,true,true,'DEMO');
            INSERT INTO cp6_approval_receipt_v2.subject_grants(environment_id,tenant_id,principal_id,submission_id,owner,revision,enabled,can_receive,can_query)
            VALUES({ScopeEnvironment},{h.Tenant},{principal},{f.Decision.SubmissionId},'Main.Wms.ToolDisposition.v1',{Guid.NewGuid()},true,true,true);
            """);
        return new(ReceiptReceiver(h, authority), authority, receipts.ToArray(), f);
    }
    private static byte[] ReceiptBytes(ApprovalApplicationReceiptV2 e) => ApprovalCanonicalV2.Bytes(e);
    private static async Task ReceiptCounts(Harness h, int inbox, int history)
    {
        Assert.Equal(inbox, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_approval_receipt_v2.inbox WHERE tenant_id={h.Tenant}").SingleAsync());
        Assert.Equal(history, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_approval_receipt_v2.history WHERE tenant_id={h.Tenant}").SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task App_receipt_out_of_order_is_durable_gap_then_drains_exact_continuous_chain_without_business_reexecution()
    {
        await using var h = await Seed(); var f = await ReceiptSetup(h); var one = f.Receipts[0]; var two = f.Receipts[1]; var three = f.Receipts[2];
        Assert.IsType<ApprovalReceiptNotObservedV2>((await f.Owner.OriginalAsync(two.ReceiptEventId)).Body);
        var raw = Encoding.UTF8.GetBytes(" \n" + Encoding.UTF8.GetString(ReceiptBytes(two)) + "\n");
        var received = await f.Owner.ReceiveAsync(raw); Assert.Equal(202, received.Status);
        var gap = Assert.IsType<ApprovalReceiptPendingV2>(received.Body); Assert.Equal("0", gap.HighestContinuousRevision); Assert.Equal("1", gap.ExpectedNextRevision);
        Assert.Equal(gap, (await f.Owner.OriginalAsync(two.ReceiptEventId)).Body); await ReceiptCounts(h, 1, 0);
        var empty = await f.Owner.CurrentAsync(two.ReceiptStreamId); Assert.Null(empty.Receipt); Assert.True(empty.PendingGap);
        Assert.Equal(raw, await h.Db.Database.SqlQuery<byte[]>($"SELECT raw AS \"Value\" FROM cp6_approval_receipt_v2.inbox WHERE tenant_id={h.Tenant}").SingleAsync());
        Assert.Equal(202, (await f.Owner.ReceiveAsync(ReceiptBytes(three))).Status);
        var first = Assert.IsType<ApprovalReceiptRecordedV2>((await f.Owner.ReceiveAsync(ReceiptBytes(one))).Body);
        Assert.Equal("3", first.HighestContinuousRevision); Assert.Equal(three.ReceiptId, first.ProjectionReceiptId);
        var current = await f.Owner.CurrentAsync(one.ReceiptStreamId); Assert.Equal(three, current.Receipt); Assert.False(current.PendingGap);
        Assert.IsType<ApprovalReceiptRecordedV2>((await f.Owner.OriginalAsync(two.ReceiptEventId)).Body);
        Assert.Equal(200, (await f.Owner.ReceiveAsync(ReceiptBytes(one))).Status); await ReceiptCounts(h, 3, 3); await ApplicationCounts(h, 3);
        Assert.Empty(await h.Db.StockTransactions.ToArrayAsync());
        var originalAck = FactAck(await f.Fact.Owner.ReceivedAsync(f.Fact.Decision.EventId)); Assert.Equal("NOT_APPLIED", originalAck.Application);
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => h.Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM cp6_approval_receipt_v2.inbox WHERE tenant_id={h.Tenant}"));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_approval_receipt_v2.streams SET highest_revision=5,row_version={Guid.NewGuid()} WHERE tenant_id={h.Tenant}"));
    }
    [CurrentPermissionPostgresFact]
    public async Task App_receipt_conflicts_preserve_original_and_registered_service_cannot_forge_owner_outcomes()
    {
        await using var h = await Seed(); var f = await ReceiptSetup(h, 1); var e = f.Receipts[0]; await f.Owner.ReceiveAsync(ReceiptBytes(e));
        var altered = ApprovalApplicationReceiptWireV2.Seal(e with { OutcomeCode = "fake receipt outcome" });
        Assert.Equal(409, (await f.Owner.ReceiveAsync(ReceiptBytes(altered))).Status);
        var fork = ApprovalApplicationReceiptWireV2.Seal(e with { ReceiptEventId = Guid.NewGuid(), ReceiptId = Guid.NewGuid() });
        Assert.Equal(409, (await f.Owner.ReceiveAsync(ReceiptBytes(fork))).Status);
        var fabricated = ApprovalApplicationReceiptWireV2.Seal(e with { ReceiptEventId = Guid.NewGuid(), ReceiptId = Guid.NewGuid(), ReceiptRevision = "2", PreviousReceiptId = e.ReceiptId,
            Application = "APPLIED", OutcomeCode = "DISCARDED", AppliedAt = e.RecordedAt, BusinessResultRef = new("invented-result", Guid.NewGuid().ToString(), "1"), BusinessResultVersion = "1" });
        Assert.Equal(409, (await f.Owner.ReceiveAsync(ReceiptBytes(fabricated))).Status);
        Assert.Equal(e, (await f.Owner.CurrentAsync(e.ReceiptStreamId)).Receipt); await ReceiptCounts(h, 1, 1);
        Assert.Equal(3, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_approval_receipt_v2.conflicts WHERE tenant_id={h.Tenant}").SingleAsync());
        Assert.Empty(await h.Db.StockTransactions.ToArrayAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task App_receipt_lost_commit_response_recovers_original_under_current_read_after_write_revocation()
    {
        var fault = new CommitFault(); await using var h = await Seed(fault); var f = await ReceiptSetup(h, 2); var e = f.Receipts[0]; fault.Armed = true;
        Assert.Equal("RECEIPT_OUTCOME_UNKNOWN", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => f.Owner.ReceiveAsync(ReceiptBytes(e)))).Message);
        f.Authority.Identity = f.Authority.Identity with { CanSubmit = false };
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_approval_receipt_v2.subject_grants SET can_receive=false,revision={Guid.NewGuid()} WHERE tenant_id={h.Tenant}");
        var saved = await f.Owner.OriginalAsync(e.ReceiptEventId); Assert.Equal(200, saved.Status);
        Assert.Equal(saved.Body, (await f.Owner.ReceiveAsync(ReceiptBytes(e))).Body);
        Assert.Equal(403, (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => f.Owner.ReceiveAsync(ReceiptBytes(f.Receipts[1])))).Status);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_approval_receipt_v2.subject_grants SET can_query=false,revision={Guid.NewGuid()} WHERE tenant_id={h.Tenant}");
        Assert.Equal(404, (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => f.Owner.OriginalAsync(e.ReceiptEventId))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => f.Owner.CurrentAsync(e.ReceiptStreamId))).Status);
        await ReceiptCounts(h, 1, 1);
    }
    [CurrentPermissionPostgresFact]
    public async Task App_receipt_two_connections_serialize_gap_and_predecessor_without_duplicate_history()
    {
        await using var h = await Seed(); var f = await ReceiptSetup(h, 2);
        await using var other = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        var peer = ReceiptReceiver(h with { Db = other }, new IntakeAuthority(f.Authority.Identity));
        var two = ReceiptBytes(f.Receipts[1]);
        await Task.WhenAll(f.Owner.ReceiveAsync(two), peer.ReceiveAsync(two)); await ReceiptCounts(h, 1, 0);
        var one = ReceiptBytes(f.Receipts[0]); await Task.WhenAll(f.Owner.ReceiveAsync(one), peer.ReceiveAsync(one));
        await ReceiptCounts(h, 2, 2); Assert.Equal("2", (await f.Owner.CurrentAsync(f.Receipts[0].ReceiptStreamId)).HighestContinuousRevision);
    }
    [CurrentPermissionPostgresFact]
    public async Task App_receipt_missing_gap_history_or_late_authority_change_rolls_back_entire_ingestion()
    {
        var hook = new StartWriteHook(); await using var h = await Seed(hook); var f = await ReceiptSetup(h, 2);
        hook.Before = c => { if (c.CommandText.Contains("INSERT INTO cp6_approval_receipt_v2.gaps")) c.CommandText = "SELECT 1"; return Task.CompletedTask; };
        Assert.Equal("RECEIPT_OUTCOME_UNKNOWN", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => f.Owner.ReceiveAsync(ReceiptBytes(f.Receipts[1])))).Message);
        hook.Before = null; await ReceiptCounts(h, 0, 0);
        hook.Before = c => { if (c.CommandText.Contains("INSERT INTO cp6_approval_receipt_v2.history")) c.CommandText = "SELECT 1"; return Task.CompletedTask; };
        Assert.Equal("RECEIPT_PROJECTION_INCOMPLETE", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => f.Owner.ReceiveAsync(ReceiptBytes(f.Receipts[0])))).Message);
        hook.Before = null; await ReceiptCounts(h, 0, 0);
        hook.Before = async c => {
            if (!c.CommandText.Contains("INSERT INTO cp6_approval_receipt_v2.inbox")) return; hook.Before = null;
            await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_approval_receipt_v2.subject_grants SET can_receive=false WHERE tenant_id={h.Tenant}");
            await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_approval_receipt_v2.subject_grants SET can_receive=true WHERE tenant_id={h.Tenant}");
        };
        Assert.Equal("SCOPE_AUTHORITY_CHANGED", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => f.Owner.ReceiveAsync(ReceiptBytes(f.Receipts[0])))).Message);
        hook.Before = null; await ReceiptCounts(h, 0, 0); Assert.Equal(200, (await f.Owner.ReceiveAsync(ReceiptBytes(f.Receipts[0]))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task App_receipt_intake_or_workflow_registration_alone_cannot_authorize_receipt_projection()
    {
        await using var h = await Seed(); var f = await ReceiptSetup(h, 1); var bytes = ReceiptBytes(f.Receipts[0]);
        Assert.Equal(403, (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => ReceiptReceiver(h, f.Fact.Authority).ReceiveAsync(bytes))).Status);
        var original = f.Authority.Identity; f.Authority.Identity = original with { EvidenceMode = "REAL" };
        Assert.Equal(403, (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => f.Owner.ReceiveAsync(bytes))).Status);
        f.Authority.Identity = original;
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_approval_receipt_v2.registrations SET enabled=false,revision={Guid.NewGuid()} WHERE tenant_id={h.Tenant}");
        Assert.Equal(403, (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => f.Owner.ReceiveAsync(bytes))).Status);
        await ReceiptCounts(h, 0, 0);
    }
}
