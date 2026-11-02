using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261102040000_NumberRuleChangesV1")]
public sealed class NumberRuleChangesV1 : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE TABLE cp6_number_allocation_v1.rule_versions(
          rule_id uuid NOT NULL REFERENCES cp6_number_allocation_v1.scopes(rule_id),
          revision bigint NOT NULL CHECK(revision>=0),version_hash bytea NOT NULL CHECK(octet_length(version_hash)=32),
          body bytea NOT NULL,PRIMARY KEY(rule_id,revision),UNIQUE(rule_id,version_hash));
        CREATE TABLE cp6_number_allocation_v1.rule_changes(
          rule_id uuid NOT NULL,operation_hash bytea NOT NULL CHECK(octet_length(operation_hash)=32),
          operation_bytes bytea NOT NULL,input_bytes bytea NOT NULL,actor_id uuid NOT NULL,result_bytes bytea NOT NULL,
          version_hash bytea NOT NULL,changed_at timestamptz NOT NULL,PRIMARY KEY(rule_id,operation_hash),
          UNIQUE(rule_id,version_hash),FOREIGN KEY(rule_id,version_hash)
            REFERENCES cp6_number_allocation_v1.rule_versions(rule_id,version_hash));
        CREATE TRIGGER immutable BEFORE UPDATE OR DELETE ON cp6_number_allocation_v1.rule_versions
          FOR EACH ROW EXECUTE FUNCTION cp6_number_allocation_v1.immutable();
        CREATE TRIGGER immutable BEFORE UPDATE OR DELETE ON cp6_number_allocation_v1.rule_changes
          FOR EACH ROW EXECUTE FUNCTION cp6_number_allocation_v1.immutable();
        CREATE TRIGGER no_truncate BEFORE TRUNCATE ON cp6_number_allocation_v1.rule_versions
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_number_allocation_v1.immutable();
        CREATE TRIGGER no_truncate BEFORE TRUNCATE ON cp6_number_allocation_v1.rule_changes
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_number_allocation_v1.immutable();
        CREATE FUNCTION cp6_number_allocation_v1.append_rule() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE previous bigint;
        BEGIN
          PERFORM 1 FROM cp6_number_allocation_v1.scopes WHERE rule_id=NEW.rule_id FOR UPDATE;
          SELECT max(revision) INTO previous FROM cp6_number_allocation_v1.rule_versions WHERE rule_id=NEW.rule_id;
          IF NEW.revision<>coalesce(previous,-1)+1 THEN
            RAISE EXCEPTION 'NUMBER_RULE_NOT_MONOTONIC' USING ERRCODE='23514'; END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER append_rule BEFORE INSERT ON cp6_number_allocation_v1.rule_versions
          FOR EACH ROW EXECUTE FUNCTION cp6_number_allocation_v1.append_rule();
        CREATE FUNCTION cp6_number_allocation_v1.complete_rule() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF NEW.revision>0 AND NOT EXISTS(SELECT FROM cp6_number_allocation_v1.rule_changes c
            WHERE c.rule_id=NEW.rule_id AND c.version_hash=NEW.version_hash) THEN
            RAISE EXCEPTION 'NUMBER_RULE_CHANGE_INCOMPLETE' USING ERRCODE='23514'; END IF;
          RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER complete_rule AFTER INSERT ON cp6_number_allocation_v1.rule_versions
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_number_allocation_v1.complete_rule();
        ALTER TABLE cp6_number_allocation_v1.receipts ADD COLUMN rule_version_hash bytea
          CHECK(rule_version_hash IS NULL OR octet_length(rule_version_hash)=32);
        CREATE FUNCTION cp6_number_allocation_v1.current_rule_receipt() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE current_hash bytea;
        BEGIN
          PERFORM 1 FROM cp6_number_allocation_v1.scopes WHERE rule_id=NEW.rule_id FOR UPDATE;
          SELECT version_hash INTO current_hash FROM cp6_number_allocation_v1.rule_versions
            WHERE rule_id=NEW.rule_id ORDER BY revision DESC LIMIT 1;
          IF current_hash IS NOT NULL AND NEW.rule_version_hash IS DISTINCT FROM current_hash THEN
            RAISE EXCEPTION 'NUMBER_RULE_VERSION_CHANGED' USING ERRCODE='23514'; END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER current_rule_receipt BEFORE INSERT ON cp6_number_allocation_v1.receipts
          FOR EACH ROW EXECUTE FUNCTION cp6_number_allocation_v1.current_rule_receipt();
        """);
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Rule history and original commands are forward-only.");
}
