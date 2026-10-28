using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)),Migration("20261030013500_LocationRuleReviewStateV1")]
public sealed class LocationRuleReviewStateV1:Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        ALTER TABLE cp6_location_rule_v1.proposals DROP CONSTRAINT proposals_state_check;
        ALTER TABLE cp6_location_rule_v1.proposals ADD COLUMN active_submission_id uuid;
        ALTER TABLE cp6_location_rule_v1.proposals ADD CONSTRAINT rule_review_state CHECK(
          state='DRAFT' AND active_submission_id IS NULL OR state='WITHDRAWN' OR
          state IN('SUBMITTED','RETURNED','REJECTED','APPROVED_PENDING') AND active_submission_id IS NOT NULL);
        CREATE OR REPLACE FUNCTION cp6_location_rule_v1.advance() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP='INSERT' THEN
            IF NEW.version<>1 OR NEW.revision<>1 OR NEW.state<>'DRAFT' OR NEW.active_submission_id IS NOT NULL THEN RAISE EXCEPTION 'RULE_FRONTIER_INVALID';END IF;
          ELSIF (to_jsonb(NEW)-ARRAY['version','revision','token','state','operation_id','actor_id','active_submission_id']) IS DISTINCT FROM
              (to_jsonb(OLD)-ARRAY['version','revision','token','state','operation_id','actor_id','active_submission_id'])
            OR NEW.revision<>OLD.revision+1 OR NEW.token=OLD.token OR NEW.operation_id=OLD.operation_id
            OR NOT (
              OLD.state IN('DRAFT','RETURNED') AND NEW.state='DRAFT' AND NEW.version=OLD.version+1 AND NEW.active_submission_id IS NULL
              OR OLD.state IN('DRAFT','RETURNED') AND NEW.state='SUBMITTED' AND NEW.version=OLD.version AND NEW.active_submission_id IS NOT NULL
                AND NEW.active_submission_id IS DISTINCT FROM OLD.active_submission_id
              OR OLD.state='SUBMITTED' AND NEW.state IN('APPROVED_PENDING','RETURNED','REJECTED') AND NEW.version=OLD.version
                AND NEW.active_submission_id=OLD.active_submission_id
              OR OLD.state IN('DRAFT','RETURNED','SUBMITTED','APPROVED_PENDING','REJECTED') AND NEW.state='WITHDRAWN' AND NEW.version=OLD.version
                AND NEW.active_submission_id IS NOT DISTINCT FROM OLD.active_submission_id)
            THEN RAISE EXCEPTION 'RULE_FRONTIER_INVALID';END IF;RETURN NEW;
        END $$;
        CREATE FUNCTION cp6_location_rule_v1.review_provenance() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF NEW.state='SUBMITTED' AND NOT EXISTS(
            SELECT FROM cp6_location_rule_review_v1.submissions s JOIN cp6_main_owner_v1.operations o
              ON o.tenant_id=s.tenant_id AND o.owner=s.journal AND o.operation_id=s.operation_id
            WHERE s.tenant_id=NEW.tenant_id AND s.id=NEW.active_submission_id AND s.location_rule_id=NEW.id AND s.location_rule_revision=NEW.version
              AND s.operation_id=NEW.operation_id AND s.actor_id=NEW.actor_id AND s.xmin=pg_current_xact_id()::text::xid
              AND o.contract='Main.Wms.LocationRuleReview.commands.v1' AND o.state='COMMITTED' AND o.actor_id=NEW.actor_id
              AND o.xmin=pg_current_xact_id()::text::xid AND o.object_ids=ARRAY[NEW.id]
              AND convert_from(o.result_bytes,'UTF8')::jsonb->'Submission'->>'SubmissionId'=s.id::text)
            THEN RAISE EXCEPTION 'RULE_REVIEW_SUBMISSION_REQUIRED';END IF;
          IF NEW.state IN('APPROVED_PENDING','RETURNED','REJECTED') AND NOT EXISTS(
            SELECT FROM cp6_location_rule_review_application_v1.applications a
            JOIN cp6_location_rule_review_application_v1.receipts r ON r.environment_id=a.environment_id AND r.tenant_id=a.tenant_id AND r.id=a.receipt_id
            JOIN cp6_location_rule_review_v1.submissions s ON s.environment_id=a.environment_id AND s.tenant_id=a.tenant_id AND s.id=a.submission_id
            JOIN cp6_location_rule_review_v1.policies p ON p.environment_id=s.environment_id AND p.tenant_id=s.tenant_id AND p.id=s.policy_id
            WHERE a.tenant_id=NEW.tenant_id AND a.submission_id=NEW.active_submission_id AND a.location_rule_id=NEW.id AND a.location_rule_revision=NEW.version
              AND r.operation_id=NEW.operation_id AND r.actor_id=NEW.actor_id AND r.state='APPLIED'
              AND a.xmin=pg_current_xact_id()::text::xid AND r.xmin=pg_current_xact_id()::text::xid
              AND NEW.state=CASE WHEN a.approved THEN 'APPROVED_PENDING' ELSE p.body::jsonb#>>'{body,rejectionDisposition}' END)
            THEN RAISE EXCEPTION 'RULE_REVIEW_DECISION_REQUIRED';END IF;
          RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER rule_review_provenance AFTER UPDATE ON cp6_location_rule_v1.proposals DEFERRABLE INITIALLY DEFERRED
          FOR EACH ROW EXECUTE FUNCTION cp6_location_rule_v1.review_provenance();
        ALTER FUNCTION cp6_location_rule_v1.ready() RENAME TO proposal_ready_before_review;
        CREATE FUNCTION cp6_location_rule_v1.ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT cp6_location_rule_v1.proposal_ready_before_review() AND EXISTS(SELECT FROM pg_trigger t
            WHERE t.tgrelid='cp6_location_rule_v1.proposals'::regclass AND t.tgname='rule_review_provenance'
              AND t.tgfoid='cp6_location_rule_v1.review_provenance()'::regprocedure AND t.tgtype=17 AND t.tgenabled IN('O','A')
              AND t.tgqual IS NULL AND t.tgnargs=0 AND t.tgdeferrable AND t.tginitdeferred);
        $$;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_location_rule_v1 FROM PUBLIC;
        """);
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Rule approval evidence requires forward recovery.");
}
