using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context))]
[Migration("20261114030500_StockOrdinaryWriteAuthorityV1")]
public sealed class StockOrdinaryWriteAuthorityV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        -- In a fresh database the Stock writer roles are provisioned after this migration.
        CREATE SCHEMA cp6_stock_ordinary_authority_v1;
        REVOKE ALL ON SCHEMA cp6_stock_ordinary_authority_v1 FROM PUBLIC;

        CREATE FUNCTION cp6_stock_ordinary_authority_v1.before_write() RETURNS trigger
        LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $cp6$
        DECLARE
            acquire_oid oid := to_regprocedure('cp6_stock_write_fence_v1.acquire(uuid)');
            own_tx xid8 := pg_current_xact_id_if_assigned();
            tenant uuid;
        BEGIN
            -- Only the ordinary Stock roles with the installed explicit grant take this path.
            -- The Core Owner keeps its existing authority contract.
            IF NOT EXISTS (
                SELECT FROM pg_proc p CROSS JOIN LATERAL aclexplode(p.proacl) a
                JOIN pg_roles r ON r.oid=a.grantee
                WHERE p.oid=acquire_oid AND a.privilege_type='EXECUTE' AND r.rolname=session_user
            ) THEN RETURN NULL; END IF;

            -- Observe the row version updated by the authority root in this very transaction.
            -- Never acquire the root here: the write statement has already locked its relation.
            IF own_tx IS NULL OR NOT EXISTS (
                SELECT FROM cp6_platform_admin_guard_v1.root
                WHERE singleton AND xmin=own_tx::xid
            ) THEN
                RAISE EXCEPTION 'STOCK_WRITE_AUTHORITY_REQUIRED' USING ERRCODE='42501';
            END IF;

            SELECT cp6_tenancy_v1.current_tenant_id() INTO tenant;
            IF NOT EXISTS (SELECT FROM public."Sys_Tenants" t WHERE t."Id"=tenant AND t."Enable") THEN
                RAISE EXCEPTION 'STOCK_WRITE_TENANT_UNAVAILABLE' USING ERRCODE='42501';
            END IF;
            RETURN NULL;
        END $cp6$;

        REVOKE ALL ON FUNCTION cp6_stock_ordinary_authority_v1.before_write() FROM PUBLIC;
        CREATE TRIGGER stock_ordinary_authority_v1 BEFORE INSERT OR UPDATE ON public."T_Stock"
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_stock_ordinary_authority_v1.before_write();
        CREATE TRIGGER stock_ordinary_authority_v1 BEFORE INSERT ON public."T_StockTransaction"
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_stock_ordinary_authority_v1.before_write();
        CREATE TRIGGER stock_ordinary_authority_v1 BEFORE INSERT OR UPDATE ON public."T_WmsSequence"
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_stock_ordinary_authority_v1.before_write();
        CREATE TRIGGER stock_ordinary_authority_v1 BEFORE INSERT ON cp6_stock_return_v1.movement_sequences
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_stock_ordinary_authority_v1.before_write();
        """);

    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("Ordinary Stock write authority requires forward recovery.");
}
