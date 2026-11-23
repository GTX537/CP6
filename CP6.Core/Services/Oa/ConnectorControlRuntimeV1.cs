using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CP6.Core.Services.Oa;

public sealed record RegisteredConnectorVersionV1(ConnectorPinV1 Pin,string Implementation,string ImplementationVersion,
    string DestinationPolicy,string EndpointProfile,string InputSchema,string OutputSchema,string DisclosurePolicy,
    string? CredentialLocator,string? CredentialGeneration,string EffectClass,int MaxCallDurationSec,bool Simulated);

/// <summary>已登记的派发参与者须在本次控制事务中建立准入记录。后继卡负责外部调用与原结果恢复。</summary>
public interface IConnectorDispatchAdmissionV1
{
    bool IsSimulation => false;
    bool Matches(RegisteredConnectorVersionV1 version);
    Task<ServiceTaskResult> AdmitAsync(CP6Context context,RegisteredConnectorVersionV1 version,ServiceTaskContext job,CancellationToken ct);
}

public sealed class ConnectorControlRuntimeV1(CP6Context db,ScopeAuthorizationFenceV1 fence,
    IOptions<OaConnectorControlOptionsV1> options,IEnumerable<IConnectorDispatchAdmissionV1> dispatchers)
{
    private sealed record Row(string SourceKind,string Name,string State,Guid? Active,string Profile,string Adoption,
        bool Enabled,string? Evidence);
    // 旧按名调用持名称共享锁直到返回，交接在同一名称尝试独占锁。外呼不占用 Main 身份事务锁。
    internal async Task<ServiceTaskResult> LegacyCall(string name,Func<Task<ServiceTaskResult>> call,CancellationToken ct)
    {
        if(!db.Database.IsNpgsql()) return await call();
        await using var connection=new Npgsql.NpgsqlConnection(db.Database.GetConnectionString());
        await connection.OpenAsync(ct);
        await using var tx=await connection.BeginTransactionAsync(ct);
        await using var read=new Npgsql.NpgsqlCommand("SELECT pg_advisory_xact_lock_shared(cp6_oa_conn_v1.name_lock($1,$2))",connection,tx);
        read.Parameters.Add(new Npgsql.NpgsqlParameter{Value=db.CurrentTenantId});
        read.Parameters.Add(new Npgsql.NpgsqlParameter{Value=name});
        await read.ExecuteNonQueryAsync(ct);
        read.CommandText="""
            SELECT EXISTS(SELECT FROM cp6_oa_conn_v1.roots WHERE tenant_id=$1 AND kind='CONNECTOR' AND lower(name)=lower($2)
                AND environment_id IN (SELECT environment_id FROM cp6_scope_authorization_v1.environment WHERE singleton))
            """;
        if((bool)(await read.ExecuteScalarAsync(ct))!) return ServiceTaskResult.Blocked("BLOCKED_CONNECTOR");
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
    internal async Task<RegisteredConnectorVersionV1?> Resolve(ConnectorPinV1 pin,CancellationToken ct)
    {
        fence.RequireTransaction();
        var r=await db.Database.SqlQuery<Row>($"""
            SELECT r.source_kind AS "SourceKind",r.name AS "Name",v.state AS "State",r.active_version AS "Active",
              p.content::text AS "Profile",p.adoption AS "Adoption",coalesce(c.enabled,false) AS "Enabled",p.evidence_ref AS "Evidence"
            FROM cp6_oa_conn_v1.roots r
            JOIN cp6_oa_conn_v1.version_control v ON (v.tenant_id,v.environment_id,v.root_id)=(r.tenant_id,r.environment_id,r.id)
            JOIN cp6_oa_conn_v1.versions x ON (x.tenant_id,x.environment_id,x.root_id,x.id)=(v.tenant_id,v.environment_id,v.root_id,v.version_id)
            JOIN cp6_oa_conn_v1.profiles p ON (p.tenant_id,p.environment_id,p.id)=(x.tenant_id,x.environment_id,x.profile_id)
            LEFT JOIN cp6_oa_conn_v1.profile_control c ON (c.tenant_id,c.environment_id,c.profile_id)=(p.tenant_id,p.environment_id,p.id)
            WHERE r.tenant_id={db.CurrentTenantId} AND r.environment_id={fence.EnvironmentId} AND r.kind='CONNECTOR'
              AND r.id={pin.RootId} AND v.version_id={pin.VersionId}
            """).SingleOrDefaultAsync(ct);
        if(r==null || r.SourceKind!=pin.SourceKind || r.Active!=pin.VersionId || r.State!="ENABLED" || !r.Enabled ||
            string.IsNullOrWhiteSpace(r.Evidence) || !(r.Adoption=="ADOPTED" || r.Adoption=="SIMULATED" && options.Value.AllowSimulatedParticipants)) return null;
        var spec=JsonSerializer.Deserialize<ConnProfileV1>(r.Profile,ConnectorVersionControlService.Json)!;
        if(spec.Operation!=pin.Operation || spec.MaxCallDurationSec is not >0 || spec.MaxCallDurationSec>=WfServiceJobService.LeaseDuration.TotalSeconds) return null;
        return Describe(pin,spec,r.Adoption=="SIMULATED");
    }
    internal static RegisteredConnectorVersionV1 Describe(ConnectorPinV1 pin,ConnProfileV1 spec,bool simulated) =>
        new(pin,spec.Implementation!,spec.ImplementationVersion!,spec.DestinationPolicy!,spec.EndpointProfile!,spec.InputSchema!,
            spec.OutputSchema!,spec.DisclosurePolicy!,spec.CredentialLocator,spec.CredentialGeneration,spec.EffectClass!,spec.MaxCallDurationSec!.Value,simulated);
    internal async Task<ServiceTaskResult> Admit(ConnectorPinV1 pin,ServiceTaskContext job,CancellationToken ct)
    {
        if(!db.Database.IsNpgsql() || job.JobId==Guid.Empty || db.Database.CurrentTransaction!=null)
            return ServiceTaskResult.Blocked("BLOCKED_CONNECTOR");
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        var lease=await fence.AcquireAsync(ct);
        var version=await Resolve(pin,ct);
        if(version==null) return ServiceTaskResult.Blocked("BLOCKED_CONNECTOR");
        if(await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS(SELECT FROM cp6_oa_conn_v1.runtime_members WHERE tenant_id={db.CurrentTenantId}
                AND environment_id={fence.EnvironmentId} AND root_id={pin.RootId} AND kind='DISPATCH' AND id={job.JobId}) AS "Value"
            """).SingleAsync(ct)) return ServiceTaskResult.Blocked("DISPATCH_RECOVERY_REQUIRED");
        var participants=dispatchers.Where(d=>d.IsSimulation==version.Simulated && d.Matches(version)).Take(2).ToArray();
        if(participants.Length!=1) return ServiceTaskResult.Blocked("DISPATCH_PARTICIPANT_UNPROVEN");
        var result=await participants[0].AdmitAsync(db,version,job,ct);
        await fence.ValidateAsync(lease,ct);
        await tx.CommitAsync(ct);
        return result;
    }
}
