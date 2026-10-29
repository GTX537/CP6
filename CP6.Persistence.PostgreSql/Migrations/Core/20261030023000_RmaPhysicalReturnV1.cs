using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261030023000_RmaPhysicalReturnV1")]
public sealed class RmaPhysicalReturnV1 : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        ALTER TABLE public."T_InboundReceipt" DROP CONSTRAINT "CK_T_InboundReceipt_OperatorCd_Utf16Capacity";
        ALTER TABLE public."T_InboundReceipt" ADD CONSTRAINT "CK_T_InboundReceipt_OperatorCd_Utf16Capacity"
          CHECK(char_length(regexp_replace(convert_from(pg_catalog.bpcharsend("OperatorCd"), 'UTF8') COLLATE "C", U&'[\+010000-\+10FFFF]', 'aa', 'g')) <= 100);
        ALTER TABLE public."T_StockTransaction" DROP CONSTRAINT "CK_T_StockTransaction_OperatorCd_Utf16Capacity";
        ALTER TABLE public."T_StockTransaction" ADD CONSTRAINT "CK_T_StockTransaction_OperatorCd_Utf16Capacity"
          CHECK(char_length(regexp_replace(convert_from(pg_catalog.bpcharsend("OperatorCd"), 'UTF8') COLLATE "C", U&'[\+010000-\+10FFFF]', 'aa', 'g')) <= 100);
        CREATE TABLE cp6_rma_formal_v1.physical_heads(tenant_id uuid NOT NULL,application_id uuid NOT NULL,
          revision integer NOT NULL CHECK(revision>0),reference bytea NOT NULL,PRIMARY KEY(tenant_id,application_id),
          FOREIGN KEY(tenant_id,application_id) REFERENCES cp6_rma_formal_v1.heads(tenant_id,id));
        CREATE TABLE cp6_rma_formal_v1.physical_work(tenant_id uuid NOT NULL,work_id uuid NOT NULL,application_id uuid NOT NULL,
          actor_id uuid NOT NULL,revision integer NOT NULL CHECK(revision>0),state text NOT NULL,updated_at timestamptz NOT NULL,
          original bytea NOT NULL,binding_ref bytea NOT NULL,preparation bytea NULL,result bytea NULL,no_effect_proof bytea NULL,
          PRIMARY KEY(tenant_id,work_id),FOREIGN KEY(tenant_id,application_id) REFERENCES cp6_rma_formal_v1.heads(tenant_id,id),
          CHECK(state IN('REGISTERED','PREPARED','SUCCEEDED','SEALED_NO_EFFECT')));
        CREATE TABLE cp6_rma_formal_v1.physical_work_history(tenant_id uuid NOT NULL,work_id uuid NOT NULL,revision integer NOT NULL,
          body bytea NOT NULL,PRIMARY KEY(tenant_id,work_id,revision),
          FOREIGN KEY(tenant_id,work_id) REFERENCES cp6_rma_formal_v1.physical_work(tenant_id,work_id));
        CREATE TABLE cp6_rma_formal_v1.physical_lines(tenant_id uuid NOT NULL,line_id uuid NOT NULL,work_id uuid NOT NULL,
          body bytea NOT NULL,PRIMARY KEY(tenant_id,line_id),
          FOREIGN KEY(tenant_id,work_id) REFERENCES cp6_rma_formal_v1.physical_work(tenant_id,work_id));
        CREATE TABLE cp6_rma_formal_v1.physical_line_ranges(tenant_id uuid NOT NULL,work_id uuid NOT NULL,line_id uuid NOT NULL,
          claim_id uuid NOT NULL,ordinal integer NOT NULL,range_from numeric(21,8) NOT NULL,range_to numeric(21,8) NOT NULL,
          PRIMARY KEY(tenant_id,work_id,line_id,ordinal),CHECK(range_from>=0 AND range_to>range_from),
          FOREIGN KEY(tenant_id,work_id) REFERENCES cp6_rma_formal_v1.physical_work(tenant_id,work_id),
          FOREIGN KEY(tenant_id,line_id) REFERENCES cp6_rma_formal_v1.physical_lines(tenant_id,line_id),
          FOREIGN KEY(tenant_id,claim_id) REFERENCES cp6_rma_formal_v1.return_claims(tenant_id,claim_id));
        CREATE INDEX physical_range_claim ON cp6_rma_formal_v1.physical_line_ranges(tenant_id,claim_id);
        CREATE TABLE cp6_rma_formal_v1.physical_receipts(tenant_id uuid NOT NULL,work_id uuid NOT NULL,line_id uuid NOT NULL,
          body bytea NOT NULL,PRIMARY KEY(tenant_id,work_id,line_id),
          FOREIGN KEY(tenant_id,work_id) REFERENCES cp6_rma_formal_v1.physical_work(tenant_id,work_id));
        CREATE TABLE cp6_rma_formal_v1.inbound_outbox(tenant_id uuid NOT NULL,event_id uuid NOT NULL,work_id uuid NOT NULL,
          body bytea NOT NULL,PRIMARY KEY(tenant_id,event_id),UNIQUE(tenant_id,work_id),
          FOREIGN KEY(tenant_id,work_id) REFERENCES cp6_rma_formal_v1.physical_work(tenant_id,work_id));
        CREATE TABLE cp6_rma_formal_v1.inbound_receipt_sources(tenant_id uuid NOT NULL,receipt_id uuid NOT NULL,outbound_id uuid NOT NULL,
          PRIMARY KEY(tenant_id,receipt_id,outbound_id));
        CREATE SCHEMA IF NOT EXISTS cp6_stock_return_v1;
        CREATE TABLE cp6_stock_return_v1.movement_sequences(sequence bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
          tenant_id uuid NOT NULL,pool_id uuid NOT NULL,movement_id uuid NOT NULL,UNIQUE(tenant_id,movement_id));
        CREATE INDEX native_pool_revision ON cp6_stock_return_v1.movement_sequences(tenant_id,pool_id,sequence);
        CREATE TABLE cp6_stock_return_v1.plans(tenant_id uuid NOT NULL,plan_id uuid NOT NULL,work_id uuid NOT NULL,
          reference bytea NOT NULL,intent_ref bytea NOT NULL,body bytea NOT NULL,PRIMARY KEY(tenant_id,plan_id));
        CREATE TABLE cp6_stock_return_v1.operations(tenant_id uuid NOT NULL,plan_id uuid NOT NULL,body bytea NOT NULL,
          PRIMARY KEY(tenant_id,plan_id),FOREIGN KEY(tenant_id,plan_id) REFERENCES cp6_stock_return_v1.plans(tenant_id,plan_id));
        CREATE TABLE cp6_stock_return_v1.root_receipts(tenant_id uuid NOT NULL,root_id uuid NOT NULL,plan_id uuid NOT NULL,
          line_key text NOT NULL,ordinal integer NOT NULL,range_from numeric(21,8) NOT NULL,range_to numeric(21,8) NOT NULL,
          movement_id uuid NOT NULL,body bytea NOT NULL,PRIMARY KEY(tenant_id,plan_id,line_key,ordinal),CHECK(range_from>=0 AND range_to>range_from),
          FOREIGN KEY(tenant_id,plan_id) REFERENCES cp6_stock_return_v1.plans(tenant_id,plan_id));
        CREATE INDEX stock_return_root ON cp6_stock_return_v1.root_receipts(tenant_id,root_id);
        DO $body$ DECLARE name text; BEGIN
          FOREACH name IN ARRAY ARRAY['physical_work_history','physical_lines','physical_line_ranges','physical_receipts','inbound_outbox','inbound_receipt_sources'] LOOP
            EXECUTE format('CREATE TRIGGER immutable BEFORE UPDATE OR DELETE ON cp6_rma_formal_v1.%I FOR EACH ROW EXECUTE FUNCTION cp6_rma_formal_v1.immutable()',name);
            EXECUTE format('CREATE TRIGGER no_truncate BEFORE TRUNCATE ON cp6_rma_formal_v1.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_rma_formal_v1.immutable()',name);
          END LOOP;
          FOREACH name IN ARRAY ARRAY['plans','operations','root_receipts','movement_sequences'] LOOP
            EXECUTE format('CREATE TRIGGER immutable BEFORE UPDATE OR DELETE ON cp6_stock_return_v1.%I FOR EACH ROW EXECUTE FUNCTION cp6_rma_formal_v1.immutable()',name);
            EXECUTE format('CREATE TRIGGER no_truncate BEFORE TRUNCATE ON cp6_stock_return_v1.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_rma_formal_v1.immutable()',name);
          END LOOP;
        END $body$;
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",a.code,a.name,a.sort,t."Id",LOCALTIMESTAMP
        FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t
        CROSS JOIN (VALUES ('physical-register','登记退货实物',30),('physical-prepare','准备退货入库',31),
          ('physical-post','确认退货入库',32),('physical-seal','关闭未入库请求',33)) a(code,name,sort)
        WHERE m."MenuKey"='wms-rma' AND NOT EXISTS(SELECT FROM public."Sys_MenuAction" x
          WHERE x."TenantId"=t."Id" AND x."MenuId"=m."MenuId" AND x."ActionCode"=a.code);
        """);
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Physical return and Stock original results require forward-only migration.");
}
