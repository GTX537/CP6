using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)),Migration("20261104041000_FormFieldDispositionV1")]
public sealed class FormFieldDispositionV1:Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        CREATE SCHEMA cp6_form_disposition_v1;
        REVOKE ALL ON SCHEMA cp6_form_disposition_v1 FROM PUBLIC;
        CREATE TABLE cp6_form_disposition_v1.requests(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,id uuid NOT NULL,draft_id uuid NOT NULL,
          history_id uuid NOT NULL REFERENCES cp6_personal_form_v2.history(id),scope text NOT NULL,
          PRIMARY KEY(tenant_id,environment_id,id),
          FOREIGN KEY(draft_id) REFERENCES cp6_personal_form_v2.drafts(id));
        CREATE TABLE cp6_form_disposition_v1.events(
          sequence bigint GENERATED ALWAYS AS IDENTITY UNIQUE,
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,request_id uuid NOT NULL,
          version uuid NOT NULL,previous_version uuid,state text NOT NULL,
          actor_id uuid NOT NULL,reason text NOT NULL CHECK(length(reason) BETWEEN 1 AND 500),
          recorded_at timestamptz NOT NULL,retain_until timestamptz,operation_id uuid NOT NULL,journal text NOT NULL,
          PRIMARY KEY(tenant_id,environment_id,request_id,version),
          UNIQUE NULLS NOT DISTINCT(tenant_id,environment_id,request_id,previous_version),
          FOREIGN KEY(tenant_id,environment_id,request_id) REFERENCES cp6_form_disposition_v1.requests(tenant_id,environment_id,id),
          FOREIGN KEY(tenant_id,environment_id,request_id,previous_version) REFERENCES cp6_form_disposition_v1.events(tenant_id,environment_id,request_id,version),
          FOREIGN KEY(tenant_id,journal,operation_id) REFERENCES cp6_main_owner_v1.operations(tenant_id,owner,operation_id) DEFERRABLE INITIALLY DEFERRED,
          CHECK(state IN('REQUESTED','APPROVED','REJECTED','RETAINED','REVOKED')),
          CHECK((previous_version IS NULL)=(state='REQUESTED')),
          CHECK(previous_version IS NOT NULL OR version=request_id),
          CHECK(state<>'RETAINED' OR retain_until>recorded_at),
          CHECK(journal='Main.Oa.FormFieldDisposition.v1/'||environment_id::text));
        ALTER TABLE cp6_form_disposition_v1.requests ADD CONSTRAINT disposition_first_event
          FOREIGN KEY(tenant_id,environment_id,id,id) REFERENCES cp6_form_disposition_v1.events(tenant_id,environment_id,request_id,version) DEFERRABLE INITIALLY DEFERRED;
        CREATE TABLE cp6_form_disposition_v1.holds(
          sequence bigint GENERATED ALWAYS AS IDENTITY UNIQUE,
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,draft_id uuid NOT NULL REFERENCES cp6_personal_form_v2.drafts(id),
          version uuid NOT NULL,previous_version uuid,active boolean NOT NULL,
          basis text NOT NULL CHECK(length(basis) BETWEEN 1 AND 200),reason text NOT NULL CHECK(length(reason) BETWEEN 1 AND 500),
          actor_id uuid NOT NULL,operation_id uuid NOT NULL,journal text NOT NULL,
          recorded_at timestamptz NOT NULL DEFAULT clock_timestamp(),
          PRIMARY KEY(tenant_id,environment_id,draft_id,version),
          UNIQUE NULLS NOT DISTINCT(tenant_id,environment_id,draft_id,previous_version),
          FOREIGN KEY(tenant_id,environment_id,draft_id,previous_version) REFERENCES cp6_form_disposition_v1.holds(tenant_id,environment_id,draft_id,version),
          FOREIGN KEY(tenant_id,journal,operation_id) REFERENCES cp6_main_owner_v1.operations(tenant_id,owner,operation_id) DEFERRABLE INITIALLY DEFERRED,
          CHECK(journal='Main.Oa.FormFieldDisposition.v1/'||environment_id::text));
        CREATE FUNCTION cp6_form_disposition_v1.source() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE h jsonb;s jsonb;BEGIN
          SELECT after_json::jsonb INTO h FROM cp6_personal_form_v2.history WHERE id=NEW.history_id AND draft_id=NEW.draft_id;
          s=NEW.scope::jsonb;
          IF h IS NULL OR (h->>'tenant_id')::uuid IS DISTINCT FROM NEW.tenant_id
            OR (h->>'environment_id')::uuid IS DISTINCT FROM NEW.environment_id
            OR (s->>'environmentId')::uuid IS DISTINCT FROM NEW.environment_id
            OR (s->>'tenantId')::uuid IS DISTINCT FROM NEW.tenant_id
            OR (s->>'draftId')::uuid IS DISTINCT FROM NEW.draft_id
            OR (s->>'historyId')::uuid IS DISTINCT FROM NEW.history_id
            OR (s->>'requesterId')::uuid IS DISTINCT FROM (h->>'owner_id')::uuid
            OR (s->>'draftVersion')::uuid IS DISTINCT FROM (h->>'row_version')::uuid
            OR (s->>'sourceVersionId')::uuid IS DISTINCT FROM (h->>'form_version')::uuid
            OR (s->>'headId')::uuid IS DISTINCT FROM (h->>'head_id')::uuid
            THEN RAISE EXCEPTION 'FORM_DISPOSITION_SOURCE_MISMATCH' USING ERRCODE='23514';END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER disposition_source BEFORE INSERT ON cp6_form_disposition_v1.requests FOR EACH ROW EXECUTE FUNCTION cp6_form_disposition_v1.source();
        CREATE FUNCTION cp6_form_disposition_v1.complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE o cp6_main_owner_v1.operations;r jsonb;p text;BEGIN
          SELECT * INTO o FROM cp6_main_owner_v1.operations WHERE tenant_id=NEW.tenant_id AND owner=NEW.journal AND operation_id=NEW.operation_id;
          r=convert_from(o.result_bytes,'UTF8')::jsonb->'Request';
          IF o.state IS DISTINCT FROM 'COMMITTED' OR o.actor_id IS DISTINCT FROM NEW.actor_id
            OR o.contract IS DISTINCT FROM 'Main.Oa.FormFieldDisposition.commands.v1'
            THEN RAISE EXCEPTION 'FORM_DISPOSITION_ORIGINAL_REQUIRED' USING ERRCODE='23514';END IF;
          IF TG_TABLE_NAME='events' THEN
            IF (r->>'RequestId')::uuid IS DISTINCT FROM NEW.request_id OR (r->>'Version')::uuid IS DISTINCT FROM NEW.version
              OR r->>'State' IS DISTINCT FROM NEW.state THEN RAISE EXCEPTION 'FORM_DISPOSITION_RESULT_MISMATCH' USING ERRCODE='23514';END IF;
            IF NEW.previous_version IS NOT NULL THEN
              SELECT state INTO p FROM cp6_form_disposition_v1.events WHERE tenant_id=NEW.tenant_id AND environment_id=NEW.environment_id AND request_id=NEW.request_id AND version=NEW.previous_version;
              IF NOT (p='REQUESTED' AND NEW.state IN('APPROVED','REJECTED','REVOKED') OR p='APPROVED' AND NEW.state IN('RETAINED','REVOKED') OR p='RETAINED' AND NEW.state='REVOKED')
                THEN RAISE EXCEPTION 'FORM_DISPOSITION_TRANSITION_INVALID' USING ERRCODE='23514';END IF;
            END IF;
          ELSE
            IF (r->>'DraftId')::uuid IS DISTINCT FROM NEW.draft_id OR (r->'Hold'->>'Version')::uuid IS DISTINCT FROM NEW.version
              OR (r->'Hold'->>'Active')::boolean IS DISTINCT FROM NEW.active THEN RAISE EXCEPTION 'FORM_DISPOSITION_HOLD_RESULT_MISMATCH' USING ERRCODE='23514';END IF;
          END IF;
          RETURN NULL;
        END $$;
        DO $$ DECLARE t text;BEGIN FOREACH t IN ARRAY ARRAY['requests','events','holds'] LOOP
          EXECUTE format('CREATE TRIGGER disposition_root BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_form_disposition_v1.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change()',t);
          EXECUTE format('CREATE TRIGGER disposition_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_form_disposition_v1.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_main_owner_v1.immutable()',t);
          IF t<>'requests' THEN EXECUTE format('CREATE CONSTRAINT TRIGGER disposition_complete AFTER INSERT ON cp6_form_disposition_v1.%I DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_form_disposition_v1.complete()',t);END IF;
        END LOOP;END $$;
        CREATE FUNCTION cp6_form_disposition_v1.ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT NOT EXISTS(SELECT FROM (VALUES
            ('requests','disposition_root','cp6_authentication_v1.before_change()'),
            ('events','disposition_root','cp6_authentication_v1.before_change()'),
            ('holds','disposition_root','cp6_authentication_v1.before_change()'),
            ('requests','disposition_immutable','cp6_main_owner_v1.immutable()'),
            ('events','disposition_immutable','cp6_main_owner_v1.immutable()'),
            ('holds','disposition_immutable','cp6_main_owner_v1.immutable()'),
            ('requests','disposition_source','cp6_form_disposition_v1.source()'),
            ('events','disposition_complete','cp6_form_disposition_v1.complete()'),
            ('holds','disposition_complete','cp6_form_disposition_v1.complete()')) v(tab,trig,fn)
            WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid=('cp6_form_disposition_v1.'||v.tab)::regclass
              AND t.tgname=v.trig AND t.tgenabled='O' AND t.tgfoid=v.fn::regprocedure));
        $$;
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_form_disposition_v1 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_form_disposition_v1 FROM PUBLIC;
        """);
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Disposition and retention originals require forward recovery.");
}
