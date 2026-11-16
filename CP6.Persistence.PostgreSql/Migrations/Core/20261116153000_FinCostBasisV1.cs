using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261116153000_FinCostBasisV1")]
public sealed class FinCostBasisV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE SCHEMA cp6_fin_cost_basis_v1;

        ALTER TABLE public."Fin_CostSheet"
            ADD CONSTRAINT uq_fin_cost_sheet_tenant_basis UNIQUE ("TenantId", "Id");

        CREATE TABLE cp6_fin_cost_basis_v1.versions (
            tenant_id uuid NOT NULL,
            cost_sheet_id uuid NOT NULL,
            version bigint NOT NULL CHECK (version > 0),
            basis jsonb NOT NULL,
            recorded_at timestamp with time zone NOT NULL DEFAULT clock_timestamp(),
            PRIMARY KEY (tenant_id, cost_sheet_id, version),
            FOREIGN KEY (tenant_id, cost_sheet_id) REFERENCES public."Fin_CostSheet" ("TenantId", "Id"),
            CHECK ((jsonb_typeof(basis->'Amounts')='object' AND jsonb_typeof(basis->'PolicyVersion')='string'
                AND jsonb_typeof(basis->'Rates')='array' AND jsonb_array_length(basis->'Rates')>0) IS TRUE)
        );

        CREATE VIEW cp6_fin_cost_basis_v1.current_rates AS
            SELECT v.tenant_id, v.cost_sheet_id, (r.value->>'RateId')::uuid AS rate_id,
                (r.value->>'WorkCenterId')::uuid AS work_center_id, r.value->>'WorkCenter' AS work_center
            FROM (
                SELECT DISTINCT ON (tenant_id, cost_sheet_id) tenant_id, cost_sheet_id, basis
                FROM cp6_fin_cost_basis_v1.versions ORDER BY tenant_id, cost_sheet_id, version DESC
            ) v
            CROSS JOIN LATERAL jsonb_array_elements(v.basis->'Rates') AS r(value);

        CREATE TABLE cp6_fin_cost_basis_v1.settlements (
            tenant_id uuid NOT NULL,
            cost_sheet_id uuid NOT NULL,
            basis_version bigint NOT NULL,
            wip_journal_id uuid REFERENCES public."Fin_JournalEntry" ("Id"),
            fg_journal_id uuid REFERENCES public."Fin_JournalEntry" ("Id"),
            variance_journal_id uuid REFERENCES public."Fin_JournalEntry" ("Id"),
            actor text NOT NULL,
            recorded_at timestamp with time zone NOT NULL DEFAULT clock_timestamp(),
            PRIMARY KEY (tenant_id, cost_sheet_id),
            FOREIGN KEY (tenant_id, cost_sheet_id, basis_version)
                REFERENCES cp6_fin_cost_basis_v1.versions (tenant_id, cost_sheet_id, version),
            CHECK ((wip_journal_id IS NULL)=(fg_journal_id IS NULL)),
            CHECK (variance_journal_id IS NULL OR fg_journal_id IS NOT NULL)
        );

        CREATE FUNCTION cp6_fin_cost_basis_v1.immutable_result() RETURNS trigger
        LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        BEGIN
            RAISE EXCEPTION 'COST_BASIS_IMMUTABLE' USING ERRCODE = '55000';
        END;
        $$;

        CREATE TRIGGER cost_basis_immutable BEFORE UPDATE OR DELETE ON cp6_fin_cost_basis_v1.versions
            FOR EACH ROW EXECUTE FUNCTION cp6_fin_cost_basis_v1.immutable_result();
        CREATE TRIGGER cost_basis_keep_versions BEFORE TRUNCATE ON cp6_fin_cost_basis_v1.versions
            FOR EACH STATEMENT EXECUTE FUNCTION cp6_fin_cost_basis_v1.immutable_result();
        CREATE TRIGGER cost_settlement_immutable BEFORE UPDATE OR DELETE ON cp6_fin_cost_basis_v1.settlements
            FOR EACH ROW EXECUTE FUNCTION cp6_fin_cost_basis_v1.immutable_result();
        CREATE TRIGGER cost_settlement_keep_results BEFORE TRUNCATE ON cp6_fin_cost_basis_v1.settlements
            FOR EACH STATEMENT EXECUTE FUNCTION cp6_fin_cost_basis_v1.immutable_result();
        """);

    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("成本归集版本与结转依据只支持前向迁移。");
}
