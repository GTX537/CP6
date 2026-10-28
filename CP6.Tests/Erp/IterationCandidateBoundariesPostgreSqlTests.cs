using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Plm;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task IterationCandidate_final_revocation_and_missing_journal_roll_back_candidate_and_recover_exact_key()
    {
        var hook=new StartWriteHook();await using var h=await Seed(hook);var f=await CandidateSetup(h);var o=Candidates(h);
        var menu=await h.Db.Sys_Menus.SingleAsync(x=>x.MenuKey=="plm-configuration-rule");
        foreach(var failure in new[]{"actor","source","journal"})
        {
            var command=f.Command with{OperationId=Guid.NewGuid(),CandidateId=Guid.NewGuid()};var injected=false;
            hook.Before=async c=>
            {
                if(!c.CommandText.Contains("INSERT INTO cp6_main_owner_v1.operations"))return;
                if(failure=="journal"){c.CommandText="SELECT 1";injected=true;return;}
                using var revoke=c.Connection!.CreateCommand();revoke.Transaction=c.Transaction;
                revoke.CommandText=failure=="actor"
                    ?"UPDATE public.\"Sys_Users\" SET \"Enable\"=false WHERE \"Id\"=@who"
                    :"DELETE FROM public.\"Sys_RoleAction\" WHERE \"TenantId\"=@who AND \"ActionCode\"='query' AND \"MenuId\"=@menu";
                var p=revoke.CreateParameter();p.ParameterName="who";p.Value=failure=="actor"?h.User:h.Tenant;revoke.Parameters.Add(p);
                if(failure=="source"){var m=revoke.CreateParameter();m.ParameterName="menu";m.Value=menu.MenuId;revoke.Parameters.Add(m);}
                injected=await revoke.ExecuteNonQueryAsync()>0;
            };
            Assert.NotNull(await Record.ExceptionAsync(()=>o.CommandAsync(command)));hook.Before=null;Assert.True(injected);
            Assert.True(await h.Db.Sys_Users.AnyAsync(x=>x.Id==h.User&&x.Enable));
            Assert.Equal("UNKNOWN",(await o.OperationAsync(command.OperationId,false)).State);
            Assert.DoesNotContain(await o.ListAsync(command.IterationId),x=>x.Reference.Id==command.CandidateId);
            var result=await o.CommandAsync(command);Assert.Equal("1",result.Version);Assert.Equal(result,(await o.OperationAsync(command.OperationId,false)).Result);
        }
    }
    [CurrentPermissionPostgresFact]
    public async Task IterationCandidate_current_fields_parent_scope_and_same_transaction_dependencies_are_required()
    {
        await using var h=await Seed();var f=await CandidateSetup(h);var o=Candidates(h);await o.CommandAsync(f.Command);
        h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey="plm-engineering-iteration",FieldName="Inputs",Access=3});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.ReadAsync(f.Command.IterationId,f.Command.CandidateId))).Status);
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.OperationAsync(f.Command.OperationId,false))).Status);
        await h.Db.Sys_RoleFieldPerms.Where(x=>x.ResourceKey=="plm-engineering-iteration").ExecuteDeleteAsync();
        h.Db.Sys_RoleDataScopes.Add(new(){RoleId=72,ResourceKey="plm-engineering-iteration",ScopeType=4});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.ListAsync(f.Command.IterationId))).Status);
        await h.Db.Sys_RoleDataScopes.Where(x=>x.ResourceKey=="plm-engineering-iteration").ExecuteDeleteAsync();
        await using var db=server.Context(new TenantContext{CurrentTenantId=h.Tenant});var other=h with{Db=db};
        var invalid=new IterationCandidateService(h.Db,CandidateAuthority(h),Iterations(other),CandidateChecks(h),ScopeFence(h));
        Assert.Equal("AUTHORITY_FENCE_UNAVAILABLE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>invalid.ListAsync(f.Command.IterationId))).Message);
        invalid=new(h.Db,CandidateAuthority(h),Iterations(h),CandidateChecks(other),ScopeFence(h));
        Assert.Equal("AUTHORITY_FENCE_UNAVAILABLE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>invalid.ReadAsync(f.Command.IterationId,f.Command.CandidateId))).Message);
        Assert.Single(await o.ListAsync(f.Command.IterationId));
    }
}
