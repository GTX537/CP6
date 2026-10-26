using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;

namespace CP6.Core.Services.Plm;
internal sealed partial class EngineeringProductStoreV1
{
    internal sealed class MappingHead
    {
        public Guid Id { get; init; }
        public Guid EngineeringId { get; init; }
        public long Revision { get; init; }
    }
    internal IQueryable<MappingHead> MappingHeads() => db.Database.SqlQuery<MappingHead>($"""
        SELECT mapping_id AS "Id",engineering_id AS "EngineeringId",revision AS "Revision"
        FROM cp6_engineering_product_v1.mapping_heads WHERE tenant_id={db.CurrentTenantId}
        """);
    internal async Task<EngineeringMappingVersionV1> ReadMapping(Guid id, long? version, CancellationToken ct)
    {
        Transaction();
        var head = await MappingHeads().SingleOrDefaultAsync(row => row.Id == id, ct) ?? throw Error("ENGINEERING_MAPPING_NOT_FOUND", 404);
        var revision = version ?? head.Revision;
        var row = await db.Database.SqlQuery<VersionRow>($"""
            SELECT body_bytes AS "Body",body_digest AS "Digest" FROM cp6_engineering_product_v1.mapping_versions
            WHERE tenant_id={db.CurrentTenantId} AND mapping_id={id} AND revision={revision}
            """).SingleOrDefaultAsync(ct) ?? throw Error("ENGINEERING_MAPPING_VERSION_NOT_FOUND", 404);
        if (Digest(row.Body) != row.Digest) throw Error("ENGINEERING_MAPPING_ORIGINAL_UNAVAILABLE", 503);
        EngineeringMappingStoredV1? value;
        try { value = JsonSerializer.Deserialize<EngineeringMappingStoredV1>(row.Body, Json); }
        catch (JsonException) { throw Error("ENGINEERING_MAPPING_ORIGINAL_UNAVAILABLE", 503); }
        if (value == null || value.Candidate == null || value.TenantId != db.CurrentTenantId || value.Id != id
            || value.Version != revision.ToString(CultureInfo.InvariantCulture) || value.EngineeringId != head.EngineeringId
            || value.Candidate.EngineeringRef?.Id != head.EngineeringId || value.State is not ("DRAFT" or "CONFIRMED" or "DISABLED")
            || value.State == "CONFIRMED" && (value.EngineeringConfirmation == null || value.BusinessConfirmation == null))
            throw Error("ENGINEERING_MAPPING_ORIGINAL_UNAVAILABLE", 503);
        return new(db.CurrentTenantId, new(MappingOwner, id, value.Version, row.Digest), value.State,
            revision == head.Revision, value.Candidate, value.EngineeringConfirmation, value.BusinessConfirmation, Encoding.UTF8.GetString(row.Body));
    }
    internal async Task<EngineeringMappingVersionV1> AppendMapping(Guid id, long? expected, string state,
        EngineeringMappingCandidateV1 candidate, EngineeringOwnerConfirmationV1? engineering,
        EngineeringOwnerConfirmationV1? business, CancellationToken ct)
    {
        Transaction(); var head = await MappingHeads().SingleOrDefaultAsync(row => row.Id == id, ct); long next;
        if (expected == null)
        {
            if (head != null) throw Error("ENGINEERING_MAPPING_IDENTITY_CONFLICT", 409);
            next = 1;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_engineering_product_v1.mapping_heads(tenant_id,mapping_id,engineering_id,revision)
                VALUES({db.CurrentTenantId},{id},{candidate.EngineeringRef.Id},1)
                """, ct);
        }
        else
        {
            if (head == null || head.EngineeringId != candidate.EngineeringRef.Id) throw Error("ENGINEERING_MAPPING_IDENTITY_CONFLICT", 409);
            if (head.Revision != expected) throw Error("ENGINEERING_MAPPING_CHANGED", 412);
            if (head.Revision == long.MaxValue) throw Error("ENGINEERING_MAPPING_VERSION_EXHAUSTED", 503);
            next = head.Revision + 1;
            if (await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE cp6_engineering_product_v1.mapping_heads SET revision={next}
                WHERE tenant_id={db.CurrentTenantId} AND mapping_id={id} AND revision={expected.Value}
                """, ct) != 1) throw Error("ENGINEERING_MAPPING_CHANGED", 412);
        }
        var body = Encode(new EngineeringMappingStoredV1(db.CurrentTenantId, id, next.ToString(CultureInfo.InvariantCulture),
            candidate.EngineeringRef.Id, state, candidate, engineering, business)); var digest = Digest(body);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_engineering_product_v1.mapping_versions(tenant_id,mapping_id,revision,body_bytes,body_digest)
            VALUES({db.CurrentTenantId},{id},{next},{body},{digest})
            """, ct);
        return await ReadMapping(id, next, ct);
    }
}
