using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;

namespace CP6.Tests.Oa;
public sealed class ApprovalTaskAuthorityEvidenceTests
{
    [Fact]
    public void Same_permission_sets_in_different_database_order_preserve_task_authority_but_revocation_changes_it()
    {
        var user=Guid.NewGuid();var a=Guid.NewGuid();var b=Guid.NewGuid();
        UserPermissionContext Facts(bool reverse)=>new(){UserId=user,UserName="reviewer",
            RoleIds=reverse?[74,72]:[72,74],MenuKeys=reverse?["document","oa-inbox"]:["oa-inbox","document"],
            ActionKeys=reverse?["document:query","oa-inbox:approve"]:["oa-inbox:approve","document:query"],
            DataScopes=reverse?new(){["document"]=4,["oa-inbox"]=5}:new(){["oa-inbox"]=5,["document"]=4},
            CustomDeptIds=new(){["document"]=reverse?[b,a]:[a,b]},
            FieldPerms=new(){["document"]=reverse?new(){["Metadata"]=1,["Content"]=1}:new(){["Content"]=1,["Metadata"]=1}}};
        var first=Facts(false);var reordered=Facts(true);
        var original=ApprovalTaskAuthorityV2.PermissionEvidence(first,first);
        Assert.Equal(original,ApprovalTaskAuthorityV2.PermissionEvidence(reordered,reordered));
        reordered.ActionKeys.Remove("oa-inbox:approve");
        Assert.NotEqual(original,ApprovalTaskAuthorityV2.PermissionEvidence(reordered,reordered));
    }
}
