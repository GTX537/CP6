using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context))]
[Migration("20261020042000_IamAdministrationV1")]
public sealed class IamAdministrationV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE cp6_authentication_v1.iam_operations (
          tenant_id uuid NOT NULL, operation_id uuid NOT NULL, actor_id uuid NOT NULL,
          kind text NOT NULL, state text NOT NULL CHECK(state IN ('COMMITTED','CLOSED')),
          request_protected text, result_json text NOT NULL,
          created_at timestamptz NOT NULL DEFAULT clock_timestamp(), PRIMARY KEY(tenant_id,operation_id));
        CREATE TRIGGER cp6_authentication_gate_v1 BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE
          ON cp6_authentication_v1.iam_operations FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER cp6_iam_operation_immutable BEFORE UPDATE OR DELETE OR TRUNCATE
          ON cp6_authentication_v1.iam_operations FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.sso_operation_immutable();
        INSERT INTO cp6_authentication_v1.identity_admission_revisions
          SELECT "TenantId",'dept:'||"Id"::text,gen_random_uuid() FROM public."Sys_Dept";
        INSERT INTO cp6_authentication_v1.identity_admission_revisions
          SELECT DISTINCT "TenantId",'tree',gen_random_uuid() FROM public."Sys_Dept"
          ON CONFLICT(tenant_id,object_key) DO NOTHING;
        CREATE FUNCTION cp6_authentication_v1.organization_revision_writer() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP='TRUNCATE' THEN
            UPDATE cp6_authentication_v1.identity_admission_revisions SET revision=gen_random_uuid() WHERE object_key='tree' OR object_key LIKE 'dept:%';
            RETURN NULL;
          END IF;
          IF TG_OP!='INSERT' THEN
            PERFORM cp6_authentication_v1.bump_admission_revision(OLD."TenantId",'dept:'||OLD."Id"::text);
            PERFORM cp6_authentication_v1.bump_admission_revision(OLD."TenantId",'tree');
          END IF;
          IF TG_OP!='DELETE' THEN
            PERFORM cp6_authentication_v1.bump_admission_revision(NEW."TenantId",'dept:'||NEW."Id"::text);
            PERFORM cp6_authentication_v1.bump_admission_revision(NEW."TenantId",'tree');
          END IF;
          RETURN NULL;
        END $$;
        CREATE TRIGGER cp6_authentication_gate_v1 BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE
          ON public."Sys_Dept" FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER cp6_iam_organization_revision_v1 AFTER INSERT OR UPDATE OR DELETE ON public."Sys_Dept"
          FOR EACH ROW EXECUTE FUNCTION cp6_authentication_v1.organization_revision_writer();
        CREATE TRIGGER cp6_iam_organization_truncate_v1 AFTER TRUNCATE ON public."Sys_Dept"
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.organization_revision_writer();
        REVOKE ALL ON cp6_authentication_v1.iam_operations FROM PUBLIC;
        REVOKE ALL ON FUNCTION cp6_authentication_v1.organization_revision_writer() FROM PUBLIC;
        """);
    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("IAM operations and revision evidence require forward recovery.");
}
