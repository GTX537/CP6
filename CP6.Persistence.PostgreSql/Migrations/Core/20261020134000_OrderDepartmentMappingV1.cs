using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261020134000_OrderDepartmentMappingV1")]
public sealed class OrderDepartmentMappingV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE SCHEMA cp6_order_owner_v1;
        REVOKE ALL ON SCHEMA cp6_order_owner_v1 FROM PUBLIC;
        -- Same unbounded bpchar/collation as OrderDepartment; never truncate before validation.
        -- No seed/backfill: equal-looking ERP and IAM codes are not an approved relationship.
        CREATE TABLE cp6_order_owner_v1.department_mappings (
          id uuid PRIMARY KEY, tenant_id uuid NOT NULL REFERENCES public."Sys_Tenants"("Id"),
          code bpchar COLLATE public.cp6_ci_as_provider_v1 NOT NULL,
          department_id uuid NOT NULL REFERENCES public."Sys_Dept"("Id") ON DELETE RESTRICT,
          enabled boolean NOT NULL, revision uuid NOT NULL DEFAULT gen_random_uuid(),
          UNIQUE(tenant_id,code),
          CHECK (tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid),
          CHECK (char_length(regexp_replace(convert_from(pg_catalog.bpcharsend(code),'UTF8') COLLATE "C",
            U&'[\+010000-\+10FFFF]','aa','g')) BETWEEN 1 AND 10),
          CHECK (btrim(convert_from(pg_catalog.bpcharsend(code),'UTF8')) <> ''));
        CREATE TRIGGER cp6_authentication_gate_v1 BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE
          ON cp6_order_owner_v1.department_mappings FOR EACH STATEMENT
          EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE FUNCTION cp6_order_owner_v1.mapping_validate() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF NOT EXISTS(SELECT FROM public."Sys_Dept" d WHERE d."Id"=NEW.department_id AND d."TenantId"=NEW.tenant_id) THEN
            RAISE EXCEPTION 'ORDER_DEPARTMENT_TENANT_MISMATCH' USING ERRCODE='23514';
          END IF;
          NEW.revision=gen_random_uuid(); RETURN NEW;
        END $$;
        CREATE TRIGGER mapping_validate BEFORE INSERT OR UPDATE ON cp6_order_owner_v1.department_mappings
          FOR EACH ROW EXECUTE FUNCTION cp6_order_owner_v1.mapping_validate();
        CREATE FUNCTION cp6_order_owner_v1.mapping_revision() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP='TRUNCATE' THEN
            UPDATE cp6_authentication_v1.identity_admission_revisions SET revision=gen_random_uuid()
              WHERE object_key='order-department-mapping';
            RETURN NULL;
          END IF;
          IF TG_OP!='INSERT' THEN PERFORM cp6_authentication_v1.bump_admission_revision(OLD.tenant_id,'order-department-mapping'); END IF;
          IF TG_OP!='DELETE' THEN PERFORM cp6_authentication_v1.bump_admission_revision(NEW.tenant_id,'order-department-mapping'); END IF;
          RETURN NULL;
        END $$;
        CREATE TRIGGER mapping_revision AFTER INSERT OR UPDATE OR DELETE ON cp6_order_owner_v1.department_mappings
          FOR EACH ROW EXECUTE FUNCTION cp6_order_owner_v1.mapping_revision();
        CREATE TRIGGER mapping_truncate AFTER TRUNCATE ON cp6_order_owner_v1.department_mappings
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_order_owner_v1.mapping_revision();
        REVOKE ALL ON cp6_order_owner_v1.department_mappings FROM PUBLIC;
        REVOKE ALL ON FUNCTION cp6_order_owner_v1.mapping_validate() FROM PUBLIC;
        REVOKE ALL ON FUNCTION cp6_order_owner_v1.mapping_revision() FROM PUBLIC;
        """);

    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("Department authority mappings require explicit forward recovery.");
}
