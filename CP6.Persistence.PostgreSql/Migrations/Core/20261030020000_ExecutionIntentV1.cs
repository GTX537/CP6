using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261030020000_ExecutionIntentV1")]
public sealed class ExecutionIntentV1 : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE SCHEMA cp6_execution_v1;
        REVOKE ALL ON SCHEMA cp6_execution_v1 FROM PUBLIC;
        CREATE TABLE cp6_execution_v1.intents(
          environment_id uuid NOT NULL,sandbox_id uuid NOT NULL,tenant_id uuid NOT NULL,authority_key text NOT NULL,
          authority_bytes bytea NOT NULL CHECK(authority_key=encode(sha256(authority_bytes),'hex')),intent_id uuid NOT NULL,
          intent_bytes bytea NOT NULL,intent_digest text NOT NULL CHECK(intent_digest=encode(sha256(intent_bytes),'hex')),
          required_count integer NOT NULL CHECK(required_count>0),state text NOT NULL CHECK(state IN('OPEN','PREPARING','READY','APPLIED','SEALED_NO_EFFECT')),
          revision bigint NOT NULL CHECK(revision>0),execution_epoch bigint NOT NULL CHECK(execution_epoch>0),decision_bytes bytea,
          CHECK((state IN('APPLIED','SEALED_NO_EFFECT'))=(decision_bytes IS NOT NULL)),
          PRIMARY KEY(environment_id,sandbox_id,tenant_id,authority_key),UNIQUE(environment_id,sandbox_id,tenant_id,intent_id));
        CREATE TABLE cp6_execution_v1.aliases(
          environment_id uuid NOT NULL,sandbox_id uuid NOT NULL,tenant_id uuid NOT NULL,authority_key text NOT NULL,
          alias_key text NOT NULL,body_bytes bytea NOT NULL CHECK(alias_key=encode(sha256(body_bytes),'hex')),
          PRIMARY KEY(environment_id,sandbox_id,tenant_id,alias_key),
          FOREIGN KEY(environment_id,sandbox_id,tenant_id,authority_key) REFERENCES cp6_execution_v1.intents);
        CREATE TABLE cp6_execution_v1.bindings(
          environment_id uuid NOT NULL,sandbox_id uuid NOT NULL,tenant_id uuid NOT NULL,authority_key text NOT NULL,binding_id uuid NOT NULL,
          owner text NOT NULL,scope_key text NOT NULL,protection_kind text NOT NULL,prepare_key uuid NOT NULL,
          body_bytes bytea NOT NULL,body_digest text NOT NULL CHECK(body_digest=encode(sha256(body_bytes),'hex')),
          PRIMARY KEY(environment_id,sandbox_id,tenant_id,authority_key,binding_id),
          UNIQUE(environment_id,sandbox_id,tenant_id,authority_key,owner,scope_key,protection_kind),UNIQUE(environment_id,sandbox_id,tenant_id,owner,prepare_key),
          FOREIGN KEY(environment_id,sandbox_id,tenant_id,authority_key) REFERENCES cp6_execution_v1.intents);
        CREATE TABLE cp6_execution_v1.actions(
          environment_id uuid NOT NULL,sandbox_id uuid NOT NULL,tenant_id uuid NOT NULL,authority_key text NOT NULL,action_id uuid NOT NULL,binding_id uuid NOT NULL,
          kind text NOT NULL CHECK(kind IN('PREPARE','CONFIRM_COMMITTED','RELEASE_SEALED')),payload_bytes bytea NOT NULL,
          payload_digest text NOT NULL CHECK(payload_digest=encode(sha256(payload_bytes),'hex')),state text NOT NULL CHECK(state IN('PENDING','WORKING','SUCCEEDED','CONFLICT')),
          attempt_epoch bigint NOT NULL CHECK(attempt_epoch>=0),attempt_revision bigint NOT NULL CHECK(attempt_revision>0),
          observation_state text NOT NULL CHECK(observation_state IN('UNKNOWN','CONFIRMED','CONFLICT')),last_confirmed_bytes bytea,last_observation_bytes bytea,
          PRIMARY KEY(environment_id,sandbox_id,tenant_id,authority_key,action_id),UNIQUE(environment_id,sandbox_id,tenant_id,authority_key,binding_id,kind),
          FOREIGN KEY(environment_id,sandbox_id,tenant_id,authority_key,binding_id) REFERENCES cp6_execution_v1.bindings);
        CREATE TABLE cp6_execution_v1.observations(
          environment_id uuid NOT NULL,sandbox_id uuid NOT NULL,tenant_id uuid NOT NULL,authority_key text NOT NULL,action_id uuid NOT NULL,
          attempt_epoch bigint NOT NULL CHECK(attempt_epoch>0),body_bytes bytea NOT NULL,body_digest text NOT NULL CHECK(body_digest=encode(sha256(body_bytes),'hex')),
          PRIMARY KEY(environment_id,sandbox_id,tenant_id,authority_key,action_id,attempt_epoch),
          FOREIGN KEY(environment_id,sandbox_id,tenant_id,authority_key,action_id) REFERENCES cp6_execution_v1.actions);
        CREATE FUNCTION cp6_execution_v1.intent_update() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF OLD.state IN('APPLIED','SEALED_NO_EFFECT') OR NEW.revision<>OLD.revision+1
            OR (NEW.environment_id,NEW.sandbox_id,NEW.tenant_id,NEW.authority_key,NEW.authority_bytes,NEW.intent_id,NEW.intent_bytes,NEW.intent_digest,NEW.required_count)
              IS DISTINCT FROM (OLD.environment_id,OLD.sandbox_id,OLD.tenant_id,OLD.authority_key,OLD.authority_bytes,OLD.intent_id,OLD.intent_bytes,OLD.intent_digest,OLD.required_count)
            OR NOT ((NEW.state='SEALED_NO_EFFECT' AND NEW.execution_epoch=OLD.execution_epoch+1)
              OR (OLD.state='READY' AND NEW.state='APPLIED' AND NEW.execution_epoch=OLD.execution_epoch)
              OR (OLD.state='OPEN' AND NEW.state='PREPARING' AND NEW.execution_epoch=OLD.execution_epoch)
              OR (OLD.state IN('PREPARING','READY') AND NEW.state='PREPARING' AND NEW.execution_epoch IN(OLD.execution_epoch,OLD.execution_epoch+1))
              OR (OLD.state='PREPARING' AND NEW.state='READY' AND NEW.execution_epoch=OLD.execution_epoch))
          THEN RAISE EXCEPTION 'EXECUTION_INTENT_UPDATE_INVALID'; END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER execution_intent_update BEFORE UPDATE ON cp6_execution_v1.intents FOR EACH ROW EXECUTE FUNCTION cp6_execution_v1.intent_update();
        CREATE FUNCTION cp6_execution_v1.action_update() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF NEW.attempt_revision<>OLD.attempt_revision+1 OR NEW.attempt_epoch NOT IN(OLD.attempt_epoch,OLD.attempt_epoch+1)
            OR (OLD.last_confirmed_bytes IS NOT NULL AND NEW.last_confirmed_bytes IS NULL)
            OR (NEW.environment_id,NEW.sandbox_id,NEW.tenant_id,NEW.authority_key,NEW.action_id,NEW.binding_id,NEW.kind,NEW.payload_bytes,NEW.payload_digest)
              IS DISTINCT FROM (OLD.environment_id,OLD.sandbox_id,OLD.tenant_id,OLD.authority_key,OLD.action_id,OLD.binding_id,OLD.kind,OLD.payload_bytes,OLD.payload_digest)
          THEN RAISE EXCEPTION 'EXECUTION_ACTION_UPDATE_INVALID'; END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER execution_action_update BEFORE UPDATE ON cp6_execution_v1.actions FOR EACH ROW EXECUTE FUNCTION cp6_execution_v1.action_update();
        CREATE FUNCTION cp6_execution_v1.complete_set() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE i cp6_execution_v1.intents%ROWTYPE; prepares bigint; confirms bigint; releases bigint;
        BEGIN
          SELECT * INTO STRICT i FROM cp6_execution_v1.intents WHERE environment_id=NEW.environment_id AND sandbox_id=NEW.sandbox_id AND tenant_id=NEW.tenant_id AND authority_key=NEW.authority_key FOR UPDATE;
          SELECT count(*) FILTER(WHERE kind='PREPARE'),count(*) FILTER(WHERE kind='CONFIRM_COMMITTED'),count(*) FILTER(WHERE kind='RELEASE_SEALED') INTO prepares,confirms,releases
            FROM cp6_execution_v1.actions WHERE environment_id=i.environment_id AND sandbox_id=i.sandbox_id AND tenant_id=i.tenant_id AND authority_key=i.authority_key;
          IF i.required_count<>(SELECT count(*) FROM cp6_execution_v1.bindings WHERE environment_id=i.environment_id AND sandbox_id=i.sandbox_id AND tenant_id=i.tenant_id AND authority_key=i.authority_key)
            OR prepares<>i.required_count OR confirms<>(CASE WHEN i.state='APPLIED' THEN i.required_count ELSE 0 END)
            OR releases<>(CASE WHEN i.state='SEALED_NO_EFFECT' THEN i.required_count ELSE 0 END)
          THEN RAISE EXCEPTION 'EXECUTION_ACTION_SET_INCOMPLETE'; END IF;
          RETURN NULL;
        END $$;
        DO $$ DECLARE tbl text; BEGIN
          FOREACH tbl IN ARRAY ARRAY['intents','bindings','actions'] LOOP
            EXECUTE format('CREATE CONSTRAINT TRIGGER execution_complete_set AFTER INSERT OR UPDATE ON cp6_execution_v1.%I DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_execution_v1.complete_set()',tbl);
          END LOOP;
          FOREACH tbl IN ARRAY ARRAY['aliases','bindings','observations'] LOOP
            EXECUTE format('CREATE TRIGGER execution_original_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_execution_v1.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable()',tbl);
          END LOOP;
          FOREACH tbl IN ARRAY ARRAY['intents','actions'] LOOP
            EXECUTE format('CREATE TRIGGER execution_permanent BEFORE DELETE OR TRUNCATE ON cp6_execution_v1.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable()',tbl);
          END LOOP;
        END $$;
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_execution_v1 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_execution_v1 FROM PUBLIC;
        """);
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Execution decisions and action originals require forward recovery.");
}
