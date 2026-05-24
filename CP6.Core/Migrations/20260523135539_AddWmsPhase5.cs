using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddWmsPhase5 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "T_QcInspection",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InspectionNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    InboundNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    SupplierCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    SupplierName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ArrivalDateTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    InspectorCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    FinalJudgement = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    JudgementReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    GeneratedReceiptNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    PhotoUrls = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
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
                    table.PrimaryKey("PK_T_QcInspection", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "T_QcInspectionItem",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InspectionNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    ProductCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ProductName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ExpectedQty = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    ReceivedQty = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    AcceptedQty = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    RejectedQty = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    PendingQty = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    DefectReasonCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CheckItemsJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
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
                    table.PrimaryKey("PK_T_QcInspectionItem", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "T_RmaDetail",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RmaNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    ProductCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ProductName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LotNo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Qty = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    UnitCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    ConditionLevel = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Judgement = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    DestLocationCd = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    InboundTxnNo = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: true),
                    DispositionTxnNo = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: true),
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
                    table.PrimaryKey("PK_T_RmaDetail", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "T_RmaHeader",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RmaNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CustomerCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CustomerName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    OriginalShippingNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ReturnReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AppliedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    WarehouseCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_T_RmaHeader", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_T_QcInspection_ArrivalDateTime_IsDeleted",
                table: "T_QcInspection",
                columns: new[] { "ArrivalDateTime", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_QcInspection_InboundNo_IsDeleted",
                table: "T_QcInspection",
                columns: new[] { "InboundNo", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_QcInspection_InspectionNo",
                table: "T_QcInspection",
                column: "InspectionNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_QcInspection_Status_IsDeleted",
                table: "T_QcInspection",
                columns: new[] { "Status", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_QcInspection_SupplierCd",
                table: "T_QcInspection",
                column: "SupplierCd");

            migrationBuilder.CreateIndex(
                name: "IX_T_QcInspectionItem_InspectionNo_LineNo",
                table: "T_QcInspectionItem",
                columns: new[] { "InspectionNo", "LineNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_QcInspectionItem_ProductCd",
                table: "T_QcInspectionItem",
                column: "ProductCd");

            migrationBuilder.CreateIndex(
                name: "IX_T_RmaDetail_Judgement",
                table: "T_RmaDetail",
                column: "Judgement");

            migrationBuilder.CreateIndex(
                name: "IX_T_RmaDetail_ProductCd_LotNo",
                table: "T_RmaDetail",
                columns: new[] { "ProductCd", "LotNo" });

            migrationBuilder.CreateIndex(
                name: "IX_T_RmaDetail_RmaNo_LineNo",
                table: "T_RmaDetail",
                columns: new[] { "RmaNo", "LineNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_RmaHeader_AppliedDate_IsDeleted",
                table: "T_RmaHeader",
                columns: new[] { "AppliedDate", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_RmaHeader_CustomerCd_IsDeleted",
                table: "T_RmaHeader",
                columns: new[] { "CustomerCd", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_RmaHeader_OriginalShippingNo_IsDeleted",
                table: "T_RmaHeader",
                columns: new[] { "OriginalShippingNo", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_RmaHeader_RmaNo",
                table: "T_RmaHeader",
                column: "RmaNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_RmaHeader_Status_IsDeleted",
                table: "T_RmaHeader",
                columns: new[] { "Status", "IsDeleted" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "T_QcInspection");

            migrationBuilder.DropTable(
                name: "T_QcInspectionItem");

            migrationBuilder.DropTable(
                name: "T_RmaDetail");

            migrationBuilder.DropTable(
                name: "T_RmaHeader");
        }
    }
}
