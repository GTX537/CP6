using System.Text.Json.Serialization;

namespace CP6.Core.Services.Sys;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MenuTreePositionV1([property: JsonRequired] int MenuId,
    [property: JsonRequired] int? ParentId, [property: JsonRequired] int OrderNo);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MenuTreeCommandV1([property: JsonRequired] string OperationRef,
    [property: JsonRequired, JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] long ExpectedRevision,
    [property: JsonRequired] MenuTreePositionV1[] Positions, [property: JsonRequired] string Reason);
public sealed record MenuTreeNodeV1(int MenuId, string MenuName, string? RoutePath, string? MenuKey, string? Icon,
    int? ParentId, int OrderNo, bool Enable, DateTime CreateDate);
public sealed record MenuTreeSnapshotV1(
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] long Revision,
    MenuTreeNodeV1[] Menus);
public sealed record MenuTreeImpactV1(
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] long Revision,
    int[] ChangedMenuIds, int AffectedTenantsCount, int AffectedRolesCount,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] long MenuReferences,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] long ActionReferences);
public sealed record MenuTreeReceiptV1(string OperationRef,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] long AppliedRevision,
    MenuTreeImpactV1 Impact, DateTimeOffset CommittedAt);
public sealed class MenuTreeExceptionV1(string code, int status = 409, Exception? inner = null) : InvalidOperationException(code, inner)
{
    public int Status { get; } = status;
}
