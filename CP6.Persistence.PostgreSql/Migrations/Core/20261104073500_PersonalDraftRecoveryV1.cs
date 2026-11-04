using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)), Migration("20261104073500_PersonalDraftRecoveryV1")]
public sealed class PersonalDraftRecoveryV1:Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        CREATE TABLE cp6_personal_form_v2.draft_recoveries(
          draft_id uuid PRIMARY KEY REFERENCES cp6_personal_form_v2.drafts,
          source_draft_id uuid NOT NULL REFERENCES cp6_personal_form_v2.drafts,
          source_history_id uuid NOT NULL REFERENCES cp6_personal_form_v2.history,
          command_id uuid NOT NULL UNIQUE REFERENCES cp6_personal_form_v2.commands,
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,owner_id uuid NOT NULL,
          policy_version uuid NOT NULL,policy_ref text NOT NULL,recover_until timestamptz NOT NULL,
          CHECK(draft_id<>source_draft_id));
        CREATE FUNCTION cp6_personal_form_v2.recovery_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE c cp6_personal_form_v2.commands;prior cp6_personal_form_v2.commands;
          d cp6_personal_form_v2.drafts;s cp6_personal_form_v2.drafts;h cp6_personal_form_v2.history;b jsonb;
        BEGIN
          SELECT * INTO c FROM cp6_personal_form_v2.commands WHERE id=NEW.command_id;
          SELECT * INTO d FROM cp6_personal_form_v2.drafts WHERE id=NEW.draft_id;
          SELECT * INTO s FROM cp6_personal_form_v2.drafts WHERE id=NEW.source_draft_id;
          SELECT * INTO h FROM cp6_personal_form_v2.history WHERE id=NEW.source_history_id;
          SELECT * INTO prior FROM cp6_personal_form_v2.commands WHERE id=h.command_id;b=h.before_json::jsonb;
          IF c.id IS NULL OR d.id IS NULL OR s.id IS NULL OR h.id IS NULL OR b IS NULL
            OR c.operation<>'FORM_DRAFT_RECOVER_SNAPSHOT' OR c.completed OR c.closed OR c.writer_xid<>pg_current_xact_id()
            OR (c.tenant_id,c.environment_id,c.actor_id,c.draft_id,c.resource_key,c.expected_version)
              IS DISTINCT FROM (NEW.tenant_id,NEW.environment_id,NEW.owner_id,d.id,s.id::text,s.row_version::text)
            OR (d.tenant_id,d.environment_id,d.owner_id,d.head_id,d.form_version,d.data_json,d.title,d.status)
              IS DISTINCT FROM (s.tenant_id,s.environment_id,s.owner_id,s.head_id,(b->>'form_version')::uuid,b->>'data_json',b->>'title',0)
            OR (s.tenant_id,s.environment_id,s.owner_id) IS DISTINCT FROM (NEW.tenant_id,NEW.environment_id,NEW.owner_id)
            OR h.draft_id<>s.id OR NOT prior.completed OR prior.closed OR prior.operation<>'FORM_DRAFT_REBASE'
            OR NEW.source_history_id IS DISTINCT FROM (convert_from(c.raw_body,'UTF8')::jsonb->'body'->>'historyId')::uuid
            OR (b->>'id')::uuid<>s.id OR (b->>'owner_id')::uuid<>s.owner_id OR (b->>'head_id')::uuid<>s.head_id
            OR EXISTS(SELECT FROM cp6_personal_form_v2.history WHERE draft_id=d.id)
            THEN RAISE EXCEPTION 'FORM_SNAPSHOT_RECOVERY_SOURCE_REQUIRED' USING ERRCODE='23514';END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER personal_recovery_root BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_personal_form_v2.draft_recoveries
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER personal_recovery_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_personal_form_v2.draft_recoveries
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER personal_recovery_guard BEFORE INSERT ON cp6_personal_form_v2.draft_recoveries
          FOR EACH ROW EXECUTE FUNCTION cp6_personal_form_v2.recovery_guard();
        CREATE FUNCTION cp6_personal_form_v2.recovery_complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE c cp6_personal_form_v2.commands;BEGIN
          SELECT * INTO c FROM cp6_personal_form_v2.commands WHERE id=NEW.id;
          IF c.operation<>'FORM_DRAFT_RECOVER_SNAPSHOT' OR c.closed THEN RETURN NULL;END IF;
          IF NOT c.completed OR NOT EXISTS(SELECT FROM cp6_personal_form_v2.draft_recoveries WHERE command_id=c.id)
            THEN RAISE EXCEPTION 'FORM_SNAPSHOT_RECOVERY_ANCESTRY_REQUIRED' USING ERRCODE='23514';END IF;
          RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER personal_recovery_complete AFTER INSERT OR UPDATE ON cp6_personal_form_v2.commands
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_personal_form_v2.recovery_complete();
        CREATE FUNCTION cp6_personal_form_v2.recovery_ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT NOT EXISTS(SELECT FROM (VALUES
            ('draft_recoveries','personal_recovery_root','cp6_authentication_v1.before_change()',62,false),
            ('draft_recoveries','personal_recovery_immutable','cp6_scope_authorization_v1.immutable()',58,false),
            ('draft_recoveries','personal_recovery_guard','cp6_personal_form_v2.recovery_guard()',7,false),
            ('commands','personal_recovery_complete','cp6_personal_form_v2.recovery_complete()',21,true)) a(tab,name,fn,typ,deferred)
            WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid=to_regclass('cp6_personal_form_v2.'||a.tab)
              AND t.tgname=a.name AND t.tgfoid=to_regprocedure(a.fn) AND t.tgtype=a.typ
              AND t.tgenabled IN('O','A') AND t.tgqual IS NULL AND t.tgnargs=0 AND t.tgdeferrable=a.deferred AND t.tginitdeferred=a.deferred));
        $$;
        CREATE FUNCTION cp6_personal_form_v2.draft_lineage(t uuid,e uuid,descendant uuid,ancestor uuid) RETURNS boolean
          LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          WITH RECURSIVE chain(id) AS (
            SELECT descendant UNION ALL SELECT r.source_draft_id FROM cp6_personal_form_v2.draft_recoveries r
            JOIN chain c ON c.id=r.draft_id WHERE r.tenant_id=t AND r.environment_id=e)
          SELECT cp6_personal_form_v2.recovery_ready() AND EXISTS(SELECT FROM chain WHERE id=ancestor);
        $$;
        -- Existing file claims keep their original identity. Only the trusted recovery chain
        -- extends allowed bindings to the new draft and its eventual submission.
        CREATE OR REPLACE FUNCTION cp6_form_files_v2.binding_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
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
              OR k.target_kind='DRAFT' AND NEW.target_kind='DRAFT' AND cp6_personal_form_v2.draft_lineage(NEW.tenant_id,NEW.environment_id,NEW.target_id,k.target_id)
              OR k.target_kind='DRAFT' AND NEW.target_kind='SUBMISSION' AND EXISTS(SELECT FROM cp6_personal_form_v2.submissions s WHERE s.form_data_id=NEW.target_id AND (s.draft_id=k.target_id OR cp6_personal_form_v2.draft_lineage(NEW.tenant_id,NEW.environment_id,s.draft_id,k.target_id)) AND s.tenant_id=NEW.tenant_id AND s.environment_id=NEW.environment_id))
              THEN RAISE EXCEPTION 'FORM_FILE_BINDING_SCOPE_INVALID' USING ERRCODE='23514';END IF;
          END IF;RETURN NEW;
        END $$;
        REVOKE ALL ON cp6_personal_form_v2.draft_recoveries FROM PUBLIC;
        REVOKE ALL ON FUNCTION cp6_personal_form_v2.recovery_guard(),cp6_personal_form_v2.recovery_complete(),cp6_personal_form_v2.recovery_ready(),
          cp6_personal_form_v2.draft_lineage(uuid,uuid,uuid,uuid) FROM PUBLIC;
        """);
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Exact recovery ancestry and original file claims require forward recovery.");
}
