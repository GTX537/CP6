using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261102150000_FormPublicationActivationV2")]
public sealed class FormPublicationActivationV2 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE cp6_form_authoring_v2.drafts
          ADD COLUMN status integer NOT NULL DEFAULT 0 CHECK(status IN(0,1)),
          ADD COLUMN version_number integer NOT NULL DEFAULT 1 CHECK(version_number>0),
          ADD COLUMN source_version_id uuid;
        CREATE TABLE cp6_form_authoring_v2.publication_policy (
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,logical_domain text NOT NULL DEFAULT 'WF_AUTH' CHECK(logical_domain='WF_AUTH'),
          enabled boolean NOT NULL,distinct_author boolean NOT NULL,forbidden_roles integer[] NOT NULL,
          row_version uuid NOT NULL DEFAULT gen_random_uuid(),profile text NOT NULL DEFAULT 'DIRECT_FORM_PUBLICATION_V1' CHECK(profile='DIRECT_FORM_PUBLICATION_V1'),
          PRIMARY KEY(tenant_id,environment_id), FOREIGN KEY(tenant_id,environment_id,logical_domain)
            REFERENCES cp6_scope_authorization_v1.fences(tenant_id,environment_id,logical_domain));
        CREATE TRIGGER publication_policy_fence BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_form_authoring_v2.publication_policy
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.before_authority_change();
        CREATE TRIGGER publication_policy_version BEFORE INSERT OR UPDATE ON cp6_form_authoring_v2.publication_policy
          FOR EACH ROW EXECUTE FUNCTION cp6_workflow_authoring_v2.policy_version();
        CREATE TABLE cp6_form_authoring_v2.publication_previews (
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,id uuid NOT NULL,actor_id uuid NOT NULL,head_id uuid NOT NULL,
          draft_id uuid NOT NULL,draft_token uuid NOT NULL,digest text NOT NULL,expires_at timestamptz NOT NULL,snapshot_json text NOT NULL,
          PRIMARY KEY(tenant_id,environment_id,id), FOREIGN KEY(tenant_id,environment_id,head_id)
            REFERENCES cp6_form_authoring_v2.heads(tenant_id,environment_id,id));
        CREATE TABLE cp6_form_authoring_v2.versions (
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,head_id uuid NOT NULL,id uuid NOT NULL,version_number integer NOT NULL CHECK(version_number>0),
          schema_digest text NOT NULL,snapshot_json text NOT NULL,published_at timestamptz NOT NULL,publisher uuid NOT NULL,
          PRIMARY KEY(tenant_id,environment_id,id),UNIQUE(tenant_id,environment_id,head_id,version_number),
          FOREIGN KEY(tenant_id,environment_id,head_id) REFERENCES cp6_form_authoring_v2.heads(tenant_id,environment_id,id));
        ALTER TABLE cp6_form_authoring_v2.commands DROP CONSTRAINT commands_operation_check,
          ADD CHECK(operation IN('FORM_SAVE_DRAFT','FORM_PUBLISH','FORM_CREATE_DRAFT'));
        ALTER TABLE cp6_form_authoring_v2.audit DROP CONSTRAINT audit_operation_check,
          ADD CHECK(operation IN('FORM_SAVE_DRAFT','FORM_PUBLISH','FORM_CREATE_DRAFT'));
        CREATE TRIGGER publication_previews_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_form_authoring_v2.publication_previews
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER publication_versions_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_form_authoring_v2.versions
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE OR REPLACE FUNCTION cp6_form_authoring_v2.draft_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP='UPDATE' THEN
            IF ROW(NEW.tenant_id,NEW.environment_id,NEW.head_id) IS DISTINCT FROM ROW(OLD.tenant_id,OLD.environment_id,OLD.head_id) THEN
              RAISE EXCEPTION 'DRAFT_IDENTITY_IMMUTABLE' USING ERRCODE='23514'; END IF;
            IF OLD.status=0 AND ROW(NEW.id,NEW.version_number,NEW.source_version_id) IS DISTINCT FROM ROW(OLD.id,OLD.version_number,OLD.source_version_id) THEN
              RAISE EXCEPTION 'DRAFT_IDENTITY_IMMUTABLE' USING ERRCODE='23514'; END IF;
            IF OLD.status=1 AND (NEW.id=OLD.id OR NEW.status<>0 OR NEW.version_number<>OLD.version_number+1
              OR NOT EXISTS(SELECT FROM cp6_form_authoring_v2.versions v WHERE v.tenant_id=NEW.tenant_id AND v.environment_id=NEW.environment_id
                AND v.head_id=NEW.head_id AND v.id=NEW.source_version_id)) THEN
              RAISE EXCEPTION 'PUBLISHED_IMMUTABLE' USING ERRCODE='23514'; END IF;
          END IF;
          IF NEW.status=1 AND NOT EXISTS(SELECT FROM cp6_form_authoring_v2.versions v WHERE v.tenant_id=NEW.tenant_id
            AND v.environment_id=NEW.environment_id AND v.head_id=NEW.head_id AND v.id=NEW.id AND v.version_number=NEW.version_number
            AND v.snapshot_json::jsonb->'fields'->>'SchemaJson'=NEW.schema_json
            AND v.snapshot_json::jsonb->'fields'->>'Name'=NEW.name
            AND (v.snapshot_json::jsonb->'fields'->>'Category') IS NOT DISTINCT FROM NEW.category
            AND (v.snapshot_json::jsonb->'fields'->>'SubCategory') IS NOT DISTINCT FROM NEW.sub_category) THEN
            RAISE EXCEPTION 'PUBLICATION_RECEIPT_REQUIRED' USING ERRCODE='23514'; END IF;
          NEW.row_version:=gen_random_uuid(); RETURN NEW;
        END $$;
        CREATE TRIGGER publication_draft_history BEFORE DELETE OR TRUNCATE ON cp6_form_authoring_v2.drafts
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE OR REPLACE FUNCTION cp6_form_authoring_v2.head_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP='UPDATE' AND ROW(NEW.tenant_id,NEW.environment_id,NEW.id,NEW.form_key,NEW.owner_id,NEW.owner_dept_id)
            IS DISTINCT FROM ROW(OLD.tenant_id,OLD.environment_id,OLD.id,OLD.form_key,OLD.owner_id,OLD.owner_dept_id) THEN
            RAISE EXCEPTION 'AUTHORING_IDENTITY_IMMUTABLE' USING ERRCODE='23514'; END IF;
          IF EXISTS(SELECT FROM public."Wf_FormDef" h WHERE h."TenantId"=NEW.tenant_id
            AND h."FormKey"=NEW.form_key::bpchar COLLATE public.cp6_ci_as_provider_v1) THEN
            RAISE EXCEPTION 'DEFINITION_IDENTITY_CONFLICT' USING ERRCODE='23514'; END IF;
          RETURN NEW;
        END $$;
        CREATE FUNCTION cp6_form_authoring_v2.publication_ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT cp6_form_authoring_v2.ready() AND NOT EXISTS (
            SELECT FROM (VALUES
              ('publication_policy','publication_policy_fence','cp6_scope_authorization_v1.before_authority_change()',62),
              ('publication_policy','publication_policy_version','cp6_workflow_authoring_v2.policy_version()',23),
              ('publication_previews','publication_previews_immutable','cp6_scope_authorization_v1.immutable()',58),
              ('versions','publication_versions_immutable','cp6_scope_authorization_v1.immutable()',58),
              ('drafts','publication_draft_history','cp6_scope_authorization_v1.immutable()',42)
            ) expected(tbl,name,fn,typ) WHERE NOT EXISTS (SELECT FROM pg_trigger t WHERE
              t.tgrelid=to_regclass('cp6_form_authoring_v2.'||expected.tbl) AND t.tgname=expected.name AND t.tgfoid=to_regprocedure(expected.fn)
              AND t.tgenabled IN('O','A') AND t.tgtype=expected.typ AND t.tgqual IS NULL AND t.tgnargs=0 AND NOT t.tgisinternal))
        $$;
        ALTER TABLE cp6_form_authoring_v2.versions ADD UNIQUE(tenant_id,environment_id,head_id,id);
        CREATE TABLE cp6_form_authoring_v2.activation_history (
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,head_id uuid NOT NULL,revision uuid NOT NULL,
          previous_revision uuid NOT NULL,enabled boolean NOT NULL,published_version_id uuid NOT NULL,
          actor_id uuid NOT NULL,reason text NOT NULL CHECK(length(reason) BETWEEN 1 AND 500),changed_at timestamptz NOT NULL,
          PRIMARY KEY(tenant_id,environment_id,head_id,revision),
          FOREIGN KEY(tenant_id,environment_id,head_id,published_version_id)
            REFERENCES cp6_form_authoring_v2.versions(tenant_id,environment_id,head_id,id));
        CREATE TABLE cp6_form_authoring_v2.activation_current (
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,head_id uuid NOT NULL,
          revision uuid NOT NULL DEFAULT gen_random_uuid(),enabled boolean NOT NULL DEFAULT false,
          published_version_id uuid,changed_at timestamptz,CHECK(NOT enabled OR published_version_id IS NOT NULL),
          PRIMARY KEY(tenant_id,environment_id,head_id),
          FOREIGN KEY(tenant_id,environment_id,head_id) REFERENCES cp6_form_authoring_v2.heads(tenant_id,environment_id,id),
          FOREIGN KEY(tenant_id,environment_id,head_id,published_version_id)
            REFERENCES cp6_form_authoring_v2.versions(tenant_id,environment_id,head_id,id));
        INSERT INTO cp6_form_authoring_v2.activation_current(tenant_id,environment_id,head_id)
          SELECT tenant_id,environment_id,id FROM cp6_form_authoring_v2.heads;
        CREATE FUNCTION cp6_form_authoring_v2.initialize_activation() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          INSERT INTO cp6_form_authoring_v2.activation_current(tenant_id,environment_id,head_id) VALUES(NEW.tenant_id,NEW.environment_id,NEW.id);
          RETURN NEW;
        END $$;
        CREATE TRIGGER activation_initialize AFTER INSERT ON cp6_form_authoring_v2.heads FOR EACH ROW
          EXECUTE FUNCTION cp6_form_authoring_v2.initialize_activation();
        CREATE FUNCTION cp6_form_authoring_v2.activation_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP='DELETE' OR TG_OP='TRUNCATE' THEN RAISE EXCEPTION 'ACTIVATION_HISTORY_REQUIRED' USING ERRCODE='23514'; END IF;
          IF TG_OP='INSERT' THEN
            IF NEW.enabled OR NEW.published_version_id IS NOT NULL OR NEW.changed_at IS NOT NULL THEN
              RAISE EXCEPTION 'ACTIVATION_INITIAL_DISABLED' USING ERRCODE='23514'; END IF;
          ELSE
            IF ROW(NEW.tenant_id,NEW.environment_id,NEW.head_id) IS DISTINCT FROM ROW(OLD.tenant_id,OLD.environment_id,OLD.head_id)
              OR NOT EXISTS(SELECT FROM cp6_form_authoring_v2.activation_history h
                WHERE h.tenant_id=NEW.tenant_id AND h.environment_id=NEW.environment_id AND h.head_id=NEW.head_id
                  AND h.revision=NEW.revision AND h.previous_revision=OLD.revision AND h.revision<>OLD.revision
                  AND h.enabled=NEW.enabled AND h.published_version_id=NEW.published_version_id AND h.changed_at=NEW.changed_at) THEN
              RAISE EXCEPTION 'ACTIVATION_HISTORY_REQUIRED' USING ERRCODE='23514'; END IF;
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER activation_root BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_form_authoring_v2.activation_current
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER activation_scope BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_form_authoring_v2.activation_current
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.before_authority_change();
        CREATE TRIGGER activation_guard BEFORE INSERT OR UPDATE OR DELETE ON cp6_form_authoring_v2.activation_current
          FOR EACH ROW EXECUTE FUNCTION cp6_form_authoring_v2.activation_guard();
        CREATE TRIGGER activation_no_truncate BEFORE TRUNCATE ON cp6_form_authoring_v2.activation_current
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_form_authoring_v2.activation_guard();
        CREATE TRIGGER activation_history_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_form_authoring_v2.activation_history
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        ALTER TABLE cp6_form_authoring_v2.commands DROP CONSTRAINT commands_operation_check,
          ADD CHECK(operation IN('FORM_SAVE_DRAFT','FORM_PUBLISH','FORM_CREATE_DRAFT','FORM_SET_ACTIVATION'));
        ALTER TABLE cp6_form_authoring_v2.audit DROP CONSTRAINT audit_operation_check,
          ADD CHECK(operation IN('FORM_SAVE_DRAFT','FORM_PUBLISH','FORM_CREATE_DRAFT','FORM_SET_ACTIVATION'));
        CREATE FUNCTION cp6_form_authoring_v2.activation_ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT cp6_form_authoring_v2.publication_ready() AND NOT EXISTS (
            SELECT FROM (VALUES
              ('heads','activation_initialize','cp6_form_authoring_v2.initialize_activation()',5),
              ('activation_current','activation_root','cp6_authentication_v1.before_change()',62),
              ('activation_current','activation_scope','cp6_scope_authorization_v1.before_authority_change()',58),
              ('activation_current','activation_guard','cp6_form_authoring_v2.activation_guard()',31),
              ('activation_current','activation_no_truncate','cp6_form_authoring_v2.activation_guard()',34),
              ('activation_history','activation_history_immutable','cp6_scope_authorization_v1.immutable()',58)
            ) expected(tbl,name,fn,typ) WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE
              t.tgrelid=to_regclass('cp6_form_authoring_v2.'||expected.tbl) AND t.tgname=expected.name AND t.tgfoid=to_regprocedure(expected.fn)
              AND t.tgenabled IN('O','A') AND t.tgtype=expected.typ AND t.tgqual IS NULL AND t.tgnargs=0 AND NOT t.tgisinternal))
        $$;
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_form_authoring_v2 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_form_authoring_v2 FROM PUBLIC;
        """);
    protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException("Form publication and activation history require forward recovery.");
}
