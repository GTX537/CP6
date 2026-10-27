using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)),Migration("20261028120000_ApprovalIntakeV2")]
public sealed class ApprovalIntakeV2 : Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        CREATE SCHEMA cp6_approval_intake_v2;
        CREATE UNIQUE INDEX approval_intake_published_identity ON cp6_workflow_authoring_v2.versions(tenant_id,environment_id,head_id,id);
        CREATE TABLE cp6_approval_intake_v2.submissions(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,owner text NOT NULL,id uuid NOT NULL,
          principal uuid NOT NULL,command_id uuid NOT NULL,intent_hash text NOT NULL,core_hash text NOT NULL,intent_core_hash text NOT NULL,
          payload bytea NOT NULL CHECK(octet_length(payload)<=1048576),payload_digest text NOT NULL,
          binding_id uuid NOT NULL,binding_revision uuid NOT NULL,timeline_id uuid NOT NULL,
          flow_head uuid NOT NULL,flow_version uuid NOT NULL,selection_json text NOT NULL,ack_json text NOT NULL,
          received_at timestamptz NOT NULL,PRIMARY KEY(tenant_id,environment_id,owner,id),UNIQUE(tenant_id,environment_id,owner,intent_hash),
          FOREIGN KEY(tenant_id,environment_id,binding_id,binding_revision) REFERENCES cp6_approval_binding_v2.revisions(tenant_id,environment_id,binding_id,id),
          FOREIGN KEY(tenant_id,environment_id,binding_id,timeline_id) REFERENCES cp6_approval_binding_v2.timelines(tenant_id,environment_id,binding_id,id),
          FOREIGN KEY(tenant_id,environment_id,flow_head,flow_version) REFERENCES cp6_workflow_authoring_v2.versions(tenant_id,environment_id,head_id,id));
        CREATE TABLE cp6_approval_intake_v2.start_outbox(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,owner text NOT NULL,submission_id uuid NOT NULL,
          engine_start_id uuid NOT NULL,payload bytea NOT NULL,payload_digest text NOT NULL,
          CHECK(engine_start_id=submission_id),PRIMARY KEY(tenant_id,environment_id,owner,submission_id),
          FOREIGN KEY(tenant_id,environment_id,owner,submission_id) REFERENCES cp6_approval_intake_v2.submissions(tenant_id,environment_id,owner,id) DEFERRABLE INITIALLY DEFERRED);
        CREATE TABLE cp6_approval_intake_v2.commands(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,principal uuid NOT NULL,command_id uuid NOT NULL,
          owner text NOT NULL,submission_id uuid NOT NULL,full_hash text NOT NULL,raw_body bytea NOT NULL CHECK(octet_length(raw_body)<=1048576),
          result_json text NOT NULL,audit_json text NOT NULL,recorded_at timestamptz NOT NULL,
          PRIMARY KEY(tenant_id,environment_id,principal,command_id),
          FOREIGN KEY(tenant_id,environment_id,owner,submission_id) REFERENCES cp6_approval_intake_v2.submissions(tenant_id,environment_id,owner,id));
        ALTER TABLE cp6_approval_intake_v2.submissions ADD
          FOREIGN KEY(tenant_id,environment_id,owner,id) REFERENCES cp6_approval_intake_v2.start_outbox(tenant_id,environment_id,owner,submission_id) DEFERRABLE INITIALLY DEFERRED,
          ADD FOREIGN KEY(tenant_id,environment_id,principal,command_id) REFERENCES cp6_approval_intake_v2.commands(tenant_id,environment_id,principal,command_id) DEFERRABLE INITIALLY DEFERRED;
        CREATE TABLE cp6_approval_intake_v2.aliases(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,owner text NOT NULL,requested_id uuid NOT NULL,id uuid NOT NULL,
          canonical_id uuid NOT NULL,core_hash text NOT NULL,intent_core_hash text NOT NULL,first_full_hash text NOT NULL,reason text NOT NULL,
          principal uuid NOT NULL,command_id uuid NOT NULL,CHECK(requested_id<>canonical_id),
          PRIMARY KEY(tenant_id,environment_id,owner,requested_id),UNIQUE(tenant_id,environment_id,id),
          FOREIGN KEY(tenant_id,environment_id,owner,canonical_id) REFERENCES cp6_approval_intake_v2.submissions(tenant_id,environment_id,owner,id),
          FOREIGN KEY(tenant_id,environment_id,principal,command_id) REFERENCES cp6_approval_intake_v2.commands(tenant_id,environment_id,principal,command_id) DEFERRABLE INITIALLY DEFERRED);
        CREATE FUNCTION cp6_approval_intake_v2.validate() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE p jsonb; BEGIN
          IF TG_TABLE_NAME='submissions' THEN
            p=convert_from(NEW.payload,'UTF8')::jsonb;
            IF NEW.payload_digest<>encode(sha256(NEW.payload),'hex')
              OR p->>'schema'<>'oa.app/2.0' OR p#>>'{scope,environmentId}'<>NEW.environment_id::text
              OR p#>>'{scope,tenantId}'<>NEW.tenant_id::text OR p#>>'{subject,businessOwner}'<>NEW.owner
              OR p->>'submissionId'<>NEW.id::text OR p->>'commandId'<>NEW.command_id::text
              OR EXISTS(SELECT FROM cp6_approval_intake_v2.aliases a WHERE a.tenant_id=NEW.tenant_id AND a.environment_id=NEW.environment_id AND a.owner=NEW.owner AND a.requested_id=NEW.id)
              THEN RAISE EXCEPTION 'APP_SUBMISSION_IDENTITY_INVALID' USING ERRCODE='23514'; END IF;
          ELSIF TG_TABLE_NAME='aliases' THEN
            IF EXISTS(SELECT FROM cp6_approval_intake_v2.submissions s WHERE s.tenant_id=NEW.tenant_id AND s.environment_id=NEW.environment_id AND s.owner=NEW.owner AND s.id=NEW.requested_id)
              OR NOT EXISTS(SELECT FROM cp6_approval_intake_v2.submissions s WHERE s.tenant_id=NEW.tenant_id AND s.environment_id=NEW.environment_id AND s.owner=NEW.owner AND s.id=NEW.canonical_id AND s.intent_core_hash=NEW.intent_core_hash)
              THEN RAISE EXCEPTION 'APP_ALIAS_IDENTITY_INVALID' USING ERRCODE='23514'; END IF;
          ELSIF TG_TABLE_NAME='start_outbox' THEN
            IF NOT EXISTS(SELECT FROM cp6_approval_intake_v2.submissions s WHERE s.tenant_id=NEW.tenant_id AND s.environment_id=NEW.environment_id AND s.owner=NEW.owner AND s.id=NEW.submission_id AND s.payload=NEW.payload AND s.payload_digest=NEW.payload_digest)
              THEN RAISE EXCEPTION 'APP_START_ORIGINAL_REQUIRED' USING ERRCODE='23514'; END IF;
          END IF; RETURN NEW;
        END $$;
        DO $$ DECLARE tbl text; BEGIN
          FOREACH tbl IN ARRAY ARRAY['submissions','commands','aliases','start_outbox'] LOOP
            EXECUTE format('CREATE TRIGGER intake_root BEFORE INSERT ON cp6_approval_intake_v2.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change()',tbl);
            EXECUTE format('CREATE TRIGGER intake_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_approval_intake_v2.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable()',tbl);
            EXECUTE format('CREATE TRIGGER intake_validate BEFORE INSERT ON cp6_approval_intake_v2.%I FOR EACH ROW EXECUTE FUNCTION cp6_approval_intake_v2.validate()',tbl);
          END LOOP;
        END $$;
        CREATE FUNCTION cp6_approval_intake_v2.ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT cp6_approval_binding_v2.ready() AND NOT EXISTS(
            SELECT FROM (VALUES('submissions'),('commands'),('aliases'),('start_outbox')) a(tbl)
              CROSS JOIN (VALUES('intake_root','cp6_authentication_v1.before_change()',6),('intake_immutable','cp6_scope_authorization_v1.immutable()',58),('intake_validate','cp6_approval_intake_v2.validate()',7)) b(name,fn,typ)
            WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid=to_regclass('cp6_approval_intake_v2.'||a.tbl)
              AND t.tgname=b.name AND t.tgfoid=to_regprocedure(b.fn) AND t.tgtype=b.typ AND t.tgenabled IN('O','A') AND NOT t.tgisinternal AND t.tgqual IS NULL));
        $$;
        """);
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Forward recovery only; frozen submissions and start intents are permanent.");
}
