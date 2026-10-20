using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)),Migration("20261020061000_IamPermissionCatalogV1")]
public sealed class IamPermissionCatalogV1:Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)=>migrationBuilder.Sql("""
        INSERT INTO cp6_authentication_v1.identity_admission_revisions(tenant_id,object_key,revision)
          SELECT "Id",'permission-catalog',gen_random_uuid() FROM public."Sys_Tenants"
          ON CONFLICT(tenant_id,object_key) DO NOTHING;
        CREATE FUNCTION cp6_authentication_v1.permission_catalog_writer() RETURNS trigger
          LANGUAGE plpgsql SET search_path=pg_catalog AS $cp6$
        DECLARE r record;
        BEGIN
          IF TG_TABLE_NAME='Sys_Menus' OR TG_OP='TRUNCATE' THEN
            FOR r IN SELECT "Id" AS tenant_id FROM public."Sys_Tenants" LOOP
              PERFORM cp6_authentication_v1.bump_admission_revision(r.tenant_id,'permission-catalog');
            END LOOP;
          ELSE
            IF TG_OP!='INSERT' THEN PERFORM cp6_authentication_v1.bump_admission_revision(OLD."TenantId",'permission-catalog'); END IF;
            IF TG_OP!='DELETE' THEN PERFORM cp6_authentication_v1.bump_admission_revision(NEW."TenantId",'permission-catalog'); END IF;
          END IF;
          RETURN NULL;
        END $cp6$;
        CREATE TRIGGER cp6_authentication_gate_v1 BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE
          ON public."Sys_MenuAction" FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER cp6_iam_catalog_revision_v1 AFTER INSERT OR UPDATE OR DELETE ON public."Sys_MenuAction"
          FOR EACH ROW EXECUTE FUNCTION cp6_authentication_v1.permission_catalog_writer();
        CREATE TRIGGER cp6_iam_catalog_truncate_v1 AFTER TRUNCATE ON public."Sys_MenuAction"
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.permission_catalog_writer();
        CREATE TRIGGER cp6_iam_catalog_revision_v1 AFTER INSERT OR UPDATE OR DELETE OR TRUNCATE ON public."Sys_Menus"
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.permission_catalog_writer();
        REVOKE ALL ON FUNCTION cp6_authentication_v1.permission_catalog_writer() FROM PUBLIC;
        """);
    protected override void Down(MigrationBuilder migrationBuilder)=>throw new NotSupportedException("IAM catalog evidence requires forward recovery.");
}
