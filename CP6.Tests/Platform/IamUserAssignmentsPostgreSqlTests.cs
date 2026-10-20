using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;
using Moq;
using CP6.Core.EFDbContext;
using CP6.Core.Services.CrmIdentity;
using CP6.Platform.Messaging;
using CP6.Platform.EntityFramework;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Caching.Memory;
using CP6.Core.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CP6.Tests.Platform;

[Collection("TenantManagementPostgres")]
public sealed partial class IamUserAssignmentsPostgreSqlTests(TenantManagementPostgresFixture server)
{
    [CurrentPermissionPostgresFact]
    public async Task Legacy_postgres_assignment_without_versions_cannot_grant_an_unavailable_role()
    {
        var tenant = Guid.NewGuid();
        await using var db = server.Context(new TenantContext { CurrentTenantId = tenant });
        var user = new Sys_User { UserName = "assignment-target", Password = "fixture", Enable = true };
        db.Sys_Users.Add(user); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var service = new UserRoleService(db, Mock.Of<ICurrentPermissionContext>());
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => service.SaveAsync(user.Id, [9127], 9127, "fixture"));
        Assert.False(await db.Sys_UserRoles.AnyAsync(x => x.UserId == user.Id));
        Assert.Null(await db.Sys_Users.Where(x => x.Id == user.Id).Select(x => x.RoleId).SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task User_and_primary_additional_roles_commit_with_identity_outbox_audit_and_original_receipt()
    {
        await using var h=await Seed();var command=await NewCommand(h);
        var result=await h.Service.ChangeAsync("user-create",command,h.Actor,default);
        var user=await h.Db.Sys_Users.AsNoTracking().SingleAsync(x=>x.Id==result.EntityId);
        Assert.Equal(71,user.RoleId);Assert.Equal(new[]{71,72},await h.Db.Sys_UserRoles.Where(x=>x.UserId==user.Id).OrderBy(x=>x.RoleId).Select(x=>x.RoleId).ToArrayAsync());
        Assert.False(user.IsPlatformAdmin);Assert.True(user.MustChangePassword);Assert.True(new BCryptPasswordHasher(4).Verify(command.Password!,user.Password));
        var aggregate="user:"+user.Id.ToString("D");
        var snapshot=await h.Db.CrmIdentitySnapshots.SingleAsync(x=>x.AggregateId==aggregate);Assert.Equal(1,snapshot.Version);
        Assert.True(await h.Db.Set<Cp6OutboxMessage>().AnyAsync(x=>x.AggregateId==aggregate && x.AggregateVersion==1));
        Assert.Equal(result,await h.Service.ChangeAsync("user-create",command,h.Actor,default));
        Assert.Equal(result,await h.Authority.Resolve(command.OperationId,"user-create",false,h.Actor,default));
        Assert.Equal(1,await h.Db.Sys_SecurityLogs.CountAsync(x=>x.EventType==(int)SecurityEventType.IamAdministrationChanged));
        Assert.DoesNotContain(command.Password!,System.Text.Json.JsonSerializer.Serialize(await h.Service.ReadAsync(user.Id,h.Actor,default)));
    }

    [TenantManagementPostgresTheory]
    [InlineData("missing-role")][InlineData("foreign-role")][InlineData("disabled-role")][InlineData("primary")]
    [InlineData("foreign-dept")][InlineData("foreign-manager")][InlineData("disabled-ancestor")]
    public async Task Invalid_references_never_create_partial_user_or_memberships(string failure)
    {
        await using var h=await Seed();
        await using(var foreign=server.Context(new TenantContext{CurrentTenantId=Guid.NewGuid()}))
        {
            foreign.Sys_Roles.Add(new Sys_Role{RoleId=99,RoleName="foreign"});await foreign.SaveChangesAsync();
        }
        if(failure=="disabled-ancestor")await h.Db.Sys_Depts.Where(x=>x.Id==h.Parent).ExecuteUpdateAsync(x=>x.SetProperty(d=>d.Enable,false));
        var command=await NewCommand(h);
        command=failure switch
        {
            "missing-role"=>command with {RoleIds=[9127],RoleId=9127},
            "foreign-role"=>command with {RoleIds=[99],RoleId=99},
            "disabled-role"=>command with {RoleIds=[73],RoleId=73},
            "primary"=>command with {RoleIds=[72],RoleId=71},
            "foreign-dept"=>command with {DeptId=Guid.NewGuid()},
            "foreign-manager"=>command with {ManagerId=Guid.NewGuid()},
            _=>command with {DeptId=h.Child}
        };
        var error=await Assert.ThrowsAsync<IamAdministrationExceptionV1>(()=>h.Service.ChangeAsync("user-create",command,h.Actor,default));
        Assert.Equal(409,error.Status);Assert.Equal(1,await h.Db.Sys_Users.CountAsync());Assert.Empty(await h.Db.Sys_UserRoles.ToListAsync());
        Assert.Equal("UNKNOWN",(await h.Authority.Resolve(command.OperationId,"user-create",false,h.Actor,default)).State);
    }

    [CurrentPermissionPostgresFact]
    public async Task Current_reference_ABA_rejects_stale_intent_and_original_closed_operation_cannot_arrive_late()
    {
        await using var h=await Seed();var command=await NewCommand(h);
        await h.Db.Sys_Roles.Where(x=>x.RoleId==71).ExecuteUpdateAsync(x=>x.SetProperty(r=>r.Enable,false));
        await h.Db.Sys_Roles.Where(x=>x.RoleId==71).ExecuteUpdateAsync(x=>x.SetProperty(r=>r.Enable,true));
        Assert.Equal(412,(await Assert.ThrowsAsync<IamAdministrationExceptionV1>(()=>h.Service.ChangeAsync("user-create",command,h.Actor,default))).Status);
        command=await NewCommand(h);await h.Authority.Resolve(command.OperationId,"user-create",true,h.Actor,default);
        Assert.Equal("IAM_OPERATION_CLOSED",(await Assert.ThrowsAsync<IamAdministrationExceptionV1>(()=>h.Service.ChangeAsync("user-create",command,h.Actor,default))).Message);
        Assert.Equal(1,await h.Db.Sys_Users.CountAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Profile_edit_preserves_password_and_disabled_historical_role_but_cannot_newly_grant_it()
    {
        await using var h=await Seed();var created=await h.Service.ChangeAsync("user-create",await NewCommand(h),h.Actor,default);
        await h.Db.Sys_Roles.Where(x=>x.RoleId==71).ExecuteUpdateAsync(x=>x.SetProperty(r=>r.Enable,false));
        var editor=await h.Service.ReadAsync(created.EntityId,h.Actor,default);var user=editor.User!;
        var password=await h.Db.Sys_Users.Where(x=>x.Id==user.Id).Select(x=>x.Password).SingleAsync();
        var command=new IamUserCommandV1{Id=user.Id,OperationId=Guid.NewGuid(),ExpectedVersion=user.Version,ExpectedReferencesVersion=editor.ReferencesVersion,
            Reason="profile only",UserName=user.UserName,NickName="new display",RoleId=user.RoleId,RoleIds=user.RoleIds,Enable=true};
        await h.Service.ChangeAsync("user-update",command,h.Actor,default);
        Assert.Equal(password,await h.Db.Sys_Users.Where(x=>x.Id==user.Id).Select(x=>x.Password).SingleAsync());
        Assert.Equal(71,await h.Db.Sys_Users.Where(x=>x.Id==user.Id).Select(x=>x.RoleId).SingleAsync());
        Assert.Equal(412,(await Assert.ThrowsAsync<IamAdministrationExceptionV1>(()=>h.Service.ChangeAsync("user-update",command with {OperationId=Guid.NewGuid()},h.Actor,default))).Status);
    }

    [CurrentPermissionPostgresFact]
    public async Task Receipt_failure_rolls_back_user_memberships_identity_outbox_and_audit()
    {
        var fault=new ReceiptFault();await using var h=await Seed(fault);var command=await NewCommand(h);fault.Armed=true;
        await Assert.ThrowsAsync<IOException>(()=>h.Service.ChangeAsync("user-create",command,h.Actor,default));Assert.True(fault.Fired);fault.Armed=false;
        Assert.Equal(1,await h.Db.Sys_Users.CountAsync());Assert.Empty(await h.Db.Sys_UserRoles.ToListAsync());
        Assert.False(await h.Db.Sys_SecurityLogs.AnyAsync(x=>x.EventType==(int)SecurityEventType.IamAdministrationChanged));
        Assert.Equal(1,await h.Db.CrmIdentitySnapshots.CountAsync(x=>x.TenantId==h.Tenant && x.AggregateId.StartsWith("user:")));
        Assert.Equal("UNKNOWN",(await h.Authority.Resolve(command.OperationId,"user-create",false,h.Actor,default)).State);
    }

    [CurrentPermissionPostgresFact]
    public async Task Lost_commit_acknowledgement_is_resolved_by_original_receipt_without_second_effect()
    {
        var fault=new CommitFault();await using var h=await Seed(fault);var command=await NewCommand(h);fault.Armed=true;
        Assert.Equal("IAM_OPERATION_UNKNOWN",(await Assert.ThrowsAsync<IamAdministrationExceptionV1>(()=>h.Service.ChangeAsync("user-create",command,h.Actor,default))).Message);
        var status=await h.Authority.Resolve(command.OperationId,"user-create",false,h.Actor,default);Assert.Equal("COMMITTED",status.State);
        Assert.Equal(status,await h.Service.ChangeAsync("user-create",command,h.Actor,default));Assert.Equal(2,await h.Db.Sys_Users.CountAsync());
        Assert.Equal("IAM_OPERATION_INPUT_CONFLICT",(await Assert.ThrowsAsync<IamAdministrationExceptionV1>(()=>h.Service.ChangeAsync("user-create",command with {Password="other"},h.Actor,default))).Message);
    }

    [CurrentPermissionPostgresFact]
    public async Task Native_department_writer_waits_on_same_root_and_changes_reference_version()
    {
        await using var h=await Seed();var before=await h.Authority.ReferencesVersion(default);
        await using var writer=server.Context(new TenantContext{CurrentTenantId=h.Tenant});await writer.Database.OpenConnectionAsync();
        var pid=await writer.Database.SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"").SingleAsync();
        await using var tx=await h.Db.Database.BeginTransactionAsync();await AuthenticationCompletionGateV1.AcquireAsync(h.Db);
        var change=writer.Sys_Depts.Where(x=>x.Id==h.Child).ExecuteUpdateAsync(x=>x.SetProperty(d=>d.Enable,false));
        try
        {
            await using var observer=server.Context(new TenantContext{CurrentTenantId=h.Tenant});using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while(!await observer.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT FROM pg_stat_activity WHERE pid={pid} AND wait_event_type='Lock') AS \"Value\"").SingleAsync(timeout.Token))await Task.Delay(20,timeout.Token);
            Assert.False(change.IsCompleted);
        }
        finally{await tx.CommitAsync();}
        await change;Assert.NotEqual(before,await h.Authority.ReferencesVersion(default));
    }

    private async Task<Harness> Seed(params IInterceptor[] interceptors)
    {
        var tenant=Guid.NewGuid();const string issuer="https://iam-writer.example.test";
        var options=new CrmIdentityOptions{Enabled=true,Issuer=issuer,ProjectionReaderClientIds=["fixture-reader"]};options.Tenants.Add(tenant,"us");
        var runtime=new CrmIdentityRuntime(options,new IdentityEventValidator(Cp6ContractBundle.Load(Path.Combine(AppContext.BaseDirectory,"contracts/events/platform")),issuer));
        var db=server.Context(new TenantContext{CurrentTenantId=tenant},runtime,interceptors);
        var actor=new Sys_User{UserName="iam-admin",Enable=true,Password=new BCryptPasswordHasher(4).Hash("Iam1!Password")};
        var parent=Guid.NewGuid();var child=Guid.NewGuid();
        db.Sys_Tenants.Add(new Sys_Tenant{Id=tenant,TenantCode="iam_"+tenant.ToString("N"),TenantName="IAM"});db.Sys_Users.Add(actor);
        db.Sys_Roles.AddRange(new Sys_Role{RoleId=71,RoleName="one"},new Sys_Role{RoleId=72,RoleName="two"},new Sys_Role{RoleId=73,RoleName="disabled",Enable=false});
        db.Sys_Depts.AddRange(new Sys_Dept{Id=parent,DeptCode="P",DeptName="parent",Path=$"/{parent}/"},new Sys_Dept{Id=child,ParentId=parent,DeptCode="C",DeptName="child",Path=$"/{parent}/{child}/"});
        await db.SaveChangesAsync();db.ChangeTracker.Clear();
        var permission=new Mock<ICurrentPermissionContext>();permission.Setup(x=>x.GetAsync()).ReturnsAsync(new UserPermissionContext{UserId=actor.Id,ActionKeys=["user:query","user:add","user:edit"]});
        var authority=new IamAdministrationV1(db,permission.Object,new EphemeralDataProtectionProvider());
        var refresh=new RefreshTokenService(db,Options.Create(new SecurityOptions()),new TenantContext{CurrentTenantId=tenant});
        return new(db,authority,new IamUserAdministrationV1(db,authority,new BCryptPasswordHasher(4),refresh),tenant,actor.Id,parent,child);
    }

    [CurrentPermissionPostgresFact]
    public async Task Forward_migration_backfills_populated_department_revisions_without_rewriting_original_tree()
    {
        var tenant=new TenantContext{CurrentTenantId=Guid.NewGuid()};await using var template=server.Context(tenant);
        var databaseName="iam_legacy_"+Guid.NewGuid().ToString("N");
        await template.Database.ExecuteSqlRawAsync("CREATE DATABASE "+databaseName);
        var connection=new NpgsqlConnectionStringBuilder(template.Database.GetConnectionString()){Database=databaseName};
        var database=new DatabaseOptions(DatabaseProvider.PostgreSql);var profile=DatabaseMigrationProfile.For(database,DatabaseContextKind.Core);
        await using var db=new CP6Context(DatabaseContextOptions.Configure(new DbContextOptionsBuilder<CP6Context>(),database,connection.ConnectionString,
            profile.MigrationsAssembly,profile.HistoryTable,profile.HistorySchema).Options,tenant);
        var migrator=db.GetService<IMigrator>();await migrator.MigrateAsync("20261019155500_SsoIdentityMappingV1");
        var root=Guid.NewGuid();var child=Guid.NewGuid();
        db.Sys_Depts.AddRange(new Sys_Dept{Id=root,DeptCode="old-root",DeptName="old root",Path=$"/{root}/"},
            new Sys_Dept{Id=child,ParentId=root,DeptCode="old-child",DeptName="old child",Path=$"/{root}/{child}/"});
        await db.SaveChangesAsync();db.ChangeTracker.Clear();await migrator.MigrateAsync();
        var before=await db.Database.SqlQueryRaw<Guid>("SELECT revision AS \"Value\" FROM cp6_authentication_v1.identity_admission_revisions WHERE object_key='tree'").SingleAsync();
        Assert.Equal(2,await db.Sys_Depts.CountAsync());Assert.Equal($"/{root}/{child}/",await db.Sys_Depts.Where(x=>x.Id==child).Select(x=>x.Path).SingleAsync());
        Assert.Equal(3,await db.Database.SqlQueryRaw<int>("SELECT count(*)::integer AS \"Value\" FROM cp6_authentication_v1.identity_admission_revisions").SingleAsync());
        await db.Sys_Depts.Where(x=>x.Id==child).ExecuteUpdateAsync(x=>x.SetProperty(d=>d.Enable,false));
        Assert.NotEqual(before,await db.Database.SqlQueryRaw<Guid>("SELECT revision AS \"Value\" FROM cp6_authentication_v1.identity_admission_revisions WHERE object_key='tree'").SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Policy_forward_migration_backfills_existing_catalog_without_rewriting_grants()
    {
        var tenant=new TenantContext{CurrentTenantId=Guid.NewGuid()};await using var template=server.Context(tenant);
        var databaseName="iam_catalog_legacy_"+Guid.NewGuid().ToString("N");
        await template.Database.ExecuteSqlRawAsync("CREATE DATABASE "+databaseName);
        var connection=new NpgsqlConnectionStringBuilder(template.Database.GetConnectionString()){Database=databaseName};
        var database=new DatabaseOptions(DatabaseProvider.PostgreSql);var profile=DatabaseMigrationProfile.For(database,DatabaseContextKind.Core);
        await using var db=new CP6Context(DatabaseContextOptions.Configure(new DbContextOptionsBuilder<CP6Context>(),database,connection.ConnectionString,
            profile.MigrationsAssembly,profile.HistoryTable,profile.HistorySchema).Options,tenant);
        var migrator=db.GetService<IMigrator>();await migrator.MigrateAsync("20261020042000_IamAdministrationV1");
        db.Sys_Tenants.Add(new(){Id=tenant.CurrentTenantId,TenantCode="legacy-catalog",TenantName="legacy"});
        db.Sys_Roles.Add(new(){RoleId=91,RoleName="retained"});
        db.Sys_Menus.Add(new(){MenuId=991,MenuKey="retained",MenuName="retained"});
        db.Sys_MenuActions.Add(new(){MenuId=991,ActionCode="read",ActionName="Read"});
        db.Sys_RoleMenus.Add(new(){RoleId=91,MenuId=991});
        db.Sys_RoleActions.Add(new(){RoleId=91,MenuId=991,ActionCode="read"});
        await db.SaveChangesAsync();db.ChangeTracker.Clear();
        var roleRevision=await db.Database.SqlQueryRaw<Guid>("SELECT revision AS \"Value\" FROM cp6_authentication_v1.identity_admission_revisions WHERE object_key='role:91'").SingleAsync();
        await migrator.MigrateAsync();
        var catalogRevision=await db.Database.SqlQueryRaw<Guid>("SELECT revision AS \"Value\" FROM cp6_authentication_v1.identity_admission_revisions WHERE object_key='permission-catalog'").SingleAsync();
        Assert.Equal(roleRevision,await db.Database.SqlQueryRaw<Guid>("SELECT revision AS \"Value\" FROM cp6_authentication_v1.identity_admission_revisions WHERE object_key='role:91'").SingleAsync());
        Assert.Equal("read",(await db.Sys_RoleActions.SingleAsync()).ActionCode);
        Assert.Equal(991,(await db.Sys_RoleMenus.SingleAsync()).MenuId);
        await db.Sys_MenuActions.ExecuteUpdateAsync(x=>x.SetProperty(a=>a.ActionName,"Revised"));
        Assert.NotEqual(catalogRevision,await db.Database.SqlQueryRaw<Guid>("SELECT revision AS \"Value\" FROM cp6_authentication_v1.identity_admission_revisions WHERE object_key='permission-catalog'").SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Explicit_password_reset_and_refresh_revocation_roll_back_together_or_commit_together()
    {
        var fault=new ReceiptFault();await using var h=await Seed(fault);
        var created=await h.Service.ChangeAsync("user-create",await NewCommand(h),h.Actor,default);
        var target=await h.Db.Sys_Users.SingleAsync(x=>x.Id==created.EntityId);
        target.Password=new BCryptPasswordHasher(4).Hash(AuthenticationFixtureSessionV1.Password);target.MustChangePassword=false;
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();target=await h.Db.Sys_Users.SingleAsync(x=>x.Id==created.EntityId);
        var refresh=new RefreshTokenService(h.Db,Options.Create(new SecurityOptions()),new TenantContext{CurrentTenantId=h.Tenant});
        await AuthenticationFixtureSessionV1.IssueAsync(h.Db,refresh,target);h.Db.ChangeTracker.Clear();
        var editor=await h.Service.ReadAsync(target.Id,h.Actor,default);var user=editor.User!;
        var command=new IamUserCommandV1{Id=user.Id,OperationId=Guid.NewGuid(),ExpectedVersion=user.Version,ExpectedReferencesVersion=editor.ReferencesVersion,
            Reason="explicit credential reset",UserName=user.UserName,RoleId=user.RoleId,RoleIds=user.RoleIds,Password="Replacement1!Password"};
        fault.Armed=true;await Assert.ThrowsAsync<IOException>(()=>h.Service.ChangeAsync("user-update",command,h.Actor,default));fault.Armed=false;
        var before=await h.Db.Sys_Users.AsNoTracking().SingleAsync(x=>x.Id==user.Id);
        Assert.True(new BCryptPasswordHasher(4).Verify(AuthenticationFixtureSessionV1.Password,before.Password));Assert.False(before.MustChangePassword);
        Assert.True(await h.Db.Sys_RefreshTokens.AnyAsync(x=>x.UserId==user.Id && x.RevokedAt==null));
        await h.Service.ChangeAsync("user-update",command,h.Actor,default);
        var after=await h.Db.Sys_Users.AsNoTracking().SingleAsync(x=>x.Id==user.Id);
        Assert.True(new BCryptPasswordHasher(4).Verify(command.Password!,after.Password));Assert.True(after.MustChangePassword);
        Assert.False(await h.Db.Sys_RefreshTokens.AnyAsync(x=>x.UserId==user.Id && x.RevokedAt==null));
    }
    private static async Task<IamUserCommandV1> NewCommand(Harness h)
        =>new(){OperationId=Guid.NewGuid(),ExpectedVersion="absent",ExpectedReferencesVersion=(await h.Service.ReadAsync(null,h.Actor,default)).ReferencesVersion,
            Reason="tenant approved assignment",UserName="new-user",Password="New1!Password",RoleId=71,RoleIds=[71,72,71]};
    private sealed record Harness(CP6Context Db,IamAdministrationV1 Authority,IamUserAdministrationV1 Service,Guid Tenant,Guid ActorId,Guid Parent,Guid Child):IAsyncDisposable
    { public Task<Guid> Actor(CancellationToken ct)=>Task.FromResult(ActorId);public ValueTask DisposeAsync()=>Db.DisposeAsync(); }
    private sealed class ReceiptFault:DbCommandInterceptor
    {
        public bool Armed,Fired;
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,CommandEventData data,InterceptionResult<int> result,CancellationToken ct=default)
        {if(Armed&&command.CommandText.Contains("INSERT INTO cp6_authentication_v1.iam_operations")){Fired=true;throw new IOException("owned receipt failure");}return base.NonQueryExecutingAsync(command,data,result,ct);}
    }
    private sealed class CommitFault:DbTransactionInterceptor
    {
        public bool Armed;
        public override Task TransactionCommittedAsync(DbTransaction transaction,TransactionEndEventData data,CancellationToken ct=default)
        {if(Armed){Armed=false;throw new IOException("owned commit acknowledgement lost");}return Task.CompletedTask;}
    }
}
