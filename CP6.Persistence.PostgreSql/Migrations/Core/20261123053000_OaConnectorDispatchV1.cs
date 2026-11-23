using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261123053000_OaConnectorDispatchV1")]
public sealed class OaConnectorDispatchV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE INDEX connector_dispatch_original_job ON cp6_oa_conn_v1.runtime_members(tenant_id,environment_id,id)
            WHERE kind='DISPATCH';
        CREATE FUNCTION cp6_oa_conn_v1.job_lock(tenant uuid, job uuid) RETURNS bigint
        LANGUAGE sql IMMUTABLE SET search_path=pg_catalog AS $$
            SELECT hashtextextended('oa-connector-job/'||tenant::text||'/'||job::text,0)
        $$;
        CREATE TABLE cp6_oa_conn_v1.dispatch_admissions (
            tenant_id uuid NOT NULL, environment_id uuid NOT NULL, job_id uuid NOT NULL, instance_id uuid NOT NULL,
            token_id uuid NOT NULL, node_id text NOT NULL, flow_version_id uuid NOT NULL,
            root_id uuid NOT NULL, version_id uuid NOT NULL, member_kind text GENERATED ALWAYS AS ('DISPATCH') STORED,
            source_kind text NOT NULL CHECK(source_kind IN ('TENANT','APP')), operation text NOT NULL,
            profile_id uuid NOT NULL, profile_revision uuid NOT NULL, control_revision uuid NOT NULL,
            starter_id uuid NOT NULL, permission_domain uuid NOT NULL, authority_epoch bigint NOT NULL, authority_revision uuid NOT NULL,
            service_registration text NOT NULL, disclosure_decision text NOT NULL, owner_decision text,
            allows_uncertain_first_call boolean NOT NULL, simulated boolean NOT NULL,
            worker_id text NOT NULL, claim_at timestamptz NOT NULL, first_attempt integer NOT NULL CHECK(first_attempt>0),
            effect_key text NOT NULL CHECK(effect_key ~ '^[0-9a-f]{64}$'),
            transport_key text NOT NULL CHECK(transport_key ~ '^[0-9a-f]{64}$'),
            input_bytes bytea NOT NULL CHECK(octet_length(input_bytes) BETWEEN 1 AND 65536),
            input_sha256 text NOT NULL CHECK(input_sha256=encode(sha256(input_bytes),'hex')),
            admitted_at timestamptz NOT NULL, idempotency_until timestamptz, query_until timestamptz,
            PRIMARY KEY(tenant_id,environment_id,job_id), UNIQUE(tenant_id,environment_id,effect_key),
            UNIQUE(tenant_id,environment_id,transport_key),
            FOREIGN KEY(tenant_id,environment_id,root_id,member_kind,job_id) REFERENCES cp6_oa_conn_v1.runtime_members,
            FOREIGN KEY(tenant_id,environment_id,root_id,version_id) REFERENCES cp6_oa_conn_v1.versions,
            FOREIGN KEY(tenant_id,environment_id,profile_id) REFERENCES cp6_oa_conn_v1.profiles,
            CHECK(idempotency_until>admitted_at), CHECK(query_until>admitted_at));
        CREATE INDEX connector_dispatch_root ON cp6_oa_conn_v1.dispatch_admissions(tenant_id,environment_id,root_id,admitted_at DESC,job_id);
        CREATE TABLE cp6_oa_conn_v1.dispatch_observations (
            tenant_id uuid NOT NULL, environment_id uuid NOT NULL, job_id uuid NOT NULL,
            worker_id text NOT NULL, claim_at timestamptz NOT NULL, attempt integer NOT NULL CHECK(attempt>0),
            observed_at timestamptz NOT NULL DEFAULT clock_timestamp(), source text NOT NULL CHECK(source IN ('SEND','ORIGINAL_QUERY')),
            state text NOT NULL CHECK(state IN ('HTTP_ACK','OUTCOME_UNKNOWN','NOT_OBSERVED','ORIGINAL_OPERATION_OBSERVED')),
            proof_ref text,
            PRIMARY KEY(tenant_id,environment_id,job_id,attempt),
            FOREIGN KEY(tenant_id,environment_id,job_id) REFERENCES cp6_oa_conn_v1.dispatch_admissions,
            CHECK((state='ORIGINAL_OPERATION_OBSERVED')=(proof_ref IS NOT NULL)),
            CHECK((source='SEND' AND state IN ('HTTP_ACK','OUTCOME_UNKNOWN'))
              OR (source='ORIGINAL_QUERY' AND state IN ('NOT_OBSERVED','ORIGINAL_OPERATION_OBSERVED','OUTCOME_UNKNOWN'))));
        CREATE TABLE cp6_oa_conn_v1.dispatch_cancellations (
            tenant_id uuid NOT NULL, environment_id uuid NOT NULL, job_id uuid NOT NULL,
            requested_at timestamptz NOT NULL DEFAULT clock_timestamp(),
            PRIMARY KEY(tenant_id,environment_id,job_id),
            FOREIGN KEY(tenant_id,environment_id,job_id) REFERENCES cp6_oa_conn_v1.dispatch_admissions);

        -- 撤销 WF_AUTH 时保留已提交的在途准入，即便随后恢复授权。
        CREATE FUNCTION cp6_oa_conn_v1.request_dispatch_cancellation() RETURNS trigger
        LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
            INSERT INTO cp6_oa_conn_v1.dispatch_cancellations(tenant_id,environment_id,job_id)
            SELECT a.tenant_id,a.environment_id,a.job_id FROM cp6_oa_conn_v1.dispatch_admissions a
            JOIN cp6_oa_conn_v1.runtime_members m ON (m.tenant_id,m.environment_id,m.root_id,m.kind,m.id)=
                (a.tenant_id,a.environment_id,a.root_id,a.member_kind,a.job_id)
            WHERE a.tenant_id=NEW.tenant_id AND a.environment_id=NEW.environment_id
              AND m.state IN ('DISPATCH_ADMITTED','SENT','OUTCOME_UNKNOWN')
            ON CONFLICT DO NOTHING;
            RETURN NULL;
        END $$;
        CREATE TRIGGER oa_conn_cancel_dispatch AFTER UPDATE OF enabled ON cp6_scope_authorization_v1.fences
            FOR EACH ROW WHEN (OLD.enabled AND NOT NEW.enabled) EXECUTE FUNCTION cp6_oa_conn_v1.request_dispatch_cancellation();

        DO $$
        DECLARE rel text;
        BEGIN
            FOREACH rel IN ARRAY ARRAY['dispatch_admissions','dispatch_observations','dispatch_cancellations'] LOOP
                EXECUTE format('CREATE TRIGGER immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_oa_conn_v1.%I
                    FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable()',rel);
            END LOOP;
        END $$;
        CREATE FUNCTION cp6_oa_conn_v1.dispatch_ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
            SELECT cp6_oa_conn_v1.ready() AND EXISTS (
                SELECT FROM pg_trigger WHERE tgrelid='cp6_scope_authorization_v1.fences'::regclass
                  AND tgname='oa_conn_cancel_dispatch' AND tgfoid='cp6_oa_conn_v1.request_dispatch_cancellation()'::regprocedure
                  AND tgenabled IN ('O','A') AND NOT tgisinternal)
            AND NOT EXISTS (
                SELECT FROM unnest(ARRAY['dispatch_admissions','dispatch_observations','dispatch_cancellations']) rel
                WHERE NOT EXISTS (SELECT FROM pg_trigger WHERE tgrelid=to_regclass('cp6_oa_conn_v1.'||rel)
                  AND tgname='immutable' AND tgfoid='cp6_scope_authorization_v1.immutable()'::regprocedure
                  AND tgenabled IN ('O','A') AND tgtype=58 AND NOT tgisinternal))
        $$;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("已记录连接器派发历史，迁移仅支持前滚。");
}
