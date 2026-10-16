using System.Text;
using System.Text.Json;
using Npgsql;

namespace CP6.Core.Persistence.Tenancy;

internal sealed record TenantProvisioningRequest(Guid OperationId, Guid TenantId, string RuntimeLogin,
    string Host, int Port, string Database, string SystemIdentifier, uint DatabaseOid,
    string Profile = TenantMessagingProfile.CombinedModelVersion)
{
    internal string Schema => "t_" + TenantId.ToString("N");
    internal void Validate()
    {
        if (OperationId == Guid.Empty || TenantId == Guid.Empty || Port is < 1 or > 65535
            || Uri.CheckHostName(Host) == UriHostNameType.Unknown || DatabaseOid == 0
            || string.IsNullOrEmpty(SystemIdentifier) || !SystemIdentifier.All(char.IsAsciiDigit)
            || Profile != TenantMessagingProfile.CombinedModelVersion) throw new TenantProvisioningException("input-rejected");
        foreach (var name in new[] { RuntimeLogin, Database })
            if (string.IsNullOrWhiteSpace(name) || name.Any(char.IsControl) || Encoding.UTF8.GetByteCount(name) > 63)
                throw new TenantProvisioningException("input-rejected");
    }
    internal string Canonical(string scripts) => JsonSerializer.Serialize(new
    {
        OperationId, TenantId, RuntimeLogin, Host = Host.ToLowerInvariant(), Port, Database,
        SystemIdentifier, DatabaseOid, Profile, Schema,
        Migration = TenantMessagingProfile.CombinedMigrationVersion, Scripts = scripts
    });
}

internal sealed record TenantProvisioningResult(Guid OperationId, Guid TenantId, string RuntimeLogin,
    uint RuntimeRoleOid, string Schema, string Database, uint DatabaseOid, string SystemIdentifier,
    string Profile, string Migration, string Scripts, DateTimeOffset CompletedAt);

/// <summary>Never forwards provider messages, SQL, credentials, connection strings, or inner exceptions.</summary>
internal sealed class TenantProvisioningException(string code) : Exception("Tenant storage provisioning rejected: " + code + ".")
{
    internal string Code { get; } = code;
}

internal enum TenantProvisioningCheckpoint { BeforeCommit, AfterCommit }
