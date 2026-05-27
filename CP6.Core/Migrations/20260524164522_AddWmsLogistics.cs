using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddWmsLogistics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "T_CrossDockOrder",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    XDockNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    InboundNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    OutboundNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ProductCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ProductName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Qty = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    SupplierCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CustomerCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    FromDock = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ToDock = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    WarehouseCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    TempLocationCd = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    LotNo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    InTxnNo = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: true),
                    OutTxnNo = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: true),
                    ExecutedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OperatorCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
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
                    table.PrimaryKey("PK_T_CrossDockOrder", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "T_ReplenishOrder",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReplenishNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    ProductCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ProductName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    WarehouseCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    FromLocationCd = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ToLocationCd = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    LotNo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Qty = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    UnitCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    TriggerType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    OutTxnNo = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: true),
                    InTxnNo = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: true),
                    ExecutedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OperatorCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
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
                    table.PrimaryKey("PK_T_ReplenishOrder", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "T_SlottingPlan",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SlottingPlanNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    WarehouseCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    AnalysisDays = table.Column<int>(type: "int", nullable: false),
                    TxnSampleCount = table.Column<int>(type: "int", nullable: false),
                    AnalyzedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RecommendationCount = table.Column<int>(type: "int", nullable: false),
                    ApproverCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    RecommendationsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
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
                    table.PrimaryKey("PK_T_SlottingPlan", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_T_CrossDockOrder_InboundNo_IsDeleted",
                table: "T_CrossDockOrder",
                columns: new[] { "InboundNo", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_CrossDockOrder_OutboundNo_IsDeleted",
                table: "T_CrossDockOrder",
                columns: new[] { "OutboundNo", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_CrossDockOrder_ProductCd_IsDeleted",
                table: "T_CrossDockOrder",
                columns: new[] { "ProductCd", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_CrossDockOrder_Status_IsDeleted",
                table: "T_CrossDockOrder",
                columns: new[] { "Status", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_CrossDockOrder_XDockNo",
                table: "T_CrossDockOrder",
                column: "XDockNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_ReplenishOrder_Priority_Status",
                table: "T_ReplenishOrder",
                columns: new[] { "Priority", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_T_ReplenishOrder_ProductCd_IsDeleted",
                table: "T_ReplenishOrder",
                columns: new[] { "ProductCd", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_ReplenishOrder_ReplenishNo",
                table: "T_ReplenishOrder",
                column: "ReplenishNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_ReplenishOrder_Status_IsDeleted",
                table: "T_ReplenishOrder",
                columns: new[] { "Status", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_ReplenishOrder_WarehouseCd_Status",
                table: "T_ReplenishOrder",
                columns: new[] { "WarehouseCd", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_T_SlottingPlan_AnalyzedAt_IsDeleted",
                table: "T_SlottingPlan",
                columns: new[] { "AnalyzedAt", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_SlottingPlan_SlottingPlanNo",
                table: "T_SlottingPlan",
                column: "SlottingPlanNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_SlottingPlan_Status_IsDeleted",
                table: "T_SlottingPlan",
                columns: new[] { "Status", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_SlottingPlan_WarehouseCd_IsDeleted",
                table: "T_SlottingPlan",
                columns: new[] { "WarehouseCd", "IsDeleted" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "T_CrossDockOrder");

            migrationBuilder.DropTable(
                name: "T_ReplenishOrder");

            migrationBuilder.DropTable(
                name: "T_SlottingPlan");
        }
    }
}
