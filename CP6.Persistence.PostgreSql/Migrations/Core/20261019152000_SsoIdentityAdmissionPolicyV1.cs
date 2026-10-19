using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context))]
[Migration("20261019152000_SsoIdentityAdmissionPolicyV1")]
public sealed class SsoIdentityAdmissionPolicyV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE cp6_authentication_v1.sso_admission_policies (
          tenant_id uuid PRIMARY KEY REFERENCES public."Sys_Tenants"("Id"), document text NOT NULL);
        CREATE TABLE cp6_authentication_v1.sso_config_operations (
          tenant_id uuid NOT NULL, operation_id uuid NOT NULL, actor_id uuid NOT NULL,
          state text NOT NULL CHECK (state IN ('COMMITTED','CLOSED')), request_protected text,
          result_json text NOT NULL, created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
          PRIMARY KEY (tenant_id,operation_id));
        CREATE TRIGGER cp6_authentication_gate_v1 BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE
          ON cp6_authentication_v1.sso_admission_policies FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER cp6_authentication_gate_v1 BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE
          ON cp6_authentication_v1.sso_config_operations FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER cp6_authentication_gate_v1 BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE
          ON public."Sys_Roles" FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER cp6_authentication_gate_v1 BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE
          ON public."Sys_UserRole" FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER cp6_authentication_gate_v1 BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE
          ON public."Sys_RoleAction" FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER cp6_authentication_gate_v1 BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE
          ON public."Sys_Menus" FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();

        CREATE FUNCTION cp6_authentication_v1.sso_admission_revision() RETURNS trigger
          LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP='TRUNCATE' THEN
            UPDATE public."Sys_Tenants" SET "AuthenticationPolicyRevision"=gen_random_uuid();
          ELSE
            IF TG_OP!='INSERT' THEN UPDATE public."Sys_Tenants" SET "AuthenticationPolicyRevision"=gen_random_uuid() WHERE "Id"=OLD.tenant_id; END IF;
            IF TG_OP!='DELETE' THEN UPDATE public."Sys_Tenants" SET "AuthenticationPolicyRevision"=gen_random_uuid() WHERE "Id"=NEW.tenant_id; END IF;
          END IF;
          RETURN NULL;
        END $$;
        CREATE TRIGGER cp6_sso_admission_revision AFTER INSERT OR UPDATE OR DELETE
          ON cp6_authentication_v1.sso_admission_policies FOR EACH ROW EXECUTE FUNCTION cp6_authentication_v1.sso_admission_revision();
        CREATE TRIGGER cp6_sso_admission_truncate AFTER TRUNCATE
          ON cp6_authentication_v1.sso_admission_policies FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.sso_admission_revision();
        CREATE FUNCTION cp6_authentication_v1.sso_operation_immutable() RETURNS trigger
          LANGUAGE plpgsql SET search_path=pg_catalog AS $$ BEGIN RAISE EXCEPTION 'SSO operation receipts are immutable' USING ERRCODE='23514'; END $$;
        CREATE TRIGGER cp6_sso_operation_immutable BEFORE UPDATE OR DELETE OR TRUNCATE
          ON cp6_authentication_v1.sso_config_operations FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.sso_operation_immutable();
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_authentication_v1 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_authentication_v1 FROM PUBLIC;
        """);
    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("SSO admission and operation closures require forward recovery.");
}
