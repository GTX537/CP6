using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Space;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;

namespace CP6.Core.Services.Plm;

public sealed class EngineeringProductMappingService(CP6Context db, OperationsOwnerV1 owner)
{
    private const string Contract = "Main.Plm.EngineeringMapping.commands.v1";
    private OperationsOwnerV1 Authority => owner.Owns(db) ? owner : throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503);
    private static void SourceReference(EngineeringSourceRefV1 reference, string issuer)
    {
        if (reference.Owner != issuer || reference.Id == Guid.Empty || !ValidDigest(reference.Digest)) throw Error("ENGINEERING_SOURCE_REFERENCE_INVALID", 400);
        Revision(reference.Version);
    }
    private static void Candidate(EngineeringMappingCandidateV1 value)
    {
        Reference(value.EngineeringRef);
        if (value.BusinessRef != null) SourceReference(value.BusinessRef, BusinessOwner);
        if (value.SiteRef != null) SourceReference(value.SiteRef, SiteOwner);
        foreach (var text in new[] { value.Purpose, value.Evidence, value.Conflicts })
            if (text == null || text.Length > 8000 || text.Length != 0 && !ValidText(text, 8000, true)) throw Error("ENGINEERING_MAPPING_INPUT_INVALID", 400);
    }
    public Task<EngineeringBusinessChoiceV1[]> BusinessChoicesAsync(string search, CancellationToken ct = default)
    {
        if (!ValidText(search, 100)) throw Error("ENGINEERING_QUERY_INVALID", 400);
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringProduct, "query", false, async (access, token) =>
        {
            var product = await access.SourceAsync(OperationsOwnerV1.Product, token);
            var rows = await product.Products().Where(row => row.ProductCd.Contains(search)).OrderBy(row => row.ProductCd)
                .Take(50).Select(row => new { row.Id, row.ProductCd }).ToArrayAsync(token);
            var result = new List<EngineeringBusinessChoiceV1>();
            foreach (var row in rows)
            {
                var source = await CommercialProductIdentityReader.ReadOwnedAsync(db, product, row.Id, null, token);
                result.Add(new(new(BusinessOwner, row.Id, source.Version, source.Digest), row.ProductCd));
                access.AtCommit(last => RequireBusiness(product, row.Id, last));
            }
            return result.ToArray();
        }, ct);
    }
    public Task<EngineeringSiteChoiceV1[]> SiteChoicesAsync(string search, CancellationToken ct = default)
    {
        if (search == null || search.Length > 100) throw Error("ENGINEERING_QUERY_INVALID", 400);
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringProduct, "query", false, async (access, token) =>
        {
            var sites = await access.SourceAsync(OperationsOwnerV1.PhysicalSite, token); sites.RequirePhysicalSiteSourceAccess(db);
            var ids = await db.Space_Sites.AsNoTracking().Where(row => !row.IsDeleted && row.Enable && row.SiteCode.Contains(search))
                .OrderBy(row => row.SiteCode).ThenBy(row => row.Id).Take(50).Select(row => row.Id).ToArrayAsync(token);
            var result = new List<EngineeringSiteChoiceV1>();
            foreach (var id in ids) result.Add(await PhysicalSiteIdentityReaderV1.ReadOwnedAsync(db, sites, id, token));
            return result.ToArray();
        }, ct);
    }
    private static async Task RequireBusiness(OperationsAccessV1 access, Guid id, CancellationToken ct)
    { if (!await access.Products(includeDeleted: true).AnyAsync(row => row.Id == id, ct)) throw Error("OBJECT_FORBIDDEN", 403); }
    private static async Task RequireRead(CP6Context db, OperationsAccessV1 access, EngineeringMappingVersionV1 value, CancellationToken ct)
    {
        await access.RequireEngineeringProduct(value.Candidate.EngineeringRef.Id, ct);
        access.AtCommit(token => access.RequireEngineeringProduct(value.Candidate.EngineeringRef.Id, token));
        if (value.Candidate.BusinessRef is { } business)
        {
            var products = await access.SourceAsync(OperationsOwnerV1.Product, ct); await RequireBusiness(products, business.Id, ct);
            access.AtCommit(token => RequireBusiness(products, business.Id, token));
        }
        if (value.Candidate.SiteRef != null) (await access.SourceAsync(OperationsOwnerV1.PhysicalSite, ct)).RequirePhysicalSiteSourceAccess(db);
    }
    private static async Task VerifySources(CP6Context db, OperationsAccessV1 root, EngineeringProductStoreV1 store,
        EngineeringMappingCandidateV1 candidate, bool complete, CancellationToken ct)
    {
        Candidate(candidate);
        var engineering = await root.SourceAsync(OperationsOwnerV1.EngineeringProduct, ct);
        await EngineeringProductOwnerV1.CurrentExact(engineering, store, candidate.EngineeringRef, ct);
        root.AtCommit(token => engineering.RequireEngineeringProduct(candidate.EngineeringRef.Id, token));
        if (candidate.BusinessRef is { } business)
        {
            var products = await root.SourceAsync(OperationsOwnerV1.Product, ct);
            var source = await CommercialProductIdentityReader.ReadOwnedAsync(db, products, business.Id, null, ct);
            if (source.Version != business.Version || source.Digest != business.Digest) throw Error("ENGINEERING_BUSINESS_SOURCE_CHANGED", 412);
            if (!source.SourcePresent || source.Source.GetProperty("isDeleted").GetBoolean()) throw Error("ENGINEERING_BUSINESS_SOURCE_UNAVAILABLE");
            root.AtCommit(token => RequireBusiness(products, business.Id, token));
        }
        else if (complete) throw Error("ENGINEERING_BUSINESS_SOURCE_REQUIRED");
        if (candidate.SiteRef is { } site)
        {
            var sites = await root.SourceAsync(OperationsOwnerV1.PhysicalSite, ct);
            var current = await PhysicalSiteIdentityReaderV1.ReadOwnedAsync(db, sites, site.Id, ct);
            if (current.Reference != site) throw Error("ENGINEERING_SITE_SOURCE_CHANGED", 412);
            if (!current.Enabled) throw Error("ENGINEERING_SITE_DISABLED");
        }
        else if (complete) throw Error("ENGINEERING_SITE_SOURCE_REQUIRED");
        if (complete && (!ValidText(candidate.Purpose, 8000, true) || !ValidText(candidate.Evidence, 8000, true)
            || candidate.ConflictsResolved != true)) throw Error("ENGINEERING_MAPPING_REVIEW_REQUIRED");
    }
    private static async Task<EngineeringMappingVersionV1> Observe(CP6Context db, OperationsAccessV1 access,
        EngineeringProductStoreV1 store, EngineeringMappingVersionV1 value, CancellationToken ct)
    {
        await RequireRead(db, access, value, ct); var reasons = new List<string>();
        if (!value.IsCurrent) reasons.Add("ENGINEERING_MAPPING_CHANGED");
        if (value.State != "CONFIRMED") reasons.Add("ENGINEERING_MAPPING_NOT_CONFIRMED");
        try { await VerifySources(db, access, store, value.Candidate, true, ct); }
        catch (OrderPriceCorrectionException error) when (error.Status is 412 or 422) { reasons.Add(error.Message); }
        return value with { CanAdoptIdentitySite = reasons.Count == 0, Reasons = reasons.ToArray() };
    }
    public Task<EngineeringMappingVersionV1[]> ListAsync(Guid engineeringId, int page = 1, CancellationToken ct = default)
    {
        if (engineeringId == Guid.Empty || page < 1 || page > int.MaxValue / 20) throw Error("ENGINEERING_QUERY_INVALID", 400);
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringProduct, "query", false, async (access, token) =>
        {
            var store = new EngineeringProductStoreV1(db); await store.Fence(false, token); await access.RequireEngineeringProduct(engineeringId, token);
            var ids = await store.MappingHeads().Where(row => row.EngineeringId == engineeringId).OrderBy(row => row.Id)
                .Skip((page - 1) * 20).Take(20).Select(row => row.Id).ToArrayAsync(token);
            var values = new List<EngineeringMappingVersionV1>();
            foreach (var id in ids) values.Add(await Observe(db, access, store, await store.ReadMapping(id, null, token), token));
            access.AtCommit(last => access.RequireEngineeringProduct(engineeringId, last)); return values.ToArray();
        }, ct);
    }
    public Task<EngineeringMappingVersionV1> ReadAsync(Guid id, string? version, CancellationToken ct = default)
    {
        if (id == Guid.Empty) throw Error("ENGINEERING_MAPPING_INPUT_INVALID", 400); var revision = version == null ? (long?)null : Revision(version);
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringProduct, "query", false, async (access, token) =>
        {
            var store = new EngineeringProductStoreV1(db); await store.Fence(false, token);
            return await Observe(db, access, store, await store.ReadMapping(id, revision, token), token);
        }, ct);
    }
    internal static async Task<EngineeringMappingVersionV1> CurrentExact(OperationsAccessV1 access, EngineeringProductStoreV1 store,
        EngineeringMappingRefV1 reference, CancellationToken ct)
    {
        access.RequireEngineeringSourceAccess(store.Context);
        if (reference == null || reference.Owner != MappingOwner || reference.Id == Guid.Empty || !ValidDigest(reference.Digest)) throw Error("ENGINEERING_MAPPING_REFERENCE_INVALID", 400);
        Revision(reference.Version); var current = await store.ReadMapping(reference.Id, null, ct);
        await RequireRead(store.Context, access, current, ct);
        if (current.Reference != reference) throw Error("ENGINEERING_MAPPING_CHANGED", 412);
        if (current.State != "CONFIRMED") throw Error("ENGINEERING_MAPPING_NOT_CONFIRMED");
        await VerifySources(store.Context, access, store, current.Candidate, true, ct);
        return current with { CanAdoptIdentitySite = true, Reasons = [] };
    }
    public Task<EngineeringProductCommandResultV1> CommandAsync(EngineeringMappingCommandV1 input, CancellationToken ct = default)
    {
        if (input == null || input.OperationId == Guid.Empty || input.MappingId == Guid.Empty || input.Action is not ("DRAFT" or "CONFIRM" or "DISABLE")) throw Error("ENGINEERING_MAPPING_INPUT_INVALID", 400);
        var frozen = OperationsOwnerV1.Freeze(input); var expected = frozen.ExpectedVersion == "absent" ? (long?)null : Revision(frozen.ExpectedVersion);
        if (frozen.Action == "DRAFT" ? frozen.Candidate == null : frozen.Candidate != null || expected == null) throw Error("ENGINEERING_MAPPING_INPUT_INVALID", 400);
        if (frozen.Candidate != null) Candidate(frozen.Candidate);
        var bytes = MainOwnerOperationJournalV1.Encode(frozen); var journal = new MainOwnerOperationJournalV1(db); MainOwnerOperationJournalV1.Original? original = null;
        var action = frozen.Action == "CONFIRM" ? "confirm" : "edit";
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringProduct, action, true, async (access, token) =>
        {
            var store = new EngineeringProductStoreV1(db); await store.Fence(true, token);
            if (original != null) return await Disclose(access, store, original, frozen.OperationId, bytes, token);
            EngineeringMappingVersionV1? current = expected == null ? null : await store.ReadMapping(frozen.MappingId, null, token);
            if (current != null)
            {
                await RequireRead(db, access, current, token);
                if (current.Reference.Version != frozen.ExpectedVersion) throw Error("ENGINEERING_MAPPING_CHANGED", 412);
            }
            var candidate = frozen.Candidate ?? current!.Candidate;
            await access.RequireEngineeringProduct(candidate.EngineeringRef.Id, token);
            EngineeringOwnerConfirmationV1? engineering = null, business = null;
            if (frozen.Action != "DISABLE") await VerifySources(db, access, store, candidate, frozen.Action == "CONFIRM", token);
            if (frozen.Action == "CONFIRM")
            {
                if (current!.State != "DRAFT") throw Error("ENGINEERING_MAPPING_NOT_DRAFT", 409);
                engineering = access.EngineeringConfirmation(OperationsOwnerV1.EngineeringProduct, "confirm", Owner);
                business = await CommercialEngineeringMappingParticipantV1.Confirm(db, access, candidate.BusinessRef!, token);
            }
            var state = frozen.Action switch { "CONFIRM" => "CONFIRMED", "DISABLE" => "DISABLED", _ => "DRAFT" };
            if (state == "DISABLED") { engineering = current!.EngineeringConfirmation; business = current.BusinessConfirmation; }
            var saved = await store.AppendMapping(frozen.MappingId, expected, state, candidate, engineering, business, token);
            access.AtCommit(last => access.RequireEngineeringProduct(candidate.EngineeringRef.Id, last));
            var result = new EngineeringProductCommandResultV1(frozen.OperationId, "ENGINEERING_MAPPING", frozen.MappingId, saved.Reference.Version, saved.Reference.Digest);
            await journal.Store(MappingOwner, Contract, frozen.OperationId, access.ActorId, bytes, result,
                [frozen.MappingId, candidate.EngineeringRef.Id], new { action = frozen.Action, actor = access.ActorId, result, engineering, business }, token);
            return result;
        }, ct, actionAtFence: async token => { original = await journal.Read(MappingOwner, frozen.OperationId, token); return original == null ? action : "query"; });
    }
    private async Task<EngineeringProductCommandResultV1> Disclose(OperationsAccessV1 access, EngineeringProductStoreV1 store,
        MainOwnerOperationJournalV1.Original original, Guid operation, byte[]? input, CancellationToken ct)
    {
        if (original.Actor != access.ActorId || original.Contract != Contract) throw Error("ORIGINAL_OPERATION_FORBIDDEN", 403);
        if (original.State == "CLOSED") throw Error("ORIGINAL_OPERATION_CLOSED", 409);
        if (input != null && !MainOwnerOperationJournalV1.Matches(original, input)) throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT", 409);
        var result = JsonSerializer.Deserialize<EngineeringProductCommandResultV1>(original.Result);
        if (result == null || result.Kind != "ENGINEERING_MAPPING" || result.OperationId != operation || original.Objects.Length != 2 || original.Objects[0] != result.ObjectId) throw Error("ORIGINAL_OPERATION_UNAVAILABLE", 503);
        var saved = await store.ReadMapping(result.ObjectId, Revision(result.Version), ct);
        if (saved.Reference.Digest != result.Digest || saved.Candidate.EngineeringRef.Id != original.Objects[1]) throw Error("ORIGINAL_OPERATION_UNAVAILABLE", 503);
        await RequireRead(db, access, saved, ct); return result;
    }
    public Task<EngineeringProductOperationV1> OperationAsync(Guid operation, bool close, CancellationToken ct = default)
    {
        if (operation == Guid.Empty) throw Error("ORIGINAL_OPERATION_REQUIRED", 400);
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringProduct, "query", close, async (access, token) =>
        {
            var store = new EngineeringProductStoreV1(db); await store.Fence(close, token); var journal = new MainOwnerOperationJournalV1(db);
            var original = await journal.Read(MappingOwner, operation, token);
            if (original != null)
            {
                if (original.Actor != access.ActorId || original.Contract != Contract) throw Error("ORIGINAL_OPERATION_FORBIDDEN", 403);
                return original.State == "CLOSED" ? new(operation, "CLOSED") : new EngineeringProductOperationV1(operation, "COMMITTED", await Disclose(access, store, original, operation, null, token));
            }
            if (!close) return new EngineeringProductOperationV1(operation, "UNKNOWN");
            var result = new EngineeringProductOperationV1(operation, "CLOSED");
            await journal.Store(MappingOwner, Contract, operation, access.ActorId, null, result, [], new { action = "close-uncommitted-engineering-mapping", actor = access.ActorId }, token);
            return result;
        }, ct, readOnlyAuthorityFence: !close);
    }
}
