using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261028220000_ApprovalReceiptRecoveryV2")]
public sealed class ApprovalReceiptRecoveryV2 : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        ALTER TABLE cp6_approval_receipt_v2.registrations ADD COLUMN can_recover boolean NOT NULL DEFAULT false;
        ALTER TABLE cp6_approval_receipt_v2.subject_grants ADD COLUMN can_recover boolean NOT NULL DEFAULT false;
        ALTER TABLE cp6_approval_receipt_v2.gaps ADD COLUMN route_json text,
          ADD COLUMN next_attempt timestamptz NOT NULL DEFAULT clock_timestamp(),
          ADD CONSTRAINT gap_lease_shape CHECK(epoch>=0 AND attempts>=0 AND
            ((epoch=0 AND lease_token IS NULL AND lease_until IS NULL AND route_json IS NULL)
              OR (epoch>0 AND lease_token IS NOT NULL AND lease_until IS NOT NULL AND route_json IS NOT NULL)));
        CREATE TABLE cp6_approval_receipt_v2.gap_observations(
          environment_id uuid NOT NULL,tenant_id uuid NOT NULL,stream_id uuid NOT NULL,id uuid NOT NULL,epoch bigint NOT NULL,
          principal_id uuid NOT NULL,route_json text NOT NULL,authority_json text NOT NULL,status integer NOT NULL,
          raw bytea NOT NULL CHECK(octet_length(raw)<=1048576),raw_hash text NOT NULL,recorded_at timestamptz NOT NULL,
          PRIMARY KEY(environment_id,tenant_id,id),
          FOREIGN KEY(environment_id,tenant_id,stream_id) REFERENCES cp6_approval_receipt_v2.gaps,
          CHECK(encode(sha256(raw),'hex')=raw_hash));
        CREATE TRIGGER recovery_observation_root BEFORE INSERT ON cp6_approval_receipt_v2.gap_observations
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER recovery_observation_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_approval_receipt_v2.gap_observations
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE FUNCTION cp6_approval_receipt_v2.recovery_lease() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF (NEW.environment_id,NEW.tenant_id,NEW.stream_id) IS DISTINCT FROM (OLD.environment_id,OLD.tenant_id,OLD.stream_id)
            OR NEW.target_revision<OLD.target_revision THEN RAISE EXCEPTION 'RECEIPT_GAP_IDENTITY_CHANGED'; END IF;
          IF NEW.epoch<>OLD.epoch THEN
            IF NEW.epoch<>OLD.epoch+1 OR NEW.attempts<>OLD.attempts+1 OR NEW.lease_token IS NOT DISTINCT FROM OLD.lease_token
              OR OLD.lease_until>clock_timestamp() OR OLD.completed THEN RAISE EXCEPTION 'RECEIPT_GAP_LEASE_CHANGED'; END IF;
            IF OLD.route_json IS NOT NULL AND (OLD.route_json::jsonb-'version') IS DISTINCT FROM (NEW.route_json::jsonb-'version') THEN
              RAISE EXCEPTION 'RECEIPT_GAP_ROUTE_CHANGED'; END IF;
          ELSIF NEW.lease_token IS DISTINCT FROM OLD.lease_token OR NEW.route_json IS DISTINCT FROM OLD.route_json OR NEW.attempts<>OLD.attempts THEN
            RAISE EXCEPTION 'RECEIPT_GAP_LEASE_CHANGED'; END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER recovery_lease_guard BEFORE UPDATE ON cp6_approval_receipt_v2.gaps
          FOR EACH ROW EXECUTE FUNCTION cp6_approval_receipt_v2.recovery_lease();
        CREATE FUNCTION cp6_approval_receipt_v2.recovery_ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT cp6_approval_receipt_v2.ready() AND NOT EXISTS(SELECT FROM (VALUES
            ('gap_observations','recovery_observation_root','cp6_authentication_v1.before_change()',6),
            ('gap_observations','recovery_observation_immutable','cp6_scope_authorization_v1.immutable()',58),
            ('gaps','recovery_lease_guard','cp6_approval_receipt_v2.recovery_lease()',19)) a(tbl,name,fn,typ)
            WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid=to_regclass('cp6_approval_receipt_v2.'||a.tbl)
              AND t.tgname=a.name AND t.tgfoid=to_regprocedure(a.fn) AND t.tgtype=a.typ AND t.tgenabled IN('O','A') AND t.tgqual IS NULL));
        $$;
        """);
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Forward recovery only; verified receipt prefixes and source observations are permanent.");
}
