using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CP6.Core.Services.Oa;

public sealed partial class ConnectorControlRuntimeV1(CP6Context db,ScopeAuthorizationFenceV1 fence,
    IOptions<OaConnectorControlOptionsV1> options,IEnumerable<IConnectorDispatchAdmissionV1> dispatchers)
{
    private sealed record Row(string SourceKind,string State,Guid? Active,Guid RootRevision,Guid ProfileId,
        Guid? ProfileRevision,string Profile,string Adoption,bool Enabled,string? Evidence);
    private sealed record VersionSelection(RegisteredConnectorVersionV1 Version,Guid RootRevision,Guid ProfileRevision);
    // 旧按名调用持名称共享锁直到返回，交接在同一名称尝试独占锁。外呼不占用 Main 身份事务锁。
    internal async Task<ServiceTaskResult> LegacyCall(string name,Guid jobId,Func<Task<ServiceTaskResult>> call,CancellationToken ct)
    {
        if(!db.Database.IsNpgsql()) return await call();
        await using var connection=new Npgsql.NpgsqlConnection(db.Database.GetConnectionString());
        await connection.OpenAsync(ct);
        await using var tx=await connection.BeginTransactionAsync(ct);
        if (jobId != Guid.Empty)
        {
            await using var jobLock = new Npgsql.NpgsqlCommand("SELECT pg_advisory_xact_lock(cp6_oa_conn_v1.job_lock($1,$2))",connection,tx);
            jobLock.Parameters.Add(new Npgsql.NpgsqlParameter { Value = db.CurrentTenantId });
            jobLock.Parameters.Add(new Npgsql.NpgsqlParameter { Value = jobId });
            await jobLock.ExecuteNonQueryAsync(ct);
        }
        await using var read=new Npgsql.NpgsqlCommand("SELECT pg_advisory_xact_lock_shared(cp6_oa_conn_v1.name_lock($1,$2))",connection,tx);
        read.Parameters.Add(new Npgsql.NpgsqlParameter{Value=db.CurrentTenantId});
        read.Parameters.Add(new Npgsql.NpgsqlParameter{Value=name});
        await read.ExecuteNonQueryAsync(ct);
        // 已接管的 Job 以原身份识别。旧名称或当前名称的变化不能重新打开旧写入口。
        read.Parameters.Add(new Npgsql.NpgsqlParameter{Value=jobId});
        read.CommandText="""
            WITH scope_environment AS (SELECT environment_id FROM cp6_scope_authorization_v1.environment WHERE singleton)
            SELECT CASE WHEN EXISTS(SELECT FROM cp6_oa_conn_v1.roots WHERE tenant_id=$1 AND kind='CONNECTOR' AND lower(name)=lower($2)
                    AND environment_id IN (SELECT environment_id FROM scope_environment))
                THEN 'BLOCKED_CONNECTOR'
                WHEN EXISTS(SELECT FROM cp6_oa_conn_v1.runtime_members WHERE tenant_id=$1 AND kind='DISPATCH' AND id=$3
                    AND environment_id IN (SELECT environment_id FROM scope_environment))
                THEN 'DISPATCH_RECOVERY_REQUIRED'
                WHEN EXISTS(SELECT FROM public."Wf_ServiceJob" WHERE "TenantId"=$1 AND "Id"=$3
                    AND jsonb_typeof(cp6_oa_conn_v1.action_ref("ActionRefJson"::text)->'connectorPin')='object')
                THEN 'BLOCKED_CONNECTOR' END
            """;
        if(await read.ExecuteScalarAsync(ct) is string blocked) return ServiceTaskResult.Blocked(blocked);
        var result=await call();
        await tx.CommitAsync(ct);
        return result;
    }
    public Task<bool> GovernsLegacy(string kind,Guid? id,string? name,CancellationToken ct) =>
        !db.Database.IsNpgsql() ? Task.FromResult(false) : db.Database.SqlQuery<bool>($"""
            SELECT EXISTS(SELECT FROM cp6_oa_conn_v1.roots WHERE tenant_id={db.CurrentTenantId} AND kind={kind}
                AND environment_id IN (SELECT environment_id FROM cp6_scope_authorization_v1.environment WHERE singleton)
                AND (id={id} OR (kind='CONNECTOR' AND source_kind='TENANT' AND lower(name)=lower({name})))) AS "Value"
            """).SingleAsync(ct);
    internal async Task<RegisteredConnectorVersionV1?> Resolve(ConnectorPinV1 pin,CancellationToken ct) =>
        (await ReadVersion(pin,false,ct))?.Version;

    private async Task<VersionSelection?> ReadVersion(ConnectorPinV1 pin,bool forRecovery,CancellationToken ct)
    {
        fence.RequireTransaction();
        var r=await db.Database.SqlQuery<Row>($"""
            SELECT r.source_kind AS "SourceKind",v.state AS "State",r.active_version AS "Active",
              r.revision AS "RootRevision",p.id AS "ProfileId",c.revision AS "ProfileRevision",
              p.content::text AS "Profile",p.adoption AS "Adoption",coalesce(c.enabled,false) AS "Enabled",p.evidence_ref AS "Evidence"
            FROM cp6_oa_conn_v1.roots r
            JOIN cp6_oa_conn_v1.version_control v ON (v.tenant_id,v.environment_id,v.root_id)=(r.tenant_id,r.environment_id,r.id)
            JOIN cp6_oa_conn_v1.versions x ON (x.tenant_id,x.environment_id,x.root_id,x.id)=(v.tenant_id,v.environment_id,v.root_id,v.version_id)
            JOIN cp6_oa_conn_v1.profiles p ON (p.tenant_id,p.environment_id,p.id)=(x.tenant_id,x.environment_id,x.profile_id)
            LEFT JOIN cp6_oa_conn_v1.profile_control c ON (c.tenant_id,c.environment_id,c.profile_id)=(p.tenant_id,p.environment_id,p.id)
            WHERE r.tenant_id={db.CurrentTenantId} AND r.environment_id={fence.EnvironmentId} AND r.kind='CONNECTOR'
              AND r.id={pin.RootId} AND v.version_id={pin.VersionId}
            """).SingleOrDefaultAsync(ct);
        if(r==null || r.SourceKind!=pin.SourceKind || !forRecovery && (r.Active!=pin.VersionId || r.State!="ENABLED") ||
            !r.Enabled || r.ProfileRevision==null ||
            string.IsNullOrWhiteSpace(r.Evidence) || !(r.Adoption=="ADOPTED" || r.Adoption=="SIMULATED" && options.Value.AllowSimulatedParticipants)) return null;
        var spec=JsonSerializer.Deserialize<ConnProfileV1>(r.Profile,ConnectorVersionControlService.Json)!;
        if(spec.Operation!=pin.Operation || spec.MaxCallDurationSec is not >0 || spec.MaxCallDurationSec>=WfServiceJobService.LeaseDuration.TotalSeconds) return null;
        return new(Describe(pin,spec,r.Adoption=="SIMULATED",db.CurrentTenantId,fence.EnvironmentId,r.ProfileId,r.Evidence),
            r.RootRevision,r.ProfileRevision.Value);
    }
    internal static RegisteredConnectorVersionV1 Describe(ConnectorPinV1 pin,ConnProfileV1 spec,bool simulated,
        Guid tenant,Guid environment,Guid profile,string evidence) =>
        new(pin,spec.Implementation!,spec.ImplementationVersion!,spec.DestinationPolicy!,spec.EndpointProfile!,spec.InputSchema!,
            spec.OutputSchema!,spec.DisclosurePolicy!,spec.CredentialLocator,spec.CredentialGeneration,spec.EffectClass!,
            spec.MaxCallDurationSec!.Value,simulated,tenant,environment,profile,evidence,spec.ParameterMapping!,
            spec.IdempotencyContract,spec.ResultQueryContract,spec.AdmissionBasis,spec.RequiresCurrentAuthorizationAtApply)
        {
            ResponseMapping = spec.ResponseMapping
        };
}
