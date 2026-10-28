using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261029190000_RequirementSetReviewApplicationsV1")]
public sealed class RequirementSetReviewApplicationsV1 : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE SCHEMA cp6_requirement_set_review_application_v1;
        CREATE TABLE cp6_requirement_set_review_application_v1.receipts(
          environment_id uuid NOT NULL,tenant_id uuid NOT NULL,id uuid NOT NULL,stream_id uuid NOT NULL,revision bigint NOT NULL CHECK(revision>0),
          event_id uuid NOT NULL,previous_id uuid,requirement_set_id uuid NOT NULL,submission_id uuid NOT NULL,decision_id uuid NOT NULL,
          actor_id uuid NOT NULL,operation_id uuid NOT NULL,state text NOT NULL CHECK(state IN('APPLIED','BLOCKED','STALE','CONFLICT')),
          payload bytea NOT NULL,payload_hash text NOT NULL CHECK(payload_hash~'^[0-9a-f]{64}$'),audit_json text NOT NULL,audit_hash text NOT NULL,
          PRIMARY KEY(environment_id,tenant_id,id),UNIQUE(environment_id,tenant_id,stream_id,revision),UNIQUE(environment_id,tenant_id,event_id),
          UNIQUE(environment_id,tenant_id,operation_id),
          FOREIGN KEY(environment_id,tenant_id,stream_id) REFERENCES cp6_requirement_set_review_fact_v1.streams(environment_id,tenant_id,id),
          FOREIGN KEY(environment_id,tenant_id,previous_id) REFERENCES cp6_requirement_set_review_application_v1.receipts(environment_id,tenant_id,id));
        CREATE TABLE cp6_requirement_set_review_application_v1.outbox(
          environment_id uuid NOT NULL,tenant_id uuid NOT NULL,event_id uuid NOT NULL,receipt_id uuid NOT NULL,
          payload bytea NOT NULL,payload_hash text NOT NULL,
          PRIMARY KEY(environment_id,tenant_id,event_id),UNIQUE(environment_id,tenant_id,receipt_id),
          FOREIGN KEY(environment_id,tenant_id,receipt_id) REFERENCES cp6_requirement_set_review_application_v1.receipts(environment_id,tenant_id,id));
        CREATE TABLE cp6_requirement_set_review_application_v1.applications(
          environment_id uuid NOT NULL,tenant_id uuid NOT NULL,id uuid NOT NULL,requirement_set_id uuid NOT NULL,requirement_set_revision bigint NOT NULL,
          submission_id uuid NOT NULL,decision_id uuid NOT NULL,receipt_id uuid NOT NULL,approved boolean NOT NULL,body bytea NOT NULL,digest text NOT NULL,
          PRIMARY KEY(environment_id,tenant_id,id),UNIQUE(environment_id,tenant_id,submission_id),UNIQUE(environment_id,tenant_id,receipt_id),
          FOREIGN KEY(environment_id,tenant_id,receipt_id) REFERENCES cp6_requirement_set_review_application_v1.receipts(environment_id,tenant_id,id),
          FOREIGN KEY(environment_id,tenant_id,submission_id) REFERENCES cp6_requirement_set_review_v1.submissions(environment_id,tenant_id,id),
          CHECK(encode(sha256(body),'hex')=digest));
        CREATE UNIQUE INDEX requirement_set_approved_version ON cp6_requirement_set_review_application_v1.applications(environment_id,tenant_id,requirement_set_id,requirement_set_revision) WHERE approved;
        CREATE FUNCTION cp6_requirement_set_review_application_v1.validate() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE p jsonb; s cp6_requirement_set_review_fact_v1.streams; f cp6_requirement_set_review_fact_v1.facts; prior cp6_requirement_set_review_application_v1.receipts; BEGIN
          p=convert_from(NEW.payload,'UTF8')::jsonb;
          SELECT * INTO s FROM cp6_requirement_set_review_fact_v1.streams WHERE environment_id=NEW.environment_id AND tenant_id=NEW.tenant_id AND id=NEW.stream_id FOR UPDATE;
          SELECT * INTO f FROM cp6_requirement_set_review_fact_v1.facts WHERE environment_id=s.environment_id AND tenant_id=s.tenant_id AND decision_id=s.decision_id;
          SELECT * INTO prior FROM cp6_requirement_set_review_application_v1.receipts WHERE environment_id=NEW.environment_id AND tenant_id=NEW.tenant_id AND stream_id=NEW.stream_id ORDER BY revision DESC LIMIT 1;
          IF s.id IS NULL OR f.decision_id IS NULL OR (NEW.requirement_set_id,NEW.submission_id,NEW.decision_id) IS DISTINCT FROM (f.requirement_set_id,f.submission_id,f.decision_id)
            OR NEW.revision<>coalesce(prior.revision,0)+1 OR NEW.previous_id IS DISTINCT FROM prior.id OR (prior.id IS NOT NULL AND prior.state<>'BLOCKED')
            OR p->>'schema' IS DISTINCT FROM 'oa.app/2.0' OR p->>'producerOwner' IS DISTINCT FROM 'Main.Plm.RequirementSet.v1'
            OR p->>'action' IS DISTINCT FROM 'PLM_REQUIREMENT_SET_REVIEW' OR p->>'application' IS DISTINCT FROM NEW.state
            OR p->'scope'->>'environmentId' IS DISTINCT FROM NEW.environment_id::text OR p->'scope'->>'tenantId' IS DISTINCT FROM NEW.tenant_id::text
            OR p->>'receiptId' IS DISTINCT FROM NEW.id::text OR p->>'receiptStreamId' IS DISTINCT FROM NEW.stream_id::text
            OR p->>'receiptRevision' IS DISTINCT FROM NEW.revision::text OR p->>'receiptEventId' IS DISTINCT FROM NEW.event_id::text
            OR p->>'previousReceiptId' IS DISTINCT FROM NEW.previous_id::text OR p->>'submissionId' IS DISTINCT FROM f.submission_id::text
            OR p->>'decisionId' IS DISTINCT FROM f.decision_id::text OR p->>'instanceId' IS DISTINCT FROM f.instance_id::text
            OR p->'subject' IS DISTINCT FROM convert_from(f.payload,'UTF8')::jsonb->'subject'
            OR p->'decisionPayloadDigest' IS DISTINCT FROM convert_from(f.payload,'UTF8')::jsonb->'payloadDigest'
            OR p->'subject'->>'subjectId' IS DISTINCT FROM s.application_unit_id
            OR p->>'actionActorId' IS DISTINCT FROM NEW.actor_id::text OR p->>'systemExecutionRef' IS NOT NULL
            OR (NEW.state='APPLIED' AND (p->>'appliedAt' IS NULL OR p->'businessResultRef'->>'owner' IS DISTINCT FROM 'Main.Plm.RequirementSetApproval.v1'
              OR p->'businessResultRef'->>'version' IS DISTINCT FROM '1' OR p->>'businessResultVersion' IS DISTINCT FROM '1'))
            OR (NEW.state<>'APPLIED' AND (p->>'appliedAt' IS NOT NULL OR p->>'businessResultRef' IS NOT NULL OR p->>'businessResultVersion' IS NOT NULL))
            OR p->'auditRef'->>'owner' IS DISTINCT FROM 'Main.Plm.RequirementSetApplicationAudit.v1'
            OR p->'auditRef'->>'id' IS DISTINCT FROM NEW.id::text OR p->'auditRef'->>'version' IS DISTINCT FROM NEW.audit_hash
            OR NEW.payload_hash<>encode(sha256(NEW.payload),'hex') OR NEW.audit_hash<>encode(sha256(convert_to(NEW.audit_json,'UTF8')),'hex') THEN
            RAISE EXCEPTION 'REQUIREMENT_SET_APPLICATION_RECEIPT_INVALID'; END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER requirement_set_application_validate BEFORE INSERT ON cp6_requirement_set_review_application_v1.receipts FOR EACH ROW EXECUTE FUNCTION cp6_requirement_set_review_application_v1.validate();
        DO $$ DECLARE tbl text; BEGIN FOREACH tbl IN ARRAY ARRAY['receipts','outbox','applications'] LOOP
          EXECUTE format('CREATE TRIGGER requirement_set_application_root BEFORE INSERT ON cp6_requirement_set_review_application_v1.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change()',tbl);
          EXECUTE format('CREATE TRIGGER requirement_set_application_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_requirement_set_review_application_v1.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable()',tbl);
        END LOOP; END $$;
        CREATE FUNCTION cp6_requirement_set_review_application_v1.complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE r cp6_requirement_set_review_application_v1.receipts; BEGIN
          IF TG_TABLE_NAME='receipts' THEN r=NEW; ELSE
            SELECT * INTO r FROM cp6_requirement_set_review_application_v1.receipts WHERE environment_id=NEW.environment_id AND tenant_id=NEW.tenant_id AND id=NEW.receipt_id;
          END IF;
          IF r.id IS NULL OR (TG_TABLE_NAME='applications' AND r.state<>'APPLIED') OR NOT EXISTS(SELECT FROM cp6_requirement_set_review_application_v1.outbox o WHERE o.environment_id=r.environment_id AND o.tenant_id=r.tenant_id
            AND o.receipt_id=r.id AND o.event_id=r.event_id AND o.payload=r.payload AND o.payload_hash=r.payload_hash)
            OR NOT EXISTS(SELECT FROM cp6_main_owner_v1.operations c WHERE c.tenant_id=r.tenant_id
              AND c.owner='Main.Plm.RequirementSetApplication.operations.v1' AND c.contract='Main.Plm.RequirementSetApplication.commands.v1'
              AND c.operation_id=r.operation_id AND c.actor_id=r.actor_id AND c.state='COMMITTED'
              AND c.object_ids=ARRAY[r.requirement_set_id] AND convert_from(c.result_bytes,'UTF8')::jsonb->'Receipt'->>'ReceiptId'=r.id::text) THEN
            RAISE EXCEPTION 'REQUIREMENT_SET_APPLICATION_OUTBOX_REQUIRED'; END IF;
          IF (r.state='APPLIED') IS DISTINCT FROM EXISTS(SELECT FROM cp6_requirement_set_review_application_v1.applications a
            JOIN cp6_requirement_set_review_fact_v1.facts f ON f.environment_id=a.environment_id AND f.tenant_id=a.tenant_id AND f.decision_id=a.decision_id
            JOIN cp6_requirement_set_review_v1.submissions o ON o.environment_id=a.environment_id AND o.tenant_id=a.tenant_id AND o.id=a.submission_id
            WHERE a.environment_id=r.environment_id AND a.tenant_id=r.tenant_id AND a.receipt_id=r.id AND a.requirement_set_id=r.requirement_set_id
              AND a.submission_id=r.submission_id AND a.decision_id=r.decision_id AND a.requirement_set_revision=o.requirement_set_revision
              AND a.requirement_set_id=o.requirement_set_id AND a.approved=(convert_from(f.payload,'UTF8')::jsonb->>'decision'='APPROVED')
              AND convert_from(f.payload,'UTF8')::jsonb->>'decision' IN('APPROVED','REJECTED')
              AND convert_from(r.payload,'UTF8')::jsonb->'businessResultRef'->>'id'=a.id::text
              AND convert_from(r.payload,'UTF8')::jsonb->>'outcomeCode'=CASE WHEN a.approved THEN 'REQUIREMENT_SET_APPROVED' ELSE 'REQUIREMENT_SET_REJECTED' END
              AND convert_from(a.body,'UTF8')::jsonb->>'id'=a.id::text
              AND convert_from(a.body,'UTF8')::jsonb->>'submissionId'=a.submission_id::text
              AND convert_from(a.body,'UTF8')::jsonb->>'decisionId'=a.decision_id::text
              AND convert_from(a.body,'UTF8')::jsonb->>'actorId'=r.actor_id::text
              AND (convert_from(a.body,'UTF8')::jsonb->>'approved')::boolean=a.approved
              AND convert_from(a.body,'UTF8')::jsonb->>'appliedAt'=convert_from(r.payload,'UTF8')::jsonb->>'appliedAt'
              AND convert_from(a.body,'UTF8')::jsonb->'requirementSet'=convert_from(o.package_bytes,'UTF8')::jsonb->'requirementSet'
              AND convert_from(a.body,'UTF8')::jsonb->'policy'->>'owner'='Main.Plm.RequirementSetReviewPolicy.v1'
              AND convert_from(a.body,'UTF8')::jsonb->'policy'->>'id'=o.policy_id::text
              AND convert_from(a.body,'UTF8')::jsonb->'policy'->>'version'=convert_from(o.payload,'UTF8')::jsonb->'policy'->>'version'
              AND convert_from(a.body,'UTF8')::jsonb->'policy'->>'digest'=convert_from(o.payload,'UTF8')::jsonb->'policy'->'digest'->>'value') THEN
            RAISE EXCEPTION 'REQUIREMENT_SET_APPLICATION_EFFECT_REQUIRED' USING ERRCODE='23514'; END IF;
          RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER requirement_set_application_complete AFTER INSERT ON cp6_requirement_set_review_application_v1.receipts DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_requirement_set_review_application_v1.complete();
        CREATE CONSTRAINT TRIGGER requirement_set_application_complete AFTER INSERT ON cp6_requirement_set_review_application_v1.outbox DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_requirement_set_review_application_v1.complete();
        CREATE CONSTRAINT TRIGGER requirement_set_application_complete AFTER INSERT ON cp6_requirement_set_review_application_v1.applications DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_requirement_set_review_application_v1.complete();
        CREATE FUNCTION cp6_requirement_set_review_application_v1.ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT cp6_requirement_set_review_fact_v1.ready() AND NOT EXISTS(SELECT FROM (VALUES
            ('applications','requirement_set_application_root','cp6_authentication_v1.before_change()',6),
            ('applications','requirement_set_application_immutable','cp6_scope_authorization_v1.immutable()',58),
            ('applications','requirement_set_application_complete','cp6_requirement_set_review_application_v1.complete()',5),
            ('receipts','requirement_set_application_validate','cp6_requirement_set_review_application_v1.validate()',7),
            ('receipts','requirement_set_application_root','cp6_authentication_v1.before_change()',6),('outbox','requirement_set_application_root','cp6_authentication_v1.before_change()',6),
            ('receipts','requirement_set_application_immutable','cp6_scope_authorization_v1.immutable()',58),('outbox','requirement_set_application_immutable','cp6_scope_authorization_v1.immutable()',58),
            ('receipts','requirement_set_application_complete','cp6_requirement_set_review_application_v1.complete()',5),('outbox','requirement_set_application_complete','cp6_requirement_set_review_application_v1.complete()',5)) a(tbl,name,fn,typ)
            WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid=to_regclass('cp6_requirement_set_review_application_v1.'||a.tbl) AND t.tgname=a.name
              AND t.tgfoid=to_regprocedure(a.fn) AND t.tgtype=a.typ AND t.tgenabled IN('O','A') AND t.tgqual IS NULL
              AND (a.name<>'requirement_set_application_complete' OR t.tgdeferrable AND t.tginitdeferred)));
        $$;
        """);
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Forward recovery only; application receipts and pending outbox remain immutable.");
}
