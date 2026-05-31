using System.Globalization;
using System.Text;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels;
using CP6.Entity.DTOs;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services;

/// <summary>
/// MSBBPA070 / 080 / 090 — Web 受注 業務サービス
/// </summary>
/// <remarks>
/// 設計方針：
/// - T_OrderDetail は製品マスタの属性を 大量にスナップショットコピー（受注時点の固定値）
/// - 子表更新は「全削除 → 全挿入」方式（PA050 と同じ；楽観锁は親で担当）
/// - mcframe7 連携無し環境では isEditable / 仕掛 / 与信 はスタブ実装
/// </remarks>
public class OrderService : IOrderService
{
    private readonly CP6Context _db;
    private readonly IPowerEggWorkflowService _powerEgg;
    private readonly IWmsBridgeHook _wmsBridge;
    private readonly IMesBridgeHook _mesBridge;
    /// <summary>WebOrderNo 採番プレフィックス（業務 ID 識別子）</summary>
    private const string WebOrderPrefix = "WO";
    private const string McNullVal = "MCNULLVAL";
    /// <summary>1 受注当たりの最大明細行数</summary>
    private const int MaxDetailLimit = 500;

    public OrderService(CP6Context db, IPowerEggWorkflowService powerEgg, IWmsBridgeHook wmsBridge, IMesBridgeHook? mesBridge = null)
    {
        _db = db;
        _powerEgg = powerEgg;
        _wmsBridge = wmsBridge;
        _mesBridge = mesBridge ?? new NoOpMesBridgeHook();
    }

    // ═══════════════════════════════════════════════════════════
    //  詳細取得
    // ═══════════════════════════════════════════════════════════

    public async Task<OrderDto?> GetByWebOrderNoAsync(string webOrderNo, bool includeDeleted = false)
    {
        var headerQ = _db.Orders.AsNoTracking().Where(x => x.WebOrderNo == webOrderNo);
        if (!includeDeleted) headerQ = headerQ.Where(x => !x.IsDeleted);

        var header = await headerQ.FirstOrDefaultAsync();
        if (header == null) return null;

        var details = await _db.OrderDetails.AsNoTracking()
            .Where(x => x.WebOrderNo == webOrderNo && (includeDeleted || !x.IsDeleted))
            .OrderBy(x => x.WebOrderDetailNo)
            .ToListAsync();

        var processes = await _db.OrderProcesses.AsNoTracking()
            .Where(x => x.WebOrderNo == webOrderNo && (includeDeleted || !x.IsDeleted))
            .OrderBy(x => x.WebOrderDetailNo).ThenBy(x => x.SortOrder)
            .ToListAsync();

        var notes = await _db.OrderProcessNotes.AsNoTracking()
            .Where(x => x.WebOrderNo == webOrderNo && (includeDeleted || !x.IsDeleted))
            .ToListAsync();

        var materials = await _db.OrderMaterials.AsNoTracking()
            .Where(x => x.WebOrderNo == webOrderNo && (includeDeleted || !x.IsDeleted))
            .OrderBy(x => x.WebOrderDetailNo).ThenBy(x => x.SortOrder)
            .ToListAsync();

        var dto = HeaderToDto(header);

        foreach (var d in details)
        {
            var ddto = DetailToDto(d);
            ddto.Processes = processes
                .Where(p => p.WebOrderNo == d.WebOrderNo && p.WebOrderDetailNo == d.WebOrderDetailNo && p.ProductCd == d.ProductCd)
                .Select(ProcessToDto).ToList();
            ddto.ProcessNotes = notes
                .Where(p => p.WebOrderNo == d.WebOrderNo && p.WebOrderDetailNo == d.WebOrderDetailNo && p.ProductCd == d.ProductCd)
                .Select(ProcessNoteToDto).ToList();
            ddto.Materials = materials
                .Where(m => m.WebOrderNo == d.WebOrderNo && m.WebOrderDetailNo == d.WebOrderDetailNo && m.ProductCd == d.ProductCd)
                .Select(MaterialToDto).ToList();
            dto.Details.Add(ddto);
        }

        return dto;
    }

    // ═══════════════════════════════════════════════════════════
    //  登録（POST）
    // ═══════════════════════════════════════════════════════════

    public async Task<string> CreateAsync(OrderDto dto, string? userName)
    {
        if (dto.Details.Count == 0)
            throw new InvalidOperationException("登録する明細がありません。");
        if (dto.Details.Count > MaxDetailLimit)
            throw new InvalidOperationException($"明細行は {MaxDetailLimit} 件までです（現在: {dto.Details.Count} 件）。");
        if (string.IsNullOrWhiteSpace(dto.CustomerCd))
            throw new InvalidOperationException("得意先 CD は必須です。");
        if (string.IsNullOrWhiteSpace(dto.OrderType))
            throw new InvalidOperationException("受注区分は必須です。");

        // 客先納期 vs 工程 LT 合計（仕様書 §7.1 0-1）— 加工受注など isEditable=false 時のみ
        var isEditable = (await CheckIsEditableAsync(dto.OrderType, null, null)).IsEditable;
        if (!isEditable)
        {
            foreach (var d in dto.Details)
            {
                if (string.IsNullOrWhiteSpace(d.ProductCd)) continue;
                var (ok, ltSum, msg) = await CheckDeliveryLeadTimeAsync(d.ProductCd, dto.OrderDate, d.CustomerDeliveryDate ?? dto.CustomerDeliveryDate);
                if (!ok)
                    throw new InvalidOperationException($"明細 {d.WebOrderDetailNo} ({d.ProductCd}): {msg}");
            }
        }

        var webOrderNo = await NextSequenceAsync();
        var now = DateTime.Now;

        // ───── ヘッダー ─────
        var header = new Order
        {
            WebOrderNo = webOrderNo,
            CustomerCd = dto.CustomerCd,
            OrderType = dto.OrderType,
            OrderDepartment = dto.OrderDepartment,
            OrderDate = dto.OrderDate ?? now.Date,
            CustomerDeliveryDate = dto.CustomerDeliveryDate,
            Quantity = dto.Quantity,
            OrderSheetNo = dto.OrderSheetNo,
            CustomerContact = dto.CustomerContact,
            Addressee = dto.Addressee,
            Carrier = dto.Carrier,
            ShipDateTime = dto.ShipDateTime,
            ShipCondition = dto.ShipCondition,
            SalesPriceDiv = dto.SalesPriceDiv,
            McOrderNo = null,                  // 新規=NULL
            Status = 0,                        // 0=未転送
            McTransferFlg = false,
            Memo1 = dto.Memo1,
            Memo2 = dto.Memo2,
            Memo3 = dto.Memo3,
            Creator = userName,
            CreateDate = now,
            Modifier = userName,
            ModifyDate = now,
        };
        _db.Orders.Add(header);

        // ───── 明細 + 工程 + 備考 + 材料 ─────
        var detailNo = 0;
        foreach (var d in dto.Details)
        {
            detailNo++;
            d.WebOrderNo = webOrderNo;
            d.WebOrderDetailNo = detailNo;
            // 手配NO 初期値（Rev2.0 仕様）
            d.HaibaiNo1 ??= $"{webOrderNo}-{detailNo:D3}";
            d.HaibaiNo2 ??= dto.CustomerCd;
            d.HaibaiNo3 ??= d.ProjectNoParent;
            d.HaibaiNo4 = null;
            d.OrderType ??= dto.OrderType;
            d.Status = 0;
            d.WfApprovalFlg = false;
            d.McTransferFlg = false;
            d.McOrderNo = null;
            d.McOrderDetailNo = null;

            var ent = DtoToDetail(d);
            ent.Creator = userName;
            ent.CreateDate = now;
            ent.Modifier = userName;
            ent.ModifyDate = now;
            _db.OrderDetails.Add(ent);

            foreach (var p in d.Processes)
            {
                if (string.IsNullOrWhiteSpace(p.OperationCd) || string.IsNullOrWhiteSpace(p.ProcessCd))
                    continue; // 仕様書 6.3：未入力行はスキップ
                var pe = DtoToProcess(p, webOrderNo, detailNo, d.ProductCd);
                pe.Creator = userName; pe.CreateDate = now;
                pe.Modifier = userName; pe.ModifyDate = now;
                _db.OrderProcesses.Add(pe);
            }
            foreach (var n in d.ProcessNotes)
            {
                if (string.IsNullOrWhiteSpace(n.OperationCd)) continue;
                var ne = DtoToProcessNote(n, webOrderNo, detailNo, d.ProductCd);
                ne.Creator = userName; ne.CreateDate = now;
                ne.Modifier = userName; ne.ModifyDate = now;
                _db.OrderProcessNotes.Add(ne);
            }
            foreach (var m in d.Materials)
            {
                if (string.IsNullOrWhiteSpace(m.MaterialCd)) continue;
                var me = DtoToMaterial(m, webOrderNo, detailNo, d.ProductCd);
                me.Creator = userName; me.CreateDate = now;
                me.Modifier = userName; me.ModifyDate = now;
                _db.OrderMaterials.Add(me);
            }
        }

        await _db.SaveChangesAsync();

        // WM-3.5：WMS 自動展開フック（best-effort、失敗しても受注作成は成功とする）
        await _wmsBridge.OnOrderCreatedAsync(webOrderNo, userName);

        // Phase1：MES 製造指図 自動展開フック（既定無効・MesBridge:Enabled=true で有効化、best-effort）
        await _mesBridge.OnOrderCreatedAsync(webOrderNo, userName);

        return webOrderNo;
    }

    // ═══════════════════════════════════════════════════════════
    //  訂正（PUT）
    // ═══════════════════════════════════════════════════════════

    public async Task UpdateAsync(string webOrderNo, OrderDto dto, string? userName)
    {
        var entity = await _db.Orders
            .FirstOrDefaultAsync(x => x.WebOrderNo == webOrderNo && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"受注 '{webOrderNo}' が見つかりません。");

        if (dto.RowVersion != null && dto.RowVersion.Length > 0)
            _db.Entry(entity).Property(x => x.RowVersion).OriginalValue = dto.RowVersion;

        var now = DateTime.Now;

        // ヘッダー更新（mc 連携キー / Status / McTransferFlg は変更しない；6.1 仕様）
        entity.CustomerCd = dto.CustomerCd;
        entity.OrderType = dto.OrderType;
        entity.OrderDepartment = dto.OrderDepartment;
        entity.OrderDate = dto.OrderDate;
        entity.CustomerDeliveryDate = dto.CustomerDeliveryDate;
        entity.Quantity = dto.Quantity;
        entity.OrderSheetNo = dto.OrderSheetNo;
        entity.CustomerContact = dto.CustomerContact;
        entity.Addressee = dto.Addressee;
        entity.Carrier = dto.Carrier;
        entity.ShipDateTime = dto.ShipDateTime;
        entity.ShipCondition = dto.ShipCondition;
        entity.SalesPriceDiv = dto.SalesPriceDiv;
        entity.McTransferFlg = false; // 単価/属性が変わったので mc 再転送対象に
        entity.Memo1 = dto.Memo1;
        entity.Memo2 = dto.Memo2;
        entity.Memo3 = dto.Memo3;
        entity.Modifier = userName;
        entity.ModifyDate = now;

        // 子表：全削除→全挿入
        var detailKeys = await _db.OrderDetails
            .Where(x => x.WebOrderNo == webOrderNo)
            .Select(x => new { x.WebOrderNo, x.WebOrderDetailNo, x.ProductCd })
            .ToListAsync();

        var oldDetails = _db.OrderDetails.Where(x => x.WebOrderNo == webOrderNo);
        var oldProcesses = _db.OrderProcesses.Where(x => x.WebOrderNo == webOrderNo);
        var oldNotes = _db.OrderProcessNotes.Where(x => x.WebOrderNo == webOrderNo);
        var oldMaterials = _db.OrderMaterials.Where(x => x.WebOrderNo == webOrderNo);

        _db.OrderProcesses.RemoveRange(oldProcesses);
        _db.OrderProcessNotes.RemoveRange(oldNotes);
        _db.OrderMaterials.RemoveRange(oldMaterials);
        _db.OrderDetails.RemoveRange(oldDetails);

        var detailNo = 0;
        foreach (var d in dto.Details)
        {
            detailNo++;
            d.WebOrderNo = webOrderNo;
            d.WebOrderDetailNo = detailNo;
            d.HaibaiNo1 ??= $"{webOrderNo}-{detailNo:D3}";
            d.HaibaiNo2 ??= dto.CustomerCd;
            d.HaibaiNo3 ??= d.ProjectNoParent;
            d.OrderType ??= dto.OrderType;

            var ent = DtoToDetail(d);
            ent.Creator = userName;
            ent.CreateDate = now;
            ent.Modifier = userName;
            ent.ModifyDate = now;
            // mc 連携キーは保持（既に mc 側で確定している場合）
            _db.OrderDetails.Add(ent);

            foreach (var p in d.Processes)
            {
                if (string.IsNullOrWhiteSpace(p.OperationCd) || string.IsNullOrWhiteSpace(p.ProcessCd)) continue;
                var pe = DtoToProcess(p, webOrderNo, detailNo, d.ProductCd);
                pe.Creator = userName; pe.CreateDate = now;
                pe.Modifier = userName; pe.ModifyDate = now;
                _db.OrderProcesses.Add(pe);
            }
            foreach (var n in d.ProcessNotes)
            {
                if (string.IsNullOrWhiteSpace(n.OperationCd)) continue;
                var ne = DtoToProcessNote(n, webOrderNo, detailNo, d.ProductCd);
                ne.Creator = userName; ne.CreateDate = now;
                ne.Modifier = userName; ne.ModifyDate = now;
                _db.OrderProcessNotes.Add(ne);
            }
            foreach (var m in d.Materials)
            {
                if (string.IsNullOrWhiteSpace(m.MaterialCd)) continue;
                var me = DtoToMaterial(m, webOrderNo, detailNo, d.ProductCd);
                me.Creator = userName; me.CreateDate = now;
                me.Modifier = userName; me.ModifyDate = now;
                _db.OrderMaterials.Add(me);
            }
        }

        await _db.SaveChangesAsync();
    }

    // ═══════════════════════════════════════════════════════════
    //  削除（DELETE）— 軟削除
    // ═══════════════════════════════════════════════════════════

    public async Task DeleteAsync(string webOrderNo, byte[]? rowVersion, string? userName)
    {
        var entity = await _db.Orders
            .FirstOrDefaultAsync(x => x.WebOrderNo == webOrderNo && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"受注 '{webOrderNo}' が見つかりません。");

        if (rowVersion != null && rowVersion.Length > 0)
            _db.Entry(entity).Property(x => x.RowVersion).OriginalValue = rowVersion;

        var now = DateTime.Now;
        entity.IsDeleted = true;
        entity.Modifier = userName;
        entity.ModifyDate = now;

        await _db.OrderDetails.Where(x => x.WebOrderNo == webOrderNo)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.IsDeleted, true)
                .SetProperty(x => x.Modifier, userName)
                .SetProperty(x => x.ModifyDate, now));
        await _db.OrderProcesses.Where(x => x.WebOrderNo == webOrderNo)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.IsDeleted, true)
                .SetProperty(x => x.Modifier, userName)
                .SetProperty(x => x.ModifyDate, now));
        await _db.OrderProcessNotes.Where(x => x.WebOrderNo == webOrderNo)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.IsDeleted, true)
                .SetProperty(x => x.Modifier, userName)
                .SetProperty(x => x.ModifyDate, now));
        await _db.OrderMaterials.Where(x => x.WebOrderNo == webOrderNo)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.IsDeleted, true)
                .SetProperty(x => x.Modifier, userName)
                .SetProperty(x => x.ModifyDate, now));

        await _db.SaveChangesAsync();
    }

    // ═══════════════════════════════════════════════════════════
    //  採番
    // ═══════════════════════════════════════════════════════════

    public async Task<string> NextSequenceAsync()
    {
        // WebOrderNo = "WO" + 14桁数値（YYYYMMDD + 6桁連番）
        var datePart = DateTime.Now.ToString("yyyyMMdd");
        var prefix = WebOrderPrefix + datePart;

        var maxToday = await _db.Orders
            .Where(x => x.WebOrderNo.StartsWith(prefix))
            .OrderByDescending(x => x.WebOrderNo)
            .Select(x => x.WebOrderNo)
            .FirstOrDefaultAsync();

        int next = 1;
        if (!string.IsNullOrEmpty(maxToday) && maxToday.Length >= prefix.Length + 6)
        {
            if (int.TryParse(maxToday.Substring(prefix.Length, 6), out var n))
                next = n + 1;
        }
        return $"{prefix}{next:D6}";
    }

    // ═══════════════════════════════════════════════════════════
    //  検索引入仕様（NO.1〜NO.5）
    // ═══════════════════════════════════════════════════════════

    public async Task<List<OrderDetailDto>> LookupBySetProductCdAsync(string setProductCd)
    {
        // 仕様書 §5.1：製品 CD 検索（製品CD = 検索値 OR セット品CD = 検索値）
        var q = await _db.ProductMasters.AsNoTracking()
            .Where(p => !p.IsDeleted)
            .Where(p => p.ProductCd == setProductCd || p.SetProductCd == setProductCd)
            .OrderBy(p => p.ProductCd)
            .ToListAsync();

        return q.Select(p => new OrderDetailDto
        {
            ProductCd = p.ProductCd,
            ItemCd = p.ItemCd,
            Branch1 = p.Branch1,
            Branch2 = p.Branch2,
            Branch3 = p.Branch3,
            CustomerItemName1 = p.CustomerItemName1,
            CustomerItemName2 = p.CustomerItemName2,
            CpItemName1 = p.CpItemName1,
            CpItemName2 = p.CpItemName2,
            QtyUnit = p.QtyUnit,
            UnitPriceUnit = p.UnitPriceUnit,
            SetProductCd = p.SetProductCd,
            SetProductName = p.SetProductName,
            ParentChildDiv = p.ParentChildDiv,
            SetRatio = p.SetRatio,
            ProductCatBig = p.ProductCatBig,
            ProductCatMid = p.ProductCatMid,
            ProductCatSml = p.ProductCatSml,
        }).ToList();
    }

    public async Task<OrderDto?> LookupByHaibaiNoAsync(string? haibaiNo1, string? haibaiNo2, string? haibaiNo3)
    {
        // 仕様書 §5.2：手配NO検索（13+18 フィールドを引入）
        var q = _db.OrderDetails.AsNoTracking().Where(x => !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(haibaiNo1)) q = q.Where(x => x.HaibaiNo1 == haibaiNo1);
        if (!string.IsNullOrWhiteSpace(haibaiNo2)) q = q.Where(x => x.HaibaiNo2 == haibaiNo2);
        if (!string.IsNullOrWhiteSpace(haibaiNo3)) q = q.Where(x => x.HaibaiNo3 == haibaiNo3);

        var first = await q.OrderBy(x => x.WebOrderNo).ThenBy(x => x.WebOrderDetailNo).FirstOrDefaultAsync();
        if (first == null) return null;

        return await GetByWebOrderNoAsync(first.WebOrderNo);
    }

    public async Task<OrderDetailDto?> LookupProductMasterForDetailAsync(string productCd)
    {
        // 仕様書 §5.3：製品基本マスタから 63項目引入
        var p = await _db.ProductMasters.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ProductCd == productCd && !x.IsDeleted);
        if (p == null) return null;

        return new OrderDetailDto
        {
            ProductCd = p.ProductCd,
            ItemCd = p.ItemCd,
            Branch1 = p.Branch1,
            Branch2 = p.Branch2,
            Branch3 = p.Branch3,
            ProductCatBig = p.ProductCatBig,
            ProductCatMid = p.ProductCatMid,
            ProductCatSml = p.ProductCatSml,
            CustomerItemName1 = p.CustomerItemName1,
            CustomerItemName2 = p.CustomerItemName2,
            CustomerPartNo = p.CustomerPartNo,
            CpItemName1 = p.CpItemName1,
            CpItemName2 = p.CpItemName2,
            JanCode = p.JanCode,
            FscProductDiv = p.FscProductDiv,
            FscMaterialDiv = p.FscMaterialDiv,
            FscManagementNo = p.FscManagementNo,
            FoodSafety = p.FoodSafety,
            DeliveryReserve = p.DeliveryReserve,
            SalesSample = p.SalesSample,
            FixedShipment = p.FixedShipment,
            ShipInspection = p.ShipInspection,
            // 構成情報スナップショット（13フィールド）
            SheetFlute = p.SheetFlute,
            PaperCdF = p.PaperCdF, PaperCdC = p.PaperCdC, PaperCdB = p.PaperCdB,
            PrintCdF = p.PrintCdF, PrintCdC = p.PrintCdC, PrintCdB = p.PrintCdB,
            EmbossCdF = p.EmbossCdF, EmbossCdC = p.EmbossCdC, EmbossCdB = p.EmbossCdB,
            MakerCdF = p.MakerCdF, MakerCdB = p.MakerCdB,
            // 寸法
            SheetPrint = p.SheetPrint,
            BladeWidth = p.BladeWidth, BladeFlow = p.BladeFlow,
            GutterFb = p.GutterFb, GutterLr = p.GutterLr,
            SheetDimW = p.SheetDimW, SheetDimF = p.SheetDimF,
            FinalMachineProcess = p.FinalMachineProcess,
            // 備考
            PrintNote = p.PrintNote, MfgNote = p.MfgNote,
            SlipNote = p.SlipNote, DeliveryNote = p.DeliveryNote,
            ShipNote1 = p.ShipNote1, ShipNote2 = p.ShipNote2,
            // 単価関連は受注時に設定（NULL）
            QtyUnit = p.QtyUnit,
            UnitPriceUnit = p.UnitPriceUnit,
            // セット品
            SetProductCd = p.SetProductCd,
            SetProductName = p.SetProductName,
            ParentChildDiv = p.ParentChildDiv,
            SetRatio = p.SetRatio,
            // 製品属性スナップショット
            ProductUsage = p.ProductUsage,
            DistributionDiv = p.DistributionDiv,
            ConfidentialInfo = p.ConfidentialInfo,
            SeizureDiv = p.SeizureDiv,
            ImportanceDiv = p.ImportanceDiv,
            MChange = p.MChange,
            QualityDiv = p.QualityDiv,
            ProductShape = p.ProductShape,
            UnescoMark = p.UnescoMark,
            OrigamiMark = p.OrigamiMark,
            FourMContract = p.FourMContract,
            TkpWrinkleStd = p.TkpWrinkleStd,
            RecyclingPayment = p.RecyclingPayment,
            // 容リ法 使用量
            PaperUsageG = p.PaperUsageG,
            PlasticUsageG = p.PlasticUsageG,
            GlassUsageG = p.GlassUsageG,
            PetUsageG = p.PetUsageG,
            PackPaperUsageG = p.PackPaperUsageG,
            PackPlasticUsageG = p.PackPlasticUsageG,
            DesignProposalNo = p.DesignProposalNo,
            SalesPriceDiv = p.SalesPriceDiv,
            FreightBilling = p.FreightBilling,
            PurchaseVendor = p.PurchaseVendor,
        };
    }

    public async Task<List<OrderProcessDto>> LookupProductProcessesAsync(string productCd)
    {
        // 仕様書 §5.4：製品加工工程マスタから 35項目引入
        var rows = await _db.ProductProcesses.AsNoTracking()
            .Where(x => x.ProductCd == productCd && !x.IsDeleted)
            .OrderBy(x => x.SortOrder)
            .ToListAsync();

        return rows.Select(x => new OrderProcessDto
        {
            ProductCd = x.ProductCd,
            OperationCd = x.TaskCd,
            ProcessCd = x.ProcessCd,
            WorkingGroupCd = x.WgCd,
            MachineOrVendor = x.MachineOrVendor,
            MachineFixedFlg = x.MachineFixedFlg,
            CpDeliveryDiv = x.CpDeliveryDiv,
            Spec01 = x.Spec01, Spec02 = x.Spec02, Spec03 = x.Spec03, Spec04 = x.Spec04, Spec05 = x.Spec05,
            Spec06 = x.Spec06, Spec07 = x.Spec07,
            PlateNo1 = x.PlateNo1, PlateNo2 = x.PlateNo2, PlateNo3 = x.PlateNo3,
            Consumable1 = x.Consumable1, Consumable2 = x.Consumable2, Consumable3 = x.Consumable3,
            PurchaseUnitPrice = x.PurchasePrice,
            FixedPrice = x.FixedPrice,
            LossRate = x.LossRate ?? 0m,
            MachineCount = x.MachineCount ?? 0m,
            LeadTimeDays = (int)(x.LeadTime ?? 0m),
            SortOrder = x.SortOrder,
        }).ToList();
    }

    public async Task<List<OrderMaterialDto>> LookupProductMaterialsAsync(string productCd)
    {
        // 仕様書 §5.5：製品加工材料マスタから引入
        var rows = await _db.ProductMaterials.AsNoTracking()
            .Where(x => x.ProductCd == productCd && !x.IsDeleted)
            .OrderBy(x => x.SortOrder)
            .ToListAsync();

        return rows.Select(x => new OrderMaterialDto
        {
            ProductCd = x.ProductCd,
            ProcessCd = x.ProcessCd,
            MaterialCd = x.MaterialCd,
            MaterialTypeDiv = x.MaterialTypeDiv,
            ItemCd = x.ItemCd,
            Branch1 = x.Branch1,
            Branch2 = x.Branch2,
            Branch3 = x.Branch3,
            SupplyDiv = x.SupplyDiv ?? "1",
            SupplyUnitPrice = x.SupplyPrice ?? 0m,
            SortOrder = x.SortOrder,
        }).ToList();
    }

    // ═══════════════════════════════════════════════════════════
    //  業務ルール
    // ═══════════════════════════════════════════════════════════

    public Task<IsEditableResultDto> CheckIsEditableAsync(string orderType, string? productCatBig, string? productCd)
    {
        // 仕様書 §補足説明資料_受注画面制御 / §処理詳細 §0
        // 加工受注 (orderType=10) 等は構成・工程「参照のみ」
        // シート受注 (orderType=20)・原紙(40)・輪切り(80) は「編集可能」
        var editableTypes = new HashSet<string> { "20", "40", "80" };
        var isEditable = editableTypes.Contains(orderType);
        return Task.FromResult(new IsEditableResultDto
        {
            IsEditable = isEditable,
            Reason = isEditable
                ? "シート受注/原紙/輪切り → 構成・工程編集可"
                : "加工受注/購買品/商品 → 構成・工程は参照のみ（工程備考のみ入力可）",
        });
    }

    public Task<OrderWipCheckResultDto> CheckWipAsync(string webOrderNo, int webOrderDetailNo)
    {
        // mcframe7 連携無し環境 → 常に Level=0（問題なし）
        // 仕様書 §8 0-4-1 (L2 保存・確定済) / 0-4-2 (L3 指図済) は mcframe7 の品目オーダ状況区分を参照
        return Task.FromResult(new OrderWipCheckResultDto
        {
            Level = 0,
            Message = "問題なし（mcframe7 連携無し環境のため固定 Level=0）",
        });
    }

    public async Task<CreditCheckResultDto> CheckCreditAsync(string customerCd, decimal newOrderAmount)
    {
        // 与信マスタが存在しない環境ではダミー：得意先別 受注合計が 1,000万円 を超えると警告
        const decimal defaultLimit = 10_000_000m;
        var balance = await _db.OrderDetails.AsNoTracking()
            .Where(x => !x.IsDeleted && x.HaibaiNo2 == customerCd)
            .SumAsync(x => (decimal?)x.Amount ?? 0m);
        var isOver = (balance + newOrderAmount) > defaultLimit;
        return new CreditCheckResultDto
        {
            IsOver = isOver,
            CreditLimit = defaultLimit,
            Balance = balance,
            Message = isOver
                ? $"与信限度額 {defaultLimit:N0} を超過します（残高: {balance:N0} + 新規: {newOrderAmount:N0}）"
                : "与信範囲内",
        };
    }

    public async Task<bool> CheckConsignedSalesQtyAsync(string webOrderNo, int webOrderDetailNo, decimal consignedSalesQty)
    {
        // Rev4：預り売上数 ≤ 受注残数（=受注数量 − 既出荷数 − 既預り売上数）
        // 出荷数の連携が無い環境では「預り売上数 ≤ 受注数量」のみで判定
        var d = await _db.OrderDetails.AsNoTracking()
            .Where(x => x.WebOrderNo == webOrderNo && x.WebOrderDetailNo == webOrderDetailNo && !x.IsDeleted)
            .Select(x => new { x.Quantity })
            .FirstOrDefaultAsync();
        if (d == null) return true; // 新規行は OK
        var orderQty = d.Quantity ?? 0m;
        return consignedSalesQty <= orderQty;
    }

    public Task<List<DateTime>> CalcLeadTimeAsync(string shipDateTime, IEnumerable<int> leadTimeDays, string? wgCd)
    {
        // 簡易実装：拠点別カレンダ無し → 土日のみ非稼働日として営業日逆算
        var result = new List<DateTime>();
        if (!DateTime.TryParse(shipDateTime, CultureInfo.InvariantCulture, DateTimeStyles.None, out var shipDate))
            return Task.FromResult(result);

        var current = shipDate.Date;
        foreach (var lt in leadTimeDays.Reverse())
        {
            for (int i = 0; i < lt; i++)
            {
                current = current.AddDays(-1);
                while (current.DayOfWeek == DayOfWeek.Saturday || current.DayOfWeek == DayOfWeek.Sunday)
                    current = current.AddDays(-1);
            }
            result.Insert(0, current);
        }
        return Task.FromResult(result);
    }

    // ═══════════════════════════════════════════════════════════
    //  PA080 一覧照会
    // ═══════════════════════════════════════════════════════════

    public async Task<(List<OrderListItemDto>, int)> SearchOrdersAsync(OrderQueryDto query)
    {
        var q = BuildOrderListQuery(query);
        var total = await q.CountAsync();

        if (query.MaxRows.HasValue && total > query.MaxRows.Value)
        {
            // E10013：強制截断
            var rows = await ProjectListAsync(q.OrderBy(x => x.HaibaiNo2).ThenBy(x => x.HaibaiNo1)
                .ThenBy(x => x.WebOrderNo).ThenBy(x => x.WebOrderDetailNo)
                .Take(query.MaxRows.Value));
            NumberRows(rows);
            return (rows, total);
        }

        var page = await ProjectListAsync(q
            .OrderBy(x => x.HaibaiNo2).ThenBy(x => x.HaibaiNo1)
            .ThenBy(x => x.WebOrderNo).ThenBy(x => x.WebOrderDetailNo)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize));
        // 行番号の付与（ページ局所）
        for (int i = 0; i < page.Count; i++) page[i].RowNo = (query.Page - 1) * query.PageSize + i + 1;
        return (page, total);
    }

    public async Task<byte[]> ExportListCsvAsync(OrderQueryDto query)
    {
        var q = BuildOrderListQuery(query);
        var rows = await ProjectListAsync(q.OrderBy(x => x.HaibaiNo2).ThenBy(x => x.HaibaiNo1)
            .ThenBy(x => x.WebOrderNo).ThenBy(x => x.WebOrderDetailNo));
        NumberRows(rows);

        var sb = new StringBuilder();
        sb.AppendLine("NO,得意先,担当,得意先名,担当者名,注文書NO,手配NO1,不適合手配NO,注文NO(mc),受注日,客先納期,製品CD,品目CD,製品区分(大),CP品名/構成,段,表(構成),中(構成),裏(構成),全判巾,全判流れ,スリ巾,スリ流れ,数量単位,数量,個別単価,セット単価,単価単位,受注金額,預り売上,伝票備考,納入備考,WEB受注NO,WEB受注明細NO");
        foreach (var r in rows)
        {
            sb.AppendLine(string.Join(',', new[]
            {
                r.RowNo.ToString(),
                CsvE(r.CustomerCd), CsvE(r.SalesPersonCd), CsvE(r.CustomerName), CsvE(r.SalesPersonName),
                CsvE(r.OrderSheetNo), CsvE(r.HaibaiNo1), CsvE(r.DefectiveHaibaiNo), CsvE(r.McOrderNo),
                r.OrderDate?.ToString("yyyy-MM-dd") ?? "", r.CustomerDeliveryDate?.ToString("yyyy-MM-dd") ?? "",
                CsvE(r.ProductCd), CsvE(r.ItemCd), CsvE(r.ProductCatBigName ?? r.ProductCatBig),
                CsvE(r.CpItemOrComposition), CsvE(r.SheetFlute),
                CsvE(r.CompositionF), CsvE(r.CompositionC), CsvE(r.CompositionB),
                r.FullSheetWidth?.ToString("0.##") ?? "", r.FullSheetFlow?.ToString("0.##") ?? "",
                r.SlitterWidth?.ToString("0.##") ?? "", r.SlitterFlow?.ToString("0.##") ?? "",
                CsvE(r.QtyUnit), r.Quantity?.ToString("0.##") ?? "",
                r.IndividualUnitPrice?.ToString("0.####") ?? "", r.SetUnitPrice?.ToString("0.####") ?? "",
                CsvE(r.UnitPriceUnit), r.Amount?.ToString("0.####") ?? "",
                CsvE(r.ConsignedSalesFlg), CsvE(r.SlipNote), CsvE(r.DeliveryNote),
                CsvE(r.WebOrderNo), r.WebOrderDetailNo.ToString(),
            }));
        }

        // BOM 付き UTF-8（Excel 文字化け対策）
        var preamble = Encoding.UTF8.GetPreamble();
        var body = Encoding.UTF8.GetBytes(sb.ToString());
        var output = new byte[preamble.Length + body.Length];
        Buffer.BlockCopy(preamble, 0, output, 0, preamble.Length);
        Buffer.BlockCopy(body, 0, output, preamble.Length, body.Length);
        return output;
    }

    // ═══════════════════════════════════════════════════════════
    //  PA090 単価訂正
    // ═══════════════════════════════════════════════════════════

    public async Task<(List<OrderPriceCorrectionItemDto>, int)> SearchPriceCorrectionsAsync(OrderPriceCorrectionQueryDto query)
    {
        if (string.IsNullOrWhiteSpace(query.BaseCd))
            throw new InvalidOperationException("E10022: 拠点を指定してください。");

        var q = _db.OrderDetails.AsNoTracking().Where(x => !x.IsDeleted);

        if (!string.IsNullOrWhiteSpace(query.CustomerCd))
            q = q.Where(x => string.Compare(x.HaibaiNo2 ?? "", query.CustomerCd) >= 0);
        if (!string.IsNullOrWhiteSpace(query.CustomerCdTo))
            q = q.Where(x => string.Compare(x.HaibaiNo2 ?? "", query.CustomerCdTo) <= 0);
        if (!string.IsNullOrWhiteSpace(query.HaibaiNo1)) q = q.Where(x => x.HaibaiNo1 == query.HaibaiNo1);
        if (!string.IsNullOrWhiteSpace(query.OrderSheetNo))
        {
            // OrderSheetNo はヘッダー側
            var headerNos = _db.Orders.AsNoTracking()
                .Where(o => !o.IsDeleted && o.OrderSheetNo == query.OrderSheetNo)
                .Select(o => o.WebOrderNo);
            q = q.Where(x => headerNos.Contains(x.WebOrderNo));
        }
        if (!string.IsNullOrWhiteSpace(query.ProductCd))
            q = q.Where(x => x.ProductCd == query.ProductCd);
        if (!string.IsNullOrWhiteSpace(query.CustomerItemName))
        {
            var p = query.CustomerItemName;
            q = q.Where(x => (x.CustomerItemName1 ?? "").Contains(p) || (x.CustomerItemName2 ?? "").Contains(p));
        }
        if (query.OrderDateFrom.HasValue || query.OrderDateTo.HasValue)
        {
            var headerNos = _db.Orders.AsNoTracking().Where(o => !o.IsDeleted);
            if (query.OrderDateFrom.HasValue) headerNos = headerNos.Where(o => o.OrderDate >= query.OrderDateFrom.Value);
            if (query.OrderDateTo.HasValue) headerNos = headerNos.Where(o => o.OrderDate <= query.OrderDateTo.Value);
            var nos = headerNos.Select(o => o.WebOrderNo);
            q = q.Where(x => nos.Contains(x.WebOrderNo));
        }
        if (query.DeliveryDateFrom.HasValue) q = q.Where(x => x.CustomerDeliveryDate >= query.DeliveryDateFrom.Value);
        if (query.DeliveryDateTo.HasValue) q = q.Where(x => x.CustomerDeliveryDate <= query.DeliveryDateTo.Value);
        if (query.QtyFrom.HasValue) q = q.Where(x => x.Quantity >= query.QtyFrom.Value);
        if (query.QtyTo.HasValue) q = q.Where(x => x.Quantity <= query.QtyTo.Value);
        if (query.AmountFrom.HasValue) q = q.Where(x => x.Amount >= query.AmountFrom.Value);
        if (query.AmountTo.HasValue) q = q.Where(x => x.Amount <= query.AmountTo.Value);
        if (query.OnlyProvisional == true) q = q.Where(x => x.ProvisionalPriceFlg);
        if (!string.IsNullOrWhiteSpace(query.ApprovalStatus))
        {
            if (int.TryParse(query.ApprovalStatus, out var s)) q = q.Where(x => x.ApprovalStatus == s);
        }

        var total = await q.CountAsync();
        if (query.MaxRows.HasValue && total > query.MaxRows.Value)
            throw new InvalidOperationException($"E10013: 検索件数が上限 {query.MaxRows.Value} 件を超えています（{total} 件）");

        var rows = await q
            .OrderBy(x => x.HaibaiNo2).ThenBy(x => x.HaibaiNo1).ThenBy(x => x.WebOrderDetailNo)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(x => new OrderPriceCorrectionItemDto
            {
                ApprovalStatus = x.ApprovalStatus,
                CustomerCd = x.HaibaiNo2,
                SalesPersonCd = null,
                OrderSheetNo = null,
                HaibaiNo1 = x.HaibaiNo1,
                DefectiveHaibaiNo = x.DefectiveHaibaiNo,
                ProductCd = x.ProductCd,
                ItemCd = x.ItemCd,
                CpItemName1 = x.CpItemName1,
                CpItemName2 = x.CpItemName2,
                CustomerDeliveryDate = x.CustomerDeliveryDate,
                Unit = x.PurchaseUnit,
                CpItemOrComposition = x.CpItemName1, // 簡略化：シート受注時は構成名に置換
                SheetFlute = x.SheetFlute,
                CompositionF = (x.PaperCdF ?? "") + (string.IsNullOrEmpty(x.PrintCdF) ? "" : "+" + x.PrintCdF) + (string.IsNullOrEmpty(x.EmbossCdF) ? "" : "+" + x.EmbossCdF),
                CompositionC = (x.PaperCdC ?? "") + (string.IsNullOrEmpty(x.PrintCdC) ? "" : "+" + x.PrintCdC) + (string.IsNullOrEmpty(x.EmbossCdC) ? "" : "+" + x.EmbossCdC),
                CompositionB = (x.PaperCdB ?? "") + (string.IsNullOrEmpty(x.PrintCdB) ? "" : "+" + x.PrintCdB) + (string.IsNullOrEmpty(x.EmbossCdB) ? "" : "+" + x.EmbossCdB),
                FullSheetWidth = x.BladeWidth,
                FullSheetFlow = x.BladeFlow,
                SlitterWidth = x.SheetDimW,
                SlitterFlow = x.SheetDimF,
                Quantity = x.Quantity,
                QtyUnit = x.QtyUnit,
                IndividualUnitPriceBefore = x.IndividualUnitPrice,
                SetUnitPriceBefore = x.SetUnitPrice,
                UnitPriceUnit = x.UnitPriceUnit,
                IndividualUnitPriceAfter = x.IndividualUnitPrice,
                SetUnitPriceAfter = x.SetUnitPrice,
                SpecialPriceFlg = x.SpecialPriceFlg,
                ProvisionalPriceFlg = x.ProvisionalPriceFlg,
                PriceChangeReason = x.PriceChangeReason,
                Amount = x.Amount,
                ConsignedSalesFlg = x.ConsignedSalesFlg,
                SlipNote = x.SlipNote,
                DeliveryNote = x.DeliveryNote,
                WebOrderNo = x.WebOrderNo,
                WebOrderDetailNo = x.WebOrderDetailNo,
                RowVersion = x.RowVersion,
            })
            .ToListAsync();

        // 受注日 / 担当 を Order/取引先マスタからフェッチして埋める
        var webOrderNos = rows.Select(r => r.WebOrderNo).Distinct().ToList();
        var headers = await _db.Orders.AsNoTracking()
            .Where(o => webOrderNos.Contains(o.WebOrderNo))
            .Select(o => new { o.WebOrderNo, o.OrderDate, o.OrderSheetNo, o.CustomerCd })
            .ToListAsync();
        var headerMap = headers.ToDictionary(h => h.WebOrderNo);
        for (int i = 0; i < rows.Count; i++)
        {
            rows[i].RowNo = (query.Page - 1) * query.PageSize + i + 1;
            if (headerMap.TryGetValue(rows[i].WebOrderNo, out var h))
            {
                rows[i].OrderDate = h.OrderDate;
                rows[i].OrderSheetNo = h.OrderSheetNo;
            }
        }
        return (rows, total);
    }

    public async Task<OrderPriceCorrectionBatchResultDto> BatchUpdatePriceAsync(
        OrderPriceCorrectionBatchUpdateDto request, string? userName)
    {
        var result = new OrderPriceCorrectionBatchResultDto();
        if (request.Items.Count == 0) return result;

        var now = DateTime.Now;
        var headerSetUnitPriceMap = new Dictionary<string, decimal?>();

        foreach (var item in request.Items)
        {
            var entity = await _db.OrderDetails
                .FirstOrDefaultAsync(x => x.WebOrderNo == item.WebOrderNo
                                       && x.WebOrderDetailNo == item.WebOrderDetailNo
                                       && !x.IsDeleted);
            if (entity == null)
            {
                result.ConflictedKeys.Add($"{item.WebOrderNo}-{item.WebOrderDetailNo}");
                continue;
            }
            if (item.RowVersion != null && item.RowVersion.Length > 0)
                _db.Entry(entity).Property(x => x.RowVersion).OriginalValue = item.RowVersion;

            // 単価変更があったかチェック（変更ありなら 承認状況=1, WfApprovalFlg=false に戻す）
            var priceChanged =
                item.IndividualUnitPriceAfter != entity.IndividualUnitPrice ||
                item.SetUnitPriceAfter != entity.SetUnitPrice ||
                item.SpecialPriceFlg != entity.SpecialPriceFlg;

            entity.IndividualUnitPrice = item.IndividualUnitPriceAfter;
            entity.SetUnitPrice = item.SetUnitPriceAfter;
            entity.SpecialPriceFlg = item.SpecialPriceFlg;
            entity.PriceChangeReason = item.PriceChangeReason;
            entity.ProvisionalPriceFlg = false; // 仕様書 §2.6：本単価に確定
            // 金額再計算
            entity.Amount = (entity.Quantity ?? 0m) * (entity.SalesPriceDiv == "1"
                ? (entity.IndividualUnitPrice ?? 0m)
                : (entity.SetUnitPrice ?? 0m));
            if (priceChanged)
            {
                entity.ApprovalStatus = 1; // 承認依頼中
                entity.WfApprovalFlg = false;
                result.WfRequestedCount++;
                // POWER EGG WF 起票（46項目）— 環境無のためログ出力のみ
                await PostPowerEggWorkflowAsync(entity, userName);
            }
            entity.Modifier = userName;
            entity.ModifyDate = now;

            // セット単価一括更新（NO.1'）：同一 WebOrderNo の全明細
            if (item.SetUnitPriceAfter.HasValue)
                headerSetUnitPriceMap[item.WebOrderNo] = item.SetUnitPriceAfter;

            result.UpdatedCount++;
        }

        // セット単価一括更新
        foreach (var kv in headerSetUnitPriceMap)
        {
            await _db.OrderDetails
                .Where(x => x.WebOrderNo == kv.Key && !x.IsDeleted)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.SetUnitPrice, kv.Value)
                    .SetProperty(x => x.ModifyDate, now)
                    .SetProperty(x => x.Modifier, userName));
        }

        await _db.SaveChangesAsync();
        return result;
    }

    /// <summary>POWER EGG WF 起票（IPowerEggWorkflowService に委譲）</summary>
    private async Task PostPowerEggWorkflowAsync(OrderDetail entity, string? userName)
    {
        // 仕様書 §第三部分 3：46項目 を POWER EGG に送信して WF 起票
        // 実環境では HTTP 実装に DI で差し替え
        await _powerEgg.RequestPriceCorrectionAsync(entity, userName);
    }

    // ═══════════════════════════════════════════════════════════
    //  Phase E：共通処理 / 業務ヘルパー
    // ═══════════════════════════════════════════════════════════

    public async Task<decimal> CalcAmountAsync(string webOrderNo, int detailNo, decimal? newIndPrice, decimal? newSetPrice)
    {
        // 仕様書 §PA090 NO.33：個別売=数量×個別単価 / セット売=数量×セット単価
        var d = await _db.OrderDetails.AsNoTracking()
            .Where(x => x.WebOrderNo == webOrderNo && x.WebOrderDetailNo == detailNo && !x.IsDeleted)
            .Select(x => new { x.Quantity, x.SalesPriceDiv })
            .FirstOrDefaultAsync();
        if (d == null) return 0m;
        var qty = d.Quantity ?? 0m;
        var price = d.SalesPriceDiv == "1" ? (newIndPrice ?? 0m) : (newSetPrice ?? 0m);
        return qty * price;
    }

    public async Task<(string? CatBig, string? CatMid, string? CatSml)> CalcProductCategoryAsync(
        string productCd, IEnumerable<OrderProcessDto> processes)
    {
        // 仕様書 §処理詳細「製品区分設定」：
        // 工程の最終機械工程 → 機械マスタ → 製品区分_大/中/小 を引継ぐ
        // mcframe7 機械マスタ無し環境 → 製品マスタの値をそのまま使う
        var p = await _db.ProductMasters.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ProductCd == productCd && !x.IsDeleted);
        if (p != null) return (p.ProductCatBig, p.ProductCatMid, p.ProductCatSml);

        // 製品マスタなし → 工程の最終行から推測
        var last = processes?.OrderByDescending(x => x.SortOrder).FirstOrDefault();
        return (last?.WorkingGroupCd, null, null);
    }

    public async Task<List<OrderMaterialDto>> CalcMaterialsAsync(
        string productCd, IEnumerable<OrderProcessDto> processes)
    {
        // 仕様書 §処理詳細「材料設定」：
        // 製品加工材料マスタを基に「BOM 展開」「品目マスタ連携」を実行
        // ① 製品マスタの材料マスタを引入（mcframe7 BOM が無い環境では製品材料マスタをそのまま採用）
        var baseRows = await _db.ProductMaterials.AsNoTracking()
            .Where(x => x.ProductCd == productCd && !x.IsDeleted)
            .OrderBy(x => x.SortOrder)
            .ToListAsync();

        var processCds = processes?.Select(p => p.ProcessCd).Where(c => !string.IsNullOrEmpty(c)).ToHashSet()
                          ?? new HashSet<string>();

        return baseRows
            // 工程に存在する材料行のみ採用（工程変更後の整合性確保）
            .Where(r => processCds.Count == 0 || processCds.Contains(r.ProcessCd))
            .Select(r => new OrderMaterialDto
            {
                ProductCd = r.ProductCd,
                ProcessCd = r.ProcessCd,
                MaterialCd = r.MaterialCd,
                MaterialTypeDiv = r.MaterialTypeDiv,
                ItemCd = r.ItemCd,
                Branch1 = r.Branch1,
                Branch2 = r.Branch2,
                Branch3 = r.Branch3,
                SupplyDiv = r.SupplyDiv ?? "1",
                SupplyUnitPrice = r.SupplyPrice ?? 0m,
                SortOrder = r.SortOrder,
            }).ToList();
    }

    public async Task<(bool Ok, int LtSumDays, string? Message)> CheckDeliveryLeadTimeAsync(
        string productCd, DateTime? orderDate, DateTime? deliveryDate)
    {
        if (!orderDate.HasValue || !deliveryDate.HasValue)
            return (true, 0, null); // 日付未設定はチェック不要

        var procs = await _db.ProductProcesses.AsNoTracking()
            .Where(x => x.ProductCd == productCd && !x.IsDeleted)
            .Select(x => new { x.LeadTime })
            .ToListAsync();

        var ltSum = (int)procs.Sum(x => x.LeadTime ?? 0m);
        var avail = (int)(deliveryDate.Value - orderDate.Value).TotalDays;

        if (avail < ltSum)
            return (false, ltSum,
                $"納期まで製造必要日数を下回っています（必要LT合計={ltSum}日 / 残り={avail}日）");
        return (true, ltSum, null);
    }

    public async Task<byte[]> ExportOrderReportPdfAsync(IEnumerable<string> webOrderDetailKeys)
    {
        // 仕様書 §PA080 受注伝票 PDF 発行
        // 簡易実装：選択行の受注情報を プレーンテキスト→ "PDFっぽい" バイナリ化
        // 本格実装は QuestPDF / iText / Crystal Reports などを採用
        var keys = webOrderDetailKeys.ToList();
        var sb = new StringBuilder();
        sb.AppendLine("受注伝票（簡易版）");
        sb.AppendLine($"発行日時: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine(new string('=', 60));

        foreach (var key in keys)
        {
            // key 形式：WebOrderNo-DetailNo
            var parts = key.Split('-');
            if (parts.Length < 2) continue;
            if (!int.TryParse(parts[^1], out var detailNo)) continue;
            var webOrderNo = string.Join('-', parts[..^1]);

            var d = await _db.OrderDetails.AsNoTracking()
                .FirstOrDefaultAsync(x => x.WebOrderNo == webOrderNo
                                       && x.WebOrderDetailNo == detailNo
                                       && !x.IsDeleted);
            if (d == null) continue;

            sb.AppendLine($"手配NO1   : {d.HaibaiNo1}");
            sb.AppendLine($"得意先     : {d.HaibaiNo2}");
            sb.AppendLine($"製品CD     : {d.ProductCd}");
            sb.AppendLine($"CP品名     : {d.CpItemName1}");
            sb.AppendLine($"客先納期   : {d.CustomerDeliveryDate:yyyy-MM-dd}");
            sb.AppendLine($"数量×単価 : {d.Quantity} × {d.SetUnitPrice ?? d.IndividualUnitPrice} = {d.Amount}");
            sb.AppendLine(new string('-', 60));
        }

        // 簡易 PDF（実体はテキスト bytes）— 本番では QuestPDF へ差し替え
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    // ═══════════════════════════════════════════════════════════
    //  Helper：PA080 リスト構築
    // ═══════════════════════════════════════════════════════════

    private IQueryable<OrderDetail> BuildOrderListQuery(OrderQueryDto query)
    {
        var q = _db.OrderDetails.AsNoTracking().Where(x => query.IncludeDeleted == true || !x.IsDeleted);

        // ヘッダー条件は WebOrderNo を介して絞り込み
        var hq = _db.Orders.AsNoTracking().Where(o => query.IncludeDeleted == true || !o.IsDeleted);
        bool hasHeaderFilter = false;
        if (!string.IsNullOrWhiteSpace(query.OrderType)) { hq = hq.Where(o => o.OrderType == query.OrderType); hasHeaderFilter = true; }
        if (query.OrderDateFrom.HasValue) { hq = hq.Where(o => o.OrderDate >= query.OrderDateFrom.Value); hasHeaderFilter = true; }
        if (query.OrderDateTo.HasValue) { hq = hq.Where(o => o.OrderDate <= query.OrderDateTo.Value); hasHeaderFilter = true; }
        if (!string.IsNullOrWhiteSpace(query.OrderSheetNo)) { hq = hq.Where(o => o.OrderSheetNo == query.OrderSheetNo); hasHeaderFilter = true; }
        if (!string.IsNullOrWhiteSpace(query.Carrier)) { hq = hq.Where(o => o.Carrier == query.Carrier); hasHeaderFilter = true; }
        if (!string.IsNullOrWhiteSpace(query.SalesPriceDiv)) { hq = hq.Where(o => o.SalesPriceDiv == query.SalesPriceDiv); hasHeaderFilter = true; }
        if (!string.IsNullOrWhiteSpace(query.McOrderNo)) { hq = hq.Where(o => o.McOrderNo == query.McOrderNo); hasHeaderFilter = true; }
        if (hasHeaderFilter)
        {
            var nos = hq.Select(o => o.WebOrderNo);
            q = q.Where(x => nos.Contains(x.WebOrderNo));
        }

        // 明細条件
        if (!string.IsNullOrWhiteSpace(query.CustomerCd))
            q = q.Where(x => string.Compare(x.HaibaiNo2 ?? "", query.CustomerCd) >= 0);
        if (!string.IsNullOrWhiteSpace(query.CustomerCdTo))
            q = q.Where(x => string.Compare(x.HaibaiNo2 ?? "", query.CustomerCdTo) <= 0);
        if (!string.IsNullOrWhiteSpace(query.HaibaiNo1From))
            q = q.Where(x => string.Compare(x.HaibaiNo1 ?? "", query.HaibaiNo1From) >= 0);
        if (!string.IsNullOrWhiteSpace(query.HaibaiNo1To))
            q = q.Where(x => string.Compare(x.HaibaiNo1 ?? "", query.HaibaiNo1To) <= 0);
        if (!string.IsNullOrWhiteSpace(query.HaibaiNo2From))
            q = q.Where(x => string.Compare(x.HaibaiNo2 ?? "", query.HaibaiNo2From) >= 0);
        if (!string.IsNullOrWhiteSpace(query.HaibaiNo2To))
            q = q.Where(x => string.Compare(x.HaibaiNo2 ?? "", query.HaibaiNo2To) <= 0);
        if (!string.IsNullOrWhiteSpace(query.HaibaiNo3From))
            q = q.Where(x => string.Compare(x.HaibaiNo3 ?? "", query.HaibaiNo3From) >= 0);
        if (!string.IsNullOrWhiteSpace(query.HaibaiNo3To))
            q = q.Where(x => string.Compare(x.HaibaiNo3 ?? "", query.HaibaiNo3To) <= 0);
        if (!string.IsNullOrWhiteSpace(query.DefectiveHaibaiNo))
            q = q.Where(x => x.DefectiveHaibaiNo == query.DefectiveHaibaiNo);
        if (!string.IsNullOrWhiteSpace(query.ProductCd)) q = q.Where(x => x.ProductCd == query.ProductCd);
        if (!string.IsNullOrWhiteSpace(query.ItemCd)) q = q.Where(x => x.ItemCd == query.ItemCd);
        if (!string.IsNullOrWhiteSpace(query.CustomerItemName))
        {
            var p = query.CustomerItemName;
            q = q.Where(x => (x.CustomerItemName1 ?? "").Contains(p) || (x.CustomerItemName2 ?? "").Contains(p));
        }
        if (!string.IsNullOrWhiteSpace(query.CustomerPartNo)) q = q.Where(x => x.CustomerPartNo == query.CustomerPartNo);
        if (query.DeliveryDateFrom.HasValue) q = q.Where(x => x.CustomerDeliveryDate >= query.DeliveryDateFrom.Value);
        if (query.DeliveryDateTo.HasValue) q = q.Where(x => x.CustomerDeliveryDate <= query.DeliveryDateTo.Value);
        if (!string.IsNullOrWhiteSpace(query.ProductCatBig)) q = q.Where(x => x.ProductCatBig == query.ProductCatBig);
        if (!string.IsNullOrWhiteSpace(query.ProductCatMid)) q = q.Where(x => x.ProductCatMid == query.ProductCatMid);
        if (!string.IsNullOrWhiteSpace(query.ProductCatSml)) q = q.Where(x => x.ProductCatSml == query.ProductCatSml);
        if (!string.IsNullOrWhiteSpace(query.SheetFlute)) q = q.Where(x => x.SheetFlute == query.SheetFlute);
        if (!string.IsNullOrWhiteSpace(query.PaperCd))
        {
            var p = query.PaperCd;
            q = q.Where(x => x.PaperCdF == p || x.PaperCdC == p || x.PaperCdB == p);
        }
        if (!string.IsNullOrWhiteSpace(query.PrintCd))
        {
            var p = query.PrintCd;
            q = q.Where(x => x.PrintCdF == p || x.PrintCdC == p || x.PrintCdB == p);
        }
        if (!string.IsNullOrWhiteSpace(query.EmbossCd))
        {
            var p = query.EmbossCd;
            q = q.Where(x => x.EmbossCdF == p || x.EmbossCdC == p || x.EmbossCdB == p);
        }
        if (!string.IsNullOrWhiteSpace(query.MakerCd))
        {
            var p = query.MakerCd;
            q = q.Where(x => x.MakerCdF == p || x.MakerCdC == p || x.MakerCdB == p);
        }
        if (!string.IsNullOrWhiteSpace(query.FscOrderType)) q = q.Where(x => x.FscOrderType == query.FscOrderType);
        if (!string.IsNullOrWhiteSpace(query.ProductShape)) q = q.Where(x => x.ProductShape == query.ProductShape);
        if (!string.IsNullOrWhiteSpace(query.DistributionDiv)) q = q.Where(x => x.DistributionDiv == query.DistributionDiv);
        if (query.OnlyConsignedSales == true) q = q.Where(x => x.ConsignedSalesFlg == "1");
        if (query.OnlyMcUntransferred == true) q = q.Where(x => !x.McTransferFlg);

        return q;
    }

    private async Task<List<OrderListItemDto>> ProjectListAsync(IQueryable<OrderDetail> q)
    {
        return await q.Select(x => new OrderListItemDto
        {
            CustomerCd = x.HaibaiNo2,
            OrderSheetNo = null,
            HaibaiNo1 = x.HaibaiNo1,
            DefectiveHaibaiNo = x.DefectiveHaibaiNo,
            McOrderNo = x.McOrderNo,
            CustomerDeliveryDate = x.CustomerDeliveryDate,
            ProductCd = x.ProductCd,
            ItemCd = x.ItemCd,
            ProductCatBig = x.ProductCatBig,
            CpItemOrComposition = x.CpItemName1,
            SheetFlute = x.SheetFlute,
            CompositionF = (x.PaperCdF ?? "") + (string.IsNullOrEmpty(x.PrintCdF) ? "" : "+" + x.PrintCdF) + (string.IsNullOrEmpty(x.EmbossCdF) ? "" : "+" + x.EmbossCdF),
            CompositionC = (x.PaperCdC ?? "") + (string.IsNullOrEmpty(x.PrintCdC) ? "" : "+" + x.PrintCdC) + (string.IsNullOrEmpty(x.EmbossCdC) ? "" : "+" + x.EmbossCdC),
            CompositionB = (x.PaperCdB ?? "") + (string.IsNullOrEmpty(x.PrintCdB) ? "" : "+" + x.PrintCdB) + (string.IsNullOrEmpty(x.EmbossCdB) ? "" : "+" + x.EmbossCdB),
            FullSheetWidth = x.BladeWidth,
            FullSheetFlow = x.BladeFlow,
            SlitterWidth = x.SheetDimW,
            SlitterFlow = x.SheetDimF,
            QtyUnit = x.QtyUnit,
            Quantity = x.Quantity,
            IndividualUnitPrice = x.IndividualUnitPrice,
            SetUnitPrice = x.SetUnitPrice,
            UnitPriceUnit = x.UnitPriceUnit,
            Amount = x.Amount,
            ConsignedSalesFlg = x.ConsignedSalesFlg,
            SlipNote = x.SlipNote,
            DeliveryNote = x.DeliveryNote,
            WebOrderNo = x.WebOrderNo,
            WebOrderDetailNo = x.WebOrderDetailNo,
        }).ToListAsync();
    }

    private static void NumberRows(List<OrderListItemDto> rows)
    {
        for (int i = 0; i < rows.Count; i++) rows[i].RowNo = i + 1;
    }

    private static string CsvE(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return s;
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }

    // ═══════════════════════════════════════════════════════════
    //  Helper：DTO ↔ Entity マッピング
    // ═══════════════════════════════════════════════════════════

    private static OrderDto HeaderToDto(Order h) => new()
    {
        WebOrderNo = h.WebOrderNo,
        CustomerCd = h.CustomerCd,
        OrderType = h.OrderType,
        OrderDepartment = h.OrderDepartment,
        OrderDate = h.OrderDate,
        CustomerDeliveryDate = h.CustomerDeliveryDate,
        Quantity = h.Quantity,
        OrderSheetNo = h.OrderSheetNo,
        CustomerContact = h.CustomerContact,
        Addressee = h.Addressee,
        Carrier = h.Carrier,
        ShipDateTime = h.ShipDateTime,
        ShipCondition = h.ShipCondition,
        SalesPriceDiv = h.SalesPriceDiv,
        McOrderNo = h.McOrderNo,
        Status = h.Status,
        McTransferFlg = h.McTransferFlg,
        Memo1 = h.Memo1,
        Memo2 = h.Memo2,
        Memo3 = h.Memo3,
        RowVersion = h.RowVersion,
        ShipStatus = h.ShipStatus,
        ActualShipDate = h.ActualShipDate,
    };

    private static OrderDetailDto DetailToDto(OrderDetail d) => new()
    {
        WebOrderNo = d.WebOrderNo,
        WebOrderDetailNo = d.WebOrderDetailNo,
        HaibaiNo1 = d.HaibaiNo1, HaibaiNo2 = d.HaibaiNo2, HaibaiNo3 = d.HaibaiNo3, HaibaiNo4 = d.HaibaiNo4,
        ProductCd = d.ProductCd, ItemCd = d.ItemCd, Branch1 = d.Branch1, Branch2 = d.Branch2, Branch3 = d.Branch3,
        ProductCatBig = d.ProductCatBig, ProductCatMid = d.ProductCatMid, ProductCatSml = d.ProductCatSml,
        CustomerItemName1 = d.CustomerItemName1, CustomerItemName2 = d.CustomerItemName2,
        CustomerPartNo = d.CustomerPartNo, CpItemName1 = d.CpItemName1, CpItemName2 = d.CpItemName2, JanCode = d.JanCode,
        QtyUnit = d.QtyUnit, Quantity = d.Quantity, SpecialPriceFlg = d.SpecialPriceFlg,
        UnitPriceUnit = d.UnitPriceUnit, SetUnitPrice = d.SetUnitPrice,
        IndividualUnitPrice = d.IndividualUnitPrice, Amount = d.Amount,
        DeliveryCd = d.DeliveryCd, DeliveryName = d.DeliveryName,
        CustomerDeliveryDate = d.CustomerDeliveryDate,
        LogisticsGroup = d.LogisticsGroup, HaibaiKbn = d.HaibaiKbn,
        ConsignedSalesFlg = d.ConsignedSalesFlg, SalesReason = d.SalesReason,
        ConsignedSalesQty = d.ConsignedSalesQty,
        FscOrderType = d.FscOrderType, FscProductDiv = d.FscProductDiv,
        FscMaterialDiv = d.FscMaterialDiv, FscManagementNo = d.FscManagementNo,
        FoodSafety = d.FoodSafety,
        ShipInspection = d.ShipInspection, FixedShipment = d.FixedShipment,
        DeliveryReserve = d.DeliveryReserve, SalesSample = d.SalesSample,
        SalesAvailable = d.SalesAvailable,
        SheetFlute = d.SheetFlute,
        PaperCdF = d.PaperCdF, PaperCdC = d.PaperCdC, PaperCdB = d.PaperCdB,
        PrintCdF = d.PrintCdF, PrintCdC = d.PrintCdC, PrintCdB = d.PrintCdB,
        EmbossCdF = d.EmbossCdF, EmbossCdC = d.EmbossCdC, EmbossCdB = d.EmbossCdB,
        MakerCdF = d.MakerCdF, MakerCdC = d.MakerCdC, MakerCdB = d.MakerCdB,
        SheetPrint = d.SheetPrint,
        BladeWidth = d.BladeWidth, BladeFlow = d.BladeFlow,
        GutterFb = d.GutterFb, GutterLr = d.GutterLr,
        SheetDimW = d.SheetDimW, SheetDimF = d.SheetDimF,
        SalesWidth = d.SalesWidth, FinalMachineProcess = d.FinalMachineProcess,
        PrintNote = d.PrintNote, MfgNote = d.MfgNote, RemfgNote = d.RemfgNote,
        SlipNote = d.SlipNote, DeliveryNote = d.DeliveryNote,
        ShipNote1 = d.ShipNote1, ShipNote2 = d.ShipNote2,
        DefectiveHaibaiNo = d.DefectiveHaibaiNo, PurchaseVendor = d.PurchaseVendor,
        RollMeter = d.RollMeter, PurchaseUnitPrice = d.PurchaseUnitPrice,
        PurchaseUnit = d.PurchaseUnit,
        ProjectNoParent = d.ProjectNoParent, ProjectNoChild = d.ProjectNoChild,
        ProjectNoMaterial = d.ProjectNoMaterial,
        QuotationNo = d.QuotationNo, EstimateCalcNo = d.EstimateCalcNo,
        RefEstimateCalcNo = d.RefEstimateCalcNo,
        SetProductCd = d.SetProductCd, SetProductName = d.SetProductName,
        ParentChildDiv = d.ParentChildDiv, SetRatio = d.SetRatio,
        OrderType = d.OrderType,
        ProductUsage = d.ProductUsage, DistributionDiv = d.DistributionDiv,
        ConfidentialInfo = d.ConfidentialInfo, SeizureDiv = d.SeizureDiv,
        ImportanceDiv = d.ImportanceDiv, MChange = d.MChange, QualityDiv = d.QualityDiv,
        ProductShape = d.ProductShape, UnescoMark = d.UnescoMark,
        OrigamiMark = d.OrigamiMark, FourMContract = d.FourMContract,
        TkpWrinkleStd = d.TkpWrinkleStd, RecyclingPayment = d.RecyclingPayment,
        PaperUsageG = d.PaperUsageG, PlasticUsageG = d.PlasticUsageG,
        GlassUsageG = d.GlassUsageG, PetUsageG = d.PetUsageG,
        PackPaperUsageG = d.PackPaperUsageG, PackPlasticUsageG = d.PackPlasticUsageG,
        DesignProposalNo = d.DesignProposalNo, SalesPriceDiv = d.SalesPriceDiv,
        FreightBilling = d.FreightBilling,
        McOrderNo = d.McOrderNo, McOrderDetailNo = d.McOrderDetailNo,
        Status = d.Status, WfApprovalFlg = d.WfApprovalFlg, McTransferFlg = d.McTransferFlg,
        ProvisionalPriceFlg = d.ProvisionalPriceFlg,
        PriceChangeReason = d.PriceChangeReason,
        ApprovalStatus = d.ApprovalStatus,
        ShippedQty = d.ShippedQty,
        ShipStatus = d.ShipStatus,
        LastShipDate = d.LastShipDate,
        LastOutboundNo = d.LastOutboundNo,
    };

    private static OrderDetail DtoToDetail(OrderDetailDto d) => new()
    {
        WebOrderNo = d.WebOrderNo ?? "",
        WebOrderDetailNo = d.WebOrderDetailNo,
        HaibaiNo1 = d.HaibaiNo1, HaibaiNo2 = d.HaibaiNo2, HaibaiNo3 = d.HaibaiNo3, HaibaiNo4 = d.HaibaiNo4,
        ProductCd = d.ProductCd,
        ItemCd = d.ItemCd, Branch1 = d.Branch1, Branch2 = d.Branch2, Branch3 = d.Branch3,
        ProductCatBig = d.ProductCatBig, ProductCatMid = d.ProductCatMid, ProductCatSml = d.ProductCatSml,
        CustomerItemName1 = d.CustomerItemName1, CustomerItemName2 = d.CustomerItemName2,
        CustomerPartNo = d.CustomerPartNo, CpItemName1 = d.CpItemName1, CpItemName2 = d.CpItemName2, JanCode = d.JanCode,
        QtyUnit = d.QtyUnit, Quantity = d.Quantity, SpecialPriceFlg = d.SpecialPriceFlg,
        UnitPriceUnit = d.UnitPriceUnit, SetUnitPrice = d.SetUnitPrice,
        IndividualUnitPrice = d.IndividualUnitPrice, Amount = d.Amount,
        DeliveryCd = d.DeliveryCd, DeliveryName = d.DeliveryName,
        CustomerDeliveryDate = d.CustomerDeliveryDate,
        LogisticsGroup = d.LogisticsGroup, HaibaiKbn = d.HaibaiKbn,
        ConsignedSalesFlg = d.ConsignedSalesFlg, SalesReason = d.SalesReason,
        ConsignedSalesQty = d.ConsignedSalesQty,
        FscOrderType = d.FscOrderType, FscProductDiv = d.FscProductDiv,
        FscMaterialDiv = d.FscMaterialDiv, FscManagementNo = d.FscManagementNo,
        FoodSafety = d.FoodSafety,
        ShipInspection = d.ShipInspection, FixedShipment = d.FixedShipment,
        DeliveryReserve = d.DeliveryReserve, SalesSample = d.SalesSample,
        SalesAvailable = d.SalesAvailable,
        SheetFlute = d.SheetFlute,
        PaperCdF = d.PaperCdF, PaperCdC = d.PaperCdC, PaperCdB = d.PaperCdB,
        PrintCdF = d.PrintCdF, PrintCdC = d.PrintCdC, PrintCdB = d.PrintCdB,
        EmbossCdF = d.EmbossCdF, EmbossCdC = d.EmbossCdC, EmbossCdB = d.EmbossCdB,
        MakerCdF = d.MakerCdF, MakerCdC = d.MakerCdC, MakerCdB = d.MakerCdB,
        SheetPrint = d.SheetPrint,
        BladeWidth = d.BladeWidth, BladeFlow = d.BladeFlow,
        GutterFb = d.GutterFb, GutterLr = d.GutterLr,
        SheetDimW = d.SheetDimW, SheetDimF = d.SheetDimF,
        SalesWidth = d.SalesWidth, FinalMachineProcess = d.FinalMachineProcess,
        PrintNote = d.PrintNote, MfgNote = d.MfgNote, RemfgNote = d.RemfgNote,
        SlipNote = d.SlipNote, DeliveryNote = d.DeliveryNote,
        ShipNote1 = d.ShipNote1, ShipNote2 = d.ShipNote2,
        DefectiveHaibaiNo = d.DefectiveHaibaiNo, PurchaseVendor = d.PurchaseVendor,
        RollMeter = d.RollMeter, PurchaseUnitPrice = d.PurchaseUnitPrice,
        PurchaseUnit = d.PurchaseUnit,
        ProjectNoParent = d.ProjectNoParent, ProjectNoChild = d.ProjectNoChild,
        ProjectNoMaterial = d.ProjectNoMaterial,
        QuotationNo = d.QuotationNo, EstimateCalcNo = d.EstimateCalcNo,
        RefEstimateCalcNo = d.RefEstimateCalcNo,
        SetProductCd = d.SetProductCd, SetProductName = d.SetProductName,
        ParentChildDiv = d.ParentChildDiv, SetRatio = d.SetRatio,
        OrderType = d.OrderType,
        ProductUsage = d.ProductUsage, DistributionDiv = d.DistributionDiv,
        ConfidentialInfo = d.ConfidentialInfo, SeizureDiv = d.SeizureDiv,
        ImportanceDiv = d.ImportanceDiv, MChange = d.MChange, QualityDiv = d.QualityDiv,
        ProductShape = d.ProductShape, UnescoMark = d.UnescoMark,
        OrigamiMark = d.OrigamiMark, FourMContract = d.FourMContract,
        TkpWrinkleStd = d.TkpWrinkleStd, RecyclingPayment = d.RecyclingPayment,
        PaperUsageG = d.PaperUsageG, PlasticUsageG = d.PlasticUsageG,
        GlassUsageG = d.GlassUsageG, PetUsageG = d.PetUsageG,
        PackPaperUsageG = d.PackPaperUsageG, PackPlasticUsageG = d.PackPlasticUsageG,
        DesignProposalNo = d.DesignProposalNo, SalesPriceDiv = d.SalesPriceDiv,
        FreightBilling = d.FreightBilling,
        McOrderNo = d.McOrderNo, McOrderDetailNo = d.McOrderDetailNo,
        Status = d.Status, WfApprovalFlg = d.WfApprovalFlg, McTransferFlg = d.McTransferFlg,
        ProvisionalPriceFlg = d.ProvisionalPriceFlg,
        PriceChangeReason = d.PriceChangeReason,
        ApprovalStatus = d.ApprovalStatus,
    };

    private static OrderProcessDto ProcessToDto(OrderProcess p) => new()
    {
        ProductCd = p.ProductCd, OperationCd = p.OperationCd, ProcessCd = p.ProcessCd,
        TopItemCd = p.TopItemCd, TopBranch1 = p.TopBranch1, TopBranch2 = p.TopBranch2, TopBranch3 = p.TopBranch3,
        ItemCd = p.ItemCd, Branch1 = p.Branch1, Branch2 = p.Branch2, Branch3 = p.Branch3,
        WorkingGroupCd = p.WorkingGroupCd, MachineOrVendor = p.MachineOrVendor,
        MachineFixedFlg = p.MachineFixedFlg, CpDeliveryDiv = p.CpDeliveryDiv,
        Spec01 = p.Spec01, Spec02 = p.Spec02, Spec03 = p.Spec03, Spec04 = p.Spec04, Spec05 = p.Spec05,
        Spec06 = p.Spec06, Spec07 = p.Spec07, Spec08 = p.Spec08, Spec09 = p.Spec09, Spec10 = p.Spec10,
        QtyUnit = p.QtyUnit,
        PlateNo1 = p.PlateNo1, PlateNo2 = p.PlateNo2, PlateNo3 = p.PlateNo3,
        Consumable1 = p.Consumable1, Consumable2 = p.Consumable2, Consumable3 = p.Consumable3,
        PurchaseUnitPrice = p.PurchaseUnitPrice, FixedPrice = p.FixedPrice,
        LossRate = p.LossRate, MachineCount = p.MachineCount, LeadTimeDays = p.LeadTimeDays,
        StorageLocation = p.StorageLocation, SortOrder = p.SortOrder,
        PriorityItem1 = p.PriorityItem1, PriorityItem2 = p.PriorityItem2,
        PriorityItem3 = p.PriorityItem3, PriorityItem4 = p.PriorityItem4,
        PriorityItem5 = p.PriorityItem5, PriorityItem6 = p.PriorityItem6,
        PriorityItem7 = p.PriorityItem7, PriorityItem8 = p.PriorityItem8,
        ScheduledDate = p.ScheduledDate,
    };

    private static OrderProcess DtoToProcess(OrderProcessDto p, string webOrderNo, int detailNo, string productCd) => new()
    {
        WebOrderNo = webOrderNo,
        WebOrderDetailNo = detailNo,
        ProductCd = productCd,
        OperationCd = p.OperationCd,
        ProcessCd = p.ProcessCd,
        TopItemCd = p.TopItemCd, TopBranch1 = p.TopBranch1, TopBranch2 = p.TopBranch2, TopBranch3 = p.TopBranch3,
        ItemCd = p.ItemCd, Branch1 = p.Branch1, Branch2 = p.Branch2, Branch3 = p.Branch3,
        WorkingGroupCd = p.WorkingGroupCd, MachineOrVendor = p.MachineOrVendor,
        MachineFixedFlg = p.MachineFixedFlg, CpDeliveryDiv = p.CpDeliveryDiv,
        Spec01 = p.Spec01, Spec02 = p.Spec02, Spec03 = p.Spec03, Spec04 = p.Spec04, Spec05 = p.Spec05,
        Spec06 = p.Spec06, Spec07 = p.Spec07, Spec08 = p.Spec08, Spec09 = p.Spec09, Spec10 = p.Spec10,
        QtyUnit = p.QtyUnit,
        PlateNo1 = p.PlateNo1, PlateNo2 = p.PlateNo2, PlateNo3 = p.PlateNo3,
        Consumable1 = p.Consumable1, Consumable2 = p.Consumable2, Consumable3 = p.Consumable3,
        PurchaseUnitPrice = p.PurchaseUnitPrice, FixedPrice = p.FixedPrice,
        LossRate = p.LossRate, MachineCount = p.MachineCount, LeadTimeDays = p.LeadTimeDays,
        StorageLocation = p.StorageLocation, SortOrder = p.SortOrder,
        PriorityItem1 = p.PriorityItem1, PriorityItem2 = p.PriorityItem2,
        PriorityItem3 = p.PriorityItem3, PriorityItem4 = p.PriorityItem4,
        PriorityItem5 = p.PriorityItem5, PriorityItem6 = p.PriorityItem6,
        PriorityItem7 = p.PriorityItem7, PriorityItem8 = p.PriorityItem8,
        ScheduledDate = p.ScheduledDate,
    };

    private static OrderProcessNoteDto ProcessNoteToDto(OrderProcessNote n) => new()
    {
        ProductCd = n.ProductCd, OperationCd = n.OperationCd,
        Note1 = n.Note1, Note2 = n.Note2,
    };

    private static OrderProcessNote DtoToProcessNote(OrderProcessNoteDto n, string webOrderNo, int detailNo, string productCd) => new()
    {
        WebOrderNo = webOrderNo, WebOrderDetailNo = detailNo,
        ProductCd = productCd, OperationCd = n.OperationCd,
        Note1 = n.Note1, Note2 = n.Note2,
    };

    private static OrderMaterialDto MaterialToDto(OrderMaterial m) => new()
    {
        ProductCd = m.ProductCd, ProcessCd = m.ProcessCd, MaterialCd = m.MaterialCd,
        MaterialTypeDiv = m.MaterialTypeDiv,
        ItemCd = m.ItemCd, Branch1 = m.Branch1, Branch2 = m.Branch2, Branch3 = m.Branch3,
        SupplyDiv = m.SupplyDiv, SupplyUnitPrice = m.SupplyUnitPrice, SortOrder = m.SortOrder,
    };

    private static OrderMaterial DtoToMaterial(OrderMaterialDto m, string webOrderNo, int detailNo, string productCd) => new()
    {
        WebOrderNo = webOrderNo, WebOrderDetailNo = detailNo,
        ProductCd = productCd,
        ProcessCd = m.ProcessCd,
        MaterialCd = m.MaterialCd,
        MaterialTypeDiv = string.IsNullOrEmpty(m.MaterialTypeDiv) ? "3" : m.MaterialTypeDiv,
        ItemCd = m.ItemCd ?? McNullVal,
        Branch1 = m.Branch1 ?? McNullVal,
        Branch2 = m.Branch2 ?? McNullVal,
        Branch3 = m.Branch3 ?? McNullVal,
        SupplyDiv = string.IsNullOrEmpty(m.SupplyDiv) ? "1" : m.SupplyDiv,
        SupplyUnitPrice = m.SupplyUnitPrice,
        SortOrder = m.SortOrder,
    };
}
