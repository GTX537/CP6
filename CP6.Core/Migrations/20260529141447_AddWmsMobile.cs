using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddWmsMobile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "T_MobileTask",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MobileTaskNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TaskType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AssignedTo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RelatedNo = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: true),
                    RelatedType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ProductCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ProductName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LotNo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    WarehouseCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    FromLocationCd = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ToLocationCd = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Qty = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    ScannedQty = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    UnitCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Instruction = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DoneAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OutTxnNo = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: true),
                    InTxnNo = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: true),
                    Remarks = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_T_MobileTask", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_T_MobileTask_AssignedTo_Status_IsDeleted",
                table: "T_MobileTask",
                columns: new[] { "AssignedTo", "Status", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_MobileTask_MobileTaskNo",
                table: "T_MobileTask",
                column: "MobileTaskNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_MobileTask_Priority_Status",
                table: "T_MobileTask",
                columns: new[] { "Priority", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_T_MobileTask_RelatedNo",
                table: "T_MobileTask",
                column: "RelatedNo");

            migrationBuilder.CreateIndex(
                name: "IX_T_MobileTask_TaskType_Status",
                table: "T_MobileTask",
                columns: new[] { "TaskType", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "T_MobileTask");
        }
    }
}
