using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)), Migration("20261028030000_ToolActualUsageV1")]
public sealed class ToolActualUsageV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE SCHEMA cp6_tool_actual_v1;
        REVOKE ALL ON SCHEMA cp6_tool_actual_v1 FROM PUBLIC;
        CREATE TABLE cp6_tool_actual_v1.environment(singleton boolean PRIMARY KEY CHECK(singleton),environment_id uuid NOT NULL,
          mode text NOT NULL CHECK(mode IN ('PRODUCTION','SIMULATION')));
        CREATE TABLE cp6_tool_actual_v1.documents(tenant_id uuid NOT NULL,site_id uuid NOT NULL,document_key text COLLATE "C" NOT NULL,
          document_id uuid NOT NULL,PRIMARY KEY(tenant_id,site_id,document_key),UNIQUE(tenant_id,document_id));
        CREATE TABLE cp6_tool_actual_v1.facts(tenant_id uuid NOT NULL,tool_id uuid NOT NULL,record_id uuid NOT NULL,site_id uuid NOT NULL,
          sequence bigint GENERATED ALWAYS AS IDENTITY,kind text NOT NULL CHECK(kind IN ('EVIDENCE','OCCURRENCE','OBSERVATION','POLICY','RECOGNITION','USE','APPLICATION')),
          subject_id uuid,identity_key text COLLATE "C" NOT NULL CHECK(identity_key ~ '^[0-9a-f]{64}$'),revision bigint NOT NULL CHECK(revision>0),
          body_json text NOT NULL CHECK(octet_length(body_json) BETWEEN 2 AND 131072),body_digest text COLLATE "C" NOT NULL,
          CHECK(body_digest=encode(sha256(convert_to(body_json,'UTF8')),'hex')),
          PRIMARY KEY(tenant_id,record_id),UNIQUE(tenant_id,tool_id,sequence),
          FOREIGN KEY(tenant_id,tool_id) REFERENCES cp6_tool_instance_v1.registrations(tenant_id,object_id));
        CREATE UNIQUE INDEX tool_actual_evidence_revision ON cp6_tool_actual_v1.facts(tenant_id,identity_key,revision) WHERE kind='EVIDENCE';
        CREATE UNIQUE INDEX tool_actual_occurrence_once ON cp6_tool_actual_v1.facts(tenant_id,identity_key,kind) WHERE kind IN ('OCCURRENCE','OBSERVATION','USE');
        CREATE UNIQUE INDEX tool_actual_application_once ON cp6_tool_actual_v1.facts(tenant_id,subject_id) WHERE kind='APPLICATION';
        CREATE FUNCTION cp6_tool_actual_v1.immutable() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN RAISE EXCEPTION 'TOOL_ACTUAL_IMMUTABLE' USING ERRCODE='23514'; END $$;
        CREATE FUNCTION cp6_tool_actual_v1.validate() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE b jsonb=NEW.body_json::jsonb; prior jsonb; before_body jsonb; after_body jsonb; u jsonb;
        BEGIN
          IF b#>>'{scope,tenantId}' IS DISTINCT FROM NEW.tenant_id::text OR b#>>'{scope,siteId}' IS DISTINCT FROM NEW.site_id::text
            OR b->>'toolId' IS DISTINCT FROM NEW.tool_id::text OR b->>'recordId' IS DISTINCT FROM NEW.record_id::text
            OR b->>'kind' IS DISTINCT FROM NEW.kind OR b->>'subjectId' IS DISTINCT FROM NEW.subject_id::text
            OR b->>'identityKey' IS DISTINCT FROM NEW.identity_key OR b->>'revision' IS DISTINCT FROM NEW.revision::text
            OR jsonb_array_length(b->'authorities') IS NULL OR jsonb_array_length(b->'authorities')<1
            OR b->>'actorId' IS NULL OR b->>'recordedAt' IS NULL
            OR NOT EXISTS(SELECT FROM cp6_tool_actual_v1.environment e WHERE e.environment_id::text=b#>>'{scope,environmentId}' AND e.mode=b#>>'{scope,mode}') THEN
            RAISE EXCEPTION 'TOOL_ACTUAL_ORIGINAL_INVALID' USING ERRCODE='23514'; END IF;
          SELECT source_json::jsonb INTO before_body FROM cp6_tool_instance_v1.versions WHERE tenant_id=NEW.tenant_id AND object_id=NEW.tool_id
            AND revision::text=b#>>'{nativeBefore,version}' AND source_digest=b#>>'{nativeBefore,digest}' AND source_present;
          SELECT source_json::jsonb INTO after_body FROM cp6_tool_instance_v1.versions WHERE tenant_id=NEW.tenant_id AND object_id=NEW.tool_id
            AND revision::text=b#>>'{nativeAfter,version}' AND source_digest=b#>>'{nativeAfter,digest}' AND source_present;
          IF before_body IS NULL OR after_body IS NULL OR NOT EXISTS(SELECT FROM cp6_tool_instance_v1.heads h WHERE h.tenant_id=NEW.tenant_id AND h.object_id=NEW.tool_id
            AND h.revision::text=b#>>'{nativeAfter,version}') OR b#>>'{nativeBefore,id}' IS DISTINCT FROM NEW.tool_id::text
            OR b#>>'{nativeAfter,id}' IS DISTINCT FROM NEW.tool_id::text OR b#>>'{nativeBefore,owner}' IS DISTINCT FROM 'Main.Wms.ToolInstance.v1'
            OR b#>>'{nativeAfter,owner}' IS DISTINCT FROM 'Main.Wms.ToolInstance.v1' THEN
            RAISE EXCEPTION 'TOOL_ACTUAL_SOURCE_INVALID' USING ERRCODE='23514'; END IF;
          IF NEW.kind<>'USE' AND b->'nativeBefore' IS DISTINCT FROM b->'nativeAfter' THEN RAISE EXCEPTION 'TOOL_ACTUAL_UNEXPECTED_NATIVE_EFFECT' USING ERRCODE='23514'; END IF;
          IF NEW.kind='EVIDENCE' THEN
            SELECT body_json::jsonb INTO prior FROM cp6_tool_actual_v1.facts WHERE tenant_id=NEW.tenant_id AND kind='EVIDENCE' AND identity_key=NEW.identity_key ORDER BY revision DESC LIMIT 1;
            IF prior IS NULL AND NEW.revision<>1 OR prior IS NOT NULL AND (NEW.revision<>(prior->>'revision')::bigint+1
              OR (b#>'{body,documentId}',b#>'{body,lineKey}',b#>'{body,eventOrdinal}',b#>'{body,shots}',b#>'{body,occurredAt}',b->'toolId') IS DISTINCT FROM
                 (prior#>'{body,documentId}',prior#>'{body,lineKey}',prior#>'{body,eventOrdinal}',prior#>'{body,shots}',prior#>'{body,occurredAt}',prior->'toolId')) THEN
              RAISE EXCEPTION 'TOOL_ACTUAL_EVIDENCE_CONFLICT' USING ERRCODE='23514'; END IF;
          ELSIF NEW.kind='USE' THEN
            IF (b#>>'{body,shots}')::bigint<=0 OR b#>>'{body,outcome}' IS DISTINCT FROM 'APPLIED_ACTUAL_WITH_RESTRICTIONS'
              OR NOT EXISTS(SELECT FROM cp6_tool_actual_v1.facts d WHERE d.tenant_id=NEW.tenant_id AND d.tool_id=NEW.tool_id AND d.kind='RECOGNITION'
                AND d.record_id::text=b#>>'{body,decision,id}' AND d.body_digest=b#>>'{body,decision,digest}' AND d.body_json::jsonb#>>'{body,outcome}'='ACCEPT_FACT'
                AND d.body_json::jsonb#>'{body,observation}'=b#>'{body,observation}' AND d.body_json::jsonb#>'{body,targetCycle}'=b#>'{body,targetCycle}'
                AND d.body_json::jsonb#>'{body,identity}'=b#>'{body,identity}' AND d.body_json::jsonb#>'{body,shots}'=b#>'{body,shots}')
              OR NOT EXISTS(SELECT FROM cp6_tool_actual_v1.facts o WHERE o.tenant_id=NEW.tenant_id AND o.tool_id=NEW.tool_id AND o.kind='OBSERVATION'
                AND o.record_id::text=b#>>'{body,observation,id}' AND o.body_digest=b#>>'{body,observation,digest}' AND o.identity_key=NEW.identity_key)
              OR (b#>>'{body,nextTargetShots}')::bigint<>(b#>>'{body,previousTargetShots}')::bigint+(b#>>'{body,shots}')::bigint THEN
              RAISE EXCEPTION 'TOOL_ACTUAL_USE_INVALID' USING ERRCODE='23514'; END IF;
            IF b#>>'{body,targetCycle,id}'=b#>>'{body,currentCycleBefore,id}' THEN
              IF (after_body->>'usedShots')::bigint<>(before_body->>'usedShots')::bigint+(b#>>'{body,shots}')::bigint THEN
                RAISE EXCEPTION 'TOOL_ACTUAL_COUNTER_INVALID' USING ERRCODE='23514'; END IF;
            ELSIF b->'nativeBefore' IS DISTINCT FROM b->'nativeAfter' THEN RAISE EXCEPTION 'TOOL_ACTUAL_CLOSED_CYCLE_NATIVE_EFFECT' USING ERRCODE='23514'; END IF;
          ELSIF NEW.kind='APPLICATION' THEN
            SELECT body_json::jsonb INTO u FROM cp6_tool_actual_v1.facts WHERE tenant_id=NEW.tenant_id AND tool_id=NEW.tool_id AND kind='USE'
              AND record_id::text=b#>>'{body,use,id}' AND body_digest=b#>>'{body,use,digest}';
            IF u IS NULL OR u#>'{body,observation}' IS DISTINCT FROM b#>'{body,observation}' OR b#>>'{body,observation,id}' IS DISTINCT FROM NEW.subject_id::text
              OR b#>>'{body,isUsePermit}' IS DISTINCT FROM 'false' THEN RAISE EXCEPTION 'TOOL_ACTUAL_APPLICATION_INVALID' USING ERRCODE='23514'; END IF;
          END IF; RETURN NEW;
        END $$;
        CREATE TRIGGER tool_actual_environment_immutable BEFORE UPDATE OR DELETE ON cp6_tool_actual_v1.environment FOR EACH ROW EXECUTE FUNCTION cp6_tool_actual_v1.immutable();
        CREATE TRIGGER tool_actual_environment_truncate BEFORE TRUNCATE ON cp6_tool_actual_v1.environment FOR EACH STATEMENT EXECUTE FUNCTION cp6_tool_actual_v1.immutable();
        CREATE TRIGGER tool_actual_documents_immutable BEFORE UPDATE OR DELETE ON cp6_tool_actual_v1.documents FOR EACH ROW EXECUTE FUNCTION cp6_tool_actual_v1.immutable();
        CREATE TRIGGER tool_actual_documents_truncate BEFORE TRUNCATE ON cp6_tool_actual_v1.documents FOR EACH STATEMENT EXECUTE FUNCTION cp6_tool_actual_v1.immutable();
        CREATE TRIGGER tool_actual_facts_immutable BEFORE UPDATE OR DELETE ON cp6_tool_actual_v1.facts FOR EACH ROW EXECUTE FUNCTION cp6_tool_actual_v1.immutable();
        CREATE TRIGGER tool_actual_facts_truncate BEFORE TRUNCATE ON cp6_tool_actual_v1.facts FOR EACH STATEMENT EXECUTE FUNCTION cp6_tool_actual_v1.immutable();
        CREATE TRIGGER tool_actual_validate BEFORE INSERT ON cp6_tool_actual_v1.facts FOR EACH ROW EXECUTE FUNCTION cp6_tool_actual_v1.validate();
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_tool_actual_v1 FROM PUBLIC;
        REVOKE ALL ON ALL SEQUENCES IN SCHEMA cp6_tool_actual_v1 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_tool_actual_v1 FROM PUBLIC;
        """);
    protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException("Actual-use originals are retained; forward-only migration.");
}
