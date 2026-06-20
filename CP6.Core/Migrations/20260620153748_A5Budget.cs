using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class A5Budget : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Fin_Budget",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    No = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FiscalYear = table.Column<int>(type: "int", nullable: false),
                    Scope = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fin_Budget", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Fin_BudgetLine",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CostCenterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CostObjectType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CostObjectId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CostCenterKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CostObjectTypeKey = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CostObjectIdKey = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    AnnualAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ControlMode = table.Column<int>(type: "int", nullable: true),
                    ControlBasis = table.Column<int>(type: "int", nullable: true),
                    Memo = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fin_BudgetLine", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Fin_BudgetLinePeriod",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BudgetLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PeriodNo = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fin_BudgetLinePeriod", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Fin_BudgetVersion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BudgetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionNo = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    DefaultControlMode = table.Column<int>(type: "int", nullable: false),
                    DefaultControlBasis = table.Column<int>(type: "int", nullable: false),
                    ApprovalInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApprovalRef = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SubmittedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RejectReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fin_BudgetVersion", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "UX_Fin_Budget_FiscalYear",
                table: "Fin_Budget",
                columns: new[] { "TenantId", "FiscalYear" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Fin_BudgetLine_Dim",
                table: "Fin_BudgetLine",
                columns: new[] { "TenantId", "VersionId", "AccountId", "CostCenterKey", "CostObjectTypeKey", "CostObjectIdKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Fin_BudgetLinePeriod_LinePeriod",
                table: "Fin_BudgetLinePeriod",
                columns: new[] { "TenantId", "BudgetLineId", "PeriodNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Fin_BudgetVersion_BudgetNo",
                table: "Fin_BudgetVersion",
                columns: new[] { "TenantId", "BudgetId", "VersionNo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Fin_Budget");

            migrationBuilder.DropTable(
                name: "Fin_BudgetLine");

            migrationBuilder.DropTable(
                name: "Fin_BudgetLinePeriod");

            migrationBuilder.DropTable(
                name: "Fin_BudgetVersion");
        }
    }
}
