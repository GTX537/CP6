using System.Text.Json.Nodes;
using CP6.Core.Services.Common;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private sealed class ReceiptChainTransport(ApprovalDecisionDeliveryRouteV2 route,
        Func<Guid, string, int, string?, CancellationToken, Task<ApprovalDecisionTransportResponseV2>> read) : IApprovalReceiptChainTransportV2
    {
        internal ApprovalDecisionDeliveryRouteV2 Current = route;
        internal readonly List<(string After, string? Cursor)> Queries = [];
        public ApprovalDecisionDeliveryRouteV2? Route(Guid tenant) => Current.Tenant == tenant ? Current : null;
        public Task<ApprovalDecisionTransportResponseV2> ReadAsync(ApprovalDecisionDeliveryRouteV2 r, Guid stream, string after, int limit, string? cursor, CancellationToken ct)
        { Queries.Add((after, cursor)); return read(stream, after, limit, cursor, ct); }
    }
    private static async Task RecoveryGrant(Harness h, ReceiptFixture f)
    {
        var principal = f.Fact.Authority.Identity.Principal; var submission = f.Receipts[0].SubmissionId;
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_approval_receipt_v2.registrations(environment_id,tenant_id,principal_id,owner,revision,enabled,can_receive,can_query,evidence_mode,can_recover)
            VALUES({ScopeEnvironment},{h.Tenant},{principal},'Main.Wms.ToolDisposition.v1',{Guid.NewGuid()},true,true,true,'DEMO',true);
            INSERT INTO cp6_approval_receipt_v2.subject_grants(environment_id,tenant_id,principal_id,submission_id,owner,revision,enabled,can_receive,can_query,can_recover)
            VALUES({ScopeEnvironment},{h.Tenant},{principal},{submission},'Main.Wms.ToolDisposition.v1',{Guid.NewGuid()},true,true,true,true);
            """);
    }
    private static async Task<ApprovalDecisionTransportResponseV2> ActualChain(Harness h, ReceiptFixture f, Guid stream, string after, int limit, string? cursor, CancellationToken ct)
    {
        Assert.Null(h.Db.Database.CurrentTransaction);
        try { return new(200, ToolApprovalFactReceiverV2.ApplicationResponseBytes(await f.Fact.Owner.ReceiptChainAsync(stream, after, limit, cursor, ct))); }
        catch (ScopeAuthorizationExceptionV1 e) { return new(e.Status, ApprovalCanonicalV2.Bytes(new { code = e.Message })); }
    }
    private static Task RecoveryDue(Harness h) => h.Db.Database.ExecuteSqlInterpolatedAsync($"""
        UPDATE cp6_approval_receipt_v2.gaps SET lease_until=CASE WHEN epoch>0 THEN clock_timestamp()-interval '1 second' ELSE NULL END,
          next_attempt=clock_timestamp()-interval '1 second' WHERE tenant_id={h.Tenant}
        """);
    [CurrentPermissionPostgresFact]
    public async Task Receipt_recovery_actual_signed_snapshot_pages_preserve_concurrent_append_and_never_reapply_business()
    {
        await using var h = await Seed(); var f = await ReceiptSetup(h, 3); await RecoveryGrant(h, f);
        var last = f.Receipts[^1]; Assert.Equal(202, (await f.Owner.ReceiveAsync(ReceiptBytes(last))).Status); var append = true;
        var transport = new ReceiptChainTransport(DecisionRoute(h, f.Fact), async (stream, after, limit, cursor, ct) => {
            var response = await ActualChain(h, f, stream, after, limit, cursor, ct);
            if (append) { append = false; await ApplicationAttempt(h, await ApplicationTool(h)); } return response;
        });
        var worker = new ApprovalReceiptRecoveryV2(h.Db, ScopeFence(h), transport) { PageLimit = 1 };
        Assert.Equal("RECOVERED", (await worker.RunAsync(h.Tenant)).State); Assert.Equal(3, transport.Queries.Count);
        Assert.All(transport.Queries, q => Assert.Equal("0", q.After)); Assert.Null(transport.Queries[0].Cursor); Assert.NotNull(transport.Queries[1].Cursor);
        var projection = await f.Owner.CurrentAsync(last.ReceiptStreamId); Assert.Equal(last, projection.Receipt); Assert.False(projection.PendingGap);
        await ReceiptCounts(h, 3, 3); await ApplicationCounts(h, 4); Assert.Empty(await h.Db.StockTransactions.ToArrayAsync());
        Assert.Equal("IDLE_OR_DISABLED", (await worker.RunAsync(h.Tenant)).State);
        Assert.Equal(3, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_approval_receipt_v2.gap_observations WHERE tenant_id={h.Tenant}").SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Receipt_recovery_lost_page_or_expired_cursor_restarts_at_durable_prefix_without_clearing_history()
    {
        foreach (var expiration in new[] { false, true })
        {
            await using var h = await Seed(); var f = await ReceiptSetup(h, 3); await RecoveryGrant(h, f);
            var last = f.Receipts[^1]; await f.Owner.ReceiveAsync(ReceiptBytes(last)); var fail = true;
            var transport = new ReceiptChainTransport(DecisionRoute(h, f.Fact), (stream, after, limit, cursor, ct) => {
                if (cursor != null && fail)
                {
                    fail = false;
                    if (expiration) return Task.FromResult(new ApprovalDecisionTransportResponseV2(409, ApprovalCanonicalV2.Bytes(new { code = "CURSOR_EXPIRED" })));
                    throw new HttpRequestException("page response lost after committed prefix");
                }
                return ActualChain(h, f, stream, after, limit, cursor, ct);
            });
            var worker = new ApprovalReceiptRecoveryV2(h.Db, ScopeFence(h), transport) { PageLimit = 1 };
            Assert.Equal(expiration ? "CURSOR_RESTART" : "RECEIPT_RECOVERY_OUTCOME_UNKNOWN", (await worker.RunAsync(h.Tenant)).State);
            Assert.Equal("1", (await f.Owner.CurrentAsync(last.ReceiptStreamId)).HighestContinuousRevision); await ReceiptCounts(h, 2, 1);
            await RecoveryDue(h); Assert.Equal("RECOVERED", (await worker.RunAsync(h.Tenant)).State);
            Assert.Equal(("1", (string?)null), transport.Queries[2]); await ReceiptCounts(h, 3, 3); await ApplicationCounts(h, 3);
        }
    }
    [CurrentPermissionPostgresFact]
    public async Task Receipt_recovery_mismatched_snapshot_is_quarantined_without_overwriting_verified_prefix()
    {
        await using var h = await Seed(); var f = await ReceiptSetup(h, 3); await RecoveryGrant(h, f); var last = f.Receipts[^1];
        await f.Owner.ReceiveAsync(ReceiptBytes(last)); var corrupt = true;
        var transport = new ReceiptChainTransport(DecisionRoute(h, f.Fact), async (stream, after, limit, cursor, ct) => {
            var response = await ActualChain(h, f, stream, after, limit, cursor, ct);
            if (corrupt && cursor != null)
            {
                var json = JsonNode.Parse(response.Body)!.AsObject(); json["snapshotToken"] = "different-snapshot";
                return response with { Body = ApprovalCanonicalV2.Bytes(json) };
            }
            return response;
        });
        var worker = new ApprovalReceiptRecoveryV2(h.Db, ScopeFence(h), transport) { PageLimit = 1 };
        Assert.Equal("RECEIPT_CHAIN_CONFLICT", (await worker.RunAsync(h.Tenant)).State);
        await ReceiptCounts(h, 2, 1); Assert.Equal(f.Receipts[0], (await f.Owner.CurrentAsync(last.ReceiptStreamId)).Receipt);
        await RecoveryDue(h); Assert.Equal("IDLE_OR_DISABLED", (await worker.RunAsync(h.Tenant)).State);
        corrupt = false; transport.Current = transport.Current with { Version = "source-corrected" };
        Assert.Equal("RECOVERED", (await worker.RunAsync(h.Tenant)).State); await ReceiptCounts(h, 3, 3);
    }
    [CurrentPermissionPostgresFact]
    public async Task Receipt_recovery_requires_explicit_recovery_grants_and_rechecks_revocation_after_network()
    {
        await using var h = await Seed(); var f = await ReceiptSetup(h, 2); var last = f.Receipts[^1]; await f.Owner.ReceiveAsync(ReceiptBytes(last));
        var revoke = true; var principal = f.Fact.Authority.Identity.Principal;
        var transport = new ReceiptChainTransport(DecisionRoute(h, f.Fact), async (stream, after, limit, cursor, ct) => {
            var response = await ActualChain(h, f, stream, after, limit, cursor, ct);
            if (revoke) { revoke = false; await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_approval_receipt_v2.subject_grants SET can_recover=false,revision={Guid.NewGuid()} WHERE tenant_id={h.Tenant} AND principal_id={principal}", ct); }
            return response;
        });
        var worker = new ApprovalReceiptRecoveryV2(h.Db, ScopeFence(h), transport);
        Assert.Equal("IDLE_OR_DISABLED", (await worker.RunAsync(h.Tenant)).State); Assert.Empty(transport.Queries);
        await RecoveryGrant(h, f); Assert.Equal("RECEIPT_RECOVERY_AUTHORITY_UNAVAILABLE", (await worker.RunAsync(h.Tenant)).State); await ReceiptCounts(h, 1, 0);
        Assert.Equal("IDLE_OR_DISABLED", (await worker.RunAsync(h.Tenant)).State);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_approval_receipt_v2.subject_grants SET can_recover=true,revision={Guid.NewGuid()} WHERE tenant_id={h.Tenant} AND principal_id={principal}");
        await RecoveryDue(h); Assert.Equal("RECOVERED", (await worker.RunAsync(h.Tenant)).State); await ReceiptCounts(h, 2, 2);
    }
    [CurrentPermissionPostgresFact]
    public async Task Receipt_recovery_concurrent_claims_allow_one_worker_and_resume_expired_lease()
    {
        await using var h = await Seed(); var f = await ReceiptSetup(h, 2); await RecoveryGrant(h, f); await f.Owner.ReceiveAsync(ReceiptBytes(f.Receipts[^1]));
        await using var other = server.Context(new TenantContext { CurrentTenantId = h.Tenant }); var peer = h with { Db = other };
        var transport = new ReceiptChainTransport(DecisionRoute(h, f.Fact), (stream, after, limit, cursor, ct) => ActualChain(h, f, stream, after, limit, cursor, ct));
        var a = new ApprovalReceiptRecoveryV2(h.Db, ScopeFence(h), transport); var b = new ApprovalReceiptRecoveryV2(other, ScopeFence(peer), transport);
        var claims = await Task.WhenAll(a.ClaimAsync(h.Tenant), b.ClaimAsync(h.Tenant)); var old = Assert.Single(claims, x => x != null)!;
        await RecoveryDue(h); Assert.Equal("RECOVERED", (await a.RunAsync(h.Tenant)).State);
        Assert.Equal("RECEIPT_RECOVERY_LEASE_LOST", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => b.FinishAsync(old, "late network result", default))).Message);
        await ReceiptCounts(h, 2, 2); Assert.Equal(2, await h.Db.Database.SqlQuery<long>($"SELECT epoch AS \"Value\" FROM cp6_approval_receipt_v2.gaps WHERE tenant_id={h.Tenant}").SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Receipt_recovery_page_budget_keeps_snapshot_remainder_queued_after_initial_gap_is_filled()
    {
        await using var h = await Seed(); var f = await ReceiptSetup(h, 5); await RecoveryGrant(h, f);
        await f.Owner.ReceiveAsync(ReceiptBytes(f.Receipts[1]));
        var transport = new ReceiptChainTransport(DecisionRoute(h, f.Fact), (stream, after, limit, cursor, ct) => ActualChain(h, f, stream, after, limit, cursor, ct));
        var worker = new ApprovalReceiptRecoveryV2(h.Db, ScopeFence(h), transport) { PageLimit = 1 };
        Assert.Equal("PREFIX_RECORDED", (await worker.RunAsync(h.Tenant)).State);
        var current = await f.Owner.CurrentAsync(f.Receipts[0].ReceiptStreamId); Assert.Equal("3", current.HighestContinuousRevision); Assert.True(current.PendingGap);
        Assert.Equal(5, await h.Db.Database.SqlQuery<long>($"SELECT target_revision AS \"Value\" FROM cp6_approval_receipt_v2.gaps WHERE tenant_id={h.Tenant}").SingleAsync());
        await RecoveryDue(h); Assert.Equal("RECOVERED", (await worker.RunAsync(h.Tenant)).State);
        Assert.Equal(("3", (string?)null), transport.Queries[3]); await ReceiptCounts(h, 5, 5); await ApplicationCounts(h, 5);
    }
}
