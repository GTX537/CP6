using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Tests.Platform;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static Task<WorkspaceResponse> ProbeAccount(AuthenticationApiProcess host, Dictionary<string, string> cookies, Guid account)
        => WorkspaceCall(host, cookies, "/account-probes", HttpMethod.Post, new { legalEntityKey = MappingLegal, accountId = account });

    private static object MappingCriteria(string? code, string name, string? standard = null, string? type = null, string? ein = null)
        => new { bpCd = code, bpName = name, stdCoCd = standard, einType = type, ein };

    private static Task<WorkspaceResponse> ReadMappingContext(AuthenticationApiProcess host, Dictionary<string, string> cookies, Guid account,
        Guid? observation = null, object? criteria = null)
        => WorkspaceCall(host, cookies, "/mapping-contexts", HttpMethod.Post,
            new { legalEntityKey = MappingLegal, accountId = account, observationId = observation, candidateCriteria = criteria });

    private static Task<WorkspaceResponse> MappingHead(AuthenticationApiProcess host, Dictionary<string, string> cookies, Guid account)
        => WorkspaceCall(host, cookies, $"/mapping?legalEntityKey={MappingLegal}&accountId={account:D}");

    private static object MappingSubject(Guid account, string action, string? revision, Guid? target, string reason, Guid? correction = null)
        => new { legalEntityKey = MappingLegal, accountId = account, action, expectedMappingRevision = revision,
            targetBpId = target, correctionContextId = correction, reason };

    private static object MappingInput(WorkspaceResponse context, string action, Guid? target, string reason,
        object? newPartner = null, Guid? correction = null, Guid? binding = null, MappingTestRef[]? evidence = null)
    {
        var head = context.Body.GetProperty("head");
        var observation = context.Body.GetProperty("observation");
        return new
        {
            context = context.Body.GetProperty("reference"), legalEntityKey = head.GetProperty("legalEntityKey").GetString(),
            accountId = head.GetProperty("accountId").GetGuid(),
            observationId = observation.ValueKind == JsonValueKind.Null ? (Guid?)null : observation.GetProperty("observationId").GetGuid(),
            expectedMappingRevision = head.GetProperty("mappingRevision").GetString(), action, targetBpId = target, newPartner,
            correctionContextId = correction, decisionBindingId = binding,
            candidateSetHash = context.Body.GetProperty("candidates").GetProperty("setHash").GetString(),
            decisionEvidence = evidence ?? [], reason
        };
    }

    private static Task<WorkspaceResponse> MapPreview(AuthenticationApiProcess host, Dictionary<string, string> cookies, object intent, int? status = 200)
        => WorkspaceCall(host, cookies, "/mapping-previews", HttpMethod.Post, intent, status: status);

    private static async Task<WorkspaceResponse> GoodMapPreview(AuthenticationApiProcess host, Dictionary<string, string> cookies, object intent)
    {
        var result = await MapPreview(host, cookies, intent);
        Assert.True(result.Body.GetProperty("canApply").GetBoolean(), result.Body.GetRawText());
        return result;
    }

    private static Task<WorkspaceResponse> ApplyMap(AuthenticationApiProcess host, Dictionary<string, string> cookies,
        WorkspaceResponse preview, Guid key, int? status = 200)
        => ApplyWorkspace(host, cookies, preview, key, status, "/mapping-apply");

    private static async Task<Guid> AdoptMappingPartner(AuthenticationApiProcess host, Dictionary<string, string> cookies,
        string code, string name, string registration = "PRE_REGISTERED")
    {
        var options = await WorkspaceCall(host, cookies, $"/options?legalEntityKey={MappingLegal}");
        return PartnerId(await ApplyWorkspace(host, cookies, await WorkspacePreview(host, cookies,
            WorkspaceInput(options, code, name, registration: registration, complete: true)), Guid.NewGuid()));
    }

    private static async Task<Guid> SignedAccountObservation(AuthenticationApiProcess host, Dictionary<string, string> cookies, Guid account)
    {
        var probe = await ProbeAccount(host, cookies, account);
        Assert.Equal("FOUND", probe.Body.GetProperty("availability").GetString());
        Assert.DoesNotContain("originalUnpublishedFields", probe.Body.GetRawText(), StringComparison.Ordinal);
        return probe.Body.GetProperty("observation").GetProperty("observationId").GetGuid();
    }

    private static async Task<(Guid Binding, MappingTestRef Evidence)> BindMapping(Harness h, MappingOwnerFixture bridge,
        AuthenticationApiProcess host, Dictionary<string, string> cookies, Guid account, object subject, MappingTestRecord? record = null)
    {
        record ??= TestEvidence("Main.ApprovedEvidence", Guid.NewGuid().ToString("D"), "BP_MAPPING_DECISION", new
        {
            sourceType = "模拟本次已批准的原决定", subject, originalRef = Guid.NewGuid()
        });
        bridge.Evidence(record, subject);
        await SetEvidenceGuard(h, bridge, account, "BP_MAPPING_DECISION", MappingOwnerFixture.EvidenceGuard(account), "1");
        var binding = await WorkspaceCall(host, cookies, "/mapping-decision-bindings", HttpMethod.Post, new { subject, evidence = new[] { record.Reference } });
        Assert.Equal("VERIFIED", binding.Body.GetProperty("availability").GetString());
        return (binding.Body.GetProperty("bindingId").GetGuid(), record.Reference);
    }

    private static JsonElement MappingReceipt(WorkspaceResponse result) => result.Body.GetProperty("receipt");

    private static Task<int[]> MappingCounts(Harness h) => h.Db.Database.SqlQuery<int>($"""
        SELECT count(*)::integer AS "Value" FROM main_bp.mapping_revisions WHERE tenant_id={h.Tenant}
        UNION ALL SELECT count(*)::integer FROM main_bp.mapping_reverse_claims WHERE tenant_id={h.Tenant}
        UNION ALL SELECT count(*)::integer FROM main_bp.mapping_events WHERE tenant_id={h.Tenant}
        UNION ALL SELECT count(*)::integer FROM main_bp.mapping_audit WHERE tenant_id={h.Tenant}
        UNION ALL SELECT count(*)::integer FROM main_bp.commands WHERE tenant_id={h.Tenant} AND account_id IS NOT NULL
        UNION ALL SELECT count(*)::integer FROM main_bp.mapping_owner_decisions WHERE tenant_id={h.Tenant}
        """).ToArrayAsync();

    [CurrentPermissionPostgresFact]
    public async Task Business_partner_wp04_03_B15_B18_keep_exact_context_and_one_claim_across_process_restart()
    {
        await using var h = await Seed();
        await using var bridge = await MappingOwnerFixture.Start(h.Tenant);
        await EnableMapping(h, bridge);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var forged = Guid.NewGuid();
        var forbidden = Guid.NewGuid();
        bridge.Account(first, "00000007", "INACTIVE");
        bridge.Account(second, "00000008");
        bridge.Account(forged, "FAKE-AC-1", wrongSignature: true);
        bridge.DenyAccount(forbidden);

        object firstIntent;
        WorkspaceResponse secondPreview;
        Guid bp;
        Guid originalObservation;
        MappingTestObservation persisted;
        Dictionary<string, string> currentCookies;
        var conn = h.Db.Database.GetConnectionString()!;
        var configuration = bridge.Environment();
        configuration["JWT__Secret"] = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        await using (var host = await AuthenticationApiProcess.Start(conn, configuration))
        {
            var cookies = currentCookies = await Login(host, h);
            Assert.Equal("NOT_FOUND", (await ProbeAccount(host, cookies, Guid.NewGuid())).Body.GetProperty("availability").GetString());
            Assert.Equal("UNPROVEN", (await ProbeAccount(host, cookies, forged)).Body.GetProperty("availability").GetString());
            var deniedProbe = (await ProbeAccount(host, cookies, forbidden)).Body;
            Assert.Equal("NOT_AUTHORIZED", deniedProbe.GetProperty("availability").GetString());
            Assert.Equal(JsonValueKind.Null, deniedProbe.GetProperty("observation").ValueKind);
            bp = await AdoptMappingPartner(host, cookies, "000071", "模拟原条件甲");
            originalObservation = await SignedAccountObservation(host, cookies, first);
            using var dp = MappingProtection(h);
            var protector = dp.GetRequiredService<IDataProtectionProvider>();
            persisted = await h.Db.Database.SqlQuery<MappingTestObservation>($"""
                SELECT protected_original AS "Original", protected_trust AS "Trust", body_hash AS "BodyHash",
                    account_status AS "Status", identity_version AS "Version" FROM main_bp.mapping_account_observations
                 WHERE tenant_id={h.Tenant} AND observation_id={originalObservation}
                """).SingleAsync();
            byte[] Open(string domain, byte[] value) => protector.CreateProtector(BusinessPartnerWorkspaceV1.ProfileOwner,
                h.Tenant.ToString("D"), domain, originalObservation.ToString("D")).Unprotect(value);
            var original = Open("mapping-account", persisted.Original);
            Assert.Equal(bridge.AccountOriginals[first], original);
            Assert.Equal(MappingSha(original), persisted.BodyHash);
            using var trust = JsonDocument.Parse(Open("mapping-account-trust", persisted.Trust));
            Assert.Equal(h.Tenant, trust.RootElement.GetProperty("tenantId").GetGuid());
            Assert.Equal(bridge.Organization, trust.RootElement.GetProperty("organizationId").GetGuid());
            Assert.Equal(persisted.BodyHash, trust.RootElement.GetProperty("bodyHash").GetString());
            Assert.Equal("Ed25519", trust.RootElement.GetProperty("algorithm").GetString());
            Assert.Equal("INACTIVE", persisted.Status);
            Assert.Equal("9007199254740993", persisted.Version);
            var secondProof = await SignedAccountObservation(host, cookies, second);
            var precise = await ReadMappingContext(host, cookies, first, originalObservation, MappingCriteria("000071", "模拟原条件甲"));
            var alternative = await ReadMappingContext(host, cookies, first, originalObservation, MappingCriteria("000071", "另一窗口条件"));
            var mixed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(
                MappingInput(precise, "LINK", bp, "模拟身份关联"), MappingJson))!;
            mixed["candidateSetHash"] = alternative.Body.GetProperty("candidates").GetProperty("setHash");
            Assert.Equal("MAPPING_CONTEXT_MISMATCH", (await MapPreview(host, cookies, mixed, 409)).Body.GetProperty("code").GetString());
            Assert.NotEqual(precise.Body.GetProperty("reference").GetProperty("contextHash").GetString(),
                alternative.Body.GetProperty("reference").GetProperty("contextHash").GetString());
            firstIntent = MappingInput(precise, "LINK", bp, "模拟身份关联");
            var rival = await ReadMappingContext(host, cookies, second, secondProof, MappingCriteria("000071", "模拟原条件甲"));
            secondPreview = await GoodMapPreview(host, cookies, MappingInput(rival, "LINK", bp, "模拟竞争关联"));
            Assert.Equal(new[] { 0, 0, 0, 0, 0, 0 }, await MappingCounts(h));
        }

        await using var restarted = await AuthenticationApiProcess.Start(conn, configuration);
        var firstPreview = await GoodMapPreview(restarted, currentCookies, firstIntent);
        var commandA = Guid.NewGuid();
        var commandB = Guid.NewGuid();
        var results = await Task.WhenAll(ApplyMap(restarted, currentCookies, firstPreview, commandA, null),
            ApplyMap(restarted, currentCookies, secondPreview, commandB, null));
        Assert.Equal(new[] { 200, 409 }, results.Select(x => x.Status).Order().ToArray());
        var loser = results.Single(x => x.Status == 409);
        Assert.Equal("MAPPING_TARGET_CLAIMED", loser.Body.GetProperty("code").GetString());
        Assert.DoesNotContain(first.ToString("D"), loser.Body.GetRawText());
        Assert.DoesNotContain(second.ToString("D"), loser.Body.GetRawText());
        Assert.Equal(new[] { 1, 1, 1, 1, 1, 1 }, await MappingCounts(h));

        var win = results[0].Status == 200 ? first : second;
        var winningKey = results[0].Status == 200 ? commandA : commandB;
        var winningPreview = results[0].Status == 200 ? firstPreview : secondPreview;
        var lost = win == first ? second : first;
        var exposed = (await ReadMappingContext(restarted, currentCookies, lost, criteria: MappingCriteria("000071", "模拟原条件甲"))).Body;
        Assert.DoesNotContain(win.ToString("D"), exposed.GetRawText());
        Assert.Equal("CLAIMED", Assert.Single(exposed.GetProperty("candidates").GetProperty("items").EnumerateArray())
            .GetProperty("availability").GetString());
        var winHead = (await MappingHead(restarted, currentCookies, win)).Body.GetProperty("head");
        Assert.Equal("1", winHead.GetProperty("mappingRevision").GetString());
        Assert.Equal(bp, winHead.GetProperty("bp").GetProperty("bpId").GetGuid());
        await h.Db.Sys_RoleActions.Where(row => row.MenuId == 212 && row.RoleId == 72 && row.ActionCode == "mapping").ExecuteDeleteAsync();
        var recovered = await WorkspaceCall(restarted, currentCookies, $"/commands/{winningKey:D}");
        Assert.Equal("FOUND", recovered.Body.GetProperty("knowledge").GetString());
        var replay = await ApplyMap(restarted, currentCookies, winningPreview, winningKey);
        Assert.True(replay.Body.GetProperty("replayed").GetBoolean());
        var originalReceipt = MappingReceipt(results.Single(x => x.Status == 200));
        Assert.Equal(originalReceipt.GetProperty("receiptHash").GetString(), MappingReceipt(replay).GetProperty("receiptHash").GetString());
        Assert.Equal(originalReceipt.GetRawText(), recovered.Body.GetProperty("receipt").GetRawText());
        await DeviceEvidence("wp04-03-b15-b18.json", new { signedOriginal = persisted.BodyHash, exactTrust = true, currentActorAfterRestart = true,
            forgedSignature = "UNPROVEN", inactiveObserve = true, wrongWindow = 412, atomicReverseClaim = true,
            win, lost, bp, originalCommand = winningKey, replayAfterMappingRevoked = true, original = originalReceipt });
    }

    private sealed class MappingTestObservation
    {
        public byte[] Original { get; init; } = [];
        public byte[] Trust { get; init; } = [];
        public string BodyHash { get; init; } = "";
        public string Status { get; init; } = "";
        public string Version { get; init; } = "";
    }

    [CurrentPermissionPostgresFact]
    public async Task Business_partner_wp04_03_create_rollback_replace_unlink_and_confirm_commit_exactly_once()
    {
        await using var h = await Seed();
        await using var bridge = await MappingOwnerFixture.Start(h.Tenant);
        await EnableMapping(h, bridge);
        var account = Guid.NewGuid();
        bridge.Account(account, "00000020", "INACTIVE");
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, bridge.Environment());
        var cookies = await Login(host, h);
        var replacement = await AdoptMappingPartner(host, cookies, "0000202", "模拟替换伙伴");
        var options = await WorkspaceCall(host, cookies, "/options?legalEntityKey=JP-A");
        var observation = await SignedAccountObservation(host, cookies, account);
        var newInput = JsonSerializer.SerializeToElement(WorkspaceInput(options, "0000201", "模拟复合伙伴"), MappingJson);
        var newPartner = new
        {
            registration = newInput.GetProperty("registration"), identity = newInput.GetProperty("identity"),
            duplicateDecision = newInput.GetProperty("duplicateDecision")
        };
        var empty = await ReadMappingContext(host, cookies, account, observation, MappingCriteria("0000201", "模拟复合伙伴"));
        Assert.Equal("COMPLETE", empty.Body.GetProperty("candidates").GetProperty("visibility").GetString());
        Assert.Empty(empty.Body.GetProperty("candidates").GetProperty("items").EnumerateArray());
        var newPreview = await GoodMapPreview(host, cookies, MappingInput(empty, "CREATE_AND_LINK", null, "模拟复合建档", newPartner));
        var lackRelation = await MapPreview(host, cookies, MappingInput(empty, "UNLINK", null, "未建立关系先解绑"));
        Assert.False(lackRelation.Body.GetProperty("canApply").GetBoolean());

        var before = await FormalCounts(h);
        var command = Guid.NewGuid();
        await h.Db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION main_bp.test_wp0403_fail_event() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'WP04-03 模拟伙伴图已写后的事件故障'; END $$;
            CREATE TRIGGER z_test_wp0403_fail_event BEFORE INSERT ON main_bp.mapping_events FOR EACH ROW
            EXECUTE FUNCTION main_bp.test_wp0403_fail_event();
            """);
        try { await ApplyMap(host, cookies, newPreview, command, 503); }
        finally
        {
            await h.Db.Database.ExecuteSqlRawAsync("""
                DROP TRIGGER z_test_wp0403_fail_event ON main_bp.mapping_events;
                DROP FUNCTION main_bp.test_wp0403_fail_event();
                """);
        }
        Assert.Equal(before, await FormalCounts(h));
        Assert.False(await h.Db.BusinessPartners.AnyAsync(row => row.BpCd == "0000201"));
        Assert.Equal(new[] { 0, 0, 0, 0, 0, 0 }, await MappingCounts(h));
        Assert.Equal("NOT_OBSERVED", (await WorkspaceCall(host, cookies, $"/commands/{command:D}")).Body.GetProperty("knowledge").GetString());
        var made = await ApplyMap(host, cookies, newPreview, command);
        var first = PartnerId(made);
        Assert.Equal("1", MappingReceipt(made).GetProperty("mapping").GetProperty("mappingRevision").GetString());
        Assert.Equal(new[] { 1, 1, 1, 1, 1, 1 }, await MappingCounts(h));
        var newScopeRevision = await h.Db.Database.SqlQuery<long>($"""
            SELECT revision AS "Value" FROM main_bp.scopes WHERE tenant_id={h.Tenant} AND legal_entity_key={MappingLegal}
            """).SingleAsync();
        Assert.Equal(long.Parse(empty.Body.GetProperty("scopeRevision").GetString()!) + 1, newScopeRevision);

        var confirmContext = await ReadMappingContext(host, cookies, account);
        var confirm = await GoodMapPreview(host, cookies, MappingInput(confirmContext, "CONFIRM", first, "模拟确认原版"));
        var confirmed = MappingReceipt(await ApplyMap(host, cookies, confirm, Guid.NewGuid()));
        Assert.Equal("NO_CHANGE", confirmed.GetProperty("outcome").GetString());
        Assert.Equal("1", confirmed.GetProperty("mapping").GetProperty("mappingRevision").GetString());
        Assert.Equal(new[] { 1, 1, 2, 2, 2, 1 }, await MappingCounts(h));

        var context = await ReadMappingContext(host, cookies, account, observation, MappingCriteria("0000202", "模拟替换伙伴"));
        var illegalLink = await MapPreview(host, cookies, MappingInput(context, "LINK", replacement, "不能绕过已建立关系"));
        Assert.False(illegalLink.Body.GetProperty("canApply").GetBoolean());
        Assert.Equal("MAPPING_ACTION_CONFLICT", Assert.Single(illegalLink.Body.GetProperty("issues").EnumerateArray()).GetProperty("code").GetString());
        var unstamped = await MapPreview(host, cookies, MappingInput(context, "REPLACE", replacement, "缺证据"));
        Assert.False(unstamped.Body.GetProperty("canApply").GetBoolean());
        const string replaceReason = "模拟准确换指决定";
        var proof = await BindMapping(h, bridge, host, cookies, account, MappingSubject(account, "REPLACE", "1", replacement, replaceReason));
        var wrong = await WorkspaceCall(host, cookies, "/mapping-decision-bindings", HttpMethod.Post, new
        {
            subject = MappingSubject(account, "REPLACE", "1", replacement, "改写原原因"), evidence = new[] { proof.Evidence }
        });
        Assert.Equal("UNPROVEN", wrong.Body.GetProperty("availability").GetString());
        var changedReason = await MapPreview(host, cookies, MappingInput(context, "REPLACE", replacement, "已绑定后修改原因",
            binding: proof.Binding, evidence: [proof.Evidence]));
        Assert.False(changedReason.Body.GetProperty("canApply").GetBoolean());
        var prepared = await GoodMapPreview(host, cookies, MappingInput(context, "REPLACE", replacement, replaceReason,
            binding: proof.Binding, evidence: [proof.Evidence]));

        await SetEvidenceGuard(h, bridge, account, "BP_MAPPING_DECISION", MappingOwnerFixture.EvidenceGuard(account), "2", false);
        var denied = await ApplyMap(host, cookies, prepared, Guid.NewGuid(), 409);
        Assert.Equal("DECISION_EVIDENCE_UNPROVEN", denied.Body.GetProperty("code").GetString());
        var untouched = await MappingHead(host, cookies, account);
        Assert.Equal(first, untouched.Body.GetProperty("head").GetProperty("bp").GetProperty("bpId").GetGuid());
        Assert.Equal(new[] { 1, 1, 2, 2, 2, 1 }, await MappingCounts(h));
        await SetEvidenceGuard(h, bridge, account, "BP_MAPPING_DECISION", MappingOwnerFixture.EvidenceGuard(account), "1");
        await ApplyMap(host, cookies, prepared, Guid.NewGuid(), 409);
        var renewed = await BindMapping(h, bridge, host, cookies, account, MappingSubject(account, "REPLACE", "1", replacement, replaceReason));
        prepared = await GoodMapPreview(host, cookies, MappingInput(context, "REPLACE", replacement, replaceReason,
            binding: renewed.Binding, evidence: [renewed.Evidence]));

        var replaceId = Guid.NewGuid();
        await h.Db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION main_bp.test_wp0403_fail_replace() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'WP04-03 模拟释放旧占用和取得新占用后的故障'; END $$;
            CREATE TRIGGER z_test_wp0403_fail_replace BEFORE INSERT ON main_bp.mapping_events FOR EACH ROW
            EXECUTE FUNCTION main_bp.test_wp0403_fail_replace();
            """);
        try { await ApplyMap(host, cookies, prepared, replaceId, 503); }
        finally
        {
            await h.Db.Database.ExecuteSqlRawAsync("""
                DROP TRIGGER z_test_wp0403_fail_replace ON main_bp.mapping_events;
                DROP FUNCTION main_bp.test_wp0403_fail_replace();
                """);
        }
        Assert.Equal(new[] { first }, await h.Db.Database.SqlQuery<Guid>($"""
            SELECT bp_id AS "Value" FROM main_bp.mapping_reverse_claims WHERE tenant_id={h.Tenant} AND account_id={account}
            """).ToArrayAsync());
        Assert.Equal(new[] { 1, 1, 2, 2, 2, 1 }, await MappingCounts(h));
        var replaced = await ApplyMap(host, cookies, prepared, replaceId);
        Assert.Equal(replacement, PartnerId(replaced));
        Assert.Equal(1, await h.Db.Database.SqlQuery<int>($"""
            SELECT count(DISTINCT tx)::integer AS "Value" FROM (
                 SELECT xmin::text AS tx FROM main_bp.mapping_reverse_claims WHERE tenant_id={h.Tenant} AND bp_id={replacement} AND revision=2
                 UNION ALL SELECT xmin::text AS tx FROM main_bp.mapping_revisions WHERE tenant_id={h.Tenant} AND command_id={replaceId}
                 UNION ALL SELECT xmin::text AS tx FROM main_bp.mapping_events WHERE tenant_id={h.Tenant} AND command_id={replaceId}
                 UNION ALL SELECT xmin::text AS tx FROM main_bp.mapping_audit WHERE tenant_id={h.Tenant} AND command_id={replaceId}
                 UNION ALL SELECT xmin::text AS tx FROM main_bp.commands WHERE tenant_id={h.Tenant} AND command_id={replaceId}
            ) committed
            """).SingleAsync());
        Assert.Equal(new[] { replacement }, await h.Db.Database.SqlQuery<Guid>($"""
            SELECT bp_id AS "Value" FROM main_bp.mapping_reverse_claims WHERE tenant_id={h.Tenant} AND account_id={account}
            """).ToArrayAsync());

        var unlinkContext = await ReadMappingContext(host, cookies, account);
        const string unlinkReason = "模拟解绑同一伙伴";
        var unlink = await BindMapping(h, bridge, host, cookies, account, MappingSubject(account, "UNLINK", "2", null, unlinkReason));
        var unlinkPreview = await GoodMapPreview(host, cookies, MappingInput(unlinkContext, "UNLINK", null, unlinkReason,
            binding: unlink.Binding, evidence: [unlink.Evidence]));
        var released = MappingReceipt(await ApplyMap(host, cookies, unlinkPreview, Guid.NewGuid()));
        Assert.Equal("UNLINKED", released.GetProperty("mapping").GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, released.GetProperty("bp").ValueKind);

        var confirmNone = await ReadMappingContext(host, cookies, account);
        var latest = MappingReceipt(await ApplyMap(host, cookies, await GoodMapPreview(host, cookies,
            MappingInput(confirmNone, "CONFIRM", null, "模拟确认已解绑")), Guid.NewGuid()));
        Assert.Equal("3", latest.GetProperty("mapping").GetProperty("mappingRevision").GetString());
        Assert.Equal("NO_CHANGE", latest.GetProperty("outcome").GetString());
        Assert.Equal(new[] { 3, 0, 5, 5, 5, 3 }, await MappingCounts(h));
        var history = await WorkspaceCall(host, cookies, $"/mapping-history?legalEntityKey={MappingLegal}&accountId={account:D}&limit=2");
        Assert.Equal(new[] { "3", "2" }, history.Body.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("mappingRevision").GetString()));
        Assert.Equal("2", history.Body.GetProperty("nextBeforeRevision").GetString());
        var lastEvents = await StoredEvents(h, account);
        Assert.Equal(5, lastEvents.Length);
        Assert.NotEqual(lastEvents[0].Hash, lastEvents[1].Hash);
        Assert.NotEqual(lastEvents[3].Hash, lastEvents[4].Hash);
        await StateCommand(host, cookies, first, await WorkspaceStatus(host, cookies, first), "freeze", Guid.NewGuid());
        await h.Db.Sys_RoleActions.Where(row => row.MenuId == 212 && row.RoleId == 72 &&
            (row.ActionCode == "mapping" || row.ActionCode == "add")).ExecuteDeleteAsync();
        var original = await ApplyMap(host, cookies, newPreview, command);
        Assert.True(original.Body.GetProperty("replayed").GetBoolean());
        Assert.Equal(MappingReceipt(made).GetRawText(), MappingReceipt(original).GetRawText());
        Assert.Equal(new[] { 3, 0, 5, 5, 5, 3 }, await MappingCounts(h));
        await DeviceEvidence("wp04-03-mutations.json", new { account, first, replacement, auditedReplace = replaceId,
            failedEventRevertedEveryEffect = true, inactiveIdentityLink = true, exactSubjectRequired = true, revokedOwnerEvidence = 409,
            replaceAtomic = true, replaceLateFailureRetainedClaim = true, oneScopeRevisionForCreate = true,
            compoundOriginalAfterRevocationAndChangedBp = true, unlinkHistoricalIdentityWithoutNewProbe = true,
            confirms = new[] { "1", "3" }, released, latest });
    }
}
