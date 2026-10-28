using System.Text.Json.Serialization;
using CP6.Core.Services.Plm;

namespace CP6.Core.Services.Wms;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LocationHierarchyEdgeV1(int ParentLevel,int ChildLevel);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LocationHierarchyCommandV1(Guid OperationId,string ExpectedVersion,string ExpectedHierarchyRevision,
    [property:JsonRequired]bool Enabled,int[] RootLevels,LocationHierarchyEdgeV1[] Edges,string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LocationHierarchyPrecheckV1(string ExpectedVersion,string ExpectedHierarchyRevision,
    [property:JsonRequired]Guid? ParentLocationId,[property:JsonRequired]int LocationLevel);
public sealed record LocationHierarchyOriginalV1(Guid TenantId,Guid Id,Guid WarehouseId,string Version,Guid OperationId,
    Guid ActorId,DateTimeOffset RecordedAt,bool Enabled,int[] RootLevels,LocationHierarchyEdgeV1[] Edges,string Reason);
public sealed record LocationHierarchyPolicyV1(EngineeringSourceRefV1 Reference,LocationHierarchyOriginalV1 Original,string OriginalJson);
public sealed record LocationHierarchyContextV1(Guid TenantId,Guid ActorId,Guid WarehouseId,string WarehouseCode,
    string Version,string HierarchyRevision,LocationHierarchyPolicyV1? Policy,string Compatibility)
{
    public bool CreationGrant=>false;
    public string InitializationFence=>"UNAVAILABLE";
}
public sealed record LocationHierarchyResultV1(Guid OperationId,Guid WarehouseId,EngineeringSourceRefV1 Reference,bool Changed);
public sealed record LocationHierarchyOperationV1(Guid OperationId,string State,LocationHierarchyResultV1? Result=null);
public sealed record LocationHierarchyCheckV1(Guid WarehouseId,Guid? ParentLocationId,int LocationLevel,
    EngineeringSourceRefV1 PolicyRef,string HierarchyRevision)
{
    public bool StructureValid=>true;
    public bool CreationGrant=>false;
    public string InitializationFence=>"UNAVAILABLE";
    public bool RequiresFinalRecheck=>true;
}
