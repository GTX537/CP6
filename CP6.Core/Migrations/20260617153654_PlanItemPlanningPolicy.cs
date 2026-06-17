using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class PlanItemPlanningPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Plan_ItemPlanningPolicy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SafetyStock = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    PurchaseLeadDays = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    LotRule = table.Column<int>(type: "int", nullable: false),
                    MoqQty = table.Column<decimal>(type: "decimal(21,8)", nullable: true),
                    MultipleQty = table.Column<decimal>(type: "decimal(21,8)", nullable: true),
                    MakeOrBuy = table.Column<int>(type: "int", nullable: false),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Plan_ItemPlanningPolicy", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "UX_Plan_ItemPolicy_ItemCd",
                table: "Plan_ItemPlanningPolicy",
                columns: new[] { "TenantId", "ItemCd" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Plan_ItemPlanningPolicy");
        }
    }
}
