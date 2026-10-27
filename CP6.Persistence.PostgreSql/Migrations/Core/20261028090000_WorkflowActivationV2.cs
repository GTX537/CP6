using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261028090000_WorkflowActivationV2")]
public sealed class WorkflowActivationV2 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE cp6_workflow_authoring_v2.versions ADD UNIQUE(tenant_id,environment_id,head_id,id);
        CREATE TABLE cp6_workflow_authoring_v2.activation_history (
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,head_id uuid NOT NULL,revision uuid NOT NULL,
          previous_revision uuid NOT NULL,enabled boolean NOT NULL,published_version_id uuid NOT NULL,
          actor_id uuid NOT NULL,reason text NOT NULL CHECK(length(reason) BETWEEN 1 AND 500),changed_at timestamptz NOT NULL,
          PRIMARY KEY(tenant_id,environment_id,head_id,revision),
          FOREIGN KEY(tenant_id,environment_id,head_id,published_version_id)
            REFERENCES cp6_workflow_authoring_v2.versions(tenant_id,environment_id,head_id,id));
        CREATE TABLE cp6_workflow_authoring_v2.activation_current (
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,head_id uuid NOT NULL,
          revision uuid NOT NULL DEFAULT gen_random_uuid(),enabled boolean NOT NULL DEFAULT false,
          published_version_id uuid,changed_at timestamptz,CHECK(NOT enabled OR published_version_id IS NOT NULL),
          PRIMARY KEY(tenant_id,environment_id,head_id),
          FOREIGN KEY(tenant_id,environment_id,head_id) REFERENCES cp6_workflow_authoring_v2.heads(tenant_id,environment_id,id),
          FOREIGN KEY(tenant_id,environment_id,head_id,published_version_id)
            REFERENCES cp6_workflow_authoring_v2.versions(tenant_id,environment_id,head_id,id));
        INSERT INTO cp6_workflow_authoring_v2.activation_current(tenant_id,environment_id,head_id)
          SELECT tenant_id,environment_id,id FROM cp6_workflow_authoring_v2.heads;
        CREATE FUNCTION cp6_workflow_authoring_v2.initialize_activation() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          INSERT INTO cp6_workflow_authoring_v2.activation_current(tenant_id,environment_id,head_id) VALUES(NEW.tenant_id,NEW.environment_id,NEW.id);
          RETURN NEW;
        END $$;
        CREATE TRIGGER activation_initialize AFTER INSERT ON cp6_workflow_authoring_v2.heads FOR EACH ROW
          EXECUTE FUNCTION cp6_workflow_authoring_v2.initialize_activation();
        CREATE FUNCTION cp6_workflow_authoring_v2.activation_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP='DELETE' OR TG_OP='TRUNCATE' THEN RAISE EXCEPTION 'ACTIVATION_HISTORY_REQUIRED' USING ERRCODE='23514'; END IF;
          IF TG_OP='INSERT' THEN
            IF NEW.enabled OR NEW.published_version_id IS NOT NULL OR NEW.changed_at IS NOT NULL THEN
              RAISE EXCEPTION 'ACTIVATION_INITIAL_DISABLED' USING ERRCODE='23514'; END IF;
          ELSE
            IF ROW(NEW.tenant_id,NEW.environment_id,NEW.head_id) IS DISTINCT FROM ROW(OLD.tenant_id,OLD.environment_id,OLD.head_id)
              OR NOT EXISTS(SELECT FROM cp6_workflow_authoring_v2.activation_history h
                WHERE h.tenant_id=NEW.tenant_id AND h.environment_id=NEW.environment_id AND h.head_id=NEW.head_id
                  AND h.revision=NEW.revision AND h.previous_revision=OLD.revision AND h.revision<>OLD.revision
                  AND h.enabled=NEW.enabled AND h.published_version_id=NEW.published_version_id AND h.changed_at=NEW.changed_at) THEN
              RAISE EXCEPTION 'ACTIVATION_HISTORY_REQUIRED' USING ERRCODE='23514'; END IF;
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER activation_root BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_workflow_authoring_v2.activation_current
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER activation_scope BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_workflow_authoring_v2.activation_current
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.before_authority_change();
        CREATE TRIGGER activation_guard BEFORE INSERT OR UPDATE OR DELETE ON cp6_workflow_authoring_v2.activation_current
          FOR EACH ROW EXECUTE FUNCTION cp6_workflow_authoring_v2.activation_guard();
        CREATE TRIGGER activation_no_truncate BEFORE TRUNCATE ON cp6_workflow_authoring_v2.activation_current
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_workflow_authoring_v2.activation_guard();
        CREATE TRIGGER activation_history_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_workflow_authoring_v2.activation_history
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        ALTER TABLE cp6_workflow_authoring_v2.commands DROP CONSTRAINT commands_operation_check,
          ADD CHECK(operation IN('FLOW_SAVE_DRAFT','FLOW_PUBLISH','FLOW_CREATE_DRAFT','FLOW_SET_ACTIVATION'));
        ALTER TABLE cp6_workflow_authoring_v2.audit DROP CONSTRAINT audit_operation_check,
          ADD CHECK(operation IN('FLOW_SAVE_DRAFT','FLOW_PUBLISH','FLOW_CREATE_DRAFT','FLOW_SET_ACTIVATION'));
        CREATE FUNCTION cp6_workflow_authoring_v2.activation_ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT cp6_workflow_authoring_v2.publication_ready() AND NOT EXISTS (
            SELECT FROM (VALUES
              ('heads','activation_initialize','cp6_workflow_authoring_v2.initialize_activation()',5),
              ('activation_current','activation_root','cp6_authentication_v1.before_change()',62),
              ('activation_current','activation_scope','cp6_scope_authorization_v1.before_authority_change()',58),
              ('activation_current','activation_guard','cp6_workflow_authoring_v2.activation_guard()',31),
              ('activation_current','activation_no_truncate','cp6_workflow_authoring_v2.activation_guard()',34),
              ('activation_history','activation_history_immutable','cp6_scope_authorization_v1.immutable()',58)
            ) expected(tbl,name,fn,typ) WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE
              t.tgrelid=to_regclass('cp6_workflow_authoring_v2.'||expected.tbl) AND t.tgname=expected.name AND t.tgfoid=to_regprocedure(expected.fn)
              AND t.tgenabled IN('O','A') AND t.tgtype=expected.typ AND t.tgqual IS NULL AND t.tgnargs=0 AND NOT t.tgisinternal))
        $$;
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_workflow_authoring_v2 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_workflow_authoring_v2 FROM PUBLIC;
        """);
    protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException("Activation history requires forward recovery.");
}
