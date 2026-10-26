using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261026220000_ResourceSourceV1")]
public sealed class ResourceSourceV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE SCHEMA cp6_resource_source_v1;
        REVOKE ALL ON SCHEMA cp6_resource_source_v1 FROM PUBLIC;
        CREATE TABLE cp6_resource_source_v1.heads (
          tenant_id uuid NOT NULL,kind text NOT NULL CHECK(kind IN ('MACHINE','WORK_CENTER')),
          resource_id uuid NOT NULL,revision bigint NOT NULL CHECK(revision>0),
          PRIMARY KEY(tenant_id,kind,resource_id));
        CREATE TABLE cp6_resource_source_v1.versions (
          tenant_id uuid NOT NULL,kind text NOT NULL CHECK(kind IN ('MACHINE','WORK_CENTER')),resource_id uuid NOT NULL,
          revision bigint NOT NULL CHECK(revision>0),master_revision bigint NOT NULL CHECK(master_revision>0),
          capability_revision bigint NOT NULL CHECK(capability_revision>0),assignment_revision bigint NOT NULL CHECK(assignment_revision>0),
          control_revision bigint NOT NULL CHECK(control_revision>0),source_present boolean NOT NULL,source_json text NOT NULL,
          source_digest text NOT NULL CHECK(source_digest=encode(sha256(convert_to(source_json,'UTF8')),'hex')),
          observed_at timestamptz NOT NULL DEFAULT clock_timestamp(),PRIMARY KEY(tenant_id,kind,resource_id,revision));
        ALTER TABLE cp6_resource_source_v1.heads ADD CONSTRAINT resource_source_head_version
          FOREIGN KEY(tenant_id,kind,resource_id,revision) REFERENCES cp6_resource_source_v1.versions DEFERRABLE INITIALLY DEFERRED;
        -- Actual original values; neither approved capacity/UOM nor formal Site/Assignment plans.
        -- Decimal text preserves null and original precision. Instantaneous Status/audit timestamps are not technical revisions.
        CREATE FUNCTION cp6_resource_source_v1.payload(k text,p jsonb) RETURNS text
        LANGUAGE sql IMMUTABLE SET search_path=pg_catalog AS $$
          SELECT jsonb_build_object('master',CASE WHEN k='MACHINE' THEN
            jsonb_build_object('code',p->'MachineCd','name',p->'MachineName','manufacturer',p->'Manufacturer','model',p->'Model',
              'installDate',p->'InstallDate','lastMaintenanceDate',p->'LastMaintenanceDate','nextMaintenanceDate',p->'NextMaintenanceDate','remarks',p->'Remarks')
            ELSE jsonb_build_object('code',p->'WgCd','name',p->'WgName') END,
          'capability',CASE WHEN k='MACHINE' THEN
            jsonb_build_object('machineType',p->'MachineType','processCode',p->'ProcessCd','plannedRunMinutesPerDay',p->'PlannedRunMinutesPerDay',
              'standardCycleSeconds',p->>'StandardCycleSec','capacityPerHour',p->>'CapacityPerHour')
            ELSE jsonb_build_object('dailyCapacityHours',p->>'DailyCapacityHours') END,
          'assignment',CASE WHEN k='MACHINE' THEN jsonb_build_object('legacyWorkCenterCode',p->'WgCd','legacyBaseCode',p->'BaseCd') ELSE '{}'::jsonb END,
          'control',jsonb_build_object('enabled',CASE WHEN k='MACHINE' THEN p->'ActiveFlg' ELSE p->'Enable' END,
            'isDeleted',p->'IsDeleted','creator',p->'Creator'))::text
        $$;
        CREATE FUNCTION cp6_resource_source_v1.capture(k text,p jsonb,present boolean) RETURNS void
        LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE t uuid=(p->>'TenantId')::uuid; i uuid=(p->>'Id')::uuid; v bigint; body text;
          prior cp6_resource_source_v1.versions%ROWTYPE; oldbody jsonb; newbody jsonb;
        BEGIN
          body=cp6_resource_source_v1.payload(k,p);newbody=body::jsonb;
          SELECT h.revision INTO v FROM cp6_resource_source_v1.heads h
            WHERE h.tenant_id=t AND h.kind=k AND h.resource_id=i FOR UPDATE;
          IF FOUND THEN
            SELECT * INTO STRICT prior FROM cp6_resource_source_v1.versions
              WHERE tenant_id=t AND kind=k AND resource_id=i AND revision=v;
            IF NOT prior.source_present AND present THEN
              RAISE EXCEPTION 'RESOURCE_IDENTITY_REUSED' USING ERRCODE='23514';
            END IF;
            IF prior.source_json=body AND prior.source_present=present THEN RETURN; END IF;
            oldbody=prior.source_json::jsonb;
            UPDATE cp6_resource_source_v1.heads SET revision=revision+1 WHERE tenant_id=t AND kind=k AND resource_id=i RETURNING revision INTO v;
          ELSE
            INSERT INTO cp6_resource_source_v1.heads VALUES(t,k,i,1);v=1;
          END IF;
          INSERT INTO cp6_resource_source_v1.versions(tenant_id,kind,resource_id,revision,master_revision,capability_revision,assignment_revision,
            control_revision,source_present,source_json,source_digest) VALUES(t,k,i,v,
            CASE WHEN v=1 THEN 1 ELSE prior.master_revision+((oldbody->'master') IS DISTINCT FROM (newbody->'master'))::int END,
            CASE WHEN v=1 THEN 1 ELSE prior.capability_revision+((oldbody->'capability') IS DISTINCT FROM (newbody->'capability'))::int END,
            CASE WHEN v=1 THEN 1 ELSE prior.assignment_revision+((oldbody->'assignment') IS DISTINCT FROM (newbody->'assignment'))::int END,
            CASE WHEN v=1 THEN 1 ELSE prior.control_revision+(((oldbody->'control') IS DISTINCT FROM (newbody->'control')) OR prior.source_present<>present)::int END,
            present,body,encode(sha256(convert_to(body,'UTF8')),'hex'));
        END $$;
        CREATE FUNCTION cp6_resource_source_v1.record_change() RETURNS trigger
        LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP='DELETE' THEN PERFORM cp6_resource_source_v1.capture(TG_ARGV[0],to_jsonb(OLD),false); RETURN OLD; END IF;
          IF TG_OP='UPDATE' AND (OLD."TenantId",OLD."Id") IS DISTINCT FROM (NEW."TenantId",NEW."Id") THEN
            PERFORM cp6_resource_source_v1.capture(TG_ARGV[0],to_jsonb(OLD),false);
          END IF;
          PERFORM cp6_resource_source_v1.capture(TG_ARGV[0],to_jsonb(NEW),true);RETURN NEW;
        END $$;
        CREATE FUNCTION cp6_resource_source_v1.immutable() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN RAISE EXCEPTION 'RESOURCE_SOURCE_ORIGINAL_IMMUTABLE' USING ERRCODE='23514'; END $$;
        CREATE FUNCTION cp6_resource_source_v1.advance_head() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF (OLD.tenant_id,OLD.kind,OLD.resource_id) IS DISTINCT FROM (NEW.tenant_id,NEW.kind,NEW.resource_id) OR NEW.revision<>OLD.revision+1 THEN
            RAISE EXCEPTION 'RESOURCE_SOURCE_FRONTIER_INVALID' USING ERRCODE='23514';
          END IF; RETURN NEW;
        END $$;
        CREATE TRIGGER resource_source_head_monotonic BEFORE UPDATE ON cp6_resource_source_v1.heads FOR EACH ROW EXECUTE FUNCTION cp6_resource_source_v1.advance_head();
        CREATE TRIGGER resource_source_head_no_delete BEFORE DELETE ON cp6_resource_source_v1.heads FOR EACH ROW EXECUTE FUNCTION cp6_resource_source_v1.immutable();
        CREATE TRIGGER resource_source_head_no_truncate BEFORE TRUNCATE ON cp6_resource_source_v1.heads FOR EACH STATEMENT EXECUTE FUNCTION cp6_resource_source_v1.immutable();
        CREATE TRIGGER resource_source_original_immutable BEFORE UPDATE OR DELETE ON cp6_resource_source_v1.versions FOR EACH ROW EXECUTE FUNCTION cp6_resource_source_v1.immutable();
        CREATE TRIGGER resource_source_original_no_truncate BEFORE TRUNCATE ON cp6_resource_source_v1.versions FOR EACH STATEMENT EXECUTE FUNCTION cp6_resource_source_v1.immutable();
        LOCK TABLE public."M_Machine",public."T_WorkCenter" IN SHARE ROW EXCLUSIVE MODE;
        CREATE TRIGGER resource_source_capture AFTER INSERT OR UPDATE OR DELETE ON public."M_Machine" FOR EACH ROW EXECUTE FUNCTION cp6_resource_source_v1.record_change('MACHINE');
        CREATE TRIGGER resource_source_capture AFTER INSERT OR UPDATE OR DELETE ON public."T_WorkCenter" FOR EACH ROW EXECUTE FUNCTION cp6_resource_source_v1.record_change('WORK_CENTER');
        CREATE TRIGGER resource_source_no_truncate BEFORE TRUNCATE ON public."M_Machine" FOR EACH STATEMENT EXECUTE FUNCTION cp6_resource_source_v1.immutable();
        CREATE TRIGGER resource_source_no_truncate BEFORE TRUNCATE ON public."T_WorkCenter" FOR EACH STATEMENT EXECUTE FUNCTION cp6_resource_source_v1.immutable();
        -- Only actual migration observation; no fabricated prior revisions and no original business-row changes/grants.
        SELECT cp6_resource_source_v1.capture('MACHINE',to_jsonb(p),true) FROM public."M_Machine" p ORDER BY p."TenantId",p."Id";
        SELECT cp6_resource_source_v1.capture('WORK_CENTER',to_jsonb(p),true) FROM public."T_WorkCenter" p ORDER BY p."TenantId",p."Id";
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_resource_source_v1 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_resource_source_v1 FROM PUBLIC;
        """);
    protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException("Resource source history requires forward recovery.");
}
