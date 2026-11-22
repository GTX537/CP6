using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261122153000_ContactHistoryExportV11")]
public sealed class ContactHistoryExportV11 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE SCHEMA cp6_contact_history_export_v11;
        REVOKE ALL ON SCHEMA cp6_contact_history_export_v11 FROM PUBLIC;

        CREATE TABLE cp6_contact_history_export_v11.sources (
            tenant_id uuid NOT NULL,
            contact_id uuid NOT NULL,
            privacy_revision bigint NOT NULL CHECK (privacy_revision>0),
            retained boolean NOT NULL,
            PRIMARY KEY (tenant_id,contact_id));
        CREATE TABLE cp6_contact_history_export_v11.access (
            tenant_id uuid NOT NULL,
            contact_id uuid NOT NULL,
            subject_id uuid NOT NULL,
            watermark text COLLATE "C" NOT NULL CHECK (length(watermark) BETWEEN 1 AND 256),
            field_ids text[] NOT NULL,
            purposes text[] NOT NULL,
            can_read boolean NOT NULL,
            can_request boolean NOT NULL,
            can_download boolean NOT NULL,
            access_revision bigint NOT NULL DEFAULT 1 CHECK (access_revision>0),
            valid_from timestamptz NOT NULL DEFAULT clock_timestamp() CHECK (isfinite(valid_from)),
            valid_until timestamptz NOT NULL CHECK (isfinite(valid_until) AND valid_until>valid_from),
            recorded_through timestamptz NOT NULL CHECK (isfinite(recorded_through)),
            coverage text NOT NULL CHECK (coverage IN ('COMPLETE','PARTIAL')),
            PRIMARY KEY (tenant_id,contact_id,subject_id),
            FOREIGN KEY (tenant_id,contact_id) REFERENCES cp6_contact_history_export_v11.sources);
        CREATE FUNCTION cp6_contact_history_export_v11.advance_access_revision() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
            NEW.access_revision := OLD.access_revision + 1;
            RETURN NEW;
        END;
        $$;
        CREATE TRIGGER contact_export_access_revision BEFORE UPDATE ON cp6_contact_history_export_v11.access
            FOR EACH ROW EXECUTE FUNCTION cp6_contact_history_export_v11.advance_access_revision();
        CREATE TABLE cp6_contact_history_export_v11.history (
            tenant_id uuid NOT NULL,
            contact_id uuid NOT NULL,
            subject_id uuid NOT NULL,
            watermark text COLLATE "C" NOT NULL,
            purpose text COLLATE "C" NOT NULL,
            view text NOT NULL CHECK (view IN ('AS_RECORDED','CORRECTED_INTERPRETATION')),
            fact_id uuid NOT NULL,
            revision bigint NOT NULL CHECK (revision>0),
            recorded_at timestamptz NOT NULL CHECK (isfinite(recorded_at)),
            occurred_at timestamptz NOT NULL CHECK (isfinite(occurred_at)),
            values_json jsonb NOT NULL CHECK (jsonb_typeof(values_json)='object' AND octet_length(values_json::text)<=65536),
            PRIMARY KEY (tenant_id,contact_id,subject_id,watermark,purpose,view,fact_id,revision),
            FOREIGN KEY (tenant_id,contact_id,subject_id) REFERENCES cp6_contact_history_export_v11.access);

        CREATE TABLE cp6_contact_history_export_v11.requests (
            tenant_id uuid NOT NULL,
            request_id uuid NOT NULL,
            client_id text COLLATE "C" NOT NULL,
            source_event_id uuid NOT NULL,
            contact_id uuid NOT NULL,
            subject_id uuid NOT NULL,
            epoch text COLLATE "C" NOT NULL,
            intent_digest text COLLATE "C" NOT NULL CHECK (intent_digest ~ '^[a-f0-9]{64}$'),
            intent_bytes bytea NOT NULL,
            job_id uuid,
            receipt_event_id uuid NOT NULL,
            decision text NOT NULL CHECK (decision IN ('ACCEPTED','DECLINED')),
            PRIMARY KEY (tenant_id,request_id),
            UNIQUE (tenant_id,source_event_id),
            UNIQUE (tenant_id,job_id),
            UNIQUE (tenant_id,request_id,job_id),
            FOREIGN KEY (tenant_id,contact_id) REFERENCES cp6_contact_history_export_v11.sources,
            CHECK ((decision='ACCEPTED')=(job_id IS NOT NULL)));

        CREATE TABLE cp6_contact_history_export_v11.artifacts (
            tenant_id uuid NOT NULL,
            request_id uuid NOT NULL,
            job_id uuid NOT NULL,
            artifact_id uuid NOT NULL,
            artifact_version text COLLATE "C" NOT NULL,
            digest text COLLATE "C" NOT NULL CHECK (digest ~ '^[a-f0-9]{64}$'),
            byte_length integer NOT NULL CHECK (byte_length BETWEEN 1 AND 2097152),
            privacy_revision bigint NOT NULL CHECK (privacy_revision>0),
            source_watermark text COLLATE "C" NOT NULL,
            state text NOT NULL CHECK (state IN ('READY','REVOKED')),
            redacted boolean NOT NULL,
            PRIMARY KEY (tenant_id,artifact_id,artifact_version),
            FOREIGN KEY (tenant_id,request_id,job_id) REFERENCES cp6_contact_history_export_v11.requests(tenant_id,request_id,job_id));
        CREATE UNIQUE INDEX contact_export_redacted ON cp6_contact_history_export_v11.artifacts(tenant_id,request_id,artifact_id) WHERE redacted;
        CREATE TABLE cp6_contact_history_export_v11.payloads (
            tenant_id uuid NOT NULL,
            artifact_id uuid NOT NULL,
            artifact_version text COLLATE "C" NOT NULL,
            bytes bytea NOT NULL CHECK (octet_length(bytes) BETWEEN 1 AND 2097152),
            PRIMARY KEY (tenant_id,artifact_id,artifact_version),
            FOREIGN KEY (tenant_id,artifact_id,artifact_version) REFERENCES cp6_contact_history_export_v11.artifacts);
        CREATE FUNCTION cp6_contact_history_export_v11.reject_payload_update() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
            RAISE EXCEPTION 'CONTACT_EXPORT_PAYLOAD_IMMUTABLE' USING ERRCODE='23514';
        END;
        $$;
        CREATE TRIGGER contact_export_payload_immutable BEFORE UPDATE ON cp6_contact_history_export_v11.payloads
            FOR EACH ROW EXECUTE FUNCTION cp6_contact_history_export_v11.reject_payload_update();

        CREATE TABLE cp6_contact_history_export_v11.originals (
            tenant_id uuid NOT NULL,
            request_id uuid NOT NULL,
            ordinal bigint GENERATED ALWAYS AS IDENTITY,
            event_id uuid NOT NULL,
            kind text NOT NULL CHECK (kind IN ('JOB','ARTIFACT','DECLINED')),
            stream_digest text COLLATE "C" NOT NULL CHECK (stream_digest ~ '^[a-f0-9]{64}$'),
            revision bigint NOT NULL CHECK (revision>0),
            body bytea NOT NULL CHECK (octet_length(body) BETWEEN 1 AND 262144),
            body_sha256 text COLLATE "C" NOT NULL CHECK (body_sha256=encode(sha256(body),'hex')),
            key_id text COLLATE "C" NOT NULL,
            signature text COLLATE "C" NOT NULL,
            PRIMARY KEY (tenant_id,event_id),
            UNIQUE (tenant_id,stream_digest,revision),
            FOREIGN KEY (tenant_id,request_id) REFERENCES cp6_contact_history_export_v11.requests);
        CREATE INDEX contact_export_request_events ON cp6_contact_history_export_v11.originals(tenant_id,request_id,ordinal);
        CREATE TABLE cp6_contact_history_export_v11.conflicts (
            tenant_id uuid NOT NULL,
            request_id uuid NOT NULL,
            conflict_id uuid NOT NULL,
            event_id uuid NOT NULL,
            alternate_hash text COLLATE "C" NOT NULL CHECK (alternate_hash ~ '^[a-f0-9]{64}$'),
            resolved_by uuid,
            PRIMARY KEY (tenant_id,conflict_id),
            FOREIGN KEY (tenant_id,request_id) REFERENCES cp6_contact_history_export_v11.requests,
            FOREIGN KEY (tenant_id,event_id) REFERENCES cp6_contact_history_export_v11.originals,
            FOREIGN KEY (tenant_id,resolved_by) REFERENCES cp6_contact_history_export_v11.originals);

        CREATE TABLE cp6_contact_history_export_v11.permits (
            tenant_id uuid NOT NULL,
            permit_id uuid NOT NULL,
            request_id uuid NOT NULL,
            service_token_jti text COLLATE "C" NOT NULL,
            binding bytea NOT NULL,
            binding_hash text COLLATE "C" NOT NULL CHECK (binding_hash=encode(sha256(binding),'hex')),
            valid_from timestamptz NOT NULL,
            valid_until timestamptz NOT NULL CHECK (isfinite(valid_until) AND valid_until>valid_from),
            access_revision bigint NOT NULL CHECK (access_revision>0),
            PRIMARY KEY (tenant_id,permit_id),
            FOREIGN KEY (tenant_id,request_id) REFERENCES cp6_contact_history_export_v11.requests);
        """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("联系人历史导出原决定及制品只支持前向迁移。");
}
