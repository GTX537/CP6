using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private async Task OptionBlocked(Harness h,int process)
    {
        await using var observer=server.Context(new TenantContext{CurrentTenantId=h.Tenant});
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while(!await observer.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT FROM pg_stat_activity WHERE pid={process} AND wait_event='advisory') AS \"Value\"").SingleAsync(deadline.Token))
            await Task.Delay(10,deadline.Token);
    }
    [CurrentPermissionPostgresFact]
    public async Task FormOptions_two_connections_final_submit_holds_source_gate_until_its_commit()
    {
        await using var h=await Seed();var type=await OptionDictionary(h);var(prepared,_,_)=await PreparePersonal(h,OptionFormSchema(type));var context=await prepared.ContextAsync("personal");
        await using var peer=server.Context(new TenantContext{CurrentTenantId=h.Tenant});await peer.Database.OpenConnectionAsync();
        var pid=await peer.Database.SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"").SingleAsync();Task<int>? writing=null;var calls=0;
        var owner=PersonalForm(h,async _=>
        {
            if(++calls==2)
            {
                writing=peer.Sys_DictTypes.Where(t=>t.Id==type).ExecuteUpdateAsync(s=>s.SetProperty(t=>t.Enable,false));
                await OptionBlocked(h,pid);Assert.False(writing.IsCompleted);
            }
            return h.User;
        });
        var raw=PersonalCommand(PersonalDirect(context,"{\"choice\":\"A\"}"),"source-race");
        Assert.Equal("SUCCEEDED",(await owner.SubmitDirectAsync("personal",raw)).Outcome);Assert.NotNull(writing);Assert.Equal(1,await writing!);
        Assert.Equal(1,await h.Db.Wf_FormDatas.CountAsync());Assert.False((await h.Db.Sys_DictTypes.AsNoTracking().SingleAsync(t=>t.Id==type)).Enable);
        Assert.Equal("SUCCEEDED",(await prepared.SubmitDirectAsync("personal",raw)).Outcome);
        Assert.Equal("FORM_OPTION_SOURCE_UNAVAILABLE",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>prepared.SubmitDirectAsync("personal",PersonalCommand(PersonalDirect(context,"{\"choice\":\"A\"}"),"later")))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task FormOptions_two_connections_prior_source_write_is_observed_before_form_effect()
    {
        await using var h=await Seed();var type=await OptionDictionary(h);var(owner,_,_)=await PreparePersonal(h,OptionFormSchema(type));var context=await owner.ContextAsync("personal");
        await using var peer=server.Context(new TenantContext{CurrentTenantId=h.Tenant});await using var tx=await peer.Database.BeginTransactionAsync();
        await peer.Sys_DictTypes.Where(t=>t.Id==type).ExecuteUpdateAsync(s=>s.SetProperty(t=>t.Enable,false));
        await h.Db.Database.OpenConnectionAsync();var pid=await h.Db.Database.SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"").SingleAsync();
        var attempt=owner.SubmitDirectAsync("personal",PersonalCommand(PersonalDirect(context,"{\"choice\":\"A\"}"),"prior-disable"));
        await OptionBlocked(h,pid);Assert.False(attempt.IsCompleted);await tx.CommitAsync();
        Assert.Equal("FORM_OPTION_SOURCE_UNAVAILABLE",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>attempt)).Message);
        Assert.Empty(await h.Db.Wf_FormDatas.ToArrayAsync());
        Assert.Equal("NOT_OBSERVED",(await owner.OriginalAsync(PersonalFormOwnerV2.Direct,"personal","prior-disable")).Outcome);
    }
}
