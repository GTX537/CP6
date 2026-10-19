using Npgsql;
using NpgsqlTypes;

namespace CP6.Core.Persistence.Tenancy;

/// <summary>Frozen host expectation from explicit installation/inspection. Its fingerprint must be protected
/// alongside host configuration. Checks detect drift at read entry, not arbitrary concurrent hostile control DDL.</summary>
internal sealed record IdentityDirectoryDatabaseIdentity(Guid InstallationId, string Server, int Port, string Database,
    uint DatabaseOid, string SystemIdentifier, string Fingerprint)
{
    internal async Task ValidateAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken ct)
    {
        if (InstallationId == Guid.Empty || DatabaseOid == 0 || string.IsNullOrEmpty(Fingerprint) || Fingerprint.Length != 64 ||
            Fingerprint.Any(c => !char.IsAsciiHexDigitUpper(c))) throw TenantStorageBindingV1.Rejected();
        await using var command = new NpgsqlCommand($$"""
            SELECT i.installation_id=@id AND i.profile=@profile AND i.database_oid=@oid AND i.system_identifier=@system
              AND pg_catalog.current_database()=@database
              AND (SELECT oid FROM pg_catalog.pg_database WHERE datname=pg_catalog.current_database())=@oid
              AND pg_catalog.host(pg_catalog.inet_server_addr())=@server AND pg_catalog.inet_server_port()=@port
              AND {{IdentityDirectoryInstallationV2.Schema}}.system_identifier()=@system
              AND i.fingerprint=@fingerprint AND {{IdentityDirectoryInstallationV2.Schema}}.fingerprint()=@fingerprint
            FROM {{IdentityDirectoryInstallationV2.Schema}}.installation i WHERE i.singleton
            """, connection, transaction);
        command.Parameters.AddWithValue("id", InstallationId); command.Parameters.AddWithValue("profile", IdentityDirectoryAuthorizationV2.ModelVersion);
        command.Parameters.AddWithValue("oid", NpgsqlDbType.Oid, DatabaseOid); command.Parameters.AddWithValue("database", Database);
        command.Parameters.AddWithValue("system", SystemIdentifier); command.Parameters.AddWithValue("server", Server);
        command.Parameters.AddWithValue("port", Port); command.Parameters.AddWithValue("fingerprint", Fingerprint);
        if (await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is not true)
            throw new TenantStorageException(TenantStorageFailure.IdentityRejected);
    }
}
