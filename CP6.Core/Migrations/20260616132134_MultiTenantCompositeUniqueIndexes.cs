using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class MultiTenantCompositeUniqueIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS [UX_Wf_FormDef_FormKey] ON [Wf_FormDef];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [UX_Wf_FlowDef_FlowKey] ON [Wf_FlowDef];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [UX_Wf_ApprovalBinding_BizType] ON [Wf_ApprovalBinding];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_WorkOrderProcess_WorkOrderNo_ProcessCd_TaskCd] ON [T_WorkOrderProcess];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_WorkOrderMaterial_WorkOrderNo_ProcessCd_MaterialCd] ON [T_WorkOrderMaterial];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_WmsSequence_Prefix_DateKey] ON [T_WmsSequence];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_WebBusinessPartner_BpCd] ON [T_WebBusinessPartner];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_WcsTask_TaskNo] ON [T_WcsTask];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_Warehouse_WarehouseCd] ON [T_Warehouse];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_VmiBilling_BillingNo] ON [T_VmiBilling];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_VmiBilling_CustomerCd_YearMonth] ON [T_VmiBilling];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_StockTransaction_TxnNo] ON [T_StockTransaction];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_StockTakeDetail_StockTakeNo_LineNo] ON [T_StockTakeDetail];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_StockTake_StockTakeNo] ON [T_StockTake];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [UX_Stock_WLPL] ON [T_Stock];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_SlottingPlan_SlottingPlanNo] ON [T_SlottingPlan];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_ShippingPackage_PackageNo] ON [T_ShippingPackage];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [UX_SheetUnitPriceEst_Pk13] ON [T_SheetUnitPriceEstimate];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [UX_SheetUnitPrice_Pk13] ON [T_SheetUnitPrice];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_SampleStock_SampleNo] ON [T_SampleStock];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_RmaHeader_RmaNo] ON [T_RmaHeader];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_RmaDetail_RmaNo_LineNo] ON [T_RmaDetail];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_ReplenishOrder_ReplenishNo] ON [T_ReplenishOrder];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_RemnantMaterial_RemnantNo] ON [T_RemnantMaterial];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_QuotationDetail_QtnNo_DetailNo] ON [T_QuotationDetail];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_QuotationCalc_QtnNo_QtnCalcNo] ON [T_QuotationCalc];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_QualityInspectionItem_InspectionNo_ItemSeqNo] ON [T_QualityInspectionItem];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_QcInspectionItem_InspectionNo_LineNo] ON [T_QcInspectionItem];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_QcInspection_InspectionNo] ON [T_QcInspection];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_ProductProcess_ProductCd_TaskCd] ON [T_ProductProcess];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_ProductMaterial_ProductCd_ProcessCd_MaterialCd] ON [T_ProductMaterial];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_ProductLotPrice_ProductCd_DetailNo] ON [T_ProductLotPrice];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_ProductionResult_ResultNo] ON [T_ProductionResult];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_ProductCoProduct_ProductCd_ProcessCd_RowNo] ON [T_ProductCoProduct];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_PlateMoldStock_PlateNo] ON [T_PlateMoldStock];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_PlateMold_WdPtnNo_WdRev] ON [T_PlateMold];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_PaperRoll_RollNo] ON [T_PaperRoll];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_Pallet_PalletNo] ON [T_Pallet];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_OutboundOrderDetail_OutboundNo_LineNo] ON [T_OutboundOrderDetail];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_OutboundOrder_OutboundNo] ON [T_OutboundOrder];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_OrderProcessNote_WebOrderNo_WebOrderDetailNo_ProductCd_OperationCd] ON [T_OrderProcessNote];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_OrderProcess_WebOrderNo_WebOrderDetailNo_ProductCd_OperationCd] ON [T_OrderProcess];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_OrderMaterial_WebOrderNo_WebOrderDetailNo_ProductCd_ProcessCd_MaterialCd] ON [T_OrderMaterial];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_OrderDetail_WebOrderNo_WebOrderDetailNo] ON [T_OrderDetail];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_OeeDaily_OeeDate_MachineCd] ON [T_OeeDaily];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_MobileTask_MobileTaskNo] ON [T_MobileTask];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_MachineDowntime_DowntimeNo] ON [T_MachineDowntime];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_Location_LocationCd] ON [T_Location];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_KitOrder_KitOrderNo] ON [T_KitOrder];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_KitMasterComponent_KitSku_LineNo] ON [T_KitMasterComponent];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_KitMaster_KitSku] ON [T_KitMaster];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_IotSensor_SensorId] ON [T_IotSensor];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_InkLot_InkLotNo] ON [T_InkLot];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_InkColorMatchHistory_MatchNo] ON [T_InkColorMatchHistory];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_InboundReceiptDetail_ReceiptNo_LineNo] ON [T_InboundReceiptDetail];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_InboundReceipt_ReceiptNo] ON [T_InboundReceipt];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_InboundOrderDetail_InboundNo_LineNo] ON [T_InboundOrderDetail];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_InboundOrder_InboundNo] ON [T_InboundOrder];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_FscChecklist_FscManagementNo] ON [T_FscChecklist];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_EstimateCalcProcess_QtnCalcNo_SeqNo] ON [T_EstimateCalcProcess];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_DefectRecord_DefectNo] ON [T_DefectRecord];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_CrossDockOrder_XDockNo] ON [T_CrossDockOrder];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_CreditNote_CreditNoteNo] ON [T_CreditNote];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_CarrierShipment_ShipmentNo] ON [T_CarrierShipment];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_Sys_UserRole_UserId_RoleId] ON [Sys_UserRole];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [UX_Sys_RoleFieldPerm_RoleResourceField] ON [Sys_RoleFieldPerm];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [UX_Sys_RoleDataScope_RoleResource] ON [Sys_RoleDataScope];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [UX_Sys_RoleAction_RoleMenuAction] ON [Sys_RoleAction];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [UX_Sys_MenuAction_MenuAction] ON [Sys_MenuAction];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_Sys_Dept_DeptCode] ON [Sys_Dept];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [UX_Pub_GenTable_Entity] ON [Pub_GenTable];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [UX_Pub_DocSequence_BizKey] ON [Pub_DocSequence];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_M_Staff_StaffCd] ON [M_Staff];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_M_Machine_MachineCd] ON [M_Machine];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_M_InspectionTemplate_TemplateCd_ItemSeqNo] ON [M_InspectionTemplate];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_M_GenericCode_GroupCode_Code] ON [M_GenericCode];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_M_DefectCategory_CategoryCd_DetailCd] ON [M_DefectCategory];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_M_Base_BaseCd] ON [M_Base];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_Fin_TaxCode_Code] ON [Fin_TaxCode];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_Fin_Sequence_SeqKey_SeqDate] ON [Fin_Sequence];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_Fin_Receipt_No] ON [Fin_Receipt];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_Fin_Payment_No] ON [Fin_Payment];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_Fin_JournalEntry_No] ON [Fin_JournalEntry];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [UX_Fin_JournalEntry_AutoVoucherSource] ON [Fin_JournalEntry];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_Fin_GlAccount_Code] ON [Fin_GlAccount];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_Fin_FiscalPeriod_Year_Month] ON [Fin_FiscalPeriod];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_Fin_CostCenter_Code] ON [Fin_CostCenter];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_Fin_BankAccount_Code] ON [Fin_BankAccount];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_Fin_ArInvoice_No] ON [Fin_ArInvoice];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [UX_Fin_ArInvoice_ShipmentDupGuard] ON [Fin_ArInvoice];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_Fin_ApInvoice_No] ON [Fin_ApInvoice];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [UX_Fin_ApInvoice_SupplierDupGuard] ON [Fin_ApInvoice];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [UX_Wf_FormDef_FormKey] ON [Wf_FormDef];");
            migrationBuilder.CreateIndex(
                name: "UX_Wf_FormDef_FormKey",
                table: "Wf_FormDef",
                columns: new[] { "TenantId", "FormKey" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [UX_Wf_FlowDef_FlowKey] ON [Wf_FlowDef];");
            migrationBuilder.CreateIndex(
                name: "UX_Wf_FlowDef_FlowKey",
                table: "Wf_FlowDef",
                columns: new[] { "TenantId", "FlowKey" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [UX_Wf_ApprovalBinding_BizType] ON [Wf_ApprovalBinding];");
            migrationBuilder.CreateIndex(
                name: "UX_Wf_ApprovalBinding_BizType",
                table: "Wf_ApprovalBinding",
                columns: new[] { "TenantId", "BizType" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_WorkOrderProcess_WorkOrderNo_ProcessCd_TaskCd] ON [T_WorkOrderProcess];");
            migrationBuilder.CreateIndex(
                name: "IX_T_WorkOrderProcess_WorkOrderNo_ProcessCd_TaskCd",
                table: "T_WorkOrderProcess",
                columns: new[] { "TenantId", "WorkOrderNo", "ProcessCd", "TaskCd" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_WorkOrderMaterial_WorkOrderNo_ProcessCd_MaterialCd] ON [T_WorkOrderMaterial];");
            migrationBuilder.CreateIndex(
                name: "IX_T_WorkOrderMaterial_WorkOrderNo_ProcessCd_MaterialCd",
                table: "T_WorkOrderMaterial",
                columns: new[] { "TenantId", "WorkOrderNo", "ProcessCd", "MaterialCd" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_WmsSequence_Prefix_DateKey] ON [T_WmsSequence];");
            migrationBuilder.CreateIndex(
                name: "IX_T_WmsSequence_Prefix_DateKey",
                table: "T_WmsSequence",
                columns: new[] { "TenantId", "Prefix", "DateKey" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_WebBusinessPartner_BpCd] ON [T_WebBusinessPartner];");
            migrationBuilder.CreateIndex(
                name: "IX_T_WebBusinessPartner_BpCd",
                table: "T_WebBusinessPartner",
                columns: new[] { "TenantId", "BpCd" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_WcsTask_TaskNo] ON [T_WcsTask];");
            migrationBuilder.CreateIndex(
                name: "IX_T_WcsTask_TaskNo",
                table: "T_WcsTask",
                columns: new[] { "TenantId", "TaskNo" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_Warehouse_WarehouseCd] ON [T_Warehouse];");
            migrationBuilder.CreateIndex(
                name: "IX_T_Warehouse_WarehouseCd",
                table: "T_Warehouse",
                columns: new[] { "TenantId", "WarehouseCd" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_VmiBilling_BillingNo] ON [T_VmiBilling];");
            migrationBuilder.CreateIndex(
                name: "IX_T_VmiBilling_BillingNo",
                table: "T_VmiBilling",
                columns: new[] { "TenantId", "BillingNo" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_VmiBilling_CustomerCd_YearMonth] ON [T_VmiBilling];");
            migrationBuilder.CreateIndex(
                name: "IX_T_VmiBilling_CustomerCd_YearMonth",
                table: "T_VmiBilling",
                columns: new[] { "TenantId", "CustomerCd", "YearMonth" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_StockTransaction_TxnNo] ON [T_StockTransaction];");
            migrationBuilder.CreateIndex(
                name: "IX_T_StockTransaction_TxnNo",
                table: "T_StockTransaction",
                columns: new[] { "TenantId", "TxnNo" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_StockTakeDetail_StockTakeNo_LineNo] ON [T_StockTakeDetail];");
            migrationBuilder.CreateIndex(
                name: "IX_T_StockTakeDetail_StockTakeNo_LineNo",
                table: "T_StockTakeDetail",
                columns: new[] { "TenantId", "StockTakeNo", "LineNo" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_StockTake_StockTakeNo] ON [T_StockTake];");
            migrationBuilder.CreateIndex(
                name: "IX_T_StockTake_StockTakeNo",
                table: "T_StockTake",
                columns: new[] { "TenantId", "StockTakeNo" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [UX_Stock_WLPL] ON [T_Stock];");
            migrationBuilder.CreateIndex(
                name: "UX_Stock_WLPL",
                table: "T_Stock",
                columns: new[] { "TenantId", "WarehouseCd", "LocationCd", "ProductCd", "LotNo" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_SlottingPlan_SlottingPlanNo] ON [T_SlottingPlan];");
            migrationBuilder.CreateIndex(
                name: "IX_T_SlottingPlan_SlottingPlanNo",
                table: "T_SlottingPlan",
                columns: new[] { "TenantId", "SlottingPlanNo" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_ShippingPackage_PackageNo] ON [T_ShippingPackage];");
            migrationBuilder.CreateIndex(
                name: "IX_T_ShippingPackage_PackageNo",
                table: "T_ShippingPackage",
                columns: new[] { "TenantId", "PackageNo" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [UX_SheetUnitPriceEst_Pk13] ON [T_SheetUnitPriceEstimate];");
            migrationBuilder.CreateIndex(
                name: "UX_SheetUnitPriceEst_Pk13",
                table: "T_SheetUnitPriceEstimate",
                columns: new[] { "TenantId", "RevisionDate", "BaseCd", "CustomerCd", "SheetFlute", "PaperCdF", "PrintCdF", "EmbossCdF", "PaperCdC", "PrintCdC", "EmbossCdC", "PaperCdB", "PrintCdB", "EmbossCdB" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [UX_SheetUnitPrice_Pk13] ON [T_SheetUnitPrice];");
            migrationBuilder.CreateIndex(
                name: "UX_SheetUnitPrice_Pk13",
                table: "T_SheetUnitPrice",
                columns: new[] { "TenantId", "RevisionDate", "BaseCd", "CustomerCd", "SheetFlute", "PaperCdF", "PrintCdF", "EmbossCdF", "PaperCdC", "PrintCdC", "EmbossCdC", "PaperCdB", "PrintCdB", "EmbossCdB" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_SampleStock_SampleNo] ON [T_SampleStock];");
            migrationBuilder.CreateIndex(
                name: "IX_T_SampleStock_SampleNo",
                table: "T_SampleStock",
                columns: new[] { "TenantId", "SampleNo" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_RmaHeader_RmaNo] ON [T_RmaHeader];");
            migrationBuilder.CreateIndex(
                name: "IX_T_RmaHeader_RmaNo",
                table: "T_RmaHeader",
                columns: new[] { "TenantId", "RmaNo" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_RmaDetail_RmaNo_LineNo] ON [T_RmaDetail];");
            migrationBuilder.CreateIndex(
                name: "IX_T_RmaDetail_RmaNo_LineNo",
                table: "T_RmaDetail",
                columns: new[] { "TenantId", "RmaNo", "LineNo" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_ReplenishOrder_ReplenishNo] ON [T_ReplenishOrder];");
            migrationBuilder.CreateIndex(
                name: "IX_T_ReplenishOrder_ReplenishNo",
                table: "T_ReplenishOrder",
                columns: new[] { "TenantId", "ReplenishNo" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_RemnantMaterial_RemnantNo] ON [T_RemnantMaterial];");
            migrationBuilder.CreateIndex(
                name: "IX_T_RemnantMaterial_RemnantNo",
                table: "T_RemnantMaterial",
                columns: new[] { "TenantId", "RemnantNo" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_QuotationDetail_QtnNo] ON [T_QuotationDetail];");
            migrationBuilder.CreateIndex(
                name: "IX_T_QuotationDetail_QtnNo",
                table: "T_QuotationDetail",
                column: "QtnNo");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_QuotationDetail_QtnNo_DetailNo] ON [T_QuotationDetail];");
            migrationBuilder.CreateIndex(
                name: "IX_T_QuotationDetail_QtnNo_DetailNo",
                table: "T_QuotationDetail",
                columns: new[] { "TenantId", "QtnNo", "DetailNo" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_QuotationCalc_QtnNo] ON [T_QuotationCalc];");
            migrationBuilder.CreateIndex(
                name: "IX_T_QuotationCalc_QtnNo",
                table: "T_QuotationCalc",
                column: "QtnNo");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_QuotationCalc_QtnNo_QtnCalcNo] ON [T_QuotationCalc];");
            migrationBuilder.CreateIndex(
                name: "IX_T_QuotationCalc_QtnNo_QtnCalcNo",
                table: "T_QuotationCalc",
                columns: new[] { "TenantId", "QtnNo", "QtnCalcNo" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_QualityInspectionItem_InspectionNo] ON [T_QualityInspectionItem];");
            migrationBuilder.CreateIndex(
                name: "IX_T_QualityInspectionItem_InspectionNo",
                table: "T_QualityInspectionItem",
                column: "InspectionNo");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_QualityInspectionItem_InspectionNo_ItemSeqNo] ON [T_QualityInspectionItem];");
            migrationBuilder.CreateIndex(
                name: "IX_T_QualityInspectionItem_InspectionNo_ItemSeqNo",
                table: "T_QualityInspectionItem",
                columns: new[] { "TenantId", "InspectionNo", "ItemSeqNo" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_QcInspectionItem_InspectionNo_LineNo] ON [T_QcInspectionItem];");
            migrationBuilder.CreateIndex(
                name: "IX_T_QcInspectionItem_InspectionNo_LineNo",
                table: "T_QcInspectionItem",
                columns: new[] { "TenantId", "InspectionNo", "LineNo" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_QcInspection_InspectionNo] ON [T_QcInspection];");
            migrationBuilder.CreateIndex(
                name: "IX_T_QcInspection_InspectionNo",
                table: "T_QcInspection",
                columns: new[] { "TenantId", "InspectionNo" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_ProductProcess_ProductCd_TaskCd] ON [T_ProductProcess];");
            migrationBuilder.CreateIndex(
                name: "IX_T_ProductProcess_ProductCd_TaskCd",
                table: "T_ProductProcess",
                columns: new[] { "TenantId", "ProductCd", "TaskCd" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_ProductMaterial_ProductCd_ProcessCd_MaterialCd] ON [T_ProductMaterial];");
            migrationBuilder.CreateIndex(
                name: "IX_T_ProductMaterial_ProductCd_ProcessCd_MaterialCd",
                table: "T_ProductMaterial",
                columns: new[] { "TenantId", "ProductCd", "ProcessCd", "MaterialCd" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_ProductLotPrice_ProductCd] ON [T_ProductLotPrice];");
            migrationBuilder.CreateIndex(
                name: "IX_T_ProductLotPrice_ProductCd",
                table: "T_ProductLotPrice",
                column: "ProductCd");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_ProductLotPrice_ProductCd_DetailNo] ON [T_ProductLotPrice];");
            migrationBuilder.CreateIndex(
                name: "IX_T_ProductLotPrice_ProductCd_DetailNo",
                table: "T_ProductLotPrice",
                columns: new[] { "TenantId", "ProductCd", "DetailNo" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_ProductionResult_ResultNo] ON [T_ProductionResult];");
            migrationBuilder.CreateIndex(
                name: "IX_T_ProductionResult_ResultNo",
                table: "T_ProductionResult",
                columns: new[] { "TenantId", "ResultNo" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_ProductCoProduct_ProductCd] ON [T_ProductCoProduct];");
            migrationBuilder.CreateIndex(
                name: "IX_T_ProductCoProduct_ProductCd",
                table: "T_ProductCoProduct",
                column: "ProductCd");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_ProductCoProduct_ProductCd_ProcessCd_RowNo] ON [T_ProductCoProduct];");
            migrationBuilder.CreateIndex(
                name: "IX_T_ProductCoProduct_ProductCd_ProcessCd_RowNo",
                table: "T_ProductCoProduct",
                columns: new[] { "TenantId", "ProductCd", "ProcessCd", "RowNo" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_PlateMoldStock_PlateNo] ON [T_PlateMoldStock];");
            migrationBuilder.CreateIndex(
                name: "IX_T_PlateMoldStock_PlateNo",
                table: "T_PlateMoldStock",
                columns: new[] { "TenantId", "PlateNo" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_PlateMold_WdPtnNo_WdRev] ON [T_PlateMold];");
            migrationBuilder.CreateIndex(
                name: "IX_T_PlateMold_WdPtnNo_WdRev",
                table: "T_PlateMold",
                columns: new[] { "TenantId", "WdPtnNo", "WdRev" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_PaperRoll_RollNo] ON [T_PaperRoll];");
            migrationBuilder.CreateIndex(
                name: "IX_T_PaperRoll_RollNo",
                table: "T_PaperRoll",
                columns: new[] { "TenantId", "RollNo" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_Pallet_PalletNo] ON [T_Pallet];");
            migrationBuilder.CreateIndex(
                name: "IX_T_Pallet_PalletNo",
                table: "T_Pallet",
                columns: new[] { "TenantId", "PalletNo" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_OutboundOrderDetail_OutboundNo_LineNo] ON [T_OutboundOrderDetail];");
            migrationBuilder.CreateIndex(
                name: "IX_T_OutboundOrderDetail_OutboundNo_LineNo",
                table: "T_OutboundOrderDetail",
                columns: new[] { "TenantId", "OutboundNo", "LineNo" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_OutboundOrder_OutboundNo] ON [T_OutboundOrder];");
            migrationBuilder.CreateIndex(
                name: "IX_T_OutboundOrder_OutboundNo",
                table: "T_OutboundOrder",
                columns: new[] { "TenantId", "OutboundNo" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_OrderProcessNote_WebOrderNo_WebOrderDetailNo_ProductCd] ON [T_OrderProcessNote];");
            migrationBuilder.CreateIndex(
                name: "IX_T_OrderProcessNote_WebOrderNo_WebOrderDetailNo_ProductCd",
                table: "T_OrderProcessNote",
                columns: new[] { "WebOrderNo", "WebOrderDetailNo", "ProductCd" });

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_OrderProcessNote_WebOrderNo_WebOrderDetailNo_ProductCd_OperationCd] ON [T_OrderProcessNote];");
            migrationBuilder.CreateIndex(
                name: "IX_T_OrderProcessNote_WebOrderNo_WebOrderDetailNo_ProductCd_OperationCd",
                table: "T_OrderProcessNote",
                columns: new[] { "TenantId", "WebOrderNo", "WebOrderDetailNo", "ProductCd", "OperationCd" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_OrderProcess_WebOrderNo_WebOrderDetailNo_ProductCd] ON [T_OrderProcess];");
            migrationBuilder.CreateIndex(
                name: "IX_T_OrderProcess_WebOrderNo_WebOrderDetailNo_ProductCd",
                table: "T_OrderProcess",
                columns: new[] { "WebOrderNo", "WebOrderDetailNo", "ProductCd" });

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_OrderProcess_WebOrderNo_WebOrderDetailNo_ProductCd_OperationCd] ON [T_OrderProcess];");
            migrationBuilder.CreateIndex(
                name: "IX_T_OrderProcess_WebOrderNo_WebOrderDetailNo_ProductCd_OperationCd",
                table: "T_OrderProcess",
                columns: new[] { "TenantId", "WebOrderNo", "WebOrderDetailNo", "ProductCd", "OperationCd" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_OrderMaterial_WebOrderNo_WebOrderDetailNo_ProductCd] ON [T_OrderMaterial];");
            migrationBuilder.CreateIndex(
                name: "IX_T_OrderMaterial_WebOrderNo_WebOrderDetailNo_ProductCd",
                table: "T_OrderMaterial",
                columns: new[] { "WebOrderNo", "WebOrderDetailNo", "ProductCd" });

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_OrderMaterial_WebOrderNo_WebOrderDetailNo_ProductCd_ProcessCd_MaterialCd] ON [T_OrderMaterial];");
            migrationBuilder.CreateIndex(
                name: "IX_T_OrderMaterial_WebOrderNo_WebOrderDetailNo_ProductCd_ProcessCd_MaterialCd",
                table: "T_OrderMaterial",
                columns: new[] { "TenantId", "WebOrderNo", "WebOrderDetailNo", "ProductCd", "ProcessCd", "MaterialCd" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_OrderDetail_WebOrderNo_WebOrderDetailNo] ON [T_OrderDetail];");
            migrationBuilder.CreateIndex(
                name: "IX_T_OrderDetail_WebOrderNo_WebOrderDetailNo",
                table: "T_OrderDetail",
                columns: new[] { "TenantId", "WebOrderNo", "WebOrderDetailNo" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_OeeDaily_OeeDate_MachineCd] ON [T_OeeDaily];");
            migrationBuilder.CreateIndex(
                name: "IX_T_OeeDaily_OeeDate_MachineCd",
                table: "T_OeeDaily",
                columns: new[] { "TenantId", "OeeDate", "MachineCd" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_MobileTask_MobileTaskNo] ON [T_MobileTask];");
            migrationBuilder.CreateIndex(
                name: "IX_T_MobileTask_MobileTaskNo",
                table: "T_MobileTask",
                columns: new[] { "TenantId", "MobileTaskNo" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_MachineDowntime_DowntimeNo] ON [T_MachineDowntime];");
            migrationBuilder.CreateIndex(
                name: "IX_T_MachineDowntime_DowntimeNo",
                table: "T_MachineDowntime",
                columns: new[] { "TenantId", "DowntimeNo" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_Location_LocationCd] ON [T_Location];");
            migrationBuilder.CreateIndex(
                name: "IX_T_Location_LocationCd",
                table: "T_Location",
                columns: new[] { "TenantId", "LocationCd" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_KitOrder_KitOrderNo] ON [T_KitOrder];");
            migrationBuilder.CreateIndex(
                name: "IX_T_KitOrder_KitOrderNo",
                table: "T_KitOrder",
                columns: new[] { "TenantId", "KitOrderNo" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_KitMasterComponent_KitSku_LineNo] ON [T_KitMasterComponent];");
            migrationBuilder.CreateIndex(
                name: "IX_T_KitMasterComponent_KitSku_LineNo",
                table: "T_KitMasterComponent",
                columns: new[] { "TenantId", "KitSku", "LineNo" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_KitMaster_KitSku] ON [T_KitMaster];");
            migrationBuilder.CreateIndex(
                name: "IX_T_KitMaster_KitSku",
                table: "T_KitMaster",
                columns: new[] { "TenantId", "KitSku" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_IotSensor_SensorId] ON [T_IotSensor];");
            migrationBuilder.CreateIndex(
                name: "IX_T_IotSensor_SensorId",
                table: "T_IotSensor",
                columns: new[] { "TenantId", "SensorId" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_InkLot_InkLotNo] ON [T_InkLot];");
            migrationBuilder.CreateIndex(
                name: "IX_T_InkLot_InkLotNo",
                table: "T_InkLot",
                columns: new[] { "TenantId", "InkLotNo" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_InkColorMatchHistory_MatchNo] ON [T_InkColorMatchHistory];");
            migrationBuilder.CreateIndex(
                name: "IX_T_InkColorMatchHistory_MatchNo",
                table: "T_InkColorMatchHistory",
                columns: new[] { "TenantId", "MatchNo" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_InboundReceiptDetail_ReceiptNo_LineNo] ON [T_InboundReceiptDetail];");
            migrationBuilder.CreateIndex(
                name: "IX_T_InboundReceiptDetail_ReceiptNo_LineNo",
                table: "T_InboundReceiptDetail",
                columns: new[] { "TenantId", "ReceiptNo", "LineNo" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_InboundReceipt_ReceiptNo] ON [T_InboundReceipt];");
            migrationBuilder.CreateIndex(
                name: "IX_T_InboundReceipt_ReceiptNo",
                table: "T_InboundReceipt",
                columns: new[] { "TenantId", "ReceiptNo" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_InboundOrderDetail_InboundNo_LineNo] ON [T_InboundOrderDetail];");
            migrationBuilder.CreateIndex(
                name: "IX_T_InboundOrderDetail_InboundNo_LineNo",
                table: "T_InboundOrderDetail",
                columns: new[] { "TenantId", "InboundNo", "LineNo" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_InboundOrder_InboundNo] ON [T_InboundOrder];");
            migrationBuilder.CreateIndex(
                name: "IX_T_InboundOrder_InboundNo",
                table: "T_InboundOrder",
                columns: new[] { "TenantId", "InboundNo" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_FscChecklist_FscManagementNo] ON [T_FscChecklist];");
            migrationBuilder.CreateIndex(
                name: "IX_T_FscChecklist_FscManagementNo",
                table: "T_FscChecklist",
                columns: new[] { "TenantId", "FscManagementNo" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_EstimateCalcProcess_QtnCalcNo] ON [T_EstimateCalcProcess];");
            migrationBuilder.CreateIndex(
                name: "IX_T_EstimateCalcProcess_QtnCalcNo",
                table: "T_EstimateCalcProcess",
                column: "QtnCalcNo");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_EstimateCalcProcess_QtnCalcNo_SeqNo] ON [T_EstimateCalcProcess];");
            migrationBuilder.CreateIndex(
                name: "IX_T_EstimateCalcProcess_QtnCalcNo_SeqNo",
                table: "T_EstimateCalcProcess",
                columns: new[] { "TenantId", "QtnCalcNo", "SeqNo" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_DefectRecord_DefectNo] ON [T_DefectRecord];");
            migrationBuilder.CreateIndex(
                name: "IX_T_DefectRecord_DefectNo",
                table: "T_DefectRecord",
                columns: new[] { "TenantId", "DefectNo" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_CrossDockOrder_XDockNo] ON [T_CrossDockOrder];");
            migrationBuilder.CreateIndex(
                name: "IX_T_CrossDockOrder_XDockNo",
                table: "T_CrossDockOrder",
                columns: new[] { "TenantId", "XDockNo" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_CreditNote_CreditNoteNo] ON [T_CreditNote];");
            migrationBuilder.CreateIndex(
                name: "IX_T_CreditNote_CreditNoteNo",
                table: "T_CreditNote",
                columns: new[] { "TenantId", "CreditNoteNo" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_CarrierShipment_ShipmentNo] ON [T_CarrierShipment];");
            migrationBuilder.CreateIndex(
                name: "IX_T_CarrierShipment_ShipmentNo",
                table: "T_CarrierShipment",
                columns: new[] { "TenantId", "ShipmentNo" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_Sys_UserRole_UserId_RoleId] ON [Sys_UserRole];");
            migrationBuilder.CreateIndex(
                name: "IX_Sys_UserRole_UserId_RoleId",
                table: "Sys_UserRole",
                columns: new[] { "TenantId", "UserId", "RoleId" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [UX_Sys_RoleFieldPerm_RoleResourceField] ON [Sys_RoleFieldPerm];");
            migrationBuilder.CreateIndex(
                name: "UX_Sys_RoleFieldPerm_RoleResourceField",
                table: "Sys_RoleFieldPerm",
                columns: new[] { "TenantId", "RoleId", "ResourceKey", "FieldName" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [UX_Sys_RoleDataScope_RoleResource] ON [Sys_RoleDataScope];");
            migrationBuilder.CreateIndex(
                name: "UX_Sys_RoleDataScope_RoleResource",
                table: "Sys_RoleDataScope",
                columns: new[] { "TenantId", "RoleId", "ResourceKey" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [UX_Sys_RoleAction_RoleMenuAction] ON [Sys_RoleAction];");
            migrationBuilder.CreateIndex(
                name: "UX_Sys_RoleAction_RoleMenuAction",
                table: "Sys_RoleAction",
                columns: new[] { "TenantId", "RoleId", "MenuId", "ActionCode" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [UX_Sys_MenuAction_MenuAction] ON [Sys_MenuAction];");
            migrationBuilder.CreateIndex(
                name: "UX_Sys_MenuAction_MenuAction",
                table: "Sys_MenuAction",
                columns: new[] { "TenantId", "MenuId", "ActionCode" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_Sys_Dept_DeptCode] ON [Sys_Dept];");
            migrationBuilder.CreateIndex(
                name: "IX_Sys_Dept_DeptCode",
                table: "Sys_Dept",
                columns: new[] { "TenantId", "DeptCode" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [UX_Pub_GenTable_Entity] ON [Pub_GenTable];");
            migrationBuilder.CreateIndex(
                name: "UX_Pub_GenTable_Entity",
                table: "Pub_GenTable",
                columns: new[] { "TenantId", "EntityName" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [UX_Pub_DocSequence_BizKey] ON [Pub_DocSequence];");
            migrationBuilder.CreateIndex(
                name: "UX_Pub_DocSequence_BizKey",
                table: "Pub_DocSequence",
                columns: new[] { "TenantId", "BizKey" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_M_Staff_StaffCd] ON [M_Staff];");
            migrationBuilder.CreateIndex(
                name: "IX_M_Staff_StaffCd",
                table: "M_Staff",
                columns: new[] { "TenantId", "StaffCd" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_M_Machine_MachineCd] ON [M_Machine];");
            migrationBuilder.CreateIndex(
                name: "IX_M_Machine_MachineCd",
                table: "M_Machine",
                columns: new[] { "TenantId", "MachineCd" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_M_InspectionTemplate_TemplateCd_ItemSeqNo] ON [M_InspectionTemplate];");
            migrationBuilder.CreateIndex(
                name: "IX_M_InspectionTemplate_TemplateCd_ItemSeqNo",
                table: "M_InspectionTemplate",
                columns: new[] { "TenantId", "TemplateCd", "ItemSeqNo" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_M_GenericCode_GroupCode_Code] ON [M_GenericCode];");
            migrationBuilder.CreateIndex(
                name: "IX_M_GenericCode_GroupCode_Code",
                table: "M_GenericCode",
                columns: new[] { "TenantId", "GroupCode", "Code" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_M_DefectCategory_CategoryCd_DetailCd] ON [M_DefectCategory];");
            migrationBuilder.CreateIndex(
                name: "IX_M_DefectCategory_CategoryCd_DetailCd",
                table: "M_DefectCategory",
                columns: new[] { "TenantId", "CategoryCd", "DetailCd" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_M_Base_BaseCd] ON [M_Base];");
            migrationBuilder.CreateIndex(
                name: "IX_M_Base_BaseCd",
                table: "M_Base",
                columns: new[] { "TenantId", "BaseCd" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_Fin_TaxCode_Code] ON [Fin_TaxCode];");
            migrationBuilder.CreateIndex(
                name: "IX_Fin_TaxCode_Code",
                table: "Fin_TaxCode",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_Fin_Sequence_SeqKey_SeqDate] ON [Fin_Sequence];");
            migrationBuilder.CreateIndex(
                name: "IX_Fin_Sequence_SeqKey_SeqDate",
                table: "Fin_Sequence",
                columns: new[] { "TenantId", "SeqKey", "SeqDate" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_Fin_Receipt_No] ON [Fin_Receipt];");
            migrationBuilder.CreateIndex(
                name: "IX_Fin_Receipt_No",
                table: "Fin_Receipt",
                columns: new[] { "TenantId", "No" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_Fin_Payment_No] ON [Fin_Payment];");
            migrationBuilder.CreateIndex(
                name: "IX_Fin_Payment_No",
                table: "Fin_Payment",
                columns: new[] { "TenantId", "No" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_Fin_JournalEntry_No] ON [Fin_JournalEntry];");
            migrationBuilder.CreateIndex(
                name: "IX_Fin_JournalEntry_No",
                table: "Fin_JournalEntry",
                columns: new[] { "TenantId", "No" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [UX_Fin_JournalEntry_AutoVoucherSource] ON [Fin_JournalEntry];");
            migrationBuilder.CreateIndex(
                name: "UX_Fin_JournalEntry_AutoVoucherSource",
                table: "Fin_JournalEntry",
                columns: new[] { "TenantId", "Source", "SourceDocNo" },
                unique: true,
                filter: "[Source] <> 0 AND [Status] = 2 AND [SourceDocNo] IS NOT NULL");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_Fin_GlAccount_Code] ON [Fin_GlAccount];");
            migrationBuilder.CreateIndex(
                name: "IX_Fin_GlAccount_Code",
                table: "Fin_GlAccount",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_Fin_FiscalPeriod_Year_Month] ON [Fin_FiscalPeriod];");
            migrationBuilder.CreateIndex(
                name: "IX_Fin_FiscalPeriod_Year_Month",
                table: "Fin_FiscalPeriod",
                columns: new[] { "TenantId", "Year", "Month" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_Fin_CostCenter_Code] ON [Fin_CostCenter];");
            migrationBuilder.CreateIndex(
                name: "IX_Fin_CostCenter_Code",
                table: "Fin_CostCenter",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_Fin_BankAccount_Code] ON [Fin_BankAccount];");
            migrationBuilder.CreateIndex(
                name: "IX_Fin_BankAccount_Code",
                table: "Fin_BankAccount",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_Fin_ArInvoice_No] ON [Fin_ArInvoice];");
            migrationBuilder.CreateIndex(
                name: "IX_Fin_ArInvoice_No",
                table: "Fin_ArInvoice",
                columns: new[] { "TenantId", "No" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [UX_Fin_ArInvoice_ShipmentDupGuard] ON [Fin_ArInvoice];");
            migrationBuilder.CreateIndex(
                name: "UX_Fin_ArInvoice_ShipmentDupGuard",
                table: "Fin_ArInvoice",
                columns: new[] { "TenantId", "ShipmentId" },
                unique: true,
                filter: "[ShipmentId] IS NOT NULL AND [IsCreditMemo] = 0");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_Fin_ApInvoice_No] ON [Fin_ApInvoice];");
            migrationBuilder.CreateIndex(
                name: "IX_Fin_ApInvoice_No",
                table: "Fin_ApInvoice",
                columns: new[] { "TenantId", "No" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [UX_Fin_ApInvoice_SupplierDupGuard] ON [Fin_ApInvoice];");
            migrationBuilder.CreateIndex(
                name: "UX_Fin_ApInvoice_SupplierDupGuard",
                table: "Fin_ApInvoice",
                columns: new[] { "TenantId", "SupplierId", "SupplierInvoiceNo" },
                unique: true,
                filter: "[SupplierInvoiceNo] IS NOT NULL AND [IsCreditMemo] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS [UX_Wf_FormDef_FormKey] ON [Wf_FormDef];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [UX_Wf_FlowDef_FlowKey] ON [Wf_FlowDef];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [UX_Wf_ApprovalBinding_BizType] ON [Wf_ApprovalBinding];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_WorkOrderProcess_WorkOrderNo_ProcessCd_TaskCd] ON [T_WorkOrderProcess];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_WorkOrderMaterial_WorkOrderNo_ProcessCd_MaterialCd] ON [T_WorkOrderMaterial];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_WmsSequence_Prefix_DateKey] ON [T_WmsSequence];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_WebBusinessPartner_BpCd] ON [T_WebBusinessPartner];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_WcsTask_TaskNo] ON [T_WcsTask];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_Warehouse_WarehouseCd] ON [T_Warehouse];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_VmiBilling_BillingNo] ON [T_VmiBilling];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_VmiBilling_CustomerCd_YearMonth] ON [T_VmiBilling];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_StockTransaction_TxnNo] ON [T_StockTransaction];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_StockTakeDetail_StockTakeNo_LineNo] ON [T_StockTakeDetail];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_StockTake_StockTakeNo] ON [T_StockTake];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [UX_Stock_WLPL] ON [T_Stock];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_SlottingPlan_SlottingPlanNo] ON [T_SlottingPlan];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_ShippingPackage_PackageNo] ON [T_ShippingPackage];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [UX_SheetUnitPriceEst_Pk13] ON [T_SheetUnitPriceEstimate];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [UX_SheetUnitPrice_Pk13] ON [T_SheetUnitPrice];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_SampleStock_SampleNo] ON [T_SampleStock];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_RmaHeader_RmaNo] ON [T_RmaHeader];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_RmaDetail_RmaNo_LineNo] ON [T_RmaDetail];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_ReplenishOrder_ReplenishNo] ON [T_ReplenishOrder];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_RemnantMaterial_RemnantNo] ON [T_RemnantMaterial];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_QuotationDetail_QtnNo] ON [T_QuotationDetail];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_QuotationDetail_QtnNo_DetailNo] ON [T_QuotationDetail];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_QuotationCalc_QtnNo] ON [T_QuotationCalc];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_QuotationCalc_QtnNo_QtnCalcNo] ON [T_QuotationCalc];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_QualityInspectionItem_InspectionNo] ON [T_QualityInspectionItem];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_QualityInspectionItem_InspectionNo_ItemSeqNo] ON [T_QualityInspectionItem];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_QcInspectionItem_InspectionNo_LineNo] ON [T_QcInspectionItem];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_QcInspection_InspectionNo] ON [T_QcInspection];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_ProductProcess_ProductCd_TaskCd] ON [T_ProductProcess];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_ProductMaterial_ProductCd_ProcessCd_MaterialCd] ON [T_ProductMaterial];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_ProductLotPrice_ProductCd] ON [T_ProductLotPrice];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_ProductLotPrice_ProductCd_DetailNo] ON [T_ProductLotPrice];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_ProductionResult_ResultNo] ON [T_ProductionResult];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_ProductCoProduct_ProductCd] ON [T_ProductCoProduct];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_ProductCoProduct_ProductCd_ProcessCd_RowNo] ON [T_ProductCoProduct];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_PlateMoldStock_PlateNo] ON [T_PlateMoldStock];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_PlateMold_WdPtnNo_WdRev] ON [T_PlateMold];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_PaperRoll_RollNo] ON [T_PaperRoll];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_Pallet_PalletNo] ON [T_Pallet];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_OutboundOrderDetail_OutboundNo_LineNo] ON [T_OutboundOrderDetail];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_OutboundOrder_OutboundNo] ON [T_OutboundOrder];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_OrderProcessNote_WebOrderNo_WebOrderDetailNo_ProductCd] ON [T_OrderProcessNote];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_OrderProcessNote_WebOrderNo_WebOrderDetailNo_ProductCd_OperationCd] ON [T_OrderProcessNote];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_OrderProcess_WebOrderNo_WebOrderDetailNo_ProductCd] ON [T_OrderProcess];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_OrderProcess_WebOrderNo_WebOrderDetailNo_ProductCd_OperationCd] ON [T_OrderProcess];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_OrderMaterial_WebOrderNo_WebOrderDetailNo_ProductCd] ON [T_OrderMaterial];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_OrderMaterial_WebOrderNo_WebOrderDetailNo_ProductCd_ProcessCd_MaterialCd] ON [T_OrderMaterial];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_OrderDetail_WebOrderNo_WebOrderDetailNo] ON [T_OrderDetail];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_OeeDaily_OeeDate_MachineCd] ON [T_OeeDaily];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_MobileTask_MobileTaskNo] ON [T_MobileTask];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_MachineDowntime_DowntimeNo] ON [T_MachineDowntime];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_Location_LocationCd] ON [T_Location];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_KitOrder_KitOrderNo] ON [T_KitOrder];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_KitMasterComponent_KitSku_LineNo] ON [T_KitMasterComponent];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_KitMaster_KitSku] ON [T_KitMaster];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_IotSensor_SensorId] ON [T_IotSensor];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_InkLot_InkLotNo] ON [T_InkLot];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_InkColorMatchHistory_MatchNo] ON [T_InkColorMatchHistory];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_InboundReceiptDetail_ReceiptNo_LineNo] ON [T_InboundReceiptDetail];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_InboundReceipt_ReceiptNo] ON [T_InboundReceipt];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_InboundOrderDetail_InboundNo_LineNo] ON [T_InboundOrderDetail];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_InboundOrder_InboundNo] ON [T_InboundOrder];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_FscChecklist_FscManagementNo] ON [T_FscChecklist];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_EstimateCalcProcess_QtnCalcNo] ON [T_EstimateCalcProcess];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_EstimateCalcProcess_QtnCalcNo_SeqNo] ON [T_EstimateCalcProcess];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_DefectRecord_DefectNo] ON [T_DefectRecord];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_CrossDockOrder_XDockNo] ON [T_CrossDockOrder];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_CreditNote_CreditNoteNo] ON [T_CreditNote];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_CarrierShipment_ShipmentNo] ON [T_CarrierShipment];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_Sys_UserRole_UserId_RoleId] ON [Sys_UserRole];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [UX_Sys_RoleFieldPerm_RoleResourceField] ON [Sys_RoleFieldPerm];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [UX_Sys_RoleDataScope_RoleResource] ON [Sys_RoleDataScope];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [UX_Sys_RoleAction_RoleMenuAction] ON [Sys_RoleAction];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [UX_Sys_MenuAction_MenuAction] ON [Sys_MenuAction];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_Sys_Dept_DeptCode] ON [Sys_Dept];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [UX_Pub_GenTable_Entity] ON [Pub_GenTable];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [UX_Pub_DocSequence_BizKey] ON [Pub_DocSequence];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_M_Staff_StaffCd] ON [M_Staff];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_M_Machine_MachineCd] ON [M_Machine];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_M_InspectionTemplate_TemplateCd_ItemSeqNo] ON [M_InspectionTemplate];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_M_GenericCode_GroupCode_Code] ON [M_GenericCode];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_M_DefectCategory_CategoryCd_DetailCd] ON [M_DefectCategory];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_M_Base_BaseCd] ON [M_Base];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_Fin_TaxCode_Code] ON [Fin_TaxCode];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_Fin_Sequence_SeqKey_SeqDate] ON [Fin_Sequence];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_Fin_Receipt_No] ON [Fin_Receipt];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_Fin_Payment_No] ON [Fin_Payment];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_Fin_JournalEntry_No] ON [Fin_JournalEntry];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [UX_Fin_JournalEntry_AutoVoucherSource] ON [Fin_JournalEntry];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_Fin_GlAccount_Code] ON [Fin_GlAccount];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_Fin_FiscalPeriod_Year_Month] ON [Fin_FiscalPeriod];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_Fin_CostCenter_Code] ON [Fin_CostCenter];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_Fin_BankAccount_Code] ON [Fin_BankAccount];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_Fin_ArInvoice_No] ON [Fin_ArInvoice];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [UX_Fin_ArInvoice_ShipmentDupGuard] ON [Fin_ArInvoice];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_Fin_ApInvoice_No] ON [Fin_ApInvoice];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [UX_Fin_ApInvoice_SupplierDupGuard] ON [Fin_ApInvoice];");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [UX_Wf_FormDef_FormKey] ON [Wf_FormDef];");
            migrationBuilder.CreateIndex(
                name: "UX_Wf_FormDef_FormKey",
                table: "Wf_FormDef",
                column: "FormKey",
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [UX_Wf_FlowDef_FlowKey] ON [Wf_FlowDef];");
            migrationBuilder.CreateIndex(
                name: "UX_Wf_FlowDef_FlowKey",
                table: "Wf_FlowDef",
                column: "FlowKey",
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [UX_Wf_ApprovalBinding_BizType] ON [Wf_ApprovalBinding];");
            migrationBuilder.CreateIndex(
                name: "UX_Wf_ApprovalBinding_BizType",
                table: "Wf_ApprovalBinding",
                column: "BizType",
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_WorkOrderProcess_WorkOrderNo_ProcessCd_TaskCd] ON [T_WorkOrderProcess];");
            migrationBuilder.CreateIndex(
                name: "IX_T_WorkOrderProcess_WorkOrderNo_ProcessCd_TaskCd",
                table: "T_WorkOrderProcess",
                columns: new[] { "WorkOrderNo", "ProcessCd", "TaskCd" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_WorkOrderMaterial_WorkOrderNo_ProcessCd_MaterialCd] ON [T_WorkOrderMaterial];");
            migrationBuilder.CreateIndex(
                name: "IX_T_WorkOrderMaterial_WorkOrderNo_ProcessCd_MaterialCd",
                table: "T_WorkOrderMaterial",
                columns: new[] { "WorkOrderNo", "ProcessCd", "MaterialCd" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_WmsSequence_Prefix_DateKey] ON [T_WmsSequence];");
            migrationBuilder.CreateIndex(
                name: "IX_T_WmsSequence_Prefix_DateKey",
                table: "T_WmsSequence",
                columns: new[] { "Prefix", "DateKey" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_WebBusinessPartner_BpCd] ON [T_WebBusinessPartner];");
            migrationBuilder.CreateIndex(
                name: "IX_T_WebBusinessPartner_BpCd",
                table: "T_WebBusinessPartner",
                column: "BpCd",
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_WcsTask_TaskNo] ON [T_WcsTask];");
            migrationBuilder.CreateIndex(
                name: "IX_T_WcsTask_TaskNo",
                table: "T_WcsTask",
                column: "TaskNo",
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_Warehouse_WarehouseCd] ON [T_Warehouse];");
            migrationBuilder.CreateIndex(
                name: "IX_T_Warehouse_WarehouseCd",
                table: "T_Warehouse",
                column: "WarehouseCd",
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_VmiBilling_BillingNo] ON [T_VmiBilling];");
            migrationBuilder.CreateIndex(
                name: "IX_T_VmiBilling_BillingNo",
                table: "T_VmiBilling",
                column: "BillingNo",
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_VmiBilling_CustomerCd_YearMonth] ON [T_VmiBilling];");
            migrationBuilder.CreateIndex(
                name: "IX_T_VmiBilling_CustomerCd_YearMonth",
                table: "T_VmiBilling",
                columns: new[] { "CustomerCd", "YearMonth" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_StockTransaction_TxnNo] ON [T_StockTransaction];");
            migrationBuilder.CreateIndex(
                name: "IX_T_StockTransaction_TxnNo",
                table: "T_StockTransaction",
                column: "TxnNo",
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_StockTakeDetail_StockTakeNo_LineNo] ON [T_StockTakeDetail];");
            migrationBuilder.CreateIndex(
                name: "IX_T_StockTakeDetail_StockTakeNo_LineNo",
                table: "T_StockTakeDetail",
                columns: new[] { "StockTakeNo", "LineNo" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_StockTake_StockTakeNo] ON [T_StockTake];");
            migrationBuilder.CreateIndex(
                name: "IX_T_StockTake_StockTakeNo",
                table: "T_StockTake",
                column: "StockTakeNo",
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [UX_Stock_WLPL] ON [T_Stock];");
            migrationBuilder.CreateIndex(
                name: "UX_Stock_WLPL",
                table: "T_Stock",
                columns: new[] { "WarehouseCd", "LocationCd", "ProductCd", "LotNo" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_SlottingPlan_SlottingPlanNo] ON [T_SlottingPlan];");
            migrationBuilder.CreateIndex(
                name: "IX_T_SlottingPlan_SlottingPlanNo",
                table: "T_SlottingPlan",
                column: "SlottingPlanNo",
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_ShippingPackage_PackageNo] ON [T_ShippingPackage];");
            migrationBuilder.CreateIndex(
                name: "IX_T_ShippingPackage_PackageNo",
                table: "T_ShippingPackage",
                column: "PackageNo",
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [UX_SheetUnitPriceEst_Pk13] ON [T_SheetUnitPriceEstimate];");
            migrationBuilder.CreateIndex(
                name: "UX_SheetUnitPriceEst_Pk13",
                table: "T_SheetUnitPriceEstimate",
                columns: new[] { "RevisionDate", "BaseCd", "CustomerCd", "SheetFlute", "PaperCdF", "PrintCdF", "EmbossCdF", "PaperCdC", "PrintCdC", "EmbossCdC", "PaperCdB", "PrintCdB", "EmbossCdB" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [UX_SheetUnitPrice_Pk13] ON [T_SheetUnitPrice];");
            migrationBuilder.CreateIndex(
                name: "UX_SheetUnitPrice_Pk13",
                table: "T_SheetUnitPrice",
                columns: new[] { "RevisionDate", "BaseCd", "CustomerCd", "SheetFlute", "PaperCdF", "PrintCdF", "EmbossCdF", "PaperCdC", "PrintCdC", "EmbossCdC", "PaperCdB", "PrintCdB", "EmbossCdB" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_SampleStock_SampleNo] ON [T_SampleStock];");
            migrationBuilder.CreateIndex(
                name: "IX_T_SampleStock_SampleNo",
                table: "T_SampleStock",
                column: "SampleNo",
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_RmaHeader_RmaNo] ON [T_RmaHeader];");
            migrationBuilder.CreateIndex(
                name: "IX_T_RmaHeader_RmaNo",
                table: "T_RmaHeader",
                column: "RmaNo",
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_RmaDetail_RmaNo_LineNo] ON [T_RmaDetail];");
            migrationBuilder.CreateIndex(
                name: "IX_T_RmaDetail_RmaNo_LineNo",
                table: "T_RmaDetail",
                columns: new[] { "RmaNo", "LineNo" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_ReplenishOrder_ReplenishNo] ON [T_ReplenishOrder];");
            migrationBuilder.CreateIndex(
                name: "IX_T_ReplenishOrder_ReplenishNo",
                table: "T_ReplenishOrder",
                column: "ReplenishNo",
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_RemnantMaterial_RemnantNo] ON [T_RemnantMaterial];");
            migrationBuilder.CreateIndex(
                name: "IX_T_RemnantMaterial_RemnantNo",
                table: "T_RemnantMaterial",
                column: "RemnantNo",
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_QuotationDetail_QtnNo_DetailNo] ON [T_QuotationDetail];");
            migrationBuilder.CreateIndex(
                name: "IX_T_QuotationDetail_QtnNo_DetailNo",
                table: "T_QuotationDetail",
                columns: new[] { "QtnNo", "DetailNo" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_QuotationCalc_QtnNo_QtnCalcNo] ON [T_QuotationCalc];");
            migrationBuilder.CreateIndex(
                name: "IX_T_QuotationCalc_QtnNo_QtnCalcNo",
                table: "T_QuotationCalc",
                columns: new[] { "QtnNo", "QtnCalcNo" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_QualityInspectionItem_InspectionNo_ItemSeqNo] ON [T_QualityInspectionItem];");
            migrationBuilder.CreateIndex(
                name: "IX_T_QualityInspectionItem_InspectionNo_ItemSeqNo",
                table: "T_QualityInspectionItem",
                columns: new[] { "InspectionNo", "ItemSeqNo" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_QcInspectionItem_InspectionNo_LineNo] ON [T_QcInspectionItem];");
            migrationBuilder.CreateIndex(
                name: "IX_T_QcInspectionItem_InspectionNo_LineNo",
                table: "T_QcInspectionItem",
                columns: new[] { "InspectionNo", "LineNo" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_QcInspection_InspectionNo] ON [T_QcInspection];");
            migrationBuilder.CreateIndex(
                name: "IX_T_QcInspection_InspectionNo",
                table: "T_QcInspection",
                column: "InspectionNo",
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_ProductProcess_ProductCd_TaskCd] ON [T_ProductProcess];");
            migrationBuilder.CreateIndex(
                name: "IX_T_ProductProcess_ProductCd_TaskCd",
                table: "T_ProductProcess",
                columns: new[] { "ProductCd", "TaskCd" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_ProductMaterial_ProductCd_ProcessCd_MaterialCd] ON [T_ProductMaterial];");
            migrationBuilder.CreateIndex(
                name: "IX_T_ProductMaterial_ProductCd_ProcessCd_MaterialCd",
                table: "T_ProductMaterial",
                columns: new[] { "ProductCd", "ProcessCd", "MaterialCd" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_ProductLotPrice_ProductCd_DetailNo] ON [T_ProductLotPrice];");
            migrationBuilder.CreateIndex(
                name: "IX_T_ProductLotPrice_ProductCd_DetailNo",
                table: "T_ProductLotPrice",
                columns: new[] { "ProductCd", "DetailNo" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_ProductionResult_ResultNo] ON [T_ProductionResult];");
            migrationBuilder.CreateIndex(
                name: "IX_T_ProductionResult_ResultNo",
                table: "T_ProductionResult",
                column: "ResultNo",
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_ProductCoProduct_ProductCd_ProcessCd_RowNo] ON [T_ProductCoProduct];");
            migrationBuilder.CreateIndex(
                name: "IX_T_ProductCoProduct_ProductCd_ProcessCd_RowNo",
                table: "T_ProductCoProduct",
                columns: new[] { "ProductCd", "ProcessCd", "RowNo" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_PlateMoldStock_PlateNo] ON [T_PlateMoldStock];");
            migrationBuilder.CreateIndex(
                name: "IX_T_PlateMoldStock_PlateNo",
                table: "T_PlateMoldStock",
                column: "PlateNo",
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_PlateMold_WdPtnNo_WdRev] ON [T_PlateMold];");
            migrationBuilder.CreateIndex(
                name: "IX_T_PlateMold_WdPtnNo_WdRev",
                table: "T_PlateMold",
                columns: new[] { "WdPtnNo", "WdRev" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_PaperRoll_RollNo] ON [T_PaperRoll];");
            migrationBuilder.CreateIndex(
                name: "IX_T_PaperRoll_RollNo",
                table: "T_PaperRoll",
                column: "RollNo",
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_Pallet_PalletNo] ON [T_Pallet];");
            migrationBuilder.CreateIndex(
                name: "IX_T_Pallet_PalletNo",
                table: "T_Pallet",
                column: "PalletNo",
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_OutboundOrderDetail_OutboundNo_LineNo] ON [T_OutboundOrderDetail];");
            migrationBuilder.CreateIndex(
                name: "IX_T_OutboundOrderDetail_OutboundNo_LineNo",
                table: "T_OutboundOrderDetail",
                columns: new[] { "OutboundNo", "LineNo" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_OutboundOrder_OutboundNo] ON [T_OutboundOrder];");
            migrationBuilder.CreateIndex(
                name: "IX_T_OutboundOrder_OutboundNo",
                table: "T_OutboundOrder",
                column: "OutboundNo",
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_OrderProcessNote_WebOrderNo_WebOrderDetailNo_ProductCd_OperationCd] ON [T_OrderProcessNote];");
            migrationBuilder.CreateIndex(
                name: "IX_T_OrderProcessNote_WebOrderNo_WebOrderDetailNo_ProductCd_OperationCd",
                table: "T_OrderProcessNote",
                columns: new[] { "WebOrderNo", "WebOrderDetailNo", "ProductCd", "OperationCd" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_OrderProcess_WebOrderNo_WebOrderDetailNo_ProductCd_OperationCd] ON [T_OrderProcess];");
            migrationBuilder.CreateIndex(
                name: "IX_T_OrderProcess_WebOrderNo_WebOrderDetailNo_ProductCd_OperationCd",
                table: "T_OrderProcess",
                columns: new[] { "WebOrderNo", "WebOrderDetailNo", "ProductCd", "OperationCd" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_OrderMaterial_WebOrderNo_WebOrderDetailNo_ProductCd_ProcessCd_MaterialCd] ON [T_OrderMaterial];");
            migrationBuilder.CreateIndex(
                name: "IX_T_OrderMaterial_WebOrderNo_WebOrderDetailNo_ProductCd_ProcessCd_MaterialCd",
                table: "T_OrderMaterial",
                columns: new[] { "WebOrderNo", "WebOrderDetailNo", "ProductCd", "ProcessCd", "MaterialCd" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_OrderDetail_WebOrderNo_WebOrderDetailNo] ON [T_OrderDetail];");
            migrationBuilder.CreateIndex(
                name: "IX_T_OrderDetail_WebOrderNo_WebOrderDetailNo",
                table: "T_OrderDetail",
                columns: new[] { "WebOrderNo", "WebOrderDetailNo" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_OeeDaily_OeeDate_MachineCd] ON [T_OeeDaily];");
            migrationBuilder.CreateIndex(
                name: "IX_T_OeeDaily_OeeDate_MachineCd",
                table: "T_OeeDaily",
                columns: new[] { "OeeDate", "MachineCd" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_MobileTask_MobileTaskNo] ON [T_MobileTask];");
            migrationBuilder.CreateIndex(
                name: "IX_T_MobileTask_MobileTaskNo",
                table: "T_MobileTask",
                column: "MobileTaskNo",
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_MachineDowntime_DowntimeNo] ON [T_MachineDowntime];");
            migrationBuilder.CreateIndex(
                name: "IX_T_MachineDowntime_DowntimeNo",
                table: "T_MachineDowntime",
                column: "DowntimeNo",
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_Location_LocationCd] ON [T_Location];");
            migrationBuilder.CreateIndex(
                name: "IX_T_Location_LocationCd",
                table: "T_Location",
                column: "LocationCd",
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_KitOrder_KitOrderNo] ON [T_KitOrder];");
            migrationBuilder.CreateIndex(
                name: "IX_T_KitOrder_KitOrderNo",
                table: "T_KitOrder",
                column: "KitOrderNo",
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_KitMasterComponent_KitSku_LineNo] ON [T_KitMasterComponent];");
            migrationBuilder.CreateIndex(
                name: "IX_T_KitMasterComponent_KitSku_LineNo",
                table: "T_KitMasterComponent",
                columns: new[] { "KitSku", "LineNo" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_KitMaster_KitSku] ON [T_KitMaster];");
            migrationBuilder.CreateIndex(
                name: "IX_T_KitMaster_KitSku",
                table: "T_KitMaster",
                column: "KitSku",
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_IotSensor_SensorId] ON [T_IotSensor];");
            migrationBuilder.CreateIndex(
                name: "IX_T_IotSensor_SensorId",
                table: "T_IotSensor",
                column: "SensorId",
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_InkLot_InkLotNo] ON [T_InkLot];");
            migrationBuilder.CreateIndex(
                name: "IX_T_InkLot_InkLotNo",
                table: "T_InkLot",
                column: "InkLotNo",
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_InkColorMatchHistory_MatchNo] ON [T_InkColorMatchHistory];");
            migrationBuilder.CreateIndex(
                name: "IX_T_InkColorMatchHistory_MatchNo",
                table: "T_InkColorMatchHistory",
                column: "MatchNo",
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_InboundReceiptDetail_ReceiptNo_LineNo] ON [T_InboundReceiptDetail];");
            migrationBuilder.CreateIndex(
                name: "IX_T_InboundReceiptDetail_ReceiptNo_LineNo",
                table: "T_InboundReceiptDetail",
                columns: new[] { "ReceiptNo", "LineNo" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_InboundReceipt_ReceiptNo] ON [T_InboundReceipt];");
            migrationBuilder.CreateIndex(
                name: "IX_T_InboundReceipt_ReceiptNo",
                table: "T_InboundReceipt",
                column: "ReceiptNo",
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_InboundOrderDetail_InboundNo_LineNo] ON [T_InboundOrderDetail];");
            migrationBuilder.CreateIndex(
                name: "IX_T_InboundOrderDetail_InboundNo_LineNo",
                table: "T_InboundOrderDetail",
                columns: new[] { "InboundNo", "LineNo" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_InboundOrder_InboundNo] ON [T_InboundOrder];");
            migrationBuilder.CreateIndex(
                name: "IX_T_InboundOrder_InboundNo",
                table: "T_InboundOrder",
                column: "InboundNo",
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_FscChecklist_FscManagementNo] ON [T_FscChecklist];");
            migrationBuilder.CreateIndex(
                name: "IX_T_FscChecklist_FscManagementNo",
                table: "T_FscChecklist",
                column: "FscManagementNo",
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_EstimateCalcProcess_QtnCalcNo_SeqNo] ON [T_EstimateCalcProcess];");
            migrationBuilder.CreateIndex(
                name: "IX_T_EstimateCalcProcess_QtnCalcNo_SeqNo",
                table: "T_EstimateCalcProcess",
                columns: new[] { "QtnCalcNo", "SeqNo" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_DefectRecord_DefectNo] ON [T_DefectRecord];");
            migrationBuilder.CreateIndex(
                name: "IX_T_DefectRecord_DefectNo",
                table: "T_DefectRecord",
                column: "DefectNo",
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_CrossDockOrder_XDockNo] ON [T_CrossDockOrder];");
            migrationBuilder.CreateIndex(
                name: "IX_T_CrossDockOrder_XDockNo",
                table: "T_CrossDockOrder",
                column: "XDockNo",
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_CreditNote_CreditNoteNo] ON [T_CreditNote];");
            migrationBuilder.CreateIndex(
                name: "IX_T_CreditNote_CreditNoteNo",
                table: "T_CreditNote",
                column: "CreditNoteNo",
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_T_CarrierShipment_ShipmentNo] ON [T_CarrierShipment];");
            migrationBuilder.CreateIndex(
                name: "IX_T_CarrierShipment_ShipmentNo",
                table: "T_CarrierShipment",
                column: "ShipmentNo",
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_Sys_UserRole_UserId_RoleId] ON [Sys_UserRole];");
            migrationBuilder.CreateIndex(
                name: "IX_Sys_UserRole_UserId_RoleId",
                table: "Sys_UserRole",
                columns: new[] { "UserId", "RoleId" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [UX_Sys_RoleFieldPerm_RoleResourceField] ON [Sys_RoleFieldPerm];");
            migrationBuilder.CreateIndex(
                name: "UX_Sys_RoleFieldPerm_RoleResourceField",
                table: "Sys_RoleFieldPerm",
                columns: new[] { "RoleId", "ResourceKey", "FieldName" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [UX_Sys_RoleDataScope_RoleResource] ON [Sys_RoleDataScope];");
            migrationBuilder.CreateIndex(
                name: "UX_Sys_RoleDataScope_RoleResource",
                table: "Sys_RoleDataScope",
                columns: new[] { "RoleId", "ResourceKey" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [UX_Sys_RoleAction_RoleMenuAction] ON [Sys_RoleAction];");
            migrationBuilder.CreateIndex(
                name: "UX_Sys_RoleAction_RoleMenuAction",
                table: "Sys_RoleAction",
                columns: new[] { "RoleId", "MenuId", "ActionCode" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [UX_Sys_MenuAction_MenuAction] ON [Sys_MenuAction];");
            migrationBuilder.CreateIndex(
                name: "UX_Sys_MenuAction_MenuAction",
                table: "Sys_MenuAction",
                columns: new[] { "MenuId", "ActionCode" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_Sys_Dept_DeptCode] ON [Sys_Dept];");
            migrationBuilder.CreateIndex(
                name: "IX_Sys_Dept_DeptCode",
                table: "Sys_Dept",
                column: "DeptCode",
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [UX_Pub_GenTable_Entity] ON [Pub_GenTable];");
            migrationBuilder.CreateIndex(
                name: "UX_Pub_GenTable_Entity",
                table: "Pub_GenTable",
                column: "EntityName",
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [UX_Pub_DocSequence_BizKey] ON [Pub_DocSequence];");
            migrationBuilder.CreateIndex(
                name: "UX_Pub_DocSequence_BizKey",
                table: "Pub_DocSequence",
                column: "BizKey",
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_M_Staff_StaffCd] ON [M_Staff];");
            migrationBuilder.CreateIndex(
                name: "IX_M_Staff_StaffCd",
                table: "M_Staff",
                column: "StaffCd",
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_M_Machine_MachineCd] ON [M_Machine];");
            migrationBuilder.CreateIndex(
                name: "IX_M_Machine_MachineCd",
                table: "M_Machine",
                column: "MachineCd",
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_M_InspectionTemplate_TemplateCd_ItemSeqNo] ON [M_InspectionTemplate];");
            migrationBuilder.CreateIndex(
                name: "IX_M_InspectionTemplate_TemplateCd_ItemSeqNo",
                table: "M_InspectionTemplate",
                columns: new[] { "TemplateCd", "ItemSeqNo" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_M_GenericCode_GroupCode_Code] ON [M_GenericCode];");
            migrationBuilder.CreateIndex(
                name: "IX_M_GenericCode_GroupCode_Code",
                table: "M_GenericCode",
                columns: new[] { "GroupCode", "Code" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_M_DefectCategory_CategoryCd_DetailCd] ON [M_DefectCategory];");
            migrationBuilder.CreateIndex(
                name: "IX_M_DefectCategory_CategoryCd_DetailCd",
                table: "M_DefectCategory",
                columns: new[] { "CategoryCd", "DetailCd" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_M_Base_BaseCd] ON [M_Base];");
            migrationBuilder.CreateIndex(
                name: "IX_M_Base_BaseCd",
                table: "M_Base",
                column: "BaseCd",
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_Fin_TaxCode_Code] ON [Fin_TaxCode];");
            migrationBuilder.CreateIndex(
                name: "IX_Fin_TaxCode_Code",
                table: "Fin_TaxCode",
                column: "Code",
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_Fin_Sequence_SeqKey_SeqDate] ON [Fin_Sequence];");
            migrationBuilder.CreateIndex(
                name: "IX_Fin_Sequence_SeqKey_SeqDate",
                table: "Fin_Sequence",
                columns: new[] { "SeqKey", "SeqDate" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_Fin_Receipt_No] ON [Fin_Receipt];");
            migrationBuilder.CreateIndex(
                name: "IX_Fin_Receipt_No",
                table: "Fin_Receipt",
                column: "No",
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_Fin_Payment_No] ON [Fin_Payment];");
            migrationBuilder.CreateIndex(
                name: "IX_Fin_Payment_No",
                table: "Fin_Payment",
                column: "No",
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_Fin_JournalEntry_No] ON [Fin_JournalEntry];");
            migrationBuilder.CreateIndex(
                name: "IX_Fin_JournalEntry_No",
                table: "Fin_JournalEntry",
                column: "No",
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [UX_Fin_JournalEntry_AutoVoucherSource] ON [Fin_JournalEntry];");
            migrationBuilder.CreateIndex(
                name: "UX_Fin_JournalEntry_AutoVoucherSource",
                table: "Fin_JournalEntry",
                columns: new[] { "Source", "SourceDocNo" },
                unique: true,
                filter: "[Source] <> 0 AND [Status] = 2 AND [SourceDocNo] IS NOT NULL");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_Fin_GlAccount_Code] ON [Fin_GlAccount];");
            migrationBuilder.CreateIndex(
                name: "IX_Fin_GlAccount_Code",
                table: "Fin_GlAccount",
                column: "Code",
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_Fin_FiscalPeriod_Year_Month] ON [Fin_FiscalPeriod];");
            migrationBuilder.CreateIndex(
                name: "IX_Fin_FiscalPeriod_Year_Month",
                table: "Fin_FiscalPeriod",
                columns: new[] { "Year", "Month" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_Fin_CostCenter_Code] ON [Fin_CostCenter];");
            migrationBuilder.CreateIndex(
                name: "IX_Fin_CostCenter_Code",
                table: "Fin_CostCenter",
                column: "Code",
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_Fin_BankAccount_Code] ON [Fin_BankAccount];");
            migrationBuilder.CreateIndex(
                name: "IX_Fin_BankAccount_Code",
                table: "Fin_BankAccount",
                column: "Code",
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_Fin_ArInvoice_No] ON [Fin_ArInvoice];");
            migrationBuilder.CreateIndex(
                name: "IX_Fin_ArInvoice_No",
                table: "Fin_ArInvoice",
                column: "No",
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [UX_Fin_ArInvoice_ShipmentDupGuard] ON [Fin_ArInvoice];");
            migrationBuilder.CreateIndex(
                name: "UX_Fin_ArInvoice_ShipmentDupGuard",
                table: "Fin_ArInvoice",
                column: "ShipmentId",
                unique: true,
                filter: "[ShipmentId] IS NOT NULL AND [IsCreditMemo] = 0");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_Fin_ApInvoice_No] ON [Fin_ApInvoice];");
            migrationBuilder.CreateIndex(
                name: "IX_Fin_ApInvoice_No",
                table: "Fin_ApInvoice",
                column: "No",
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [UX_Fin_ApInvoice_SupplierDupGuard] ON [Fin_ApInvoice];");
            migrationBuilder.CreateIndex(
                name: "UX_Fin_ApInvoice_SupplierDupGuard",
                table: "Fin_ApInvoice",
                columns: new[] { "SupplierId", "SupplierInvoiceNo" },
                unique: true,
                filter: "[SupplierInvoiceNo] IS NOT NULL AND [IsCreditMemo] = 0");
        }
    }
}
