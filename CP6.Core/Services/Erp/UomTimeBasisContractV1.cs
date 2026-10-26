using System.Text.Json.Serialization;
namespace CP6.Core.Services.Erp;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UomTimeBasisRefV1(string Owner, Guid Id, string Version, string Digest);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UomTimeBasisDefinitionV1(UomProfileRefV1 BaseRef, string Basis, [property: JsonRequired] bool Enabled, string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UomTimeBasisCommandV1(Guid OperationId, string ExpectedVersion, UomTimeBasisDefinitionV1 Definition);
public sealed record UomTimeBasisVersionV1(Guid TenantId, UomTimeBasisRefV1 Reference, bool IsCurrent,
    UomTimeBasisDefinitionV1 Definition, string OriginalJson, bool CurrentApplicable = false, string[]? Reasons = null);
public sealed record UomTimeBasisEditorV1(UomProfileVersionV1 CurrentBase, string ExpectedVersion, UomTimeBasisVersionV1? Definition);
internal sealed record UomTimeBasisStoredV1(Guid TenantId, Guid ProfileId, string Version, UomTimeBasisDefinitionV1 Definition);
