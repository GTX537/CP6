namespace CP6.WebApi.Services.ServiceTokenTenancy;

/// <summary>Registration-time signing inputs for the explicit service-token profile only.</summary>
public sealed class ServiceTokenSigningSnapshot
{
    private readonly string _issuer;
    private readonly string _activeKey;
    private readonly (string Kid, string Pem)[] _keys;

    internal ServiceTokenSigningSnapshot(CrmOidcOptions options)
    {
        _issuer = options.Issuer;
        _activeKey = options.ActiveKeyId;
        _keys = options.Keys.Select(k => (k.Kid, k.Pem)).ToArray();
    }

    // No caller receives the stored array or mutable option/key objects.
    internal CrmOidcOptions CreateOptions() => new()
    {
        Enabled = true, Issuer = _issuer, ActiveKeyId = _activeKey,
        Keys = _keys.Select(k => new CrmOidcKey { Kid = k.Kid, Pem = k.Pem }).ToList()
    };
}
