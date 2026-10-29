using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261030022200_OutboundOrderLineageV1")]
public sealed class OutboundOrderLineageV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE SCHEMA cp6_outbound_lineage_v1;
        REVOKE ALL ON SCHEMA cp6_outbound_lineage_v1 FROM PUBLIC;
        CREATE TABLE cp6_outbound_lineage_v1.order_lines (
          tenant_id uuid NOT NULL, outbound_line_id uuid NOT NULL, outbound_no text NOT NULL,
          payload jsonb NOT NULL, PRIMARY KEY(tenant_id,outbound_line_id)
        );
        CREATE INDEX outbound_lineage_document ON cp6_outbound_lineage_v1.order_lines(tenant_id,outbound_no);
        CREATE FUNCTION cp6_outbound_lineage_v1.immutable() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN RAISE EXCEPTION 'OUTBOUND_ORDER_LINEAGE_IMMUTABLE' USING ERRCODE='23514'; END $$;
        CREATE TRIGGER outbound_lineage_immutable BEFORE UPDATE OR DELETE ON cp6_outbound_lineage_v1.order_lines
          FOR EACH ROW EXECUTE FUNCTION cp6_outbound_lineage_v1.immutable();
        CREATE TRIGGER outbound_lineage_no_truncate BEFORE TRUNCATE ON cp6_outbound_lineage_v1.order_lines
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_outbound_lineage_v1.immutable();
        """);
    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Outbound source history requires forward-only migration.");
}
