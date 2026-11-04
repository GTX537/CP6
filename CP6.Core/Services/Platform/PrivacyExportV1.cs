using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace CP6.Core.Services.Platform;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PrivacyExportCommandV1(
    [property: JsonRequired] Guid OperationId, [property: JsonRequired] Guid RequestRef,
    [property: JsonRequired] Guid TargetTenantId, [property: JsonRequired] Guid SubjectId,
    [property: JsonRequired][Required] string Purpose, [property: JsonRequired][Required] string[] Sources,
    [property: JsonRequired] Guid RecipientId, [property: JsonRequired][Required] string PolicyVersion,
    [property: JsonRequired] int AccessMinutes);
public sealed record PrivacyExportContextV1(Guid EnvironmentId, Guid ActorTenantId, Guid ActorId,
    string PolicyVersion, string[] Purposes, string[] Sources, string[] Actions, int MaximumAccessMinutes);
public sealed record PrivacyExportReceiptV1(Guid OperationId, Guid RequestRef, Guid ArtifactId,
    Guid EnvironmentId, Guid RequesterTenantId, Guid RequesterId, Guid OperatorId, Guid RecipientId,
    Guid TargetTenantId, Guid SubjectId, string Purpose, string[] Sources, string PolicyVersion,
    DateTimeOffset CapturedAt, DateTimeOffset ExpiresAt, long Length, string Sha256, SubjectExportManifestV1 Manifest);
public sealed record PrivacyExportOperationV1(Guid OperationId, string State, PrivacyExportReceiptV1? Receipt);
public sealed record PrivacyExportArtifactV1(PrivacyExportReceiptV1 Receipt, string Availability);
public sealed record PrivacyExportDownloadV1(byte[] Bytes, Guid ArtifactId, DateTimeOffset ExpiresAt);
public sealed class PrivacyExportExceptionV1(string code, int status=409, Exception? inner=null) : InvalidOperationException(code,inner)
{ public int Status { get; }=status; }
