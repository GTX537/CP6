using System.Text.Json.Serialization;
using CP6.Core.Services.Plm;
namespace CP6.Core.Services.Wms;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ToolDispositionRefV1(string Owner, Guid Id, string Version, string Digest);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ToolDispositionRequestV1(Guid OperationId, string ReasonCode, string Reason, string ExpectedObligationVersion);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ToolDispositionBlockV1(Guid OperationId, ToolDispositionRefV1 Request, string Reason, string ExpectedObligationVersion);
public sealed record ToolDispositionObligationV1(string Owner, string Id, string Version, string Digest, string Kind, string State);
public sealed record ToolDispositionObligationsV1(string Version, ToolDispositionObligationV1[] Local, string[] UnknownOwners, bool CanFinalize = false);
public sealed record ToolDispositionFactV1(Guid TenantId, Guid ToolId, Guid RecordId, string Kind, Guid ActorId,
    DateTimeOffset RecordedAt, ToolInstanceRefV1 Native, EngineeringSourceRefV1 Site, ToolMaintenanceAuthorityV1[] Authorities,
    string ReasonCode, string Reason, ToolDispositionRefV1? Request, ToolDispositionObligationsV1 Obligations);
public sealed record ToolDispositionFactViewV1(ToolDispositionRefV1 Reference, ToolDispositionFactV1 Fact);
public sealed record ToolDispositionContextV1(Guid TenantId, Guid ToolId, string Etag, ToolInstanceRefV1 Native,
    EngineeringSourceRefV1 Site, string State, bool NewUseBlocked, ToolDispositionFactViewV1? Request,
    ToolDispositionFactViewV1? Block, ToolDispositionObligationsV1 Obligations, string ApprovalKnowledge = "NOT_CONNECTED",
    bool IsDiscarded = false, bool IsUsePermit = false);
public sealed record ToolDispositionResultV1(Guid OperationId, Guid ObjectId, string Kind, ToolDispositionRefV1 Fact,
    string Outcome, bool IsDiscarded = false, bool IsUsePermit = false);
public sealed record ToolDispositionOperationV1(Guid OperationId, string State, ToolDispositionResultV1? Result = null);
