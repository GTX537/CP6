using CP6.Core.Services.ErpIntegration;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.ErpIntegration;

[DbContext(typeof(ErpIntegrationContext))]
[Migration("20261120081000_CommercialProtectionV1")]
public sealed class CommercialProtectionV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE erp_integration.ic_source_control_v1 (
            tenant_id uuid NOT NULL,
            authority text COLLATE "C" NOT NULL,
            kind text COLLATE "C" NOT NULL CHECK (kind IN ('SUBJECT','PURPOSE_POLICY','PRODUCER_STREAM')),
            control_key text COLLATE "C" NOT NULL,
            watermark text COLLATE "C" NOT NULL,
            revocation_id uuid,
            revocation_bytes bytea,
            requested_at timestamptz,
            settled_at timestamptz,
            PRIMARY KEY (tenant_id,authority,kind,control_key),
            CHECK ((revocation_id IS NULL) = (revocation_bytes IS NULL)),
            CHECK ((requested_at IS NULL) = (revocation_id IS NULL)),
            CHECK (settled_at IS NULL OR (requested_at IS NOT NULL AND settled_at >= requested_at)),
            UNIQUE (tenant_id,revocation_id)
        );

        CREATE TABLE erp_integration.ic_source_publication_v1 (
            tenant_id uuid NOT NULL,
            participant text COLLATE "C" NOT NULL,
            subject_key char(64) COLLATE "C" NOT NULL,
            purpose text COLLATE "C" NOT NULL CHECK (purpose IN ('MAIN_INTAKE_NEW_EFFECT','CRM_RESULT_PROJECTION')),
            original_cipher bytea NOT NULL,
            PRIMARY KEY (tenant_id,participant,subject_key,purpose)
        );

        CREATE TABLE erp_integration.ic_grant_v1 (
            tenant_id uuid NOT NULL,
            protection_key char(64) COLLATE "C" NOT NULL,
            participant text COLLATE "C" NOT NULL,
            subject_key char(64) COLLATE "C" NOT NULL,
            purpose text COLLATE "C" NOT NULL CHECK (purpose IN ('MAIN_INTAKE_NEW_EFFECT','CRM_RESULT_PROJECTION')),
            consumer text COLLATE "C" NOT NULL,
            operation_kind text COLLATE "C" NOT NULL,
            operation_id uuid NOT NULL,
            execution_generation bigint NOT NULL CHECK (execution_generation > 0),
            state text COLLATE "C" NOT NULL CHECK (state IN ('GRANTED','DENIED','SETTLED')),
            original_cipher bytea NOT NULL,
            terminal_cipher bytea,
            PRIMARY KEY (tenant_id,protection_key),
            FOREIGN KEY (tenant_id,participant,subject_key,purpose)
              REFERENCES erp_integration.ic_source_publication_v1(tenant_id,participant,subject_key,purpose),
            UNIQUE (tenant_id,consumer,operation_kind,operation_id,execution_generation,participant,subject_key,purpose),
            CHECK ((state='SETTLED') = (terminal_cipher IS NOT NULL))
        );

        CREATE TABLE erp_integration.ic_control_grant_v1 (
            tenant_id uuid NOT NULL,
            authority text COLLATE "C" NOT NULL,
            kind text COLLATE "C" NOT NULL,
            control_key text COLLATE "C" NOT NULL,
            protection_key char(64) COLLATE "C" NOT NULL,
            PRIMARY KEY (tenant_id,authority,kind,control_key,protection_key),
            FOREIGN KEY (tenant_id,authority,kind,control_key)
              REFERENCES erp_integration.ic_source_control_v1(tenant_id,authority,kind,control_key),
            FOREIGN KEY (tenant_id,protection_key) REFERENCES erp_integration.ic_grant_v1(tenant_id,protection_key)
        );
        CREATE INDEX ix_ic_control_grant_original ON erp_integration.ic_control_grant_v1(tenant_id,protection_key);

        CREATE TABLE erp_integration.ic_execution_v1 (
            tenant_id uuid NOT NULL,
            consumer text COLLATE "C" NOT NULL,
            operation_kind text COLLATE "C" NOT NULL CHECK (operation_kind IN ('intake.decide','opportunity.result.apply')),
            operation_id uuid NOT NULL,
            state text COLLATE "C" NOT NULL CHECK (state IN ('PREPARING','READY','COMMITTED','ABORTED')),
            definition_cipher bytea NOT NULL,
            grant_set_cipher bytea,
            terminal_cipher bytea,
            PRIMARY KEY (tenant_id,consumer,operation_kind,operation_id),
            CHECK (state <> 'PREPARING' OR grant_set_cipher IS NULL),
            CHECK (state NOT IN ('READY','COMMITTED') OR grant_set_cipher IS NOT NULL),
            CHECK ((state IN ('COMMITTED','ABORTED')) = (terminal_cipher IS NOT NULL))
        );

        CREATE FUNCTION erp_integration.ic_preserve_original_v1() RETURNS trigger LANGUAGE plpgsql AS $preserve$
        BEGIN
            IF TG_OP='DELETE' THEN
                RAISE EXCEPTION 'IC_ORIGINAL_IMMUTABLE' USING ERRCODE='23514';
            END IF;
            IF TG_TABLE_NAME IN ('ic_source_publication_v1','ic_control_grant_v1') THEN
                RAISE EXCEPTION 'IC_ORIGINAL_IMMUTABLE' USING ERRCODE='23514';
            END IF;
            IF TG_TABLE_NAME='ic_source_control_v1' THEN
                IF NEW.tenant_id<>OLD.tenant_id OR NEW.authority<>OLD.authority OR NEW.kind<>OLD.kind OR NEW.control_key<>OLD.control_key
                   OR OLD.settled_at IS NOT NULL
                   OR OLD.revocation_id IS NULL AND (NEW.revocation_id IS NULL OR NEW.watermark<>OLD.watermark OR NEW.settled_at IS NOT NULL)
                   OR OLD.revocation_id IS NOT NULL AND
                     (NEW.revocation_id IS DISTINCT FROM OLD.revocation_id OR NEW.revocation_bytes IS DISTINCT FROM OLD.revocation_bytes
                      OR NEW.requested_at IS DISTINCT FROM OLD.requested_at OR NEW.settled_at IS NULL OR NEW.watermark=OLD.watermark)
                THEN RAISE EXCEPTION 'IC_CONTROL_TRANSITION_INVALID' USING ERRCODE='23514'; END IF;
            ELSIF TG_TABLE_NAME='ic_execution_v1' THEN
                IF NEW.tenant_id<>OLD.tenant_id OR NEW.consumer<>OLD.consumer OR NEW.operation_kind<>OLD.operation_kind
                   OR NEW.operation_id<>OLD.operation_id OR NEW.definition_cipher<>OLD.definition_cipher
                   OR OLD.state IN ('COMMITTED','ABORTED')
                   OR OLD.state='PREPARING' AND NEW.state NOT IN ('READY','ABORTED')
                   OR OLD.state='READY' AND (NEW.state NOT IN ('COMMITTED','ABORTED') OR NEW.grant_set_cipher IS DISTINCT FROM OLD.grant_set_cipher)
                THEN RAISE EXCEPTION 'IC_EXECUTION_TRANSITION_INVALID' USING ERRCODE='23514'; END IF;
            ELSIF TG_TABLE_NAME='ic_grant_v1' THEN
                IF NEW.tenant_id<>OLD.tenant_id OR NEW.protection_key<>OLD.protection_key OR NEW.participant<>OLD.participant
                   OR NEW.subject_key<>OLD.subject_key OR NEW.purpose<>OLD.purpose OR NEW.consumer<>OLD.consumer
                   OR NEW.operation_kind<>OLD.operation_kind OR NEW.operation_id<>OLD.operation_id
                   OR NEW.execution_generation<>OLD.execution_generation OR NEW.original_cipher<>OLD.original_cipher
                   OR OLD.state<>'GRANTED' OR NEW.state<>'SETTLED'
                THEN RAISE EXCEPTION 'IC_GRANT_TRANSITION_INVALID' USING ERRCODE='23514'; END IF;
            END IF;
            RETURN NEW;
        END
        $preserve$;
        CREATE TRIGGER ic_preserve_control BEFORE UPDATE OR DELETE ON erp_integration.ic_source_control_v1
            FOR EACH ROW EXECUTE FUNCTION erp_integration.ic_preserve_original_v1();
        CREATE TRIGGER ic_preserve_publication BEFORE UPDATE OR DELETE ON erp_integration.ic_source_publication_v1
            FOR EACH ROW EXECUTE FUNCTION erp_integration.ic_preserve_original_v1();
        CREATE TRIGGER ic_preserve_control_grant BEFORE UPDATE OR DELETE ON erp_integration.ic_control_grant_v1
            FOR EACH ROW EXECUTE FUNCTION erp_integration.ic_preserve_original_v1();
        CREATE TRIGGER ic_preserve_grant BEFORE UPDATE OR DELETE ON erp_integration.ic_grant_v1
            FOR EACH ROW EXECUTE FUNCTION erp_integration.ic_preserve_original_v1();
        CREATE TRIGGER ic_preserve_execution BEFORE UPDATE OR DELETE ON erp_integration.ic_execution_v1
            FOR EACH ROW EXECUTE FUNCTION erp_integration.ic_preserve_original_v1();
        """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("商业来源保护与原终态需要持久保留，仅支持前向迁移。");
}
