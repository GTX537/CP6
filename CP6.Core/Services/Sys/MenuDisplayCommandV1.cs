using System.Text.Json.Serialization;

namespace CP6.Core.Services.Sys;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MenuDisplayCommandV1([property: JsonRequired] string OperationRef,
    [property: JsonRequired, JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] long ExpectedRevision,
    [property: JsonRequired] int MenuId, [property: JsonRequired] string MenuName,
    [property: JsonRequired] string? Icon, [property: JsonRequired] bool Enable, [property: JsonRequired] string Reason);
