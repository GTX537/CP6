namespace CP6.Core.Services.Wf;

public interface IFlowFormCompatibilityValidator
{
    Task ValidateFlowPublishAsync(Guid flowDefId, string flowSchemaJson, CancellationToken ct = default);
    Task ValidateFormPublishAsync(Guid formDefId, string formSchemaJson, CancellationToken ct = default);
    Task ValidateBindingAsync(Guid formDefId, Guid flowDefId, CancellationToken ct = default);
}
