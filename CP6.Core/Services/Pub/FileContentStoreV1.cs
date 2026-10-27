using System.Globalization;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Plm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;

namespace CP6.Core.Services.Pub;

internal sealed class FileContentRowV1
{
    public Guid Id { get; init; }
    public Guid CreatorId { get; init; }
    public Guid ParentId { get; init; }
    public string ParentVersion { get; init; } = "";
    public string ParentDigest { get; init; } = "";
    public string FileName { get; init; } = "";
    public string Digest { get; init; } = "";
    public long ByteLength { get; init; }
    public string MediaType { get; init; } = "";
    public string StorePath { get; init; } = "";
    public long Revision { get; init; }
}
internal sealed class FileContentStoreV1(CP6Context db)
{
    private IDbContextTransaction? fenced;
    private void Transaction(bool required = true)
    {
        if (!db.Database.IsNpgsql() || db.CurrentTenantId == Guid.Empty || db.Database.CurrentTransaction == null
            || required && !ReferenceEquals(fenced, db.Database.CurrentTransaction)) throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503);
    }
    internal async Task Fence(bool write, CancellationToken ct)
    {
        Transaction(false); await new EngineeringRevisionStoreV1(db).Fence(false, ct);
        await db.Database.ExecuteSqlRawAsync(write ? "LOCK TABLE cp6_file_content_v1.contents,cp6_file_content_v1.heads,cp6_file_content_v1.events IN SHARE ROW EXCLUSIVE MODE"
            : "LOCK TABLE cp6_file_content_v1.contents,cp6_file_content_v1.heads,cp6_file_content_v1.events IN SHARE MODE", ct);
        if (!await db.Database.SqlQueryRaw<bool>("SELECT cp6_file_content_v1.ready() AS \"Value\"").SingleAsync(ct)) throw Error("FILE_STORE_UNAVAILABLE", 503);
        fenced = db.Database.CurrentTransaction;
    }
    internal static IQueryable<FileContentRowV1> Rows(CP6Context db) => db.Database.SqlQuery<FileContentRowV1>($"""
        SELECT c.file_id AS "Id",c.creator_id AS "CreatorId",c.parent_id AS "ParentId",c.parent_version AS "ParentVersion",
          c.parent_digest AS "ParentDigest",c.file_name AS "FileName",c.digest AS "Digest",c.byte_length AS "ByteLength",
          c.media_type AS "MediaType",c.store_path AS "StorePath",h.revision AS "Revision"
        FROM cp6_file_content_v1.contents c JOIN cp6_file_content_v1.heads h USING(tenant_id,file_id) WHERE c.tenant_id={db.CurrentTenantId}
        """);
    internal async Task<FileContentRowV1> Row(Guid id, CancellationToken ct)
    { Transaction(); return await Rows(db).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw Error("OBJECT_FORBIDDEN", 403); }
    private sealed class Raw { public byte[] Body { get; init; } = []; public string Digest { get; init; } = ""; }
    internal async Task<FileContentSnapshotV1> Read(Guid id, long? version, CancellationToken ct)
    {
        var row = await Row(id, ct); var selected = version ?? row.Revision;
        var raw = await db.Database.SqlQuery<Raw>($"""
            SELECT body_bytes AS "Body",body_digest AS "Digest" FROM cp6_file_content_v1.events
            WHERE tenant_id={db.CurrentTenantId} AND file_id={id} AND revision={selected}
            """).SingleOrDefaultAsync(ct) ?? throw Error("FILE_STATE_NOT_FOUND", 404);
        FileContentEventV1? v;
        try { v = JsonSerializer.Deserialize<FileContentEventV1>(raw.Body, EngineeringProductStoreV1.Json); }
        catch (JsonException) { throw Error("FILE_ORIGINAL_UNAVAILABLE", 503); }
        if (v == null || v.TenantId != db.CurrentTenantId || v.Id != id || v.Version != selected.ToString(CultureInfo.InvariantCulture)
            || v.Etag == Guid.Empty || FileContentBytesV1.Digest(raw.Body) != raw.Digest || v.Inspection == null)
            throw Error("FILE_ORIGINAL_UNAVAILABLE", 503);
        var availability = v.Held ? "HELD" : v.Inspection.Status == "CLEAN" ? "AVAILABLE" : v.Inspection.Status == "PENDING" ? "PENDING_INSPECTION" : "QUARANTINED";
        return new(db.CurrentTenantId, new(FileContentBytesV1.Owner, id, "1", "SHA256", row.Digest, row.ByteLength, row.MediaType),
            new(EngineeringRevisionValuesV1.RevisionOwner, row.ParentId, row.ParentVersion, row.ParentDigest), row.FileName,
            v.Version, v.Etag, v.Inspection, v.Held, availability, v.ActorId, v.RecordedAt, v.Action, v.Reason, selected == row.Revision, raw.Digest);
    }
    internal async Task<FileContentSnapshotV1> Create(FileUploadV1 input, Guid actor, string path, string media, FileInspectionV1 inspection, CancellationToken ct)
    {
        Transaction();
        if (await Rows(db).AnyAsync(x => x.Id == input.FileId, ct)) throw Error("FILE_IDENTITY_CONFLICT", 409);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_file_content_v1.contents(tenant_id,file_id,creator_id,parent_id,parent_version,parent_digest,file_name,digest,byte_length,media_type,store_path)
            VALUES({db.CurrentTenantId},{input.FileId},{actor},{input.Parent.Id},{input.Parent.Version},{input.Parent.Digest},{input.FileName},{input.Digest},{input.ByteLength},{media},{path});
            INSERT INTO cp6_file_content_v1.heads(tenant_id,file_id,revision) VALUES({db.CurrentTenantId},{input.FileId},1)
            """, ct);
        await Event(input.FileId, 1, actor, "UPLOAD", input.Reason, inspection, false, ct);
        return await Read(input.FileId, 1, ct);
    }
    internal async Task<FileContentSnapshotV1> Append(FileContentCommandV1 input, Guid actor, FileInspectionV1 inspection, bool held, CancellationToken ct)
    {
        var row = await Row(input.FileId, ct); var current = await Read(input.FileId, null, ct);
        if (current.StateVersion != input.ExpectedVersion || current.Etag != input.ExpectedEtag) throw Error("FILE_STATE_CHANGED", 412);
        if (row.Revision == long.MaxValue) throw Error("FILE_VERSION_EXHAUSTED", 503);
        var next = row.Revision + 1;
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_file_content_v1.heads SET revision={next} WHERE tenant_id={db.CurrentTenantId} AND file_id={input.FileId}", ct);
        await Event(input.FileId, next, actor, input.Action, input.Reason, inspection, held, ct);
        return await Read(input.FileId, next, ct);
    }
    private Task Event(Guid id, long version, Guid actor, string action, string reason, FileInspectionV1 inspection, bool held, CancellationToken ct)
    {
        var raw = EngineeringProductStoreV1.Encode(new FileContentEventV1(db.CurrentTenantId, id, version.ToString(CultureInfo.InvariantCulture), Guid.NewGuid(), inspection, held, actor, DateTimeOffset.UtcNow, action, reason));
        return db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_file_content_v1.events(tenant_id,file_id,revision,body_bytes,body_digest,actor_id)
            VALUES({db.CurrentTenantId},{id},{version},{raw},{FileContentBytesV1.Digest(raw)},{actor})
            """, ct);
    }
}
