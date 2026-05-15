using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddMesModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "M_DefectCategory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CategoryCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    DetailCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    CategoryName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    DetailName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    ActiveFlg = table.Column<bool>(type: "bit", nullable: false),
                    Remarks = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_M_DefectCategory", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "M_InspectionTemplate",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ItemSeqNo = table.Column<int>(type: "int", nullable: false),
                    TemplateName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ItemName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    InspectionMethod = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    StandardValue = table.Column<decimal>(type: "decimal(21,8)", nullable: true),
                    UpperLimit = table.Column<decimal>(type: "decimal(21,8)", nullable: true),
                    LowerLimit = table.Column<decimal>(type: "decimal(21,8)", nullable: true),
                    Unit = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    ProcessCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    RequiredFlg = table.Column<bool>(type: "bit", nullable: false),
                    ActiveFlg = table.Column<bool>(type: "bit", nullable: false),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_M_InspectionTemplate", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "T_DefectRecord",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DefectNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    WorkOrderNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ProcessCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    InspectionNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    OccurDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReporterCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CategoryCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    DetailCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    DefectQty = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    DefectDescription = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CauseAnalysis = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CorrectiveAction = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    AssigneeCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_T_DefectRecord", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "T_MesSequence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SeqKey = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    SeqDate = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    CurrentValue = table.Column<int>(type: "int", nullable: false),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_T_MesSequence", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "T_ProductionResult",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResultNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    WorkOrderNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ProcessCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    TaskCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    ResultType = table.Column<int>(type: "int", nullable: false),
                    OperatorCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OperatorName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ActualStartTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ActualEndTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    GoodQty = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    DefectQty = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    ActualLossRate = table.Column<decimal>(type: "decimal(8,4)", nullable: true),
                    DefectReasonCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    SuspendReasonCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    MachineCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ResultNote = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_T_ProductionResult", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "T_QualityInspection",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InspectionNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    WorkOrderNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ProcessCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    InspectionDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    InspectorCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    InspectorName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    InspectionType = table.Column<int>(type: "int", nullable: false),
                    TemplateCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    InspectionQty = table.Column<decimal>(type: "decimal(21,8)", nullable: true),
                    SampleQty = table.Column<decimal>(type: "decimal(21,8)", nullable: true),
                    OverallResult = table.Column<int>(type: "int", nullable: true),
                    DispositionAction = table.Column<int>(type: "int", nullable: true),
                    JudgmentReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("PK_T_QualityInspection", x => x.Id);
                    table.UniqueConstraint("AK_T_QualityInspection_InspectionNo", x => x.InspectionNo);
                });

            migrationBuilder.CreateTable(
                name: "T_WorkOrder",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkOrderNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    OrderNo1 = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    OrderNo2 = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    OrderNo3 = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    WebOrderNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CustomerCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ProductCd = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    ProductName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ProductionQty = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    CompletedQty = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    DefectQty = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    DeliveryDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PlanStartDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PlanEndDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ActualStartDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ActualEndDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    LotNo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    BaseCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
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
                    table.PrimaryKey("PK_T_WorkOrder", x => x.Id);
                    table.UniqueConstraint("AK_T_WorkOrder_WorkOrderNo", x => x.WorkOrderNo);
                });

            migrationBuilder.CreateTable(
                name: "T_QualityInspectionItem",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InspectionNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ItemSeqNo = table.Column<int>(type: "int", nullable: false),
                    ItemName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    InspectionMethod = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    StandardValue = table.Column<decimal>(type: "decimal(21,8)", nullable: true),
                    UpperLimit = table.Column<decimal>(type: "decimal(21,8)", nullable: true),
                    LowerLimit = table.Column<decimal>(type: "decimal(21,8)", nullable: true),
                    MeasuredValue = table.Column<decimal>(type: "decimal(21,8)", nullable: true),
                    MeasuredText = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Result = table.Column<int>(type: "int", nullable: true),
                    Unit = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Remarks = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_T_QualityInspectionItem", x => x.Id);
                    table.ForeignKey(
                        name: "FK_T_QualityInspectionItem_T_QualityInspection_InspectionNo",
                        column: x => x.InspectionNo,
                        principalTable: "T_QualityInspection",
                        principalColumn: "InspectionNo",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "T_WorkOrderMaterial",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkOrderNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ProcessCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    MaterialCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    MaterialName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    MaterialTypeDiv = table.Column<string>(type: "nvarchar(1)", maxLength: 1, nullable: true),
                    PlanQty = table.Column<decimal>(type: "decimal(21,8)", nullable: true),
                    ActualQty = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    SupplyStatus = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Remarks = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_T_WorkOrderMaterial", x => x.Id);
                    table.ForeignKey(
                        name: "FK_T_WorkOrderMaterial_T_WorkOrder_WorkOrderNo",
                        column: x => x.WorkOrderNo,
                        principalTable: "T_WorkOrder",
                        principalColumn: "WorkOrderNo",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "T_WorkOrderProcess",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkOrderNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ProcessCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    TaskCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    ProcessName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    ProcessStatus = table.Column<int>(type: "int", nullable: false),
                    MachineCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    WgCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    PlanStartTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PlanEndTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ActualStartTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ActualEndTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PlanQty = table.Column<decimal>(type: "decimal(21,8)", nullable: true),
                    GoodQty = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    DefectQty = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    StdLossRate = table.Column<decimal>(type: "decimal(21,8)", nullable: true),
                    LeadTime = table.Column<decimal>(type: "decimal(21,8)", nullable: true),
                    PrevProcessCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Remarks = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_T_WorkOrderProcess", x => x.Id);
                    table.ForeignKey(
                        name: "FK_T_WorkOrderProcess_T_WorkOrder_WorkOrderNo",
                        column: x => x.WorkOrderNo,
                        principalTable: "T_WorkOrder",
                        principalColumn: "WorkOrderNo",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_M_DefectCategory_ActiveFlg",
                table: "M_DefectCategory",
                column: "ActiveFlg");

            migrationBuilder.CreateIndex(
                name: "IX_M_DefectCategory_CategoryCd_DetailCd",
                table: "M_DefectCategory",
                columns: new[] { "CategoryCd", "DetailCd" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_M_InspectionTemplate_ProcessCd",
                table: "M_InspectionTemplate",
                column: "ProcessCd");

            migrationBuilder.CreateIndex(
                name: "IX_M_InspectionTemplate_TemplateCd_ActiveFlg",
                table: "M_InspectionTemplate",
                columns: new[] { "TemplateCd", "ActiveFlg" });

            migrationBuilder.CreateIndex(
                name: "IX_M_InspectionTemplate_TemplateCd_ItemSeqNo",
                table: "M_InspectionTemplate",
                columns: new[] { "TemplateCd", "ItemSeqNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_DefectRecord_CategoryCd_DetailCd",
                table: "T_DefectRecord",
                columns: new[] { "CategoryCd", "DetailCd" });

            migrationBuilder.CreateIndex(
                name: "IX_T_DefectRecord_DefectNo",
                table: "T_DefectRecord",
                column: "DefectNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_DefectRecord_OccurDate_IsDeleted",
                table: "T_DefectRecord",
                columns: new[] { "OccurDate", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_DefectRecord_Status_IsDeleted",
                table: "T_DefectRecord",
                columns: new[] { "Status", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_DefectRecord_WorkOrderNo_IsDeleted",
                table: "T_DefectRecord",
                columns: new[] { "WorkOrderNo", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_MesSequence_SeqKey_SeqDate",
                table: "T_MesSequence",
                columns: new[] { "SeqKey", "SeqDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_ProductionResult_ActualEndTime_IsDeleted",
                table: "T_ProductionResult",
                columns: new[] { "ActualEndTime", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_ProductionResult_ActualStartTime_IsDeleted",
                table: "T_ProductionResult",
                columns: new[] { "ActualStartTime", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_ProductionResult_OperatorCd_IsDeleted",
                table: "T_ProductionResult",
                columns: new[] { "OperatorCd", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_ProductionResult_ResultNo",
                table: "T_ProductionResult",
                column: "ResultNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_ProductionResult_ResultType",
                table: "T_ProductionResult",
                column: "ResultType");

            migrationBuilder.CreateIndex(
                name: "IX_T_ProductionResult_WorkOrderNo_ProcessCd_IsDeleted",
                table: "T_ProductionResult",
                columns: new[] { "WorkOrderNo", "ProcessCd", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_QualityInspection_InspectionDate_IsDeleted",
                table: "T_QualityInspection",
                columns: new[] { "InspectionDate", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_QualityInspection_InspectionNo",
                table: "T_QualityInspection",
                column: "InspectionNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_QualityInspection_OverallResult",
                table: "T_QualityInspection",
                column: "OverallResult");

            migrationBuilder.CreateIndex(
                name: "IX_T_QualityInspection_WorkOrderNo_IsDeleted",
                table: "T_QualityInspection",
                columns: new[] { "WorkOrderNo", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_QualityInspectionItem_InspectionNo_ItemSeqNo",
                table: "T_QualityInspectionItem",
                columns: new[] { "InspectionNo", "ItemSeqNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_WorkOrder_CustomerCd_IsDeleted",
                table: "T_WorkOrder",
                columns: new[] { "CustomerCd", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_WorkOrder_DeliveryDate_IsDeleted",
                table: "T_WorkOrder",
                columns: new[] { "DeliveryDate", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_WorkOrder_ProductCd_IsDeleted",
                table: "T_WorkOrder",
                columns: new[] { "ProductCd", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_WorkOrder_Status_IsDeleted",
                table: "T_WorkOrder",
                columns: new[] { "Status", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_WorkOrder_WebOrderNo",
                table: "T_WorkOrder",
                column: "WebOrderNo");

            migrationBuilder.CreateIndex(
                name: "IX_T_WorkOrder_WorkOrderNo",
                table: "T_WorkOrder",
                column: "WorkOrderNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_WorkOrderMaterial_WorkOrderNo_ProcessCd_MaterialCd",
                table: "T_WorkOrderMaterial",
                columns: new[] { "WorkOrderNo", "ProcessCd", "MaterialCd" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_WorkOrderMaterial_WorkOrderNo_SortOrder",
                table: "T_WorkOrderMaterial",
                columns: new[] { "WorkOrderNo", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_T_WorkOrderProcess_MachineCd",
                table: "T_WorkOrderProcess",
                column: "MachineCd");

            migrationBuilder.CreateIndex(
                name: "IX_T_WorkOrderProcess_ProcessStatus",
                table: "T_WorkOrderProcess",
                column: "ProcessStatus");

            migrationBuilder.CreateIndex(
                name: "IX_T_WorkOrderProcess_WgCd",
                table: "T_WorkOrderProcess",
                column: "WgCd");

            migrationBuilder.CreateIndex(
                name: "IX_T_WorkOrderProcess_WorkOrderNo_ProcessCd_TaskCd",
                table: "T_WorkOrderProcess",
                columns: new[] { "WorkOrderNo", "ProcessCd", "TaskCd" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_WorkOrderProcess_WorkOrderNo_SortOrder",
                table: "T_WorkOrderProcess",
                columns: new[] { "WorkOrderNo", "SortOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "M_DefectCategory");

            migrationBuilder.DropTable(
                name: "M_InspectionTemplate");

            migrationBuilder.DropTable(
                name: "T_DefectRecord");

            migrationBuilder.DropTable(
                name: "T_MesSequence");

            migrationBuilder.DropTable(
                name: "T_ProductionResult");

            migrationBuilder.DropTable(
                name: "T_QualityInspectionItem");

            migrationBuilder.DropTable(
                name: "T_WorkOrderMaterial");

            migrationBuilder.DropTable(
                name: "T_WorkOrderProcess");

            migrationBuilder.DropTable(
                name: "T_QualityInspection");

            migrationBuilder.DropTable(
                name: "T_WorkOrder");
        }
    }
}
