using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261117011000_FinCenterUseReceiptsV1")]
public sealed class FinCenterUseReceiptsV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE cp6_fin_mfg_v1.center_use_effects (
            tenant_id uuid NOT NULL, ledger_id uuid NOT NULL, id uuid NOT NULL,
            result_kind text NOT NULL CHECK (result_kind IN ('RATE_RELEASE','COST_COLLECTION')),
            result_id uuid NOT NULL, result_revision bigint NOT NULL CHECK (result_revision>0),
            actor_id uuid NOT NULL, business_date date NOT NULL, currency text NOT NULL CHECK (currency ~ '^[A-Z]{3}$'),
            item_count integer NOT NULL CHECK (item_count BETWEEN 1 AND 1000),
            writer_xid xid8 NOT NULL DEFAULT pg_current_xact_id(),
            recorded_at timestamptz NOT NULL DEFAULT clock_timestamp(),
            PRIMARY KEY (tenant_id,ledger_id,id), UNIQUE (tenant_id,id),
            UNIQUE (tenant_id,ledger_id,result_kind,result_id,result_revision),
            FOREIGN KEY (tenant_id,ledger_id) REFERENCES cp6_fin_mfg_v1.ledgers
        );
        CREATE TABLE cp6_fin_mfg_v1.center_use_intents (
            tenant_id uuid NOT NULL, ledger_id uuid NOT NULL, effect_id uuid NOT NULL,
            batch_no integer NOT NULL CHECK (batch_no>0),
            items jsonb NOT NULL CHECK (jsonb_typeof(items)='array' AND jsonb_array_length(items) BETWEEN 1 AND 64),
            PRIMARY KEY (tenant_id,ledger_id,effect_id,batch_no),
            FOREIGN KEY (tenant_id,ledger_id,effect_id) REFERENCES cp6_fin_mfg_v1.center_use_effects
        );
        CREATE TABLE cp6_fin_mfg_v1.center_use_receipts (
            tenant_id uuid NOT NULL, ledger_id uuid NOT NULL, effect_id uuid NOT NULL, batch_no integer NOT NULL,
            adopted jsonb NOT NULL CHECK (jsonb_typeof(adopted)='array' AND jsonb_array_length(adopted) BETWEEN 1 AND 64),
            recorded_at timestamptz NOT NULL DEFAULT clock_timestamp(),
            PRIMARY KEY (tenant_id,ledger_id,effect_id,batch_no),
            FOREIGN KEY (tenant_id,ledger_id,effect_id,batch_no) REFERENCES cp6_fin_mfg_v1.center_use_intents
        );
        ALTER TABLE cp6_fin_mfg_v1.releases
            ADD COLUMN center_use_effect_id uuid,
            ADD COLUMN source_product_id uuid,
            ADD FOREIGN KEY (tenant_id,ledger_id,center_use_effect_id) REFERENCES cp6_fin_mfg_v1.center_use_effects;
        ALTER TABLE cp6_fin_cost_basis_v1.versions
            ADD COLUMN center_use_effect_id uuid,
            ADD FOREIGN KEY (tenant_id,center_use_effect_id) REFERENCES cp6_fin_mfg_v1.center_use_effects (tenant_id,id);

        CREATE FUNCTION cp6_fin_mfg_v1.require_center_use_xact() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM cp6_fin_mfg_v1.center_use_effects e
                WHERE (e.tenant_id,e.ledger_id,e.id)=(NEW.tenant_id,NEW.ledger_id,NEW.effect_id)
                AND NEW.batch_no BETWEEN 1 AND (e.item_count+63)/64
                AND e.writer_xid=pg_current_xact_id_if_assigned()) THEN
                RAISE EXCEPTION 'FINANCE_CENTER_USE_TRANSACTION_REQUIRED' USING ERRCODE='55000';
            END IF;
            RETURN NEW;
        END; $$;
        CREATE TRIGGER center_intent_transaction BEFORE INSERT ON cp6_fin_mfg_v1.center_use_intents
            FOR EACH ROW EXECUTE FUNCTION cp6_fin_mfg_v1.require_center_use_xact();
        CREATE TRIGGER center_receipt_transaction BEFORE INSERT ON cp6_fin_mfg_v1.center_use_receipts
            FOR EACH ROW EXECUTE FUNCTION cp6_fin_mfg_v1.require_center_use_xact();

        CREATE FUNCTION cp6_fin_mfg_v1.require_center_use_result() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE target_kind text; target_id uuid; target_revision bigint; scoped_ledger uuid; requires_use boolean;
        BEGIN
            IF TG_TABLE_NAME='releases' THEN
                target_kind := 'RATE_RELEASE'; target_id := NEW.rate_id; target_revision := NEW.timeline_revision;
                scoped_ledger := NEW.ledger_id;
                SELECT v.submission_id IS NOT NULL INTO requires_use
                FROM cp6_fin_mfg_v1.rate_versions r JOIN cp6_fin_mfg_v1.center_versions v
                    ON (v.tenant_id,v.ledger_id,v.root_id,v.version_id)=(r.tenant_id,r.ledger_id,r.center_id,r.center_version_id)
                WHERE (r.tenant_id,r.ledger_id,r.id)=(NEW.tenant_id,scoped_ledger,target_id);
            ELSE
                target_kind := 'COST_COLLECTION'; target_id := NEW.cost_sheet_id; target_revision := NEW.version;
                scoped_ledger := (NEW.basis->>'LedgerId')::uuid;
                SELECT EXISTS(SELECT 1 FROM jsonb_array_elements(NEW.basis->'Rates') r(item)
                    JOIN cp6_fin_mfg_v1.center_versions v
                        ON (v.tenant_id,v.ledger_id,v.root_id,v.version_id)=(NEW.tenant_id,scoped_ledger,
                            (r.item->>'CostCenterId')::uuid,(r.item->>'CostCenterVersionId')::uuid)
                    WHERE v.submission_id IS NOT NULL) INTO requires_use;
            END IF;
            IF NEW.center_use_effect_id IS NULL THEN
                IF requires_use THEN RAISE EXCEPTION 'FINANCE_CENTER_USE_REQUIRED' USING ERRCODE='55000'; END IF;
            ELSIF NOT EXISTS(SELECT 1 FROM cp6_fin_mfg_v1.center_use_effects e
                WHERE (e.tenant_id,e.ledger_id,e.id)=(NEW.tenant_id,scoped_ledger,NEW.center_use_effect_id)
                AND (e.result_kind,e.result_id,e.result_revision)=(target_kind,target_id,target_revision)
                AND e.writer_xid=pg_current_xact_id_if_assigned()) THEN
                RAISE EXCEPTION 'FINANCE_CENTER_USE_RESULT_MISMATCH' USING ERRCODE='55000';
            END IF;
            RETURN NEW;
        END; $$;
        CREATE TRIGGER rate_release_center_use BEFORE INSERT ON cp6_fin_mfg_v1.releases
            FOR EACH ROW EXECUTE FUNCTION cp6_fin_mfg_v1.require_center_use_result();
        CREATE TRIGGER cost_basis_center_use BEFORE INSERT ON cp6_fin_cost_basis_v1.versions
            FOR EACH ROW EXECUTE FUNCTION cp6_fin_mfg_v1.require_center_use_result();

        CREATE FUNCTION cp6_fin_mfg_v1.complete_center_use() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE batches integer := (NEW.item_count+63)/64;
        BEGIN
            IF NEW.writer_xid<>pg_current_xact_id() OR batches<>(
                SELECT count(*) FROM cp6_fin_mfg_v1.center_use_intents i
                JOIN cp6_fin_mfg_v1.center_use_receipts r USING(tenant_id,ledger_id,effect_id,batch_no)
                WHERE (i.tenant_id,i.ledger_id,i.effect_id)=(NEW.tenant_id,NEW.ledger_id,NEW.id)
                  AND i.batch_no BETWEEN 1 AND batches
                  AND jsonb_array_length(i.items)=least(64,NEW.item_count-(i.batch_no-1)*64)
                  AND jsonb_array_length(r.adopted)=jsonb_array_length(i.items)
            ) THEN RAISE EXCEPTION 'FINANCE_CENTER_USE_INCOMPLETE' USING ERRCODE='55000'; END IF;
            IF NEW.result_kind='RATE_RELEASE' THEN
                IF NOT EXISTS(SELECT 1 FROM cp6_fin_mfg_v1.releases r
                    WHERE (r.tenant_id,r.ledger_id,r.rate_id,r.timeline_revision,r.center_use_effect_id)=
                        (NEW.tenant_id,NEW.ledger_id,NEW.result_id,NEW.result_revision,NEW.id)) THEN
                    RAISE EXCEPTION 'FINANCE_CENTER_USE_RESULT_MISSING' USING ERRCODE='55000';
                END IF;
            ELSE
                IF NOT EXISTS(SELECT 1 FROM cp6_fin_cost_basis_v1.versions v
                    WHERE (v.tenant_id,v.cost_sheet_id,v.version,v.center_use_effect_id)=
                        (NEW.tenant_id,NEW.result_id,NEW.result_revision,NEW.id))
                OR EXISTS(SELECT 1 FROM cp6_fin_mfg_v1.center_use_intents i CROSS JOIN LATERAL jsonb_array_elements(i.items) x(item)
                    WHERE (i.tenant_id,i.ledger_id,i.effect_id)=(NEW.tenant_id,NEW.ledger_id,NEW.id)
                    AND NOT EXISTS(SELECT 1 FROM public."Fin_CostSheetLine" l
                        WHERE (l."TenantId",l."CostSheetId",l."Id")=(NEW.tenant_id,NEW.result_id,(x.item->>'ItemId')::uuid))) THEN
                    RAISE EXCEPTION 'FINANCE_CENTER_USE_RESULT_MISSING' USING ERRCODE='55000';
                END IF;
            END IF;
            RETURN NEW;
        END; $$;
        CREATE CONSTRAINT TRIGGER center_use_complete_at_commit AFTER INSERT ON cp6_fin_mfg_v1.center_use_effects
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_fin_mfg_v1.complete_center_use();

        DO $$
        DECLARE relation text;
        BEGIN
            FOREACH relation IN ARRAY ARRAY['center_use_effects','center_use_intents','center_use_receipts'] LOOP
                EXECUTE format('CREATE TRIGGER immutable_use BEFORE UPDATE OR DELETE ON cp6_fin_mfg_v1.%I FOR EACH ROW EXECUTE FUNCTION cp6_fin_mfg_v1.immutable_source()',relation);
                EXECUTE format('CREATE TRIGGER keep_use BEFORE TRUNCATE ON cp6_fin_mfg_v1.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_fin_mfg_v1.immutable_source()',relation);
            END LOOP;
        END; $$;
        """);

    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("制造成本中心采用记录只支持前向迁移。");
}
