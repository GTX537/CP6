using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)),Migration("20261029210000_EngineeringIterationV1")]
public sealed class EngineeringIterationV1:Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        CREATE SCHEMA cp6_engineering_iteration_v1;
        REVOKE ALL ON SCHEMA cp6_engineering_iteration_v1 FROM PUBLIC;
        CREATE TABLE cp6_engineering_iteration_v1.heads(
          tenant_id uuid NOT NULL,object_id uuid NOT NULL,creator_id uuid NOT NULL,
          kind text NOT NULL CHECK(kind IN('PROJECT','ITERATION')),project_id uuid,member_ids uuid[] NOT NULL,number text NOT NULL,name text NOT NULL,revision bigint NOT NULL CHECK(revision>0),
          UNIQUE(tenant_id,kind,number),FOREIGN KEY(tenant_id,project_id) REFERENCES cp6_engineering_iteration_v1.heads,
          CHECK((kind='PROJECT' AND project_id IS NULL AND cardinality(member_ids)>0) OR (kind='ITERATION' AND project_id IS NOT NULL AND cardinality(member_ids)=0)),
          PRIMARY KEY(tenant_id,object_id),CHECK(object_id<>'00000000-0000-0000-0000-000000000000'::uuid));
        CREATE TABLE cp6_engineering_iteration_v1.versions(
          tenant_id uuid NOT NULL,object_id uuid NOT NULL,revision bigint NOT NULL CHECK(revision>0),
          body_bytes bytea NOT NULL CHECK(octet_length(body_bytes) BETWEEN 2 AND 2097152),
          body_digest text NOT NULL CHECK(body_digest=encode(sha256(body_bytes),'hex')),actor_id uuid NOT NULL,operation_id uuid NOT NULL,
          journal text NOT NULL DEFAULT 'Main.Plm.EngineeringIteration.v1' CHECK(journal='Main.Plm.EngineeringIteration.v1'),
          PRIMARY KEY(tenant_id,object_id,revision),UNIQUE(tenant_id,operation_id),
          FOREIGN KEY(tenant_id,object_id) REFERENCES cp6_engineering_iteration_v1.heads,
          FOREIGN KEY(tenant_id,journal,operation_id) REFERENCES cp6_main_owner_v1.operations(tenant_id,owner,operation_id) DEFERRABLE INITIALLY DEFERRED);
        ALTER TABLE cp6_engineering_iteration_v1.heads ADD CONSTRAINT engineering_iteration_original
          FOREIGN KEY(tenant_id,object_id,revision) REFERENCES cp6_engineering_iteration_v1.versions DEFERRABLE INITIALLY DEFERRED;
        CREATE FUNCTION cp6_engineering_iteration_v1.advance() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP='INSERT' THEN
            IF NEW.revision<>1 THEN RAISE EXCEPTION 'ITERATION_FRONTIER_INVALID';END IF;
          ELSIF (to_jsonb(NEW)-'name'-'revision'-'member_ids') IS DISTINCT FROM (to_jsonb(OLD)-'name'-'revision'-'member_ids') OR NEW.revision<>OLD.revision+1 THEN
            RAISE EXCEPTION 'ITERATION_FRONTIER_INVALID';
          END IF;RETURN NEW;
        END $$;
        CREATE FUNCTION cp6_engineering_iteration_v1.validate() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE b jsonb=convert_from(NEW.body_bytes,'UTF8')::jsonb;p jsonb;BEGIN
          IF b->>'tenantId' IS DISTINCT FROM NEW.tenant_id::text OR b->>'id' IS DISTINCT FROM NEW.object_id::text
            OR b->>'version' IS DISTINCT FROM NEW.revision::text OR b->>'actorId' IS DISTINCT FROM NEW.actor_id::text
            OR b->>'operationId' IS DISTINCT FROM NEW.operation_id::text OR b->>'etag' IS NULL OR b->>'etag'='00000000-0000-0000-0000-000000000000'
            OR NOT EXISTS(SELECT FROM cp6_engineering_iteration_v1.heads h WHERE h.tenant_id=NEW.tenant_id AND h.object_id=NEW.object_id
              AND h.revision=NEW.revision AND h.creator_id::text=b->>'creatorId' AND h.kind=b#>>'{body,kind}' AND h.name=b#>>'{body,name}' AND h.number=b#>>'{body,number}'
              AND ((h.kind='PROJECT' AND b#>'{body,iteration}'='null'::jsonb
                AND h.member_ids=ARRAY(SELECT value::uuid FROM jsonb_array_elements_text(b#>'{body,project,definition,members}')))
                OR (h.kind='ITERATION' AND b#>'{body,project}'='null'::jsonb AND h.project_id::text=b#>>'{body,iteration,projectId}'
                  AND EXISTS(SELECT FROM cp6_engineering_iteration_v1.heads p WHERE p.tenant_id=h.tenant_id AND p.object_id=h.project_id AND p.kind='PROJECT')))) THEN
            RAISE EXCEPTION 'ITERATION_ORIGINAL_INVALID';END IF;
          IF NEW.revision=1 THEN
            IF b->>'parentVersion' IS NOT NULL OR b->>'action' IS DISTINCT FROM 'CREATE' THEN RAISE EXCEPTION 'ITERATION_PARENT_INVALID';END IF;
          ELSE
            SELECT convert_from(body_bytes,'UTF8')::jsonb INTO p FROM cp6_engineering_iteration_v1.versions
              WHERE tenant_id=NEW.tenant_id AND object_id=NEW.object_id AND revision=NEW.revision-1;
            IF p IS NULL OR b->>'parentVersion' IS DISTINCT FROM (NEW.revision-1)::text OR b->>'action' IS DISTINCT FROM 'REVISE'
              OR b->>'etag'=p->>'etag' THEN RAISE EXCEPTION 'ITERATION_PARENT_INVALID';END IF;
          END IF;RETURN NEW;
        END $$;
        CREATE FUNCTION cp6_engineering_iteration_v1.provenance() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF NOT EXISTS(SELECT FROM cp6_main_owner_v1.operations o WHERE o.tenant_id=NEW.tenant_id AND o.owner=NEW.journal
            AND o.operation_id=NEW.operation_id AND o.actor_id=NEW.actor_id AND o.contract='Main.Plm.EngineeringIteration.commands.v1'
            AND o.state='COMMITTED' AND o.object_ids=ARRAY[NEW.object_id]) THEN RAISE EXCEPTION 'ITERATION_ORIGINAL_REQUIRED';END IF;RETURN NULL;
        END $$;
        CREATE TRIGGER engineering_iteration_advance BEFORE INSERT OR UPDATE ON cp6_engineering_iteration_v1.heads FOR EACH ROW EXECUTE FUNCTION cp6_engineering_iteration_v1.advance();
        CREATE TRIGGER engineering_iteration_validate BEFORE INSERT ON cp6_engineering_iteration_v1.versions FOR EACH ROW EXECUTE FUNCTION cp6_engineering_iteration_v1.validate();
        CREATE TRIGGER engineering_iteration_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_engineering_iteration_v1.versions FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER engineering_iteration_permanent BEFORE DELETE OR TRUNCATE ON cp6_engineering_iteration_v1.heads FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE CONSTRAINT TRIGGER engineering_iteration_provenance AFTER INSERT ON cp6_engineering_iteration_v1.versions
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_engineering_iteration_v1.provenance();
        CREATE FUNCTION cp6_engineering_iteration_v1.ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT NOT EXISTS(SELECT FROM (VALUES
            ('heads','engineering_iteration_advance','cp6_engineering_iteration_v1.advance()',23),
            ('versions','engineering_iteration_validate','cp6_engineering_iteration_v1.validate()',7),
            ('versions','engineering_iteration_immutable','cp6_scope_authorization_v1.immutable()',58),
            ('heads','engineering_iteration_permanent','cp6_scope_authorization_v1.immutable()',42),
            ('versions','engineering_iteration_provenance','cp6_engineering_iteration_v1.provenance()',5)) a(tbl,name,fn,typ)
            WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid=to_regclass('cp6_engineering_iteration_v1.'||a.tbl)
              AND t.tgname=a.name AND t.tgfoid=to_regprocedure(a.fn) AND t.tgtype=a.typ AND t.tgenabled IN('O','A') AND t.tgqual IS NULL
              AND (a.name<>'engineering_iteration_provenance' OR t.tgdeferrable AND t.tginitdeferred)));
        $$;
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_engineering_iteration_v1 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_engineering_iteration_v1 FROM PUBLIC;
        -- Extend the current shared definition; preserve every previously admitted Owner.
        DO $$ DECLARE d text;f text;BEGIN
          SELECT pg_get_constraintdef(oid) INTO d FROM pg_constraint WHERE conrelid='cp6_operations_owner_v1.department_mappings'::regclass AND conname='department_mappings_object_kind_check';
          IF d IS NULL OR position('plm-requirement-set' in d)=0 THEN RAISE EXCEPTION 'ITERATION_MAPPING_BASE_UNAVAILABLE';END IF;
          EXECUTE 'ALTER TABLE cp6_operations_owner_v1.department_mappings DROP CONSTRAINT department_mappings_object_kind_check';
          EXECUTE 'ALTER TABLE cp6_operations_owner_v1.department_mappings ADD CONSTRAINT department_mappings_object_kind_check CHECK ('||substring(d from 8 for length(d)-8)||' OR object_kind=''plm-engineering-iteration'')';
          SELECT pg_get_functiondef('cp6_operations_owner_v1.mapping_validate()'::regprocedure) INTO f;
          IF position('WHEN ''plm-requirement-set''' in f)=0 THEN RAISE EXCEPTION 'ITERATION_MAPPING_BASE_UNAVAILABLE';END IF;
          EXECUTE replace(f,'WHEN ''plm-requirement-set''','WHEN ''plm-engineering-iteration'' THEN EXISTS(SELECT FROM cp6_engineering_iteration_v1.heads o WHERE o.object_id=NEW.object_id AND o.tenant_id=NEW.tenant_id AND o.kind=''PROJECT'') WHEN ''plm-requirement-set''');
        END $$;
        LOCK TABLE public."Sys_Menus" IN SHARE ROW EXCLUSIVE MODE;
        INSERT INTO public."Sys_Menus"("MenuId","MenuKey","MenuName","RoutePath","Icon","ParentId","OrderNo","Enable","CreateDate")
        SELECT (SELECT CASE WHEN COALESCE(MAX("MenuId"),0)<2147483647 THEN COALESCE(MAX("MenuId"),0)+1 ELSE
          (SELECT min(candidate) FROM (SELECT 1 AS candidate UNION SELECT "MenuId"+1 FROM public."Sys_Menus" WHERE "MenuId">=1 AND "MenuId"<2147483647) available
          WHERE NOT EXISTS(SELECT FROM public."Sys_Menus" used WHERE used."MenuId"=available.candidate)) END FROM public."Sys_Menus"),
          'plm-engineering-iteration','工程项目与迭代','/plm/engineering-iteration','Connection',NULL,158,true,LOCALTIMESTAMP
        WHERE NOT EXISTS(SELECT FROM public."Sys_Menus" WHERE "MenuKey"='plm-engineering-iteration');
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",a.code,a.name,a.sort,t."Id",LOCALTIMESTAMP FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t
          CROSS JOIN (VALUES('query','读取工程项目',0),('add','创建项目或迭代',1),('edit','编辑计划',2),('start','启动迭代',3),('hold','暂停迭代',4),('resume','恢复迭代',5),('task-submit','提交任务',6),('task-confirm','确认任务',7)) a(code,name,sort)
        WHERE m."MenuKey"='plm-engineering-iteration' AND NOT EXISTS(SELECT FROM public."Sys_MenuAction" x
          WHERE x."TenantId"=t."Id" AND x."MenuId"=m."MenuId" AND x."ActionCode"=a.code);

        """);
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Requirement originals require forward recovery.");
}
