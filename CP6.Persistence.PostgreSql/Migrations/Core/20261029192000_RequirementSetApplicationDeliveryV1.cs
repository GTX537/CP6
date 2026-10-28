using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261029192000_RequirementSetApplicationDeliveryV1")]
public sealed class RequirementSetApplicationDeliveryV1 : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE SCHEMA cp6_requirement_set_application_delivery_v1;
        CREATE TABLE cp6_requirement_set_application_delivery_v1.delivery(
          environment_id uuid NOT NULL,tenant_id uuid NOT NULL,outbox_id uuid NOT NULL,
          state text NOT NULL CHECK(state IN('OUTCOME_UNKNOWN','PENDING_GAP','BLOCKED','CONFLICT','RECORDED')),
          epoch bigint NOT NULL CHECK(epoch>0),lease_token uuid NOT NULL,lease_until timestamptz NOT NULL,next_attempt timestamptz NOT NULL,
          route_hash text NOT NULL CHECK(route_hash~'^[0-9a-f]{64}$'),route_json text NOT NULL,
          registration_revision uuid NOT NULL,subject_revision uuid NOT NULL,error_code text,ack_json text,
          PRIMARY KEY(environment_id,tenant_id,outbox_id),
          FOREIGN KEY(environment_id,tenant_id,outbox_id) REFERENCES cp6_requirement_set_review_application_v1.outbox(environment_id,tenant_id,event_id),
          CHECK(state NOT IN('RECORDED','PENDING_GAP') OR ack_json IS NOT NULL),
          CHECK(state IN('RECORDED','PENDING_GAP','CONFLICT') OR ack_json IS NULL));
        CREATE TABLE cp6_requirement_set_application_delivery_v1.attempts(
          environment_id uuid NOT NULL,tenant_id uuid NOT NULL,outbox_id uuid NOT NULL,epoch bigint NOT NULL,lease_token uuid NOT NULL,
          route_json text NOT NULL,registration_revision uuid NOT NULL,subject_revision uuid NOT NULL,authority_json text NOT NULL,started_at timestamptz NOT NULL,
          PRIMARY KEY(environment_id,tenant_id,outbox_id,epoch),UNIQUE(environment_id,tenant_id,outbox_id,epoch,lease_token),
          FOREIGN KEY(environment_id,tenant_id,outbox_id) REFERENCES cp6_requirement_set_application_delivery_v1.delivery);
        CREATE TABLE cp6_requirement_set_application_delivery_v1.results(
          environment_id uuid NOT NULL,tenant_id uuid NOT NULL,outbox_id uuid NOT NULL,epoch bigint NOT NULL,lease_token uuid NOT NULL,
          state text NOT NULL CHECK(state IN('OUTCOME_UNKNOWN','PENDING_GAP','BLOCKED','CONFLICT','RECORDED')),
          error_code text,ack_json text,recorded_at timestamptz NOT NULL,
          PRIMARY KEY(environment_id,tenant_id,outbox_id,epoch),
          FOREIGN KEY(environment_id,tenant_id,outbox_id,epoch,lease_token) REFERENCES cp6_requirement_set_application_delivery_v1.attempts(environment_id,tenant_id,outbox_id,epoch,lease_token),
          CHECK(state NOT IN('RECORDED','PENDING_GAP') OR ack_json IS NOT NULL),
          CHECK(state IN('RECORDED','PENDING_GAP','CONFLICT') OR ack_json IS NULL));
        ALTER TABLE cp6_requirement_set_application_delivery_v1.delivery ADD CONSTRAINT current_attempt
          FOREIGN KEY(environment_id,tenant_id,outbox_id,epoch,lease_token) REFERENCES cp6_requirement_set_application_delivery_v1.attempts(environment_id,tenant_id,outbox_id,epoch,lease_token)
          DEFERRABLE INITIALLY DEFERRED;
        CREATE INDEX application_delivery_due ON cp6_requirement_set_application_delivery_v1.delivery(environment_id,tenant_id,next_attempt)
          WHERE state IN('OUTCOME_UNKNOWN','PENDING_GAP','BLOCKED');
        CREATE FUNCTION cp6_requirement_set_application_delivery_v1.validate() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE r jsonb; BEGIN
          r=NEW.route_json::jsonb;
          IF (r->>'environment') IS DISTINCT FROM NEW.environment_id::text OR (r->>'tenant') IS DISTINCT FROM NEW.tenant_id::text
            OR NEW.route_hash<>encode(sha256(convert_to(NEW.route_json,'UTF8')),'hex') THEN
            RAISE EXCEPTION 'APPLICATION_DELIVERY_ROUTE_INVALID'; END IF;
          IF TG_OP='INSERT' THEN
            IF NEW.epoch<>1 OR NEW.state<>'OUTCOME_UNKNOWN' THEN RAISE EXCEPTION 'APPLICATION_DELIVERY_CLAIM_REQUIRED'; END IF;
          ELSE
            IF (NEW.environment_id,NEW.tenant_id,NEW.outbox_id) IS DISTINCT FROM (OLD.environment_id,OLD.tenant_id,OLD.outbox_id)
              OR OLD.state IN('RECORDED','CONFLICT') THEN RAISE EXCEPTION 'APPLICATION_DELIVERY_ORIGINAL_IMMUTABLE'; END IF;
            IF (NEW.route_json::jsonb-'version') IS DISTINCT FROM (OLD.route_json::jsonb-'version') THEN
              RAISE EXCEPTION 'APPLICATION_DELIVERY_ROUTE_IDENTITY_CHANGED'; END IF;
            IF NEW.epoch<>OLD.epoch THEN
              IF NEW.epoch<>OLD.epoch+1 OR NEW.state<>'OUTCOME_UNKNOWN' OR OLD.lease_until>clock_timestamp() OR NEW.lease_token=OLD.lease_token THEN
                RAISE EXCEPTION 'APPLICATION_DELIVERY_LEASE_INVALID'; END IF;
            ELSIF NEW.lease_token<>OLD.lease_token OR NEW.route_json<>OLD.route_json OR NEW.registration_revision<>OLD.registration_revision OR NEW.subject_revision<>OLD.subject_revision THEN
              RAISE EXCEPTION 'APPLICATION_DELIVERY_LEASE_INVALID';
            END IF;
          END IF; RETURN NEW;
        END $$;
        CREATE TRIGGER application_delivery_validate BEFORE INSERT OR UPDATE ON cp6_requirement_set_application_delivery_v1.delivery FOR EACH ROW EXECUTE FUNCTION cp6_requirement_set_application_delivery_v1.validate();
        CREATE TRIGGER application_delivery_no_delete BEFORE DELETE OR TRUNCATE ON cp6_requirement_set_application_delivery_v1.delivery FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER application_delivery_root BEFORE INSERT OR UPDATE ON cp6_requirement_set_application_delivery_v1.delivery FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        DO $$ DECLARE tbl text; BEGIN FOREACH tbl IN ARRAY ARRAY['attempts','results'] LOOP
          EXECUTE format('CREATE TRIGGER application_delivery_root BEFORE INSERT ON cp6_requirement_set_application_delivery_v1.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change()',tbl);
          EXECUTE format('CREATE TRIGGER application_delivery_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_requirement_set_application_delivery_v1.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable()',tbl);
        END LOOP; END $$;
        CREATE FUNCTION cp6_requirement_set_application_delivery_v1.complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE d cp6_requirement_set_application_delivery_v1.delivery; BEGIN
          SELECT * INTO d FROM cp6_requirement_set_application_delivery_v1.delivery WHERE environment_id=NEW.environment_id AND tenant_id=NEW.tenant_id AND outbox_id=NEW.outbox_id;
          IF NOT EXISTS(SELECT FROM cp6_requirement_set_application_delivery_v1.attempts a WHERE a.environment_id=d.environment_id AND a.tenant_id=d.tenant_id
            AND a.outbox_id=d.outbox_id AND a.epoch=d.epoch AND a.lease_token=d.lease_token AND a.route_json=d.route_json
            AND a.registration_revision=d.registration_revision AND a.subject_revision=d.subject_revision)
            OR (d.state IN('RECORDED','PENDING_GAP','BLOCKED','CONFLICT') AND NOT EXISTS(
              SELECT FROM cp6_requirement_set_application_delivery_v1.results r WHERE r.environment_id=d.environment_id AND r.tenant_id=d.tenant_id
              AND r.outbox_id=d.outbox_id AND r.epoch=d.epoch AND r.lease_token=d.lease_token AND r.state=d.state
              AND r.ack_json IS NOT DISTINCT FROM d.ack_json AND r.error_code IS NOT DISTINCT FROM d.error_code)) THEN
            RAISE EXCEPTION 'APPLICATION_DELIVERY_ATOMIC_RECEIPT_REQUIRED'; END IF;
          RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER application_delivery_complete AFTER INSERT OR UPDATE ON cp6_requirement_set_application_delivery_v1.delivery
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_requirement_set_application_delivery_v1.complete();
        CREATE FUNCTION cp6_requirement_set_application_delivery_v1.ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT cp6_requirement_set_review_application_v1.ready() AND cp6_approval_receipt_v2.ready() AND NOT EXISTS(
            SELECT FROM (VALUES
              ('delivery','application_delivery_validate','cp6_requirement_set_application_delivery_v1.validate()',23),
              ('delivery','application_delivery_no_delete','cp6_scope_authorization_v1.immutable()',42),
              ('delivery','application_delivery_root','cp6_authentication_v1.before_change()',22),
              ('delivery','application_delivery_complete','cp6_requirement_set_application_delivery_v1.complete()',21),
              ('attempts','application_delivery_root','cp6_authentication_v1.before_change()',6),
              ('results','application_delivery_root','cp6_authentication_v1.before_change()',6),
              ('attempts','application_delivery_immutable','cp6_scope_authorization_v1.immutable()',58),
              ('results','application_delivery_immutable','cp6_scope_authorization_v1.immutable()',58)) a(tbl,name,fn,typ)
            WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid=to_regclass('cp6_requirement_set_application_delivery_v1.'||a.tbl)
              AND t.tgname=a.name AND t.tgfoid=to_regprocedure(a.fn) AND t.tgtype=a.typ AND t.tgenabled IN('O','A') AND t.tgqual IS NULL
              AND (a.name<>'application_delivery_complete' OR t.tgdeferrable AND t.tginitdeferred)));
        $$;
        """);
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Forward recovery only; native delivery attempts and receipts are permanent.");
}
