using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261030022400_RmaFormalApplicationV1")]
public sealed class RmaFormalApplicationV1 : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE SCHEMA cp6_rma_formal_v1;
        REVOKE ALL ON SCHEMA cp6_rma_formal_v1 FROM PUBLIC;
        CREATE TABLE cp6_rma_formal_v1.namespace(singleton boolean PRIMARY KEY CHECK(singleton),environment_id uuid NOT NULL,mode text NOT NULL CHECK(mode IN ('REAL','DEMO')));
        CREATE TABLE cp6_rma_formal_v1.facts(tenant_id uuid NOT NULL,id uuid NOT NULL,actor_id uuid NOT NULL,owner text NOT NULL,
          reference_id text NOT NULL,digest text NOT NULL,body bytea NOT NULL,PRIMARY KEY(tenant_id,id));
        CREATE TABLE cp6_rma_formal_v1.scope_heads(tenant_id uuid NOT NULL,warehouse_id uuid NOT NULL,coordinate_digest text NOT NULL,
          reference jsonb NOT NULL,PRIMARY KEY(tenant_id,warehouse_id));
        CREATE TABLE cp6_rma_formal_v1.heads(tenant_id uuid NOT NULL,id uuid NOT NULL,actor_id uuid NOT NULL,version integer NOT NULL CHECK(version>0),PRIMARY KEY(tenant_id,id));
        CREATE TABLE cp6_rma_formal_v1.versions(tenant_id uuid NOT NULL,id uuid NOT NULL,version integer NOT NULL,body bytea NOT NULL,
          PRIMARY KEY(tenant_id,id,version),FOREIGN KEY(tenant_id,id) REFERENCES cp6_rma_formal_v1.heads(tenant_id,id));
        ALTER TABLE cp6_rma_formal_v1.heads ADD CONSTRAINT rma_formal_current_version FOREIGN KEY(tenant_id,id,version)
          REFERENCES cp6_rma_formal_v1.versions(tenant_id,id,version) DEFERRABLE INITIALLY DEFERRED;
        CREATE FUNCTION cp6_rma_formal_v1.immutable() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN RAISE EXCEPTION 'RMA_FORMAL_FACT_IMMUTABLE' USING ERRCODE='23514'; END $$;
        DO $$ DECLARE tab text; BEGIN FOREACH tab IN ARRAY ARRAY['namespace','facts','versions'] LOOP
          EXECUTE format('CREATE TRIGGER immutable BEFORE UPDATE OR DELETE ON cp6_rma_formal_v1.%I FOR EACH ROW EXECUTE FUNCTION cp6_rma_formal_v1.immutable()',tab);
          EXECUTE format('CREATE TRIGGER no_truncate BEFORE TRUNCATE ON cp6_rma_formal_v1.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_rma_formal_v1.immutable()',tab);
        END LOOP; END $$;
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",a.code,a.label,20,t."Id",LOCALTIMESTAMP
        FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t
        CROSS JOIN (VALUES('application-write','创建/编辑正式退货申请'),('application-submit','提交正式退货申请'),('application-withdraw','撤回正式退货申请')) a(code,label)
        WHERE m."MenuKey"='wms-rma' AND NOT EXISTS(SELECT FROM public."Sys_MenuAction" x
          WHERE x."TenantId"=t."Id" AND x."MenuId"=m."MenuId" AND x."ActionCode"=a.code);
        """);
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Formal RMA facts require forward-only migration.");
}
