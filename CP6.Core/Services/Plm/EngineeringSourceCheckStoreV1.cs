using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;
using static CP6.Core.Services.Plm.EngineeringSourceCheckValuesV1;

namespace CP6.Core.Services.Plm;
internal sealed class EngineeringSourceCheckStoreV1(CP6Context db)
{
    private IDbContextTransaction? fenced;
    internal async Task Fence(CancellationToken ct)
    {
        if (!db.Database.IsNpgsql() || db.CurrentTenantId == Guid.Empty || db.Database.CurrentTransaction == null)
            throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503);
        await db.Database.ExecuteSqlRawAsync("LOCK TABLE cp6_engineering_check_v1.runs IN SHARE ROW EXCLUSIVE MODE", ct);
        fenced = db.Database.CurrentTransaction;
    }
    private void Ready()
    {
        if (fenced == null || !ReferenceEquals(fenced, db.Database.CurrentTransaction)) throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503);
    }
    internal async Task<EngineeringSourceRefV1> Save(EngineeringCheckRunV1 value, CancellationToken ct)
    {
        Ready(); var bytes = EngineeringProductStoreV1.Encode(value); var digest = EngineeringProductStoreV1.Digest(bytes);
        if (bytes.Length > 134217728) throw Error("ENGINEERING_CHECK_SIZE_LIMIT", 422);
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_engineering_check_v1.runs(tenant_id,id,revision_id,operation_id,actor_id,body,digest)
            VALUES({db.CurrentTenantId},{value.Id},{value.Command.Revision.Id},{value.Command.OperationId},{value.ActorId},{bytes},{digest})
            """, ct);
        if (inserted != 1) throw Error("ENGINEERING_CHECK_WRITE_INCOMPLETE", 503);
        var stored = await Read(value.Id, ct);
        if (stored.Reference.Digest != digest) throw Error("ENGINEERING_CHECK_WRITE_INCOMPLETE", 503);
        return new(EngineeringSourceCheckValuesV1.Owner, value.Id, "1", digest);
    }
    private sealed class Row { public byte[] Body { get; init; } = []; public string Digest { get; init; } = ""; public Guid Revision { get; init; } public Guid Actor { get; init; } public Guid Operation { get; init; } }
    internal async Task<(EngineeringSourceRefV1 Reference, EngineeringCheckRunV1 Value, string Json)> Read(Guid id, CancellationToken ct)
    {
        Ready();
        var row = await db.Database.SqlQuery<Row>($"""
            SELECT body AS "Body",digest AS "Digest",revision_id AS "Revision",actor_id AS "Actor",operation_id AS "Operation"
            FROM cp6_engineering_check_v1.runs WHERE tenant_id={db.CurrentTenantId} AND id={id}
            """).SingleOrDefaultAsync(ct) ?? throw Error("ENGINEERING_CHECK_NOT_FOUND", 404);
        if (EngineeringProductStoreV1.Digest(row.Body) != row.Digest) throw Error("ENGINEERING_CHECK_ORIGINAL_UNAVAILABLE", 503);
        EngineeringCheckRunV1 value;
        try { value = JsonSerializer.Deserialize<EngineeringCheckRunV1>(row.Body, EngineeringProductStoreV1.Json) ?? throw new JsonException(); }
        catch (JsonException) { throw Error("ENGINEERING_CHECK_ORIGINAL_UNAVAILABLE", 503); }
        if (value.TenantId != db.CurrentTenantId || value.Id != id || value.Command.Revision.Id != row.Revision || value.ActorId != row.Actor
            || value.Command.OperationId != row.Operation || value.Purpose != "TECHNICAL_PRECHECK_ONLY"
            || value.InputDigest != Digest(value.Input) || value.State != State(value.Items)) throw Error("ENGINEERING_CHECK_ORIGINAL_UNAVAILABLE", 503);
        return (new(EngineeringSourceCheckValuesV1.Owner, id, "1", row.Digest), value, Encoding.UTF8.GetString(row.Body));
    }
    internal Task<Guid[]> History(Guid revision, int page, CancellationToken ct)
    {
        Ready();
        return db.Database.SqlQuery<Guid>($"""
            SELECT id AS "Value" FROM cp6_engineering_check_v1.runs WHERE tenant_id={db.CurrentTenantId} AND revision_id={revision}
            ORDER BY recorded_at DESC,id OFFSET {(page - 1) * 20} LIMIT 20
            """).ToArrayAsync(ct);
    }
}
