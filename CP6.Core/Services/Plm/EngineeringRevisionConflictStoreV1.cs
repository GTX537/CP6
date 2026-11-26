using System.Text.Json;
using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;

namespace CP6.Core.Services.Plm;

internal sealed class EngineeringRevisionConflictStoreV1(CP6Context db)
{
    internal sealed class Original
    {
        public Guid Actor { get; init; }
        public byte[] Input { get; init; } = [];
        public string InputDigest { get; init; } = "";
        public byte[] Body { get; init; } = [];
        public string BodyDigest { get; init; } = "";

        internal EngineeringRevisionConflictV1 Decode()
        {
            if (EngineeringProductStoreV1.Digest(Input) != InputDigest || EngineeringProductStoreV1.Digest(Body) != BodyDigest)
                throw Error("ENGINEERING_REVISION_CONFLICT_ORIGINAL_UNAVAILABLE", 503);
            return JsonSerializer.Deserialize<EngineeringRevisionConflictV1>(Body, EngineeringProductStoreV1.Json)
                ?? throw Error("ENGINEERING_REVISION_CONFLICT_ORIGINAL_UNAVAILABLE", 503);
        }
    }

    internal Task<Original?> Read(Guid operation, CancellationToken ct) => db.Database.SqlQuery<Original>($"""
        SELECT actor_id AS "Actor",input_bytes AS "Input",input_digest AS "InputDigest",body AS "Body",body_digest AS "BodyDigest"
        FROM cp6_engineering_revision_v1.revision_conflicts
        WHERE tenant_id={db.CurrentTenantId} AND operation_id={operation}
        """).SingleOrDefaultAsync(ct);

    internal Task Save(Guid actor, byte[] input, EngineeringRevisionConflictV1 conflict, CancellationToken ct)
    {
        var body = EngineeringProductStoreV1.Encode(conflict);
        return db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_engineering_revision_v1.revision_conflicts
              (tenant_id,operation_id,actor_id,revision_id,expected_version,current_version,input_bytes,input_digest,body,body_digest)
            VALUES({db.CurrentTenantId},{conflict.OperationId},{actor},{conflict.Expected.Revision.Id},
              {Revision(conflict.Expected.Revision.Version)},{Revision(conflict.Current.Revision.Version)},
              {input},{EngineeringProductStoreV1.Digest(input)},{body},{EngineeringProductStoreV1.Digest(body)})
            """, ct);
    }
}
