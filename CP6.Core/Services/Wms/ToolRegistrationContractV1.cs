using System.Text.Json.Serialization;
using CP6.Core.Services.Plm;
namespace CP6.Core.Services.Wms;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ToolNewInstanceV1(string PlateType,int? ColorCount=null,string? SizeNote=null,string? MadeDate=null,
    string? MadeCost=null,int? MaxShots=null,string? Remarks=null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ToolRegistrationCommandV1(Guid OperationId,Guid ObjectId,string Kind,EngineeringSourceRefV1 Site,string Evidence,
    ToolInstanceRefV1? Expected=null,string? CreationAuthorityVersion=null,Guid? DepartmentId=null,ToolNewInstanceV1? Profile=null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ToolRegistrationRefV1(string Owner,Guid Id,string Version,string Digest);
public sealed record ToolRegistrationViewV1(Guid TenantId,ToolRegistrationRefV1 Reference,ToolInstanceRefV1 CurrentNative,
    EngineeringSourceRefV1 SiteAtRegistration,EngineeringSiteChoiceV1 CurrentSite,Guid CycleAnchorId,string Origin,
    string InitialCycleShots,string? CycleNo,string CycleKnowledge,string? CurrentCycleShots,string LifetimeKnowledge,string? LifetimeShots,
    string OriginalJson,string CurrentEligibility="UNKNOWN",string InputContract="tool.registration.v1",ToolRegistrationLinkViewV1? Links=null,string ActualKnowledge="NO_RECORDED_ACTUAL_RESTRICTION",bool NewUseBlocked=false);
public sealed record ToolRegistrationResultV1(Guid OperationId,string Kind,Guid ObjectId,string Version,string Digest,string InputContract="tool.registration.v1");
public sealed record ToolRegistrationOperationV1(Guid OperationId,string State,ToolRegistrationResultV1? Result=null);
internal sealed record ToolRegistrationOriginalV1(Guid TenantId,Guid ObjectId,Guid ActorId,string Origin,Guid CycleAnchorId,
    string? CycleNo,string InitialCycleShots,string LifetimeKnowledge,string? LifetimeShots,ToolInstanceRefV1 NativeAtRegistration,
    EngineeringSourceRefV1 Site,string Evidence,string InputContract="tool.registration.v1");

public sealed record ToolRegistrationCreationContextV1(Guid TenantId,Guid ActorId,string AuthorityVersion,bool AllowsUnbound,EngineeringProductDepartmentV1[] Departments,string[] WritableFields);
