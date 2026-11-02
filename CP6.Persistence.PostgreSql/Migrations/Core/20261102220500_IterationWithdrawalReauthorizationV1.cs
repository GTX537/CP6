using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)),Migration("20261102220500_IterationWithdrawalReauthorizationV1")]
public sealed class IterationWithdrawalReauthorizationV1:Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        ALTER TABLE cp6_iteration_review_control_v1.withdrawals DROP CONSTRAINT withdrawals_environment_id_tenant_id_submission_id_key;
        ALTER TABLE cp6_iteration_review_control_v1.withdrawals ADD COLUMN previous_command_id uuid;
        ALTER TABLE cp6_iteration_review_control_v1.withdrawals ADD CONSTRAINT withdrawal_previous FOREIGN KEY(environment_id,tenant_id,previous_command_id)
          REFERENCES cp6_iteration_review_control_v1.withdrawals(environment_id,tenant_id,command_id);
        CREATE UNIQUE INDEX withdrawal_first_fact ON cp6_iteration_review_control_v1.withdrawals(environment_id,tenant_id,submission_id) WHERE previous_command_id IS NULL;
        CREATE UNIQUE INDEX withdrawal_explicit_successor ON cp6_iteration_review_control_v1.withdrawals(environment_id,tenant_id,previous_command_id) WHERE previous_command_id IS NOT NULL;
        CREATE OR REPLACE FUNCTION cp6_iteration_review_control_v1.head_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP IN('DELETE','TRUNCATE') THEN RAISE EXCEPTION 'ITERATION_REVIEW_CONTROL_PERMANENT';END IF;
          IF TG_OP='UPDATE' AND ((OLD.environment_id,OLD.tenant_id,OLD.iteration_id) IS DISTINCT FROM (NEW.environment_id,NEW.tenant_id,NEW.iteration_id)
            OR (OLD.state<>'IN_REVIEW' AND NEW.state<>'IN_REVIEW' AND NOT(OLD.state='WITHDRAWN' AND NEW.state='WITHDRAWN' AND OLD.submission_id=NEW.submission_id))
            OR OLD.state='IN_REVIEW' AND NEW.state='IN_REVIEW' OR OLD.state='IN_REVIEW' AND OLD.submission_id<>NEW.submission_id)
            THEN RAISE EXCEPTION 'ITERATION_REVIEW_CONTROL_CHANGED';END IF;
          -- A repeated withdrawn state is a new explicit Owner authorization transition.
          -- The deferred control/fact guards require its new business version, original command,
          -- exact predecessor token, complete actor/grant and immutable outbox in this transaction.
          NEW.row_version=int8send(nextval('cp6_iteration_review_control_v1.version'));RETURN NEW;
        END $$;
        CREATE OR REPLACE FUNCTION cp6_iteration_review_control_v1.withdraw_complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
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
            OR f#>>'{actor,actualHandlerId}' IS DISTINCT FROM w.actor_id::text
            OR NOT ((f#>>'{actor,grantId}' IS NULL AND f#>>'{actor,grantRevision}' IS NULL AND f#>>'{actor,onBehalfOfId}' IS NULL
                AND f#>>'{actor,requesterId}' IS NOT DISTINCT FROM w.actor_id::text)
              OR EXISTS(SELECT FROM cp6_iteration_review_control_v1.business_grants g
                WHERE g.environment_id=w.environment_id AND g.tenant_id=w.tenant_id AND g.iteration_id=w.iteration_id AND g.submission_id=w.submission_id
                  AND g.id=(f#>>'{actor,grantId}')::uuid AND g.revision::text=f#>>'{actor,grantRevision}'
                  AND g.grantor_id::text=f#>>'{actor,requesterId}' AND g.grantor_id::text=f#>>'{actor,onBehalfOfId}' AND g.grantor_id<>w.actor_id
                  AND g.body::jsonb->>'delegateId'=w.actor_id::text AND g.body::jsonb->>'enabled'='true' AND g.body::jsonb->'actions' ? 'review-withdraw'
                  AND (g.body::jsonb->>'fromUtc')::timestamptz<=clock_timestamp() AND clock_timestamp()<(g.body::jsonb->>'toUtc')::timestamptz
                  AND NOT EXISTS(SELECT FROM cp6_iteration_review_control_v1.business_grants newer WHERE newer.environment_id=g.environment_id
                    AND newer.tenant_id=g.tenant_id AND newer.id=g.id AND newer.revision>g.revision)))
            OR (w.previous_command_id IS NOT NULL AND NOT EXISTS(SELECT FROM cp6_iteration_review_control_v1.withdrawals prior
              JOIN cp6_approval_withdraw_v2.commands command ON command.environment_id=prior.environment_id AND command.tenant_id=prior.tenant_id
                AND command.owner='Main.Plm.EngineeringIteration.v1' AND command.id=prior.command_id
              WHERE prior.environment_id=w.environment_id AND prior.tenant_id=w.tenant_id AND prior.command_id=w.previous_command_id
                AND prior.submission_id=w.submission_id AND prior.iteration_id=w.iteration_id AND prior.after_version=w.before_version
                AND command.state='BLOCKED'))
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
        """);
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Withdrawal evidence is forward-only.");
}
