using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261030022700_RmaPolicyV1")]
public sealed class RmaPolicyV1 : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE TABLE cp6_rma_formal_v1.policy_heads(tenant_id uuid NOT NULL,id uuid NOT NULL,warehouse_id uuid NOT NULL,site_id uuid NOT NULL,
          version integer NOT NULL CHECK(version>0),PRIMARY KEY(tenant_id,id),UNIQUE(tenant_id,warehouse_id,site_id));
        CREATE TABLE cp6_rma_formal_v1.policy_versions(tenant_id uuid NOT NULL,id uuid NOT NULL,version integer NOT NULL,body bytea NOT NULL,
          PRIMARY KEY(tenant_id,id,version),FOREIGN KEY(tenant_id,id) REFERENCES cp6_rma_formal_v1.policy_heads(tenant_id,id));
        ALTER TABLE cp6_rma_formal_v1.policy_heads ADD CONSTRAINT rma_policy_current_version FOREIGN KEY(tenant_id,id,version)
          REFERENCES cp6_rma_formal_v1.policy_versions(tenant_id,id,version) DEFERRABLE INITIALLY DEFERRED;
        CREATE TRIGGER immutable BEFORE UPDATE OR DELETE ON cp6_rma_formal_v1.policy_versions
          FOR EACH ROW EXECUTE FUNCTION cp6_rma_formal_v1.immutable();
        CREATE TRIGGER no_truncate BEFORE TRUNCATE ON cp6_rma_formal_v1.policy_versions
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_rma_formal_v1.immutable();
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",'policy-publish','发布/停用退货政策',25,t."Id",LOCALTIMESTAMP
        FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t
        WHERE m."MenuKey"='wms-rma' AND NOT EXISTS(SELECT FROM public."Sys_MenuAction" x
          WHERE x."TenantId"=t."Id" AND x."MenuId"=m."MenuId" AND x."ActionCode"='policy-publish');
        """);
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Published RMA policy history requires forward-only migration.");
}
