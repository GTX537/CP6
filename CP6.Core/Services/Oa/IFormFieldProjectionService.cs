namespace CP6.Core.Services.Oa;

public sealed record ProjectedForm(
    Guid? FormDataId,
    string? FormKey,
    int? FormVersion,
    string SchemaJson,
    string DataJson,
    IReadOnlyDictionary<string, string> FieldMask,
    bool LegacyFallback);

public interface IFormFieldProjectionService
{
    Task<ProjectedForm> ProjectAsync(
        Guid instanceId, Guid viewerId, string dataJson, CancellationToken ct = default);

    Task<IReadOnlyDictionary<string, string>> DecisionMaskAsync(
        Guid instanceId, string nodeId, string dataJson, CancellationToken ct = default);
}
