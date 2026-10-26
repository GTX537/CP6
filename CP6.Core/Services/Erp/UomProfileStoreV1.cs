using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using static CP6.Core.Services.Erp.UomProfileValuesV1;

namespace CP6.Core.Services.Erp;

internal sealed class UomProfileHeadV1
{
    public Guid Id { get; init; }
    public Guid CreatorId { get; init; }
    public string Code { get; init; } = "";
    public long Revision { get; init; }
}

/// <summary>Native original profiles and product bindings, only inside the actual Main Owner transaction.</summary>
internal sealed partial class UomProfileStoreV1(CP6Context db)
{
    private IDbContextTransaction? fencedTransaction;
    internal CP6Context Context => db;
    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    internal static byte[] Encode<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Json);
    internal static string Digest(byte[] value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
    private void Transaction(bool fenced = true)
    {
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction == null || db.CurrentTenantId == Guid.Empty)
            throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503);
        if (fenced && !ReferenceEquals(fencedTransaction, db.Database.CurrentTransaction)) throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503);
    }
    internal async Task Fence(bool write, CancellationToken ct)
    {
        Transaction(false);
        await db.Database.ExecuteSqlRawAsync(write
            ? "LOCK TABLE cp6_uom_profile_v1.heads,cp6_uom_profile_v1.versions,cp6_uom_profile_v1.product_heads,cp6_uom_profile_v1.product_versions IN SHARE ROW EXCLUSIVE MODE"
            : "LOCK TABLE cp6_uom_profile_v1.heads,cp6_uom_profile_v1.versions,cp6_uom_profile_v1.product_heads,cp6_uom_profile_v1.product_versions IN SHARE MODE", ct);
        fencedTransaction = db.Database.CurrentTransaction;
    }
    internal static IQueryable<UomProfileHeadV1> Heads(CP6Context db) => db.Database.SqlQuery<UomProfileHeadV1>($"""
        SELECT profile_id AS "Id",creator_id AS "CreatorId",code AS "Code",revision AS "Revision"
        FROM cp6_uom_profile_v1.heads WHERE tenant_id={db.CurrentTenantId}
        """);

    private sealed class VersionRow
    {
        public byte[] Body { get; init; } = [];
        public string Digest { get; init; } = "";
    }
    internal async Task<UomProfileVersionV1> Read(Guid id, long? version, CancellationToken ct)
    {
        Transaction();
        var head = await Heads(db).SingleOrDefaultAsync(row => row.Id == id, ct)
            ?? throw Error("OBJECT_FORBIDDEN", 403);
        var revision = version ?? head.Revision;
        var original = await db.Database.SqlQuery<VersionRow>($"""
            SELECT body_bytes AS "Body",body_digest AS "Digest" FROM cp6_uom_profile_v1.versions
            WHERE tenant_id={db.CurrentTenantId} AND profile_id={id} AND revision={revision}
            """).SingleOrDefaultAsync(ct) ?? throw Error("UOM_VERSION_NOT_FOUND", 404);
        if (Digest(original.Body) != original.Digest) throw Error("UOM_ORIGINAL_UNAVAILABLE", 503);
        try
        {
            var stored = JsonSerializer.Deserialize<UomProfileStoredV1>(original.Body, Json);
            if (stored == null || stored.TenantId != db.CurrentTenantId || stored.Id != id || stored.CreatorId != head.CreatorId
                || stored.Profile.Code != head.Code || stored.Version != revision.ToString(CultureInfo.InvariantCulture)
                || Validate(stored.Profile) != stored.Profile) throw Error("UOM_ORIGINAL_UNAVAILABLE", 503);
            return new(db.CurrentTenantId, new(Owner, id, stored.Version, original.Digest), revision == head.Revision,
                stored.Profile, Encoding.UTF8.GetString(original.Body));
        }
        catch (JsonException error) { throw new OrderPriceCorrectionException("UOM_ORIGINAL_UNAVAILABLE", 503, error); }
        catch (OrderPriceCorrectionException error) when (error.Status != 503)
        { throw new OrderPriceCorrectionException("UOM_ORIGINAL_UNAVAILABLE", 503, error); }
    }

    internal async Task<UomProfileVersionV1> Append(Guid id, Guid actor, UomProfileInputV1 profile, long? expected, CancellationToken ct)
    {
        Transaction();
        var head = await Heads(db).SingleOrDefaultAsync(row => row.Id == id, ct);
        long next;
        if (expected == null)
        {
            if (head != null || await Heads(db).AnyAsync(row => row.Code == profile.Code, ct)) throw Error("UOM_IDENTITY_CONFLICT", 409);
            next = 1;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_uom_profile_v1.heads(tenant_id,profile_id,code,creator_id,revision)
                VALUES({db.CurrentTenantId},{id},{profile.Code},{actor},1)
                """, ct);
        }
        else
        {
            if (head == null || head.Code != profile.Code) throw Error("UOM_IDENTITY_CONFLICT", 409);
            if (head.Revision != expected) throw Error("UOM_VERSION_CHANGED", 412);
            if (head.Revision == long.MaxValue) throw Error("UOM_VERSION_EXHAUSTED", 503);
            next = head.Revision + 1;
            var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE cp6_uom_profile_v1.heads SET revision={next}
                WHERE tenant_id={db.CurrentTenantId} AND profile_id={id} AND revision={expected.Value}
                """, ct);
            if (changed != 1) throw Error("UOM_VERSION_CHANGED", 412);
        }
        var body = Encode(new UomProfileStoredV1(db.CurrentTenantId, id, next.ToString(CultureInfo.InvariantCulture), head?.CreatorId ?? actor, profile));
        var digest = Digest(body);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_uom_profile_v1.versions(tenant_id,profile_id,revision,body_bytes,body_digest,actor_id)
            VALUES({db.CurrentTenantId},{id},{next},{body},{digest},{actor})
            """, ct);
        return await Read(id, next, ct);
    }
}
