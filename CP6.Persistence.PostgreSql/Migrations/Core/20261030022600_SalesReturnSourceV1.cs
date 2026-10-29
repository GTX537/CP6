using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261030022600_SalesReturnSourceV1")]
public sealed class SalesReturnSourceV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE SCHEMA cp6_sales_return_source_v1;
        REVOKE ALL ON SCHEMA cp6_sales_return_source_v1 FROM PUBLIC;
        CREATE TABLE cp6_sales_return_source_v1.snapshots (
          tenant_id uuid NOT NULL, outbound_id uuid NOT NULL, line_id uuid NOT NULL,
          version text NOT NULL, digest text NOT NULL, body bytea NOT NULL,
          PRIMARY KEY(tenant_id,outbound_id,line_id)
        );
        CREATE FUNCTION cp6_sales_return_source_v1.immutable() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN RAISE EXCEPTION 'SALES_RETURN_SOURCE_IMMUTABLE' USING ERRCODE='23514'; END $$;
        CREATE TRIGGER sales_return_source_immutable BEFORE UPDATE OR DELETE ON cp6_sales_return_source_v1.snapshots
          FOR EACH ROW EXECUTE FUNCTION cp6_sales_return_source_v1.immutable();
        CREATE TRIGGER sales_return_source_no_truncate BEFORE TRUNCATE ON cp6_sales_return_source_v1.snapshots
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_sales_return_source_v1.immutable();
        """);
    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Original commercial shipment facts require forward-only migration.");
}
