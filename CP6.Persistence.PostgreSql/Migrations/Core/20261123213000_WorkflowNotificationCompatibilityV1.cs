using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261123213000_WorkflowNotificationCompatibilityV1")]
public sealed class WorkflowNotificationCompatibilityV1 : Migration
{
    protected override void Up(MigrationBuilder migration) => migration.Sql("""
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
              AND (n."NotificationInsertXid" IS NULL OR n."NotificationInsertXid"=r.writer_xid);

        -- 两版 worker 都从状态 0 或 2 领取、按原 ID 读取并在确认后写 1。
        -- 原领取的 UPDATE 仍返回受影响行数 1，但原子关闭二次领取窗口。
        -- 状态 3 表示原尝试结果待核对。原 worker 的迟到成功可以写回状态 1。
        CREATE FUNCTION cp6_oa_notification_v1.hold_dispatch_attempt() RETURNS trigger
        LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
            NEW."DispatchStatus"=3;
            RETURN NEW;
        END $$;
        REVOKE ALL ON FUNCTION cp6_oa_notification_v1.hold_dispatch_attempt() FROM PUBLIC;

        -- 签入领取闸门与迁移既有在途记录使用同一个写锁和事务。
        LOCK TABLE public."Wf_Notification" IN SHARE ROW EXCLUSIVE MODE;
        CREATE TRIGGER hold_workflow_dispatch_attempt BEFORE UPDATE OF "DispatchStatus","DispatchAttempts"
            ON public."Wf_Notification" FOR EACH ROW
            WHEN (NEW."DispatchStatus"=2 OR (NEW."DispatchStatus"=0 AND NEW."DispatchAttempts">0))
            EXECUTE FUNCTION cp6_oa_notification_v1.hold_dispatch_attempt();
        UPDATE public."Wf_Notification" SET "DispatchStatus"=3
        WHERE "DispatchStatus"=2 OR ("DispatchStatus"=0 AND "DispatchAttempts">0);
        """);

    protected override void Down(MigrationBuilder migration)
        => throw new NotSupportedException("通知领取、历史事实与原回执只允许前向迁移。");
}
