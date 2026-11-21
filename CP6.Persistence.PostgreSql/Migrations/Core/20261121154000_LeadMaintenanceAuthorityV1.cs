using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261121154000_LeadMaintenanceAuthorityV1")]
public sealed class LeadMaintenanceAuthorityV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE SCHEMA cp6_lead_maintenance_v1;
        CREATE TABLE cp6_lead_maintenance_v1.scope_grants (
            tenant_id uuid NOT NULL,
            target_kind text COLLATE "C" NOT NULL CHECK (target_kind IN ('INDEPENDENT_INBOUND','MANUAL_LEAD')),
            object_id uuid NOT NULL,
            object_authority text COLLATE "C" NOT NULL,
            principal_id uuid NOT NULL,
            authentication_epoch uuid NOT NULL,
            purpose text COLLATE "C" NOT NULL CHECK (purpose IN ('privacy:policy','privacy:hold','privacy:maintain')),
            version text COLLATE "C" NOT NULL CHECK (length(version) BETWEEN 1 AND 128),
            valid_from_utc timestamptz NOT NULL,
            valid_until_utc timestamptz NOT NULL,
            enabled boolean NOT NULL,
            PRIMARY KEY (tenant_id,target_kind,object_id,object_authority,principal_id,authentication_epoch,purpose),
            CHECK (isfinite(valid_from_utc) AND isfinite(valid_until_utc) AND valid_until_utc > valid_from_utc)
        );

        CREATE TABLE cp6_lead_maintenance_v1.evidence (
            tenant_id uuid NOT NULL,
            target_kind text COLLATE "C" NOT NULL CHECK (target_kind IN ('INDEPENDENT_INBOUND','MANUAL_LEAD')),
            object_id uuid NOT NULL,
            component text COLLATE "C" NOT NULL CHECK (component IN ('SOURCE','POLICY','HOLD')),
            object_authority text COLLATE "C",
            source_message_id text COLLATE "C",
            sequence bigint NOT NULL CHECK (sequence > 0),
            payload bytea NOT NULL CHECK (octet_length(payload) BETWEEN 1 AND 16384),
            signature bytea NOT NULL CHECK (octet_length(signature) = 64),
            PRIMARY KEY (tenant_id,target_kind,object_id,component),
            CHECK ((component = 'SOURCE' AND object_authority IS NOT NULL
                    AND ((target_kind = 'INDEPENDENT_INBOUND' AND source_message_id IS NOT NULL)
                         OR (target_kind = 'MANUAL_LEAD' AND source_message_id IS NULL)))
                   OR (component <> 'SOURCE' AND object_authority IS NULL AND source_message_id IS NULL)),
            CHECK (length(object_authority) BETWEEN 1 AND 128 AND length(source_message_id) BETWEEN 1 AND 128)
        );
        CREATE UNIQUE INDEX inbound_source_message_slot ON cp6_lead_maintenance_v1.evidence
            (tenant_id,object_authority,source_message_id) WHERE target_kind='INDEPENDENT_INBOUND' AND component='SOURCE';

        -- 同一对象的当前授权与证据发布在同一把事务锁下读取。
        CREATE FUNCTION cp6_lead_maintenance_v1.lock_target(tenant uuid, kind text, target uuid)
        RETURNS boolean LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
            PERFORM pg_advisory_xact_lock(hashtextextended(
                'cp6-lead-maintenance-v1/' || tenant::text || '/' || kind || '/' || target::text, 0));
            RETURN true;
        END
        $$;

        CREATE FUNCTION cp6_lead_maintenance_v1.lock_grant_write()
        RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
            IF TG_OP = 'UPDATE' AND ROW(OLD.tenant_id,OLD.target_kind,OLD.object_id)
                IS DISTINCT FROM ROW(NEW.tenant_id,NEW.target_kind,NEW.object_id) THEN
                RAISE EXCEPTION 'LEAD_MAINTENANCE_GRANT_TARGET_IMMUTABLE' USING ERRCODE='23514';
            END IF;
            IF TG_OP = 'DELETE' THEN
                PERFORM cp6_lead_maintenance_v1.lock_target(OLD.tenant_id,OLD.target_kind,OLD.object_id);
                RETURN OLD;
            END IF;
            PERFORM cp6_lead_maintenance_v1.lock_target(NEW.tenant_id,NEW.target_kind,NEW.object_id);
            RETURN NEW;
        END
        $$;
        CREATE TRIGGER lock_grant_write BEFORE INSERT OR UPDATE OR DELETE ON cp6_lead_maintenance_v1.scope_grants
            FOR EACH ROW EXECUTE FUNCTION cp6_lead_maintenance_v1.lock_grant_write();

        CREATE FUNCTION cp6_lead_maintenance_v1.fence_evidence_write()
        RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
            IF TG_OP = 'DELETE' THEN
                RAISE EXCEPTION 'LEAD_MAINTENANCE_EVIDENCE_DELETE_FORBIDDEN' USING ERRCODE='23514';
            END IF;
            IF TG_OP = 'UPDATE' AND (
                ROW(OLD.tenant_id,OLD.target_kind,OLD.object_id,OLD.component)
                    IS DISTINCT FROM ROW(NEW.tenant_id,NEW.target_kind,NEW.object_id,NEW.component)
                OR ROW(OLD.object_authority,OLD.source_message_id) IS DISTINCT FROM ROW(NEW.object_authority,NEW.source_message_id)
                OR NEW.sequence < OLD.sequence
                OR (NEW.sequence = OLD.sequence AND NEW.payload IS DISTINCT FROM OLD.payload)
                OR ((OLD.component = 'SOURCE' OR (OLD.component = 'POLICY' AND OLD.target_kind = 'INDEPENDENT_INBOUND'))
                    AND NEW.sequence <> OLD.sequence)
            ) THEN
                RAISE EXCEPTION 'LEAD_MAINTENANCE_EVIDENCE_REWRITE_FORBIDDEN' USING ERRCODE='23514';
            END IF;
            PERFORM cp6_lead_maintenance_v1.lock_target(NEW.tenant_id,NEW.target_kind,NEW.object_id);
            RETURN NEW;
        END
        $$;
        CREATE TRIGGER fence_evidence_write BEFORE INSERT OR UPDATE OR DELETE ON cp6_lead_maintenance_v1.evidence
            FOR EACH ROW EXECUTE FUNCTION cp6_lead_maintenance_v1.fence_evidence_write();

        REVOKE ALL ON SCHEMA cp6_lead_maintenance_v1 FROM PUBLIC;
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_lead_maintenance_v1 FROM PUBLIC;
        """);

    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("Lead 维护授权证据只支持前向迁移。");
}
