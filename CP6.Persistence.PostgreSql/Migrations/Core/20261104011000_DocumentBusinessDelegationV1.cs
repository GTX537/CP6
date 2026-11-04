using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)),Migration("20261104011000_DocumentBusinessDelegationV1")]
public sealed class DocumentBusinessDelegationV1:Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        CREATE TABLE cp6_document_review_control_v1.business_grants(
          environment_id uuid NOT NULL,tenant_id uuid NOT NULL,id uuid NOT NULL,revision bigint NOT NULL CHECK(revision>0),version uuid NOT NULL UNIQUE,
          document_id uuid NOT NULL,submission_id uuid NOT NULL,grantor_id uuid NOT NULL,operation_id uuid NOT NULL,
          journal text NOT NULL DEFAULT 'Main.Plm.DocumentBusinessGrant.operations.v1' CHECK(journal='Main.Plm.DocumentBusinessGrant.operations.v1'),
          body text NOT NULL,PRIMARY KEY(environment_id,tenant_id,id,revision),UNIQUE(environment_id,tenant_id,operation_id),
          FOREIGN KEY(environment_id,tenant_id,submission_id) REFERENCES cp6_document_review_v1.submissions(environment_id,tenant_id,id),
          FOREIGN KEY(tenant_id,journal,operation_id) REFERENCES cp6_main_owner_v1.operations(tenant_id,owner,operation_id) DEFERRABLE INITIALLY DEFERRED);
        CREATE INDEX document_business_grant_subject ON cp6_document_review_control_v1.business_grants(environment_id,tenant_id,document_id,submission_id,id,revision DESC);
        CREATE FUNCTION cp6_document_review_control_v1.business_grant_complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE b jsonb=NEW.body::jsonb;prior cp6_document_review_control_v1.business_grants;BEGIN
          SELECT * INTO prior FROM cp6_document_review_control_v1.business_grants
            WHERE environment_id=NEW.environment_id AND tenant_id=NEW.tenant_id AND id=NEW.id AND revision<NEW.revision ORDER BY revision DESC LIMIT 1;
          IF b->>'grantId' IS DISTINCT FROM NEW.id::text OR b->>'revision' IS DISTINCT FROM NEW.revision::text
            OR b->>'version' IS DISTINCT FROM NEW.version::text OR b->>'documentId' IS DISTINCT FROM NEW.document_id::text
            OR b->>'submissionId' IS DISTINCT FROM NEW.submission_id::text OR b->>'grantorId' IS DISTINCT FROM NEW.grantor_id::text
            OR b->>'operationId' IS DISTINCT FROM NEW.operation_id::text
            OR (prior.id IS NULL AND NEW.revision<>1) OR (prior.id IS NOT NULL AND (NEW.revision<>prior.revision+1
              OR (NEW.document_id,NEW.submission_id,NEW.grantor_id) IS DISTINCT FROM (prior.document_id,prior.submission_id,prior.grantor_id)))
            OR NOT EXISTS(SELECT FROM cp6_document_review_v1.submissions s WHERE s.environment_id=NEW.environment_id AND s.tenant_id=NEW.tenant_id
              AND s.id=NEW.submission_id AND s.document_id=NEW.document_id)
            OR NOT EXISTS(SELECT FROM cp6_main_owner_v1.operations o WHERE o.tenant_id=NEW.tenant_id AND o.owner=NEW.journal AND o.operation_id=NEW.operation_id
              AND o.actor_id=NEW.grantor_id AND o.contract='Main.Plm.DocumentBusinessGrant.commands.v1' AND o.state='COMMITTED' AND o.object_ids=ARRAY[NEW.document_id]
              AND convert_from(o.result_bytes,'UTF8')::jsonb->'Grant'=jsonb_build_object(
                'GrantId',b->'grantId','Revision',b->'revision','Version',b->'version','DocumentId',b->'documentId','SubmissionId',b->'submissionId',
                'GrantorId',b->'grantorId','DelegateId',b->'delegateId','FromUtc',b->'fromUtc','ToUtc',b->'toUtc','Actions',b->'actions','Enabled',b->'enabled',
                'OperationId',b->'operationId','RecordedAtUtc',b->'recordedAtUtc','Reason',b->'reason'))
            THEN RAISE EXCEPTION 'DOCUMENT_BUSINESS_GRANT_COMMAND_REQUIRED' USING ERRCODE='23514';END IF;RETURN NULL;
        END $$;
        CREATE TRIGGER business_grant_authority BEFORE INSERT ON cp6_document_review_control_v1.business_grants FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.before_authority_change();
        CREATE TRIGGER business_grant_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_document_review_control_v1.business_grants FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE CONSTRAINT TRIGGER business_grant_complete AFTER INSERT ON cp6_document_review_control_v1.business_grants DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_document_review_control_v1.business_grant_complete();
        CREATE FUNCTION cp6_document_review_control_v1.business_grants_ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT cp6_scope_authorization_v1.writers_ready() AND NOT EXISTS(SELECT FROM (VALUES
            ('business_grant_authority','cp6_scope_authorization_v1.before_authority_change()',6,false),
            ('business_grant_immutable','cp6_scope_authorization_v1.immutable()',58,false),
            ('business_grant_complete','cp6_document_review_control_v1.business_grant_complete()',5,true)) a(name,fn,typ,deferred)
            WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid='cp6_document_review_control_v1.business_grants'::regclass AND t.tgname=a.name
              AND t.tgfoid=to_regprocedure(a.fn) AND t.tgtype=a.typ AND t.tgenabled IN('O','A') AND t.tgqual IS NULL AND t.tgdeferrable=a.deferred AND t.tginitdeferred=a.deferred));
        $$;
        REVOKE ALL ON cp6_document_review_control_v1.business_grants FROM PUBLIC;
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",'review-delegate','管理本人文档评审业务委派',17,t."Id",LOCALTIMESTAMP FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t
        WHERE m."MenuKey"='plm-controlled-document' AND NOT EXISTS(SELECT FROM public."Sys_MenuAction" x WHERE x."TenantId"=t."Id" AND x."MenuId"=m."MenuId" AND x."ActionCode"='review-delegate');
        """);
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Authorization history is forward-only.");
}
