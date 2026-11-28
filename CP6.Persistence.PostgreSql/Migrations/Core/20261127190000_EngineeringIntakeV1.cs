using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261127190000_EngineeringIntakeV1")]
public sealed class EngineeringIntakeV1 : Migration
{
    protected override void Up(MigrationBuilder migration) => migration.Sql("""
        CREATE SCHEMA cp6_engineering_intake_v1;
        REVOKE ALL ON SCHEMA cp6_engineering_intake_v1 FROM PUBLIC;

        -- 由 Main Owner 在核实全部现有写入方和历史目录后登记。迁移不为任意租户自动填充。
        CREATE TABLE cp6_engineering_intake_v1.coverage (
            tenant_id uuid NOT NULL,
            authority text COLLATE "C" NOT NULL,
            legal_entity_key text COLLATE "C" NOT NULL,
            generation uuid NOT NULL CHECK (generation <> '00000000-0000-0000-0000-000000000000'::uuid),
            evidence_key text NOT NULL CHECK (length(evidence_key) BETWEEN 1 AND 128),
            enabled boolean NOT NULL DEFAULT false,
            PRIMARY KEY (tenant_id, authority, legal_entity_key)
        );

        CREATE TABLE cp6_engineering_intake_v1.cases (
            tenant_id uuid NOT NULL,
            case_id uuid NOT NULL,
            authority text COLLATE "C" NOT NULL,
            legal_entity_key text COLLATE "C" NOT NULL,
            opportunity_id uuid NOT NULL,
            case_revision bigint NOT NULL CHECK (case_revision > 0),
            current_es bigint NOT NULL CHECK (current_es > 0),
            current_request_key uuid NOT NULL,
            execution_generation uuid NOT NULL,
            PRIMARY KEY (tenant_id, case_id),
            UNIQUE (tenant_id, authority, legal_entity_key, opportunity_id),
            FOREIGN KEY (tenant_id, authority, legal_entity_key) REFERENCES cp6_engineering_intake_v1.coverage
        );

        CREATE TABLE cp6_engineering_intake_v1.requests (
            tenant_id uuid NOT NULL,
            request_key uuid NOT NULL,
            case_id uuid NOT NULL,
            submission_version bigint NOT NULL CHECK (submission_version > 0),
            event_id uuid NOT NULL,
            source_digest text NOT NULL CHECK (source_digest ~ '^[0-9a-f]{64}$'),
            source_body_hash text NOT NULL CHECK (source_body_hash ~ '^[0-9a-f]{64}$'),
            body_hash text NOT NULL CHECK (body_hash ~ '^[0-9a-f]{64}$'),
            original_cipher bytea NOT NULL CHECK (octet_length(original_cipher) > 0),
            first_received_at timestamptz NOT NULL,
            PRIMARY KEY (tenant_id, request_key),
            UNIQUE (tenant_id, event_id),
            UNIQUE (tenant_id, case_id, submission_version),
            UNIQUE (tenant_id, case_id, submission_version, request_key),
            FOREIGN KEY (tenant_id, case_id) REFERENCES cp6_engineering_intake_v1.cases DEFERRABLE INITIALLY DEFERRED
        );

        ALTER TABLE cp6_engineering_intake_v1.cases ADD CONSTRAINT intake_current_es
            FOREIGN KEY (tenant_id, case_id, current_es, current_request_key)
            REFERENCES cp6_engineering_intake_v1.requests (tenant_id, case_id, submission_version, request_key)
            DEFERRABLE INITIALLY DEFERRED;

        CREATE TABLE cp6_engineering_intake_v1.intake_results (
            tenant_id uuid NOT NULL,
            request_key uuid NOT NULL,
            receipt_id text COLLATE "C" NOT NULL,
            outcome text NOT NULL CHECK (outcome IN ('ACCEPTED', 'NEEDS_INFO', 'REJECTED')),
            engineering_id uuid,
            engineering_revision bigint,
            source_package_id uuid,
            source_package_revision bigint,
            body_hash text NOT NULL CHECK (body_hash ~ '^[0-9a-f]{64}$'),
            original_cipher bytea NOT NULL CHECK (octet_length(original_cipher) > 0),
            PRIMARY KEY (tenant_id, request_key),
            UNIQUE (tenant_id, receipt_id),
            FOREIGN KEY (tenant_id, request_key) REFERENCES cp6_engineering_intake_v1.requests,
            FOREIGN KEY (tenant_id, engineering_id, engineering_revision)
                REFERENCES cp6_engineering_requirement_v1.versions,
            FOREIGN KEY (tenant_id, source_package_id, source_package_revision)
                REFERENCES cp6_engineering_requirement_v1.versions,
            CHECK ((engineering_id IS NULL AND engineering_revision IS NULL
                AND source_package_id IS NULL AND source_package_revision IS NULL AND outcome <> 'ACCEPTED')
                OR (engineering_id IS NOT NULL AND engineering_revision IS NOT NULL
                AND source_package_id IS NOT NULL AND source_package_revision IS NOT NULL AND engineering_id <> source_package_id))
        );

        CREATE TABLE cp6_engineering_intake_v1.outbox (
            tenant_id uuid NOT NULL,
            request_key uuid NOT NULL,
            PRIMARY KEY (tenant_id, request_key),
            FOREIGN KEY (tenant_id, request_key) REFERENCES cp6_engineering_intake_v1.intake_results
        );
        ALTER TABLE cp6_engineering_intake_v1.requests ADD CONSTRAINT intake_original_result
            FOREIGN KEY (tenant_id, request_key) REFERENCES cp6_engineering_intake_v1.outbox DEFERRABLE INITIALLY DEFERRED;

        CREATE FUNCTION cp6_engineering_intake_v1.advance_coverage() RETURNS trigger LANGUAGE plpgsql
        SET search_path=pg_catalog AS $$
        BEGIN
            IF (NEW.tenant_id, NEW.authority, NEW.legal_entity_key)
                IS DISTINCT FROM (OLD.tenant_id, OLD.authority, OLD.legal_entity_key) THEN
                RAISE EXCEPTION 'INTAKE_COVERAGE_SCOPE_IMMUTABLE';
            END IF;
            NEW.generation := gen_random_uuid();
            RETURN NEW;
        END $$;
        CREATE TRIGGER intake_coverage_advance BEFORE UPDATE ON cp6_engineering_intake_v1.coverage
            FOR EACH ROW EXECUTE FUNCTION cp6_engineering_intake_v1.advance_coverage();
        CREATE TRIGGER intake_coverage_permanent BEFORE DELETE OR TRUNCATE ON cp6_engineering_intake_v1.coverage
            FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();

        CREATE FUNCTION cp6_engineering_intake_v1.advance_case() RETURNS trigger LANGUAGE plpgsql
        SET search_path=pg_catalog AS $$
        BEGIN
            IF TG_OP = 'INSERT' THEN
                IF NEW.case_revision <> 1 OR NEW.current_es <> 1 THEN RAISE EXCEPTION 'INTAKE_CASE_FRONTIER_INVALID'; END IF;
            ELSIF (NEW.tenant_id, NEW.case_id, NEW.authority, NEW.legal_entity_key, NEW.opportunity_id)
                IS DISTINCT FROM (OLD.tenant_id, OLD.case_id, OLD.authority, OLD.legal_entity_key, OLD.opportunity_id)
                OR NEW.case_revision <> OLD.case_revision + 1 OR NEW.current_es <> OLD.current_es + 1
                OR NEW.execution_generation = OLD.execution_generation THEN
                RAISE EXCEPTION 'INTAKE_CASE_FRONTIER_INVALID';
            END IF;
            RETURN NEW;
        END $$;

        CREATE TRIGGER intake_case_advance BEFORE INSERT OR UPDATE ON cp6_engineering_intake_v1.cases
            FOR EACH ROW EXECUTE FUNCTION cp6_engineering_intake_v1.advance_case();
        CREATE TRIGGER intake_case_permanent BEFORE DELETE OR TRUNCATE ON cp6_engineering_intake_v1.cases
            FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER intake_request_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_engineering_intake_v1.requests
            FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER intake_result_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_engineering_intake_v1.intake_results
            FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();

        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_engineering_intake_v1 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_engineering_intake_v1 FROM PUBLIC;
        """);

    protected override void Down(MigrationBuilder migration) =>
        throw new NotSupportedException("工程受理原件与目录需要前向迁移。");
}
