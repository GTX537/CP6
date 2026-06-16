using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class MultiTenantBiz : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_WorkOrderProcess",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_WorkOrderMaterial",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_WorkOrder",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_WmsSequence",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_WebBusinessPartner",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_WcsTask",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_Warehouse",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_VmiBilling",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_StockTransaction",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_StockTakeDetail",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_StockTake",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_Stock",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_SlottingPlan",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_ShippingPackage",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_SheetUnitPriceEstimate",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_SheetUnitPrice",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_SampleStock",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_RmaHeader",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_RmaDetail",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_ReplenishOrder",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_RemnantMaterial",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_QuotationDetail",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_QuotationCalc",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_Quotation",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_QualityInspectionItem",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_QualityInspection",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_QcInspectionItem",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_QcInspection",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_ProductProcess",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_ProductMaterial",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_ProductMaster",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_ProductLotPrice",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_ProductionResult",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_ProductCoProduct",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_PlateMoldStock",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_PlateMold",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_PaperRoll",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_Pallet",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_OutboundRoutingRule",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_OutboundOrderDetail",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_OutboundOrder",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_OrderProcessNote",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_OrderProcess",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_OrderMaterial",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_OrderDetail",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_Order",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_OeeDaily",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_MobileTask",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_MaterialShortage",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_MachineDowntime",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_Location",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_KitOrder",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_KitMasterComponent",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_KitMaster",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_IotSensorReading",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_IotSensor",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_IntegrationEvent",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_InkLot",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_InkColorMatchHistory",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_InboundReceiptDetail",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_InboundReceipt",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_InboundOrderDetail",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_InboundOrder",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_FxRate",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_FscChecklist",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_EstimateCalcProcess",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_EstimateCalc",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_DefectRecord",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_CrossDockOrder",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_CreditNote",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "T_CarrierShipment",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "M_Staff",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "M_Machine",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "M_InspectionTemplate",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "M_GenericCode",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "M_DefectCategory",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "M_Base",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_WorkOrderProcess");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_WorkOrderMaterial");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_WorkOrder");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_WmsSequence");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_WebBusinessPartner");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_WcsTask");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_Warehouse");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_VmiBilling");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_StockTransaction");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_StockTakeDetail");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_StockTake");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_Stock");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_SlottingPlan");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_ShippingPackage");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_SheetUnitPriceEstimate");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_SheetUnitPrice");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_SampleStock");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_RmaHeader");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_RmaDetail");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_ReplenishOrder");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_RemnantMaterial");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_QuotationDetail");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_QuotationCalc");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_Quotation");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_QualityInspectionItem");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_QualityInspection");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_QcInspectionItem");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_QcInspection");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_ProductProcess");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_ProductMaterial");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_ProductMaster");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_ProductLotPrice");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_ProductionResult");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_ProductCoProduct");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_PlateMoldStock");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_PlateMold");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_PaperRoll");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_Pallet");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_OutboundRoutingRule");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_OutboundOrderDetail");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_OutboundOrder");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_OrderProcessNote");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_OrderProcess");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_OrderMaterial");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_OrderDetail");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_Order");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_OeeDaily");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_MobileTask");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_MaterialShortage");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_MachineDowntime");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_Location");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_KitOrder");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_KitMasterComponent");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_KitMaster");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_IotSensorReading");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_IotSensor");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_IntegrationEvent");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_InkLot");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_InkColorMatchHistory");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_InboundReceiptDetail");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_InboundReceipt");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_InboundOrderDetail");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_InboundOrder");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_FxRate");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_FscChecklist");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_EstimateCalcProcess");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_EstimateCalc");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_DefectRecord");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_CrossDockOrder");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_CreditNote");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "T_CarrierShipment");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "M_Staff");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "M_Machine");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "M_InspectionTemplate");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "M_GenericCode");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "M_DefectCategory");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "M_Base");
        }
    }
}
