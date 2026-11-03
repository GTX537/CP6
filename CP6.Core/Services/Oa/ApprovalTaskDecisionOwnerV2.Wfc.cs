using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Core.Services.Sys;
using CP6.Entity.Contracts.Wire;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AppTaskDecisionInputV2(string Kind,Guid TaskId,string Decision,string Comment,string ExpectedTaskVersion,string ExpectedInstanceVersion);
public sealed partial class ApprovalTaskDecisionOwnerV2
{
    private sealed record WfcAlias(byte[] Raw,string Digest);
    // This boundary preserves the accepted APP GUID domain and five-field canonical request.
    // WFC is transport metadata on that same AppCommand, never a second completion authority.
    public Task<ApprovalTaskDecisionResultV2> DecideWfcAsync(Guid task,byte[] raw,Guid? behalf=null,CancellationToken ct=default)
    {
        try
        {
            using var doc=WorkflowCommandJsonV1.Parse(raw);var root=doc.RootElement;
            if(root.ValueKind!=JsonValueKind.Object||root.EnumerateObject().Any(p=>p.Name is not("commandId" or "expectedVersion" or "body" or "clientCorrelationId")))throw new JsonException();
            var id=root.GetProperty("commandId").GetString();
            if(!Guid.TryParseExact(id,"D",out var command)||command==Guid.Empty||id!=command.ToString("D"))throw Error("APP_COMMAND_ID_INVALID",422);
            var body=root.GetProperty("body");var input=body.Deserialize<AppTaskDecisionInputV2>(PersonalFormOwnerV2.Json)??throw new JsonException();
            if(input.Kind!="APP"||input.TaskId!=task||task==Guid.Empty||input.Decision is not("APPROVE" or "REJECT"))throw new JsonException();
            var expected=root.TryGetProperty("expectedVersion",out var version)?version.GetString():null;
            if(expected==null||input.ExpectedTaskVersion==null||input.ExpectedInstanceVersion==null)throw Error("PRECONDITION_REQUIRED",428);
            if(expected!=input.ExpectedTaskVersion)throw Error("PRECONDITION_MISMATCH",400);
            if(root.TryGetProperty("clientCorrelationId",out var correlation)&&correlation.GetString()?.Length>100)throw new JsonException();
            var accepted=new ApprovalTaskDecisionInputV2(command,input.Decision=="APPROVE"?"approve":"reject",input.Comment,input.ExpectedTaskVersion,input.ExpectedInstanceVersion);
            return DecideCore(task,accepted,behalf,"\""+expected+"\"",new(raw.ToArray(),WorkflowCommandJsonV1.Digest(body,expected)),ct);
        }
        catch(Exception e)when(e is not ScopeAuthorizationExceptionV1 && e is JsonException or InvalidOperationException or KeyNotFoundException){throw Error("COMMAND_SHAPE_INVALID",400);}
    }
    public async Task<ApprovalTaskCommandResolutionV2> DecisionOperationAsync(Guid task,Guid command,CancellationToken ct=default)
    {
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var fence=await scope.AcquireAsync(ct);await Ready(ct);
        var actor=await authority.CurrentActor(false,ct);var old=await Original(command,ct);ApprovalTaskCommandResolutionV2 result;
        if(old!=null)
        {
            if(old.Task!=task)throw Error("RESOURCE_NOT_FOUND",404);
            result=new(command,"COMPLETED",await Disclose(old,actor,ct));
        }
        else
        {
            await Business.Decision(ApprovalSubmissionWireV2.Parse((await ReadSource(task,ct)).Payload).Value,null,false,ct);
            var closed=await Closed(command,ct);
            if(closed!=null&&(closed.Actor!=actor||closed.Task!=task))throw Error("RESOURCE_NOT_FOUND",404);
            result=new(command,closed!=null?"CLOSED":"NOT_OBSERVED");
        }
        await scope.ValidateAsync(fence,ct);await tx.CommitAsync(ct);return result;
    }
}
