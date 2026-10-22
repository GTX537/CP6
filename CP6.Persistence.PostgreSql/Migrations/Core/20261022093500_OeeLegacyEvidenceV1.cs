using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261022093500_OeeLegacyEvidenceV1")]
public sealed class OeeLegacyEvidenceV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE SCHEMA cp6_oee_legacy_v1;
        REVOKE ALL ON SCHEMA cp6_oee_legacy_v1 FROM PUBLIC;
        CREATE TABLE cp6_oee_legacy_v1.calculations (
          id uuid PRIMARY KEY, tenant_id uuid NOT NULL REFERENCES public."Sys_Tenants"("Id"),
          daily_id uuid NOT NULL REFERENCES public."T_OeeDaily"("Id"),
          machine_id uuid NOT NULL REFERENCES public."M_Machine"("Id"),
          profile text COLLATE "C" NOT NULL CHECK(profile='LEGACY_CALC_V1'),
          evidence_version text COLLATE "C" NOT NULL CHECK(evidence_version='Main.Oee.InputEvidence.v1'),
          input_bytes bytea NOT NULL, input_hash bytea NOT NULL CHECK(input_hash=sha256(input_bytes)),
          result_bytes bytea NOT NULL, result_hash bytea NOT NULL CHECK(result_hash=sha256(result_bytes)),
          daily_version bytea NOT NULL,
          observed_at timestamp without time zone NOT NULL,
          actor_kind text COLLATE "C" NOT NULL CHECK(actor_kind IN ('HUMAN','OEE_WORKER_V1')),
          actor_id uuid, actor_name text NOT NULL,
          saved_at timestamptz NOT NULL DEFAULT clock_timestamp(),
          CHECK(tenant_id<>'00000000-0000-0000-0000-000000000000'::uuid),
          CHECK((actor_kind='HUMAN' AND actor_id IS NOT NULL AND actor_id<>'00000000-0000-0000-0000-000000000000'::uuid)
             OR (actor_kind='OEE_WORKER_V1' AND actor_id IS NULL)));
        CREATE INDEX calculations_daily_version ON cp6_oee_legacy_v1.calculations(tenant_id,daily_id,daily_version);
        CREATE TRIGGER authority_gate BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE
          ON cp6_oee_legacy_v1.calculations FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE FUNCTION cp6_oee_legacy_v1.validate() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF NOT EXISTS(SELECT FROM public."T_OeeDaily" d JOIN public."M_Machine" m
            ON m."Id"=NEW.machine_id AND m."TenantId"=NEW.tenant_id AND m."MachineCd"=d."MachineCd" AND NOT m."IsDeleted"
            WHERE d."Id"=NEW.daily_id AND d."TenantId"=NEW.tenant_id AND NOT d."IsDeleted"
              AND d."RowVersion"=NEW.daily_version) THEN
            RAISE EXCEPTION 'OEE_EVIDENCE_OBJECT_MISMATCH' USING ERRCODE='23514';
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER validate BEFORE INSERT ON cp6_oee_legacy_v1.calculations
          FOR EACH ROW EXECUTE FUNCTION cp6_oee_legacy_v1.validate();
        CREATE FUNCTION cp6_oee_legacy_v1.immutable() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN RAISE EXCEPTION 'OEE_CALCULATION_EVIDENCE_IMMUTABLE' USING ERRCODE='23514'; END $$;
        CREATE TRIGGER immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_oee_legacy_v1.calculations
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_oee_legacy_v1.immutable();
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_oee_legacy_v1 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_oee_legacy_v1 FROM PUBLIC;
        -- Existing tenant catalog only; no new role grant or historical calculation backfill.
        INSERT INTO public."Sys_MenuAction" ("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",'query','读取OEE参考计算',0,t."Id",LOCALTIMESTAMP
        FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t
        WHERE m."MenuKey"='mes-oee' AND NOT EXISTS
          (SELECT 1 FROM public."Sys_MenuAction" a WHERE a."TenantId"=t."Id" AND a."MenuId"=m."MenuId" AND a."ActionCode"='query');
        """);

    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("OEE input/output evidence requires explicit forward recovery.");
}
