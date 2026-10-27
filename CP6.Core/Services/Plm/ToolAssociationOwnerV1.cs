using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wms;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;
using static CP6.Core.Services.Plm.EngineeringProductStoreV1;
using static CP6.Core.Services.Plm.ToolAssociationStoreV1;

namespace CP6.Core.Services.Plm;
public sealed class ToolAssociationOwnerV1(CP6Context db, OperationsOwnerV1 owner)
{
    private const string Contract = "Main.ToolInstanceAssociation.commands.v1";
    private OperationsOwnerV1 Authority => owner.Owns(db) ? owner : throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503);
    private async Task<(OperationsAccessV1 Physical, OperationsAccessV1 Engineering, ToolInstanceStoreV1 Tools,
        PlateMoldSourceStoreV1 Native, ToolSpecificationStoreV1 Specs, EngineeringProductStoreV1 Products, ToolAssociationStoreV1 Store)> Stores(OperationsAccessV1 a, bool write, CancellationToken ct)
    {
        var physical = await a.SourceAsync(OperationsOwnerV1.ToolInstance, ct);
        var engineering = await a.SourceAsync(OperationsOwnerV1.ToolSpecification, ct);
        var tools = new ToolInstanceStoreV1(db); await tools.Fence(ct);
        var native = new PlateMoldSourceStoreV1(db); await native.Fence(ct);
        var specs = new ToolSpecificationStoreV1(db); await specs.Fence(false, ct);
        var products = new EngineeringProductStoreV1(db); await products.Fence(false, ct);
        var store = new ToolAssociationStoreV1(db); await store.Fence(write, ct);
        return (physical, engineering, tools, native, specs, products, store);
    }
    private async Task<ToolSpecificationV1> Technical(OperationsAccessV1 a, EngineeringSourceRefV1 reference, bool exact, CancellationToken ct)
    {
        if (reference == null || reference.Owner != ToolSpecificationStoreV1.Owner || reference.Id == Guid.Empty || !ValidDigest(reference.Digest))
            throw Error("TOOL_TECHNICAL_REF_INVALID", 400);
        var s = await Stores(a, false, ct); await s.Engineering.RequireSpecificationTool(reference.Id, ct);
        var original = await s.Specs.Read(reference.Id, Revision(reference.Version), ct) ?? throw Error("TOOL_SPECIFICATION_VERSION_NOT_FOUND", 404);
        if (original.Reference != reference) throw Error("TOOL_TECHNICAL_REF_MISMATCH", 412);
        var native = await a.SourceAsync(OperationsOwnerV1.PlateMold, ct);
        var source = await s.Native.Read(native, reference.Id, Revision(original.Source.Version), ct);
        if (source.Reference != original.Source) throw Error("TOOL_SPECIFICATION_ORIGINAL_UNAVAILABLE", 503);
        var product = await a.SourceAsync(OperationsOwnerV1.EngineeringProduct, ct);
        await product.RequireEngineeringProduct(original.Requirements.ProductRef.Id, ct);
        var prod = await s.Products.Read(original.Requirements.ProductRef.Id, Revision(original.Requirements.ProductRef.Version), ct);
        if (prod.Reference != original.Requirements.ProductRef) throw Error("TOOL_SPECIFICATION_ORIGINAL_UNAVAILABLE", 503);
        if (exact)
        {
            if ((await s.Specs.Read(reference.Id, null, ct))?.Reference != reference) throw Error("TOOL_TECHNICAL_CHANGED", 412);
            await s.Native.CurrentExact(native, original.Source, ct);
            await EngineeringProductOwnerV1.CurrentExact(product, s.Products, original.Requirements.ProductRef, ct);
            // All three immutable Owner heads remain protected through commit, including same-UoW SQL.
            a.AtCommit(async t => { if (!await TechnicalHeadsCurrent(original, t)) throw Error("TOOL_TECHNICAL_CHANGED", 412); });
        }
        return original;
    }
    private Task<bool> TechnicalHeadsCurrent(ToolSpecificationV1 spec, CancellationToken ct) => db.Database.SqlQuery<bool>($"""
        SELECT EXISTS(SELECT FROM cp6_tool_specification_v1.heads h WHERE h.tenant_id={db.CurrentTenantId}
          AND h.tool_id={spec.Reference.Id} AND h.revision={Revision(spec.Reference.Version)})
        AND EXISTS(SELECT FROM cp6_plate_mold_source_v1.heads h WHERE h.tenant_id={db.CurrentTenantId}
          AND h.object_id={spec.Source.Id} AND h.revision={Revision(spec.Source.Version)})
        AND EXISTS(SELECT FROM cp6_engineering_product_v1.heads h WHERE h.tenant_id={db.CurrentTenantId}
          AND h.product_id={spec.Requirements.ProductRef.Id} AND h.revision={Revision(spec.Requirements.ProductRef.Version)}) AS "Value"
        """).SingleAsync(ct);
    public Task<ToolAssociationCandidateV1[]> CandidatesAsync(Guid physical, string number, int revision, CancellationToken ct = default)
    {
        if (physical == Guid.Empty || !ValidText(number, 40) || revision < 0) throw Error("TOOL_CANDIDATE_QUERY_INVALID", 400);
        return Authority.ExecuteAsync(OperationsOwnerV1.ToolInstance, "query", false, async (a, t) =>
        {
            var s = await Stores(a, false, t); await s.Tools.Read(s.Physical, physical, null, t);
            var native = await a.SourceAsync(OperationsOwnerV1.PlateMold, t);
            var visible = native.PlateMolds().Select(x => x.Id);
            var ids = await s.Engineering.SpecificationTools().Where(x => visible.Contains(x.Id) && x.WdPtnNo == number && x.WdRev == revision)
                .OrderBy(x => x.BaseCd).ThenBy(x => x.Id).Take(100).Select(x => x.Id).ToArrayAsync(t);
            var result = new List<ToolAssociationCandidateV1>();
            foreach (var id in ids)
            {
                var spec = await s.Specs.Read(id, null, t); if (spec == null) continue;
                try
                {
                    await Technical(a, spec.Reference, true, t);
                    var src = await s.Native.Read(native, id, null, t); var identity = src.Source.GetProperty("identity");
                    result.Add(new(spec.Reference, spec.Source, identity.GetProperty("baseCode").GetString()!,
                        identity.GetProperty("number").GetString()!, identity.GetProperty("businessRevision").GetInt32(), spec.Requirements));
                }
                catch (OrderPriceCorrectionException e) when (e.Status is 403 or 404 or 412 || e.Message == "ENGINEERING_PRODUCT_DISABLED") { }
            }
            return result.ToArray();
        }, ct);
    }
    public Task<ToolAssociationViewV1[]> ListAsync(Guid physical, int page = 1, CancellationToken ct = default)
    {
        if (physical == Guid.Empty || page is < 1 or > 100000) throw Error("TOOL_ASSOCIATION_QUERY_INVALID", 400);
        return Authority.ExecuteAsync(OperationsOwnerV1.ToolInstance, "query", false, async (a, t) =>
        {
            var s = await Stores(a, false, t); await s.Tools.Read(s.Physical, physical, null, t);
            // Page only after both Owners' current object scope. Do not disclose hidden proposal IDs/counts.
            var ids = await db.Database.SqlQuery<Guid>($"SELECT proposal_id AS \"Value\" FROM cp6_tool_association_v1.proposals WHERE tenant_id={db.CurrentTenantId} AND physical_id={physical} ORDER BY proposal_id").ToArrayAsync(t);
            var result = new List<ToolAssociationViewV1>(); var skipped = 0;
            foreach (var id in ids)
            {
                ToolAssociationViewV1 view;
                try { view = await View(a, id, null, t); }
                catch (OrderPriceCorrectionException e) when (e.Status is 403 or 404) { continue; }
                if (skipped++ < (page - 1) * 20) continue; result.Add(view); if (result.Count == 20) break;
            }
            return result.ToArray();
        }, ct);
    }
    public Task<ToolAssociationViewV1> ReadAsync(Guid proposal, string? version = null, CancellationToken ct = default)
        => Authority.ExecuteAsync(OperationsOwnerV1.ToolInstance, "query", false, (a, t) => View(a, proposal, version == null ? null : Revision(version), t), ct);
    private async Task<bool> PairCurrent(OperationsAccessV1 a, ToolAssociationSubjectV1 subject, bool hold, CancellationToken ct)
    {
        var s = await Stores(a, false, ct);
        var spec = await Technical(a, subject.Technical, false, ct);
        if (spec.Source != subject.Native || spec.Requirements.ProductRef != subject.Product) throw Error("TOOL_ASSOCIATION_ORIGINAL_UNAVAILABLE", 503);
        var original = await s.Tools.Read(s.Physical, subject.Physical.Id, Revision(subject.Physical.Version), ct);
        if (original.Reference != subject.Physical) throw Error("TOOL_ASSOCIATION_ORIGINAL_UNAVAILABLE", 503);
        var current = await s.Tools.Read(s.Physical, subject.Physical.Id, null, ct);
        var product = await s.Products.Read(subject.Product.Id, null, ct);
        var native = await a.SourceAsync(OperationsOwnerV1.PlateMold, ct);
        var src = await s.Native.Read(native, subject.Native.Id, null, ct);
        var live = current.Reference == subject.Physical && current.Source.GetProperty("status").GetInt32() == 0
            && !src.Source.GetProperty("control").GetProperty("isDeleted").GetBoolean()
            && product.Product.Enabled == true && await TechnicalHeadsCurrent(spec, ct);
        if (live && hold) { await Technical(a, subject.Technical, true, ct); await s.Tools.HoldExactAsync(s.Physical, subject.Physical, ct); }
        return live;
    }
    private async Task<bool> DecisionCurrent(OperationsAccessV1 a, ToolAssociationSubjectV1 subject, ToolAssociationProjectionV1 p,
        ToolAssociationDecisionRefV1? reference, string role, bool hold, CancellationToken ct)
    {
        if (reference == null) return false;
        var s = new ToolAssociationStoreV1(db); await s.Fence(false, ct); var d = await s.Decision(reference, ct);
        if (d.ProposalId != p.ProposalId || d.SubjectDigest != p.SubjectDigest || d.Role != role || d.RoundId != p.RoundId || d.Generation != p.Generation || d.Decision != "CONFIRM") return false;
        var resource = role == EngineeringRole ? OperationsOwnerV1.ToolSpecification : OperationsOwnerV1.ToolInstance;
        try
        {
            if (d.Authorities == null || d.Authorities.Length == 0 || d.Authorities.Select(x => (x.Resource, x.Action)).Distinct().Count() != d.Authorities.Length)
                throw Error("TOOL_ASSOCIATION_ORIGINAL_UNAVAILABLE", 503);
            OperationsOwnerV1.Authority? authority = null;
            foreach (var witness in d.Authorities)
            {
                var check = await Authority.AssociationReceiptAuthorityAsync(witness.Resource, witness.Action, d.ActorId, ct);
                if (check.Source.SourcePermissionVersion != witness.PermissionVersion || check.OrganizationVersion != witness.OrganizationVersion) return false;
                if (witness.Resource == resource && witness.Action == (role == EngineeringRole ? "instance-associate" : "engineering-link")) authority = check;
            }
            if (authority == null) throw Error("TOOL_ASSOCIATION_ORIGINAL_UNAVAILABLE", 503);
            var originalActor = new OperationsAccessV1(db, owner, authority);
            if (role == EngineeringRole) await originalActor.RequireSpecificationTool(subject.Technical.Id, ct);
            else await originalActor.RequireToolInstance(db, subject.Physical.Id, ct);
            await originalActor.ValidateFinalAsync(ct);
            if (hold) a.AtCommit(async t =>
            {
                foreach (var witness in d.Authorities)
                {
                    var final = await Authority.AssociationReceiptAuthorityAsync(witness.Resource, witness.Action, d.ActorId, t);
                    if (final.Source.SourcePermissionVersion != witness.PermissionVersion || final.OrganizationVersion != witness.OrganizationVersion)
                        throw Error("TOOL_ASSOCIATION_AUTHORITY_CHANGED", 403);
                }
                if (role == EngineeringRole)
                {
                    if (!await originalActor.SpecificationTools(true).AnyAsync(x => x.Id == subject.Technical.Id, t)) throw Error("OBJECT_FORBIDDEN", 403);
                }
                else await originalActor.RequireToolInstance(db, subject.Physical.Id, t);
            });
            return true;
        }
        catch (OrderPriceCorrectionException e) when (e.Status is 403 or 404) { return false; }
        catch (CurrentPermissionUnavailableException) { return false; }
    }
    private async Task<ToolAssociationViewV1> View(OperationsAccessV1 a, Guid id, long? version, CancellationToken ct)
    {
        var s = await Stores(a, false, ct); var (subject, digest) = await s.Store.Subject(id, ct);
        var p = await s.Store.Projection(id, version, ct); if (p.SubjectDigest != digest) throw Error("TOOL_ASSOCIATION_ORIGINAL_UNAVAILABLE", 503);
        var pair = await PairCurrent(a, subject, false, ct);
        var eng = await DecisionCurrent(a, subject, p, p.Engineering, EngineeringRole, false, ct);
        var physical = await DecisionCurrent(a, subject, p, p.Physical, PhysicalRole, false, ct);
        var terminal = p.State is "REJECTED" or "REVOKED"; var currentHead = await s.Store.Projection(id, null, ct);
        var known = terminal || pair && eng && physical && currentHead.Revision == p.Revision;
        var unresolved = new List<string>(); if (!pair) unresolved.Add("SOURCE_CHANGED"); if (!eng) unresolved.Add(EngineeringRole); if (!physical) unresolved.Add(PhysicalRole);
        var now = DateTimeOffset.UtcNow;
        return new(subject, digest, p.Revision, p.ControlRevision, p.Generation, p.RoundId,
            terminal ? p.State : known ? p.State : "UNKNOWN", p.State, p.EverConfirmed, known ? "KNOWN" : "UNKNOWN", unresolved.ToArray(), p.Engineering, p.Physical, now,
            now >= subject.Scope.EffectiveFrom && (subject.Scope.EffectiveTo == null || now < subject.Scope.EffectiveTo));
    }
    public Task<ToolAssociationResultV1> ProposeAsync(Guid physical, string? etag, ToolAssociationProposalCommandV1 input, CancellationToken ct = default)
    {
        var c = OperationsOwnerV1.Freeze(input); Validate(c);
        if (c.Physical.Id != physical) throw Error("TOOL_ASSOCIATION_SUBJECT_INVALID", 400);
        var bytes = MainOwnerOperationJournalV1.Encode(new { command = c, etag });
        return Command(c.OperationId, bytes, OperationsOwnerV1.ToolInstance, "engineering-link", async (a, s, t) =>
        {
            await a.RequireToolInstance(db, physical, t); var tool = await s.Tools.HoldExactAsync(s.Physical, c.Physical, t);
            if (string.IsNullOrEmpty(etag)) throw Error("IF_MATCH_REQUIRED", 428);
            if (etag != tool.Etag) throw Error("TOOL_INSTANCE_CHANGED", 412);
            if (tool.Source.GetProperty("status").GetInt32() != 0) throw Error("TOOL_ASSOCIATION_PHYSICAL_STATE_INVALID", 409);
            var spec = await Technical(a, c.Technical, true, t); var nativeAccess = await a.SourceAsync(OperationsOwnerV1.PlateMold, t);
            var native = await s.Native.Read(nativeAccess, c.Technical.Id, null, t); var identity = native.Source.GetProperty("identity");
            if (c.Supersedes != null)
            {
                var previous = await View(a, c.Supersedes.Value, null, t);
                if (previous.Subject.Physical.Id != physical || previous.LastKnownState is not ("REJECTED" or "REVOKED")) throw Error("TOOL_ASSOCIATION_SUPERSEDES_INVALID", 409);
            }
            var subject = new ToolAssociationSubjectV1(db.CurrentTenantId, c.ProposalId, "1", c.Technical, spec.Source,
                identity.GetProperty("baseCode").GetString()!, identity.GetProperty("number").GetString()!, identity.GetProperty("businessRevision").GetInt32(),
                c.Physical, spec.Requirements.ProductRef, c.Scope, c.Reason, c.Supersedes, a.ActorId, DateTimeOffset.UtcNow);
            var digest = await s.Store.Add(subject, t); var p = new ToolAssociationProjectionV1(db.CurrentTenantId, c.ProposalId, digest, "1", "1", "1", Guid.NewGuid(), "PROPOSED", false, null, null, DateTimeOffset.UtcNow);
            var hash = await s.Store.Append(p, physical, t); return Result(c.OperationId, subject, p, hash, true, "PROPOSED");
        }, ct);
    }
    private static void Validate(ToolAssociationProposalCommandV1 c)
    {
        if (c == null || c.OperationId == Guid.Empty || c.ProposalId == Guid.Empty || c.Technical == null || c.Physical == null
            || c.Physical.Owner != ToolInstanceStoreV1.Owner || c.Physical.Id == Guid.Empty || !ValidDigest(c.Physical.Digest)
            || c.Scope == null || c.Supersedes == Guid.Empty || c.Supersedes == c.ProposalId || !ValidText(c.Reason, 500, true)
            || !ValidText(c.Scope.ProductApplicability, 2000, true) || !ValidText(c.Scope.ProcessApplicability, 2000, true)
            || !ValidText(c.Scope.ConfigurationApplicability, 2000, true) || !ValidText(c.Scope.Purpose, 1000, true)
            || c.Scope.EffectiveFrom.Offset != TimeSpan.Zero || c.Scope.EffectiveTo?.Offset != null && c.Scope.EffectiveTo.Value.Offset != TimeSpan.Zero
            || c.Scope.EffectiveTo <= c.Scope.EffectiveFrom) throw Error("TOOL_ASSOCIATION_PROPOSAL_INVALID", 400);
        Revision(c.Physical.Version);
    }
    // Only the actual role-specific HTTP Owner selects this role. No browser Owner/evidence envelope input.
    internal Task<ToolAssociationResultV1> DecideAsync(bool engineering, string? etag, ToolAssociationDecisionCommandV1 input, CancellationToken ct)
    {
        var c = OperationsOwnerV1.Freeze(input);
        if (c == null || c.OperationId == Guid.Empty || c.ProposalId == Guid.Empty || c.RoundId == Guid.Empty || !ValidDigest(c.SubjectDigest)
            || c.Decision is not ("CONFIRM" or "REJECT" or "WITHDRAW" or "REFRESH") || !ValidText(c.Evidence, 2000, true)) throw Error("TOOL_ASSOCIATION_DECISION_INVALID", 400);
        Revision(c.ExpectedControlRevision); Revision(c.ExpectedGeneration);
        var resource = engineering ? OperationsOwnerV1.ToolSpecification : OperationsOwnerV1.ToolInstance;
        var bytes = MainOwnerOperationJournalV1.Encode(new { engineering, command = c, etag });
        return Command(c.OperationId, bytes, resource, engineering ? "instance-associate" : "engineering-link", async (a, s, t) =>
        {
            var (subject, digest) = await s.Store.Subject(c.ProposalId, t); var p = await s.Store.Projection(c.ProposalId, null, t);
            await PairCurrent(a, subject, false, t); // Current disclosure rights, including all original sources.
            if (engineering) await a.RequireSpecificationTool(subject.Technical.Id, t); else await a.RequireToolInstance(db, subject.Physical.Id, t);
            if (string.IsNullOrEmpty(etag)) throw Error("IF_MATCH_REQUIRED", 428);
            if (etag != Etag(p)) throw Error("TOOL_ASSOCIATION_CHANGED", 412);
            if (c.SubjectDigest != digest) throw Error("TOOL_ASSOCIATION_SUBJECT_INVALID", 409);
            if (p.State is "REJECTED" or "REVOKED") throw Error("TOOL_ASSOCIATION_TERMINAL", 409);
            if (c.RoundId != p.RoundId || c.ExpectedControlRevision != p.ControlRevision || c.ExpectedGeneration != p.Generation)
                return Result(c.OperationId, subject, p, Digest(Encode(p)), false, "STALE_OBSERVATION");
            var pair = await PairCurrent(a, subject, c.Decision is "CONFIRM" or "REFRESH", t);
            var engCurrent = await DecisionCurrent(a, subject, p, p.Engineering, EngineeringRole, c.Decision == "CONFIRM", t);
            var physicalCurrent = await DecisionCurrent(a, subject, p, p.Physical, PhysicalRole, c.Decision == "CONFIRM", t);
            var expired = !pair || p.Engineering != null && !engCurrent || p.Physical != null && !physicalCurrent;
            if (c.Decision == "REFRESH" || expired && c.Decision == "CONFIRM")
            {
                p = p with { Revision = Next(p.Revision), Generation = Next(p.Generation), RoundId = Guid.NewGuid(), State = "UNKNOWN", Engineering = null, Physical = null, UpdatedAt = DateTimeOffset.UtcNow };
                var hash = await s.Store.Append(p, subject.Physical.Id, t);
                return Result(c.OperationId, subject, p, hash, c.Decision == "REFRESH", !pair ? "SOURCE_CHANGED" : c.Decision == "REFRESH" ? "ROUND_STARTED" : "STALE_OBSERVATION");
            }
            var witnesses = a.Authorities.DistinctBy(x => (x.Resource, x.Action)).OrderBy(x => x.Resource, StringComparer.Ordinal).ThenBy(x => x.Action, StringComparer.Ordinal)
                .Select(x => new ToolAssociationAuthorityV1(x.Resource, x.Action, x.Source.SourcePermissionVersion, x.OrganizationVersion)).ToArray();
            var decision = new ToolAssociationDecisionStoredV1(db.CurrentTenantId, subject.ProposalId, p.RoundId, p.Generation, digest,
                engineering ? EngineeringRole : PhysicalRole, engineering ? EngineeringOwner : PhysicalOwner, Guid.NewGuid(), c.Decision,
                c.Evidence, a.ActorId, witnesses, DateTimeOffset.UtcNow);
            var receipt = await s.Store.AddDecision(decision, t);
            p = p with { Revision = Next(p.Revision), Engineering = engineering ? receipt : p.Engineering, Physical = engineering ? p.Physical : receipt, UpdatedAt = DateTimeOffset.UtcNow };
            if (c.Decision is "REJECT" or "WITHDRAW") p = p with { State = p.EverConfirmed ? "REVOKED" : "REJECTED", ControlRevision = Next(p.ControlRevision) };
            else
            {
                var both = (engineering || engCurrent) && (!engineering || physicalCurrent);
                if (both && await s.Store.OtherConfirmed(subject.Physical.Id, subject.ProposalId, t)) throw Error("TOOL_ASSOCIATION_ALREADY_CONFIRMED", 409);
                p = p with { State = both ? "CONFIRMED" : "UNKNOWN", EverConfirmed = p.EverConfirmed || both };
            }
            var saved = await s.Store.Append(p, subject.Physical.Id, t);
            return Result(c.OperationId, subject, p, saved, true, p.State);
        }, ct);
    }
    internal static string Etag(ToolAssociationProjectionV1 p) => $"\"tool-association:{p.ProposalId:D}:{p.Revision}\"";
    public static string Etag(ToolAssociationViewV1 p) => $"\"tool-association:{p.Subject.ProposalId:D}:{p.Revision}\"";
    private static ToolAssociationResultV1 Result(Guid operation, ToolAssociationSubjectV1 subject, ToolAssociationProjectionV1 p, string digest, bool applied, string outcome)
        => new(operation, "TOOL_ASSOCIATION", subject.Physical.Id, subject.ProposalId, p.Revision, digest, applied, outcome, p.RoundId, p.Generation);
    private Task<ToolAssociationResultV1> Command(Guid operation, byte[] input, string resource, string action,
        Func<OperationsAccessV1, (OperationsAccessV1 Physical, OperationsAccessV1 Engineering, ToolInstanceStoreV1 Tools, PlateMoldSourceStoreV1 Native,
            ToolSpecificationStoreV1 Specs, EngineeringProductStoreV1 Products, ToolAssociationStoreV1 Store), CancellationToken, Task<ToolAssociationResultV1>> execute, CancellationToken ct)
    {
        var journal = new MainOwnerOperationJournalV1(db); MainOwnerOperationJournalV1.Original? original = null;
        return Authority.ExecuteAsync(resource, action, true, async (a, t) =>
        {
            var s = await Stores(a, true, t);
            if (original != null) return await Disclose(a, original, operation, input, t);
            var result = await execute(a, s, t);
            await journal.Store(ToolAssociationStoreV1.Owner, Contract, operation, a.ActorId, input, result, [result.ObjectId, result.ProposalId],
                new { action, result, actor = a.ActorId }, t); return result;
        }, ct, actionAtFence: async t => { original = await journal.Read(ToolAssociationStoreV1.Owner, operation, t); return original == null ? action : "query"; });
    }
    private async Task<ToolAssociationResultV1> Disclose(OperationsAccessV1 a, MainOwnerOperationJournalV1.Original original, Guid id, byte[]? input, CancellationToken ct)
    {
        if (original.Actor != a.ActorId || original.Contract != Contract) throw Error("ORIGINAL_OPERATION_FORBIDDEN", 403);
        if (original.State == "CLOSED") throw Error("ORIGINAL_OPERATION_CLOSED", 409);
        if (input != null && !MainOwnerOperationJournalV1.Matches(original, input)) throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT", 409);
        var r = JsonSerializer.Deserialize<ToolAssociationResultV1>(original.Result);
        if (r == null || r.OperationId != id || r.Kind != "TOOL_ASSOCIATION" || !original.Objects.SequenceEqual(new[] { r.ObjectId, r.ProposalId })) throw Error("ORIGINAL_OPERATION_UNAVAILABLE", 503);
        await View(a, r.ProposalId, Revision(r.Version), ct);
        var s = new ToolAssociationStoreV1(db); await s.Fence(false, ct); var p = await s.Projection(r.ProposalId, Revision(r.Version), ct);
        if (Digest(Encode(p)) != r.Digest) throw Error("ORIGINAL_OPERATION_UNAVAILABLE", 503); return r;
    }
    public Task<ToolAssociationOperationV1> OperationAsync(Guid id, bool close, CancellationToken ct = default)
    {
        if (id == Guid.Empty) throw Error("ORIGINAL_OPERATION_REQUIRED", 400);
        return Authority.ExecuteAsync(OperationsOwnerV1.ToolInstance, "query", close, async (a, t) =>
        {
            var journal = new MainOwnerOperationJournalV1(db); var original = await journal.Read(ToolAssociationStoreV1.Owner, id, t);
            if (original != null)
            {
                if (original.Actor != a.ActorId || original.Contract != Contract) throw Error("ORIGINAL_OPERATION_FORBIDDEN", 403);
                return original.State == "CLOSED" ? new ToolAssociationOperationV1(id, "CLOSED") : new(id, "COMMITTED", await Disclose(a, original, id, null, t));
            }
            if (!close) return new ToolAssociationOperationV1(id, "UNKNOWN");
            var result = new ToolAssociationOperationV1(id, "CLOSED");
            await journal.Store(ToolAssociationStoreV1.Owner, Contract, id, a.ActorId, null, result, [], new { action = "close-uncommitted-tool-association", actor = a.ActorId }, t); return result;
        }, ct, readOnlyAuthorityFence: !close);
    }
}

// Distinct actual producers: their action permissions and immutable original receipt Owners differ.
public sealed class ToolAssociationEngineeringOwnerV1(ToolAssociationOwnerV1 association)
{
    public Task<ToolAssociationResultV1> DecideAsync(string? etag, ToolAssociationDecisionCommandV1 command, CancellationToken ct = default)
        => association.DecideAsync(true, etag, command, ct);
}
public sealed class ToolAssociationPhysicalOwnerV1(ToolAssociationOwnerV1 association)
{
    public Task<ToolAssociationResultV1> DecideAsync(string? etag, ToolAssociationDecisionCommandV1 command, CancellationToken ct = default)
        => association.DecideAsync(false, etag, command, ct);
}
