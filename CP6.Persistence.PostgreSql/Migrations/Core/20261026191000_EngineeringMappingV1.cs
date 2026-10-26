using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)), Migration("20261026191000_EngineeringMappingV1")]
public sealed class EngineeringMappingV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE cp6_engineering_product_v1.mapping_heads (
          tenant_id uuid NOT NULL,mapping_id uuid NOT NULL CHECK(mapping_id<>'00000000-0000-0000-0000-000000000000'::uuid),
          engineering_id uuid NOT NULL,revision bigint NOT NULL CHECK(revision>0),PRIMARY KEY(tenant_id,mapping_id),
          FOREIGN KEY(tenant_id,engineering_id) REFERENCES cp6_engineering_product_v1.heads(tenant_id,product_id));
        CREATE TABLE cp6_engineering_product_v1.mapping_versions (
          tenant_id uuid NOT NULL,mapping_id uuid NOT NULL,revision bigint NOT NULL CHECK(revision>0),
          body_bytes bytea NOT NULL CHECK(octet_length(body_bytes) BETWEEN 2 AND 262144),
          body_digest text COLLATE "C" NOT NULL CHECK(body_digest=encode(sha256(body_bytes),'hex')),
          recorded_at timestamptz NOT NULL DEFAULT clock_timestamp(),PRIMARY KEY(tenant_id,mapping_id,revision),
          FOREIGN KEY(tenant_id,mapping_id) REFERENCES cp6_engineering_product_v1.mapping_heads DEFERRABLE INITIALLY DEFERRED);
        ALTER TABLE cp6_engineering_product_v1.mapping_heads ADD CONSTRAINT engineering_mapping_original
          FOREIGN KEY(tenant_id,mapping_id,revision) REFERENCES cp6_engineering_product_v1.mapping_versions DEFERRABLE INITIALLY DEFERRED;
        CREATE FUNCTION cp6_engineering_product_v1.mapping_original_validate() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE body jsonb=convert_from(NEW.body_bytes,'UTF8')::jsonb;
        BEGIN
          IF body->>'tenantId' IS DISTINCT FROM NEW.tenant_id::text OR body->>'id' IS DISTINCT FROM NEW.mapping_id::text
             OR body->>'version' IS DISTINCT FROM NEW.revision::text OR COALESCE(body->>'state' NOT IN ('DRAFT','CONFIRMED','DISABLED'),true)
             OR NOT EXISTS(SELECT FROM cp6_engineering_product_v1.mapping_heads h
               WHERE h.tenant_id=NEW.tenant_id AND h.mapping_id=NEW.mapping_id AND h.revision=NEW.revision
               AND h.engineering_id::text=body->>'engineeringId' AND h.engineering_id::text=body#>>'{candidate,engineeringRef,id}') THEN
            RAISE EXCEPTION 'ENGINEERING_MAPPING_ORIGINAL_INVALID' USING ERRCODE='23514';
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER engineering_mapping_advance BEFORE INSERT OR UPDATE ON cp6_engineering_product_v1.mapping_heads
          FOR EACH ROW EXECUTE FUNCTION cp6_engineering_product_v1.advance_head();
        CREATE TRIGGER engineering_mapping_no_delete BEFORE DELETE OR TRUNCATE ON cp6_engineering_product_v1.mapping_heads
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_engineering_product_v1.immutable();
        CREATE TRIGGER engineering_mapping_original_validate BEFORE INSERT ON cp6_engineering_product_v1.mapping_versions
          FOR EACH ROW EXECUTE FUNCTION cp6_engineering_product_v1.mapping_original_validate();
        CREATE TRIGGER engineering_mapping_original_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_engineering_product_v1.mapping_versions
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_engineering_product_v1.immutable();
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_engineering_product_v1 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_engineering_product_v1 FROM PUBLIC;
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",'confirm-engineering','确认经营产品工程映射',90,t."Id",LOCALTIMESTAMP
        FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t
        WHERE m."MenuKey"='erp-product' AND NOT EXISTS(SELECT FROM public."Sys_MenuAction" a
          WHERE a."TenantId"=t."Id" AND a."MenuId"=m."MenuId" AND a."ActionCode"='confirm-engineering');
        """);
    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("Engineering mapping originals require forward recovery.");
}
