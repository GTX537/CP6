using System.Globalization;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Plm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using static CP6.Core.Services.Wms.InventoryIdentityValuesV1;

namespace CP6.Core.Services.Wms;
internal sealed class InventoryIdentityHeadV1
{
    public Guid Id { get; init; } public Guid CreatorId { get; init; }
    public string Kind { get; init; } = ""; public string Code { get; init; } = "";
    public long Revision { get; init; }
}
internal sealed class InventoryIdentityStoreV1(CP6Context db, Guid environment)
{
    private IDbContextTransaction? fenced;
    internal static IQueryable<InventoryIdentityHeadV1> Heads(CP6Context db, Guid environment) => db.Database.SqlQuery<InventoryIdentityHeadV1>($"""
        SELECT object_id AS "Id",creator_id AS "CreatorId",kind AS "Kind",code AS "Code",revision AS "Revision"
        FROM cp6_inventory_identity_v1.heads WHERE environment_id={environment} AND tenant_id={db.CurrentTenantId}
        """);
    internal async Task Fence(bool write, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql() || db.CurrentTenantId == Guid.Empty || environment == Guid.Empty || db.Database.CurrentTransaction == null)
            throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503);
        await db.Database.ExecuteSqlRawAsync("LOCK TABLE cp6_inventory_identity_v1.heads,cp6_inventory_identity_v1.versions"
            + (write ? " IN SHARE ROW EXCLUSIVE MODE" : " IN SHARE MODE"), ct);
        if (!await db.Database.SqlQueryRaw<bool>("SELECT cp6_inventory_identity_v1.ready() AS \"Value\"").SingleAsync(ct))
            throw Error("INVENTORY_IDENTITY_STORE_UNAVAILABLE", 503);
        fenced = db.Database.CurrentTransaction;
    }
    private void Transaction()
    { if (fenced == null || !ReferenceEquals(fenced, db.Database.CurrentTransaction)) throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503); }
    private sealed class Row
    { public byte[] Body { get; init; } = []; public string Digest { get; init; } = ""; }
    internal async Task<InventoryIdentityViewV1> Read(Guid id, long? revision, CancellationToken ct)
    {
        Transaction(); var head = await Heads(db, environment).SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw Error("INVENTORY_IDENTITY_NOT_FOUND", 404);
        var version = revision ?? head.Revision;
        var row = await db.Database.SqlQuery<Row>($"""
            SELECT body AS "Body",digest AS "Digest" FROM cp6_inventory_identity_v1.versions
            WHERE environment_id={environment} AND tenant_id={db.CurrentTenantId} AND object_id={id} AND revision={version}
            """).SingleOrDefaultAsync(ct) ?? throw Error("INVENTORY_IDENTITY_VERSION_NOT_FOUND", 404);
        InventoryIdentityOriginalV1? o;
        try { o = JsonSerializer.Deserialize<InventoryIdentityOriginalV1>(row.Body, EngineeringProductStoreV1.Json); }
        catch (JsonException) { throw Error("INVENTORY_IDENTITY_ORIGINAL_UNAVAILABLE", 503); }
        if (o == null || o.EnvironmentId != environment || o.TenantId != db.CurrentTenantId || o.Id != id || o.CreatorId != head.CreatorId
            || o.Version != version.ToString(CultureInfo.InvariantCulture) || o.Definition.Kind != head.Kind || o.Definition.Code != head.Code
            || EngineeringProductStoreV1.Digest(row.Body) != row.Digest)
            throw Error("INVENTORY_IDENTITY_ORIGINAL_UNAVAILABLE", 503);
        return new(new(Owner, id, o.Version, row.Digest), o, Encoding.UTF8.GetString(row.Body), version == head.Revision, false, []);
    }
    internal async Task<InventoryIdentityViewV1> Append(InventoryIdentityCommandV1 c, InventoryIdentityViewV1? before,
        Guid actor, string state, string mode, InventoryIdentitySourceV1[] sources, EngineeringOwnerConfirmationV1[] confirmations, CancellationToken ct)
    {
        Transaction(); var head = await Heads(db, environment).SingleOrDefaultAsync(x => x.Id == c.Id, ct);
        var expected = c.ExpectedVersion == "absent" ? (long?)null : Revision(c.ExpectedVersion);
        if (head?.Revision != expected) throw Error("INVENTORY_IDENTITY_CHANGED", 412);
        var definition = c.Definition ?? before!.Original.Definition;
        if (before != null && (before.Original.Definition.Code != definition.Code || before.Original.Definition.Kind != definition.Kind
            || StableKey(before.Original.Definition) != StableKey(definition))) throw Error("INVENTORY_IDENTITY_KEY_IMMUTABLE", 422);
        if (before != null && c.Action == "SAVE" && EngineeringProductStoreV1.Encode(before.Original.Definition).AsSpan()
            .SequenceEqual(EngineeringProductStoreV1.Encode(definition))) return before;
        if (head?.Revision == long.MaxValue) throw Error("INVENTORY_IDENTITY_VERSION_EXHAUSTED", 503);
        var next = (head?.Revision ?? 0) + 1; var creator = head?.CreatorId ?? actor;
        if (head == null)
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_inventory_identity_v1.heads(environment_id,tenant_id,object_id,kind,code,stable_key,creator_id,revision)
                VALUES({environment},{db.CurrentTenantId},{c.Id},{definition.Kind},{definition.Code},{StableKey(definition)},{creator},1)
                """, ct);
        else await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_inventory_identity_v1.heads SET revision={next}
            WHERE environment_id={environment} AND tenant_id={db.CurrentTenantId} AND object_id={c.Id} AND revision={expected}
            """, ct);
        var o = new InventoryIdentityOriginalV1(environment, db.CurrentTenantId, c.Id, next.ToString(CultureInfo.InvariantCulture), c.OperationId, c.Action, creator,
            c.Action == "SAVE" ? actor : before!.Original.AuthorId, actor, DateTimeOffset.UtcNow, mode, state, definition, c.Reason, sources, confirmations);
        // Native baselines already allow large immutable review originals. Do not narrow that accepted source contract here.
        var body = EngineeringProductStoreV1.Encode(o);
        var digest = EngineeringProductStoreV1.Digest(body);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_inventory_identity_v1.versions(environment_id,tenant_id,object_id,revision,operation_id,actor_id,body,digest)
            VALUES({environment},{db.CurrentTenantId},{c.Id},{next},{c.OperationId},{actor},{body},{digest})
            """, ct);
        return await Read(c.Id, next, ct);
    }
}
