using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)),Migration("20261027220000_ToolRegistrationV1")]
public sealed class ToolRegistrationV1:Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)=>migrationBuilder.Sql("""
        CREATE TABLE cp6_tool_instance_v1.registrations(
          tenant_id uuid NOT NULL,object_id uuid NOT NULL,native_revision bigint NOT NULL,
          original_json text NOT NULL,original_digest text NOT NULL
            CHECK(original_digest=encode(sha256(convert_to(original_json,'UTF8')),'hex')),
          PRIMARY KEY(tenant_id,object_id),
          FOREIGN KEY(tenant_id,object_id,native_revision) REFERENCES cp6_tool_instance_v1.versions(tenant_id,object_id,revision));
        CREATE TRIGGER tool_registration_immutable BEFORE UPDATE OR DELETE ON cp6_tool_instance_v1.registrations
          FOR EACH ROW EXECUTE FUNCTION cp6_tool_instance_v1.immutable();
        CREATE TRIGGER tool_registration_no_truncate BEFORE TRUNCATE ON cp6_tool_instance_v1.registrations
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_tool_instance_v1.immutable();
        REVOKE ALL ON cp6_tool_instance_v1.registrations FROM PUBLIC;
        -- Definition only. No actor/role receives authority through this migration.
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",'initialize','确认旧实物站点及周期起点',70,t."Id",LOCALTIMESTAMP
        FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t
        WHERE m."MenuKey"='wms-plate-mold' AND NOT EXISTS(SELECT FROM public."Sys_MenuAction" a
          WHERE a."TenantId"=t."Id" AND a."MenuId"=m."MenuId" AND a."ActionCode"='initialize');
        """);
    protected override void Down(MigrationBuilder migrationBuilder)=>throw new NotSupportedException("Tool registration provenance requires forward recovery.");
}
