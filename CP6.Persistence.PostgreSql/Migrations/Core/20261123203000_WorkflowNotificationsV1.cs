using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261123203000_WorkflowNotificationsV1")]
public sealed class WorkflowNotificationsV1 : Migration
{
    protected override void Up(MigrationBuilder migration) => migration.Sql("""
        CREATE SCHEMA cp6_oa_notification_v1;
        REVOKE ALL ON SCHEMA cp6_oa_notification_v1 FROM PUBLIC;

        ALTER TABLE public."Wf_InboxPref" ADD COLUMN "NotificationPreferenceVersion" uuid NOT NULL DEFAULT gen_random_uuid();
        CREATE FUNCTION cp6_oa_notification_v1.bump_preference_version() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
            NEW."NotificationPreferenceVersion"=gen_random_uuid();
            RETURN NEW;
        END $$;
        CREATE TRIGGER notification_preference_version BEFORE UPDATE ON public."Wf_InboxPref"
            FOR EACH ROW EXECUTE FUNCTION cp6_oa_notification_v1.bump_preference_version();
        CREATE TABLE cp6_oa_notification_v1.preference_commands(
            tenant_id uuid NOT NULL,
            environment_id uuid NOT NULL,
            actor_id uuid NOT NULL,
            command_id text COLLATE "C" NOT NULL,
            digest text COLLATE "C" NOT NULL,
            result_json text NOT NULL,
            PRIMARY KEY(tenant_id,environment_id,actor_id,command_id)
        );

        CREATE TRIGGER preference_command_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_oa_notification_v1.preference_commands
            FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();

        ALTER TABLE public."Wf_Notification" ADD COLUMN "NotificationInsertXid" xid8;
        ALTER TABLE public."Wf_Notification" ALTER COLUMN "NotificationInsertXid" SET DEFAULT pg_current_xact_id();
        CREATE INDEX oa_notification_cursor ON public."Wf_Notification"("TenantId","UserId","CreateDate" DESC,"Id" DESC)
            WHERE "InAppRequested";

        ALTER TABLE cp6_personal_form_v2.outbox ADD COLUMN preference_version uuid;
        ALTER TABLE cp6_native_tasks_v2.notification_outbox ADD COLUMN preference_version uuid;
        CREATE VIEW cp6_oa_notification_v1.owner_sources AS
            SELECT o.notification_id, c.tenant_id, c.environment_id, o.recipient_id, o.task_id, o.kind,
                   o.preference_version, c.actor_id
            FROM cp6_personal_form_v2.outbox o JOIN cp6_personal_form_v2.commands c ON c.id=o.command_id
            WHERE c.completed AND NOT c.closed
            UNION ALL
            SELECT o.id, c.tenant_id, c.environment_id, o.recipient_id, o.task_id, o.kind,
                   o.preference_version, c.actor_id
            FROM cp6_native_tasks_v2.notification_outbox o JOIN cp6_native_tasks_v2.commands c ON c.id=o.command_id
            WHERE c.completed AND NOT c.closed;

        CREATE TABLE cp6_oa_notification_v1.deliveries(
            tenant_id uuid NOT NULL,
            environment_id uuid NOT NULL,
            notification_id uuid NOT NULL REFERENCES public."Wf_Notification"("Id"),
            recipient_id uuid NOT NULL,
            channel text COLLATE "C" NOT NULL CHECK(channel IN ('IN_APP','EMAIL')),
            recipient_version text COLLATE "C" NOT NULL,
            state text COLLATE "C" NOT NULL CHECK(state IN ('PENDING','IN_FLIGHT','ACCEPTED_BY_PROVIDER',
                'UNKNOWN','RETRYABLE_FAILED','BLOCKED_ADDRESS','SUPPRESSED')),
            reason text COLLATE "C",
            attempt_id uuid,
            attempts integer NOT NULL DEFAULT 0,
            next_attempt_at timestamptz,
            updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
            PRIMARY KEY(tenant_id,environment_id,notification_id,channel,recipient_version)
        );
        CREATE INDEX oa_notification_deliveries_owner
            ON cp6_oa_notification_v1.deliveries(tenant_id,environment_id,recipient_id,notification_id);
        """);

    protected override void Down(MigrationBuilder migration)
        => throw new NotSupportedException("通知原回执和投递状态只允许前向迁移。");
}
