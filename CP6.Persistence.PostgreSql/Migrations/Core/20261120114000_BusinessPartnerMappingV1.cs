using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context))]
[Migration("20261120114000_BusinessPartnerMappingV1")]
public sealed class BusinessPartnerMappingV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE main_bp.commands ALTER COLUMN bp_id DROP NOT NULL,
            ADD COLUMN account_id uuid,
            ADD CHECK (bp_id IS NOT NULL OR account_id IS NOT NULL),
            ADD FOREIGN KEY (tenant_id,legal_entity_key) REFERENCES main_bp.scopes;

        CREATE TABLE main_bp.mapping_authorities (
            tenant_id uuid NOT NULL, legal_entity_key text COLLATE "C" NOT NULL,
            purpose text COLLATE "C" NOT NULL, organization_id uuid NOT NULL, authority text COLLATE "C" NOT NULL,
            epoch text COLLATE "C" NOT NULL, key_id text COLLATE "C" NOT NULL, public_key bytea NOT NULL,
            route_key text COLLATE "C" NOT NULL, enabled boolean NOT NULL DEFAULT false,
            PRIMARY KEY (tenant_id,legal_entity_key,purpose),
            FOREIGN KEY (tenant_id,legal_entity_key) REFERENCES main_bp.scopes,
            CHECK (purpose IN ('ACCOUNT_IDENTITY','BP_MAPPING_DECISION','CRM_PROJECTION','ACC_MAPPING_AUTHORITY')),
            CHECK (octet_length(public_key)=32)
        );
        CREATE TABLE main_bp.mapping_account_observations (
            tenant_id uuid NOT NULL, observation_id uuid NOT NULL, actor_id uuid NOT NULL, legal_entity_key text COLLATE "C" NOT NULL,
            account_id uuid NOT NULL, account_code text COLLATE "C" NOT NULL, account_status text COLLATE "C" NOT NULL,
            identity_version text COLLATE "C" NOT NULL, identity_hash text COLLATE "C" NOT NULL, body_hash text COLLATE "C" NOT NULL,
            protected_original bytea NOT NULL, protected_trust bytea NOT NULL, authority_hash text COLLATE "C" NOT NULL,
            source_as_of timestamptz NOT NULL, read_at timestamptz NOT NULL, expires_at timestamptz NOT NULL,
            PRIMARY KEY (tenant_id,observation_id),
            UNIQUE (tenant_id,legal_entity_key,account_id,observation_id),
            FOREIGN KEY (tenant_id,legal_entity_key) REFERENCES main_bp.scopes,
            CHECK (account_status IN ('ACTIVE','INACTIVE')), CHECK (expires_at>read_at)
        );
        CREATE TABLE main_bp.mapping_readers (
            tenant_id uuid NOT NULL, legal_entity_key text COLLATE "C" NOT NULL,
            client_id text COLLATE "C" NOT NULL, purpose text COLLATE "C" NOT NULL CHECK (purpose IN ('SNAPSHOT','OWNER_DECISION')),
            enabled boolean NOT NULL DEFAULT false,
            PRIMARY KEY (tenant_id,legal_entity_key,client_id,purpose),
            FOREIGN KEY (tenant_id,legal_entity_key) REFERENCES main_bp.scopes
        );
        CREATE TABLE main_bp.mapping_account_identities (
            tenant_id uuid NOT NULL, account_id uuid NOT NULL, identity_hash text COLLATE "C" NOT NULL,
            PRIMARY KEY (tenant_id,account_id)
        );
        CREATE TABLE main_bp.mapping_slots (
            tenant_id uuid NOT NULL, legal_entity_key text COLLATE "C" NOT NULL, account_id uuid NOT NULL,
            scope_identity uuid NOT NULL UNIQUE, revision bigint CHECK (revision>0), observation_sequence bigint NOT NULL DEFAULT 0,
            generation bigint NOT NULL DEFAULT 1 CHECK (generation>0), owner_conflict boolean NOT NULL DEFAULT false,
            current_projection_id uuid, decision_position bigint CHECK (decision_position>0),
            PRIMARY KEY (tenant_id,legal_entity_key,account_id),
            FOREIGN KEY (tenant_id,legal_entity_key) REFERENCES main_bp.scopes
        );
        CREATE TABLE main_bp.mapping_revisions (
            tenant_id uuid NOT NULL, legal_entity_key text COLLATE "C" NOT NULL, account_id uuid NOT NULL, revision bigint NOT NULL,
            bp_id uuid, business_partner_key text COLLATE "C", semantic_hash text COLLATE "C" NOT NULL,
            identity_observation_id uuid NOT NULL, previous_revision bigint, command_id uuid NOT NULL, recorded_at timestamptz NOT NULL,
            action text COLLATE "C" NOT NULL, resolved_conflicts uuid[] NOT NULL DEFAULT '{}',
            PRIMARY KEY (tenant_id,legal_entity_key,account_id,revision),
            FOREIGN KEY (tenant_id,legal_entity_key,account_id) REFERENCES main_bp.mapping_slots,
            FOREIGN KEY (tenant_id,legal_entity_key,account_id,identity_observation_id)
                REFERENCES main_bp.mapping_account_observations(tenant_id,legal_entity_key,account_id,observation_id),
            FOREIGN KEY (tenant_id,legal_entity_key,account_id,previous_revision) REFERENCES main_bp.mapping_revisions,
            FOREIGN KEY (tenant_id,legal_entity_key,bp_id) REFERENCES main_bp.heads(tenant_id,legal_entity_key,bp_id),
            FOREIGN KEY (tenant_id,command_id) REFERENCES main_bp.commands DEFERRABLE INITIALLY DEFERRED,
            UNIQUE (tenant_id,legal_entity_key,account_id,revision,bp_id),
            CHECK ((bp_id IS NULL)=(business_partner_key IS NULL)),
            CHECK ((revision=1 AND previous_revision IS NULL) OR (revision>1 AND previous_revision=revision-1)),
            CHECK (action IN ('LINK','CREATE_AND_LINK','REPLACE','UNLINK','CORRECT_CURRENT'))
        );
        ALTER TABLE main_bp.mapping_slots ADD FOREIGN KEY (tenant_id,legal_entity_key,account_id,revision)
            REFERENCES main_bp.mapping_revisions;
        CREATE TABLE main_bp.mapping_reverse_claims (
            tenant_id uuid NOT NULL, legal_entity_key text COLLATE "C" NOT NULL, bp_id uuid NOT NULL,
            account_id uuid NOT NULL, revision bigint NOT NULL,
            PRIMARY KEY (tenant_id,legal_entity_key,bp_id), UNIQUE (tenant_id,legal_entity_key,account_id),
            FOREIGN KEY (tenant_id,legal_entity_key,account_id,revision,bp_id)
                REFERENCES main_bp.mapping_revisions(tenant_id,legal_entity_key,account_id,revision,bp_id)
        );
        CREATE TABLE main_bp.mapping_contexts (
            tenant_id uuid NOT NULL, context_id uuid NOT NULL, kind text COLLATE "C" NOT NULL,
            actor_id uuid NOT NULL, legal_entity_key text COLLATE "C" NOT NULL, account_id uuid NOT NULL,
            scope_revision bigint NOT NULL, permission_version text COLLATE "C" NOT NULL,
            content_hash text COLLATE "C" NOT NULL, protected_body bytea NOT NULL,
            created_at timestamptz NOT NULL, expires_at timestamptz NOT NULL,
            PRIMARY KEY (tenant_id,context_id), FOREIGN KEY (tenant_id,legal_entity_key) REFERENCES main_bp.scopes,
            CHECK (kind IN ('CONTEXT','PREVIEW','BINDING','CORRECTION')), CHECK (expires_at>created_at)
        );
        CREATE TABLE main_bp.mapping_consumed_previews (
            tenant_id uuid NOT NULL, preview_id uuid NOT NULL, command_id uuid NOT NULL,
            PRIMARY KEY (tenant_id,preview_id),
            FOREIGN KEY (tenant_id,preview_id) REFERENCES main_bp.mapping_contexts,
            FOREIGN KEY (tenant_id,command_id) REFERENCES main_bp.commands
        );
        CREATE TABLE main_bp.mapping_evidence_guards (
            tenant_id uuid NOT NULL, legal_entity_key text COLLATE "C" NOT NULL, account_id uuid NOT NULL,
            purpose text COLLATE "C" NOT NULL, guard_key text COLLATE "C" NOT NULL, guard_version text COLLATE "C" NOT NULL,
            authority_hash text COLLATE "C" NOT NULL, active boolean NOT NULL, generation bigint NOT NULL DEFAULT 1 CHECK (generation>0),
            PRIMARY KEY (tenant_id,legal_entity_key,account_id,purpose,guard_key),
            FOREIGN KEY (tenant_id,legal_entity_key,purpose) REFERENCES main_bp.mapping_authorities
        );
        CREATE TABLE main_bp.mapping_use_guards (
            tenant_id uuid NOT NULL, legal_entity_key text COLLATE "C" NOT NULL, account_id uuid NOT NULL,
            revision bigint NOT NULL, semantic_hash text COLLATE "C" NOT NULL, account_business_version text COLLATE "C" NOT NULL,
            authority_hash text COLLATE "C" NOT NULL, active boolean NOT NULL,
            PRIMARY KEY (tenant_id,legal_entity_key,account_id),
            FOREIGN KEY (tenant_id,legal_entity_key,account_id,revision) REFERENCES main_bp.mapping_revisions
        );
        CREATE TABLE main_bp.mapping_audit (
            tenant_id uuid NOT NULL, legal_entity_key text COLLATE "C" NOT NULL, account_id uuid NOT NULL,
            command_id uuid NOT NULL, action text COLLATE "C" NOT NULL, revision bigint NOT NULL, actor_id uuid NOT NULL,
            preview_id uuid NOT NULL, recorded_at timestamptz NOT NULL,
            PRIMARY KEY (tenant_id,command_id),
            FOREIGN KEY (tenant_id,command_id) REFERENCES main_bp.commands,
            FOREIGN KEY (tenant_id,legal_entity_key,account_id,revision) REFERENCES main_bp.mapping_revisions,
            FOREIGN KEY (tenant_id,preview_id) REFERENCES main_bp.mapping_contexts
        );
        CREATE TABLE main_bp.mapping_events (
            tenant_id uuid NOT NULL, legal_entity_key text COLLATE "C" NOT NULL, account_id uuid NOT NULL,
            event_id uuid NOT NULL, revision bigint NOT NULL, observation_sequence bigint NOT NULL CHECK (observation_sequence>=0),
            command_id uuid NOT NULL, body bytea NOT NULL, body_hash text COLLATE "C" NOT NULL, source_as_of timestamptz NOT NULL,
            PRIMARY KEY (tenant_id,event_id), UNIQUE (tenant_id,legal_entity_key,account_id,revision,observation_sequence),
            FOREIGN KEY (tenant_id,legal_entity_key,account_id,revision) REFERENCES main_bp.mapping_revisions,
            FOREIGN KEY (tenant_id,command_id) REFERENCES main_bp.commands DEFERRABLE INITIALLY DEFERRED
        );
        CREATE TABLE main_bp.mapping_deliveries (
            tenant_id uuid NOT NULL, event_id uuid NOT NULL,
            state text COLLATE "C" NOT NULL DEFAULT 'READY', attempt integer NOT NULL DEFAULT 0 CHECK (attempt>=0),
            next_attempt_at timestamptz, claim_id uuid, lease_until timestamptz, last_error text COLLATE "C",
            PRIMARY KEY (tenant_id,event_id), FOREIGN KEY (tenant_id,event_id) REFERENCES main_bp.mapping_events,
            CHECK (state IN ('READY','LEASED','SENT_UNKNOWN','ACKED_PROCESSING','DEAD')),
            CHECK ((state='LEASED')=(claim_id IS NOT NULL AND lease_until IS NOT NULL))
        );
        CREATE TABLE main_bp.mapping_delivery_attempts (
            tenant_id uuid NOT NULL, event_id uuid NOT NULL, claim_id uuid NOT NULL, started_at timestamptz NOT NULL,
            response_status integer, response_hash text COLLATE "C", protected_response bytea, finished_at timestamptz,
            PRIMARY KEY (tenant_id,event_id,claim_id), FOREIGN KEY (tenant_id,event_id) REFERENCES main_bp.mapping_deliveries
        );
        CREATE TABLE main_bp.mapping_jobs (
            tenant_id uuid NOT NULL, legal_entity_key text COLLATE "C" NOT NULL, account_id uuid NOT NULL,
            job_id uuid NOT NULL, command_id uuid NOT NULL, actor_id uuid NOT NULL, reason text NOT NULL,
            mode text COLLATE "C" NOT NULL, expected_revision bigint NOT NULL, expected_hash text COLLATE "C" NOT NULL,
            generation bigint, event_id uuid, state text COLLATE "C" NOT NULL DEFAULT 'QUEUED',
            claim_id uuid, lease_until timestamptz, attempt integer NOT NULL DEFAULT 0 CHECK (attempt>=0), next_attempt_at timestamptz,
            observation_id uuid, current_applicable boolean NOT NULL DEFAULT false,
            last_error text COLLATE "C", updated_at timestamptz NOT NULL,
            PRIMARY KEY (tenant_id,job_id),
            FOREIGN KEY (tenant_id,command_id) REFERENCES main_bp.commands DEFERRABLE INITIALLY DEFERRED,
            FOREIGN KEY (tenant_id,legal_entity_key,account_id,expected_revision) REFERENCES main_bp.mapping_revisions,
            FOREIGN KEY (tenant_id,event_id) REFERENCES main_bp.mapping_events,
            CHECK (mode IN ('QUERY_PROJECTION','RETRY_ORIGINAL_EVENT')),
            CHECK (state IN ('QUEUED','RUNNING','WAITING','COMPLETE','FAILED')),
            CHECK ((mode='QUERY_PROJECTION')=(generation IS NOT NULL AND event_id IS NULL)),
            CHECK ((state='RUNNING')=(claim_id IS NOT NULL AND lease_until IS NOT NULL))
        );
        CREATE INDEX mapping_jobs_ready ON main_bp.mapping_jobs(tenant_id,state,updated_at);
        CREATE TABLE main_bp.mapping_projection_reads (
            tenant_id uuid NOT NULL, legal_entity_key text COLLATE "C" NOT NULL, account_id uuid NOT NULL,
            observation_id uuid NOT NULL, job_id uuid NOT NULL, claim_id uuid NOT NULL, generation bigint NOT NULL,
            knowledge text COLLATE "C" NOT NULL, authority_hash text COLLATE "C", read_result bytea NOT NULL, protected_original bytea,
            protected_trust bytea, observed_at timestamptz NOT NULL, current_applicable boolean NOT NULL,
            PRIMARY KEY (tenant_id,observation_id), UNIQUE (tenant_id,job_id,claim_id),
            FOREIGN KEY (tenant_id,job_id) REFERENCES main_bp.mapping_jobs,
            FOREIGN KEY (tenant_id,legal_entity_key,account_id) REFERENCES main_bp.mapping_slots
        );
        ALTER TABLE main_bp.mapping_slots ADD FOREIGN KEY (tenant_id,current_projection_id) REFERENCES main_bp.mapping_projection_reads;
        ALTER TABLE main_bp.mapping_jobs ADD FOREIGN KEY (tenant_id,observation_id) REFERENCES main_bp.mapping_projection_reads;
        CREATE TABLE main_bp.mapping_owner_decisions (
            tenant_id uuid NOT NULL, legal_entity_key text COLLATE "C" NOT NULL, account_id uuid NOT NULL,
            position bigint NOT NULL CHECK (position>0), decision_id uuid NOT NULL, authority_hash text COLLATE "C" NOT NULL,
            mapping_revision bigint NOT NULL, scope_epoch text COLLATE "C" NOT NULL,
            digest text COLLATE "C" NOT NULL, decision_body bytea NOT NULL, proof_body bytea NOT NULL,
            PRIMARY KEY (tenant_id,legal_entity_key,account_id,position), UNIQUE (tenant_id,decision_id),
            FOREIGN KEY (tenant_id,legal_entity_key,account_id,mapping_revision) REFERENCES main_bp.mapping_revisions
        );
        ALTER TABLE main_bp.mapping_slots ADD FOREIGN KEY (tenant_id,legal_entity_key,account_id,decision_position)
            REFERENCES main_bp.mapping_owner_decisions;

        CREATE FUNCTION main_bp.advance_mapping_evidence_generation() RETURNS trigger
        LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
            IF TG_OP='DELETE' THEN
                RAISE EXCEPTION 'BP_MAPPING_EVIDENCE_REVOKE_REQUIRED' USING ERRCODE='P0001';
            END IF;
            IF TG_OP='INSERT' THEN NEW.generation:=1; RETURN NEW; END IF;
            IF (OLD.tenant_id,OLD.legal_entity_key,OLD.account_id,OLD.purpose,OLD.guard_key)
               IS DISTINCT FROM (NEW.tenant_id,NEW.legal_entity_key,NEW.account_id,NEW.purpose,NEW.guard_key) THEN
                RAISE EXCEPTION 'BP_MAPPING_EVIDENCE_IDENTITY_IMMUTABLE' USING ERRCODE='P0001';
            END IF;
            NEW.generation := OLD.generation;
            IF (OLD.guard_version,OLD.authority_hash,OLD.active) IS DISTINCT FROM (NEW.guard_version,NEW.authority_hash,NEW.active) THEN
                NEW.generation:=OLD.generation+1;
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER advance_mapping_evidence_generation BEFORE INSERT OR UPDATE OR DELETE ON main_bp.mapping_evidence_guards
          FOR EACH ROW EXECUTE FUNCTION main_bp.advance_mapping_evidence_generation();

        CREATE FUNCTION main_bp.require_mapping_scope_writer() RETURNS trigger
        LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
            IF TG_OP <> 'INSERT' AND NOT main_bp.use_scope_held(OLD.tenant_id,OLD.legal_entity_key,true) THEN
                RAISE EXCEPTION 'BP_MAPPING_SCOPE_X_REQUIRED' USING ERRCODE='P0001';
            END IF;
            IF TG_OP <> 'DELETE' AND NOT main_bp.use_scope_held(NEW.tenant_id,NEW.legal_entity_key,true) THEN
                RAISE EXCEPTION 'BP_MAPPING_SCOPE_X_REQUIRED' USING ERRCODE='P0001';
            END IF;
            IF TG_OP='DELETE' THEN RETURN OLD; END IF;
            RETURN NEW;
        END $$;

        DO $$
        DECLARE item text;
        BEGIN
            FOREACH item IN ARRAY ARRAY['mapping_authorities','mapping_readers','mapping_slots','mapping_revisions','mapping_reverse_claims','mapping_evidence_guards',
                'mapping_use_guards','mapping_audit','mapping_events','mapping_owner_decisions'] LOOP
                EXECUTE format('CREATE TRIGGER require_mapping_scope_writer BEFORE INSERT OR UPDATE OR DELETE ON main_bp.%I
                    FOR EACH ROW EXECUTE FUNCTION main_bp.require_mapping_scope_writer()',item);
            END LOOP;
            FOREACH item IN ARRAY ARRAY['mapping_account_identities','mapping_account_observations','mapping_revisions','mapping_contexts','mapping_consumed_previews',
                'mapping_audit','mapping_events','mapping_projection_reads','mapping_owner_decisions'] LOOP
                EXECUTE format('CREATE TRIGGER preserve_mapping_original BEFORE UPDATE OR DELETE ON main_bp.%I
                    FOR EACH ROW EXECUTE FUNCTION main_bp.protect_history()',item);
            END LOOP;
        END $$;
        """);

    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("已提交的伙伴映射及原始决定必须保留，迁移只能前向修复。");
}
