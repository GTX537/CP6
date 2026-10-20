using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261020104000_OrderPriceCorrectionIntentV1")]
public sealed class OrderPriceCorrectionIntentV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE SCHEMA cp6_order_price_v1;
        REVOKE ALL ON SCHEMA cp6_order_price_v1 FROM PUBLIC;
        CREATE TABLE cp6_order_price_v1.workflow_intents (
            id uuid PRIMARY KEY,
            batch_id uuid NOT NULL,
            tenant_id uuid NOT NULL,
            actor_id uuid NOT NULL,
            detail_id uuid NOT NULL,
            created_at_utc timestamptz NOT NULL,
            mapping_version text NOT NULL,
            registry_version text NOT NULL,
            source_version text NOT NULL,
            payload jsonb NOT NULL,
            delivery_state text NOT NULL DEFAULT 'AwaitingAdapter' CHECK (delivery_state='AwaitingAdapter'),
            UNIQUE (batch_id,detail_id),
            CHECK (tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid),
            CHECK (actor_id <> '00000000-0000-0000-0000-000000000000'::uuid)
        );
        CREATE INDEX workflow_intents_tenant_batch ON cp6_order_price_v1.workflow_intents(tenant_id,batch_id);
        REVOKE ALL ON cp6_order_price_v1.workflow_intents FROM PUBLIC;
        """);

    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("Persisted price correction intents require forward recovery.");
}
