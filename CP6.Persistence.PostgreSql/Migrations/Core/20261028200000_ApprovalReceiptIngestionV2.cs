using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261028200000_ApprovalReceiptIngestionV2")]
public sealed class ApprovalReceiptIngestionV2 : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE SCHEMA cp6_approval_receipt_v2;
        -- Receipt authority is separate from intake and decision-delivery authority. No grants are seeded.
        CREATE TABLE cp6_approval_receipt_v2.registrations(
          environment_id uuid NOT NULL,tenant_id uuid NOT NULL,principal_id uuid NOT NULL,owner text COLLATE "C" NOT NULL,
          revision uuid NOT NULL,enabled boolean NOT NULL,can_receive boolean NOT NULL,can_query boolean NOT NULL,
          evidence_mode text NOT NULL CHECK(evidence_mode IN('REAL','DEMO')),
          CHECK(owner='Main.Wms.ToolDisposition.v1'),PRIMARY KEY(environment_id,tenant_id,principal_id));
        CREATE TABLE cp6_approval_receipt_v2.subject_grants(
          environment_id uuid NOT NULL,tenant_id uuid NOT NULL,principal_id uuid NOT NULL,submission_id uuid NOT NULL,
          owner text NOT NULL CHECK(owner='Main.Wms.ToolDisposition.v1'),revision uuid NOT NULL,
          enabled boolean NOT NULL,can_receive boolean NOT NULL,can_query boolean NOT NULL,
          PRIMARY KEY(environment_id,tenant_id,principal_id,submission_id),
          FOREIGN KEY(environment_id,tenant_id,principal_id) REFERENCES cp6_approval_receipt_v2.registrations,
          FOREIGN KEY(tenant_id,environment_id,owner,submission_id) REFERENCES cp6_approval_intake_v2.submissions(tenant_id,environment_id,owner,id));
        CREATE TABLE cp6_approval_receipt_v2.streams(
          environment_id uuid NOT NULL,tenant_id uuid NOT NULL,id uuid NOT NULL,owner text NOT NULL,
          submission_id uuid NOT NULL,instance_id uuid NOT NULL,decision_id uuid NOT NULL,
          subject_revision text COLLATE "C" NOT NULL,application_unit_id text COLLATE "C" NOT NULL,
          highest_revision bigint NOT NULL DEFAULT 0 CHECK(highest_revision>=0),latest_receipt_id uuid,row_version uuid NOT NULL,
          CHECK((highest_revision=0)=(latest_receipt_id IS NULL)),
          PRIMARY KEY(environment_id,tenant_id,id),UNIQUE(environment_id,tenant_id,instance_id,subject_revision,application_unit_id),
          FOREIGN KEY(tenant_id,environment_id,owner,submission_id) REFERENCES cp6_approval_intake_v2.submissions(tenant_id,environment_id,owner,id),
          FOREIGN KEY(tenant_id,environment_id,decision_id) REFERENCES cp6_approval_decision_v2.decisions(tenant_id,environment_id,id));
        CREATE TABLE cp6_approval_receipt_v2.inbox(
          environment_id uuid NOT NULL,tenant_id uuid NOT NULL,event_id uuid NOT NULL,receipt_id uuid NOT NULL,
          stream_id uuid NOT NULL,revision bigint NOT NULL CHECK(revision>0),previous_id uuid,submission_id uuid NOT NULL,
          principal_id uuid NOT NULL,raw bytea NOT NULL CHECK(octet_length(raw)<=1048576),raw_hash text NOT NULL,
          payload bytea NOT NULL,payload_hash text NOT NULL,audit_json text NOT NULL,audit_hash text NOT NULL,received_at timestamptz NOT NULL,
          PRIMARY KEY(environment_id,tenant_id,event_id),UNIQUE(environment_id,tenant_id,receipt_id),
          UNIQUE(environment_id,tenant_id,stream_id,revision),
          FOREIGN KEY(environment_id,tenant_id,stream_id) REFERENCES cp6_approval_receipt_v2.streams,
          CHECK(encode(sha256(raw),'hex')=raw_hash),CHECK(encode(sha256(payload),'hex')=payload_hash),
          CHECK(encode(sha256(convert_to(audit_json,'UTF8')),'hex')=audit_hash));
        CREATE TABLE cp6_approval_receipt_v2.history(
          environment_id uuid NOT NULL,tenant_id uuid NOT NULL,stream_id uuid NOT NULL,revision bigint NOT NULL CHECK(revision>0),
          receipt_id uuid NOT NULL,event_id uuid NOT NULL,principal_id uuid NOT NULL,recorded_at timestamptz NOT NULL,
          PRIMARY KEY(environment_id,tenant_id,stream_id,revision),UNIQUE(environment_id,tenant_id,receipt_id),
          FOREIGN KEY(environment_id,tenant_id,event_id) REFERENCES cp6_approval_receipt_v2.inbox);
        ALTER TABLE cp6_approval_receipt_v2.streams ADD FOREIGN KEY(environment_id,tenant_id,latest_receipt_id)
          REFERENCES cp6_approval_receipt_v2.history(environment_id,tenant_id,receipt_id) DEFERRABLE INITIALLY DEFERRED;
        CREATE TABLE cp6_approval_receipt_v2.gaps(
          environment_id uuid NOT NULL,tenant_id uuid NOT NULL,stream_id uuid NOT NULL,target_revision bigint NOT NULL CHECK(target_revision>0),
          completed boolean NOT NULL DEFAULT false,epoch bigint NOT NULL DEFAULT 0,lease_token uuid,lease_until timestamptz,
          attempts bigint NOT NULL DEFAULT 0,error_code text,
          PRIMARY KEY(environment_id,tenant_id,stream_id),
          FOREIGN KEY(environment_id,tenant_id,stream_id) REFERENCES cp6_approval_receipt_v2.streams);
        CREATE TABLE cp6_approval_receipt_v2.conflicts(
          environment_id uuid NOT NULL,tenant_id uuid NOT NULL,id uuid NOT NULL,event_id uuid NOT NULL,submission_id uuid NOT NULL,
          principal_id uuid NOT NULL,original_hash text,raw bytea NOT NULL,raw_hash text NOT NULL,payload_hash text NOT NULL,
          code text NOT NULL,recorded_at timestamptz NOT NULL,
          PRIMARY KEY(environment_id,tenant_id,id),UNIQUE(environment_id,tenant_id,event_id,payload_hash,code),
          CHECK(encode(sha256(raw),'hex')=raw_hash));
        CREATE FUNCTION cp6_approval_receipt_v2.stream_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP IN('DELETE','TRUNCATE') THEN RAISE EXCEPTION 'RECEIPT_PROJECTION_PERMANENT'; END IF;
          IF TG_OP='UPDATE' AND ((to_jsonb(NEW)-ARRAY['highest_revision','latest_receipt_id','row_version']) IS DISTINCT FROM
            (to_jsonb(OLD)-ARRAY['highest_revision','latest_receipt_id','row_version']) OR NEW.highest_revision<>OLD.highest_revision+1
            OR NEW.row_version=OLD.row_version OR NEW.latest_receipt_id=OLD.latest_receipt_id) THEN
            RAISE EXCEPTION 'RECEIPT_PROJECTION_SEQUENCE'; END IF;
          IF TG_OP='INSERT' AND (NEW.highest_revision<>0 OR NEW.latest_receipt_id IS NOT NULL) THEN RAISE EXCEPTION 'RECEIPT_PROJECTION_SEQUENCE'; END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER receipt_stream_guard BEFORE INSERT OR UPDATE OR DELETE ON cp6_approval_receipt_v2.streams
          FOR EACH ROW EXECUTE FUNCTION cp6_approval_receipt_v2.stream_guard();
        CREATE TRIGGER receipt_stream_truncate BEFORE TRUNCATE ON cp6_approval_receipt_v2.streams
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_approval_receipt_v2.stream_guard();
        CREATE FUNCTION cp6_approval_receipt_v2.advance() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE s cp6_approval_receipt_v2.streams;i cp6_approval_receipt_v2.inbox;p jsonb;prior jsonb; BEGIN
          SELECT * INTO s FROM cp6_approval_receipt_v2.streams WHERE environment_id=NEW.environment_id AND tenant_id=NEW.tenant_id AND id=NEW.stream_id FOR UPDATE;
          SELECT * INTO i FROM cp6_approval_receipt_v2.inbox WHERE environment_id=NEW.environment_id AND tenant_id=NEW.tenant_id AND event_id=NEW.event_id;
          p=convert_from(i.payload,'UTF8')::jsonb;
          IF s.id IS NULL OR i.event_id IS NULL OR (i.stream_id,i.revision,i.receipt_id) IS DISTINCT FROM (NEW.stream_id,NEW.revision,NEW.receipt_id)
            OR NEW.revision<>s.highest_revision+1 OR i.previous_id IS DISTINCT FROM s.latest_receipt_id THEN RAISE EXCEPTION 'RECEIPT_CHAIN_INVALID'; END IF;
          IF s.latest_receipt_id IS NOT NULL THEN
            SELECT convert_from(payload,'UTF8')::jsonb INTO prior FROM cp6_approval_receipt_v2.inbox WHERE environment_id=s.environment_id AND tenant_id=s.tenant_id AND receipt_id=s.latest_receipt_id;
            IF prior->>'application' IS DISTINCT FROM 'BLOCKED' THEN RAISE EXCEPTION 'RECEIPT_CHAIN_TERMINAL'; END IF;
          END IF;
          UPDATE cp6_approval_receipt_v2.streams SET highest_revision=NEW.revision,latest_receipt_id=NEW.receipt_id,row_version=gen_random_uuid()
            WHERE environment_id=s.environment_id AND tenant_id=s.tenant_id AND id=s.id AND row_version=s.row_version;
          IF NOT FOUND THEN RAISE EXCEPTION 'RECEIPT_PROJECTION_CHANGED'; END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER receipt_advance AFTER INSERT ON cp6_approval_receipt_v2.history FOR EACH ROW EXECUTE FUNCTION cp6_approval_receipt_v2.advance();
        CREATE FUNCTION cp6_approval_receipt_v2.complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE s cp6_approval_receipt_v2.streams; i cp6_approval_receipt_v2.inbox;p jsonb;sid uuid; BEGIN
          IF TG_TABLE_NAME='streams' THEN sid=NEW.id; ELSE sid=NEW.stream_id; END IF;
          SELECT * INTO s FROM cp6_approval_receipt_v2.streams WHERE environment_id=NEW.environment_id AND tenant_id=NEW.tenant_id AND id=sid;
          IF s.id IS NULL OR s.highest_revision<>(SELECT coalesce(max(revision),0) FROM cp6_approval_receipt_v2.history WHERE environment_id=s.environment_id AND tenant_id=s.tenant_id AND stream_id=s.id)
            OR (s.highest_revision>0 AND NOT EXISTS(SELECT FROM cp6_approval_receipt_v2.history WHERE environment_id=s.environment_id AND tenant_id=s.tenant_id AND stream_id=s.id AND revision=s.highest_revision AND receipt_id=s.latest_receipt_id)) THEN
            RAISE EXCEPTION 'RECEIPT_PROJECTION_INCOMPLETE'; END IF;
          -- Check each newly inserted original once, rather than rescanning the entire stream for every deferred trigger.
          IF TG_TABLE_NAME='inbox' THEN
            SELECT * INTO i FROM cp6_approval_receipt_v2.inbox WHERE environment_id=s.environment_id AND tenant_id=s.tenant_id AND event_id=NEW.event_id;
            p=convert_from(i.payload,'UTF8')::jsonb;
            IF NOT EXISTS(SELECT FROM cp6_tool_application_v2.receipts r WHERE r.environment_id=i.environment_id AND r.tenant_id=i.tenant_id
              AND r.id=i.receipt_id AND r.event_id=i.event_id AND r.stream_id=i.stream_id AND r.revision=i.revision AND r.previous_id IS NOT DISTINCT FROM i.previous_id
              AND r.submission_id=i.submission_id AND r.decision_id=s.decision_id AND r.payload=i.payload AND r.payload_hash=i.payload_hash)
              OR p->>'instanceId' IS DISTINCT FROM s.instance_id::text OR p->>'producerOwner' IS DISTINCT FROM s.owner
              OR p->'subject'->>'businessRevision' IS DISTINCT FROM s.subject_revision OR p->'subject'->>'subjectId' IS DISTINCT FROM s.application_unit_id
              OR i.submission_id<>s.submission_id THEN RAISE EXCEPTION 'RECEIPT_NATIVE_SOURCE_REQUIRED'; END IF;
            IF i.revision<=s.highest_revision THEN
              IF NOT EXISTS(SELECT FROM cp6_approval_receipt_v2.history h WHERE h.environment_id=i.environment_id AND h.tenant_id=i.tenant_id
                AND h.stream_id=i.stream_id AND h.revision=i.revision AND h.receipt_id=i.receipt_id AND h.event_id=i.event_id) THEN RAISE EXCEPTION 'RECEIPT_HISTORY_REQUIRED'; END IF;
            ELSIF NOT EXISTS(SELECT FROM cp6_approval_receipt_v2.gaps g WHERE g.environment_id=i.environment_id AND g.tenant_id=i.tenant_id
              AND g.stream_id=i.stream_id AND NOT g.completed AND g.target_revision>=i.revision) THEN RAISE EXCEPTION 'RECEIPT_GAP_WORK_REQUIRED'; END IF;
          END IF;
          IF EXISTS(SELECT FROM cp6_approval_receipt_v2.gaps g WHERE g.environment_id=s.environment_id AND g.tenant_id=s.tenant_id AND g.stream_id=s.id
            AND g.completed IS DISTINCT FROM (g.target_revision<=s.highest_revision)) THEN RAISE EXCEPTION 'RECEIPT_GAP_STATE_INVALID'; END IF;
          RETURN NULL;
        END $$;
        DO $$ DECLARE tbl text; BEGIN
          FOREACH tbl IN ARRAY ARRAY['registrations','subject_grants'] LOOP
            EXECUTE format('CREATE TRIGGER receipt_authority BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_approval_receipt_v2.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.before_authority_change()',tbl);
          END LOOP;
          FOREACH tbl IN ARRAY ARRAY['inbox','history','conflicts'] LOOP
            EXECUTE format('CREATE TRIGGER receipt_root BEFORE INSERT ON cp6_approval_receipt_v2.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change()',tbl);
            EXECUTE format('CREATE TRIGGER receipt_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_approval_receipt_v2.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable()',tbl);
          END LOOP;
          FOREACH tbl IN ARRAY ARRAY['streams','gaps'] LOOP
            EXECUTE format('CREATE TRIGGER receipt_root BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_approval_receipt_v2.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change()',tbl);
          END LOOP;
          FOREACH tbl IN ARRAY ARRAY['streams','inbox','history','gaps'] LOOP
            EXECUTE format('CREATE CONSTRAINT TRIGGER receipt_complete AFTER INSERT OR UPDATE ON cp6_approval_receipt_v2.%I DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_approval_receipt_v2.complete()',tbl);
          END LOOP;
        END $$;
        CREATE TRIGGER receipt_gap_permanent BEFORE DELETE OR TRUNCATE ON cp6_approval_receipt_v2.gaps
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE FUNCTION cp6_approval_receipt_v2.ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT cp6_tool_application_v2.ready() AND NOT EXISTS(SELECT FROM (
            SELECT a.tbl,b.* FROM (VALUES('inbox'),('history'),('conflicts')) a(tbl)
              CROSS JOIN (VALUES('receipt_root','cp6_authentication_v1.before_change()',6),('receipt_immutable','cp6_scope_authorization_v1.immutable()',58)) b(name,fn,typ)
            UNION ALL SELECT a.tbl,'receipt_authority','cp6_scope_authorization_v1.before_authority_change()',62 FROM (VALUES('registrations'),('subject_grants')) a(tbl)
            UNION ALL SELECT a.tbl,'receipt_root','cp6_authentication_v1.before_change()',62 FROM (VALUES('streams'),('gaps')) a(tbl)
            UNION ALL SELECT a.tbl,'receipt_complete','cp6_approval_receipt_v2.complete()',21 FROM (VALUES('streams'),('inbox'),('history'),('gaps')) a(tbl)
            UNION ALL SELECT 'streams','receipt_stream_guard','cp6_approval_receipt_v2.stream_guard()',31
            UNION ALL SELECT 'streams','receipt_stream_truncate','cp6_approval_receipt_v2.stream_guard()',34
            UNION ALL SELECT 'history','receipt_advance','cp6_approval_receipt_v2.advance()',5
            UNION ALL SELECT 'gaps','receipt_gap_permanent','cp6_scope_authorization_v1.immutable()',42
          ) a WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid=to_regclass('cp6_approval_receipt_v2.'||a.tbl)
            AND t.tgname=a.name AND t.tgfoid=to_regprocedure(a.fn) AND t.tgtype=a.typ AND t.tgenabled IN('O','A') AND t.tgqual IS NULL
            AND (a.name<>'receipt_complete' OR t.tgdeferrable AND t.tginitdeferred)));
        $$;
        """);
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Forward recovery only; received original receipts and projection history are permanent.");
}
