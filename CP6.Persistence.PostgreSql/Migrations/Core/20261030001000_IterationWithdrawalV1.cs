using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)),Migration("20261030001000_IterationWithdrawalV1")]
public sealed class IterationWithdrawalV1:Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        CREATE TABLE cp6_iteration_review_control_v1.withdrawals(
          environment_id uuid NOT NULL,tenant_id uuid NOT NULL,iteration_id uuid NOT NULL,submission_id uuid NOT NULL,id uuid NOT NULL,
          operation_id uuid NOT NULL,command_id uuid NOT NULL,outbox_id uuid NOT NULL,actor_id uuid NOT NULL,
          before_version bytea NOT NULL CHECK(octet_length(before_version)=8),after_version bytea NOT NULL CHECK(octet_length(after_version)=8 AND after_version<>before_version),
          iteration_revision bigint NOT NULL,payload bytea NOT NULL,digest text NOT NULL CHECK(digest=encode(sha256(payload),'hex')),authority_json text NOT NULL,
          journal text NOT NULL DEFAULT 'Main.Plm.IterationWithdrawal.operations.v1' CHECK(journal='Main.Plm.IterationWithdrawal.operations.v1'),
          PRIMARY KEY(environment_id,tenant_id,id),UNIQUE(environment_id,tenant_id,operation_id),UNIQUE(environment_id,tenant_id,command_id),
          UNIQUE(environment_id,tenant_id,submission_id),UNIQUE(environment_id,tenant_id,outbox_id),UNIQUE(environment_id,tenant_id,command_id,outbox_id),
          FOREIGN KEY(environment_id,tenant_id,submission_id) REFERENCES cp6_iteration_review_v1.submissions(environment_id,tenant_id,id),
          FOREIGN KEY(tenant_id,iteration_id,iteration_revision) REFERENCES cp6_engineering_iteration_v1.versions,
          FOREIGN KEY(tenant_id,journal,operation_id) REFERENCES cp6_main_owner_v1.operations(tenant_id,owner,operation_id) DEFERRABLE INITIALLY DEFERRED);
        CREATE TABLE cp6_iteration_review_control_v1.withdraw_outbox(
          environment_id uuid NOT NULL,tenant_id uuid NOT NULL,id uuid NOT NULL,command_id uuid NOT NULL,submission_id uuid NOT NULL,
          payload bytea NOT NULL,digest text NOT NULL CHECK(digest=encode(sha256(payload),'hex')),
          PRIMARY KEY(environment_id,tenant_id,id),UNIQUE(environment_id,tenant_id,command_id),
          FOREIGN KEY(environment_id,tenant_id,command_id,id) REFERENCES cp6_iteration_review_control_v1.withdrawals(environment_id,tenant_id,command_id,outbox_id) DEFERRABLE INITIALLY DEFERRED);
        ALTER TABLE cp6_iteration_review_control_v1.withdrawals ADD CONSTRAINT withdrawal_outbox_required
          FOREIGN KEY(environment_id,tenant_id,outbox_id) REFERENCES cp6_iteration_review_control_v1.withdraw_outbox(environment_id,tenant_id,id) DEFERRABLE INITIALLY DEFERRED;
        CREATE FUNCTION cp6_iteration_review_control_v1.withdraw_complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE w cp6_iteration_review_control_v1.withdrawals;p jsonb;f jsonb;v jsonb;s jsonb;BEGIN
          SELECT * INTO w FROM cp6_iteration_review_control_v1.withdrawals WHERE environment_id=NEW.environment_id AND tenant_id=NEW.tenant_id AND command_id=NEW.command_id;
          p=convert_from(w.payload,'UTF8')::jsonb;f=p->'fact';
          SELECT convert_from(body_bytes,'UTF8')::jsonb INTO v FROM cp6_engineering_iteration_v1.versions WHERE tenant_id=w.tenant_id AND object_id=w.iteration_id AND revision=w.iteration_revision;
          SELECT convert_from(payload,'UTF8')::jsonb INTO s FROM cp6_iteration_review_v1.submissions WHERE environment_id=w.environment_id AND tenant_id=w.tenant_id AND id=w.submission_id;
          IF w.id IS NULL OR f->>'schema' IS DISTINCT FROM 'oa.app.withdrawal-fact/1.0' OR p->>'schema' IS DISTINCT FROM 'oa.app.withdraw/2.0'
            OR p->>'commandId' IS DISTINCT FROM w.command_id::text OR p->>'ownerOutboxId' IS DISTINCT FROM w.outbox_id::text
            OR p->>'submissionId' IS DISTINCT FROM w.submission_id::text OR f->>'submissionId' IS DISTINCT FROM w.submission_id::text
            OR p->'scope' IS DISTINCT FROM s->'scope' OR f->'scope' IS DISTINCT FROM s->'scope'
            OR p->>'producerOwner' IS DISTINCT FROM 'Main.Plm.EngineeringIteration.v1' OR f->>'owner' IS DISTINCT FROM p->>'producerOwner'
            OR f#>>'{ownerWithdrawalRef,owner}' IS DISTINCT FROM 'Main.Plm.IterationWithdrawal.v1' OR f#>>'{ownerWithdrawalRef,id}' IS DISTINCT FROM w.id::text
            OR f#>>'{ownerWithdrawalRef,version}' IS DISTINCT FROM '1' OR f->>'ownerCommandId' IS DISTINCT FROM w.operation_id::text
            OR f->'subject' IS DISTINCT FROM s->'subject' OR f->'policyRef' IS DISTINCT FROM s#>'{policy,ref}'
            OR f#>>'{actor,actualHandlerId}' IS DISTINCT FROM w.actor_id::text OR f#>>'{actor,requesterId}' IS DISTINCT FROM w.actor_id::text
            OR f#>>'{actor,withdrawCommandId}' IS DISTINCT FROM w.operation_id::text OR f#>'{actor,ownerWithdrawalRef}' IS DISTINCT FROM f->'ownerWithdrawalRef'
            OR f#>>'{actor,occurredAt}' IS DISTINCT FROM f->>'withdrawnAt' OR f#>>'{actor,kind}' IS DISTINCT FROM 'SUBMITTER_WITHDRAWAL'
            OR decode(f->>'businessBeforeRowVersion','base64') IS DISTINCT FROM w.before_version OR decode(f->>'businessAfterRowVersion','base64') IS DISTINCT FROM w.after_version
            OR v->>'operationId' IS DISTINCT FROM w.operation_id::text OR v->>'actorId' IS DISTINCT FROM w.actor_id::text
            OR v#>>'{body,iteration,stage}' IS DISTINCT FROM 'IN_DESIGN' OR v#>>'{body,iteration,currentReview}' IS NOT NULL
            OR NOT EXISTS(SELECT FROM cp6_main_owner_v1.operations o WHERE o.tenant_id=w.tenant_id AND o.owner=w.journal AND o.operation_id=w.operation_id
              AND o.actor_id=w.actor_id AND o.contract='Main.Plm.IterationWithdrawal.commands.v1' AND o.state='COMMITTED' AND o.object_ids=ARRAY[w.iteration_id]
              AND convert_from(o.result_bytes,'UTF8')::jsonb->>'State'='WITHDRAWN' AND convert_from(o.result_bytes,'UTF8')::jsonb->>'TransportCommandId'=w.command_id::text)
            OR NOT EXISTS(SELECT FROM cp6_iteration_review_control_v1.withdraw_outbox o WHERE o.environment_id=w.environment_id AND o.tenant_id=w.tenant_id
              AND o.id=w.outbox_id AND o.command_id=w.command_id AND o.submission_id=w.submission_id AND o.payload=w.payload AND o.digest=w.digest)
            THEN RAISE EXCEPTION 'ITERATION_WITHDRAWAL_ATOMIC_FACT_REQUIRED';END IF;RETURN NULL;
        END $$;
        DO $$ DECLARE tbl text;BEGIN FOREACH tbl IN ARRAY ARRAY['withdrawals','withdraw_outbox'] LOOP
          EXECUTE format('CREATE TRIGGER iteration_withdraw_root BEFORE INSERT ON cp6_iteration_review_control_v1.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change()',tbl);
          EXECUTE format('CREATE TRIGGER iteration_withdraw_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_iteration_review_control_v1.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable()',tbl);
          EXECUTE format('CREATE CONSTRAINT TRIGGER iteration_withdraw_complete AFTER INSERT ON cp6_iteration_review_control_v1.%I DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_iteration_review_control_v1.withdraw_complete()',tbl);
        END LOOP;END $$;
        CREATE FUNCTION cp6_iteration_review_control_v1.control_complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE h cp6_iteration_review_control_v1.heads;v jsonb;s cp6_iteration_review_v1.submissions;BEGIN
          SELECT * INTO h FROM cp6_iteration_review_control_v1.heads WHERE environment_id=NEW.environment_id AND tenant_id=NEW.tenant_id AND iteration_id=NEW.iteration_id;
          SELECT convert_from(vs.body_bytes,'UTF8')::jsonb INTO v FROM cp6_engineering_iteration_v1.heads hs JOIN cp6_engineering_iteration_v1.versions vs
            ON (vs.tenant_id,vs.object_id,vs.revision)=(hs.tenant_id,hs.object_id,hs.revision) WHERE hs.tenant_id=h.tenant_id AND hs.object_id=h.iteration_id;
          SELECT * INTO s FROM cp6_iteration_review_v1.submissions WHERE environment_id=h.environment_id AND tenant_id=h.tenant_id AND id=h.submission_id;
          IF s.id IS NULL OR s.iteration_id<>h.iteration_id THEN RAISE EXCEPTION 'ITERATION_CONTROL_SUBMISSION_REQUIRED';END IF;
          IF h.state='IN_REVIEW' THEN
            IF v#>>'{body,iteration,stage}' IS DISTINCT FROM 'IN_REVIEW' OR v#>>'{body,iteration,currentReview,submissionId}' IS DISTINCT FROM h.submission_id::text
              OR v#>>'{body,iteration,currentReview,roundId}' IS DISTINCT FROM s.round_id::text OR v#>>'{body,iteration,currentReview,bindingId}' IS DISTINCT FROM s.binding_id::text
              THEN RAISE EXCEPTION 'ITERATION_CONTROL_STAGE_REQUIRED';END IF;
          ELSIF h.state='APPLIED' THEN
            IF NOT EXISTS(SELECT FROM cp6_iteration_review_application_v1.applications a WHERE a.environment_id=h.environment_id AND a.tenant_id=h.tenant_id
              AND a.submission_id=h.submission_id AND convert_from(a.body,'UTF8')::jsonb#>>'{appliedIteration,version}'=v->>'version'
              AND convert_from(a.body,'UTF8')::jsonb->>'businessStage'=v#>>'{body,iteration,stage}') THEN RAISE EXCEPTION 'ITERATION_CONTROL_APPLICATION_REQUIRED';END IF;
          ELSE
            IF v#>>'{body,iteration,stage}' IS DISTINCT FROM 'IN_DESIGN' OR NOT EXISTS(SELECT FROM cp6_iteration_review_control_v1.withdrawals w
              WHERE w.environment_id=h.environment_id AND w.tenant_id=h.tenant_id AND w.submission_id=h.submission_id AND w.after_version=h.row_version
                AND w.iteration_revision::text=v->>'version') THEN RAISE EXCEPTION 'ITERATION_CONTROL_WITHDRAWAL_REQUIRED';END IF;
          END IF;RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER iteration_control_complete AFTER INSERT OR UPDATE ON cp6_iteration_review_control_v1.heads DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_iteration_review_control_v1.control_complete();
        CREATE FUNCTION cp6_iteration_review_control_v1.application_effect() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE b jsonb=convert_from(NEW.body,'UTF8')::jsonb;v jsonb;s cp6_iteration_review_v1.submissions;p jsonb;receipt cp6_iteration_review_application_v1.receipts;stage text;BEGIN
          SELECT * INTO s FROM cp6_iteration_review_v1.submissions WHERE environment_id=NEW.environment_id AND tenant_id=NEW.tenant_id AND id=NEW.submission_id;
          SELECT body::jsonb INTO p FROM cp6_iteration_review_v1.policies WHERE environment_id=s.environment_id AND tenant_id=s.tenant_id AND id=s.policy_id;
          SELECT * INTO receipt FROM cp6_iteration_review_application_v1.receipts WHERE environment_id=NEW.environment_id AND tenant_id=NEW.tenant_id AND id=NEW.receipt_id;
          stage=CASE WHEN NEW.approved THEN 'REVIEW_APPROVED' WHEN p#>>'{body,rejectDisposition}'='RETURN' THEN 'IN_DESIGN' WHEN p#>>'{body,rejectDisposition}'='REJECT' THEN 'AWAITING_EVALUATION' END;
          SELECT convert_from(body_bytes,'UTF8')::jsonb INTO v FROM cp6_engineering_iteration_v1.versions WHERE tenant_id=NEW.tenant_id AND object_id=NEW.iteration_id
            AND revision::text=b#>>'{appliedIteration,version}' AND body_digest=b#>>'{appliedIteration,digest}';
          IF stage IS NULL OR b->>'businessStage' IS DISTINCT FROM stage OR v IS NULL OR v#>>'{body,iteration,stage}' IS DISTINCT FROM stage
            OR v->>'operationId' IS DISTINCT FROM receipt.operation_id::text OR v->>'actorId' IS DISTINCT FROM receipt.actor_id::text
            OR b#>>'{appliedIteration,owner}' IS DISTINCT FROM 'Main.Plm.EngineeringIteration.v1' OR b#>>'{appliedIteration,id}' IS DISTINCT FROM NEW.iteration_id::text
            OR convert_from(receipt.payload,'UTF8')::jsonb->>'currentSubjectRevision' IS DISTINCT FROM b#>>'{appliedIteration,version}'
            OR NOT EXISTS(SELECT FROM cp6_iteration_review_control_v1.heads h WHERE h.environment_id=NEW.environment_id AND h.tenant_id=NEW.tenant_id AND h.iteration_id=NEW.iteration_id AND h.submission_id=s.id AND h.state='APPLIED')
            OR NEW.approved AND EXISTS(SELECT FROM (SELECT DISTINCT ON(id) body FROM cp6_iteration_review_control_v1.issues WHERE environment_id=NEW.environment_id AND tenant_id=NEW.tenant_id AND iteration_id=NEW.iteration_id ORDER BY id,revision DESC) x
              WHERE convert_from(x.body,'UTF8')::jsonb->>'severity'='BLOCKING' AND convert_from(x.body,'UTF8')::jsonb->>'state'<>'VERIFIED_CLOSED')
            THEN RAISE EXCEPTION 'ITERATION_APPLICATION_EFFECT_REQUIRED';END IF;RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER iteration_application_effect AFTER INSERT ON cp6_iteration_review_application_v1.applications DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_iteration_review_control_v1.application_effect();
        CREATE FUNCTION cp6_iteration_review_control_v1.effects_ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT cp6_iteration_review_control_v1.ready() AND cp6_approval_withdraw_v2.ready()
            AND NOT EXISTS(SELECT FROM (VALUES('withdrawals'),('withdraw_outbox')) a(tbl) CROSS JOIN (VALUES
              ('iteration_withdraw_root','cp6_authentication_v1.before_change()',6,false),('iteration_withdraw_immutable','cp6_scope_authorization_v1.immutable()',58,false),
              ('iteration_withdraw_complete','cp6_iteration_review_control_v1.withdraw_complete()',5,true)) b(name,fn,typ,deferred)
              WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid=to_regclass('cp6_iteration_review_control_v1.'||a.tbl) AND t.tgname=b.name AND t.tgfoid=to_regprocedure(b.fn)
                AND t.tgtype=b.typ AND t.tgenabled IN('O','A') AND t.tgqual IS NULL AND t.tgdeferrable=b.deferred AND t.tginitdeferred=b.deferred))
            AND EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid='cp6_iteration_review_control_v1.heads'::regclass AND t.tgname='iteration_control_complete' AND t.tgfoid='cp6_iteration_review_control_v1.control_complete()'::regprocedure
              AND t.tgtype=21 AND t.tgenabled IN('O','A') AND t.tgqual IS NULL AND t.tgdeferrable AND t.tginitdeferred)
            AND EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid='cp6_iteration_review_application_v1.applications'::regclass AND t.tgname='iteration_application_effect' AND t.tgfoid='cp6_iteration_review_control_v1.application_effect()'::regprocedure
              AND t.tgtype=5 AND t.tgenabled IN('O','A') AND t.tgqual IS NULL AND t.tgdeferrable AND t.tginitdeferred);
        $$;
        """);
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Forward recovery preserves withdrawal facts, application and audit.");
}
