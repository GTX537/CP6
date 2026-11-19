using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261119165000_BusinessPartnerFinanceReferencesV1")]
public sealed class BusinessPartnerFinanceReferencesV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE SCHEMA cp6_bp_finance_ref_v1;
        REVOKE ALL ON SCHEMA cp6_bp_finance_ref_v1 FROM PUBLIC;

        -- 仅登记已有的 Owner 作用域。旧表没有法人/账套，迁移不根据代码或默认组织登记。
        CREATE TABLE cp6_bp_finance_ref_v1.scopes (
            tenant_id uuid NOT NULL, owner text NOT NULL, source_id text NOT NULL, ledger_id uuid NOT NULL, partner_id uuid,
            PRIMARY KEY (tenant_id,owner,source_id),
            FOREIGN KEY (tenant_id,ledger_id) REFERENCES cp6_fin_mfg_v1.ledgers(tenant_id,id),
            CHECK (owner IN ('Main.Fin.ApMaster.BankAccount.v1','Main.Erp.BusinessPartner.CreditLimit.v1','FIN-MASTER','FIN-CREDIT')),
            FOREIGN KEY (tenant_id,partner_id) REFERENCES cp6_business_partner_identity_v1.heads(tenant_id,partner_id),
            CHECK (length(source_id) BETWEEN 1 AND 128),
            CHECK ((owner='Main.Fin.ApMaster.BankAccount.v1')=(partner_id IS NULL))
        );
        CREATE TABLE cp6_bp_finance_ref_v1.versions (
            tenant_id uuid NOT NULL, owner text NOT NULL, source_id text NOT NULL, version text NOT NULL,
            purpose text NOT NULL, envelope text NOT NULL, digest text NOT NULL,
            masked_account text, amount numeric(38,8), currency text, native_nonce uuid, publisher_ledger_id uuid,
            observed_at timestamptz NOT NULL DEFAULT clock_timestamp(),
            PRIMARY KEY (tenant_id,owner,source_id,version),
            FOREIGN KEY (tenant_id,publisher_ledger_id) REFERENCES cp6_fin_mfg_v1.ledgers(tenant_id,id),
            CHECK ((owner IN ('FIN-MASTER','FIN-CREDIT'))=(publisher_ledger_id IS NOT NULL)),
            CHECK (length(source_id) BETWEEN 1 AND 128 AND length(version) BETWEEN 1 AND 128),
            CHECK (digest=encode(sha256(convert_to(envelope,'UTF8')),'hex')),
            CHECK (masked_account IS NULL OR masked_account ~ '^\*{4}([0-9]{4})?$'),
            CHECK (currency IS NULL OR currency ~ '^[A-Z]{3}$'),
            CHECK ((owner='Main.Fin.ApMaster.BankAccount.v1')=(native_nonce IS NOT NULL)),
            CHECK (
                owner='Main.Fin.ApMaster.BankAccount.v1' AND purpose='BP_RECEIPT_BANK_REFERENCE' AND amount IS NULL
                OR owner='Main.Erp.BusinessPartner.CreditLimit.v1' AND purpose='BP_LEGACY_CREDIT_LIMIT_OBSERVATION'
                    AND masked_account IS NULL AND currency IS NULL
                OR owner='FIN-MASTER' AND purpose='BP_PAYMENT_BANK_REFERENCE' AND amount IS NULL
                OR owner='FIN-CREDIT' AND purpose='BP_CREDIT_MASTER_REFERENCE' AND masked_account IS NULL
                    AND (amount IS NULL OR currency IS NOT NULL))
        );
        CREATE TABLE cp6_bp_finance_ref_v1.frontiers (
            tenant_id uuid NOT NULL, owner text NOT NULL, source_id text NOT NULL, version text NOT NULL,
            generation bigint NOT NULL CHECK (generation > 0),
            availability text NOT NULL CHECK (availability IN ('ONLINE','OFFLINE','ABSENT')),
            enabled boolean NOT NULL, effective_from date, effective_until date,
            PRIMARY KEY (tenant_id,owner,source_id),
            FOREIGN KEY (tenant_id,owner,source_id,version) REFERENCES cp6_bp_finance_ref_v1.versions,
            CHECK (effective_from IS NULL OR effective_until IS NULL OR effective_from < effective_until)
        );

        -- BP 原引用独立于来源前沿。原件不可用时仍保留原引用。五个空成员表示显式解除。
        CREATE TABLE cp6_bp_finance_ref_v1.links (
            tenant_id uuid NOT NULL, partner_id uuid NOT NULL, bp_revision bigint NOT NULL,
            field text NOT NULL CHECK (field IN ('finance.RECEIPT_BANK','finance.PAYMENT_BANK','finance.CREDIT_MASTER')),
            ledger_id uuid NOT NULL, owner text, source_id text, version text, digest text, purpose text,
            PRIMARY KEY (tenant_id,partner_id,field,bp_revision),
            FOREIGN KEY (tenant_id,partner_id,bp_revision)
                REFERENCES cp6_business_partner_identity_v1.versions(tenant_id,partner_id,revision),
            FOREIGN KEY (tenant_id,ledger_id) REFERENCES cp6_fin_mfg_v1.ledgers(tenant_id,id),
            CHECK (num_nonnulls(owner,source_id,version,digest,purpose) IN (0,5)),
            CHECK (digest IS NULL OR digest ~ '^[0-9a-f]{64}$'),
            CHECK (owner IS NULL OR (length(owner) BETWEEN 1 AND 128 AND length(source_id) BETWEEN 1 AND 128
                AND length(version) BETWEEN 1 AND 128 AND length(purpose) BETWEEN 1 AND 128))
        );

        CREATE TABLE cp6_bp_finance_ref_v1.guards (
            tenant_id uuid NOT NULL, ledger_id uuid NOT NULL, revision bigint NOT NULL CHECK (revision>0),
            PRIMARY KEY (tenant_id,ledger_id),
            FOREIGN KEY (tenant_id,ledger_id) REFERENCES cp6_fin_mfg_v1.ledgers(tenant_id,id)
        );

        CREATE FUNCTION cp6_bp_finance_ref_v1.immutable() RETURNS trigger
        LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN RAISE EXCEPTION 'BP_FINANCE_ORIGINAL_IMMUTABLE' USING ERRCODE='23514'; END $$;
        CREATE TRIGGER bp_finance_versions_immutable BEFORE UPDATE OR DELETE ON cp6_bp_finance_ref_v1.versions
            FOR EACH ROW EXECUTE FUNCTION cp6_bp_finance_ref_v1.immutable();
        CREATE TRIGGER bp_finance_no_truncate_versions BEFORE TRUNCATE ON cp6_bp_finance_ref_v1.versions
            FOR EACH STATEMENT EXECUTE FUNCTION cp6_bp_finance_ref_v1.immutable();
        CREATE TRIGGER bp_finance_links_immutable BEFORE UPDATE OR DELETE ON cp6_bp_finance_ref_v1.links
            FOR EACH ROW EXECUTE FUNCTION cp6_bp_finance_ref_v1.immutable();
        CREATE TRIGGER bp_finance_no_truncate_links BEFORE TRUNCATE ON cp6_bp_finance_ref_v1.links
            FOR EACH STATEMENT EXECUTE FUNCTION cp6_bp_finance_ref_v1.immutable();
        CREATE TRIGGER bp_finance_scopes_immutable BEFORE UPDATE OR DELETE ON cp6_bp_finance_ref_v1.scopes
            FOR EACH ROW EXECUTE FUNCTION cp6_bp_finance_ref_v1.immutable();
        CREATE TRIGGER bp_finance_no_truncate_scopes BEFORE TRUNCATE ON cp6_bp_finance_ref_v1.scopes
            FOR EACH STATEMENT EXECUTE FUNCTION cp6_bp_finance_ref_v1.immutable();

        CREATE FUNCTION cp6_bp_finance_ref_v1.advance() RETURNS trigger
        LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
            IF TG_OP='INSERT' THEN
                IF NEW.generation=1 THEN RETURN NEW; END IF;
            ELSIF (NEW.tenant_id,NEW.owner,NEW.source_id)=(OLD.tenant_id,OLD.owner,OLD.source_id)
                AND NEW.generation=OLD.generation+1 THEN
                RETURN NEW;
            END IF;
            RAISE EXCEPTION 'BP_FINANCE_FRONTIER_INVALID' USING ERRCODE='23514';
        END $$;
        CREATE FUNCTION cp6_bp_finance_ref_v1.guard_advance() RETURNS trigger
        LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
            IF TG_OP='INSERT' THEN
                IF NEW.revision=1 THEN RETURN NEW; END IF;
            ELSIF (NEW.tenant_id,NEW.ledger_id)=(OLD.tenant_id,OLD.ledger_id) AND NEW.revision=OLD.revision+1 THEN
                RETURN NEW;
            END IF;
            RAISE EXCEPTION 'BP_FINANCE_GUARD_INVALID' USING ERRCODE='23514';
        END $$;
        CREATE TRIGGER bp_finance_advance_guard BEFORE INSERT OR UPDATE ON cp6_bp_finance_ref_v1.guards
            FOR EACH ROW EXECUTE FUNCTION cp6_bp_finance_ref_v1.guard_advance();
        CREATE TRIGGER bp_finance_no_delete_guard BEFORE DELETE ON cp6_bp_finance_ref_v1.guards
            FOR EACH ROW EXECUTE FUNCTION cp6_bp_finance_ref_v1.immutable();
        CREATE TRIGGER bp_finance_no_truncate_guards BEFORE TRUNCATE ON cp6_bp_finance_ref_v1.guards
            FOR EACH STATEMENT EXECUTE FUNCTION cp6_bp_finance_ref_v1.immutable();
        CREATE TRIGGER bp_finance_advance BEFORE INSERT OR UPDATE ON cp6_bp_finance_ref_v1.frontiers
            FOR EACH ROW EXECUTE FUNCTION cp6_bp_finance_ref_v1.advance();
        CREATE TRIGGER bp_finance_no_delete_frontier BEFORE DELETE ON cp6_bp_finance_ref_v1.frontiers
            FOR EACH ROW EXECUTE FUNCTION cp6_bp_finance_ref_v1.immutable();
        CREATE TRIGGER bp_finance_no_truncate_frontiers BEFORE TRUNCATE ON cp6_bp_finance_ref_v1.frontiers
            FOR EACH STATEMENT EXECUTE FUNCTION cp6_bp_finance_ref_v1.immutable();

        CREATE FUNCTION cp6_bp_finance_ref_v1.bump_scope() RETURNS trigger
        LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE ledger uuid;
        BEGIN
            IF TG_TABLE_NAME='frontiers' THEN
                SELECT ledger_id INTO ledger FROM cp6_bp_finance_ref_v1.scopes
                    WHERE tenant_id=NEW.tenant_id AND owner=NEW.owner AND source_id=NEW.source_id;
            ELSE
                ledger=NEW.ledger_id;
            END IF;
            IF ledger IS NOT NULL THEN
                INSERT INTO cp6_bp_finance_ref_v1.guards(tenant_id,ledger_id,revision) VALUES(NEW.tenant_id,ledger,1)
                ON CONFLICT (tenant_id,ledger_id) DO UPDATE SET revision=cp6_bp_finance_ref_v1.guards.revision+1;
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER bp_finance_bump_frontier AFTER INSERT OR UPDATE ON cp6_bp_finance_ref_v1.frontiers
            FOR EACH ROW EXECUTE FUNCTION cp6_bp_finance_ref_v1.bump_scope();
        CREATE TRIGGER bp_finance_bump_scope AFTER INSERT ON cp6_bp_finance_ref_v1.scopes
            FOR EACH ROW EXECUTE FUNCTION cp6_bp_finance_ref_v1.bump_scope();
        CREATE TRIGGER bp_finance_bump_link AFTER INSERT ON cp6_bp_finance_ref_v1.links
            FOR EACH ROW EXECUTE FUNCTION cp6_bp_finance_ref_v1.bump_scope();

        -- 旧写者和直接 SQL 同事务见证来源版本。银行原件只保存遮罩、范围所需元数据及不可猜的承诺。
        CREATE FUNCTION cp6_bp_finance_ref_v1.bank_material(p jsonb) RETURNS jsonb
        LANGUAGE sql IMMUTABLE SET search_path=pg_catalog AS $$
            SELECT jsonb_build_array(p->'TenantId',p->'Id',p->'Code',p->'Name',p->'BankName',p->'AccountNo',
                p->'GlAccountId',p->'CurrencyCd',p->'IsActive')
        $$;
        CREATE FUNCTION cp6_bp_finance_ref_v1.bank_proof(p jsonb, nonce uuid) RETURNS text
        LANGUAGE sql IMMUTABLE SET search_path=pg_catalog AS $$
            SELECT encode(sha256(convert_to(nonce::text || cp6_bp_finance_ref_v1.bank_material(p)::text,'UTF8')),'hex')
        $$;
        CREATE FUNCTION cp6_bp_finance_ref_v1.capture(p jsonb, bank boolean, present boolean) RETURNS void
        LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE
            tenant uuid=(p->>'TenantId')::uuid;
            id text=p->>'Id';
            original_owner text=CASE WHEN bank THEN 'Main.Fin.ApMaster.BankAccount.v1'
                ELSE 'Main.Erp.BusinessPartner.CreditLimit.v1' END;
            original_purpose text=CASE WHEN bank THEN 'BP_RECEIPT_BANK_REFERENCE'
                ELSE 'BP_LEGACY_CREDIT_LIMIT_OBSERVATION' END;
            next_version bigint;
            account_digits text=regexp_replace(coalesce(p->>'AccountNo',''),'[^0-9]','','g');
            mask text;
            amount_value numeric(38,8);
            currency_value text;
            is_enabled boolean;
            nonce uuid=CASE WHEN bank THEN gen_random_uuid() END;
            body text;
        BEGIN
            SELECT generation+1 INTO next_version FROM cp6_bp_finance_ref_v1.frontiers
                WHERE tenant_id=tenant AND owner=original_owner AND source_id=id FOR UPDATE;
            next_version=coalesce(next_version,1);
            IF bank AND present THEN
                IF nullif(btrim(p->>'AccountNo'),'') IS NOT NULL THEN
                    mask='****' || CASE WHEN length(account_digits)>4 THEN right(account_digits,4) ELSE '' END;
                END IF;
                IF (p->>'CurrencyCd') ~ '^[A-Z]{3}$' THEN currency_value=p->>'CurrencyCd'; END IF;
            ELSIF present THEN
                amount_value=(p->>'CreditLimit')::numeric;
            END IF;
            is_enabled=bank AND present AND mask IS NOT NULL AND coalesce((p->>'IsActive')::boolean,false)
                AND (p->>'GlAccountId')::uuid<>'00000000-0000-0000-0000-000000000000';
            body=jsonb_build_object('tenantId',tenant,'owner',original_owner,'id',id,'version',next_version::text,
                'purpose',original_purpose,'present',present,'enabled',is_enabled,'maskedAccount',mask,
                'currency',currency_value,'creditLimit',amount_value,
                'accountCommitment',CASE WHEN bank AND present THEN cp6_bp_finance_ref_v1.bank_proof(p,nonce) END)::text;
            INSERT INTO cp6_bp_finance_ref_v1.versions(tenant_id,owner,source_id,version,purpose,
                envelope,digest,masked_account,amount,currency,native_nonce)
            VALUES(tenant,original_owner,id,next_version::text,original_purpose,
                body,encode(sha256(convert_to(body,'UTF8')),'hex'),mask,amount_value,currency_value,nonce);
            UPDATE cp6_bp_finance_ref_v1.frontiers
                SET version=next_version::text,generation=next_version,
                    availability=CASE WHEN present THEN 'ONLINE' ELSE 'ABSENT' END,enabled=is_enabled
                WHERE tenant_id=tenant AND owner=original_owner AND source_id=id;
            IF NOT FOUND THEN
                INSERT INTO cp6_bp_finance_ref_v1.frontiers(tenant_id,owner,source_id,version,generation,availability,enabled)
                VALUES(tenant,original_owner,id,next_version::text,next_version,
                    CASE WHEN present THEN 'ONLINE' ELSE 'ABSENT' END,is_enabled);
            END IF;
        END $$;

        CREATE FUNCTION cp6_bp_finance_ref_v1.record_legacy() RETURNS trigger
        LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE bank boolean=TG_TABLE_NAME='Fin_BankAccount';
        BEGIN
            IF TG_OP='DELETE' THEN
                PERFORM cp6_bp_finance_ref_v1.capture(to_jsonb(OLD),bank,false);
                RETURN OLD;
            END IF;
            IF TG_OP='UPDATE' THEN
                IF (OLD."TenantId",OLD."Id") IS DISTINCT FROM (NEW."TenantId",NEW."Id") THEN
                    PERFORM cp6_bp_finance_ref_v1.capture(to_jsonb(OLD),bank,false);
                ELSIF bank THEN
                    IF cp6_bp_finance_ref_v1.bank_material(to_jsonb(OLD))=cp6_bp_finance_ref_v1.bank_material(to_jsonb(NEW))
                    THEN RETURN NEW; END IF;
                ELSIF OLD."CreditLimit" IS NOT DISTINCT FROM NEW."CreditLimit" THEN
                    RETURN NEW;
                END IF;
            END IF;
            PERFORM cp6_bp_finance_ref_v1.capture(to_jsonb(NEW),bank,true);
            RETURN NEW;
        END $$;
        CREATE TRIGGER bp_finance_capture_bank AFTER INSERT OR UPDATE OF
            "TenantId","Id","AccountNo","CurrencyCd","IsActive","GlAccountId","BankName","Name","Code" OR DELETE
            ON public."Fin_BankAccount" FOR EACH ROW EXECUTE FUNCTION cp6_bp_finance_ref_v1.record_legacy();
        CREATE TRIGGER bp_finance_no_truncate_bank BEFORE TRUNCATE ON public."Fin_BankAccount"
            FOR EACH STATEMENT EXECUTE FUNCTION cp6_bp_finance_ref_v1.immutable();
        CREATE TRIGGER bp_finance_capture_credit AFTER INSERT OR UPDATE OF "TenantId","Id","CreditLimit" OR DELETE
            ON public."T_WebBusinessPartner" FOR EACH ROW EXECUTE FUNCTION cp6_bp_finance_ref_v1.record_legacy();

        SELECT cp6_bp_finance_ref_v1.capture(to_jsonb(p),false,true) FROM public."T_WebBusinessPartner" p;
        SELECT cp6_bp_finance_ref_v1.capture(to_jsonb(b),true,true) FROM public."Fin_BankAccount" b;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_bp_finance_ref_v1 FROM PUBLIC;
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_bp_finance_ref_v1 FROM PUBLIC;

        -- 登记能力，不自动给任何角色授权。
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",'finance-reference-read','读取伙伴财务原引用',0,t."Id",LOCALTIMESTAMP
        FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t
        WHERE m."MenuKey"='erp-business-partner' AND NOT EXISTS(SELECT FROM public."Sys_MenuAction" a
            WHERE a."TenantId"=t."Id" AND a."MenuId"=m."MenuId" AND a."ActionCode"='finance-reference-read');
        """);

    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("伙伴财务历史原件需通过前向迁移恢复。");
}
