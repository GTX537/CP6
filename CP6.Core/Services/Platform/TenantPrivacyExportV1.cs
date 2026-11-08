using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
namespace CP6.Core.Services.Platform;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TenantPrivacyExportCommandV1([property:JsonRequired] Guid OperationId,[property:JsonRequired] Guid RequestRef,
    [property:JsonRequired] Guid TargetTenantId,[property:JsonRequired][Required] string Purpose,[property:JsonRequired][Required] string[] Sources,
    [property:JsonRequired] Guid RecipientId,[property:JsonRequired][Required] string PolicyVersion,[property:JsonRequired] int AccessMinutes);
public sealed record TenantPrivacyExportManifestV1(string Version,Guid TenantId,string State,bool Complete,string DirectoryCoverage,string Consistency,SubjectExportSourceV1[] Sources);
public sealed record TenantPrivacyExportContextV1(Guid EnvironmentId,Guid ActorTenantId,Guid ActorId,string Owner,string PolicyVersion,string[] Purposes,string[] Sources,string[] Actions,int MaximumAccessMinutes);
public sealed record TenantPrivacyExportReceiptV1(Guid OperationId,Guid RequestRef,Guid ArtifactId,Guid EnvironmentId,Guid RequesterTenantId,Guid RequesterId,Guid OperatorId,
    Guid RecipientId,Guid TargetTenantId,string Purpose,string[] Sources,string PolicyVersion,DateTimeOffset CapturedAt,DateTimeOffset ExpiresAt,long Length,string Sha256,TenantPrivacyExportManifestV1 Manifest);
public sealed record TenantPrivacyExportOperationV1(Guid OperationId,string State,TenantPrivacyExportReceiptV1? Receipt);
public sealed record TenantPrivacyExportArtifactV1(TenantPrivacyExportReceiptV1 Receipt,string Availability);
