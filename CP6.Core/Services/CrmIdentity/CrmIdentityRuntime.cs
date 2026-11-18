using CP6.Platform.Messaging;
using CP6.Core.Services.Platform;

namespace CP6.Core.Services.CrmIdentity;

public sealed class CrmIdentityOptions
{
    public bool Enabled { get; set; }
    public string Issuer { get; set; } = "";
    public Dictionary<Guid, string> Tenants { get; set; } = [];
    public string[] ProjectionReaderClientIds { get; set; } = [];
    // 由宿主从已验证的 OIDC/identity 配置合成，不从客户端请求绑定。
    public Dictionary<Guid, IdentitySourceBindingV1> SourceBindings { get; } = [];
    internal bool RequireSourceRegistration { get; set; }
    internal bool AccountServiceSources { get; set; }
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
    internal CrmInquiryTenantPauseV1? InquiryFence { get; }
    public const string AccountBundleSha256 = "d4d19f3a5ebd2e08d0a18776d5a1f51487123796326354c9d394ee34f2a53770";
    public const string InquiryBundleSha256 = "938957675b9249db87333beeb7f2eba90104853415b11a99ff36557ae3452360";

    internal static string ReadBundleDigest(string directory, bool accountSources)
    {
        var current = IdentityEventContracts.Hash(File.ReadAllBytes(Path.Combine(directory, Cp6ContractBundle.IndexFileName)));
        if (accountSources && current != AccountBundleSha256) throw new InvalidOperationException("C02_ACCOUNT_BUNDLE_DRIFT");
        if (accountSources || current != AccountBundleSha256) return current;
        // 未启用账户来源的既有宿主继续使用已登记的 1.3.0 摘要。
        var retained = IdentityEventContracts.Hash(File.ReadAllBytes(Path.Combine(directory, "contract-bundle.1.3.0.json")));
        return retained == InquiryBundleSha256 ? retained : throw new InvalidOperationException("C02_INQUIRY_BUNDLE_DRIFT");
    }

    public CrmIdentityRuntime(CrmIdentityOptions options, IdentityEventValidator validator, TimeProvider? clock = null,
        string? contractDirectory = null, CrmInquiryTenantPauseV1? inquiryFence = null)
    {
        if (!options.Enabled || options.Tenants.Count == 0 || options.Tenants.Keys.Any(t => t == Guid.Empty) ||
            options.PasswordMaxAgeDays < 0 || options.ProjectionReaderClientIds.Length == 0)
            throw new ArgumentException("C02 requires explicit tenants, reader clients and password policy.", nameof(options));
        foreach (var region in options.Tenants.Values) Cp6CloudEventAttributes.Region.Validate(region);
        Options = options;
        Validator = validator;
        Clock = clock ?? TimeProvider.System;
        InquiryFence = inquiryFence;
        ContractBundleSha256 = ReadBundleDigest(contractDirectory ??
            Path.Combine(AppContext.BaseDirectory, "contracts/events/platform"), options.AccountServiceSources);
    }
}
