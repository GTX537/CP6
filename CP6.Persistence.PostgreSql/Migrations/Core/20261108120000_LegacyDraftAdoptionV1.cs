using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)),Migration("20261108120000_LegacyDraftAdoptionV1")]
public sealed class LegacyDraftAdoptionV1:Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        CREATE TABLE cp6_personal_form_v2.legacy_source_revisions(
          kind text NOT NULL CHECK(kind IN('VERSION','DRAFT')),id uuid NOT NULL,tenant_id uuid NOT NULL,revision uuid NOT NULL,
          PRIMARY KEY(kind,id));
        INSERT INTO cp6_personal_form_v2.legacy_source_revisions SELECT 'VERSION',"Id","TenantId",gen_random_uuid() FROM public."Wf_FormDefVersion";
        INSERT INTO cp6_personal_form_v2.legacy_source_revisions SELECT 'DRAFT',"Id","TenantId",gen_random_uuid() FROM public."Wf_FormDraft";
        CREATE FUNCTION cp6_personal_form_v2.legacy_source_changed() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE r jsonb;k text;BEGIN
          k=CASE WHEN TG_TABLE_NAME='Wf_FormDraft' THEN 'DRAFT' ELSE 'VERSION' END;
          IF TG_OP='TRUNCATE' THEN UPDATE cp6_personal_form_v2.legacy_source_revisions SET revision=gen_random_uuid() WHERE kind=k;RETURN NULL;END IF;
          r=CASE WHEN TG_OP='DELETE' THEN to_jsonb(OLD) ELSE to_jsonb(NEW) END;
          INSERT INTO cp6_personal_form_v2.legacy_source_revisions VALUES(k,(r->>'Id')::uuid,(r->>'TenantId')::uuid,gen_random_uuid())
          ON CONFLICT(kind,id) DO UPDATE SET tenant_id=EXCLUDED.tenant_id,revision=EXCLUDED.revision;
          RETURN NULL;
        END $$;
        CREATE TRIGGER legacy_source_root BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON public."Wf_FormDraft"
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER legacy_source_revision AFTER INSERT OR UPDATE OR DELETE ON public."Wf_FormDraft"
          FOR EACH ROW EXECUTE FUNCTION cp6_personal_form_v2.legacy_source_changed();
        CREATE TRIGGER legacy_source_truncate AFTER TRUNCATE ON public."Wf_FormDraft"
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_personal_form_v2.legacy_source_changed();
        CREATE TRIGGER legacy_source_root BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON public."Wf_FormDefVersion"
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER legacy_source_revision AFTER INSERT OR UPDATE OR DELETE ON public."Wf_FormDefVersion"
          FOR EACH ROW EXECUTE FUNCTION cp6_personal_form_v2.legacy_source_changed();
        CREATE TRIGGER legacy_source_truncate AFTER TRUNCATE ON public."Wf_FormDefVersion"
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_personal_form_v2.legacy_source_changed();
        CREATE TABLE cp6_personal_form_v2.legacy_source_bindings(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,legacy_version_id uuid NOT NULL,legacy_form_id uuid NOT NULL,
          source_revision uuid NOT NULL,head_id uuid NOT NULL,form_version uuid NOT NULL,enabled boolean NOT NULL,
          revision uuid NOT NULL DEFAULT gen_random_uuid(),bound_at timestamptz NOT NULL DEFAULT clock_timestamp(),bound_by uuid NOT NULL,reason text NOT NULL,
          PRIMARY KEY(tenant_id,environment_id,legacy_version_id));
        CREATE TRIGGER legacy_binding_root BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_personal_form_v2.legacy_source_bindings
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE FUNCTION cp6_personal_form_v2.legacy_binding_version() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP='UPDATE' AND (NEW.tenant_id,NEW.environment_id,NEW.legacy_version_id) IS DISTINCT FROM (OLD.tenant_id,OLD.environment_id,OLD.legacy_version_id)
            THEN RAISE EXCEPTION 'LEGACY_SOURCE_BINDING_IDENTITY_IMMUTABLE' USING ERRCODE='23514';END IF;
          NEW.revision=gen_random_uuid();NEW.bound_at=clock_timestamp();RETURN NEW;
        END $$;
        CREATE TRIGGER legacy_binding_version BEFORE INSERT OR UPDATE ON cp6_personal_form_v2.legacy_source_bindings
          FOR EACH ROW EXECUTE FUNCTION cp6_personal_form_v2.legacy_binding_version();
        CREATE TRIGGER legacy_binding_no_delete BEFORE DELETE OR TRUNCATE ON cp6_personal_form_v2.legacy_source_bindings
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TABLE cp6_personal_form_v2.legacy_adoptions(
          draft_id uuid PRIMARY KEY REFERENCES cp6_personal_form_v2.drafts,
          command_id uuid NOT NULL UNIQUE REFERENCES cp6_personal_form_v2.commands,
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,owner_id uuid NOT NULL,
          source_draft_id uuid NOT NULL,source_version_id uuid NOT NULL,source_draft_revision uuid NOT NULL,source_schema_revision uuid NOT NULL,
          binding_revision uuid NOT NULL,policy_version uuid NOT NULL,policy_ref text NOT NULL,adopt_until timestamptz NOT NULL);
        CREATE TRIGGER legacy_adoption_root BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_personal_form_v2.legacy_adoptions
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER legacy_adoption_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_personal_form_v2.legacy_adoptions
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE FUNCTION cp6_personal_form_v2.legacy_adoption_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE c cp6_personal_form_v2.commands;d cp6_personal_form_v2.drafts;s public."Wf_FormDraft";b cp6_personal_form_v2.legacy_source_bindings;BEGIN
          SELECT * INTO c FROM cp6_personal_form_v2.commands WHERE id=NEW.command_id;
          SELECT * INTO d FROM cp6_personal_form_v2.drafts WHERE id=NEW.draft_id;
          SELECT * INTO s FROM public."Wf_FormDraft" WHERE "Id"=NEW.source_draft_id;
          SELECT * INTO b FROM cp6_personal_form_v2.legacy_source_bindings WHERE tenant_id=NEW.tenant_id AND environment_id=NEW.environment_id AND legacy_version_id=NEW.source_version_id;
          IF c.id IS NULL OR d.id IS NULL OR s."Id" IS NULL OR b.legacy_version_id IS NULL OR NOT b.enabled
            OR c.operation<>'FORM_DRAFT_ADOPT_LEGACY' OR c.completed OR c.closed OR c.writer_xid<>pg_current_xact_id()
            OR (c.tenant_id,c.environment_id,c.actor_id,c.draft_id,c.resource_key,c.expected_version)
              IS DISTINCT FROM (NEW.tenant_id,NEW.environment_id,NEW.owner_id,d.id,s."Id"::text,NEW.source_draft_revision::text)
            OR (s."TenantId",s."OwnerUserId",s."FormDefVersionId",s."FormDefId",s."Status")
              IS DISTINCT FROM (NEW.tenant_id,NEW.owner_id,NEW.source_version_id,b.legacy_form_id,0)
            OR (d.tenant_id,d.environment_id,d.owner_id,d.head_id,d.form_version,d.data_json,d.title,d.status)
              IS DISTINCT FROM (NEW.tenant_id,NEW.environment_id,NEW.owner_id,b.head_id,b.form_version,s."DataJson",s."Title",0)
            OR b.revision<>NEW.binding_revision OR b.source_revision<>NEW.source_schema_revision
            OR NEW.binding_revision IS DISTINCT FROM (convert_from(c.raw_body,'UTF8')::jsonb->'body'->>'mappingVersion')::uuid
            OR NOT EXISTS(SELECT FROM cp6_personal_form_v2.policy p WHERE p.tenant_id=NEW.tenant_id AND p.environment_id=NEW.environment_id AND p.head_id=b.head_id
              AND p.row_version=NEW.policy_version AND (p.body::jsonb->>'enabled')::boolean AND (p.body::jsonb->>'allowRestore')::boolean
              AND p.body::jsonb->>'retentionPolicyRef'=NEW.policy_ref
              AND extract(epoch FROM NEW.adopt_until)-extract(epoch FROM b.bound_at)=(p.body::jsonb->>'retentionDays')::bigint*86400)
            OR NOT EXISTS(SELECT FROM cp6_personal_form_v2.legacy_source_revisions WHERE kind='DRAFT' AND id=s."Id" AND tenant_id=NEW.tenant_id AND revision=NEW.source_draft_revision)
            OR NOT EXISTS(SELECT FROM cp6_personal_form_v2.legacy_source_revisions WHERE kind='VERSION' AND id=NEW.source_version_id AND tenant_id=NEW.tenant_id AND revision=NEW.source_schema_revision)
            OR EXISTS(SELECT FROM cp6_personal_form_v2.history WHERE draft_id=d.id)
            THEN RAISE EXCEPTION 'LEGACY_DRAFT_ADOPTION_SOURCE_REQUIRED' USING ERRCODE='23514';END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER legacy_adoption_guard BEFORE INSERT ON cp6_personal_form_v2.legacy_adoptions
          FOR EACH ROW EXECUTE FUNCTION cp6_personal_form_v2.legacy_adoption_guard();
        CREATE FUNCTION cp6_personal_form_v2.legacy_adoption_complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE c cp6_personal_form_v2.commands;BEGIN
          SELECT * INTO c FROM cp6_personal_form_v2.commands WHERE id=NEW.id;
          IF c.operation='FORM_DRAFT_ADOPT_LEGACY' AND NOT c.closed AND (NOT c.completed OR NOT EXISTS(SELECT FROM cp6_personal_form_v2.legacy_adoptions WHERE command_id=c.id))
            THEN RAISE EXCEPTION 'LEGACY_DRAFT_ADOPTION_ORIGIN_REQUIRED' USING ERRCODE='23514';END IF;RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER legacy_adoption_complete AFTER INSERT OR UPDATE ON cp6_personal_form_v2.commands
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_personal_form_v2.legacy_adoption_complete();
        CREATE FUNCTION cp6_personal_form_v2.legacy_adoption_ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT NOT EXISTS(SELECT FROM (VALUES
            ('public."Wf_FormDraft"','legacy_source_root','cp6_authentication_v1.before_change()',62,false),
            ('public."Wf_FormDraft"','legacy_source_revision','cp6_personal_form_v2.legacy_source_changed()',29,false),
            ('public."Wf_FormDraft"','legacy_source_truncate','cp6_personal_form_v2.legacy_source_changed()',32,false),
            ('public."Wf_FormDefVersion"','legacy_source_root','cp6_authentication_v1.before_change()',62,false),
            ('public."Wf_FormDefVersion"','legacy_source_revision','cp6_personal_form_v2.legacy_source_changed()',29,false),
            ('public."Wf_FormDefVersion"','legacy_source_truncate','cp6_personal_form_v2.legacy_source_changed()',32,false),
            ('cp6_personal_form_v2.legacy_source_bindings','legacy_binding_root','cp6_authentication_v1.before_change()',62,false),
            ('cp6_personal_form_v2.legacy_source_bindings','legacy_binding_version','cp6_personal_form_v2.legacy_binding_version()',23,false),
            ('cp6_personal_form_v2.legacy_source_bindings','legacy_binding_no_delete','cp6_scope_authorization_v1.immutable()',42,false),
            ('cp6_personal_form_v2.legacy_adoptions','legacy_adoption_root','cp6_authentication_v1.before_change()',62,false),
            ('cp6_personal_form_v2.legacy_adoptions','legacy_adoption_immutable','cp6_scope_authorization_v1.immutable()',58,false),
            ('cp6_personal_form_v2.legacy_adoptions','legacy_adoption_guard','cp6_personal_form_v2.legacy_adoption_guard()',7,false),
            ('cp6_personal_form_v2.commands','legacy_adoption_complete','cp6_personal_form_v2.legacy_adoption_complete()',21,true)) a(tab,name,fn,typ,deferred)
            WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid=to_regclass(a.tab) AND t.tgname=a.name AND t.tgfoid=to_regprocedure(a.fn)
              AND t.tgtype=a.typ AND t.tgenabled IN('O','A') AND t.tgqual IS NULL AND t.tgnargs=0 AND t.tgdeferrable=a.deferred AND t.tginitdeferred=a.deferred));
        $$;
        REVOKE ALL ON cp6_personal_form_v2.legacy_source_revisions,cp6_personal_form_v2.legacy_source_bindings,cp6_personal_form_v2.legacy_adoptions FROM PUBLIC;
        REVOKE ALL ON FUNCTION cp6_personal_form_v2.legacy_source_changed(),cp6_personal_form_v2.legacy_binding_version(),cp6_personal_form_v2.legacy_adoption_guard(),
          cp6_personal_form_v2.legacy_adoption_complete(),cp6_personal_form_v2.legacy_adoption_ready() FROM PUBLIC;
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",'form-legacy-source','设置旧FORM当前披露与采用映射',0,t."Id",LOCALTIMESTAMP
        FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t WHERE m."MenuKey"='oa-designer'
          AND NOT EXISTS(SELECT FROM public."Sys_MenuAction" a WHERE a."TenantId"=t."Id" AND a."MenuId"=m."MenuId" AND a."ActionCode"='form-legacy-source');
        """);
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Legacy adoption origin and current mapping history require forward recovery.");
}
