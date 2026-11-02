using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261102021000_NumberAllocationV1")]
public sealed class NumberAllocationV1 : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE SCHEMA cp6_number_allocation_v1;
        CREATE TABLE cp6_number_allocation_v1.scopes(
          rule_id uuid PRIMARY KEY REFERENCES public."Pub_DocSequence"("Id"),tenant_id uuid NOT NULL,body bytea NOT NULL);
        CREATE TABLE cp6_number_allocation_v1.periods(
          rule_id uuid NOT NULL REFERENCES cp6_number_allocation_v1.scopes(rule_id),period_key text COLLATE "C" NOT NULL,
          current_value integer NOT NULL CHECK(current_value>0),PRIMARY KEY(rule_id,period_key));
        CREATE TABLE cp6_number_allocation_v1.receipts(
          rule_id uuid NOT NULL,operation_hash bytea NOT NULL CHECK(octet_length(operation_hash)=32),
          operation_bytes bytea NOT NULL,input_bytes bytea NOT NULL,actor_id uuid NOT NULL,result_bytes bytea NOT NULL,
          period_key text COLLATE "C" NOT NULL,sequence_value integer NOT NULL CHECK(sequence_value>0),
          number_hash bytea NOT NULL CHECK(octet_length(number_hash)=32),number_bytes bytea NOT NULL,allocated_at timestamptz NOT NULL,
          PRIMARY KEY(rule_id,operation_hash),UNIQUE(rule_id,period_key,sequence_value),
          CONSTRAINT number_unique UNIQUE(rule_id,number_hash),
          FOREIGN KEY(rule_id,period_key) REFERENCES cp6_number_allocation_v1.periods(rule_id,period_key));
        CREATE FUNCTION cp6_number_allocation_v1.immutable() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN RAISE EXCEPTION 'NUMBER_HISTORY_IMMUTABLE' USING ERRCODE='23514'; END $$;
        CREATE TRIGGER immutable BEFORE UPDATE OR DELETE ON cp6_number_allocation_v1.scopes
          FOR EACH ROW EXECUTE FUNCTION cp6_number_allocation_v1.immutable();
        CREATE TRIGGER immutable BEFORE UPDATE OR DELETE ON cp6_number_allocation_v1.receipts
          FOR EACH ROW EXECUTE FUNCTION cp6_number_allocation_v1.immutable();
        CREATE TRIGGER no_delete BEFORE DELETE ON cp6_number_allocation_v1.periods
          FOR EACH ROW EXECUTE FUNCTION cp6_number_allocation_v1.immutable();
        CREATE TRIGGER no_truncate BEFORE TRUNCATE ON cp6_number_allocation_v1.scopes
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_number_allocation_v1.immutable();
        CREATE TRIGGER no_truncate BEFORE TRUNCATE ON cp6_number_allocation_v1.periods
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_number_allocation_v1.immutable();
        CREATE TRIGGER no_truncate BEFORE TRUNCATE ON cp6_number_allocation_v1.receipts
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_number_allocation_v1.immutable();
        CREATE FUNCTION cp6_number_allocation_v1.advance() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF NEW.rule_id<>OLD.rule_id OR NEW.period_key<>OLD.period_key OR NEW.current_value::bigint<>OLD.current_value::bigint+1
          THEN RAISE EXCEPTION 'NUMBER_COUNTER_NOT_MONOTONIC' USING ERRCODE='23514'; END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER advance BEFORE UPDATE ON cp6_number_allocation_v1.periods
          FOR EACH ROW EXECUTE FUNCTION cp6_number_allocation_v1.advance();
        CREATE FUNCTION cp6_number_allocation_v1.complete_counter() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF NOT EXISTS(SELECT FROM cp6_number_allocation_v1.receipts r WHERE r.rule_id=NEW.rule_id
            AND r.period_key=NEW.period_key AND r.sequence_value=NEW.current_value)
          THEN RAISE EXCEPTION 'NUMBER_ALLOCATION_INCOMPLETE' USING ERRCODE='23514'; END IF;
          RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER complete_counter AFTER INSERT OR UPDATE ON cp6_number_allocation_v1.periods
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_number_allocation_v1.complete_counter();
        CREATE FUNCTION cp6_number_allocation_v1.complete_receipt() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF NOT EXISTS(SELECT FROM cp6_number_allocation_v1.periods p WHERE p.rule_id=NEW.rule_id
            AND p.period_key=NEW.period_key AND p.current_value>=NEW.sequence_value)
          THEN RAISE EXCEPTION 'NUMBER_ALLOCATION_INCOMPLETE' USING ERRCODE='23514'; END IF;
          RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER complete_receipt AFTER INSERT ON cp6_number_allocation_v1.receipts
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_number_allocation_v1.complete_receipt();
        CREATE FUNCTION cp6_number_allocation_v1.protect_rule() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF TG_OP='TRUNCATE' THEN
            IF EXISTS(SELECT FROM cp6_number_allocation_v1.scopes) THEN
              RAISE EXCEPTION 'NUMBER_ALLOCATION_OWNER_REQUIRED' USING ERRCODE='23514'; END IF;
            RETURN NULL;
          END IF;
          IF EXISTS(SELECT FROM cp6_number_allocation_v1.scopes s WHERE s.rule_id=OLD."Id") THEN
            RAISE EXCEPTION 'NUMBER_ALLOCATION_OWNER_REQUIRED' USING ERRCODE='23514'; END IF;
          IF TG_OP='DELETE' THEN RETURN OLD; END IF; RETURN NEW;
        END $$;
        CREATE TRIGGER number_owner BEFORE UPDATE OR DELETE ON public."Pub_DocSequence"
          FOR EACH ROW EXECUTE FUNCTION cp6_number_allocation_v1.protect_rule();
        CREATE TRIGGER number_owner_truncate BEFORE TRUNCATE ON public."Pub_DocSequence"
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_number_allocation_v1.protect_rule();
        """);
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Allocated/exposed numbers and original receipts are forward-only.");
}
