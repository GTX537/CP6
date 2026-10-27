using System.Text;
using System.Text.Json;
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
    private sealed record FactFixture(ToolApprovalFactReceiverV2 Owner, IntakeAuthority Authority,
        byte[] Payload, ApprovalDecisionEnvelopeV2 Decision);
    private static ToolApprovalFactReceiverV2 FactReceiver(Harness h, IApprovalServiceAuthorityV2 authority)
        => new(h.Db, ScopeFence(h), authority, new(h.Db, ScopeFence(h)));
    private async Task<FactFixture> FactSetup(Harness h, bool approve = true)
    {
        var (intake, first, second) = await DecisionSetup(h);
        var a = DecisionActor(h, first); await Vote(a, await DecisionInput(a, approve ? "approve" : "reject"));
        if (approve) { var b = DecisionActor(h, second); await Vote(b, await DecisionInput(b)); }
        var payload = await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_approval_decision_v2.outbox WHERE tenant_id={h.Tenant}").SingleAsync();
        var decision = JsonSerializer.Deserialize<ApprovalDecisionEnvelopeV2>(payload, ApprovalBindingOwnerV2.Json)!;
        // The Workflow producer has its own registration; the original Tool intake client grants nothing here.
        var service = new IntakeAuthority(intake.Authority.Identity with { Principal = Guid.NewGuid(), Client = "workflow-producer" });
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_tool_approval_fact_v2.registrations(environment_id,tenant_id,principal_id,revision,enabled,can_receive,can_query,evidence_mode)
            VALUES({ScopeEnvironment},{h.Tenant},{service.Identity.Principal},{Guid.NewGuid()},true,true,true,'DEMO');
            INSERT INTO cp6_tool_approval_fact_v2.subject_grants(environment_id,tenant_id,principal_id,submission_id,revision,enabled,can_receive,can_query)
            VALUES({ScopeEnvironment},{h.Tenant},{service.Identity.Principal},{decision.SubmissionId},{Guid.NewGuid()},true,true,true);
            """);
        return new(FactReceiver(h, service), service, payload, decision);
    }
    private static byte[] FactPayload(byte[] original, Action<JsonObject> change)
    {
        var n = JsonNode.Parse(original)!.AsObject(); change(n); n.Remove("payloadDigest");
        var digest = ApprovalCanonicalV2.Digest(ApprovalCanonicalV2.Bytes(n));
        n["payloadDigest"] = JsonSerializer.SerializeToNode(digest, ApprovalBindingOwnerV2.Json);
        return ApprovalCanonicalV2.Bytes(n);
    }
    private static ApprovalFactAckV2 FactAck(ApprovalFactReceiveResultV2 r)
    { Assert.Equal(200, r.Status); return Assert.IsType<ApprovalFactAckV2>(r.Body); }
    private static async Task FactCounts(Harness h, int facts, int inbox, int streams)
    {
        Assert.Equal(facts, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_tool_approval_fact_v2.facts WHERE tenant_id={h.Tenant}").SingleAsync());
        Assert.Equal(inbox, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_tool_approval_fact_v2.inbox WHERE tenant_id={h.Tenant}").SingleAsync());
        Assert.Equal(streams, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_tool_approval_fact_v2.streams WHERE tenant_id={h.Tenant}").SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task App_fact_native_approved_and_rejected_originals_are_received_without_business_application()
    {
        foreach (var approve in new[] { true, false })
        {
            await using var h = await Seed(); var f = await FactSetup(h, approve);
            // Past human submit/decision rights are not the current service's precise read/write grant.
            await h.Db.Sys_RoleActions.Where(x => x.ActionCode == "approve" || x.ActionCode == "dispose-submit").ExecuteDeleteAsync();
            var raw = Encoding.UTF8.GetBytes(" \n" + Encoding.UTF8.GetString(f.Payload) + "\n");
            var ack = FactAck(await f.Owner.ReceiveAsync(raw));
            Assert.Equal("RECORDED", ack.ReceiveState); Assert.Equal("NOT_APPLIED", ack.Application); Assert.Null(ack.ApplicationReceiptId);
            Assert.NotNull(ack.ReceiptStreamId); Assert.Equal(f.Decision.DecisionId, ack.DecisionId);
            Assert.Equal(ack, FactAck(await f.Owner.ReceivedAsync(f.Decision.EventId)));
            Assert.Equal("NOT_APPLIED", (await f.Owner.ApplicationAsync(ack.ReceiptStreamId.Value)).Kind);
            Assert.Equal(raw, await h.Db.Database.SqlQuery<byte[]>($"SELECT raw AS \"Value\" FROM cp6_tool_approval_fact_v2.inbox WHERE tenant_id={h.Tenant}").SingleAsync());
            Assert.Equal(f.Payload, await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_tool_approval_fact_v2.facts WHERE tenant_id={h.Tenant}").SingleAsync());
            var original = await h.Db.Database.SqlQuery<string>($"SELECT evidence_json AS \"Value\" FROM cp6_approval_decision_v2.decisions WHERE tenant_id={h.Tenant}").SingleAsync();
            Assert.Equal(original, await h.Db.Database.SqlQuery<string>($"SELECT evidence_json AS \"Value\" FROM cp6_tool_approval_fact_v2.facts WHERE tenant_id={h.Tenant}").SingleAsync());
            await FactCounts(h, 1, 1, 1); Assert.Empty(await h.Db.StockTransactions.ToArrayAsync());
            Assert.Equal(404, (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => f.Owner.ApplicationAsync(Guid.NewGuid()))).Status);
            Assert.IsType<ApprovalFactNotObservedV2>((await f.Owner.ReceivedAsync(Guid.NewGuid())).Body);
            await Assert.ThrowsAsync<Npgsql.PostgresException>(() => h.Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM cp6_tool_approval_fact_v2.inbox WHERE tenant_id={h.Tenant}"));
        }
    }

    [CurrentPermissionPostgresFact]
    public async Task App_fact_event_and_logical_duplicates_keep_one_stream_conflicts_keep_original()
    {
        await using var h = await Seed(); var f = await FactSetup(h); var first = FactAck(await f.Owner.ReceiveAsync(f.Payload));
        var repeat = FactAck(await f.Owner.ReceiveAsync(f.Payload)); Assert.Equal(first with { ReceiveState = "DUPLICATE" }, repeat);
        var alias = FactPayload(f.Payload, n => n["eventId"] = Guid.NewGuid());
        var logical = FactAck(await f.Owner.ReceiveAsync(alias)); Assert.Equal("DUPLICATE", logical.ReceiveState);
        Assert.NotEqual(first.InboxId, logical.InboxId); Assert.Equal(first.ReceiptStreamId, logical.ReceiptStreamId);
        var altered = FactPayload(f.Payload, n => n["reason"] = "different outcome evidence");
        Assert.Equal(409, (await f.Owner.ReceiveAsync(altered)).Status);
        Assert.Equal(first, FactAck(await f.Owner.ReceivedAsync(f.Decision.EventId)));
        await FactCounts(h, 1, 2, 1);
        Assert.Equal(altered, await h.Db.Database.SqlQuery<byte[]>($"SELECT raw AS \"Value\" FROM cp6_tool_approval_fact_v2.conflicts WHERE tenant_id={h.Tenant}").SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task App_fact_lost_commit_ack_recovers_after_receive_revocation_but_current_read_is_required()
    {
        var fault = new CommitFault(); await using var h = await Seed(fault); var f = await FactSetup(h); fault.Armed = true;
        Assert.Equal("OUTCOME_UNKNOWN", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => f.Owner.ReceiveAsync(f.Payload))).Message);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_tool_approval_fact_v2.subject_grants SET can_receive=false,revision={Guid.NewGuid()} WHERE tenant_id={h.Tenant}");
        f.Authority.Identity = f.Authority.Identity with { CanSubmit = false };
        var first = FactAck(await f.Owner.ReceivedAsync(f.Decision.EventId));
        Assert.Equal(first with { ReceiveState = "DUPLICATE" }, FactAck(await f.Owner.ReceiveAsync(f.Payload)));
        Assert.Equal(403, (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => f.Owner.ReceiveAsync(FactPayload(f.Payload, n => n["eventId"] = Guid.NewGuid())))).Status);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_tool_approval_fact_v2.subject_grants SET can_query=false,revision={Guid.NewGuid()} WHERE tenant_id={h.Tenant}");
        Assert.Equal(404, (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => f.Owner.ReceivedAsync(f.Decision.EventId))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => f.Owner.ApplicationAsync(first.ReceiptStreamId!.Value))).Status);
        await FactCounts(h, 1, 1, 1);
    }

    [CurrentPermissionPostgresFact]
    public async Task App_fact_fault_missing_inbox_and_late_authority_aba_roll_back_the_whole_receipt()
    {
        var hook = new StartWriteHook(); await using var h = await Seed(hook); var f = await FactSetup(h);
        hook.Before = c => { if (c.CommandText.Contains("INSERT INTO cp6_tool_approval_fact_v2.inbox")) throw new InvalidOperationException("fact crash"); return Task.CompletedTask; };
        Assert.Equal("fact crash", (await Assert.ThrowsAsync<InvalidOperationException>(() => f.Owner.ReceiveAsync(f.Payload))).Message);
        await FactCounts(h, 0, 0, 0);
        hook.Before = c => { if (c.CommandText.Contains("INSERT INTO cp6_tool_approval_fact_v2.inbox")) c.CommandText = "SELECT 1"; return Task.CompletedTask; };
        var missing = await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => f.Owner.ReceiveAsync(f.Payload));
        Assert.Contains("OWNER_FACT_INBOX_STREAM_REQUIRED", missing.ToString()); await FactCounts(h, 0, 0, 0);
        hook.Before = async c => {
            if (!c.CommandText.Contains("INSERT INTO cp6_tool_approval_fact_v2.inbox")) return;
            hook.Before = null;
            await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_tool_approval_fact_v2.subject_grants SET can_receive=false WHERE tenant_id={h.Tenant}");
            await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_tool_approval_fact_v2.subject_grants SET can_receive=true WHERE tenant_id={h.Tenant}");
        };
        Assert.Equal("SCOPE_AUTHORITY_CHANGED", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => f.Owner.ReceiveAsync(f.Payload))).Message);
        await FactCounts(h, 0, 0, 0); hook.Before = null; FactAck(await f.Owner.ReceiveAsync(f.Payload));
    }

    [CurrentPermissionPostgresFact]
    public async Task App_fact_two_actual_connections_receive_once()
    {
        await using var h = await Seed(); var f = await FactSetup(h);
        await using var other = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        var peer = FactReceiver(h with { Db = other }, new IntakeAuthority(f.Authority.Identity));
        var results = await Task.WhenAll(f.Owner.ReceiveAsync(f.Payload), peer.ReceiveAsync(f.Payload));
        var acks = results.Select(FactAck).ToArray(); Assert.Single(acks.Where(x => x.ReceiveState == "RECORDED"));
        Assert.Single(acks.Where(x => x.ReceiveState == "DUPLICATE")); Assert.Equal(acks[0].InboxId, acks[1].InboxId);
        await FactCounts(h, 1, 1, 1);
    }

    [CurrentPermissionPostgresFact]
    public async Task App_fact_native_source_scope_and_exact_subject_are_required_and_gap_is_not_applied()
    {
        await using var h = await Seed(); var f = await FactSetup(h);
        var foreign = FactPayload(f.Payload, n => n["scope"]!["tenantId"] = Guid.NewGuid());
        Assert.Equal(403, (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => f.Owner.ReceiveAsync(foreign))).Status);
        var wrongSubject = FactPayload(f.Payload, n => n["subject"]!["businessRevision"] = "wrong");
        Assert.Equal(409, (await f.Owner.ReceiveAsync(wrongSubject)).Status);
        var forged = FactPayload(f.Payload, n => n["terminalActor"]!["actualHandlerId"] = Guid.NewGuid());
        Assert.Equal(422, (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => f.Owner.ReceiveAsync(forged))).Status);
        var changedDecision = FactPayload(f.Payload, n => n["reason"] = "forged native proof");
        Assert.Equal(409, (await f.Owner.ReceiveAsync(changedDecision)).Status);
        var absentSource = FactPayload(f.Payload, n => n["decisionId"] = Guid.NewGuid());
        Assert.Equal(503, (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => f.Owner.ReceiveAsync(absentSource))).Status);
        var gap = FactPayload(f.Payload, n => { n["eventId"] = Guid.NewGuid(); n["sequence"] = "99999999999999999999999999999999"; });
        var pending = await f.Owner.ReceiveAsync(gap); Assert.Equal(202, pending.Status);
        var ack = Assert.IsType<ApprovalFactAckV2>(pending.Body); Assert.Equal("GAP", ack.ReceiveState); Assert.Null(ack.ReceiptStreamId); Assert.Equal("NOT_APPLIED", ack.Application);
        Assert.Equal(202, (await f.Owner.ReceivedAsync(ack.EventId)).Status); await FactCounts(h, 0, 1, 0);
        FactAck(await f.Owner.ReceiveAsync(f.Payload)); await FactCounts(h, 1, 2, 1);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM cp6_tool_approval_fact_v2.subject_grants WHERE tenant_id={h.Tenant}");
        Assert.Equal(404, (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => f.Owner.ReceiveAsync(f.Payload))).Status);
    }

    [CurrentPermissionPostgresFact]
    public async Task App_fact_demo_source_cannot_become_real_and_disabled_guard_fails_closed()
    {
        await using var h = await Seed(); var f = await FactSetup(h);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_tool_approval_fact_v2.registrations SET evidence_mode='REAL',revision={Guid.NewGuid()} WHERE tenant_id={h.Tenant}");
        f.Authority.Identity = f.Authority.Identity with { EvidenceMode = "REAL" };
        Assert.Equal(403, (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => f.Owner.ReceiveAsync(f.Payload))).Status);
        f.Authority.Identity = f.Authority.Identity with { EvidenceMode = "DEMO" };
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_tool_approval_fact_v2.registrations SET evidence_mode='DEMO',revision={Guid.NewGuid()} WHERE tenant_id={h.Tenant}");
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_tool_approval_fact_v2.inbox DISABLE TRIGGER fact_inbox_complete");
        try { Assert.Equal("OWNER_FACT_STORE_UNAVAILABLE", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => f.Owner.ReceiveAsync(f.Payload))).Message); }
        finally { await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_tool_approval_fact_v2.inbox ENABLE TRIGGER fact_inbox_complete"); }
        await FactCounts(h, 0, 0, 0); FactAck(await f.Owner.ReceiveAsync(f.Payload));
    }

    [CurrentPermissionPostgresFact]
    public async Task App_fact_closed_wire_duplicate_fields_and_digest_tampering_never_write()
    {
        await using var h = await Seed(); var f = await FactSetup(h);
        var text = Encoding.UTF8.GetString(f.Payload);
        var duplicate = Encoding.UTF8.GetBytes("{\"schema\":\"oa.app/2.0\"," + text[1..]);
        Assert.Equal(400, (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => f.Owner.ReceiveAsync(duplicate))).Status);
        var extra = FactPayload(f.Payload, n => n["unregistered"] = true);
        Assert.Equal(422, (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => f.Owner.ReceiveAsync(extra))).Status);
        var node = JsonNode.Parse(f.Payload)!; node["reason"] = "tampered";
        Assert.Equal("PAYLOAD_DIGEST_MISMATCH", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => f.Owner.ReceiveAsync(ApprovalCanonicalV2.Bytes(node)))).Message);
        await FactCounts(h, 0, 0, 0);
    }
}
