using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.Core.Services.Common;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
namespace CP6.Core.Services.Platform;
internal static class PrivacyRestoreNativeV1
{
    internal const string MarkerPrefix="CP6-PRIVACY-RESTORE-CANDIDATE/v1/";
    internal static string IdentityJson(PrivacyRestoreDatabaseV1 value)=>JsonSerializer.Serialize(value);
    // Same name/OID is conservatively rejected even when connection aliases expose another server address.
    internal static bool SameDatabase(PrivacyRestoreDatabaseV1 a,PrivacyRestoreDatabaseV1 b)=>a.Name==b.Name&&a.Oid==b.Oid;
    internal static CP6Context Context(string connection,Guid tenant,CrmIdentityRuntime? runtime=null)
    {
        var cs=new NpgsqlConnectionStringBuilder(connection){Pooling=false,Timeout=5,CommandTimeout=10};
        var database=new DatabaseOptions(DatabaseProvider.PostgreSql);var profile=DatabaseMigrationProfile.For(database,DatabaseContextKind.Core);
        return new(DatabaseContextOptions.Configure(new DbContextOptionsBuilder<CP6Context>(),database,cs.ConnectionString,
            profile.MigrationsAssembly,profile.HistoryTable,profile.HistorySchema).Options,new TenantContext{CurrentTenantId=tenant},identity:runtime);
    }
    internal static Task<PrivacyRestoreDatabaseV1> Database(CP6Context db,CancellationToken ct)=>db.Database.SqlQueryRaw<PrivacyRestoreDatabaseV1>("""
        SELECT current_database() AS "Name",COALESCE(inet_server_addr()::text,'local') AS "Address",COALESCE(inet_server_port(),0) AS "Port",
          (SELECT oid::bigint FROM pg_database WHERE datname=current_database()) AS "Oid"
        """).SingleAsync(ct);
    internal static Task<string> Marker(CP6Context db,CancellationToken ct)=>db.Database.SqlQueryRaw<string>("""
        SELECT COALESCE(shobj_description(oid,'pg_database'),'') AS "Value" FROM pg_database WHERE datname=current_database()
        """).SingleAsync(ct);
    internal static Task<Guid> Epoch(CP6Context db,CancellationToken ct)=>db.Database.SqlQueryRaw<Guid>("SELECT version AS \"Value\" FROM cp6_privacy_disposition_v1.restore_epoch WHERE singleton").SingleAsync(ct);
    internal static async Task<IDbContextTransaction> CandidateGate(CP6Context candidate,PrivacyRestoreScopeV1 scope,CancellationToken ct)
    {
        await candidate.Database.OpenConnectionAsync(ct);
        // Check physical separation before requesting a lock: an alias to the source must not deadlock on its held gate.
        if(await Database(candidate,ct)!=scope.Target||SameDatabase(scope.Source,scope.Target))throw new PrivacyDispositionExceptionV1("PRIVACY_RESTORE_TARGET_IDENTITY_CHANGED",412);
        if(await Marker(candidate,ct)!=MarkerPrefix+scope.CandidateId.ToString("D"))throw new PrivacyDispositionExceptionV1("PRIVACY_RESTORE_TARGET_NOT_OWNED",412);
        var bound=await candidate.Database.SqlQueryRaw<Guid?>("SELECT environment_id AS \"Value\" FROM cp6_scope_authorization_v1.environment WHERE singleton").SingleOrDefaultAsync(ct);
        if(bound!=scope.EnvironmentId)throw new PrivacyDispositionExceptionV1("PRIVACY_RESTORE_TARGET_ENVIRONMENT_CHANGED",412);
        var tx=await candidate.Database.BeginTransactionAsync(ct);
        try{await candidate.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='10s'",ct);await AuthenticationCompletionGateV1.AcquireAsync(candidate,ct);return tx;}
        catch{await tx.DisposeAsync();throw;}
    }
    internal static Task<PrivacyRestoreResultV1?> Receipt(CP6Context candidate,Guid environment,Guid tenant,Guid actor,Guid operation,CancellationToken ct)
        =>ReadReceipt(candidate,environment,tenant,actor,operation,ct);
    private static async Task<PrivacyRestoreResultV1?> ReadReceipt(CP6Context candidate,Guid environment,Guid tenant,Guid actor,Guid operation,CancellationToken ct)
    {
        var json=await candidate.Database.SqlQuery<string>($"""
            SELECT result_json AS "Value" FROM cp6_privacy_disposition_v1.restore_receipts
            WHERE environment_id={environment} AND actor_tenant={tenant} AND actor_id={actor} AND operation_id={operation}
            """).SingleOrDefaultAsync(ct);
        return json==null?null:JsonSerializer.Deserialize<PrivacyRestoreResultV1>(json)!;
    }
    internal static async Task<PrivacyRestoreResultV1> Apply(CP6Context candidate,IPasswordHasher hasher,PrivacyRestoreScopeV1 scope,
        Guid actorTenant,Guid actor,Guid operation,Guid plan,string digest,PrivacyRestoreTombstoneV1[] tombstones,PrivacyExportPayloadTombstoneV1[] payloads,CancellationToken ct)
    {
        var units=new List<PrivacyRestoreUnitV1>();
        foreach(var tomb in tombstones)
        {
            var user=await candidate.Sys_Users.IgnoreQueryFilters().AsNoTracking().Where(x=>x.TenantId==tomb.TargetTenantId&&x.Id==tomb.SubjectId)
                .Select(x=>new{x.Id,x.IsPlatformAdmin}).SingleOrDefaultAsync(ct);
            if(user?.IsPlatformAdmin==true)throw new PrivacyDispositionExceptionV1("PRIVACY_RESTORE_IAM_AUTHORITY_RECOVERY_REQUIRED",409);
            Guid? before=null,after=null;
            if(user!=null)
            {
                before=await candidate.Database.SqlQuery<Guid>($"SELECT revision AS \"Value\" FROM cp6_authentication_v1.identity_admission_revisions WHERE tenant_id={tomb.TargetTenantId} AND object_key={"user:"+tomb.SubjectId.ToString("D")}").SingleAsync(ct);
                after=await PrivacyIdentityEffectV1.ApplyAsync(candidate,hasher,tomb.TargetTenantId,tomb.SubjectId,actor,ct);
            }
            var existing=await candidate.Database.SqlQuery<string>($"SELECT receipt_json AS \"Value\" FROM cp6_privacy_disposition_v1.tombstones WHERE plan_id={tomb.PlanId}").SingleOrDefaultAsync(ct);
            if(existing!=null&&existing!=tomb.Receipt)throw new PrivacyDispositionExceptionV1("PRIVACY_RESTORE_TOMBSTONE_CONFLICT",412);
            if(existing==null)await candidate.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_privacy_disposition_v1.tombstones(plan_id,environment_id,actor_tenant,target_tenant,subject_id,policy_id,receipt_json)
                VALUES({tomb.PlanId},{scope.EnvironmentId},{tomb.ActorTenantId},{tomb.TargetTenantId},{tomb.SubjectId},{tomb.PolicyId},{tomb.Receipt})
                """,ct);
            units.Add(new(tomb.PlanId,tomb.TargetTenantId,tomb.SubjectId,user==null?"ABSENT_FENCED":"REAPPLIED",before,after));
        }
        if(payloads.Length>0)await PrivacyExportPayloadEffectV1.Ready(candidate,ct);
        foreach(var tomb in payloads)units.Add(await PrivacyExportPayloadEffectV1.Qualify(candidate,tomb,scope.EnvironmentId,ct));
        var at=await candidate.Database.SqlQueryRaw<DateTimeOffset>("SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);
        var result=new PrivacyRestoreResultV1(operation,"APPLIED",plan,scope.CandidateId,scope.SourceVersion,digest,units.ToArray(),scope.UncoveredOwners,at);
        await candidate.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_privacy_disposition_v1.restore_receipts(environment_id,actor_tenant,actor_id,operation_id,result_json)
            VALUES({scope.EnvironmentId},{actorTenant},{actor},{operation},{JsonSerializer.Serialize(result)});
            """,ct);
        // Parent receipt, actual export effects and admission remain invisible until the same candidate commit.
        // Deferred SQL constraints reject a parent receipt whose listed effects have not happened.
        foreach(var tomb in payloads)await PrivacyExportPayloadEffectV1.Apply(candidate,tomb,scope.EnvironmentId,ct);
        await candidate.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_privacy_disposition_v1.restore_admission(singleton,candidate_id,source_identity,target_identity,environment_id,source_version,actor_tenant,actor_id,operation_id,plan_id)
            VALUES(true,{scope.CandidateId},{IdentityJson(scope.Source)},{IdentityJson(scope.Target)},{scope.EnvironmentId},{scope.SourceVersion},{actorTenant},{actor},{operation},{plan})
            ON CONFLICT(singleton) DO UPDATE SET candidate_id=EXCLUDED.candidate_id,source_identity=EXCLUDED.source_identity,target_identity=EXCLUDED.target_identity,
              environment_id=EXCLUDED.environment_id,source_version=EXCLUDED.source_version,actor_tenant=EXCLUDED.actor_tenant,actor_id=EXCLUDED.actor_id,
              operation_id=EXCLUDED.operation_id,plan_id=EXCLUDED.plan_id;
            """,ct);
        return result;
    }
}
internal sealed record PrivacyRestoreTombstoneV1(Guid PlanId,Guid ActorTenantId,Guid TargetTenantId,Guid SubjectId,Guid PolicyId,string Receipt);
