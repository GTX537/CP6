using System.Diagnostics;
using System.Text.Json;
using CP6.Core.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;

namespace CP6.Tests.Persistence.Tenancy;

[Collection("TenantStoragePostgres")]
public sealed class TenantProvisioningPostgresTests(TenantStoragePostgresFixture server)
{
    [TenantStoragePostgresFact]
    public async Task Atomic_install_append_replay_conflict_interruptions_drift_and_real_cli()
    {
        const string database="provisioning_target";
        await using (var admin=await server.OpenOwnerAsync("postgres"))
            await TenantStoragePostgresFixture.Sql(admin,$"CREATE DATABASE {database}");
        string system; uint oid;
        await using (var admin=await server.OpenOwnerAsync(database))
        {
            system=(string)(await TenantStoragePostgresFixture.Scalar(admin,"SELECT system_identifier::text FROM pg_control_system()"))!;
            oid=(uint)(await TenantStoragePostgresFixture.Scalar(admin,"SELECT oid FROM pg_database WHERE datname=current_database()"))!;
            _=new TenantProvisioningMigrations(admin);
        }
        var first=new TenantProvisioningRequest(Guid.NewGuid(),TenantStorageContext.A,"tenant_a","127.0.0.1",55436,database,system,oid);
        var second=first with {OperationId=Guid.NewGuid(),TenantId=TenantStorageContext.B,RuntimeLogin="tenant_b"};
        async Task<TenantProvisioningResult> Run(TenantProvisioningRequest request,Func<TenantProvisioningCheckpoint,Task>? fault=null)
        {
            await using var connection=await server.OpenOwnerAsync(database);
            return await new TenantStorageProvisioner(fault).ProvisionAsync(request,connection);
        }
        async Task<string> Reject(TenantProvisioningRequest request)
            => (await Assert.ThrowsAsync<TenantProvisioningException>(()=>Run(request))).Code;

        Assert.Equal("target-identity-rejected",await Reject(first with {DatabaseOid=oid+1}));
        await using (var admin=await server.OpenOwnerAsync(database))
        {
            foreach(var (create,drop) in new[]{("CREATE TABLE public.unknown(id int)","DROP TABLE public.unknown"),
                ("CREATE OPERATOR public.=== (LEFTARG=integer,RIGHTARG=integer,FUNCTION=pg_catalog.int4eq)","DROP OPERATOR public.=== (integer,integer)"),
                ("CREATE CAST (text AS uuid) WITH INOUT","DROP CAST (text AS uuid)"),
                ("GRANT CREATE ON SCHEMA public TO tenant_b","REVOKE CREATE ON SCHEMA public FROM tenant_b")})
            {
                await TenantStoragePostgresFixture.Sql(admin,create);
                Assert.Equal("unknown-layout",await Reject(first));
                await TenantStoragePostgresFixture.Sql(admin,drop);
            }
        }
        var reachedBeforeCommit=false;
        var interrupted=await Assert.ThrowsAsync<TenantProvisioningException>(()=>Run(first,p=>
        {
            reachedBeforeCommit=p==TenantProvisioningCheckpoint.BeforeCommit;
            return Task.FromException(new IOException("simulated interruption"));
        }));
        Assert.True(reachedBeforeCommit,interrupted.Code);
        Assert.Equal("execution-uncertain-retry-same-operation",interrupted.Code);
        await using (var admin=await server.OpenOwnerAsync(database))
        {
            Assert.Equal(0L,await TenantStoragePostgresFixture.Scalar(admin,"SELECT count(*) FROM pg_namespace WHERE nspname IN ('cp6_provisioning_v1','cp6_tenancy_v1','erp_integration','crm_identity_priority')"));
            Assert.Equal(0L,await TenantStoragePostgresFixture.Scalar(admin,"SELECT count(*) FROM pg_roles WHERE rolname='cp6_tenant_lookup_v1'"));
        }
        var concurrent=await Task.WhenAll(Run(first),Run(first));
        Assert.Equal(concurrent[0],concurrent[1]);
        Assert.Equal(concurrent[0],await Run(first));
        Assert.Equal("operation-conflict",await Reject(first with {TenantId=Guid.NewGuid()}));
        Assert.Equal("mapping-conflict",await Reject(first with {OperationId=Guid.NewGuid()}));

        var reachedAfterCommit=false;
        await Assert.ThrowsAsync<TenantProvisioningException>(()=>Run(second,p=>
        {
            if(p!=TenantProvisioningCheckpoint.AfterCommit)return Task.CompletedTask;
            reachedAfterCommit=true;
            return Task.FromException(new IOException("simulated lost response"));
        }));
        Assert.True(reachedAfterCommit);
        TenantProvisioningResult committed;
        await using(var fresh=await server.OpenOwnerAsync(database))
        {
            var persisted=(string)(await TenantStoragePostgresFixture.Scalar(fresh,$"SELECT result FROM cp6_provisioning_v1.operations WHERE operation_id='{second.OperationId}'"))!;
            Assert.Equal(persisted,await TenantStoragePostgresFixture.Scalar(fresh,$"SELECT result FROM {second.Schema}.initialization_history_v1 WHERE operation_id='{second.OperationId}'"));
            committed=JsonSerializer.Deserialize<TenantProvisioningResult>(persisted)!;
        }
        var recovered=await Run(second);
        Assert.Equal(committed,recovered);
        Assert.Equal(recovered,await Run(second));
        Assert.Equal(second.OperationId,recovered.OperationId);
        await using (var admin=await server.OpenOwnerAsync(database))
        {
            Assert.Equal(2L,await TenantStoragePostgresFixture.Scalar(admin,"SELECT count(*) FROM cp6_provisioning_v1.operations"));
            foreach(var (tenant,role) in new[]{(TenantStorageContext.A,"tenant_a"),(TenantStorageContext.B,"tenant_b")})
                await TenantStoragePostgresFixture.Sql(admin,$"INSERT INTO erp_integration.\"Aggregate\" (\"TenantId\",\"Kind\",\"AggregateId\",\"LastRequestVersion\",\"LastAggregateVersion\",\"LastResultVersion\") VALUES ('{tenant}',1,'{Guid.NewGuid()}',1,1,1)");
        }
        await using var modelConnection=await server.OpenOwnerAsync(database,"public");
        await using var modelContext=IdentityTenantPostgresFixture.Context(modelConnection);
        var entity=modelContext.Model.GetEntityTypes().Single(e=>e.GetTableName()=="Cp6_OutboxMessage");
        async Task<int> InsertIdentity(NpgsqlConnection c,Guid tenant)
        {
            var row=ErpTenantPostgresFixture.Row(entity.ClrType,tenant);
            var properties=entity.GetProperties().Where(p=>p.ValueGenerated!=ValueGenerated.OnAddOrUpdate).ToArray();
            var store=StoreObjectIdentifier.Table("Cp6_OutboxMessage","crm_identity_priority");
            await using var command=new NpgsqlCommand("INSERT INTO crm_identity_priority.\"Cp6_OutboxMessage\" ("
                +string.Join(',',properties.Select(p=>TenantStorageProvisioner.Q(p.GetColumnName(store)!)))+") VALUES ("
                +string.Join(',',properties.Select((_,i)=>"@p"+i))+")",c);
            for(var i=0;i<properties.Length;i++)command.Parameters.Add(properties[i].GetRelationalTypeMapping().CreateParameter(command,"p"+i,properties[i].PropertyInfo!.GetValue(row)));
            return await command.ExecuteNonQueryAsync();
        }
        foreach(var (tenant,role,password) in new[]{(TenantStorageContext.A,"tenant_a",server.PasswordA),(TenantStorageContext.B,"tenant_b",server.PasswordB)})
        {
            await using var runtime=await TenantStoragePostgresFixture.OpenRawAsync(role,password,database);
            Assert.Equal(1L,await TenantStoragePostgresFixture.Scalar(runtime,"SELECT count(*) FROM erp_integration.\"Aggregate\""));
            Assert.Equal(tenant,await TenantStoragePostgresFixture.Scalar(runtime,"SELECT \"TenantId\" FROM erp_integration.\"Aggregate\""));
            foreach(var sql in new[]{"CREATE SCHEMA unauthorized","CREATE TABLE public.unauthorized(id int)",
                "SELECT * FROM cp6_provisioning_v1.operations",$"SELECT * FROM t_{tenant:N}.initialization_history_v1",
                $"CREATE TABLE t_{tenant:N}.unauthorized(id int)","SET ROLE cp6_tenant_lookup_v1"})
                Assert.Equal(PostgresErrorCodes.InsufficientPrivilege,(await Assert.ThrowsAsync<PostgresException>(()=>TenantStoragePostgresFixture.Sql(runtime,sql))).SqlState);
            var other=tenant==TenantStorageContext.A?TenantStorageContext.B:TenantStorageContext.A;
            Assert.Equal(1,await InsertIdentity(runtime,tenant));
            Assert.Equal(1L,await TenantStoragePostgresFixture.Scalar(runtime,"SELECT count(*) FROM crm_identity_priority.\"Cp6_OutboxMessage\""));
            Assert.Equal(0L,await TenantStoragePostgresFixture.Scalar(runtime,$"SELECT count(*) FROM crm_identity_priority.\"Cp6_OutboxMessage\" WHERE \"TenantId\"='{other}'"));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege,(await Assert.ThrowsAsync<PostgresException>(()=>InsertIdentity(runtime,other))).SqlState);
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege,(await Assert.ThrowsAsync<PostgresException>(()=>TenantStoragePostgresFixture.Sql(runtime,$"UPDATE crm_identity_priority.\"Cp6_OutboxMessage\" SET \"TenantId\"='{other}'"))).SqlState);
        }
        await using(var tenantA=await TenantStoragePostgresFixture.OpenRawAsync("tenant_a",server.PasswordA,database))
            Assert.Equal(0L,await TenantStoragePostgresFixture.Scalar(tenantA,$"SELECT count(*) FROM crm_identity_priority.\"Cp6_OutboxMessage\" WHERE \"TenantId\"='{TenantStorageContext.B}'"));
        Assert.Equal(concurrent[0],await Run(first)); // normal data and sequence movement preserve the seal
        await using (var admin=await server.OpenOwnerAsync(database))
        {
            foreach(var (mutation,repair,expected) in new[]{
                ("ALTER TABLE erp_integration.\"Aggregate\" DISABLE ROW LEVEL SECURITY","ALTER TABLE erp_integration.\"Aggregate\" ENABLE ROW LEVEL SECURITY","layout-drift"),
                ("GRANT UPDATE ON cp6_tenancy_v1.login_tenant TO tenant_a","REVOKE UPDATE ON cp6_tenancy_v1.login_tenant FROM tenant_a","layout-drift"),
                ("UPDATE public.\"__EFMigrationsHistory_ErpIntegration\" SET \"ProductVersion\"='bad'","UPDATE public.\"__EFMigrationsHistory_ErpIntegration\" SET \"ProductVersion\"='8.0.30'","history-rejected"),
                ($"UPDATE {first.Schema}.initialization_history_v1 SET result='bad'",$"UPDATE {first.Schema}.initialization_history_v1 SET result=(SELECT result FROM cp6_provisioning_v1.operations WHERE operation_id='{first.OperationId}')","history-rejected")})
            {
                await TenantStoragePostgresFixture.Sql(admin,mutation);
                Assert.Equal(expected,await Reject(first));
                await TenantStoragePostgresFixture.Sql(admin,repair);
            }
        }
        // A separately launched CLI receives credentials on stdin; neither argv nor output contains them.
        var cli=Environment.GetEnvironmentVariable("CP6_TENANCY_CLI") ?? throw new InvalidOperationException("CP6_TENANCY_CLI must point to the built tool for this requested test.");
        var payload=JsonSerializer.Serialize(new {Request=first,AdminUsername="fixture_owner",AdminPassword=server.ProvisioningAdminPassword,SslMode="Disable"});
        var success=await Cli(cli,["provision","--stdin"],payload);
        Assert.Equal(0,success.Code); Assert.Empty(success.Error);
        Assert.Equal(concurrent[0],JsonSerializer.Deserialize<TenantProvisioningResult>(success.Output));
        foreach(var payloadOrArgs in new[]{(new[]{"provision","--stdin"},payload[..^1]+",\"Unknown\":true}"),
            (new[]{"provision","--password","secret"},payload),(new[]{"provision","--stdin"},"not json "+server.ProvisioningAdminPassword),
            (new[]{"provision","--stdin"},payload.Replace("\"TenantId\":",$"\"TenantId\":\"{Guid.NewGuid()}\",\"TenantId\":")),
            (new[]{"provision","--stdin"},payload.Replace(server.ProvisioningAdminPassword,"intentionally-invalid-private-password"))})
        {
            var denied=await Cli(cli,payloadOrArgs.Item1,payloadOrArgs.Item2);
            Assert.NotEqual(0,denied.Code); Assert.DoesNotContain(server.ProvisioningAdminPassword,denied.Output+denied.Error);
            Assert.DoesNotContain("secret",denied.Output+denied.Error);
        }
    }

    private static async Task<(int Code,string Output,string Error)> Cli(string path,string[] args,string input)
    {
        var info=new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        info.ArgumentList.Add(path);foreach(var arg in args)info.ArgumentList.Add(arg);
        using var process=Process.Start(info)!;
        var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
        try {await process.StandardInput.WriteAsync(input);process.StandardInput.Close();} catch(IOException) { }
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90));
        await process.WaitForExitAsync(timeout.Token);
        return(process.ExitCode,await output,await error);
    }
}
