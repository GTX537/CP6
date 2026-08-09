using CP6.Space.Contracts;
using CP6.Space.Domain;

namespace CP6.Space.Application;

public sealed class SpaceAiTenantPolicy
{
    public const int PlatformMaxConcurrentRuns = 3;

    private SpaceAiTenantPolicy(
        Guid tenantId,
        SpaceAiDataPolicy dataPolicy,
        IReadOnlyList<Guid> allowedSiteIds,
        IReadOnlyList<string> allowedProviderAliases,
        int maxConcurrentRuns,
        bool externalProviderEnabled,
        SpaceAiBudgetLimits budgetLimits)
    {
        TenantId = tenantId;
        DataPolicy = dataPolicy;
        AllowedSiteIds = allowedSiteIds;
        AllowedProviderAliases = allowedProviderAliases;
        MaxConcurrentRuns = maxConcurrentRuns;
        ExternalProviderEnabled = externalProviderEnabled;
        BudgetLimits = budgetLimits;
    }

    public Guid TenantId { get; }
    public SpaceAiDataPolicy DataPolicy { get; }
    public IReadOnlyList<Guid> AllowedSiteIds { get; }
    public IReadOnlyList<string> AllowedProviderAliases { get; }
    public int MaxConcurrentRuns { get; }
    public bool ExternalProviderEnabled { get; }
    public SpaceAiBudgetLimits BudgetLimits { get; }
    public bool IsEnabled => DataPolicy != SpaceAiDataPolicy.Disabled;

    public static SpaceAiTenantPolicy Disabled(Guid tenantId)
    {
        EnsureTenant(tenantId);
        return new SpaceAiTenantPolicy(
            tenantId,
            SpaceAiDataPolicy.Disabled,
            [],
            [],
            PlatformMaxConcurrentRuns,
            false,
            SpaceAiBudgetLimits.Unpriced);
    }

    public static SpaceAiTenantPolicy Enabled(
        Guid tenantId,
        SpaceAiDataPolicy dataPolicy,
        IEnumerable<Guid> allowedSiteIds,
        IEnumerable<string> allowedProviderAliases,
        int maxConcurrentRuns = PlatformMaxConcurrentRuns,
        bool externalProviderEnabled = false,
        SpaceAiBudgetLimits? budgetLimits = null)
    {
        EnsureTenant(tenantId);
        if (dataPolicy == SpaceAiDataPolicy.Disabled)
        {
            throw new ArgumentOutOfRangeException(
                nameof(dataPolicy),
                "Use Disabled for a disabled tenant policy.");
        }
        if (maxConcurrentRuns is < 1 or > PlatformMaxConcurrentRuns)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxConcurrentRuns),
                $"Concurrent runs must be between 1 and " +
                $"{PlatformMaxConcurrentRuns}.");
        }

        var sites = allowedSiteIds?
            .Where(id => id != Guid.Empty)
            .Distinct()
            .Order()
            .ToArray() ?? throw new ArgumentNullException(
                nameof(allowedSiteIds));
        if (sites.Length == 0)
        {
            throw new ArgumentException(
                "At least one allowed site is required.",
                nameof(allowedSiteIds));
        }

        var aliases = allowedProviderAliases?
            .Select(WarehouseGenerationProviderRegistration.NormalizeAlias)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray() ?? throw new ArgumentNullException(
                nameof(allowedProviderAliases));
        if (aliases.Length == 0)
        {
            throw new ArgumentException(
                "At least one approved provider alias is required.",
                nameof(allowedProviderAliases));
        }

        return new SpaceAiTenantPolicy(
            tenantId,
            dataPolicy,
            sites,
            aliases,
            maxConcurrentRuns,
            externalProviderEnabled,
            (budgetLimits ?? SpaceAiBudgetLimits.Unpriced).Validate());
    }

    public bool AllowsSite(Guid siteId) =>
        AllowedSiteIds.Contains(siteId);

    public bool AllowsProvider(string providerAlias) =>
        AllowedProviderAliases.Contains(
            providerAlias,
            StringComparer.Ordinal);

    private static void EnsureTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException(
                "Tenant id is required.",
                nameof(tenantId));
        }
    }
}

public interface ISpaceAiTenantPolicySource
{
    Task<SpaceAiTenantPolicy> GetPolicyAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Safe default for both existing and newly observed tenants.
/// Enabling AI requires an explicit tenant-scoped replacement.
/// </summary>
public sealed class DisabledSpaceAiTenantPolicySource :
    ISpaceAiTenantPolicySource
{
    public Task<SpaceAiTenantPolicy> GetPolicyAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(SpaceAiTenantPolicy.Disabled(tenantId));
}

public interface ISpaceAiQuotaLease : IAsyncDisposable
{
}

public interface ISpaceAiQuotaLeaseManager
{
    Task<ISpaceAiQuotaLease?> TryAcquireAsync(
        Guid tenantId,
        int maxConcurrentRuns,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Fail-closed until E13-S12 supplies atomic concurrency and budget leases.
/// </summary>
public sealed class ClosedSpaceAiQuotaLeaseManager :
    ISpaceAiQuotaLeaseManager
{
    public Task<ISpaceAiQuotaLease?> TryAcquireAsync(
        Guid tenantId,
        int maxConcurrentRuns,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<ISpaceAiQuotaLease?>(null);
}

public sealed record WarehouseGenerationProviderRegistration
{
    public WarehouseGenerationProviderRegistration(
        string alias,
        WarehouseGenerationProviderKind kind,
        IWarehouseGenerationProvider provider)
    {
        Alias = NormalizeAlias(alias);
        Kind = kind;
        Provider = provider ??
            throw new ArgumentNullException(nameof(provider));
    }

    public string Alias { get; }
    public WarehouseGenerationProviderKind Kind { get; }
    public IWarehouseGenerationProvider Provider { get; }

    public static string NormalizeAlias(string alias)
    {
        if (string.IsNullOrWhiteSpace(alias))
        {
            throw new ArgumentException(
                "Provider alias is required.",
                nameof(alias));
        }

        var normalized = alias.Trim();
        if (normalized.Length > 64 ||
            normalized.Any(character =>
                !(character is >= 'a' and <= 'z' ||
                  character is >= '0' and <= '9' ||
                  character is '.' or '_' or '-')) ||
            normalized[0] is '.' or '_' or '-')
        {
            throw new ArgumentException(
                "Provider alias must be a lowercase approved alias.",
                nameof(alias));
        }

        return normalized;
    }
}

public interface IWarehouseGenerationProviderRegistry
{
    IReadOnlyList<WarehouseGenerationProviderRegistration> Registrations { get; }

    bool TryGet(
        string alias,
        out WarehouseGenerationProviderRegistration? registration);
}

public sealed class WarehouseGenerationProviderRegistry :
    IWarehouseGenerationProviderRegistry
{
    private readonly IReadOnlyDictionary<
        string,
        WarehouseGenerationProviderRegistration> _providers;

    public WarehouseGenerationProviderRegistry(
        IEnumerable<WarehouseGenerationProviderRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        var providers = new Dictionary<
            string,
            WarehouseGenerationProviderRegistration>(
                StringComparer.Ordinal);
        foreach (var registration in registrations)
        {
            if (!providers.TryAdd(registration.Alias, registration))
            {
                throw new InvalidOperationException(
                    $"Duplicate warehouse generation provider alias " +
                    $"'{registration.Alias}'.");
            }
        }
        _providers = providers;
    }

    public IReadOnlyList<WarehouseGenerationProviderRegistration>
        Registrations => _providers.Values
            .OrderBy(item => item.Alias, StringComparer.Ordinal)
            .ToArray();

    public bool TryGet(
        string alias,
        out WarehouseGenerationProviderRegistration? registration)
    {
        registration = null;
        if (string.IsNullOrWhiteSpace(alias))
            return false;
        return _providers.TryGetValue(alias.Trim(), out registration);
    }
}

public sealed class SpaceAiGenerationGateway(
    ISpaceExecutionContext executionContext,
    ISpaceAiTenantPolicySource policySource,
    ISpaceAiQuotaLeaseManager quotaLeaseManager,
    IWarehouseGenerationProviderRegistry providerRegistry,
    IWarehouseGenerationOutputValidator outputValidator)
{
    public async Task<WarehouseGenerationResult> GenerateAsync(
        Guid siteId,
        string providerAlias,
        WarehouseGenerationInput input,
        CancellationToken cancellationToken = default)
    {
        var tenantId = RequireInternalExecution();
        if (siteId == Guid.Empty)
            throw new ArgumentException("Site id is required.", nameof(siteId));
        ArgumentNullException.ThrowIfNull(input);

        var policy = await policySource.GetPolicyAsync(
            tenantId,
            cancellationToken);
        if (policy.TenantId != tenantId)
        {
            throw new InvalidOperationException(
                "SPACE_AI_POLICY_TENANT_SCOPE_MISMATCH");
        }
        if (!policy.IsEnabled)
            throw Disabled();
        if (!policy.AllowsSite(siteId) ||
            !policy.AllowsProvider(providerAlias) ||
            input.Policy != policy.DataPolicy)
        {
            throw SourcePolicyDenied();
        }
        if (!providerRegistry.TryGet(providerAlias, out var registration) ||
            registration is null)
        {
            throw ProviderUnavailable();
        }
        if (registration.Kind == WarehouseGenerationProviderKind.External &&
            !policy.ExternalProviderEnabled)
        {
            throw SourcePolicyDenied();
        }
        if (registration.Kind == WarehouseGenerationProviderKind.External)
            SpaceAiExternalProviderRequestGate.EnsureSafe(input);

        await using var lease = await quotaLeaseManager.TryAcquireAsync(
            tenantId,
            policy.MaxConcurrentRuns,
            cancellationToken);
        if (lease is null)
            throw QuotaExceeded();

        var output = await registration.Provider.GenerateAsync(
            input,
            cancellationToken);
        return outputValidator.Validate(input, output).Output;
    }

    private Guid RequireInternalExecution()
    {
        if (executionContext.IsExternal)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.ExternalSubjectDenied,
                403,
                "External principals cannot invoke AI generation providers.",
                recoveryAction: "use-internal-space-editor");
        }
        if (executionContext.TenantId == Guid.Empty ||
            executionContext.ActorId == Guid.Empty)
        {
            throw new SpaceTenantScopeException(
                "A verified internal Space tenant context is required.");
        }
        return executionContext.TenantId;
    }

    private static SpaceProblemException Disabled() =>
        new(
            SpaceErrorCodes.AiDisabled,
            403,
            "AI warehouse generation is disabled.",
            recoveryAction: "enable-ai-for-tenant");

    private static SpaceProblemException SourcePolicyDenied() =>
        new(
            SpaceErrorCodes.AiSourcePolicyDenied,
            403,
            "The requested AI data policy, site, or provider is not allowed.",
            recoveryAction: "review-ai-tenant-policy");

    private static SpaceProblemException ProviderUnavailable() =>
        new(
            SpaceErrorCodes.AiProviderUnavailable,
            503,
            "The approved AI provider is unavailable.",
            recoveryAction: "use-rule-only-generation",
            retryable: true);

    private static SpaceProblemException QuotaExceeded() =>
        new(
            SpaceErrorCodes.AiQuotaExceeded,
            429,
            "The tenant AI concurrency or budget quota is unavailable.",
            recoveryAction: "use-rule-only-or-retry-later",
            retryable: true);
}
