using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261118073000_NumberAllocationCutover")]
public sealed class NumberAllocationCutover : Migration
{
    protected override void Up(MigrationBuilder migration) => migration.Sql("""
        -- 旧版缺少初始登记时刻；仅为迁移后的实际登记生成时间。
        ALTER TABLE cp6_number_allocation_v1.scopes ADD COLUMN registered_at timestamptz;
        ALTER TABLE cp6_number_allocation_v1.scopes ALTER COLUMN registered_at SET DEFAULT clock_timestamp();
        CREATE TABLE cp6_number_allocation_v1.cutovers (
            rule_id uuid PRIMARY KEY REFERENCES cp6_number_allocation_v1.scopes(rule_id),
            approval bytea NOT NULL, allocator text NOT NULL, period_count integer NOT NULL CHECK (period_count>=0),
            slot_count bigint NOT NULL CHECK (slot_count>=0), operation_count bigint NOT NULL CHECK (operation_count>=0),
            adopted_at timestamptz NOT NULL DEFAULT clock_timestamp());
        CREATE TABLE cp6_number_allocation_v1.prior_periods (
            rule_id uuid NOT NULL REFERENCES cp6_number_allocation_v1.cutovers(rule_id) DEFERRABLE INITIALLY DEFERRED,
            period_key text COLLATE "C" NOT NULL, last_issued integer NOT NULL CHECK (last_issued >= 0),
            PRIMARY KEY (rule_id, period_key));
        CREATE TABLE cp6_number_allocation_v1.prior_operations (
            rule_id uuid NOT NULL REFERENCES cp6_number_allocation_v1.cutovers(rule_id) DEFERRABLE INITIALLY DEFERRED,
            operation_hash bytea NOT NULL CHECK (octet_length(operation_hash) = 32), body bytea NOT NULL,
            PRIMARY KEY (rule_id, operation_hash));
        CREATE TABLE cp6_number_allocation_v1.prior_slots (
            rule_id uuid NOT NULL, period_key text COLLATE "C" NOT NULL, sequence_value integer NOT NULL CHECK (sequence_value > 0),
            number_hash bytea CHECK (octet_length(number_hash) = 32), operation_hash bytea, body bytea NOT NULL,
            PRIMARY KEY (rule_id, period_key, sequence_value), UNIQUE (rule_id, number_hash), UNIQUE (rule_id, operation_hash),
            FOREIGN KEY (rule_id, period_key) REFERENCES cp6_number_allocation_v1.prior_periods(rule_id, period_key),
            FOREIGN KEY (rule_id, operation_hash) REFERENCES cp6_number_allocation_v1.prior_operations(rule_id, operation_hash));
        CREATE FUNCTION cp6_number_allocation_v1.require_open_manifest() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          PERFORM 1 FROM cp6_number_allocation_v1.scopes WHERE rule_id=NEW.rule_id FOR UPDATE;
          IF NOT FOUND OR EXISTS(SELECT FROM cp6_number_allocation_v1.cutovers WHERE rule_id=NEW.rule_id) THEN
            RAISE EXCEPTION 'NUMBER_CUTOVER_MANIFEST_CLOSED' USING ERRCODE='23514';
          END IF;
          RETURN NEW;
        END $$;
        CREATE FUNCTION cp6_number_allocation_v1.seal_manifest() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          PERFORM 1 FROM cp6_number_allocation_v1.scopes WHERE rule_id=NEW.rule_id FOR UPDATE;
          IF NEW.period_count<>(SELECT count(*) FROM cp6_number_allocation_v1.prior_periods WHERE rule_id=NEW.rule_id)
             OR NEW.slot_count<>(SELECT count(*) FROM cp6_number_allocation_v1.prior_slots WHERE rule_id=NEW.rule_id)
             OR NEW.operation_count<>(SELECT count(*) FROM cp6_number_allocation_v1.prior_operations WHERE rule_id=NEW.rule_id)
          THEN RAISE EXCEPTION 'NUMBER_CUTOVER_INCOMPLETE' USING ERRCODE='23514'; END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER seal BEFORE INSERT ON cp6_number_allocation_v1.cutovers
          FOR EACH ROW EXECUTE FUNCTION cp6_number_allocation_v1.seal_manifest();
        DO $$
        DECLARE name text;
        BEGIN
          FOREACH name IN ARRAY ARRAY['cutovers','prior_periods','prior_operations','prior_slots'] LOOP
            EXECUTE format('CREATE TRIGGER immutable BEFORE UPDATE OR DELETE ON cp6_number_allocation_v1.%I
                FOR EACH ROW EXECUTE FUNCTION cp6_number_allocation_v1.immutable()', name);
            EXECUTE format('CREATE TRIGGER no_truncate BEFORE TRUNCATE ON cp6_number_allocation_v1.%I
                FOR EACH STATEMENT EXECUTE FUNCTION cp6_number_allocation_v1.immutable()', name);
          END LOOP;
          FOREACH name IN ARRAY ARRAY['prior_periods','prior_operations','prior_slots'] LOOP
            EXECUTE format('CREATE TRIGGER open_manifest BEFORE INSERT ON cp6_number_allocation_v1.%I
                FOR EACH ROW EXECUTE FUNCTION cp6_number_allocation_v1.require_open_manifest()', name);
          END LOOP;
        END $$;

        CREATE FUNCTION cp6_number_allocation_v1.protect_prior_allocation() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF EXISTS (SELECT FROM cp6_number_allocation_v1.prior_periods
               WHERE rule_id=NEW.rule_id AND period_key=NEW.period_key AND last_issued>=NEW.sequence_value)
             OR EXISTS (SELECT FROM cp6_number_allocation_v1.prior_slots WHERE rule_id=NEW.rule_id AND number_hash=NEW.number_hash)
             OR EXISTS (SELECT FROM cp6_number_allocation_v1.prior_operations WHERE rule_id=NEW.rule_id AND operation_hash=NEW.operation_hash)
          THEN RAISE EXCEPTION 'NUMBER_PREDECESSOR_HISTORY' USING ERRCODE='23514', CONSTRAINT='number_predecessor_history'; END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER predecessor_receipt BEFORE INSERT ON cp6_number_allocation_v1.receipts
          FOR EACH ROW EXECUTE FUNCTION cp6_number_allocation_v1.protect_prior_allocation();
        """);

    protected override void Down(MigrationBuilder migration)
        => throw new NotSupportedException("已外露号码、跳号及未决原操作只能前向迁移。");
}
