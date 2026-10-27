using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task App_intake_two_connections_commit_one_intent_and_retries_keep_original_binding_after_disable()
    {
        await using var h=await Seed();var f=await IntakeSetup(h);var e=IntakeBody(f);
        await using var db=server.Context(new TenantContext{CurrentTenantId=h.Tenant});var peer=h with{Db=db};
        var second=new ApprovalIntakeOwnerV2(db,ScopeFence(peer),new IntakeAuthority(f.Authority.Identity),ToolApproval(peer),Authoring(peer)){ReceiveClock=()=>f.ReceiveAt};
        var results=await Task.WhenAll(f.Owner.SubmitAsync(f.Payload),second.SubmitAsync(ApprovalCanonicalV2.Bytes(e with{CommandId=Guid.NewGuid(),OwnerOutboxId=Guid.NewGuid()})));
        Assert.Equal(new[]{200,201},results.Select(x=>x.Status).Order());Assert.Equal(results[0].Ack,results[1].Ack);
        await f.Binding.DisableAsync(f.Bound.Result.BindingId,BindingEtag(f.Bound),BindingBytes(new ApprovalBindingDisableV2(Guid.NewGuid(),"disable future only")));
        var retry=await f.Owner.SubmitAsync(ApprovalCanonicalV2.Bytes(e with{CommandId=Guid.NewGuid()}));Assert.Equal(results[0].Ack,retry.Ack);
        var alias=await second.SubmitAsync(ApprovalCanonicalV2.Bytes(e with{CommandId=Guid.NewGuid(),SubmissionId=Guid.NewGuid(),Reason="same intent"}));
        Assert.Equal(e.SubmissionId,alias.Ack.Alias!.CanonicalSubmissionId);
        Assert.Equal(1,await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_approval_intake_v2.submissions WHERE tenant_id={h.Tenant}").SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task App_intake_receive_time_boundary_and_exact_or_dynamic_publication_are_persisted_once()
    {
        await using var h=await Seed();var f=await IntakeSetup(h);
        var head=await f.Flows.ReadHeadAsync("binding-flow");
        var draft=await f.Flows.CreateNextDraftAsync("binding-flow",NextDraftCommand(head.Version,f.Version));
        var edit=await f.Flows.SaveAsync("binding-flow",AuthoringCommand("second-edit",draft.ResourceVersion,"second"));
        var publication=await f.Flows.PublishAsync("binding-flow",PublicationCommand(await Preview(f.Flows,"binding-flow",edit),"second-publish"));
        var secondVersion=publication.Result!.Publication!.VersionId;
        var input=BindingInput(f.Version,f.ReceiveAt.AddSeconds(10)) with{BizType="WMS_TOOL_DISCARD",SubjectKind="TOOL_DISPOSITION_REQUEST",SelectionMode="RESOLVE_PUBLISHED_ON_SUBMIT",FlowDefVersionId=null};
        var switched=await f.Binding.SaveAsync(f.Bound.Result.BindingId,BindingEtag(f.Bound),BindingBytes(input));
        f.Owner.ReceiveClock=()=>f.ReceiveAt.AddSeconds(10);var accepted=await f.Owner.SubmitAsync(f.Payload);
        Assert.Equal(switched.Result.RevisionId,accepted.Ack.BindingRevisionId);Assert.Equal(secondVersion,accepted.Ack.FlowDefVersionId);
        Assert.True(IntakeBody(f).OwnerFrozenAt<f.ReceiveAt);
        await f.Binding.DisableAsync(switched.Result.BindingId,BindingEtag(switched),BindingBytes(new ApprovalBindingDisableV2(Guid.NewGuid(),"later configuration")));
        Assert.Equal(accepted.Ack,(await f.Owner.SubmitAsync(f.Payload)).Ack);
        // An exact pin is independently exercised by the first fixture test; both modes use the same immutable publication source.
    }
    [CurrentPermissionPostgresFact]
    public async Task App_intake_rule_errors_and_exclusive_conflicts_never_fall_back_ordered_rules_use_original_array()
    {
        await using var h=await Seed();var f=await IntakeSetup(h);var input=BindingInput(f.Version,f.ReceiveAt.AddSeconds(-1)) with{BizType="WMS_TOOL_DISCARD",SubjectKind="TOOL_DISPOSITION_REQUEST"};
        var first=Guid.NewGuid();var second=Guid.NewGuid();
        input=input with{Rules=[new(first,9,"purpose == 'TOOL_DISCARD'","binding-flow",f.Version),new(second,0,"purpose == 'TOOL_DISCARD'","binding-flow",f.Version)]};
        var current=await f.Binding.SaveAsync(f.Bound.Result.BindingId,BindingEtag(f.Bound),BindingBytes(input));
        Assert.Equal("MULTIPLE_RULE_MATCH",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>f.Owner.SubmitAsync(f.Payload))).Message);
        input=input with{CommandId=Guid.NewGuid(),Rules=[new(first,0,"purpose > 0","binding-flow",f.Version)]};
        current=await f.Binding.SaveAsync(current.Result.BindingId,BindingEtag(current),BindingBytes(input));
        Assert.Equal("SNAPSHOT_SCHEMA_INVALID",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>f.Owner.SubmitAsync(f.Payload))).Message);
        Assert.Empty(await h.Db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM cp6_approval_intake_v2.submissions WHERE tenant_id={h.Tenant}").ToArrayAsync());
        input=input with{CommandId=Guid.NewGuid(),RuleMode="ORDERED_FIRST_MATCH",Rules=[new(first,9,"purpose == 'TOOL_DISCARD'","binding-flow",f.Version),new(second,0,"purpose > 0","binding-flow",f.Version)]};
        current=await f.Binding.SaveAsync(current.Result.BindingId,BindingEtag(current),BindingBytes(input));
        Assert.Equal(201,(await f.Owner.SubmitAsync(f.Payload)).Status);
        var proof=await h.Db.Database.SqlQuery<string>($"SELECT selection_json AS \"Value\" FROM cp6_approval_intake_v2.submissions WHERE tenant_id={h.Tenant}").SingleAsync();
        Assert.Equal(first,JsonDocument.Parse(proof).RootElement.GetProperty("ruleId").GetGuid());
    }
    [CurrentPermissionPostgresFact]
    public async Task App_intake_same_transaction_native_change_after_start_insert_rolls_back_all_effects()
    {
        var hook=new CommandHook();await using var h=await Seed(hook);var f=await IntakeSetup(h);
        var id=await h.Db.Database.SqlQuery<Guid>($"SELECT tool_id AS \"Value\" FROM cp6_tool_approval_v1.submissions WHERE tenant_id={h.Tenant}").SingleAsync();
        hook.Before=async command=>{
            if(!command.CommandText.Contains("SELECT body_json AS"))return;
            await using var probe=command.Connection!.CreateCommand();probe.Transaction=command.Transaction;
            probe.CommandText="SELECT EXISTS(SELECT FROM cp6_approval_intake_v2.start_outbox WHERE tenant_id=@tenant)";probe.Parameters.Add(new Npgsql.NpgsqlParameter("tenant",h.Tenant));
            if(!(bool)(await probe.ExecuteScalarAsync())!)return;hook.Before=null;
            await using var change=command.Connection.CreateCommand();change.Transaction=command.Transaction;
            change.CommandText="UPDATE public.\"T_PlateMoldStock\" SET \"UsedShots\"=19 WHERE \"Id\"=@id";change.Parameters.Add(new Npgsql.NpgsqlParameter("id",id));await change.ExecuteNonQueryAsync();
        };
        var failure=await Record.ExceptionAsync(()=>f.Owner.SubmitAsync(f.Payload));Assert.NotNull(failure);
        Assert.True(failure is CP6.Core.Services.Erp.OrderPriceCorrectionException{Status:412} or ScopeAuthorizationExceptionV1{Status:403},failure.ToString());
        Assert.Empty(await h.Db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM cp6_approval_intake_v2.submissions WHERE tenant_id={h.Tenant}").ToArrayAsync());
        Assert.Empty(await h.Db.Database.SqlQuery<Guid>($"SELECT command_id AS \"Value\" FROM cp6_approval_intake_v2.commands WHERE tenant_id={h.Tenant}").ToArrayAsync());
        Assert.Equal(0,(await ToolInstances(h).ReadAsync(id)).Source.GetProperty("usedShots").GetInt32());
    }
}
