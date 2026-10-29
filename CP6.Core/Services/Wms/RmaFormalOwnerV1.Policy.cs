using System.Globalization;
using System.Text.Json;
using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.RmaFormalStoreV1;

namespace CP6.Core.Services.Wms;

public sealed partial class RmaFormalOwnerV1
{
    private const string PolicyOwner = "Main.Wms.RmaPolicy.v1", PolicyJournal = "Main.Wms.RmaPolicyCommand.v1";
    private sealed class PolicyHead
    {
        public Guid Id { get; init; }
        public int Version { get; init; }
    }
    // All policy reads/writes use Run's existing RMA head fence; no second lock/qualification protocol.
    private Task<PolicyHead?> PolicyCurrent(RmaTrustedScopeV1 scope, CancellationToken ct) => db.Database.SqlQuery<PolicyHead>($"""
        SELECT id AS "Id",version AS "Version" FROM cp6_rma_formal_v1.policy_heads
        WHERE tenant_id={db.CurrentTenantId} AND warehouse_id={scope.WarehouseId} AND site_id={scope.SiteId}
        """).SingleOrDefaultAsync(ct);
    private async Task<RmaPolicyVersionV1> PolicyVersion(Guid id, int version, CancellationToken ct)
    {
        var bytes = await db.Database.SqlQuery<byte[]>($"""
            SELECT body AS "Value" FROM cp6_rma_formal_v1.policy_versions
            WHERE tenant_id={db.CurrentTenantId} AND id={id} AND version={version}
            """).SingleOrDefaultAsync(ct) ?? throw Error("RMA_POLICY_VERSION_NOT_FOUND", 404);
        return JsonSerializer.Deserialize<RmaPolicyVersionV1>(bytes, RmaFormalStoreV1.Json)!;
    }
    public Task<RmaPolicyLookupV1> ReadPolicyAsync(RmaPolicyReadV1 query, CancellationToken ct = default)
    {
        Reference(query.ScopeOptionRef); if (query.Reference != null) Reference(query.Reference);
        return Run("query", false, async (store, a, t) =>
        {
            ReadFields(a); var scope = (await ResolveScope(store, a, query.ScopeOptionRef, t)).Scope;
            var head = await PolicyCurrent(scope, t);
            if (head == null)
            {
                if (query.Reference != null) throw Error("RMA_POLICY_VERSION_NOT_FOUND", 404);
                return new RmaPolicyLookupV1("NOT_CONFIGURED", null);
            }
            var version = head.Version;
            if (query.Reference != null && (query.Reference.Owner != PolicyOwner || query.Reference.Id != head.Id.ToString()
                || !int.TryParse(query.Reference.Version, NumberStyles.None, CultureInfo.InvariantCulture, out version) || version < 1))
                throw Error("RMA_POLICY_REFERENCE_INVALID", 400);
            var value = await PolicyVersion(head.Id, version, t);
            if (query.Reference != null && query.Reference != value.Reference) throw Error("RMA_POLICY_REFERENCE_CHANGED", 412);
            return new RmaPolicyLookupV1(value.Policy.Enabled ? "ENABLED" : "DISABLED", value);
        }, ct);
    }
    private static RmaPolicyPublishV1 PolicyInput(RmaPolicyPublishV1 input)
    {
        Reference(input.ScopeOptionRef); if (input.ExpectedPolicyRef != null) Reference(input.ExpectedPolicyRef);
        var p = input.Policy;
        if (p == null || string.IsNullOrWhiteSpace(p.Name) || p.Name.Length > 200
            || string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length > 500
            || p.ReturnWindowDaysAfterDelivery is < 1 or > 36500
            || p.ReasonOptions == null || p.ReasonOptions.Length is < 1 or > 50
            || p.ReasonOptions.Any(x => x == null || string.IsNullOrWhiteSpace(x.Code) || x.Code.Length > 200
                || x.Code != x.Code.Trim() || string.IsNullOrWhiteSpace(x.Label) || x.Label.Length > 200)
            || p.ReasonOptions.Select(x => x.Code).Distinct(StringComparer.Ordinal).Count() != p.ReasonOptions.Length
            || p.AllowedOutcomes == null || p.AllowedOutcomes.Length is < 1 or > 6
            || p.AllowedOutcomes.Any(x => x is not ("CREDIT" or "REFUND" or "REPLACEMENT" or "SUPPLEMENTARY_SHIPMENT" or "RETURN_ONLY" or "TO_BE_DECIDED"))
            || p.AllowedOutcomes.Distinct().Count() != p.AllowedOutcomes.Length) throw Error("RMA_POLICY_INVALID", 400);
        // Freeze the mutable request arrays once at the external Owner boundary.
        return input with { Policy = p with { ReasonOptions = [..p.ReasonOptions], AllowedOutcomes = [..p.AllowedOutcomes] } };
    }
    public Task<RmaPolicyVersionV1> PublishPolicyAsync(Guid commandId, RmaPolicyPublishV1 input, CancellationToken ct = default)
    {
        if (commandId == Guid.Empty) throw Error("VALIDATION_FAILED", 400);
        var c = PolicyInput(input); var bytes = Encode(c); var journal = new MainOwnerOperationJournalV1(db);
        MainOwnerOperationJournalV1.Original? original = null;
        return Run("policy-publish", true, async (store, a, t) =>
        {
            if (original != null) return await DisclosePolicy(a, original, bytes, t);
            ReadFields(a); WriteFields(a, false); var scope = (await ResolveScope(store, a, c.ScopeOptionRef, t)).Scope;
            var head = await PolicyCurrent(scope, t);
            var previous = head == null ? null : (await PolicyVersion(head.Id, head.Version, t)).Reference;
            if (c.ExpectedPolicyRef != previous) throw Error("CAS_CONFLICT", 412);
            var id = head?.Id ?? Guid.NewGuid(); var version = checked((head?.Version ?? 0) + 1);
            var actor = await Actor(store, a, t); var at = DateTimeOffset.UtcNow;
            var reference = new FulfilmentRefV1(PolicyOwner, id.ToString(), version.ToString(CultureInfo.InvariantCulture),
                Digest(new { scope, c.Policy, PublishedAt = at, PublishedBy = actor, c.Reason, PreviousRef = previous }));
            var value = new RmaPolicyVersionV1(reference, scope, c.Policy, at, actor, c.Reason, previous);
            if (head == null)
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO cp6_rma_formal_v1.policy_heads(tenant_id,id,warehouse_id,site_id,version)
                    VALUES({db.CurrentTenantId},{id},{scope.WarehouseId},{scope.SiteId},{version})
                    """, t);
            else await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_rma_formal_v1.policy_heads SET version={version} WHERE tenant_id={db.CurrentTenantId} AND id={id}", t);
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_rma_formal_v1.policy_versions(tenant_id,id,version,body) VALUES({db.CurrentTenantId},{id},{version},{Encode(value)})", t);
            await journal.Store(PolicyJournal, "WMS-RMA.POLICY/1", commandId, a.ActorId, bytes, value, [id],
                new { action = "POLICY_PUBLISH", scope, previous, reference }, t);
            return value;
        }, ct, async t => { original = await journal.Read(PolicyJournal, commandId, t); return original == null ? "policy-publish" : "query"; });
    }
    private async Task<RmaPolicyVersionV1> DisclosePolicy(OperationsAccessV1 a, MainOwnerOperationJournalV1.Original original,
        byte[]? input, CancellationToken ct, RmaTrustedScopeV1? resolvedScope = null)
    {
        if (original.Actor != a.ActorId) throw Error("OBJECT_FORBIDDEN", 403);
        if (input != null && !MainOwnerOperationJournalV1.Matches(original, input)) throw Error("COMMAND_BINDING_CONFLICT", 409);
        var result = JsonSerializer.Deserialize<RmaPolicyVersionV1>(original.Result)!;
        ReadFields(a);
        if (resolvedScope == null) await RequireScope(a, result.Scope, ct);
        else if (resolvedScope.EnvironmentId != result.Scope.EnvironmentId || resolvedScope.Mode != result.Scope.Mode
            || resolvedScope.SiteId != result.Scope.SiteId || resolvedScope.WarehouseId != result.Scope.WarehouseId)
            throw Error("COMMAND_BINDING_CONFLICT", 409);
        return result;
    }
    public Task<RmaPolicyCommandResultV1> QueryPolicyCommandAsync(RmaPolicyCommandQueryV1 query, CancellationToken ct = default)
    {
        Reference(query.ScopeOptionRef); if (query.CommandId == Guid.Empty) throw Error("VALIDATION_FAILED", 400);
        return Run("query", false, async (store, a, t) =>
        {
            var scope = (await ResolveScope(store, a, query.ScopeOptionRef, t)).Scope;
            var original = await new MainOwnerOperationJournalV1(db).Read(PolicyJournal, query.CommandId, t);
            if (original == null) { ReadFields(a); return new RmaPolicyCommandResultV1("NOT_OBSERVED", null); }
            var result = await DisclosePolicy(a, original, null, t, scope);
            return new RmaPolicyCommandResultV1("FOUND", result);
        }, ct);
    }
}
