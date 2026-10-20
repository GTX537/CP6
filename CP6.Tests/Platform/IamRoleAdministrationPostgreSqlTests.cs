using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace CP6.Tests.Platform;

public sealed partial class IamUserAssignmentsPostgreSqlTests
{
    private static (IamRoleAdministrationV1 Service,IamAdministrationV1 Authority) RoleSession(Harness h)
    {
        var permission=new Mock<ICurrentPermissionContext>();
        permission.Setup(x=>x.GetAsync()).ReturnsAsync(new UserPermissionContext{UserId=h.ActorId,ActionKeys=["role:query","role:add","role:edit","role:delete"]});
        var authority=new IamAdministrationV1(h.Db,permission.Object,new EphemeralDataProtectionProvider());
        return(new(h.Db,authority),authority);
    }
    private static async Task<IamRoleCommandV1> RoleCommand(Harness h,int id=72)
    {
        var editor=await RoleSession(h).Service.ReadAsync(h.Actor,default);var role=editor.Roles.SingleOrDefault(x=>x.RoleId==id);
        return new(){OperationId=Guid.NewGuid(),RoleId=id,RoleName=role?.RoleName??"admin",Description=role?.Description,
            Enable=role?.Enable??true,OrderNo=role?.OrderNo??0,ExpectedVersion=role?.Version??"absent",
            ExpectedReferencesVersion=editor.ReferencesVersion,Reason="owned role administration"};
    }
    [CurrentPermissionPostgresFact]
    public async Task Role_custom_integer_identity_and_name_never_grant_platform_administration()
    {
        await using var h=await Seed();var s=RoleSession(h);
        foreach(var id in new[]{1,int.MaxValue})
        {
            var command=await RoleCommand(h,id);var result=await s.Service.ChangeAsync("role-create",command,h.Actor,default);
            Assert.Equal(id,result.RoleId);Assert.Equal(result,await s.Service.ChangeAsync("role-create",command,h.Actor,default));
            Assert.Equal(result,await s.Authority.Resolve(result.OperationId,"role-create",false,h.Actor,default));
            Assert.Equal(h.Tenant,(await h.Db.Sys_Roles.AsNoTracking().SingleAsync(x=>x.RoleId==id)).TenantId);
        }
        Assert.False(await h.Db.Sys_Users.AnyAsync(x=>x.IsPlatformAdmin));
        Assert.Equal(2,await h.Db.Sys_SecurityLogs.CountAsync(x=>x.EventType==(int)SecurityEventType.IamAdministrationChanged));
    }
    [CurrentPermissionPostgresFact]
    public async Task Role_integer_inputs_outside_pinned_projection_contract_are_not_reclassified_or_partially_written()
    {
        await using var h=await Seed();var s=RoleSession(h);
        foreach(var id in new[]{int.MinValue,0})
        {
            var command=await RoleCommand(h,id);
            var error=await Assert.ThrowsAsync<IamAdministrationExceptionV1>(()=>s.Service.ChangeAsync("role-create",command,h.Actor,default));
            Assert.Equal(503,error.Status);Assert.Equal("IAM_IDENTITY_CONTRACT_UNAVAILABLE",error.Message);
            Assert.False(await h.Db.Sys_Roles.AnyAsync(x=>x.RoleId==id));
            Assert.Equal("UNKNOWN",(await s.Authority.Resolve(command.OperationId,"role-create",false,h.Actor,default)).State);
        }
    }
    [CurrentPermissionPostgresFact]
    public async Task Role_disable_preserves_assignments_and_atomically_updates_permission_identity()
    {
        await using var h=await Seed();var s=RoleSession(h);
        h.Db.Sys_UserRoles.Add(new(){UserId=h.ActorId,RoleId=72});
        h.Db.Sys_RoleMenus.Add(new(){RoleId=72,MenuId=9991});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var command=(await RoleCommand(h)) with{Enable=false};var before=await h.Db.CrmIdentitySnapshots.AsNoTracking().SingleAsync(x=>x.TenantId==h.Tenant && x.AggregateId=="permission:role:72");
        var result=await s.Service.ChangeAsync("role-update",command,h.Actor,default);
        Assert.False(await h.Db.Sys_Roles.Where(x=>x.RoleId==72).Select(x=>x.Enable).SingleAsync());
        Assert.True(await h.Db.Sys_UserRoles.AnyAsync(x=>x.RoleId==72));Assert.True(await h.Db.Sys_RoleMenus.AnyAsync(x=>x.RoleId==72));
        Assert.NotEqual(command.ExpectedVersion,result.Version);
        Assert.Equal(before.Version+1,await h.Db.CrmIdentitySnapshots.Where(x=>x.TenantId==h.Tenant && x.AggregateId=="permission:role:72").Select(x=>x.Version).SingleAsync());
        Assert.Equal(result,await s.Authority.Resolve(command.OperationId,"role-update",false,h.Actor,default));
    }
    [CurrentPermissionPostgresFact]
    public async Task Role_CAS_detects_raw_writer_ABA_and_membership_change()
    {
        await using var h=await Seed();var s=RoleSession(h);var stale=await RoleCommand(h);
        await h.Db.Sys_Roles.Where(x=>x.RoleId==72).ExecuteUpdateAsync(x=>x.SetProperty(r=>r.Enable,false));
        await h.Db.Sys_Roles.Where(x=>x.RoleId==72).ExecuteUpdateAsync(x=>x.SetProperty(r=>r.Enable,true));
        Assert.Equal(412,(await Assert.ThrowsAsync<IamAdministrationExceptionV1>(()=>s.Service.ChangeAsync("role-update",stale,h.Actor,default))).Status);
        var current=await RoleCommand(h);h.Db.Sys_UserRoles.Add(new(){UserId=h.ActorId,RoleId=72});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        Assert.Equal(412,(await Assert.ThrowsAsync<IamAdministrationExceptionV1>(()=>s.Service.ChangeAsync("role-update",current,h.Actor,default))).Status);
        await s.Authority.Resolve(stale.OperationId,"role-update",true,h.Actor,default);
        Assert.Equal("IAM_OPERATION_CLOSED",(await Assert.ThrowsAsync<IamAdministrationExceptionV1>(()=>s.Service.ChangeAsync("role-update",stale,h.Actor,default))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task Role_receipt_failure_rolls_back_audit_role_revision_and_identity()
    {
        var fault=new ReceiptFault();await using var h=await Seed(fault);var s=RoleSession(h);var command=(await RoleCommand(h)) with{Enable=false};
        var before=await h.Db.CrmIdentitySnapshots.Where(x=>x.TenantId==h.Tenant && x.AggregateId=="permission:role:72").Select(x=>x.Version).SingleAsync();
        fault.Armed=true;await Assert.ThrowsAsync<IOException>(()=>s.Service.ChangeAsync("role-update",command,h.Actor,default));
        Assert.True(fault.Fired);Assert.True(await h.Db.Sys_Roles.Where(x=>x.RoleId==72).Select(x=>x.Enable).SingleAsync());
        Assert.Equal(command.ExpectedVersion,await s.Authority.Version("role:72",default));
        Assert.Equal(before,await h.Db.CrmIdentitySnapshots.Where(x=>x.TenantId==h.Tenant && x.AggregateId=="permission:role:72").Select(x=>x.Version).SingleAsync());
        Assert.False(await h.Db.Sys_SecurityLogs.AnyAsync(x=>x.EventType==(int)SecurityEventType.IamAdministrationChanged));
        Assert.Equal("UNKNOWN",(await s.Authority.Resolve(command.OperationId,"role-update",false,h.Actor,default)).State);
    }
    [CurrentPermissionPostgresFact]
    public async Task Role_lost_commit_ack_recovers_exactly_and_rejects_changed_input()
    {
        var fault=new CommitFault();await using var h=await Seed(fault);var s=RoleSession(h);var command=(await RoleCommand(h)) with{Enable=false};
        fault.Armed=true;Assert.Equal(503,(await Assert.ThrowsAsync<IamAdministrationExceptionV1>(()=>s.Service.ChangeAsync("role-update",command,h.Actor,default))).Status);
        var recovered=await s.Authority.Resolve(command.OperationId,"role-update",true,h.Actor,default);Assert.Equal("COMMITTED",recovered.State);
        Assert.Equal(recovered,await s.Service.ChangeAsync("role-update",command,h.Actor,default));
        Assert.Equal("IAM_OPERATION_INPUT_CONFLICT",(await Assert.ThrowsAsync<IamAdministrationExceptionV1>(()=>s.Service.ChangeAsync("role-update",command with{Enable=true},h.Actor,default))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task Role_reference_report_includes_SSO_and_deletion_retains_identity_even_without_current_users()
    {
        await using var h=await Seed();var s=RoleSession(h);
        h.Db.Sys_TenantSsoConfigs.Add(new(){Authority="https://role.example.test",ClientId="role",DefaultRoleId=72});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var policy=new SsoIdentityAdmissionPolicyV1(Guid.NewGuid(),Guid.NewGuid(),"https://role.example.test","https://role.example.test","role","email","email",true,false,true,[72],h.ActorId,DateTimeOffset.UtcNow);
        var document=JsonSerializer.Serialize(policy);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_authentication_v1.sso_admission_policies(tenant_id,document) VALUES({h.Tenant},{document})");
        var references=await s.Service.ReferencesAsync(72,h.Actor,default);Assert.Equal(1,references.SsoDefaults);Assert.True(references.SsoApproved);Assert.False(references.DeletionAllowed);
        var command=await RoleCommand(h);
        Assert.Equal("IAM_ROLE_IDENTITY_REFERENCED",(await Assert.ThrowsAsync<IamAdministrationExceptionV1>(()=>s.Service.ChangeAsync("role-delete",command,h.Actor,default))).Message);
        var unreferenced=await RoleCommand(h,73);
        Assert.Equal("IAM_ROLE_RETENTION_REVIEW_REQUIRED",(await Assert.ThrowsAsync<IamAdministrationExceptionV1>(()=>s.Service.ChangeAsync("role-delete",unreferenced,h.Actor,default))).Message);
        Assert.True(await h.Db.Sys_Roles.AnyAsync(x=>x.RoleId==72));
    }
    [CurrentPermissionPostgresFact]
    public async Task Role_reference_CAS_detects_SSO_default_ABA_and_cross_tenant_same_id_is_unchanged()
    {
        await using var h=await Seed();var s=RoleSession(h);var foreignId=Guid.NewGuid();
        await using var foreign=server.Context(new TenantContext{CurrentTenantId=foreignId});
        foreign.Sys_Roles.Add(new(){RoleId=72,RoleName="foreign role"});await foreign.SaveChangesAsync();
        h.Db.Sys_TenantSsoConfigs.Add(new(){Authority="https://role.example.test",ClientId="role",DefaultRoleId=72});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();var stale=await RoleCommand(h);
        await h.Db.Sys_TenantSsoConfigs.ExecuteUpdateAsync(x=>x.SetProperty(c=>c.DefaultRoleId,73));
        await h.Db.Sys_TenantSsoConfigs.ExecuteUpdateAsync(x=>x.SetProperty(c=>c.DefaultRoleId,72));
        Assert.Equal(412,(await Assert.ThrowsAsync<IamAdministrationExceptionV1>(()=>s.Service.ChangeAsync("role-update",stale,h.Actor,default))).Status);
        await s.Service.ChangeAsync("role-update",(await RoleCommand(h)) with{RoleName="local changed",Enable=false},h.Actor,default);
        var retained=await foreign.Sys_Roles.AsNoTracking().SingleAsync(x=>x.RoleId==72);
        Assert.Equal("foreign role",retained.RoleName);Assert.True(retained.Enable);
    }
}
