using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)),Migration("20261101023700_ReplenishPlanningV1")]
public sealed class ReplenishPlanningV1:Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        CREATE SCHEMA cp6_replenish_v1;
        CREATE TABLE cp6_replenish_v1.observations(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,scope_key text COLLATE "C" NOT NULL,
          cut_key text COLLATE "C" NOT NULL,body bytea NOT NULL,recorded_at timestamptz NOT NULL DEFAULT clock_timestamp(),
          PRIMARY KEY(tenant_id,environment_id,scope_key,cut_key));
        CREATE TABLE cp6_replenish_v1.current_observations(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,scope_key text COLLATE "C" NOT NULL,cut_key text COLLATE "C" NOT NULL,
          PRIMARY KEY(tenant_id,environment_id,scope_key),
          FOREIGN KEY(tenant_id,environment_id,scope_key,cut_key) REFERENCES cp6_replenish_v1.observations);
        CREATE TABLE cp6_replenish_v1.documents(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,id uuid NOT NULL,kind text COLLATE "C" NOT NULL,
          creator_id uuid NOT NULL,department_id uuid,scope_key text COLLATE "C" NOT NULL,body bytea NOT NULL,recorded_at timestamptz NOT NULL DEFAULT clock_timestamp(),
          PRIMARY KEY(tenant_id,environment_id,id));
        CREATE TABLE cp6_replenish_v1.batches(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,canonical text COLLATE "C" NOT NULL,id uuid NOT NULL,
          PRIMARY KEY(tenant_id,environment_id,canonical),UNIQUE(tenant_id,environment_id,id),
          FOREIGN KEY(tenant_id,environment_id,id) REFERENCES cp6_replenish_v1.documents);
        CREATE TABLE cp6_replenish_v1.split_orders(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,plan_id uuid NOT NULL,split_key text COLLATE "C" NOT NULL,order_id uuid NOT NULL,
          PRIMARY KEY(tenant_id,environment_id,plan_id,split_key),
          FOREIGN KEY(tenant_id,environment_id,plan_id) REFERENCES cp6_replenish_v1.documents,
          FOREIGN KEY(tenant_id,environment_id,order_id) REFERENCES cp6_replenish_v1.documents);
        CREATE TABLE cp6_replenish_v1.commands(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,actor_id uuid NOT NULL,command_key text COLLATE "C" NOT NULL,
          action text COLLATE "C" NOT NULL,input_digest text NOT NULL,result_id uuid NOT NULL,result_body bytea NOT NULL,recorded_at timestamptz NOT NULL DEFAULT clock_timestamp(),
          PRIMARY KEY(tenant_id,environment_id,actor_id,command_key),
          FOREIGN KEY(tenant_id,environment_id,result_id) REFERENCES cp6_replenish_v1.documents);
        CREATE TABLE cp6_replenish_v1.events(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,id uuid NOT NULL,action text NOT NULL,body bytea NOT NULL,
          PRIMARY KEY(tenant_id,environment_id,id),
          FOREIGN KEY(tenant_id,environment_id,id) REFERENCES cp6_replenish_v1.documents);
        DO $$ DECLARE n text; BEGIN
          FOREACH n IN ARRAY ARRAY['observations','documents','batches','split_orders','commands','events'] LOOP
            EXECUTE format('CREATE TRIGGER immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_replenish_v1.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_rma_formal_v1.immutable()',n);
          END LOOP;
        END $$;
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_replenish_v1 FROM PUBLIC;
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",'simulate-source','导入模拟补货验收输入',90,t."Id",LOCALTIMESTAMP
        FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t WHERE m."MenuKey"='wms-replenish';
        """);
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Forward-only business history.");
}
