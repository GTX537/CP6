using System.Globalization;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;

namespace CP6.Core.Services.Plm;

internal sealed class EngineeringRoutingHeadV1
{
    public Guid Id { get; init; }
    public Guid CreatorId { get; init; }
    public Guid ProductId { get; init; }
    public Guid RevisionId { get; init; }
    public long Revision { get; init; }
    public string Name { get; init; } = "";
}
internal sealed class EngineeringRoutingStoreV1(CP6Context db)
{
    private IDbContextTransaction? fenced;
    private void Transaction(bool requireFence = true)
    {
        if (!db.Database.IsNpgsql() || db.CurrentTenantId == Guid.Empty || db.Database.CurrentTransaction == null
            || requireFence && !ReferenceEquals(fenced, db.Database.CurrentTransaction)) throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503);
    }
    internal async Task Fence(bool write, CancellationToken ct)
    {
        Transaction(false);
        // Parent is first in both root authoring and engineering composition consumption.
        await new EngineeringRevisionStoreV1(db).Fence(false, ct);
        await db.Database.ExecuteSqlRawAsync(write ? "LOCK TABLE cp6_engineering_routing_v1.heads,cp6_engineering_routing_v1.versions IN SHARE ROW EXCLUSIVE MODE"
            : "LOCK TABLE cp6_engineering_routing_v1.heads,cp6_engineering_routing_v1.versions IN SHARE MODE", ct);
        if (!await db.Database.SqlQueryRaw<bool>("SELECT cp6_engineering_routing_v1.ready() AS \"Value\"").SingleAsync(ct)) throw Error("ROUTING_STORE_UNAVAILABLE", 503);
        fenced = db.Database.CurrentTransaction;
    }
    internal static IQueryable<EngineeringRoutingHeadV1> Heads(CP6Context context) => context.Database.SqlQuery<EngineeringRoutingHeadV1>($"""
        SELECT routing_id AS "Id",creator_id AS "CreatorId",product_id AS "ProductId",engineering_revision_id AS "RevisionId",revision AS "Revision",name AS "Name"
        FROM cp6_engineering_routing_v1.heads WHERE tenant_id={context.CurrentTenantId}
        """);
    private sealed class Original { public byte[] Body { get; init; } = []; public string Digest { get; init; } = ""; }
    internal async Task<EngineeringRoutingSnapshotV1> Read(Guid id, long? version, CancellationToken ct)
    {
        Transaction(); var head = await Heads(db).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw Error("OBJECT_FORBIDDEN", 403);
        var selected = version ?? head.Revision;
        var raw = await db.Database.SqlQuery<Original>($"""
            SELECT body_bytes AS "Body",body_digest AS "Digest" FROM cp6_engineering_routing_v1.versions
            WHERE tenant_id={db.CurrentTenantId} AND routing_id={id} AND revision={selected}
            """).SingleOrDefaultAsync(ct) ?? throw Error("ROUTING_VERSION_NOT_FOUND", 404);
        EngineeringRoutingStoredV1? v;
        try { v = JsonSerializer.Deserialize<EngineeringRoutingStoredV1>(raw.Body, EngineeringProductStoreV1.Json); }
        catch (JsonException) { throw Error("ROUTING_ORIGINAL_UNAVAILABLE", 503); }
        if (v == null || v.Body == null || v.TenantId != db.CurrentTenantId || v.Id != id || v.CreatorId != head.CreatorId
            || v.Body.ProductRef.Id != head.ProductId || v.Body.RevisionOrigin.Id != head.RevisionId || v.Etag == Guid.Empty
            || v.Version != selected.ToString(CultureInfo.InvariantCulture) || EngineeringProductStoreV1.Digest(raw.Body) != raw.Digest)
            throw Error("ROUTING_ORIGINAL_UNAVAILABLE", 503);
        return new(db.CurrentTenantId, new(EngineeringRoutingValuesV1.Owner, id, v.Version, raw.Digest), v.Etag, selected == head.Revision,
            v.Body, v.Action, v.Reason, v.ParentVersion, v.ActorId, v.RecordedAt, v.Legacy, Encoding.UTF8.GetString(raw.Body));
    }
    internal async Task<EngineeringRoutingSnapshotV1> Append(EngineeringRoutingCommandV1 command, Guid actor,
        EngineeringRoutingLegacyV1? legacy, CancellationToken ct)
    {
        Transaction(); var body = command.Body; var head = await Heads(db).SingleOrDefaultAsync(x => x.Id == command.RoutingId, ct);
        long next; Guid creator;
        if (command.Action == "CREATE")
        {
            if (head != null) throw Error("ROUTING_IDENTITY_CONFLICT", 409);
            next = 1; creator = actor;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_engineering_routing_v1.heads(tenant_id,routing_id,creator_id,product_id,engineering_revision_id,name,revision)
                VALUES({db.CurrentTenantId},{command.RoutingId},{actor},{body.ProductRef.Id},{body.RevisionOrigin.Id},{body.Name},1)
                """, ct);
        }
        else
        {
            if (head == null || head.Revision != Revision(command.ExpectedVersion)) throw Error("ROUTING_VERSION_CHANGED", 412);
            var current = await Read(command.RoutingId, null, ct);
            if (current.Etag != command.ExpectedEtag) throw Error("ROUTING_VERSION_CHANGED", 412);
            if (body.ProductRef.Id != head.ProductId || body.RevisionOrigin.Id != head.RevisionId) throw Error("ROUTING_PARENT_IMMUTABLE", 409);
            if (head.Revision == long.MaxValue) throw Error("ROUTING_VERSION_EXHAUSTED", 503);
            next = head.Revision + 1; creator = head.CreatorId; legacy ??= current.Legacy;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE cp6_engineering_routing_v1.heads SET revision={next},name={body.Name}
                WHERE tenant_id={db.CurrentTenantId} AND routing_id={command.RoutingId} AND revision={head.Revision}
                """, ct);
        }
        var raw = EngineeringProductStoreV1.Encode(new EngineeringRoutingStoredV1(db.CurrentTenantId, command.RoutingId,
            next.ToString(CultureInfo.InvariantCulture), creator, Guid.NewGuid(), body, command.Action, command.Reason,
            command.ExpectedVersion, actor, DateTimeOffset.UtcNow, legacy));
        if (raw.Length > 8388608) throw Error("ROUTING_SIZE_LIMIT", 400);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_engineering_routing_v1.versions(tenant_id,routing_id,revision,body_bytes,body_digest,actor_id)
            VALUES({db.CurrentTenantId},{command.RoutingId},{next},{raw},{EngineeringProductStoreV1.Digest(raw)},{actor})
            """, ct);
        return await Read(command.RoutingId, next, ct);
    }
}
