using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context))]
[Migration("20261026001000_MainStockCreationOutboxV2")]
public sealed class MainStockCreationOutboxV2 : Migration
{
    // Explicit v2 source adoption. Keep the original function ACL and every native commit-time proof.
    // Historical v1 migration/body and already committed originals remain unchanged.
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE OR REPLACE FUNCTION cp6_stock_outbox_v1.source_proof() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF NOT EXISTS (
            SELECT 1 FROM cp6_stock_outbox_v1."Source" s
            JOIN cp6_main_owner_v1.operations o ON (o.tenant_id,o.owner,o.operation_id)=(s."TenantId",s."SourceOwner",s."OperationId")
            JOIN cp6_stock_outbox_v1."Cp6_OutboxBindingV2" b ON b."NativeId"=s."NativeId"
            JOIN cp6_stock_outbox_v1."Cp6_OutboxRouteV2" r ON r."Id"=b."RootId"
            JOIN cp6_stock_outbox_v1."Authority" a ON a."RootId"=r."Id"
            WHERE s."NativeId"=NEW."Id" AND s."TenantId"=NEW."TenantId"
              AND o.state='COMMITTED' AND o.contract IN ('Main.Wms.StockOperation.v1','Main.Wms.StockOperation.v2')
              AND o.xmin=pg_current_xact_id()::text::xid
              AND s."ResultSha256"=encode(sha256(o.result_bytes),'hex')
              AND NEW."MessageId"='stock-hint:'||replace(s."TenantId"::text,'-','')||':'||replace(s."OperationId"::text,'-','')
              AND NEW."TopicName"='main.inventory.invalidated.v1' AND NEW."PartitionKey"=s."TenantId"::text
              AND NEW."Payload"=convert_to('{}','UTF8') AND NEW."PayloadSha256"=encode(sha256(NEW."Payload"),'hex')
              AND NEW."AggregateId"=s."OperationId"::text AND NEW."AggregateVersion"=1
              AND NEW."CorrelationId"=NEW."AggregateId" AND NEW."CausationId"=NEW."AggregateId"
              AND NEW."Status"=0 AND NEW."AttemptCount"=0 AND NEW."LeaseOwner" IS NULL AND NEW."LeaseToken" IS NULL
              AND NEW."LeaseExpiresAtUtc" IS NULL AND NEW."PublishedAtUtc" IS NULL AND NEW."DeadLetteredAtUtc" IS NULL
              AND b."MessageId"=NEW."MessageId" AND b."RawEnvelopeSha256"=NEW."PayloadSha256"
              AND b."AssignedEpoch"=1 AND b."CurrentClaimId" IS NULL AND b."ClaimEpoch" IS NULL
              AND r."OwnerId"='Main.Wms.InventoryNotification' AND r."TenantId"=s."TenantId"
              AND r."QueueDomainId"='Main.Stock.Notification.v1' AND r."RouteId"='InventoryInvalidatedV1'
              AND r."ProfileId"='cp6-outbox-lease-v2' AND r."ProfileVersion"=1 AND r."CurrentEpoch"=1 AND r."Phase"=0
              AND r."PublisherSetReference"='Main.SignalR.InventoryInvalidated.v1'
              AND a."TenantId"=s."TenantId" AND a."Enabled" AND a."PolicyVersion"='Main.StockOutbox.Technical.v1'
              AND a."PublisherReference"=r."PublisherSetReference") THEN
            RAISE EXCEPTION 'STOCK_OUTBOX_SOURCE_PROOF_REQUIRED' USING ERRCODE='23514';
          END IF;
          RETURN NEW;
        END $$;
        """);
    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("Stock original source contracts require forward recovery.");
}
