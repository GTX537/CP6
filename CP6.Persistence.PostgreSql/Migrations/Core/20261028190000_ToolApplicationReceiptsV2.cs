using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261028190000_ToolApplicationReceiptsV2")]
public sealed class ToolApplicationReceiptsV2 : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE SCHEMA cp6_tool_application_v2;
        CREATE TABLE cp6_tool_application_v2.receipts(
          environment_id uuid NOT NULL,tenant_id uuid NOT NULL,id uuid NOT NULL,stream_id uuid NOT NULL,revision bigint NOT NULL CHECK(revision>0),
          event_id uuid NOT NULL,previous_id uuid,tool_id uuid NOT NULL,submission_id uuid NOT NULL,decision_id uuid NOT NULL,
          actor_id uuid NOT NULL,operation_id uuid NOT NULL,state text NOT NULL CHECK(state IN('BLOCKED','STALE','CONFLICT')),
          payload bytea NOT NULL,payload_hash text NOT NULL CHECK(payload_hash~'^[0-9a-f]{64}$'),audit_json text NOT NULL,audit_hash text NOT NULL,
          PRIMARY KEY(environment_id,tenant_id,id),UNIQUE(environment_id,tenant_id,stream_id,revision),UNIQUE(environment_id,tenant_id,event_id),
          UNIQUE(environment_id,tenant_id,operation_id),
          FOREIGN KEY(environment_id,tenant_id,stream_id) REFERENCES cp6_tool_approval_fact_v2.streams(environment_id,tenant_id,id),
          FOREIGN KEY(environment_id,tenant_id,previous_id) REFERENCES cp6_tool_application_v2.receipts(environment_id,tenant_id,id));
        CREATE TABLE cp6_tool_application_v2.outbox(
          environment_id uuid NOT NULL,tenant_id uuid NOT NULL,event_id uuid NOT NULL,receipt_id uuid NOT NULL,
          payload bytea NOT NULL,payload_hash text NOT NULL,
          PRIMARY KEY(environment_id,tenant_id,event_id),UNIQUE(environment_id,tenant_id,receipt_id),
          FOREIGN KEY(environment_id,tenant_id,receipt_id) REFERENCES cp6_tool_application_v2.receipts(environment_id,tenant_id,id));
        CREATE FUNCTION cp6_tool_application_v2.validate() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE p jsonb; s cp6_tool_approval_fact_v2.streams; f cp6_tool_approval_fact_v2.facts; prior cp6_tool_application_v2.receipts; BEGIN
          p=convert_from(NEW.payload,'UTF8')::jsonb;
          SELECT * INTO s FROM cp6_tool_approval_fact_v2.streams WHERE environment_id=NEW.environment_id AND tenant_id=NEW.tenant_id AND id=NEW.stream_id FOR UPDATE;
          SELECT * INTO f FROM cp6_tool_approval_fact_v2.facts WHERE environment_id=s.environment_id AND tenant_id=s.tenant_id AND decision_id=s.decision_id;
          SELECT * INTO prior FROM cp6_tool_application_v2.receipts WHERE environment_id=NEW.environment_id AND tenant_id=NEW.tenant_id AND stream_id=NEW.stream_id ORDER BY revision DESC LIMIT 1;
          IF s.id IS NULL OR f.decision_id IS NULL OR (NEW.tool_id,NEW.submission_id,NEW.decision_id) IS DISTINCT FROM (f.tool_id,f.submission_id,f.decision_id)
            OR NEW.revision<>coalesce(prior.revision,0)+1 OR NEW.previous_id IS DISTINCT FROM prior.id OR (prior.id IS NOT NULL AND prior.state<>'BLOCKED')
            OR p->>'schema' IS DISTINCT FROM 'oa.app/2.0' OR p->>'producerOwner' IS DISTINCT FROM 'Main.Wms.ToolDisposition.v1'
            OR p->>'action' IS DISTINCT FROM 'WMS_TOOL_DISCARD' OR p->>'application' IS DISTINCT FROM NEW.state
            OR p->'scope'->>'environmentId' IS DISTINCT FROM NEW.environment_id::text OR p->'scope'->>'tenantId' IS DISTINCT FROM NEW.tenant_id::text
            OR p->>'receiptId' IS DISTINCT FROM NEW.id::text OR p->>'receiptStreamId' IS DISTINCT FROM NEW.stream_id::text
            OR p->>'receiptRevision' IS DISTINCT FROM NEW.revision::text OR p->>'receiptEventId' IS DISTINCT FROM NEW.event_id::text
            OR p->>'previousReceiptId' IS DISTINCT FROM NEW.previous_id::text OR p->>'submissionId' IS DISTINCT FROM f.submission_id::text
            OR p->>'decisionId' IS DISTINCT FROM f.decision_id::text OR p->>'instanceId' IS DISTINCT FROM f.instance_id::text
            OR p->'subject' IS DISTINCT FROM convert_from(f.payload,'UTF8')::jsonb->'subject'
            OR p->'decisionPayloadDigest' IS DISTINCT FROM convert_from(f.payload,'UTF8')::jsonb->'payloadDigest'
            OR p->'subject'->>'subjectId' IS DISTINCT FROM s.application_unit_id
            OR p->>'actionActorId' IS DISTINCT FROM NEW.actor_id::text OR p->>'systemExecutionRef' IS NOT NULL
            OR p->>'appliedAt' IS NOT NULL OR p->>'businessResultRef' IS NOT NULL OR p->>'businessResultVersion' IS NOT NULL
            OR p->'auditRef'->>'owner' IS DISTINCT FROM 'Main.Wms.ToolApplicationAudit.v2'
            OR p->'auditRef'->>'id' IS DISTINCT FROM NEW.id::text OR p->'auditRef'->>'version' IS DISTINCT FROM NEW.audit_hash
            OR NEW.payload_hash<>encode(sha256(NEW.payload),'hex') OR NEW.audit_hash<>encode(sha256(convert_to(NEW.audit_json,'UTF8')),'hex') THEN
            RAISE EXCEPTION 'TOOL_APPLICATION_RECEIPT_INVALID'; END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER tool_application_validate BEFORE INSERT ON cp6_tool_application_v2.receipts FOR EACH ROW EXECUTE FUNCTION cp6_tool_application_v2.validate();
        DO $$ DECLARE tbl text; BEGIN FOREACH tbl IN ARRAY ARRAY['receipts','outbox'] LOOP
          EXECUTE format('CREATE TRIGGER tool_application_root BEFORE INSERT ON cp6_tool_application_v2.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change()',tbl);
          EXECUTE format('CREATE TRIGGER tool_application_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_tool_application_v2.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable()',tbl);
        END LOOP; END $$;
        CREATE FUNCTION cp6_tool_application_v2.complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE r cp6_tool_application_v2.receipts; BEGIN
          IF TG_TABLE_NAME='receipts' THEN r=NEW; ELSE
            SELECT * INTO r FROM cp6_tool_application_v2.receipts WHERE environment_id=NEW.environment_id AND tenant_id=NEW.tenant_id AND id=NEW.receipt_id;
          END IF;
          IF r.id IS NULL OR NOT EXISTS(SELECT FROM cp6_tool_application_v2.outbox o WHERE o.environment_id=r.environment_id AND o.tenant_id=r.tenant_id
            AND o.receipt_id=r.id AND o.event_id=r.event_id AND o.payload=r.payload AND o.payload_hash=r.payload_hash)
            OR NOT EXISTS(SELECT FROM cp6_main_owner_v1.operations c WHERE c.tenant_id=r.tenant_id
              AND c.owner='Main.Wms.ToolApplication.operations.v2' AND c.contract='Main.Wms.ToolApplication.commands.v2'
              AND c.operation_id=r.operation_id AND c.actor_id=r.actor_id AND c.state='COMMITTED'
              AND c.object_ids=ARRAY[r.tool_id] AND convert_from(c.result_bytes,'UTF8')::jsonb->'Receipt'->>'ReceiptId'=r.id::text) THEN
            RAISE EXCEPTION 'TOOL_APPLICATION_OUTBOX_REQUIRED'; END IF;
          RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER tool_application_complete AFTER INSERT ON cp6_tool_application_v2.receipts DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_tool_application_v2.complete();
        CREATE CONSTRAINT TRIGGER tool_application_complete AFTER INSERT ON cp6_tool_application_v2.outbox DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_tool_application_v2.complete();
        CREATE FUNCTION cp6_tool_application_v2.ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT cp6_tool_approval_fact_v2.ready() AND NOT EXISTS(SELECT FROM (VALUES
            ('receipts','tool_application_validate','cp6_tool_application_v2.validate()',7),
            ('receipts','tool_application_root','cp6_authentication_v1.before_change()',6),('outbox','tool_application_root','cp6_authentication_v1.before_change()',6),
            ('receipts','tool_application_immutable','cp6_scope_authorization_v1.immutable()',58),('outbox','tool_application_immutable','cp6_scope_authorization_v1.immutable()',58),
            ('receipts','tool_application_complete','cp6_tool_application_v2.complete()',5),('outbox','tool_application_complete','cp6_tool_application_v2.complete()',5)) a(tbl,name,fn,typ)
            WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid=to_regclass('cp6_tool_application_v2.'||a.tbl) AND t.tgname=a.name
              AND t.tgfoid=to_regprocedure(a.fn) AND t.tgtype=a.typ AND t.tgenabled IN('O','A') AND t.tgqual IS NULL
              AND (a.name<>'tool_application_complete' OR t.tgdeferrable AND t.tginitdeferred)));
        $$;
        """);
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Forward recovery only; application receipts and pending outbox remain immutable.");
}
