using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context))]
[Migration("20261028170000_ToolApprovalFactsV2")]
public sealed class ToolApprovalFactsV2 : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE SCHEMA cp6_tool_approval_fact_v2;
        -- Explicit Owner service authority. Nothing is seeded or granted by this migration.
        CREATE TABLE cp6_tool_approval_fact_v2.registrations(
          environment_id uuid NOT NULL,tenant_id uuid NOT NULL,principal_id uuid NOT NULL,revision uuid NOT NULL,
          enabled boolean NOT NULL,can_receive boolean NOT NULL,can_query boolean NOT NULL,
          evidence_mode text NOT NULL CHECK(evidence_mode IN('REAL','DEMO')),
          PRIMARY KEY(environment_id,tenant_id,principal_id));
        CREATE TABLE cp6_tool_approval_fact_v2.subject_grants(
          environment_id uuid NOT NULL,tenant_id uuid NOT NULL,principal_id uuid NOT NULL,submission_id uuid NOT NULL,
          revision uuid NOT NULL,enabled boolean NOT NULL,can_receive boolean NOT NULL,can_query boolean NOT NULL,
          PRIMARY KEY(environment_id,tenant_id,principal_id,submission_id),
          FOREIGN KEY(environment_id,tenant_id,principal_id) REFERENCES cp6_tool_approval_fact_v2.registrations,
          FOREIGN KEY(environment_id,tenant_id,submission_id) REFERENCES cp6_tool_approval_v1.submissions(environment_id,tenant_id,id));
        CREATE TABLE cp6_tool_approval_fact_v2.facts(
          environment_id uuid NOT NULL,tenant_id uuid NOT NULL,decision_id uuid NOT NULL,submission_id uuid NOT NULL,
          instance_id uuid NOT NULL,tool_id uuid NOT NULL,source_event_id uuid NOT NULL,core_hash text NOT NULL,
          payload bytea NOT NULL,payload_hash text NOT NULL,evidence_json text NOT NULL,evidence_hash text NOT NULL,
          disposition text NOT NULL CHECK(disposition IN('RECORDED','STALE_RECEIPT')),
          PRIMARY KEY(environment_id,tenant_id,decision_id),UNIQUE(environment_id,tenant_id,instance_id),
          UNIQUE(environment_id,tenant_id,submission_id),
          FOREIGN KEY(environment_id,tenant_id,submission_id) REFERENCES cp6_tool_approval_v1.submissions(environment_id,tenant_id,id),
          CHECK(encode(sha256(payload),'hex')=payload_hash),
          CHECK(encode(sha256(convert_to(evidence_json,'UTF8')),'hex')=evidence_hash));
        CREATE TABLE cp6_tool_approval_fact_v2.streams(
          environment_id uuid NOT NULL,tenant_id uuid NOT NULL,id uuid NOT NULL,decision_id uuid NOT NULL,
          instance_id uuid NOT NULL,subject_revision text COLLATE "C" NOT NULL,application_unit_id text COLLATE "C" NOT NULL,
          PRIMARY KEY(environment_id,tenant_id,id),UNIQUE(environment_id,tenant_id,instance_id,subject_revision,application_unit_id),
          UNIQUE(environment_id,tenant_id,decision_id),
          FOREIGN KEY(environment_id,tenant_id,decision_id) REFERENCES cp6_tool_approval_fact_v2.facts);
        CREATE TABLE cp6_tool_approval_fact_v2.inbox(
          environment_id uuid NOT NULL,tenant_id uuid NOT NULL,event_id uuid NOT NULL,id uuid NOT NULL,
          submission_id uuid NOT NULL,instance_id uuid NOT NULL,decision_id uuid NOT NULL,principal_id uuid NOT NULL,
          sequence text COLLATE "C" NOT NULL CHECK(sequence~'^[1-9][0-9]*$'),raw bytea NOT NULL,raw_hash text NOT NULL,
          payload bytea NOT NULL,payload_hash text NOT NULL,core_hash text NOT NULL,state text NOT NULL CHECK(state IN('RECORDED','STALE_RECEIPT','GAP')),
          ack_json text NOT NULL,audit_json text NOT NULL,received_at timestamptz NOT NULL,
          PRIMARY KEY(environment_id,tenant_id,event_id),UNIQUE(environment_id,tenant_id,id),
          FOREIGN KEY(environment_id,tenant_id,submission_id) REFERENCES cp6_tool_approval_v1.submissions(environment_id,tenant_id,id),
          CHECK(encode(sha256(raw),'hex')=raw_hash),CHECK(encode(sha256(payload),'hex')=payload_hash),
          CHECK((sequence='1')=(state<>'GAP')));
        CREATE TABLE cp6_tool_approval_fact_v2.conflicts(
          environment_id uuid NOT NULL,tenant_id uuid NOT NULL,id uuid NOT NULL,event_id uuid NOT NULL,
          submission_id uuid NOT NULL,principal_id uuid NOT NULL,original_hash text,raw bytea NOT NULL,
          raw_hash text NOT NULL,payload_hash text NOT NULL,code text NOT NULL,recorded_at timestamptz NOT NULL,
          PRIMARY KEY(environment_id,tenant_id,id),UNIQUE(environment_id,tenant_id,event_id,payload_hash,code),
          FOREIGN KEY(environment_id,tenant_id,submission_id) REFERENCES cp6_tool_approval_v1.submissions(environment_id,tenant_id,id),
          CHECK(encode(sha256(raw),'hex')=raw_hash));
        DO $$ DECLARE tbl text; BEGIN
          FOREACH tbl IN ARRAY ARRAY['registrations','subject_grants'] LOOP
            EXECUTE format('CREATE TRIGGER fact_authority BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_tool_approval_fact_v2.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.before_authority_change()',tbl);
          END LOOP;
          FOREACH tbl IN ARRAY ARRAY['facts','streams','inbox','conflicts'] LOOP
            EXECUTE format('CREATE TRIGGER fact_root BEFORE INSERT ON cp6_tool_approval_fact_v2.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change()',tbl);
            EXECUTE format('CREATE TRIGGER fact_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_tool_approval_fact_v2.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable()',tbl);
          END LOOP;
        END $$;
        CREATE FUNCTION cp6_tool_approval_fact_v2.source_complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF NOT EXISTS(SELECT FROM cp6_approval_decision_v2.decisions d JOIN cp6_tool_approval_v1.submissions s
            ON s.environment_id=d.environment_id AND s.tenant_id=d.tenant_id AND s.id=d.submission_id
            WHERE d.environment_id=NEW.environment_id AND d.tenant_id=NEW.tenant_id AND d.id=NEW.decision_id
              AND d.instance_id=NEW.instance_id AND d.submission_id=NEW.submission_id AND s.tool_id=NEW.tool_id
              AND d.event_id=NEW.source_event_id AND d.payload=NEW.payload AND d.payload_digest=NEW.payload_hash
              AND d.evidence_json=NEW.evidence_json AND d.evidence_digest=NEW.evidence_hash) THEN
            RAISE EXCEPTION 'OWNER_NATIVE_DECISION_SOURCE_REQUIRED' USING ERRCODE='23514'; END IF;
          IF NOT EXISTS(SELECT FROM cp6_tool_approval_fact_v2.streams s
            JOIN cp6_tool_approval_fact_v2.inbox i ON i.environment_id=s.environment_id AND i.tenant_id=s.tenant_id AND i.decision_id=s.decision_id
            JOIN cp6_tool_approval_v1.submissions o ON o.environment_id=NEW.environment_id AND o.tenant_id=NEW.tenant_id AND o.id=NEW.submission_id
            WHERE s.environment_id=NEW.environment_id AND s.tenant_id=NEW.tenant_id AND s.decision_id=NEW.decision_id
              AND s.instance_id=NEW.instance_id AND s.subject_revision=convert_from(NEW.payload,'UTF8')::jsonb->'subject'->>'businessRevision'
              AND s.application_unit_id=o.request_id::text AND i.submission_id=NEW.submission_id AND i.instance_id=NEW.instance_id
              AND i.core_hash=NEW.core_hash AND i.state=NEW.disposition) THEN
            RAISE EXCEPTION 'OWNER_FACT_INBOX_STREAM_REQUIRED' USING ERRCODE='23514'; END IF;
          RETURN NEW;
        END $$;
        CREATE CONSTRAINT TRIGGER fact_source_complete AFTER INSERT ON cp6_tool_approval_fact_v2.facts
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_tool_approval_fact_v2.source_complete();
        CREATE FUNCTION cp6_tool_approval_fact_v2.complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE a jsonb; BEGIN
          a=NEW.ack_json::jsonb;
          IF (a->>'eventId')::uuid IS DISTINCT FROM NEW.event_id OR (a->>'inboxId')::uuid IS DISTINCT FROM NEW.id
            OR (a->>'decisionId')::uuid IS DISTINCT FROM NEW.decision_id OR (a->>'submissionId')::uuid IS DISTINCT FROM NEW.submission_id
            OR a->>'receiveState' IS DISTINCT FROM NEW.state OR a->>'application' IS DISTINCT FROM 'NOT_APPLIED'
            OR a->'applicationReceiptId' IS DISTINCT FROM 'null'::jsonb THEN
            RAISE EXCEPTION 'OWNER_FACT_ACK_MISMATCH' USING ERRCODE='23514'; END IF;
          IF NEW.state<>'GAP' AND NOT EXISTS(SELECT FROM cp6_tool_approval_fact_v2.facts f
            JOIN cp6_tool_approval_fact_v2.streams s ON s.environment_id=f.environment_id AND s.tenant_id=f.tenant_id AND s.decision_id=f.decision_id
            WHERE f.environment_id=NEW.environment_id AND f.tenant_id=NEW.tenant_id AND f.decision_id=NEW.decision_id
              AND f.submission_id=NEW.submission_id AND f.instance_id=NEW.instance_id AND f.core_hash=NEW.core_hash
              AND s.instance_id=f.instance_id AND s.id=(a->>'receiptStreamId')::uuid) THEN
            RAISE EXCEPTION 'OWNER_FACT_STREAM_REQUIRED' USING ERRCODE='23514'; END IF;
          IF NEW.state='GAP' AND (a->'receiptStreamId' IS DISTINCT FROM 'null'::jsonb OR a->'receiptQueryRef' IS DISTINCT FROM 'null'::jsonb) THEN
            RAISE EXCEPTION 'OWNER_GAP_NOT_APPLIED' USING ERRCODE='23514'; END IF;
          RETURN NEW;
        END $$;
        CREATE CONSTRAINT TRIGGER fact_inbox_complete AFTER INSERT ON cp6_tool_approval_fact_v2.inbox
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_tool_approval_fact_v2.complete();
        CREATE FUNCTION cp6_tool_approval_fact_v2.ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT cp6_approval_decision_v2.ready() AND NOT EXISTS(SELECT FROM (VALUES('registrations'),('subject_grants')) a(tbl)
            WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid=to_regclass('cp6_tool_approval_fact_v2.'||a.tbl)
              AND t.tgname='fact_authority' AND t.tgfoid='cp6_scope_authorization_v1.before_authority_change()'::regprocedure
              AND t.tgtype=62 AND t.tgenabled IN('O','A') AND t.tgqual IS NULL))
          AND NOT EXISTS(SELECT FROM (VALUES('facts'),('streams'),('inbox'),('conflicts')) a(tbl)
            CROSS JOIN(VALUES('fact_root','cp6_authentication_v1.before_change()',6),('fact_immutable','cp6_scope_authorization_v1.immutable()',58)) b(name,fn,typ)
            WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid=to_regclass('cp6_tool_approval_fact_v2.'||a.tbl)
              AND t.tgname=b.name AND t.tgfoid=to_regprocedure(b.fn) AND t.tgtype=b.typ AND t.tgenabled IN('O','A') AND t.tgqual IS NULL))
          AND EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid='cp6_tool_approval_fact_v2.inbox'::regclass AND t.tgname='fact_inbox_complete'
            AND t.tgfoid='cp6_tool_approval_fact_v2.complete()'::regprocedure AND t.tgtype=5 AND t.tgenabled IN('O','A')
            AND t.tgqual IS NULL AND t.tgdeferrable AND t.tginitdeferred)
          AND EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid='cp6_tool_approval_fact_v2.facts'::regclass AND t.tgname='fact_source_complete'
            AND t.tgfoid='cp6_tool_approval_fact_v2.source_complete()'::regprocedure AND t.tgtype=5 AND t.tgenabled IN('O','A')
            AND t.tgqual IS NULL AND t.tgdeferrable AND t.tginitdeferred);
        $$;
        """);
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Forward recovery only; received original decisions are permanent.");
}
