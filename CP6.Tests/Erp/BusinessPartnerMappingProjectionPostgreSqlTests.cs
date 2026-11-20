using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.ErpIntegration;
using CP6.Platform.EntityFramework;
using CP6.Tests.Platform;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private sealed class TestedMappingEvent
    {
        public Guid Id { get; init; }
        public byte[] Body { get; init; } = [];
        public string Hash { get; init; } = "";
    }

    private static Task<TestedMappingEvent[]> StoredEvents(Harness h, Guid account) => h.Db.Database.SqlQuery<TestedMappingEvent>($"""
        SELECT event_id AS "Id",body AS "Body",body_hash AS "Hash"
         FROM main_bp.mapping_events WHERE tenant_id={h.Tenant} AND account_id={account} ORDER BY revision,observation_sequence
        """).ToArrayAsync();

    private static async Task<Guid> ReconcileJob(AuthenticationApiProcess host, Dictionary<string, string> cookies,
        Guid account, string revision, Guid? eventId = null)
    {
        var created = await WorkspaceCall(host, cookies, "/mapping-reconcile", HttpMethod.Post, new
        {
            legalEntityKey = MappingLegal, accountId = account, expectedMappingRevision = revision,
            mode = eventId is null ? "QUERY_PROJECTION" : "RETRY_ORIGINAL_EVENT", eventId, reason = "模拟原映射受理核查"
        }, key: Guid.NewGuid());
        var reconcile = MappingReceipt(created).GetProperty("reconcile");
        Assert.False(reconcile.GetProperty("isBusinessChange").GetBoolean());
        Assert.Equal("QUEUED", reconcile.GetProperty("state").GetString());
        return reconcile.GetProperty("jobId").GetGuid();
    }

    private static Task<WorkspaceResponse> ReadReconcileJob(AuthenticationApiProcess host, Dictionary<string, string> cookies, Guid id)
        => WorkspaceCall(host, cookies, $"/reconcile-jobs/{id:D}");

    [CurrentPermissionPostgresFact]
    public async Task Business_partner_wp04_03_seven_field_original_late_projection_and_proven_correction_keep_exact_history()
    {
        await using var h = await Seed();
        await using var bridge = await MappingOwnerFixture.Start(h.Tenant);
        await EnableMapping(h, bridge);
        var account = Guid.NewGuid();
        bridge.Account(account, "00001200");
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, bridge.Environment());
        var cookies = await Login(host, h);
        var left = await AdoptMappingPartner(host, cookies, "000012A", "模拟纠错甲", "REGISTERED");
        var right = await AdoptMappingPartner(host, cookies, "000012B", "模拟纠错乙", "REGISTERED");
        var observed = await SignedAccountObservation(host, cookies, account);
        var initial = await ReadMappingContext(host, cookies, account, observed, MappingCriteria("000012A", "模拟纠错甲"));
        await ApplyMap(host, cookies, await GoodMapPreview(host, cookies, MappingInput(initial, "LINK", left, "模拟建立初始关系")), Guid.NewGuid());
        for (var version = 2; version <= 12; version++)
        {
            var even = version % 2 == 0;
            var target = even ? right : left;
            var reason = $"模拟逐版有据换指到 {version}";
            var context = await ReadMappingContext(host, cookies, account, observed,
                MappingCriteria(even ? "000012B" : "000012A", even ? "模拟纠错乙" : "模拟纠错甲"));
            var proof = await BindMapping(h, bridge, host, cookies, account,
                MappingSubject(account, "REPLACE", (version - 1).ToString(), target, reason));
            var result = MappingReceipt(await ApplyMap(host, cookies, await GoodMapPreview(host, cookies,
                MappingInput(context, "REPLACE", target, reason, binding: proof.Binding, evidence: [proof.Evidence])), Guid.NewGuid()));
            Assert.Equal(version.ToString(), result.GetProperty("mapping").GetProperty("mappingRevision").GetString());
        }
        var originals = await StoredEvents(h, account);
        Assert.Equal(12, originals.Length);
        var event12 = originals[^1];
        var exactFields = new[] { "accountId", "businessPartnerKey", "eventId", "legalEntityKey", "mappingRevision", "sourceAsOf", "state" };
        foreach (var value in originals)
        {
            using var json = JsonDocument.Parse(value.Body);
            Assert.Equal(exactFields, json.RootElement.EnumerateObject().Select(x => x.Name).Order(StringComparer.Ordinal));
            Assert.Equal(value.Hash, MappingSha(value.Body));
        }

        using var services = MappingProtection(h);
        var protection = services.GetRequiredService<IDataProtectionProvider>();
        using var transport = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        var adapter = bridge.Adapter(transport);
        var orderQueue = await PrepareC03Queue(h);
        var oldOrder = await PrepareUseOrder(h, (right, "000012B"), authoritativeAccount: account);
        var r12Head = (await MappingHead(host, cookies, account)).Body.GetProperty("head");
        await WriteUseGuard(h, bridge, account, 12, r12Head.GetProperty("semanticHash").GetString()!, true);
        var useSource = new ErpOrderAuthoritativePartnerUseSource(adapter, TimeProvider.System);
        Assert.Equal(Cp6InboxDisposition.Applied, (await ConsumeUseOrder(
            UseHandler(h, orderQueue, protection, source: useSource), UseOrderEnvelope(oldOrder))).Disposition);
        var oldOrderEffects = await UseEffectXids(h.Db, oldOrder);
        Assert.Equal(7, oldOrderEffects.Length);
        async Task<string> HistoricalUse() => await h.Db.Database.SqlQuery<string>($"""
            SELECT receipt AS "Value" FROM main_bp.use_receipts WHERE tenant_id={h.Tenant} AND use_key={oldOrder.RequestId}
            """).SingleAsync();
        var historicalUse = await HistoricalUse();
        async Task Worker(bool delivery)
        {
            await using var peer = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
            var workspace = MappingService(h, peer, protection, adapter);
            if (delivery) await workspace.RunMappingDeliveryOnce(default);
            else await workspace.RunMappingProjectionOnce(default);
        }

        bridge.DeliveryStatus = attempt => attempt == 1 ? 503 : 200;
        await Worker(true);
        var unknown = (await MappingHead(host, cookies, account)).Body;
        Assert.Equal("SENT_UNKNOWN", unknown.GetProperty("deliveries").EnumerateArray().Last().GetProperty("state").GetString());
        Assert.Equal("UNKNOWN", unknown.GetProperty("projection").GetProperty("knowledge").GetString());
        var resend = await ReconcileJob(host, cookies, account, "12", originals[0].Id);
        // 每次 Worker 使用独立连接和实例，原提交状态从 PostgreSQL 恢复。
        await Worker(true);
        Assert.Equal("COMPLETE", (await ReadReconcileJob(host, cookies, resend)).Body.GetProperty("state").GetString());
        for (var i = 1; i < originals.Length; i++) await Worker(true);
        var transmissions = bridge.Deliveries.ToArray();
        Assert.Equal(13, transmissions.Length);
        Assert.Equal(originals[0].Body, transmissions[0]);
        Assert.Equal(originals[0].Body, transmissions[1]);
        for (var i = 1; i < originals.Length; i++) Assert.Equal(originals[i].Body, transmissions[i + 1]);
        var acked = (await MappingHead(host, cookies, account)).Body;
        Assert.All(acked.GetProperty("deliveries").EnumerateArray(), d => Assert.Equal("ACKED_PROCESSING", d.GetProperty("state").GetString()));
        Assert.Equal("UNKNOWN", acked.GetProperty("projection").GetProperty("knowledge").GetString());
        var matched = await ReconcileJob(host, cookies, account, "12");
        await Worker(false);
        Assert.Equal("MATCHES_CURRENT", (await ReadReconcileJob(host, cookies, matched)).Body.GetProperty("observation").GetProperty("knowledge").GetString());
        await WorkspaceCall(host, cookies, "/mapping-reconcile", HttpMethod.Post, new
        {
            legalEntityKey = MappingLegal, accountId = account, expectedMappingRevision = "11", mode = "QUERY_PROJECTION", eventId = (Guid?)null,
            reason = "模拟旧修订不得建立新任务"
        }, key: Guid.NewGuid(), status: 412);

        var oldRequestReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOld = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bridge.ProjectionResponses.Enqueue(async _ =>
        {
            oldRequestReached.TrySetResult();
            await releaseOld.Task.WaitAsync(TimeSpan.FromSeconds(11));
            return MappingOwnerFixture.Projection(account, "12", "000012B", false);
        });
        var q1 = await ReconcileJob(host, cookies, account, "12");
        var earlier = Worker(false);
        Guid q2;
        WorkspaceResponse latest;
        try
        {
            await oldRequestReached.Task.WaitAsync(TimeSpan.FromSeconds(8));
            bridge.ProjectionResponses.Enqueue(_ => Task.FromResult<object?>(null));
            q2 = await ReconcileJob(host, cookies, account, "12");
            await Worker(false);
            latest = await ReadReconcileJob(host, cookies, q2);
            Assert.Equal("WAITING", latest.Body.GetProperty("state").GetString());
            Assert.Equal("PROJECTION_UNPROVEN", latest.Body.GetProperty("lastError").GetString());
            Assert.Equal("UNKNOWN", latest.Body.GetProperty("observation").GetProperty("knowledge").GetString());
            Assert.True(latest.Body.GetProperty("currentApplicable").GetBoolean());
        }
        finally { releaseOld.TrySetResult(); await earlier; }
        var late = (await ReadReconcileJob(host, cookies, q1)).Body;
        Assert.False(late.GetProperty("currentApplicable").GetBoolean());
        Assert.Equal("OBSERVATION_SUPERSEDED", late.GetProperty("lastError").GetString());
        Assert.Equal("MATCHES_CURRENT", late.GetProperty("observation").GetProperty("knowledge").GetString());
        var afterRace = (await MappingHead(host, cookies, account)).Body.GetProperty("projection");
        Assert.Equal("UNKNOWN", afterRace.GetProperty("knowledge").GetString());
        Assert.Equal(latest.Body.GetProperty("projectionGeneration").GetString(), afterRace.GetProperty("generation").GetString());
        Assert.True(await h.Db.Database.SqlQuery<bool>($"""
            SELECT EXISTS(SELECT FROM main_bp.mapping_projection_reads
             WHERE tenant_id={h.Tenant} AND job_id={q1} AND NOT current_applicable
                AND protected_original IS NOT NULL AND protected_trust IS NOT NULL) AS "Value"
            """).SingleAsync());

        Task<WorkspaceResponse> Correction() => WorkspaceCall(host, cookies, "/mapping-correction-contexts", HttpMethod.Post,
            new { legalEntityKey = MappingLegal, accountId = account, expectedMainRevision = "12" });
        bridge.ProjectionResponses.Enqueue(_ => Task.FromResult<object?>(MappingOwnerFixture.Projection(account, "13", "000012A", false)));
        var ahead = await ReconcileJob(host, cookies, account, "12");
        await Worker(false);
        Assert.Equal("AHEAD", (await ReadReconcileJob(host, cookies, ahead)).Body.GetProperty("observation").GetProperty("knowledge").GetString());
        bridge.ProjectionResponses.Enqueue(_ => Task.FromResult<object?>(MappingOwnerFixture.Projection(account, "13", "000012A", false)));
        Assert.Equal("UNPROVEN", (await Correction()).Body.GetProperty("resolution").GetString());

        var crm = bridge.Binding("CRM_PROJECTION");
        var conflictId = Guid.NewGuid();
        var wrongBytes = BusinessPartnerWorkspaceV1.Canonical(new
        {
            eventId = Guid.NewGuid(), accountId = account, legalEntityKey = MappingLegal, mappingRevision = "12",
            state = "LINKED", businessPartnerKey = "000012A", sourceAsOf = MappingInstant()
        });
        var reference = new { conflictId, recordVersion = "7", originalBodyHash = event12.Hash, incomingBodyHash = MappingSha(wrongBytes) };
        var trust = TestEvidence(crm.Authority, conflictId.ToString("D"), "BP_MAPPING_CONFLICT_RECEIVED_TRUST", new
        {
            tenantId = h.Tenant, organizationId = crm.OrganizationId, legalEntityKey = MappingLegal, accountId = account, mappingRevision = "12",
            conflictId, reference.originalBodyHash, reference.incomingBodyHash, producer = BusinessPartnerWorkspaceV1.DirectoryOwner,
            contract = "AccountErpMappingEvent", authorizationEpoch = crm.Epoch
        });
        var record = new
        {
            reference, accountId = account, legalEntityKey = MappingLegal, mappingRevision = "12",
            originalBodyBase64 = Convert.ToBase64String(event12.Body), incomingBodyBase64 = Convert.ToBase64String(wrongBytes),
            receivedTrustRef = trust.Reference
        };
        var journal = TestEvidence("Main.ApprovedEvidence", "fixture-correction-review-" + conflictId.ToString("D"), "BP_MAPPING_DECISION",
            new { originalEvent = event12.Id, reference, keep = "000012B", review = "模拟签发 Owner 已追溯完整原件" });
        var covered = new MappingTestRef(crm.Authority, "cover-" + account.ToString("D"), "1", MappingHash(new
        {
            accountId = account, legalEntityKey = MappingLegal, mappingRevision = "12", conflicts = new[] { reference }, complete = true
        }), "BP_MAPPING_CONFLICT_COVERAGE");
        object BridgeConflict(MappingTestRef coverage) => new { availability = "FOUND", records = new[] { record }, coverageRef = coverage, asOf = MappingInstant() };
        void ConflictProjection() => bridge.ProjectionResponses.Enqueue(_ =>
            Task.FromResult<object?>(MappingOwnerFixture.Projection(account, "12", "000012A", true)));
        bridge.Conflicts(account, BridgeConflict(covered with { Digest = new string('a', 64) }), journal, trust);
        await SetEvidenceGuard(h, bridge, account, "CRM_PROJECTION", covered.Id, covered.Version);
        await SetEvidenceGuard(h, bridge, account, "BP_MAPPING_DECISION", MappingOwnerFixture.JournalGuard(account), "1");
        ConflictProjection();
        Assert.Equal("UNPROVEN", (await Correction()).Body.GetProperty("resolution").GetString());
        bridge.Conflicts(account, BridgeConflict(covered), journal, trust);
        var ordinaryConfirm = await ReadMappingContext(host, cookies, account);
        var confirmation = MappingReceipt(await ApplyMap(host, cookies, await GoodMapPreview(host, cookies,
            MappingInput(ordinaryConfirm, "CONFIRM", right, "模拟普通确认保留争议和关系版")), Guid.NewGuid()));
        Assert.Equal("12", confirmation.GetProperty("mapping").GetProperty("mappingRevision").GetString());
        var stillConflict = await ReconcileJob(host, cookies, account, "12");
        await Worker(false);
        Assert.Equal("CONFLICT", (await ReadReconcileJob(host, cookies, stillConflict)).Body.GetProperty("observation").GetProperty("knowledge").GetString());

        var correctionBefore = await MappingCounts(h);
        foreach (var (field, replacement) in new (string, string?)[]
        {
            ("legalEntityKey", "JP-B"), ("mappingRevision", "11"),
            ("originalBodyBase64", null), ("receivedTrustRef", null)
        })
        {
            var malformed = System.Text.Json.JsonSerializer.SerializeToNode(BridgeConflict(covered), MappingJson)!.AsObject();
            var entry = malformed["records"]![0]!.AsObject();
            if (replacement is null) entry.Remove(field);
            else entry[field] = replacement;
            bridge.Conflicts(account, malformed, journal, trust);
            ConflictProjection();
            var unproved = await Correction();
            Assert.Equal("UNPROVEN", unproved.Body.GetProperty("resolution").GetString());
            Assert.Empty(unproved.Body.GetProperty("ownerEvidence").EnumerateArray());
        }
        Assert.Equal(correctionBefore, await MappingCounts(h));
        var unchanged = (await MappingHead(host, cookies, account)).Body.GetProperty("head");
        Assert.Equal("12", unchanged.GetProperty("mappingRevision").GetString());
        Assert.Equal(right, unchanged.GetProperty("bp").GetProperty("bpId").GetGuid());
        bridge.Conflicts(account, BridgeConflict(covered), journal, trust);
        await DeviceEvidence("wp04-38-ac090.json", new
        {
            rejected = new[] { "otherLegal", "otherRevision", "missingBody", "missingTrust", "crmAhead" },
            resolution = "UNPROVEN", mainRevision = "12", writes = 0
        });

        ConflictProjection();
        var correction = (await Correction()).Body;
        Assert.Equal("CORRECTABLE", correction.GetProperty("resolution").GetString());
        Assert.Equal(event12.Id, correction.GetProperty("canonicalEventId").GetGuid());
        Assert.Equal(event12.Hash, correction.GetProperty("canonicalBodyHash").GetString());
        Assert.Equal(conflictId, Assert.Single(correction.GetProperty("conflicts").EnumerateArray()).GetProperty("conflictId").GetGuid());
        var correctionId = correction.GetProperty("correctionContextId").GetGuid();
        var context12 = await ReadMappingContext(host, cookies, account);
        const string correctionReason = "模拟已证原件的当前关系纠错";
        var decision = await BindMapping(h, bridge, host, cookies, account,
            MappingSubject(account, "CORRECT_CURRENT", "12", right, correctionReason, correctionId), journal);
        var preview = await GoodMapPreview(host, cookies, MappingInput(context12, "CORRECT_CURRENT", right, correctionReason,
            correction: correctionId, binding: decision.Binding, evidence: [decision.Evidence]));
        await SetEvidenceGuard(h, bridge, account, "CRM_PROJECTION", covered.Id, "2", active: false);
        await ApplyMap(host, cookies, preview, Guid.NewGuid(), 409);
        var checksBefore = await MappingCounts(h);
        await SetEvidenceGuard(h, bridge, account, "CRM_PROJECTION", covered.Id, covered.Version);
        await ApplyMap(host, cookies, preview, Guid.NewGuid(), 409);
        ConflictProjection();
        var renewed = (await Correction()).Body;
        Assert.Equal("CORRECTABLE", renewed.GetProperty("resolution").GetString());
        var renewedId = renewed.GetProperty("correctionContextId").GetGuid();
        var renewedDecision = await BindMapping(h, bridge, host, cookies, account,
            MappingSubject(account, "CORRECT_CURRENT", "12", right, correctionReason, renewedId), journal);
        preview = await GoodMapPreview(host, cookies, MappingInput(context12, "CORRECT_CURRENT", right, correctionReason,
            correction: renewedId, binding: renewedDecision.Binding, evidence: [renewedDecision.Evidence]));
        var correctionKey = Guid.NewGuid();
        var corrected = MappingReceipt(await ApplyMap(host, cookies, preview, correctionKey));
        Assert.Equal("13", corrected.GetProperty("mapping").GetProperty("mappingRevision").GetString());
        Assert.Equal(right, corrected.GetProperty("bp").GetProperty("bpId").GetGuid());
        Assert.Equal(checksBefore[0] + 1, (await MappingCounts(h))[0]);
        Assert.Equal(corrected.GetRawText(), MappingReceipt(await ApplyMap(host, cookies, preview, correctionKey)).GetRawText());
        var current = (await MappingHead(host, cookies, account)).Body;
        Assert.Equal("UNKNOWN", current.GetProperty("projection").GetProperty("knowledge").GetString());
        await Worker(true); // 先发送确认观察。
        await Worker(true); // 再发送真实 r13。
        var validated = await ReconcileJob(host, cookies, account, "13");
        await Worker(false);
        Assert.Equal("MATCHES_CURRENT", (await ReadReconcileJob(host, cookies, validated)).Body.GetProperty("observation").GetProperty("knowledge").GetString());

        var context13 = await ReadMappingContext(host, cookies, account);
        const string endReason = "模拟正常解绑后继";
        var unlink = await BindMapping(h, bridge, host, cookies, account, MappingSubject(account, "UNLINK", "13", null, endReason));
        var release = MappingReceipt(await ApplyMap(host, cookies, await GoodMapPreview(host, cookies, MappingInput(
            context13, "UNLINK", null, endReason, binding: unlink.Binding, evidence: [unlink.Evidence])), Guid.NewGuid()));
        Assert.Equal("14", release.GetProperty("mapping").GetProperty("mappingRevision").GetString());
        Assert.Equal(JsonValueKind.Null, release.GetProperty("mapping").GetProperty("businessPartnerKey").ValueKind);
        Assert.Equal(0, (await MappingCounts(h))[1]);
        Assert.Equal(historicalUse, await HistoricalUse());
        Assert.Equal(oldOrderEffects.Order(), (await UseEffectXids(h.Db, oldOrder)).Order());
        await Worker(true);
        var older = await ReconcileJob(host, cookies, account, "14", event12.Id);
        await Worker(true);
        Assert.Equal("COMPLETE", (await ReadReconcileJob(host, cookies, older)).Body.GetProperty("state").GetString());
        var finalQuery = await ReconcileJob(host, cookies, account, "14");
        await Worker(false);
        Assert.Equal("MATCHES_CURRENT", (await ReadReconcileJob(host, cookies, finalQuery)).Body.GetProperty("observation").GetProperty("knowledge").GetString());
        Assert.Equal(event12.Body, (await StoredEvents(h, account)).Single(x => x.Id == event12.Id).Body);
        await DeviceEvidence("wp04-03-reconcile-correction.json", new { account, right, event12 = event12.Id, event12.Hash,
            sameBytesOnReplay = true, exactFields, deliveryAckNotCurrent = true, q1, q2, late, newest = latest.Body, aheadUnproven = true,
            incompleteCoverageUnproven = true, bundleRevocationRejected = true, confirmedWithoutRevisionChange = true, correction, corrected, release,
            historicalUseAndOrderUnchanged = true, originalUseKey = oldOrder.RequestId });
    }
}
