using System.Globalization;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;

namespace CP6.Core.Services.Plm;
internal sealed class EngineeringRequirementHeadV1
{
    public Guid Id { get; init; }
    public Guid CreatorId { get; init; }
    public long Revision { get; init; }
    public string Kind { get; init; } = "";
    public string Name { get; init; } = "";
}
internal sealed class EngineeringRequirementStoreV1(CP6Context db)
{
    internal CP6Context Context=>db;
    private IDbContextTransaction? fenced;
    private void Transaction(bool check = true)
    {
        if (!db.Database.IsNpgsql() || db.CurrentTenantId==Guid.Empty || db.Database.CurrentTransaction==null
            || check && !ReferenceEquals(fenced,db.Database.CurrentTransaction))throw Error("AUTHORITY_FENCE_UNAVAILABLE",503);
    }
    internal async Task Fence(bool write,CancellationToken ct)
    {
        Transaction(false);
        await db.Database.ExecuteSqlRawAsync(write
            ? "LOCK TABLE cp6_engineering_requirement_v1.heads,cp6_engineering_requirement_v1.versions IN SHARE ROW EXCLUSIVE MODE"
            : "LOCK TABLE cp6_engineering_requirement_v1.heads,cp6_engineering_requirement_v1.versions IN SHARE MODE",ct);
        if(!await db.Database.SqlQueryRaw<bool>("SELECT cp6_engineering_requirement_v1.ready() AS \"Value\"").SingleAsync(ct))throw Error("REQUIREMENT_STORE_UNAVAILABLE",503);
        fenced=db.Database.CurrentTransaction;
    }
    internal static IQueryable<EngineeringRequirementHeadV1> Heads(CP6Context context)=>context.Database.SqlQuery<EngineeringRequirementHeadV1>($"""
        SELECT object_id AS "Id",creator_id AS "CreatorId",revision AS "Revision",kind AS "Kind",name AS "Name"
        FROM cp6_engineering_requirement_v1.heads WHERE tenant_id={context.CurrentTenantId}
        """);
    private sealed class Original { public byte[] Body {get;init;}=[]; public string Digest {get;init;}=""; }
    internal async Task<EngineeringRequirementSnapshotV1> Read(Guid id,long? version,CancellationToken ct)
    {
        Transaction();var head=await Heads(db).SingleOrDefaultAsync(x=>x.Id==id,ct)??throw Error("OBJECT_FORBIDDEN",403);
        var selected=version??head.Revision;
        var raw=await db.Database.SqlQuery<Original>($"""
            SELECT body_bytes AS "Body",body_digest AS "Digest" FROM cp6_engineering_requirement_v1.versions
            WHERE tenant_id={db.CurrentTenantId} AND object_id={id} AND revision={selected}
            """).SingleOrDefaultAsync(ct)??throw Error("REQUIREMENT_VERSION_NOT_FOUND",404);
        EngineeringRequirementStoredV1? v;
        try{v=JsonSerializer.Deserialize<EngineeringRequirementStoredV1>(raw.Body,EngineeringProductStoreV1.Json);}
        catch(JsonException){throw Error("REQUIREMENT_ORIGINAL_UNAVAILABLE",503);}
        if(v==null || v.Body==null || v.Id!=id || v.TenantId!=db.CurrentTenantId || v.CreatorId!=head.CreatorId || v.Etag==Guid.Empty
            || v.Body.Kind!=head.Kind || v.Version!=selected.ToString(CultureInfo.InvariantCulture) || EngineeringProductStoreV1.Digest(raw.Body)!=raw.Digest)
            throw Error("REQUIREMENT_ORIGINAL_UNAVAILABLE",503);
        return new(db.CurrentTenantId,new(EngineeringRequirementValuesV1.Owner,id,v.Version,raw.Digest),v.Etag,selected==head.Revision,
            v.Body,v.CreatorId,v.ActorId,v.ParentVersion,v.Reason,v.RecordedAt,Encoding.UTF8.GetString(raw.Body),[]);
    }

    // 只认来源重评所指的准确前后版。工程人员另行改绑或重新建档后，新判据沿用自己的依据。
    internal async Task<Dictionary<string, HashSet<string>>> RemovedCriterionClauses(EngineeringRequirementSnapshotV1 version,
        IEnumerable<RequirementCriterionV1> criteria, CancellationToken ct)
    {
        var removed = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        if (version.Body.Requirement?.SourceReviews is not { Length: > 0 } reviews) return removed;
        var tracked = criteria.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        if (tracked.Count == 0) return removed;
        var changes = reviews.Where(review => review.ChangedClauseIds.Length > 0 && review.CriterionIds.Any(tracked.Contains))
            .ToDictionary(review => Revision(review.PreviousRequirement.Version));
        if (changes.Count == 0) return removed;
        tracked.IntersectWith(changes.Values.SelectMany(review => review.CriterionIds));
        var oldest = changes.Keys.Min();
        while (tracked.Count > 0 && version.ParentVersion is { } parent)
        {
            var revision = Revision(parent);
            if (revision < oldest) break;
            var previous = await Read(version.Reference.Id, revision, ct);
            var before = previous.Body.Requirement!;
            var after = version.Body.Requirement!;
            changes.TryGetValue(revision, out var review);
            if (review is not null && (review.PreviousRequirement != previous.Reference || review.PreviousSource != before.Source
                || review.IncomingSource != after.Source)) throw Error("REQUIREMENT_REFERENCE_CHANGED", 412);
            var oldCriteria = before.Criteria.ToDictionary(c => c.Id, StringComparer.Ordinal);
            foreach (var criterion in after.Criteria.Where(c => tracked.Contains(c.Id)))
            {
                if (!oldCriteria.TryGetValue(criterion.Id, out var old))
                {
                    tracked.Remove(criterion.Id);
                    continue;
                }
                var oldBasis = old.SourceClauseIds ?? [];
                var currentBasis = criterion.SourceClauseIds ?? [];
                if (oldBasis.SequenceEqual(currentBasis, StringComparer.Ordinal)) continue;
                var lost = oldBasis.Except(currentBasis, StringComparer.Ordinal).ToArray();
                var added = currentBasis.Except(oldBasis, StringComparer.Ordinal).Any();
                if (lost.Length == 0 && !added) continue;
                if (review is null || !review.CriterionIds.Contains(criterion.Id, StringComparer.Ordinal)
                    || added
                    || lost.Any(id => !review.ChangedClauseIds.Contains(id, StringComparer.Ordinal) || after.ClauseIds.Contains(id, StringComparer.Ordinal)))
                {
                    tracked.Remove(criterion.Id);
                    continue;
                }
                if (!removed.TryGetValue(criterion.Id, out var binding))
                    removed.Add(criterion.Id, binding = new HashSet<string>(StringComparer.Ordinal));
                binding.UnionWith(lost);
            }
            version = previous;
        }
        return removed;
    }

    internal async Task<EngineeringRequirementSnapshotV1> Append(EngineeringRequirementCommandV1 c,Guid actor,CancellationToken ct)
    {
        Transaction();var h=await Heads(db).SingleOrDefaultAsync(x=>x.Id==c.ObjectId,ct);long next;Guid creator;
        if(c.Action=="CREATE")
        {
            if(h!=null)throw Error("REQUIREMENT_IDENTITY_CONFLICT",409);next=1;creator=actor;
            if(await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_engineering_requirement_v1.heads(tenant_id,object_id,creator_id,kind,name,revision)
                VALUES({db.CurrentTenantId},{c.ObjectId},{actor},{c.Body.Kind},{c.Body.Name},1)
                """,ct)!=1)throw Error("REQUIREMENT_WRITE_UNAVAILABLE",503);
        }
        else
        {
            if(h==null || h.Revision!=Revision(c.ExpectedVersion))throw Error("REQUIREMENT_VERSION_CHANGED",412);
            var old=await Read(c.ObjectId,null,ct);if(old.Etag!=c.ExpectedEtag)throw Error("REQUIREMENT_VERSION_CHANGED",412);
            if(h.Kind!=c.Body.Kind)throw Error("REQUIREMENT_KIND_IMMUTABLE",409);
            if(h.Revision==long.MaxValue)throw Error("REQUIREMENT_VERSION_EXHAUSTED",503);next=h.Revision+1;creator=h.CreatorId;
            if(await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE cp6_engineering_requirement_v1.heads SET revision={next},name={c.Body.Name}
                WHERE tenant_id={db.CurrentTenantId} AND object_id={c.ObjectId} AND revision={h.Revision}
                """,ct)!=1)throw Error("REQUIREMENT_VERSION_CHANGED",412);
        }
        var raw=EngineeringProductStoreV1.Encode(new EngineeringRequirementStoredV1(db.CurrentTenantId,c.ObjectId,next.ToString(CultureInfo.InvariantCulture),creator,
            Guid.NewGuid(),c.Body,c.OperationId,c.Action,actor,c.ExpectedVersion,c.Reason,DateTimeOffset.UtcNow));
        if(raw.Length>2097152)throw Error("REQUIREMENT_SIZE_LIMIT",400);
        var digest=EngineeringProductStoreV1.Digest(raw);
        if(await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_engineering_requirement_v1.versions(tenant_id,object_id,revision,body_bytes,body_digest,actor_id,operation_id)
            VALUES({db.CurrentTenantId},{c.ObjectId},{next},{raw},{digest},{actor},{c.OperationId})
            """,ct)!=1)throw Error("REQUIREMENT_WRITE_UNAVAILABLE",503);
        var saved=await Read(c.ObjectId,next,ct);
        if(saved.Reference.Digest!=digest || saved.OriginalJson!=Encoding.UTF8.GetString(raw))throw Error("REQUIREMENT_WRITE_UNAVAILABLE",503);
        return saved;
    }
}
