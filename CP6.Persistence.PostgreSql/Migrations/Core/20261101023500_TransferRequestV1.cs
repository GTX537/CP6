using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261101023500_TransferRequestV1")]
public sealed class TransferRequestV1 : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE SCHEMA cp6_transfer_request_v1;
        REVOKE ALL ON SCHEMA cp6_transfer_request_v1 FROM PUBLIC;
        CREATE TABLE cp6_transfer_request_v1.heads(
          tenant_id uuid NOT NULL REFERENCES public."Sys_Tenants"("Id"),id uuid NOT NULL,
          draft_key text COLLATE "C" NOT NULL,creator_id uuid NOT NULL,revision bigint NOT NULL CHECK(revision>0),
          version bigint NOT NULL CHECK(version>0),state text NOT NULL CHECK(state IN('DRAFT','SUBMITTED','APPROVED','REJECTED','WITHDRAWN')),
          body bytea NOT NULL,PRIMARY KEY(tenant_id,id),UNIQUE(tenant_id,creator_id,draft_key));
        CREATE TABLE cp6_transfer_request_v1.versions(
          tenant_id uuid NOT NULL,id uuid NOT NULL,revision bigint NOT NULL,version bigint NOT NULL,body bytea NOT NULL,
          PRIMARY KEY(tenant_id,id,revision),FOREIGN KEY(tenant_id,id) REFERENCES cp6_transfer_request_v1.heads(tenant_id,id));
        CREATE TABLE cp6_transfer_request_v1.events(
          tenant_id uuid NOT NULL,event_id uuid NOT NULL,request_id uuid NOT NULL,revision bigint NOT NULL,
          action text NOT NULL,body bytea NOT NULL,created_at timestamptz NOT NULL DEFAULT now(),
          PRIMARY KEY(tenant_id,event_id),UNIQUE(tenant_id,request_id,revision),
          FOREIGN KEY(tenant_id,request_id,revision) REFERENCES cp6_transfer_request_v1.versions(tenant_id,id,revision));
        CREATE TRIGGER immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_transfer_request_v1.versions
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_rma_formal_v1.immutable();
        CREATE TRIGGER immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_transfer_request_v1.events
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_rma_formal_v1.immutable();
        CREATE FUNCTION cp6_transfer_request_v1.transition() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF (NEW.tenant_id,NEW.id,NEW.creator_id,NEW.draft_key) IS DISTINCT FROM (OLD.tenant_id,OLD.id,OLD.creator_id,OLD.draft_key)
            OR NEW.revision<>OLD.revision+1
            OR NOT ((OLD.state='DRAFT' AND NEW.state='DRAFT' AND NEW.version=OLD.version+1)
                 OR (OLD.state='DRAFT' AND NEW.state IN('SUBMITTED','WITHDRAWN') AND NEW.version=OLD.version)
                 OR (OLD.state='SUBMITTED' AND NEW.state IN('APPROVED','REJECTED','WITHDRAWN') AND NEW.version=OLD.version))
          THEN RAISE EXCEPTION 'TRANSFER_REQUEST_TRANSITION_INVALID'; END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER transition BEFORE UPDATE ON cp6_transfer_request_v1.heads
          FOR EACH ROW EXECUTE FUNCTION cp6_transfer_request_v1.transition();
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_transfer_request_v1 FROM PUBLIC;
        DO $$ DECLARE d text;f text;BEGIN
          SELECT pg_get_constraintdef(oid) INTO d FROM pg_constraint WHERE conrelid='cp6_operations_owner_v1.department_mappings'::regclass AND conname='department_mappings_object_kind_check';
          ALTER TABLE cp6_operations_owner_v1.department_mappings DROP CONSTRAINT department_mappings_object_kind_check;
          EXECUTE 'ALTER TABLE cp6_operations_owner_v1.department_mappings ADD CONSTRAINT department_mappings_object_kind_check CHECK ('||substring(d from 8 for length(d)-8)||' OR object_kind=''transfer-request'')';
          SELECT pg_get_functiondef('cp6_operations_owner_v1.mapping_validate()'::regprocedure) INTO f;
          EXECUTE replace(f,'WHEN ''inventory-identity''','WHEN ''transfer-request'' THEN EXISTS(SELECT FROM cp6_transfer_request_v1.heads o WHERE o.id=NEW.object_id AND o.tenant_id=NEW.tenant_id) WHEN ''inventory-identity''');
        END $$;
        LOCK TABLE public."Sys_Menus" IN SHARE ROW EXCLUSIVE MODE;
        INSERT INTO public."Sys_Menus"("MenuId","MenuKey","MenuName","RoutePath","Icon","ParentId","OrderNo","Enable","CreateDate")
        SELECT CASE WHEN COALESCE(MAX("MenuId"),0)<2147483647 THEN COALESCE(MAX("MenuId"),0)+1 ELSE
          (SELECT min(candidate) FROM (SELECT 1 AS candidate UNION SELECT "MenuId"+1 FROM public."Sys_Menus" WHERE "MenuId">=1 AND "MenuId"<2147483647) a
           WHERE NOT EXISTS(SELECT FROM public."Sys_Menus" x WHERE x."MenuId"=a.candidate)) END,'wms-transfer-request','调拨请求','/wms/transfer-requests','Switch',NULL,161,true,LOCALTIMESTAMP
        FROM public."Sys_Menus" HAVING NOT EXISTS(SELECT FROM public."Sys_Menus" WHERE "MenuKey"='wms-transfer-request');
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",a.code,a.name,a.sort,t."Id",LOCALTIMESTAMP
        FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t
        CROSS JOIN (VALUES('query','读取调拨请求',0),('add','创建调拨请求',1),('edit','编辑草稿',2),
          ('submit','提交审批',3),('approve','独立审批',4),('withdraw','撤回请求',5)) a(code,name,sort)
        WHERE m."MenuKey"='wms-transfer-request';
        """);
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Forward-only business history.");
}
