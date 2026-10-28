using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)),Migration("20261029090000_ApprovalDecisionRecoveryV2")]
public sealed class ApprovalDecisionRecoveryV2:Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        CREATE TABLE cp6_approval_decision_v2.closed_commands(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,id uuid NOT NULL,actor_id uuid NOT NULL,task_id uuid NOT NULL,recorded_at timestamptz NOT NULL,
          PRIMARY KEY(tenant_id,environment_id,id));
        CREATE FUNCTION cp6_approval_decision_v2.close_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_TABLE_NAME='closed_commands' THEN
            IF EXISTS(SELECT FROM cp6_approval_decision_v2.commands WHERE tenant_id=NEW.tenant_id AND environment_id=NEW.environment_id AND id=NEW.id)
              OR NOT EXISTS(SELECT FROM public."Wf_FlowTask" t JOIN cp6_approval_start_v2.starts s ON s.tenant_id=t."TenantId" AND s.instance_id=t."InstanceId"
                WHERE t."TenantId"=NEW.tenant_id AND t."Id"=NEW.task_id AND s.environment_id=NEW.environment_id)
              THEN RAISE EXCEPTION 'DECISION_CLOSE_CONFLICT' USING ERRCODE='23514'; END IF;
          ELSE
            IF EXISTS(SELECT FROM cp6_approval_decision_v2.closed_commands WHERE tenant_id=NEW.tenant_id AND environment_id=NEW.environment_id AND id=NEW.id)
              THEN RAISE EXCEPTION 'DECISION_COMMAND_CLOSED' USING ERRCODE='23514'; END IF;
          END IF;RETURN NEW;
        END $$;
        CREATE TRIGGER decision_close_root BEFORE INSERT ON cp6_approval_decision_v2.closed_commands FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER decision_close_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_approval_decision_v2.closed_commands FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER decision_close_guard BEFORE INSERT ON cp6_approval_decision_v2.closed_commands FOR EACH ROW EXECUTE FUNCTION cp6_approval_decision_v2.close_guard();
        CREATE TRIGGER decision_close_guard BEFORE INSERT ON cp6_approval_decision_v2.commands FOR EACH ROW EXECUTE FUNCTION cp6_approval_decision_v2.close_guard();
        ALTER FUNCTION cp6_approval_decision_v2.ready() RENAME TO ready_before_recovery;
        CREATE FUNCTION cp6_approval_decision_v2.ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT cp6_approval_decision_v2.ready_before_recovery() AND NOT EXISTS(SELECT FROM (VALUES
            ('closed_commands','decision_close_root','cp6_authentication_v1.before_change()',6),
            ('closed_commands','decision_close_immutable','cp6_scope_authorization_v1.immutable()',58),
            ('closed_commands','decision_close_guard','cp6_approval_decision_v2.close_guard()',7),
            ('commands','decision_close_guard','cp6_approval_decision_v2.close_guard()',7)) a(tbl,name,fn,typ)
            WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid=to_regclass('cp6_approval_decision_v2.'||a.tbl)
              AND t.tgname=a.name AND t.tgfoid=to_regprocedure(a.fn) AND t.tgtype=a.typ AND t.tgenabled IN('O','A') AND t.tgqual IS NULL));
        $$;
        """);
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Forward recovery only; closed command identities remain permanent.");
}
