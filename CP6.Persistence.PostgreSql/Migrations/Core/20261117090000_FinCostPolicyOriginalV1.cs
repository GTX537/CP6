using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261117090000_FinCostPolicyOriginalV1")]
public sealed class FinCostPolicyOriginalV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE cp6_fin_mfg_v1.policy_originals (
            tenant_id uuid NOT NULL, ledger_id uuid NOT NULL, version_id uuid NOT NULL,
            original jsonb NOT NULL,
            PRIMARY KEY (tenant_id,ledger_id,version_id),
            FOREIGN KEY (tenant_id,ledger_id,version_id) REFERENCES cp6_fin_mfg_v1.policies,
            CHECK ((original->'policy'->>'schema'='COST-POLICY-1') IS TRUE)
        );
        CREATE TRIGGER policy_original_immutable BEFORE UPDATE OR DELETE ON cp6_fin_mfg_v1.policy_originals
            FOR EACH ROW EXECUTE FUNCTION cp6_fin_mfg_v1.immutable_source();
        CREATE TRIGGER policy_original_keep BEFORE TRUNCATE ON cp6_fin_mfg_v1.policy_originals
            FOR EACH STATEMENT EXECUTE FUNCTION cp6_fin_mfg_v1.immutable_source();
        """);

    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("成本政策原件只支持前向迁移。");
}
