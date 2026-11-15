using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.CrmIdentity;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Sys;

internal sealed record CrmUserSealAdvanceV1(string Aggregate, int Version, string? PredecessorType,
    string? PredecessorHash, string SuccessorType, string IntentHash);

internal sealed class IamCrmUserSealV1
{
    public Guid UserId { get; set; }
    public Guid OperationId { get; set; }
    public Guid ActorId { get; set; }
    public string Kind { get; set; } = null!;
    public string RequestProtected { get; set; } = null!;
    public Guid StoreId { get; set; }
    public string SourceSeals { get; set; } = null!;
    public bool SealStarted { get; set; }

    internal CrmUserSealAdvanceV1[] ReadAdvances() =>
        JsonSerializer.Deserialize<CrmUserSealAdvanceV1[]>(SourceSeals, IdentityEventContracts.Json)!;

    internal void RequireOwner(Guid actor, string kind)
    {
        if (ActorId != actor) throw IamAdministrationV1.Error(403, "IAM_OPERATION_OWNER_MISMATCH");
        if (Kind != kind) throw IamAdministrationV1.Error(409, "IAM_OPERATION_KIND_CONFLICT");
    }

    internal static Task<IamCrmUserSealV1?> ReadAsync(CP6Context db, Guid tenant, Guid operation,
        CancellationToken ct, Guid? user = null) => db.Database.SqlQuery<IamCrmUserSealV1>($"""
            SELECT user_id AS "UserId", operation_id AS "OperationId", actor_id AS "ActorId", kind AS "Kind",
                   request_protected AS "RequestProtected", store_id AS "StoreId", source_seals::text AS "SourceSeals",
                   seal_started AS "SealStarted"
            FROM cp6_authentication_v1.pending_crm_user_seals
            WHERE tenant_id={tenant} AND (operation_id={operation} OR user_id={user})
            ORDER BY (operation_id={operation}) DESC LIMIT 1
            """).SingleOrDefaultAsync(ct);

    internal static Task<bool> ExistsAsync(CP6Context db, Guid tenant, Guid user, CancellationToken ct) =>
        db.Database.SqlQuery<bool>($"""
            SELECT EXISTS(SELECT 1 FROM cp6_authentication_v1.pending_crm_user_seals
              WHERE tenant_id={tenant} AND user_id={user}) AS "Value"
            """).SingleAsync(ct);

    internal static Task InsertAsync(CP6Context db, Guid tenant, Guid user, Guid actor, string kind, Guid operation,
        string requestProtected, Guid store, IReadOnlyList<CrmUserSealAdvanceV1> advances, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(advances, IdentityEventContracts.Json);
        return db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_authentication_v1.pending_crm_user_seals
                (tenant_id,user_id,operation_id,actor_id,kind,request_protected,store_id,source_seals)
            VALUES ({tenant},{user},{operation},{actor},{kind},{requestProtected},{store},{json}::jsonb)
            """, ct);
    }

    internal Task MarkStartedAsync(CP6Context db, Guid tenant, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"""
        UPDATE cp6_authentication_v1.pending_crm_user_seals SET seal_started=true
        WHERE tenant_id={tenant} AND operation_id={OperationId}
        """, ct);

    internal Task RemoveAsync(CP6Context db, Guid tenant, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"""
        DELETE FROM cp6_authentication_v1.pending_crm_user_seals WHERE tenant_id={tenant} AND operation_id={OperationId}
        """, ct);
}
