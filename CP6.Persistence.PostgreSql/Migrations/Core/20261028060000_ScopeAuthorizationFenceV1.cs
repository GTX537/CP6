using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261028060000_ScopeAuthorizationFenceV1")]
public sealed class ScopeAuthorizationFenceV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE SCHEMA cp6_scope_authorization_v1;
        REVOKE ALL ON SCHEMA cp6_scope_authorization_v1 FROM PUBLIC;
        CREATE TABLE cp6_scope_authorization_v1.environment (
          singleton boolean PRIMARY KEY CHECK(singleton), environment_id uuid NOT NULL UNIQUE
            CHECK(environment_id <> '00000000-0000-0000-0000-000000000000'));
        CREATE TABLE cp6_scope_authorization_v1.fences (
          tenant_id uuid NOT NULL REFERENCES public."Sys_Tenants"("Id"),
          environment_id uuid NOT NULL REFERENCES cp6_scope_authorization_v1.environment(environment_id),
          logical_domain text COLLATE "C" NOT NULL CHECK(logical_domain='WF_AUTH'),
          permission_domain uuid NOT NULL CHECK(permission_domain <> '00000000-0000-0000-0000-000000000000'),
          enabled boolean NOT NULL, epoch bigint NOT NULL CHECK(epoch>=0), row_version uuid NOT NULL,
          writer_profile text NOT NULL CHECK(writer_profile='MAIN_NATIVE_IAM_WF_V1'),
          PRIMARY KEY(tenant_id,environment_id,logical_domain), UNIQUE(tenant_id,environment_id,permission_domain));

        CREATE FUNCTION cp6_scope_authorization_v1.immutable() RETURNS trigger
          LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN RAISE EXCEPTION 'SCOPE_AUTHORITY_IMMUTABLE' USING ERRCODE='23514'; END $$;
        CREATE TRIGGER scope_environment_immutable BEFORE UPDATE OR DELETE OR TRUNCATE
          ON cp6_scope_authorization_v1.environment FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER scope_fence_no_delete BEFORE DELETE OR TRUNCATE
          ON cp6_scope_authorization_v1.fences FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER scope_environment_gate BEFORE INSERT ON cp6_scope_authorization_v1.environment
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER scope_fence_gate BEFORE INSERT OR UPDATE ON cp6_scope_authorization_v1.fences
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE FUNCTION cp6_scope_authorization_v1.advance() RETURNS trigger
          LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP='INSERT' THEN
            NEW.epoch:=0; NEW.row_version:=gen_random_uuid();
          ELSE
            IF ROW(NEW.tenant_id,NEW.environment_id,NEW.logical_domain,NEW.permission_domain,NEW.writer_profile)
              IS DISTINCT FROM ROW(OLD.tenant_id,OLD.environment_id,OLD.logical_domain,OLD.permission_domain,OLD.writer_profile) THEN
              RAISE EXCEPTION 'SCOPE_AUTHORITY_MAPPING_IMMUTABLE' USING ERRCODE='23514';
            END IF;
            NEW.epoch:=OLD.epoch+1; NEW.row_version:=gen_random_uuid();
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER scope_fence_advance BEFORE INSERT OR UPDATE ON cp6_scope_authorization_v1.fences
          FOR EACH ROW EXECUTE FUNCTION cp6_scope_authorization_v1.advance();

        -- Conservative database-wide invalidation: global menu facts and multi-tenant SQL
        -- statements use the same deterministic order. No permission fact is read before the root.
        CREATE FUNCTION cp6_scope_authorization_v1.before_authority_change() RETURNS trigger
          LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE f record;
        BEGIN
          PERFORM cp6_authentication_v1.acquire();
          FOR f IN SELECT tenant_id,environment_id,logical_domain FROM cp6_scope_authorization_v1.fences
            ORDER BY tenant_id,environment_id,permission_domain FOR UPDATE LOOP
            UPDATE cp6_scope_authorization_v1.fences SET epoch=epoch
              WHERE tenant_id=f.tenant_id AND environment_id=f.environment_id AND logical_domain=f.logical_domain;
          END LOOP;
          RETURN NULL;
        END $$;
        DO $install$
        DECLARE t text;
        BEGIN
          FOREACH t IN ARRAY ARRAY['Sys_Tenants','Sys_Users','Sys_TenantSsoConfigs','Sys_Roles','Sys_UserRole',
            'Sys_RoleMenus','Sys_RoleAction','Sys_RoleDataScope','Sys_RoleFieldPerm','Sys_MenuAction','Sys_Menus',
            'Sys_Dept','Wf_FlowDelegate','Wf_ApproverMap'] LOOP
            EXECUTE format('CREATE TRIGGER cp6_scope_authority_writer_v1 BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON public.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.before_authority_change()',t);
          END LOOP;
        END $install$;
        CREATE FUNCTION cp6_scope_authorization_v1.writers_ready() RETURNS boolean
          LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT current_setting('session_replication_role')='origin' AND NOT EXISTS (
            SELECT FROM unnest(ARRAY['Sys_Tenants','Sys_Users','Sys_TenantSsoConfigs','Sys_Roles','Sys_UserRole',
              'Sys_RoleMenus','Sys_RoleAction','Sys_RoleDataScope','Sys_RoleFieldPerm','Sys_MenuAction','Sys_Menus',
              'Sys_Dept','Wf_FlowDelegate','Wf_ApproverMap']) expected(table_name)
            WHERE NOT EXISTS (SELECT FROM pg_trigger t WHERE t.tgrelid=to_regclass(format('public.%I',expected.table_name))
              AND t.tgname='cp6_scope_authority_writer_v1' AND t.tgenabled IN ('O','A') AND NOT t.tgisinternal
              AND t.tgtype=62 AND t.tgqual IS NULL AND t.tgnargs=0
              AND t.tgfoid='cp6_scope_authorization_v1.before_authority_change()'::regprocedure))
        $$;
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_scope_authorization_v1 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_scope_authorization_v1 FROM PUBLIC;
        """);
    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("Scope mappings and authorization epochs require forward recovery.");
}
