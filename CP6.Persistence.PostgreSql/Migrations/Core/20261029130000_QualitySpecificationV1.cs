using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261029130000_QualitySpecificationV1")]
public sealed class QualitySpecificationV1 : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE SCHEMA cp6_quality_specification_v1;
        REVOKE ALL ON SCHEMA cp6_quality_specification_v1 FROM PUBLIC;
        CREATE TABLE cp6_quality_specification_v1.heads(
          environment_id uuid NOT NULL,tenant_id uuid NOT NULL,specification_id uuid NOT NULL,creator_id uuid NOT NULL,
          product_id uuid NOT NULL,site_id uuid NOT NULL,name text NOT NULL,draft_revision bigint NOT NULL CHECK(draft_revision>0),
          control_revision bigint NOT NULL CHECK(control_revision>=0),PRIMARY KEY(environment_id,tenant_id,specification_id),
          FOREIGN KEY(tenant_id,product_id) REFERENCES cp6_engineering_product_v1.heads(tenant_id,product_id));
        CREATE TABLE cp6_quality_specification_v1.versions(
          environment_id uuid NOT NULL,tenant_id uuid NOT NULL,specification_id uuid NOT NULL,revision bigint NOT NULL CHECK(revision>0),
          body bytea NOT NULL CHECK(octet_length(body) BETWEEN 2 AND 33554432),digest text NOT NULL CHECK(digest=encode(sha256(body),'hex')),
          operation_id uuid NOT NULL,actor_id uuid NOT NULL,
          journal text NOT NULL DEFAULT 'Main.Quality.Specification.operations.v1' CHECK(journal='Main.Quality.Specification.operations.v1'),
          PRIMARY KEY(environment_id,tenant_id,specification_id,revision),
          FOREIGN KEY(environment_id,tenant_id,specification_id) REFERENCES cp6_quality_specification_v1.heads,
          FOREIGN KEY(tenant_id,journal,operation_id) REFERENCES cp6_main_owner_v1.operations(tenant_id,owner,operation_id) DEFERRABLE INITIALLY DEFERRED);
        CREATE TABLE cp6_quality_specification_v1.controls(
          environment_id uuid NOT NULL,tenant_id uuid NOT NULL,specification_id uuid NOT NULL,revision bigint NOT NULL CHECK(revision>0),
          body bytea NOT NULL,digest text NOT NULL CHECK(digest=encode(sha256(body),'hex')),operation_id uuid NOT NULL,actor_id uuid NOT NULL,
          journal text NOT NULL DEFAULT 'Main.Quality.Specification.operations.v1' CHECK(journal='Main.Quality.Specification.operations.v1'),
          PRIMARY KEY(environment_id,tenant_id,specification_id,revision),
          FOREIGN KEY(environment_id,tenant_id,specification_id) REFERENCES cp6_quality_specification_v1.heads,
          FOREIGN KEY(tenant_id,journal,operation_id) REFERENCES cp6_main_owner_v1.operations(tenant_id,owner,operation_id) DEFERRABLE INITIALLY DEFERRED);
        CREATE TABLE cp6_quality_specification_v1.records(
          environment_id uuid NOT NULL,tenant_id uuid NOT NULL,specification_id uuid NOT NULL,id uuid NOT NULL,
          kind text NOT NULL CHECK(kind IN('POLICY','PACKET','VOTE','APPROVAL')),packet_id uuid,CHECK((kind='VOTE')=(packet_id IS NOT NULL)),
          body bytea NOT NULL,digest text NOT NULL CHECK(digest=encode(sha256(body),'hex')),operation_id uuid NOT NULL,actor_id uuid NOT NULL,
          journal text NOT NULL DEFAULT 'Main.Quality.Specification.operations.v1' CHECK(journal='Main.Quality.Specification.operations.v1'),
          PRIMARY KEY(environment_id,tenant_id,id),
          FOREIGN KEY(environment_id,tenant_id,specification_id) REFERENCES cp6_quality_specification_v1.heads,
          FOREIGN KEY(tenant_id,journal,operation_id) REFERENCES cp6_main_owner_v1.operations(tenant_id,owner,operation_id) DEFERRABLE INITIALLY DEFERRED);
        CREATE UNIQUE INDEX quality_vote_once ON cp6_quality_specification_v1.records(environment_id,tenant_id,
          packet_id,actor_id) WHERE kind='VOTE';
        CREATE FUNCTION cp6_quality_specification_v1.validate() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE p jsonb;h cp6_quality_specification_v1.heads; BEGIN
          IF TG_TABLE_NAME='heads' THEN
            IF TG_OP='UPDATE' AND ((NEW.environment_id,NEW.tenant_id,NEW.specification_id,NEW.creator_id,NEW.product_id,NEW.site_id)
              IS DISTINCT FROM (OLD.environment_id,OLD.tenant_id,OLD.specification_id,OLD.creator_id,OLD.product_id,OLD.site_id)
              OR NEW.draft_revision NOT IN(OLD.draft_revision,OLD.draft_revision+1) OR NEW.control_revision NOT IN(OLD.control_revision,OLD.control_revision+1)) THEN
              RAISE EXCEPTION 'QUALITY_SPEC_HEAD_INVALID'; END IF;
            RETURN NEW;
          END IF;
          p=convert_from(NEW.body,'UTF8')::jsonb;
          SELECT * INTO h FROM cp6_quality_specification_v1.heads WHERE environment_id=NEW.environment_id AND tenant_id=NEW.tenant_id AND specification_id=NEW.specification_id;
          IF TG_TABLE_NAME='versions' THEN
            IF p->>'environmentId' IS DISTINCT FROM NEW.environment_id::text OR p->>'tenantId' IS DISTINCT FROM NEW.tenant_id::text
              OR p->>'id' IS DISTINCT FROM NEW.specification_id::text OR p->>'version' IS DISTINCT FROM NEW.revision::text
              OR p->>'creatorId' IS DISTINCT FROM h.creator_id::text OR p->>'authorId' IS DISTINCT FROM NEW.actor_id::text
              OR p#>>'{body,product,id}' IS DISTINCT FROM h.product_id::text OR p#>>'{body,site,id}' IS DISTINCT FROM h.site_id::text
              OR coalesce(p->>'evidenceMode','') NOT IN('REAL','DEMO') OR NEW.revision<>h.draft_revision THEN RAISE EXCEPTION 'QUALITY_SPEC_VERSION_INVALID'; END IF;
          ELSIF TG_TABLE_NAME='controls' THEN
            IF p->>'specificationId' IS DISTINCT FROM NEW.specification_id::text OR p->>'revision' IS DISTINCT FROM NEW.revision::text
              OR p->>'actorId' IS DISTINCT FROM NEW.actor_id::text OR p->>'draftVersion' IS DISTINCT FROM h.draft_revision::text
              OR coalesce(p->>'state','') NOT IN('DRAFT','IN_REVIEW','APPROVED','RETURNED','WITHDRAWN') OR NEW.revision<>h.control_revision+1 THEN
              RAISE EXCEPTION 'QUALITY_SPEC_CONTROL_INVALID'; END IF;
          ELSE
            IF p->>'id' IS DISTINCT FROM NEW.id::text OR (NEW.kind='VOTE' AND (p->>'actorId' IS DISTINCT FROM NEW.actor_id::text OR p->>'packetId' IS DISTINCT FROM NEW.packet_id::text))
              OR (NEW.kind='POLICY' AND (p->>'specificationId' IS DISTINCT FROM NEW.specification_id::text OR p->>'actorId' IS DISTINCT FROM NEW.actor_id::text)) THEN
              RAISE EXCEPTION 'QUALITY_SPEC_RECORD_INVALID'; END IF;
          END IF; RETURN NEW;
        END $$;
        CREATE FUNCTION cp6_quality_specification_v1.complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE h cp6_quality_specification_v1.heads;c jsonb;p jsonb;r jsonb;pk jsonb;policy jsonb; BEGIN
          SELECT * INTO h FROM cp6_quality_specification_v1.heads WHERE environment_id=NEW.environment_id AND tenant_id=NEW.tenant_id AND specification_id=NEW.specification_id;
          SELECT convert_from(body,'UTF8')::jsonb INTO c FROM cp6_quality_specification_v1.controls WHERE environment_id=h.environment_id AND tenant_id=h.tenant_id AND specification_id=h.specification_id AND revision=h.control_revision;
          IF c IS NULL OR c->>'draftVersion' IS DISTINCT FROM h.draft_revision::text OR NOT EXISTS(SELECT FROM cp6_quality_specification_v1.versions
            WHERE environment_id=h.environment_id AND tenant_id=h.tenant_id AND specification_id=h.specification_id AND revision=h.draft_revision) THEN RAISE EXCEPTION 'QUALITY_SPEC_HEAD_INCOMPLETE'; END IF;
          IF TG_TABLE_NAME='heads' THEN RETURN NULL; END IF;
          p=convert_from(NEW.body,'UTF8')::jsonb;
          IF NOT EXISTS(SELECT FROM cp6_main_owner_v1.operations o WHERE o.tenant_id=NEW.tenant_id AND o.owner=NEW.journal
            AND o.contract='Main.Quality.Specification.commands.v1' AND o.operation_id=NEW.operation_id AND o.actor_id=NEW.actor_id
            AND o.state='COMMITTED' AND o.object_ids=ARRAY[NEW.specification_id]) THEN RAISE EXCEPTION 'QUALITY_SPEC_ORIGINAL_REQUIRED'; END IF;
          IF TG_TABLE_NAME='controls' THEN
            IF NEW.revision>h.control_revision THEN RAISE EXCEPTION 'QUALITY_SPEC_CONTROL_WRITE_INCOMPLETE'; END IF;
            IF (p->>'policyId' IS NOT NULL AND NOT EXISTS(SELECT FROM cp6_quality_specification_v1.records WHERE environment_id=NEW.environment_id AND tenant_id=NEW.tenant_id AND specification_id=NEW.specification_id AND kind='POLICY' AND id=(p->>'policyId')::uuid))
              OR (p->>'packetId' IS NOT NULL AND NOT EXISTS(SELECT FROM cp6_quality_specification_v1.records WHERE environment_id=NEW.environment_id AND tenant_id=NEW.tenant_id AND specification_id=NEW.specification_id AND kind='PACKET' AND id=(p->>'packetId')::uuid))
              OR (p->>'approvalId' IS NOT NULL AND NOT EXISTS(SELECT FROM cp6_quality_specification_v1.records WHERE environment_id=NEW.environment_id AND tenant_id=NEW.tenant_id AND specification_id=NEW.specification_id AND kind='APPROVAL' AND id=(p->>'approvalId')::uuid))
              OR (p->>'state'='APPROVED' AND p->>'approvalId' IS NULL) OR (p->>'state'='WITHDRAWN' AND p->>'approvalId' IS NOT NULL) THEN RAISE EXCEPTION 'QUALITY_SPEC_CONTROL_PROOF_REQUIRED'; END IF;
          ELSIF TG_TABLE_NAME='records' THEN
            IF NEW.kind='POLICY' THEN
              IF coalesce(p#>>'{input,state}','') NOT IN('ENABLED','DISABLED') OR jsonb_typeof(p#>'{input,roles}') IS DISTINCT FROM 'array'
                OR coalesce(jsonb_array_length(p#>'{input,roles}'),0)=0
                OR (p#>>'{input,effectiveTo}')::timestamptz <= (p#>>'{input,effectiveFrom}')::timestamptz THEN RAISE EXCEPTION 'QUALITY_SPEC_POLICY_INVALID'; END IF;
              FOR r IN SELECT value FROM jsonb_array_elements(p#>'{input,roles}') LOOP
                IF coalesce((r->>'roleId')::integer,0)<=0 OR coalesce((r->>'minimumApprovals')::integer,0)<=0 THEN RAISE EXCEPTION 'QUALITY_SPEC_POLICY_INVALID'; END IF;
              END LOOP;
            ELSIF NEW.kind='PACKET' THEN
              IF p->>'submitterId' IS DISTINCT FROM NEW.actor_id::text OR p#>>'{policy,owner}' IS DISTINCT FROM 'Main.Quality.SpecificationReviewPolicy.v1'
                OR p#>>'{policy,version}' IS DISTINCT FROM '1' OR NOT EXISTS(SELECT FROM cp6_quality_specification_v1.records
                  WHERE environment_id=NEW.environment_id AND tenant_id=NEW.tenant_id AND specification_id=NEW.specification_id AND kind='POLICY'
                    AND id::text=p#>>'{policy,id}' AND digest=p#>>'{policy,digest}') THEN RAISE EXCEPTION 'QUALITY_SPEC_PACKET_POLICY_REQUIRED'; END IF;
              IF NOT EXISTS(SELECT FROM cp6_quality_specification_v1.versions v WHERE v.environment_id=NEW.environment_id AND v.tenant_id=NEW.tenant_id AND v.specification_id=NEW.specification_id
                AND v.revision::text=p#>>'{specification,version}' AND v.digest=p#>>'{specification,digest}' AND p#>>'{specification,id}'=v.specification_id::text
                AND p#>>'{specification,owner}'='Main.Quality.Specification.v1' AND convert_from(v.body,'UTF8')::jsonb->>'authorId'=p->>'authorId') THEN RAISE EXCEPTION 'QUALITY_SPEC_PACKET_SOURCE_REQUIRED'; END IF;
            ELSIF NEW.kind='VOTE' THEN
              SELECT convert_from(body,'UTF8')::jsonb INTO pk FROM cp6_quality_specification_v1.records WHERE environment_id=NEW.environment_id AND tenant_id=NEW.tenant_id AND specification_id=NEW.specification_id AND kind='PACKET' AND id=(p->>'packetId')::uuid;
              IF pk IS NULL OR p->>'actorId'=pk->>'authorId' THEN RAISE EXCEPTION 'QUALITY_SPEC_INDEPENDENT_VOTE_REQUIRED'; END IF;
            ELSIF NEW.kind='APPROVAL' THEN
              SELECT convert_from(body,'UTF8')::jsonb INTO pk FROM cp6_quality_specification_v1.records WHERE environment_id=NEW.environment_id AND tenant_id=NEW.tenant_id AND specification_id=NEW.specification_id
                AND kind='PACKET' AND id=(p#>>'{packet,id}')::uuid AND digest=p#>>'{packet,digest}';
              SELECT convert_from(body,'UTF8')::jsonb INTO policy FROM cp6_quality_specification_v1.records WHERE environment_id=NEW.environment_id AND tenant_id=NEW.tenant_id AND specification_id=NEW.specification_id
                AND kind='POLICY' AND id=(p#>>'{policy,id}')::uuid AND digest=p#>>'{policy,digest}';
              IF p#>>'{packet,owner}' IS DISTINCT FROM 'Main.Quality.SpecificationReviewPacket.v1' OR p#>>'{packet,version}' IS DISTINCT FROM '1'
                OR pk IS NULL OR policy IS NULL OR p->'specification' IS DISTINCT FROM pk->'specification' OR p->'policy' IS DISTINCT FROM pk->'policy'
                OR p->>'requirementsDigest' IS DISTINCT FROM pk->>'requirementsDigest' OR jsonb_typeof(p->'votes') IS DISTINCT FROM 'array' OR coalesce(jsonb_array_length(p->'votes'),0)=0 THEN RAISE EXCEPTION 'QUALITY_SPEC_APPROVAL_SOURCE_REQUIRED'; END IF;
              IF EXISTS(SELECT FROM jsonb_array_elements(p->'votes') x WHERE NOT EXISTS(SELECT FROM cp6_quality_specification_v1.records v
                WHERE v.environment_id=NEW.environment_id AND v.tenant_id=NEW.tenant_id AND v.specification_id=NEW.specification_id AND v.kind='VOTE'
                  AND x->>'owner'='Main.Quality.SpecificationReviewVote.v1' AND x->>'version'='1' AND v.id::text=x->>'id' AND v.digest=x->>'digest' AND convert_from(v.body,'UTF8')::jsonb->>'packetId'=pk->>'id'
                  AND convert_from(v.body,'UTF8')::jsonb->>'approved'='true')) THEN RAISE EXCEPTION 'QUALITY_SPEC_APPROVAL_VOTES_REQUIRED'; END IF;
              FOR r IN SELECT value FROM jsonb_array_elements(policy#>'{input,roles}') LOOP
                IF (SELECT count(DISTINCT v.actor_id) FROM cp6_quality_specification_v1.records v JOIN jsonb_array_elements(p->'votes') x ON x->>'id'=v.id::text
                  WHERE v.environment_id=NEW.environment_id AND v.tenant_id=NEW.tenant_id AND v.specification_id=NEW.specification_id AND v.kind='VOTE'
                    AND convert_from(v.body,'UTF8')::jsonb->'roleIds' @> jsonb_build_array((r->>'roleId')::integer)) < (r->>'minimumApprovals')::integer THEN
                  RAISE EXCEPTION 'QUALITY_SPEC_PROFESSIONAL_APPROVAL_REQUIRED'; END IF;
              END LOOP;
            END IF;
          END IF; RETURN NULL;
        END $$;
        DO $$ DECLARE tbl text; BEGIN FOREACH tbl IN ARRAY ARRAY['heads','versions','controls','records'] LOOP
          EXECUTE format('CREATE TRIGGER quality_spec_validate BEFORE INSERT OR UPDATE ON cp6_quality_specification_v1.%I FOR EACH ROW EXECUTE FUNCTION cp6_quality_specification_v1.validate()',tbl);
          EXECUTE format('CREATE TRIGGER quality_spec_root BEFORE INSERT OR UPDATE ON cp6_quality_specification_v1.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change()',tbl);
          EXECUTE format('CREATE CONSTRAINT TRIGGER quality_spec_complete AFTER INSERT OR UPDATE ON cp6_quality_specification_v1.%I DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_quality_specification_v1.complete()',tbl);
          IF tbl='heads' THEN
            EXECUTE 'CREATE TRIGGER quality_spec_permanent BEFORE DELETE OR TRUNCATE ON cp6_quality_specification_v1.heads FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable()';
          ELSE
            EXECUTE format('CREATE TRIGGER quality_spec_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_quality_specification_v1.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable()',tbl);
          END IF;
        END LOOP; END $$;
        CREATE FUNCTION cp6_quality_specification_v1.ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT NOT EXISTS(SELECT FROM (VALUES('heads'),('versions'),('controls'),('records')) a(tbl)
            CROSS JOIN(VALUES('quality_spec_validate','cp6_quality_specification_v1.validate()',23),('quality_spec_root','cp6_authentication_v1.before_change()',22),('quality_spec_complete','cp6_quality_specification_v1.complete()',21)) b(name,fn,typ)
            WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid=to_regclass('cp6_quality_specification_v1.'||a.tbl) AND t.tgname=b.name AND t.tgfoid=to_regprocedure(b.fn)
              AND t.tgtype=b.typ AND t.tgenabled IN('O','A') AND t.tgqual IS NULL AND (b.name<>'quality_spec_complete' OR t.tgdeferrable AND t.tginitdeferred)))
          AND NOT EXISTS(SELECT FROM (VALUES('heads','quality_spec_permanent',42),('versions','quality_spec_immutable',58),('controls','quality_spec_immutable',58),('records','quality_spec_immutable',58)) a(tbl,name,typ)
            WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid=to_regclass('cp6_quality_specification_v1.'||a.tbl) AND t.tgname=a.name
              AND t.tgfoid='cp6_scope_authorization_v1.immutable()'::regprocedure AND t.tgtype=a.typ AND t.tgenabled IN('O','A') AND t.tgqual IS NULL));
        $$;
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_quality_specification_v1 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_quality_specification_v1 FROM PUBLIC;
        LOCK TABLE public."Sys_Menus" IN SHARE ROW EXCLUSIVE MODE;
        INSERT INTO public."Sys_Menus"("MenuId","MenuKey","MenuName","RoutePath","Icon","ParentId","OrderNo","Enable","CreateDate")
        SELECT (SELECT CASE WHEN COALESCE(MAX("MenuId"),0)<2147483647 THEN COALESCE(MAX("MenuId"),0)+1 ELSE
          (SELECT min(candidate) FROM (SELECT 1 AS candidate UNION SELECT "MenuId"+1 FROM public."Sys_Menus" WHERE "MenuId">=1 AND "MenuId"<2147483647) available
          WHERE NOT EXISTS(SELECT FROM public."Sys_Menus" used WHERE used."MenuId"=available.candidate)) END FROM public."Sys_Menus"),'quality-specification','独立质量规格','/mes/quality-specifications','CircleCheck',NULL,158,true,LOCALTIMESTAMP
          WHERE NOT EXISTS(SELECT FROM public."Sys_Menus" WHERE "MenuKey"='quality-specification');
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",a.code,a.name,a.sort,t."Id",LOCALTIMESTAMP FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t
          CROSS JOIN(VALUES('query','读取质量规格',0),('add','创建质量规格',1),('edit','修订质量规格',2),('review-policy','质量专业规则',3),('review-submit','质量规格送审',4),('review-decide','质量专业签署',5),('withdraw','撤回质量规格',6)) a(code,name,sort)
          WHERE m."MenuKey"='quality-specification' AND NOT EXISTS(SELECT FROM public."Sys_MenuAction" x WHERE x."TenantId"=t."Id" AND x."MenuId"=m."MenuId" AND x."ActionCode"=a.code);
        """);
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Quality originals and professional signatures require forward recovery.");
}
