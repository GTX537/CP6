using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace CP6.Tests.Platform;

/// <summary>Service-level identity writer fixtures use the current versioned entry. HTTP tests use real sessions.</summary>
internal sealed class IamAdministrationTestSession(CP6Context db, Guid actorId)
{
    internal Task<Guid> Actor(CancellationToken ct) => Task.FromResult(actorId);
    internal IamAdministrationV1 Authority {get;} = new(db, Mock.Of<ICurrentPermissionContext>(p => p.GetAsync() == Task.FromResult(
        new UserPermissionContext { UserId=actorId,ActionKeys=new(){"user:query","user:edit","pub-dept:query","pub-dept:add","pub-dept:edit","pub-dept:delete"} })), new EphemeralDataProtectionProvider());
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
