using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)), Migration("20261028020000_ToolMaintenanceV1")]
public sealed class ToolMaintenanceV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE SCHEMA cp6_tool_maintenance_v1;
        REVOKE ALL ON SCHEMA cp6_tool_maintenance_v1 FROM PUBLIC;
        CREATE TABLE cp6_tool_maintenance_v1.facts(
          tenant_id uuid NOT NULL,tool_id uuid NOT NULL,record_id uuid NOT NULL,sequence bigint GENERATED ALWAYS AS IDENTITY,
          kind text NOT NULL CHECK(kind IN ('POLICY','START','REPORT','COMPLETE','ASSESSMENT','RESET')),maintenance_id uuid,
          body_json text NOT NULL CHECK(octet_length(body_json) BETWEEN 2 AND 65536),
          body_digest text COLLATE "C" NOT NULL CHECK(body_digest=encode(sha256(convert_to(body_json,'UTF8')),'hex')),
          PRIMARY KEY(tenant_id,record_id),UNIQUE(tenant_id,tool_id,sequence),UNIQUE(tenant_id,tool_id,record_id),
          FOREIGN KEY(tenant_id,tool_id) REFERENCES cp6_tool_instance_v1.registrations(tenant_id,object_id),
          CHECK((kind='POLICY')=(maintenance_id IS NULL)));
        CREATE UNIQUE INDEX one_tool_maintenance_fact ON cp6_tool_maintenance_v1.facts(tenant_id,maintenance_id,kind) WHERE kind IN ('START','COMPLETE','RESET');
        CREATE TABLE cp6_tool_maintenance_v1.open_maintenance(
          tenant_id uuid NOT NULL,tool_id uuid NOT NULL,maintenance_id uuid NOT NULL,start_id uuid NOT NULL,
          PRIMARY KEY(tenant_id,tool_id),UNIQUE(tenant_id,maintenance_id),
          FOREIGN KEY(tenant_id,tool_id,start_id) REFERENCES cp6_tool_maintenance_v1.facts(tenant_id,tool_id,record_id));
        CREATE FUNCTION cp6_tool_maintenance_v1.immutable() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN RAISE EXCEPTION 'TOOL_MAINTENANCE_ORIGINAL_IMMUTABLE' USING ERRCODE='23514'; END $$;
        CREATE FUNCTION cp6_tool_maintenance_v1.open_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP='INSERT' THEN
            IF NOT EXISTS(SELECT FROM cp6_tool_maintenance_v1.facts f WHERE f.tenant_id=NEW.tenant_id AND f.tool_id=NEW.tool_id
              AND f.record_id=NEW.start_id AND f.maintenance_id=NEW.maintenance_id AND f.kind='START') THEN
              RAISE EXCEPTION 'TOOL_MAINTENANCE_OPEN_INVALID' USING ERRCODE='23514'; END IF; RETURN NEW;
          END IF;
          IF NOT EXISTS(SELECT FROM cp6_tool_maintenance_v1.facts f WHERE f.tenant_id=OLD.tenant_id AND f.tool_id=OLD.tool_id
            AND f.maintenance_id=OLD.maintenance_id AND f.kind='COMPLETE') THEN
            RAISE EXCEPTION 'TOOL_MAINTENANCE_OPEN_INVALID' USING ERRCODE='23514'; END IF; RETURN OLD;
        END $$;
        CREATE FUNCTION cp6_tool_maintenance_v1.validate() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE b jsonb=NEW.body_json::jsonb; before_body jsonb; after_body jsonb;
        BEGIN
          IF b->>'tenantId' IS DISTINCT FROM NEW.tenant_id::text OR b->>'toolId' IS DISTINCT FROM NEW.tool_id::text
            OR b->>'recordId' IS DISTINCT FROM NEW.record_id::text OR b->>'kind' IS DISTINCT FROM NEW.kind
            OR b->>'maintenanceId' IS DISTINCT FROM NEW.maintenance_id::text
            OR b#>>'{nativeBefore,id}' IS DISTINCT FROM NEW.tool_id::text OR b#>>'{nativeAfter,id}' IS DISTINCT FROM NEW.tool_id::text
            OR b#>>'{nativeBefore,owner}' IS DISTINCT FROM 'Main.Wms.ToolInstance.v1'
            OR b#>>'{nativeAfter,owner}' IS DISTINCT FROM 'Main.Wms.ToolInstance.v1'
            OR jsonb_array_length(b->'authorities')<1 OR b->>'actorId' IS NULL OR b->>'recordedAt' IS NULL THEN
            RAISE EXCEPTION 'TOOL_MAINTENANCE_ORIGINAL_INVALID' USING ERRCODE='23514'; END IF;
          SELECT source_json::jsonb INTO before_body FROM cp6_tool_instance_v1.versions WHERE tenant_id=NEW.tenant_id AND object_id=NEW.tool_id
            AND revision::text=b#>>'{nativeBefore,version}' AND source_digest=b#>>'{nativeBefore,digest}' AND source_present;
          SELECT source_json::jsonb INTO after_body FROM cp6_tool_instance_v1.versions WHERE tenant_id=NEW.tenant_id AND object_id=NEW.tool_id
            AND revision::text=b#>>'{nativeAfter,version}' AND source_digest=b#>>'{nativeAfter,digest}' AND source_present;
          IF before_body IS NULL OR after_body IS NULL OR NOT EXISTS(SELECT FROM cp6_tool_instance_v1.heads h WHERE h.tenant_id=NEW.tenant_id
            AND h.object_id=NEW.tool_id AND h.revision::text=b#>>'{nativeAfter,version}') THEN
            RAISE EXCEPTION 'TOOL_MAINTENANCE_SOURCE_INVALID' USING ERRCODE='23514'; END IF;
          IF NEW.kind NOT IN ('POLICY','START') AND NOT EXISTS(SELECT FROM cp6_tool_maintenance_v1.facts f
            WHERE f.tenant_id=NEW.tenant_id AND f.tool_id=NEW.tool_id AND f.maintenance_id=NEW.maintenance_id AND kind='START') THEN
            RAISE EXCEPTION 'TOOL_MAINTENANCE_START_REQUIRED' USING ERRCODE='23514'; END IF;
          IF NEW.kind='START' THEN
            IF after_body->>'status' IS DISTINCT FROM '1' OR before_body->'usedShots' IS DISTINCT FROM after_body->'usedShots'
              OR before_body->'lastUsedAt' IS DISTINCT FROM after_body->'lastUsedAt'
              OR EXISTS(SELECT FROM cp6_tool_maintenance_v1.facts c WHERE c.tenant_id=NEW.tenant_id AND c.tool_id=NEW.tool_id
                AND c.kind='COMPLETE' AND c.body_json::jsonb#>>'{body,outcome}'='PASS' AND NOT EXISTS(
                  SELECT FROM cp6_tool_maintenance_v1.facts r WHERE r.tenant_id=c.tenant_id AND r.maintenance_id=c.maintenance_id AND r.kind='RESET')) THEN
              RAISE EXCEPTION 'TOOL_MAINTENANCE_START_INVALID' USING ERRCODE='23514'; END IF;
            INSERT INTO cp6_tool_maintenance_v1.open_maintenance VALUES(NEW.tenant_id,NEW.tool_id,NEW.maintenance_id,NEW.record_id);
          ELSIF NEW.kind='COMPLETE' THEN
            IF b#>>'{body,outcome}' IS NULL OR b#>>'{body,outcome}' NOT IN ('PASS','REPAIR_REQUIRED','UNSERVICEABLE')
              OR NOT EXISTS(SELECT FROM cp6_tool_maintenance_v1.open_maintenance WHERE tenant_id=NEW.tenant_id AND tool_id=NEW.tool_id AND maintenance_id=NEW.maintenance_id) THEN
              RAISE EXCEPTION 'TOOL_MAINTENANCE_COMPLETION_INVALID' USING ERRCODE='23514'; END IF;
            DELETE FROM cp6_tool_maintenance_v1.open_maintenance WHERE tenant_id=NEW.tenant_id AND tool_id=NEW.tool_id AND maintenance_id=NEW.maintenance_id;
          ELSIF NEW.kind='RESET' THEN
            IF before_body->>'status' IS DISTINCT FROM '1' OR after_body->>'status' IS DISTINCT FROM '0' OR after_body->>'usedShots' IS DISTINCT FROM '0'
              OR before_body->'lastUsedAt' IS DISTINCT FROM after_body->'lastUsedAt' OR b#>>'{cycleAfter,number}' IS NULL
              OR (b#>>'{cycleAfter,number}')::bigint<>(b#>>'{body,previousCycle,number}')::bigint+1
              OR b#>>'{cycleAfter,shots}' IS DISTINCT FROM '0'
              OR b#>>'{cycleAfter,lifetimeKnowledge}' IS DISTINCT FROM b#>>'{body,previousCycle,lifetimeKnowledge}'
              OR b#>>'{cycleAfter,lifetimeShots}' IS DISTINCT FROM b#>>'{body,previousCycle,lifetimeShots}'
              OR NOT EXISTS(SELECT FROM cp6_tool_maintenance_v1.facts c WHERE c.tenant_id=NEW.tenant_id AND c.tool_id=NEW.tool_id AND c.maintenance_id=NEW.maintenance_id
                AND c.kind='COMPLETE' AND c.body_json::jsonb#>>'{body,outcome}'='PASS' AND c.record_id::text=b#>>'{body,completion,id}' AND c.body_digest=b#>>'{body,completion,digest}')
              OR NOT EXISTS(SELECT FROM cp6_tool_maintenance_v1.facts q WHERE q.tenant_id=NEW.tenant_id AND q.tool_id=NEW.tool_id AND q.maintenance_id=NEW.maintenance_id
                AND q.kind='ASSESSMENT' AND q.record_id::text=b#>>'{body,qualification,id}' AND q.body_digest=b#>>'{body,qualification,digest}'
                AND q.body_json::jsonb#>>'{body,decision}'='ELIGIBLE' AND q.body_json::jsonb#>'{body,completionRef}'=b#>'{body,completion}'
                AND q.body_json::jsonb#>'{body,policy}'=b#>'{body,policy}') THEN
              RAISE EXCEPTION 'TOOL_MAINTENANCE_RESET_INVALID' USING ERRCODE='23514'; END IF;
          ELSIF b->'nativeBefore' IS DISTINCT FROM b->'nativeAfter' THEN
            RAISE EXCEPTION 'TOOL_MAINTENANCE_UNEXPECTED_NATIVE_EFFECT' USING ERRCODE='23514';
          END IF; RETURN NEW;
        END $$;
        CREATE TRIGGER tool_maintenance_immutable BEFORE UPDATE OR DELETE ON cp6_tool_maintenance_v1.facts FOR EACH ROW EXECUTE FUNCTION cp6_tool_maintenance_v1.immutable();
        CREATE TRIGGER tool_maintenance_no_truncate BEFORE TRUNCATE ON cp6_tool_maintenance_v1.facts FOR EACH STATEMENT EXECUTE FUNCTION cp6_tool_maintenance_v1.immutable();
        CREATE TRIGGER tool_maintenance_validate AFTER INSERT ON cp6_tool_maintenance_v1.facts FOR EACH ROW EXECUTE FUNCTION cp6_tool_maintenance_v1.validate();
        CREATE TRIGGER tool_maintenance_open_insert BEFORE INSERT ON cp6_tool_maintenance_v1.open_maintenance FOR EACH ROW EXECUTE FUNCTION cp6_tool_maintenance_v1.open_guard();
        CREATE TRIGGER tool_maintenance_open_delete BEFORE DELETE ON cp6_tool_maintenance_v1.open_maintenance FOR EACH ROW EXECUTE FUNCTION cp6_tool_maintenance_v1.open_guard();
        CREATE TRIGGER tool_maintenance_open_update BEFORE UPDATE OR TRUNCATE ON cp6_tool_maintenance_v1.open_maintenance FOR EACH STATEMENT EXECUTE FUNCTION cp6_tool_maintenance_v1.immutable();
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_tool_maintenance_v1 FROM PUBLIC;
        REVOKE ALL ON ALL SEQUENCES IN SCHEMA cp6_tool_maintenance_v1 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_tool_maintenance_v1 FROM PUBLIC;
        """);
    protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException("Maintenance originals are retained; forward-only migration.");
}
