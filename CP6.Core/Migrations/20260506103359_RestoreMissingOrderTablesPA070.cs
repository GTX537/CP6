using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <summary>
    /// PA070 受注テーブル復旧（worktree 削除で失われた migration の代替）
    /// 元の migration: 20260502020929_AddOrderTablesPA070 が消失したため、
    /// 必要最低限の CREATE TABLE 文を Raw SQL で再作成する。
    /// </summary>
    public partial class RestoreMissingOrderTablesPA070 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ───── T_Order ─────
            migrationBuilder.Sql(@"
IF OBJECT_ID('T_Order', 'U') IS NULL
CREATE TABLE [T_Order] (
    [Id] uniqueidentifier NOT NULL,
    [WebOrderNo] nvarchar(20) NOT NULL,
    [CustomerCd] nvarchar(20) NOT NULL,
    [OrderType] nvarchar(4) NOT NULL,
    [OrderDepartment] nvarchar(10) NULL,
    [OrderDate] datetime2 NULL,
    [CustomerDeliveryDate] datetime2 NULL,
    [Quantity] decimal(21,8) NULL,
    [OrderSheetNo] nvarchar(30) NULL,
    [CustomerContact] nvarchar(50) NULL,
    [Addressee] nvarchar(100) NULL,
    [Carrier] nvarchar(20) NULL,
    [ShipDateTime] nvarchar(16) NULL,
    [ShipCondition] nvarchar(20) NULL,
    [SalesPriceDiv] nvarchar(1) NULL,
    [McOrderNo] nvarchar(20) NULL,
    [Status] int NOT NULL DEFAULT 0,
    [McTransferFlg] bit NOT NULL DEFAULT 0,
    [Memo1] nvarchar(100) NULL,
    [Memo2] nvarchar(100) NULL,
    [Memo3] nvarchar(100) NULL,
    [Creator] nvarchar(100) NULL,
    [CreateDate] datetime2 NOT NULL DEFAULT GETDATE(),
    [Modifier] nvarchar(100) NULL,
    [ModifyDate] datetime2 NULL,
    [IsDeleted] bit NOT NULL DEFAULT 0,
    [RowVersion] rowversion NULL,
    CONSTRAINT [PK_T_Order] PRIMARY KEY ([Id]),
    CONSTRAINT [AK_T_Order_WebOrderNo] UNIQUE ([WebOrderNo])
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_T_Order_CustomerCd_IsDeleted' AND object_id=OBJECT_ID('T_Order'))
    CREATE INDEX [IX_T_Order_CustomerCd_IsDeleted] ON [T_Order] ([CustomerCd], [IsDeleted]);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_T_Order_OrderDate_IsDeleted' AND object_id=OBJECT_ID('T_Order'))
    CREATE INDEX [IX_T_Order_OrderDate_IsDeleted] ON [T_Order] ([OrderDate], [IsDeleted]);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_T_Order_McOrderNo' AND object_id=OBJECT_ID('T_Order'))
    CREATE INDEX [IX_T_Order_McOrderNo] ON [T_Order] ([McOrderNo]);
");

            // ───── T_OrderDetail ─────
            migrationBuilder.Sql(@"
IF OBJECT_ID('T_OrderDetail', 'U') IS NULL
CREATE TABLE [T_OrderDetail] (
    [Id] uniqueidentifier NOT NULL,
    [WebOrderNo] nvarchar(20) NOT NULL,
    [WebOrderDetailNo] int NOT NULL,
    [HaibaiNo1] nvarchar(20) NULL,
    [HaibaiNo2] nvarchar(20) NULL,
    [HaibaiNo3] nvarchar(20) NULL,
    [HaibaiNo4] nvarchar(20) NULL,
    [ProductCd] nvarchar(15) NOT NULL,
    [ItemCd] nvarchar(15) NULL,
    [Branch1] nvarchar(10) NULL, [Branch2] nvarchar(10) NULL, [Branch3] nvarchar(10) NULL,
    [ProductCatBig] nvarchar(4) NULL, [ProductCatMid] nvarchar(4) NULL, [ProductCatSml] nvarchar(4) NULL,
    [CustomerItemName1] nvarchar(100) NULL,
    [CustomerItemName2] nvarchar(100) NULL,
    [CustomerPartNo] nvarchar(20) NULL,
    [CpItemName1] nvarchar(100) NULL,
    [CpItemName2] nvarchar(100) NULL,
    [JanCode] nvarchar(13) NULL,
    [QtyUnit] nvarchar(4) NULL,
    [Quantity] decimal(21,8) NULL,
    [SpecialPriceFlg] nvarchar(1) NULL,
    [UnitPriceUnit] nvarchar(4) NULL,
    [SetUnitPrice] decimal(21,8) NULL,
    [IndividualUnitPrice] decimal(21,8) NULL,
    [Amount] decimal(21,8) NULL,
    [DeliveryCd] nvarchar(20) NULL,
    [DeliveryName] nvarchar(100) NULL,
    [CustomerDeliveryDate] datetime2 NULL,
    [LogisticsGroup] nvarchar(10) NULL,
    [HaibaiKbn] nvarchar(4) NULL,
    [ConsignedSalesFlg] nvarchar(1) NULL,
    [SalesReason] nvarchar(100) NULL,
    [ConsignedSalesQty] decimal(21,8) NULL,
    [FscOrderType] nvarchar(4) NULL,
    [FscProductDiv] nvarchar(4) NULL,
    [FscMaterialDiv] nvarchar(4) NULL,
    [FscManagementNo] nvarchar(20) NULL,
    [FoodSafety] nvarchar(4) NULL,
    [ShipInspection] nvarchar(4) NULL,
    [FixedShipment] nvarchar(4) NULL,
    [DeliveryReserve] decimal(21,8) NULL,
    [SalesSample] decimal(21,8) NULL,
    [SalesAvailable] nvarchar(4) NULL,
    [SheetFlute] nvarchar(4) NULL,
    [PaperCdF] nvarchar(20) NULL, [PaperCdC] nvarchar(20) NULL, [PaperCdB] nvarchar(20) NULL,
    [PrintCdF] nvarchar(20) NULL, [PrintCdC] nvarchar(20) NULL, [PrintCdB] nvarchar(20) NULL,
    [EmbossCdF] nvarchar(20) NULL, [EmbossCdC] nvarchar(20) NULL, [EmbossCdB] nvarchar(20) NULL,
    [MakerCdF] nvarchar(20) NULL, [MakerCdC] nvarchar(20) NULL, [MakerCdB] nvarchar(20) NULL,
    [SheetPrint] nvarchar(20) NULL,
    [BladeWidth] decimal(21,8) NULL, [BladeFlow] decimal(21,8) NULL,
    [GutterFb] decimal(21,8) NULL, [GutterLr] decimal(21,8) NULL,
    [SheetDimW] decimal(21,8) NULL, [SheetDimF] decimal(21,8) NULL,
    [SalesWidth] decimal(21,8) NULL, [FinalMachineProcess] nvarchar(20) NULL,
    [PrintNote] nvarchar(100) NULL, [MfgNote] nvarchar(100) NULL,
    [RemfgNote] nvarchar(100) NULL, [SlipNote] nvarchar(100) NULL,
    [DeliveryNote] nvarchar(100) NULL, [ShipNote1] nvarchar(100) NULL, [ShipNote2] nvarchar(100) NULL,
    [DefectiveHaibaiNo] nvarchar(20) NULL,
    [PurchaseVendor] nvarchar(20) NULL,
    [RollMeter] decimal(21,8) NULL,
    [PurchaseUnitPrice] decimal(21,8) NULL,
    [PurchaseUnit] nvarchar(4) NULL,
    [ProjectNoParent] nvarchar(15) NULL, [ProjectNoChild] nvarchar(15) NULL, [ProjectNoMaterial] nvarchar(15) NULL,
    [QuotationNo] nvarchar(11) NULL, [EstimateCalcNo] nvarchar(11) NULL, [RefEstimateCalcNo] nvarchar(11) NULL,
    [SetProductCd] nvarchar(15) NULL, [SetProductName] nvarchar(100) NULL,
    [ParentChildDiv] nvarchar(1) NULL, [SetRatio] decimal(18,8) NULL,
    [OrderType] nvarchar(4) NULL,
    [ProductUsage] nvarchar(4) NULL, [DistributionDiv] nvarchar(4) NULL,
    [ConfidentialInfo] nvarchar(4) NULL, [SeizureDiv] nvarchar(4) NULL,
    [ImportanceDiv] nvarchar(4) NULL, [MChange] nvarchar(4) NULL,
    [QualityDiv] nvarchar(4) NULL, [ProductShape] nvarchar(4) NULL,
    [UnescoMark] nvarchar(4) NULL, [OrigamiMark] nvarchar(4) NULL,
    [FourMContract] nvarchar(4) NULL, [TkpWrinkleStd] nvarchar(4) NULL,
    [RecyclingPayment] nvarchar(2) NULL,
    [PaperUsageG] decimal(21,8) NULL, [PlasticUsageG] decimal(21,8) NULL,
    [GlassUsageG] decimal(21,8) NULL, [PetUsageG] decimal(21,8) NULL,
    [PackPaperUsageG] decimal(21,8) NULL, [PackPlasticUsageG] decimal(21,8) NULL,
    [DesignProposalNo] nvarchar(11) NULL, [SalesPriceDiv] nvarchar(1) NULL, [FreightBilling] nvarchar(1) NULL,
    [McOrderNo] nvarchar(20) NULL, [McOrderDetailNo] nvarchar(20) NULL,
    [Status] int NOT NULL DEFAULT 0,
    [WfApprovalFlg] bit NOT NULL DEFAULT 0,
    [McTransferFlg] bit NOT NULL DEFAULT 0,
    [ProvisionalPriceFlg] bit NOT NULL DEFAULT 0,
    [PriceChangeReason] nvarchar(200) NULL,
    [ApprovalStatus] int NULL,
    [Creator] nvarchar(100) NULL,
    [CreateDate] datetime2 NOT NULL DEFAULT GETDATE(),
    [Modifier] nvarchar(100) NULL,
    [ModifyDate] datetime2 NULL,
    [IsDeleted] bit NOT NULL DEFAULT 0,
    [RowVersion] rowversion NULL,
    CONSTRAINT [PK_T_OrderDetail] PRIMARY KEY ([Id])
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_T_OrderDetail_OrderProduct' AND object_id=OBJECT_ID('T_OrderDetail'))
    CREATE UNIQUE INDEX [UX_T_OrderDetail_OrderProduct] ON [T_OrderDetail] ([WebOrderNo], [WebOrderDetailNo], [ProductCd]);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_T_OrderDetail_HaibaiNo1' AND object_id=OBJECT_ID('T_OrderDetail'))
    CREATE INDEX [IX_T_OrderDetail_HaibaiNo1] ON [T_OrderDetail] ([HaibaiNo1]);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_T_OrderDetail_HaibaiNo23' AND object_id=OBJECT_ID('T_OrderDetail'))
    CREATE INDEX [IX_T_OrderDetail_HaibaiNo23] ON [T_OrderDetail] ([HaibaiNo2], [HaibaiNo3]);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_T_OrderDetail_ProductCd' AND object_id=OBJECT_ID('T_OrderDetail'))
    CREATE INDEX [IX_T_OrderDetail_ProductCd] ON [T_OrderDetail] ([ProductCd]);
");

            // ───── T_OrderProcess ─────
            migrationBuilder.Sql(@"
IF OBJECT_ID('T_OrderProcess', 'U') IS NULL
CREATE TABLE [T_OrderProcess] (
    [Id] uniqueidentifier NOT NULL,
    [WebOrderNo] nvarchar(20) NOT NULL,
    [WebOrderDetailNo] int NOT NULL,
    [ProductCd] nvarchar(15) NOT NULL,
    [OperationCd] nvarchar(15) NOT NULL,
    [ProcessCd] nvarchar(15) NOT NULL,
    [TopItemCd] nvarchar(15) NULL,
    [TopBranch1] nvarchar(10) NULL, [TopBranch2] nvarchar(10) NULL, [TopBranch3] nvarchar(10) NULL,
    [ItemCd] nvarchar(15) NULL,
    [Branch1] nvarchar(10) NULL, [Branch2] nvarchar(10) NULL, [Branch3] nvarchar(10) NULL,
    [WorkingGroupCd] nvarchar(10) NULL,
    [MachineOrVendor] nvarchar(20) NULL,
    [MachineFixedFlg] bit NOT NULL DEFAULT 0,
    [CpDeliveryDiv] nvarchar(4) NULL,
    [Spec01] nvarchar(50) NULL, [Spec02] nvarchar(50) NULL, [Spec03] nvarchar(50) NULL,
    [Spec04] nvarchar(50) NULL, [Spec05] nvarchar(50) NULL, [Spec06] nvarchar(50) NULL,
    [Spec07] nvarchar(50) NULL, [Spec08] nvarchar(50) NULL, [Spec09] nvarchar(50) NULL, [Spec10] nvarchar(50) NULL,
    [QtyUnit] nvarchar(4) NULL,
    [PlateNo1] nvarchar(20) NULL, [PlateNo2] nvarchar(20) NULL, [PlateNo3] nvarchar(20) NULL,
    [Consumable1] nvarchar(20) NULL, [Consumable2] nvarchar(20) NULL, [Consumable3] nvarchar(20) NULL,
    [PurchaseUnitPrice] decimal(21,8) NULL, [FixedPrice] decimal(21,8) NULL,
    [LossRate] decimal(21,8) NOT NULL DEFAULT 0,
    [MachineCount] decimal(21,8) NOT NULL DEFAULT 0,
    [LeadTimeDays] int NOT NULL DEFAULT 0,
    [StorageLocation] nvarchar(20) NULL,
    [SortOrder] int NOT NULL DEFAULT 0,
    [PriorityItem1] nvarchar(20) NULL, [PriorityItem2] nvarchar(20) NULL,
    [PriorityItem3] nvarchar(20) NULL, [PriorityItem4] nvarchar(20) NULL,
    [PriorityItem5] nvarchar(20) NULL, [PriorityItem6] nvarchar(20) NULL,
    [PriorityItem7] nvarchar(20) NULL, [PriorityItem8] nvarchar(20) NULL,
    [ScheduledDate] datetime2 NULL,
    [Creator] nvarchar(100) NULL,
    [CreateDate] datetime2 NOT NULL DEFAULT GETDATE(),
    [Modifier] nvarchar(100) NULL,
    [ModifyDate] datetime2 NULL,
    [IsDeleted] bit NOT NULL DEFAULT 0,
    [RowVersion] rowversion NULL,
    CONSTRAINT [PK_T_OrderProcess] PRIMARY KEY ([Id])
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_T_OrderProcess_Pk' AND object_id=OBJECT_ID('T_OrderProcess'))
    CREATE UNIQUE INDEX [UX_T_OrderProcess_Pk] ON [T_OrderProcess] ([WebOrderNo], [WebOrderDetailNo], [ProductCd], [OperationCd]);
");

            // ───── T_OrderProcessNote ─────
            migrationBuilder.Sql(@"
IF OBJECT_ID('T_OrderProcessNote', 'U') IS NULL
CREATE TABLE [T_OrderProcessNote] (
    [Id] uniqueidentifier NOT NULL,
    [WebOrderNo] nvarchar(20) NOT NULL,
    [WebOrderDetailNo] int NOT NULL,
    [ProductCd] nvarchar(15) NOT NULL,
    [OperationCd] nvarchar(15) NOT NULL,
    [Note1] nvarchar(200) NULL,
    [Note2] nvarchar(200) NULL,
    [Creator] nvarchar(100) NULL,
    [CreateDate] datetime2 NOT NULL DEFAULT GETDATE(),
    [Modifier] nvarchar(100) NULL,
    [ModifyDate] datetime2 NULL,
    [IsDeleted] bit NOT NULL DEFAULT 0,
    [RowVersion] rowversion NULL,
    CONSTRAINT [PK_T_OrderProcessNote] PRIMARY KEY ([Id])
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_T_OrderProcessNote_Pk' AND object_id=OBJECT_ID('T_OrderProcessNote'))
    CREATE UNIQUE INDEX [UX_T_OrderProcessNote_Pk] ON [T_OrderProcessNote] ([WebOrderNo], [WebOrderDetailNo], [ProductCd], [OperationCd]);
");

            // ───── T_OrderMaterial ─────
            migrationBuilder.Sql(@"
IF OBJECT_ID('T_OrderMaterial', 'U') IS NULL
CREATE TABLE [T_OrderMaterial] (
    [Id] uniqueidentifier NOT NULL,
    [WebOrderNo] nvarchar(20) NOT NULL,
    [WebOrderDetailNo] int NOT NULL,
    [ProductCd] nvarchar(15) NOT NULL,
    [ProcessCd] nvarchar(15) NOT NULL,
    [MaterialCd] nvarchar(15) NOT NULL,
    [MaterialTypeDiv] nvarchar(1) NOT NULL DEFAULT N'3',
    [ItemCd] nvarchar(15) NULL,
    [Branch1] nvarchar(10) NULL, [Branch2] nvarchar(10) NULL, [Branch3] nvarchar(10) NULL,
    [SupplyDiv] nvarchar(1) NOT NULL DEFAULT N'1',
    [SupplyUnitPrice] decimal(21,8) NOT NULL DEFAULT 0,
    [SortOrder] int NOT NULL DEFAULT 0,
    [Creator] nvarchar(100) NULL,
    [CreateDate] datetime2 NOT NULL DEFAULT GETDATE(),
    [Modifier] nvarchar(100) NULL,
    [ModifyDate] datetime2 NULL,
    [IsDeleted] bit NOT NULL DEFAULT 0,
    [RowVersion] rowversion NULL,
    CONSTRAINT [PK_T_OrderMaterial] PRIMARY KEY ([Id])
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_T_OrderMaterial_Pk' AND object_id=OBJECT_ID('T_OrderMaterial'))
    CREATE UNIQUE INDEX [UX_T_OrderMaterial_Pk] ON [T_OrderMaterial] ([WebOrderNo], [WebOrderDetailNo], [ProductCd], [ProcessCd], [MaterialCd]);
");

            // ───── T_SheetUnitPrice / T_SheetUnitPriceEstimate（PA130 — 同一構造） ─────
            foreach (var tableName in new[] { "T_SheetUnitPrice", "T_SheetUnitPriceEstimate" })
            {
                migrationBuilder.Sql($@"
IF OBJECT_ID('{tableName}', 'U') IS NULL
CREATE TABLE [{tableName}] (
    [Id] uniqueidentifier NOT NULL,
    [RevisionDate] datetime2 NOT NULL,
    [BaseCd] nvarchar(10) NOT NULL,
    [CustomerCd] nvarchar(20) NOT NULL,
    [SheetFlute] nvarchar(4) NOT NULL,
    [PaperCdF] nvarchar(20) NOT NULL,
    [PrintCdF] nvarchar(20) NOT NULL,
    [EmbossCdF] nvarchar(20) NOT NULL,
    [PaperCdC] nvarchar(20) NOT NULL,
    [PrintCdC] nvarchar(20) NOT NULL,
    [EmbossCdC] nvarchar(20) NOT NULL,
    [PaperCdB] nvarchar(20) NOT NULL,
    [PrintCdB] nvarchar(20) NOT NULL,
    [EmbossCdB] nvarchar(20) NOT NULL,
    [UnitPrice] decimal(15,4) NOT NULL DEFAULT 0,
    [SalesStaffCd] nvarchar(20) NULL,
    [Creator] nvarchar(100) NULL,
    [CreateDate] datetime2 NOT NULL DEFAULT GETDATE(),
    [Modifier] nvarchar(100) NULL,
    [ModifyDate] datetime2 NULL,
    [IsDeleted] bit NOT NULL DEFAULT 0,
    [RowVersion] rowversion NULL,
    CONSTRAINT [PK_{tableName}] PRIMARY KEY ([Id])
);
");
            }
            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_SheetUnitPrice_Pk13' AND object_id=OBJECT_ID('T_SheetUnitPrice'))
    CREATE UNIQUE INDEX [UX_SheetUnitPrice_Pk13] ON [T_SheetUnitPrice]
        ([RevisionDate],[BaseCd],[CustomerCd],[SheetFlute],
         [PaperCdF],[PrintCdF],[EmbossCdF],[PaperCdC],[PrintCdC],[EmbossCdC],
         [PaperCdB],[PrintCdB],[EmbossCdB]);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_SheetUnitPriceEst_Pk13' AND object_id=OBJECT_ID('T_SheetUnitPriceEstimate'))
    CREATE UNIQUE INDEX [UX_SheetUnitPriceEst_Pk13] ON [T_SheetUnitPriceEstimate]
        ([RevisionDate],[BaseCd],[CustomerCd],[SheetFlute],
         [PaperCdF],[PrintCdF],[EmbossCdF],[PaperCdC],[PrintCdC],[EmbossCdC],
         [PaperCdB],[PrintCdB],[EmbossCdB]);
");

            // ───── T_PlateMold（PA140/150） ─────
            migrationBuilder.Sql(@"
IF OBJECT_ID('T_PlateMold', 'U') IS NULL
CREATE TABLE [T_PlateMold] (
    [Id] uniqueidentifier NOT NULL,
    [WdPtnNo] nvarchar(40) NOT NULL,
    [WdRev] int NOT NULL DEFAULT 1,
    [BaseCd] nvarchar(20) NOT NULL,
    [DecisionEstimateNo] nvarchar(20) NOT NULL,
    [VrsnName] nvarchar(100) NOT NULL,
    [EarningsCd] nvarchar(4) NULL,
    [ArrangeNo] nvarchar(20) NULL,
    [StDate] datetime2 NOT NULL,
    [EndDate] datetime2 NOT NULL,
    [CustomerCd] nvarchar(20) NOT NULL,
    [RepresentativeProductCd] nvarchar(20) NOT NULL,
    [NewVerCd] nvarchar(20) NULL,
    [ProcessCd] nvarchar(20) NULL,
    [TypeClass] nvarchar(20) NULL,
    [EndPlaceCd] nvarchar(20) NULL,
    [AchieveDate] datetime2 NULL,
    [SheetFlute] nvarchar(4) NULL,
    [PaperCdF] nvarchar(20) NULL, [PrintCdF] nvarchar(20) NULL, [EmbossCdF] nvarchar(20) NULL, [MakerCdF] nvarchar(20) NULL,
    [PaperCdC] nvarchar(20) NULL, [PrintCdC] nvarchar(20) NULL, [EmbossCdC] nvarchar(20) NULL, [MakerCdC] nvarchar(20) NULL,
    [PaperCdB] nvarchar(20) NULL, [PrintCdB] nvarchar(20) NULL, [EmbossCdB] nvarchar(20) NULL, [MakerCdB] nvarchar(20) NULL,
    [ExtractionDirect] nvarchar(20) NULL,
    [DuplicatePlateFlg] bit NOT NULL DEFAULT 0,
    [SheetWidth] decimal(15,4) NOT NULL DEFAULT 0,
    [SheetFlow] decimal(15,4) NOT NULL DEFAULT 0,
    [BladeWidth] decimal(15,4) NOT NULL DEFAULT 0,
    [BladeFlow] decimal(15,4) NOT NULL DEFAULT 0,
    [CompositionQty] int NOT NULL DEFAULT 0,
    [MfgQty] int NOT NULL DEFAULT 1,
    [ColorQty] int NOT NULL DEFAULT 0,
    [BookQty] int NOT NULL DEFAULT 0,
    [FlexoThick] decimal(15,4) NULL,
    [CylinderSize] decimal(15,4) NULL,
    [SupplierCd] nvarchar(20) NOT NULL,
    [ProcessDestination] nvarchar(20) NULL,
    [DeliveryDate] datetime2 NULL,
    [ArrivalActualDate] datetime2 NULL,
    [EstimateAmount] decimal(15,4) NOT NULL DEFAULT 0,
    [DecisionAmount] decimal(15,4) NOT NULL DEFAULT 0,
    [PurchaseAmount] decimal(15,4) NOT NULL DEFAULT 0,
    [SalesDate] datetime2 NULL,
    [StrippingCd] nvarchar(20) NULL,
    [StandingCd] nvarchar(20) NULL,
    [HammerCd] nvarchar(20) NULL,
    [OversightCd] nvarchar(20) NULL,
    [PlaceCd] nvarchar(20) NULL,
    [ShelfLineCd] nvarchar(20) NULL,
    [WdQty] int NOT NULL DEFAULT 0,
    [LimitWdQty] int NOT NULL DEFAULT 0,
    [DispScheduleDate] datetime2 NULL,
    [ReturnScheduleDate] datetime2 NULL,
    [ReturnDate] datetime2 NULL,
    [ReturnReason] nvarchar(300) NULL,
    [Memo] nvarchar(300) NULL,
    [SalesNote] nvarchar(300) NULL,
    [DispActualDate] datetime2 NULL,
    [AtachInfoSheetFront] bit NOT NULL DEFAULT 0,
    [AtachInfoSheetBack] bit NOT NULL DEFAULT 0,
    [AtachInfoActual] bit NOT NULL DEFAULT 0,
    [AtachInfoBaseplate] bit NOT NULL DEFAULT 0,
    [AtachInfoPositive] bit NOT NULL DEFAULT 0,
    [AtachInfoNegative] bit NOT NULL DEFAULT 0,
    [AtachInfoMo] bit NOT NULL DEFAULT 0,
    [AtachInfoFd] bit NOT NULL DEFAULT 0,
    [NeedDraft] int NOT NULL DEFAULT 0,
    [NeedMylar] int NOT NULL DEFAULT 0,
    [NeedGalley] int NOT NULL DEFAULT 0,
    [NeedProof] int NOT NULL DEFAULT 0,
    [NeedBlueprint] int NOT NULL DEFAULT 0,
    [NeedComp] int NOT NULL DEFAULT 0,
    [NeedDesignSheet] int NOT NULL DEFAULT 0,
    [NeedOtherName] nvarchar(50) NULL,
    [NeedOther] nvarchar(50) NULL,
    [McOrderNo] nvarchar(20) NULL,
    [McOrderDetailNo] nvarchar(20) NULL,
    [Status] int NOT NULL DEFAULT 0,
    [McTransferFlg] bit NOT NULL DEFAULT 0,
    [Creator] nvarchar(100) NULL,
    [CreateDate] datetime2 NOT NULL DEFAULT GETDATE(),
    [Modifier] nvarchar(100) NULL,
    [ModifyDate] datetime2 NULL,
    [IsDeleted] bit NOT NULL DEFAULT 0,
    [RowVersion] rowversion NULL,
    CONSTRAINT [PK_T_PlateMold] PRIMARY KEY ([Id])
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_T_PlateMold_NoRev' AND object_id=OBJECT_ID('T_PlateMold'))
    CREATE UNIQUE INDEX [UX_T_PlateMold_NoRev] ON [T_PlateMold] ([WdPtnNo], [WdRev]);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_T_PlateMold_CustomerCd' AND object_id=OBJECT_ID('T_PlateMold'))
    CREATE INDEX [IX_T_PlateMold_CustomerCd] ON [T_PlateMold] ([CustomerCd]);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_T_PlateMold_SupplierCd' AND object_id=OBJECT_ID('T_PlateMold'))
    CREATE INDEX [IX_T_PlateMold_SupplierCd] ON [T_PlateMold] ([SupplierCd]);
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF OBJECT_ID('T_PlateMold', 'U') IS NOT NULL DROP TABLE [T_PlateMold];");
            migrationBuilder.Sql("IF OBJECT_ID('T_SheetUnitPriceEstimate', 'U') IS NOT NULL DROP TABLE [T_SheetUnitPriceEstimate];");
            migrationBuilder.Sql("IF OBJECT_ID('T_SheetUnitPrice', 'U') IS NOT NULL DROP TABLE [T_SheetUnitPrice];");
            migrationBuilder.Sql("IF OBJECT_ID('T_OrderMaterial', 'U') IS NOT NULL DROP TABLE [T_OrderMaterial];");
            migrationBuilder.Sql("IF OBJECT_ID('T_OrderProcessNote', 'U') IS NOT NULL DROP TABLE [T_OrderProcessNote];");
            migrationBuilder.Sql("IF OBJECT_ID('T_OrderProcess', 'U') IS NOT NULL DROP TABLE [T_OrderProcess];");
            migrationBuilder.Sql("IF OBJECT_ID('T_OrderDetail', 'U') IS NOT NULL DROP TABLE [T_OrderDetail];");
            migrationBuilder.Sql("IF OBJECT_ID('T_Order', 'U') IS NOT NULL DROP TABLE [T_Order];");
        }
    }
}
