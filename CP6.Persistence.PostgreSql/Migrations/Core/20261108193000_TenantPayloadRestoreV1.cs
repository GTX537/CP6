using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)),Migration("20261108193000_TenantPayloadRestoreV1")]
public sealed class TenantPayloadRestoreV1:Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        CREATE FUNCTION cp6_privacy_tenant_payload_disposition_v1.restore_owns(artifact uuid,source_plan uuid,revision uuid DEFAULT NULL,payload_length bigint DEFAULT NULL) RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT EXISTS(SELECT FROM cp6_privacy_disposition_v1.restore_receipts r,
            jsonb_array_elements(COALESCE(NULLIF(r.result_json::jsonb->'TenantPayloadUnits','null'::jsonb),'[]'::jsonb)) u
            WHERE r.writer_xid=pg_current_xact_id() AND u->>'Owner'='Main.Privacy.TenantExportPayload.v1'
              AND (u->>'ArtifactId')::uuid=artifact AND (u->>'SourcePlanId')::uuid=source_plan
              AND (revision IS NULL OR (u->>'BeforeVersion')::uuid=revision AND (u->>'RemovedBytes')::bigint=payload_length
                AND u->>'Outcome'='REAPPLIED' AND u->>'BeforeState'='PRESENT'));
        $$;
        CREATE OR REPLACE FUNCTION cp6_privacy_tenant_payload_disposition_v1.tomb_origin() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE a cp6_privacy_tenant_export_v1.artifacts;s jsonb;BEGIN
          s=NEW.scope_json::jsonb;SELECT * INTO a FROM cp6_privacy_tenant_export_v1.artifacts WHERE id=NEW.artifact_id;
          IF NEW.installed_xid<>pg_current_xact_id()
            OR (NEW.environment_id,NEW.operator_tenant,NEW.artifact_id,NEW.target_tenant,NEW.policy_id)
              IS DISTINCT FROM ((s->>'EnvironmentId')::uuid,(s->>'OperatorTenantId')::uuid,(s->>'ArtifactId')::uuid,(s->>'TargetTenantId')::uuid,(s->>'PolicyId')::uuid)
            OR a.id IS NOT NULL AND ((a.environment_id,a.actor_tenant,a.target_tenant)
              IS DISTINCT FROM (NEW.environment_id,NEW.operator_tenant,NEW.target_tenant)
              OR convert_from(a.receipt_bytes,'UTF8')::jsonb->>'Sha256' IS DISTINCT FROM s->>'OriginalSha256')
            OR NOT EXISTS(SELECT FROM cp6_privacy_tenant_payload_disposition_v1.executions x WHERE x.plan_id=NEW.source_plan_id AND x.artifact_id=NEW.artifact_id
              AND x.writer_xid=pg_current_xact_id() AND x.scope_json::jsonb=s AND x.receipt_json::jsonb=NEW.receipt_json::jsonb)
              AND NOT cp6_privacy_tenant_payload_disposition_v1.restore_owns(NEW.artifact_id,NEW.source_plan_id)

            THEN RAISE EXCEPTION 'PRIVACY_TENANT_PAYLOAD_TOMBSTONE_ORIGIN_REQUIRED' USING ERRCODE='23514';END IF;RETURN NEW;
        END $$;
        CREATE OR REPLACE FUNCTION cp6_privacy_tenant_payload_disposition_v1.owned_delete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP='TRUNCATE' THEN RAISE EXCEPTION 'PRIVACY_TENANT_PAYLOAD_DISPOSITION_REQUIRED' USING ERRCODE='23514';END IF;
          IF EXISTS(SELECT FROM cp6_privacy_tenant_export_v1.artifacts a WHERE a.id=OLD.artifact_id
              AND cp6_privacy_tenant_payload_disposition_v1.active_tenant_hold(a.environment_id,a.target_tenant)
              AND NOT EXISTS(SELECT FROM cp6_privacy_tenant_payload_disposition_v1.tombstones t WHERE t.artifact_id=OLD.artifact_id
                AND cp6_privacy_tenant_payload_disposition_v1.restore_owns(t.artifact_id,t.source_plan_id)))
            THEN RAISE EXCEPTION 'PRIVACY_TENANT_PAYLOAD_ACTIVE_HOLD' USING ERRCODE='23514';END IF;
          IF NOT EXISTS(SELECT FROM cp6_privacy_tenant_payload_disposition_v1.tombstones t WHERE t.artifact_id=OLD.artifact_id AND
              (EXISTS(SELECT FROM cp6_privacy_tenant_payload_disposition_v1.executions x WHERE x.plan_id=t.source_plan_id AND x.artifact_id=t.artifact_id
                  AND x.writer_xid=pg_current_xact_id() AND (x.scope_json::jsonb->>'PayloadVersion')::uuid=OLD.revision)
               OR cp6_privacy_tenant_payload_disposition_v1.restore_owns(t.artifact_id,t.source_plan_id,OLD.revision,octet_length(OLD.bytes)::bigint)
               ))
            THEN RAISE EXCEPTION 'PRIVACY_TENANT_PAYLOAD_DISPOSITION_REQUIRED' USING ERRCODE='23514';END IF;RETURN OLD;
        END $$;
        CREATE FUNCTION cp6_privacy_tenant_payload_disposition_v1.restore_complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE u jsonb;BEGIN
          FOR u IN SELECT value FROM jsonb_array_elements(COALESCE(NULLIF(NEW.result_json::jsonb->'TenantPayloadUnits','null'::jsonb),'[]'::jsonb)) LOOP
            IF u->>'Owner'='Main.Privacy.TenantExportPayload.v1' AND
              (NOT EXISTS(SELECT FROM cp6_privacy_tenant_payload_disposition_v1.tombstones t WHERE t.artifact_id=(u->>'ArtifactId')::uuid
                  AND t.source_plan_id=(u->>'SourcePlanId')::uuid AND t.target_tenant=(u->>'TargetTenantId')::uuid AND t.environment_id=NEW.environment_id)
               OR EXISTS(SELECT FROM cp6_privacy_tenant_export_v1.payloads WHERE artifact_id=(u->>'ArtifactId')::uuid)
               OR EXISTS(SELECT FROM cp6_privacy_tenant_export_v1.artifacts WHERE id=(u->>'ArtifactId')::uuid AND revoked_at IS NULL))
              THEN RAISE EXCEPTION 'PRIVACY_TENANT_PAYLOAD_RESTORE_EFFECT_REQUIRED' USING ERRCODE='23514';END IF;
          END LOOP;RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER tenant_payload_restore_complete AFTER INSERT ON cp6_privacy_disposition_v1.restore_receipts DEFERRABLE INITIALLY DEFERRED
          FOR EACH ROW EXECUTE FUNCTION cp6_privacy_tenant_payload_disposition_v1.restore_complete();
        -- A restored candidate is an external database. Check the native fences at that one boundary.
        CREATE FUNCTION cp6_privacy_tenant_payload_disposition_v1.restore_ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT to_regprocedure('cp6_privacy_tenant_payload_disposition_v1.restore_owns(uuid,uuid,uuid,bigint)') IS NOT NULL
            AND to_regprocedure('cp6_privacy_tenant_payload_disposition_v1.active_tenant_hold(uuid,uuid)') IS NOT NULL
            AND NOT EXISTS(SELECT FROM (VALUES
            ('cp6_privacy_tenant_export_v1.payloads','authority_gate','cp6_privacy_tenant_export_v1.gate()',62,false),
            ('cp6_privacy_tenant_export_v1.payloads','payload_no_rewrite','cp6_privacy_tenant_export_v1.immutable()',18,false),
            ('cp6_privacy_tenant_export_v1.payloads','payload_no_resurrection','cp6_privacy_tenant_payload_disposition_v1.no_resurrection()',23,false),
            ('cp6_privacy_tenant_export_v1.payloads','payload_owned_delete','cp6_privacy_tenant_payload_disposition_v1.owned_delete()',11,false),
            ('cp6_privacy_tenant_export_v1.payloads','payload_no_truncate','cp6_privacy_tenant_payload_disposition_v1.owned_delete()',34,false),
            ('cp6_privacy_tenant_export_v1.artifacts','authority_gate','cp6_privacy_tenant_export_v1.gate()',62,false),
            ('cp6_privacy_tenant_export_v1.artifacts','artifact_change','cp6_privacy_tenant_export_v1.artifact_change()',27,false),
            ('cp6_privacy_tenant_export_v1.artifacts','artifact_no_truncate','cp6_privacy_tenant_export_v1.immutable()',34,false),
            ('cp6_privacy_tenant_payload_disposition_v1.tombstones','authority_gate','cp6_privacy_disposition_v1.gate()',62,false),
            ('cp6_privacy_tenant_payload_disposition_v1.tombstones','immutable','cp6_privacy_disposition_v1.immutable()',58,false),
            ('cp6_privacy_tenant_payload_disposition_v1.tombstones','tomb_origin','cp6_privacy_tenant_payload_disposition_v1.tomb_origin()',7,false),
            ('cp6_privacy_disposition_v1.restore_receipts','authority_gate','cp6_privacy_disposition_v1.gate()',62,false),
            ('cp6_privacy_disposition_v1.restore_receipts','immutable','cp6_privacy_disposition_v1.immutable()',58,false),
            ('cp6_privacy_disposition_v1.restore_receipts','tenant_payload_restore_complete','cp6_privacy_tenant_payload_disposition_v1.restore_complete()',5,true)) a(tab,name,fn,typ,deferred)
            WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid=to_regclass(a.tab) AND t.tgname=a.name AND t.tgfoid=to_regprocedure(a.fn)
              AND t.tgtype=a.typ AND t.tgenabled IN('O','A') AND t.tgattr=''::int2vector AND t.tgqual IS NULL AND t.tgnargs=0
              AND t.tgdeferrable=a.deferred AND t.tginitdeferred=a.deferred));
        $$;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_privacy_tenant_payload_disposition_v1 FROM PUBLIC;
        """);
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Restored tenant payload origins and resurrection fences require forward recovery.");
}
