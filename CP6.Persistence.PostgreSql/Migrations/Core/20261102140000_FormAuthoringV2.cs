using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261102140000_FormAuthoringV2")]
public sealed class FormAuthoringV2 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE SCHEMA cp6_form_authoring_v2;
        REVOKE ALL ON SCHEMA cp6_form_authoring_v2 FROM PUBLIC;
        CREATE TABLE cp6_form_authoring_v2.policy (
          tenant_id uuid NOT NULL, environment_id uuid NOT NULL, logical_domain text NOT NULL DEFAULT 'WF_AUTH' CHECK(logical_domain='WF_AUTH'),
          enabled boolean NOT NULL, forbidden_roles integer[] NOT NULL, row_version uuid NOT NULL DEFAULT gen_random_uuid(),
          profile text NOT NULL CHECK(profile='DIRECT_FORM_AUTHORING_V1'),
          PRIMARY KEY(tenant_id,environment_id), FOREIGN KEY(tenant_id,environment_id,logical_domain)
            REFERENCES cp6_scope_authorization_v1.fences(tenant_id,environment_id,logical_domain));
        CREATE TRIGGER form_policy_fence BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_form_authoring_v2.policy
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.before_authority_change();
        CREATE TRIGGER form_policy_version BEFORE INSERT OR UPDATE ON cp6_form_authoring_v2.policy
          FOR EACH ROW EXECUTE FUNCTION cp6_workflow_authoring_v2.policy_version();
        CREATE TABLE cp6_form_authoring_v2.heads (
          tenant_id uuid NOT NULL, environment_id uuid NOT NULL, id uuid NOT NULL,
          form_key text COLLATE "C" NOT NULL CHECK(length(form_key) BETWEEN 1 AND 100),
          owner_id uuid NOT NULL, owner_dept_id uuid, enabled boolean NOT NULL DEFAULT false CHECK(NOT enabled),
          draft_id uuid NOT NULL,
          PRIMARY KEY(tenant_id,environment_id,id), UNIQUE(tenant_id,form_key),
          FOREIGN KEY(tenant_id,environment_id) REFERENCES cp6_form_authoring_v2.policy(tenant_id,environment_id));
        CREATE UNIQUE INDEX form_authoring_native_key ON cp6_form_authoring_v2.heads(tenant_id,(form_key::bpchar COLLATE public.cp6_ci_as_provider_v1));
        CREATE TABLE cp6_form_authoring_v2.drafts (
          tenant_id uuid NOT NULL, environment_id uuid NOT NULL, head_id uuid NOT NULL, id uuid NOT NULL,
          name text NOT NULL CHECK(length(name) BETWEEN 1 AND 200), schema_json text NOT NULL,
          category text CHECK(length(category)<=100), sub_category text CHECK(length(sub_category)<=100),
          row_version uuid NOT NULL DEFAULT gen_random_uuid(), saved_by uuid NOT NULL,
          PRIMARY KEY(tenant_id,environment_id,head_id), UNIQUE(tenant_id,environment_id,id),
          FOREIGN KEY(tenant_id,environment_id,head_id) REFERENCES cp6_form_authoring_v2.heads(tenant_id,environment_id,id));
        CREATE TABLE cp6_form_authoring_v2.commands (
          tenant_id uuid NOT NULL, environment_id uuid NOT NULL, actor_id uuid NOT NULL,
          operation text NOT NULL CHECK(operation='FORM_SAVE_DRAFT'), resource_key text COLLATE "C" NOT NULL,
          command_id text COLLATE "C" NOT NULL CHECK(command_id ~ '^[A-Za-z0-9_.-]{1,100}$'),
          canonical_version text NOT NULL CHECK(canonical_version='WFC-CJ-1'), body_digest text NOT NULL CHECK(length(body_digest)=64),
          raw_body bytea NOT NULL, raw_digest text NOT NULL, result_json text NOT NULL, recorded_at timestamptz NOT NULL,
          PRIMARY KEY(tenant_id,environment_id,actor_id,operation,resource_key,command_id));
        CREATE TABLE cp6_form_authoring_v2.audit (
          tenant_id uuid NOT NULL, environment_id uuid NOT NULL, actor_id uuid NOT NULL,
          operation text NOT NULL DEFAULT 'FORM_SAVE_DRAFT' CHECK(operation='FORM_SAVE_DRAFT'),
          resource_key text COLLATE "C" NOT NULL, command_id text COLLATE "C" NOT NULL, evidence_json text NOT NULL, recorded_at timestamptz NOT NULL,
          FOREIGN KEY(tenant_id,environment_id,actor_id,operation,resource_key,command_id)
            REFERENCES cp6_form_authoring_v2.commands(tenant_id,environment_id,actor_id,operation,resource_key,command_id));
        CREATE TRIGGER form_commands_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_form_authoring_v2.commands
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER form_audit_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_form_authoring_v2.audit
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        -- Installation serializes with current native creators; no historical definitions are rewritten.
        LOCK TABLE public."Wf_FormDef" IN SHARE ROW EXCLUSIVE MODE;
        CREATE TRIGGER form_heads_root BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_form_authoring_v2.heads
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER form_drafts_root BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_form_authoring_v2.drafts
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER form_native_root BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON public."Wf_FormDef"
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE FUNCTION cp6_form_authoring_v2.native_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF EXISTS(SELECT FROM cp6_form_authoring_v2.heads h WHERE h.tenant_id=NEW."TenantId"
            AND h.form_key::bpchar COLLATE public.cp6_ci_as_provider_v1=NEW."FormKey") THEN
            RAISE EXCEPTION 'AUTHORITY_FENCE_UNAVAILABLE' USING ERRCODE='23514';
          END IF; RETURN NEW;
        END $$;
        CREATE TRIGGER form_native_identity BEFORE INSERT OR UPDATE ON public."Wf_FormDef"
          FOR EACH ROW EXECUTE FUNCTION cp6_form_authoring_v2.native_guard();
        CREATE FUNCTION cp6_form_authoring_v2.head_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP='UPDATE' AND ROW(NEW.tenant_id,NEW.environment_id,NEW.id,NEW.form_key,NEW.owner_id,NEW.owner_dept_id,NEW.draft_id)
            IS DISTINCT FROM ROW(OLD.tenant_id,OLD.environment_id,OLD.id,OLD.form_key,OLD.owner_id,OLD.owner_dept_id,OLD.draft_id) THEN
            RAISE EXCEPTION 'AUTHORING_IDENTITY_IMMUTABLE' USING ERRCODE='23514';
          END IF;
          IF EXISTS(SELECT FROM public."Wf_FormDef" h WHERE h."TenantId"=NEW.tenant_id
            AND h."FormKey"=NEW.form_key::bpchar COLLATE public.cp6_ci_as_provider_v1) THEN
            RAISE EXCEPTION 'DEFINITION_IDENTITY_CONFLICT' USING ERRCODE='23514';
          END IF; RETURN NEW;
        END $$;
        CREATE TRIGGER form_head_identity BEFORE INSERT OR UPDATE ON cp6_form_authoring_v2.heads
          FOR EACH ROW EXECUTE FUNCTION cp6_form_authoring_v2.head_guard();
        CREATE FUNCTION cp6_form_authoring_v2.draft_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP='UPDATE' AND ROW(NEW.tenant_id,NEW.environment_id,NEW.head_id,NEW.id)
            IS DISTINCT FROM ROW(OLD.tenant_id,OLD.environment_id,OLD.head_id,OLD.id) THEN
            RAISE EXCEPTION 'DRAFT_IDENTITY_IMMUTABLE' USING ERRCODE='23514';
          END IF;
          NEW.row_version:=gen_random_uuid(); RETURN NEW;
        END $$;
        CREATE TRIGGER form_draft_version BEFORE INSERT OR UPDATE ON cp6_form_authoring_v2.drafts
          FOR EACH ROW EXECUTE FUNCTION cp6_form_authoring_v2.draft_guard();
        CREATE FUNCTION cp6_form_authoring_v2.ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT current_setting('session_replication_role')='origin' AND NOT EXISTS (
            SELECT FROM (VALUES
              ('cp6_form_authoring_v2.policy','form_policy_fence','cp6_scope_authorization_v1.before_authority_change()',62),
              ('cp6_form_authoring_v2.policy','form_policy_version','cp6_workflow_authoring_v2.policy_version()',23),
              ('cp6_form_authoring_v2.commands','form_commands_immutable','cp6_scope_authorization_v1.immutable()',58),
              ('cp6_form_authoring_v2.audit','form_audit_immutable','cp6_scope_authorization_v1.immutable()',58),
              ('cp6_form_authoring_v2.heads','form_heads_root','cp6_authentication_v1.before_change()',62),
              ('cp6_form_authoring_v2.heads','form_head_identity','cp6_form_authoring_v2.head_guard()',23),
              ('cp6_form_authoring_v2.drafts','form_drafts_root','cp6_authentication_v1.before_change()',62),
              ('cp6_form_authoring_v2.drafts','form_draft_version','cp6_form_authoring_v2.draft_guard()',23),
              ('public."Wf_FormDef"','form_native_root','cp6_authentication_v1.before_change()',62),
              ('public."Wf_FormDef"','form_native_identity','cp6_form_authoring_v2.native_guard()',23)
            ) expected(tbl,name,fn,typ) WHERE NOT EXISTS (SELECT FROM pg_trigger t WHERE
              t.tgrelid=to_regclass(expected.tbl) AND t.tgname=expected.name AND t.tgfoid=to_regprocedure(expected.fn)
              AND t.tgenabled IN ('O','A') AND t.tgtype=expected.typ AND t.tgqual IS NULL AND t.tgnargs=0 AND NOT t.tgisinternal))
        $$;
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_form_authoring_v2 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_form_authoring_v2 FROM PUBLIC;
        """);
    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("Form authoring original commands require forward recovery.");
}
