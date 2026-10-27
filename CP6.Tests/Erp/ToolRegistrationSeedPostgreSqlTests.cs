using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task Tool_registration_new_tenant_seed_defines_query_initialize_without_granting_them()
    {
        await using var h=await Seed();
        CP6.WebApi.Seed.WmsPermissionSeed.EnsureSeeded(h.Db);h.Db.ChangeTracker.Clear();
        CP6.WebApi.Seed.WmsPermissionSeed.EnsureSeeded(h.Db);h.Db.ChangeTracker.Clear();
        foreach(var action in new[]{"query","initialize"})
        {
            Assert.Equal(1,await h.Db.Sys_MenuActions.CountAsync(x=>x.MenuId==443&&x.ActionCode==action));
            Assert.False(await h.Db.Sys_RoleActions.AnyAsync(x=>x.MenuId==443&&x.ActionCode==action));
        }
    }
}
