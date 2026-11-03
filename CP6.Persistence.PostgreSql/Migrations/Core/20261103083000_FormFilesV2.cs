using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)),Migration("20261103083000_FormFilesV2")]
public sealed class FormFilesV2:Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        CREATE SCHEMA cp6_form_files_v2;
        REVOKE ALL ON SCHEMA cp6_form_files_v2 FROM PUBLIC;
        CREATE TABLE cp6_form_files_v2.contents(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,file_id uuid NOT NULL CHECK(file_id<>'00000000-0000-0000-0000-000000000000'),
          creator_id uuid NOT NULL,head_id uuid NOT NULL,form_version uuid NOT NULL,field_name text COLLATE "C" NOT NULL,
          scope_kind text NOT NULL CHECK(scope_kind IN('FORM','DRAFT','RETURNED')),scope_id uuid,
          file_name text NOT NULL CHECK(length(file_name) BETWEEN 1 AND 255),digest text NOT NULL CHECK(digest~'^[0-9a-f]{64}$'),
          byte_length bigint NOT NULL CHECK(byte_length BETWEEN 1 AND 20971520),media_type text NOT NULL,store_path text NOT NULL UNIQUE,operation_id uuid NOT NULL,
          PRIMARY KEY(tenant_id,environment_id,file_id),CHECK((scope_kind='FORM')=(scope_id IS NULL)),
          FOREIGN KEY(tenant_id,environment_id,head_id,form_version) REFERENCES cp6_form_authoring_v2.versions(tenant_id,environment_id,head_id,id));
        CREATE TABLE cp6_form_files_v2.events(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,file_id uuid NOT NULL,revision bigint NOT NULL CHECK(revision>0),
          operation_id uuid NOT NULL,actor_id uuid NOT NULL,inspection text NOT NULL,held boolean NOT NULL,action text NOT NULL,
          recorded_at timestamptz NOT NULL DEFAULT clock_timestamp(),PRIMARY KEY(tenant_id,environment_id,file_id,revision),
          UNIQUE(tenant_id,environment_id,operation_id),FOREIGN KEY(tenant_id,environment_id,file_id) REFERENCES cp6_form_files_v2.contents);
        CREATE TABLE cp6_form_files_v2.claims(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,file_id uuid NOT NULL,target_kind text NOT NULL CHECK(target_kind IN('DRAFT','SUBMISSION')),
          target_id uuid NOT NULL,command_id uuid NOT NULL REFERENCES cp6_personal_form_v2.commands(id),
          PRIMARY KEY(tenant_id,environment_id,file_id),FOREIGN KEY(tenant_id,environment_id,file_id) REFERENCES cp6_form_files_v2.contents);
        CREATE TABLE cp6_form_files_v2.bindings(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,file_id uuid NOT NULL,field_name text COLLATE "C" NOT NULL,
          target_kind text NOT NULL CHECK(target_kind IN('DRAFT','SUBMISSION')),target_id uuid NOT NULL,form_version uuid NOT NULL,target_version text NOT NULL,
          command_id uuid NOT NULL REFERENCES cp6_personal_form_v2.commands(id),PRIMARY KEY(command_id,file_id,field_name),
          FOREIGN KEY(tenant_id,environment_id,file_id) REFERENCES cp6_form_files_v2.claims);
        CREATE INDEX form_file_target ON cp6_form_files_v2.bindings(tenant_id,environment_id,target_kind,target_id,field_name,file_id);
        CREATE FUNCTION cp6_form_files_v2.event_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE p cp6_form_files_v2.events;b jsonb=NEW.inspection::jsonb;BEGIN
          SELECT * INTO p FROM cp6_form_files_v2.events WHERE tenant_id=NEW.tenant_id AND environment_id=NEW.environment_id AND file_id=NEW.file_id ORDER BY revision DESC LIMIT 1;
          IF NEW.revision<>COALESCE(p.revision,0)+1 OR b->>'Status' IS NULL OR b->>'Status' NOT IN('CLEAN','PENDING','NOT_CLEAN')
            OR COALESCE(b->>'Provider','')='' OR b->>'Status'='CLEAN' AND (COALESCE(b->>'EngineVersion','')='' OR COALESCE(b->>'SignatureVersion','')='' OR COALESCE(b->>'EvidenceDigest','')!~'^[0-9a-f]{64}$')
            OR p.revision IS NULL AND (NEW.action<>'UPLOAD' OR NEW.held)
            OR p.revision IS NOT NULL AND (NEW.action NOT IN('HOLD','RELEASE','INSPECT','INTEGRITY_FAILURE')
              OR NEW.action IN('HOLD','RELEASE') AND NEW.inspection<>p.inspection
              OR NEW.action='HOLD' AND NOT NEW.held OR NEW.action='RELEASE' AND NEW.held
              OR NEW.action IN('INSPECT','INTEGRITY_FAILURE') AND NEW.held<>p.held
              OR NEW.action='INTEGRITY_FAILURE' AND b->>'Status'<>'NOT_CLEAN')
            THEN RAISE EXCEPTION 'FORM_FILE_STATE_INVALID' USING ERRCODE='23514';END IF;RETURN NEW;
        END $$;
        CREATE FUNCTION cp6_form_files_v2.receipt_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE f cp6_form_files_v2.contents;o cp6_main_owner_v1.operations;BEGIN
          SELECT * INTO f FROM cp6_form_files_v2.contents WHERE tenant_id=NEW.tenant_id AND environment_id=NEW.environment_id AND file_id=NEW.file_id;
          SELECT * INTO o FROM cp6_main_owner_v1.operations WHERE tenant_id=NEW.tenant_id AND owner='Main.File.FormContent.v2/'||NEW.environment_id::text AND operation_id=NEW.operation_id;
          IF o.operation_id IS NULL OR o.state<>'COMMITTED' OR o.contract<>'Main.File.FormContent.commands.v2' OR o.actor_id<>NEW.actor_id
            OR o.object_ids<>ARRAY[NEW.file_id] OR NEW.revision=1 AND (f.operation_id<>NEW.operation_id OR f.creator_id<>NEW.actor_id)
            THEN RAISE EXCEPTION 'FORM_FILE_RECEIPT_REQUIRED' USING ERRCODE='23514';END IF;RETURN NULL;
        END $$;
        CREATE FUNCTION cp6_form_files_v2.content_complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF NOT EXISTS(SELECT FROM cp6_form_files_v2.events WHERE tenant_id=NEW.tenant_id AND environment_id=NEW.environment_id AND file_id=NEW.file_id AND revision=1 AND operation_id=NEW.operation_id)
            THEN RAISE EXCEPTION 'FORM_FILE_INITIAL_INSPECTION_REQUIRED' USING ERRCODE='23514';END IF;RETURN NULL;
        END $$;
        CREATE FUNCTION cp6_form_files_v2.binding_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE c cp6_personal_form_v2.commands;f cp6_form_files_v2.contents;k cp6_form_files_v2.claims;BEGIN
          SELECT * INTO c FROM cp6_personal_form_v2.commands WHERE id=NEW.command_id AND writer_xid=pg_current_xact_id() AND NOT completed AND NOT closed;
          SELECT * INTO f FROM cp6_form_files_v2.contents WHERE tenant_id=NEW.tenant_id AND environment_id=NEW.environment_id AND file_id=NEW.file_id;
          IF c.id IS NULL OR (c.tenant_id,c.environment_id) IS DISTINCT FROM (NEW.tenant_id,NEW.environment_id)
            OR NEW.target_id IS DISTINCT FROM (CASE WHEN NEW.target_kind='DRAFT' THEN c.draft_id ELSE c.form_data_id END)
            THEN RAISE EXCEPTION 'FORM_FILE_BUSINESS_TRANSACTION_REQUIRED' USING ERRCODE='23514';END IF;
          IF TG_TABLE_NAME='claims' THEN
            IF f.creator_id<>c.actor_id OR f.scope_kind='DRAFT' AND (NEW.target_kind<>'DRAFT' OR f.scope_id<>NEW.target_id)
              OR f.scope_kind='RETURNED' AND (NEW.target_kind<>'SUBMISSION' OR f.scope_id<>NEW.target_id)
              THEN RAISE EXCEPTION 'FORM_FILE_CLAIM_SCOPE_INVALID' USING ERRCODE='23514';END IF;
          ELSE
            SELECT * INTO k FROM cp6_form_files_v2.claims WHERE tenant_id=NEW.tenant_id AND environment_id=NEW.environment_id AND file_id=NEW.file_id;
            IF f.field_name<>NEW.field_name OR NOT((k.target_kind,k.target_id)=(NEW.target_kind,NEW.target_id)
              OR k.target_kind='DRAFT' AND NEW.target_kind='SUBMISSION' AND EXISTS(SELECT FROM cp6_personal_form_v2.submissions s WHERE s.form_data_id=NEW.target_id AND s.draft_id=k.target_id AND s.tenant_id=NEW.tenant_id AND s.environment_id=NEW.environment_id))
              THEN RAISE EXCEPTION 'FORM_FILE_BINDING_SCOPE_INVALID' USING ERRCODE='23514';END IF;
          END IF;RETURN NEW;
        END $$;
        CREATE FUNCTION cp6_form_files_v2.references(data jsonb,schema_value jsonb) RETURNS TABLE(field_name text,reference text) LANGUAGE plpgsql IMMUTABLE SET search_path=pg_catalog AS $$
        DECLARE fields jsonb;f jsonb;kind text;v jsonb;item jsonb;BEGIN
          SELECT value INTO fields FROM jsonb_each(schema_value) WHERE lower(key)='fields';
          FOR f IN SELECT value FROM jsonb_array_elements(fields) LOOP
            SELECT value#>>'{}' INTO kind FROM jsonb_each(f) WHERE lower(key)='type';
            IF lower(kind)='upload' THEN
              SELECT value#>>'{}' INTO field_name FROM jsonb_each(f) WHERE lower(key)='name';v=data->field_name;
              IF v IS NULL OR v='null'::jsonb OR v='""'::jsonb THEN CONTINUE;END IF;
              FOR item IN SELECT value FROM jsonb_array_elements(CASE WHEN jsonb_typeof(v)='array' THEN v ELSE jsonb_build_array(v) END) LOOP
                reference=item#>>'{}';RETURN NEXT;
              END LOOP;
            END IF;
          END LOOP;
        END $$;
        CREATE FUNCTION cp6_form_files_v2.binding_complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE c cp6_personal_form_v2.commands;d jsonb;schema_value jsonb;version_id uuid;target uuid;kind text;token text;r record;BEGIN
          SELECT * INTO c FROM cp6_personal_form_v2.commands WHERE id=NEW.id;
          IF c.closed OR c.operation NOT IN('FORM_DRAFT_CREATE','FORM_DRAFT_SAVE','FORM_DRAFT_REBASE','FORM_DIRECT_SUBMIT','FORM_DRAFT_SUBMIT','SFS_RETURNED_EDIT','SFS_RETURNED_RESUBMIT') THEN RETURN NULL;END IF;
          IF c.operation IN('FORM_DRAFT_CREATE','FORM_DRAFT_SAVE','FORM_DRAFT_REBASE') THEN
            kind='DRAFT';target=c.draft_id;SELECT data_json::jsonb,form_version,row_version::text INTO d,version_id,token FROM cp6_personal_form_v2.drafts WHERE id=target;
          ELSE
            kind='SUBMISSION';target=c.form_data_id;SELECT "DataJson"::jsonb,"FormDefVersionId",encode("RowVersion",'base64') INTO d,version_id,token FROM public."Wf_FormData" WHERE "Id"=target;
          END IF;
          SELECT (snapshot_json::jsonb#>>'{fields,SchemaJson}')::jsonb INTO schema_value FROM cp6_form_authoring_v2.versions WHERE tenant_id=c.tenant_id AND environment_id=c.environment_id AND id=version_id;
          FOR r IN SELECT * FROM cp6_form_files_v2.references(d,schema_value) LOOP
            IF NOT EXISTS(SELECT FROM cp6_form_files_v2.bindings b JOIN cp6_form_files_v2.contents f USING(tenant_id,environment_id,file_id)
              WHERE b.command_id=c.id AND b.target_kind=kind AND b.target_id=target AND b.field_name=r.field_name AND b.form_version=version_id AND b.target_version=token
                AND r.reference='cp6-form-file-v2/'||f.file_id::text||'/1/'||f.digest)
              THEN RAISE EXCEPTION 'FORM_FILE_BINDING_REQUIRED' USING ERRCODE='23514';END IF;
          END LOOP;
          IF EXISTS(SELECT FROM cp6_form_files_v2.bindings b JOIN cp6_form_files_v2.contents f USING(tenant_id,environment_id,file_id)
            WHERE b.command_id=c.id AND NOT EXISTS(SELECT FROM cp6_form_files_v2.references(d,schema_value) expected_ref WHERE expected_ref.field_name=b.field_name AND expected_ref.reference='cp6-form-file-v2/'||f.file_id::text||'/1/'||f.digest))
            THEN RAISE EXCEPTION 'FORM_FILE_BINDING_EXTRA' USING ERRCODE='23514';END IF;RETURN NULL;
        END $$;
        DO $$ DECLARE tbl text;BEGIN
          FOREACH tbl IN ARRAY ARRAY['contents','events','claims','bindings'] LOOP
            EXECUTE format('CREATE TRIGGER form_file_root BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_form_files_v2.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change()',tbl);
            EXECUTE format('CREATE TRIGGER form_file_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_form_files_v2.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable()',tbl);
          END LOOP;
        END $$;
        CREATE TRIGGER form_file_event BEFORE INSERT ON cp6_form_files_v2.events FOR EACH ROW EXECUTE FUNCTION cp6_form_files_v2.event_guard();
        CREATE CONSTRAINT TRIGGER form_file_receipt AFTER INSERT ON cp6_form_files_v2.events DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_form_files_v2.receipt_guard();
        CREATE CONSTRAINT TRIGGER form_file_content_complete AFTER INSERT ON cp6_form_files_v2.contents DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_form_files_v2.content_complete();
        CREATE TRIGGER form_file_claim BEFORE INSERT ON cp6_form_files_v2.claims FOR EACH ROW EXECUTE FUNCTION cp6_form_files_v2.binding_guard();
        CREATE TRIGGER form_file_binding BEFORE INSERT ON cp6_form_files_v2.bindings FOR EACH ROW EXECUTE FUNCTION cp6_form_files_v2.binding_guard();
        CREATE CONSTRAINT TRIGGER form_file_complete AFTER INSERT OR UPDATE ON cp6_personal_form_v2.commands DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_form_files_v2.binding_complete();
        CREATE FUNCTION cp6_form_files_v2.ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT current_setting('session_replication_role')='origin' AND NOT EXISTS(SELECT FROM (
            SELECT 'cp6_form_files_v2.'||tbl AS tbl,'form_file_root' AS name,'cp6_authentication_v1.before_change()' AS fn,62 AS typ,false AS deferred FROM unnest(ARRAY['contents','events','claims','bindings']) tbl
            UNION ALL SELECT 'cp6_form_files_v2.'||tbl,'form_file_immutable','cp6_scope_authorization_v1.immutable()',58,false FROM unnest(ARRAY['contents','events','claims','bindings']) tbl
            UNION ALL SELECT * FROM (VALUES
              ('cp6_form_files_v2.events','form_file_event','cp6_form_files_v2.event_guard()',7,false),
              ('cp6_form_files_v2.events','form_file_receipt','cp6_form_files_v2.receipt_guard()',5,true),
              ('cp6_form_files_v2.contents','form_file_content_complete','cp6_form_files_v2.content_complete()',5,true),
              ('cp6_form_files_v2.claims','form_file_claim','cp6_form_files_v2.binding_guard()',7,false),
              ('cp6_form_files_v2.bindings','form_file_binding','cp6_form_files_v2.binding_guard()',7,false),
              ('cp6_personal_form_v2.commands','form_file_complete','cp6_form_files_v2.binding_complete()',21,true)) v
          ) a WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid=to_regclass(a.tbl) AND t.tgname=a.name AND t.tgfoid=to_regprocedure(a.fn) AND t.tgtype=a.typ
            AND t.tgenabled IN('O','A') AND t.tgqual IS NULL AND t.tgdeferrable=a.deferred AND t.tginitdeferred=a.deferred));
        $$;
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_form_files_v2 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_form_files_v2 FROM PUBLIC;
        """);
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Original file content, bindings and inspection history require forward recovery.");
}
