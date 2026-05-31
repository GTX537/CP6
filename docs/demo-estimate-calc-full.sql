/* =====================================================================
 * 見積計算書 全項目フル投入 デモデータ
 *   - QtnCalcNo = '00000003-01'  デモ商事 牛乳1Lカートン 見積
 *   - 顧客 = BP-DEMO-ALL
 *   - T_EstimateCalc 全 99 カラムをほぼ全て埋める
 *   - T_EstimateCalcProcess は 8 工程（印刷→ニス→打抜→…→梱包）
 *   - MERGE upsert：再実行可能
 *
 *   実行: sqlcmd -S "localhost\KOUSQLSERVER" -d CP6DB -E ^
 *               -i "D:\CP6\docs\demo-estimate-calc-full.sql" -b
 * ===================================================================== */
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRY
  BEGIN TRAN;
  PRINT N'EstimateCalc full demo: デモ商事向け 牛乳1Lカートン見積 投入開始';

  DECLARE @QtnCalcNo nvarchar(11) = '00000003-01';

  -- ─── 1) ヘッダ（T_EstimateCalc）upsert ───
  -- 既存削除→再作成（MERGE は子テーブル先 DELETE が必要なため、トランザクション内で再作成）
  DELETE FROM T_EstimateCalcProcess WHERE QtnCalcNo = @QtnCalcNo;
  DELETE FROM T_EstimateCalc        WHERE QtnCalcNo = @QtnCalcNo;

  INSERT INTO T_EstimateCalc (
    Id, QtnCalcNo, QtnCalcNoMain, QtnCalcNoBranch, RefQtnCalcNo,
    ProCd, QtnDate, QtnBaseCd, OrderBaseCd, StaffCd, CustomerCd,
    -- プロジェクトNo
    ProjectNoParent, ProjectNoChild, ProjectNoMaterial,
    -- 受注種別・カテゴリ
    OrderType, ProductCategoryBig, ProductCategoryMid, ProductCategorySml,
    CustomerProductName1, CustomerProductName2,
    OrderQty, OrderYm, ParentChildDiv,
    -- FSC
    FscProductDiv, FscMaterialDiv, FscManagementNo,
    -- シート・印刷
    SheetFlute,
    PaperCdF, PrintCdF, EmbossCdF, PatternCntF,
    PaperCdC, PrintCdC, EmbossCdC,
    PaperCdB, PrintCdB, EmbossCdB, PatternCntB,
    SheetPrint,
    BladeWidth, BladeFlow, GutterFb, GutterLr, SheetDimW, SheetDimF,
    FinalMachineProc,
    -- 製品形状・分類
    ProductShape1, ProductShape2, DistDiv, RecyclePayment, IdMark, AdShape,
    -- 戦略区分 10 個（全 ON）
    StrategicDiv01, StrategicDiv02, StrategicDiv03, StrategicDiv04, StrategicDiv05,
    StrategicDiv06, StrategicDiv07, StrategicDiv08, StrategicDiv09, StrategicDiv10,
    -- 注記
    PrintNote, MfgNote, SlipNote, DeliveryNote, ShipNote1, ShipNote2,
    -- 数量バリエーション 8 段階
    EstimateQty01, EstimateQty02, EstimateQty03, EstimateQty04,
    EstimateQty05, EstimateQty06, EstimateQty07, EstimateQty08,
    ProposalLot1, ProposalLot2, Unit, DecidedQty,
    -- パレット
    PalletCnt01, PalletCnt02, PalletCnt03, PalletCnt04,
    PalletCnt05, PalletCnt06, PalletCnt07, PalletCnt08,
    -- 見積区分・単価
    QtnDiv, EstimateSqm, StandardUnitPrice, EstimateUnitPrice, ConfirmedUnitPrice,
    Status, TotalAmount,
    -- 監査
    IsDeleted, CreateDate, Creator, Modifier, ModifyDate)
  VALUES (
    NEWID(), @QtnCalcNo, 3, 1, NULL,
    'P-MILK-1L-6C', '2026-05-26', '001', '001', 'S-1001', 'BP-DEMO-ALL',
    -- プロジェクト
    'PJ-DEMO-P', 'PJ-DEMO-C', 'PJ-DEMO-M',
    -- 受注種別
    '01', 'A1', 'B1', 'C1',
    N'牛乳1Lカートン 6色印刷 デモ商事仕様',
    N'PANTONE 485C × 6色, FSC紙, ロゴ箔押し',
    100000.00, '202606', '01',
    -- FSC
    'FSC', 'FSC', 'FSC-CW-20240001',
    -- シート/印刷
    '01',
    'K280', 'OFF', 'EMB1', 6.00,
    'K210', 'OFF', 'EMB2',
    'SK',   'OFF', 'EMB3', 0.00,
    '01',
    905.00, 650.00, 5.00, 5.00, 905.00, 650.00,
    '01',
    -- 製品形状
    '01', '02', '01', '1', '01', '01',
    -- 戦略区分 10 個 全 ON
    1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
    -- 注記
    N'PANTONE 485C 厳守', N'クリーン搬送', N'伝票 朝便',
    N'時間指定 09-12', N'パレット返却便', N'ラベル 2 か所',
    -- 数量バリエーション
    50000.00, 100000.00, 200000.00, 300000.00,
    500000.00, 800000.00, 1000000.00, 1500000.00,
    100000.00, 200000.00, N'枚', 100000.00,
    -- パレット
    50.00, 100.00, 200.00, 300.00, 500.00, 800.00, 1000.00, 1500.00,
    -- 見積区分・単価
    '01', 0.5883, 3.00, 3.07, 3.05,
    1, 307000.00,
    -- 監査
    0, GETDATE(), 'demo-init', NULL, NULL);

  -- ─── 2) 工程明細（T_EstimateCalcProcess）8 工程 ───
  INSERT INTO T_EstimateCalcProcess
    (Id, QtnCalcNo, SeqNo, ProcessCd, ProcessName, TaskCd, TaskName,
     WgCd, MfgLocation,
     Spec1Label, Spec1Val, Spec2Label, Spec2Val,
     Spec3Label, Spec3Val, Spec4Label, Spec4Val,
     Spec5Label, Spec5Val, Spec6Label, Spec6Val,
     Spec7Label, Spec7Val, PlateNo,
     ProcNote1, ProcNote2,
     IsDeleted, CreateDate, Creator)
  VALUES
    -- OP10 印刷
    (NEWID(), @QtnCalcNo, 10, 'OP10', N'印刷', 'PRINT', N'6色オフセット印刷',
     'WG-PR1', N'第1印刷工場',
     N'色数', N'6色', N'用紙', N'K280',
     N'刷り順', N'CMYK+PANTONE 485C+専色',
     N'寸法', N'905×650mm', N'巾', N'905mm',
     N'流れ', N'T目', N'ロット', N'100,000枚',
     'PLT-DEMO-001', N'PANTONE 485C 厳守、ΔE<2', N'試刷 100 枚必須',
     0, GETDATE(), 'demo-init'),
    -- OP20 ニス
    (NEWID(), @QtnCalcNo, 20, 'OP20', N'ニス引き', 'VARN', N'OPニス',
     'WG-VN1', N'仕上工場',
     N'種類', N'OPニス', N'厚み', N'2g/m²',
     N'光沢', N'グロス', N'乾燥', N'UV',
     N'寸法', N'905×650mm', N'ロット', N'101,800枚',
     N'温度', N'40℃', NULL,
     N'ニス厚均一性 ±5%', N'乾燥時間 30 秒',
     0, GETDATE(), 'demo-init'),
    -- OP30 打抜
    (NEWID(), @QtnCalcNo, 30, 'OP30', N'打抜', 'DIE', N'平盤打抜',
     'WG-DC1', N'打抜工場',
     N'木型No', N'MOLD-001', N'刃幅', N'905mm',
     N'刃流れ', N'650mm', N'ガッターFB', N'5mm',
     N'ガッターLR', N'5mm', N'寸法W', N'905mm',
     N'寸法F', N'650mm', 'MOLD-DEMO-001',
     N'打抜き精度 ±0.3mm', N'刃検査 200 ショットごと',
     0, GETDATE(), 'demo-init'),
    -- OP40 表面加工
    (NEWID(), @QtnCalcNo, 40, 'OP40', N'表面加工', 'SURF', N'箔押し',
     'WG-SF1', N'仕上工場',
     N'種類', N'ゴールド箔', N'面積', N'25×25mm',
     N'温度', N'130℃', N'圧力', N'250N',
     N'箔色', N'純金箔', N'パターン数', N'2 か所',
     N'試作枚数', N'10 枚', 'PLT-DEMO-FOIL-001',
     N'箔の浮き禁止', N'剝離テスト合格必須',
     0, GETDATE(), 'demo-init'),
    -- OP50 貼合
    (NEWID(), @QtnCalcNo, 50, 'OP50', N'貼合', 'LAM', N'ラミネート',
     'WG-LM1', N'仕上工場',
     N'種類', N'OPP 30μm', N'温度', N'180℃',
     N'圧力', N'300N', N'速度', N'80m/min',
     N'幅', N'1300mm', N'長', N'500m',
     N'乾燥', N'IR 60℃', NULL,
     N'シワ厳禁', N'貼合せ後 24h 養生',
     0, GETDATE(), 'demo-init'),
    -- OP60 折・貼
    (NEWID(), @QtnCalcNo, 60, 'OP60', N'折・貼', 'FOLD', N'折貼機',
     'WG-FG1', N'仕上工場',
     N'機種', N'BOBST EXPERTFOLD 110', N'速度', N'12,000 sh/h',
     N'糊', N'ホットメルト', N'温度', N'160℃',
     N'位置', N'サイドシーム', N'幅', N'4mm',
     N'パターン', N'波形', NULL,
     N'糊はみ出し厳禁', N'寸法精度 ±0.5mm',
     0, GETDATE(), 'demo-init'),
    -- OP70 検品
    (NEWID(), @QtnCalcNo, 70, 'OP70', N'検品', 'QC', N'全数検品 + AQL 抜取',
     'WG-QC1', N'品質保証部',
     N'検査基準', N'AQL 1.0', N'検査項目', N'寸法/印刷/折強度/糊',
     N'サンプル', N'500 枚', N'合格基準', N'欠点 5 個以内',
     N'判定', N'PASS/HOLD/FAIL', N'帳票', N'検品成績書',
     N'実施者', N'高橋 検品担当', NULL,
     N'抜取 AQL 1.0 / Level II', N'不良 5 個超は全数再検',
     0, GETDATE(), 'demo-init'),
    -- OP80 梱包
    (NEWID(), @QtnCalcNo, 80, 'OP80', N'梱包', 'PACK', N'パレット梱包',
     'WG-PK1', N'出荷工場',
     N'カートン', N'1,000 枚/箱', N'パレット', N'EUR1',
     N'積層数', N'5 段 × 10 列', N'寸法', N'1200×1000mm',
     N'重量', N'450 kg/PL', N'ラップ', N'PE シュリンク',
     N'数量', N'100 PL', NULL,
     N'パレット返却便利用', N'納品時 写真記録',
     0, GETDATE(), 'demo-init');

  -- ─── 結果サマリー ───
  DECLARE @hdrCnt INT = (SELECT COUNT(*) FROM T_EstimateCalc WHERE QtnCalcNo=@QtnCalcNo);
  DECLARE @prcCnt INT = (SELECT COUNT(*) FROM T_EstimateCalcProcess WHERE QtnCalcNo=@QtnCalcNo);
  DECLARE @total decimal(15,4);
  SELECT @total = TotalAmount FROM T_EstimateCalc WHERE QtnCalcNo=@QtnCalcNo;

  PRINT N'--- 結果 ---';
  PRINT N'QtnCalcNo : ' + @QtnCalcNo;
  PRINT N'ヘッダ件数 : ' + CAST(@hdrCnt AS nvarchar(5));
  PRINT N'工程件数   : ' + CAST(@prcCnt AS nvarchar(5)) + N' (期待 8)';
  PRINT N'合計金額   : ¥' + CAST(@total AS nvarchar(20));

  COMMIT;
  PRINT 'COMMIT done';
END TRY
BEGIN CATCH
  IF @@TRANCOUNT > 0 ROLLBACK;
  PRINT 'ERROR: ' + ERROR_MESSAGE();
  THROW;
END CATCH;
