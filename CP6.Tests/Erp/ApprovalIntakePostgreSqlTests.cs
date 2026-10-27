using System.Text;
using System.Text.Json;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private sealed class IntakeAuthority(ApprovalServiceIdentityV2 identity):IApprovalServiceAuthorityV2
    {
        internal ApprovalServiceIdentityV2 Identity=identity;
        internal Func<CancellationToken,Task>? Check;
        public async Task<ApprovalServiceIdentityV2> CurrentAsync(CancellationToken ct){if(Check!=null)await Check(ct);return Identity;}
    }
    private sealed record IntakeFixture(ApprovalIntakeOwnerV2 Owner,IntakeAuthority Authority,byte[] Payload,
        ApprovalBindingOwnerV2 Binding,ApprovalBindingResultV2 Bound,FlowAuthoringOwnerV2 Flows,Guid Version,DateTimeOffset ReceiveAt);
    private async Task<IntakeFixture> IntakeSetup(Harness h,ApprovalBindingRuleV2[]? rules=null)
    {
        var id=await MaintenanceSetup(h);
        foreach(var action in new[]{"dispose","dispose-policy","dispose-submit"})await EngineeringAction(h,"wms-plate-mold",action);
        await DispositionRequest(Disposition(h),id);await DispositionBlock(Disposition(h),id);
        var(binding,flows,version)=await PrepareBinding(h);var principal=Guid.NewGuid();
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_approval_binding_v2.registrations(tenant_id,environment_id,biz_type,subject_kind,owner,revision,snapshot_schema,snapshot_fields,enabled,service_principal,audience,fixed_route,evidence_mode)
            VALUES({h.Tenant},{ScopeEnvironment},'WMS_TOOL_DISCARD','TOOL_DISPOSITION_REQUEST','Main.Wms.ToolDisposition.v1',{Guid.NewGuid()},'Main.Wms.ToolDisposition.snapshot/1','["purpose","reasonCode"]',true,{principal},'CP6.Services','/internal/wms/approval-results/v2','DEMO')
            """);
        var at=BindingTime();var input=BindingInput(version,at) with{BizType="WMS_TOOL_DISCARD",SubjectKind="TOOL_DISPOSITION_REQUEST",Rules=rules??[]};
        var bound=await binding.SaveAsync(null,null,BindingBytes(input));
        var tool=ToolApproval(h);var context=await ToolApprovalReady(tool,id);await tool.SubmitAsync(id,context.Etag,ToolSubmit(context));
        var payload=await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_tool_approval_v1.submissions WHERE tenant_id={h.Tenant} AND tool_id={id}").SingleAsync();
        var authority=new IntakeAuthority(new(ScopeEnvironment,h.Tenant,principal,"fixture-service","CP6.Services","test-registration",true,"DEMO"));
        return new(new(h.Db,ScopeFence(h),authority,tool,flows){ReceiveClock=()=>at.AddSeconds(1)},authority,payload,binding,bound,flows,version,at.AddSeconds(1));
    }
    private static ApprovalSubmitCommandV2 IntakeBody(IntakeFixture f)=>JsonSerializer.Deserialize<ApprovalSubmitCommandV2>(f.Payload,ApprovalBindingOwnerV2.Json)!;

    [CurrentPermissionPostgresFact]
    public async Task App_intake_real_frozen_owner_commits_command_submission_start_outbox_and_exact_pin_without_instance()
    {
        await using var h=await Seed();var f=await IntakeSetup(h);var e=IntakeBody(f);var received=await f.Owner.SubmitAsync(f.Payload);
        Assert.Equal(201,received.Status);Assert.Equal("DISPATCH_PENDING",received.Ack.State);Assert.Null(received.Ack.InstanceId);
        Assert.Equal(f.Version,received.Ack.FlowDefVersionId);Assert.Equal(f.Bound.Result.RevisionId,received.Ack.BindingRevisionId);
        Assert.Equal(f.Bound.Result.TimelineRevisionId,received.Ack.BindingTimelineRevisionId);Assert.Equal(e.SubmissionId,received.Ack.SubmissionId);
        Assert.Empty(await h.Db.Wf_FlowInstances.ToArrayAsync());Assert.Empty(await h.Db.StockTransactions.ToArrayAsync());
        Assert.Equal(received.Ack,await f.Owner.OriginalAsync(e.CommandId));Assert.Equal(200,(await f.Owner.SubmitAsync(f.Payload)).Status);
        var pending=await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_approval_intake_v2.start_outbox WHERE tenant_id={h.Tenant}").ToArrayAsync();Assert.Single(pending);Assert.Equal(f.Payload,pending[0]);
        await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>h.Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM cp6_approval_intake_v2.submissions WHERE tenant_id={h.Tenant}"));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>h.Db.Database.ExecuteSqlRawAsync("TRUNCATE cp6_approval_intake_v2.start_outbox CASCADE"));
    }
    [CurrentPermissionPostgresFact]
    public async Task App_intake_lost_commit_ack_replays_original_after_binding_policy_and_submit_revoked()
    {
        var fault=new CommitFault();await using var h=await Seed(fault);var f=await IntakeSetup(h);var e=IntakeBody(f);fault.Armed=true;
        Assert.Equal("OUTCOME_UNKNOWN",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>f.Owner.SubmitAsync(f.Payload))).Message);
        var first=await f.Owner.OriginalAsync(e.CommandId);
        await f.Binding.DisableAsync(f.Bound.Result.BindingId,BindingEtag(f.Bound),BindingBytes(new ApprovalBindingDisableV2(Guid.NewGuid(),"disable future intake")));
        await h.Db.Sys_RoleActions.Where(x=>x.ActionCode=="dispose-submit"||x.ActionCode=="dispose-policy").ExecuteDeleteAsync();
        f.Authority.Identity=f.Authority.Identity with{CanSubmit=false};
        Assert.Equal(first,(await f.Owner.SubmitAsync(f.Payload)).Ack);Assert.Equal(first,await f.Owner.OriginalAsync(e.CommandId));
        Assert.Equal("COMMAND_PAYLOAD_CONFLICT",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>f.Owner.SubmitAsync(ApprovalCanonicalV2.Bytes(e with{Reason="changed"})))).Message);
        Assert.Equal("COMMAND_FORBIDDEN",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>f.Owner.SubmitAsync(ApprovalCanonicalV2.Bytes(e with{CommandId=Guid.NewGuid()})))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task App_intake_three_comparison_levels_keep_one_canonical_submission_and_direct_alias()
    {
        await using var h=await Seed();var f=await IntakeSetup(h);var e=IntakeBody(f);var first=await f.Owner.SubmitAsync(f.Payload);
        var retry=e with{CommandId=Guid.NewGuid(),OwnerOutboxId=Guid.NewGuid()};Assert.Equal(first.Ack,(await f.Owner.SubmitAsync(ApprovalCanonicalV2.Bytes(retry))).Ack);
        Assert.Equal("SUBMISSION_PAYLOAD_CONFLICT",(await Assert.ThrowsAsync<CP6.Core.Services.Erp.OrderPriceCorrectionException>(()=>f.Owner.SubmitAsync(ApprovalCanonicalV2.Bytes(retry with{CommandId=Guid.NewGuid(),Reason="changed"})))).Message);
        var duplicate=e with{CommandId=Guid.NewGuid(),OwnerOutboxId=Guid.NewGuid(),SubmissionId=Guid.NewGuid(),Reason="different transport explanation"};
        var alias=await f.Owner.SubmitAsync(ApprovalCanonicalV2.Bytes(duplicate));Assert.Equal(first.Ack.SubmissionId,alias.Ack.SubmissionId);Assert.Equal(duplicate.SubmissionId,alias.Ack.Alias!.RequestedSubmissionId);
        Assert.Equal(alias.Ack,(await f.Owner.SubmitAsync(ApprovalCanonicalV2.Bytes(duplicate with{CommandId=Guid.NewGuid()}))).Ack);
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_approval_intake_v2.start_outbox WHERE tenant_id={h.Tenant}").SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task App_intake_wrong_scope_unknown_fields_forged_frozen_evidence_and_disabled_binding_write_nothing()
    {
        await using var h=await Seed();var f=await IntakeSetup(h);var e=IntakeBody(f);
        Assert.Equal(403,(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>f.Owner.SubmitAsync(ApprovalCanonicalV2.Bytes(e with{Scope=e.Scope with{TenantId=Guid.NewGuid()}})))).Status);
        var extra=System.Text.Json.Nodes.JsonNode.Parse(f.Payload)!.AsObject();extra["acceptAsApproved"]=true;
        Assert.Equal(422,(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>f.Owner.SubmitAsync(JsonSerializer.SerializeToUtf8Bytes(extra)))).Status);
        await Assert.ThrowsAsync<CP6.Core.Services.Erp.OrderPriceCorrectionException>(()=>f.Owner.SubmitAsync(ApprovalCanonicalV2.Bytes(e with{StarterId=Guid.NewGuid()})));
        await f.Binding.DisableAsync(f.Bound.Result.BindingId,BindingEtag(f.Bound),BindingBytes(new ApprovalBindingDisableV2(Guid.NewGuid(),"disabled")));
        Assert.Equal("BINDING_NOT_FOUND",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>f.Owner.SubmitAsync(f.Payload))).Message);
        Assert.Empty(await h.Db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM cp6_approval_intake_v2.submissions WHERE tenant_id={h.Tenant}").ToArrayAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task App_intake_final_service_revoke_rolls_back_complete_TxS()
    {
        await using var h=await Seed();var f=await IntakeSetup(h);var count=0;
        f.Authority.Check=_=>{if(++count==2)f.Authority.Identity=f.Authority.Identity with{CanSubmit=false};return Task.CompletedTask;};
        Assert.Equal("AUTHORITY_CHANGED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>f.Owner.SubmitAsync(f.Payload))).Message);
        Assert.Empty(await h.Db.Database.SqlQuery<Guid>($"SELECT command_id AS \"Value\" FROM cp6_approval_intake_v2.commands WHERE tenant_id={h.Tenant}").ToArrayAsync());
        Assert.Empty(await h.Db.Database.SqlQuery<Guid>($"SELECT submission_id AS \"Value\" FROM cp6_approval_intake_v2.start_outbox WHERE tenant_id={h.Tenant}").ToArrayAsync());
    }
    [Fact]
    public void App_intake_rules_keep_large_decimal_precision_and_errors_do_not_become_fallback()
    {
        using var snapshot=JsonDocument.Parse("{\"amount\":\"999999999999999999999999999999.99999999\",\"purpose\":\"TOOL_DISCARD\"}");
        Assert.True(ApprovalExpressionV2.Evaluate("amount > 999999999999999999999999999999.99999998 && purpose == 'TOOL_DISCARD'",snapshot.RootElement,["amount","purpose"]));
        Assert.True(ApprovalExpressionV2.Evaluate("(1 / 3) * 3 == 1",snapshot.RootElement,[]));
        Assert.False(ApprovalExpressionV2.Evaluate("amount < 1",snapshot.RootElement,["amount"]));
        foreach(var expr in new[]{"missing > 0","1 / 0 > 2","purpose > 0","purpose", "sum(1,2"})Assert.Throws<ScopeAuthorizationExceptionV1>(()=>ApprovalExpressionV2.Evaluate(expr,snapshot.RootElement,["amount","purpose"]));
    }
}
