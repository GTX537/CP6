using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context))]
[Migration("20261113073000_MainStockOutboxTerminalEpochV1")]
public sealed class MainStockOutboxTerminalEpochV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE FUNCTION cp6_stock_outbox_v1.route_epoch_guard() RETURNS trigger
        LANGUAGE plpgsql SET search_path=pg_catalog AS $epoch$
        BEGIN
          IF (NEW."Id",NEW."OwnerId",NEW."TenantId",NEW."QueueDomainId",NEW."RouteId",NEW."ProfileId",
                NEW."ProfileVersion",NEW."LockKey",NEW."PublisherSetReference")
              IS DISTINCT FROM (OLD."Id",OLD."OwnerId",OLD."TenantId",OLD."QueueDomainId",OLD."RouteId",OLD."ProfileId",
                OLD."ProfileVersion",OLD."LockKey",OLD."PublisherSetReference")
            OR OLD."CurrentEpoch"<1 OR NEW."LastSwitchId" IS NULL
            OR NOT EXISTS(SELECT FROM pg_locks WHERE locktype='advisory' AND pid=pg_backend_pid()
                AND database=(SELECT oid FROM pg_database WHERE datname=current_database())
                AND classid::bigint=((OLD."LockKey" >> 32) & 4294967295)
                AND objid::bigint=(OLD."LockKey" & 4294967295)
                AND objsubid=1 AND mode='ExclusiveLock' AND granted)
            OR NOT (
              (OLD."Phase"=0 AND NEW."Phase"=1 AND NEW."CurrentEpoch"=OLD."CurrentEpoch"
                AND NEW."LastSwitchId" IS DISTINCT FROM OLD."LastSwitchId")
              OR (OLD."Phase"=1 AND NEW."Phase"=0
                AND NEW."CurrentEpoch"::numeric=OLD."CurrentEpoch"::numeric+1
                AND NEW."LastSwitchId"=OLD."LastSwitchId"
                AND current_setting('cp6.outbox_v2_writer',true)=pg_current_xact_id()::text||':'||OLD."Id")
            ) IS TRUE THEN
            RAISE EXCEPTION 'STOCK_OUTBOX_ROUTE_TRANSITION_REJECTED' USING ERRCODE='55000';
          END IF;
          RETURN NEW;
        END
        $epoch$;
        REVOKE ALL ON FUNCTION cp6_stock_outbox_v1.route_epoch_guard() FROM PUBLIC;
        DROP TRIGGER stock_route_immutable ON cp6_stock_outbox_v1."Cp6_OutboxRouteV2";
        CREATE TRIGGER stock_route_epoch_guard BEFORE UPDATE ON cp6_stock_outbox_v1."Cp6_OutboxRouteV2"
          FOR EACH ROW EXECUTE FUNCTION cp6_stock_outbox_v1.route_epoch_guard();

        CREATE FUNCTION cp6_stock_outbox_v1.route_epoch_receipt() RETURNS trigger
        LANGUAGE plpgsql SET search_path=pg_catalog AS $epoch$
        BEGIN
          -- The package writes its immutable receipt after the route UPDATE. The database
          -- accepts the transition only if that original exists in the very same commit.
          IF NOT EXISTS(SELECT FROM cp6_stock_outbox_v1."Cp6_OutboxMutationReceiptV2" m
            JOIN cp6_stock_outbox_v1."Cp6_OutboxAuditV2" a ON a."RootId"=m."RootId"
              AND a."NativeId" IS NULL AND a."Evidence"=m."MutationId"::text
              AND a."Kind"=CASE NEW."Phase" WHEN 1 THEN 'Draining' ELSE 'EpochAdvanced' END
              AND a.xmin=pg_current_xact_id()::text::xid
            WHERE m."RootId"=NEW."Id" AND m."NativeId"='00000000-0000-0000-0000-000000000000'::uuid
              AND m."WriterTransaction"=pg_current_xact_id()::text AND m.xmin=pg_current_xact_id()::text::xid
              AND m."Input"::jsonb->>'switchId'=NEW."LastSwitchId"::text
              AND m."Input"::jsonb->>'expectedEpoch'=OLD."CurrentEpoch"::text
              AND m."Input"::jsonb->>'expectedControlVersion'='pg-xmin-v1:'||OLD.xmin::text
              AND m."Output"::jsonb->>'Epoch'=NEW."CurrentEpoch"::text
              AND m."Output"::jsonb->>'ControlVersion'='pg-xmin-v1:'||NEW.xmin::text
              AND ((NEW."Phase"=1 AND m."Input"::jsonb->>'Kind'='BeginDrain' AND m."MutationId"=NEW."LastSwitchId")
                OR (NEW."Phase"=0 AND m."Input"::jsonb->>'Kind'='AdvanceEpoch'
                    AND m."Input"::jsonb->>'mutationId'=m."MutationId"::text))) THEN
            RAISE EXCEPTION 'STOCK_OUTBOX_ROUTE_RECEIPT_REQUIRED' USING ERRCODE='23514';
          END IF;
          RETURN NEW;
        END
        $epoch$;
        REVOKE ALL ON FUNCTION cp6_stock_outbox_v1.route_epoch_receipt() FROM PUBLIC;
        CREATE CONSTRAINT TRIGGER stock_route_epoch_receipt AFTER UPDATE ON cp6_stock_outbox_v1."Cp6_OutboxRouteV2"
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_stock_outbox_v1.route_epoch_receipt();

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
              AND b."AssignedEpoch"=r."CurrentEpoch" AND b."CurrentClaimId" IS NULL AND b."ClaimEpoch" IS NULL
              AND r."OwnerId"='Main.Wms.InventoryNotification' AND r."TenantId"=s."TenantId"
              AND r."QueueDomainId"='Main.Stock.Notification.v1' AND r."RouteId"='InventoryInvalidatedV1'
              AND r."ProfileId"='cp6-outbox-lease-v2' AND r."ProfileVersion"=1 AND r."CurrentEpoch">0 AND r."Phase"=0
              AND r."PublisherSetReference"='Main.SignalR.InventoryInvalidated.v1'
              AND a."TenantId"=s."TenantId" AND a."Enabled" AND a."PolicyVersion"='Main.StockOutbox.Technical.v1'
              AND a."PublisherReference"=r."PublisherSetReference") THEN
            RAISE EXCEPTION 'STOCK_OUTBOX_SOURCE_PROOF_REQUIRED' USING ERRCODE='23514';
          END IF;
          RETURN NEW;
        END $$;
        """);

    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("Stock route epochs require forward recovery.");
}
