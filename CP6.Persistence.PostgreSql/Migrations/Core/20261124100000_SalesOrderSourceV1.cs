using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261124100000_SalesOrderSourceV1")]
public sealed class SalesOrderSourceV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE SCHEMA cp6_sales_order_source_v1;
        CREATE TABLE cp6_sales_order_source_v1.legacy_originals (
          tenant_id uuid NOT NULL,
          order_id uuid NOT NULL,
          revision bigint NOT NULL CHECK (revision > 0),
          writer text NOT NULL,
          origin bytea NOT NULL,
          body bytea NOT NULL,
          digest text NOT NULL CHECK (digest ~ '^[0-9a-f]{64}$'),
          recorded_at timestamptz NOT NULL DEFAULT clock_timestamp(),
          PRIMARY KEY (tenant_id, order_id, revision)
        );

        CREATE TABLE cp6_sales_order_source_v1.confirmed_scopes (
          tenant_id uuid NOT NULL,
          operation_key text COLLATE "C" NOT NULL,
          actor_id uuid NOT NULL,
          order_id uuid NOT NULL,
          line_id uuid NOT NULL,
          order_version text COLLATE "C" NOT NULL,
          schedule_id text COLLATE "C" NOT NULL,
          proof bytea NOT NULL,
          proof_sha256 text NOT NULL CHECK (proof_sha256 ~ '^[0-9a-f]{64}$'),
          schedule bytea NOT NULL,
          schedule_sha256 text NOT NULL CHECK (schedule_sha256 ~ '^[0-9a-f]{64}$'),
          native_order_version text NOT NULL,
          native_line_version text NOT NULL,
          supply_sha256 text NOT NULL CHECK (supply_sha256 ~ '^[0-9a-f]{64}$'),
          legacy_revision bigint NOT NULL CHECK (legacy_revision > 0),
          recorded_at timestamptz NOT NULL DEFAULT clock_timestamp(),
          PRIMARY KEY (tenant_id, order_id, line_id, order_version, schedule_id),
          UNIQUE (tenant_id, operation_key),
          FOREIGN KEY (tenant_id, order_id, legacy_revision)
              REFERENCES cp6_sales_order_source_v1.legacy_originals (tenant_id, order_id, revision)
        );

        CREATE TABLE cp6_sales_order_source_v1.operations (
          tenant_id uuid NOT NULL,
          producer text COLLATE "C" NOT NULL,
          operation_key text COLLATE "C" NOT NULL,
          actor_id uuid NOT NULL,
          order_id uuid NOT NULL,
          line_id uuid NOT NULL,
          order_version text COLLATE "C" NOT NULL,
          schedule_id text COLLATE "C" NOT NULL,
          lineage_id text COLLATE "C" NOT NULL,
          source_version bigint NOT NULL CHECK (source_version > 0),
          source_digest text NOT NULL CHECK (source_digest ~ '^[0-9a-f]{64}$'),
          body_sha256 text NOT NULL CHECK (body_sha256 ~ '^[0-9a-f]{64}$'),
          body bytea NOT NULL,
          receipt bytea NOT NULL,
          recorded_at timestamptz NOT NULL DEFAULT clock_timestamp(),
          PRIMARY KEY (tenant_id, producer, operation_key),
          UNIQUE (tenant_id, lineage_id, source_version),
          FOREIGN KEY (tenant_id, order_id, line_id, order_version, schedule_id)
              REFERENCES cp6_sales_order_source_v1.confirmed_scopes (tenant_id, order_id, line_id, order_version, schedule_id)
        );

        CREATE TABLE cp6_sales_order_source_v1.outbox (
          tenant_id uuid NOT NULL,
          producer text COLLATE "C" NOT NULL,
          operation_key text COLLATE "C" NOT NULL,
          handoff text NOT NULL CHECK (handoff IN ('H10', 'H32')),
          envelope bytea NOT NULL,
          envelope_sha256 text NOT NULL CHECK (envelope_sha256 ~ '^[0-9a-f]{64}$'),
          PRIMARY KEY (tenant_id, producer, operation_key),
          FOREIGN KEY (tenant_id, producer, operation_key)
              REFERENCES cp6_sales_order_source_v1.operations (tenant_id, producer, operation_key)
        );

        CREATE TABLE cp6_sales_order_source_v1.frontiers (
          tenant_id uuid NOT NULL,
          lineage_id text COLLATE "C" NOT NULL,
          source_version bigint NOT NULL,
          PRIMARY KEY (tenant_id, lineage_id),
          FOREIGN KEY (tenant_id, lineage_id, source_version)
              REFERENCES cp6_sales_order_source_v1.operations (tenant_id, lineage_id, source_version)
        );

        CREATE FUNCTION cp6_sales_order_source_v1.immutable() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN RAISE EXCEPTION 'SALES_ORDER_SOURCE_IMMUTABLE' USING ERRCODE='23514'; END $$;
        CREATE TRIGGER sales_legacy_originals_immutable BEFORE UPDATE OR DELETE ON cp6_sales_order_source_v1.legacy_originals
          FOR EACH ROW EXECUTE FUNCTION cp6_sales_order_source_v1.immutable();
        CREATE TRIGGER sales_confirmed_scopes_immutable BEFORE UPDATE OR DELETE ON cp6_sales_order_source_v1.confirmed_scopes
          FOR EACH ROW EXECUTE FUNCTION cp6_sales_order_source_v1.immutable();
        CREATE TRIGGER sales_operations_immutable BEFORE UPDATE OR DELETE ON cp6_sales_order_source_v1.operations
          FOR EACH ROW EXECUTE FUNCTION cp6_sales_order_source_v1.immutable();
        CREATE TRIGGER sales_outbox_immutable BEFORE UPDATE OR DELETE ON cp6_sales_order_source_v1.outbox
          FOR EACH ROW EXECUTE FUNCTION cp6_sales_order_source_v1.immutable();
        CREATE TRIGGER sales_legacy_originals_no_truncate BEFORE TRUNCATE ON cp6_sales_order_source_v1.legacy_originals
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_sales_order_source_v1.immutable();
        CREATE TRIGGER sales_confirmed_scopes_no_truncate BEFORE TRUNCATE ON cp6_sales_order_source_v1.confirmed_scopes
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_sales_order_source_v1.immutable();
        CREATE TRIGGER sales_operations_no_truncate BEFORE TRUNCATE ON cp6_sales_order_source_v1.operations
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_sales_order_source_v1.immutable();
        CREATE TRIGGER sales_outbox_no_truncate BEFORE TRUNCATE ON cp6_sales_order_source_v1.outbox
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_sales_order_source_v1.immutable();
        """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Sales 原件使用前滚迁移保留。");
}
