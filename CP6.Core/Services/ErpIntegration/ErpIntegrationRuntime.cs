using CP6.Platform.Messaging;

namespace CP6.Core.Services.ErpIntegration;

public sealed class ErpIntegrationOptions
{
    public bool Enabled { get; set; }
    public Dictionary<Guid, string> Tenants { get; set; } = [];
    public string[] ReaderClientIds { get; set; } = [];
    public string DaprAppToken { get; set; } = "";
    public string DaprApiToken { get; set; } = "";
    public string DaprHttpEndpoint { get; set; } = "http://127.0.0.1:3500";
    public string DaprGrpcEndpoint { get; set; } = "http://127.0.0.1:50001";
    public int MaxInboxAttempts { get; set; } = 10;
    public int InitialRetrySeconds { get; set; } = 1;
    public int MaximumRetrySeconds { get; set; } = 300;
}

public sealed class ErpIntegrationRuntime
{
    public ErpIntegrationOptions Options { get; }
    public ErpEventValidator Validator { get; }
    public TimeProvider Clock { get; }

    public ErpIntegrationRuntime(ErpIntegrationOptions options, ErpEventValidator validator, TimeProvider? clock = null)
    {
        if (!options.Enabled || options.Tenants.Count == 0 || options.Tenants.Keys.Any(t => t == Guid.Empty) ||
            options.ReaderClientIds.Length == 0 || options.ReaderClientIds.Any(string.IsNullOrWhiteSpace) ||
            options.ReaderClientIds.Distinct(StringComparer.Ordinal).Count() != options.ReaderClientIds.Length ||
            options.DaprAppToken.Length is < 32 or > 4096 || options.DaprApiToken.Length is < 32 or > 4096 ||
            options.DaprAppToken == options.DaprApiToken ||
            options.DaprAppToken.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)) ||
            options.DaprApiToken.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)) ||
            options.MaxInboxAttempts is < 1 or > 100 ||
            options.InitialRetrySeconds < 1 || options.MaximumRetrySeconds < options.InitialRetrySeconds ||
            options.MaximumRetrySeconds > 3600)
            throw new ArgumentException("C03 requires explicit tenants, reader clients, authenticated sidecar and bounded retries.", nameof(options));
        foreach (var region in options.Tenants.Values) Cp6CloudEventAttributes.Region.Validate(region);
        Options = options; Validator = validator; Clock = clock ?? TimeProvider.System;
    }
}
