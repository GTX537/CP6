using CP6.Core.Services.Sys;
using CP6.Entity.DTOs.Sys;
using Moq;

namespace CP6.Tests.Platform;

public sealed partial class IamUserAssignmentsPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task Legacy_role_policy_unknown_scope_cannot_be_saved_without_versions()
    {
        await using var h=await Seed();var service=new RolePermService(h.Db,Mock.Of<ICurrentPermissionContext>());
        await Assert.ThrowsAnyAsync<InvalidOperationException>(()=>service.SaveRoleDataScopesAsync(72,
            [new(){ResourceKey="unknown-"+Guid.NewGuid().ToString("N"),ScopeType=99}],"owned red test"));
    }
    [CurrentPermissionPostgresFact]
    public async Task Legacy_role_policy_invalid_field_access_cannot_be_saved_without_versions()
    {
        await using var h=await Seed();var resource="field-"+Guid.NewGuid().ToString("N");FieldRegistry.Register(resource,new FieldRegistry.Field("Amount","Amount"));
        var service=new RolePermService(h.Db,Mock.Of<ICurrentPermissionContext>());
        await Assert.ThrowsAnyAsync<InvalidOperationException>(()=>service.SaveRoleFieldPermsAsync(72,
            [new(){ResourceKey=resource,FieldName="Amount",Access=0}],"owned red test"));
    }
    [CurrentPermissionPostgresFact]
    public async Task Legacy_role_policy_nonexistent_menu_and_action_cannot_be_saved_without_versions()
    {
        await using var h=await Seed();var service=new RolePermService(h.Db,Mock.Of<ICurrentPermissionContext>());
        await Assert.ThrowsAnyAsync<InvalidOperationException>(()=>service.SaveRolePermAsync(72,
            new(){MenuIds=[int.MaxValue],Actions=[new(){MenuId=int.MaxValue,ActionCode="unregistered"}]},"owned red test"));
    }
}
