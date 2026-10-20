using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DTOs.Sys;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace CP6.Tests.Platform;

/// <summary>Service-level identity writer fixtures use the current versioned entry. HTTP tests use real sessions.</summary>
internal sealed class IamAdministrationTestSession(CP6Context db, Guid actorId)
{
    internal Task<Guid> Actor(CancellationToken ct) => Task.FromResult(actorId);
    internal IamAdministrationV1 Authority {get;} = new(db, Mock.Of<ICurrentPermissionContext>(p => p.GetAsync() == Task.FromResult(
        new UserPermissionContext { UserId=actorId,ActionKeys=new(){"user:query","user:edit","pub-dept:query","pub-dept:add","pub-dept:edit","pub-dept:delete",
            "pub-role-perm:query","pub-role-perm:edit","pub-data-scope:query","pub-data-scope:edit","pub-field-perm:query","pub-field-perm:edit"} })), new EphemeralDataProtectionProvider());
    internal static async Task<IamAdministrationTestSession> ForPolicy(CP6Context db)
    {
        if(!await db.Sys_Tenants.AnyAsync(x=>x.Id==db.CurrentTenantId))
            db.Sys_Tenants.Add(new(){Id=db.CurrentTenantId,TenantCode=db.CurrentTenantId.ToString("N"),TenantName="owned policy fixture"});
        var actor=new Sys_User{UserName="policy-"+Guid.NewGuid().ToString("N"),Password="owned fixture",Enable=true};
        db.Sys_Users.Add(actor);await db.SaveChangesAsync();db.ChangeTracker.Clear();return new(db,actor.Id);
    }
    internal IamRolePolicyAdministrationV1 Policies=>new(db,Authority);
    internal async Task<IamRolePolicyCommandV1> PolicyCommand(string mode,int role)
    {
        db.ChangeTracker.Clear();var e=await Policies.ReadAsync(mode,role,Actor,default);
        return new(){OperationId=Guid.NewGuid(),ExpectedVersion=e.Version,ExpectedReferencesVersion=e.ReferencesVersion,
            ExpectedCatalogVersion=e.Catalog.Version,Reason="owned real policy writer",MenuIds=e.Function.MenuIds,Actions=e.Function.Actions,
            Scopes=e.Catalog.Scopes.Select(x=>new RoleDataScopeDto{ResourceKey=x.Key,ScopeType=x.Default}).ToList()};
    }
    internal async Task SetRolePermissions(int role,RolePermDto value)
        =>await Policies.ChangeAsync("actions",role,(await PolicyCommand("actions",role)) with{MenuIds=value.MenuIds,Actions=value.Actions},Actor,default);
    internal async Task SetRoleScopes(int role,List<RoleDataScopeDto> value)
    {
        var command=await PolicyCommand("scopes",role);
        foreach(var row in value){command.Scopes!.RemoveAll(x=>x.ResourceKey==row.ResourceKey);command.Scopes.Add(row);}
        await Policies.ChangeAsync("scopes",role,command,Actor,default);
    }
    internal async Task SetRoleFields(int role,string resource,List<RoleFieldPermDto> value)
        =>await Policies.ChangeAsync("fields",role,(await PolicyCommand("fields",role)) with{ResourceKey=resource,Fields=value},Actor,default);
    internal async Task SetUser(Guid userId, int[]? roles=null, int? primary=null, Guid? department=null, string? email=null)
    {
        if(!await db.Sys_Tenants.AnyAsync(x=>x.Id==db.CurrentTenantId))
        {db.Sys_Tenants.Add(new(){Id=db.CurrentTenantId,TenantName="owned writer fixture",TenantCode=db.CurrentTenantId.ToString("N"),Enable=true});await db.SaveChangesAsync();}
        db.ChangeTracker.Clear();
        var service=new IamUserAdministrationV1(db,Authority,new BCryptPasswordHasher(4),Mock.Of<IRefreshTokenService>());
        var editor=await service.ReadAsync(userId,Actor,default);
        await service.ChangeAsync(roles==null?"user-org":"user-roles",new(){Id=userId,OperationId=Guid.NewGuid(),ExpectedVersion=editor.User!.Version,
            ExpectedReferencesVersion=editor.ReferencesVersion,Reason="owned identity writer regression",RoleId=primary,RoleIds=roles,DeptId=department,Email=email},Actor,default);
    }
    internal IamDepartmentAdministrationV1 Departments=>new(db,Authority);
    internal async Task<IamDepartmentCommandV1> DepartmentCommand(Guid? id=null)
    {
        var editor=await Departments.ReadAsync(Actor,default);var row=editor.Departments.SingleOrDefault(x=>x.Id==id);
        return new(){Id=id,OperationId=Guid.NewGuid(),ExpectedVersion=row?.Version??"absent",ExpectedReferencesVersion=editor.ReferencesVersion,
            ExpectedTreeVersion=editor.TreeVersion,Reason="owned organization writer regression",DeptCode=row?.DeptCode,DeptName=row?.DeptName,
            ParentId=row?.ParentId,LeaderId=row?.LeaderId,Enable=row?.Enable??true,Sort=row?.Sort??0};
    }
    internal async Task<Guid> CreateDepartment(string code,string name,Guid? parent=null)
        =>(await Departments.ChangeAsync("dept-create",(await DepartmentCommand()) with{DeptCode=code,DeptName=name,ParentId=parent},Actor,default)).EntityId!.Value;
    internal async Task MoveDepartment(Guid id,Guid? parent)
    {
        var preview=await Departments.PreviewAsync(id,parent,Actor,default);
        await Departments.ChangeAsync("dept-move",(await DepartmentCommand(id)) with{ParentId=parent,PreviewVersion=preview.PreviewVersion},Actor,default);
    }
    internal async Task SetLeader(Guid id,Guid? leader)=>await Departments.ChangeAsync("dept-leader",(await DepartmentCommand(id)) with{LeaderId=leader},Actor,default);
}
