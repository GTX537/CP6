using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Core.Persistence.Tenancy;

internal static class StockAdjustOwnerStorageV1
{
    internal const string Contract = "stock-adjust-owner-v1";

    internal static async Task InstallAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid readInstallationId, IReadOnlyCollection<TenantLoginV1> writers, CancellationToken ct = default)
    {
        var roles = await StockWriteAuthorityFenceV1.PrepareInstallAsync(connection, transaction, readInstallationId,
            writers, requireFence: true, ct);
        await using (var preflight = new NpgsqlCommand("""
            SELECT NOT EXISTS (SELECT FROM pg_catalog.pg_roles r WHERE r.rolname::text=ANY(@logins)
                AND (pg_catalog.has_schema_privilege(r.oid, pg_catalog.to_regnamespace('cp6_stock_movement_writer_v1'), 'USAGE')
                    AND pg_catalog.has_function_privilege(r.oid, pg_catalog.to_regprocedure(
                        'cp6_stock_movement_writer_v1.record_department(uuid,uuid,uuid,uuid)'), 'EXECUTE')
                    AND pg_catalog.has_column_privilege(r.oid, 'public."T_Stock"', 'PhysicalQty', 'UPDATE')) IS DISTINCT FROM true)
            """, connection, transaction))
        {
            preflight.Parameters.AddWithValue("logins", writers.Select(binding => binding.Login).ToArray());
            if (await preflight.ExecuteScalarAsync(ct) is not true)
                throw new TenantStorageException(TenantStorageFailure.ConfigurationRejected);
        }

        var options = new DbContextOptionsBuilder<CP6Context>().UseNpgsql(connection, contextOwnsConnection: false);
        DatabaseContextOptions.ConfigurePostgreSql(options);
        await using var model = new StockMovementWriteContextV1(options.Options, Guid.Empty);
        var (business, masters) = OperationsOwnerV1.GraphTableNames(model, OperationsOwnerV1.Stock);
        var businessNames = string.Join(", ", business);
        var masterNames = string.Join(", ", masters);
        // Match the legacy Owner lock modes and order without granting the ordinary login any additional table rights.
        await using var install = new NpgsqlCommand($$"""
            CREATE ROLE cp6_stock_adjust_owner_executor_v1
                NOLOGIN NOINHERIT NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION;
            GRANT USAGE ON SCHEMA public, cp6_stock_write_fence_v1 TO cp6_stock_adjust_owner_executor_v1;
            GRANT EXECUTE ON FUNCTION cp6_stock_write_fence_v1.acquire(uuid) TO cp6_stock_adjust_owner_executor_v1;
            GRANT MAINTAIN ON {{businessNames}}, {{masterNames}} TO cp6_stock_adjust_owner_executor_v1;

            CREATE SCHEMA cp6_stock_adjust_owner_v1;
            REVOKE ALL ON SCHEMA cp6_stock_adjust_owner_v1 FROM PUBLIC;
            CREATE FUNCTION cp6_stock_adjust_owner_v1.lock_graph(expected uuid)
            RETURNS void LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog, pg_temp AS $cp6$
            BEGIN
                PERFORM cp6_stock_write_fence_v1.acquire(expected);
                SET LOCAL lock_timeout = '10s';
                LOCK TABLE {{businessNames}} IN SHARE ROW EXCLUSIVE MODE;
                LOCK TABLE {{masterNames}} IN SHARE MODE;
            END $cp6$;
            ALTER FUNCTION cp6_stock_adjust_owner_v1.lock_graph(uuid) OWNER TO cp6_stock_adjust_owner_executor_v1;
            REVOKE ALL ON FUNCTION cp6_stock_adjust_owner_v1.lock_graph(uuid) FROM PUBLIC;
            GRANT USAGE ON SCHEMA cp6_stock_adjust_owner_v1 TO {{roles}};
            GRANT EXECUTE ON FUNCTION cp6_stock_adjust_owner_v1.lock_graph(uuid) TO {{roles}};
            """, connection, transaction);
        await install.ExecuteNonQueryAsync(ct);
    }
}
