using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;

namespace CP6.Core.Services.Oa;

public sealed record RegisteredConnectorVersionV1(ConnectorPinV1 Pin, string Implementation, string ImplementationVersion,
    string DestinationPolicy, string EndpointProfile, string InputSchema, string OutputSchema, string DisclosurePolicy,
    string? CredentialLocator, string? CredentialGeneration, string EffectClass, int MaxCallDurationSec, bool Simulated,
    Guid TenantId, Guid EnvironmentId, Guid ProfileId, string AdoptionEvidence, string ParameterMapping,
    string? IdempotencyContract, string? ResultQueryContract, string? AdmissionBasis, bool? RequiresCurrentAuthorizationAtApply);

// 各窗口与远端门必须属于该登记的 operation。只有已接入且能核验的能力才可声明。
public sealed record ConnectorOperationGuaranteesV1(TimeSpan? IdempotencyRetention, TimeSpan? OriginalQueryRetention,
    bool ChecksCurrentAuthorizationAtApply);

public sealed record ConnectorDispatchIdentityV1(Guid TenantId, Guid EnvironmentId, Guid JobId, Guid InstanceId,
    Guid TokenId, string NodeId, Guid FlowVersionId, ConnectorPinV1 Pin);

public sealed record ConnectorDispatchSourceV1(ConnectorDispatchIdentityV1 Identity, Guid StarterId,
    ScopeAuthorizationSnapshotV1 Authority, string VarsJson);

public sealed record ConnectorDispatchQualificationV1(string ServiceRegistration, string DisclosureDecision, string? OwnerDecision,
    bool AllowsUncertainFirstCall, ReadOnlyMemory<byte> MappedInput);

public sealed record ConnectorOriginalOperationV1(ConnectorDispatchIdentityV1 Identity,
    string EffectKey, string TransportKey, string InputSha256, Guid StarterId);

public sealed record ConnectorDispatchRequestV1(ConnectorOriginalOperationV1 Original, ReadOnlyMemory<byte> FrozenInput,
    ScopeAuthorizationSnapshotV1 Authority, string ServiceRegistration, string DisclosureDecision, string? OwnerDecision);

public enum ConnectorTransportStateV1
{
    Unknown = 0,
    Acknowledged,
    NotObserved,
    OriginalOperationObserved,
}

// 原操作查证须核对提供方回传的操作身份与输入摘要，再交回该合同。凭据与响应正文留在提供方边界。
public sealed record ConnectorTransportObservationV1(ConnectorTransportStateV1 State, string? EffectKey = null,
    string? TransportKey = null, string? InputSha256 = null, string? OriginalProofRef = null);

/// <summary>只注册信任的精确版本参与者。准入及查询授权参加原 WF_AUTH 事务，且只允许本地验证。
/// 发送与原操作查证在事务提交后执行，分别只允许一次提交与只读查询。提供方自行核验端点、合同和响应来源。</summary>
public interface IConnectorDispatchAdmissionV1
{
    bool IsSimulation => false;
    bool Matches(RegisteredConnectorVersionV1 version);
    ConnectorOperationGuaranteesV1 Guarantees(RegisteredConnectorVersionV1 version);
    Task<ConnectorDispatchQualificationV1?> PrepareAsync(CP6Context context, RegisteredConnectorVersionV1 version,
        ConnectorDispatchSourceV1 source, CancellationToken ct);
    Task<bool> MayQueryOriginalAsync(CP6Context context, RegisteredConnectorVersionV1 version,
        ConnectorOriginalOperationV1 original, ScopeAuthorizationSnapshotV1 authority, CancellationToken ct);
    Task<ConnectorTransportObservationV1> SendAsync(RegisteredConnectorVersionV1 version,
        ConnectorDispatchRequestV1 request, CancellationToken ct);
    Task<ConnectorTransportObservationV1> QueryOriginalAsync(RegisteredConnectorVersionV1 version,
        ConnectorOriginalOperationV1 original, CancellationToken ct);
}
