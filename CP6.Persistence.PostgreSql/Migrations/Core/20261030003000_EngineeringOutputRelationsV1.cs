using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261030003000_EngineeringOutputRelationsV1")]
public sealed class EngineeringOutputRelationsV1 : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE SCHEMA cp6_output_relations_v1;
        REVOKE ALL ON SCHEMA cp6_output_relations_v1 FROM PUBLIC;
        CREATE TABLE cp6_output_relations_v1.heads(
          environment_id uuid NOT NULL,tenant_id uuid NOT NULL,relation_id uuid NOT NULL,creator_id uuid NOT NULL,
          product_id uuid NOT NULL,engineering_revision_id uuid NOT NULL,name text NOT NULL,draft_revision bigint NOT NULL CHECK(draft_revision>0),
          control_revision bigint NOT NULL CHECK(control_revision>=0),PRIMARY KEY(environment_id,tenant_id,relation_id),
          FOREIGN KEY(tenant_id,product_id) REFERENCES cp6_engineering_product_v1.heads(tenant_id,product_id));
        CREATE TABLE cp6_output_relations_v1.versions(
          environment_id uuid NOT NULL,tenant_id uuid NOT NULL,relation_id uuid NOT NULL,revision bigint NOT NULL CHECK(revision>0),
          body bytea NOT NULL CHECK(octet_length(body) BETWEEN 2 AND 33554432),digest text NOT NULL CHECK(digest=encode(sha256(body),'hex')),
          operation_id uuid NOT NULL,actor_id uuid NOT NULL,
          journal text NOT NULL DEFAULT 'Main.Plm.EngineeringOutputRelations.operations.v1' CHECK(journal='Main.Plm.EngineeringOutputRelations.operations.v1'),
          PRIMARY KEY(environment_id,tenant_id,relation_id,revision),
          FOREIGN KEY(environment_id,tenant_id,relation_id) REFERENCES cp6_output_relations_v1.heads,
          FOREIGN KEY(tenant_id,journal,operation_id) REFERENCES cp6_main_owner_v1.operations(tenant_id,owner,operation_id) DEFERRABLE INITIALLY DEFERRED);
        CREATE TABLE cp6_output_relations_v1.controls(
          environment_id uuid NOT NULL,tenant_id uuid NOT NULL,relation_id uuid NOT NULL,revision bigint NOT NULL CHECK(revision>0),
          body bytea NOT NULL,digest text NOT NULL CHECK(digest=encode(sha256(body),'hex')),operation_id uuid NOT NULL,actor_id uuid NOT NULL,
          journal text NOT NULL DEFAULT 'Main.Plm.EngineeringOutputRelations.operations.v1' CHECK(journal='Main.Plm.EngineeringOutputRelations.operations.v1'),
          PRIMARY KEY(environment_id,tenant_id,relation_id,revision),
          FOREIGN KEY(environment_id,tenant_id,relation_id) REFERENCES cp6_output_relations_v1.heads,
          FOREIGN KEY(tenant_id,journal,operation_id) REFERENCES cp6_main_owner_v1.operations(tenant_id,owner,operation_id) DEFERRABLE INITIALLY DEFERRED);
        CREATE TABLE cp6_output_relations_v1.records(
          environment_id uuid NOT NULL,tenant_id uuid NOT NULL,relation_id uuid NOT NULL,id uuid NOT NULL,
          kind text NOT NULL CHECK(kind IN('POLICY','QUALIFICATION')),
          body bytea NOT NULL,digest text NOT NULL CHECK(digest=encode(sha256(body),'hex')),operation_id uuid NOT NULL,actor_id uuid NOT NULL,
          journal text NOT NULL DEFAULT 'Main.Plm.EngineeringOutputRelations.operations.v1' CHECK(journal='Main.Plm.EngineeringOutputRelations.operations.v1'),
          PRIMARY KEY(environment_id,tenant_id,id),
          FOREIGN KEY(environment_id,tenant_id,relation_id) REFERENCES cp6_output_relations_v1.heads,
          FOREIGN KEY(tenant_id,journal,operation_id) REFERENCES cp6_main_owner_v1.operations(tenant_id,owner,operation_id) DEFERRABLE INITIALLY DEFERRED);
        CREATE FUNCTION cp6_output_relations_v1.validate() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE p jsonb;h cp6_output_relations_v1.heads; BEGIN
          IF TG_TABLE_NAME='heads' THEN
            IF TG_OP='UPDATE' AND ((NEW.environment_id,NEW.tenant_id,NEW.relation_id,NEW.creator_id,NEW.product_id,NEW.engineering_revision_id)
              IS DISTINCT FROM (OLD.environment_id,OLD.tenant_id,OLD.relation_id,OLD.creator_id,OLD.product_id,OLD.engineering_revision_id)
              OR NEW.draft_revision NOT IN(OLD.draft_revision,OLD.draft_revision+1) OR NEW.control_revision NOT IN(OLD.control_revision,OLD.control_revision+1)) THEN
              RAISE EXCEPTION 'OUTPUT_RELATIONS_HEAD_INVALID'; END IF;
            RETURN NEW;
          END IF;
          p=convert_from(NEW.body,'UTF8')::jsonb;
          SELECT * INTO h FROM cp6_output_relations_v1.heads WHERE environment_id=NEW.environment_id AND tenant_id=NEW.tenant_id AND relation_id=NEW.relation_id;
          IF TG_TABLE_NAME='versions' THEN
            IF p->>'environmentId' IS DISTINCT FROM NEW.environment_id::text OR p->>'tenantId' IS DISTINCT FROM NEW.tenant_id::text
              OR p->>'id' IS DISTINCT FROM NEW.relation_id::text OR p->>'version' IS DISTINCT FROM NEW.revision::text
              OR p->>'creatorId' IS DISTINCT FROM h.creator_id::text OR p->>'authorId' IS DISTINCT FROM NEW.actor_id::text
              OR p#>>'{product,id}' IS DISTINCT FROM h.product_id::text OR p#>>'{revisionOrigin,id}' IS DISTINCT FROM h.engineering_revision_id::text
              OR coalesce(p->>'evidenceMode','') NOT IN('REAL','DEMO') OR NEW.revision<>h.draft_revision THEN RAISE EXCEPTION 'OUTPUT_RELATIONS_VERSION_INVALID'; END IF;
          ELSIF TG_TABLE_NAME='controls' THEN
            IF p->>'relationId' IS DISTINCT FROM NEW.relation_id::text OR p->>'revision' IS DISTINCT FROM NEW.revision::text
              OR p->>'actorId' IS DISTINCT FROM NEW.actor_id::text OR p->>'draftVersion' IS DISTINCT FROM h.draft_revision::text
              OR coalesce(p->>'state','') NOT IN('DRAFT','QUALIFIED','WITHDRAWN') OR NEW.revision<>h.control_revision+1 THEN
              RAISE EXCEPTION 'OUTPUT_RELATIONS_CONTROL_INVALID'; END IF;
          ELSE
            IF p->>'id' IS DISTINCT FROM NEW.id::text OR p->>'relationId' IS DISTINCT FROM NEW.relation_id::text
              OR p->>'actorId' IS DISTINCT FROM NEW.actor_id::text THEN RAISE EXCEPTION 'OUTPUT_RELATIONS_RECORD_INVALID'; END IF;
          END IF; RETURN NEW;
        END $$;
        CREATE FUNCTION cp6_output_relations_v1.complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE h cp6_output_relations_v1.heads;c jsonb;p jsonb;q jsonb;policy jsonb;v jsonb; BEGIN
          SELECT * INTO h FROM cp6_output_relations_v1.heads WHERE environment_id=NEW.environment_id AND tenant_id=NEW.tenant_id AND relation_id=NEW.relation_id;
          SELECT convert_from(body,'UTF8')::jsonb INTO c FROM cp6_output_relations_v1.controls WHERE environment_id=h.environment_id AND tenant_id=h.tenant_id AND relation_id=h.relation_id AND revision=h.control_revision;
          IF c IS NULL OR c->>'draftVersion' IS DISTINCT FROM h.draft_revision::text OR NOT EXISTS(SELECT FROM cp6_output_relations_v1.versions
            WHERE environment_id=h.environment_id AND tenant_id=h.tenant_id AND relation_id=h.relation_id AND revision=h.draft_revision) THEN RAISE EXCEPTION 'OUTPUT_RELATIONS_HEAD_INCOMPLETE'; END IF;
          IF TG_TABLE_NAME='heads' THEN RETURN NULL; END IF;
          p=convert_from(NEW.body,'UTF8')::jsonb;
          IF NOT EXISTS(SELECT FROM cp6_main_owner_v1.operations o WHERE o.tenant_id=NEW.tenant_id AND o.owner=NEW.journal
            AND o.contract='Main.Plm.EngineeringOutputRelations.commands.v1' AND o.operation_id=NEW.operation_id AND o.actor_id=NEW.actor_id
            AND o.state='COMMITTED' AND o.object_ids=ARRAY[NEW.relation_id]) THEN RAISE EXCEPTION 'OUTPUT_RELATIONS_ORIGINAL_REQUIRED'; END IF;
          IF TG_TABLE_NAME='controls' THEN
            IF NEW.revision>h.control_revision THEN RAISE EXCEPTION 'OUTPUT_RELATIONS_CONTROL_INCOMPLETE'; END IF;
            IF p->>'policyId' IS NOT NULL AND NOT EXISTS(SELECT FROM cp6_output_relations_v1.records WHERE environment_id=NEW.environment_id AND tenant_id=NEW.tenant_id AND relation_id=NEW.relation_id AND kind='POLICY' AND id=(p->>'policyId')::uuid) THEN RAISE EXCEPTION 'OUTPUT_RELATIONS_POLICY_REQUIRED'; END IF;
            IF p->>'state'='QUALIFIED' THEN
              SELECT convert_from(body,'UTF8')::jsonb INTO q FROM cp6_output_relations_v1.records WHERE environment_id=NEW.environment_id AND tenant_id=NEW.tenant_id AND relation_id=NEW.relation_id AND kind='QUALIFICATION' AND id=(p->>'qualificationId')::uuid;
              IF q IS NULL OR q#>>'{relation,version}' IS DISTINCT FROM p->>'draftVersion' OR q#>>'{policy,id}' IS DISTINCT FROM p->>'policyId' THEN RAISE EXCEPTION 'OUTPUT_RELATIONS_QUALIFICATION_REQUIRED'; END IF;
            ELSIF p->>'qualificationId' IS NOT NULL THEN RAISE EXCEPTION 'OUTPUT_RELATIONS_CONTROL_INVALID'; END IF;
          ELSIF TG_TABLE_NAME='records' THEN
            IF NEW.kind='POLICY' THEN
              IF coalesce((p#>>'{input,roleId}')::integer,0)<=0 OR (p#>>'{input,effectiveTo}')::timestamptz <= (p#>>'{input,effectiveFrom}')::timestamptz THEN RAISE EXCEPTION 'OUTPUT_RELATIONS_POLICY_INVALID'; END IF;
            ELSE
              SELECT convert_from(body,'UTF8')::jsonb INTO policy FROM cp6_output_relations_v1.records WHERE environment_id=NEW.environment_id AND tenant_id=NEW.tenant_id AND relation_id=NEW.relation_id AND kind='POLICY' AND id=(p#>>'{policy,id}')::uuid AND digest=p#>>'{policy,digest}';
              SELECT convert_from(body,'UTF8')::jsonb INTO v FROM cp6_output_relations_v1.versions WHERE environment_id=NEW.environment_id AND tenant_id=NEW.tenant_id AND relation_id=NEW.relation_id AND revision::text=p#>>'{relation,version}' AND digest=p#>>'{relation,digest}';
              IF policy IS NULL OR v IS NULL OR p#>>'{relation,id}' IS DISTINCT FROM NEW.relation_id::text OR p#>>'{relation,owner}' IS DISTINCT FROM 'Main.Plm.EngineeringOutputRelations.v1'
                OR p#>>'{policy,owner}' IS DISTINCT FROM 'Main.Plm.OutputRelationsPolicy.v1' OR p#>>'{policy,version}' IS DISTINCT FROM '1'
                OR p->>'actorId' IN(v->>'authorId',v->>'creatorId') OR p->>'sourceFingerprint' IS DISTINCT FROM v->>'sourceFingerprint'
                OR p->>'relationDigest' IS DISTINCT FROM v#>>'{wire,bodyDigest}' OR p->>'roleId' IS DISTINCT FROM policy#>>'{input,roleId}'
                THEN RAISE EXCEPTION 'OUTPUT_RELATIONS_QUALIFICATION_PROOF_INVALID'; END IF;
            END IF;
          END IF; RETURN NULL;
        END $$;
        DO $$ DECLARE tbl text; BEGIN FOREACH tbl IN ARRAY ARRAY['heads','versions','controls','records'] LOOP
          EXECUTE format('CREATE TRIGGER output_relations_validate BEFORE INSERT OR UPDATE ON cp6_output_relations_v1.%I FOR EACH ROW EXECUTE FUNCTION cp6_output_relations_v1.validate()',tbl);
          EXECUTE format('CREATE TRIGGER output_relations_root BEFORE INSERT OR UPDATE ON cp6_output_relations_v1.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change()',tbl);
          EXECUTE format('CREATE CONSTRAINT TRIGGER output_relations_complete AFTER INSERT OR UPDATE ON cp6_output_relations_v1.%I DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_output_relations_v1.complete()',tbl);
          IF tbl='heads' THEN
            EXECUTE 'CREATE TRIGGER output_relations_permanent BEFORE DELETE OR TRUNCATE ON cp6_output_relations_v1.heads FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable()';
          ELSE
            EXECUTE format('CREATE TRIGGER output_relations_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_output_relations_v1.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable()',tbl);
          END IF;
        END LOOP; END $$;
        CREATE FUNCTION cp6_output_relations_v1.ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT NOT EXISTS(SELECT FROM (VALUES('heads'),('versions'),('controls'),('records')) a(tbl)
            CROSS JOIN(VALUES('output_relations_validate','cp6_output_relations_v1.validate()',23),('output_relations_root','cp6_authentication_v1.before_change()',22),('output_relations_complete','cp6_output_relations_v1.complete()',21)) b(name,fn,typ)
            WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid=to_regclass('cp6_output_relations_v1.'||a.tbl) AND t.tgname=b.name AND t.tgfoid=to_regprocedure(b.fn)
              AND t.tgtype=b.typ AND t.tgenabled IN('O','A') AND t.tgqual IS NULL AND (b.name<>'output_relations_complete' OR t.tgdeferrable AND t.tginitdeferred)))
          AND NOT EXISTS(SELECT FROM (VALUES('heads','output_relations_permanent',42),('versions','output_relations_immutable',58),('controls','output_relations_immutable',58),('records','output_relations_immutable',58)) a(tbl,name,typ)
            WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid=to_regclass('cp6_output_relations_v1.'||a.tbl) AND t.tgname=a.name
              AND t.tgfoid='cp6_scope_authorization_v1.immutable()'::regprocedure AND t.tgtype=a.typ AND t.tgenabled IN('O','A') AND t.tgqual IS NULL));
        $$;
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_output_relations_v1 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_output_relations_v1 FROM PUBLIC;
        LOCK TABLE public."Sys_Menus" IN SHARE ROW EXCLUSIVE MODE;
        INSERT INTO public."Sys_Menus"("MenuId","MenuKey","MenuName","RoutePath","Icon","ParentId","OrderNo","Enable","CreateDate")
        SELECT (SELECT CASE WHEN COALESCE(MAX("MenuId"),0)<2147483647 THEN COALESCE(MAX("MenuId"),0)+1 ELSE
          (SELECT min(candidate) FROM (SELECT 1 AS candidate UNION SELECT "MenuId"+1 FROM public."Sys_Menus" WHERE "MenuId">=1 AND "MenuId"<2147483647) available
          WHERE NOT EXISTS(SELECT FROM public."Sys_Menus" used WHERE used."MenuId"=available.candidate)) END FROM public."Sys_Menus"),'plm-engineering-output-relations','工程输出关系','/plm/engineering-output-relations','Connection',NULL,158,true,LOCALTIMESTAMP
          WHERE NOT EXISTS(SELECT FROM public."Sys_Menus" WHERE "MenuKey"='plm-engineering-output-relations');
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",a.code,a.name,a.sort,t."Id",LOCALTIMESTAMP FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t
          CROSS JOIN(VALUES('query','读取输出关系',0),('add','创建输出关系',1),('edit','修订输出关系',2),('review-policy','关系专业规则',3),('qualify','独立专业确认',4),('withdraw','撤回关系确认',5)) a(code,name,sort)
          WHERE m."MenuKey"='plm-engineering-output-relations' AND NOT EXISTS(SELECT FROM public."Sys_MenuAction" x WHERE x."TenantId"=t."Id" AND x."MenuId"=m."MenuId" AND x."ActionCode"=a.code);
        """);
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Output relation originals and professional confirmations require forward recovery.");
}
