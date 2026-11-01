using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)),Migration("20261101024200_SchedulingQueuesV1")]
public sealed class SchedulingQueuesV1:Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        CREATE TABLE cp6_scheduling_v1.queue_heads(tenant_id uuid NOT NULL,environment_id uuid NOT NULL,site_id uuid NOT NULL,resource_id uuid NOT NULL,queue_id uuid NOT NULL,body bytea NOT NULL,PRIMARY KEY(tenant_id,environment_id,site_id,resource_id),UNIQUE(tenant_id,environment_id,queue_id));
        CREATE TABLE cp6_scheduling_v1.queue_revisions(tenant_id uuid NOT NULL,environment_id uuid NOT NULL,queue_id uuid NOT NULL,revision text NOT NULL,body bytea NOT NULL,audit bytea NOT NULL,PRIMARY KEY(tenant_id,environment_id,queue_id,revision),FOREIGN KEY(tenant_id,environment_id,queue_id) REFERENCES cp6_scheduling_v1.queue_heads(tenant_id,environment_id,queue_id));
        CREATE TABLE cp6_scheduling_v1.queue_commands(tenant_id uuid NOT NULL,environment_id uuid NOT NULL,actor_id uuid NOT NULL,command_id uuid NOT NULL,input_digest text NOT NULL,plan_id uuid NOT NULL,body bytea NOT NULL,PRIMARY KEY(tenant_id,environment_id,actor_id,command_id),FOREIGN KEY(tenant_id,environment_id,plan_id) REFERENCES cp6_scheduling_v1.arrangement_documents(tenant_id,environment_id,id));
        CREATE TABLE cp6_scheduling_v1.queue_applications(tenant_id uuid NOT NULL,environment_id uuid NOT NULL,plan_id uuid NOT NULL,body bytea NOT NULL,PRIMARY KEY(tenant_id,environment_id,plan_id),FOREIGN KEY(tenant_id,environment_id,plan_id) REFERENCES cp6_scheduling_v1.arrangement_documents(tenant_id,environment_id,id));
        CREATE TABLE cp6_scheduling_v1.queue_execution_events(tenant_id uuid NOT NULL,environment_id uuid NOT NULL,owner text NOT NULL,fact_id uuid NOT NULL,version text NOT NULL,input_digest text NOT NULL,body bytea NOT NULL,PRIMARY KEY(tenant_id,environment_id,owner,fact_id,version));
        CREATE TABLE cp6_scheduling_v1.queue_execution_heads(tenant_id uuid NOT NULL,environment_id uuid NOT NULL,root_id uuid NOT NULL,operation_id uuid NOT NULL,body bytea NOT NULL,PRIMARY KEY(tenant_id,environment_id,root_id,operation_id),FOREIGN KEY(tenant_id,environment_id,root_id) REFERENCES cp6_scheduling_v1.wo_roots);
        CREATE TRIGGER immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_scheduling_v1.queue_revisions FOR EACH STATEMENT EXECUTE FUNCTION cp6_rma_formal_v1.immutable();
        CREATE TRIGGER immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_scheduling_v1.queue_commands FOR EACH STATEMENT EXECUTE FUNCTION cp6_rma_formal_v1.immutable();
        CREATE TRIGGER immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_scheduling_v1.queue_applications FOR EACH STATEMENT EXECUTE FUNCTION cp6_rma_formal_v1.immutable();
        CREATE TRIGGER immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_scheduling_v1.queue_execution_events FOR EACH STATEMENT EXECUTE FUNCTION cp6_rma_formal_v1.immutable();
        REVOKE ALL ON cp6_scheduling_v1.queue_heads,cp6_scheduling_v1.queue_revisions,cp6_scheduling_v1.queue_commands,cp6_scheduling_v1.queue_applications,cp6_scheduling_v1.queue_execution_events,cp6_scheduling_v1.queue_execution_heads FROM PUBLIC;
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",v.action,v.action,0,t."Id",LOCALTIMESTAMP
        FROM (VALUES('queue.read'),('queue.prepare'),('queue.apply'))v(action)
        JOIN public."Sys_Menus" m ON m."MenuKey"='mes-planning-board' CROSS JOIN public."Sys_Tenants" t
        WHERE NOT EXISTS(SELECT FROM public."Sys_MenuAction" a WHERE a."TenantId"=t."Id" AND a."MenuId"=m."MenuId" AND a."ActionCode"=v.action);
        """);
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Forward-only queue history and original commands.");
}
