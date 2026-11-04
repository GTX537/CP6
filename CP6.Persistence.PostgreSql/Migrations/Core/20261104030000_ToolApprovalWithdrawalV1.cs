using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)),Migration("20261104030000_ToolApprovalWithdrawalV1")]
public sealed class ToolApprovalWithdrawalV1:Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        CREATE SCHEMA cp6_tool_approval_control_v1;
        REVOKE ALL ON SCHEMA cp6_tool_approval_control_v1 FROM PUBLIC;
        CREATE SEQUENCE cp6_tool_approval_control_v1.row_versions;
        CREATE TABLE cp6_tool_approval_control_v1.heads(
          environment_id uuid NOT NULL,tenant_id uuid NOT NULL,tool_id uuid NOT NULL,submission_id uuid NOT NULL,
          state text NOT NULL CHECK(state IN('IN_REVIEW','WITHDRAWN')),
          origin text NOT NULL CHECK(origin IN('SUBMISSION','EXISTING_SUBMISSION_ADOPTION')),
          row_version bytea NOT NULL DEFAULT int8send(nextval('cp6_tool_approval_control_v1.row_versions')) CHECK(octet_length(row_version)=8),
          PRIMARY KEY(environment_id,tenant_id,submission_id),
          FOREIGN KEY(environment_id,tenant_id,submission_id) REFERENCES cp6_tool_approval_v1.submissions(environment_id,tenant_id,id));
        -- Existing submissions retain their actual application receipts. Current native Tool
        -- application has no APPLIED writer; adopting a control never invents physical disposal.
        INSERT INTO cp6_tool_approval_control_v1.heads(environment_id,tenant_id,tool_id,submission_id,state,origin)
        SELECT environment_id,tenant_id,tool_id,id,'IN_REVIEW','EXISTING_SUBMISSION_ADOPTION' FROM cp6_tool_approval_v1.submissions;
        CREATE TABLE cp6_tool_approval_control_v1.withdrawals(
          environment_id uuid NOT NULL,tenant_id uuid NOT NULL,tool_id uuid NOT NULL,submission_id uuid NOT NULL,id uuid NOT NULL,
          operation_id uuid NOT NULL,command_id uuid NOT NULL,outbox_id uuid NOT NULL,actor_id uuid NOT NULL,previous_command_id uuid,
          before_version bytea NOT NULL CHECK(octet_length(before_version)=8),after_version bytea NOT NULL CHECK(octet_length(after_version)=8 AND after_version<>before_version),
          payload bytea NOT NULL,digest text NOT NULL CHECK(digest=encode(sha256(payload),'hex')),authority_json text NOT NULL,
          journal text NOT NULL DEFAULT 'Main.Wms.ToolWithdrawal.operations.v1' CHECK(journal='Main.Wms.ToolWithdrawal.operations.v1'),
          PRIMARY KEY(environment_id,tenant_id,id),UNIQUE(environment_id,tenant_id,operation_id),UNIQUE(environment_id,tenant_id,command_id),
          UNIQUE(environment_id,tenant_id,outbox_id),UNIQUE(environment_id,tenant_id,command_id,outbox_id),UNIQUE(environment_id,tenant_id,previous_command_id),
          FOREIGN KEY(environment_id,tenant_id,submission_id) REFERENCES cp6_tool_approval_control_v1.heads(environment_id,tenant_id,submission_id),
          FOREIGN KEY(environment_id,tenant_id,previous_command_id) REFERENCES cp6_tool_approval_control_v1.withdrawals(environment_id,tenant_id,command_id),
          FOREIGN KEY(tenant_id,journal,operation_id) REFERENCES cp6_main_owner_v1.operations(tenant_id,owner,operation_id) DEFERRABLE INITIALLY DEFERRED);
        CREATE UNIQUE INDEX tool_withdrawal_first ON cp6_tool_approval_control_v1.withdrawals(environment_id,tenant_id,submission_id) WHERE previous_command_id IS NULL;
        CREATE TABLE cp6_tool_approval_control_v1.withdraw_outbox(
          environment_id uuid NOT NULL,tenant_id uuid NOT NULL,id uuid NOT NULL,command_id uuid NOT NULL,submission_id uuid NOT NULL,
          payload bytea NOT NULL,digest text NOT NULL CHECK(digest=encode(sha256(payload),'hex')),
          PRIMARY KEY(environment_id,tenant_id,id),UNIQUE(environment_id,tenant_id,command_id),
          FOREIGN KEY(environment_id,tenant_id,command_id,id) REFERENCES cp6_tool_approval_control_v1.withdrawals(environment_id,tenant_id,command_id,outbox_id) DEFERRABLE INITIALLY DEFERRED);
        ALTER TABLE cp6_tool_approval_control_v1.withdrawals ADD CONSTRAINT tool_withdrawal_outbox_required
          FOREIGN KEY(environment_id,tenant_id,outbox_id) REFERENCES cp6_tool_approval_control_v1.withdraw_outbox(environment_id,tenant_id,id) DEFERRABLE INITIALLY DEFERRED;
        CREATE FUNCTION cp6_tool_approval_control_v1.advance() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$ BEGIN
          IF (NEW.environment_id,NEW.tenant_id,NEW.tool_id,NEW.submission_id,NEW.origin) IS DISTINCT FROM
             (OLD.environment_id,OLD.tenant_id,OLD.tool_id,OLD.submission_id,OLD.origin)
             OR NEW.state='IN_REVIEW' OR OLD.state='WITHDRAWN' AND NEW.state<>'WITHDRAWN'
             THEN RAISE EXCEPTION 'TOOL_APPROVAL_CONTROL_TRANSITION_INVALID';END IF;
          NEW.row_version=int8send(nextval('cp6_tool_approval_control_v1.row_versions'));RETURN NEW;
        END $$;
        CREATE FUNCTION cp6_tool_approval_control_v1.complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE h cp6_tool_approval_control_v1.heads;w cp6_tool_approval_control_v1.withdrawals;prior cp6_tool_approval_control_v1.withdrawals;BEGIN
          SELECT * INTO h FROM cp6_tool_approval_control_v1.heads WHERE environment_id=NEW.environment_id AND tenant_id=NEW.tenant_id AND submission_id=NEW.submission_id;
          IF h.submission_id IS NULL OR NOT EXISTS(SELECT FROM cp6_tool_approval_v1.submissions s WHERE
            (s.environment_id,s.tenant_id,s.id,s.tool_id)=(h.environment_id,h.tenant_id,h.submission_id,h.tool_id))
            THEN RAISE EXCEPTION 'TOOL_CONTROL_SUBMISSION_REQUIRED';END IF;
          IF h.state='WITHDRAWN' AND NOT EXISTS(SELECT FROM cp6_tool_approval_control_v1.withdrawals x WHERE
            (x.environment_id,x.tenant_id,x.submission_id,x.after_version)=(h.environment_id,h.tenant_id,h.submission_id,h.row_version))
            THEN RAISE EXCEPTION 'TOOL_CONTROL_WITHDRAWAL_REQUIRED';END IF;
          IF TG_TABLE_NAME IN('withdrawals','withdraw_outbox') THEN
            SELECT * INTO w FROM cp6_tool_approval_control_v1.withdrawals WHERE environment_id=NEW.environment_id AND tenant_id=NEW.tenant_id AND command_id=NEW.command_id;
            IF w.id IS NULL OR w.tool_id<>h.tool_id OR h.state<>'WITHDRAWN'
              OR NOT EXISTS(SELECT FROM cp6_tool_approval_control_v1.withdraw_outbox o WHERE
                (o.environment_id,o.tenant_id,o.command_id,o.id,o.submission_id,o.payload,o.digest)=(w.environment_id,w.tenant_id,w.command_id,w.outbox_id,w.submission_id,w.payload,w.digest))
              OR NOT EXISTS(SELECT FROM cp6_main_owner_v1.operations o WHERE o.tenant_id=w.tenant_id AND o.owner=w.journal AND o.operation_id=w.operation_id
                AND o.actor_id=w.actor_id AND o.contract='Main.Wms.ToolWithdrawal.commands.v1' AND o.state='COMMITTED' AND o.object_ids=ARRAY[w.tool_id]
                AND convert_from(o.result_bytes,'UTF8')::jsonb->>'State'='WITHDRAWN' AND convert_from(o.result_bytes,'UTF8')::jsonb->>'TransportCommandId'=w.command_id::text)
              THEN RAISE EXCEPTION 'TOOL_WITHDRAWAL_ATOMIC_FACT_REQUIRED';END IF;
            IF w.previous_command_id IS NOT NULL THEN
              SELECT * INTO prior FROM cp6_tool_approval_control_v1.withdrawals WHERE environment_id=w.environment_id AND tenant_id=w.tenant_id AND command_id=w.previous_command_id;
              IF (prior.tool_id,prior.submission_id,prior.after_version) IS DISTINCT FROM (w.tool_id,w.submission_id,w.before_version)
                OR NOT EXISTS(SELECT FROM cp6_approval_withdraw_v2.commands c WHERE
                  (c.environment_id,c.tenant_id,c.id,c.submission_id)=(w.environment_id,w.tenant_id,w.previous_command_id,w.submission_id)
                  AND c.owner='Main.Wms.ToolDisposition.v1' AND c.state='BLOCKED')
                THEN RAISE EXCEPTION 'TOOL_BLOCKED_WITHDRAWAL_REQUIRED';END IF;
            END IF;
          END IF;RETURN NULL;
        END $$;
        CREATE TRIGGER tool_control_root BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_tool_approval_control_v1.heads FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER tool_control_advance BEFORE UPDATE ON cp6_tool_approval_control_v1.heads FOR EACH ROW EXECUTE FUNCTION cp6_tool_approval_control_v1.advance();
        CREATE TRIGGER tool_control_no_delete BEFORE DELETE OR TRUNCATE ON cp6_tool_approval_control_v1.heads FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE CONSTRAINT TRIGGER tool_control_complete AFTER INSERT OR UPDATE ON cp6_tool_approval_control_v1.heads DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_tool_approval_control_v1.complete();
        CREATE FUNCTION cp6_tool_approval_control_v1.submission_complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$ BEGIN
          IF NOT EXISTS(SELECT FROM cp6_tool_approval_control_v1.heads h WHERE (h.environment_id,h.tenant_id,h.submission_id,h.tool_id)=(NEW.environment_id,NEW.tenant_id,NEW.id,NEW.tool_id))
            THEN RAISE EXCEPTION 'TOOL_SUBMISSION_CONTROL_REQUIRED';END IF;RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER tool_submission_control AFTER INSERT ON cp6_tool_approval_v1.submissions DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_tool_approval_control_v1.submission_complete();
        DO $$ DECLARE tbl text;BEGIN FOREACH tbl IN ARRAY ARRAY['withdrawals','withdraw_outbox'] LOOP
          EXECUTE format('CREATE TRIGGER tool_withdraw_root BEFORE INSERT ON cp6_tool_approval_control_v1.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change()',tbl);
          EXECUTE format('CREATE TRIGGER tool_withdraw_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_tool_approval_control_v1.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable()',tbl);
          EXECUTE format('CREATE CONSTRAINT TRIGGER tool_withdraw_complete AFTER INSERT ON cp6_tool_approval_control_v1.%I DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_tool_approval_control_v1.complete()',tbl);
        END LOOP;END $$;
        CREATE FUNCTION cp6_tool_approval_control_v1.ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT NOT EXISTS(SELECT FROM (VALUES
            ('cp6_tool_approval_control_v1.heads','tool_control_root','cp6_authentication_v1.before_change()',62,false),
            ('cp6_tool_approval_control_v1.heads','tool_control_advance','cp6_tool_approval_control_v1.advance()',19,false),
            ('cp6_tool_approval_control_v1.heads','tool_control_no_delete','cp6_scope_authorization_v1.immutable()',42,false),
            ('cp6_tool_approval_control_v1.heads','tool_control_complete','cp6_tool_approval_control_v1.complete()',21,true),
            ('cp6_tool_approval_v1.submissions','tool_submission_control','cp6_tool_approval_control_v1.submission_complete()',5,true)) a(tbl,name,fn,typ,deferred)
            WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid=to_regclass(a.tbl) AND t.tgname=a.name AND t.tgfoid=to_regprocedure(a.fn)
              AND t.tgtype=a.typ AND t.tgenabled IN('O','A') AND t.tgqual IS NULL AND t.tgdeferrable=a.deferred AND t.tginitdeferred=a.deferred))
            AND NOT EXISTS(SELECT FROM (VALUES('withdrawals'),('withdraw_outbox')) a(tbl) CROSS JOIN (VALUES
              ('tool_withdraw_root','cp6_authentication_v1.before_change()',6,false),('tool_withdraw_immutable','cp6_scope_authorization_v1.immutable()',58,false),
              ('tool_withdraw_complete','cp6_tool_approval_control_v1.complete()',5,true)) b(name,fn,typ,deferred)
              WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid=to_regclass('cp6_tool_approval_control_v1.'||a.tbl) AND t.tgname=b.name AND t.tgfoid=to_regprocedure(b.fn)
                AND t.tgtype=b.typ AND t.tgenabled IN('O','A') AND t.tgqual IS NULL AND t.tgdeferrable=b.deferred AND t.tginitdeferred=b.deferred));
        $$;
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_tool_approval_control_v1 FROM PUBLIC;
        REVOKE ALL ON ALL SEQUENCES IN SCHEMA cp6_tool_approval_control_v1 FROM PUBLIC;
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",'dispose-withdraw','撤回版模处置审批',16,t."Id",LOCALTIMESTAMP FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t
        WHERE m."MenuKey"='wms-plate-mold' AND NOT EXISTS(SELECT FROM public."Sys_MenuAction" x WHERE x."TenantId"=t."Id" AND x."MenuId"=m."MenuId" AND x."ActionCode"='dispose-withdraw');
        """);
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Tool control history is forward-only.");
}
