using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261030022300_OutboundFulfilmentV1")]
public sealed class OutboundFulfilmentV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE SCHEMA cp6_outbound_fulfilment_v1;
        REVOKE ALL ON SCHEMA cp6_outbound_fulfilment_v1 FROM PUBLIC;
        CREATE TABLE cp6_outbound_fulfilment_v1.snapshots (
          tenant_id uuid NOT NULL, outbound_id uuid NOT NULL, payload jsonb NOT NULL,
          PRIMARY KEY(tenant_id,outbound_id)
        );
        CREATE FUNCTION cp6_outbound_fulfilment_v1.immutable() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN RAISE EXCEPTION 'OUTBOUND_FULFILMENT_IMMUTABLE' USING ERRCODE='23514'; END $$;
        CREATE TRIGGER outbound_fulfilment_immutable BEFORE UPDATE OR DELETE ON cp6_outbound_fulfilment_v1.snapshots
          FOR EACH ROW EXECUTE FUNCTION cp6_outbound_fulfilment_v1.immutable();
        CREATE TRIGGER outbound_fulfilment_no_truncate BEFORE TRUNCATE ON cp6_outbound_fulfilment_v1.snapshots
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_outbound_fulfilment_v1.immutable();
        """);
    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Shipment fulfilment history requires forward-only migration.");
}
