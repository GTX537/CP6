using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)),Migration("20261029232000_IterationReviewControlV1")]
public sealed class IterationReviewControlV1:Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        CREATE SCHEMA cp6_iteration_review_control_v1;
        REVOKE ALL ON SCHEMA cp6_iteration_review_control_v1 FROM PUBLIC;
        ALTER TABLE cp6_iteration_review_v1.submissions DROP CONSTRAINT submissions_environment_id_tenant_id_iteration_id_key;
        ALTER TABLE cp6_iteration_review_v1.submissions ADD UNIQUE(environment_id,tenant_id,iteration_id,frozen_revision);
        CREATE SEQUENCE cp6_iteration_review_control_v1.version AS bigint MINVALUE 1 NO CYCLE;
        CREATE TABLE cp6_iteration_review_control_v1.heads(
          environment_id uuid NOT NULL,tenant_id uuid NOT NULL,iteration_id uuid NOT NULL,submission_id uuid NOT NULL,
          state text NOT NULL CHECK(state IN('IN_REVIEW','APPLIED','WITHDRAWN')),
          row_version bytea NOT NULL DEFAULT int8send(nextval('cp6_iteration_review_control_v1.version')) CHECK(octet_length(row_version)=8),
          PRIMARY KEY(environment_id,tenant_id,iteration_id),
          FOREIGN KEY(environment_id,tenant_id,submission_id) REFERENCES cp6_iteration_review_v1.submissions(environment_id,tenant_id,id));
        -- Establish a new control token for already-persisted W199 rounds, without changing their bytes or claiming a historical action.
        INSERT INTO cp6_iteration_review_control_v1.heads(environment_id,tenant_id,iteration_id,submission_id,state)
          SELECT environment_id,tenant_id,iteration_id,id,'IN_REVIEW' FROM cp6_iteration_review_v1.submissions;
        CREATE TABLE cp6_iteration_review_control_v1.issues(
          environment_id uuid NOT NULL,tenant_id uuid NOT NULL,iteration_id uuid NOT NULL,id uuid NOT NULL,revision bigint NOT NULL CHECK(revision>0),
          submission_id uuid NOT NULL,operation_id uuid NOT NULL,actor_id uuid NOT NULL,body bytea NOT NULL,digest text NOT NULL,audit_json text NOT NULL,
          journal text NOT NULL DEFAULT 'Main.Plm.IterationReviewIssue.operations.v1' CHECK(journal='Main.Plm.IterationReviewIssue.operations.v1'),
          PRIMARY KEY(environment_id,tenant_id,id,revision),UNIQUE(environment_id,tenant_id,operation_id),
          FOREIGN KEY(environment_id,tenant_id,submission_id) REFERENCES cp6_iteration_review_v1.submissions(environment_id,tenant_id,id),
          FOREIGN KEY(tenant_id,journal,operation_id) REFERENCES cp6_main_owner_v1.operations(tenant_id,owner,operation_id) DEFERRABLE INITIALLY DEFERRED,
          CHECK(digest=encode(sha256(body),'hex')));
        CREATE FUNCTION cp6_iteration_review_control_v1.head_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP IN('DELETE','TRUNCATE') THEN RAISE EXCEPTION 'ITERATION_REVIEW_CONTROL_PERMANENT';END IF;
          IF TG_OP='UPDATE' AND ((OLD.environment_id,OLD.tenant_id,OLD.iteration_id) IS DISTINCT FROM (NEW.environment_id,NEW.tenant_id,NEW.iteration_id)
            OR OLD.state<>'IN_REVIEW' AND NEW.state<>'IN_REVIEW' OR OLD.state='IN_REVIEW' AND NEW.state='IN_REVIEW'
            OR OLD.state='IN_REVIEW' AND OLD.submission_id<>NEW.submission_id) THEN RAISE EXCEPTION 'ITERATION_REVIEW_CONTROL_CHANGED';END IF;
          NEW.row_version=int8send(nextval('cp6_iteration_review_control_v1.version'));RETURN NEW;
        END $$;
        CREATE TRIGGER iteration_control_root BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_iteration_review_control_v1.heads FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER iteration_control_guard BEFORE INSERT OR UPDATE OR DELETE ON cp6_iteration_review_control_v1.heads FOR EACH ROW EXECUTE FUNCTION cp6_iteration_review_control_v1.head_guard();
        CREATE TRIGGER iteration_control_truncate BEFORE TRUNCATE ON cp6_iteration_review_control_v1.heads FOR EACH STATEMENT EXECUTE FUNCTION cp6_iteration_review_control_v1.head_guard();
        CREATE FUNCTION cp6_iteration_review_control_v1.issue_complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE b jsonb=convert_from(NEW.body,'UTF8')::jsonb;prev jsonb;BEGIN
          SELECT convert_from(body,'UTF8')::jsonb INTO prev FROM cp6_iteration_review_control_v1.issues WHERE environment_id=NEW.environment_id AND tenant_id=NEW.tenant_id AND id=NEW.id AND revision=NEW.revision-1;
          IF (NEW.revision=1)<>(prev IS NULL) OR b->>'id' IS DISTINCT FROM NEW.id::text OR b->>'iterationId' IS DISTINCT FROM NEW.iteration_id::text
            OR b->>'submissionId' IS DISTINCT FROM NEW.submission_id::text OR b->>'revision' IS DISTINCT FROM NEW.revision::text
            OR NOT EXISTS(SELECT FROM cp6_main_owner_v1.operations o WHERE o.tenant_id=NEW.tenant_id AND o.owner=NEW.journal AND o.operation_id=NEW.operation_id
              AND o.actor_id=NEW.actor_id AND o.contract='Main.Plm.IterationReviewIssue.commands.v1' AND o.state='COMMITTED' AND o.object_ids=ARRAY[NEW.iteration_id]
              AND convert_from(o.result_bytes,'UTF8')::jsonb->'Issue'->>'Id'=NEW.id::text
              AND convert_from(o.result_bytes,'UTF8')::jsonb->'Issue'->>'Revision'=NEW.revision::text)
            OR prev IS NOT NULL AND (prev->'target' IS DISTINCT FROM b->'target' OR prev->>'authorId' IS DISTINCT FROM b->>'authorId'
              OR prev->>'text' IS DISTINCT FROM b->>'text' OR prev->>'responsibleId' IS DISTINCT FROM b->>'responsibleId'
              OR prev->>'iterationId' IS DISTINCT FROM b->>'iterationId' OR prev->>'submissionId' IS DISTINCT FROM b->>'submissionId'
              OR prev->>'dueDate' IS DISTINCT FROM b->>'dueDate'
              OR jsonb_array_length(b->'responses')<jsonb_array_length(prev->'responses')
              OR EXISTS(SELECT FROM jsonb_array_elements(prev->'responses') WITH ORDINALITY x(value,n)
                WHERE x.value IS DISTINCT FROM b->'responses'->(x.n::int-1))
              OR prev->>'state'='VERIFIED_CLOSED' AND ((prev->'state',prev->'verifiedBy',prev->'verifiedAt')
                IS DISTINCT FROM (b->'state',b->'verifiedBy',b->'verifiedAt')))
            OR b->>'state'='VERIFIED_CLOSED' AND (b->>'verifiedBy' IS NULL OR b->>'verifiedBy'=b->>'authorId' OR b->>'verifiedBy'=b->>'responsibleId'
              OR EXISTS(SELECT FROM jsonb_array_elements(b->'responses') x WHERE x->>'actorId'=b->>'verifiedBy' AND x->>'correctiveNote' IS DISTINCT FROM 'true'))
            THEN RAISE EXCEPTION 'ITERATION_REVIEW_ISSUE_ORIGINAL_REQUIRED';END IF;RETURN NULL;
        END $$;
        CREATE TRIGGER iteration_issue_root BEFORE INSERT ON cp6_iteration_review_control_v1.issues FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER iteration_issue_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_iteration_review_control_v1.issues FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE CONSTRAINT TRIGGER iteration_issue_complete AFTER INSERT ON cp6_iteration_review_control_v1.issues DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_iteration_review_control_v1.issue_complete();
        CREATE FUNCTION cp6_iteration_review_control_v1.ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT cp6_iteration_review_v1.ready() AND NOT EXISTS(SELECT FROM (VALUES
            ('heads','iteration_control_root','cp6_authentication_v1.before_change()',62,false),
            ('heads','iteration_control_guard','cp6_iteration_review_control_v1.head_guard()',31,false),
            ('heads','iteration_control_truncate','cp6_iteration_review_control_v1.head_guard()',34,false),
            ('issues','iteration_issue_root','cp6_authentication_v1.before_change()',6,false),
            ('issues','iteration_issue_immutable','cp6_scope_authorization_v1.immutable()',58,false),
            ('issues','iteration_issue_complete','cp6_iteration_review_control_v1.issue_complete()',5,true)) a(tbl,name,fn,typ,deferred)
            WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid=to_regclass('cp6_iteration_review_control_v1.'||a.tbl) AND t.tgname=a.name
              AND t.tgfoid=to_regprocedure(a.fn) AND t.tgtype=a.typ AND t.tgenabled IN('O','A') AND t.tgqual IS NULL AND t.tgdeferrable=a.deferred AND t.tginitdeferred=a.deferred));
        $$;
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",a.code,a.name,a.sort,t."Id",LOCALTIMESTAMP FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t
          CROSS JOIN (VALUES('review-issue','迭代问题处理',12),('review-issue-verify','独立验收问题',13),('review-issue-degrade','问题降级',14),('review-apply','应用评审决定',15),('review-withdraw','撤回迭代评审',16)) a(code,name,sort)
        WHERE m."MenuKey"='plm-engineering-iteration' AND NOT EXISTS(SELECT FROM public."Sys_MenuAction" x WHERE x."TenantId"=t."Id" AND x."MenuId"=m."MenuId" AND x."ActionCode"=a.code);
        CREATE OR REPLACE FUNCTION cp6_iteration_review_v1.validate() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE b jsonb;d text;p bytea;policy_digest text;package jsonb;previous text;frozen jsonb;snapshot jsonb;snapshot_bytes bytea;member jsonb; BEGIN
          IF TG_TABLE_NAME='policies' THEN
            b:=NEW.body::jsonb;
            IF b->>'environmentId' IS DISTINCT FROM NEW.environment_id::text OR b->>'tenantId' IS DISTINCT FROM NEW.tenant_id::text
              OR b->>'iterationId' IS DISTINCT FROM NEW.iteration_id::text OR b->>'id' IS DISTINCT FROM NEW.id::text
              OR b->>'actorId' IS DISTINCT FROM NEW.actor_id::text OR b#>>'{body,operationId}' IS DISTINCT FROM NEW.operation_id::text
              THEN RAISE EXCEPTION 'ITERATION_REVIEW_POLICY_IDENTITY_INVALID'; END IF;
          ELSIF TG_TABLE_NAME='submissions' THEN
            b:=convert_from(NEW.payload,'UTF8')::jsonb;package:=convert_from(NEW.package_bytes,'UTF8')::jsonb;
            SELECT body_digest,convert_from(body_bytes,'UTF8') INTO d,previous FROM cp6_engineering_iteration_v1.versions
              WHERE tenant_id=NEW.tenant_id AND object_id=NEW.iteration_id AND revision=NEW.iteration_revision;
            SELECT convert_from(body_bytes,'UTF8')::jsonb INTO frozen FROM cp6_engineering_iteration_v1.versions
              WHERE tenant_id=NEW.tenant_id AND object_id=NEW.iteration_id AND revision=NEW.frozen_revision;
            SELECT digest INTO policy_digest FROM cp6_iteration_review_v1.policies WHERE environment_id=NEW.environment_id
              AND tenant_id=NEW.tenant_id AND iteration_id=NEW.iteration_id AND id=NEW.policy_id;
            IF d IS NULL OR policy_digest IS NULL OR frozen IS NULL OR b->>'schema' IS DISTINCT FROM 'oa.app/2.0'
              OR b#>>'{scope,environmentId}' IS DISTINCT FROM NEW.environment_id::text OR b#>>'{scope,tenantId}' IS DISTINCT FROM NEW.tenant_id::text
              OR b->>'submissionId' IS DISTINCT FROM NEW.id::text OR b->>'commandId' IS DISTINCT FROM NEW.operation_id::text
              OR b->>'starterId' IS DISTINCT FROM NEW.actor_id::text OR b->>'ownerOutboxId' IS DISTINCT FROM NEW.outbox_id::text
              OR b#>>'{subject,businessOwner}' IS DISTINCT FROM 'Main.Plm.EngineeringIteration.v1'
              OR b#>>'{subject,bizType}' IS DISTINCT FROM 'PLM_ITER_REVIEW' OR b#>>'{subject,subjectKind}' IS DISTINCT FROM 'PLM_ITER_REVIEW'
              OR b#>>'{subject,subjectId}' IS DISTINCT FROM NEW.round_id::text OR b->>'roundId' IS DISTINCT FROM NEW.round_id::text
              OR b#>>'{subject,businessRevision}' IS DISTINCT FROM NEW.binding_id::text
              OR b#>>'{policy,ref,id}' IS DISTINCT FROM NEW.policy_id::text
              OR b#>>'{policy,ref,owner}' IS DISTINCT FROM 'Main.Plm.IterationReviewPolicy.v1' OR b#>>'{policy,ref,version}' IS DISTINCT FROM '1'
              OR b#>>'{policy,digest,value}' IS DISTINCT FROM policy_digest
              OR previous::jsonb#>>'{body,iteration,stage}' IS DISTINCT FROM 'IN_DESIGN'
              OR frozen#>>'{body,iteration,stage}' IS DISTINCT FROM 'IN_REVIEW'
              OR frozen->>'operationId' IS DISTINCT FROM NEW.operation_id::text OR frozen->>'actorId' IS DISTINCT FROM NEW.actor_id::text
              OR ((frozen#>'{body,iteration}')-'stage'-'currentReview') IS DISTINCT FROM ((previous::jsonb#>'{body,iteration}')-'stage'-'currentReview')
              THEN RAISE EXCEPTION 'ITERATION_REVIEW_SUBMISSION_INVALID'; END IF;
            IF package->>'schema' IS DISTINCT FROM 'Main.Plm.IterationReviewPackage/1' OR package->'scope' IS DISTINCT FROM b->'scope'
              OR package->>'roundId' IS DISTINCT FROM NEW.round_id::text OR package#>>'{iteration,id}' IS DISTINCT FROM NEW.iteration_id::text
              OR package#>>'{iteration,version}' IS DISTINCT FROM NEW.iteration_revision::text OR package#>>'{iteration,digest}' IS DISTINCT FROM d
              OR package->>'iterationOriginal' IS DISTINCT FROM previous OR package#>>'{policy,digest}' IS DISTINCT FROM policy_digest
              OR package->'issues' IS DISTINCT FROM coalesce((SELECT jsonb_agg(convert_from(x.body,'UTF8')::jsonb ORDER BY x.id) FROM (SELECT DISTINCT ON(id) id,body FROM cp6_iteration_review_control_v1.issues WHERE environment_id=NEW.environment_id AND tenant_id=NEW.tenant_id AND iteration_id=NEW.iteration_id ORDER BY id,revision DESC) x),'[]'::jsonb) OR jsonb_array_length(package->'candidates')<1
              OR jsonb_array_length(package->'candidates')<>(SELECT count(*) FROM cp6_iteration_candidate_v1.heads WHERE tenant_id=NEW.tenant_id AND iteration_id=NEW.iteration_id)
              OR jsonb_array_length(package->'candidates')<>(SELECT count(DISTINCT x#>>'{reference,id}') FROM jsonb_array_elements(package->'candidates') x)
              THEN RAISE EXCEPTION 'ITERATION_REVIEW_PACKAGE_INVALID'; END IF;
            FOR member IN SELECT value FROM jsonb_array_elements(package->'candidates') LOOP
              IF NOT EXISTS(SELECT FROM cp6_iteration_candidate_v1.versions v JOIN cp6_iteration_candidate_v1.heads h
                ON (h.tenant_id,h.candidate_id,h.revision)=(v.tenant_id,v.candidate_id,v.revision)
                WHERE v.tenant_id=NEW.tenant_id AND v.iteration_id=NEW.iteration_id AND v.candidate_id::text=member#>>'{reference,id}'
                  AND v.revision::text=member#>>'{reference,version}' AND v.body_digest=member#>>'{reference,digest}'
                  AND convert_from(v.body_bytes,'UTF8')=member->>'originalJson' AND convert_from(v.body_bytes,'UTF8')::jsonb=member->'original')
                THEN RAISE EXCEPTION 'ITERATION_REVIEW_COMPLETE_CANDIDATES_REQUIRED'; END IF;
            END LOOP;
            snapshot_bytes:=decode(b#>>'{snapshot,canonicalBytesBase64}','base64');snapshot:=convert_from(snapshot_bytes,'UTF8')::jsonb;
            IF octet_length(snapshot_bytes)>262144 OR snapshot->>'schema' IS DISTINCT FROM 'ITER-OA-SNAPSHOT/1'
              OR b#>>'{snapshot,digest,value}' IS DISTINCT FROM encode(sha256(snapshot_bytes),'hex')
              OR b#>'{subject,contentDigest}' IS DISTINCT FROM b#>'{snapshot,digest}'
              OR snapshot#>>'{aggregate,aggregateId}' IS DISTINCT FROM NEW.aggregate_id::text
              OR snapshot#>>'{aggregate,roundId}' IS DISTINCT FROM NEW.round_id::text
              OR snapshot#>>'{aggregate,iterationId}' IS DISTINCT FROM NEW.iteration_id::text
              OR snapshot#>>'{binding,roundBindingId}' IS DISTINCT FROM NEW.binding_id::text
              OR snapshot#>>'{binding,submissionId}' IS DISTINCT FROM NEW.id::text
              OR snapshot#>>'{binding,reviewPackageDigest,value}' IS DISTINCT FROM NEW.package_digest
              OR jsonb_array_length(snapshot#>'{aggregate,members}')<>jsonb_array_length(package->'candidates')
              OR b#>>'{engineering,candidateId}' IS DISTINCT FROM NEW.aggregate_id::text
              OR b#>>'{engineering,reviewPackageDigest,value}' IS DISTINCT FROM NEW.package_digest
              THEN RAISE EXCEPTION 'ITERATION_REVIEW_MAPPING_INVALID'; END IF;
          ELSE
            SELECT payload INTO p FROM cp6_iteration_review_v1.submissions WHERE environment_id=NEW.environment_id AND tenant_id=NEW.tenant_id AND id=NEW.submission_id;
            IF p IS NULL OR p<>NEW.payload THEN RAISE EXCEPTION 'ITERATION_REVIEW_OUTBOX_INVALID'; END IF;
          END IF; RETURN NEW;
        END $$;
        """);
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Forward recovery preserves control and issue history.");
}
