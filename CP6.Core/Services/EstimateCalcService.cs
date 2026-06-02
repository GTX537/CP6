using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels;
using CP6.Entity.DTOs;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services;

/// <summary>
/// MSBBPA010 - 見積計算書 業務服務
/// </summary>
/// <remarks>
/// - 软删除：所有写操作不走 RepositoryBase.DeleteAsync，而是置 IsDeleted=true
/// - 乐观锁：依赖 EF 的 RowVersion；版本冲突抛 DbUpdateConcurrencyException
/// - 采番：EMC + 年(4) + 月(2) + 自増(4) = 13桁 + 枝番 "-01"（永不重置；DocNumber 経由）
/// </remarks>
public class EstimateCalcService : IEstimateCalcService
{
    private readonly CP6Context _db;

    public EstimateCalcService(CP6Context db)
    {
        _db = db;
    }

    /// <summary>一覧排序白名单：前端列 prop → 实体属性</summary>
    private static readonly IReadOnlyDictionary<string, string> EstimateCalcSortMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["qtnCalcNo"] = nameof(EstimateCalc.QtnCalcNo),
            ["qtnDate"] = nameof(EstimateCalc.QtnDate),
            ["qtnBaseCd"] = nameof(EstimateCalc.QtnBaseCd),
            ["staffCd"] = nameof(EstimateCalc.StaffCd),
            ["customerCd"] = nameof(EstimateCalc.CustomerCd),
            ["customerProductName1"] = nameof(EstimateCalc.CustomerProductName1),
            ["orderQty"] = nameof(EstimateCalc.OrderQty),
            ["estimateUnitPrice"] = nameof(EstimateCalc.EstimateUnitPrice),
            ["qtnDiv"] = nameof(EstimateCalc.QtnDiv),
            ["modifyDate"] = nameof(EstimateCalc.ModifyDate),
        };

    public async Task<(List<EstimateCalcListItem>, int)> GetPageListAsync(EstimateCalcQuery query)
    {
        var q = _db.EstimateCalcs.AsNoTracking().Where(x => !x.IsDeleted);

        if (!string.IsNullOrWhiteSpace(query.QtnCalcNo))
            q = q.Where(x => x.QtnCalcNo.Contains(query.QtnCalcNo));
        if (!string.IsNullOrWhiteSpace(query.CustomerCd))
            q = q.Where(x => x.CustomerCd == query.CustomerCd);
        if (!string.IsNullOrWhiteSpace(query.BaseCd))
            q = q.Where(x => x.QtnBaseCd == query.BaseCd);
        if (query.DateFrom.HasValue)
            q = q.Where(x => x.QtnDate >= query.DateFrom.Value);
        if (query.DateTo.HasValue)
            q = q.Where(x => x.QtnDate <= query.DateTo.Value);

        var total = await q.CountAsync();
        q = QuerySort.Apply(q, query.SortField, query.SortOrder, EstimateCalcSortMap,
            s => s.OrderByDescending(x => x.QtnDate).ThenByDescending(x => x.QtnCalcNo));
        var rows = await q
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(x => new EstimateCalcListItem
            {
                QtnCalcNo = x.QtnCalcNo,
                QtnDate = x.QtnDate,
                CustomerCd = x.CustomerCd,
                CustomerProductName1 = x.CustomerProductName1,
                OrderQty = x.OrderQty,
                QtnBaseCd = x.QtnBaseCd,
                StaffCd = x.StaffCd,
                QtnDiv = x.QtnDiv,
                EstimateUnitPrice = x.EstimateUnitPrice,
                CreateDate = x.CreateDate,
                ModifyDate = x.ModifyDate,
            })
            .ToListAsync();
        return (rows, total);
    }

    public async Task<EstimateCalcDto?> GetByNoAsync(string qtnCalcNo, bool includeDeleted = false)
    {
        var q = _db.EstimateCalcs.AsNoTracking().Include(x => x.Processes).AsQueryable();
        if (!includeDeleted) q = q.Where(x => !x.IsDeleted);

        var entity = await q.FirstOrDefaultAsync(x => x.QtnCalcNo == qtnCalcNo);
        return entity == null ? null : ToDto(entity);
    }

    public async Task<string> CreateAsync(EstimateCalcDto dto, string? userName)
    {
        // 采番：機能コード(EMC)+年(4)+月(2)+自増(4)=13桁；訂正/コピー用に枝番 -01 を付与
        var (mainNo, nextMain) = await DocNumber.NextAsync(_db, "EMC");
        var qtnCalcNo = $"{mainNo}-01";

        var entity = new EstimateCalc
        {
            QtnCalcNo = qtnCalcNo,
            QtnCalcNoMain = nextMain,
            QtnCalcNoBranch = 1,
            QtnDate = DateTime.Today,
            Creator = userName,
            CreateDate = DateTime.Now,
        };
        ApplyDto(entity, dto);

        // 工程明细：全部作为新增
        int seq = 1;
        foreach (var p in dto.Processes.OrderBy(p => p.SeqNo))
        {
            entity.Processes.Add(ToProcessEntity(p, qtnCalcNo, seq++, userName));
        }

        _db.EstimateCalcs.Add(entity);
        await _db.SaveChangesAsync();
        return qtnCalcNo;
    }

    public async Task UpdateAsync(string qtnCalcNo, EstimateCalcDto dto, string? userName)
    {
        var entity = await _db.EstimateCalcs
            .Include(x => x.Processes)
            .FirstOrDefaultAsync(x => x.QtnCalcNo == qtnCalcNo && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"見積計算書NO 不存在或已删除: {qtnCalcNo}");

        // 让 EF 通过 RowVersion 做乐观锁校验：用 DTO 上的版本覆盖 original value
        if (dto.RowVersion != null && dto.RowVersion.Length > 0)
        {
            _db.Entry(entity).Property(x => x.RowVersion).OriginalValue = dto.RowVersion;
        }

        ApplyDto(entity, dto);
        entity.Modifier = userName;
        entity.ModifyDate = DateTime.Now;

        // 明细 diff：按 SeqNo 对比
        var incoming = dto.Processes.ToDictionary(p => p.SeqNo);
        var existing = entity.Processes.ToDictionary(p => p.SeqNo);

        // 软删除：已有但 DTO 中没有的
        foreach (var (seq, old) in existing)
        {
            if (!incoming.ContainsKey(seq)) old.IsDeleted = true;
        }
        // 更新/新增
        foreach (var (seq, p) in incoming)
        {
            if (existing.TryGetValue(seq, out var cur))
            {
                ApplyProcessDto(cur, p);
                cur.IsDeleted = false; // 覆盖：可能是之前软删的被恢复
                cur.Modifier = userName;
                cur.ModifyDate = DateTime.Now;
            }
            else
            {
                entity.Processes.Add(ToProcessEntity(p, qtnCalcNo, seq, userName));
            }
        }

        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(string qtnCalcNo, byte[]? rowVersion, string? userName)
    {
        var entity = await _db.EstimateCalcs
            .FirstOrDefaultAsync(x => x.QtnCalcNo == qtnCalcNo && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"見積計算書NO 不存在或已删除: {qtnCalcNo}");

        if (rowVersion != null && rowVersion.Length > 0)
        {
            _db.Entry(entity).Property(x => x.RowVersion).OriginalValue = rowVersion;
        }

        entity.IsDeleted = true;
        entity.Modifier = userName;
        entity.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    public async Task<string> CopyAsync(string sourceQtnCalcNo, string? userName)
    {
        var source = await _db.EstimateCalcs.AsNoTracking()
            .Include(x => x.Processes)
            .FirstOrDefaultAsync(x => x.QtnCalcNo == sourceQtnCalcNo && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"源見積計算書不存在: {sourceQtnCalcNo}");

        var (mainNo, nextMain) = await DocNumber.NextAsync(_db, "EMC");
        var newNo = $"{mainNo}-01";

        var dto = ToDto(source);
        dto.QtnCalcNo = newNo;
        dto.RowVersion = null;

        var entity = new EstimateCalc
        {
            QtnCalcNo = newNo,
            QtnCalcNoMain = nextMain,
            QtnCalcNoBranch = 1,
            QtnDate = DateTime.Today,
            RefQtnCalcNo = sourceQtnCalcNo,
            Creator = userName,
            CreateDate = DateTime.Now,
        };
        ApplyDto(entity, dto);

        int seq = 1;
        foreach (var p in source.Processes.Where(x => !x.IsDeleted).OrderBy(x => x.SeqNo))
        {
            var pdto = ToProcessDto(p);
            entity.Processes.Add(ToProcessEntity(pdto, newNo, seq++, userName));
        }

        _db.EstimateCalcs.Add(entity);
        await _db.SaveChangesAsync();
        return newNo;
    }

    /// <summary>
    /// 計算引擎
    /// 公式：
    ///   SheetArea         = (SheetDimW × SheetDimF) / 1,000,000 [m²/枚]
    ///   EstimateSqm       = SheetArea × OrderQty
    ///   PaperUnitPrice    = M_GenericCode(GroupCode='Paper', Code=PaperCdF).Num1
    ///                       若未找到则 fallback = 30 円/m²
    ///   YieldRate         = M_GenericCode(GroupCode='M067', Code=SheetFlute).Num1
    ///                       若未找到则 fallback = 1.00（無損耗）
    ///   ProcessCost       = 工程明细行数 × 5 円/枚
    ///   PaperCostPerSheet = PaperUnitPrice × SheetArea × YieldRate
    ///   StandardUnitPrice = PaperCostPerSheet + ProcessCost
    ///   EstimateUnitPrice = StandardUnitPrice × 1.20（利益率 20%）
    ///   ConfirmedUnitPrice = EstimateUnitPrice
    ///   AmountRows[i]     = EstimateQtys[i] × EstimateUnitPrice（非零数量段）
    /// </summary>
    public async Task<EstimateCalcResult> CalculateAsync(EstimateCalcDto dto)
    {
        const decimal paperPriceFallback = 30m;
        const decimal yieldRateFallback = 1.00m;
        const decimal processCostPerSheet = 5m;
        const decimal profitRate = 1.20m;

        var result = new EstimateCalcResult();

        // 查主数据：原紙単価
        decimal paperPrice = paperPriceFallback;
        string paperSource = "fallback 30";
        if (!string.IsNullOrWhiteSpace(dto.PaperCdF))
        {
            var paper = await _db.MasterGenericCodes.AsNoTracking()
                .FirstOrDefaultAsync(x => x.GroupCode == "Paper" && x.Code == dto.PaperCdF);
            if (paper?.Num1 is decimal pp && pp > 0)
            {
                paperPrice = pp;
                paperSource = $"M_GenericCode(Paper,{dto.PaperCdF}).Num1={pp}";
            }
        }

        // ── 補充（MSBBPA130 連携）：見積用シート単価マスタが優先 ──
        // 得意先 × 段 × 原紙構成(表) が一致する「見積用シート単価」(基準日時点の最新改定)が存在する場合、
        // 汎用コード(Paper)の既定単価より顧客交渉済みのシート単価を優先採用する。
        // これが PA130 設計書の本来の目的（シート単価マスタ → 見積算価への反映）。
        if (!string.IsNullOrWhiteSpace(dto.CustomerCd) && !string.IsNullOrWhiteSpace(dto.SheetFlute))
        {
            var baseCd = !string.IsNullOrWhiteSpace(dto.QtnBaseCd) ? dto.QtnBaseCd : dto.OrderBaseCd;
            var today = DateTime.Today;
            var sheetPrice = await _db.SheetUnitPriceEstimates.AsNoTracking()
                .Where(x => !x.IsDeleted
                    && x.BaseCd == baseCd
                    && x.CustomerCd == dto.CustomerCd
                    && x.SheetFlute == dto.SheetFlute
                    && (dto.PaperCdF == null || x.PaperCdF == dto.PaperCdF)
                    && x.RevisionDate <= today)
                .OrderByDescending(x => x.RevisionDate)
                .Select(x => (decimal?)x.UnitPrice)
                .FirstOrDefaultAsync();
            if (sheetPrice is decimal sp && sp > 0)
            {
                paperPrice = sp;
                paperSource = $"見積用シート単価マスタ(得意先{dto.CustomerCd}/段{dto.SheetFlute}/原紙{dto.PaperCdF})={sp}";
            }
        }

        // 查主数据：段成率（M067）
        decimal yieldRate = yieldRateFallback;
        string yieldSource = "fallback 1.00";
        if (!string.IsNullOrWhiteSpace(dto.SheetFlute))
        {
            var yld = await _db.MasterGenericCodes.AsNoTracking()
                .FirstOrDefaultAsync(x => x.GroupCode == "M067" && x.Code == dto.SheetFlute);
            if (yld?.Num1 is decimal yy && yy > 0)
            {
                yieldRate = yy;
                yieldSource = $"M_GenericCode(M067,{dto.SheetFlute}).Num1={yy}";
            }
        }

        // 面積
        decimal sheetAreaSqm = 0m;
        if ((dto.SheetDimW ?? 0) > 0 && (dto.SheetDimF ?? 0) > 0)
        {
            sheetAreaSqm = (dto.SheetDimW!.Value * dto.SheetDimF!.Value) / 1_000_000m;
        }

        decimal orderQty = dto.OrderQty ?? 0;
        result.EstimateSqm = Math.Round(sheetAreaSqm * orderQty, 4);

        int processLines = dto.Processes?.Count ?? 0;
        decimal processCost = processLines * processCostPerSheet;

        // 原紙原価 = 単価 × 面積 × 段成率
        decimal paperCostPerSheet = paperPrice * sheetAreaSqm * yieldRate;
        decimal stdPrice = paperCostPerSheet + processCost;

        result.StandardUnitPrice = Math.Round(stdPrice, 3);
        result.EstimateUnitPrice = Math.Round(stdPrice * profitRate, 3);
        result.ConfirmedUnitPrice = result.EstimateUnitPrice;

        if (dto.EstimateQtys != null)
        {
            for (int i = 0; i < dto.EstimateQtys.Length; i++)
            {
                var q = dto.EstimateQtys[i] ?? 0;
                if (q <= 0) continue;
                result.AmountRows.Add(new EstimateCalcAmountRow
                {
                    Index = i + 1,
                    Qty = q,
                    UnitPrice = result.EstimateUnitPrice,
                    Amount = Math.Round(q * result.EstimateUnitPrice, 0),
                });
            }
        }

        result.Notes.Add($"シート面積 = {dto.SheetDimW ?? 0}mm × {dto.SheetDimF ?? 0}mm = {sheetAreaSqm:F6} m²/枚");
        result.Notes.Add($"総面積 = {sheetAreaSqm:F6} × 受注数量 {orderQty} = {result.EstimateSqm:F4} m²");
        result.Notes.Add($"原紙単価 = {paperPrice:F3} 円/m²（{paperSource}）");
        result.Notes.Add($"段成率 = {yieldRate:F3}（{yieldSource}）");
        result.Notes.Add($"原紙原価 = {paperPrice:F3} × {sheetAreaSqm:F6} × {yieldRate:F3} = {paperCostPerSheet:F3} 円/枚");
        result.Notes.Add($"工程原価 = 工程{processLines}行 × {processCostPerSheet:F2} = {processCost:F2} 円/枚");
        result.Notes.Add($"標準原価 = {result.StandardUnitPrice:F3} 円/枚");
        result.Notes.Add($"見積単価 = 標準原価 × {profitRate:F2} = {result.EstimateUnitPrice:F3} 円/枚");

        return result;
    }

    // ===== Entity <-> DTO 映射 =====

    private static void ApplyDto(EstimateCalc e, EstimateCalcDto d)
    {
        e.QtnBaseCd = d.QtnBaseCd;
        e.OrderBaseCd = d.OrderBaseCd;
        e.StaffCd = d.StaffCd;
        e.ProCd = d.ProCd;
        e.CustomerCd = d.CustomerCd;
        e.ProjectNoParent = d.ProjectNoParent;
        e.ProjectNoChild = d.ProjectNoChild;
        e.ProjectNoMaterial = d.ProjectNoMaterial;
        e.OrderType = d.OrderType;
        e.ProductCategoryBig = d.ProductCategoryBig;
        e.ProductCategoryMid = d.ProductCategoryMid;
        e.ProductCategorySml = d.ProductCategorySml;
        e.CustomerProductName1 = d.CustomerProductName1;
        e.CustomerProductName2 = d.CustomerProductName2;
        e.OrderQty = d.OrderQty;
        e.OrderYm = d.OrderYm;
        e.ParentChildDiv = d.ParentChildDiv;
        e.FscProductDiv = d.FscProductDiv;
        e.FscMaterialDiv = d.FscMaterialDiv;

        e.SheetFlute = d.SheetFlute;
        e.PaperCdF = d.PaperCdF; e.PrintCdF = d.PrintCdF; e.EmbossCdF = d.EmbossCdF; e.PatternCntF = d.PatternCntF;
        e.PaperCdC = d.PaperCdC; e.PrintCdC = d.PrintCdC; e.EmbossCdC = d.EmbossCdC;
        e.PaperCdB = d.PaperCdB; e.PrintCdB = d.PrintCdB; e.EmbossCdB = d.EmbossCdB; e.PatternCntB = d.PatternCntB;
        e.SheetPrint = d.SheetPrint;
        e.BladeWidth = d.BladeWidth; e.BladeFlow = d.BladeFlow;
        e.GutterFb = d.GutterFb; e.GutterLr = d.GutterLr;
        e.SheetDimW = d.SheetDimW; e.SheetDimF = d.SheetDimF;
        e.FinalMachineProc = d.FinalMachineProc;
        e.ProductShape1 = d.ProductShape1; e.ProductShape2 = d.ProductShape2;
        e.DistDiv = d.DistDiv;
        e.RecyclePayment = d.RecyclePayment ?? "0";
        e.IdMark = d.IdMark;
        e.AdShape = d.AdShape;

        e.StrategicDiv01 = d.StrategicDivs.ElementAtOrDefault(0);
        e.StrategicDiv02 = d.StrategicDivs.ElementAtOrDefault(1);
        e.StrategicDiv03 = d.StrategicDivs.ElementAtOrDefault(2);
        e.StrategicDiv04 = d.StrategicDivs.ElementAtOrDefault(3);
        e.StrategicDiv05 = d.StrategicDivs.ElementAtOrDefault(4);
        e.StrategicDiv06 = d.StrategicDivs.ElementAtOrDefault(5);
        e.StrategicDiv07 = d.StrategicDivs.ElementAtOrDefault(6);
        e.StrategicDiv08 = d.StrategicDivs.ElementAtOrDefault(7);
        e.StrategicDiv09 = d.StrategicDivs.ElementAtOrDefault(8);
        e.StrategicDiv10 = d.StrategicDivs.ElementAtOrDefault(9);

        e.PrintNote = d.PrintNote; e.MfgNote = d.MfgNote; e.SlipNote = d.SlipNote;
        e.DeliveryNote = d.DeliveryNote; e.ShipNote1 = d.ShipNote1; e.ShipNote2 = d.ShipNote2;

        e.EstimateQty01 = d.EstimateQtys.ElementAtOrDefault(0);
        e.EstimateQty02 = d.EstimateQtys.ElementAtOrDefault(1);
        e.EstimateQty03 = d.EstimateQtys.ElementAtOrDefault(2);
        e.EstimateQty04 = d.EstimateQtys.ElementAtOrDefault(3);
        e.EstimateQty05 = d.EstimateQtys.ElementAtOrDefault(4);
        e.EstimateQty06 = d.EstimateQtys.ElementAtOrDefault(5);
        e.EstimateQty07 = d.EstimateQtys.ElementAtOrDefault(6);
        e.EstimateQty08 = d.EstimateQtys.ElementAtOrDefault(7);

        e.PalletCnt01 = d.PalletCnts.ElementAtOrDefault(0);
        e.PalletCnt02 = d.PalletCnts.ElementAtOrDefault(1);
        e.PalletCnt03 = d.PalletCnts.ElementAtOrDefault(2);
        e.PalletCnt04 = d.PalletCnts.ElementAtOrDefault(3);
        e.PalletCnt05 = d.PalletCnts.ElementAtOrDefault(4);
        e.PalletCnt06 = d.PalletCnts.ElementAtOrDefault(5);
        e.PalletCnt07 = d.PalletCnts.ElementAtOrDefault(6);
        e.PalletCnt08 = d.PalletCnts.ElementAtOrDefault(7);

        e.ProposalLot1 = d.ProposalLot1; e.ProposalLot2 = d.ProposalLot2;
        e.Unit = d.Unit; e.DecidedQty = d.DecidedQty;

        e.QtnDiv = d.QtnDiv;
        e.EstimateSqm = d.EstimateSqm;
        e.StandardUnitPrice = d.StandardUnitPrice;
        e.EstimateUnitPrice = d.EstimateUnitPrice;
        e.ConfirmedUnitPrice = d.ConfirmedUnitPrice;
    }

    private static EstimateCalcDto ToDto(EstimateCalc e) => new()
    {
        QtnCalcNo = e.QtnCalcNo,
        QtnDate = e.QtnDate,
        RefQtnCalcNo = e.RefQtnCalcNo,
        QtnBaseCd = e.QtnBaseCd,
        OrderBaseCd = e.OrderBaseCd,
        StaffCd = e.StaffCd,
        ProCd = e.ProCd,
        CustomerCd = e.CustomerCd,
        ProjectNoParent = e.ProjectNoParent,
        ProjectNoChild = e.ProjectNoChild,
        ProjectNoMaterial = e.ProjectNoMaterial,
        OrderType = e.OrderType,
        ProductCategoryBig = e.ProductCategoryBig,
        ProductCategoryMid = e.ProductCategoryMid,
        ProductCategorySml = e.ProductCategorySml,
        CustomerProductName1 = e.CustomerProductName1,
        CustomerProductName2 = e.CustomerProductName2,
        OrderQty = e.OrderQty,
        OrderYm = e.OrderYm,
        ParentChildDiv = e.ParentChildDiv,
        FscProductDiv = e.FscProductDiv,
        FscMaterialDiv = e.FscMaterialDiv,
        SheetFlute = e.SheetFlute,
        PaperCdF = e.PaperCdF, PrintCdF = e.PrintCdF, EmbossCdF = e.EmbossCdF, PatternCntF = e.PatternCntF,
        PaperCdC = e.PaperCdC, PrintCdC = e.PrintCdC, EmbossCdC = e.EmbossCdC,
        PaperCdB = e.PaperCdB, PrintCdB = e.PrintCdB, EmbossCdB = e.EmbossCdB, PatternCntB = e.PatternCntB,
        SheetPrint = e.SheetPrint,
        BladeWidth = e.BladeWidth, BladeFlow = e.BladeFlow,
        GutterFb = e.GutterFb, GutterLr = e.GutterLr,
        SheetDimW = e.SheetDimW, SheetDimF = e.SheetDimF,
        FinalMachineProc = e.FinalMachineProc,
        ProductShape1 = e.ProductShape1, ProductShape2 = e.ProductShape2,
        DistDiv = e.DistDiv, RecyclePayment = e.RecyclePayment, IdMark = e.IdMark, AdShape = e.AdShape,
        StrategicDivs = new[] {
            e.StrategicDiv01, e.StrategicDiv02, e.StrategicDiv03, e.StrategicDiv04, e.StrategicDiv05,
            e.StrategicDiv06, e.StrategicDiv07, e.StrategicDiv08, e.StrategicDiv09, e.StrategicDiv10,
        },
        PrintNote = e.PrintNote, MfgNote = e.MfgNote, SlipNote = e.SlipNote,
        DeliveryNote = e.DeliveryNote, ShipNote1 = e.ShipNote1, ShipNote2 = e.ShipNote2,
        EstimateQtys = new[] {
            e.EstimateQty01, e.EstimateQty02, e.EstimateQty03, e.EstimateQty04,
            e.EstimateQty05, e.EstimateQty06, e.EstimateQty07, e.EstimateQty08,
        },
        PalletCnts = new[] {
            e.PalletCnt01, e.PalletCnt02, e.PalletCnt03, e.PalletCnt04,
            e.PalletCnt05, e.PalletCnt06, e.PalletCnt07, e.PalletCnt08,
        },
        ProposalLot1 = e.ProposalLot1, ProposalLot2 = e.ProposalLot2,
        Unit = e.Unit, DecidedQty = e.DecidedQty,
        QtnDiv = e.QtnDiv,
        EstimateSqm = e.EstimateSqm,
        StandardUnitPrice = e.StandardUnitPrice,
        EstimateUnitPrice = e.EstimateUnitPrice,
        ConfirmedUnitPrice = e.ConfirmedUnitPrice,
        Processes = e.Processes.Where(p => !p.IsDeleted).OrderBy(p => p.SeqNo).Select(ToProcessDto).ToList(),
        RowVersion = e.RowVersion,
        CreateDate = e.CreateDate,
        ModifyDate = e.ModifyDate,
    };

    private static EstimateCalcProcessDto ToProcessDto(EstimateCalcProcess p) => new()
    {
        SeqNo = p.SeqNo,
        ProcessCd = p.ProcessCd, ProcessName = p.ProcessName,
        TaskCd = p.TaskCd, TaskName = p.TaskName,
        WgCd = p.WgCd, MfgLocation = p.MfgLocation,
        Spec1Label = p.Spec1Label, Spec1Val = p.Spec1Val,
        Spec2Label = p.Spec2Label, Spec2Val = p.Spec2Val,
        Spec3Label = p.Spec3Label, Spec3Val = p.Spec3Val,
        Spec4Label = p.Spec4Label, Spec4Val = p.Spec4Val,
        Spec5Label = p.Spec5Label, Spec5Val = p.Spec5Val,
        Spec6Label = p.Spec6Label, Spec6Val = p.Spec6Val,
        Spec7Label = p.Spec7Label, Spec7Val = p.Spec7Val,
        PlateNo = p.PlateNo, ProcNote1 = p.ProcNote1, ProcNote2 = p.ProcNote2,
    };

    private static EstimateCalcProcess ToProcessEntity(EstimateCalcProcessDto d, string no, int seq, string? user)
    {
        var p = new EstimateCalcProcess
        {
            QtnCalcNo = no,
            SeqNo = seq,
            Creator = user,
            CreateDate = DateTime.Now,
        };
        ApplyProcessDto(p, d);
        return p;
    }

    private static void ApplyProcessDto(EstimateCalcProcess p, EstimateCalcProcessDto d)
    {
        p.ProcessCd = d.ProcessCd; p.ProcessName = d.ProcessName;
        p.TaskCd = d.TaskCd; p.TaskName = d.TaskName;
        p.WgCd = d.WgCd; p.MfgLocation = d.MfgLocation;
        p.Spec1Label = d.Spec1Label; p.Spec1Val = d.Spec1Val;
        p.Spec2Label = d.Spec2Label; p.Spec2Val = d.Spec2Val;
        p.Spec3Label = d.Spec3Label; p.Spec3Val = d.Spec3Val;
        p.Spec4Label = d.Spec4Label; p.Spec4Val = d.Spec4Val;
        p.Spec5Label = d.Spec5Label; p.Spec5Val = d.Spec5Val;
        p.Spec6Label = d.Spec6Label; p.Spec6Val = d.Spec6Val;
        p.Spec7Label = d.Spec7Label; p.Spec7Val = d.Spec7Val;
        p.PlateNo = d.PlateNo; p.ProcNote1 = d.ProcNote1; p.ProcNote2 = d.ProcNote2;
    }
}
