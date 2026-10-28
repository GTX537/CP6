using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)),Migration("20261029220000_IterationCandidateV1")]
public sealed class IterationCandidateV1:Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        CREATE SCHEMA cp6_iteration_candidate_v1;
        REVOKE ALL ON SCHEMA cp6_iteration_candidate_v1 FROM PUBLIC;
        CREATE TABLE cp6_iteration_candidate_v1.heads(
          tenant_id uuid NOT NULL,candidate_id uuid NOT NULL,iteration_id uuid NOT NULL,creator_id uuid NOT NULL,
          revision bigint NOT NULL CHECK(revision>0),title text NOT NULL,
          PRIMARY KEY(tenant_id,candidate_id),UNIQUE(tenant_id,candidate_id,iteration_id),
          FOREIGN KEY(tenant_id,iteration_id) REFERENCES cp6_engineering_iteration_v1.heads,
          CHECK(candidate_id<>'00000000-0000-0000-0000-000000000000'::uuid));
        CREATE TABLE cp6_iteration_candidate_v1.versions(
          tenant_id uuid NOT NULL,candidate_id uuid NOT NULL,iteration_id uuid NOT NULL,revision bigint NOT NULL CHECK(revision>0),
          body_bytes bytea NOT NULL CHECK(octet_length(body_bytes) BETWEEN 2 AND 16777216),
          body_digest text NOT NULL CHECK(body_digest=encode(sha256(body_bytes),'hex')),actor_id uuid NOT NULL,operation_id uuid NOT NULL,
          journal text NOT NULL DEFAULT 'Main.Plm.IterationCandidate.v1' CHECK(journal='Main.Plm.IterationCandidate.v1'),
          PRIMARY KEY(tenant_id,candidate_id,revision),UNIQUE(tenant_id,operation_id),
          FOREIGN KEY(tenant_id,candidate_id,iteration_id) REFERENCES cp6_iteration_candidate_v1.heads(tenant_id,candidate_id,iteration_id),
          FOREIGN KEY(tenant_id,journal,operation_id) REFERENCES cp6_main_owner_v1.operations(tenant_id,owner,operation_id) DEFERRABLE INITIALLY DEFERRED);
        ALTER TABLE cp6_iteration_candidate_v1.heads ADD CONSTRAINT iteration_candidate_original
          FOREIGN KEY(tenant_id,candidate_id,revision) REFERENCES cp6_iteration_candidate_v1.versions DEFERRABLE INITIALLY DEFERRED;
        CREATE FUNCTION cp6_iteration_candidate_v1.advance() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP='INSERT' THEN
            IF NEW.revision<>1 OR NOT EXISTS(SELECT FROM cp6_engineering_iteration_v1.heads i WHERE i.tenant_id=NEW.tenant_id AND i.object_id=NEW.iteration_id AND i.kind='ITERATION')
              THEN RAISE EXCEPTION 'ITERATION_CANDIDATE_FRONTIER_INVALID';END IF;
          ELSIF (to_jsonb(NEW)-'title'-'revision') IS DISTINCT FROM (to_jsonb(OLD)-'title'-'revision') OR NEW.revision<>OLD.revision+1 THEN
            RAISE EXCEPTION 'ITERATION_CANDIDATE_FRONTIER_INVALID';
          END IF;RETURN NEW;
        END $$;
        CREATE FUNCTION cp6_iteration_candidate_v1.validate() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE b jsonb=convert_from(NEW.body_bytes,'UTF8')::jsonb;p jsonb;parent jsonb=b#>'{definition,parentCandidate}';BEGIN
          IF b->>'tenantId' IS DISTINCT FROM NEW.tenant_id::text OR b->>'id' IS DISTINCT FROM NEW.candidate_id::text
            OR b->>'iterationId' IS DISTINCT FROM NEW.iteration_id::text OR b->>'version' IS DISTINCT FROM NEW.revision::text
            OR b->>'actorId' IS DISTINCT FROM NEW.actor_id::text OR b->>'operationId' IS DISTINCT FROM NEW.operation_id::text
            OR b->>'etag' IS NULL OR b->>'etag'='00000000-0000-0000-0000-000000000000'
            OR b#>>'{definition,iteration,id}' IS DISTINCT FROM NEW.iteration_id::text
            OR b#>>'{definition,iteration,owner}' IS DISTINCT FROM 'Main.Plm.EngineeringIteration.v1'
            OR NOT EXISTS(SELECT FROM cp6_engineering_iteration_v1.versions i WHERE i.tenant_id=NEW.tenant_id AND i.object_id=NEW.iteration_id
              AND i.revision::text=b#>>'{definition,iteration,version}' AND i.body_digest=b#>>'{definition,iteration,digest}')
            OR NOT EXISTS(SELECT FROM cp6_iteration_candidate_v1.heads h WHERE h.tenant_id=NEW.tenant_id AND h.candidate_id=NEW.candidate_id
              AND h.revision=NEW.revision AND h.creator_id::text=b->>'creatorId' AND h.title=b#>>'{definition,title}') THEN RAISE EXCEPTION 'ITERATION_CANDIDATE_ORIGINAL_INVALID';END IF;
          IF parent IS DISTINCT FROM 'null'::jsonb THEN
            IF parent IS NULL OR parent->>'owner' IS DISTINCT FROM 'Main.Plm.IterationCandidate.v1' OR parent->>'id'=NEW.candidate_id::text
              OR NOT EXISTS(SELECT FROM cp6_iteration_candidate_v1.versions v WHERE v.tenant_id=NEW.tenant_id AND v.iteration_id=NEW.iteration_id
                AND v.candidate_id::text=parent->>'id' AND v.revision::text=parent->>'version' AND v.body_digest=parent->>'digest') THEN RAISE EXCEPTION 'ITERATION_CANDIDATE_PARENT_INVALID';END IF;
          END IF;
          IF NEW.revision=1 THEN
            IF b->>'previousVersion' IS NOT NULL OR b->>'action' IS DISTINCT FROM 'CREATE' THEN RAISE EXCEPTION 'ITERATION_CANDIDATE_PARENT_INVALID';END IF;
          ELSE
            SELECT convert_from(body_bytes,'UTF8')::jsonb INTO p FROM cp6_iteration_candidate_v1.versions
              WHERE tenant_id=NEW.tenant_id AND candidate_id=NEW.candidate_id AND revision=NEW.revision-1;
            IF p IS NULL OR b->>'previousVersion' IS DISTINCT FROM (NEW.revision-1)::text OR b->>'action' IS DISTINCT FROM 'REVISE'
              OR b->>'etag'=p->>'etag' OR parent IS DISTINCT FROM p#>'{definition,parentCandidate}' THEN RAISE EXCEPTION 'ITERATION_CANDIDATE_PARENT_INVALID';END IF;
          END IF;RETURN NEW;
        END $$;
        CREATE FUNCTION cp6_iteration_candidate_v1.provenance() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF NOT EXISTS(SELECT FROM cp6_main_owner_v1.operations o WHERE o.tenant_id=NEW.tenant_id AND o.owner=NEW.journal
            AND o.operation_id=NEW.operation_id AND o.actor_id=NEW.actor_id AND o.contract='Main.Plm.IterationCandidate.commands.v1'
            AND o.state='COMMITTED' AND o.object_ids=ARRAY[NEW.iteration_id,NEW.candidate_id]) THEN RAISE EXCEPTION 'ITERATION_CANDIDATE_ORIGINAL_REQUIRED';END IF;RETURN NULL;
        END $$;
        CREATE TRIGGER iteration_candidate_advance BEFORE INSERT OR UPDATE ON cp6_iteration_candidate_v1.heads FOR EACH ROW EXECUTE FUNCTION cp6_iteration_candidate_v1.advance();
        CREATE TRIGGER iteration_candidate_validate BEFORE INSERT ON cp6_iteration_candidate_v1.versions FOR EACH ROW EXECUTE FUNCTION cp6_iteration_candidate_v1.validate();
        CREATE TRIGGER iteration_candidate_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_iteration_candidate_v1.versions FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER iteration_candidate_permanent BEFORE DELETE OR TRUNCATE ON cp6_iteration_candidate_v1.heads FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE CONSTRAINT TRIGGER iteration_candidate_provenance AFTER INSERT ON cp6_iteration_candidate_v1.versions
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_iteration_candidate_v1.provenance();
        CREATE FUNCTION cp6_iteration_candidate_v1.ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT NOT EXISTS(SELECT FROM (VALUES
            ('heads','iteration_candidate_advance','cp6_iteration_candidate_v1.advance()',23),
            ('versions','iteration_candidate_validate','cp6_iteration_candidate_v1.validate()',7),
            ('versions','iteration_candidate_immutable','cp6_scope_authorization_v1.immutable()',58),
            ('heads','iteration_candidate_permanent','cp6_scope_authorization_v1.immutable()',42),
            ('versions','iteration_candidate_provenance','cp6_iteration_candidate_v1.provenance()',5)) a(tbl,name,fn,typ)
            WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid=to_regclass('cp6_iteration_candidate_v1.'||a.tbl)
              AND t.tgname=a.name AND t.tgfoid=to_regprocedure(a.fn) AND t.tgtype=a.typ AND t.tgenabled IN('O','A') AND t.tgqual IS NULL
              AND (a.name<>'iteration_candidate_provenance' OR t.tgdeferrable AND t.tginitdeferred)));
        $$;
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_iteration_candidate_v1 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_iteration_candidate_v1 FROM PUBLIC;
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",'candidate-write','维护迭代候选方案',8,t."Id",LOCALTIMESTAMP
          FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t WHERE m."MenuKey"='plm-engineering-iteration'
          AND NOT EXISTS(SELECT FROM public."Sys_MenuAction" x WHERE x."TenantId"=t."Id" AND x."MenuId"=m."MenuId" AND x."ActionCode"='candidate-write');
        """);
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Candidate originals require forward recovery.");
}
