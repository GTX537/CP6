using System.Text;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels;
using CP6.Entity.DTOs;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services;

/// <summary>
/// MSBBPA140/150 — Web 版型/木型マスタ業務サービス
/// </summary>
/// <remarks>
/// 5 操作モード（登録/改定/訂正/削除/参照）+ Rev 管理 + 版型受注連携 + PE API 連携。
/// </remarks>
public class PlateMoldService : IPlateMoldService
{
    private readonly CP6Context _db;
    private readonly IPlateMoldPeApiService _peApi;
    private const string WdPtnPrefix = "PM"; // 版型 NO 採番プレフィックス

    public PlateMoldService(CP6Context db, IPlateMoldPeApiService peApi)
    {
        _db = db;
        _peApi = peApi;
    }

    // ═══════════════════════════════════════════════════════════
    //  検索
    // ═══════════════════════════════════════════════════════════

    public async Task<PlateMoldDto?> GetByNoAsync(string wdPtnNo, int? rev = null)
    {
        var q = _db.PlateMolds.AsNoTracking().Where(x => x.WdPtnNo == wdPtnNo && !x.IsDeleted);
        if (rev.HasValue) q = q.Where(x => x.WdRev == rev.Value);
        var ent = await q.OrderByDescending(x => x.WdRev).FirstOrDefaultAsync();
        return ent == null ? null : EntityToDto(ent);
    }

    public async Task<PlateMoldDto?> GetByEstimateNoAsync(string decisionEstimateNo)
    {
        // 仕様書 §3 NO.1 — 決定見積 NO から見積計算書を検索し基本情報・構成情報を引入
        var calc = await _db.EstimateCalcs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.QtnCalcNo == decisionEstimateNo && !c.IsDeleted);
        if (calc == null) return null;

        var customerName = await _db.BusinessPartners.AsNoTracking()
            .Where(b => b.BpCd == calc.CustomerCd && !b.IsDeleted && b.CustomerFlg)
            .Select(b => b.BpName).FirstOrDefaultAsync();

        return new PlateMoldDto
        {
            DecisionEstimateNo = calc.QtnCalcNo,
            VrsnName = calc.CustomerProductName1 ?? "",
            CustomerCd = calc.CustomerCd ?? "",
            CustomerName = customerName,
            // 構成情報スナップショット — 代表製品CD 選択時に GetCompositionByProductAsync で製品マスタ参照から引入（Rev3.0 変更）
            StDate = DateTime.Today,
            EndDate = DateTime.Today.AddYears(10),
        };
    }

    public async Task<PlateMoldDto?> GetCompositionByProductAsync(string productCd)
    {
        if (string.IsNullOrWhiteSpace(productCd)) return null;

        // 仕様書 §3 NO.1 第2ブロック — 代表製品CD 入力選択時、製品マスタ参照から構成情報を引入（Rev3.0）
        var p = await _db.ProductMasters.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ProductCd == productCd && !x.IsDeleted);
        if (p == null) return null;

        var dto = new PlateMoldDto { RepresentativeProductCd = p.ProductCd };
        ApplyCompositionFromProduct(dto, p);
        return dto;
    }

    /// <summary>製品マスタの段ボール・原紙構成（段/原紙/印刷/エンボス/メーカ F・C・B）を DTO へ反映。
    /// 製品マスタは中層メーカ CD（MakerCdC）を持たないため null とする。</summary>
    private static void ApplyCompositionFromProduct(PlateMoldDto dto, ProductMaster p)
    {
        dto.SheetFlute = p.SheetFlute;
        dto.PaperCdF = p.PaperCdF; dto.PrintCdF = p.PrintCdF; dto.EmbossCdF = p.EmbossCdF; dto.MakerCdF = p.MakerCdF;
        dto.PaperCdC = p.PaperCdC; dto.PrintCdC = p.PrintCdC; dto.EmbossCdC = p.EmbossCdC; dto.MakerCdC = null;
        dto.PaperCdB = p.PaperCdB; dto.PrintCdB = p.PrintCdB; dto.EmbossCdB = p.EmbossCdB; dto.MakerCdB = p.MakerCdB;
    }

    public async Task<List<PlateMoldHistoryItemDto>> GetHistoryAsync(string wdPtnNo)
    {
        var rows = await _db.PlateMolds.AsNoTracking()
            .Where(x => x.WdPtnNo == wdPtnNo && !x.IsDeleted)
            .OrderBy(x => x.WdRev)
            .ToListAsync();
        var supplierMap = await _db.BusinessPartners.AsNoTracking()
            .Where(b => !b.IsDeleted && b.SupplierFlg)
            .ToDictionaryAsync(b => b.BpCd, b => b.BpName);
        return rows.Select(r => new PlateMoldHistoryItemDto
        {
            WdPtnNo = r.WdPtnNo,
            WdRev = r.WdRev,
            StDate = r.StDate, EndDate = r.EndDate,
            SupplierCd = r.SupplierCd,
            SupplierName = supplierMap.TryGetValue(r.SupplierCd, out var n) ? n : null,
            WdQty = r.WdQty,
            DispScheduleDate = r.DispScheduleDate,
            ReturnScheduleDate = r.ReturnScheduleDate,
            ReturnDate = r.ReturnDate,
            ReturnReason = r.ReturnReason,
        }).ToList();
    }

    // ═══════════════════════════════════════════════════════════
    //  登録（Rev=1）+ 受注連携 + PE API
    // ═══════════════════════════════════════════════════════════

    public async Task<(string, int, PlateMoldOrderLinkResultDto, PlateMoldPeApiResultDto)>
        CreateAsync(PlateMoldDto dto, string? userName)
    {
        ValidateBeforeSave(dto);
        var wdPtnNo = await NextSequenceAsync();
        var now = DateTime.Now;

        // 工程→版型分類 自動セット（仕様書 §5）
        if (string.IsNullOrEmpty(dto.TypeClass) && !string.IsNullOrEmpty(dto.ProcessCd))
            dto.TypeClass = AutoTypeClassFromProcess(dto.ProcessCd);

        var ent = DtoToEntity(dto);
        ent.WdPtnNo = wdPtnNo;
        ent.WdRev = 1;
        ent.Status = 0;
        ent.McTransferFlg = false;
        ent.Creator = userName; ent.CreateDate = now;
        ent.Modifier = userName; ent.ModifyDate = now;
        _db.PlateMolds.Add(ent);
        await _db.SaveChangesAsync();

        // 版型受注連携（118 項）
        var orderLink = await CreatePlateMoldOrderAsync(ent, userName);
        ent.ArrangeNo = orderLink.ArrangeNo;
        ent.McOrderNo = null;
        ent.Modifier = userName; ent.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();

        // PE API 連携（53 項）
        var peDto = EntityToDto(ent);
        var peResult = await _peApi.SendAsync(peDto);

        return (wdPtnNo, 1, orderLink, peResult);
    }

    public async Task<(int, PlateMoldOrderLinkResultDto, PlateMoldPeApiResultDto)>
        ReviseAsync(string wdPtnNo, PlateMoldDto dto, string? userName)
    {
        ValidateBeforeSave(dto);

        // 最大 Rev を取得
        var maxRev = await _db.PlateMolds.AsNoTracking()
            .Where(x => x.WdPtnNo == wdPtnNo && !x.IsDeleted)
            .OrderByDescending(x => x.WdRev)
            .Select(x => x.WdRev)
            .FirstOrDefaultAsync();
        if (maxRev == 0)
            throw new KeyNotFoundException($"E10008: 版型 NO '{wdPtnNo}' が見つかりません");

        // 未来日制限：未来日の Rev が既に存在する場合エラー
        var hasFutureRev = await _db.PlateMolds.AsNoTracking()
            .AnyAsync(x => x.WdPtnNo == wdPtnNo && !x.IsDeleted && x.StDate > DateTime.Today);
        if (hasFutureRev)
            throw new InvalidOperationException("未来日の Rev が既に存在します。改定は 1 つのみ可能です。");

        var now = DateTime.Now;
        var newRev = maxRev + 1;

        // 前 Rev の適用終了日を新適用開始日 -1 に更新
        var prevRev = await _db.PlateMolds.FirstOrDefaultAsync(x => x.WdPtnNo == wdPtnNo && x.WdRev == maxRev);
        if (prevRev != null)
        {
            prevRev.EndDate = dto.StDate.AddDays(-1);
            prevRev.Modifier = userName;
            prevRev.ModifyDate = now;
        }

        // 新 Rev INSERT
        if (string.IsNullOrEmpty(dto.TypeClass) && !string.IsNullOrEmpty(dto.ProcessCd))
            dto.TypeClass = AutoTypeClassFromProcess(dto.ProcessCd);
        var ent = DtoToEntity(dto);
        ent.WdPtnNo = wdPtnNo;
        ent.WdRev = newRev;
        ent.Status = 0;
        ent.McTransferFlg = false;
        ent.Creator = userName; ent.CreateDate = now;
        ent.Modifier = userName; ent.ModifyDate = now;
        _db.PlateMolds.Add(ent);
        await _db.SaveChangesAsync();

        var orderLink = await CreatePlateMoldOrderAsync(ent, userName);
        ent.ArrangeNo = orderLink.ArrangeNo;
        await _db.SaveChangesAsync();

        var peResult = await _peApi.SendAsync(EntityToDto(ent));
        return (newRev, orderLink, peResult);
    }

    public async Task UpdateAsync(string wdPtnNo, int wdRev, PlateMoldDto dto, string? userName)
    {
        var ent = await _db.PlateMolds.FirstOrDefaultAsync(x =>
            x.WdPtnNo == wdPtnNo && x.WdRev == wdRev && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"E10008: 版型 '{wdPtnNo}' Rev {wdRev} が見つかりません");

        // 最新 Rev のみ訂正可
        var maxRev = await _db.PlateMolds.AsNoTracking()
            .Where(x => x.WdPtnNo == wdPtnNo && !x.IsDeleted)
            .MaxAsync(x => (int?)x.WdRev) ?? 0;
        if (wdRev != maxRev)
            throw new InvalidOperationException("最新 Rev のみ訂正可能です");

        if (dto.RowVersion != null && dto.RowVersion.Length > 0)
            _db.Entry(ent).Property(x => x.RowVersion).OriginalValue = dto.RowVersion;

        ValidateBeforeSave(dto);
        ApplyDtoToEntity(dto, ent);
        ent.Modifier = userName;
        ent.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();

        // 仕様書 §12 処理概要(訂正) — 連携済の版型受注を更新し、PE API を再送信
        await RelinkOrderOnUpdateAsync(ent, userName);
        await _peApi.SendAsync(EntityToDto(ent));
    }

    public async Task DeleteAsync(string wdPtnNo, int wdRev, byte[]? rowVersion, string? userName)
    {
        var ent = await _db.PlateMolds.FirstOrDefaultAsync(x =>
            x.WdPtnNo == wdPtnNo && x.WdRev == wdRev && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"E10008: 版型 '{wdPtnNo}' Rev {wdRev} が見つかりません");
        if (rowVersion != null && rowVersion.Length > 0)
            _db.Entry(ent).Property(x => x.RowVersion).OriginalValue = rowVersion;
        ent.IsDeleted = true;
        ent.Modifier = userName;
        ent.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();

        // 仕様書 §12 処理概要(削除) — 連携済の版型受注を論理削除し、PE API へ削除連携
        await LogicalDeleteLinkedOrderAsync(ent.ArrangeNo, userName);
        await _peApi.SendDeleteAsync(EntityToDto(ent));
    }

    // ═══════════════════════════════════════════════════════════
    //  版型受注 連携（訂正/削除時のカスケード）
    // ═══════════════════════════════════════════════════════════

    /// <summary>訂正時 — 連携済 受注明細を最新内容で更新。連携が見つからない場合は新規連携を生成。</summary>
    private async Task RelinkOrderOnUpdateAsync(PlateMold ent, string? userName)
    {
        if (string.IsNullOrEmpty(ent.ArrangeNo))
        {
            // 連携実績なし → 新規に版型受注を生成
            var link = await CreatePlateMoldOrderAsync(ent, userName);
            ent.ArrangeNo = link.ArrangeNo;
            await _db.SaveChangesAsync();
            return;
        }

        var now = DateTime.Now;
        var detail = await _db.OrderDetails
            .FirstOrDefaultAsync(d => d.HaibaiNo1 == ent.ArrangeNo && !d.IsDeleted);
        if (detail == null)
        {
            // 連携先が消えている → 再生成
            var link = await CreatePlateMoldOrderAsync(ent, userName);
            ent.ArrangeNo = link.ArrangeNo;
            await _db.SaveChangesAsync();
            return;
        }

        // 受注明細 — 訂正された版型情報を反映
        detail.HaibaiNo2 = ent.CustomerCd;
        detail.HaibaiNo3 = ent.DecisionEstimateNo;
        detail.CustomerItemName1 = ent.VrsnName;
        detail.CpItemName1 = ent.VrsnName;
        detail.IndividualUnitPrice = ent.DecisionAmount;
        detail.Amount = ent.DecisionAmount;
        detail.DeliveryCd = ent.CustomerCd;
        detail.CustomerDeliveryDate = ent.DeliveryDate;
        detail.SalesAvailable = ent.EarningsCd;
        detail.Modifier = userName;
        detail.ModifyDate = now;

        // 受注ヘッダ — 得意先/納期を同期
        var header = await _db.Orders
            .FirstOrDefaultAsync(o => o.WebOrderNo == detail.WebOrderNo && !o.IsDeleted);
        if (header != null)
        {
            header.CustomerCd = ent.CustomerCd;
            header.CustomerDeliveryDate = ent.DeliveryDate;
            header.Modifier = userName;
            header.ModifyDate = now;
        }
        await _db.SaveChangesAsync();
    }

    /// <summary>削除時 — 連携済 受注ヘッダ・明細を論理削除。</summary>
    private async Task LogicalDeleteLinkedOrderAsync(string? arrangeNo, string? userName)
    {
        if (string.IsNullOrEmpty(arrangeNo)) return;
        var now = DateTime.Now;

        var detail = await _db.OrderDetails
            .FirstOrDefaultAsync(d => d.HaibaiNo1 == arrangeNo && !d.IsDeleted);
        if (detail == null) return;

        detail.IsDeleted = true;
        detail.Modifier = userName;
        detail.ModifyDate = now;

        // 同一受注の明細が他に無ければヘッダも論理削除
        var hasOtherDetail = await _db.OrderDetails
            .AnyAsync(d => d.WebOrderNo == detail.WebOrderNo && d.WebOrderDetailNo != detail.WebOrderDetailNo && !d.IsDeleted);
        if (!hasOtherDetail)
        {
            var header = await _db.Orders
                .FirstOrDefaultAsync(o => o.WebOrderNo == detail.WebOrderNo && !o.IsDeleted);
            if (header != null)
            {
                header.IsDeleted = true;
                header.Modifier = userName;
                header.ModifyDate = now;
            }
        }
        await _db.SaveChangesAsync();
    }

    public async Task<string> NextSequenceAsync()
    {
        // 木型・版型 NO = 機能コード(MLD)+年(4)+月(2)+自増(4)=13桁
        var (no, _) = await DocNumber.NextAsync(_db, "MLD");
        return no;
    }

    // ═══════════════════════════════════════════════════════════
    //  PA150 一覧
    // ═══════════════════════════════════════════════════════════

    /// <summary>抜型一覧 排序白名单（前端 el-table prop → PlateMold 属性名）</summary>
    private static readonly IReadOnlyDictionary<string, string> PlateMoldSortMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["wdPtnNo"] = "WdPtnNo",
            ["wdRev"] = "WdRev",
            ["vrsnName"] = "VrsnName",
            ["typeClass"] = "TypeClass",
            ["customerCd"] = "CustomerCd",
            ["representativeProductCd"] = "RepresentativeProductCd",
            ["wdQty"] = "WdQty",
            ["limitWdQty"] = "LimitWdQty",
            ["achieveDate"] = "AchieveDate",
            ["supplierCd"] = "SupplierCd",
            ["arrivalActualDate"] = "ArrivalActualDate",
            ["dispScheduleDate"] = "DispScheduleDate",
            ["returnScheduleDate"] = "ReturnScheduleDate",
            ["returnDate"] = "ReturnDate",
            ["processCd"] = "ProcessCd",
        };

    public async Task<(List<PlateMoldListItemDto>, int)> SearchAsync(PlateMoldQueryDto query)
    {
        var q = BuildSearchQuery(query);

        List<PlateMold> rows;
        int total;

        if (query.OnlyLatestRev != false)
        {
            // EF Core では GroupBy + First() は SQL 翻訳不可 → メモリ上で GROUP BY
            var all = await q.OrderBy(x => x.WdPtnNo).ThenByDescending(x => x.WdRev).ToListAsync();
            var latest = all.GroupBy(x => x.WdPtnNo)
                .Select(g => g.First())  // すでに WdRev DESC で並べ替え済み
                .ToList();
            total = latest.Count;
            if (query.MaxRows.HasValue && total > query.MaxRows.Value)
                throw new InvalidOperationException($"E10013: 検索件数の上限値を超えました（{total} > {query.MaxRows}）");
            rows = QuerySort.Apply(latest.AsQueryable(), query.SortField, query.SortOrder, PlateMoldSortMap,
                    s => s.OrderBy(x => x.WdPtnNo).ThenBy(x => x.WdRev))
                .Skip((query.Page - 1) * query.PageSize)
                .Take(query.PageSize)
                .ToList();
        }
        else
        {
            total = await q.CountAsync();
            if (query.MaxRows.HasValue && total > query.MaxRows.Value)
                throw new InvalidOperationException($"E10013: 検索件数の上限値を超えました（{total} > {query.MaxRows}）");
            rows = await QuerySort.Apply(q, query.SortField, query.SortOrder, PlateMoldSortMap,
                    s => s.OrderBy(x => x.WdPtnNo).ThenBy(x => x.WdRev))
                .Skip((query.Page - 1) * query.PageSize)
                .Take(query.PageSize)
                .ToListAsync();
        }

        // JOIN 用辞書
        var customers = await _db.BusinessPartners.AsNoTracking()
            .Where(b => !b.IsDeleted && b.CustomerFlg)
            .ToDictionaryAsync(b => b.BpCd, b => b.BpName);
        var suppliers = await _db.BusinessPartners.AsNoTracking()
            .Where(b => !b.IsDeleted && b.SupplierFlg)
            .ToDictionaryAsync(b => b.BpCd, b => b.BpName);
        var products = await _db.ProductMasters.AsNoTracking()
            .Where(p => !p.IsDeleted)
            .ToDictionaryAsync(p => p.ProductCd, p => p);

        var items = new List<PlateMoldListItemDto>();
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            var item = new PlateMoldListItemDto
            {
                RowNo = (query.Page - 1) * query.PageSize + i + 1,
                WdPtnNo = r.WdPtnNo, WdRev = r.WdRev,
                VrsnName = r.VrsnName,
                TypeClass = r.TypeClass,
                CustomerCd = r.CustomerCd,
                CustomerName = customers.TryGetValue(r.CustomerCd, out var cn) ? cn : null,
                RepresentativeProductCd = r.RepresentativeProductCd,
                WdQty = r.WdQty,
                LimitWdQty = r.LimitWdQty,
                AchieveDate = r.AchieveDate,
                SupplierCd = r.SupplierCd,
                SupplierName = suppliers.TryGetValue(r.SupplierCd, out var sn) ? sn : null,
                ArrivalActualDate = r.ArrivalActualDate,
                DispScheduleDate = r.DispScheduleDate,
                ReturnScheduleDate = r.ReturnScheduleDate,
                ReturnDate = r.ReturnDate,
                ReturnReason = r.ReturnReason,
                ProcessCd = r.ProcessCd,
                PlaceCd = r.PlaceCd,
                ShelfLineCd = r.ShelfLineCd,
                NewVerCd = r.NewVerCd,
                EndPlaceCd = r.EndPlaceCd,
                BladeWidth = r.BladeWidth,
                BladeFlow = r.BladeFlow,
                CompositionQty = r.CompositionQty,
                MfgQty = r.MfgQty,
                ColorQty = r.ColorQty,
                StrippingCd = r.StrippingCd,
                StandingCd = r.StandingCd,
                HammerCd = r.HammerCd,
                OversightCd = r.OversightCd,
                Memo = r.Memo,
            };
            if (products.TryGetValue(r.RepresentativeProductCd, out var p))
                item.RepresentativeProductName = (p.CustomerItemName1 ?? "") + (string.IsNullOrEmpty(p.CustomerItemName2) ? "" : " / " + p.CustomerItemName2);
            items.Add(item);
        }
        return (items, total);
    }

    public async Task<byte[]> ExportListCsvAsync(PlateMoldQueryDto query)
    {
        var (rows, _) = await SearchAsync(new PlateMoldQueryDto
        {
            BaseCd = query.BaseCd, StaffCd = query.StaffCd,
            StDateFrom = query.StDateFrom, StDateTo = query.StDateTo,
            ArrangeNo = query.ArrangeNo, ProjectNo = query.ProjectNo,
            WdPtnNoFrom = query.WdPtnNoFrom, WdPtnNoTo = query.WdPtnNoTo,
            VrsnName = query.VrsnName, TypeClass = query.TypeClass,
            NewVerCd = query.NewVerCd, CustomerCd = query.CustomerCd,
            RepresentativeProductCd = query.RepresentativeProductCd,
            ProcessCd = query.ProcessCd,
            WdQtyFrom = query.WdQtyFrom, WdQtyTo = query.WdQtyTo,
            SupplierCd = query.SupplierCd,
            LimitWdQtyFrom = query.LimitWdQtyFrom, LimitWdQtyTo = query.LimitWdQtyTo,
            PlaceCd = query.PlaceCd, ShelfLineCd = query.ShelfLineCd,
            AchieveDateFrom = query.AchieveDateFrom, AchieveDateTo = query.AchieveDateTo,
            ArrivalDateFrom = query.ArrivalDateFrom, ArrivalDateTo = query.ArrivalDateTo,
            DispScheduleFrom = query.DispScheduleFrom, DispScheduleTo = query.DispScheduleTo,
            ReturnScheduleFrom = query.ReturnScheduleFrom, ReturnScheduleTo = query.ReturnScheduleTo,
            ReturnDateFrom = query.ReturnDateFrom, ReturnDateTo = query.ReturnDateTo,
            OnlyLatestRev = query.OnlyLatestRev,
            Page = 1, PageSize = int.MaxValue,
        });

        var sb = new StringBuilder();
        sb.AppendLine("NO,版型NO,版型名,Rev,版型分類,得意先,得意先名,代表製品CD,代表製品名,通し数,限界通し数,最終使用実績日,発注先,発注先名,入荷日,廃棄予定日,返却予定日,返却日,返却理由,工程,場所,棚ライン,新版区分,刃渡(巾),刃渡(流れ),付数,個数,色数,落丁型,置き版,ハンマー,見落とし型,備考");
        foreach (var r in rows)
        {
            sb.AppendLine(string.Join(',', new[]
            {
                r.RowNo.ToString(),
                CsvE(r.WdPtnNo), CsvE(r.VrsnName), r.WdRev.ToString(), CsvE(r.TypeClass),
                CsvE(r.CustomerCd), CsvE(r.CustomerName),
                CsvE(r.RepresentativeProductCd), CsvE(r.RepresentativeProductName),
                r.WdQty.ToString(), r.LimitWdQty.ToString(),
                r.AchieveDate?.ToString("yyyy-MM-dd") ?? "",
                CsvE(r.SupplierCd), CsvE(r.SupplierName),
                r.ArrivalActualDate?.ToString("yyyy-MM-dd") ?? "",
                r.DispScheduleDate?.ToString("yyyy-MM-dd") ?? "",
                r.ReturnScheduleDate?.ToString("yyyy-MM-dd") ?? "",
                r.ReturnDate?.ToString("yyyy-MM-dd") ?? "",
                CsvE(r.ReturnReason),
                CsvE(r.ProcessCd), CsvE(r.PlaceCd), CsvE(r.ShelfLineCd),
                CsvE(r.NewVerCd),
                r.BladeWidth?.ToString("0.##") ?? "", r.BladeFlow?.ToString("0.##") ?? "",
                r.CompositionQty?.ToString() ?? "", r.MfgQty?.ToString() ?? "", r.ColorQty?.ToString() ?? "",
                CsvE(r.StrippingCd), CsvE(r.StandingCd), CsvE(r.HammerCd), CsvE(r.OversightCd),
                CsvE(r.Memo),
            }));
        }
        var preamble = Encoding.UTF8.GetPreamble();
        var body = Encoding.UTF8.GetBytes(sb.ToString());
        var output = new byte[preamble.Length + body.Length];
        Buffer.BlockCopy(preamble, 0, output, 0, preamble.Length);
        Buffer.BlockCopy(body, 0, output, preamble.Length, body.Length);
        return output;
    }

    public async Task<byte[]> IssueLabelCsvAsync(IEnumerable<(string WdPtnNo, int WdRev)> targets)
    {
        var keys = targets.ToList();
        if (keys.Count == 0) throw new InvalidOperationException("E10010: 発行対象がありません");

        var sb = new StringBuilder();
        sb.AppendLine("版型NO,Rev,版型名,版型分類,得意先,代表製品CD,刃渡(巾),刃渡(流れ),付数,個数,色数,場所,棚ライン");
        foreach (var (no, rev) in keys)
        {
            var ent = await _db.PlateMolds.AsNoTracking()
                .FirstOrDefaultAsync(x => x.WdPtnNo == no && x.WdRev == rev && !x.IsDeleted);
            if (ent == null) continue;
            sb.AppendLine(string.Join(',', new[]
            {
                CsvE(ent.WdPtnNo), ent.WdRev.ToString(), CsvE(ent.VrsnName), CsvE(ent.TypeClass),
                CsvE(ent.CustomerCd), CsvE(ent.RepresentativeProductCd),
                ent.BladeWidth.ToString("0.##"), ent.BladeFlow.ToString("0.##"),
                ent.CompositionQty.ToString(), ent.MfgQty.ToString(), ent.ColorQty.ToString(),
                CsvE(ent.PlaceCd), CsvE(ent.ShelfLineCd),
            }));
        }
        var preamble = Encoding.UTF8.GetPreamble();
        var body = Encoding.UTF8.GetBytes(sb.ToString());
        var output = new byte[preamble.Length + body.Length];
        Buffer.BlockCopy(preamble, 0, output, 0, preamble.Length);
        Buffer.BlockCopy(body, 0, output, preamble.Length, body.Length);
        return output;
    }

    // ═══════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════

    private IQueryable<PlateMold> BuildSearchQuery(PlateMoldQueryDto q)
    {
        var x = _db.PlateMolds.AsNoTracking().Where(p => !p.IsDeleted);
        if (!string.IsNullOrWhiteSpace(q.BaseCd)) x = x.Where(p => p.BaseCd == q.BaseCd);
        if (q.StDateFrom.HasValue) x = x.Where(p => p.StDate >= q.StDateFrom.Value);
        if (q.StDateTo.HasValue) x = x.Where(p => p.StDate <= q.StDateTo.Value);
        if (!string.IsNullOrWhiteSpace(q.ArrangeNo)) x = x.Where(p => (p.ArrangeNo ?? "").Contains(q.ArrangeNo!));
        if (!string.IsNullOrWhiteSpace(q.WdPtnNoFrom)) x = x.Where(p => string.Compare(p.WdPtnNo, q.WdPtnNoFrom) >= 0);
        if (!string.IsNullOrWhiteSpace(q.WdPtnNoTo)) x = x.Where(p => string.Compare(p.WdPtnNo, q.WdPtnNoTo) <= 0);
        if (!string.IsNullOrWhiteSpace(q.VrsnName)) x = x.Where(p => p.VrsnName.Contains(q.VrsnName!));
        if (!string.IsNullOrWhiteSpace(q.TypeClass)) x = x.Where(p => p.TypeClass == q.TypeClass);
        if (!string.IsNullOrWhiteSpace(q.NewVerCd)) x = x.Where(p => p.NewVerCd == q.NewVerCd);
        if (!string.IsNullOrWhiteSpace(q.CustomerCd)) x = x.Where(p => p.CustomerCd == q.CustomerCd);
        if (!string.IsNullOrWhiteSpace(q.RepresentativeProductCd)) x = x.Where(p => p.RepresentativeProductCd == q.RepresentativeProductCd);
        if (!string.IsNullOrWhiteSpace(q.ProcessCd)) x = x.Where(p => p.ProcessCd == q.ProcessCd);
        if (q.WdQtyFrom.HasValue) x = x.Where(p => p.WdQty >= q.WdQtyFrom.Value);
        if (q.WdQtyTo.HasValue) x = x.Where(p => p.WdQty <= q.WdQtyTo.Value);
        if (!string.IsNullOrWhiteSpace(q.SupplierCd)) x = x.Where(p => p.SupplierCd == q.SupplierCd);
        if (q.LimitWdQtyFrom.HasValue) x = x.Where(p => p.LimitWdQty >= q.LimitWdQtyFrom.Value);
        if (q.LimitWdQtyTo.HasValue) x = x.Where(p => p.LimitWdQty <= q.LimitWdQtyTo.Value);
        if (!string.IsNullOrWhiteSpace(q.PlaceCd)) x = x.Where(p => p.PlaceCd == q.PlaceCd);
        if (!string.IsNullOrWhiteSpace(q.ShelfLineCd)) x = x.Where(p => p.ShelfLineCd == q.ShelfLineCd);
        if (q.AchieveDateFrom.HasValue) x = x.Where(p => p.AchieveDate >= q.AchieveDateFrom.Value);
        if (q.AchieveDateTo.HasValue) x = x.Where(p => p.AchieveDate <= q.AchieveDateTo.Value);
        if (q.ArrivalDateFrom.HasValue) x = x.Where(p => p.ArrivalActualDate >= q.ArrivalDateFrom.Value);
        if (q.ArrivalDateTo.HasValue) x = x.Where(p => p.ArrivalActualDate <= q.ArrivalDateTo.Value);
        if (q.DispScheduleFrom.HasValue) x = x.Where(p => p.DispScheduleDate >= q.DispScheduleFrom.Value);
        if (q.DispScheduleTo.HasValue) x = x.Where(p => p.DispScheduleDate <= q.DispScheduleTo.Value);
        if (q.ReturnScheduleFrom.HasValue) x = x.Where(p => p.ReturnScheduleDate >= q.ReturnScheduleFrom.Value);
        if (q.ReturnScheduleTo.HasValue) x = x.Where(p => p.ReturnScheduleDate <= q.ReturnScheduleTo.Value);
        if (q.ReturnDateFrom.HasValue) x = x.Where(p => p.ReturnDate >= q.ReturnDateFrom.Value);
        if (q.ReturnDateTo.HasValue) x = x.Where(p => p.ReturnDate <= q.ReturnDateTo.Value);
        return x;
    }

    private static void ValidateBeforeSave(PlateMoldDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.BaseCd))
            throw new InvalidOperationException("E10022: 必須項目に値が指定されていません。項目名:拠点");
        if (string.IsNullOrWhiteSpace(dto.DecisionEstimateNo))
            throw new InvalidOperationException("E10022: 必須項目に値が指定されていません。項目名:決定見積NO");
        if (string.IsNullOrWhiteSpace(dto.VrsnName))
            throw new InvalidOperationException("E10022: 必須項目に値が指定されていません。項目名:版型名");
        if (string.IsNullOrWhiteSpace(dto.CustomerCd))
            throw new InvalidOperationException("E10022: 必須項目に値が指定されていません。項目名:得意先");
        if (string.IsNullOrWhiteSpace(dto.SupplierCd))
            throw new InvalidOperationException("E10022: 必須項目に値が指定されていません。項目名:発注先");
        if (string.IsNullOrWhiteSpace(dto.RepresentativeProductCd))
            throw new InvalidOperationException("E10022: 必須項目に値が指定されていません。項目名:代表製品CD");
        // ST <= END
        if (dto.StDate > dto.EndDate)
            throw new InvalidOperationException($"E10027: 適用開始日と適用終了日の前後関係が不正です。開始:{dto.StDate:yyyy-MM-dd} 終了:{dto.EndDate:yyyy-MM-dd}");
        // ST <= 廃棄予定日（廃棄予定日があれば）
        if (dto.DispScheduleDate.HasValue && dto.StDate > dto.DispScheduleDate.Value)
            throw new InvalidOperationException($"E10028: 適用開始日と廃棄予定日の前後関係が不正です。開始:{dto.StDate:yyyy-MM-dd} 廃棄:{dto.DispScheduleDate.Value:yyyy-MM-dd}");
        // 終了 <= 廃棄予定日
        if (dto.DispScheduleDate.HasValue && dto.EndDate > dto.DispScheduleDate.Value)
            throw new InvalidOperationException($"E10029: 適用終了日と廃棄予定日の前後関係が不正です。終了:{dto.EndDate:yyyy-MM-dd} 廃棄:{dto.DispScheduleDate.Value:yyyy-MM-dd}");
        if (dto.MfgQty < 1)
            throw new InvalidOperationException("E00001: 入力形式・桁数・型に誤りがあります。項目名:個数（1 以上）");
    }

    /// <summary>仕様書 §5 — 工程→版型分類 自動セット</summary>
    private static string? AutoTypeClassFromProcess(string processCd) => processCd switch
    {
        "0300" => "ゴム型",
        "0450" => "オフ型",
        "0600" => "木型",
        "0650" => "箔版",
        _ => null,
    };

    /// <summary>仕様書 §7 — 版型受注連携（118 項を WEB 受注 + 受注明細に書込）</summary>
    private async Task<PlateMoldOrderLinkResultDto> CreatePlateMoldOrderAsync(PlateMold ent, string? userName)
    {
        var now = DateTime.Now;

        // 版型受注 NO 採番（受注と同一機能コード ORD：13桁）
        var (webOrderNo, _) = await DocNumber.NextAsync(_db, "ORD");

        // T_Order — 1〜17 項
        var header = new Order
        {
            WebOrderNo = webOrderNo,
            CustomerCd = ent.CustomerCd,
            OrderType = "30",                              // 30=購買品（仕様書 §7）
            OrderDate = now.Date,
            CustomerDeliveryDate = ent.DeliveryDate,
            Quantity = 1m,
            SalesPriceDiv = "1",                           // 個別単価
            Status = 0,
            McTransferFlg = false,
            Creator = userName, CreateDate = now,
            Modifier = userName, ModifyDate = now,
        };
        _db.Orders.Add(header);

        // T_OrderDetail — 18〜118 項（118 項のうち 構成情報スナップショットは NULL でセット）
        var arrangeNo = $"{webOrderNo}-001";
        var detail = new OrderDetail
        {
            WebOrderNo = webOrderNo,
            WebOrderDetailNo = 1,
            HaibaiNo1 = arrangeNo,
            HaibaiNo2 = ent.CustomerCd,
            HaibaiNo3 = ent.DecisionEstimateNo,
            // 製品コード=版型 NO（OrderDetail.ProductCd は 15 桁、ItemCd も 15 桁）
            // 版型 NO は最大 40 桁 → 15 桁にトリム
            ProductCd = ent.WdPtnNo.Length > 15 ? ent.WdPtnNo.Substring(0, 15) : ent.WdPtnNo,
            ItemCd = ent.WdPtnNo.Length > 15 ? ent.WdPtnNo.Substring(0, 15) : ent.WdPtnNo,
            ProductCatBig = "61",                          // 製品区分_大=61(版・型)
            CustomerItemName1 = ent.VrsnName,
            CpItemName1 = ent.VrsnName,
            QtyUnit = "個",
            Quantity = 1m,
            IndividualUnitPrice = ent.DecisionAmount,
            Amount = ent.DecisionAmount,                   // 数量 × 個別単価
            DeliveryCd = ent.CustomerCd,
            CustomerDeliveryDate = ent.DeliveryDate,
            SalesAvailable = ent.EarningsCd,
            SalesPriceDiv = "1",
            OrderType = "30",
            Status = 0, WfApprovalFlg = false, McTransferFlg = false,
            Creator = userName, CreateDate = now,
            Modifier = userName, ModifyDate = now,
        };
        _db.OrderDetails.Add(detail);
        await _db.SaveChangesAsync();

        return new PlateMoldOrderLinkResultDto
        {
            WebOrderNo = webOrderNo,
            WebOrderDetailNo = 1,
            ArrangeNo = arrangeNo,
        };
    }

    private static string CsvE(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return s;
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }

    // ─── DTO ↔ Entity ───

    private static PlateMoldDto EntityToDto(PlateMold e) => new()
    {
        WdPtnNo = e.WdPtnNo, WdRev = e.WdRev,
        BaseCd = e.BaseCd,
        DecisionEstimateNo = e.DecisionEstimateNo,
        VrsnName = e.VrsnName,
        EarningsCd = e.EarningsCd,
        ArrangeNo = e.ArrangeNo,
        StDate = e.StDate, EndDate = e.EndDate,
        CustomerCd = e.CustomerCd,
        RepresentativeProductCd = e.RepresentativeProductCd,
        NewVerCd = e.NewVerCd,
        ProcessCd = e.ProcessCd,
        TypeClass = e.TypeClass,
        EndPlaceCd = e.EndPlaceCd,
        AchieveDate = e.AchieveDate,
        SheetFlute = e.SheetFlute,
        PaperCdF = e.PaperCdF, PrintCdF = e.PrintCdF, EmbossCdF = e.EmbossCdF, MakerCdF = e.MakerCdF,
        PaperCdC = e.PaperCdC, PrintCdC = e.PrintCdC, EmbossCdC = e.EmbossCdC, MakerCdC = e.MakerCdC,
        PaperCdB = e.PaperCdB, PrintCdB = e.PrintCdB, EmbossCdB = e.EmbossCdB, MakerCdB = e.MakerCdB,
        ExtractionDirect = e.ExtractionDirect,
        DuplicatePlateFlg = e.DuplicatePlateFlg,
        SheetWidth = e.SheetWidth, SheetFlow = e.SheetFlow,
        BladeWidth = e.BladeWidth, BladeFlow = e.BladeFlow,
        CompositionQty = e.CompositionQty, MfgQty = e.MfgQty,
        ColorQty = e.ColorQty, BookQty = e.BookQty,
        FlexoThick = e.FlexoThick, CylinderSize = e.CylinderSize,
        SupplierCd = e.SupplierCd,
        ProcessDestination = e.ProcessDestination,
        DeliveryDate = e.DeliveryDate, ArrivalActualDate = e.ArrivalActualDate,
        EstimateAmount = e.EstimateAmount, DecisionAmount = e.DecisionAmount, PurchaseAmount = e.PurchaseAmount,
        SalesDate = e.SalesDate,
        StrippingCd = e.StrippingCd, StandingCd = e.StandingCd, HammerCd = e.HammerCd, OversightCd = e.OversightCd,
        PlaceCd = e.PlaceCd, ShelfLineCd = e.ShelfLineCd,
        WdQty = e.WdQty, LimitWdQty = e.LimitWdQty,
        DispScheduleDate = e.DispScheduleDate, ReturnScheduleDate = e.ReturnScheduleDate,
        ReturnDate = e.ReturnDate, ReturnReason = e.ReturnReason,
        Memo = e.Memo, SalesNote = e.SalesNote, DispActualDate = e.DispActualDate,
        AtachInfoSheetFront = e.AtachInfoSheetFront, AtachInfoSheetBack = e.AtachInfoSheetBack,
        AtachInfoActual = e.AtachInfoActual, AtachInfoBaseplate = e.AtachInfoBaseplate,
        AtachInfoPositive = e.AtachInfoPositive, AtachInfoNegative = e.AtachInfoNegative,
        AtachInfoMo = e.AtachInfoMo, AtachInfoFd = e.AtachInfoFd,
        NeedDraft = e.NeedDraft, NeedMylar = e.NeedMylar, NeedGalley = e.NeedGalley,
        NeedProof = e.NeedProof, NeedBlueprint = e.NeedBlueprint, NeedComp = e.NeedComp,
        NeedDesignSheet = e.NeedDesignSheet, NeedOtherName = e.NeedOtherName, NeedOther = e.NeedOther,
        Status = e.Status, McTransferFlg = e.McTransferFlg,
        McOrderNo = e.McOrderNo, McOrderDetailNo = e.McOrderDetailNo,
        RowVersion = e.RowVersion,
    };

    private static PlateMold DtoToEntity(PlateMoldDto d)
    {
        var e = new PlateMold();
        ApplyDtoToEntity(d, e);
        return e;
    }

    private static void ApplyDtoToEntity(PlateMoldDto d, PlateMold e)
    {
        e.BaseCd = d.BaseCd;
        e.DecisionEstimateNo = d.DecisionEstimateNo;
        e.VrsnName = d.VrsnName;
        e.EarningsCd = d.EarningsCd;
        e.StDate = d.StDate; e.EndDate = d.EndDate;
        e.CustomerCd = d.CustomerCd;
        e.RepresentativeProductCd = d.RepresentativeProductCd;
        e.NewVerCd = d.NewVerCd;
        e.ProcessCd = d.ProcessCd;
        e.TypeClass = d.TypeClass;
        e.EndPlaceCd = d.EndPlaceCd;
        e.SheetFlute = d.SheetFlute;
        e.PaperCdF = d.PaperCdF; e.PrintCdF = d.PrintCdF; e.EmbossCdF = d.EmbossCdF; e.MakerCdF = d.MakerCdF;
        e.PaperCdC = d.PaperCdC; e.PrintCdC = d.PrintCdC; e.EmbossCdC = d.EmbossCdC; e.MakerCdC = d.MakerCdC;
        e.PaperCdB = d.PaperCdB; e.PrintCdB = d.PrintCdB; e.EmbossCdB = d.EmbossCdB; e.MakerCdB = d.MakerCdB;
        e.ExtractionDirect = d.ExtractionDirect;
        e.DuplicatePlateFlg = d.DuplicatePlateFlg;
        e.SheetWidth = d.SheetWidth; e.SheetFlow = d.SheetFlow;
        e.BladeWidth = d.BladeWidth; e.BladeFlow = d.BladeFlow;
        e.CompositionQty = d.CompositionQty; e.MfgQty = d.MfgQty;
        e.ColorQty = d.ColorQty; e.BookQty = d.BookQty;
        e.FlexoThick = d.FlexoThick; e.CylinderSize = d.CylinderSize;
        e.SupplierCd = d.SupplierCd;
        e.ProcessDestination = d.ProcessDestination;
        e.DeliveryDate = d.DeliveryDate; e.ArrivalActualDate = d.ArrivalActualDate;
        e.EstimateAmount = d.EstimateAmount; e.DecisionAmount = d.DecisionAmount; e.PurchaseAmount = d.PurchaseAmount;
        e.SalesDate = d.SalesDate;
        e.StrippingCd = d.StrippingCd; e.StandingCd = d.StandingCd; e.HammerCd = d.HammerCd; e.OversightCd = d.OversightCd;
        e.LimitWdQty = d.LimitWdQty;
        e.DispScheduleDate = d.DispScheduleDate; e.ReturnScheduleDate = d.ReturnScheduleDate;
        e.ReturnDate = d.ReturnDate; e.ReturnReason = d.ReturnReason;
        e.Memo = d.Memo; e.SalesNote = d.SalesNote; e.DispActualDate = d.DispActualDate;
        e.AtachInfoSheetFront = d.AtachInfoSheetFront; e.AtachInfoSheetBack = d.AtachInfoSheetBack;
        e.AtachInfoActual = d.AtachInfoActual; e.AtachInfoBaseplate = d.AtachInfoBaseplate;
        e.AtachInfoPositive = d.AtachInfoPositive; e.AtachInfoNegative = d.AtachInfoNegative;
        e.AtachInfoMo = d.AtachInfoMo; e.AtachInfoFd = d.AtachInfoFd;
        e.NeedDraft = d.NeedDraft; e.NeedMylar = d.NeedMylar; e.NeedGalley = d.NeedGalley;
        e.NeedProof = d.NeedProof; e.NeedBlueprint = d.NeedBlueprint; e.NeedComp = d.NeedComp;
        e.NeedDesignSheet = d.NeedDesignSheet; e.NeedOtherName = d.NeedOtherName; e.NeedOther = d.NeedOther;
    }
}
