using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)), Migration("20261030013600_LocationRuleDecisionDeliveryV1")]
public sealed class LocationRuleDecisionDeliveryV1 : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE OR REPLACE FUNCTION cp6_approval_delivery_v2.validate() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE r jsonb; BEGIN
          r=NEW.route_json::jsonb;
          IF (r->>'environment') IS DISTINCT FROM NEW.environment_id::text OR (r->>'tenant') IS DISTINCT FROM NEW.tenant_id::text
            OR coalesce(r->>'consumerOwner','') NOT IN ('Main.Wms.ToolDisposition.v1','Main.Plm.ControlledDocument.v1','Main.Plm.RequirementSet.v1','Main.Plm.EngineeringIteration.v1','Main.Wms.LocationRuleProposal.v1')
            OR NOT EXISTS(SELECT FROM cp6_approval_decision_v2.outbox o WHERE o.environment_id=NEW.environment_id
              AND o.tenant_id=NEW.tenant_id AND o.id=NEW.outbox_id AND o.consumer_owner=r->>'consumerOwner')
            OR NEW.route_hash<>encode(sha256(convert_to(NEW.route_json,'UTF8')),'hex') THEN
            RAISE EXCEPTION 'DECISION_DELIVERY_ROUTE_INVALID'; END IF;
          IF TG_OP='INSERT' THEN
            IF NEW.epoch<>1 OR NEW.state<>'OUTCOME_UNKNOWN' THEN RAISE EXCEPTION 'DECISION_DELIVERY_CLAIM_REQUIRED'; END IF;
          ELSE
            IF (NEW.environment_id,NEW.tenant_id,NEW.outbox_id) IS DISTINCT FROM (OLD.environment_id,OLD.tenant_id,OLD.outbox_id)
              OR OLD.state IN('RECEIVED','CONFLICT') THEN RAISE EXCEPTION 'DECISION_DELIVERY_ORIGINAL_IMMUTABLE'; END IF;
            IF (NEW.route_json::jsonb-'version') IS DISTINCT FROM (OLD.route_json::jsonb-'version') THEN
              RAISE EXCEPTION 'DECISION_DELIVERY_ROUTE_IDENTITY_CHANGED'; END IF;
            IF NEW.epoch<>OLD.epoch THEN
              IF NEW.epoch<>OLD.epoch+1 OR NEW.state<>'OUTCOME_UNKNOWN' OR OLD.lease_until>clock_timestamp() OR NEW.lease_token=OLD.lease_token THEN
                RAISE EXCEPTION 'DECISION_DELIVERY_LEASE_INVALID'; END IF;
            ELSIF NEW.lease_token<>OLD.lease_token OR NEW.route_json<>OLD.route_json OR NEW.registration_revision<>OLD.registration_revision OR NEW.subject_revision<>OLD.subject_revision THEN
              RAISE EXCEPTION 'DECISION_DELIVERY_LEASE_INVALID';
            END IF;
          END IF; RETURN NEW;
        END $$;
        """);
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Forward recovery only; preserve exact decision delivery identity.");
}
