using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)),Migration("20261104052000_FormDispositionRebaseV1")]
public sealed class FormDispositionRebaseV1:Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        CREATE TABLE cp6_form_disposition_v1.consumptions(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,request_id uuid NOT NULL,receipt_version uuid NOT NULL,
          command_ref uuid NOT NULL UNIQUE REFERENCES cp6_personal_form_v2.commands(id) DEFERRABLE INITIALLY DEFERRED,
          command_id text NOT NULL,actor_id uuid NOT NULL,processor_id uuid NOT NULL,draft_version uuid NOT NULL,
          recorded_at timestamptz NOT NULL DEFAULT clock_timestamp(),
          PRIMARY KEY(tenant_id,environment_id,request_id),
          FOREIGN KEY(tenant_id,environment_id,request_id,receipt_version) REFERENCES cp6_form_disposition_v1.events(tenant_id,environment_id,request_id,version));
        CREATE FUNCTION cp6_form_disposition_v1.consume_complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE c cp6_personal_form_v2.commands;e cp6_form_disposition_v1.events;s jsonb;h cp6_personal_form_v2.history;b jsonb;a jsonb;original jsonb;ref jsonb;BEGIN
          SELECT * INTO c FROM cp6_personal_form_v2.commands WHERE id=NEW.command_ref;
          SELECT * INTO e FROM cp6_form_disposition_v1.events WHERE tenant_id=NEW.tenant_id AND environment_id=NEW.environment_id AND request_id=NEW.request_id ORDER BY sequence DESC LIMIT 1;
          SELECT scope::jsonb INTO s FROM cp6_form_disposition_v1.requests WHERE tenant_id=NEW.tenant_id AND environment_id=NEW.environment_id AND id=NEW.request_id;
          SELECT * INTO h FROM cp6_personal_form_v2.history WHERE command_id=NEW.command_ref;
          SELECT after_json::jsonb INTO original FROM cp6_personal_form_v2.history WHERE id=(s->>'historyId')::uuid;
          a=h.after_json::jsonb;b=h.before_json::jsonb;ref=convert_from(c.raw_body,'UTF8')::jsonb->'body'->'dispositionRef';
          IF c.id IS NULL OR NOT c.completed OR c.closed OR c.operation<>'FORM_DRAFT_REBASE'
            OR c.writer_xid IS DISTINCT FROM pg_current_xact_id() OR c.tenant_id IS DISTINCT FROM NEW.tenant_id
            OR c.environment_id IS DISTINCT FROM NEW.environment_id OR c.actor_id IS DISTINCT FROM NEW.actor_id
            OR c.command_id IS DISTINCT FROM NEW.command_id OR c.draft_id IS DISTINCT FROM (s->>'draftId')::uuid
            OR c.expected_version IS DISTINCT FROM s->>'draftVersion' OR NEW.actor_id IS DISTINCT FROM (s->>'requesterId')::uuid
            OR e.version IS DISTINCT FROM NEW.receipt_version OR e.state IS DISTINCT FROM 'RETAINED'
            OR e.actor_id IS DISTINCT FROM NEW.processor_id OR e.retain_until<=clock_timestamp()
            OR (ref->>'requestId')::uuid IS DISTINCT FROM NEW.request_id OR (ref->>'receiptVersion')::uuid IS DISTINCT FROM NEW.receipt_version
            THEN RAISE EXCEPTION 'FORM_DISPOSITION_CONSUMPTION_MISMATCH' USING ERRCODE='23514';END IF;
          IF h.id IS NULL OR h.draft_id IS DISTINCT FROM c.draft_id OR b IS DISTINCT FROM original
            OR (a->>'tenant_id')::uuid IS DISTINCT FROM NEW.tenant_id OR (a->>'environment_id')::uuid IS DISTINCT FROM NEW.environment_id
            OR (a->>'owner_id')::uuid IS DISTINCT FROM NEW.actor_id OR (a->>'row_version')::uuid IS DISTINCT FROM NEW.draft_version
            OR (a->>'form_version')::uuid IS DISTINCT FROM (s->>'targetVersionId')::uuid
            OR (a->>'rebased_from')::uuid IS DISTINCT FROM (s->>'sourceVersionId')::uuid
            THEN RAISE EXCEPTION 'FORM_DISPOSITION_NATIVE_HISTORY_REQUIRED' USING ERRCODE='23514';END IF;
          IF (SELECT active FROM cp6_form_disposition_v1.holds WHERE tenant_id=NEW.tenant_id AND environment_id=NEW.environment_id AND draft_id=c.draft_id ORDER BY sequence DESC LIMIT 1) IS TRUE
            THEN RAISE EXCEPTION 'FORM_DISPOSITION_ON_HOLD' USING ERRCODE='23514';END IF;
          RETURN NULL;
        END $$;
        CREATE FUNCTION cp6_form_disposition_v1.unconsumed() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF EXISTS(SELECT FROM cp6_form_disposition_v1.consumptions WHERE tenant_id=NEW.tenant_id AND environment_id=NEW.environment_id AND request_id=NEW.request_id)
            THEN RAISE EXCEPTION 'FORM_DISPOSITION_ALREADY_CONSUMED' USING ERRCODE='23514';END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER disposition_unconsumed BEFORE INSERT ON cp6_form_disposition_v1.events FOR EACH ROW EXECUTE FUNCTION cp6_form_disposition_v1.unconsumed();
        CREATE TRIGGER disposition_root BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_form_disposition_v1.consumptions FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER disposition_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_form_disposition_v1.consumptions FOR EACH STATEMENT EXECUTE FUNCTION cp6_main_owner_v1.immutable();
        CREATE CONSTRAINT TRIGGER disposition_consume_complete AFTER INSERT ON cp6_form_disposition_v1.consumptions DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_form_disposition_v1.consume_complete();
        ALTER FUNCTION cp6_form_disposition_v1.ready() RENAME TO retained_ready;
        CREATE FUNCTION cp6_form_disposition_v1.ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT cp6_form_disposition_v1.retained_ready() AND NOT EXISTS(SELECT FROM(VALUES
            ('consumptions','disposition_root','cp6_authentication_v1.before_change()'),
            ('consumptions','disposition_immutable','cp6_main_owner_v1.immutable()'),
            ('consumptions','disposition_consume_complete','cp6_form_disposition_v1.consume_complete()'),
            ('events','disposition_unconsumed','cp6_form_disposition_v1.unconsumed()'))v(tab,trig,fn)
            WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid=('cp6_form_disposition_v1.'||v.tab)::regclass
              AND t.tgname=v.trig AND t.tgenabled='O' AND t.tgfoid=v.fn::regprocedure));
        $$;
        REVOKE ALL ON cp6_form_disposition_v1.consumptions FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_form_disposition_v1 FROM PUBLIC;
        """);
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Native disposition consumption requires forward recovery.");
}
