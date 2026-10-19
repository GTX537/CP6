using System.Data;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace CP6.Core.Persistence.Tenancy;

internal sealed record IdentityDirectoryInstallRequestV2(Guid InstallationId, string Server, int Port, string Database,
    uint DatabaseOid, string SystemIdentifier, string SourceRevision, TenantLoginV1[] Tenants);
internal sealed record IdentityDirectoryInstallationResultV2(string Contract, string Outcome, IdentityDirectoryDatabaseIdentity? Identity);
internal sealed class IdentityDirectoryInstallationException(string outcome, string code) : Exception(code)
{ internal string Outcome { get; } = outcome; }
internal enum IdentityDirectoryInstallCheckpoint { BeforeCommit, AfterCommit }

/// <summary>Explicit fresh-only control entry. SourceRevision is operator provenance, not proof of Core source equivalence.
/// A lost install commit acknowledgement requires read-only inspection, never automatic replay or adoption.</summary>
internal sealed class IdentityDirectoryInstallationV2
{
    internal const string Contract = "identity-directory-installation-v2";
    internal const string Schema = "cp6_identity_directory_v2";
    internal Func<IdentityDirectoryInstallCheckpoint, Task>? Checkpoint { get; init; }
    // Add the protected mapping and immutable receipt facts to the existing structural catalog.
    // Only this profile changes; older service-token seals and business rows remain untouched.
    internal static string CatalogSql => $$"""
        SELECT jsonb_build_array(({{ServiceTokenProfileV1.CatalogSql}})::jsonb,
          (SELECT coalesce(jsonb_agg(jsonb_build_array(role_oid,role_name,tenant_id) ORDER BY role_oid),'[]')
            FROM cp6_tenancy_v1.login_tenant),
          (SELECT coalesce(jsonb_agg(to_jsonb(i)-'fingerprint' ORDER BY singleton),'[]')
            FROM {{Schema}}.installation i))::text
        """;

    internal async Task<IdentityDirectoryInstallationResultV2> InstallAsync(NpgsqlConnection connection,
        IdentityDirectoryInstallRequestV2 request, CancellationToken ct = default)
    {
        NpgsqlTransaction? tx = null; var commitStarted = false;
        try
        {
            request = ValidateAndCopy(request);
            if (connection.State == ConnectionState.Closed) await connection.OpenAsync(ct).ConfigureAwait(false);
            tx = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
            await Sql(connection, tx, "SET LOCAL search_path=pg_catalog", ct).ConfigureAwait(false);
            await ValidateIdentity(connection, tx, request, ct).ConfigureAwait(false);
            await Sql(connection, tx, "SELECT pg_catalog.pg_advisory_xact_lock(1129330260,3)", ct).ConfigureAwait(false);
            if (await Exists(connection, tx, ct).ConfigureAwait(false)) Reject("already-installed-or-unknown-layout");
            await IdentityDirectoryAuthorizationV2.InstallAsync(connection, tx, request.Tenants, ct).ConfigureAwait(false);
            var roles = string.Join(",", request.Tenants.Select(t => new NpgsqlCommandBuilder().QuoteIdentifier(t.Login)));
            await Sql(connection, tx, $$"""
                CREATE SCHEMA {{Schema}};
                REVOKE ALL ON SCHEMA {{Schema}} FROM PUBLIC;
                CREATE TABLE {{Schema}}.installation(
                  singleton boolean PRIMARY KEY CHECK(singleton),installation_id uuid NOT NULL,profile text NOT NULL,
                  database_oid oid NOT NULL,system_identifier text NOT NULL,source_revision text NOT NULL,
                  canonical text NOT NULL,fingerprint text NOT NULL);
                REVOKE ALL ON {{Schema}}.installation FROM PUBLIC;
                CREATE FUNCTION {{Schema}}.system_identifier() RETURNS text
                  LANGUAGE sql STABLE SECURITY DEFINER SET search_path=pg_catalog
                  AS 'SELECT system_identifier::text FROM pg_catalog.pg_control_system()';
                CREATE FUNCTION {{Schema}}.fingerprint() RETURNS text
                  LANGUAGE sql STABLE SECURITY DEFINER SET search_path=pg_catalog
                  AS $fp$ SELECT upper(encode(sha256(convert_to(({{CatalogSql}}),'UTF8')),'hex')) $fp$;
                REVOKE ALL ON ALL FUNCTIONS IN SCHEMA {{Schema}} FROM PUBLIC;
                GRANT USAGE ON SCHEMA {{Schema}} TO {{roles}};
                GRANT EXECUTE ON FUNCTION {{Schema}}.system_identifier(),{{Schema}}.fingerprint() TO {{roles}};
                GRANT SELECT(singleton,installation_id,profile,database_oid,system_identifier,fingerprint)
                  ON {{Schema}}.installation TO {{roles}};
                """, ct).ConfigureAwait(false);
            await using (var insert = new NpgsqlCommand($$"""
                INSERT INTO {{Schema}}.installation VALUES(true,@id,@profile,@oid,@system,@source,@canonical,'')
                """, connection, tx))
            {
                Parameters(insert, request); await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            var fingerprint = await Hash(connection, tx, ct).ConfigureAwait(false);
            await using (var seal = new NpgsqlCommand($"UPDATE {Schema}.installation SET fingerprint=@fingerprint WHERE singleton", connection, tx))
            {
                seal.Parameters.AddWithValue("fingerprint", fingerprint); await seal.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            await TenantLoginDirectoryV1.ValidateAsync(connection, tx, request.Tenants, ct).ConfigureAwait(false);
            var result = await Verify(connection, tx, request, ct).ConfigureAwait(false);
            if (Checkpoint is not null) await Checkpoint(IdentityDirectoryInstallCheckpoint.BeforeCommit).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested(); commitStarted = true;
            await tx.CommitAsync(ct).ConfigureAwait(false);
            if (Checkpoint is not null) await Checkpoint(IdentityDirectoryInstallCheckpoint.AfterCommit).ConfigureAwait(false);
            return result with { Outcome = "INSTALLED" };
        }
        catch (Exception error)
        {
            var rolledBack = tx is null;
            if (tx is not null && !commitStarted)
                try { using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10)); await tx.RollbackAsync(cleanup.Token).ConfigureAwait(false); rolledBack = true; }
                catch { /* No rollback acknowledgement means no claim of NOT_APPLIED. */ }
            throw new IdentityDirectoryInstallationException(commitStarted || !rolledBack ? "UNKNOWN" : "NOT_APPLIED",
                error is IdentityDirectoryInstallationException known ? known.Message : error is OperationCanceledException ? "cancelled" : "execution-rejected");
        }
        finally { if (tx is not null) try { await tx.DisposeAsync().ConfigureAwait(false); } catch { } }
    }

    internal async Task<IdentityDirectoryInstallationResultV2> InspectAsync(NpgsqlConnection connection,
        IdentityDirectoryInstallRequestV2 request, CancellationToken ct = default)
    {
        try
        {
            request = ValidateAndCopy(request);
            if (connection.State == ConnectionState.Closed) await connection.OpenAsync(ct).ConfigureAwait(false);
            await using var tx = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct).ConfigureAwait(false);
            await Sql(connection, tx, "SET TRANSACTION READ ONLY", ct).ConfigureAwait(false);
            await Sql(connection, tx, "SET LOCAL search_path=pg_catalog", ct).ConfigureAwait(false);
            await ValidateIdentity(connection, tx, request, ct).ConfigureAwait(false);
            // ABSENT describes the receipt only; it does not certify a layout is eligible to install.
            if (!await Exists(connection, tx, ct).ConfigureAwait(false)) return new(Contract, "ABSENT", null);
            return await Verify(connection, tx, request, ct).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            throw new IdentityDirectoryInstallationException(error is IdentityDirectoryInstallationException ? "REJECTED" : "UNAVAILABLE",
                error is IdentityDirectoryInstallationException known ? known.Message : error is OperationCanceledException ? "cancelled" : "inspection-unavailable");
        }
    }

    private static async Task<IdentityDirectoryInstallationResultV2> Verify(NpgsqlConnection connection, NpgsqlTransaction tx,
        IdentityDirectoryInstallRequestV2 request, CancellationToken ct)
    {
        // Reject views/rules/RLS/custom column types before querying receipt rows as control.
        await using (var shape = new NpgsqlCommand($$"""
            SELECT EXISTS(SELECT FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace
              WHERE n.nspname='{{Schema}}' AND n.nspowner=current_user::regrole AND c.relname='installation'
                AND c.relkind='r' AND c.relowner=current_user::regrole AND NOT c.relrowsecurity AND NOT c.relforcerowsecurity
                AND NOT EXISTS(SELECT FROM pg_catalog.pg_rewrite WHERE ev_class=c.oid)
                AND NOT EXISTS(SELECT FROM pg_catalog.pg_trigger WHERE tgrelid=c.oid AND NOT tgisinternal)
                AND (SELECT count(*) FROM pg_catalog.pg_attribute WHERE attrelid=c.oid AND attnum>0)=8
                AND (SELECT count(*) FROM unnest(
                  ARRAY['singleton','installation_id','profile','database_oid','system_identifier','source_revision','canonical','fingerprint'],
                  ARRAY['bool','uuid','text','oid','text','text','text','text']) expected(name,type)
                  JOIN pg_catalog.pg_attribute a ON a.attrelid=c.oid AND a.attname=expected.name
                  WHERE a.atttypid=expected.type::regtype AND a.attnotnull AND NOT a.attisdropped AND a.attgenerated='' AND a.attidentity='')=8)
            """, connection, tx))
            if (await shape.ExecuteScalarAsync(ct).ConfigureAwait(false) is not true) Reject("receipt-shape-rejected");
        await TenantLoginDirectoryV1.ValidateAsync(connection, tx, request.Tenants, ct).ConfigureAwait(false);
        var fingerprint = await Hash(connection, tx, ct).ConfigureAwait(false);
        await using var verify = new NpgsqlCommand($$"""
            SELECT (SELECT count(*) FROM {{Schema}}.installation)=1 AND EXISTS(SELECT FROM {{Schema}}.installation
              WHERE singleton AND installation_id=@id AND profile=@profile AND database_oid=@oid AND system_identifier=@system
                AND source_revision=@source AND canonical=@canonical AND fingerprint=@fingerprint)
            """, connection, tx);
        Parameters(verify, request); verify.Parameters.AddWithValue("fingerprint", fingerprint);
        if (await verify.ExecuteScalarAsync(ct).ConfigureAwait(false) is not true) Reject("installation-or-drift-rejected");
        return new(Contract, "VERIFIED", new(request.InstallationId, request.Server, request.Port, request.Database,
            request.DatabaseOid, request.SystemIdentifier, fingerprint));
    }

    private static async Task ValidateIdentity(NpgsqlConnection connection, NpgsqlTransaction tx,
        IdentityDirectoryInstallRequestV2 request, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("""
            SELECT r.rolsuper AND session_user=current_user AND d.datdba=r.oid AND d.oid=@oid AND d.datname=@database
              AND c.system_identifier::text=@system AND pg_catalog.host(pg_catalog.inet_server_addr())=@server
              AND pg_catalog.inet_server_port()=@port
            FROM pg_catalog.pg_roles r,pg_catalog.pg_database d,pg_catalog.pg_control_system() c
            WHERE r.rolname=current_user AND d.datname=current_database()
            """, connection, tx);
        command.Parameters.AddWithValue("oid", NpgsqlDbType.Oid, request.DatabaseOid); command.Parameters.AddWithValue("database", request.Database);
        command.Parameters.AddWithValue("system", request.SystemIdentifier); command.Parameters.AddWithValue("server", request.Server);
        command.Parameters.AddWithValue("port", request.Port);
        if (await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is not true) Reject("control-identity-rejected");
    }
    private static IdentityDirectoryInstallRequestV2 ValidateAndCopy(IdentityDirectoryInstallRequestV2 request)
    {
        if (request is null || request.InstallationId == Guid.Empty || request.DatabaseOid == 0 ||
            !IPAddress.TryParse(request.Server, out var address) || address.ToString() != request.Server || request.Port is < 1 or > 65535 ||
            string.IsNullOrWhiteSpace(request.Database) || Encoding.UTF8.GetByteCount(request.Database) > 63 || request.Database.Any(char.IsControl) ||
            !ulong.TryParse(request.SystemIdentifier, out var system) || system == 0 || system.ToString(System.Globalization.CultureInfo.InvariantCulture) != request.SystemIdentifier ||
            request.SourceRevision is null || request.SourceRevision.Length != 40 || request.SourceRevision.Any(c => !char.IsAsciiHexDigitLower(c)) ||
            request.Tenants is null || request.Tenants.Length is < 1 or > 128 || request.Tenants.Any(x => x is null || x.TenantId == Guid.Empty ||
                string.IsNullOrWhiteSpace(x.Login) || x.Login.Any(char.IsControl) || Encoding.UTF8.GetByteCount(x.Login) > 63)) Reject("input-rejected");
        var copy = request with { Tenants = request.Tenants.OrderBy(x => x.TenantId).ToArray() };
        if (copy.Tenants.Select(x => x.TenantId).Distinct().Count() != copy.Tenants.Length ||
            copy.Tenants.Select(x => x.Login).Distinct(StringComparer.Ordinal).Count() != copy.Tenants.Length) Reject("input-rejected");
        return copy;
    }
    private static void Parameters(NpgsqlCommand command, IdentityDirectoryInstallRequestV2 request)
    {
        command.Parameters.AddWithValue("id", request.InstallationId); command.Parameters.AddWithValue("profile", IdentityDirectoryAuthorizationV2.ModelVersion);
        command.Parameters.AddWithValue("oid", NpgsqlDbType.Oid, request.DatabaseOid); command.Parameters.AddWithValue("system", request.SystemIdentifier);
        command.Parameters.AddWithValue("source", request.SourceRevision); command.Parameters.AddWithValue("canonical", JsonSerializer.Serialize(request));
    }
    private static async Task<bool> Exists(NpgsqlConnection c, NpgsqlTransaction t, CancellationToken ct)
    { await using var q = new NpgsqlCommand($"SELECT EXISTS(SELECT FROM pg_catalog.pg_namespace WHERE nspname='{Schema}')", c, t); return await q.ExecuteScalarAsync(ct).ConfigureAwait(false) is true; }
    private static async Task<string> Hash(NpgsqlConnection c, NpgsqlTransaction t, CancellationToken ct)
    {
        await using var q = new NpgsqlCommand(CatalogSql, c, t);
        var value = (string)(await q.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }
    private static async Task Sql(NpgsqlConnection c, NpgsqlTransaction t, string sql, CancellationToken ct)
    { await using var q = new NpgsqlCommand(sql, c, t); await q.ExecuteNonQueryAsync(ct).ConfigureAwait(false); }
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Reject(string code) => throw new IdentityDirectoryInstallationException("REJECTED", code);
}
