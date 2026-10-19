using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context))]
[Migration("20261019155500_SsoIdentityMappingV1")]
public sealed class SsoIdentityMappingV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        -- Protocol identity is byte-exact. Existing duplicate bindings stop installation; no account is selected or rewritten.
        -- bpcharsend is STABLE, so do not mislabel it IMMUTABLE for an expression index.
        -- A transactional actual-writer trigger materializes the exact bytes instead.
        CREATE TABLE cp6_authentication_v1.federated_keys (
          user_id uuid PRIMARY KEY REFERENCES public."Sys_Users"("Id") ON DELETE CASCADE ON UPDATE CASCADE,
          tenant_id uuid NOT NULL, issuer bytea NOT NULL, subject bytea NOT NULL,
          UNIQUE(tenant_id,issuer,subject));
        INSERT INTO cp6_authentication_v1.federated_keys
          SELECT "Id","TenantId",pg_catalog.bpcharsend("ExternalProvider"),pg_catalog.bpcharsend("ExternalSubject")
          FROM public."Sys_Users" WHERE "ExternalProvider" IS NOT NULL AND "ExternalSubject" IS NOT NULL
            AND octet_length(pg_catalog.bpcharsend("ExternalProvider"))>0 AND octet_length(pg_catalog.bpcharsend("ExternalSubject"))>0;
        CREATE FUNCTION cp6_authentication_v1.federated_key_writer() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP='DELETE' THEN DELETE FROM cp6_authentication_v1.federated_keys WHERE user_id=OLD."Id"; RETURN NULL; END IF;
          IF NEW."ExternalProvider" IS NULL OR NEW."ExternalSubject" IS NULL
            OR octet_length(pg_catalog.bpcharsend(NEW."ExternalProvider"))=0 OR octet_length(pg_catalog.bpcharsend(NEW."ExternalSubject"))=0 THEN
            DELETE FROM cp6_authentication_v1.federated_keys WHERE user_id=NEW."Id";
          ELSE
            INSERT INTO cp6_authentication_v1.federated_keys VALUES(NEW."Id",NEW."TenantId",pg_catalog.bpcharsend(NEW."ExternalProvider"),pg_catalog.bpcharsend(NEW."ExternalSubject"))
              ON CONFLICT(user_id) DO UPDATE SET tenant_id=EXCLUDED.tenant_id,issuer=EXCLUDED.issuer,subject=EXCLUDED.subject;
          END IF;
          RETURN NULL;
        END $$;
        CREATE TRIGGER cp6_sso_federated_key_v1 AFTER INSERT OR UPDATE OR DELETE ON public."Sys_Users"
          FOR EACH ROW EXECUTE FUNCTION cp6_authentication_v1.federated_key_writer();
        CREATE TRIGGER cp6_authentication_gate_v1 BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE
          ON cp6_authentication_v1.federated_keys FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();

        -- Preserve unchanged legacy duplicates, but every new/renamed username obeys Main's
        -- existing case-insensitive tenant domain, including raw SQL/ExecuteUpdate writers.
        CREATE FUNCTION cp6_authentication_v1.username_admission_writer() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP='UPDATE' AND NEW."TenantId"=OLD."TenantId" AND NEW."UserName" IS NOT DISTINCT FROM OLD."UserName" THEN RETURN NEW; END IF;
          IF EXISTS(SELECT FROM public."Sys_Users" u WHERE u."TenantId"=NEW."TenantId" AND u."UserName"=NEW."UserName" AND u."Id"<>NEW."Id") THEN
            RAISE EXCEPTION 'SSO_IDENTITY_USERNAME_CONFLICT' USING ERRCODE='23505',CONSTRAINT='cp6_username_admission_v1';
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER cp6_sso_username_admission_v1 BEFORE INSERT OR UPDATE ON public."Sys_Users"
          FOR EACH ROW EXECUTE FUNCTION cp6_authentication_v1.username_admission_writer();

        CREATE TABLE cp6_authentication_v1.identity_admission_revisions (
          tenant_id uuid NOT NULL, object_key text NOT NULL, revision uuid NOT NULL,
          PRIMARY KEY(tenant_id,object_key));
        INSERT INTO cp6_authentication_v1.identity_admission_revisions
          SELECT "TenantId",'user:'||"Id"::text,gen_random_uuid() FROM public."Sys_Users";
        INSERT INTO cp6_authentication_v1.identity_admission_revisions
          SELECT "TenantId",'role:'||"RoleId"::text,gen_random_uuid() FROM public."Sys_Roles";
        CREATE TABLE cp6_authentication_v1.sso_mapping_intents (
          operation_ref text PRIMARY KEY REFERENCES cp6_authentication_v1.sso_states(state_hash),
          tenant_id uuid NOT NULL, issuer bytea NOT NULL, subject bytea NOT NULL,
          client_kind text NOT NULL, client_binding text NOT NULL, document text NOT NULL,
          created_at timestamptz NOT NULL DEFAULT clock_timestamp());
        CREATE INDEX cp6_sso_mapping_identity_v1 ON cp6_authentication_v1.sso_mapping_intents(tenant_id,issuer,subject);
        CREATE TABLE cp6_authentication_v1.sso_mapping_receipts (
          operation_ref text PRIMARY KEY REFERENCES cp6_authentication_v1.sso_states(state_hash),
          tenant_id uuid NOT NULL, state text NOT NULL CHECK(state IN ('IDENTITY_BOUND','USER_PROVISIONED','CLOSED')),
          user_id uuid, identity_version integer, created_at timestamptz NOT NULL DEFAULT clock_timestamp());
        CREATE TRIGGER cp6_authentication_gate_v1 BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE
          ON cp6_authentication_v1.identity_admission_revisions FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER cp6_authentication_gate_v1 BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE
          ON cp6_authentication_v1.sso_mapping_intents FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER cp6_authentication_gate_v1 BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE
          ON cp6_authentication_v1.sso_mapping_receipts FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER cp6_sso_mapping_immutable BEFORE UPDATE OR DELETE OR TRUNCATE
          ON cp6_authentication_v1.sso_mapping_intents FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.sso_operation_immutable();
        CREATE TRIGGER cp6_sso_mapping_immutable BEFORE UPDATE OR DELETE OR TRUNCATE
          ON cp6_authentication_v1.sso_mapping_receipts FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.sso_operation_immutable();

        CREATE FUNCTION cp6_authentication_v1.bump_admission_revision(t uuid,k text) RETURNS void LANGUAGE sql SET search_path=pg_catalog AS $$
          INSERT INTO cp6_authentication_v1.identity_admission_revisions(tenant_id,object_key,revision) VALUES(t,k,gen_random_uuid())
          ON CONFLICT(tenant_id,object_key) DO UPDATE SET revision=EXCLUDED.revision;
        $$;
        CREATE FUNCTION cp6_authentication_v1.admission_revision_writer() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE r record;
        BEGIN
          IF TG_OP='TRUNCATE' THEN
            UPDATE cp6_authentication_v1.identity_admission_revisions SET revision=gen_random_uuid(); RETURN NULL;
          END IF;
          IF TG_TABLE_NAME='Sys_Users' THEN
            IF TG_OP!='INSERT' THEN PERFORM cp6_authentication_v1.bump_admission_revision(OLD."TenantId",'user:'||OLD."Id"::text); END IF;
            IF TG_OP!='DELETE' THEN PERFORM cp6_authentication_v1.bump_admission_revision(NEW."TenantId",'user:'||NEW."Id"::text); END IF;
          ELSIF TG_TABLE_NAME='Sys_UserRole' THEN
            IF TG_OP!='INSERT' THEN PERFORM cp6_authentication_v1.bump_admission_revision(OLD."TenantId",'user:'||OLD."UserId"::text); END IF;
            IF TG_OP!='DELETE' THEN PERFORM cp6_authentication_v1.bump_admission_revision(NEW."TenantId",'user:'||NEW."UserId"::text); END IF;
          ELSIF TG_TABLE_NAME='Sys_Menus' THEN
            FOR r IN SELECT DISTINCT "TenantId","RoleId" FROM public."Sys_RoleAction"
              WHERE (TG_OP!='INSERT' AND "MenuId"=OLD."MenuId") OR (TG_OP!='DELETE' AND "MenuId"=NEW."MenuId") LOOP
              PERFORM cp6_authentication_v1.bump_admission_revision(r."TenantId",'role:'||r."RoleId"::text);
            END LOOP;
          ELSE
            IF TG_OP!='INSERT' THEN PERFORM cp6_authentication_v1.bump_admission_revision(OLD."TenantId",'role:'||OLD."RoleId"::text); END IF;
            IF TG_OP!='DELETE' THEN PERFORM cp6_authentication_v1.bump_admission_revision(NEW."TenantId",'role:'||NEW."RoleId"::text); END IF;
          END IF;
          RETURN NULL;
        END $$;
        DO $$ DECLARE n text; BEGIN
          FOREACH n IN ARRAY ARRAY['Sys_Users','Sys_Roles','Sys_UserRole','Sys_RoleAction','Sys_RoleDataScope','Sys_RoleFieldPerm','Sys_RoleMenus','Sys_Menus'] LOOP
            IF n IN ('Sys_RoleDataScope','Sys_RoleFieldPerm','Sys_RoleMenus') THEN
              EXECUTE format('CREATE TRIGGER cp6_authentication_gate_v1 BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON public.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change()',n);
            END IF;
            EXECUTE format('CREATE TRIGGER cp6_sso_admission_revision_v1 AFTER INSERT OR UPDATE OR DELETE ON public.%I FOR EACH ROW EXECUTE FUNCTION cp6_authentication_v1.admission_revision_writer()',n);
            EXECUTE format('CREATE TRIGGER cp6_sso_admission_truncate_v1 AFTER TRUNCATE ON public.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.admission_revision_writer()',n);
          END LOOP;
        END $$;
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_authentication_v1 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_authentication_v1 FROM PUBLIC;
        """);
    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("Federated identity and recovery receipts require forward recovery.");
}
