using System.Text.Json.Serialization;
using CP6.Core.Services.Plm;

namespace CP6.Core.Services.Wms.Transfer;

// Deployment configuration, never bound from a request or inferred from a simulation flag.
public sealed class TransferEnvironmentOptionsV1
{
    public string Mode { get; set; } = "";
    public Guid? SandboxScopeId { get; set; }
}
public sealed record TransferDeploymentV1(Guid EnvironmentId, string Mode, Guid? SandboxScopeId);
public sealed record TransferExecutionEnvironmentV1(Guid EnvironmentId, string Mode, Guid? SandboxScopeId, string RegistryRevision);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TransferEnvironmentCommandV1(Guid OperationId, string ExpectedVersion, [property: JsonRequired] bool Enabled, string Reason);
public sealed record TransferEnvironmentOriginalV1(Guid TenantId, Guid Id, string Version, Guid OperationId, Guid ActorId,
    DateTimeOffset RecordedAt, TransferDeploymentV1 Deployment, bool Enabled, string Profile, string[] Owners, string Reason);
public sealed record TransferEnvironmentRegistrationV1(EngineeringSourceRefV1 Reference, TransferEnvironmentOriginalV1 Original, string OriginalJson);
public sealed record TransferEnvironmentContextV1(Guid TenantId, Guid ActorId, TransferDeploymentV1 Deployment,
    string Version, TransferEnvironmentRegistrationV1? Registration, string[] AvailableOwners,
    string Qualification = "NATIVE_SOURCE_REGISTRATION_ONLY");
public sealed record TransferEnvironmentResultV1(Guid OperationId, EngineeringSourceRefV1 Reference, bool Enabled, bool Changed);
public sealed record TransferEnvironmentOperationV1(Guid OperationId, string State, TransferEnvironmentResultV1? Result = null);
public sealed record TransferProductSourceV1(TransferExecutionEnvironmentV1 Environment, EngineeringSourceRefV1 RegistrationRef,
    InventoryIdentityRefV1 CatalogueRef, InventoryProductOptionV1 Product, string Qualification = "REQUIRES_FINAL_OWNER_GATES");
