using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class Phase9AddMaterialShortage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "T_MaterialShortage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkOrderNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RelatedOutboundNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ProductCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LotNo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    RequiredQty = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    AvailableQty = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    DetectedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Remark = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_T_MaterialShortage", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_T_MaterialShortage_Status_DetectedAt",
                table: "T_MaterialShortage",
                columns: new[] { "Status", "DetectedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_T_MaterialShortage_WorkOrderNo",
                table: "T_MaterialShortage",
                column: "WorkOrderNo");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "T_MaterialShortage");
        }
    }
}
