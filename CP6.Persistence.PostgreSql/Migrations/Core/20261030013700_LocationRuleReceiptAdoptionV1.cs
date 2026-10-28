using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)),Migration("20261030013700_LocationRuleReceiptAdoptionV1")]
public sealed class LocationRuleReceiptAdoptionV1:Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        ALTER TABLE cp6_approval_receipt_v2.registrations DROP CONSTRAINT registrations_owner_check;
        ALTER TABLE cp6_approval_receipt_v2.registrations ADD CONSTRAINT registrations_owner_check CHECK(owner IN('Main.Wms.ToolDisposition.v1','Main.Plm.ControlledDocument.v1','Main.Plm.RequirementSet.v1','Main.Plm.EngineeringIteration.v1','Main.Wms.LocationRuleProposal.v1'));
        ALTER TABLE cp6_approval_receipt_v2.subject_grants DROP CONSTRAINT subject_grants_owner_check;
        ALTER TABLE cp6_approval_receipt_v2.subject_grants ADD CONSTRAINT subject_grants_owner_check CHECK(owner IN('Main.Wms.ToolDisposition.v1','Main.Plm.ControlledDocument.v1','Main.Plm.RequirementSet.v1','Main.Plm.EngineeringIteration.v1','Main.Wms.LocationRuleProposal.v1'));
        CREATE OR REPLACE FUNCTION cp6_approval_receipt_v2.complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
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
            IF NOT EXISTS(SELECT FROM (
                SELECT environment_id,tenant_id,id,event_id,stream_id,revision,previous_id,submission_id,decision_id,payload,payload_hash
                  FROM cp6_tool_application_v2.receipts WHERE s.owner='Main.Wms.ToolDisposition.v1'
                UNION ALL
                SELECT environment_id,tenant_id,id,event_id,stream_id,revision,previous_id,submission_id,decision_id,payload,payload_hash
                  FROM cp6_document_review_application_v1.receipts WHERE s.owner='Main.Plm.ControlledDocument.v1'
                UNION ALL
                SELECT environment_id,tenant_id,id,event_id,stream_id,revision,previous_id,submission_id,decision_id,payload,payload_hash
                  FROM cp6_requirement_set_review_application_v1.receipts WHERE s.owner='Main.Plm.RequirementSet.v1'
                UNION ALL
                SELECT environment_id,tenant_id,id,event_id,stream_id,revision,previous_id,submission_id,decision_id,payload,payload_hash
                  FROM cp6_iteration_review_application_v1.receipts WHERE s.owner='Main.Plm.EngineeringIteration.v1'
                UNION ALL
                SELECT environment_id,tenant_id,id,event_id,stream_id,revision,previous_id,submission_id,decision_id,payload,payload_hash
                  FROM cp6_location_rule_review_application_v1.receipts WHERE s.owner='Main.Wms.LocationRuleProposal.v1'
              ) r WHERE r.environment_id=i.environment_id AND r.tenant_id=i.tenant_id
              AND r.id=i.receipt_id AND r.event_id=i.event_id AND r.stream_id=i.stream_id AND r.revision=i.revision AND r.previous_id IS NOT DISTINCT FROM i.previous_id
              AND r.submission_id=i.submission_id AND r.decision_id=s.decision_id AND r.payload=i.payload AND r.payload_hash=i.payload_hash)
              OR p->>'instanceId' IS DISTINCT FROM s.instance_id::text OR p->>'producerOwner' IS DISTINCT FROM s.owner
              OR p->'subject'->>'businessRevision' IS DISTINCT FROM s.subject_revision OR (CASE WHEN s.owner='Main.Plm.EngineeringIteration.v1' THEN 'PLM_ITER_REVIEW_DECISION' ELSE p->'subject'->>'subjectId' END) IS DISTINCT FROM s.application_unit_id
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
        """);
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Forward recovery only; native receipt provenance is permanent.");
}
