using CP6.Platform.Messaging;

namespace CP6.Core.Services.CrmIdentity;

public sealed class CrmIdentityOptions
{
    public bool Enabled { get; set; }
    public string Issuer { get; set; } = "";
    public Dictionary<Guid, string> Tenants { get; set; } = [];
    public string[] ProjectionReaderClientIds { get; set; } = [];
    public int PasswordMaxAgeDays { get; set; }
    public string DaprHttpEndpoint { get; set; } = "http://127.0.0.1:3500";
    public string DaprGrpcEndpoint { get; set; } = "http://127.0.0.1:50001";
}

/// <summary>Optional DI dependency: existing applications enable the projection explicitly.</summary>
public sealed class CrmIdentityRuntime
{
    public CrmIdentityOptions Options { get; }
    public IdentityEventValidator Validator { get; }
    public TimeProvider Clock { get; }
    public string ContractBundleSha256 { get; }

    public CrmIdentityRuntime(CrmIdentityOptions options, IdentityEventValidator validator, TimeProvider? clock = null,
        string? contractDirectory = null)
    {
        if (!options.Enabled || options.Tenants.Count == 0 || options.Tenants.Keys.Any(t => t == Guid.Empty) ||
            options.PasswordMaxAgeDays < 0 || options.ProjectionReaderClientIds.Length == 0)
            throw new ArgumentException("C02 requires explicit tenants, reader clients and password policy.", nameof(options));
        foreach (var region in options.Tenants.Values) Cp6CloudEventAttributes.Region.Validate(region);
        Options = options;
        Validator = validator;
        Clock = clock ?? TimeProvider.System;
        ContractBundleSha256 = IdentityEventContracts.Hash(File.ReadAllBytes(Path.Combine(contractDirectory ??
            Path.Combine(AppContext.BaseDirectory, "contracts/events/platform"), Cp6ContractBundle.IndexFileName)));
    }
}
