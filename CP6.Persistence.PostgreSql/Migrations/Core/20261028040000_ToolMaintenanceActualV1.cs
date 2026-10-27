using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)), Migration("20261028040000_ToolMaintenanceActualV1")]
public sealed class ToolMaintenanceActualV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE cp6_tool_actual_v1.facts DROP CONSTRAINT facts_kind_check;
        ALTER TABLE cp6_tool_actual_v1.facts ADD CONSTRAINT facts_kind_check CHECK(kind IN
          ('EVIDENCE','OCCURRENCE','OBSERVATION','POLICY','RECOGNITION','USE','APPLICATION',
           'M_OCCURRENCE','M_OBSERVATION','M_POLICY','M_RECOGNITION','M_APPLICATION'));
        CREATE UNIQUE INDEX tool_actual_maintenance_occurrence_once ON cp6_tool_actual_v1.facts(tenant_id,identity_key,kind)
          WHERE kind IN ('M_OCCURRENCE','M_OBSERVATION');
        CREATE UNIQUE INDEX tool_actual_maintenance_application_once ON cp6_tool_actual_v1.facts(tenant_id,subject_id)
          WHERE kind='M_APPLICATION';
        CREATE FUNCTION cp6_tool_actual_v1.validate_maintenance() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE b jsonb=NEW.body_json::jsonb; r jsonb; o jsonb; d jsonb; c jsonb;
        BEGIN
          IF NEW.kind='M_OCCURRENCE' THEN
            SELECT body_json::jsonb INTO r FROM cp6_tool_maintenance_v1.facts WHERE tenant_id=NEW.tenant_id AND tool_id=NEW.tool_id
              AND kind='REPORT' AND record_id::text=b#>>'{body,evidenceRef,id}' AND body_digest=b#>>'{body,evidenceRef,digest}'
              AND maintenance_id::text=b#>>'{body,identity,sourceDocumentId}';
            IF r IS NULL OR b#>>'{body,evidenceRef,owner}' IS DISTINCT FROM 'Main.Wms.ToolMaintenanceReport.v1'
              OR b#>>'{body,evidenceRef,version}' IS DISTINCT FROM '1' OR b#>>'{body,evidenceLineId}' IS DISTINCT FROM 'RESULT'
              OR b#>>'{body,eventOrdinal}' IS DISTINCT FROM '1' OR b#>'{body,outcome}' IS DISTINCT FROM r#>'{body,outcome}'
              OR (b#>>'{body,occurredAt}')::timestamptz IS DISTINCT FROM (r#>>'{body,occurredAt}')::timestamptz
              OR b#>>'{body,identity,sourceKind}' IS DISTINCT FROM 'WMS_TOOL_MAINTENANCE'
              OR b#>>'{body,identity,action}' IS DISTINCT FROM 'MAINTENANCE_RESULT'
              OR NEW.subject_id::text IS DISTINCT FROM b#>>'{body,identity,sourceDocumentId}' THEN
              RAISE EXCEPTION 'TOOL_MAINTENANCE_OCCURRENCE_INVALID' USING ERRCODE='23514'; END IF;
          ELSIF NEW.kind='M_OBSERVATION' THEN
            SELECT body_json::jsonb INTO o FROM cp6_tool_actual_v1.facts WHERE tenant_id=NEW.tenant_id AND tool_id=NEW.tool_id AND kind='M_OCCURRENCE'
              AND record_id=NEW.subject_id AND record_id::text=b#>>'{body,occurrenceRef,id}' AND body_digest=b#>>'{body,occurrenceRef,digest}' AND identity_key=NEW.identity_key;
            IF o IS NULL OR b#>>'{body,kind}' IS DISTINCT FROM 'MAINTENANCE_RESULT_ACTUAL'
              OR b#>'{body,identity}' IS DISTINCT FROM o#>'{body,identity}' OR b#>'{body,evidenceRef}' IS DISTINCT FROM o#>'{body,evidenceRef}'
              OR b#>'{body,outcome}' IS DISTINCT FROM o#>'{body,outcome}'
              OR (b#>>'{body,occurredAt}')::timestamptz IS DISTINCT FROM (o#>>'{body,occurredAt}')::timestamptz THEN
              RAISE EXCEPTION 'TOOL_MAINTENANCE_OBSERVATION_INVALID' USING ERRCODE='23514'; END IF;
          ELSIF NEW.kind='M_RECOGNITION' THEN
            SELECT body_json::jsonb INTO o FROM cp6_tool_actual_v1.facts WHERE tenant_id=NEW.tenant_id AND tool_id=NEW.tool_id AND kind='M_OBSERVATION'
              AND record_id=NEW.subject_id AND record_id::text=b#>>'{body,observation,id}' AND body_digest=b#>>'{body,observation,digest}' AND identity_key=NEW.identity_key;
            IF o IS NULL OR b#>'{body,identity}' IS DISTINCT FROM o#>'{body,identity}' OR b#>'{body,evidenceRef}' IS DISTINCT FROM o#>'{body,evidenceRef}'
              OR b#>'{body,maintenanceOutcome}' IS DISTINCT FROM o#>'{body,outcome}'
              OR (b#>>'{body,occurredAt}')::timestamptz IS DISTINCT FROM (o#>>'{body,occurredAt}')::timestamptz
              OR b#>>'{body,outcome}' NOT IN ('ACCEPT_FACT','NEEDS_EVIDENCE','REJECT_ASSERTION') THEN
              RAISE EXCEPTION 'TOOL_MAINTENANCE_RECOGNITION_INVALID' USING ERRCODE='23514'; END IF;
          ELSIF NEW.kind='M_APPLICATION' THEN
            SELECT body_json::jsonb INTO d FROM cp6_tool_actual_v1.facts WHERE tenant_id=NEW.tenant_id AND tool_id=NEW.tool_id AND kind='M_RECOGNITION'
              AND record_id::text=b#>>'{body,decision,id}' AND body_digest=b#>>'{body,decision,digest}' AND subject_id=NEW.subject_id AND identity_key=NEW.identity_key;
            SELECT body_json::jsonb INTO c FROM cp6_tool_maintenance_v1.facts WHERE tenant_id=NEW.tenant_id AND tool_id=NEW.tool_id AND kind='COMPLETE'
              AND record_id::text=b#>>'{body,completion,id}' AND body_digest=b#>>'{body,completion,digest}';
            IF d IS NULL OR c IS NULL OR d#>>'{body,outcome}' IS DISTINCT FROM 'ACCEPT_FACT'
              OR d#>'{body,observation}' IS DISTINCT FROM b#>'{body,observation}' OR b#>>'{body,observation,id}' IS DISTINCT FROM NEW.subject_id::text
              OR b#>>'{body,completion,owner}' IS DISTINCT FROM 'Main.Wms.ToolMaintenance.v1' OR b#>>'{body,completion,version}' IS DISTINCT FROM '1'
              OR d#>>'{body,identity,sourceDocumentId}' IS DISTINCT FROM c->>'maintenanceId'
              OR d#>'{body,evidenceRef}' IS DISTINCT FROM c#>'{body,report}'
              OR d#>'{body,maintenanceOutcome}' IS DISTINCT FROM c#>'{body,outcome}'
              OR b#>'{body,maintenanceOutcome}' IS DISTINCT FROM c#>'{body,outcome}'
              OR (d#>>'{body,occurredAt}')::timestamptz IS DISTINCT FROM (c#>>'{body,occurredAt}')::timestamptz
              OR b#>>'{body,isUsePermit}' IS DISTINCT FROM 'false' THEN
              RAISE EXCEPTION 'TOOL_MAINTENANCE_APPLICATION_INVALID' USING ERRCODE='23514'; END IF;
          END IF; RETURN NEW;
        END $$;
        CREATE TRIGGER tool_actual_validate_maintenance BEFORE INSERT ON cp6_tool_actual_v1.facts
          FOR EACH ROW EXECUTE FUNCTION cp6_tool_actual_v1.validate_maintenance();
        REVOKE ALL ON FUNCTION cp6_tool_actual_v1.validate_maintenance() FROM PUBLIC;
        """);
    protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException("Maintenance actual originals are retained; forward-only migration.");
}
