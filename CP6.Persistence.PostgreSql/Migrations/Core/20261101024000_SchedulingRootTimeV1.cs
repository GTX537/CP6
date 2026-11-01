using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)),Migration("20261101024000_SchedulingRootTimeV1")]
public sealed class SchedulingRootTimeV1:Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        CREATE SCHEMA cp6_scheduling_v1;
        CREATE TABLE cp6_scheduling_v1.wo_roots(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,root_id uuid NOT NULL,site_id uuid NOT NULL,body bytea NOT NULL,
          PRIMARY KEY(tenant_id,environment_id,root_id));
        CREATE TABLE cp6_scheduling_v1.openings(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,root_id uuid NOT NULL,work_order_no text COLLATE "C" NOT NULL,
          input_digest text COLLATE "C" NOT NULL,body bytea NOT NULL,
          PRIMARY KEY(tenant_id,environment_id,root_id),UNIQUE(tenant_id,root_id),UNIQUE(tenant_id,work_order_no),
          FOREIGN KEY(tenant_id,environment_id,root_id) REFERENCES cp6_scheduling_v1.wo_roots);
        CREATE TABLE cp6_scheduling_v1.observations(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,root_id uuid NOT NULL,cut text COLLATE "C" NOT NULL,body bytea NOT NULL,
          PRIMARY KEY(tenant_id,environment_id,root_id,cut),FOREIGN KEY(tenant_id,environment_id,root_id) REFERENCES cp6_scheduling_v1.wo_roots);
        CREATE TABLE cp6_scheduling_v1.source_heads(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,root_id uuid NOT NULL,cut text COLLATE "C" NOT NULL,
          PRIMARY KEY(tenant_id,environment_id,root_id),FOREIGN KEY(tenant_id,environment_id,root_id,cut) REFERENCES cp6_scheduling_v1.observations);
        CREATE TABLE cp6_scheduling_v1.documents(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,id uuid NOT NULL,root_id uuid NOT NULL,kind text NOT NULL,actor_id uuid NOT NULL,body bytea NOT NULL,
          PRIMARY KEY(tenant_id,environment_id,id),FOREIGN KEY(tenant_id,environment_id,root_id) REFERENCES cp6_scheduling_v1.wo_roots);
        CREATE TABLE cp6_scheduling_v1.applications(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,plan_id uuid NOT NULL,root_id uuid NOT NULL,apply_key uuid NOT NULL,actor_id uuid NOT NULL,body bytea NOT NULL,
          PRIMARY KEY(tenant_id,environment_id,plan_id),UNIQUE(tenant_id,environment_id,apply_key),
          FOREIGN KEY(tenant_id,environment_id,root_id) REFERENCES cp6_scheduling_v1.wo_roots,
          FOREIGN KEY(tenant_id,environment_id,plan_id) REFERENCES cp6_scheduling_v1.documents);
        CREATE TABLE cp6_scheduling_v1.commands(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,actor_id uuid NOT NULL,command_id uuid NOT NULL,input_digest text COLLATE "C" NOT NULL,
          plan_id uuid NOT NULL,body bytea NOT NULL,PRIMARY KEY(tenant_id,environment_id,actor_id,command_id),
          FOREIGN KEY(tenant_id,environment_id,plan_id) REFERENCES cp6_scheduling_v1.applications);
        CREATE TABLE cp6_scheduling_v1.audit_outbox(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,plan_id uuid NOT NULL,id uuid NOT NULL,body bytea NOT NULL,
          PRIMARY KEY(tenant_id,environment_id,plan_id),UNIQUE(tenant_id,environment_id,id),
          FOREIGN KEY(tenant_id,environment_id,plan_id) REFERENCES cp6_scheduling_v1.applications);
        CREATE FUNCTION cp6_scheduling_v1.complete_application() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF NOT EXISTS(SELECT FROM cp6_scheduling_v1.audit_outbox e WHERE e.tenant_id=NEW.tenant_id AND e.environment_id=NEW.environment_id AND e.plan_id=NEW.plan_id) THEN
            RAISE EXCEPTION 'SCHED_AUDIT_REQUIRED' USING ERRCODE='23514';
          END IF; RETURN NEW;
        END $$;
        CREATE CONSTRAINT TRIGGER complete_application AFTER INSERT ON cp6_scheduling_v1.applications DEFERRABLE INITIALLY DEFERRED
          FOR EACH ROW EXECUTE FUNCTION cp6_scheduling_v1.complete_application();
        CREATE FUNCTION cp6_scheduling_v1.reject_legacy_writer() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF TG_OP<>'INSERT' AND EXISTS(SELECT FROM cp6_scheduling_v1.openings o WHERE o.tenant_id=OLD."TenantId" AND o.work_order_no=OLD."WorkOrderNo" COLLATE "C") THEN
            RAISE EXCEPTION 'MIGRATED_SCOPE_NEW_FLOW_REQUIRED:/mes/planning-board' USING ERRCODE='23514';
          END IF;
          IF TG_OP<>'DELETE' AND EXISTS(SELECT FROM cp6_scheduling_v1.openings o WHERE o.tenant_id=NEW."TenantId" AND o.work_order_no=NEW."WorkOrderNo" COLLATE "C") THEN
            RAISE EXCEPTION 'MIGRATED_SCOPE_NEW_FLOW_REQUIRED:/mes/planning-board' USING ERRCODE='23514';
          END IF;
          IF TG_OP='DELETE' THEN RETURN OLD; ELSE RETURN NEW; END IF;
        END $$;
        CREATE TRIGGER sched_writer BEFORE INSERT OR UPDATE OR DELETE ON public."T_WorkOrder" FOR EACH ROW EXECUTE FUNCTION cp6_scheduling_v1.reject_legacy_writer();
        CREATE TRIGGER sched_writer BEFORE INSERT OR UPDATE OR DELETE ON public."T_WorkOrderProcess" FOR EACH ROW EXECUTE FUNCTION cp6_scheduling_v1.reject_legacy_writer();
        DO $$ DECLARE name text; BEGIN
          FOREACH name IN ARRAY ARRAY['openings','observations','documents','applications','commands','audit_outbox'] LOOP
            EXECUTE format('CREATE TRIGGER immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_scheduling_v1.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_rma_formal_v1.immutable()',name);
          END LOOP;
        END $$;
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_scheduling_v1 FROM PUBLIC;
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",v.action,v.action,0,t."Id",LOCALTIMESTAMP
        FROM (VALUES('mes-planning-board','plan.prepare'),('mes-planning-board','change.apply'),('mes-planning-board','command.read'),
          ('mes-planning-board','history.read'),('mes-planning-board','simulate-source'),('mes-work-order','change.schedule'))v(menu,action)
        JOIN public."Sys_Menus" m ON m."MenuKey"=v.menu CROSS JOIN public."Sys_Tenants" t
        WHERE NOT EXISTS(SELECT FROM public."Sys_MenuAction" a WHERE a."TenantId"=t."Id" AND a."MenuId"=m."MenuId" AND a."ActionCode"=v.action);
        """);
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Forward-only scheduling and original result history.");
}
