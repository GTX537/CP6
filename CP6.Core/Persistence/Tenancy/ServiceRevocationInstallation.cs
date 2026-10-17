using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;

namespace CP6.Core.Persistence.Tenancy;

internal sealed record ServiceRevocationInstallRequest(Guid InstallationId, string Server, int Port, string Database,
    uint DatabaseOid, string SystemIdentifier, string IssuerLogin, TenantLoginV1[] Tenants);
internal sealed record ServiceRevocationInstallResult(Guid InstallationId, string Profile, uint DatabaseOid,
    string SystemIdentifier, string BaselineSha256);

/// <summary>Explicit fresh-layout control operation. Existing profiles/data are never adopted or modified.</summary>
internal static class ServiceRevocationInstallation
{
    internal static async Task<ServiceRevocationInstallResult> InstallAsync(NpgsqlConnection connection,
        ServiceRevocationInstallRequest request, string baseline, CancellationToken ct = default)
        => await InstallCoreAsync(connection, request, baseline, false, ct).ConfigureAwait(false);

    internal static Task<ServiceRevocationInstallResult> InstallServiceTokenAsync(NpgsqlConnection connection,
        ServiceTokenInstallRequest request, string baseline, CancellationToken ct = default) =>
        InstallCoreAsync(connection, new(request.InstallationId, request.Server, request.Port, request.Database,
            request.DatabaseOid, request.SystemIdentifier, "", request.Tenants), baseline, true, ct);

    private static async Task<ServiceRevocationInstallResult> InstallCoreAsync(NpgsqlConnection connection,
        ServiceRevocationInstallRequest request, string baseline, bool serviceTokens, CancellationToken ct)
    {
        var schema = serviceTokens ? ServiceTokenProfileV1.Schema : "cp6_service_revocation_v1";
        var profile = serviceTokens ? ServiceTokenProfileV1.Profile : ServiceRevocationTransaction.Profile;
        if (request.InstallationId == Guid.Empty || request.DatabaseOid == 0 || request.Tenants.Length == 0
            || !System.Net.IPAddress.TryParse(request.Server, out _) || request.Port is < 1 or > 65535
            || !ulong.TryParse(request.SystemIdentifier, out var system) || system == 0
            || !serviceTokens && (string.IsNullOrWhiteSpace(request.IssuerLogin) || request.Tenants.Any(t => t.Login == request.IssuerLogin)))
            throw TenantStorageBindingV1.Rejected();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync(ct).ConfigureAwait(false);
        var settings = new NpgsqlConnectionStringBuilder(connection.ConnectionString);
        if (settings.Host != request.Server || settings.Port != request.Port || settings.Database != request.Database
            || settings.SearchPath != "pg_catalog" || settings.Username == request.IssuerLogin
            || request.Tenants.Any(t => t.Login == settings.Username)) throw TenantStorageBindingV1.Rejected();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(baseline)));
        var canonical = JsonSerializer.Serialize(request with { Tenants = request.Tenants.OrderBy(t => t.TenantId).ToArray() });
        var result = new ServiceRevocationInstallResult(request.InstallationId, profile,
            request.DatabaseOid, request.SystemIdentifier, hash);
        await using var tx = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        TenantLoginDirectoryV1.ValidateArguments(connection, tx, request.Tenants);
        await Execute("SELECT pg_catalog.pg_advisory_xact_lock(1129330260, 2)");
        await using (var identity = new NpgsqlCommand(Sql("""
            SELECT r.rolsuper AND session_user=current_user AND d.datdba=r.oid AND d.oid=@oid
              AND c.system_identifier::text=@system AND pg_catalog.host(pg_catalog.inet_server_addr())=@server
              AND pg_catalog.inet_server_port()=@port
            FROM pg_catalog.pg_roles r, pg_catalog.pg_database d, pg_catalog.pg_control_system() c
            WHERE r.rolname=current_user AND d.datname=current_database()
            """), connection, tx))
        {
            identity.Parameters.AddWithValue("oid", NpgsqlTypes.NpgsqlDbType.Oid, request.DatabaseOid);
            identity.Parameters.AddWithValue("system", request.SystemIdentifier); identity.Parameters.AddWithValue("server", request.Server);
            identity.Parameters.AddWithValue("port", request.Port);
            if (await identity.ExecuteScalarAsync(ct).ConfigureAwait(false) is not true) throw TenantStorageBindingV1.Rejected();
        }
        var exists = await Scalar("SELECT EXISTS(SELECT FROM pg_catalog.pg_namespace WHERE nspname='cp6_service_revocation_v1')") is true;
        if (exists)
        {
            if (await Scalar("""
                SELECT EXISTS(SELECT FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace
                  WHERE n.nspname='cp6_service_revocation_v1' AND n.nspowner=current_user::regrole
                    AND c.relname='installation' AND c.relkind='r' AND c.relowner=current_user::regrole
                    AND NOT c.relrowsecurity AND NOT c.relforcerowsecurity
                    AND NOT EXISTS(SELECT FROM pg_catalog.pg_trigger WHERE tgrelid=c.oid AND NOT tgisinternal)
                    AND NOT EXISTS(SELECT FROM pg_catalog.pg_rewrite WHERE ev_class=c.oid))
                """) is not true) throw TenantStorageBindingV1.Rejected();
            var actualSeal = await CatalogHash().ConfigureAwait(false);
            await using var previous = new NpgsqlCommand(Sql("""
                SELECT issuer_oid FROM cp6_service_revocation_v1.installation
                WHERE singleton AND installation_id=@id AND canonical=@canonical AND baseline_sha256=@hash AND fingerprint=@seal
                  AND profile=@profile AND database_oid=@oid AND system_identifier=@system AND issuer_name=@issuer
                """), connection, tx);
            previous.Parameters.AddWithValue("canonical", canonical); previous.Parameters.AddWithValue("hash", hash);
            previous.Parameters.AddWithValue("id", request.InstallationId);
            previous.Parameters.AddWithValue("seal", actualSeal);
            previous.Parameters.AddWithValue("profile", result.Profile);
            previous.Parameters.AddWithValue("oid", NpgsqlTypes.NpgsqlDbType.Oid, request.DatabaseOid);
            previous.Parameters.AddWithValue("system", request.SystemIdentifier); previous.Parameters.AddWithValue("issuer", request.IssuerLogin);
            if (await previous.ExecuteScalarAsync(ct).ConfigureAwait(false) is not uint issuerOid) throw TenantStorageBindingV1.Rejected();
            // The catalog seal deliberately excludes row data and the separate issuance identity.
            await TenantLoginDirectoryV1.ValidateAsync(connection, tx, request.Tenants, ct).ConfigureAwait(false);
            await ValidateIssuer(issuerOid, installed: true).ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false); return result;
        }
        // New profile only. No quiet takeover of a partial/full Core baseline or old recovery seal.
        if (await Scalar(TenantEmptyLayoutV1.Sql + " AND NOT EXISTS(SELECT FROM pg_catalog.pg_default_acl)") is not true)
            throw TenantStorageBindingV1.Rejected();
        var issuanceOid = await ValidateIssuer(null, installed: false).ConfigureAwait(false);
        foreach (var login in Logins())
        {
            if (serviceTokens)
                await ServiceTokenProfileV1.ValidateFreshLoginAsync(connection, tx, login, ct).ConfigureAwait(false);
            await using var role = new NpgsqlCommand(Sql("""
                SELECT NOT (r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication)
                  AND r.rolcanlogin AND NOT EXISTS(SELECT FROM pg_catalog.pg_auth_members WHERE member=r.oid OR roleid=r.oid)
                  AND NOT EXISTS(SELECT FROM pg_catalog.pg_database WHERE datdba=r.oid)
                  AND NOT EXISTS(SELECT FROM pg_catalog.pg_namespace WHERE nspowner=r.oid)
                  AND NOT EXISTS(SELECT FROM pg_catalog.pg_class WHERE relowner=r.oid)
                  AND NOT EXISTS(SELECT FROM pg_catalog.pg_proc WHERE proowner=r.oid)
                  AND NOT EXISTS(SELECT FROM pg_catalog.pg_db_role_setting WHERE setrole=r.oid)
                FROM pg_catalog.pg_roles r WHERE r.rolname=@login
                """), connection, tx);
            role.Parameters.AddWithValue("login", login);
            if (await role.ExecuteScalarAsync(ct).ConfigureAwait(false) is not true) throw TenantStorageBindingV1.Rejected();
        }
        await Execute("SET LOCAL search_path=public");
        await Execute(baseline);
        await Execute("SET LOCAL search_path=pg_catalog");
        await TenantLoginDirectoryV1.InstallAsync(connection, tx, request.Tenants, ct).ConfigureAwait(false);
        var roles = string.Join(',', request.Tenants.Select(t => Q(t.Login)));
        var issuer = serviceTokens ? roles : Q(request.IssuerLogin);
        await Execute($$"""
            REVOKE CREATE,TEMPORARY ON DATABASE {{Q(request.Database)}} FROM PUBLIC;
            REVOKE ALL ON SCHEMA public,crm_identity,crm_identity_priority,cp6_storage_core,cp6_storage_identity FROM PUBLIC;
            REVOKE ALL ON ALL FUNCTIONS IN SCHEMA public,cp6_storage_core,cp6_storage_identity FROM PUBLIC;
            CREATE SCHEMA cp6_service_revocation_v1;
            REVOKE ALL ON SCHEMA cp6_service_revocation_v1 FROM PUBLIC;
            CREATE TABLE cp6_service_revocation_v1.installation(
              singleton boolean PRIMARY KEY CHECK(singleton),installation_id uuid NOT NULL,profile text NOT NULL,
              database_oid oid NOT NULL,system_identifier text NOT NULL,baseline_sha256 text NOT NULL,
              canonical text NOT NULL,fingerprint text NOT NULL,issuer_oid oid NOT NULL,issuer_name name NOT NULL);
            REVOKE ALL ON cp6_service_revocation_v1.installation FROM PUBLIC;
            CREATE FUNCTION cp6_service_revocation_v1.system_identifier() RETURNS text
            LANGUAGE sql SECURITY DEFINER SET search_path=pg_catalog
            AS 'SELECT system_identifier::text FROM pg_catalog.pg_control_system()';
            CREATE FUNCTION cp6_service_revocation_v1.fingerprint() RETURNS text
            LANGUAGE sql SECURITY DEFINER SET search_path=pg_catalog
            AS $seal$ SELECT upper(encode(sha256(convert_to(({{(serviceTokens ? ServiceTokenProfileV1.CatalogSql : TenantProvisioningCatalog.Sql)}}),'UTF8')),'hex')) $seal$;
            REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_service_revocation_v1 FROM PUBLIC;
            GRANT CONNECT ON DATABASE {{Q(request.Database)}} TO {{roles}},{{issuer}};
            GRANT USAGE ON SCHEMA public,crm_identity,cp6_storage_core TO {{roles}},{{issuer}};
            GRANT USAGE ON SCHEMA cp6_tenancy_v1,crm_identity_priority,cp6_storage_identity,cp6_service_revocation_v1 TO {{roles}};
            GRANT EXECUTE ON FUNCTION cp6_tenancy_v1.current_tenant_id(),cp6_service_revocation_v1.system_identifier(),
              cp6_service_revocation_v1.fingerprint() TO {{roles}};
            GRANT SELECT(installation_id,profile,database_oid,singleton,fingerprint) ON cp6_service_revocation_v1.installation TO {{roles}};
            GRANT EXECUTE ON FUNCTION public.cp6_cp936_length_v1(text),cp6_storage_core.set_rowversion_v1() TO {{roles}},{{issuer}};
            GRANT EXECUTE ON FUNCTION cp6_storage_core.advance_identity_generation_v1(),cp6_storage_identity.set_rowversion_v1() TO {{roles}};
            GRANT USAGE ON SEQUENCE cp6_storage_core.rowversion_v1 TO {{roles}},{{issuer}};
            GRANT USAGE ON SEQUENCE cp6_storage_identity.rowversion_v1 TO {{roles}};
            """);
        foreach (var table in new[] { "crm_identity.\"ServiceToken\"", "crm_identity.\"Snapshot\"", "public.\"CrmIdentityTenantGenerations\"", "crm_identity_priority.\"Cp6_OutboxMessage\"" })
        {
            var predicate = "\"TenantId\"=cp6_tenancy_v1.current_tenant_id()";
            await Execute($$"""
                ALTER TABLE {{table}} ENABLE ROW LEVEL SECURITY;
                ALTER TABLE {{table}} FORCE ROW LEVEL SECURITY;
                CREATE POLICY revoke_select_v1 ON {{table}} FOR SELECT TO {{roles}} USING ({{predicate}});
                CREATE POLICY revoke_insert_v1 ON {{table}} FOR INSERT TO {{roles}} WITH CHECK ({{predicate}});
                CREATE POLICY revoke_update_v1 ON {{table}} FOR UPDATE TO {{roles}} USING ({{predicate}}) WITH CHECK ({{predicate}});
                """);
        }
        if (serviceTokens)
        {
            await Execute("REVOKE ALL ON public.\"Sys_Tenants\" FROM PUBLIC");
            await Execute(ServiceTokenProfileV1.EligibilitySql);
            await Execute($"GRANT EXECUTE ON FUNCTION {schema}.current_tenant_eligibility() TO {roles}");
            await Execute($"GRANT INSERT ON crm_identity.\"ServiceToken\" TO {roles}");
        }
        await Execute($$"""
            GRANT SELECT,UPDATE("RevokedAtUtc") ON crm_identity."ServiceToken" TO {{roles}};
            GRANT SELECT,INSERT,UPDATE ON crm_identity."Snapshot",public."CrmIdentityTenantGenerations" TO {{roles}};
            GRANT SELECT,INSERT ON crm_identity_priority."Cp6_OutboxMessage" TO {{roles}};
            """);
        if (!serviceTokens) await Execute($$"""
            GRANT INSERT,SELECT ON crm_identity."ServiceToken" TO {{issuer}};
            CREATE POLICY issue_insert_v1 ON crm_identity."ServiceToken" FOR INSERT TO {{issuer}} WITH CHECK (true);
            CREATE POLICY issue_select_v1 ON crm_identity."ServiceToken" FOR SELECT TO {{issuer}} USING (true);
            """);
        await TenantLoginDirectoryV1.ValidateAsync(connection, tx, request.Tenants, ct).ConfigureAwait(false);
        foreach (var login in Logins())
        {
            await using var rights = new NpgsqlCommand(Sql("""
                SELECT NOT pg_catalog.has_database_privilege(@login,current_database(),'CREATE')
                  AND NOT pg_catalog.has_database_privilege(@login,current_database(),'TEMP')
                  AND NOT EXISTS(SELECT FROM pg_catalog.pg_namespace n WHERE n.nspname !~ '^pg_'
                    AND pg_catalog.has_schema_privilege(@login,n.oid,'CREATE'))
                """), connection, tx);
            rights.Parameters.AddWithValue("login", login);
            if (await rights.ExecuteScalarAsync(ct).ConfigureAwait(false) is not true) throw TenantStorageBindingV1.Rejected();
        }
        await ValidateIssuer(issuanceOid, installed: true).ConfigureAwait(false);
        // Keep the old catalog-seal contract; issuance identity and directory data have explicit checks.
        await using (var save = new NpgsqlCommand(Sql("""
            INSERT INTO cp6_service_revocation_v1.installation VALUES(true,@id,@profile,@oid,@system,@hash,@canonical,@seal,@issuerOid,@issuer)
            """), connection, tx))
        {
            save.Parameters.AddWithValue("id", request.InstallationId); save.Parameters.AddWithValue("profile", result.Profile);
            save.Parameters.AddWithValue("oid", NpgsqlTypes.NpgsqlDbType.Oid, request.DatabaseOid); save.Parameters.AddWithValue("system", request.SystemIdentifier);
            save.Parameters.AddWithValue("hash", hash); save.Parameters.AddWithValue("canonical", canonical);
            save.Parameters.AddWithValue("issuerOid", NpgsqlTypes.NpgsqlDbType.Oid, issuanceOid); save.Parameters.AddWithValue("issuer", request.IssuerLogin);
            save.Parameters.AddWithValue("seal", await CatalogHash().ConfigureAwait(false));
            await save.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await tx.CommitAsync(ct).ConfigureAwait(false);
        return result;

        Task<string> CatalogHash() => serviceTokens ? ServiceTokenProfileV1.HashAsync(connection, tx, ct)
            : TenantProvisioningCatalog.HashAsync(connection, tx, ct);
        string Sql(string sql) => serviceTokens ? sql.Replace("cp6_service_revocation_v1", schema, StringComparison.Ordinal) : sql;
        IEnumerable<string> Logins() => serviceTokens ? request.Tenants.Select(t => t.Login)
            : request.Tenants.Select(t => t.Login).Append(request.IssuerLogin);
        Task<uint> ValidateIssuer(uint? expected, bool installed) => serviceTokens
            ? Task.FromResult(0u)
            : ServiceRevocationIssuerV1.ValidateAsync(connection, tx, request.IssuerLogin, expected, installed, ct);
        async Task Execute(string sql) { await using var command = new NpgsqlCommand(Sql(sql), connection, tx); await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false); }
        async Task<object?> Scalar(string sql) { await using var command = new NpgsqlCommand(Sql(sql), connection, tx); return await command.ExecuteScalarAsync(ct).ConfigureAwait(false); }
    }
    private static string Q(string identifier) => new NpgsqlCommandBuilder().QuoteIdentifier(identifier);
}
