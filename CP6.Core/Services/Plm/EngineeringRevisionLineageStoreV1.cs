using System.Text.Json;
using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;

namespace CP6.Core.Services.Plm;

internal sealed class EngineeringRevisionLineageStoreV1(CP6Context db)
{
    internal const string LegacyOwner = "Main.Plm.EngineeringLegacyReference.v1";
    private sealed class Original
    {
        public long Version { get; init; }
        public Guid OperationId { get; init; }
        public byte[] Body { get; init; } = [];
        public string Digest { get; init; } = "";
    }

    internal async Task<(long Version, EngineeringRebaseRecordV1 Record)?> RebaseAtOrBefore(Guid revision, long version, CancellationToken ct)
    {
        var row = await db.Database.SqlQuery<Original>($"""
            SELECT revision_version AS "Version",operation_id AS "OperationId",body AS "Body",digest AS "Digest"
            FROM cp6_engineering_revision_v1.rebases
            WHERE tenant_id={db.CurrentTenantId} AND revision_id={revision} AND revision_version<={version}
            ORDER BY revision_version DESC LIMIT 1
            """).SingleOrDefaultAsync(ct);
        if (row == null) return null;
        var record = Decode<EngineeringRebaseRecordV1>(row);
        if (record.OperationId != row.OperationId) throw Error("ENGINEERING_LINEAGE_ORIGINAL_UNAVAILABLE", 503);
        return (row.Version, record);
    }

    internal async Task SaveRebase(EngineeringRevisionSnapshotV1 revision, EngineeringRebaseRecordV1 record, CancellationToken ct)
    {
        var body = EngineeringProductStoreV1.Encode(record);
        if (body.Length > 8388608) throw Error("ENGINEERING_REBASE_RECORD_SIZE_LIMIT", 400);
        var digest = EngineeringProductStoreV1.Digest(body);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_engineering_revision_v1.rebases(tenant_id,revision_id,revision_version,operation_id,body,digest)
            VALUES({db.CurrentTenantId},{revision.Reference.Id},{Revision(revision.Reference.Version)},{record.OperationId},{body},{digest})
            """, ct);
    }

    internal async Task<EngineeringLegacyReferenceV1> SaveLegacy(EngineeringLegacyOriginalV1 original, CancellationToken ct)
    {
        var c = original.Registration;
        var body = EngineeringProductStoreV1.Encode(original);
        if (body.Length > 131072) throw Error("ENGINEERING_LEGACY_SIZE_LIMIT", 400);
        var digest = EngineeringProductStoreV1.Digest(body);
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_engineering_revision_v1.legacy_references(tenant_id,id,product_id,source_repo_sha,source_record_ref,operation_id,body,digest)
            VALUES({db.CurrentTenantId},{original.Id},{c.Product.Id},{c.SourceRepositorySha},{c.SourceRecordRef},{c.OperationId},{body},{digest})
            ON CONFLICT (tenant_id,product_id,source_repo_sha,source_record_ref) DO NOTHING
            """, ct);
        if (inserted != 1) throw Error("ENGINEERING_LEGACY_REFERENCE_EXISTS", 409);
        return new(new(LegacyOwner, original.Id, "1", digest), original);
    }

    internal async Task<EngineeringLegacyReferenceV1> ReadLegacy(Guid id, CancellationToken ct)
    {
        var row = await db.Database.SqlQuery<Original>($"""
            SELECT 1::bigint AS "Version",operation_id AS "OperationId",body AS "Body",digest AS "Digest"
            FROM cp6_engineering_revision_v1.legacy_references WHERE tenant_id={db.CurrentTenantId} AND id={id}
            """).SingleOrDefaultAsync(ct) ?? throw Error("OBJECT_FORBIDDEN", 403);
        var original = Decode<EngineeringLegacyOriginalV1>(row);
        if (original.Id != id || original.TenantId != db.CurrentTenantId || original.Registration.OperationId != row.OperationId)
            throw Error("ENGINEERING_LINEAGE_ORIGINAL_UNAVAILABLE", 503);
        return new(new(LegacyOwner, id, "1", row.Digest), original);
    }

    private static T Decode<T>(Original row) where T : class
    {
        if (EngineeringProductStoreV1.Digest(row.Body) != row.Digest) throw Error("ENGINEERING_LINEAGE_ORIGINAL_UNAVAILABLE", 503);
        return JsonSerializer.Deserialize<T>(row.Body, EngineeringProductStoreV1.Json) ?? throw Error("ENGINEERING_LINEAGE_ORIGINAL_UNAVAILABLE", 503);
    }
}
