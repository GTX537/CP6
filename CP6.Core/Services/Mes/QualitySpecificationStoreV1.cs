using System.Globalization;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Plm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;
using static CP6.Core.Services.Mes.QualitySpecificationValuesV1;

namespace CP6.Core.Services.Mes;
internal sealed class QualitySpecificationHeadV1
{
    public Guid Id { get; init; }
    public Guid CreatorId { get; init; }
    public Guid ProductId { get; init; }
    public Guid SiteId { get; init; }
    public long Revision { get; init; }
    public long Control { get; init; }
    public string Name { get; init; } = "";
}
internal sealed record QualitySpecificationStoredV1(Guid EnvironmentId, Guid TenantId, Guid Id, string Version,
    Guid CreatorId, Guid AuthorId, string EvidenceMode, QualitySpecificationBodyV1 Body, string? PreviousVersion, DateTimeOffset RecordedAt);
internal sealed class QualitySpecificationStoreV1(CP6Context db, Guid environment)
{
    private IDbContextTransaction? fenced;
    internal static byte[] Bytes<T>(T value) => ApprovalCanonicalV2.Bytes(value);
    internal static string Hash(byte[] bytes) => ApprovalCanonicalV2.Hash(bytes);
    internal static DateTimeOffset Now() => DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    internal static string RecordOwner(string kind) => kind switch
    { "POLICY" => "Main.Quality.SpecificationReviewPolicy.v1", "PACKET" => "Main.Quality.SpecificationReviewPacket.v1",
      "VOTE" => "Main.Quality.SpecificationReviewVote.v1", "APPROVAL" => "Main.Quality.SpecificationApproval.v1",
      _ => throw Error("QUALITY_SPEC_RECORD_KIND_INVALID", 503) };
    private void Transaction(bool ready = true)
    {
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction == null || db.CurrentTenantId == Guid.Empty || environment == Guid.Empty
            || ready && !ReferenceEquals(fenced, db.Database.CurrentTransaction)) throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503);
    }
    internal async Task Fence(bool write, CancellationToken ct)
    {
        Transaction(false);
        // All native source readers and controls use the same transaction-held gate. The
        // original Main authorization gate precedes this gate, including engineering callers.
        await db.Database.ExecuteSqlRawAsync(write
            ? "LOCK TABLE cp6_quality_specification_v1.heads,cp6_quality_specification_v1.versions,cp6_quality_specification_v1.controls,cp6_quality_specification_v1.records IN SHARE ROW EXCLUSIVE MODE"
            : "LOCK TABLE cp6_quality_specification_v1.heads,cp6_quality_specification_v1.versions,cp6_quality_specification_v1.controls,cp6_quality_specification_v1.records IN SHARE MODE", ct);
        if (!await db.Database.SqlQueryRaw<bool>("SELECT cp6_quality_specification_v1.ready() AS \"Value\"").SingleAsync(ct)) throw Error("QUALITY_SPEC_STORE_UNAVAILABLE", 503);
        fenced = db.Database.CurrentTransaction;
    }
    internal static IQueryable<QualitySpecificationHeadV1> Heads(CP6Context db, Guid environment) => db.Database.SqlQuery<QualitySpecificationHeadV1>($"""
        SELECT specification_id AS "Id",creator_id AS "CreatorId",product_id AS "ProductId",site_id AS "SiteId",
          draft_revision AS "Revision",control_revision AS "Control",name AS "Name"
        FROM cp6_quality_specification_v1.heads WHERE environment_id={environment} AND tenant_id={db.CurrentTenantId}
        """);
    private sealed record Raw(byte[] Body, string Digest);
    private static T Parse<T>(Raw raw)
    {
        try
        {
            if (Hash(raw.Body) != raw.Digest) throw new JsonException();
            return JsonSerializer.Deserialize<T>(raw.Body, ApprovalBindingOwnerV2.Json) ?? throw new JsonException();
        }
        catch (JsonException) { throw Error("QUALITY_SPEC_ORIGINAL_UNAVAILABLE", 503); }
    }
    internal async Task<QualitySpecificationVersionV1> Read(Guid id, long? version, CancellationToken ct)
    {
        Transaction(); var head = await Heads(db, environment).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw Error("OBJECT_FORBIDDEN", 403);
        var selected = version ?? head.Revision;
        var raw = await db.Database.SqlQuery<Raw>($"""
            SELECT body AS "Body",digest AS "Digest" FROM cp6_quality_specification_v1.versions
            WHERE environment_id={environment} AND tenant_id={db.CurrentTenantId} AND specification_id={id} AND revision={selected}
            """).SingleOrDefaultAsync(ct) ?? throw Error("QUALITY_SPEC_VERSION_NOT_FOUND", 404);
        var v = Parse<QualitySpecificationStoredV1>(raw);
        if (v.EnvironmentId != environment || v.TenantId != db.CurrentTenantId || v.Id != id || v.Version != selected.ToString(CultureInfo.InvariantCulture)
            || v.CreatorId != head.CreatorId || v.Body.Product.Id != head.ProductId || v.Body.Site.Id != head.SiteId) throw Error("QUALITY_SPEC_ORIGINAL_UNAVAILABLE", 503);
        return new(environment, db.CurrentTenantId, new(QualitySpecificationValuesV1.Owner, id, v.Version, raw.Digest), v.CreatorId, v.AuthorId,
            v.EvidenceMode, v.Body, v.PreviousVersion, v.RecordedAt, Encoding.UTF8.GetString(raw.Body));
    }
    internal async Task<QualitySpecificationVersionV1> Version(Guid id, QualitySpecificationBodyV1 body, Guid actor,
        string mode, Guid operation, bool create, CancellationToken ct)
    {
        Transaction(); var head = await Heads(db, environment).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (create != (head == null)) throw Error("QUALITY_SPEC_IDENTITY_CONFLICT", 409);
        if (head != null && (head.ProductId != body.Product.Id || head.SiteId != body.Site.Id)) throw Error("QUALITY_SPEC_PARENT_IMMUTABLE", 409);
        var revision = head == null ? 1 : checked(head.Revision + 1); var creator = head?.CreatorId ?? actor;
        if (head == null)
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_quality_specification_v1.heads(environment_id,tenant_id,specification_id,creator_id,product_id,site_id,name,draft_revision,control_revision)
                VALUES({environment},{db.CurrentTenantId},{id},{creator},{body.Product.Id},{body.Site.Id},{body.Name},1,0)
                """, ct);
        else await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_quality_specification_v1.heads SET draft_revision={revision},name={body.Name}
            WHERE environment_id={environment} AND tenant_id={db.CurrentTenantId} AND specification_id={id}
            """, ct);
        var raw = Bytes(new QualitySpecificationStoredV1(environment, db.CurrentTenantId, id, revision.ToString(CultureInfo.InvariantCulture),
            creator, actor, mode, body, head?.Revision.ToString(CultureInfo.InvariantCulture), Now()));
        if (raw.Length > 33554432) throw Error("QUALITY_SPEC_SIZE_LIMIT", 400);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_quality_specification_v1.versions(environment_id,tenant_id,specification_id,revision,body,digest,operation_id,actor_id)
            VALUES({environment},{db.CurrentTenantId},{id},{revision},{raw},{Hash(raw)},{operation},{actor})
            """, ct);
        return await Read(id, revision, ct);
    }
    internal async Task<QualitySpecificationRecordV1<QualitySpecificationControlV1>> Control(Guid id, CancellationToken ct)
    {
        Transaction(); var raw = await db.Database.SqlQuery<Raw>($"""
            SELECT c.body AS "Body",c.digest AS "Digest" FROM cp6_quality_specification_v1.controls c
            JOIN cp6_quality_specification_v1.heads h USING(environment_id,tenant_id,specification_id)
            WHERE c.environment_id={environment} AND c.tenant_id={db.CurrentTenantId} AND c.specification_id={id} AND c.revision=h.control_revision
            """).SingleOrDefaultAsync(ct) ?? throw Error("QUALITY_SPEC_CONTROL_UNAVAILABLE", 503);
        var value = Parse<QualitySpecificationControlV1>(raw);
        if (value.SpecificationId != id) throw Error("QUALITY_SPEC_CONTROL_UNAVAILABLE", 503);
        return new(new(ControlOwner, id, value.Revision.ToString(CultureInfo.InvariantCulture), raw.Digest), value);
    }
    internal async Task<EngineeringSourceRefV1> SaveControl(QualitySpecificationControlV1 value, Guid operation, CancellationToken ct)
    {
        Transaction(); var raw = Bytes(value);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_quality_specification_v1.controls(environment_id,tenant_id,specification_id,revision,body,digest,operation_id,actor_id)
            VALUES({environment},{db.CurrentTenantId},{value.SpecificationId},{value.Revision},{raw},{Hash(raw)},{operation},{value.ActorId});
            UPDATE cp6_quality_specification_v1.heads SET control_revision={value.Revision}
            WHERE environment_id={environment} AND tenant_id={db.CurrentTenantId} AND specification_id={value.SpecificationId} AND control_revision={value.Revision-1};
            """, ct);
        var saved=await Control(value.SpecificationId,ct);
        if(saved.Reference.Version!=value.Revision.ToString(CultureInfo.InvariantCulture) || saved.Reference.Digest!=Hash(raw))
            throw Error("QUALITY_SPEC_CONTROL_WRITE_INCOMPLETE",503);
        return saved.Reference;
    }
    internal async Task<QualitySpecificationRecordV1<T>> Record<T>(Guid specification, string kind, Guid id, T value, Guid operation, Guid actor, CancellationToken ct)
    {
        Transaction(); var raw = Bytes(value); var recordOwner = RecordOwner(kind);
        Guid? packet = value is QualityReviewVoteV1 vote ? vote.PacketId : null;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_quality_specification_v1.records(environment_id,tenant_id,specification_id,id,kind,body,digest,operation_id,actor_id,packet_id)
            VALUES({environment},{db.CurrentTenantId},{specification},{id},{kind},{raw},{Hash(raw)},{operation},{actor},{packet})
            """, ct);
        return new(new(recordOwner, id, "1", Hash(raw)), value);
    }
    internal async Task<QualitySpecificationRecordV1<T>?> Record<T>(Guid specification, string kind, Guid? id, CancellationToken ct)
    {
        Transaction(); if (id == null) return null;
        var raw = await db.Database.SqlQuery<Raw>($"""
            SELECT body AS "Body",digest AS "Digest" FROM cp6_quality_specification_v1.records
            WHERE environment_id={environment} AND tenant_id={db.CurrentTenantId} AND specification_id={specification} AND id={id} AND kind={kind}
            """).SingleOrDefaultAsync(ct) ?? throw Error("QUALITY_SPEC_RECORD_UNAVAILABLE", 503);
        return new(new(RecordOwner(kind), id.Value, "1", raw.Digest), Parse<T>(raw));
    }
    internal async Task<QualitySpecificationRecordV1<QualityReviewVoteV1>[]> Votes(Guid specification, Guid? packet, CancellationToken ct)
    {
        Transaction(); if (packet == null) return [];
        var ids = await db.Database.SqlQuery<Guid>($"""
            SELECT id AS "Value" FROM cp6_quality_specification_v1.records WHERE environment_id={environment} AND tenant_id={db.CurrentTenantId}
            AND specification_id={specification} AND kind='VOTE' AND convert_from(body,'UTF8')::jsonb->>'packetId'={packet.Value.ToString()}
            ORDER BY id
            """).ToArrayAsync(ct);
        var rows = new List<QualitySpecificationRecordV1<QualityReviewVoteV1>>();
        foreach (var id in ids) rows.Add((await Record<QualityReviewVoteV1>(specification, "VOTE", id, ct))!);
        return rows.ToArray();
    }
}
