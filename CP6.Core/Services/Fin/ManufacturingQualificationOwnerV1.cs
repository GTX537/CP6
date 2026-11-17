using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Fin.ManufacturingFinanceScopeV1;

namespace CP6.Core.Services.Fin;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ManufacturingQualificationCommandV1(Guid CommandId, Guid SubjectId, string Purpose,
    bool Grant, Guid? ExpectedQualificationId, DateTimeOffset? ExpiresAt, string EvidenceReference, string Reason)
{
    [JsonRequired] public bool Grant { get; init; } = Grant;
}

public sealed record ManufacturingQualificationDecisionV1(Guid DecisionId, Guid QualificationId, bool Enabled);

public sealed class ManufacturingQualificationOwnerV1(CP6Context db, OperationsOwnerV1 owner, ManufacturingFinanceScopeV1 scope)
{
    public Task<ManufacturingQualificationDecisionV1> DecideAsync(Guid context, ManufacturingQualificationCommandV1 command,
        CancellationToken ct) => scope.WithContextAsync(owner, context, "qualification-admin", true, async (ledger, access, token) =>
    {
        if (command.CommandId == Guid.Empty || command.SubjectId == Guid.Empty
            || command.Purpose is not ("rate-review" or "rate-release" or "center-review" or "center-release")
            || string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Length > 1000
            || string.IsNullOrWhiteSpace(command.EvidenceReference) || command.EvidenceReference.Length > 256
            || command.Grant != command.ExpiresAt.HasValue)
            throw Error("FINANCE_QUALIFICATION_COMMAND_INVALID", 422);
        if (access.ActorId == command.SubjectId)
            throw Error("FINANCE_QUALIFICATION_INDEPENDENCE_REQUIRED", 403);

        var request = JsonSerializer.Serialize(command);
        var replay = await db.Database.SqlQuery<DecisionReplay>($"""
            SELECT id AS "DecisionId",qualification_id AS "QualificationId",enabled AS "Enabled",
                (issuer_id={access.ActorId} AND request=CAST({request} AS jsonb)) AS "Matches"
            FROM cp6_fin_mfg_v1.qualification_decisions
            WHERE tenant_id={db.CurrentTenantId} AND ledger_id={ledger.Id} AND command_id={command.CommandId}
            """).SingleOrDefaultAsync(token);
        if (replay != null)
            return replay.Matches ? new(replay.DecisionId, replay.QualificationId, replay.Enabled)
                : throw Error("FINANCE_COMMAND_CONFLICT", 409);

        var current = await db.Database.SqlQuery<QualificationRow>($"""
            SELECT qualification_id AS "Id",enabled AS "Enabled"
            FROM cp6_fin_mfg_v1.qualifications WHERE tenant_id={db.CurrentTenantId} AND ledger_id={ledger.Id}
                AND subject_id={command.SubjectId} AND action={command.Purpose}
            """).SingleOrDefaultAsync(token);
        if (current?.Id != command.ExpectedQualificationId)
            throw Error("FINANCE_QUALIFICATION_STALE", 412);

        var decisionId = Guid.NewGuid();
        Guid qualificationId;
        if (command.Grant)
        {
            if (!await scope.CanUseLedgerAsync(ledger.Id, command.SubjectId, token)
                || !await db.Sys_Users.AnyAsync(user => user.Id == command.SubjectId && user.Enable, token))
                throw Error("FINANCE_QUALIFICATION_SUBJECT_NOT_FOUND", 404);
            var expiresAt = command.ExpiresAt!.Value.ToUniversalTime();
            RequireFuture(expiresAt);
            access.AtCommit(_ => { RequireFuture(expiresAt); return Task.CompletedTask; });
            qualificationId = decisionId;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_fin_mfg_v1.qualifications
                    (tenant_id,ledger_id,subject_id,action,qualification_id,expires_at)
                VALUES ({db.CurrentTenantId},{ledger.Id},{command.SubjectId},{command.Purpose},{qualificationId},{expiresAt})
                ON CONFLICT (tenant_id,ledger_id,subject_id,action) DO UPDATE SET
                    qualification_id=EXCLUDED.qualification_id,expires_at=EXCLUDED.expires_at,enabled=true
                """, token);
        }
        else
        {
            if (current?.Enabled != true)
                throw Error("FINANCE_QUALIFICATION_NOT_ACTIVE", 409);
            qualificationId = current.Id;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE cp6_fin_mfg_v1.qualifications SET enabled=false
                WHERE tenant_id={db.CurrentTenantId} AND ledger_id={ledger.Id} AND subject_id={command.SubjectId}
                    AND action={command.Purpose}
                """, token);
        }
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_fin_mfg_v1.qualification_decisions
                (tenant_id,ledger_id,command_id,id,issuer_id,subject_id,action,qualification_id,enabled,request)
            VALUES ({db.CurrentTenantId},{ledger.Id},{command.CommandId},{decisionId},{access.ActorId},{command.SubjectId},
                {command.Purpose},{qualificationId},{command.Grant},CAST({request} AS jsonb))
            """, token);
        return new ManufacturingQualificationDecisionV1(decisionId, qualificationId, command.Grant);
    }, ct);

    private static void RequireFuture(DateTimeOffset until)
    {
        if (until <= DateTimeOffset.UtcNow)
            throw Error("FINANCE_QUALIFICATION_EXPIRY_INVALID", 422);
    }

    private sealed class QualificationRow
    {
        public Guid Id { get; set; }
        public bool Enabled { get; set; }
    }

    private sealed class DecisionReplay
    {
        public Guid DecisionId { get; set; }
        public Guid QualificationId { get; set; }
        public bool Enabled { get; set; }
        public bool Matches { get; set; }
    }
}
