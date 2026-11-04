using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)),Migration("20261104040000_LocationRuleReviewWithdrawalV1")]
public sealed class LocationRuleReviewWithdrawalV1:Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        CREATE SCHEMA cp6_location_rule_review_control_v1;
        REVOKE ALL ON SCHEMA cp6_location_rule_review_control_v1 FROM PUBLIC;
        CREATE TABLE cp6_location_rule_review_control_v1.withdrawals(
          environment_id uuid NOT NULL,tenant_id uuid NOT NULL,location_rule_id uuid NOT NULL,submission_id uuid NOT NULL,id uuid NOT NULL,
          operation_id uuid NOT NULL,command_id uuid NOT NULL,outbox_id uuid NOT NULL,actor_id uuid NOT NULL,previous_command_id uuid,
          before_version bytea NOT NULL CHECK(octet_length(before_version)=8),after_version bytea NOT NULL CHECK(octet_length(after_version)=8 AND after_version<>before_version),
          payload bytea NOT NULL,digest text NOT NULL CHECK(digest=encode(sha256(payload),'hex')),authority_json text NOT NULL,
          journal text NOT NULL DEFAULT 'Main.Wms.LocationRuleWithdrawal.operations.v1' CHECK(journal='Main.Wms.LocationRuleWithdrawal.operations.v1'),
          PRIMARY KEY(environment_id,tenant_id,id),UNIQUE(environment_id,tenant_id,operation_id),UNIQUE(environment_id,tenant_id,command_id),
          UNIQUE(environment_id,tenant_id,outbox_id),UNIQUE(environment_id,tenant_id,command_id,outbox_id),UNIQUE(environment_id,tenant_id,previous_command_id),
          FOREIGN KEY(environment_id,tenant_id,submission_id) REFERENCES cp6_location_rule_review_v1.submissions(environment_id,tenant_id,id),
          FOREIGN KEY(environment_id,tenant_id,previous_command_id) REFERENCES cp6_location_rule_review_control_v1.withdrawals(environment_id,tenant_id,command_id),
          FOREIGN KEY(tenant_id,journal,operation_id) REFERENCES cp6_main_owner_v1.operations(tenant_id,owner,operation_id) DEFERRABLE INITIALLY DEFERRED);
        CREATE UNIQUE INDEX locationRule_withdrawal_first ON cp6_location_rule_review_control_v1.withdrawals(environment_id,tenant_id,submission_id) WHERE previous_command_id IS NULL;
        CREATE TABLE cp6_location_rule_review_control_v1.withdraw_outbox(
          environment_id uuid NOT NULL,tenant_id uuid NOT NULL,id uuid NOT NULL,command_id uuid NOT NULL,submission_id uuid NOT NULL,
          payload bytea NOT NULL,digest text NOT NULL CHECK(digest=encode(sha256(payload),'hex')),
          PRIMARY KEY(environment_id,tenant_id,id),UNIQUE(environment_id,tenant_id,command_id),
          FOREIGN KEY(environment_id,tenant_id,command_id,id) REFERENCES cp6_location_rule_review_control_v1.withdrawals(environment_id,tenant_id,command_id,outbox_id) DEFERRABLE INITIALLY DEFERRED);
        ALTER TABLE cp6_location_rule_review_control_v1.withdrawals ADD CONSTRAINT locationRule_withdrawal_outbox_required
          FOREIGN KEY(environment_id,tenant_id,outbox_id) REFERENCES cp6_location_rule_review_control_v1.withdraw_outbox(environment_id,tenant_id,id) DEFERRABLE INITIALLY DEFERRED;
        CREATE OR REPLACE FUNCTION cp6_location_rule_v1.advance() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP='INSERT' THEN
            IF NEW.version<>1 OR NEW.revision<>1 OR NEW.state<>'DRAFT' OR NEW.active_submission_id IS NOT NULL THEN RAISE EXCEPTION 'RULE_FRONTIER_INVALID';END IF;
          ELSIF (to_jsonb(NEW)-ARRAY['version','revision','token','state','operation_id','actor_id','active_submission_id']) IS DISTINCT FROM
              (to_jsonb(OLD)-ARRAY['version','revision','token','state','operation_id','actor_id','active_submission_id'])
            OR NEW.revision<>OLD.revision+1 OR NEW.token=OLD.token OR NEW.operation_id=OLD.operation_id
            OR NOT (
              OLD.state IN('DRAFT','RETURNED') AND NEW.state='DRAFT' AND NEW.version=OLD.version+1 AND NEW.active_submission_id IS NULL
              OR OLD.state IN('DRAFT','RETURNED') AND NEW.state='SUBMITTED' AND NEW.version=OLD.version AND NEW.active_submission_id IS NOT NULL
                AND NEW.active_submission_id IS DISTINCT FROM OLD.active_submission_id
              OR OLD.state='SUBMITTED' AND NEW.state IN('APPROVED_PENDING','RETURNED','REJECTED') AND NEW.version=OLD.version
                AND NEW.active_submission_id=OLD.active_submission_id
              OR OLD.state IN('DRAFT','RETURNED','SUBMITTED','APPROVED_PENDING','REJECTED') AND NEW.state='WITHDRAWN' AND NEW.version=OLD.version
                AND NEW.active_submission_id IS NOT DISTINCT FROM OLD.active_submission_id
              OR OLD.state='WITHDRAWN' AND NEW.state='WITHDRAWN' AND NEW.version=OLD.version
                AND NEW.active_submission_id=OLD.active_submission_id AND NEW.active_submission_id IS NOT NULL)
            THEN RAISE EXCEPTION 'RULE_FRONTIER_INVALID';END IF;RETURN NEW;
        END $$;
        -- The control is the existing proposal. No GET or migration fabricates historical withdrawal facts.
        CREATE FUNCTION cp6_location_rule_review_control_v1.complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE p cp6_location_rule_v1.proposals;w cp6_location_rule_review_control_v1.withdrawals;prior cp6_location_rule_review_control_v1.withdrawals;BEGIN
          SELECT * INTO w FROM cp6_location_rule_review_control_v1.withdrawals
            WHERE environment_id=NEW.environment_id AND tenant_id=NEW.tenant_id AND command_id=NEW.command_id;
          SELECT * INTO p FROM cp6_location_rule_v1.proposals WHERE tenant_id=w.tenant_id AND id=w.location_rule_id;
          IF w.id IS NULL OR p.id IS NULL OR p.state<>'WITHDRAWN' OR p.active_submission_id IS DISTINCT FROM w.submission_id
            OR p.operation_id<>w.operation_id OR p.actor_id<>w.actor_id
            OR NOT EXISTS(SELECT FROM cp6_location_rule_v1.proposals x WHERE x.tenant_id=p.tenant_id AND x.id=p.id AND x.xmin=pg_current_xact_id()::text::xid)
            OR w.before_version<>int8send(p.revision-1) OR w.after_version<>int8send(p.revision)
            OR NOT EXISTS(SELECT FROM cp6_location_rule_review_v1.submissions s WHERE
              (s.environment_id,s.tenant_id,s.id,s.location_rule_id)=(w.environment_id,w.tenant_id,w.submission_id,w.location_rule_id)
              AND s.location_rule_revision=p.version)
            OR EXISTS(SELECT FROM cp6_location_rule_review_application_v1.applications a WHERE
              (a.environment_id,a.tenant_id,a.submission_id)=(w.environment_id,w.tenant_id,w.submission_id))
            OR NOT EXISTS(SELECT FROM cp6_location_rule_review_control_v1.withdraw_outbox o WHERE
              (o.environment_id,o.tenant_id,o.command_id,o.id,o.submission_id,o.payload,o.digest)=(w.environment_id,w.tenant_id,w.command_id,w.outbox_id,w.submission_id,w.payload,w.digest))
            OR NOT EXISTS(SELECT FROM cp6_main_owner_v1.operations o WHERE o.tenant_id=w.tenant_id AND o.owner=w.journal AND o.operation_id=w.operation_id
              AND o.actor_id=w.actor_id AND o.contract='Main.Wms.LocationRuleWithdrawal.commands.v1' AND o.state='COMMITTED'
              AND o.object_ids=ARRAY[w.location_rule_id] AND o.xmin=pg_current_xact_id()::text::xid
              AND convert_from(o.result_bytes,'UTF8')::jsonb->>'State'='WITHDRAWN'
              AND convert_from(o.result_bytes,'UTF8')::jsonb->>'TransportCommandId'=w.command_id::text)
            THEN RAISE EXCEPTION 'LOCATION_RULE_WITHDRAWAL_ATOMIC_FACT_REQUIRED';END IF;
          IF w.previous_command_id IS NOT NULL THEN
            SELECT * INTO prior FROM cp6_location_rule_review_control_v1.withdrawals
              WHERE environment_id=w.environment_id AND tenant_id=w.tenant_id AND command_id=w.previous_command_id;
            IF (prior.location_rule_id,prior.submission_id,prior.after_version) IS DISTINCT FROM (w.location_rule_id,w.submission_id,w.before_version)
              OR NOT EXISTS(SELECT FROM cp6_approval_withdraw_v2.commands c WHERE
                (c.environment_id,c.tenant_id,c.id,c.submission_id)=(w.environment_id,w.tenant_id,w.previous_command_id,w.submission_id)
                AND c.owner='Main.Wms.LocationRuleProposal.v1' AND c.state='BLOCKED')
              THEN RAISE EXCEPTION 'LOCATION_RULE_BLOCKED_WITHDRAWAL_REQUIRED';END IF;
          END IF;RETURN NULL;
        END $$;
        CREATE FUNCTION cp6_location_rule_review_control_v1.repeat_provenance() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$ BEGIN
          IF OLD.state='WITHDRAWN' AND NEW.state='WITHDRAWN' AND NOT EXISTS(
            SELECT FROM cp6_location_rule_review_control_v1.withdrawals w WHERE w.tenant_id=NEW.tenant_id AND w.location_rule_id=NEW.id
              AND w.submission_id=NEW.active_submission_id AND w.operation_id=NEW.operation_id AND w.actor_id=NEW.actor_id
              AND w.before_version=int8send(OLD.revision) AND w.after_version=int8send(NEW.revision)
              AND w.xmin=pg_current_xact_id()::text::xid)
            THEN RAISE EXCEPTION 'LOCATION_RULE_EXPLICIT_WITHDRAWAL_REQUIRED';END IF;RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER rule_withdrawal_repeat_provenance AFTER UPDATE ON cp6_location_rule_v1.proposals
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_location_rule_review_control_v1.repeat_provenance();
        CREATE FUNCTION cp6_location_rule_review_control_v1.command_complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$ BEGIN
          -- Legacy proposal withdrawal remains legal without an OA fact. A new trusted
          -- withdrawal command must never commit a successful result without its fact.
          IF NEW.owner='Main.Wms.LocationRuleWithdrawal.operations.v1' AND NEW.state='COMMITTED'
            AND convert_from(NEW.result_bytes,'UTF8')::jsonb->>'State'='WITHDRAWN' AND NOT EXISTS(
              SELECT FROM cp6_location_rule_review_control_v1.withdrawals w WHERE w.tenant_id=NEW.tenant_id
                AND w.operation_id=NEW.operation_id AND w.actor_id=NEW.actor_id AND w.journal=NEW.owner
                AND NEW.contract='Main.Wms.LocationRuleWithdrawal.commands.v1' AND NEW.object_ids=ARRAY[w.location_rule_id]
                AND w.command_id::text=convert_from(NEW.result_bytes,'UTF8')::jsonb->>'TransportCommandId'
                AND w.xmin=pg_current_xact_id()::text::xid)
            THEN RAISE EXCEPTION 'LOCATION_RULE_WITHDRAWAL_ATOMIC_FACT_REQUIRED';END IF;RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER location_rule_withdrawal_command_complete AFTER INSERT ON cp6_main_owner_v1.operations
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_location_rule_review_control_v1.command_complete();
        DO $$ DECLARE tbl text;BEGIN FOREACH tbl IN ARRAY ARRAY['withdrawals','withdraw_outbox'] LOOP
          EXECUTE format('CREATE TRIGGER rule_withdraw_root BEFORE INSERT ON cp6_location_rule_review_control_v1.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change()',tbl);
          EXECUTE format('CREATE TRIGGER rule_withdraw_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_location_rule_review_control_v1.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable()',tbl);
          EXECUTE format('CREATE CONSTRAINT TRIGGER rule_withdraw_complete AFTER INSERT ON cp6_location_rule_review_control_v1.%I DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_location_rule_review_control_v1.complete()',tbl);
        END LOOP;END $$;
        CREATE FUNCTION cp6_location_rule_review_control_v1.ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT cp6_location_rule_v1.ready() AND EXISTS(SELECT FROM pg_trigger t
            WHERE t.tgrelid='cp6_location_rule_v1.proposals'::regclass AND t.tgname='rule_withdrawal_repeat_provenance'
              AND t.tgfoid='cp6_location_rule_review_control_v1.repeat_provenance()'::regprocedure AND t.tgtype=17
              AND t.tgenabled IN('O','A') AND t.tgqual IS NULL AND t.tgdeferrable AND t.tginitdeferred)
            AND EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid='cp6_main_owner_v1.operations'::regclass
              AND t.tgname='location_rule_withdrawal_command_complete'
              AND t.tgfoid='cp6_location_rule_review_control_v1.command_complete()'::regprocedure AND t.tgtype=5
              AND t.tgenabled IN('O','A') AND t.tgqual IS NULL AND t.tgdeferrable AND t.tginitdeferred)
            AND NOT EXISTS(SELECT FROM (VALUES('withdrawals'),('withdraw_outbox')) a(tbl) CROSS JOIN (VALUES
              ('rule_withdraw_root','cp6_authentication_v1.before_change()',6,false),
              ('rule_withdraw_immutable','cp6_scope_authorization_v1.immutable()',58,false),
              ('rule_withdraw_complete','cp6_location_rule_review_control_v1.complete()',5,true)) b(name,fn,typ,deferred)
              WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid=to_regclass('cp6_location_rule_review_control_v1.'||a.tbl)
                AND t.tgname=b.name AND t.tgfoid=to_regprocedure(b.fn) AND t.tgtype=b.typ AND t.tgenabled IN('O','A')
                AND t.tgqual IS NULL AND t.tgdeferrable=b.deferred AND t.tginitdeferred=b.deferred));
        $$;
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_location_rule_review_control_v1 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_location_rule_review_control_v1 FROM PUBLIC;
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",'review-withdraw','撤回库位规则评审',16,t."Id",LOCALTIMESTAMP FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t
        WHERE m."MenuKey"='wms-location-rule' AND NOT EXISTS(SELECT FROM public."Sys_MenuAction" x WHERE x."TenantId"=t."Id" AND x."MenuId"=m."MenuId" AND x."ActionCode"='review-withdraw');
        """);
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Location rule withdrawal history is forward-only.");
}
