using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261120052000_BusinessPartnerUseV1")]
public sealed class BusinessPartnerUseV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        -- 部署准备不会自动选择法人、迁移旧行或启用消费者。已登记租户内，未选主写者的法人保持只读。
        CREATE TABLE main_bp.use_writer_scopes (
            tenant_id uuid NOT NULL, legal_entity_key text COLLATE "C" NOT NULL,
            primary_writer text COLLATE "C" NOT NULL CHECK (primary_writer IN ('WORKSPACE','READ_ONLY')),
            PRIMARY KEY (tenant_id,legal_entity_key),
            FOREIGN KEY (tenant_id,legal_entity_key) REFERENCES main_bp.scopes
        );

        CREATE FUNCTION main_bp.use_scope_key(tenant uuid, legal text) RETURNS bigint
        LANGUAGE sql IMMUTABLE STRICT SET search_path=pg_catalog AS $$
            SELECT hashtextextended('Main.BP.Scope/1:' || tenant::text || ':' || legal, 0)
        $$;

        CREATE FUNCTION main_bp.use_scope_held(tenant uuid, legal text, exclusive_required boolean) RETURNS boolean
        LANGUAGE sql VOLATILE STRICT SET search_path=pg_catalog AS $$
            SELECT EXISTS (SELECT FROM pg_locks
                WHERE locktype='advisory' AND pid=pg_backend_pid() AND granted AND objsubid=1
                  AND database=(SELECT oid FROM pg_database WHERE datname=current_database())
                  AND classid::bigint=((main_bp.use_scope_key(tenant,legal) >> 32) & 4294967295)
                  AND objid::bigint=(main_bp.use_scope_key(tenant,legal) & 4294967295)
                  AND (mode='ExclusiveLock' OR (NOT exclusive_required AND mode='ShareLock')))
        $$;

        CREATE FUNCTION main_bp.lock_use_scope(tenant uuid, legal text, exclusive_required boolean) RETURNS boolean
        LANGUAGE plpgsql VOLATILE STRICT SET search_path=pg_catalog AS $$
        BEGIN
            IF NOT main_bp.use_scope_held(tenant,legal,exclusive_required) THEN
                IF exclusive_required THEN
                    IF main_bp.use_scope_held(tenant,legal,false) THEN
                        RAISE EXCEPTION 'LOCK_PLAN_UNPROVEN' USING ERRCODE='23514';
                    END IF;
                    PERFORM pg_advisory_xact_lock(main_bp.use_scope_key(tenant,legal));
                ELSE
                    PERFORM pg_advisory_xact_lock_shared(main_bp.use_scope_key(tenant,legal));
                END IF;
            END IF;
            RETURN true;
        END $$;

        CREATE FUNCTION main_bp.guard_use_scope_catalogue() RETURNS trigger
        LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE tenant uuid; legal text;
        BEGIN
            IF TG_OP='TRUNCATE' THEN
                IF EXISTS (SELECT FROM main_bp.use_writer_scopes) THEN
                    RAISE EXCEPTION 'LOCK_PLAN_UNPROVEN' USING ERRCODE='23514';
                END IF;
                RETURN NULL;
            END IF;
            IF TG_OP='DELETE' THEN
                tenant:=OLD.tenant_id; legal:=OLD.legal_entity_key;
            ELSE
                tenant:=NEW.tenant_id; legal:=NEW.legal_entity_key;
            END IF;
            IF TG_TABLE_NAME='use_writer_scopes' OR
               EXISTS (SELECT FROM main_bp.use_writer_scopes WHERE tenant_id=tenant) OR
               (TG_OP='UPDATE' AND EXISTS (SELECT FROM main_bp.use_writer_scopes WHERE tenant_id=OLD.tenant_id)) THEN
                IF (TG_TABLE_NAME='use_writer_scopes' AND TG_OP='DELETE')
                    OR (TG_OP='UPDATE' AND ROW(OLD.tenant_id,OLD.legal_entity_key) IS DISTINCT FROM ROW(tenant,legal))
                    OR NOT main_bp.use_scope_held(tenant,legal,true) THEN
                    RAISE EXCEPTION 'LOCK_PLAN_UNPROVEN' USING ERRCODE='23514';
                END IF;
            END IF;
            IF TG_OP='DELETE' THEN RETURN OLD; END IF;
            RETURN NEW;
        END $$;

        DO $guard$
        DECLARE table_name text;
        BEGIN
            FOREACH table_name IN ARRAY ARRAY['use_writer_scopes','scopes','bases','base_grants','option_values','heads','relations'] LOOP
                EXECUTE format('CREATE TRIGGER use_scope_writer BEFORE INSERT OR UPDATE OR DELETE ON main_bp.%I
                    FOR EACH ROW EXECUTE FUNCTION main_bp.guard_use_scope_catalogue()',table_name);
                EXECUTE format('CREATE TRIGGER use_scope_no_truncate BEFORE TRUNCATE ON main_bp.%I
                    FOR EACH STATEMENT EXECUTE FUNCTION main_bp.guard_use_scope_catalogue()',table_name);
            END LOOP;
        END $guard$;

        -- 保护切换租户的所有旧表入口，包括导入和不经过 EF 的批作业。
        CREATE FUNCTION main_bp.guard_use_business_partner() RETURNS trigger
        LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE tenant uuid; legal text;
        BEGIN
            IF TG_OP='DELETE' THEN tenant:=OLD."TenantId"; ELSE tenant:=NEW."TenantId"; END IF;
            IF NOT EXISTS (SELECT FROM main_bp.use_writer_scopes WHERE tenant_id=tenant) AND
               (TG_OP<>'UPDATE' OR NOT EXISTS (SELECT FROM main_bp.use_writer_scopes WHERE tenant_id=OLD."TenantId")) THEN
                IF TG_OP='DELETE' THEN RETURN OLD; END IF;
                RETURN NEW;
            END IF;
            IF TG_OP='DELETE' OR (TG_OP='UPDATE' AND
               ROW(OLD."TenantId",OLD."Id",OLD."BpCd",OLD."CrmAccountId") IS DISTINCT FROM
               ROW(NEW."TenantId",NEW."Id",NEW."BpCd",NEW."CrmAccountId")) THEN
                RAISE EXCEPTION 'LOCK_PLAN_UNPROVEN' USING ERRCODE='23514';
            END IF;
            IF TG_OP='UPDATE' THEN
                SELECT legal_entity_key INTO legal FROM main_bp.heads
                    WHERE tenant_id=tenant AND bp_id=NEW."Id";
            ELSE
                SELECT legal_entity_key INTO legal FROM main_bp.bases
                    WHERE tenant_id=tenant AND base_cd=rtrim(NEW."BaseCd"::text) COLLATE "C" AND enabled;
            END IF;
            IF legal IS NULL OR current_setting('cp6.bp_workspace_writer',true) IS DISTINCT FROM NEW."Id"::text
               OR NOT main_bp.use_scope_held(tenant,legal,true)
               OR NOT EXISTS (SELECT FROM main_bp.use_writer_scopes s JOIN main_bp.scopes p USING(tenant_id,legal_entity_key)
                    JOIN main_bp.bases b USING(tenant_id,legal_entity_key)
                    WHERE s.tenant_id=tenant AND s.legal_entity_key=legal AND s.primary_writer='WORKSPACE'
                      AND p.enabled AND b.enabled AND b.base_cd=rtrim(NEW."BaseCd"::text) COLLATE "C") THEN
                RAISE EXCEPTION 'LOCK_PLAN_UNPROVEN' USING ERRCODE='23514';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER use_scope_root_writer BEFORE INSERT OR UPDATE OR DELETE ON public."T_WebBusinessPartner"
            FOR EACH ROW EXECUTE FUNCTION main_bp.guard_use_business_partner();

        CREATE FUNCTION main_bp.use_guards_ready() RETURNS boolean
        LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
            SELECT current_setting('session_replication_role')='origin'
             AND EXISTS (SELECT FROM pg_trigger WHERE tgrelid='public."T_WebBusinessPartner"'::regclass
                 AND tgname='use_scope_root_writer' AND tgenabled IN ('O','A') AND NOT tgisinternal
                 AND tgtype=31 AND tgqual IS NULL AND tgnargs=0
                 AND tgfoid='main_bp.guard_use_business_partner()'::regprocedure)
             AND NOT EXISTS (
                 SELECT FROM unnest(ARRAY['use_writer_scopes','scopes','bases','base_grants','option_values','heads','relations']) tab(name)
                 CROSS JOIN (VALUES ('use_scope_writer',31),('use_scope_no_truncate',34)) guard(name,kind)
                 WHERE NOT EXISTS (SELECT FROM pg_trigger WHERE tgrelid=to_regclass(format('main_bp.%I',tab.name))
                    AND tgname=guard.name AND tgenabled IN ('O','A') AND NOT tgisinternal
                    AND tgtype=guard.kind AND tgqual IS NULL AND tgnargs=0
                    AND tgfoid='main_bp.guard_use_scope_catalogue()'::regprocedure))
        $$;

        CREATE TABLE main_bp.use_receipts (
            tenant_id uuid NOT NULL, consumer_owner text COLLATE "C" NOT NULL, use_key uuid NOT NULL,
            legal_entity_key text COLLATE "C" NOT NULL, bp_id uuid NOT NULL, bp_revision bigint NOT NULL,
            account_id uuid, mapping_revision bigint,
            purpose text COLLATE "C" NOT NULL CHECK(purpose IN ('SALES_NEW_SOURCE','SALES_CONFIRM','PURCHASE_NEW_SOURCE','PURCHASE_RELEASE')),
            consumer_id text COLLATE "C" NOT NULL, consumer_version text COLLATE "C" NOT NULL,
            business_effect_key text COLLATE "C" NOT NULL,
            history_sequence bigint GENERATED ALWAYS AS IDENTITY UNIQUE,
            actor_display text NOT NULL, recorded_at timestamptz NOT NULL,
            fingerprint bytea NOT NULL CHECK(octet_length(fingerprint)=32),
            receipt text NOT NULL CHECK(jsonb_typeof(receipt::jsonb)='object'),
            PRIMARY KEY (tenant_id,consumer_owner,use_key),
            UNIQUE (tenant_id,consumer_owner,consumer_id,consumer_version,purpose),
            FOREIGN KEY (tenant_id,legal_entity_key,bp_id,bp_revision) REFERENCES main_bp.profile_revisions,
            CHECK ((account_id IS NULL)=(mapping_revision IS NULL))
        );
        CREATE INDEX use_receipt_history ON main_bp.use_receipts(tenant_id,legal_entity_key,bp_id,history_sequence DESC);
        CREATE TRIGGER use_receipt_immutable BEFORE UPDATE OR DELETE ON main_bp.use_receipts
            FOR EACH ROW EXECUTE FUNCTION main_bp.protect_history();
        CREATE TRIGGER use_receipt_no_truncate BEFORE TRUNCATE ON main_bp.use_receipts
            FOR EACH STATEMENT EXECUTE FUNCTION main_bp.protect_history();

        REVOKE ALL ON main_bp.use_writer_scopes,main_bp.use_receipts FROM PUBLIC;
        REVOKE ALL ON FUNCTION main_bp.use_scope_key(uuid,text),main_bp.use_scope_held(uuid,text,boolean),
            main_bp.lock_use_scope(uuid,text,boolean),main_bp.guard_use_scope_catalogue(),
            main_bp.guard_use_business_partner(),main_bp.use_guards_ready() FROM PUBLIC;
        """);

    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("交易伙伴用途凭证只支持前向恢复。");
}
