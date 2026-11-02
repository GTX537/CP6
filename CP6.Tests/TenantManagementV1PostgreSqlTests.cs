using System.Net;
using System.Security.Claims;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.Core.Services.Common;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private async Task<Harness> TenantManagementSeed()
    {
        var h = await Seed();
        await h.Db.Sys_Users.Where(x => x.Id == h.User).ExecuteUpdateAsync(x => x.SetProperty(u => u.IsPlatformAdmin, true));
        ((ClaimsIdentity)h.Http.User.Identity!).AddClaim(new("is_platform_admin", "true"));
        return h;
    }
    private async Task<Guid> ManagementTarget(Harness h)
    {
        var target = new Sys_Tenant { TenantCode = "management_" + Guid.NewGuid().ToString("N"), TenantName = "原租户 ", Remark = "原备注 ", TimeZoneId = "UTC",
            ExpireDate = new DateTime(2030, 1, 2, 3, 4, 5).AddTicks(1234560) };
        h.Db.Sys_Tenants.Add(target); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear(); return target.Id;
    }
    private async Task<T> ManagementCall<T>(Harness h, Func<TenantAdminService, TenantManagementSessionV1, Task<T>> call,
        Func<CancellationToken, Task<Guid>>? session = null, params IInterceptor[] interceptors)
    {
        await using var db = server.Context(new TenantContext { CurrentTenantId = h.Tenant }, null, interceptors);
        session ??= _ => Task.FromResult(h.User);
        return await call(new(db, new BCryptPasswordHasher(4), http: new NumberHttp(h.Http)), new(session, async ct => { _ = await session(ct); }));
    }
    private Task<TenantManagementSnapshotV1?> ManagementRead(Harness h, Guid id)
        => ManagementCall(h, (service, session) => service.ReadManagementAsync(id, session));
    private Task<TenantManagementReceiptV1?> ManagementOriginal(Harness h, Guid id, string operation)
        => ManagementCall(h, (service, session) => service.ReadManagementOperationAsync(id, operation, session));
    private Task<TenantManagementReceiptV1> ManagementWrite(Harness h, Guid id, TenantManagementCommandV1 command, params IInterceptor[] interceptors)
        => ManagementCall(h, (service, session) => service.ExecuteManagementAsync(id, command, session), null, interceptors);
    private static TenantManagementCommandV1 ManagementUpdate(TenantManagementSnapshotV1 row, string operation = "配置/原命令")
        => new(operation, row.Revision, "update", new("修改名称 ", row.ExpireDate, "保留备注 ", row.TimeZoneId));
    private static string ManagementPath(Guid id, string suffix = "") => $"api/platform/tenant/{id}/management-v1" + suffix;

    [CurrentPermissionPostgresFact]
    public async Task Tenant_management_v1_configuration_lifecycle_concurrency_and_original_results()
    {
        await using var h = await TenantManagementSeed(); var id = await ManagementTarget(h); var row = (await ManagementRead(h, id))!;
        Assert.Equal(1, row.Revision); Assert.Equal("原租户 ", row.TenantName);
        var command = ManagementUpdate(row, new string('界', 3000) + "\0/原命令");
        var first = await ManagementWrite(h, id, command);
        Assert.Equal(2, first.Snapshot.Revision); Assert.Equal(command.Configuration!.Name, first.Snapshot.TenantName);
        Assert.Equal(command.Configuration.Remark, first.Snapshot.Remark); Assert.Equal(row.ExpireDate, first.Snapshot.ExpireDate);
        Assert.Equal(first, await ManagementWrite(h, id, command));
        Assert.Equal("TENANT_OPERATION_CONFLICT", (await Assert.ThrowsAsync<TenantManagementExceptionV1>(() => ManagementWrite(h, id, command with { ExpectedRevision = 2 }))).Message);
        var results = await Task.WhenAll(new[] { "left", "right" }.Select(async operation =>
        {
            try { await ManagementWrite(h, id, new(operation, 2, "suspend", null)); return 200; }
            catch (TenantManagementExceptionV1 e) { return e.Status; }
        }));
        Assert.Equal(new[] { 200, 412 }, results.Order());
        var disabled = (await ManagementRead(h, id))!; Assert.False(disabled.Enable); Assert.Equal(3, disabled.Revision);
        var enabled = await ManagementWrite(h, id, new("enable", 3, "reactivate", null));
        Assert.True(enabled.Snapshot.Enable); Assert.Equal(4, enabled.Snapshot.Revision);
        Assert.Equal(first, await ManagementOriginal(h, id, command.OperationRef)); Assert.Equal(first, await ManagementWrite(h, id, command));
        var audit = await h.Db.Sys_SecurityLogs.IgnoreQueryFilters().Where(x => x.RequestTenantCode == row.TenantCode).ToArrayAsync();
        Assert.Equal(3, audit.Length); Assert.All(audit, x => Assert.Equal(h.User, x.UserId));
        Assert.Equal(3, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_tenant_management_v1.commands WHERE tenant_id={id}").SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Tenant_management_v1_native_ABA_legacy_writers_and_delete_recreate_advance_revision()
    {
        await using var h = await TenantManagementSeed(); var id = await ManagementTarget(h); var initial = (await ManagementRead(h, id))!;
        await h.Db.Sys_Tenants.Where(x => x.Id == id).ExecuteUpdateAsync(x => x.SetProperty(t => t.TenantName, "临时"));
        await h.Db.Sys_Tenants.Where(x => x.Id == id).ExecuteUpdateAsync(x => x.SetProperty(t => t.TenantName, initial.TenantName));
        Assert.Equal(3, (await ManagementRead(h, id))!.Revision);
        Assert.Equal(412, (await Assert.ThrowsAsync<TenantManagementExceptionV1>(() => ManagementWrite(h, id, ManagementUpdate(initial)))).Status);
        await ManagementCall(h, async (service, _) => { await service.SuspendAsync(id); return true; });
        Assert.Equal(4, (await ManagementRead(h, id))!.Revision);
        // Authentication-policy-only writes belong to their own revision and must remain available.
        await h.Db.Sys_Tenants.Where(x => x.Id == id).ExecuteUpdateAsync(x => x.SetProperty(t => t.TwoFactorMode, 1));
        Assert.Equal(4, (await ManagementRead(h, id))!.Revision);
        await h.Db.Sys_Tenants.Where(x => x.Id == id).ExecuteDeleteAsync(); Assert.Null(await ManagementRead(h, id));
        h.Db.Sys_Tenants.Add(new() { Id = id, TenantCode = initial.TenantCode, TenantName = initial.TenantName });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        Assert.Equal(6, (await ManagementRead(h, id))!.Revision);
        Assert.Equal(412, (await Assert.ThrowsAsync<TenantManagementExceptionV1>(() => ManagementWrite(h, id, ManagementUpdate(initial) with { ExpectedRevision = 4 }))).Status);
        await Assert.ThrowsAsync<PostgresException>(() => h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_tenant_management_v1.versions SET revision=1 WHERE tenant_id={id}"));
    }

    [CurrentPermissionPostgresFact]
    public async Task Tenant_management_v1_actor_binding_current_authority_and_invalid_inputs_fail_without_effect()
    {
        await using var a = await TenantManagementSeed(); await using var b = await TenantManagementSeed(); var id = await ManagementTarget(a);
        var row = (await ManagementRead(a, id))!; var command = ManagementUpdate(row); var first = await ManagementWrite(a, id, command);
        Assert.Equal(403, (await Assert.ThrowsAsync<TenantManagementExceptionV1>(() => ManagementOriginal(b, id, command.OperationRef))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<TenantManagementExceptionV1>(() => ManagementWrite(b, id, command))).Status);
        foreach (var invalid in new[] {
            command with { OperationRef="invalid-kind",Kind="create" },
            command with { OperationRef="missing",Configuration=null },
            command with { OperationRef="name",ExpectedRevision=2,Configuration=command.Configuration! with {Name=""} },
            command with { OperationRef="utc",ExpectedRevision=2,Configuration=command.Configuration! with {ExpireDate=DateTime.UtcNow} },
            command with { OperationRef="precision",ExpectedRevision=2,Configuration=command.Configuration! with {ExpireDate=new DateTime(2030,1,1).AddTicks(1)} },
            command with { OperationRef="nul",ExpectedRevision=2,Configuration=command.Configuration! with {Remark="\0"} } })
            Assert.Equal(400, (await Assert.ThrowsAsync<TenantManagementExceptionV1>(() => ManagementWrite(a, id, invalid))).Status);
        await a.Db.Sys_Users.Where(x => x.Id == a.User).ExecuteUpdateAsync(x => x.SetProperty(u => u.IsPlatformAdmin, false));
        Assert.Equal("E-SEC-031", (await Assert.ThrowsAnyAsync<InvalidOperationException>(() => ManagementOriginal(a, id, command.OperationRef))).Message);
        Assert.Equal(first.Snapshot, await ManagementRead(b, id));
    }

    [CurrentPermissionPostgresFact]
    public async Task Tenant_management_v1_audit_and_final_failure_rollback_then_unknown_commit_recovers()
    {
        await using var h = await TenantManagementSeed(); var id = await ManagementTarget(h); var row = (await ManagementRead(h, id))!;
        var command = ManagementUpdate(row); var calls = 0;
        await Assert.ThrowsAsync<IOException>(() => ManagementCall(h, (service, session) => service.ExecuteManagementAsync(id, command, session),
            _ => ++calls == 2 ? throw new IOException("final session validation failed") : Task.FromResult(h.User)));
        Assert.Equal(row, await ManagementRead(h, id)); Assert.Null(await ManagementOriginal(h, id, command.OperationRef));
        var constraint = "management_" + id.ToString("N");
        await h.Db.Database.ExecuteSqlRawAsync($"ALTER TABLE public.\"Sys_SecurityLogs\" ADD CONSTRAINT {constraint} CHECK (\"RequestTenantCode\" <> '{row.TenantCode}')");
        await Assert.ThrowsAsync<DbUpdateException>(() => ManagementWrite(h, id, command));
        Assert.Equal(row, await ManagementRead(h, id)); Assert.Null(await ManagementOriginal(h, id, command.OperationRef));
        await h.Db.Database.ExecuteSqlRawAsync($"ALTER TABLE public.\"Sys_SecurityLogs\" DROP CONSTRAINT {constraint}");
        Assert.Equal("TENANT_OUTCOME_UNKNOWN", (await Assert.ThrowsAsync<TenantManagementExceptionV1>(() => ManagementWrite(h, id, command, new NumberLostCommit { Armed = true }))).Message);
        var original = await ManagementOriginal(h, id, command.OperationRef); Assert.NotNull(original);
        Assert.Equal(original, await ManagementWrite(h, id, command)); Assert.Equal(2, (await ManagementRead(h, id))!.Revision);
        Assert.Single(await h.Db.Sys_SecurityLogs.IgnoreQueryFilters().Where(x => x.RequestTenantCode == row.TenantCode).ToArrayAsync());
        await Assert.ThrowsAsync<PostgresException>(() => h.Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM cp6_tenant_management_v1.commands WHERE tenant_id={id}"));
    }

    [CurrentPermissionPostgresFact]
    public async Task Tenant_management_v1_http_real_platform_session_CSRF_lifecycle_and_recovery()
    {
        await using var h = await TenantManagementSeed(); await using var ordinary = await Seed(); await using var other = await TenantManagementSeed();
        var id = await ManagementTarget(h); await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h);
        Task<HttpResponseMessage> Send(HttpMethod method, string suffix="", object? body=null, bool csrf=true)
            => host.Send(OperationsRequest(cookies, method, ManagementPath(id,suffix),body,csrf));
        using (var anonymous = await host.Send(new(HttpMethod.Get,ManagementPath(id)))) Assert.Equal(HttpStatusCode.Unauthorized,anonymous.StatusCode);
        TenantManagementSnapshotV1 row;
        using (var response = await Send(HttpMethod.Get)) row=await NumberHttpData<TenantManagementSnapshotV1>(response);
        var command = ManagementUpdate(row);
        using (var denied = await Send(HttpMethod.Post,"/commands",command,false)) Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        using (var invalid = await Send(HttpMethod.Post,"/commands",new {command.OperationRef,command.ExpectedRevision,command.Kind,command.Configuration,ActorId=h.User})) Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode);
        TenantManagementReceiptV1 first;
        using (var response = await Send(HttpMethod.Post,"/commands",command)) first=await NumberHttpData<TenantManagementReceiptV1>(response);
        using (var response = await Send(HttpMethod.Post,"/commands",command)) Assert.Equal(first,await NumberHttpData<TenantManagementReceiptV1>(response));
        using (var conflict = await Send(HttpMethod.Post,"/commands",command with {OperationRef="new"})) Assert.Equal(HttpStatusCode.PreconditionFailed,conflict.StatusCode);
        using (var conflict = await Send(HttpMethod.Post,"/commands",command with {ExpectedRevision=2})) Assert.Equal(HttpStatusCode.Conflict,conflict.StatusCode);
        using (var response = await Send(HttpMethod.Post,"/commands",new TenantManagementCommandV1("suspend",2,"suspend",null))) Assert.False((await NumberHttpData<TenantManagementReceiptV1>(response)).Snapshot.Enable);
        using (var response = await Send(HttpMethod.Post,"/commands",new TenantManagementCommandV1("reactivate",3,"reactivate",null))) Assert.True((await NumberHttpData<TenantManagementReceiptV1>(response)).Snapshot.Enable);
        using (var response = await Send(HttpMethod.Post,"/commands/read",new {command.OperationRef})) Assert.Equal(first,await NumberHttpData<TenantManagementReceiptV1>(response));
        var ordinaryCookies=await Login(host,ordinary);
        using (var response=await host.Send(OperationsRequest(ordinaryCookies,HttpMethod.Get,ManagementPath(id)))) Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
        var otherCookies=await Login(host,other);
        using (var response=await host.Send(OperationsRequest(otherCookies,HttpMethod.Post,ManagementPath(id,"/commands/read"),new {command.OperationRef}))) Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
        using (var logout=await host.Send(OperationsRequest(cookies,HttpMethod.Post,"api/auth/logout",new {}))) Assert.Equal(HttpStatusCode.OK,logout.StatusCode);
        using (var response=await Send(HttpMethod.Get)) Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode);
    }

    [TenantManagementPostgresTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task Tenant_management_v1_http_rechecks_platform_and_credentials_after_wait(bool credentials)
    {
        await using var h=await TenantManagementSeed(); await using var spare=await TenantManagementSeed(); var id=await ManagementTarget(h);
        var row=(await ManagementRead(h,id))!;
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!); var cookies=await Login(host,h);
        await using var tx=await h.Db.Database.BeginTransactionAsync(); await AuthenticationCompletionGateV1.AcquireAsync(h.Db);
        var attempt=host.Send(OperationsRequest(cookies,HttpMethod.Post,ManagementPath(id,"/commands"),ManagementUpdate(row)));
        try
        {
            await using var observer=server.Context(new TenantContext {CurrentTenantId=h.Tenant}); using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(12));
            while(!await observer.Database.SqlQueryRaw<bool>("""
                SELECT EXISTS(SELECT FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock'
                AND query LIKE '%cp6_authentication_v1.acquire()%') AS "Value"
                """).SingleAsync(timeout.Token))
            {
                if(attempt.IsCompleted) Assert.Fail("Request completed before the Owner lock: "+await OperationsFailure(await attempt));
                await Task.Delay(20,timeout.Token);
            }
            if(credentials) await h.Db.Sys_Users.Where(x=>x.Id==h.User).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.AuthenticationEpoch,Guid.NewGuid()));
            else await h.Db.Sys_Users.Where(x=>x.Id==h.User).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.IsPlatformAdmin,false));
        }
        finally {await tx.CommitAsync();}
        using var response=await attempt;
        // Existing session authority invalidates an original platform-admin claim on revocation.
        Assert.True(response.StatusCode==HttpStatusCode.Unauthorized,await OperationsFailure(response));
        Assert.Contains("REAUTH_REQUIRED",await response.Content.ReadAsStringAsync());
        Assert.Equal(row,await ManagementRead(spare,id));
    }

    [CurrentPermissionPostgresFact]
    public async Task Tenant_management_v1_http_own_tenant_change_commits_before_invalidating_original_session()
    {
        await using var h=await TenantManagementSeed();await using var recovery=await TenantManagementSeed();
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies=await Login(host,h);var row=(await ManagementRead(h,h.Tenant))!;
        var command=ManagementUpdate(row) with {Configuration=new(row.TenantName,new DateTime(2030,1,1),row.Remark,row.TimeZoneId)};
        using(var changed=await host.Send(OperationsRequest(cookies,HttpMethod.Post,ManagementPath(h.Tenant,"/commands"),command)))
            Assert.Equal(2,(await NumberHttpData<TenantManagementReceiptV1>(changed)).Snapshot.Revision);
        using(var old=await host.Send(OperationsRequest(cookies,HttpMethod.Get,ManagementPath(h.Tenant)))) Assert.Equal(HttpStatusCode.Unauthorized,old.StatusCode);
        cookies=await Login(host,h);
        using(var original=await host.Send(OperationsRequest(cookies,HttpMethod.Post,ManagementPath(h.Tenant,"/commands/read"),new {command.OperationRef})))
            Assert.Equal(command.OperationRef,(await NumberHttpData<TenantManagementReceiptV1>(original)).OperationRef);
        using(var suspend=await host.Send(OperationsRequest(cookies,HttpMethod.Post,ManagementPath(h.Tenant,"/commands"),new TenantManagementCommandV1("self-suspend",2,"suspend",null))))
            Assert.False((await NumberHttpData<TenantManagementReceiptV1>(suspend)).Snapshot.Enable);
        using(var old=await host.Send(OperationsRequest(cookies,HttpMethod.Get,ManagementPath(h.Tenant)))) Assert.Equal(HttpStatusCode.Unauthorized,old.StatusCode);
        await ManagementWrite(recovery,h.Tenant,new("recover-tenant",3,"reactivate",null));
        cookies=await Login(host,h);
        using(var original=await host.Send(OperationsRequest(cookies,HttpMethod.Post,ManagementPath(h.Tenant,"/commands/read"),new {OperationRef="self-suspend"})))
            Assert.False((await NumberHttpData<TenantManagementReceiptV1>(original)).Snapshot.Enable);
        Assert.True((await ManagementRead(h,h.Tenant))!.Enable);
    }

    [CurrentPermissionPostgresFact]
    public async Task Tenant_management_v1_upgrade_preserves_tenant_rows_and_initializes_versions_once()
    {
        await using var h=await TenantManagementSeed();
        var settings=new NpgsqlConnectionStringBuilder(h.Db.Database.GetConnectionString()){Database="postgres"}; var name="tenant_upgrade_"+Guid.NewGuid().ToString("N");
        await using(var root=new NpgsqlConnection(settings.ConnectionString))
        {await root.OpenAsync();await using var create=new NpgsqlCommand("CREATE DATABASE "+name,root);await create.ExecuteNonQueryAsync();}
        settings.Database=name;var database=new DatabaseOptions(DatabaseProvider.PostgreSql);var policy=DatabaseMigrationProfile.For(database,DatabaseContextKind.Core);
        var options=DatabaseContextOptions.Configure(new DbContextOptionsBuilder<CP6Context>(),database,settings.ConnectionString,policy.MigrationsAssembly,policy.HistoryTable,policy.HistorySchema);
        await using var db=new CP6Context(options.Options,new TenantContext());await db.GetService<IMigrator>().MigrateAsync("20261102090000_CodeGenMetadataAdoptionV1");
        db.Sys_Tenants.Add(new(){TenantCode="existing",TenantName="Existing ",Remark="保留 ",ExpireDate=new DateTime(2030,1,2,3,4,5).AddTicks(1234560)});
        await db.SaveChangesAsync();db.ChangeTracker.Clear();
        Task<string[]> Rows()=>db.Database.SqlQueryRaw<string>("SELECT row_to_json(t)::text AS \"Value\" FROM public.\"Sys_Tenants\" t ORDER BY \"Id\"").ToArrayAsync();
        var before=await Rows();
        await using(var writer=new CP6Context(options.Options,new TenantContext()))
        await using(var transaction=await writer.Database.BeginTransactionAsync())
        {
            writer.Sys_Tenants.Add(new(){TenantCode="concurrent",TenantName="Created before migration lock"});await writer.SaveChangesAsync();
            var migration=db.Database.MigrateAsync();
            try
            {
                await using var observer=new NpgsqlConnection(settings.ConnectionString);await observer.OpenAsync();
                using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await using var waiting=new NpgsqlCommand("SELECT EXISTS(SELECT FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock' AND query LIKE '%LOCK TABLE public.%Sys_Tenants%')",observer);
                while(!(bool)(await waiting.ExecuteScalarAsync(timeout.Token))!)
                {Assert.False(migration.IsCompleted,"Migration must wait for the existing native writer.");await Task.Delay(20,timeout.Token);}
            }
            finally {await transaction.CommitAsync();}
            await migration;
        }
        var after=await Rows();Assert.Equal(before.Length+1,after.Length);Assert.All(before,row=>Assert.Contains(row,after));
        Assert.Equal(after.Length,await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM cp6_tenant_management_v1.versions").SingleAsync());
        await db.Database.MigrateAsync();Assert.Equal(after,await Rows());
        Assert.All(await db.Database.SqlQueryRaw<long>("SELECT revision AS \"Value\" FROM cp6_tenant_management_v1.versions").ToArrayAsync(),x=>Assert.Equal(1,x));
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Tenant_management_v1_actor_tenant_expiring_during_write_rolls_back()
    {
        await using var h=await TenantManagementSeed();var id=await ManagementTarget(h);var row=(await ManagementRead(h,id))!;
        var deadline=DateTime.Now.AddSeconds(2);
        await h.Db.Sys_Tenants.Where(x=>x.Id==h.Tenant).ExecuteUpdateAsync(x=>x.SetProperty(t=>t.ExpireDate,deadline));
        var calls=0;
        var error=await Assert.ThrowsAsync<TenantManagementExceptionV1>(()=>ManagementCall(h,(service,session)=>service.ExecuteManagementAsync(id,ManagementUpdate(row),session),async ct=>
        {
            if(++calls==2) await Task.Delay(TimeSpan.FromSeconds(2.1),ct);
            return h.User;
        }));
        Assert.Equal(401,error.Status);
        await using var observer=server.Context(new TenantContext{CurrentTenantId=h.Tenant});
        Assert.Equal(row.TenantName,await observer.Sys_Tenants.Where(x=>x.Id==id).Select(x=>x.TenantName).SingleAsync());
        Assert.Equal(0,await observer.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_tenant_management_v1.commands WHERE tenant_id={id}").SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Tenant_management_v1_http_logout_during_audit_wait_rolls_back_before_commit()
    {
        await using var h=await TenantManagementSeed();var id=await ManagementTarget(h);var row=(await ManagementRead(h,id))!;
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var cookies=await Login(host,h);
        await using var blocker=server.Context(new TenantContext{CurrentTenantId=h.Tenant});
        await using var tx=await blocker.Database.BeginTransactionAsync();
        await blocker.Database.ExecuteSqlRawAsync("LOCK TABLE public.\"Sys_SecurityLogs\" IN SHARE MODE");
        var operation=host.Send(OperationsRequest(cookies,HttpMethod.Post,ManagementPath(id,"/commands"),ManagementUpdate(row)));
        Task<HttpResponseMessage>? logout=null;
        try
        {
            await using var observer=server.Context(new TenantContext{CurrentTenantId=h.Tenant});using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));
            async Task Wait(string part,Task<HttpResponseMessage> request)
            {
                while(!await observer.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock' AND query LIKE {part}) AS \"Value\"").SingleAsync(timeout.Token))
                {if(request.IsCompleted) Assert.Fail(await OperationsFailure(await request));await Task.Delay(20,timeout.Token);}
            }
            await Wait("%INSERT INTO %Sys_SecurityLogs%",operation);
            logout=host.Send(OperationsRequest(cookies,HttpMethod.Post,"api/auth/logout",new {}));
            // Logout blacklists first, then waits for the DB authentication gate held by the write.
            await Wait("%cp6_authentication_v1.acquire()%",logout);
        }
        finally {await tx.RollbackAsync();}
        using var result=await operation;Assert.True(result.StatusCode==HttpStatusCode.Unauthorized,await OperationsFailure(result));
        using var loggedOut=await logout!;Assert.True(loggedOut.StatusCode==HttpStatusCode.OK,await OperationsFailure(loggedOut));
        Assert.Equal(row,await ManagementRead(h,id));Assert.Null(await ManagementOriginal(h,id,"配置/原命令"));
    }
}
