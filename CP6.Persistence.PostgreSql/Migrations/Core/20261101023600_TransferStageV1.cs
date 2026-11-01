using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261101023600_TransferStageV1")]
public sealed class TransferStageV1 : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE TABLE cp6_transfer_request_v1.stage_occurrences(
          tenant_id uuid NOT NULL,stage_id uuid NOT NULL,request_id uuid NOT NULL,
          device text COLLATE "C" NOT NULL,scan_key text COLLATE "C" NOT NULL,input_digest text NOT NULL,
          PRIMARY KEY(tenant_id,stage_id),UNIQUE(tenant_id,device,scan_key),
          FOREIGN KEY(tenant_id,request_id) REFERENCES cp6_transfer_request_v1.heads(tenant_id,id));
        CREATE TRIGGER immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_transfer_request_v1.stage_occurrences
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_rma_formal_v1.immutable();
        REVOKE ALL ON cp6_transfer_request_v1.stage_occurrences FROM PUBLIC;
        DO $$ DECLARE f text;BEGIN
          SELECT pg_get_functiondef('cp6_transfer_request_v1.transition()'::regprocedure) INTO f;
          EXECUTE replace(f,'OR (OLD.state=''SUBMITTED''',
            'OR (OLD.state=''APPROVED'' AND NEW.state=''APPROVED'' AND NEW.version=OLD.version) OR (OLD.state=''SUBMITTED''');
        END $$;
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",a.code,a.name,a.sort,t."Id",LOCALTIMESTAMP
        FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t
        CROSS JOIN (VALUES('prepare','准备调拨阶段',6),('execute','执行调拨阶段',7),('close-stage','关闭未执行阶段',8)) a(code,name,sort)
        WHERE m."MenuKey"='wms-transfer-request';
        """);
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Forward-only business history.");
}
