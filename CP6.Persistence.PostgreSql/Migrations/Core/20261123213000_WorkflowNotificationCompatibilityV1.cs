using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261123213000_WorkflowNotificationCompatibilityV1")]
public sealed class WorkflowNotificationCompatibilityV1 : Migration
{
    protected override void Up(MigrationBuilder migration) => migration.Sql("""
        ALTER TABLE public."Wf_Notification" ADD COLUMN "LegacyEngineEnvironmentId" uuid;

        -- 只记录原引擎新写入时已登记的可信环境。不为旧行补猜环境。
        CREATE FUNCTION cp6_oa_notification_v1.capture_legacy_engine_scope() RETURNS trigger
        LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE event_prefix text;
        BEGIN
            NEW."LegacyEngineEnvironmentId"=NULL;
            IF NEW."InstanceId" IS NULL OR NEW."EventKey" IS NULL THEN RETURN NEW; END IF;
            event_prefix=NEW."Type"::text || ':' || replace(NEW."InstanceId"::text,'-','') || ':' ||
                coalesce(replace(NEW."TaskId"::text,'-',''),'-') || ':' || replace(NEW."UserId"::text,'-','') || ':';
            IF (NEW."Type"=1 AND NEW."TaskId" IS NOT NULL AND NEW."EventKey"=event_prefix||'-')
               OR (NEW."Type" IN (2,3) AND NEW."TaskId" IS NULL AND NEW."EventKey"=event_prefix||'-')
               OR (NEW."Type"=5 AND NEW."TaskId" IS NULL AND starts_with(NEW."EventKey",event_prefix)
                   AND length(NEW."EventKey")>length(event_prefix)) THEN
                SELECT e.environment_id INTO NEW."LegacyEngineEnvironmentId"
                FROM cp6_scope_authorization_v1.environment e JOIN cp6_scope_authorization_v1.fences f USING(environment_id)
                WHERE e.singleton AND f.tenant_id=NEW."TenantId" AND f.logical_domain='WF_AUTH' AND f.enabled
                    AND f.writer_profile='MAIN_NATIVE_IAM_WF_V1';
            END IF;
            RETURN NEW;
        END $$;
        REVOKE ALL ON FUNCTION cp6_oa_notification_v1.capture_legacy_engine_scope() FROM PUBLIC;
        CREATE TRIGGER capture_legacy_engine_scope BEFORE INSERT ON public."Wf_Notification"
            FOR EACH ROW EXECUTE FUNCTION cp6_oa_notification_v1.capture_legacy_engine_scope();
        CREATE TRIGGER legacy_engine_scope_immutable BEFORE UPDATE OF "LegacyEngineEnvironmentId" ON public."Wf_Notification"
            FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE INDEX oa_notification_legacy_engine_scope ON public."Wf_Notification"("TenantId","LegacyEngineEnvironmentId")
            WHERE "LegacyEngineEnvironmentId" IS NOT NULL;

        CREATE OR REPLACE VIEW cp6_oa_notification_v1.owner_sources AS
            SELECT o.notification_id, c.tenant_id, c.environment_id, o.recipient_id, o.task_id, o.kind,
                   o.preference_version, c.actor_id
            FROM cp6_personal_form_v2.outbox o JOIN cp6_personal_form_v2.commands c ON c.id=o.command_id
            WHERE c.completed AND NOT c.closed
            UNION ALL
            SELECT o.id, c.tenant_id, c.environment_id, o.recipient_id, o.task_id, o.kind,
                   o.preference_version, c.actor_id
            FROM cp6_native_tasks_v2.notification_outbox o JOIN cp6_native_tasks_v2.commands c ON c.id=o.command_id
            WHERE c.completed AND NOT c.closed
            UNION ALL
            SELECT n."Id", r.tenant_id, r.environment_id, n."UserId", r.task_id, 'DEADLINE_RECALCULATED',
                   NULL::uuid, r.actor_id
            FROM cp6_work_calendar_v1.recalculations r
            JOIN cp6_native_timeout_v2.deadlines d ON (d.task_id,d.revision,d.tenant_id,d.environment_id)=
                (r.task_id,r.to_revision,r.tenant_id,r.environment_id)
            JOIN cp6_main_owner_v1.operations j ON (j.tenant_id,j.operation_id,j.actor_id)=(r.tenant_id,r.id,r.actor_id)
                AND j.owner='OA_WORK_CALENDAR' AND j.contract='OA_DEADLINE_RECALCULATE/1' AND j.state='COMMITTED'
                AND j.object_ids=ARRAY[r.task_id,d.instance_id]
            JOIN public."Wf_Notification" n ON n."TenantId"=r.tenant_id AND n."TaskId"=r.task_id
                AND n."InstanceId"=d.instance_id AND n."EventKey"='OA_DEADLINE_RECALCULATED/'||r.id::text
            WHERE n."Type"=1 AND n."InAppRequested" AND NOT n."EmailRequested"
              AND (n."NotificationInsertXid" IS NULL OR n."NotificationInsertXid"=r.writer_xid)
            UNION ALL
            SELECT n."Id", n."TenantId", n."LegacyEngineEnvironmentId", n."UserId", n."TaskId",
                   CASE WHEN n."Type"=1 THEN 'LEGACY_ENGINE_ASSIGNMENT' ELSE 'LEGACY_ENGINE_STARTER' END,
                   NULL::uuid, n."UserId"
            FROM public."Wf_Notification" n
            JOIN public."Wf_FlowInstance" i ON (i."TenantId",i."Id",i."FlowKey")=(n."TenantId",n."InstanceId",n."FlowKey")
            WHERE n."LegacyEngineEnvironmentId" IS NOT NULL
              AND (n."Type"=1 AND EXISTS(SELECT FROM public."Wf_FlowTask" t
                    WHERE (t."TenantId",t."InstanceId",t."Id")=(n."TenantId",n."InstanceId",n."TaskId"))
                OR n."UserId"=i."StarterId" AND (n."Type"=2 AND i."Status"=1
                    OR n."Type"=3 AND i."Status"=2 OR n."Type"=5));
        """);

    protected override void Down(MigrationBuilder migration)
        => throw new NotSupportedException("通知来源及历史处置证据只允许前向迁移。");
}
