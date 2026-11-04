using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Core.Services.Integration;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;

namespace CP6.Core.Services.Plm;

public sealed record DocumentAvailabilityV1(string StateVersion,Guid Etag,string State,string Action,string Reason,
    Guid ActorId,DateTimeOffset RecordedAt,Guid? OperationId);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DocumentAvailabilityCommandV1(Guid OperationId,EngineeringSourceRefV1 Document,string Action,
    string ExpectedStateVersion,Guid ExpectedEtag,string Reason);

public sealed partial class ControlledDocumentOwnerV1
{
    private static void RequireAvailable(ControlledDocumentSnapshotV1 value)
    {
        if(value.Availability!.State!="AVAILABLE")throw Error("DOCUMENT_NOT_AVAILABLE",409);
    }
    public Task<DocumentAvailabilityV1[]> AvailabilityHistoryAsync(Guid id,string version,CancellationToken ct=default,string? before=null)
    {
        if(id==Guid.Empty)throw Error("DOCUMENT_ID_REQUIRED",400);var revision=Revision(version);var cursor=before==null?(long?)null:Revision(before);
        return Authority.ExecuteAsync(OperationsOwnerV1.ControlledDocument,"query",false,async(a,t)=>{
            var store=new ControlledDocumentStoreV1(db);await store.Fence(false,t);
            await Observe(a,await store.Read(id,revision,t),t);
            return await store.AvailabilityHistory(id,revision,cursor,t);
        },ct);
    }
    public Task<ControlledDocumentResultV1> AvailabilityAsync(DocumentAvailabilityCommandV1 input,CancellationToken ct=default)
    {
        if(input==null || input.OperationId==Guid.Empty || input.ExpectedEtag==Guid.Empty
            || input.Action is not ("HOLD" or "RELEASE" or "WITHDRAW"))throw Error("DOCUMENT_AVAILABILITY_COMMAND_INVALID",400);
        ControlledDocumentValuesV1.Ref(input.Document);Revision(input.ExpectedStateVersion);ControlledDocumentValuesV1.Text(input.Reason);
        // The boundary DTO and its exact reference contain only immutable values.
        var command=input;var body=MainOwnerOperationJournalV1.Encode(command);
        var journal=new MainOwnerOperationJournalV1(db);MainOwnerOperationJournalV1.Original? original=null;
        return Authority.ExecuteAsync(OperationsOwnerV1.ControlledDocument,"availability",true,async(a,t)=>{
            var store=new ControlledDocumentStoreV1(db);await store.Fence(true,t);
            if(original!=null)return await Disclose(a,store,original,command.OperationId,body,t);
            a.RequireDocumentFields(true);
            var document=await Observe(a,await store.Read(command.Document.Id,Revision(command.Document.Version),t),t);
            if(document.Reference!=command.Document)throw Error("DOCUMENT_REFERENCE_CHANGED",412);
            var current=document.Availability!;
            if(current.StateVersion!=command.ExpectedStateVersion || current.Etag!=command.ExpectedEtag)throw Error("DOCUMENT_AVAILABILITY_CHANGED",412);
            var state=(current.State,command.Action) switch {
                ("AVAILABLE","HOLD")=>"HELD",("HELD","RELEASE")=>"AVAILABLE",
                ("AVAILABLE" or "HELD","WITHDRAW")=>"WITHDRAWN",_=>throw Error("DOCUMENT_AVAILABILITY_TRANSITION_INVALID",409)};
            // A historical revision remains controllable under current document and parent authority.
            var parent=await a.ActionAsync(OperationsOwnerV1.EngineeringRevision,"edit",t);
            await parent.RequireEngineeringRevision(document.Body.RevisionOrigin.Id,t);
            parent.AtCommit(token=>parent.RequireEngineeringRevision(document.Body.RevisionOrigin.Id,token));
            if(state=="AVAILABLE")foreach(var member in document.Body.Contents)
                await files.BoundSource(a,member.File,document.Body.RevisionOrigin.Id,true,t);
            var availability=await store.AppendAvailability(command,a.ActorId,state,t);
            var result=new ControlledDocumentResultV1(command.OperationId,"CONTROLLED_DOCUMENT",document.Reference.Id,
                document.Reference.Version,document.Reference.Digest,availability);
            await journal.Store(ControlledDocumentValuesV1.Owner,Contract,command.OperationId,a.ActorId,body,result,[document.Reference.Id],
                new{command.Document,command.Action,command.Reason,a.ActorId,before=current,after=availability},t);
            return result;
        },ct,actionAtFence:async t=>{
            original=await journal.Read(ControlledDocumentValuesV1.Owner,command.OperationId,t);return original==null?"availability":"query";
        });
    }
}
