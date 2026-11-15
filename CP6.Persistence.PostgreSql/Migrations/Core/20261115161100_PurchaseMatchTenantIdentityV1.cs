using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context))]
[Migration("20261115161100_PurchaseMatchTenantIdentityV1")]
public sealed class PurchaseMatchTenantIdentityV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddUniqueConstraint("AK_Pur_ThreeWayMatch_TenantId_MatchNo", "Pur_ThreeWayMatch",
            new[] { "TenantId", "MatchNo" });
        // Validate the new relationship against existing rows before removing the original keys.
        migrationBuilder.AddForeignKey("FK_Pur_ThreeWayMatchLine_TenantId_MatchNo", "Pur_ThreeWayMatchLine",
            new[] { "TenantId", "MatchNo" }, "Pur_ThreeWayMatch",
            principalColumns: new[] { "TenantId", "MatchNo" }, onDelete: ReferentialAction.Cascade);
        migrationBuilder.DropForeignKey("FK_Pur_ThreeWayMatchLine_Pur_ThreeWayMatch_MatchNo", "Pur_ThreeWayMatchLine");
        migrationBuilder.DropUniqueConstraint("AK_Pur_ThreeWayMatch_MatchNo", "Pur_ThreeWayMatch");
        migrationBuilder.DropIndex("IX_Pur_ThreeWayMatch_MatchNo", "Pur_ThreeWayMatch");
        migrationBuilder.DropIndex("IX_Pur_ThreeWayMatchLine_MatchNo", "Pur_ThreeWayMatchLine");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("Tenant-scoped match numbers require forward migration.");
}
