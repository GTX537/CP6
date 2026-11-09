using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using static CP6.Core.Persistence.Tenancy.TenantStorageProvisioner;

namespace CP6.Core.Persistence.Tenancy;

internal sealed record RmaCoreCommissionRequestV1(
    RmaTenantTargetV1 Target, Guid InstallationId, Guid OperationId, TenantLoginV1[] Tenants);

internal static partial class RmaTenantStorageV1
{
    private const string CoreProfile = "rma-application-core-tenant-v1";
    private const string CoreControl = "cp6_rma_core_commission_v1";
    private const string RmaOwner = "Main.Wms.RmaApplication.v1";

    internal static async Task<string> PrepareCoreAsync(NpgsqlConnection connection, RmaCoreCommissionRequestV1 request,
        CancellationToken ct)
    {
        ValidateCoreRequest(request);
        await connection.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        await RequireControlTarget(connection, tx, request.Target, ct);
        TenantLoginDirectoryV1.ValidateArguments(connection, tx, request.Tenants);
        await Sql(connection, tx, $"SELECT pg_catalog.pg_advisory_xact_lock({TenantLoginDirectoryV1.DirectoryLockNamespace},4)", ct);
        if (await CorePreparationExistsAsync(connection, tx, ct))
            return (await ReadCorePreparationAsync(connection, tx, request, ct)).Prepared;

        if (await Scalar(connection, tx, TenantEmptyLayoutV1.Sql
                + " AND NOT EXISTS (SELECT FROM pg_catalog.pg_default_acl)", ct) is not true)
            throw new TenantProvisioningException("fresh-layout-required");

        var result = JsonSerializer.Serialize(new
        {
            Outcome = "PREPARED",
            Profile = CoreProfile,
            request.InstallationId,
            request.OperationId,
            request.Target.DatabaseOid,
            request.Target.SystemIdentifier,
            PreparedAt = DateTimeOffset.UtcNow
        });
        await Sql(connection, tx, $"""
            CREATE SCHEMA {CoreControl};
            REVOKE ALL ON SCHEMA {CoreControl} FROM PUBLIC;
            CREATE TABLE {CoreControl}.installation (
                singleton boolean PRIMARY KEY CHECK (singleton),
                request_json text NOT NULL, prepared_receipt text NOT NULL, commission_receipt text
            );
            REVOKE ALL ON {CoreControl}.installation FROM PUBLIC;
            """, ct);
        await using var save = new NpgsqlCommand($"""
            INSERT INTO {CoreControl}.installation VALUES (true,@request,@receipt,NULL)
            """, connection, tx);
        save.Parameters.AddWithValue("request", CanonicalCoreRequest(request));
        save.Parameters.AddWithValue("receipt", result);
        await save.ExecuteNonQueryAsync(ct);
        await Commit(tx, ct);
        return result;
    }

    internal static async Task<string> CommissionCoreAsync(NpgsqlConnection connection, RmaCoreCommissionRequestV1 request,
        CancellationToken ct)
    {
        ValidateCoreRequest(request);
        await connection.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        await RequireControlTarget(connection, tx, request.Target, ct);
        TenantLoginDirectoryV1.ValidateArguments(connection, tx, request.Tenants);
        await Sql(connection, tx, $"SELECT pg_catalog.pg_advisory_xact_lock({TenantLoginDirectoryV1.DirectoryLockNamespace},4)", ct);
        if (!await CorePreparationExistsAsync(connection, tx, ct))
            throw new TenantProvisioningException("core-preparation-required");
        var (_, previous) = await ReadCorePreparationAsync(connection, tx, request, ct);
        await RequireCoreTablesAsync(connection, tx, ct);

        // Match the legacy Owner order: authentication fence, then the affected business and result relations.
        await Sql(connection, tx, """
            SELECT cp6_authentication_v1.acquire();
            LOCK TABLE public."__EFMigrationsHistory" IN SHARE MODE;
            LOCK TABLE cp6_rma_application_v1.heads, cp6_rma_application_v1.versions IN ACCESS EXCLUSIVE MODE;
            LOCK TABLE cp6_main_owner_v1.operations IN SHARE ROW EXCLUSIVE MODE;
            """, ct);
        var history = await RequireCoreHistoryAsync(connection, tx, ct);
        if (await Scalar(connection, tx, $"""
                SELECT NOT EXISTS (SELECT FROM cp6_rma_application_v1.heads)
                   AND NOT EXISTS (SELECT FROM cp6_rma_application_v1.versions)
                   AND NOT EXISTS (SELECT FROM cp6_main_owner_v1.operations WHERE owner='{RmaOwner}')
                """, ct) is not true)
            throw new TenantProvisioningException("shared-rma-not-empty");

        var tenants = request.Tenants.OrderBy(t => t.TenantId).ToArray();
        if (previous is not null)
        {
            await RequireLegacyFencesAsync(connection, tx, ct);
            foreach (var tenant in tenants)
                await RequireCommissionedTenantAsync(connection, tx, request, tenant, ct);
            return previous;
        }

        if (await Scalar(connection, tx, $"""
                SELECT to_regnamespace('{Control}') IS NOT NULL
                    OR to_regnamespace('cp6_tenancy_v1') IS NOT NULL
                    OR to_regnamespace('cp6_provisioning_v1') IS NOT NULL
                    OR EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname='{TenantLoginDirectoryV1.HelperOwner}')
                """, ct) is true)
            throw new TenantProvisioningException("core-layout-conflict");

        await DisableLegacyWritesAsync(connection, tx, ct);
        await InstallDirectoryAsync(connection, tx, request.Target.Database, tenants, fullCore: true, ct);
        var install = new RmaTenantInstallRequestV1(request.Target, request.InstallationId, tenants);
        var bindings = JsonSerializer.Serialize(tenants);
        var installation = JsonSerializer.Serialize(new { Outcome = "INSTALLED", request.InstallationId, Profile = CoreProfile, Lineage });
        await InstallControlRowsAsync(connection, tx, install, CoreProfile, bindings, installation, ct);

        var receipts = new List<JsonElement>(tenants.Length);
        foreach (var tenant in tenants)
        {
            var migration = new RmaTenantMigrationRequestV1(request.Target, request.InstallationId, tenant.TenantId, request.OperationId);
            var schema = "t_" + tenant.TenantId.ToString("N");
            var ddl = TenantDdl(tenant.TenantId, Q(schema), Q(tenant.Login), writable: false);
            var receipt = await ApplyTenantAsync(connection, tx, migration, schema, ddl, Sha256(ddl), CoreProfile, ct);
            receipts.Add(JsonSerializer.Deserialize<JsonElement>(receipt));
        }
        var result = JsonSerializer.Serialize(new
        {
            Outcome = "COMMISSIONED",
            Profile = CoreProfile,
            request.InstallationId,
            request.OperationId,
            request.Target.DatabaseOid,
            request.Target.SystemIdentifier,
            CoreMigrationCount = history.Length,
            CoreHistorySha256 = Sha256(string.Join('\n', history)),
            Tenants = receipts,
            CompletedAt = DateTimeOffset.UtcNow
        });
        await using var save = new NpgsqlCommand($"""
            UPDATE {CoreControl}.installation SET commission_receipt=@receipt WHERE singleton AND commission_receipt IS NULL
            """, connection, tx);
        save.Parameters.AddWithValue("receipt", result);
        if (await save.ExecuteNonQueryAsync(ct) != 1)
            throw new TenantProvisioningException("core-preparation-conflict");
        await Commit(tx, ct);
        return result;
    }

    private static void ValidateCoreRequest(RmaCoreCommissionRequestV1 request)
    {
        if (request.InstallationId == Guid.Empty || request.OperationId == Guid.Empty
            || request.Tenants is not { Length: >= 1 and <= 128 })
            throw new TenantProvisioningException("input-rejected");
    }

    private static string CanonicalCoreRequest(RmaCoreCommissionRequestV1 request) =>
        JsonSerializer.Serialize(request with { Tenants = request.Tenants.OrderBy(t => t.TenantId).ToArray() });

    private static async Task<bool> CorePreparationExistsAsync(NpgsqlConnection connection, NpgsqlTransaction tx, CancellationToken ct) =>
        await Scalar(connection, tx, $"SELECT to_regnamespace('{CoreControl}') IS NOT NULL", ct) is true;

    private static async Task<(string Prepared, string? Commissioned)> ReadCorePreparationAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, RmaCoreCommissionRequestV1 request, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand($"""
            SELECT prepared_receipt,commission_receipt FROM {CoreControl}.installation
            WHERE singleton AND request_json=@request
            """, connection, tx);
        command.Parameters.AddWithValue("request", CanonicalCoreRequest(request));
        await using var row = await command.ExecuteReaderAsync(ct);
        if (!await row.ReadAsync(ct))
            throw new TenantProvisioningException("core-preparation-conflict");
        return (row.GetString(0), row.IsDBNull(1) ? null : row.GetString(1));
    }

    private static async Task RequireCoreTablesAsync(NpgsqlConnection connection, NpgsqlTransaction tx, CancellationToken ct)
    {
        if (await Scalar(connection, tx, """
                SELECT (SELECT count(*) FROM pg_catalog.pg_class c WHERE c.relkind='r' AND c.relowner=current_user::regrole
                    AND c.oid IN (to_regclass('public."__EFMigrationsHistory"'),to_regclass('cp6_rma_application_v1.heads'),
                                  to_regclass('cp6_rma_application_v1.versions'),to_regclass('cp6_main_owner_v1.operations')))=4
                    AND to_regprocedure('cp6_authentication_v1.acquire()') IS NOT NULL
                """, ct) is not true)
            throw new TenantProvisioningException("core-history-required");
    }

    private static async Task<string[]> RequireCoreHistoryAsync(NpgsqlConnection connection, NpgsqlTransaction tx, CancellationToken ct)
    {
        var options = new DatabaseOptions(DatabaseProvider.PostgreSql);
        var profile = DatabaseMigrationProfile.For(options, DatabaseContextKind.Core);
        // EF resolves the exact packaged migration IDs without opening a second connection.
        var settings = new NpgsqlConnectionStringBuilder(connection.ConnectionString) { SearchPath = "public" };
        using var model = new CP6Context(DatabaseContextOptions.Configure(new DbContextOptionsBuilder<CP6Context>(),
            options, settings.ConnectionString, profile.MigrationsAssembly, profile.HistoryTable, profile.HistorySchema).Options);
        var expected = model.Database.GetMigrations().Order(StringComparer.Ordinal).ToArray();
        var observed = await Scalar(connection, tx, """
            SELECT ARRAY(SELECT "MigrationId" FROM public."__EFMigrationsHistory" ORDER BY "MigrationId" COLLATE "C")
            """, ct);
        if (expected.Length == 0 || observed is not string[] actual || !actual.SequenceEqual(expected, StringComparer.Ordinal))
            throw new TenantProvisioningException("core-history-required");
        return actual;
    }

    private static Task DisableLegacyWritesAsync(NpgsqlConnection connection, NpgsqlTransaction tx, CancellationToken ct) =>
        Sql(connection, tx, $"""
            CREATE FUNCTION {CoreControl}.reject_legacy() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $retired$
            BEGIN RAISE EXCEPTION 'RMA_SHARED_AUTHORITY_RETIRED' USING ERRCODE='42501'; END $retired$;
            REVOKE ALL ON FUNCTION {CoreControl}.reject_legacy() FROM PUBLIC;
            CREATE TRIGGER rma_core_legacy_disabled BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE
                ON cp6_rma_application_v1.heads FOR EACH STATEMENT EXECUTE FUNCTION {CoreControl}.reject_legacy();
            CREATE TRIGGER rma_core_legacy_disabled BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE
                ON cp6_rma_application_v1.versions FOR EACH STATEMENT EXECUTE FUNCTION {CoreControl}.reject_legacy();
            CREATE TRIGGER rma_core_legacy_disabled BEFORE INSERT
                ON cp6_main_owner_v1.operations FOR EACH ROW WHEN (NEW.owner='{RmaOwner}')
                EXECUTE FUNCTION {CoreControl}.reject_legacy();
            ALTER TABLE cp6_rma_application_v1.heads ENABLE ALWAYS TRIGGER rma_core_legacy_disabled;
            ALTER TABLE cp6_rma_application_v1.versions ENABLE ALWAYS TRIGGER rma_core_legacy_disabled;
            ALTER TABLE cp6_main_owner_v1.operations ENABLE ALWAYS TRIGGER rma_core_legacy_disabled;
            """, ct);

    private static async Task RequireLegacyFencesAsync(NpgsqlConnection connection, NpgsqlTransaction tx, CancellationToken ct)
    {
        if (await Scalar(connection, tx, $"""
                SELECT count(*)=3 FROM pg_catalog.pg_trigger
                WHERE NOT tgisinternal AND tgname='rma_core_legacy_disabled' AND tgenabled='A'
                  AND tgfoid='{CoreControl}.reject_legacy()'::regprocedure
                  AND tgrelid IN ('cp6_rma_application_v1.heads'::regclass,'cp6_rma_application_v1.versions'::regclass,
                                  'cp6_main_owner_v1.operations'::regclass)
                """, ct) is not true)
            throw new TenantProvisioningException("legacy-fence-mismatch");
    }

    private static async Task RequireCoreRoleIsolationAsync(NpgsqlConnection connection, NpgsqlTransaction tx,
        TenantLoginV1[] tenants, CancellationToken ct)
    {
        // In the full Core layout, inaccessible schemas may still contain functions
        // with default EXECUTE. Prove the new principals have no USAGE on those schemas.
        await using var command = new NpgsqlCommand("""
            SELECT NOT EXISTS (SELECT FROM pg_catalog.pg_namespace n, pg_catalog.pg_roles r
                WHERE r.rolname::text=ANY(@roles) AND n.nspname !~ '^pg_'
                  AND n.nspname NOT IN ('information_schema','cp6_tenancy_v1')
                  AND pg_catalog.has_schema_privilege(r.oid,n.oid,'USAGE'))
            """, connection, tx);
        command.Parameters.AddWithValue("roles", tenants.Select(t => t.Login).Append(TenantLoginDirectoryV1.HelperOwner).ToArray());
        if (await command.ExecuteScalarAsync(ct) is not true)
            throw new TenantProvisioningException("core-runtime-acl-rejected");
    }

    private static async Task RequireCommissionedTenantAsync(NpgsqlConnection connection, NpgsqlTransaction tx,
        RmaCoreCommissionRequestV1 request, TenantLoginV1 tenant, CancellationToken ct)
    {
        var migration = new RmaTenantMigrationRequestV1(request.Target, request.InstallationId, tenant.TenantId, request.OperationId);
        var schema = "t_" + tenant.TenantId.ToString("N");
        var hash = Sha256(TenantDdl(tenant.TenantId, Q(schema), Q(tenant.Login), writable: false));
        await using var command = new NpgsqlCommand($"""
            SELECT result_json FROM {Control}.tenant_migrations
            WHERE tenant_id=@tenant AND lineage=@lineage AND state='Ready'
                AND operation_id=@operation AND migration_id=@migration AND script_sha256=@hash
            """, connection, tx);
        command.Parameters.AddWithValue("tenant", migration.TenantId);
        command.Parameters.AddWithValue("lineage", Lineage);
        command.Parameters.AddWithValue("operation", migration.OperationId);
        command.Parameters.AddWithValue("migration", MigrationId);
        command.Parameters.AddWithValue("hash", hash);
        if (await command.ExecuteScalarAsync(ct) is not string receipt)
            throw new TenantProvisioningException("migration-history-mismatch");
        await RequireTenantHistoryAsync(connection, tx, migration, schema, hash, receipt, ct);
    }

    private static string Sha256(string content) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
}
