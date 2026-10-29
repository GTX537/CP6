using System.Text.Json.Serialization;
using CP6.Core.Services.Plm;
namespace CP6.Core.Services.Wms;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LocationBlockCommandV1(Guid OperationId,string Reason,string BlockScope);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LocationReleaseDecisionCommandV1(Guid OperationId,Guid BlockId,string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LocationBlockReleaseCommandV1(Guid OperationId,Guid BlockId,string Reason,EngineeringSourceRefV1 DecisionRef);
public sealed record LocationControlOriginalV1(Guid TenantId,Guid LocationId,Guid Id,Guid BlockId,string Kind,
    Guid OperationId,Guid ActorId,DateTimeOffset RecordedAt,string Reason,string ControlBefore,string NativeEtag,
    EngineeringSourceRefV1? DecisionRef);
public sealed record LocationControlEntryV1(EngineeringSourceRefV1 Reference,LocationControlOriginalV1 Original,string State);
public sealed record LocationControlViewV1(Guid TenantId,Guid ActorId,Guid LocationId,string Etag,string ControlRevision,
    bool IsBlocked,bool InitializationQuarantined,bool OtherRestrictionRetained,int OpenBlocks,LocationControlEntryV1[] Entries,int Page)
{
    public string Qualification=>"NATIVE_LOCAL_CONTROL_ONLY";
    public bool AdmissionGrant=>false;
    public string ReleaseBasisProfile=>"CURRENT_AUTHORIZED_LOCAL_OPERATOR_DECISION_V1";
}
public sealed record LocationControlResultV1(Guid OperationId,Guid LocationId,EngineeringSourceRefV1 Reference,
    Guid BlockId,string Effect,string Etag,string ControlRevision,bool IsBlocked);
public sealed record LocationControlOperationV1(Guid OperationId,Guid LocationId,string State,LocationControlResultV1? Result=null);
