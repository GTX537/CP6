using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context))]
[Migration("20261023003000_MainStockOutboxV1")]
public sealed class MainStockOutboxV1 : Migration
{
    // Frozen from the adopted Platform 0.10.3 pg-xmin-v1 model/guard assets. Runtime upgrades
    // cannot silently change this historical migration. Existing C02/ERP tables are untouched.
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DO $EF$
        BEGIN
            IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'cp6_stock_outbox_v1') THEN
                CREATE SCHEMA cp6_stock_outbox_v1;
            END IF;
        END $EF$;


        CREATE TABLE cp6_stock_outbox_v1."Authority" (
            "RootId" text NOT NULL,
            "TenantId" uuid NOT NULL,
            "Enabled" boolean NOT NULL,
            "PolicyVersion" text NOT NULL,
            "PublisherReference" text NOT NULL,
            CONSTRAINT "PK_Authority" PRIMARY KEY ("RootId")
        );


        CREATE TABLE cp6_stock_outbox_v1."Cp6_DeadLetterRecord" (
            "Id" uuid NOT NULL,
            "Direction" integer NOT NULL,
            "TenantId" uuid NOT NULL,
            "MessageId" character varying(128) NOT NULL,
            "ConsumerName" character varying(128),
            "PayloadSha256" character varying(64) NOT NULL,
            "ErrorCode" character varying(128) NOT NULL,
            "SupportReference" character varying(128),
            "CreatedAtUtc" timestamp with time zone NOT NULL,
            "ReplayedAtUtc" timestamp with time zone,
            "ReplayReasonCode" character varying(128),
            CONSTRAINT "PK_Cp6_DeadLetterRecord" PRIMARY KEY ("Id")
        );


        CREATE TABLE cp6_stock_outbox_v1."Cp6_OutboxAuditV2" (
            "Id" uuid NOT NULL,
            "RootId" text NOT NULL,
            "NativeId" uuid,
            "Kind" text NOT NULL,
            "Evidence" text NOT NULL,
            "AtUtc" timestamp with time zone NOT NULL,
            CONSTRAINT "PK_Cp6_OutboxAuditV2" PRIMARY KEY ("Id")
        );


        CREATE TABLE cp6_stock_outbox_v1."Cp6_OutboxClaimReceiptV2" (
            "RootId" text NOT NULL,
            "RequestId" uuid NOT NULL,
            "Input" text NOT NULL,
            "Tickets" text NOT NULL,
            "WriterTransaction" text NOT NULL,
            CONSTRAINT "PK_Cp6_OutboxClaimReceiptV2" PRIMARY KEY ("RootId", "RequestId")
        );


        CREATE TABLE cp6_stock_outbox_v1."Cp6_OutboxMessage" (
            "Id" uuid NOT NULL,
            "MessageId" character varying(128) NOT NULL,
            "TenantId" uuid NOT NULL,
            "TopicName" character varying(249) NOT NULL,
            "PartitionKey" character varying(512) NOT NULL,
            "Payload" bytea NOT NULL,
            "PayloadSha256" character varying(64) NOT NULL,
            "CorrelationId" character varying(128) NOT NULL,
            "CausationId" character varying(128) NOT NULL,
            "AggregateId" character varying(128) NOT NULL,
            "AggregateVersion" integer NOT NULL,
            "Status" integer NOT NULL,
            "AttemptCount" integer NOT NULL,
            "CreatedAtUtc" timestamp with time zone NOT NULL,
            "AvailableAtUtc" timestamp with time zone NOT NULL,
            "LeaseOwner" character varying(128),
            "LeaseToken" character varying(32),
            "LeaseExpiresAtUtc" timestamp with time zone,
            "PublishedAtUtc" timestamp with time zone,
            "DeadLetteredAtUtc" timestamp with time zone,
            "LastErrorCode" character varying(128),
            "SupportReference" character varying(128),
            CONSTRAINT "PK_Cp6_OutboxMessage" PRIMARY KEY ("Id")
        );


        CREATE TABLE cp6_stock_outbox_v1."Cp6_OutboxMutationReceiptV2" (
            "RootId" text NOT NULL,
            "MutationId" uuid NOT NULL,
            "NativeId" uuid NOT NULL,
            "Input" text NOT NULL,
            "Output" text NOT NULL,
            "WriterTransaction" text NOT NULL,
            CONSTRAINT "PK_Cp6_OutboxMutationReceiptV2" PRIMARY KEY ("RootId", "MutationId")
        );


        CREATE TABLE cp6_stock_outbox_v1."Cp6_OutboxRouteV2" (
            "Id" text NOT NULL,
            "OwnerId" text NOT NULL,
            "TenantId" uuid NOT NULL,
            "QueueDomainId" text NOT NULL,
            "RouteId" text NOT NULL,
            "ProfileId" text NOT NULL,
            "ProfileVersion" integer NOT NULL,
            "CurrentEpoch" bigint NOT NULL,
            "Phase" integer NOT NULL,
            "LockKey" bigint NOT NULL,
            "PublisherSetReference" text NOT NULL,
            "LastSwitchId" uuid,
            CONSTRAINT "PK_Cp6_OutboxRouteV2" PRIMARY KEY ("Id")
        );


        CREATE TABLE cp6_stock_outbox_v1."Cp6_OutboxSendAttemptV2" (
            "RootId" text NOT NULL,
            "ClaimId" uuid NOT NULL,
            "MutationId" uuid NOT NULL,
            "OriginalTicket" text NOT NULL,
            "PolicyVersion" text NOT NULL,
            "WriterTransaction" text NOT NULL,
            CONSTRAINT "PK_Cp6_OutboxSendAttemptV2" PRIMARY KEY ("RootId", "ClaimId")
        );


        CREATE TABLE cp6_stock_outbox_v1."Cp6_OutboxTokenSealV2" (
            "NativeId" uuid NOT NULL,
            "WriterTransaction" text NOT NULL,
            CONSTRAINT "PK_Cp6_OutboxTokenSealV2" PRIMARY KEY ("NativeId", "WriterTransaction")
        );


        CREATE TABLE cp6_stock_outbox_v1."Cp6_OutboxClaimTicketV2" (
            "RootId" text NOT NULL,
            "RequestId" uuid NOT NULL,
            "NativeId" uuid NOT NULL,
            "ReceiptReference" uuid NOT NULL,
            "Ticket" text NOT NULL,
            "WriterTransaction" text NOT NULL,
            CONSTRAINT "PK_Cp6_OutboxClaimTicketV2" PRIMARY KEY ("RootId", "RequestId", "NativeId"),
            CONSTRAINT "FK_Cp6_OutboxClaimTicketV2_Cp6_OutboxClaimReceiptV2_RootId_Req~" FOREIGN KEY ("RootId", "RequestId") REFERENCES cp6_stock_outbox_v1."Cp6_OutboxClaimReceiptV2" ("RootId", "RequestId") ON DELETE RESTRICT
        );


        CREATE TABLE cp6_stock_outbox_v1."Source" (
            "NativeId" uuid NOT NULL,
            "TenantId" uuid NOT NULL,
            "OperationId" uuid NOT NULL,
            "SourceOwner" text NOT NULL,
            "ResultSha256" text NOT NULL,
            CONSTRAINT "PK_Source" PRIMARY KEY ("NativeId"),
            CONSTRAINT "FK_Source_Cp6_OutboxMessage_NativeId" FOREIGN KEY ("NativeId") REFERENCES cp6_stock_outbox_v1."Cp6_OutboxMessage" ("Id") ON DELETE RESTRICT
        );


        CREATE TABLE cp6_stock_outbox_v1."Cp6_OutboxBindingV2" (
            "NativeId" uuid NOT NULL,
            "RootId" text NOT NULL,
            "MessageId" text NOT NULL,
            "RawEnvelopeSha256" text NOT NULL,
            "AssignedEpoch" bigint NOT NULL,
            "CurrentClaimId" uuid,
            "ClaimEpoch" bigint,
            "ClaimStartedAtUtc" timestamp with time zone,
            CONSTRAINT "PK_Cp6_OutboxBindingV2" PRIMARY KEY ("NativeId"),
            CONSTRAINT "FK_Cp6_OutboxBindingV2_Cp6_OutboxMessage_NativeId" FOREIGN KEY ("NativeId") REFERENCES cp6_stock_outbox_v1."Cp6_OutboxMessage" ("Id") ON DELETE RESTRICT,
            CONSTRAINT "FK_Cp6_OutboxBindingV2_Cp6_OutboxRouteV2_RootId" FOREIGN KEY ("RootId") REFERENCES cp6_stock_outbox_v1."Cp6_OutboxRouteV2" ("Id") ON DELETE RESTRICT
        );


        CREATE UNIQUE INDEX "IX_Authority_TenantId" ON cp6_stock_outbox_v1."Authority" ("TenantId");


        CREATE INDEX "IX_Cp6_DeadLetterRecord_CreatedAtUtc" ON cp6_stock_outbox_v1."Cp6_DeadLetterRecord" ("CreatedAtUtc");


        CREATE INDEX "IX_Cp6_DeadLetterRecord_Direction_MessageId_ConsumerName" ON cp6_stock_outbox_v1."Cp6_DeadLetterRecord" ("Direction", "MessageId", "ConsumerName");


        CREATE UNIQUE INDEX "IX_Cp6_OutboxBindingV2_MessageId" ON cp6_stock_outbox_v1."Cp6_OutboxBindingV2" ("MessageId");


        CREATE INDEX "IX_Cp6_OutboxBindingV2_RootId_AssignedEpoch" ON cp6_stock_outbox_v1."Cp6_OutboxBindingV2" ("RootId", "AssignedEpoch");


        CREATE UNIQUE INDEX "IX_Cp6_OutboxClaimTicketV2_ReceiptReference" ON cp6_stock_outbox_v1."Cp6_OutboxClaimTicketV2" ("ReceiptReference");


        CREATE UNIQUE INDEX "IX_Cp6_OutboxMessage_MessageId" ON cp6_stock_outbox_v1."Cp6_OutboxMessage" ("MessageId");


        CREATE INDEX "IX_Cp6_OutboxMessage_Status_AvailableAtUtc_LeaseExpiresAtUtc" ON cp6_stock_outbox_v1."Cp6_OutboxMessage" ("Status", "AvailableAtUtc", "LeaseExpiresAtUtc");


        CREATE INDEX "IX_Cp6_OutboxMessage_TenantId_AggregateId_AggregateVersion" ON cp6_stock_outbox_v1."Cp6_OutboxMessage" ("TenantId", "AggregateId", "AggregateVersion");


        CREATE UNIQUE INDEX "IX_Cp6_OutboxRouteV2_LockKey" ON cp6_stock_outbox_v1."Cp6_OutboxRouteV2" ("LockKey");


        CREATE UNIQUE INDEX "IX_Cp6_OutboxRouteV2_OwnerId_TenantId_QueueDomainId_RouteId" ON cp6_stock_outbox_v1."Cp6_OutboxRouteV2" ("OwnerId", "TenantId", "QueueDomainId", "RouteId");


        CREATE UNIQUE INDEX "IX_Cp6_OutboxSendAttemptV2_RootId_MutationId" ON cp6_stock_outbox_v1."Cp6_OutboxSendAttemptV2" ("RootId", "MutationId");


        CREATE UNIQUE INDEX "IX_Source_TenantId_OperationId" ON cp6_stock_outbox_v1."Source" ("TenantId", "OperationId");



        CREATE FUNCTION "cp6_stock_outbox_v1"."cp6_outbox_v2_guard"() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $guard$DECLARE nid uuid; rid text; lk bigint;
        BEGIN
          IF TG_TABLE_NAME = 'Cp6_OutboxMessage' THEN nid := OLD."Id";
          ELSIF TG_TABLE_NAME = 'Cp6_OutboxBindingV2' THEN nid := OLD."NativeId";
          ELSE
            SELECT "NativeId" INTO nid FROM "cp6_stock_outbox_v1"."Cp6_OutboxBindingV2" WHERE "MessageId"=OLD."MessageId";
          END IF;
          SELECT b."RootId", r."LockKey" INTO rid, lk FROM "cp6_stock_outbox_v1"."Cp6_OutboxBindingV2" b
            JOIN "cp6_stock_outbox_v1"."Cp6_OutboxRouteV2" r ON r."Id"=b."RootId" WHERE b."NativeId"=nid;
          IF rid IS NOT NULL THEN
            IF current_setting('cp6.outbox_v2_writer', true) IS DISTINCT FROM pg_current_xact_id()::text || ':' || rid
              OR NOT EXISTS (SELECT 1 FROM pg_locks WHERE locktype='advisory' AND pid=pg_backend_pid()
                AND database=(SELECT oid FROM pg_database WHERE datname=current_database())
                AND classid::bigint=((lk >> 32) & 4294967295) AND objid::bigint=(lk & 4294967295)
                AND objsubid=1 AND mode='ExclusiveLock' AND granted) THEN
              RAISE EXCEPTION 'CP6_OUTBOX_V2_LEGACY_BYPASS' USING ERRCODE='55000';
            END IF;
            IF EXISTS (SELECT 1 FROM "cp6_stock_outbox_v1"."Cp6_OutboxTokenSealV2" WHERE "NativeId"=nid AND "WriterTransaction"=pg_current_xact_id()::text) THEN
              RAISE EXCEPTION 'CP6_OUTBOX_TOKEN_ALREADY_RECORDED' USING ERRCODE='55000';
            END IF;
          END IF;
          IF TG_OP='DELETE' THEN RETURN OLD; ELSE RETURN NEW; END IF;
        END$guard$;
        CREATE TRIGGER cp6_outbox_v2_guard BEFORE UPDATE OR DELETE ON "cp6_stock_outbox_v1"."Cp6_OutboxMessage" FOR EACH ROW EXECUTE FUNCTION "cp6_stock_outbox_v1"."cp6_outbox_v2_guard"();
        CREATE TRIGGER cp6_outbox_v2_guard BEFORE UPDATE OR DELETE ON "cp6_stock_outbox_v1"."Cp6_OutboxBindingV2" FOR EACH ROW EXECUTE FUNCTION "cp6_stock_outbox_v1"."cp6_outbox_v2_guard"();
        CREATE TRIGGER cp6_outbox_v2_guard BEFORE UPDATE OR DELETE ON "cp6_stock_outbox_v1"."Cp6_DeadLetterRecord" FOR EACH ROW EXECUTE FUNCTION "cp6_stock_outbox_v1"."cp6_outbox_v2_guard"();
        CREATE FUNCTION "cp6_stock_outbox_v1"."cp6_outbox_v2_immutable"() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $guard$BEGIN RAISE EXCEPTION 'CP6_OUTBOX_IMMUTABLE_EVIDENCE' USING ERRCODE='55000'; END$guard$;
        CREATE TRIGGER cp6_outbox_v2_immutable BEFORE UPDATE OR DELETE ON "cp6_stock_outbox_v1"."Cp6_OutboxClaimReceiptV2" FOR EACH ROW EXECUTE FUNCTION "cp6_stock_outbox_v1"."cp6_outbox_v2_immutable"();
        CREATE TRIGGER cp6_outbox_v2_immutable BEFORE UPDATE OR DELETE ON "cp6_stock_outbox_v1"."Cp6_OutboxClaimTicketV2" FOR EACH ROW EXECUTE FUNCTION "cp6_stock_outbox_v1"."cp6_outbox_v2_immutable"();
        CREATE TRIGGER cp6_outbox_v2_immutable BEFORE UPDATE OR DELETE ON "cp6_stock_outbox_v1"."Cp6_OutboxMutationReceiptV2" FOR EACH ROW EXECUTE FUNCTION "cp6_stock_outbox_v1"."cp6_outbox_v2_immutable"();
        CREATE TRIGGER cp6_outbox_v2_immutable BEFORE UPDATE OR DELETE ON "cp6_stock_outbox_v1"."Cp6_OutboxAuditV2" FOR EACH ROW EXECUTE FUNCTION "cp6_stock_outbox_v1"."cp6_outbox_v2_immutable"();
        CREATE TRIGGER cp6_outbox_v2_immutable BEFORE UPDATE OR DELETE ON "cp6_stock_outbox_v1"."Cp6_OutboxTokenSealV2" FOR EACH ROW EXECUTE FUNCTION "cp6_stock_outbox_v1"."cp6_outbox_v2_immutable"();
        CREATE TRIGGER cp6_outbox_v2_immutable BEFORE UPDATE OR DELETE ON "cp6_stock_outbox_v1"."Cp6_OutboxSendAttemptV2" FOR EACH ROW EXECUTE FUNCTION "cp6_stock_outbox_v1"."cp6_outbox_v2_immutable"();


        REVOKE ALL ON SCHEMA cp6_stock_outbox_v1 FROM PUBLIC;
        ALTER TABLE cp6_stock_outbox_v1."Authority" ADD CONSTRAINT stock_authority_tenant
          FOREIGN KEY ("TenantId") REFERENCES public."Sys_Tenants"("Id") ON DELETE RESTRICT;
        ALTER TABLE cp6_stock_outbox_v1."Source" ADD CONSTRAINT stock_source_original
          FOREIGN KEY ("TenantId","SourceOwner","OperationId")
          REFERENCES cp6_main_owner_v1.operations(tenant_id,owner,operation_id) ON DELETE RESTRICT;
        ALTER TABLE cp6_stock_outbox_v1."Source" ADD CHECK("SourceOwner"='Main.Wms.StockMovement');
        CREATE TABLE cp6_stock_outbox_v1."DispatchRun" (
          "RootId" text NOT NULL REFERENCES cp6_stock_outbox_v1."Cp6_OutboxRouteV2"("Id") ON DELETE RESTRICT,
          "RequestId" uuid NOT NULL CHECK("RequestId"<>'00000000-0000-0000-0000-000000000000'::uuid),
          "CreatedAtUtc" timestamptz NOT NULL, "CompletedAtUtc" timestamptz,
          PRIMARY KEY("RootId","RequestId"));
        CREATE UNIQUE INDEX "IX_DispatchRun_RootId" ON cp6_stock_outbox_v1."DispatchRun"("RootId") WHERE "CompletedAtUtc" IS NULL;

        -- This queue only adopts newly committed keyed stock originals. The deferred check observes
        -- source, native envelope and binding at commit, regardless of EF INSERT order.
        CREATE FUNCTION cp6_stock_outbox_v1.source_proof() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF NOT EXISTS (
            SELECT 1 FROM cp6_stock_outbox_v1."Source" s
            JOIN cp6_main_owner_v1.operations o ON (o.tenant_id,o.owner,o.operation_id)=(s."TenantId",s."SourceOwner",s."OperationId")
            JOIN cp6_stock_outbox_v1."Cp6_OutboxBindingV2" b ON b."NativeId"=s."NativeId"
            JOIN cp6_stock_outbox_v1."Cp6_OutboxRouteV2" r ON r."Id"=b."RootId"
            JOIN cp6_stock_outbox_v1."Authority" a ON a."RootId"=r."Id"
            WHERE s."NativeId"=NEW."Id" AND s."TenantId"=NEW."TenantId"
              AND o.state='COMMITTED' AND o.contract='Main.Wms.StockOperation.v1'
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
        CREATE CONSTRAINT TRIGGER stock_source_proof AFTER INSERT ON cp6_stock_outbox_v1."Cp6_OutboxMessage"
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_stock_outbox_v1.source_proof();

        CREATE FUNCTION cp6_stock_outbox_v1.envelope_immutable() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF (NEW."Id",NEW."MessageId",NEW."TenantId",NEW."TopicName",NEW."PartitionKey",NEW."Payload",NEW."PayloadSha256",
              NEW."CorrelationId",NEW."CausationId",NEW."AggregateId",NEW."AggregateVersion",NEW."CreatedAtUtc")
            IS DISTINCT FROM
             (OLD."Id",OLD."MessageId",OLD."TenantId",OLD."TopicName",OLD."PartitionKey",OLD."Payload",OLD."PayloadSha256",
              OLD."CorrelationId",OLD."CausationId",OLD."AggregateId",OLD."AggregateVersion",OLD."CreatedAtUtc") THEN
            RAISE EXCEPTION 'STOCK_OUTBOX_ENVELOPE_IMMUTABLE' USING ERRCODE='23514';
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER stock_envelope_immutable BEFORE UPDATE ON cp6_stock_outbox_v1."Cp6_OutboxMessage"
          FOR EACH ROW EXECUTE FUNCTION cp6_stock_outbox_v1.envelope_immutable();
        CREATE TRIGGER stock_source_immutable BEFORE UPDATE OR DELETE ON cp6_stock_outbox_v1."Source"
          FOR EACH ROW EXECUTE FUNCTION cp6_stock_outbox_v1.cp6_outbox_v2_immutable();
        CREATE TRIGGER stock_route_immutable BEFORE UPDATE ON cp6_stock_outbox_v1."Cp6_OutboxRouteV2"
          FOR EACH ROW EXECUTE FUNCTION cp6_stock_outbox_v1.cp6_outbox_v2_immutable();
        CREATE FUNCTION cp6_stock_outbox_v1.binding_identity_immutable() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF (NEW."NativeId",NEW."RootId",NEW."MessageId",NEW."RawEnvelopeSha256",NEW."AssignedEpoch")
            IS DISTINCT FROM (OLD."NativeId",OLD."RootId",OLD."MessageId",OLD."RawEnvelopeSha256",OLD."AssignedEpoch") THEN
            RAISE EXCEPTION 'STOCK_OUTBOX_BINDING_IMMUTABLE' USING ERRCODE='23514';
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER stock_binding_identity BEFORE UPDATE ON cp6_stock_outbox_v1."Cp6_OutboxBindingV2"
          FOR EACH ROW EXECUTE FUNCTION cp6_stock_outbox_v1.binding_identity_immutable();
        CREATE FUNCTION cp6_stock_outbox_v1.authority_identity_immutable() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF (NEW."RootId",NEW."TenantId",NEW."PolicyVersion",NEW."PublisherReference")
            IS DISTINCT FROM (OLD."RootId",OLD."TenantId",OLD."PolicyVersion",OLD."PublisherReference") THEN
            RAISE EXCEPTION 'STOCK_OUTBOX_AUTHORITY_IDENTITY_IMMUTABLE' USING ERRCODE='23514';
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER stock_authority_identity BEFORE UPDATE ON cp6_stock_outbox_v1."Authority"
          FOR EACH ROW EXECUTE FUNCTION cp6_stock_outbox_v1.authority_identity_immutable();

        CREATE FUNCTION cp6_stock_outbox_v1.run_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE lk bigint;
        BEGIN
          SELECT "LockKey" INTO lk FROM cp6_stock_outbox_v1."Cp6_OutboxRouteV2" WHERE "Id"=NEW."RootId";
          IF lk IS NULL OR NOT EXISTS(SELECT 1 FROM pg_locks WHERE locktype='advisory' AND pid=pg_backend_pid()
            AND database=(SELECT oid FROM pg_database WHERE datname=current_database())
            AND classid::bigint=((lk >> 32) & 4294967295) AND objid::bigint=(lk & 4294967295)
            AND objsubid=1 AND mode='ExclusiveLock' AND granted) THEN
            RAISE EXCEPTION 'STOCK_OUTBOX_RUN_FENCE_REQUIRED' USING ERRCODE='23514';
          END IF;
          IF TG_OP='INSERT' AND NEW."CompletedAtUtc" IS NOT NULL THEN
            RAISE EXCEPTION 'STOCK_OUTBOX_RUN_ORIGINAL_REQUIRED' USING ERRCODE='23514';
          END IF;
          IF TG_OP='UPDATE' THEN
            IF (NEW."RootId",NEW."RequestId",NEW."CreatedAtUtc") IS DISTINCT FROM (OLD."RootId",OLD."RequestId",OLD."CreatedAtUtc")
              OR OLD."CompletedAtUtc" IS NOT NULL OR NEW."CompletedAtUtc" IS NULL
              OR NOT EXISTS(SELECT 1 FROM cp6_stock_outbox_v1."Cp6_OutboxClaimReceiptV2" c
                WHERE c."RootId"=OLD."RootId" AND c."RequestId"=OLD."RequestId")
              OR EXISTS(SELECT 1 FROM cp6_stock_outbox_v1."Cp6_OutboxClaimTicketV2" t
                WHERE t."RootId"=OLD."RootId" AND t."RequestId"=OLD."RequestId"
                AND NOT EXISTS(SELECT 1 FROM cp6_stock_outbox_v1."Cp6_OutboxMutationReceiptV2" m
                  WHERE m."RootId"=t."RootId" AND m."MutationId"=t."ReceiptReference" AND m."NativeId"=t."NativeId"
                  AND m."Output"::jsonb->>'Transition' IN ('Published','RetryScheduled','DeadLettered'))) THEN
              RAISE EXCEPTION 'STOCK_OUTBOX_RUN_UNRESOLVED' USING ERRCODE='23514';
            END IF;
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER stock_run_guard BEFORE INSERT OR UPDATE ON cp6_stock_outbox_v1."DispatchRun"
          FOR EACH ROW EXECUTE FUNCTION cp6_stock_outbox_v1.run_guard();

        -- No retention or epoch handover has been adopted in this initial Main route.
        DO $$ DECLARE t text; BEGIN
          FOREACH t IN ARRAY ARRAY['Authority','Source','DispatchRun','Cp6_OutboxMessage','Cp6_OutboxBindingV2',
            'Cp6_OutboxRouteV2','Cp6_DeadLetterRecord','Cp6_OutboxClaimReceiptV2','Cp6_OutboxClaimTicketV2',
            'Cp6_OutboxMutationReceiptV2','Cp6_OutboxAuditV2','Cp6_OutboxTokenSealV2','Cp6_OutboxSendAttemptV2'] LOOP
            EXECUTE format('CREATE TRIGGER stock_no_truncate BEFORE TRUNCATE ON cp6_stock_outbox_v1.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_stock_outbox_v1.cp6_outbox_v2_immutable()',t);
            EXECUTE format('CREATE TRIGGER stock_no_delete BEFORE DELETE ON cp6_stock_outbox_v1.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_stock_outbox_v1.cp6_outbox_v2_immutable()',t);
          END LOOP;
        END $$;
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_stock_outbox_v1 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_stock_outbox_v1 FROM PUBLIC;
        """);
    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("Durable notification originals and unknown delivery evidence require forward recovery.");
}
