using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261116160500_FinManufacturingVersionsV1")]
public sealed class FinManufacturingVersionsV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE SCHEMA cp6_fin_mfg_v1;
        CREATE TABLE cp6_fin_mfg_v1.ledgers (
            tenant_id uuid NOT NULL REFERENCES public."Sys_Tenants" ("Id"),
            id uuid NOT NULL, environment_id text NOT NULL, mode text NOT NULL CHECK (mode IN ('REAL','DEMO')),
            legal_entity_key text NOT NULL, ledger_key text NOT NULL, currency text NOT NULL CHECK (currency ~ '^[A-Z]{3}$'),
            business_date date NOT NULL, enabled boolean NOT NULL DEFAULT true,
            PRIMARY KEY (tenant_id,id), UNIQUE (tenant_id,environment_id,mode,legal_entity_key,ledger_key)
        );
        CREATE TABLE cp6_fin_mfg_v1.members (
            tenant_id uuid NOT NULL, ledger_id uuid NOT NULL, subject_id uuid NOT NULL, enabled boolean NOT NULL DEFAULT true,
            PRIMARY KEY (tenant_id,ledger_id,subject_id),
            FOREIGN KEY (tenant_id,ledger_id) REFERENCES cp6_fin_mfg_v1.ledgers
        );
        CREATE TABLE cp6_fin_mfg_v1.contexts (
            token uuid PRIMARY KEY, tenant_id uuid NOT NULL, ledger_id uuid NOT NULL, subject_id uuid NOT NULL,
            expires_at timestamptz NOT NULL, UNIQUE (tenant_id,ledger_id,subject_id),
            FOREIGN KEY (tenant_id,ledger_id,subject_id) REFERENCES cp6_fin_mfg_v1.members ON DELETE CASCADE
        );
        CREATE TABLE cp6_fin_mfg_v1.center_versions (
            tenant_id uuid NOT NULL, ledger_id uuid NOT NULL, root_id uuid NOT NULL, version_id uuid NOT NULL,
            version bigint NOT NULL CHECK (version>0), code text NOT NULL, name text NOT NULL,
            valid_from date NOT NULL, to_exclusive date, approval_id uuid NOT NULL, enabled boolean NOT NULL DEFAULT true,
            PRIMARY KEY (tenant_id,ledger_id,root_id,version_id), UNIQUE (tenant_id,ledger_id,root_id,version),
            FOREIGN KEY (tenant_id,ledger_id) REFERENCES cp6_fin_mfg_v1.ledgers,
            FOREIGN KEY (root_id) REFERENCES public."Fin_CostCenter" ("Id"),
            CHECK (to_exclusive IS NULL OR valid_from<to_exclusive)
        );
        CREATE TABLE cp6_fin_mfg_v1.center_selections (
            token uuid PRIMARY KEY, tenant_id uuid NOT NULL, ledger_id uuid NOT NULL, subject_id uuid NOT NULL,
            root_id uuid NOT NULL, version_id uuid NOT NULL, expires_at timestamptz NOT NULL,
            UNIQUE (tenant_id,ledger_id,subject_id,root_id,version_id),
            FOREIGN KEY (tenant_id,ledger_id,subject_id) REFERENCES cp6_fin_mfg_v1.members ON DELETE CASCADE,
            FOREIGN KEY (tenant_id,ledger_id,root_id,version_id) REFERENCES cp6_fin_mfg_v1.center_versions
        );
        CREATE TABLE cp6_fin_mfg_v1.policies (
            tenant_id uuid NOT NULL, ledger_id uuid NOT NULL, version_id uuid NOT NULL, approval_id uuid NOT NULL,
            currency text NOT NULL CHECK (currency ~ '^[A-Z]{3}$'),
            currency_scale integer NOT NULL CHECK (currency_scale BETWEEN 0 AND 8),
            rounding text NOT NULL CHECK (rounding IN ('HALF_UP','HALF_EVEN')),
            rate_date_basis text NOT NULL CHECK (rate_date_basis IN ('APPROVED_ACCOUNTING_DATE','ACTUAL_INTERVAL_SEGMENTS')),
            valuation text NOT NULL CHECK (valuation IN ('ACTUAL_OUTPUT','STANDARD_OUTPUT_WITH_VARIANCE')),
            variance text NOT NULL CHECK (variance IN ('VAR_TO_EXPENSE','RETAIN_WIP','ALLOCATE_OUTPUT')),
            allow_zero_rate boolean NOT NULL DEFAULT false,
            valid_from date NOT NULL, to_exclusive date, enabled boolean NOT NULL DEFAULT true,
            PRIMARY KEY (tenant_id,ledger_id,version_id), FOREIGN KEY (tenant_id,ledger_id) REFERENCES cp6_fin_mfg_v1.ledgers,
            CHECK (to_exclusive IS NULL OR valid_from<to_exclusive)
        );
        CREATE TABLE cp6_fin_mfg_v1.rate_bases (
            tenant_id uuid NOT NULL, ledger_id uuid NOT NULL, id uuid NOT NULL, policy_version_id uuid NOT NULL,
            approval_id uuid NOT NULL, work_center_id uuid NOT NULL, operation_id uuid NOT NULL,
            valid_from date NOT NULL, to_exclusive date, zero_evidenced boolean NOT NULL DEFAULT false,
            enabled boolean NOT NULL DEFAULT true,
            PRIMARY KEY (tenant_id,ledger_id,id),
            FOREIGN KEY (tenant_id,ledger_id,policy_version_id) REFERENCES cp6_fin_mfg_v1.policies,
            CHECK (to_exclusive IS NULL OR valid_from<to_exclusive)
        );
        CREATE TABLE cp6_fin_mfg_v1.qualifications (
            tenant_id uuid NOT NULL, ledger_id uuid NOT NULL, subject_id uuid NOT NULL, action text NOT NULL
                CHECK (action IN ('rate-review','rate-release')),
            qualification_id uuid NOT NULL, expires_at timestamptz NOT NULL, enabled boolean NOT NULL DEFAULT true,
            PRIMARY KEY (tenant_id,ledger_id,subject_id,action),
            FOREIGN KEY (tenant_id,ledger_id,subject_id) REFERENCES cp6_fin_mfg_v1.members ON DELETE CASCADE
        );
        CREATE TABLE cp6_fin_mfg_v1.work_orders (
            tenant_id uuid NOT NULL, work_order_id uuid NOT NULL REFERENCES public."T_WorkOrder" ("Id"),
            ledger_id uuid NOT NULL, PRIMARY KEY (tenant_id,work_order_id),
            FOREIGN KEY (tenant_id,ledger_id) REFERENCES cp6_fin_mfg_v1.ledgers
        );
        CREATE TABLE cp6_fin_mfg_v1.rate_heads (
            tenant_id uuid NOT NULL, ledger_id uuid NOT NULL, work_center_id uuid NOT NULL, operation_id uuid NOT NULL,
            revision bigint NOT NULL DEFAULT 0 CHECK (revision>=0),
            PRIMARY KEY (tenant_id,ledger_id,work_center_id,operation_id),
            FOREIGN KEY (tenant_id,ledger_id) REFERENCES cp6_fin_mfg_v1.ledgers
        );
        CREATE TABLE cp6_fin_mfg_v1.rate_versions (
            tenant_id uuid NOT NULL, ledger_id uuid NOT NULL, id uuid NOT NULL,
            work_center_id uuid NOT NULL, operation_id uuid NOT NULL, center_id uuid NOT NULL,
            center_version_id uuid NOT NULL, policy_version_id uuid NOT NULL, basis_id uuid NOT NULL,
            labor_rate numeric(28,8) NOT NULL CHECK (labor_rate>=0), overhead_rate numeric(28,8) NOT NULL CHECK (overhead_rate>=0),
            valid_from date NOT NULL, to_exclusive date, maker uuid NOT NULL,
            state text NOT NULL DEFAULT 'DRAFT' CHECK (state IN ('DRAFT','SUBMITTED','APPROVED','RELEASED')),
            revision bigint NOT NULL DEFAULT 1 CHECK (revision BETWEEN 1 AND 4),
            reviewer uuid, review_qualification_id uuid, publisher uuid, release_qualification_id uuid,
            created_at timestamptz NOT NULL DEFAULT clock_timestamp(), submitted_at timestamptz, reviewed_at timestamptz,
            PRIMARY KEY (tenant_id,ledger_id,id),
            FOREIGN KEY (tenant_id,ledger_id,work_center_id,operation_id) REFERENCES cp6_fin_mfg_v1.rate_heads,
            FOREIGN KEY (tenant_id,ledger_id,center_id,center_version_id) REFERENCES cp6_fin_mfg_v1.center_versions,
            FOREIGN KEY (tenant_id,ledger_id,policy_version_id) REFERENCES cp6_fin_mfg_v1.policies,
            FOREIGN KEY (tenant_id,ledger_id,basis_id) REFERENCES cp6_fin_mfg_v1.rate_bases,
            CHECK (to_exclusive IS NULL OR valid_from<to_exclusive), CHECK (reviewer IS NULL OR reviewer<>maker),
            CHECK ((state IN ('APPROVED','RELEASED')) = (reviewer IS NOT NULL AND review_qualification_id IS NOT NULL)),
            CHECK ((state<>'DRAFT') = (submitted_at IS NOT NULL)),
            CHECK ((state IN ('APPROVED','RELEASED')) = (reviewed_at IS NOT NULL)),
            CHECK ((state='RELEASED') = (publisher IS NOT NULL AND release_qualification_id IS NOT NULL))
        );
        CREATE TABLE cp6_fin_mfg_v1.release_previews (
            token uuid PRIMARY KEY, tenant_id uuid NOT NULL, ledger_id uuid NOT NULL, rate_id uuid NOT NULL, subject_id uuid NOT NULL,
            rate_revision bigint NOT NULL, timeline_revision bigint NOT NULL, expires_at timestamptz NOT NULL,
            FOREIGN KEY (tenant_id,ledger_id,rate_id) REFERENCES cp6_fin_mfg_v1.rate_versions
        );
        CREATE TABLE cp6_fin_mfg_v1.timeline (
            tenant_id uuid NOT NULL, ledger_id uuid NOT NULL, work_center_id uuid NOT NULL, operation_id uuid NOT NULL,
            rate_id uuid NOT NULL, valid_from date NOT NULL, to_exclusive date,
            PRIMARY KEY (tenant_id,ledger_id,work_center_id,operation_id,valid_from),
            FOREIGN KEY (tenant_id,ledger_id,rate_id) REFERENCES cp6_fin_mfg_v1.rate_versions,
            FOREIGN KEY (tenant_id,ledger_id,work_center_id,operation_id) REFERENCES cp6_fin_mfg_v1.rate_heads,
            CHECK (to_exclusive IS NULL OR valid_from<to_exclusive)
        );
        CREATE TABLE cp6_fin_mfg_v1.releases (
            tenant_id uuid NOT NULL, ledger_id uuid NOT NULL, rate_id uuid NOT NULL, preview_token uuid NOT NULL UNIQUE,
            timeline_revision bigint NOT NULL, before_segments jsonb NOT NULL, after_segments jsonb NOT NULL,
            released_at timestamptz NOT NULL DEFAULT clock_timestamp(),
            PRIMARY KEY (tenant_id,ledger_id,rate_id),
            FOREIGN KEY (tenant_id,ledger_id,rate_id) REFERENCES cp6_fin_mfg_v1.rate_versions,
            FOREIGN KEY (preview_token) REFERENCES cp6_fin_mfg_v1.release_previews
        );

        CREATE FUNCTION cp6_fin_mfg_v1.immutable_source() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
            IF TG_OP = 'UPDATE' AND TG_TABLE_NAME='ledgers'
                AND (to_jsonb(OLD)-ARRAY['enabled','business_date'])=(to_jsonb(NEW)-ARRAY['enabled','business_date']) THEN
                RETURN NEW;
            END IF;
            IF TG_OP = 'UPDATE' AND TG_TABLE_NAME IN ('center_versions','policies','rate_bases')
                AND (to_jsonb(OLD)-'enabled')=(to_jsonb(NEW)-'enabled') THEN RETURN NEW; END IF;
            RAISE EXCEPTION 'FINANCE_VERSION_IMMUTABLE' USING ERRCODE='55000';
        END; $$;

        CREATE FUNCTION cp6_fin_mfg_v1.guard_rate() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
            IF TG_OP='DELETE' OR OLD.state='RELEASED' OR
                (to_jsonb(OLD)-ARRAY['state','revision','reviewer','review_qualification_id','publisher',
                    'release_qualification_id','submitted_at','reviewed_at'])
                IS DISTINCT FROM
                (to_jsonb(NEW)-ARRAY['state','revision','reviewer','review_qualification_id','publisher',
                    'release_qualification_id','submitted_at','reviewed_at'])
                OR NEW.revision<>OLD.revision+1
                OR (OLD.state,NEW.state) NOT IN (('DRAFT','SUBMITTED'),('SUBMITTED','APPROVED'),('APPROVED','RELEASED'))
                OR (OLD.submitted_at IS NOT NULL AND OLD.submitted_at IS DISTINCT FROM NEW.submitted_at)
                OR (OLD.state='APPROVED' AND (OLD.reviewer,OLD.review_qualification_id,OLD.reviewed_at) IS DISTINCT FROM
                    (NEW.reviewer,NEW.review_qualification_id,NEW.reviewed_at)) THEN
                RAISE EXCEPTION 'FINANCE_VERSION_IMMUTABLE' USING ERRCODE='55000';
            END IF;
            RETURN NEW;
        END; $$;

        CREATE FUNCTION cp6_fin_mfg_v1.guard_work_order_ledger() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM public."T_WorkOrder" w
                WHERE w."TenantId"=NEW.tenant_id AND w."Id"=NEW.work_order_id AND NOT w."IsDeleted"
                AND NOT EXISTS (SELECT 1 FROM public."Fin_CostSheet" c
                    WHERE c."TenantId"=w."TenantId" AND c."WorkOrderNo"=w."WorkOrderNo")) THEN
                RAISE EXCEPTION 'COST_SCOPE_CUTOVER_REQUIRED' USING ERRCODE='55000';
            END IF;
            RETURN NEW;
        END; $$;

        CREATE TRIGGER immutable_center BEFORE UPDATE OR DELETE ON cp6_fin_mfg_v1.center_versions
            FOR EACH ROW EXECUTE FUNCTION cp6_fin_mfg_v1.immutable_source();
        CREATE TRIGGER immutable_ledger_identity BEFORE UPDATE OR DELETE ON cp6_fin_mfg_v1.ledgers
            FOR EACH ROW EXECUTE FUNCTION cp6_fin_mfg_v1.immutable_source();
        CREATE TRIGGER immutable_policy BEFORE UPDATE OR DELETE ON cp6_fin_mfg_v1.policies
            FOR EACH ROW EXECUTE FUNCTION cp6_fin_mfg_v1.immutable_source();
        CREATE TRIGGER immutable_rate_basis BEFORE UPDATE OR DELETE ON cp6_fin_mfg_v1.rate_bases
            FOR EACH ROW EXECUTE FUNCTION cp6_fin_mfg_v1.immutable_source();
        CREATE TRIGGER immutable_release BEFORE UPDATE OR DELETE ON cp6_fin_mfg_v1.releases
            FOR EACH ROW EXECUTE FUNCTION cp6_fin_mfg_v1.immutable_source();
        CREATE TRIGGER immutable_work_order_ledger BEFORE UPDATE OR DELETE ON cp6_fin_mfg_v1.work_orders
            FOR EACH ROW EXECUTE FUNCTION cp6_fin_mfg_v1.immutable_source();
        CREATE TRIGGER validate_work_order_ledger BEFORE INSERT ON cp6_fin_mfg_v1.work_orders
            FOR EACH ROW EXECUTE FUNCTION cp6_fin_mfg_v1.guard_work_order_ledger();
        CREATE TRIGGER rate_transition BEFORE UPDATE OR DELETE ON cp6_fin_mfg_v1.rate_versions
            FOR EACH ROW EXECUTE FUNCTION cp6_fin_mfg_v1.guard_rate();

        CREATE OR REPLACE VIEW cp6_fin_cost_basis_v1.current_rates AS
            SELECT v.tenant_id, v.cost_sheet_id, (r.value->>'RateId')::uuid AS rate_id,
                (r.value->>'WorkCenterId')::uuid AS work_center_id, r.value->>'WorkCenter' AS work_center,
                (r.value->>'LedgerId')::uuid AS ledger_id,(r.value->>'RateMaker')::uuid AS rate_maker
            FROM (
                SELECT DISTINCT ON (tenant_id, cost_sheet_id) tenant_id, cost_sheet_id, basis
                FROM cp6_fin_cost_basis_v1.versions ORDER BY tenant_id, cost_sheet_id, version DESC
            ) v
            CROSS JOIN LATERAL jsonb_array_elements(v.basis->'Rates') AS r(value);

        INSERT INTO public."Sys_MenuAction" ("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",a.code,a.name,0,t."Id",LOCALTIMESTAMP
        FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t
        CROSS JOIN (VALUES ('rate-draft','制订费率'),('rate-review','专业复核'),('rate-release','发布费率')) a(code,name)
        WHERE m."MenuKey"='fin-cost' AND NOT EXISTS (
            SELECT 1 FROM public."Sys_MenuAction" e WHERE e."TenantId"=t."Id" AND e."MenuId"=m."MenuId" AND e."ActionCode"=a.code);
        """);

    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("制造成本账套和费率版本只支持前向迁移。");
}
