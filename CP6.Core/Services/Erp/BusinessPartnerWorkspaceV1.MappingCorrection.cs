using System.Text.Json;
using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Erp;

public sealed partial class BusinessPartnerWorkspaceV1
{
    private sealed record MappingConflictReference(Guid ConflictId, string RecordVersion, string OriginalBodyHash, string IncomingBodyHash);
    private sealed record MappingConflict(MappingConflictReference Reference, byte[] Original, byte[] Incoming, MappingSourceReference ReceivedTrustRef);
    private sealed record ProjectionCorrectionRequest(string LegalEntityKey, Guid AccountId, string ExpectedMainRevision);
    private sealed record CorrectionJournalRequest(Guid ActorId, MappingReference Main, Guid CanonicalEventId, string CanonicalBodyHash,
        MappingConflictReference[] Conflicts, MappingSourceReference CoverageRef, MappingSourceReference[] ReceivedTrustRefs);
    private sealed record CorrectionCapture(MappingSlot Slot, MappingRevisionRow Main, MappingEventRow Event, MappingBridgeCheck Crm,
        BusinessPartnerMappingAuthorityBindingV1? Journal);
    private sealed record CorrectionBody(MappingReference Main, string ExpectedProjectionGeneration, Guid CanonicalEventId, string CanonicalBodyHash,
        MappingConflictReference[] Conflicts, MappingSourceReference[] OwnerEvidence, string Resolution, MappingSourceReference? Coverage,
        string? ProjectionAuthority, string? JournalAuthority, string? GuardKey, string? GuardVersion, long? CoverageGeneration, long? JournalGeneration,
        BusinessPartnerMappingOriginalV1? ConflictOriginal, BusinessPartnerMappingOriginalV1? ProjectionOriginal, BusinessPartnerMappingOriginalV1? JournalOriginal);

    public async Task<HttpResult> PrepareMappingCorrection(JsonElement input, CancellationToken ct)
    {
        var fields = Exact(input, "legalEntityKey", "accountId", "expectedMainRevision");
        var legal = LegalKey(Token(fields["legalEntityKey"], 64));
        var account = Uuid(fields["accountId"]);
        var expected = Version(fields["expectedMainRevision"]);
        var snapshot = await owner.ExecuteAsync(Resource, MappingCorrect, false, async (access, token) =>
        {
            var (slot, current, original) = await CorrectionHead(access, legal, account, expected, token);
            var crm = new MappingBridgeCheck(access.ReadScopeVersion, access.ActorId, await MappingAuthority(legal, ProjectionPurpose, token));
            return new CorrectionCapture(slot, current, original, crm, await MappingAuthority(legal, DecisionEvidencePurpose, token));
        }, ct);

        var main = MappingRef(legal, account, snapshot.Main);
        var incomplete = new CorrectionBody(main, Revision(snapshot.Slot.Generation), snapshot.Event.EventId, snapshot.Event.BodyHash,
            [], [], "UNPROVEN", null, null, null, null, null, null, null, null, null, null);
        var body = await VerifyCorrectionSources(snapshot, incomplete, ct) ?? incomplete;

        return await owner.ExecuteAsync(Resource, MappingCorrect, true, async (access, token) =>
        {
            var (slot, current, original) = await CorrectionHead(access, legal, account, expected, token);
            var scope = await MappingAccess(access, legal, false, token);
            if (slot.Generation != snapshot.Slot.Generation || current.SemanticHash != snapshot.Main.SemanticHash ||
                original.EventId != snapshot.Event.EventId || access.ActorId != snapshot.Crm.Actor ||
                access.ReadScopeVersion != snapshot.Crm.Permission) throw Stale();
            if (body.Resolution == "CORRECTABLE")
            {
                var (coverage, journal) = await ReadCorrectionGuards(legal, account, body, token);
                body = coverage is null || journal is null ? incomplete : body with { CoverageGeneration = coverage, JournalGeneration = journal };
            }
            var id = Guid.NewGuid();
            var expiry = MappingExpires();
            var hash = CorrectionHash(id, body, expiry);
            await SaveMappingArtifact("CORRECTION", id, access, scope, account, expiry, hash, body, token);
            return new HttpResult(new
            {
                correctionContextId = id, body.Main, body.ExpectedProjectionGeneration, body.CanonicalEventId, body.CanonicalBodyHash,
                body.Conflicts, body.OwnerEvidence, body.Resolution, contextHash = hash, expiresAt = Instant(expiry)
            });
        }, ct);
    }

    private async Task<(MappingSlot Slot, MappingRevisionRow Current, MappingEventRow Original)> CorrectionHead(OperationsAccessV1 access,
        string legal, Guid account, long expected, CancellationToken ct)
    {
        await access.ActionAsync(Resource, MappingAction, ct);
        await access.ActionAsync(Resource, MappingRead, ct);
        await MappingAccess(access, legal, false, ct);
        var slot = await ReadMappingSlot(legal, account, false, ct);
        var current = await MappingRevisionAt(legal, account, slot?.Revision, ct);
        if (slot is null || current?.Revision != expected) throw Stale();
        await ViewMappingHead(access, legal, account, current, ct);
        var original = await FindMappingEvent(legal, account, null, expected, false, ct) ?? throw Missing();
        if (RequireMappingEvent(original).BpKey != current.BpKey || current.SemanticHash != MappingSemanticHash(account, legal, expected, current.BpKey))
            throw Rejected("MAPPING_ORIGINAL_UNPROVEN", 503);
        return (slot, current, original);
    }

    private async Task<CorrectionBody?> VerifyCorrectionSources(CorrectionCapture snapshot, CorrectionBody local, CancellationToken ct)
    {
        if (!snapshot.Slot.OwnerConflict || snapshot.Crm.Binding is not { } crm || snapshot.Journal is not { } journal)
            return null;
        var legal = local.Main.LegalEntityKey;
        var account = local.Main.AccountId;
        var expected = snapshot.Main.Revision;
        var projection = await mappingAuthority!.ReadAsync(crm, "PROJECTION", Canonical(new MappingCoordinates(legal, account)), ct);
        if (projection is null) return null;
        try
        {
            var crmHead = ParseProjection(projection.Body, legal, account);
            if (crmHead is null || crmHead.Revision != expected || !crmHead.Conflict && crmHead.Hash == local.Main.SemanticHash) return null;

            var conflictOriginal = await mappingAuthority.ReadAsync(crm, "CONFLICTS",
                Canonical(new ProjectionCorrectionRequest(legal, account, Revision(expected))), ct);
            if (conflictOriginal is null) return null;
            var response = Exact(ParseOriginal(conflictOriginal.Body), "availability", "records", "coverageRef", "asOf");
            if (Token(response["availability"], 20) != "FOUND" || MappingUtc(response["asOf"]) > Now) return null;
            var coverage = SourceReference(response["coverageRef"]);
            var conflicts = Array(response["records"], 50).Select(raw => ReadCorrectionConflict(raw, snapshot.Event, snapshot.Main, crm)).ToArray();
            if (conflicts.Length == 0 || conflicts.Select(x => x.Reference.ConflictId).Distinct().Count() != conflicts.Length) return null;
            var sorted = conflicts.OrderBy(x => x.Reference.ConflictId.ToString("D"), StringComparer.Ordinal).ToArray();
            var conflictRefs = sorted.Select(x => x.Reference).ToArray();
            var receivedTrustRefs = sorted.Select(x => x.ReceivedTrustRef).ToArray();
            if (coverage.Owner != crm.Authority || coverage.Purpose != "BP_MAPPING_CONFLICT_COVERAGE" ||
                coverage.Digest != Hash(new { accountId = account, legalEntityKey = legal, mappingRevision = Revision(expected), conflicts = conflictRefs, complete = true }))
                return null;

            var review = new CorrectionJournalRequest(snapshot.Crm.Actor, local.Main, local.CanonicalEventId, local.CanonicalBodyHash,
                conflictRefs, coverage, receivedTrustRefs);
            var journalOriginal = await mappingAuthority.ReadAsync(journal, "CORRECTION_EVIDENCE", Canonical(review), ct);
            if (journalOriginal is null) return null;
            var verification = Exact(ParseOriginal(journalOriginal.Body), "availability", "reviewedInputHash", "guardKey", "guardVersion",
                "ownerEvidence", "receivedTrustRecords");
            if (Token(verification["availability"], 20) != "VERIFIED" || Digest(verification["reviewedInputHash"]) != Hash(review)) return null;
            var evidence = Array(verification["ownerEvidence"], 50).Select(ParseEvidenceRecord).Select(x => x.Reference).ToArray();
            if (evidence.Length == 0 || evidence.Any(x => x.Purpose != DecisionEvidencePurpose)) return null;
            var trust = Array(verification["receivedTrustRecords"], 50).Select(ParseEvidenceRecord).ToArray();
            if (!trust.Select(x => x.Reference).SequenceEqual(receivedTrustRefs)) return null;
            for (var i = 0; i < sorted.Length; i++)
                if (!ExactConflictTrust(trust[i].Original, sorted[i].Reference, crm, account, expected)) return null;
            return local with
            {
                Conflicts = conflictRefs, OwnerEvidence = evidence, Resolution = "CORRECTABLE", Coverage = coverage,
                ProjectionAuthority = MappingAuthorityHash(crm), JournalAuthority = MappingAuthorityHash(journal),
                GuardKey = Token(verification["guardKey"], 160), GuardVersion = Token(verification["guardVersion"], 120),
                ConflictOriginal = conflictOriginal, ProjectionOriginal = projection, JournalOriginal = journalOriginal
            };
        }
        catch (Exception ex) when (ex is JsonException or OrderPriceCorrectionException or FormatException or InvalidOperationException)
        { return null; }
    }

    private static MappingConflict ReadCorrectionConflict(JsonElement raw, MappingEventRow main, MappingRevisionRow expected,
        BusinessPartnerMappingAuthorityBindingV1 crm)
    {
        var f = Exact(raw, "reference", "accountId", "legalEntityKey", "mappingRevision", "originalBodyBase64", "incomingBodyBase64", "receivedTrustRef");
        var r = Exact(f["reference"], "conflictId", "recordVersion", "originalBodyHash", "incomingBodyHash");
        var reference = new MappingConflictReference(Uuid(r["conflictId"]), Revision(Version(r["recordVersion"])), Digest(r["originalBodyHash"]),
            Digest(r["incomingBodyHash"]));
        var original = DecodeBody(f["originalBodyBase64"]);
        var incoming = DecodeBody(f["incomingBodyBase64"]);
        var receivedTrust = SourceReference(f["receivedTrustRef"]);
        if (Uuid(f["accountId"]) != main.Account || Token(f["legalEntityKey"], 64) != main.Legal ||
            Version(f["mappingRevision"]) != expected.Revision || RawDigest(original) != reference.OriginalBodyHash ||
            RawDigest(incoming) != reference.IncomingBodyHash || receivedTrust.Owner != crm.Authority ||
            receivedTrust.Purpose != "BP_MAPPING_CONFLICT_RECEIVED_TRUST")
            throw Invalid("OWNER_STREAM_CONFLICT");
        var first = ParseMappingEvent(original);
        var second = ParseMappingEvent(incoming);
        if (new[] { first, second }.Any(x => x.AccountId != main.Account || x.LegalEntityKey != main.Legal || x.Revision != main.Revision) ||
            first.BpKey == second.BpKey || !original.AsSpan().SequenceEqual(main.Body) && !incoming.AsSpan().SequenceEqual(main.Body))
            throw Invalid("OWNER_STREAM_CONFLICT");
        return new(reference, original, incoming, receivedTrust);
    }

    private static bool ExactConflictTrust(byte[] original, MappingConflictReference reference, BusinessPartnerMappingAuthorityBindingV1 crm,
        Guid account, long revision)
    {
        var f = Exact(ParseOriginal(original), "tenantId", "organizationId", "legalEntityKey", "accountId", "mappingRevision",
            "conflictId", "originalBodyHash", "incomingBodyHash", "producer", "contract", "authorizationEpoch");
        return Uuid(f["tenantId"]) == crm.TenantId && Uuid(f["organizationId"]) == crm.OrganizationId &&
               Token(f["legalEntityKey"], 64) == crm.LegalEntityKey && Uuid(f["accountId"]) == account &&
               Version(f["mappingRevision"]) == revision && Uuid(f["conflictId"]) == reference.ConflictId &&
               Digest(f["originalBodyHash"]) == reference.OriginalBodyHash && Digest(f["incomingBodyHash"]) == reference.IncomingBodyHash &&
               Token(f["producer"], 120) == DirectoryOwner && Token(f["contract"], 120) == "AccountErpMappingEvent" &&
               Token(f["authorizationEpoch"], 120) == crm.Epoch;
    }

    private static string CorrectionHash(Guid id, CorrectionBody body, DateTimeOffset expires)
        => Hash(new { correctionContextId = id, body.Main, body.ExpectedProjectionGeneration, body.CanonicalEventId,
            body.CanonicalBodyHash, body.Conflicts, body.OwnerEvidence, expiresAt = Instant(expires) });

    private async Task<(long? Coverage, long? Journal)> ReadCorrectionGuards(string legal, Guid account, CorrectionBody body, CancellationToken ct)
    {
        if (body.Coverage is not { } cover || body.ProjectionAuthority is not { } projection ||
            body.JournalAuthority is not { } journal || body.GuardKey is not { } key || body.GuardVersion is not { } version)
            return (null, null);
        return (await EvidenceGuardGeneration(legal, account, ProjectionPurpose, cover.Id, cover.Version, projection, ct),
            await EvidenceGuardGeneration(legal, account, DecisionEvidencePurpose, key, version, journal, ct));
    }

    private async Task<CorrectionBody> RequireMappingCorrection(OperationsAccessV1 access, string legal, Guid account, Guid id,
        MappingRevisionRow? prior, CancellationToken ct)
    {
        var saved = await FindMappingArtifact("CORRECTION", id, ct);
        EnsureArtifact(saved, access, legal, account, Now);
        var body = OpenArtifact<CorrectionBody>("CORRECTION", saved!);
        if (body.Resolution != "CORRECTABLE" || body.Conflicts.Length == 0 || prior is null ||
            body.Main != MappingRef(legal, account, prior) || body.ConflictOriginal is null || body.JournalOriginal is null || body.ProjectionOriginal is null)
            throw Rejected("OWNER_STREAM_CONFLICT", 503);
        var slot = await ReadMappingSlot(legal, account, false, ct);
        var guards = await ReadCorrectionGuards(legal, account, body, ct);
        if (slot is null || !slot.OwnerConflict || Revision(slot.Generation) != body.ExpectedProjectionGeneration ||
            saved!.Hash != CorrectionHash(id, body, saved.Expires) || body.CoverageGeneration is null || body.JournalGeneration is null ||
            guards != (body.CoverageGeneration, body.JournalGeneration))
            throw Rejected("CORRECTION_CONTEXT_STALE", 409);
        var original = await FindMappingEvent(legal, account, null, prior.Revision, false, ct);
        var verified = RequireMappingEvent(original);
        if (original!.EventId != body.CanonicalEventId || original.BodyHash != body.CanonicalBodyHash || verified.BpKey != prior.BpKey ||
            !await db.Database.SqlQuery<bool>($"""
                SELECT EXISTS(SELECT FROM main_bp.mapping_events e
                  JOIN main_bp.mapping_audit a ON a.tenant_id=e.tenant_id AND a.command_id=e.command_id
                  JOIN main_bp.mapping_revisions r ON r.tenant_id=e.tenant_id AND r.legal_entity_key=e.legal_entity_key
                    AND r.account_id=e.account_id AND r.revision=e.revision
                  WHERE e.tenant_id={Tenant} AND e.event_id={body.CanonicalEventId} AND r.command_id=e.command_id
                   AND a.revision=e.revision AND a.legal_entity_key=e.legal_entity_key AND a.account_id=e.account_id) AS "Value"
                """).SingleAsync(ct))
            throw Rejected("OWNER_STREAM_CONFLICT", 503);
        return body;
    }
}
