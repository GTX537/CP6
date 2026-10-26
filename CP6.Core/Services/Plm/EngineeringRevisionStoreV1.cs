using System.Globalization;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;
using static CP6.Core.Services.Plm.EngineeringRevisionValuesV1;

namespace CP6.Core.Services.Plm;
internal sealed class EngineeringRevisionHeadV1
{
    public Guid Id { get; init; }
    public Guid CreatorId { get; init; }
    public Guid? ProductId { get; init; }
    public Guid CandidateId { get; init; }
    public string Code { get; init; } = "";
    public long Revision { get; init; }
}
internal sealed class EngineeringRevisionStoreV1(CP6Context db)
{
    private IDbContextTransaction? fenced;
    internal CP6Context Context => db;
    internal static byte[] Encode<T>(T value) => EngineeringProductStoreV1.Encode(value);
    internal static string Digest(byte[] value) => EngineeringProductStoreV1.Digest(value);
    private void Transaction(bool requireFence = true)
    {
        if (!db.Database.IsNpgsql() || db.CurrentTenantId == Guid.Empty || db.Database.CurrentTransaction == null
            || requireFence && !ReferenceEquals(fenced, db.Database.CurrentTransaction)) throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503);
    }
    internal async Task Fence(bool write, CancellationToken ct)
    {
        Transaction(false);
        await db.Database.ExecuteSqlRawAsync(write
            ? "LOCK TABLE cp6_engineering_revision_v1.heads,cp6_engineering_revision_v1.working_versions IN SHARE ROW EXCLUSIVE MODE"
            : "LOCK TABLE cp6_engineering_revision_v1.heads,cp6_engineering_revision_v1.working_versions IN SHARE MODE", ct);
        fenced = db.Database.CurrentTransaction;
    }
    internal static IQueryable<EngineeringRevisionHeadV1> Heads(CP6Context context) => context.Database.SqlQuery<EngineeringRevisionHeadV1>($"""
        SELECT revision_id AS "Id",creator_id AS "CreatorId",product_id AS "ProductId",candidate_id AS "CandidateId",code AS "Code",revision AS "Revision"
        FROM cp6_engineering_revision_v1.heads WHERE tenant_id={context.CurrentTenantId}
        """);
    private sealed class Original { public byte[] Body { get; init; } = []; public string Digest { get; init; } = ""; }
    private async Task<(EngineeringRevisionHeadV1 Head, Original Raw, EngineeringRevisionStoredV1 Value)> OriginalAt(Guid id, long? version, CancellationToken ct)
    {
        Transaction();
        var head = await Heads(db).SingleOrDefaultAsync(row => row.Id == id, ct) ?? throw Error("OBJECT_FORBIDDEN", 403);
        var selected = version ?? head.Revision;
        var raw = await db.Database.SqlQuery<Original>($"""
            SELECT body_bytes AS "Body",body_digest AS "Digest" FROM cp6_engineering_revision_v1.working_versions
            WHERE tenant_id={db.CurrentTenantId} AND revision_id={id} AND revision={selected}
            """).SingleOrDefaultAsync(ct) ?? throw Error("ENGINEERING_WORKING_VERSION_NOT_FOUND", 404);
        if (Digest(raw.Body) != raw.Digest) throw Error("ENGINEERING_WORKING_ORIGINAL_UNAVAILABLE", 503);
        EngineeringRevisionStoredV1? value;
        try { value = JsonSerializer.Deserialize<EngineeringRevisionStoredV1>(raw.Body, EngineeringProductStoreV1.Json); }
        catch (JsonException) { throw Error("ENGINEERING_WORKING_ORIGINAL_UNAVAILABLE", 503); }
        if (value == null || value.Draft == null || value.Origin == null || value.Numbering == null || value.Draft.Components == null
            || value.TenantId != db.CurrentTenantId || value.Id != id || value.Code != head.Code || value.CreatorId != head.CreatorId
            || value.CandidateId != head.CandidateId || value.Etag == Guid.Empty || value.Version != selected.ToString(CultureInfo.InvariantCulture)
            || value.Draft.ProductRef != null && value.Draft.ProductRef.Id != head.ProductId)
            throw Error("ENGINEERING_WORKING_ORIGINAL_UNAVAILABLE", 503);
        return (head, raw, value);
    }
    internal async Task<EngineeringRevisionSnapshotV1> Read(Guid id, long? version, CancellationToken ct)
    {
        var (head, raw, value) = await OriginalAt(id, version, ct);
        return new(db.CurrentTenantId, new(RevisionOwner, id, value.Version, raw.Digest), value.Code, value.Etag,
            new(CandidateOwner, value.CandidateId, value.Version, Digest(Encode(value.Draft))), value.Version == head.Revision.ToString(CultureInfo.InvariantCulture),
            "DRAFT", value.Origin, value.Draft, value.Reason, value.ParentVersion, value.RecordedAt, Encoding.UTF8.GetString(raw.Body));
    }
    internal async Task<EngineeringRevisionSnapshotV1> Append(Guid id, Guid actor, EngineeringWorkingDraftV1 draft, string reason,
        long? expected, Guid? etag, string? code, EngineeringRevisionOriginV1? origin, EngineeringNumberAllocationV1? numbering, CancellationToken ct)
    {
        Transaction(); var productId = draft.ProductRef?.Id; var head = await Heads(db).SingleOrDefaultAsync(row => row.Id == id, ct); long next;
        EngineeringRevisionStoredV1 value;
        if (expected == null)
        {
            if (head != null) throw Error("ENGINEERING_REVISION_IDENTITY_CONFLICT", 409);
            if (code == null || origin == null || numbering == null) throw Error("ENGINEERING_NUMBER_ALLOCATION_REQUIRED", 503);
            if (await Heads(db).AnyAsync(row => row.ProductId == productId && row.Code == code, ct))
                throw Error("ENGINEERING_REVISION_CODE_CONFLICT", 409);
            next = 1; var candidate = Guid.NewGuid();
            value = new(db.CurrentTenantId, id, "1", code, actor, candidate, Guid.NewGuid(), origin, numbering, draft, reason, null, DateTimeOffset.UtcNow);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_engineering_revision_v1.heads(tenant_id,revision_id,code,creator_id,product_id,candidate_id,revision)
                VALUES({db.CurrentTenantId},{id},{code},{actor},{productId},{candidate},1)
                """, ct);
        }
        else
        {
            if (head == null || head.Revision != expected) throw Error("ENGINEERING_WORKING_VERSION_CHANGED", 412);
            var (_, _, before) = await OriginalAt(id, null, ct);
            if (before.Etag != etag) throw Error("ENGINEERING_WORKING_VERSION_CHANGED", 412);
            if (head.ProductId != null && head.ProductId != draft.ProductRef?.Id) throw Error("ENGINEERING_REVISION_PRODUCT_IMMUTABLE", 409);
            if (head.ProductId == null && productId != null && await Heads(db).AnyAsync(row => row.Id != id && row.ProductId == productId && row.Code == head.Code, ct))
                throw Error("ENGINEERING_REVISION_CODE_CONFLICT", 409);
            if (head.Revision == long.MaxValue) throw Error("ENGINEERING_WORKING_VERSION_EXHAUSTED", 503);
            next = head.Revision + 1;
            value = before with { Version = next.ToString(CultureInfo.InvariantCulture), Etag = Guid.NewGuid(), Draft = draft,
                Reason = reason, ParentVersion = before.Version, RecordedAt = DateTimeOffset.UtcNow };
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE cp6_engineering_revision_v1.heads SET revision={next},product_id={productId}
                WHERE tenant_id={db.CurrentTenantId} AND revision_id={id} AND revision={expected.Value}
                """, ct);
        }
        var bytes = Encode(value); var digest = Digest(bytes);
        if (bytes.Length > 8388608) throw Error("ENGINEERING_WORKING_SIZE_LIMIT", 400);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_engineering_revision_v1.working_versions(tenant_id,revision_id,revision,body_bytes,body_digest,actor_id)
            VALUES({db.CurrentTenantId},{id},{next},{bytes},{digest},{actor})
            """, ct);
        return await Read(id, next, ct);
    }
}
