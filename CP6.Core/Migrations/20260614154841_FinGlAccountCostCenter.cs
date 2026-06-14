using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class FinGlAccountCostCenter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Fin_CostCenter",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LinkMachineId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fin_CostCenter", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Fin_GlAccount",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    NormalSide = table.Column<int>(type: "int", nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Level = table.Column<int>(type: "int", nullable: false),
                    IsLeaf = table.Column<bool>(type: "bit", nullable: false),
                    IsControl = table.Column<bool>(type: "bit", nullable: false),
                    SubLedgerType = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    RequirePartner = table.Column<bool>(type: "bit", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    StandardScheme = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CurrencyCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fin_GlAccount", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Fin_CostCenter_Code",
                table: "Fin_CostCenter",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Fin_CostCenter_ParentId",
                table: "Fin_CostCenter",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_Fin_GlAccount_Code",
                table: "Fin_GlAccount",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Fin_GlAccount_ParentId",
                table: "Fin_GlAccount",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_Fin_GlAccount_Role",
                table: "Fin_GlAccount",
                column: "Role");

            migrationBuilder.CreateIndex(
                name: "IX_Fin_GlAccount_StandardScheme_IsActive",
                table: "Fin_GlAccount",
                columns: new[] { "StandardScheme", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Fin_CostCenter");

            migrationBuilder.DropTable(
                name: "Fin_GlAccount");
        }
    }
}
