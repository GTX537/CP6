using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261101194000_RmaCommercialV1")]
public sealed class RmaCommercialV1 : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE TABLE cp6_rma_formal_v1.commercial_requests(tenant_id uuid NOT NULL,request_id uuid NOT NULL,
          application_id uuid NOT NULL,revision integer NOT NULL CHECK(revision>0),body bytea NOT NULL,
          PRIMARY KEY(tenant_id,request_id),FOREIGN KEY(tenant_id,application_id) REFERENCES cp6_rma_formal_v1.heads(tenant_id,id));
        CREATE INDEX commercial_application ON cp6_rma_formal_v1.commercial_requests(tenant_id,application_id);
        CREATE TABLE cp6_rma_formal_v1.commercial_ranges(tenant_id uuid NOT NULL,request_id uuid NOT NULL,coordinate_key text NOT NULL,
          range_from numeric(21,8) NOT NULL,range_to numeric(21,8) NOT NULL,CHECK(range_from>=0 AND range_to>range_from),
          PRIMARY KEY(tenant_id,request_id,coordinate_key,range_from),
          FOREIGN KEY(tenant_id,request_id) REFERENCES cp6_rma_formal_v1.commercial_requests(tenant_id,request_id));
        CREATE INDEX commercial_coordinate ON cp6_rma_formal_v1.commercial_ranges(tenant_id,coordinate_key,range_from);
        CREATE TRIGGER immutable BEFORE UPDATE OR DELETE ON cp6_rma_formal_v1.commercial_ranges
          FOR EACH ROW EXECUTE FUNCTION cp6_rma_formal_v1.immutable();
        CREATE TRIGGER no_truncate BEFORE TRUNCATE ON cp6_rma_formal_v1.commercial_ranges
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_rma_formal_v1.immutable();
        CREATE TABLE cp6_rma_formal_v1.commercial_history(tenant_id uuid NOT NULL,request_id uuid NOT NULL,revision integer NOT NULL,
          body bytea NOT NULL,PRIMARY KEY(tenant_id,request_id,revision),
          FOREIGN KEY(tenant_id,request_id) REFERENCES cp6_rma_formal_v1.commercial_requests(tenant_id,request_id));
        CREATE TRIGGER immutable BEFORE UPDATE OR DELETE ON cp6_rma_formal_v1.commercial_history
          FOR EACH ROW EXECUTE FUNCTION cp6_rma_formal_v1.immutable();
        CREATE TRIGGER no_truncate BEFORE TRUNCATE ON cp6_rma_formal_v1.commercial_history
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_rma_formal_v1.immutable();
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",a.code,a.name,a.sort,t."Id",LOCALTIMESTAMP
        FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t
        CROSS JOIN (VALUES ('commercial-query','查询退货商业及财务结果',40),('commercial-request','申请退货商业结果',41),
          ('commercial-dispatch','提交原商业请求至外部Owner',42)) a(code,name,sort)
        WHERE m."MenuKey"='wms-rma' AND NOT EXISTS(SELECT FROM public."Sys_MenuAction" x
          WHERE x."TenantId"=t."Id" AND x."MenuId"=m."MenuId" AND x."ActionCode"=a.code);
        """);
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Commercial requests and observations are forward-only.");
}
