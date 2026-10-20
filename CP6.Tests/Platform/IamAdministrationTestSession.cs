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
    internal IamAdministrationV1 Authority => new(db, Mock.Of<ICurrentPermissionContext>(p => p.GetAsync() == Task.FromResult(
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
}
