using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261029180000_RequirementSetReviewV1")]
public sealed class RequirementSetReviewV1 : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE SCHEMA cp6_requirement_set_review_v1;
        REVOKE ALL ON SCHEMA cp6_requirement_set_review_v1 FROM PUBLIC;
        CREATE TABLE cp6_requirement_set_review_v1.policies(
          sequence bigint GENERATED ALWAYS AS IDENTITY UNIQUE,
          environment_id uuid NOT NULL,tenant_id uuid NOT NULL,requirement_set_id uuid NOT NULL,id uuid NOT NULL,actor_id uuid NOT NULL,
          body text NOT NULL,digest text NOT NULL CHECK(digest=encode(sha256(convert_to(body,'UTF8')),'hex')),
          operation_id uuid NOT NULL,journal text NOT NULL DEFAULT 'Main.Plm.RequirementSetReview.operations.v1' CHECK(journal='Main.Plm.RequirementSetReview.operations.v1'),
          PRIMARY KEY(environment_id,tenant_id,id),UNIQUE(environment_id,tenant_id,requirement_set_id,id),
          FOREIGN KEY(tenant_id,requirement_set_id) REFERENCES cp6_requirement_set_v1.heads,
          FOREIGN KEY(tenant_id,journal,operation_id) REFERENCES cp6_main_owner_v1.operations(tenant_id,owner,operation_id) DEFERRABLE INITIALLY DEFERRED);
        CREATE TABLE cp6_requirement_set_review_v1.submissions(
          environment_id uuid NOT NULL,tenant_id uuid NOT NULL,id uuid NOT NULL,requirement_set_id uuid NOT NULL,requirement_set_revision bigint NOT NULL,
          policy_id uuid NOT NULL,actor_id uuid NOT NULL,operation_id uuid NOT NULL,outbox_id uuid NOT NULL,
          intent_hash text NOT NULL CHECK(intent_hash ~ '^[0-9a-f]{64}$'),
          payload bytea NOT NULL CHECK(octet_length(payload)<=1048576),payload_digest text NOT NULL CHECK(payload_digest=encode(sha256(payload),'hex')),
          authorization_json text NOT NULL,journal text NOT NULL DEFAULT 'Main.Plm.RequirementSetReview.operations.v1' CHECK(journal='Main.Plm.RequirementSetReview.operations.v1'),
          package_bytes bytea NOT NULL CHECK(octet_length(package_bytes) BETWEEN 2 AND 16777216),
          package_digest text NOT NULL CHECK(package_digest=encode(sha256(package_bytes),'hex')),
          PRIMARY KEY(environment_id,tenant_id,id),UNIQUE(environment_id,tenant_id,intent_hash),
          UNIQUE(environment_id,tenant_id,id,outbox_id),
          FOREIGN KEY(tenant_id,requirement_set_id,requirement_set_revision) REFERENCES cp6_requirement_set_v1.versions,
          FOREIGN KEY(environment_id,tenant_id,requirement_set_id,policy_id) REFERENCES cp6_requirement_set_review_v1.policies(environment_id,tenant_id,requirement_set_id,id),
          FOREIGN KEY(tenant_id,journal,operation_id) REFERENCES cp6_main_owner_v1.operations(tenant_id,owner,operation_id) DEFERRABLE INITIALLY DEFERRED);
        CREATE TABLE cp6_requirement_set_review_v1.outbox(
          environment_id uuid NOT NULL,tenant_id uuid NOT NULL,id uuid NOT NULL,submission_id uuid NOT NULL,
          payload bytea NOT NULL,payload_digest text NOT NULL CHECK(payload_digest=encode(sha256(payload),'hex')),
          PRIMARY KEY(environment_id,tenant_id,id),UNIQUE(environment_id,tenant_id,submission_id),
          FOREIGN KEY(environment_id,tenant_id,submission_id,id) REFERENCES cp6_requirement_set_review_v1.submissions(environment_id,tenant_id,id,outbox_id) DEFERRABLE INITIALLY DEFERRED);
        ALTER TABLE cp6_requirement_set_review_v1.submissions ADD CONSTRAINT requirement_set_review_outbox_required
          FOREIGN KEY(environment_id,tenant_id,outbox_id) REFERENCES cp6_requirement_set_review_v1.outbox(environment_id,tenant_id,id) DEFERRABLE INITIALLY DEFERRED;
        CREATE FUNCTION cp6_requirement_set_review_v1.validate() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE b jsonb;d text;p bytea;policy_digest text;package jsonb;requirementSet jsonb; BEGIN
          IF TG_TABLE_NAME='policies' THEN
            b:=NEW.body::jsonb;
            IF b->>'environmentId' IS DISTINCT FROM NEW.environment_id::text OR b->>'tenantId' IS DISTINCT FROM NEW.tenant_id::text
              OR b->>'requirementSetId' IS DISTINCT FROM NEW.requirement_set_id::text OR b->>'id' IS DISTINCT FROM NEW.id::text
              OR b->>'actorId' IS DISTINCT FROM NEW.actor_id::text OR b#>>'{body,operationId}' IS DISTINCT FROM NEW.operation_id::text
              THEN RAISE EXCEPTION 'REQUIREMENT_SET_POLICY_IDENTITY_INVALID'; END IF;
          ELSIF TG_TABLE_NAME='submissions' THEN
            b:=convert_from(NEW.payload,'UTF8')::jsonb;
            SELECT body_digest,convert_from(body_bytes,'UTF8')::jsonb INTO d,requirementSet FROM cp6_requirement_set_v1.versions WHERE tenant_id=NEW.tenant_id
              AND object_id=NEW.requirement_set_id AND revision=NEW.requirement_set_revision;
            SELECT digest INTO policy_digest FROM cp6_requirement_set_review_v1.policies WHERE environment_id=NEW.environment_id
              AND tenant_id=NEW.tenant_id AND requirement_set_id=NEW.requirement_set_id AND id=NEW.policy_id;
            IF d IS NULL OR policy_digest IS NULL OR b->>'schema' IS DISTINCT FROM 'oa.app/2.0'
              OR b#>>'{scope,environmentId}' IS DISTINCT FROM NEW.environment_id::text OR b#>>'{scope,tenantId}' IS DISTINCT FROM NEW.tenant_id::text
              OR b->>'submissionId' IS DISTINCT FROM NEW.id::text OR b->>'commandId' IS DISTINCT FROM NEW.operation_id::text
              OR b->>'starterId' IS DISTINCT FROM NEW.actor_id::text OR b->>'ownerOutboxId' IS DISTINCT FROM NEW.outbox_id::text
              OR b#>>'{subject,businessOwner}' IS DISTINCT FROM 'Main.Plm.RequirementSet.v1'
              OR b#>>'{subject,bizType}' IS DISTINCT FROM 'PLM_REQUIREMENT_SET_REVIEW' OR b#>>'{subject,subjectKind}' IS DISTINCT FROM 'REQUIREMENT_SET_REVISION'
              OR b#>>'{subject,subjectId}' IS DISTINCT FROM NEW.requirement_set_id::text OR b#>>'{subject,businessRevision}' IS DISTINCT FROM NEW.requirement_set_revision::text
              OR b#>>'{subject,contentDigest,value}' IS DISTINCT FROM d OR b#>>'{policy,ref,id}' IS DISTINCT FROM NEW.policy_id::text
              OR b#>>'{policy,ref,owner}' IS DISTINCT FROM 'Main.Plm.RequirementSetReviewPolicy.v1' OR b#>>'{policy,ref,version}' IS DISTINCT FROM '1'
              OR b#>>'{policy,digest,value}' IS DISTINCT FROM policy_digest THEN RAISE EXCEPTION 'REQUIREMENT_SET_REVIEW_SUBMISSION_INVALID'; END IF;
            package:=convert_from(NEW.package_bytes,'UTF8')::jsonb;
            IF package#>>'{requirementSet,id}' IS DISTINCT FROM NEW.requirement_set_id::text OR package#>>'{requirementSet,version}' IS DISTINCT FROM NEW.requirement_set_revision::text
              OR package#>>'{requirementSet,digest}' IS DISTINCT FROM d OR package->'body' IS DISTINCT FROM requirementSet->'body' THEN RAISE EXCEPTION 'REQUIREMENT_SET_REVIEW_PACKAGE_INVALID'; END IF;
          ELSE
            SELECT payload INTO p FROM cp6_requirement_set_review_v1.submissions WHERE environment_id=NEW.environment_id AND tenant_id=NEW.tenant_id AND id=NEW.submission_id;
            IF p IS NULL OR p<>NEW.payload THEN RAISE EXCEPTION 'REQUIREMENT_SET_REVIEW_OUTBOX_INVALID'; END IF;
          END IF; RETURN NEW;
        END $$;
        DO $$ DECLARE t text; BEGIN FOREACH t IN ARRAY ARRAY['policies','submissions','outbox'] LOOP
          EXECUTE format('CREATE TRIGGER requirement_set_review_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_requirement_set_review_v1.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable()',t);
          EXECUTE format('CREATE TRIGGER requirement_set_review_validate BEFORE INSERT ON cp6_requirement_set_review_v1.%I FOR EACH ROW EXECUTE FUNCTION cp6_requirement_set_review_v1.validate()',t);
        END LOOP; END $$;
        CREATE FUNCTION cp6_requirement_set_review_v1.ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT NOT EXISTS(SELECT FROM (VALUES ('policies'),('submissions'),('outbox')) r(tbl)
            CROSS JOIN (VALUES ('requirement_set_review_immutable','cp6_scope_authorization_v1.immutable()',58),
              ('requirement_set_review_validate','cp6_requirement_set_review_v1.validate()',7)) a(name,fn,typ)
            WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid=to_regclass('cp6_requirement_set_review_v1.'||r.tbl)
              AND t.tgname=a.name AND t.tgfoid=to_regprocedure(a.fn) AND t.tgtype=a.typ AND t.tgenabled IN('O','A') AND t.tgqual IS NULL));
        $$;
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_requirement_set_review_v1 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_requirement_set_review_v1 FROM PUBLIC;
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",a.code,a.name,a.sort,t."Id",LOCALTIMESTAMP FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t
          CROSS JOIN (VALUES('review-policy','需求集评审规则',3),('review-submit','需求集送审',4),('review-decide','需求集专业评审',5),('review-apply','需求集批准应用',6)) a(code,name,sort)
        WHERE m."MenuKey"='plm-requirement-set' AND NOT EXISTS(SELECT FROM public."Sys_MenuAction" x
          WHERE x."TenantId"=t."Id" AND x."MenuId"=m."MenuId" AND x."ActionCode"=a.code);
        """);
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("RequirementSet review originals require forward recovery.");
}
