using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261028070000_WorkflowAuthoringV2")]
public sealed class WorkflowAuthoringV2 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE SCHEMA cp6_workflow_authoring_v2;
        REVOKE ALL ON SCHEMA cp6_workflow_authoring_v2 FROM PUBLIC;
        CREATE TABLE cp6_workflow_authoring_v2.policy (
          tenant_id uuid NOT NULL, environment_id uuid NOT NULL, logical_domain text NOT NULL DEFAULT 'WF_AUTH' CHECK(logical_domain='WF_AUTH'),
          enabled boolean NOT NULL, forbidden_roles integer[] NOT NULL, row_version uuid NOT NULL DEFAULT gen_random_uuid(),
          profile text NOT NULL CHECK(profile='DIRECT_AUTHORING_V1'),
          PRIMARY KEY(tenant_id,environment_id), FOREIGN KEY(tenant_id,environment_id,logical_domain)
            REFERENCES cp6_scope_authorization_v1.fences(tenant_id,environment_id,logical_domain));
        CREATE TRIGGER authoring_policy_fence BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_workflow_authoring_v2.policy
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.before_authority_change();
        CREATE FUNCTION cp6_workflow_authoring_v2.policy_version() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP='UPDATE' AND ROW(NEW.tenant_id,NEW.environment_id,NEW.logical_domain,NEW.profile) IS DISTINCT FROM
             ROW(OLD.tenant_id,OLD.environment_id,OLD.logical_domain,OLD.profile) THEN RAISE EXCEPTION 'AUTHORING_POLICY_IDENTITY_IMMUTABLE' USING ERRCODE='23514'; END IF;
          NEW.row_version:=gen_random_uuid(); RETURN NEW;
        END $$;
        CREATE TRIGGER authoring_policy_version BEFORE INSERT OR UPDATE ON cp6_workflow_authoring_v2.policy
          FOR EACH ROW EXECUTE FUNCTION cp6_workflow_authoring_v2.policy_version();
        CREATE TABLE cp6_workflow_authoring_v2.heads (
          tenant_id uuid NOT NULL, environment_id uuid NOT NULL, id uuid NOT NULL, flow_key text COLLATE "C" NOT NULL CHECK(length(flow_key) BETWEEN 1 AND 100),
          owner_id uuid NOT NULL, owner_dept_id uuid, enabled boolean NOT NULL DEFAULT false CHECK(NOT enabled),
          function_id text COLLATE "C", flow_code text COLLATE "C", draft_id uuid NOT NULL, row_version uuid NOT NULL,
          PRIMARY KEY(tenant_id,environment_id,id), UNIQUE(tenant_id,flow_key), UNIQUE(tenant_id,function_id), UNIQUE(tenant_id,flow_code),
          FOREIGN KEY(tenant_id,environment_id) REFERENCES cp6_workflow_authoring_v2.policy(tenant_id,environment_id));
        -- Preserve original identity collision semantics while retaining exact text for v2 references.
        CREATE UNIQUE INDEX authoring_flow_native_identity ON cp6_workflow_authoring_v2.heads(tenant_id,(flow_key::bpchar COLLATE public.cp6_ci_as_provider_v1));
        CREATE UNIQUE INDEX authoring_function_native_identity ON cp6_workflow_authoring_v2.heads(tenant_id,(function_id::bpchar COLLATE public.cp6_ci_as_provider_v1));
        CREATE UNIQUE INDEX authoring_code_native_identity ON cp6_workflow_authoring_v2.heads(tenant_id,(flow_code::bpchar COLLATE public.cp6_ci_as_provider_v1));
        CREATE TABLE cp6_workflow_authoring_v2.drafts (
          tenant_id uuid NOT NULL, environment_id uuid NOT NULL, head_id uuid NOT NULL, id uuid NOT NULL,
          name text NOT NULL CHECK(length(name) BETWEEN 1 AND 200), form_key text, schema_json text NOT NULL,
          row_version uuid NOT NULL, PRIMARY KEY(tenant_id,environment_id,head_id), UNIQUE(tenant_id,environment_id,id),
          FOREIGN KEY(tenant_id,environment_id,head_id) REFERENCES cp6_workflow_authoring_v2.heads(tenant_id,environment_id,id));
        CREATE TABLE cp6_workflow_authoring_v2.commands (
          tenant_id uuid NOT NULL, environment_id uuid NOT NULL, actor_id uuid NOT NULL, operation text NOT NULL CHECK(operation='FLOW_SAVE_DRAFT'),
          resource_key text COLLATE "C" NOT NULL, command_id text COLLATE "C" NOT NULL CHECK(command_id ~ '^[A-Za-z0-9_.-]{1,100}$'),
          canonical_version text NOT NULL CHECK(canonical_version='WFC-CJ-1'), body_digest text NOT NULL CHECK(length(body_digest)=64),
          raw_body bytea NOT NULL, raw_digest text NOT NULL, result_json text NOT NULL, recorded_at timestamptz NOT NULL,
          PRIMARY KEY(tenant_id,environment_id,actor_id,operation,resource_key,command_id));
        CREATE TABLE cp6_workflow_authoring_v2.audit (
          tenant_id uuid NOT NULL, environment_id uuid NOT NULL, actor_id uuid NOT NULL, command_id text NOT NULL, resource_key text NOT NULL,
          operation text NOT NULL DEFAULT 'FLOW_SAVE_DRAFT' CHECK(operation='FLOW_SAVE_DRAFT'),
          evidence_json text NOT NULL, recorded_at timestamptz NOT NULL,
          FOREIGN KEY(tenant_id,environment_id,actor_id,operation,resource_key,command_id)
            REFERENCES cp6_workflow_authoring_v2.commands(tenant_id,environment_id,actor_id,operation,resource_key,command_id));
        CREATE TRIGGER authoring_commands_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_workflow_authoring_v2.commands
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER authoring_audit_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_workflow_authoring_v2.audit
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER authoring_heads_root BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_workflow_authoring_v2.heads
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER authoring_native_root BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON public."Wf_FlowDef"
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE FUNCTION cp6_workflow_authoring_v2.native_identity_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF EXISTS(SELECT FROM cp6_workflow_authoring_v2.heads h WHERE h.tenant_id=NEW."TenantId" AND
            (h.flow_key::bpchar COLLATE public.cp6_ci_as_provider_v1=NEW."FlowKey" OR
             h.function_id::bpchar COLLATE public.cp6_ci_as_provider_v1=NEW."FunctionId" OR
             h.flow_code::bpchar COLLATE public.cp6_ci_as_provider_v1=NEW."FlowCode")) THEN
            RAISE EXCEPTION 'AUTHORITY_FENCE_UNAVAILABLE' USING ERRCODE='23514';
          END IF; RETURN NEW;
        END $$;
        CREATE TRIGGER authoring_native_identity BEFORE INSERT OR UPDATE ON public."Wf_FlowDef"
          FOR EACH ROW EXECUTE FUNCTION cp6_workflow_authoring_v2.native_identity_guard();
        CREATE FUNCTION cp6_workflow_authoring_v2.head_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP='UPDATE' AND ROW(NEW.tenant_id,NEW.environment_id,NEW.id,NEW.flow_key,NEW.owner_id,NEW.owner_dept_id,NEW.draft_id)
             IS DISTINCT FROM ROW(OLD.tenant_id,OLD.environment_id,OLD.id,OLD.flow_key,OLD.owner_id,OLD.owner_dept_id,OLD.draft_id) THEN
            RAISE EXCEPTION 'AUTHORING_IDENTITY_IMMUTABLE' USING ERRCODE='23514';
          END IF;
          IF EXISTS(SELECT FROM public."Wf_FlowDef" h WHERE h."TenantId"=NEW.tenant_id AND
            (h."FlowKey"=NEW.flow_key::bpchar COLLATE public.cp6_ci_as_provider_v1 OR
             h."FunctionId"=NEW.function_id::bpchar COLLATE public.cp6_ci_as_provider_v1 OR
             h."FlowCode"=NEW.flow_code::bpchar COLLATE public.cp6_ci_as_provider_v1)) THEN
            RAISE EXCEPTION 'DEFINITION_IDENTITY_CONFLICT' USING ERRCODE='23514';
          END IF; RETURN NEW;
        END $$;
        CREATE TRIGGER authoring_head_identity BEFORE INSERT OR UPDATE ON cp6_workflow_authoring_v2.heads
          FOR EACH ROW EXECUTE FUNCTION cp6_workflow_authoring_v2.head_guard();
        CREATE FUNCTION cp6_workflow_authoring_v2.ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT current_setting('session_replication_role')='origin' AND NOT EXISTS (
            SELECT FROM (VALUES
              ('cp6_workflow_authoring_v2.policy','authoring_policy_fence','cp6_scope_authorization_v1.before_authority_change()',62),
              ('cp6_workflow_authoring_v2.policy','authoring_policy_version','cp6_workflow_authoring_v2.policy_version()',23),
              ('cp6_workflow_authoring_v2.commands','authoring_commands_immutable','cp6_scope_authorization_v1.immutable()',58),
              ('cp6_workflow_authoring_v2.audit','authoring_audit_immutable','cp6_scope_authorization_v1.immutable()',58),
              ('cp6_workflow_authoring_v2.heads','authoring_heads_root','cp6_authentication_v1.before_change()',62),
              ('cp6_workflow_authoring_v2.heads','authoring_head_identity','cp6_workflow_authoring_v2.head_guard()',23),
              ('public."Wf_FlowDef"','authoring_native_root','cp6_authentication_v1.before_change()',62),
              ('public."Wf_FlowDef"','authoring_native_identity','cp6_workflow_authoring_v2.native_identity_guard()',23)
            ) expected(tbl,name,fn,typ) WHERE NOT EXISTS (SELECT FROM pg_trigger t WHERE
              t.tgrelid=to_regclass(expected.tbl) AND t.tgname=expected.name AND t.tgfoid=to_regprocedure(expected.fn)
              AND t.tgenabled IN ('O','A') AND t.tgtype=expected.typ AND t.tgqual IS NULL AND t.tgnargs=0 AND NOT t.tgisinternal))
        $$;
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_workflow_authoring_v2 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_workflow_authoring_v2 FROM PUBLIC;
        """);
    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("Authoring receipts require forward recovery.");
}
