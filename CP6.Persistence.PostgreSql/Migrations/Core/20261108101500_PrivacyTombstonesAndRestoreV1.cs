using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)),Migration("20261108101500_PrivacyTombstonesAndRestoreV1")]
public sealed class PrivacyTombstonesAndRestoreV1:Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        CREATE TABLE cp6_privacy_disposition_v1.tombstones(
          plan_id uuid PRIMARY KEY,environment_id uuid NOT NULL,actor_tenant uuid NOT NULL,target_tenant uuid NOT NULL,
          subject_id uuid NOT NULL,policy_id uuid NOT NULL,receipt_json text NOT NULL);
        CREATE INDEX tombstone_subject ON cp6_privacy_disposition_v1.tombstones(target_tenant,subject_id);
        CREATE INDEX tombstone_stable_subject ON cp6_privacy_disposition_v1.tombstones(subject_id);
        INSERT INTO cp6_privacy_disposition_v1.tombstones
        SELECT e.plan_id,e.environment_id,e.actor_tenant,e.target_tenant,e.subject_id,(p.scope_json::jsonb->>'PolicyId')::uuid,e.receipt_json
        FROM cp6_privacy_disposition_v1.executions e JOIN cp6_privacy_disposition_v1.plans p ON p.id=e.plan_id;
        CREATE TABLE cp6_privacy_disposition_v1.restore_epoch(singleton boolean PRIMARY KEY DEFAULT true CHECK(singleton),version uuid NOT NULL);
        INSERT INTO cp6_privacy_disposition_v1.restore_epoch VALUES(true,gen_random_uuid());
        CREATE FUNCTION cp6_privacy_disposition_v1.advance_restore_epoch() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN UPDATE cp6_privacy_disposition_v1.restore_epoch SET version=gen_random_uuid() WHERE singleton;RETURN NULL;END $$;
        CREATE FUNCTION cp6_privacy_disposition_v1.capture_tombstone() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          INSERT INTO cp6_privacy_disposition_v1.tombstones
          SELECT NEW.plan_id,NEW.environment_id,NEW.actor_tenant,NEW.target_tenant,NEW.subject_id,(p.scope_json::jsonb->>'PolicyId')::uuid,NEW.receipt_json
          FROM cp6_privacy_disposition_v1.plans p WHERE p.id=NEW.plan_id;
          RETURN NEW;
        END $$;
        CREATE TRIGGER privacy_tombstone AFTER INSERT ON cp6_privacy_disposition_v1.executions
          FOR EACH ROW EXECUTE FUNCTION cp6_privacy_disposition_v1.capture_tombstone();
        CREATE FUNCTION cp6_privacy_disposition_v1.profile_redacted(u public."Sys_Users") RETURNS boolean LANGUAGE sql IMMUTABLE AS $$
          SELECT NOT u."Enable" AND u."UserName"='anon_'||replace(u."Id"::text,'-','')
          AND u."NickName" IS NULL AND u."Email" IS NULL AND u."LastLoginIp" IS NULL AND u."BadgeNo" IS NULL
          AND u."ExternalSubject" IS NULL AND u."ExternalProvider" IS NULL AND u."TwoFactorSecret" IS NULL
          AND NOT u."TwoFactorEnabled" AND u."QuickPinHash" IS NULL AND NOT u."AllowPasswordFallback";
        $$;
        -- Never silently accept pre-existing resurrection as a successfully migrated privacy baseline.
        DO $$ BEGIN IF EXISTS(SELECT FROM public."Sys_Users" u JOIN cp6_privacy_disposition_v1.tombstones t
          ON t.target_tenant=u."TenantId" AND t.subject_id=u."Id" WHERE NOT cp6_privacy_disposition_v1.profile_redacted(u))
          THEN RAISE EXCEPTION 'PRIVACY_TOMBSTONE_EXISTING_PROFILE_REQUIRES_CONTROLLED_REPAIR' USING ERRCODE='23514';END IF;END $$;
        CREATE FUNCTION cp6_privacy_disposition_v1.guard_profile() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF TG_OP='UPDATE' AND (NEW."TenantId",NEW."Id") IS DISTINCT FROM (OLD."TenantId",OLD."Id")
            AND EXISTS(SELECT FROM cp6_privacy_disposition_v1.tombstones WHERE target_tenant=OLD."TenantId" AND subject_id=OLD."Id")
          THEN RAISE EXCEPTION 'PRIVACY_TOMBSTONE_IDENTITY_IMMUTABLE' USING ERRCODE='23514';END IF;
          IF EXISTS(SELECT FROM cp6_privacy_disposition_v1.tombstones WHERE subject_id=NEW."Id" AND target_tenant<>NEW."TenantId")
          THEN RAISE EXCEPTION 'PRIVACY_TOMBSTONE_IDENTITY_REUSE_FORBIDDEN' USING ERRCODE='23514';END IF;
          IF EXISTS(SELECT FROM cp6_privacy_disposition_v1.tombstones WHERE target_tenant=NEW."TenantId" AND subject_id=NEW."Id")
            AND NOT cp6_privacy_disposition_v1.profile_redacted(NEW)
          THEN RAISE EXCEPTION 'PRIVACY_TOMBSTONE_PROFILE_RESURRECTION_FORBIDDEN' USING ERRCODE='23514';END IF;
          RETURN NEW;
        END $$;
        -- Existing Sys_Users statement triggers already acquire the common IAM authority gate.
        CREATE TRIGGER privacy_profile_fence BEFORE INSERT OR UPDATE ON public."Sys_Users"
          FOR EACH ROW EXECUTE FUNCTION cp6_privacy_disposition_v1.guard_profile();
        CREATE TABLE cp6_privacy_disposition_v1.restore_plans(
          id uuid PRIMARY KEY,environment_id uuid NOT NULL,actor_tenant uuid NOT NULL,requester_id uuid NOT NULL,
          request_ref uuid NOT NULL,scope_json text NOT NULL,scope_digest text NOT NULL,at timestamptz NOT NULL DEFAULT clock_timestamp());
        CREATE TABLE cp6_privacy_disposition_v1.restore_events(
          sequence bigserial PRIMARY KEY,plan_id uuid NOT NULL REFERENCES cp6_privacy_disposition_v1.restore_plans,
          version uuid UNIQUE NOT NULL,previous_version uuid,state text NOT NULL CHECK(state IN('REQUESTED','APPROVED','REJECTED','REVOKED')),
          actor_id uuid NOT NULL,decision_ref uuid,UNIQUE(plan_id,previous_version));
        CREATE TABLE cp6_privacy_disposition_v1.restore_intents(
          environment_id uuid NOT NULL,actor_tenant uuid NOT NULL,actor_id uuid NOT NULL,operation_id uuid NOT NULL,
          plan_id uuid NOT NULL REFERENCES cp6_privacy_disposition_v1.restore_plans,input_bytes bytea NOT NULL,
          PRIMARY KEY(environment_id,actor_tenant,actor_id,operation_id));
        CREATE TABLE cp6_privacy_disposition_v1.restore_outcomes(
          environment_id uuid NOT NULL,actor_tenant uuid NOT NULL,actor_id uuid NOT NULL,operation_id uuid NOT NULL,
          plan_id uuid,source_version uuid,candidate_id uuid,target_identity text,state text NOT NULL CHECK(state IN('APPLIED','CLOSED')),
          result_json text NOT NULL,PRIMARY KEY(environment_id,actor_tenant,actor_id,operation_id));
        CREATE UNIQUE INDEX restore_applied_plan ON cp6_privacy_disposition_v1.restore_outcomes(plan_id) WHERE state='APPLIED';
        CREATE TABLE cp6_privacy_disposition_v1.restore_receipts(
          environment_id uuid NOT NULL,actor_tenant uuid NOT NULL,actor_id uuid NOT NULL,operation_id uuid NOT NULL,
          result_json text NOT NULL,PRIMARY KEY(environment_id,actor_tenant,actor_id,operation_id));
        CREATE TABLE cp6_privacy_disposition_v1.restore_admission(singleton boolean PRIMARY KEY DEFAULT true CHECK(singleton),
          candidate_id uuid NOT NULL,source_identity text NOT NULL,target_identity text NOT NULL,environment_id uuid NOT NULL,
          source_version uuid NOT NULL,actor_tenant uuid NOT NULL,actor_id uuid NOT NULL,operation_id uuid NOT NULL,plan_id uuid NOT NULL);
        DO $$ DECLARE n text;BEGIN
          FOREACH n IN ARRAY ARRAY['tombstones','restore_plans','restore_events','restore_intents','restore_outcomes','restore_receipts'] LOOP
            EXECUTE format('CREATE TRIGGER authority_gate BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_privacy_disposition_v1.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_privacy_disposition_v1.gate()',n);
            EXECUTE format('CREATE TRIGGER immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_privacy_disposition_v1.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_privacy_disposition_v1.immutable()',n);
          END LOOP;
          FOREACH n IN ARRAY ARRAY['policies','holds','tombstones'] LOOP
            EXECUTE format('CREATE TRIGGER restore_source_revision AFTER INSERT ON cp6_privacy_disposition_v1.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_privacy_disposition_v1.advance_restore_epoch()',n);
          END LOOP;
        END $$;
        CREATE TRIGGER authority_gate BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_privacy_disposition_v1.restore_admission
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_privacy_disposition_v1.gate();
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_privacy_disposition_v1 FROM PUBLIC;
        REVOKE ALL ON ALL SEQUENCES IN SCHEMA cp6_privacy_disposition_v1 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_privacy_disposition_v1 FROM PUBLIC;
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",v.action,v.name,0,t."Id",LOCALTIMESTAMP
        FROM (VALUES('restore-prepare','准备隐私恢复候选'),('restore-approve','独立批准隐私恢复'),('restore-execute','执行已批准隐私恢复'))v(action,name)
        JOIN public."Sys_Menus" m ON m."MenuKey"='privacy-disposition' CROSS JOIN public."Sys_Tenants" t
        WHERE NOT EXISTS(SELECT FROM public."Sys_MenuAction" a WHERE a."TenantId"=t."Id" AND a."MenuId"=m."MenuId" AND a."ActionCode"=v.action);
        """);
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Privacy tombstones and restore history are forward-only.");
}
