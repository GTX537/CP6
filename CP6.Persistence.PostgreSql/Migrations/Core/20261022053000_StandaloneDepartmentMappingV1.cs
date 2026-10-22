using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261022053000_StandaloneDepartmentMappingV1")]
public sealed class StandaloneDepartmentMappingV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE SCHEMA cp6_operations_owner_v1;
        REVOKE ALL ON SCHEMA cp6_operations_owner_v1 FROM PUBLIC;
        -- Explicit per-object identity; no inferred ownership or real-data backfill.
        CREATE TABLE cp6_operations_owner_v1.department_mappings (
          id uuid PRIMARY KEY, tenant_id uuid NOT NULL REFERENCES public."Sys_Tenants"("Id"),
          object_kind text COLLATE "C" NOT NULL CHECK(object_kind IN ('stock','stock-transaction','stocktake','work-order')),
          object_id uuid NOT NULL CHECK(object_id<>'00000000-0000-0000-0000-000000000000'::uuid),
          department_id uuid NOT NULL REFERENCES public."Sys_Dept"("Id") ON DELETE RESTRICT,
          enabled boolean NOT NULL, revision uuid NOT NULL DEFAULT gen_random_uuid(),
          UNIQUE(tenant_id,object_kind,object_id),
          CHECK(tenant_id<>'00000000-0000-0000-0000-000000000000'::uuid));
        CREATE TRIGGER cp6_authentication_gate_v1 BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE
          ON cp6_operations_owner_v1.department_mappings FOR EACH STATEMENT
          EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE FUNCTION cp6_operations_owner_v1.mapping_validate() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF NOT EXISTS(SELECT FROM public."Sys_Dept" d WHERE d."Id"=NEW.department_id AND d."TenantId"=NEW.tenant_id) THEN
            RAISE EXCEPTION 'STANDALONE_DEPARTMENT_TENANT_MISMATCH' USING ERRCODE='23514';
          END IF;
          IF TG_OP='UPDATE' AND (OLD.id,OLD.tenant_id,OLD.object_kind,OLD.object_id)
              IS DISTINCT FROM (NEW.id,NEW.tenant_id,NEW.object_kind,NEW.object_id) THEN
            RAISE EXCEPTION 'STANDALONE_OBJECT_IMMUTABLE' USING ERRCODE='23514';
          END IF;
          IF TG_OP='INSERT' OR NEW.enabled THEN
            IF NOT (CASE NEW.object_kind
              WHEN 'stock' THEN EXISTS(SELECT FROM public."T_Stock" o WHERE o."Id"=NEW.object_id AND o."TenantId"=NEW.tenant_id AND NOT o."IsDeleted")
              WHEN 'stock-transaction' THEN EXISTS(SELECT FROM public."T_StockTransaction" o WHERE o."Id"=NEW.object_id AND o."TenantId"=NEW.tenant_id AND NOT o."IsDeleted")
              WHEN 'stocktake' THEN EXISTS(SELECT FROM public."T_StockTake" o WHERE o."Id"=NEW.object_id AND o."TenantId"=NEW.tenant_id AND NOT o."IsDeleted")
              WHEN 'work-order' THEN EXISTS(SELECT FROM public."T_WorkOrder" o WHERE o."Id"=NEW.object_id AND o."TenantId"=NEW.tenant_id AND NOT o."IsDeleted" AND (o."WebOrderNo" IS NULL OR o."WebOrderNo"=''))
              ELSE FALSE END) THEN
              RAISE EXCEPTION 'STANDALONE_OBJECT_UNAVAILABLE' USING ERRCODE='23514';
            END IF;
          END IF;
          NEW.revision=gen_random_uuid(); RETURN NEW;
        END $$;
        CREATE TRIGGER mapping_validate BEFORE INSERT OR UPDATE ON cp6_operations_owner_v1.department_mappings
          FOR EACH ROW EXECUTE FUNCTION cp6_operations_owner_v1.mapping_validate();
        CREATE FUNCTION cp6_operations_owner_v1.mapping_revision() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP='TRUNCATE' THEN
            UPDATE cp6_authentication_v1.identity_admission_revisions SET revision=gen_random_uuid()
              WHERE object_key='standalone-department-mapping';
            RETURN NULL;
          END IF;
          IF TG_OP!='INSERT' THEN PERFORM cp6_authentication_v1.bump_admission_revision(OLD.tenant_id,'standalone-department-mapping'); END IF;
          IF TG_OP!='DELETE' THEN PERFORM cp6_authentication_v1.bump_admission_revision(NEW.tenant_id,'standalone-department-mapping'); END IF;
          RETURN NULL;
        END $$;
        CREATE TRIGGER mapping_revision AFTER INSERT OR UPDATE OR DELETE ON cp6_operations_owner_v1.department_mappings
          FOR EACH ROW EXECUTE FUNCTION cp6_operations_owner_v1.mapping_revision();
        CREATE TRIGGER mapping_truncate AFTER TRUNCATE ON cp6_operations_owner_v1.department_mappings
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_operations_owner_v1.mapping_revision();
        REVOKE ALL ON cp6_operations_owner_v1.department_mappings FROM PUBLIC;
        REVOKE ALL ON FUNCTION cp6_operations_owner_v1.mapping_validate() FROM PUBLIC;
        REVOKE ALL ON FUNCTION cp6_operations_owner_v1.mapping_revision() FROM PUBLIC;
        """);

    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("Department authority mappings require explicit forward recovery.");
}
