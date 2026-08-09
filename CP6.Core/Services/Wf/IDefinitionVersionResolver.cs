using CP6.Entity.DomainModels.Wf;

namespace CP6.Core.Services.Wf;

public sealed record FlowVersionResolution(Wf_FlowDef Head, Wf_FlowDefVersion Version, bool LegacyFallback = false);
public sealed record FormVersionResolution(Wf_FormDef Head, Wf_FormDefVersion? Version, bool LegacyFallback = false);

public interface IDefinitionVersionResolver
{
    Task<FlowVersionResolution> ResolveLatestFlowAsync(string flowKey, bool validateDependencies = true, CancellationToken ct = default);
    Task<FlowVersionResolution> ResolveFlowAsync(Guid versionId, CancellationToken ct = default);
    Task<FormVersionResolution> ResolveLatestFormAsync(string formKey, CancellationToken ct = default);
    Task<FormVersionResolution> ResolveFormAsync(Guid? versionId, string? legacyFormKey = null, int? legacyVersion = null, CancellationToken ct = default);
}
