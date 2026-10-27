using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261028080000_WorkflowPublicationV2")]
public sealed class WorkflowPublicationV2 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE cp6_workflow_authoring_v2.drafts ADD COLUMN status integer NOT NULL DEFAULT 0 CHECK(status IN(0,1)),
          ADD COLUMN version_number integer NOT NULL DEFAULT 1 CHECK(version_number>0), ADD COLUMN saved_by uuid,
          ADD COLUMN source_version_id uuid;
        -- Only an exact existing command result proves the current draft editor. No owner/name inference.
        UPDATE cp6_workflow_authoring_v2.drafts d SET saved_by=(SELECT c.actor_id FROM cp6_workflow_authoring_v2.commands c
          WHERE c.tenant_id=d.tenant_id AND c.environment_id=d.environment_id AND c.operation='FLOW_SAVE_DRAFT'
            AND (c.result_json::jsonb->>'resourceVersion')=d.row_version::text
            AND (c.result_json::jsonb->'result'->>'draftId')=d.id::text LIMIT 1);
        CREATE TABLE cp6_workflow_authoring_v2.publication_policy (
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,logical_domain text NOT NULL DEFAULT 'WF_AUTH' CHECK(logical_domain='WF_AUTH'),
          enabled boolean NOT NULL,distinct_author boolean NOT NULL,forbidden_roles integer[] NOT NULL,
          row_version uuid NOT NULL DEFAULT gen_random_uuid(),profile text NOT NULL DEFAULT 'DIRECT_PUBLICATION_V1' CHECK(profile='DIRECT_PUBLICATION_V1'),
          PRIMARY KEY(tenant_id,environment_id), FOREIGN KEY(tenant_id,environment_id,logical_domain)
            REFERENCES cp6_scope_authorization_v1.fences(tenant_id,environment_id,logical_domain));
        CREATE TRIGGER publication_policy_fence BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_workflow_authoring_v2.publication_policy
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.before_authority_change();
        CREATE TRIGGER publication_policy_version BEFORE INSERT OR UPDATE ON cp6_workflow_authoring_v2.publication_policy
          FOR EACH ROW EXECUTE FUNCTION cp6_workflow_authoring_v2.policy_version();
        CREATE TABLE cp6_workflow_authoring_v2.publication_previews (
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,id uuid NOT NULL,actor_id uuid NOT NULL,head_id uuid NOT NULL,
          draft_id uuid NOT NULL,draft_token uuid NOT NULL,digest text NOT NULL,expires_at timestamptz NOT NULL,snapshot_json text NOT NULL,
          PRIMARY KEY(tenant_id,environment_id,id), FOREIGN KEY(tenant_id,environment_id,head_id)
            REFERENCES cp6_workflow_authoring_v2.heads(tenant_id,environment_id,id));
        CREATE TABLE cp6_workflow_authoring_v2.versions (
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,head_id uuid NOT NULL,id uuid NOT NULL,version_number integer NOT NULL CHECK(version_number>0),
          schema_digest text NOT NULL,snapshot_json text NOT NULL,published_at timestamptz NOT NULL,publisher uuid NOT NULL,
          PRIMARY KEY(tenant_id,environment_id,id),UNIQUE(tenant_id,environment_id,head_id,version_number),
          FOREIGN KEY(tenant_id,environment_id,head_id) REFERENCES cp6_workflow_authoring_v2.heads(tenant_id,environment_id,id));
        ALTER TABLE cp6_workflow_authoring_v2.commands DROP CONSTRAINT commands_operation_check,
          ADD CHECK(operation IN('FLOW_SAVE_DRAFT','FLOW_PUBLISH','FLOW_CREATE_DRAFT'));
        ALTER TABLE cp6_workflow_authoring_v2.audit DROP CONSTRAINT audit_operation_check,
          ADD CHECK(operation IN('FLOW_SAVE_DRAFT','FLOW_PUBLISH','FLOW_CREATE_DRAFT'));
        CREATE TRIGGER publication_previews_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_workflow_authoring_v2.publication_previews
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER publication_versions_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_workflow_authoring_v2.versions
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE FUNCTION cp6_workflow_authoring_v2.draft_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP='DELETE' THEN RAISE EXCEPTION 'DRAFT_HISTORY_REQUIRED' USING ERRCODE='23514'; END IF;
          IF TG_OP='UPDATE' AND OLD.status=1 AND (NEW.id=OLD.id OR NEW.status<>0 OR NEW.version_number<>OLD.version_number+1
            OR NOT EXISTS(SELECT FROM cp6_workflow_authoring_v2.versions v WHERE v.tenant_id=NEW.tenant_id AND v.environment_id=NEW.environment_id
              AND v.head_id=NEW.head_id AND v.id=NEW.source_version_id)) THEN
            RAISE EXCEPTION 'PUBLISHED_IMMUTABLE' USING ERRCODE='23514'; END IF;
          IF NEW.status=1 AND NOT EXISTS(SELECT FROM cp6_workflow_authoring_v2.versions v WHERE v.tenant_id=NEW.tenant_id
            AND v.environment_id=NEW.environment_id AND v.head_id=NEW.head_id AND v.id=NEW.id AND v.version_number=NEW.version_number) THEN
            RAISE EXCEPTION 'PUBLICATION_RECEIPT_REQUIRED' USING ERRCODE='23514'; END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER publication_draft_guard BEFORE INSERT OR UPDATE OR DELETE ON cp6_workflow_authoring_v2.drafts
          FOR EACH ROW EXECUTE FUNCTION cp6_workflow_authoring_v2.draft_guard();
        CREATE TRIGGER publication_draft_root BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_workflow_authoring_v2.drafts
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE OR REPLACE FUNCTION cp6_workflow_authoring_v2.head_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP='UPDATE' AND ROW(NEW.tenant_id,NEW.environment_id,NEW.id,NEW.flow_key,NEW.owner_id,NEW.owner_dept_id)
             IS DISTINCT FROM ROW(OLD.tenant_id,OLD.environment_id,OLD.id,OLD.flow_key,OLD.owner_id,OLD.owner_dept_id) THEN
            RAISE EXCEPTION 'AUTHORING_IDENTITY_IMMUTABLE' USING ERRCODE='23514'; END IF;
          IF EXISTS(SELECT FROM public."Wf_FlowDef" h WHERE h."TenantId"=NEW.tenant_id AND
            (h."FlowKey"=NEW.flow_key::bpchar COLLATE public.cp6_ci_as_provider_v1 OR
             h."FunctionId"=NEW.function_id::bpchar COLLATE public.cp6_ci_as_provider_v1 OR
             h."FlowCode"=NEW.flow_code::bpchar COLLATE public.cp6_ci_as_provider_v1)) THEN
            RAISE EXCEPTION 'DEFINITION_IDENTITY_CONFLICT' USING ERRCODE='23514'; END IF;
          RETURN NEW;
        END $$;
        CREATE FUNCTION cp6_workflow_authoring_v2.publication_ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT cp6_workflow_authoring_v2.ready() AND NOT EXISTS (
            SELECT FROM (VALUES
              ('publication_policy','publication_policy_fence','cp6_scope_authorization_v1.before_authority_change()',62),
              ('publication_policy','publication_policy_version','cp6_workflow_authoring_v2.policy_version()',23),
              ('publication_previews','publication_previews_immutable','cp6_scope_authorization_v1.immutable()',58),
              ('versions','publication_versions_immutable','cp6_scope_authorization_v1.immutable()',58),
              ('drafts','publication_draft_guard','cp6_workflow_authoring_v2.draft_guard()',31),
              ('drafts','publication_draft_root','cp6_authentication_v1.before_change()',62)
            ) expected(tbl,name,fn,typ) WHERE NOT EXISTS (SELECT FROM pg_trigger t WHERE
              t.tgrelid=to_regclass('cp6_workflow_authoring_v2.'||expected.tbl) AND t.tgname=expected.name AND t.tgfoid=to_regprocedure(expected.fn)
              AND t.tgenabled IN('O','A') AND t.tgtype=expected.typ AND t.tgqual IS NULL AND t.tgnargs=0 AND NOT t.tgisinternal))
        $$;
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_workflow_authoring_v2 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_workflow_authoring_v2 FROM PUBLIC;
        """);
    protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException("Publication history requires forward recovery.");
}
