using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)),Migration("20261030004000_EngineeringBaselineV1")]
public sealed class EngineeringBaselineV1:Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        CREATE SCHEMA cp6_engineering_baseline_v1;
        REVOKE ALL ON SCHEMA cp6_engineering_baseline_v1 FROM PUBLIC;
        CREATE TABLE cp6_engineering_baseline_v1.baselines(
          environment_id uuid NOT NULL,tenant_id uuid NOT NULL,baseline_id uuid NOT NULL,actor_id uuid NOT NULL,
          revision_id uuid NOT NULL,revision_version bigint NOT NULL CHECK(revision_version>0),candidate_id uuid NOT NULL,candidate_version bigint NOT NULL CHECK(candidate_version>0),
          configuration_id uuid NOT NULL,configuration_version bigint NOT NULL CHECK(configuration_version>0),operation_id uuid NOT NULL,intent_id uuid NOT NULL,
          body bytea NOT NULL CHECK(octet_length(body) BETWEEN 2 AND 268435456),digest text NOT NULL CHECK(digest=encode(sha256(body),'hex')),
          binding bytea,binding_digest text,CHECK((binding IS NULL AND binding_digest IS NULL) OR (binding IS NOT NULL AND binding_digest IS NOT NULL AND binding_digest=encode(sha256(binding),'hex'))),
          journal text NOT NULL DEFAULT 'Main.Plm.EngineeringFreeze.operations.v1' CHECK(journal='Main.Plm.EngineeringFreeze.operations.v1'),
          PRIMARY KEY(environment_id,tenant_id,baseline_id),UNIQUE(environment_id,tenant_id,candidate_id,candidate_version,configuration_id,configuration_version),
          FOREIGN KEY(tenant_id,revision_id,revision_version) REFERENCES cp6_engineering_revision_v1.working_versions(tenant_id,revision_id,revision),
          FOREIGN KEY(tenant_id,journal,operation_id) REFERENCES cp6_main_owner_v1.operations(tenant_id,owner,operation_id) DEFERRABLE INITIALLY DEFERRED);
        CREATE TABLE cp6_engineering_baseline_v1.members(
          environment_id uuid NOT NULL,tenant_id uuid NOT NULL,baseline_id uuid NOT NULL,ordinal integer NOT NULL CHECK(ordinal>=0),occurrence_id uuid NOT NULL,
          body bytea NOT NULL,digest text NOT NULL CHECK(digest=encode(sha256(body),'hex')),
          PRIMARY KEY(environment_id,tenant_id,baseline_id,ordinal),UNIQUE(environment_id,tenant_id,baseline_id,occurrence_id),
          FOREIGN KEY(environment_id,tenant_id,baseline_id) REFERENCES cp6_engineering_baseline_v1.baselines);
        CREATE TABLE cp6_engineering_baseline_v1.intents(
          environment_id uuid NOT NULL,tenant_id uuid NOT NULL,baseline_id uuid NOT NULL,id uuid NOT NULL,payload bytea NOT NULL,digest text NOT NULL CHECK(digest=encode(sha256(payload),'hex')),
          PRIMARY KEY(environment_id,tenant_id,id),UNIQUE(environment_id,tenant_id,baseline_id),
          FOREIGN KEY(environment_id,tenant_id,baseline_id) REFERENCES cp6_engineering_baseline_v1.baselines);
        CREATE TABLE cp6_engineering_baseline_v1.successors(
          environment_id uuid NOT NULL,tenant_id uuid NOT NULL,baseline_id uuid NOT NULL,operation_id uuid NOT NULL,actor_id uuid NOT NULL,revision_id uuid NOT NULL,revision_version bigint NOT NULL,
          body bytea NOT NULL,digest text NOT NULL CHECK(digest=encode(sha256(body),'hex')),
          journal text NOT NULL DEFAULT 'Main.Plm.EngineeringBaseline.successor.v1' CHECK(journal='Main.Plm.EngineeringBaseline.successor.v1'),
          PRIMARY KEY(environment_id,tenant_id,baseline_id),UNIQUE(environment_id,tenant_id,revision_id,revision_version),
          FOREIGN KEY(environment_id,tenant_id,baseline_id) REFERENCES cp6_engineering_baseline_v1.baselines,
          FOREIGN KEY(tenant_id,revision_id,revision_version) REFERENCES cp6_engineering_revision_v1.working_versions(tenant_id,revision_id,revision),
          FOREIGN KEY(tenant_id,journal,operation_id) REFERENCES cp6_main_owner_v1.operations(tenant_id,owner,operation_id) DEFERRABLE INITIALLY DEFERRED);
        CREATE FUNCTION cp6_engineering_baseline_v1.complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE b cp6_engineering_baseline_v1.baselines;p jsonb;c jsonb;r jsonb;v jsonb;j cp6_main_owner_v1.operations; BEGIN
          SELECT * INTO b FROM cp6_engineering_baseline_v1.baselines WHERE environment_id=NEW.environment_id AND tenant_id=NEW.tenant_id AND baseline_id=NEW.baseline_id;
          p=convert_from(b.body,'UTF8')::jsonb;c=p->'command';
          IF p->>'schema' IS DISTINCT FROM 'Main.Plm.EngineeringBaseline/1' OR p->>'version' IS DISTINCT FROM '1'
            OR p->>'environmentId' IS DISTINCT FROM b.environment_id::text OR p->>'tenantId' IS DISTINCT FROM b.tenant_id::text
            OR p->>'id' IS DISTINCT FROM b.baseline_id::text OR p->>'actorId' IS DISTINCT FROM b.actor_id::text
            OR c->>'operationId' IS DISTINCT FROM b.operation_id::text OR c#>>'{revision,id}' IS DISTINCT FROM b.revision_id::text
            OR c#>>'{revision,version}' IS DISTINCT FROM b.revision_version::text OR c#>>'{engineeringCandidate,id}' IS DISTINCT FROM b.candidate_id::text
            OR c#>>'{engineeringCandidate,version}' IS DISTINCT FROM b.candidate_version::text OR c#>>'{configuration,id}' IS DISTINCT FROM b.configuration_id::text
            OR c#>>'{configuration,version}' IS DISTINCT FROM b.configuration_version::text OR coalesce(p->>'evidenceMode','') NOT IN('REAL','DEMO')
            OR p#>>'{check,state}' IS DISTINCT FROM 'PASS' OR p#>>'{review,receipt,application}' IS DISTINCT FROM 'APPLIED'
            OR p#>>'{review,receipt,outcomeCode}' IS DISTINCT FROM 'ITERATION_APPROVED'
            OR p#>>'{review,receipt,receiptId}' IS DISTINCT FROM c->>'applicationReceiptId'
            OR p#>>'{review,package,digest}' IS DISTINCT FROM c->>'reviewPackageDigest'
            OR p#>>'{manifest,digest}' IS DISTINCT FROM c->>'technicalManifestDigest'
            OR p#>>'{manifest,digest}' IS DISTINCT FROM encode(sha256(convert_to(p#>>'{manifest,originalJson}','UTF8')),'hex')
            THEN RAISE EXCEPTION 'ENGINEERING_BASELINE_ORIGINAL_INVALID'; END IF;
          SELECT * INTO j FROM cp6_main_owner_v1.operations WHERE tenant_id=b.tenant_id AND owner=b.journal AND operation_id=b.operation_id;
          IF j.actor_id IS DISTINCT FROM b.actor_id OR j.state IS DISTINCT FROM 'COMMITTED' OR j.object_ids IS DISTINCT FROM ARRAY[b.baseline_id]
            OR j.contract IS DISTINCT FROM 'Main.Plm.EngineeringFreeze.commands.v1' THEN RAISE EXCEPTION 'ENGINEERING_FREEZE_ORIGINAL_REQUIRED'; END IF;
          IF NOT EXISTS(SELECT FROM cp6_engineering_check_v1.runs a WHERE a.tenant_id=b.tenant_id AND a.id=(c#>>'{sourceCheck,id}')::uuid
            AND a.digest=c#>>'{sourceCheck,digest}' AND convert_from(a.body,'UTF8')=p->>'checkOriginal' AND convert_from(a.body,'UTF8')::jsonb=p->'check')
            OR NOT EXISTS(SELECT FROM cp6_iteration_review_application_v1.applications a JOIN cp6_iteration_review_application_v1.receipts r
              ON r.environment_id=a.environment_id AND r.tenant_id=a.tenant_id AND r.id=a.receipt_id
              WHERE a.environment_id=b.environment_id AND a.tenant_id=b.tenant_id AND a.approved AND a.submission_id=(c->>'reviewSubmissionId')::uuid
                AND a.receipt_id=(c->>'applicationReceiptId')::uuid AND convert_from(a.body,'UTF8')=p#>>'{review,applicationOriginal}'
                AND convert_from(r.payload,'UTF8')=p#>>'{review,receiptOriginal}')
            OR NOT EXISTS(SELECT FROM cp6_iteration_review_fact_v1.facts a WHERE a.environment_id=b.environment_id AND a.tenant_id=b.tenant_id
              AND a.decision_id=(p#>>'{review,decision,decisionId}')::uuid AND a.payload_hash=p#>>'{review,decisionOriginalHash}'
              AND convert_from(a.payload,'UTF8')=p#>>'{review,decisionOriginal}')
            OR NOT EXISTS(SELECT FROM cp6_iteration_review_v1.submissions a WHERE a.environment_id=b.environment_id AND a.tenant_id=b.tenant_id
              AND a.id=(c->>'reviewSubmissionId')::uuid AND a.package_digest=c->>'reviewPackageDigest'
              AND convert_from(a.package_bytes,'UTF8')=p#>>'{review,packageOriginal}' AND convert_from(a.payload,'UTF8')=p#>>'{review,submissionOriginal}')
            THEN RAISE EXCEPTION 'ENGINEERING_BASELINE_NATIVE_PROOF_REQUIRED'; END IF;
          SELECT convert_from(body_bytes,'UTF8')::jsonb INTO v FROM cp6_engineering_revision_v1.working_versions WHERE tenant_id=b.tenant_id AND revision_id=b.revision_id AND revision=b.revision_version;
          IF v->>'candidateId' IS DISTINCT FROM b.candidate_id::text OR v->>'etag' IS DISTINCT FROM c->>'expectedEtag'
            OR v->'draft' IS DISTINCT FROM p#>'{revision,draft}' OR v#>'{draft,configuration,reference}' IS DISTINCT FROM c->'configuration'
            OR (SELECT count(*) FROM cp6_engineering_baseline_v1.members WHERE environment_id=b.environment_id AND tenant_id=b.tenant_id AND baseline_id=b.baseline_id)
              <>jsonb_array_length(p#>'{revision,draft,components}')
            OR EXISTS(SELECT FROM cp6_engineering_baseline_v1.members a WHERE a.environment_id=b.environment_id AND a.tenant_id=b.tenant_id AND a.baseline_id=b.baseline_id
              AND (convert_from(a.body,'UTF8')::jsonb IS DISTINCT FROM (p#>'{revision,draft,components}')->a.ordinal
                OR (convert_from(a.body,'UTF8')::jsonb)->>'occurrenceId' IS DISTINCT FROM a.occurrence_id::text))
            THEN RAISE EXCEPTION 'ENGINEERING_BASELINE_MEMBERS_INCOMPLETE'; END IF;
          IF NOT EXISTS(SELECT FROM cp6_engineering_baseline_v1.intents a WHERE a.environment_id=b.environment_id AND a.tenant_id=b.tenant_id AND a.baseline_id=b.baseline_id
            AND a.id=b.intent_id AND convert_from(a.payload,'UTF8')::jsonb#>>'{result,baseline,digest}'=b.digest)
            THEN RAISE EXCEPTION 'ENGINEERING_BASELINE_INTENT_REQUIRED'; END IF;
          IF p->>'outputApplicability'='APPLIES' THEN
            IF b.binding IS NULL OR p->>'outputRelationsOriginal' IS NULL THEN RAISE EXCEPTION 'ENGINEERING_BASELINE_BINDING_REQUIRED'; END IF;
            r=convert_from(b.binding,'UTF8')::jsonb;
            IF r->>'schema' IS DISTINCT FROM 'ENG-BASELINE-OUTPUT-BINDING/1' OR r->>'freezeCommandId' IS DISTINCT FROM b.operation_id::text
              OR r#>>'{scope,environmentId}' IS DISTINCT FROM b.environment_id::text OR r#>>'{scope,tenantId}' IS DISTINCT FROM b.tenant_id::text
              OR r->>'relationId' IS DISTINCT FROM p#>>'{outputRelations,id}' OR r->>'relationVersion' IS DISTINCT FROM p#>>'{outputRelations,version}'
              OR r->>'relationBodyDigest' IS DISTINCT FROM (p->>'outputRelationsOriginal')::jsonb->>'bodyDigest'
              THEN RAISE EXCEPTION 'ENGINEERING_BASELINE_BINDING_INVALID'; END IF;
          ELSIF p->>'outputApplicability' IS DISTINCT FROM 'NOT_APPLICABLE_BY_APPROVED_POLICY' OR b.binding IS NOT NULL
            OR p#>>'{check,policyBody,engineeringChecks,manufacturing}'='INTERNAL' THEN RAISE EXCEPTION 'ENGINEERING_BASELINE_APPLICABILITY_INVALID'; END IF;
          IF TG_TABLE_NAME='successors' THEN
            r=convert_from(NEW.body,'UTF8')::jsonb;
            IF NEW.revision_id<>b.revision_id OR NEW.revision_version<>b.revision_version+1 OR r#>>'{parentBaseline,digest}' IS DISTINCT FROM b.digest
              OR r#>>'{revision,id}' IS DISTINCT FROM NEW.revision_id::text OR r#>>'{revision,version}' IS DISTINCT FROM NEW.revision_version::text
              OR NOT EXISTS(SELECT FROM cp6_main_owner_v1.operations o WHERE o.tenant_id=NEW.tenant_id AND o.owner=NEW.journal AND o.operation_id=NEW.operation_id
                AND o.actor_id=NEW.actor_id AND o.state='COMMITTED' AND o.object_ids=ARRAY[b.baseline_id,NEW.revision_id])
              THEN RAISE EXCEPTION 'ENGINEERING_BASELINE_SUCCESSOR_INVALID'; END IF;
          END IF; RETURN NULL;
        END $$;
        CREATE FUNCTION cp6_engineering_baseline_v1.successor_required() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF EXISTS(SELECT FROM cp6_engineering_baseline_v1.baselines b WHERE b.tenant_id=NEW.tenant_id AND b.revision_id=NEW.revision_id AND b.revision_version=NEW.revision-1
            AND NOT EXISTS(SELECT FROM cp6_engineering_baseline_v1.successors s WHERE s.environment_id=b.environment_id AND s.tenant_id=b.tenant_id AND s.baseline_id=b.baseline_id
              AND s.revision_id=NEW.revision_id AND s.revision_version=NEW.revision)) THEN RAISE EXCEPTION 'ENGINEERING_BASELINE_SUCCESSOR_REQUIRED'; END IF;
          RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER engineering_baseline_successor_required AFTER INSERT ON cp6_engineering_revision_v1.working_versions
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_engineering_baseline_v1.successor_required();
        DO $$ DECLARE tbl text; BEGIN FOREACH tbl IN ARRAY ARRAY['baselines','members','intents','successors'] LOOP
          EXECUTE format('CREATE TRIGGER engineering_baseline_root BEFORE INSERT ON cp6_engineering_baseline_v1.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change()',tbl);
          EXECUTE format('CREATE TRIGGER engineering_baseline_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_engineering_baseline_v1.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable()',tbl);
          EXECUTE format('CREATE CONSTRAINT TRIGGER engineering_baseline_complete AFTER INSERT ON cp6_engineering_baseline_v1.%I DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_engineering_baseline_v1.complete()',tbl);
        END LOOP; END $$;
        CREATE FUNCTION cp6_engineering_baseline_v1.ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT NOT EXISTS(SELECT FROM (VALUES('baselines'),('members'),('intents'),('successors')) a(tbl)
            CROSS JOIN(VALUES('engineering_baseline_root','cp6_authentication_v1.before_change()',6),('engineering_baseline_immutable','cp6_scope_authorization_v1.immutable()',58),
              ('engineering_baseline_complete','cp6_engineering_baseline_v1.complete()',5)) b(name,fn,typ)
            WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid=to_regclass('cp6_engineering_baseline_v1.'||a.tbl) AND t.tgname=b.name AND t.tgfoid=to_regprocedure(b.fn)
              AND t.tgtype=b.typ AND t.tgenabled IN('O','A') AND t.tgqual IS NULL AND (b.name<>'engineering_baseline_complete' OR t.tgdeferrable AND t.tginitdeferred)))
          AND EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid='cp6_engineering_revision_v1.working_versions'::regclass AND t.tgname='engineering_baseline_successor_required'
            AND t.tgfoid='cp6_engineering_baseline_v1.successor_required()'::regprocedure AND t.tgtype=5 AND t.tgenabled IN('O','A') AND t.tgqual IS NULL AND t.tgdeferrable AND t.tginitdeferred);
        $$;
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_engineering_baseline_v1 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_engineering_baseline_v1 FROM PUBLIC;
        LOCK TABLE public."Sys_Menus" IN SHARE ROW EXCLUSIVE MODE;
        INSERT INTO public."Sys_Menus"("MenuId","MenuKey","MenuName","RoutePath","Icon","ParentId","OrderNo","Enable","CreateDate")
        SELECT (SELECT CASE WHEN COALESCE(MAX("MenuId"),0)<2147483647 THEN COALESCE(MAX("MenuId"),0)+1 ELSE
          (SELECT min(candidate) FROM (SELECT 1 AS candidate UNION SELECT "MenuId"+1 FROM public."Sys_Menus" WHERE "MenuId">=1 AND "MenuId"<2147483647) available
          WHERE NOT EXISTS(SELECT FROM public."Sys_Menus" used WHERE used."MenuId"=available.candidate)) END FROM public."Sys_Menus"),
          'plm-engineering-baseline','工程基线','/plm/engineering-baselines','Lock',NULL,159,true,LOCALTIMESTAMP
          WHERE NOT EXISTS(SELECT FROM public."Sys_Menus" WHERE "MenuKey"='plm-engineering-baseline');
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",a.code,a.name,a.sort,t."Id",LOCALTIMESTAMP FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t
          CROSS JOIN(VALUES('query','读取基线',0),('freeze','冻结工程候选',1),('cancel','取消未知冻结操作',2),('successor','开始后继设计',3)) a(code,name,sort)
          WHERE m."MenuKey"='plm-engineering-baseline' AND NOT EXISTS(SELECT FROM public."Sys_MenuAction" x WHERE x."TenantId"=t."Id" AND x."MenuId"=m."MenuId" AND x."ActionCode"=a.code);
        """);
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Immutable engineering baselines require forward recovery.");
}
