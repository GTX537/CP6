using CP6.Core.Services.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task PrivacyTombstone_native_writers_cannot_reintroduce_profile_or_reenable_a_disposed_subject()
    {
        await using var h=await Seed();var setup=await PrivacyDispositionSetup(h);await EngineeringAction(h,"privacy-disposition","execute");
        var plan=await PrivacyApprovedPlan(h,setup.Target,setup.Policy,setup.Approver);
        await using var db=server.Context(new TenantContext{CurrentTenantId=h.Tenant},PrivacyIdentityRuntime(h.Tenant));var worker=h with{Db=db};
        await PrivacyDispositionOwner(worker).ExecuteAsync(plan.Plan,PrivacyExecuteCommand(plan),PrivacySession(worker));
        var error=await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"Sys_Users\" SET \"Email\"='resurrected@example.invalid' WHERE \"Id\"={setup.Target}"));
        Assert.Equal(PostgresErrorCodes.CheckViolation,error.SqlState);Assert.Contains("PRIVACY_TOMBSTONE",error.MessageText);
        await Assert.ThrowsAsync<PostgresException>(()=>db.Sys_Users.Where(x=>x.Id==setup.Target).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.Enable,true)));
        Assert.Null(await db.Sys_Users.Where(x=>x.Id==setup.Target).Select(x=>x.Email).SingleAsync());
        // Non-identifying authorization/organization references retain their own accepted contract.
        await db.Sys_Users.Where(x=>x.Id==setup.Target).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.ManagerId,h.User));
        Assert.Equal(h.User,await db.Sys_Users.Where(x=>x.Id==setup.Target).Select(x=>x.ManagerId).SingleAsync());
        // Moving the stable key must not detach the disposed profile from its permanent tombstone.
        var moved=Guid.NewGuid();
        Assert.Equal(PostgresErrorCodes.CheckViolation,(await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"Sys_Users\" SET \"Id\"={moved} WHERE \"Id\"={setup.Target}"))).SqlState);
        Assert.Equal(PostgresErrorCodes.CheckViolation,(await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"Sys_Users\" SET \"TenantId\"={Guid.NewGuid()} WHERE \"Id\"={setup.Target}"))).SqlState);
        await db.Sys_Users.Where(x=>x.Id==setup.Target).ExecuteDeleteAsync();await using var other=await Seed();
        other.Db.Sys_Users.Add(new(){Id=setup.Target,UserName="anon_"+setup.Target.ToString("N"),Password="simulation",RoleId=72,Enable=false,AllowPasswordFallback=false});
        var reused=await Assert.ThrowsAsync<DbUpdateException>(()=>other.Db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation,Assert.IsType<PostgresException>(reused.InnerException).SqlState);
        Assert.Contains("PRIVACY_TOMBSTONE_IDENTITY_REUSE_FORBIDDEN",reused.InnerException!.Message);
    }
}
