using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)),Migration("20261029230000_IterationReviewV1")]
public sealed class IterationReviewV1:Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        CREATE SCHEMA cp6_iteration_review_v1;
        REVOKE ALL ON SCHEMA cp6_iteration_review_v1 FROM PUBLIC;
        CREATE TABLE cp6_iteration_review_v1.policies(
          sequence bigint GENERATED ALWAYS AS IDENTITY UNIQUE,
          environment_id uuid NOT NULL,tenant_id uuid NOT NULL,iteration_id uuid NOT NULL,id uuid NOT NULL,actor_id uuid NOT NULL,
          body text NOT NULL,digest text NOT NULL CHECK(digest=encode(sha256(convert_to(body,'UTF8')),'hex')),
          operation_id uuid NOT NULL,journal text NOT NULL DEFAULT 'Main.Plm.IterationReview.operations.v1' CHECK(journal='Main.Plm.IterationReview.operations.v1'),
          PRIMARY KEY(environment_id,tenant_id,id),UNIQUE(environment_id,tenant_id,iteration_id,id),
          FOREIGN KEY(tenant_id,iteration_id) REFERENCES cp6_engineering_iteration_v1.heads,
          FOREIGN KEY(tenant_id,journal,operation_id) REFERENCES cp6_main_owner_v1.operations(tenant_id,owner,operation_id) DEFERRABLE INITIALLY DEFERRED);
        CREATE TABLE cp6_iteration_review_v1.submissions(
          environment_id uuid NOT NULL,tenant_id uuid NOT NULL,id uuid NOT NULL,iteration_id uuid NOT NULL,iteration_revision bigint NOT NULL,
          frozen_revision bigint NOT NULL CHECK(frozen_revision=iteration_revision+1),policy_id uuid NOT NULL,actor_id uuid NOT NULL,
          operation_id uuid NOT NULL,outbox_id uuid NOT NULL,round_id uuid NOT NULL,aggregate_id uuid NOT NULL,binding_id uuid NOT NULL,
          intent_hash text NOT NULL CHECK(intent_hash ~ '^[0-9a-f]{64}$'),
          payload bytea NOT NULL CHECK(octet_length(payload)<=1048576),payload_digest text NOT NULL CHECK(payload_digest=encode(sha256(payload),'hex')),
          authorization_json text NOT NULL,registration_json text NOT NULL,
          journal text NOT NULL DEFAULT 'Main.Plm.IterationReview.operations.v1' CHECK(journal='Main.Plm.IterationReview.operations.v1'),
          package_bytes bytea NOT NULL CHECK(octet_length(package_bytes) BETWEEN 2 AND 16777216),
          package_digest text NOT NULL CHECK(package_digest=encode(sha256(package_bytes),'hex')),
          PRIMARY KEY(environment_id,tenant_id,id),UNIQUE(environment_id,tenant_id,intent_hash),
          UNIQUE(environment_id,tenant_id,id,outbox_id),UNIQUE(environment_id,tenant_id,iteration_id),
          UNIQUE(environment_id,tenant_id,round_id),UNIQUE(environment_id,tenant_id,aggregate_id),UNIQUE(environment_id,tenant_id,binding_id),
          FOREIGN KEY(tenant_id,iteration_id,iteration_revision) REFERENCES cp6_engineering_iteration_v1.versions,
          FOREIGN KEY(tenant_id,iteration_id,frozen_revision) REFERENCES cp6_engineering_iteration_v1.versions,
          FOREIGN KEY(environment_id,tenant_id,iteration_id,policy_id) REFERENCES cp6_iteration_review_v1.policies(environment_id,tenant_id,iteration_id,id),
          FOREIGN KEY(tenant_id,journal,operation_id) REFERENCES cp6_main_owner_v1.operations(tenant_id,owner,operation_id) DEFERRABLE INITIALLY DEFERRED);
        CREATE TABLE cp6_iteration_review_v1.outbox(
          environment_id uuid NOT NULL,tenant_id uuid NOT NULL,id uuid NOT NULL,submission_id uuid NOT NULL,
          payload bytea NOT NULL,payload_digest text NOT NULL CHECK(payload_digest=encode(sha256(payload),'hex')),
          PRIMARY KEY(environment_id,tenant_id,id),UNIQUE(environment_id,tenant_id,submission_id),
          FOREIGN KEY(environment_id,tenant_id,submission_id,id) REFERENCES cp6_iteration_review_v1.submissions(environment_id,tenant_id,id,outbox_id) DEFERRABLE INITIALLY DEFERRED);
        ALTER TABLE cp6_iteration_review_v1.submissions ADD CONSTRAINT iteration_review_outbox_required
          FOREIGN KEY(environment_id,tenant_id,outbox_id) REFERENCES cp6_iteration_review_v1.outbox(environment_id,tenant_id,id) DEFERRABLE INITIALLY DEFERRED;
        CREATE FUNCTION cp6_iteration_review_v1.validate() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
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
              OR package->'issues' IS DISTINCT FROM '[]'::jsonb OR jsonb_array_length(package->'candidates')<1
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
        CREATE FUNCTION cp6_iteration_review_v1.provenance() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF NOT EXISTS(SELECT FROM cp6_main_owner_v1.operations o WHERE o.tenant_id=NEW.tenant_id AND o.owner=NEW.journal
            AND o.operation_id=NEW.operation_id AND o.actor_id=NEW.actor_id AND o.contract='Main.Plm.IterationReview.commands.v1'
            AND o.state='COMMITTED' AND o.object_ids=ARRAY[NEW.iteration_id]) THEN RAISE EXCEPTION 'ITERATION_REVIEW_ORIGINAL_REQUIRED';END IF;RETURN NULL;
        END $$;
        CREATE FUNCTION cp6_iteration_review_v1.stage_complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE b jsonb=convert_from(NEW.body_bytes,'UTF8')::jsonb;link jsonb=b#>'{body,iteration,currentReview}';BEGIN
          IF b#>>'{body,iteration,stage}'='IN_REVIEW' THEN
            IF link IS NULL OR NOT EXISTS(SELECT FROM cp6_iteration_review_v1.submissions s WHERE s.tenant_id=NEW.tenant_id AND s.iteration_id=NEW.object_id
              AND s.frozen_revision<=NEW.revision AND s.id::text=link->>'submissionId' AND s.round_id::text=link->>'roundId' AND s.binding_id::text=link->>'bindingId'
              AND link#>>'{package,owner}'='Main.Plm.IterationReviewPackage.v1' AND link#>>'{package,id}'=s.id::text
              AND link#>>'{package,version}'='1' AND link#>>'{package,digest}'=s.package_digest) THEN RAISE EXCEPTION 'ITERATION_REVIEW_ATOMIC_STAGE_REQUIRED'; END IF;
          END IF;RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER iteration_review_stage_complete AFTER INSERT ON cp6_engineering_iteration_v1.versions
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_iteration_review_v1.stage_complete();
        DO $$ DECLARE t text; BEGIN FOREACH t IN ARRAY ARRAY['policies','submissions','outbox'] LOOP
          EXECUTE format('CREATE TRIGGER iteration_review_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_iteration_review_v1.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable()',t);
          EXECUTE format('CREATE TRIGGER iteration_review_validate BEFORE INSERT ON cp6_iteration_review_v1.%I FOR EACH ROW EXECUTE FUNCTION cp6_iteration_review_v1.validate()',t);
          IF t<>'outbox' THEN EXECUTE format('CREATE CONSTRAINT TRIGGER iteration_review_provenance AFTER INSERT ON cp6_iteration_review_v1.%I DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_iteration_review_v1.provenance()',t); END IF;
        END LOOP; END $$;
        CREATE FUNCTION cp6_iteration_review_v1.ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT NOT EXISTS(SELECT FROM (VALUES ('policies'),('submissions'),('outbox')) r(tbl)
            CROSS JOIN (VALUES ('iteration_review_immutable','cp6_scope_authorization_v1.immutable()',58),
              ('iteration_review_validate','cp6_iteration_review_v1.validate()',7),('iteration_review_provenance','cp6_iteration_review_v1.provenance()',5)) a(name,fn,typ)
            WHERE (r.tbl<>'outbox' OR a.typ<>5) AND NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid=to_regclass('cp6_iteration_review_v1.'||r.tbl)
              AND t.tgname=a.name AND t.tgfoid=to_regprocedure(a.fn) AND t.tgtype=a.typ AND t.tgenabled IN('O','A') AND t.tgqual IS NULL
              AND (a.typ<>5 OR t.tgdeferrable AND t.tginitdeferred)))
            AND EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid='cp6_engineering_iteration_v1.versions'::regclass AND t.tgname='iteration_review_stage_complete'
              AND t.tgfoid='cp6_iteration_review_v1.stage_complete()'::regprocedure AND t.tgtype=5 AND t.tgenabled IN('O','A') AND t.tgqual IS NULL AND t.tgdeferrable AND t.tginitdeferred);
        $$;
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_iteration_review_v1 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_iteration_review_v1 FROM PUBLIC;
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",a.code,a.name,a.sort,t."Id",LOCALTIMESTAMP FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t
          CROSS JOIN (VALUES('review-policy','迭代评审规则',9),('review-submit','迭代送审',10),('review-decide','迭代专业评审',11)) a(code,name,sort)
        WHERE m."MenuKey"='plm-engineering-iteration' AND NOT EXISTS(SELECT FROM public."Sys_MenuAction" x
          WHERE x."TenantId"=t."Id" AND x."MenuId"=m."MenuId" AND x."ActionCode"=a.code);
        """);
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Review packages and receipts require forward recovery.");
}
