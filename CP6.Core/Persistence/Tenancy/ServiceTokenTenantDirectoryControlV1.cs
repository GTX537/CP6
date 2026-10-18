using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Npgsql;
using NpgsqlTypes;

namespace CP6.Core.Persistence.Tenancy;

internal sealed record TenantEligibilityV1(
    [property: JsonRequired] bool Enable, [property: JsonRequired] DateTime? ExpireDate);
internal sealed record ServiceTokenTenantControlRequestV1(
    [property: JsonRequired] ServiceTokenInstallRequest Installation,
    [property: JsonRequired] Guid TenantId,
    [property: JsonRequired] string Operation,
    string? TenantCode = null, string? TenantName = null,
    TenantEligibilityV1? Expected = null, TenantEligibilityV1? Desired = null);
internal sealed record ServiceTokenTenantStateV1(Guid TenantId, string TenantCode, string TenantName,
    TenantEligibilityV1 Eligibility);
internal sealed record ServiceTokenTenantControlResultV1(string Contract, string Outcome, ServiceTokenTenantStateV1? Current);
internal sealed class ServiceTokenTenantControlException(string outcome, string code) : Exception(code)
{
    internal string Outcome { get; } = outcome;
}
internal enum TenantControlCheckpointV1 { BeforeCommit, AfterCommit }

/// <summary>Control-owner DML only. No request journal, replay guarantee, schema or ACL changes.</summary>
internal sealed class ServiceTokenTenantDirectoryControlV1
{
    internal const string Contract = "service-token-tenant-directory-control-v1";
    internal Func<TenantControlCheckpointV1, Task>? Checkpoint { get; init; }

    // baseline comes from the compiled frozen profile, never from the input document.
    internal async Task<ServiceTokenTenantControlResultV1> ExecuteAsync(NpgsqlConnection connection,
        ServiceTokenTenantControlRequestV1 request, string baseline, CancellationToken ct = default)
    {
        NpgsqlTransaction? tx = null;
        var commitStarted = false;
        try
        {
            ValidateInput(request);
            var installation = request.Installation;
            var settings = new NpgsqlConnectionStringBuilder(connection.ConnectionString);
            if (settings.Host != installation.Server || settings.Port != installation.Port || settings.Database != installation.Database
                || settings.SearchPath != "pg_catalog" || settings.Pooling || settings.Enlist
                || installation.Tenants.Any(t => t.Login == settings.Username)) Reject("control-target-rejected");
            if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync(ct).ConfigureAwait(false);
            tx = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
            TenantLoginDirectoryV1.ValidateArguments(connection, tx, installation.Tenants);
            await Sql("SET LOCAL lock_timeout='15s'; SET LOCAL statement_timeout='60s'");
            await ValidateIdentity();
            // Compatible with runtime's shared mapping lock. No directory-exclusive lock or lock upgrade.
            await Sql("SELECT pg_catalog.pg_advisory_xact_lock_shared(1129330260,2); SELECT pg_catalog.pg_advisory_xact_lock_shared(1129330260,1)");
            await Sql("LOCK TABLE cp6_service_token_v1.installation,cp6_tenancy_v1.login_tenant IN SHARE MODE; LOCK TABLE public.\"Sys_Tenants\" IN ROW EXCLUSIVE MODE");
            await ValidateInstallation();
            // Serializes the absent-row case as well as subsequent commands for this tenant.
            // Hash collisions only cause extra waiting; PK/unique constraints remain the final arbiter.
            await using (var gate = new NpgsqlCommand("SELECT pg_catalog.pg_advisory_xact_lock(pg_catalog.hashtextextended(@key,0))", connection, tx))
            {
                gate.Parameters.AddWithValue("key", Contract + "/" + request.TenantId.ToString("D"));
                await gate.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            var current = await Read();
            var outcome = current is null ? "MISSING" : "CURRENT";
            if (request.Operation == "initialize")
            {
                if (current is not null) outcome = "ALREADY_EXISTS";
                else
                {
                    await using var insert = new NpgsqlCommand("""
                        INSERT INTO public."Sys_Tenants"("Id","TenantCode","TenantName","Enable","ExpireDate","TwoFactorMode","CreateDate","Creator")
                        VALUES(@tenant,@code,@name,@enabled,@expiry,0,clock_timestamp()::timestamp,session_user)
                        ON CONFLICT DO NOTHING
                        """, connection, tx);
                    insert.Parameters.AddWithValue("tenant", request.TenantId);
                    insert.Parameters.AddWithValue("code", request.TenantCode!);
                    insert.Parameters.AddWithValue("name", request.TenantName!);
                    Eligibility(insert, request.Desired!, "");
                    var inserted = await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1;
                    current = await Read();
                    outcome = inserted ? "INITIALIZED" : current is null ? "CODE_CONFLICT" : "ALREADY_EXISTS";
                }
            }
            else if (request.Operation == "set-eligibility" && current is not null)
            {
                await using var update = new NpgsqlCommand("""
                    UPDATE public."Sys_Tenants" SET "Enable"=@enabled,"ExpireDate"=@expiry,
                        "ModifyDate"=clock_timestamp()::timestamp,"Modifier"=session_user
                    WHERE "Id"=@tenant AND "Enable" IS NOT DISTINCT FROM @old_enabled
                        AND "ExpireDate" IS NOT DISTINCT FROM @old_expiry
                    """, connection, tx);
                update.Parameters.AddWithValue("tenant", request.TenantId);
                Eligibility(update, request.Desired!, ""); Eligibility(update, request.Expected!, "old_");
                outcome = await update.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1 ? "UPDATED" : "CAS_CONFLICT";
                current = await Read();
            }
            if (Checkpoint is not null) await Checkpoint(TenantControlCheckpointV1.BeforeCommit).ConfigureAwait(false);
            await ValidateIdentity();
            await ValidateInstallation();
            ct.ThrowIfCancellationRequested();
            commitStarted = true;
            await tx.CommitAsync(ct).ConfigureAwait(false);
            if (Checkpoint is not null) await Checkpoint(TenantControlCheckpointV1.AfterCommit).ConfigureAwait(false);
            return new(Contract, outcome, current);

            async Task Sql(string sql)
            {
                await using var command = new NpgsqlCommand(sql, connection, tx);
                await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            async Task ValidateIdentity()
            {
                await using var identity = new NpgsqlCommand("""
                    SELECT r.rolsuper AND session_user=current_user AND d.datdba=r.oid AND d.oid=@oid
                      AND c.system_identifier::text=@system AND pg_catalog.host(pg_catalog.inet_server_addr())=@server
                      AND pg_catalog.inet_server_port()=@port
                    FROM pg_catalog.pg_roles r,pg_catalog.pg_database d,pg_catalog.pg_control_system() c
                    WHERE r.rolname=current_user AND d.datname=current_database()
                    """, connection, tx);
                identity.Parameters.AddWithValue("oid", NpgsqlDbType.Oid, installation.DatabaseOid);
                identity.Parameters.AddWithValue("system", installation.SystemIdentifier);
                identity.Parameters.AddWithValue("server", installation.Server); identity.Parameters.AddWithValue("port", installation.Port);
                if (await identity.ExecuteScalarAsync(ct).ConfigureAwait(false) is not true) Reject("control-identity-rejected");
            }
            async Task ValidateInstallation()
            {
                // Before reading protected rows, establish that installation is a plain control-owned table.
                await using (var owner = new NpgsqlCommand("""
                    SELECT EXISTS(SELECT FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace
                      WHERE n.nspname='cp6_service_token_v1' AND n.nspowner=current_user::regrole
                        AND c.relname='installation' AND c.relkind='r' AND c.relowner=current_user::regrole
                        AND NOT c.relrowsecurity AND NOT c.relforcerowsecurity
                        AND NOT EXISTS(SELECT FROM pg_catalog.pg_trigger WHERE tgrelid=c.oid AND NOT tgisinternal)
                        AND NOT EXISTS(SELECT FROM pg_catalog.pg_rewrite WHERE ev_class=c.oid))
                    """, connection, tx))
                    if (await owner.ExecuteScalarAsync(ct).ConfigureAwait(false) is not true) Reject("installation-rejected");
                var canonical = JsonSerializer.Serialize(new ServiceRevocationInstallRequest(installation.InstallationId,
                    installation.Server, installation.Port, installation.Database, installation.DatabaseOid,
                    installation.SystemIdentifier, "", installation.Tenants.OrderBy(t => t.TenantId).ToArray()));
                var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(baseline)));
                var seal = await ServiceTokenProfileV1.HashAsync(connection, tx!, ct).ConfigureAwait(false);
                await using var verify = new NpgsqlCommand("""
                    SELECT EXISTS(SELECT FROM cp6_service_token_v1.installation
                      WHERE singleton AND installation_id=@id AND profile='service-token-v1'
                        AND database_oid=@oid AND system_identifier=@system AND canonical=@canonical
                        AND baseline_sha256=@hash AND fingerprint=@seal AND issuer_oid=0 AND issuer_name='')
                      AND (SELECT count(*) FROM cp6_tenancy_v1.login_tenant)=cardinality(@roles)
                      AND (SELECT count(*) FROM cp6_tenancy_v1.login_tenant m
                        JOIN pg_catalog.pg_roles r ON r.oid=m.role_oid AND r.rolname=m.role_name
                        JOIN unnest(@roles::text[],@tenants::uuid[]) b(login,tenant) ON b.login=r.rolname::text AND b.tenant=m.tenant_id
                        WHERE r.rolcanlogin AND NOT (r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication)
                          AND NOT EXISTS(SELECT FROM pg_catalog.pg_auth_members WHERE member=r.oid OR roleid=r.oid))=cardinality(@roles)
                    """, connection, tx);
                verify.Parameters.AddWithValue("id", installation.InstallationId);
                verify.Parameters.AddWithValue("oid", NpgsqlDbType.Oid, installation.DatabaseOid);
                verify.Parameters.AddWithValue("system", installation.SystemIdentifier);
                verify.Parameters.AddWithValue("canonical", canonical); verify.Parameters.AddWithValue("hash", hash);
                verify.Parameters.AddWithValue("seal", seal);
                verify.Parameters.AddWithValue("roles", installation.Tenants.Select(t => t.Login).ToArray());
                verify.Parameters.AddWithValue("tenants", installation.Tenants.Select(t => t.TenantId).ToArray());
                if (await verify.ExecuteScalarAsync(ct).ConfigureAwait(false) is not true) Reject("installation-or-mapping-rejected");
            }
            async Task<ServiceTokenTenantStateV1?> Read()
            {
                await using var query = new NpgsqlCommand("""
                    SELECT "TenantCode","TenantName","Enable","ExpireDate" FROM public."Sys_Tenants" WHERE "Id"=@tenant FOR UPDATE
                    """, connection, tx);
                query.Parameters.AddWithValue("tenant", request.TenantId);
                await using var reader = await query.ExecuteReaderAsync(ct).ConfigureAwait(false);
                return await reader.ReadAsync(ct).ConfigureAwait(false)
                    ? new(request.TenantId, reader.GetString(0), reader.GetString(1),
                        new(reader.GetBoolean(2), reader.IsDBNull(3) ? null : reader.GetDateTime(3))) : null;
            }
        }
        catch (Exception error)
        {
            var rolledBack = tx is null;
            if (tx is not null && !commitStarted)
            {
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    await tx.RollbackAsync(timeout.Token).ConfigureAwait(false);
                    rolledBack = true;
                }
                catch { /* A failed rollback acknowledgement is not a proof of no commit. */ }
            }
            throw new ServiceTokenTenantControlException(commitStarted || !rolledBack ? "UNKNOWN" : "NOT_APPLIED",
                error is ServiceTokenTenantControlException known ? known.Message
                : error is OperationCanceledException ? "cancelled" : "execution-rejected");
        }
        finally
        {
            if (tx is not null)
            {
                // Cleanup must not replace a classified commit result with an unclassified provider error.
                try { await tx.DisposeAsync().ConfigureAwait(false); } catch { }
            }
        }
    }

    private static void Eligibility(NpgsqlCommand command, TenantEligibilityV1 value, string prefix)
    {
        command.Parameters.AddWithValue(prefix + "enabled", value.Enable);
        command.Parameters.AddWithValue(prefix + "expiry", NpgsqlDbType.Timestamp, (object?)value.ExpireDate ?? DBNull.Value);
    }
    private static void Reject(string code) => throw new ServiceTokenTenantControlException("NOT_APPLIED", code);
    private static void ValidateInput(ServiceTokenTenantControlRequestV1 request)
    {
        if (request is null || request.Installation is not { } i || request.TenantId == Guid.Empty
            || i.InstallationId == Guid.Empty || i.DatabaseOid == 0 || string.IsNullOrWhiteSpace(i.Database)
            || !System.Net.IPAddress.TryParse(i.Server, out _) || i.Port is < 1 or > 65535
            || !ulong.TryParse(i.SystemIdentifier, out var system) || system == 0
            || i.Tenants is null || i.Tenants.Length == 0 || i.Tenants.Any(t => t is null)
            || !i.Tenants.Any(t => t.TenantId == request.TenantId)) Reject("input-rejected");
        var valid = request!.Operation switch
        {
            "inspect" => request.TenantCode is null && request.TenantName is null && request.Expected is null && request.Desired is null,
            "initialize" => Text(request.TenantCode, 50) && Text(request.TenantName, 200) && request.Expected is null && request.Desired is not null,
            "set-eligibility" => request.TenantCode is null && request.TenantName is null && request.Expected is not null && request.Desired is not null,
            _ => false
        };
        if (!valid || !Timestamp(request.Expected?.ExpireDate) || !Timestamp(request.Desired?.ExpireDate)) Reject("input-rejected");
    }
    private static bool Text(string? value, int capacity)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > capacity || value.Contains('\0')) return false;
        try { _ = new UTF8Encoding(false, true).GetByteCount(value); return true; } catch (EncoderFallbackException) { return false; }
    }
    // v1 accepts explicit local wall time at PostgreSQL microsecond precision, without silent rounding or timezone conversion.
    private static bool Timestamp(DateTime? value) => value is null || value.Value.Kind == DateTimeKind.Unspecified
        && value.Value != DateTime.MinValue && value.Value != DateTime.MaxValue && value.Value.Ticks % 10 == 0;
}
