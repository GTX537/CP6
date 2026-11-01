using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)),Migration("20261101023800_ReplenishDraftsV1")]
public sealed class ReplenishDraftsV1:Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        CREATE TABLE cp6_replenish_v1.order_versions(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,order_id uuid NOT NULL,revision text COLLATE "C" NOT NULL,snapshot_id uuid NOT NULL,
          PRIMARY KEY(tenant_id,environment_id,order_id,revision),UNIQUE(tenant_id,environment_id,snapshot_id),
          FOREIGN KEY(tenant_id,environment_id,order_id) REFERENCES cp6_replenish_v1.documents,
          FOREIGN KEY(tenant_id,environment_id,snapshot_id) REFERENCES cp6_replenish_v1.documents);
        INSERT INTO cp6_replenish_v1.order_versions SELECT tenant_id,environment_id,id,'1',id FROM cp6_replenish_v1.documents WHERE kind='order';
        CREATE TABLE cp6_replenish_v1.order_heads(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,order_id uuid NOT NULL,revision text COLLATE "C" NOT NULL,
          PRIMARY KEY(tenant_id,environment_id,order_id),
          FOREIGN KEY(tenant_id,environment_id,order_id,revision) REFERENCES cp6_replenish_v1.order_versions);
        INSERT INTO cp6_replenish_v1.order_heads SELECT tenant_id,environment_id,order_id,revision FROM cp6_replenish_v1.order_versions;
        CREATE TABLE cp6_replenish_v1.prepare_registrations(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,order_id uuid NOT NULL,selection_version text COLLATE "C" NOT NULL,
          prepare_id uuid NOT NULL,registration_id uuid NOT NULL,
          PRIMARY KEY(tenant_id,environment_id,order_id,selection_version),
          FOREIGN KEY(tenant_id,environment_id,order_id) REFERENCES cp6_replenish_v1.documents,
          FOREIGN KEY(tenant_id,environment_id,prepare_id) REFERENCES cp6_replenish_v1.documents,
          FOREIGN KEY(tenant_id,environment_id,registration_id) REFERENCES cp6_replenish_v1.documents);
        CREATE TRIGGER immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_replenish_v1.order_versions FOR EACH STATEMENT EXECUTE FUNCTION cp6_rma_formal_v1.immutable();
        CREATE TRIGGER immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_replenish_v1.prepare_registrations FOR EACH STATEMENT EXECUTE FUNCTION cp6_rma_formal_v1.immutable();
        REVOKE ALL ON cp6_replenish_v1.order_heads,cp6_replenish_v1.order_versions,cp6_replenish_v1.prepare_registrations FROM PUBLIC;
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",a.code,a.name,a.sort,t."Id",LOCALTIMESTAMP
        FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t
        CROSS JOIN (VALUES('edit','编辑补货草稿',21),('prepare','准备补货',22),('expedite','补货加急',23)) a(code,name,sort)
        WHERE m."MenuKey"='wms-replenish' AND NOT EXISTS(SELECT 1 FROM public."Sys_MenuAction" x WHERE x."MenuId"=m."MenuId" AND x."TenantId"=t."Id" AND x."ActionCode"=a.code);
        """);
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Forward-only business history.");
}
