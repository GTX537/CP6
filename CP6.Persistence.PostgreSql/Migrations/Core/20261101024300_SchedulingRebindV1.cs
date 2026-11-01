using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)),Migration("20261101024300_SchedulingRebindV1")]
public sealed class SchedulingRebindV1:Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        CREATE TABLE cp6_scheduling_v1.rebind_sources(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,assignment_id uuid NOT NULL,body bytea NOT NULL,
          PRIMARY KEY(tenant_id,environment_id,assignment_id));
        CREATE TABLE cp6_scheduling_v1.rebind_works(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,work_id uuid NOT NULL,plan_id uuid NOT NULL,root_id uuid NOT NULL,body bytea NOT NULL,
          PRIMARY KEY(tenant_id,environment_id,work_id),UNIQUE(tenant_id,environment_id,plan_id),
          FOREIGN KEY(tenant_id,environment_id,plan_id) REFERENCES cp6_scheduling_v1.documents(tenant_id,environment_id,id));
        CREATE TABLE cp6_scheduling_v1.rebind_requests(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,work_id uuid NOT NULL,phase text NOT NULL,original_key uuid NOT NULL,body bytea NOT NULL,
          PRIMARY KEY(tenant_id,environment_id,work_id,phase),UNIQUE(tenant_id,environment_id,original_key),
          FOREIGN KEY(tenant_id,environment_id,work_id) REFERENCES cp6_scheduling_v1.rebind_works);
        CREATE TABLE cp6_scheduling_v1.rebind_receipts(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,work_id uuid NOT NULL,phase text NOT NULL,body bytea NOT NULL,
          PRIMARY KEY(tenant_id,environment_id,work_id,phase),
          FOREIGN KEY(tenant_id,environment_id,work_id,phase) REFERENCES cp6_scheduling_v1.rebind_requests(tenant_id,environment_id,work_id,phase));
        CREATE TABLE cp6_scheduling_v1.rebind_commands(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,actor_id uuid NOT NULL,command_id uuid NOT NULL,input_digest text NOT NULL,
          plan_id uuid NOT NULL,body bytea NOT NULL,PRIMARY KEY(tenant_id,environment_id,actor_id,command_id));
        CREATE TABLE cp6_scheduling_v1.rebind_claims(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,claim_id uuid NOT NULL,body bytea NOT NULL,
          PRIMARY KEY(tenant_id,environment_id,claim_id));
        CREATE TABLE cp6_scheduling_v1.rebind_executions(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,work_ref_id uuid NOT NULL,assignment_id uuid NOT NULL,body bytea NOT NULL,
          PRIMARY KEY(tenant_id,environment_id,work_ref_id));
        CREATE TABLE cp6_scheduling_v1.rebind_fences(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,root_id uuid NOT NULL,operation_id uuid NOT NULL,work_id uuid NOT NULL,
          no_new_execution boolean NOT NULL CHECK(no_new_execution),commit_stopped boolean NOT NULL,
          PRIMARY KEY(tenant_id,environment_id,work_id));
        CREATE TABLE cp6_scheduling_v1.rebind_migrations(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,work_id uuid NOT NULL,rebind_key uuid NOT NULL,new_assignment uuid NOT NULL,body bytea NOT NULL,
          PRIMARY KEY(tenant_id,environment_id,work_id),UNIQUE(tenant_id,rebind_key,new_assignment));
        DO $$ DECLARE name text; BEGIN
          FOREACH name IN ARRAY ARRAY['rebind_sources','rebind_requests','rebind_receipts','rebind_commands'] LOOP
            EXECUTE format('CREATE TRIGGER immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_scheduling_v1.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_rma_formal_v1.immutable()',name);
          END LOOP;
        END $$;
        REVOKE ALL ON cp6_scheduling_v1.rebind_sources,cp6_scheduling_v1.rebind_works,cp6_scheduling_v1.rebind_requests,
          cp6_scheduling_v1.rebind_receipts,cp6_scheduling_v1.rebind_commands,cp6_scheduling_v1.rebind_claims,
          cp6_scheduling_v1.rebind_executions,cp6_scheduling_v1.rebind_fences,cp6_scheduling_v1.rebind_migrations FROM PUBLIC;
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",'work.reconcile','work.reconcile',0,t."Id",LOCALTIMESTAMP
        FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t WHERE m."MenuKey"='mes-planning-board'
          AND NOT EXISTS(SELECT FROM public."Sys_MenuAction" a WHERE a."TenantId"=t."Id" AND a."MenuId"=m."MenuId" AND a."ActionCode"='work.reconcile');
        """);
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Forward-only original rebind work and owner receipts.");
}
