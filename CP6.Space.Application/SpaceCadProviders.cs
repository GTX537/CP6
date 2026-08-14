using CP6.Space.Contracts;
using CP6.Space.Domain;

namespace CP6.Space.Application;

public sealed class SpaceCadProviderRegistration
{
    public SpaceCadProviderRegistration(
        string providerKey,
        string providerVersion,
        string displayName,
        SpaceCadProviderDeploymentMode deploymentMode,
        SpaceCadProviderDataBoundary dataBoundary,
        bool supportsDwg,
        bool supportsDxf,
        ISpaceCadPreparationProvider preparationProvider,
        ISpaceCadParseProvider parseProvider)
    {
        ProviderKey = SpaceCadProviderKey.Normalize(providerKey);
        ProviderVersion = SpaceCadProviderVersion.Normalize(providerVersion);
        DisplayName = RequireDisplayName(displayName);
        if (!Enum.IsDefined(deploymentMode) || !Enum.IsDefined(dataBoundary))
            throw new ArgumentOutOfRangeException(nameof(deploymentMode));
        if (!supportsDwg && !supportsDxf)
            throw new ArgumentException("At least one CAD format must be supported.");
        DeploymentMode = deploymentMode;
        DataBoundary = dataBoundary;
        SupportsDwg = supportsDwg;
        SupportsDxf = supportsDxf;
        PreparationProvider = preparationProvider ??
            throw new ArgumentNullException(nameof(preparationProvider));
        ParseProvider = parseProvider ??
            throw new ArgumentNullException(nameof(parseProvider));
    }

    public string ProviderKey { get; }
    public string ProviderVersion { get; }
    public string DisplayName { get; }
    public SpaceCadProviderDeploymentMode DeploymentMode { get; }
    public SpaceCadProviderDataBoundary DataBoundary { get; }
    public bool SupportsDwg { get; }
    public bool SupportsDxf { get; }
    public ISpaceCadPreparationProvider PreparationProvider { get; }
    public ISpaceCadParseProvider ParseProvider { get; }

    private static string RequireDisplayName(string value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 120)
            throw new ArgumentException("A bounded Provider display name is required.", nameof(value));
        return normalized;
    }
}

public interface ISpaceCadProviderRegistry
{
    IReadOnlyList<SpaceCadProviderRegistration> Registrations { get; }
    bool TryGet(string providerKey, out SpaceCadProviderRegistration? registration);
}

public sealed class SpaceCadProviderRegistry : ISpaceCadProviderRegistry
{
    private readonly IReadOnlyDictionary<string, SpaceCadProviderRegistration> _providers;

    public SpaceCadProviderRegistry(IEnumerable<SpaceCadProviderRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        var values = new Dictionary<string, SpaceCadProviderRegistration>(StringComparer.Ordinal);
        foreach (var registration in registrations)
        {
            if (!values.TryAdd(registration.ProviderKey, registration))
                throw new InvalidOperationException(
                    $"Duplicate CAD Provider key '{registration.ProviderKey}'.");
        }
        _providers = values;
    }

    public IReadOnlyList<SpaceCadProviderRegistration> Registrations =>
        _providers.Values.OrderBy(item => item.ProviderKey, StringComparer.Ordinal).ToArray();

    public bool TryGet(string providerKey, out SpaceCadProviderRegistration? registration)
    {
        registration = null;
        if (string.IsNullOrWhiteSpace(providerKey))
            return false;
        return _providers.TryGetValue(providerKey.Trim(), out registration);
    }
}

public interface ISpaceCadProviderCapabilityService
{
    Task<SpaceCadSiteCapabilityDto> GetAsync(
        Guid siteId,
        CancellationToken cancellationToken = default);

    Task<ReplaceSpaceCadProviderConfigurationResponse> ReplaceAsync(
        Guid siteId,
        ReplaceSpaceCadProviderConfigurationRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default);
}
