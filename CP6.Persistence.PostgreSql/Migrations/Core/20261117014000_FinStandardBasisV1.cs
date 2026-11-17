using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261117014000_FinStandardBasisV1")]
public sealed class FinStandardBasisV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE cp6_fin_mfg_v1.qualifications DROP CONSTRAINT qualifications_action_check;
        ALTER TABLE cp6_fin_mfg_v1.qualifications ADD CHECK
            (action IN ('rate-review','rate-release','center-review','center-release','basis-review','basis-release'));
        ALTER TABLE cp6_fin_mfg_v1.qualification_decisions DROP CONSTRAINT qualification_decisions_action_check;
        ALTER TABLE cp6_fin_mfg_v1.qualification_decisions ADD CHECK
            (action IN ('rate-review','rate-release','center-review','center-release','basis-review','basis-release'));
        ALTER TABLE cp6_fin_mfg_v1.work_orders ADD UNIQUE (tenant_id,ledger_id,work_order_id);

        CREATE TABLE cp6_fin_mfg_v1.standard_basis_versions (
            tenant_id uuid NOT NULL, ledger_id uuid NOT NULL, work_order_id uuid NOT NULL, id uuid NOT NULL,
            sequence bigint NOT NULL CHECK (sequence>0), previous_id uuid, policy_version_id uuid NOT NULL,
            valid_from date NOT NULL, to_exclusive date,
            body jsonb NOT NULL CHECK (jsonb_typeof(body)='object'), maker uuid NOT NULL,
            submission_key uuid NOT NULL, submission jsonb NOT NULL, submitted_at timestamptz NOT NULL DEFAULT clock_timestamp(),
            state text NOT NULL DEFAULT 'SUBMITTED' CHECK (state IN ('SUBMITTED','APPROVED','PUBLISHED','REVOKED')),
            revision bigint NOT NULL DEFAULT 1,
            reviewer uuid, review_qualification_id uuid, review_key uuid, review_reason text, reviewed_at timestamptz,
            publisher uuid, release_qualification_id uuid, release_key uuid, released_at timestamptz,
            revoker uuid, revoke_key uuid, revoke_reason text, revoked_at timestamptz,
            PRIMARY KEY (tenant_id,ledger_id,work_order_id,id), UNIQUE (tenant_id,id),
            UNIQUE (tenant_id,ledger_id,work_order_id,sequence), UNIQUE (tenant_id,ledger_id,maker,submission_key),
            FOREIGN KEY (tenant_id,ledger_id,work_order_id) REFERENCES cp6_fin_mfg_v1.work_orders (tenant_id,ledger_id,work_order_id),
            FOREIGN KEY (tenant_id,ledger_id,policy_version_id) REFERENCES cp6_fin_mfg_v1.policies,
            FOREIGN KEY (tenant_id,ledger_id,work_order_id,previous_id) REFERENCES cp6_fin_mfg_v1.standard_basis_versions,
            CHECK (previous_id IS DISTINCT FROM id), CHECK (to_exclusive IS NULL OR valid_from<to_exclusive),
            CHECK (revision=CASE state WHEN 'SUBMITTED' THEN 1 WHEN 'APPROVED' THEN 2 WHEN 'PUBLISHED' THEN 3 ELSE 4 END),
            CHECK (reviewer IS NULL OR reviewer<>maker),
            CHECK ((state<>'SUBMITTED') = (reviewer IS NOT NULL AND review_qualification_id IS NOT NULL
                AND review_key IS NOT NULL AND review_reason IS NOT NULL AND reviewed_at IS NOT NULL)),
            CHECK ((state IN ('PUBLISHED','REVOKED'))=(publisher IS NOT NULL AND release_qualification_id IS NOT NULL
                AND release_key IS NOT NULL AND released_at IS NOT NULL)),
            CHECK ((state='REVOKED')=(revoker IS NOT NULL AND revoke_key IS NOT NULL AND revoke_reason IS NOT NULL AND revoked_at IS NOT NULL))
        );
        CREATE TABLE cp6_fin_mfg_v1.standard_basis_heads (
            tenant_id uuid NOT NULL, ledger_id uuid NOT NULL, work_order_id uuid NOT NULL, basis_id uuid NOT NULL,
            PRIMARY KEY (tenant_id,ledger_id,work_order_id),
            FOREIGN KEY (tenant_id,ledger_id,work_order_id,basis_id) REFERENCES cp6_fin_mfg_v1.standard_basis_versions
        );

        CREATE FUNCTION cp6_fin_mfg_v1.guard_standard_basis() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
            IF TG_OP<>'UPDATE' OR (OLD.state,NEW.state) NOT IN
                (('SUBMITTED','APPROVED'),('APPROVED','PUBLISHED'),('PUBLISHED','REVOKED'))
                OR (to_jsonb(OLD)-ARRAY['state','revision','reviewer','review_qualification_id','review_key','review_reason','reviewed_at',
                    'publisher','release_qualification_id','release_key','released_at','revoker','revoke_key','revoke_reason','revoked_at'])
                IS DISTINCT FROM
                (to_jsonb(NEW)-ARRAY['state','revision','reviewer','review_qualification_id','review_key','review_reason','reviewed_at',
                    'publisher','release_qualification_id','release_key','released_at','revoker','revoke_key','revoke_reason','revoked_at'])
                OR (OLD.reviewer IS NOT NULL AND (OLD.reviewer,OLD.review_qualification_id,OLD.review_key,OLD.review_reason,OLD.reviewed_at)
                    IS DISTINCT FROM (NEW.reviewer,NEW.review_qualification_id,NEW.review_key,NEW.review_reason,NEW.reviewed_at))
                OR (OLD.publisher IS NOT NULL AND (OLD.publisher,OLD.release_qualification_id,OLD.release_key,OLD.released_at)
                    IS DISTINCT FROM (NEW.publisher,NEW.release_qualification_id,NEW.release_key,NEW.released_at)) THEN
                RAISE EXCEPTION 'STANDARD_BASIS_IMMUTABLE' USING ERRCODE='55000';
            END IF;
            RETURN NEW;
        END; $$;
        CREATE TRIGGER standard_basis_transition BEFORE UPDATE OR DELETE ON cp6_fin_mfg_v1.standard_basis_versions
            FOR EACH ROW EXECUTE FUNCTION cp6_fin_mfg_v1.guard_standard_basis();
        CREATE TRIGGER standard_basis_keep BEFORE TRUNCATE ON cp6_fin_mfg_v1.standard_basis_versions
            FOR EACH STATEMENT EXECUTE FUNCTION cp6_fin_mfg_v1.immutable_source();

        CREATE FUNCTION cp6_fin_mfg_v1.guard_standard_basis_head() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
            IF TG_OP='DELETE' OR (TG_OP='UPDATE' AND
                (NEW.tenant_id,NEW.ledger_id,NEW.work_order_id) IS DISTINCT FROM (OLD.tenant_id,OLD.ledger_id,OLD.work_order_id))
                OR NOT EXISTS(SELECT 1 FROM cp6_fin_mfg_v1.standard_basis_versions v
                    WHERE (v.tenant_id,v.ledger_id,v.work_order_id,v.id)=(NEW.tenant_id,NEW.ledger_id,NEW.work_order_id,NEW.basis_id)
                    AND v.state='PUBLISHED' AND v.previous_id IS NOT DISTINCT FROM
                        CASE WHEN TG_OP='UPDATE' THEN OLD.basis_id ELSE NULL END) THEN
                RAISE EXCEPTION 'STANDARD_BASIS_HEAD_MISMATCH' USING ERRCODE='55000';
            END IF;
            RETURN NEW;
        END; $$;
        CREATE TRIGGER standard_basis_head_transition BEFORE INSERT OR UPDATE OR DELETE ON cp6_fin_mfg_v1.standard_basis_heads
            FOR EACH ROW EXECUTE FUNCTION cp6_fin_mfg_v1.guard_standard_basis_head();
        CREATE TRIGGER standard_basis_head_keep BEFORE TRUNCATE ON cp6_fin_mfg_v1.standard_basis_heads
            FOR EACH STATEMENT EXECUTE FUNCTION cp6_fin_mfg_v1.immutable_source();

        ALTER TABLE cp6_fin_cost_basis_v1.versions ADD COLUMN standard_basis_id uuid,
            ADD FOREIGN KEY (tenant_id,standard_basis_id) REFERENCES cp6_fin_mfg_v1.standard_basis_versions (tenant_id,id);
        CREATE FUNCTION cp6_fin_mfg_v1.require_standard_basis_adoption() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE current_basis uuid; basis_state text;
        BEGIN
            SELECT h.basis_id,v.state INTO current_basis,basis_state
            FROM public."Fin_CostSheet" sheet
            JOIN public."T_WorkOrder" wo ON (wo."TenantId",wo."WorkOrderNo")=(sheet."TenantId",sheet."WorkOrderNo")
            JOIN cp6_fin_mfg_v1.standard_basis_heads h ON (h.tenant_id,h.work_order_id)=(wo."TenantId",wo."Id")
            JOIN cp6_fin_mfg_v1.standard_basis_versions v ON (v.tenant_id,v.id)=(h.tenant_id,h.basis_id)
            WHERE (sheet."TenantId",sheet."Id")=(NEW.tenant_id,NEW.cost_sheet_id);
            IF NEW.standard_basis_id IS DISTINCT FROM current_basis
                OR NEW.standard_basis_id IS DISTINCT FROM (NEW.basis->'StandardBasis'->>'VersionId')::uuid
                OR (current_basis IS NOT NULL AND basis_state<>'PUBLISHED') THEN
                RAISE EXCEPTION 'STANDARD_BASIS_ADOPTION_MISMATCH' USING ERRCODE='55000';
            END IF;
            RETURN NEW;
        END; $$;
        CREATE TRIGGER cost_basis_standard_adoption BEFORE INSERT ON cp6_fin_cost_basis_v1.versions
            FOR EACH ROW EXECUTE FUNCTION cp6_fin_mfg_v1.require_standard_basis_adoption();

        CREATE OR REPLACE VIEW cp6_fin_cost_basis_v1.current_rates AS
            WITH latest AS (
                SELECT DISTINCT ON (tenant_id,cost_sheet_id) tenant_id,cost_sheet_id,basis,standard_basis_id
                FROM cp6_fin_cost_basis_v1.versions ORDER BY tenant_id,cost_sheet_id,version DESC
            )
            SELECT v.tenant_id,v.cost_sheet_id,(r.value->>'RateId')::uuid AS rate_id,
                (r.value->>'WorkCenterId')::uuid AS work_center_id,r.value->>'WorkCenter' AS work_center,
                (r.value->>'LedgerId')::uuid AS ledger_id,(r.value->>'RateMaker')::uuid AS rate_maker
            FROM latest v CROSS JOIN LATERAL jsonb_array_elements(v.basis->'Rates') r(value)
            UNION
            SELECT v.tenant_id,v.cost_sheet_id,(r.value->>'SourceId')::uuid,(r.value->>'WorkCenterId')::uuid,
                r.value->>'WorkCenter',s.ledger_id,(r.value->>'RateMaker')::uuid
            FROM latest v JOIN cp6_fin_mfg_v1.standard_basis_versions s ON (s.tenant_id,s.id)=(v.tenant_id,v.standard_basis_id)
            CROSS JOIN LATERAL jsonb_array_elements(s.body->'Lines') r(value)
            WHERE r.value->>'WorkCenterId' IS NOT NULL;

        INSERT INTO public."Sys_MenuAction" ("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",a.code,a.name,0,t."Id",LOCALTIMESTAMP
        FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t
        CROSS JOIN (VALUES ('basis-submit','登记标准成本依据'),('basis-review','独立复核标准成本依据'),
            ('basis-release','启用批准标准成本依据'),('basis-revoke','撤销标准成本依据'),
            ('basis-view','查询标准成本依据及历史比较'),('basis-use','按批准标准依据归集')) a(code,name)
        WHERE m."MenuKey"='fin-cost' AND NOT EXISTS (
            SELECT 1 FROM public."Sys_MenuAction" e WHERE e."TenantId"=t."Id" AND e."MenuId"=m."MenuId" AND e."ActionCode"=a.code);
        """);

    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("标准成本依据只支持前向迁移。");
}
