using System.Security.Cryptography;
using System.Text.Json;
using CP6.Core.Services.Integration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Erp;

public sealed partial class BusinessPartnerWorkspaceV1
{
    private sealed record AccountOriginal(string Availability, string? Code, string? Status, string? IdentityVersion, DateTimeOffset SourceAsOf);
    private sealed record MappingBridgeRequest(string LegalEntityKey, Guid AccountId, Guid ActorId);
    private sealed record MappingBridgeCheck(string Permission, Guid Actor, BusinessPartnerMappingAuthorityBindingV1? Binding);

    public async Task<HttpResult> ProbeMappingAccount(JsonElement input, CancellationToken ct)
    {
        var coordinates = MappingScope(input);
        var legal = coordinates.LegalEntityKey;
        var account = coordinates.AccountId;
        var check = await owner.ExecuteAsync(Resource, MappingAction, false, async (access, token) =>
        {
            await MappingAccess(access, legal, false, token);
            return new MappingBridgeCheck(access.ReadScopeVersion, access.ActorId, await MappingAuthority(legal, AccountIdentityPurpose, token));
        }, ct);
        if (check.Binding is null) return AccountProbe("UNPROVEN");
        var request = Canonical(new MappingBridgeRequest(legal, account, check.Actor));
        var original = await mappingAuthority!.ReadAsync(check.Binding, "ACCOUNT", request, ct);
        if (original is null) return AccountProbe("UNPROVEN");
        AccountOriginal observed;
        try { observed = ParseAccountOriginal(original.Body, account); }
        catch (Exception ex) when (ex is OrderPriceCorrectionException or JsonException) { return AccountProbe("UNPROVEN"); }

        return await owner.ExecuteAsync(Resource, MappingAction, true, async (access, token) =>
        {
            await MappingAccess(access, legal, false, token);
            var current = await MappingAuthority(legal, AccountIdentityPurpose, token);
            var authorityHash = MappingAuthorityHash(check.Binding);
            if (current is null || MappingAuthorityHash(current) != authorityHash ||
                check.Actor != access.ActorId || check.Permission != access.ReadScopeVersion)
                return AccountProbe("UNPROVEN");
            if (observed.Availability != "FOUND") return AccountProbe(observed.Availability);
            var timestamp = PgTime(Now);
            if (observed.SourceAsOf > timestamp || observed.SourceAsOf < timestamp.AddMinutes(-10)) return AccountProbe("UNPROVEN");
            var identityHash = Hash(new { accountId = account, accountCode = observed.Code, tenantId = Tenant,
                organizationId = current.OrganizationId });
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO main_bp.mapping_account_identities(tenant_id,account_id,identity_hash)
                VALUES({Tenant},{account},{identityHash}) ON CONFLICT (tenant_id,account_id) DO NOTHING
                """, token);
            if (await db.Database.SqlQuery<string>($"""
                SELECT identity_hash AS "Value" FROM main_bp.mapping_account_identities WHERE tenant_id={Tenant} AND account_id={account}
                """).SingleAsync(token) != identityHash) return AccountProbe("UNPROVEN");
            var id = Guid.NewGuid();
            var expires = MappingExpires();
            var bodyHash = RawDigest(original.Body);
            var body = Protect("mapping-account", id).Protect(original.Body);
            var trust = Protect("mapping-account-trust", id).Protect(original.ReceivedTrust);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO main_bp.mapping_account_observations(tenant_id,observation_id,actor_id,legal_entity_key,account_id,account_code,
                    account_status,identity_version,identity_hash,body_hash,protected_original,protected_trust,authority_hash,source_as_of,read_at,expires_at)
                VALUES({Tenant},{id},{access.ActorId},{legal},{account},{observed.Code},{observed.Status},{observed.IdentityVersion},
                    {identityHash},{bodyHash},{body},{trust},{authorityHash},{observed.SourceAsOf},{timestamp},{expires})
                """, token);
            return AccountProbe("FOUND", new(id, account, observed.Code!, observed.Status!, Instant(observed.SourceAsOf), Instant(expires), identityHash));
        }, ct);
    }

    private static HttpResult AccountProbe(string availability, AccountObservation? observation = null)
        => new(new { availability, observation });

    private static AccountOriginal ParseAccountOriginal(byte[] bytes, Guid account)
    {
        var raw = ParseOriginal(bytes);
        if (raw.ValueKind != JsonValueKind.Object || raw.EnumerateObject().Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != raw.EnumerateObject().Count() ||
            !raw.TryGetProperty("availability", out var availabilityValue)) throw Invalid();
        var availability = Token(availabilityValue, 20);
        if (availability is "NOT_FOUND" or "NOT_AUTHORIZED" or "UNPROVEN") return new(availability, null, null, null, default);
        if (availability != "FOUND" || !raw.TryGetProperty("accountId", out var id) || Uuid(id) != account ||
            !raw.TryGetProperty("accountCode", out var codeValue) || !raw.TryGetProperty("accountStatus", out var statusValue) ||
            !raw.TryGetProperty("immutableIdentity", out var trustValue) || !Flag(trustValue) ||
            !raw.TryGetProperty("identityBusinessRevision", out var revisionValue) || !raw.TryGetProperty("sourceAsOf", out var asOf))
            throw Invalid();
        var status = Token(statusValue, 8);
        if (status is not ("ACTIVE" or "INACTIVE")) throw Invalid();
        return new(availability, Token(codeValue, 16), status, Revision(Version(revisionValue)), MappingUtc(asOf));
    }

    public Task<HttpResult> PrepareMappingContext(JsonElement input, CancellationToken ct)
    {
        var fields = Exact(input, "legalEntityKey", "accountId", "observationId", "candidateCriteria");
        var legal = LegalKey(Token(fields["legalEntityKey"], 64));
        var account = Uuid(fields["accountId"]);
        var request = new MappingContextRequest(legal, account, OptionalId(fields["observationId"]), MappingCriteria(fields["candidateCriteria"]));
        return owner.ExecuteAsync(Resource, MappingAction, true, async (access, token) =>
        {
            var scope = await MappingAccess(access, legal, false, token);
            var revision = await MappingRevisionAt(legal, account, (await ReadMappingSlot(legal, account, false, token))?.Revision, token);
            var observation = await CurrentMappingAccount(legal, account, request.ObservationId, access.ActorId, token);
            var head = await ViewMappingHead(access, legal, account, revision, token);
            var candidates = await MappingCandidates(access, scope, account, request.CandidateCriteria, revision, token);
            var expires = MappingExpires();
            var id = Guid.NewGuid();
            var context = new MappingContextBody(request, head, candidates);
            var hash = MappingContextHash(id, access.ActorId, scope.Revision, context, expires);
            await SaveMappingArtifact("CONTEXT", id, access, scope, account, expires, hash, context, token);
            object? currentClaim = null;
            if (revision?.BpId is { } bp && await ReadReverseClaim(legal, bp, false, token) is { } claim &&
                claim.Account == account && claim.Revision == revision.Revision)
                currentClaim = new { bpId = bp, accountId = account, mappingRevision = Revision(claim.Revision) };
            return new HttpResult(new
            {
                reference = new MappingContextReference(id, hash, Instant(expires)),
                observation = observation is null ? null : Observe(account, observation), head, candidates, currentClaim,
                allowedActions = await AvailableMappingActions(access, revision, observation, request, candidates, token),
                decisionEvidence = System.Array.Empty<MappingSourceReference>(), scopeRevision = Revision(scope.Revision), asOf = Instant(Now)
            });
        }, ct);
    }

    private string MappingContextHash(Guid id, Guid actor, long scope, MappingContextBody body, DateTimeOffset expires)
        => Hash(new { contextId = id, actorId = actor, tenantId = Tenant, body.Request,
            expectedScopeRevision = Revision(scope), body.Head, body.Candidates, expiresAt = Instant(expires) });

    private static IdentityCriteria? MappingCriteria(JsonElement input)
    {
        if (input.ValueKind == JsonValueKind.Null) return null;
        var criteria = Exact(input, "bpCd", "bpName", "stdCoCd", "einType", "ein");
        var code = OptionalText(criteria["bpCd"], 20);
        if (code is not null && !BusinessCode.IsMatch(code)) throw Invalid();
        return new(code, Text(criteria["bpName"], 200), OptionalText(criteria["stdCoCd"], 120),
            OptionalText(criteria["einType"], 32), OptionalText(criteria["ein"], 120));
    }

    private async Task<CandidateSet> MappingCandidates(OperationsAccessV1 access, Scope scope, Guid account,
        IdentityCriteria? criteria, MappingRevisionRow? revision, CancellationToken ct)
    {
        CandidateItem[] items;
        string visibility;
        if (criteria is null)
        {
            items = [];
            visibility = "COMPLETE";
            if (revision?.BpId is { } bp)
            {
                var visible = await VisibleAsync(access, bp, false, ct);
                var item = Adopted(visible.Head);
                var snapshot = new { bpId = bp, revision = Revision(item.Revision), bpCd = Code(visible.Row),
                    name = Name(visible.Row), roles = RoleSet(visible.Row), registration = item.Registration,
                    lifecycle = Lifecycle(visible.Row), hold = Hold(visible.Row), availability = "SELECTABLE", matchReasons = System.Array.Empty<string>() };
                items = [new(new(bp, snapshot.revision, Hash(snapshot)), snapshot.bpCd, snapshot.name, snapshot.roles,
                    snapshot.registration, snapshot.lifecycle, snapshot.hold, snapshot.availability, snapshot.matchReasons)];
            }
        }
        else
        {
            var result = await Candidates(access, scope, criteria, ct);
            visibility = result.Set.Visibility;
            items = result.Set.Items;
            for (var i = 0; i < items.Length; i++)
            {
                var item = items[i];
                var claim = await ReadReverseClaim(scope.Key, item.Reference.BpId, false, ct);
                if (claim is not null && claim.Account != account) items[i] = item with { Availability = "CLAIMED" };
                else if (!item.Roles["Customer"] || item.Lifecycle != Active) items[i] = item with { Availability = "UNPROVEN" };
            }
        }
        var scopeRevision = Revision(scope.Revision);
        return new(visibility, items, Hash(new { legalEntityKey = scope.Key, criteria, scopeRevision, items, visibility }), scopeRevision);
    }

    private static async Task<string[]> AvailableMappingActions(OperationsAccessV1 access, MappingRevisionRow? current,
        MappingAccount? account, MappingContextRequest request, CandidateSet candidates, CancellationToken ct)
    {
        if (await access.TryPartnerActionAsync(MappingAction, ct) is null) return [];
        var actions = new List<string>();
        if (current is not null)
        {
            actions.Add("CONFIRM");
            if (await access.TryPartnerActionAsync(MappingCorrect, ct) is not null) actions.Add("CORRECT_CURRENT");
            if (current.BpId is not null) actions.Add("UNLINK");
        }
        if (account is not null && request.CandidateCriteria is not null && candidates.Visibility == "COMPLETE")
        {
            if (candidates.Items.Any(x => x.Availability == "SELECTABLE" && x.Reference.BpId != current?.BpId))
                actions.Add(current?.BpId is null ? "LINK" : "REPLACE");
            if (current?.BpId is null && candidates.Items.Length == 0 && await access.TryPartnerActionAsync("add", ct) is not null)
                actions.Add("CREATE_AND_LINK");
        }
        return actions.ToArray();
    }
}
