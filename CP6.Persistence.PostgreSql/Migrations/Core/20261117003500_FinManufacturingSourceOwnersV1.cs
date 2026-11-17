using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261117003500_FinManufacturingSourceOwnersV1")]
public sealed class FinManufacturingSourceOwnersV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE cp6_fin_mfg_v1.qualifications DROP CONSTRAINT qualifications_action_check;
        ALTER TABLE cp6_fin_mfg_v1.qualifications ADD CHECK
            (action IN ('rate-review','rate-release','center-review','center-release'));
        CREATE TABLE cp6_fin_mfg_v1.qualification_decisions (
            tenant_id uuid NOT NULL, ledger_id uuid NOT NULL, command_id uuid NOT NULL, id uuid NOT NULL,
            issuer_id uuid NOT NULL, subject_id uuid NOT NULL, action text NOT NULL,
            qualification_id uuid NOT NULL, enabled boolean NOT NULL,
            request jsonb NOT NULL, recorded_at timestamptz NOT NULL DEFAULT clock_timestamp(),
            PRIMARY KEY (tenant_id,ledger_id,command_id), UNIQUE (tenant_id,ledger_id,id),
            FOREIGN KEY (tenant_id,ledger_id) REFERENCES cp6_fin_mfg_v1.ledgers,
            CHECK (issuer_id <> subject_id),
            CHECK (action IN ('rate-review','rate-release','center-review','center-release')),
            CHECK (enabled = (qualification_id=id))
        );
        CREATE TRIGGER immutable_qualification_decision BEFORE UPDATE OR DELETE ON cp6_fin_mfg_v1.qualification_decisions
            FOR EACH ROW EXECUTE FUNCTION cp6_fin_mfg_v1.immutable_source();

        CREATE TABLE cp6_fin_mfg_v1.center_submissions (
            tenant_id uuid NOT NULL, ledger_id uuid NOT NULL, id uuid NOT NULL,
            root_id uuid NOT NULL REFERENCES public."Fin_CostCenter" ("Id"),
            operation_id uuid NOT NULL, operation_version bytea NOT NULL,
            code text NOT NULL, name text NOT NULL, valid_from date NOT NULL, to_exclusive date, supersedes_version_id uuid,
            maker uuid NOT NULL, submission_key uuid NOT NULL, submission jsonb NOT NULL,
            state text NOT NULL DEFAULT 'SUBMITTED' CHECK (state IN ('SUBMITTED','APPROVED','REJECTED','RELEASED')),
            revision bigint NOT NULL DEFAULT 1,
            reviewer uuid, review_qualification_id uuid, review_key uuid, review jsonb, reviewed_at timestamptz,
            publisher uuid, release_qualification_id uuid, release_key uuid, released_at timestamptz,
            recorded_at timestamptz NOT NULL DEFAULT clock_timestamp(),
            PRIMARY KEY (tenant_id,ledger_id,id),
            UNIQUE (tenant_id,ledger_id,maker,submission_key),
            UNIQUE (tenant_id,ledger_id,root_id,id),
            FOREIGN KEY (tenant_id,ledger_id) REFERENCES cp6_fin_mfg_v1.ledgers,
            CHECK (to_exclusive IS NULL OR valid_from<to_exclusive),
            CHECK (reviewer IS NULL OR reviewer<>maker),
            CHECK (revision=CASE state WHEN 'SUBMITTED' THEN 1 WHEN 'RELEASED' THEN 3 ELSE 2 END),
            CHECK ((state<>'SUBMITTED')=(reviewer IS NOT NULL AND review_qualification_id IS NOT NULL
                AND review_key IS NOT NULL AND review IS NOT NULL AND reviewed_at IS NOT NULL)),
            CHECK ((state='RELEASED')=(publisher IS NOT NULL AND release_qualification_id IS NOT NULL
                AND release_key IS NOT NULL AND released_at IS NOT NULL))
        );
        ALTER TABLE cp6_fin_mfg_v1.center_versions
          ADD COLUMN submission_id uuid,
          ADD FOREIGN KEY (tenant_id,ledger_id,root_id,submission_id)
            REFERENCES cp6_fin_mfg_v1.center_submissions (tenant_id,ledger_id,root_id,id),
          ADD CHECK (submission_id IS NULL OR (version_id=submission_id AND approval_id=submission_id));

        CREATE FUNCTION cp6_fin_mfg_v1.guard_center_submission() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
            IF TG_OP='DELETE' OR
                (to_jsonb(OLD)-ARRAY['state','revision','reviewer','review_qualification_id','review_key','review','reviewed_at',
                    'publisher','release_qualification_id','release_key','released_at'])
                IS DISTINCT FROM
                (to_jsonb(NEW)-ARRAY['state','revision','reviewer','review_qualification_id','review_key','review','reviewed_at',
                    'publisher','release_qualification_id','release_key','released_at'])
                OR (OLD.state,NEW.state) NOT IN
                    (('SUBMITTED','APPROVED'),('SUBMITTED','REJECTED'),('APPROVED','RELEASED'))
                OR (OLD.state='APPROVED' AND (OLD.reviewer,OLD.review_qualification_id,OLD.review_key,OLD.review,OLD.reviewed_at)
                    IS DISTINCT FROM (NEW.reviewer,NEW.review_qualification_id,NEW.review_key,NEW.review,NEW.reviewed_at)) THEN
                RAISE EXCEPTION 'FINANCE_VERSION_IMMUTABLE' USING ERRCODE='55000';
            END IF;
            RETURN NEW;
        END; $$;
        CREATE TRIGGER center_submission_transition BEFORE UPDATE OR DELETE ON cp6_fin_mfg_v1.center_submissions
            FOR EACH ROW EXECUTE FUNCTION cp6_fin_mfg_v1.guard_center_submission();

        CREATE VIEW cp6_fin_mfg_v1.center_calendar AS
            SELECT v.*,datemultirange(daterange(v.valid_from,v.to_exclusive,'[)')) - COALESCE((
                SELECT range_agg(daterange(successor.valid_from,successor.to_exclusive,'[)'))
                FROM cp6_fin_mfg_v1.center_versions successor
                WHERE (successor.tenant_id,successor.ledger_id,successor.root_id)=(v.tenant_id,v.ledger_id,v.root_id)
                    AND successor.version>v.version AND successor.submission_id IS NOT NULL
            ),'{}'::datemultirange) AS effective_dates
            FROM cp6_fin_mfg_v1.center_versions v;

        CREATE VIEW cp6_fin_mfg_v1.current_center_versions AS
            SELECT v.*,s.operation_id
            FROM cp6_fin_mfg_v1.center_calendar v
            JOIN public."Fin_CostCenter" c ON (c."TenantId",c."Id")=(v.tenant_id,v.root_id)
            LEFT JOIN cp6_fin_mfg_v1.center_submissions s
                ON (s.tenant_id,s.ledger_id,s.root_id,s.id)=(v.tenant_id,v.ledger_id,v.root_id,v.submission_id)
            WHERE v.enabled AND c."IsActive" AND (v.submission_id IS NULL OR
                (s.state='RELEASED' AND c."Type"=2 AND c."ParentId" IS NULL AND c."LinkMachineId" IS NULL AND c."Code"=s.code
                 AND EXISTS(SELECT 1 FROM public."T_ProductProcess" p WHERE p."TenantId"=s.tenant_id
                    AND p."Id"=s.operation_id AND NOT p."IsDeleted" AND p."RowVersion"=s.operation_version)));

        ALTER TABLE cp6_fin_mfg_v1.rate_versions ADD COLUMN review_original_actor boolean NOT NULL DEFAULT false;
        CREATE OR REPLACE FUNCTION cp6_fin_mfg_v1.guard_rate() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
            IF TG_OP='DELETE' OR OLD.state='RELEASED' OR
                (to_jsonb(OLD)-ARRAY['state','revision','reviewer','review_qualification_id','publisher',
                    'release_qualification_id','submitted_at','reviewed_at','review_original_actor'])
                IS DISTINCT FROM
                (to_jsonb(NEW)-ARRAY['state','revision','reviewer','review_qualification_id','publisher',
                    'release_qualification_id','submitted_at','reviewed_at','review_original_actor'])
                OR NEW.revision<>OLD.revision+1
                OR (OLD.state,NEW.state) NOT IN (('DRAFT','SUBMITTED'),('SUBMITTED','APPROVED'),('APPROVED','RELEASED'))
                OR (NEW.state IN ('APPROVED','RELEASED') AND NOT NEW.review_original_actor)
                OR (OLD.submitted_at IS NOT NULL AND OLD.submitted_at IS DISTINCT FROM NEW.submitted_at)
                OR (OLD.state='APPROVED' AND (OLD.reviewer,OLD.review_qualification_id,OLD.reviewed_at,OLD.review_original_actor)
                    IS DISTINCT FROM (NEW.reviewer,NEW.review_qualification_id,NEW.reviewed_at,NEW.review_original_actor)) THEN
                RAISE EXCEPTION 'FINANCE_VERSION_IMMUTABLE' USING ERRCODE='55000';
            END IF;
            RETURN NEW;
        END; $$;

        INSERT INTO public."Sys_MenuAction" ("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",a.code,a.name,0,t."Id",LOCALTIMESTAMP
        FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t
        CROSS JOIN (VALUES ('qualification-admin','签发或撤销财务专业资格'),('center-submit','登记工序成本中心版本'),
            ('center-review','复核工序成本中心版本'),('center-release','发布工序成本中心版本')) a(code,name)
        WHERE m."MenuKey"='fin-cost' AND NOT EXISTS (
            SELECT 1 FROM public."Sys_MenuAction" e WHERE e."TenantId"=t."Id" AND e."MenuId"=m."MenuId" AND e."ActionCode"=a.code);
        """);

    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("制造成本专业资格与成本中心版本登记只支持前向迁移。");
}
