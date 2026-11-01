using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)),Migration("20261101024100_SchedulingArrangementV1")]
public sealed class SchedulingArrangementV1:Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        CREATE TABLE cp6_scheduling_v1.planning_inputs(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,root_id uuid NOT NULL,body bytea NOT NULL,
          PRIMARY KEY(tenant_id,environment_id,root_id),FOREIGN KEY(tenant_id,environment_id,root_id) REFERENCES cp6_scheduling_v1.wo_roots);
        CREATE TABLE cp6_scheduling_v1.arrangement_documents(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,id uuid NOT NULL,site_id uuid NOT NULL,kind text NOT NULL,actor_id uuid NOT NULL,body bytea NOT NULL,
          PRIMARY KEY(tenant_id,environment_id,id));
        CREATE TABLE cp6_scheduling_v1.arrangement_batches(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,actor_id uuid NOT NULL,command_id uuid NOT NULL,batch_id uuid NOT NULL,preview_id uuid NOT NULL,body bytea NOT NULL,
          PRIMARY KEY(tenant_id,environment_id,actor_id,command_id),UNIQUE(tenant_id,environment_id,batch_id),
          FOREIGN KEY(tenant_id,environment_id,preview_id) REFERENCES cp6_scheduling_v1.arrangement_documents);
        CREATE TABLE cp6_scheduling_v1.arrangement_group_work(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,preview_id uuid NOT NULL,group_id uuid NOT NULL,root_id uuid NOT NULL,state text NOT NULL,error_code text,body bytea,
          PRIMARY KEY(tenant_id,environment_id,group_id),UNIQUE(tenant_id,environment_id,preview_id,root_id),
          FOREIGN KEY(tenant_id,environment_id,preview_id) REFERENCES cp6_scheduling_v1.arrangement_documents,
          FOREIGN KEY(tenant_id,environment_id,root_id) REFERENCES cp6_scheduling_v1.wo_roots,
          CHECK(state IN('WAITING','SEND_INTENT','COMMITTED','REJECTED','CANCELLED')),
          CHECK((state='COMMITTED')=(body IS NOT NULL)));
        CREATE FUNCTION cp6_scheduling_v1.arrangement_terminal() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF TG_OP<>'UPDATE' OR OLD.state IN('COMMITTED','REJECTED','CANCELLED') THEN
            RAISE EXCEPTION 'SCHED_GROUP_TERMINAL' USING ERRCODE='23514';
          END IF;RETURN NEW;
        END $$;
        CREATE TRIGGER terminal BEFORE UPDATE OR DELETE ON cp6_scheduling_v1.arrangement_group_work FOR EACH ROW EXECUTE FUNCTION cp6_scheduling_v1.arrangement_terminal();
        CREATE TRIGGER immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_scheduling_v1.arrangement_documents FOR EACH STATEMENT EXECUTE FUNCTION cp6_rma_formal_v1.immutable();
        CREATE TRIGGER immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_scheduling_v1.arrangement_batches FOR EACH STATEMENT EXECUTE FUNCTION cp6_rma_formal_v1.immutable();
        REVOKE ALL ON cp6_scheduling_v1.planning_inputs,cp6_scheduling_v1.arrangement_documents,cp6_scheduling_v1.arrangement_batches,cp6_scheduling_v1.arrangement_group_work FROM PUBLIC;
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",v.action,v.action,0,t."Id",LOCALTIMESTAMP
        FROM (VALUES('arrange.preview'),('preview.read'),('arrange.apply'),('work.read'))v(action)
        JOIN public."Sys_Menus" m ON m."MenuKey"='mes-planning-board' CROSS JOIN public."Sys_Tenants" t
        WHERE NOT EXISTS(SELECT FROM public."Sys_MenuAction" a WHERE a."TenantId"=t."Id" AND a."MenuId"=m."MenuId" AND a."ActionCode"=v.action);
        """);
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Forward-only arrangement original work and results.");
}
