using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261119063000_BackgroundJobEvidenceV1")]
public sealed class BackgroundJobEvidenceV1 : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        ALTER TABLE cp6_native_timeout_v2.registrations ADD COLUMN history_read boolean NOT NULL DEFAULT true;
        CREATE TABLE cp6_native_timeout_v2.schedules(
          sequence bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
          tenant_id uuid NOT NULL, environment_id uuid NOT NULL, service_id uuid NOT NULL,
          version uuid NOT NULL, previous_version uuid, published_at timestamptz NOT NULL,
          settings jsonb NOT NULL, calendar jsonb NOT NULL, calendar_version text NOT NULL,
          zone_id text NOT NULL, zone_version text NOT NULL,
          UNIQUE(tenant_id,environment_id,service_id,version));
        CREATE INDEX timeout_schedule_latest ON cp6_native_timeout_v2.schedules(tenant_id,environment_id,service_id,sequence DESC);
        CREATE TABLE cp6_native_timeout_v2.runs(
          id uuid PRIMARY KEY, tenant_id uuid NOT NULL, environment_id uuid NOT NULL, service_id uuid NOT NULL,
          schedule_version uuid NOT NULL, slot_at timestamptz, evaluated_at timestamptz NOT NULL, claim_count int NOT NULL CHECK(claim_count>=0),
          host_observed boolean NOT NULL, recovery boolean NOT NULL DEFAULT false, confirmed_zero boolean NOT NULL DEFAULT false,
          CHECK(NOT confirmed_zero OR claim_count=0),
          FOREIGN KEY(tenant_id,environment_id,service_id,schedule_version)
            REFERENCES cp6_native_timeout_v2.schedules(tenant_id,environment_id,service_id,version),
          UNIQUE(tenant_id,environment_id,service_id,schedule_version,slot_at));
        CREATE INDEX timeout_runs_latest ON cp6_native_timeout_v2.runs(tenant_id,environment_id,service_id,evaluated_at DESC,id);
        CREATE TABLE cp6_native_timeout_v2.attempts(
          id uuid PRIMARY KEY, run_id uuid NOT NULL REFERENCES cp6_native_timeout_v2.runs(id),
          execution_id uuid NOT NULL REFERENCES cp6_native_timeout_v2.claims(execution_id),
          epoch bigint NOT NULL CHECK(epoch>0), registration_revision uuid NOT NULL, input_digest text NOT NULL,
          UNIQUE(run_id,execution_id));
        CREATE INDEX timeout_attempts_original ON cp6_native_timeout_v2.attempts(execution_id,epoch DESC);
        CREATE TABLE cp6_native_timeout_v2.attempt_observations(
          sequence bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
          attempt_id uuid NOT NULL REFERENCES cp6_native_timeout_v2.attempts(id),
          state text NOT NULL CHECK(state IN('OWNER_VERIFIED','BLOCKED','FAILED_BEFORE_EFFECT','UNKNOWN','RETRYING','ISOLATED')),
          reason text, observed_at timestamptz NOT NULL DEFAULT clock_timestamp());
        CREATE INDEX timeout_attempt_timeline ON cp6_native_timeout_v2.attempt_observations(attempt_id,sequence);
        CREATE UNIQUE INDEX timeout_verified_once ON cp6_native_timeout_v2.attempt_observations(attempt_id) WHERE state='OWNER_VERIFIED';

        DO $$ DECLARE n text;BEGIN
          FOREACH n IN ARRAY ARRAY['schedules','runs','attempts','attempt_observations'] LOOP
            EXECUTE format('CREATE TRIGGER timeout_root BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_native_timeout_v2.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change()',n);
            EXECUTE format('CREATE TRIGGER timeout_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_native_timeout_v2.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable()',n);
          END LOOP;
        END $$;

        CREATE FUNCTION cp6_native_timeout_v2.run_evidence_ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT current_setting('session_replication_role')='origin' AND NOT EXISTS (
            SELECT FROM unnest(ARRAY['schedules','runs','attempts','attempt_observations']) AS x(tbl)
            CROSS JOIN (VALUES ('timeout_root','cp6_authentication_v1.before_change()',62),
                               ('timeout_immutable','cp6_scope_authorization_v1.immutable()',58)) AS y(name,fn,typ)
            WHERE NOT EXISTS (SELECT FROM pg_trigger t WHERE t.tgrelid=to_regclass('cp6_native_timeout_v2.'||x.tbl)
              AND t.tgname=y.name AND t.tgfoid=to_regprocedure(y.fn) AND t.tgtype=y.typ
              AND t.tgenabled IN('O','A') AND t.tgqual IS NULL AND t.tgnargs=0 AND NOT t.tgdeferrable));
        $$;
        REVOKE ALL ON FUNCTION cp6_native_timeout_v2.run_evidence_ready() FROM PUBLIC;

        CREATE SCHEMA cp6_job_retention_v1;
        CREATE TABLE cp6_job_retention_v1.policies(
          sequence bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
          tenant_id uuid NOT NULL, environment_id uuid NOT NULL, data_class text NOT NULL
            CHECK(data_class IN('WFS_SERVICE_JOB','WFS_TRIGGER_FIRE','WMS_RAW_SCAN')),
          version uuid UNIQUE NOT NULL, source_id uuid NOT NULL, basis_ref uuid NOT NULL, enabled boolean NOT NULL,
          database_name text NOT NULL, database_oid bigint NOT NULL, database_address text NOT NULL, database_port int NOT NULL,
          retention_days int NOT NULL CHECK(retention_days BETWEEN 0 AND 36500),
          CHECK((data_class='WMS_RAW_SCAN' AND retention_days=0) OR (data_class<>'WMS_RAW_SCAN' AND retention_days>0)));
        CREATE INDEX job_retention_policy_latest ON cp6_job_retention_v1.policies(tenant_id,environment_id,data_class,sequence DESC);
        CREATE TABLE cp6_job_retention_v1.safeguards(
          sequence bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
          tenant_id uuid NOT NULL, environment_id uuid NOT NULL, data_class text NOT NULL,
          entity_id uuid NOT NULL, reference_id uuid NOT NULL,
          kind text NOT NULL CHECK(kind IN('HOLD','REQUIRED_REFERENCE','PROCESSING','UNKNOWN','RESCHEDULING','DISPUTE')),
          active boolean NOT NULL);
        CREATE INDEX job_retention_safeguards_latest ON cp6_job_retention_v1.safeguards(tenant_id,environment_id,data_class,entity_id,kind,reference_id,sequence DESC);
        CREATE TABLE cp6_job_retention_v1.purpose_uses(
          tenant_id uuid NOT NULL, environment_id uuid NOT NULL, data_class text NOT NULL,
          entity_id uuid NOT NULL, operation_id uuid NOT NULL, owner_key text NOT NULL, input_digest text NOT NULL, review_at timestamptz NOT NULL,
          PRIMARY KEY(tenant_id,environment_id,data_class,entity_id,operation_id));
        CREATE TABLE cp6_job_retention_v1.plans(
          id uuid PRIMARY KEY, tenant_id uuid NOT NULL, environment_id uuid NOT NULL, data_class text NOT NULL,
          policy_version uuid NOT NULL REFERENCES cp6_job_retention_v1.policies(version),
          planned_at timestamptz NOT NULL, cutoff timestamptz NOT NULL, expires_at timestamptz NOT NULL,
          reference_version bigint NOT NULL, eligible int NOT NULL CHECK(eligible>=0));
        CREATE INDEX job_retention_plan_latest ON cp6_job_retention_v1.plans(tenant_id,environment_id,data_class,planned_at DESC,id);
        CREATE TABLE cp6_job_retention_v1.members(
          plan_id uuid NOT NULL REFERENCES cp6_job_retention_v1.plans(id),
          ordinal int NOT NULL CHECK(ordinal>=0), entity_id uuid NOT NULL, row_version text NOT NULL,
          held boolean NOT NULL, pending_terminal boolean NOT NULL CHECK(NOT pending_terminal OR held),
          PRIMARY KEY(plan_id,ordinal), UNIQUE(plan_id,entity_id));
        CREATE TABLE cp6_job_retention_v1.shards(
          plan_id uuid NOT NULL REFERENCES cp6_job_retention_v1.plans(id),
          cursor int NOT NULL CHECK(cursor>=0), next_cursor int NOT NULL CHECK(next_cursor>cursor),
          id uuid UNIQUE NOT NULL, disposed int NOT NULL CHECK(disposed>=0), held int NOT NULL CHECK(held>=0),
          pending_terminal int NOT NULL CHECK(pending_terminal BETWEEN 0 AND held),
          excluded int NOT NULL CHECK(excluded>=0), committed_at timestamptz NOT NULL DEFAULT clock_timestamp(),
          PRIMARY KEY(plan_id,cursor), CHECK(disposed+held+excluded=next_cursor-cursor));
        CREATE TABLE cp6_job_retention_v1.stops(
          plan_id uuid PRIMARY KEY REFERENCES cp6_job_retention_v1.plans(id),
          reason text NOT NULL, stopped_at timestamptz NOT NULL DEFAULT clock_timestamp());
        DO $$ DECLARE n text;BEGIN
          FOREACH n IN ARRAY ARRAY['policies','safeguards','purpose_uses','plans','members','shards','stops'] LOOP
            EXECUTE format('CREATE TRIGGER retention_root BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_job_retention_v1.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change()',n);
            EXECUTE format('CREATE TRIGGER retention_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_job_retention_v1.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable()',n);
          END LOOP;
        END $$;
        CREATE FUNCTION cp6_job_retention_v1.ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT cp6_scope_authorization_v1.writers_ready() AND NOT EXISTS (
            SELECT FROM (
              SELECT 'cp6_job_retention_v1.'||x.tbl AS tbl, y.name, y.fn, y.typ
              FROM unnest(ARRAY['policies','safeguards','purpose_uses','plans','members','shards','stops']) AS x(tbl)
              CROSS JOIN (VALUES ('retention_root','cp6_authentication_v1.before_change()',62),
                                 ('retention_immutable','cp6_scope_authorization_v1.immutable()',58)) AS y(name,fn,typ)
              UNION ALL SELECT 'cp6_privacy_disposition_v1.holds','authority_gate','cp6_privacy_disposition_v1.gate()',62
            ) AS e
            WHERE NOT EXISTS (SELECT FROM pg_trigger t WHERE t.tgrelid=to_regclass(e.tbl) AND t.tgname=e.name
              AND t.tgfoid=to_regprocedure(e.fn) AND t.tgtype=e.typ AND t.tgenabled IN('O','A')
              AND t.tgqual IS NULL AND t.tgnargs=0 AND NOT t.tgdeferrable));
        $$;
        REVOKE ALL ON SCHEMA cp6_job_retention_v1 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_job_retention_v1 FROM PUBLIC;
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_job_retention_v1, cp6_native_timeout_v2 FROM PUBLIC;
        REVOKE ALL ON ALL SEQUENCES IN SCHEMA cp6_job_retention_v1, cp6_native_timeout_v2 FROM PUBLIC;
        """);

    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("后台作业的计划和回执仅允许前向迁移。");
}
