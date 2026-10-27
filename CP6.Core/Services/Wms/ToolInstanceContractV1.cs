using System.Text.Json;
using System.Text.Json.Serialization;
namespace CP6.Core.Services.Wms;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ToolInstanceRefV1(string Owner,Guid Id,string Version,string Digest);
public sealed record ToolInstanceViewV1(Guid TenantId,ToolInstanceRefV1 Reference,bool IsCurrent,string Etag,string ObservedNativeRowVersion,
    DateTimeOffset ObservedAt,JsonElement Source,string[] ReadableFields,string[] WritableFields,
    string LifetimeKnowledge="UNKNOWN",string? LifetimeShots=null,string? CycleNo=null,Guid? SiteId=null,
    string SiteKnowledge="LEGACY_UNMAPPED",string CurrentEligibility="UNKNOWN",string EngineeringLinkKnowledge="LEGACY_UNMAPPED");
public sealed record ToolInstancePageV1(ToolInstanceViewV1[] Items,int Total,int Page,int PageSize);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ToolInstancePatchV1(string Field,string? Value);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ToolInstanceCommandV1(Guid OperationId,ToolInstanceRefV1 Expected,ToolInstancePatchV1[] Patches);
public sealed record ToolInstanceResultV1(Guid OperationId,string Kind,Guid ObjectId,string Version,string Digest,bool Changed);
public sealed record ToolInstanceOperationV1(Guid OperationId,string State,ToolInstanceResultV1? Result=null);
