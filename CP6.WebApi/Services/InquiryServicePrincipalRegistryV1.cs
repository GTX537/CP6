using System.Collections.ObjectModel;
using Microsoft.Extensions.Primitives;

namespace CP6.WebApi.Services;

public sealed class InquiryServicePrincipalOptionsV1
{
    public bool Enabled { get; set; }
    public string Version { get; set; } = "";
    public List<InquiryServicePrincipalRegistrationV1> Registrations { get; set; } = [];
}

public sealed class InquiryServicePrincipalRegistrationV1
{
    public string ClientId { get; set; } = "";
    public Guid SubjectId { get; set; }
    public Guid DepartmentId { get; set; }
    public int[] RoleIds { get; set; } = [];
}

/// <summary>Host-owned identity/role assignments, not request claims or a Sys_User alias.
/// Like the existing service host, changes require stopping ingress, draining every instance
/// and restarting with the new reviewed configuration. This snapshot has no live-reload claim.</summary>
public sealed class InquiryServicePrincipalRegistryV1
{
    private readonly Dictionary<string, Registration> registrations = new(StringComparer.Ordinal);
    public bool Enabled { get; }
    internal sealed class Registration(string client, string secret, Guid tenant, Guid subject, Guid department,
        IEnumerable<int> roles, string version)
    {
        internal string Client { get; } = client;
        internal string SecretHash { get; } = secret;
        internal Guid Tenant { get; } = tenant;
        internal Guid Subject { get; } = subject;
        internal Guid Department { get; } = department;
        internal ReadOnlyCollection<int> Roles { get; } = Array.AsReadOnly(roles.Order().ToArray());
        internal string Version { get; } = version;
        public override string ToString() => nameof(Registration);
    }

    public InquiryServicePrincipalRegistryV1(InquiryServicePrincipalOptionsV1 options, CrmOidcOptions oidc)
    {
        Enabled = options.Enabled;
        if (!Enabled) return;
        if (!oidc.Enabled || string.IsNullOrWhiteSpace(options.Version) || options.Version.Length > 128
            || options.Registrations is not { Count: > 0 } || options.Registrations.Any(x => x is null)
            || options.Registrations.Select(x => x.ClientId).Distinct(StringComparer.Ordinal).Count() != options.Registrations.Count
            || options.Registrations.Select(x => x.SubjectId).Distinct().Count() != options.Registrations.Count)
            throw new InvalidOperationException("INQUIRY_SERVICE_REGISTRATION_INVALID");
        foreach (var item in options.Registrations)
        {
            var clients = oidc.ServiceClients.Where(x => x.ClientId == item.ClientId).ToArray();
            if (string.IsNullOrWhiteSpace(item.ClientId) || clients.Length != 1 || !WireId(item.SubjectId) || !WireId(item.DepartmentId)
                || item.RoleIds is not { Length: > 0 } || item.RoleIds.Distinct().Count() != item.RoleIds.Length)
                throw new InvalidOperationException("INQUIRY_SERVICE_REGISTRATION_INVALID");
            var client = clients[0];
            if (client.TenantId == Guid.Empty || client.SecretSha256 is not { Length: 64 }
                || !client.SecretSha256.All(char.IsAsciiHexDigit)
                || !client.AllowedScopes.SequenceEqual(new[] { "cp6.services" })
                || !oidc.Organizations.Any(x => x.TenantId == client.TenantId && x.CrmEnabled))
                throw new InvalidOperationException("INQUIRY_SERVICE_REGISTRATION_INVALID");
            // Disabled clients remain registered but never authenticate. Full int role range is preserved.
            if (client.Enabled) registrations.Add(item.ClientId, new(item.ClientId, client.SecretSha256,
                client.TenantId, item.SubjectId, item.DepartmentId, item.RoleIds, options.Version));
        }
    }

    internal Registration? Authenticate(StringValues authorization)
    {
        if (!Enabled || !CrmOidcServiceTokens.TryDecodeBasic(authorization, out var client, out var secret)
            || !registrations.TryGetValue(client, out var registration)
            || !CrmOidcCrypto.EqualsSecret(secret, registration.SecretHash)) return null;
        return registration;
    }

    private static bool WireId(Guid id) => id.ToString("D")[14] == '4' && "89ab".Contains(id.ToString("D")[19]);
    public override string ToString() => nameof(InquiryServicePrincipalRegistryV1);
}
