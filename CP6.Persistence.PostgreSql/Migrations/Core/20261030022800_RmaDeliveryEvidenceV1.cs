using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261030022800_RmaDeliveryEvidenceV1")]
public sealed class RmaDeliveryEvidenceV1 : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE TABLE cp6_rma_formal_v1.delivery_evidence(tenant_id uuid NOT NULL,id uuid NOT NULL,actor_id uuid NOT NULL,
          outbound_id uuid NOT NULL,line_id uuid NOT NULL,warehouse_id uuid NOT NULL,site_id uuid NOT NULL,
          recorded_at timestamptz NOT NULL,input_digest text NOT NULL,body bytea NOT NULL,PRIMARY KEY(tenant_id,id));
        CREATE INDEX rma_delivery_evidence_source ON cp6_rma_formal_v1.delivery_evidence(tenant_id,outbound_id,line_id,recorded_at,id);
        CREATE TABLE cp6_rma_formal_v1.delivery_evidence_withdrawals(tenant_id uuid NOT NULL,id uuid NOT NULL,body bytea NOT NULL,
          PRIMARY KEY(tenant_id,id),FOREIGN KEY(tenant_id,id) REFERENCES cp6_rma_formal_v1.delivery_evidence(tenant_id,id));
        CREATE TRIGGER immutable BEFORE UPDATE OR DELETE ON cp6_rma_formal_v1.delivery_evidence
          FOR EACH ROW EXECUTE FUNCTION cp6_rma_formal_v1.immutable();
        CREATE TRIGGER no_truncate BEFORE TRUNCATE ON cp6_rma_formal_v1.delivery_evidence
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_rma_formal_v1.immutable();
        CREATE TRIGGER immutable BEFORE UPDATE OR DELETE ON cp6_rma_formal_v1.delivery_evidence_withdrawals
          FOR EACH ROW EXECUTE FUNCTION cp6_rma_formal_v1.immutable();
        CREATE TRIGGER no_truncate BEFORE TRUNCATE ON cp6_rma_formal_v1.delivery_evidence_withdrawals
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_rma_formal_v1.immutable();
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",a.code,a.name,a.sort,t."Id",LOCALTIMESTAMP
        FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t
        CROSS JOIN (VALUES ('evidence-record','登记客户签收证据',26),('evidence-withdraw','撤回客户签收证据',27)) a(code,name,sort)
        WHERE m."MenuKey"='wms-rma' AND NOT EXISTS(SELECT FROM public."Sys_MenuAction" x
          WHERE x."TenantId"=t."Id" AND x."MenuId"=m."MenuId" AND x."ActionCode"=a.code);
        """);
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Receipt evidence history requires forward-only migration.");
}
