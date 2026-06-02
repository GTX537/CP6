using System.Text;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels;
using CP6.Entity.DTOs;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services;

/// <summary>
/// MSBBPA050/060 - Web 製品マスタ業務服務
/// </summary>
public class ProductService : IProductService
{
    private readonly CP6Context _db;

    /// <summary>枝番 NULL を表す固定値</summary>
    private const string BranchNull = "MCNULLVAL";

    public ProductService(CP6Context db)
    {
        _db = db;
    }

    // ═══════════════════════════════════════════════════════════
    //  分页一覧（MSBBPA060）
    // ═══════════════════════════════════════════════════════════

    /// <summary>製品一覧 排序白名单（前端 el-table prop → ProductMaster 属性名）</summary>
    private static readonly IReadOnlyDictionary<string, string> ProductSortMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["productCd"] = "ProductCd",
            ["setProductCd"] = "SetProductCd",
            ["setProductName"] = "SetProductName",
            ["customerCd"] = "CustomerCd",
            ["customerItemName1"] = "CustomerItemName1",
            ["customerItemName2"] = "CustomerItemName2",
            ["projectNoParent"] = "ProjectNoParent",
            ["projectNoChild"] = "ProjectNoChild",
            ["quotationNo"] = "QuotationNo",
            ["estimateCalcNo"] = "EstimateCalcNo",
            ["status"] = "Status",
            ["modifyDate"] = "ModifyDate",
        };

    public async Task<(List<ProductListItemDto>, int)> GetPageListAsync(ProductQuery query)
    {
        var q = _db.ProductMasters.AsNoTracking().Where(x => !x.IsDeleted);

        if (!string.IsNullOrWhiteSpace(query.ProductCdFrom))
            q = q.Where(x => string.Compare(x.ProductCd, query.ProductCdFrom) >= 0);
        if (!string.IsNullOrWhiteSpace(query.ProductCdTo))
            q = q.Where(x => string.Compare(x.ProductCd, query.ProductCdTo) <= 0);
        if (!string.IsNullOrWhiteSpace(query.CustomerCd))
            q = q.Where(x => x.CustomerCd == query.CustomerCd);
        if (!string.IsNullOrWhiteSpace(query.SetProductCd))
            q = q.Where(x => x.SetProductCd == query.SetProductCd);
        if (!string.IsNullOrWhiteSpace(query.ProjectNoParent))
            q = q.Where(x => x.ProjectNoParent == query.ProjectNoParent);
        if (!string.IsNullOrWhiteSpace(query.ProjectNoChild))
            q = q.Where(x => x.ProjectNoChild == query.ProjectNoChild);
        if (!string.IsNullOrWhiteSpace(query.QuotationNo))
            q = q.Where(x => x.QuotationNo == query.QuotationNo);
        if (!string.IsNullOrWhiteSpace(query.EstimateCalcNo))
            q = q.Where(x => x.EstimateCalcNo == query.EstimateCalcNo);
        if (!string.IsNullOrWhiteSpace(query.CustomerItemName1))
        {
            var p = query.CustomerItemName1;
            q = q.Where(x => x.CustomerItemName1 != null && x.CustomerItemName1.Contains(p));
        }
        if (!string.IsNullOrWhiteSpace(query.CustomerItemName2))
        {
            var p2 = query.CustomerItemName2;
            q = q.Where(x => x.CustomerItemName2 != null && x.CustomerItemName2.Contains(p2));
        }
        if (!string.IsNullOrWhiteSpace(query.DesignProposalNo))
            q = q.Where(x => x.DesignProposalNo == query.DesignProposalNo);
        if (query.ModifyDateFrom.HasValue)
            q = q.Where(x => x.ModifyDate >= query.ModifyDateFrom.Value);
        if (query.ModifyDateTo.HasValue)
        {
            // To 日付を含めるため +1 日未満
            var to = query.ModifyDateTo.Value.Date.AddDays(1);
            q = q.Where(x => x.ModifyDate < to);
        }
        if (query.Statuses != null && query.Statuses.Count > 0)
            q = q.Where(x => query.Statuses.Contains(x.Status));

        var total = await q.CountAsync();

        q = QuerySort.Apply(q, query.SortField, query.SortOrder, ProductSortMap,
            s => s.OrderBy(x => x.ProductCd));

        var page = await q
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(x => new ProductListItemDto
            {
                ProductCd = x.ProductCd,
                SetProductCd = x.SetProductCd,
                SetProductName = x.SetProductName,
                CustomerCd = x.CustomerCd,
                CustomerItemName1 = x.CustomerItemName1,
                CustomerItemName2 = x.CustomerItemName2,
                ProjectNoParent = x.ProjectNoParent,
                ProjectNoChild = x.ProjectNoChild,
                QuotationNo = x.QuotationNo,
                EstimateCalcNo = x.EstimateCalcNo,
                Status = x.Status,
                WfApprovalFlg = x.WfApprovalFlg,
                McTransferFlg = x.McTransferFlg,
                ModifyDate = x.ModifyDate,
            })
            .ToListAsync();

        return (page, total);
    }

    // ═══════════════════════════════════════════════════════════
    //  詳細取得
    // ═══════════════════════════════════════════════════════════

    public async Task<ProductDto?> GetByCdAsync(string productCd, bool includeDeleted = false)
    {
        var query = _db.ProductMasters.AsNoTracking().Where(x => x.ProductCd == productCd);
        if (!includeDeleted) query = query.Where(x => !x.IsDeleted);

        var entity = await query.FirstOrDefaultAsync();
        if (entity == null) return null;

        var processes = await _db.ProductProcesses.AsNoTracking()
            .Where(p => p.ProductCd == productCd && !p.IsDeleted)
            .OrderBy(p => p.SortOrder).ToListAsync();
        var materials = await _db.ProductMaterials.AsNoTracking()
            .Where(m => m.ProductCd == productCd && !m.IsDeleted)
            .OrderBy(m => m.SortOrder).ToListAsync();
        var lotPrices = await _db.ProductLotPrices.AsNoTracking()
            .Where(l => l.ProductCd == productCd && !l.IsDeleted)
            .OrderBy(l => l.DetailNo).ToListAsync();
        var coProducts = await _db.ProductCoProducts.AsNoTracking()
            .Where(c => c.ProductCd == productCd && !c.IsDeleted)
            .OrderBy(c => c.RowNo).ToListAsync();

        return new ProductDto
        {
            ProductCd = entity.ProductCd,
            ItemCd = entity.ItemCd,
            Branch1 = entity.Branch1,
            Branch2 = entity.Branch2,
            Branch3 = entity.Branch3,
            BasicInfo = MapToBasicInfo(entity),
            Processes = processes.Select(MapToProcessDto).ToList(),
            Materials = materials.Select(MapToMaterialDto).ToList(),
            LotPrices = lotPrices.Select(MapToLotPriceDto).ToList(),
            CoProducts = coProducts.Select(MapToCoProductDto).ToList(),
            Status = entity.Status,
            WfApprovalFlg = entity.WfApprovalFlg,
            McTransferFlg = entity.McTransferFlg,
            RowVersion = entity.RowVersion,
        };
    }

    // ═══════════════════════════════════════════════════════════
    //  登録（CREATE）
    // ═══════════════════════════════════════════════════════════

    public async Task<string> CreateAsync(ProductDto dto, string? userName)
    {
        // 部材一覧が空の場合は最低 1 行の親部材をデフォルト生成
        var members = dto.Members.Count > 0
            ? dto.Members
            : new List<ProductMemberDto> { new() { RowNo = 1 } };

        // 採番：ItemCd は全部材で共有；Branch1 は行番ごと
        var seq = await NextSequenceAsync();
        var itemCd = seq;            // 13 桁（PRD+年月+自増）
        // セット製品 CD = 親（行 1）の ProductCd を全行で共有
        var setProductCd = $"{itemCd}0001";

        // 行 1 (親) を主表として登録（親子区分=0）
        var firstRow = members.OrderBy(m => m.RowNo).First();
        var productCd = $"{itemCd}{firstRow.RowNo:D4}";

        var entity = MapFromBasicInfo(dto.BasicInfo, new ProductMaster());
        entity.ProductCd = productCd;
        entity.ItemCd = itemCd;
        entity.Branch1 = $"{firstRow.RowNo:D4}";
        entity.Branch2 = BranchNull;
        entity.Branch3 = BranchNull;
        entity.SetProductCd = setProductCd;
        entity.ParentChildDiv = "0"; // 親
        entity.QuotationNo = firstRow.QuotationNo;
        entity.EstimateCalcNo = firstRow.EstimateCalcNo;
        entity.ProjectNoParent = firstRow.ProjectNoParent ?? dto.BasicInfo.CustomerName;
        entity.ProjectNoChild = firstRow.ProjectNoChild;
        entity.ProjectNoMaterial = firstRow.ProjectNoMaterial;
        // ステータス・WF：仕様書「ステータスと転送FLGの扱い方」§製品基本マスタ
        //   ① EstimateCalcNo あり → 見積計算書 WF 承認待ち（Status=0, WF=0）
        //   ③ EstimateCalcNo NULL → 承認済とみなして登録（Status=1, WF=1）
        var approvedAtCreate = string.IsNullOrWhiteSpace(firstRow.EstimateCalcNo);
        entity.Status = approvedAtCreate ? 1 : 0;
        entity.WfApprovalFlg = approvedAtCreate;
        entity.McTransferFlg = false; // 連携は別 IF 経由
        entity.Creator = userName;
        entity.CreateDate = DateTime.Now;

        _db.ProductMasters.Add(entity);

        // 子部材行（2 行目以降）
        foreach (var m in members.OrderBy(x => x.RowNo).Skip(1))
        {
            var childCd = $"{itemCd}{m.RowNo:D4}";
            var childEntity = MapFromBasicInfo(dto.BasicInfo, new ProductMaster());
            childEntity.ProductCd = childCd;
            childEntity.ItemCd = itemCd;
            childEntity.Branch1 = $"{m.RowNo:D4}";
            childEntity.Branch2 = BranchNull;
            childEntity.Branch3 = BranchNull;
            childEntity.SetProductCd = setProductCd;
            childEntity.ParentChildDiv = "1"; // 子
            childEntity.QuotationNo = m.QuotationNo;
            childEntity.EstimateCalcNo = m.EstimateCalcNo;
            childEntity.ProjectNoParent = m.ProjectNoParent;
            childEntity.ProjectNoChild = m.ProjectNoChild;
            childEntity.ProjectNoMaterial = m.ProjectNoMaterial;
            childEntity.CustomerItemName1 = m.CustomerProductName1 ?? dto.BasicInfo.CustomerItemName1;
            childEntity.CustomerItemName2 = m.CustomerProductName2 ?? dto.BasicInfo.CustomerItemName2;
            // 子部材も親同様：EstimateCalcNo の有無でステータス判定
            var childApproved = string.IsNullOrWhiteSpace(m.EstimateCalcNo);
            childEntity.Status = childApproved ? 1 : 0;
            childEntity.WfApprovalFlg = childApproved;
            childEntity.McTransferFlg = false;
            childEntity.Creator = userName;
            childEntity.CreateDate = DateTime.Now;
            _db.ProductMasters.Add(childEntity);
        }

        // 子表は親 ProductCd 配下に登録（仕様書の解釈：1 セットの工程/材料/単価/連産品は親に紐付）
        AddProcesses(productCd, dto.Processes, userName);
        AddMaterials(productCd, dto.Materials, userName);
        AddLotPrices(productCd, dto.LotPrices, userName);
        AddCoProducts(productCd, dto.CoProducts, userName);

        await _db.SaveChangesAsync();
        return productCd;
    }

    // ═══════════════════════════════════════════════════════════
    //  訂正（UPDATE）
    // ═══════════════════════════════════════════════════════════

    public async Task UpdateAsync(string productCd, ProductDto dto, string? userName)
    {
        var entity = await _db.ProductMasters
            .FirstOrDefaultAsync(x => x.ProductCd == productCd && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"製品 CD '{productCd}' が見つかりません。");

        // 楽観锁：渡された RowVersion を Original にセット
        if (dto.RowVersion != null)
            _db.Entry(entity).Property(x => x.RowVersion).OriginalValue = dto.RowVersion;

        MapFromBasicInfo(dto.BasicInfo, entity);
        // 仕様書「ステータスと転送FLGの扱い方」§製品基本マスタ — 更新時：
        //   DATA_STATUS / WF_APRV_FLG は「変更しない」（PE/承認 IF からのみ遷移）
        //   MCF7_TRANS_FLG は更新時に 0 へ戻す（再連携対象としてマーク）
        entity.McTransferFlg = false;
        entity.Modifier = userName;
        entity.ModifyDate = DateTime.Now;

        // 子表 = 全削除 → 再挿入（複雑な diff は Phase 3 以降で最適化）
        await ReplaceChildrenAsync(productCd, dto, userName);

        await _db.SaveChangesAsync();
    }

    // ═══════════════════════════════════════════════════════════
    //  削除（SOFT DELETE）
    // ═══════════════════════════════════════════════════════════

    public async Task DeleteAsync(string productCd, byte[]? rowVersion, string? userName)
    {
        var entity = await _db.ProductMasters
            .FirstOrDefaultAsync(x => x.ProductCd == productCd && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"製品 CD '{productCd}' が見つかりません。");

        if (rowVersion != null)
            _db.Entry(entity).Property(x => x.RowVersion).OriginalValue = rowVersion;

        entity.IsDeleted = true;
        entity.Modifier = userName;
        entity.ModifyDate = DateTime.Now;

        // 子表も同時 soft-delete
        await SoftDeleteChildrenAsync(productCd, userName);

        await _db.SaveChangesAsync();
    }

    // ═══════════════════════════════════════════════════════════
    //  コピー（COPY）
    // ═══════════════════════════════════════════════════════════

    /// <summary>製品マスタ コピー上限（仕様書 メッセージ一覧 E10107）</summary>
    private const int CopyMemberLimit = 100;

    public async Task<string> CopyAsync(string sourceProductCd, string? userName)
    {
        var src = await GetByCdAsync(sourceProductCd)
            ?? throw new KeyNotFoundException($"複製元 製品 CD '{sourceProductCd}' が見つかりません。");

        // E10107: 一度にコピーできる部材は 100 件まで
        if (src.Members.Count > CopyMemberLimit)
            throw new InvalidOperationException(
                $"E10107: 一度にコピーできる部材は {CopyMemberLimit} 件までです（現在: {src.Members.Count} 件）。");

        // ステータスをリセット、PK と RowVersion をクリアして CreateAsync に流す
        src.ProductCd = null;
        src.RowVersion = null;
        src.Status = 0;
        src.WfApprovalFlg = false;
        src.McTransferFlg = false;

        // 部材一覧を 1 行のみ（親）にリセット
        if (src.Members.Count == 0)
            src.Members.Add(new ProductMemberDto { RowNo = 1 });

        return await CreateAsync(src, userName);
    }

    // ═══════════════════════════════════════════════════════════
    //  採番（Next Sequence）
    // ═══════════════════════════════════════════════════════════

    public async Task<string> NextSequenceAsync()
    {
        // 品目コード = 機能コード(PRD)+年(4)+月(2)+自増(4)=13桁（永不重置）
        var (no, _) = await DocNumber.NextAsync(_db, "PRD");
        return no;
    }

    // ═══════════════════════════════════════════════════════════
    //  外部画面連携引入
    // ═══════════════════════════════════════════════════════════

    public async Task<List<ProductMemberDto>> GetMembersByQuotationAsync(string quotationNo)
    {
        // 既存の御見積書明細を部材一覧に変換（MSBBPA040 → MSBBPA050 第1页 引入）
        var details = await _db.QuotationDetails.AsNoTracking()
            .Where(d => d.QtnNo == quotationNo && !d.IsDeleted)
            .OrderBy(d => d.DetailNo)
            .ToListAsync();

        if (details.Count == 0) return new();

        var qtn = await _db.Quotations.AsNoTracking()
            .FirstOrDefaultAsync(q => q.QtnNo == quotationNo && !q.IsDeleted);

        // 既存マスタとの照合
        var existing = await _db.ProductMasters.AsNoTracking()
            .Where(p => p.QuotationNo == quotationNo && !p.IsDeleted)
            .ToListAsync();
        var existingMap = existing.ToDictionary(p => p.Branch1 ?? "");

        return details.Select(d =>
        {
            var branch1 = $"{d.DetailNo:D4}";
            existingMap.TryGetValue(branch1, out var em);
            return new ProductMemberDto
            {
                RowNo = d.DetailNo,
                ProductCd = em?.ProductCd,
                EstimateCalcNo = d.QtnCalcNo,
                QuotationNo = quotationNo,
                ProjectNoParent = qtn?.ProjectNoParent,
                ProjectNoChild = qtn?.ProjectNoChild,
                ProjectNoMaterial = qtn?.ProjectNoMaterial,
                CustomerProductName1 = d.ItemName1,
                CustomerProductName2 = d.ItemName2,
                Status = em?.Status ?? 0,
                WfApproved = em?.WfApprovalFlg ?? false,
                MasterLinked = em != null,
            };
        }).ToList();
    }

    public async Task<ProductBasicInfoDto?> GetBasicInfoByEstimateCalcAsync(string estimateCalcNo)
    {
        var calc = await _db.EstimateCalcs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.QtnCalcNo == estimateCalcNo && !c.IsDeleted);
        if (calc == null) return null;

        // 見積計算書 → 製品マスタ基本情報の引入（最小限のフィールドマッピング）
        return new ProductBasicInfoDto
        {
            CustomerCd = calc.CustomerCd ?? string.Empty,
            CustomerItemName1 = calc.CustomerProductName1,
            CustomerItemName2 = calc.CustomerProductName2,
        };
    }

    // ═══════════════════════════════════════════════════════════
    //  CSV 出力（MSBBPA060 一覧）
    // ═══════════════════════════════════════════════════════════

    public async Task<byte[]> ExportCsvAsync(ProductQuery query)
    {
        // ページング無視ですべての検索結果を取得
        var noPageQuery = new ProductQuery
        {
            Page = 1,
            PageSize = int.MaxValue,
            ProductCdFrom = query.ProductCdFrom,
            ProductCdTo = query.ProductCdTo,
            CustomerCd = query.CustomerCd,
            SetProductCd = query.SetProductCd,
            ProjectNoParent = query.ProjectNoParent,
            ProjectNoChild = query.ProjectNoChild,
            QuotationNo = query.QuotationNo,
            EstimateCalcNo = query.EstimateCalcNo,
            CustomerItemName1 = query.CustomerItemName1,
            CustomerItemName2 = query.CustomerItemName2,
            DesignProposalNo = query.DesignProposalNo,
            ModifyDateFrom = query.ModifyDateFrom,
            ModifyDateTo = query.ModifyDateTo,
            Statuses = query.Statuses,
        };
        var (rows, _) = await GetPageListAsync(noPageQuery);

        var sb = new StringBuilder();
        // BOM for Excel UTF-8 認識
        sb.Append('\uFEFF');
        // ヘッダー
        sb.AppendLine(string.Join(",", new[]
        {
            "製品CD", "セット製品CD", "セット品名", "顧客CD",
            "顧客品名1", "顧客品名2", "親案件", "子案件",
            "御見積書NO", "見積計算書NO", "状態", "WF承認", "MC転送", "更新日",
        }));

        foreach (var r in rows)
        {
            sb.AppendLine(string.Join(",", new[]
            {
                CsvEscape(r.ProductCd),
                CsvEscape(r.SetProductCd),
                CsvEscape(r.SetProductName),
                CsvEscape(r.CustomerCd),
                CsvEscape(r.CustomerItemName1),
                CsvEscape(r.CustomerItemName2),
                CsvEscape(r.ProjectNoParent),
                CsvEscape(r.ProjectNoChild),
                CsvEscape(r.QuotationNo),
                CsvEscape(r.EstimateCalcNo),
                StatusLabel(r.Status),
                r.WfApprovalFlg ? "○" : "",
                r.McTransferFlg ? "○" : "",
                r.ModifyDate?.ToString("yyyy-MM-dd HH:mm") ?? "",
            }));
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static string CsvEscape(string? v)
    {
        if (string.IsNullOrEmpty(v)) return "";
        var needQuote = v.Contains(',') || v.Contains('"') || v.Contains('\n') || v.Contains('\r');
        var s = v.Replace("\"", "\"\"");
        return needQuote ? $"\"{s}\"" : s;
    }

    private static string StatusLabel(int s) => s switch
    {
        9 => "承認済",
        1 => "承認待",
        _ => "未作成",
    };

    // ═══════════════════════════════════════════════════════════
    //  Helper：子表 全削除＋再挿入
    // ═══════════════════════════════════════════════════════════

    private async Task ReplaceChildrenAsync(string productCd, ProductDto dto, string? userName)
    {
        var oldProcesses = await _db.ProductProcesses.Where(x => x.ProductCd == productCd).ToListAsync();
        var oldMaterials = await _db.ProductMaterials.Where(x => x.ProductCd == productCd).ToListAsync();
        var oldLotPrices = await _db.ProductLotPrices.Where(x => x.ProductCd == productCd).ToListAsync();
        var oldCoProducts = await _db.ProductCoProducts.Where(x => x.ProductCd == productCd).ToListAsync();
        _db.ProductProcesses.RemoveRange(oldProcesses);
        _db.ProductMaterials.RemoveRange(oldMaterials);
        _db.ProductLotPrices.RemoveRange(oldLotPrices);
        _db.ProductCoProducts.RemoveRange(oldCoProducts);

        AddProcesses(productCd, dto.Processes, userName);
        AddMaterials(productCd, dto.Materials, userName);
        AddLotPrices(productCd, dto.LotPrices, userName);
        AddCoProducts(productCd, dto.CoProducts, userName);
    }

    private async Task SoftDeleteChildrenAsync(string productCd, string? userName)
    {
        await _db.ProductProcesses.Where(x => x.ProductCd == productCd && !x.IsDeleted)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.IsDeleted, true)
                .SetProperty(x => x.Modifier, userName)
                .SetProperty(x => x.ModifyDate, DateTime.Now));
        await _db.ProductMaterials.Where(x => x.ProductCd == productCd && !x.IsDeleted)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.IsDeleted, true)
                .SetProperty(x => x.Modifier, userName)
                .SetProperty(x => x.ModifyDate, DateTime.Now));
        await _db.ProductLotPrices.Where(x => x.ProductCd == productCd && !x.IsDeleted)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.IsDeleted, true)
                .SetProperty(x => x.Modifier, userName)
                .SetProperty(x => x.ModifyDate, DateTime.Now));
        await _db.ProductCoProducts.Where(x => x.ProductCd == productCd && !x.IsDeleted)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.IsDeleted, true)
                .SetProperty(x => x.Modifier, userName)
                .SetProperty(x => x.ModifyDate, DateTime.Now));
    }

    private void AddProcesses(string productCd, List<ProductProcessDto> list, string? userName)
    {
        var sortIdx = 0;
        foreach (var p in list)
        {
            sortIdx += 10;
            var entity = new ProductProcess
            {
                ProductCd = productCd,
                TaskCd = p.TaskCd,
                ProcessCd = p.ProcessCd,
                TopItemCd = p.TopItemCd,
                TopBranch1 = p.TopBranch1,
                TopBranch2 = p.TopBranch2,
                TopBranch3 = p.TopBranch3,
                ItemCd = p.ItemCd,
                Branch1 = p.Branch1,
                Branch2 = p.Branch2,
                Branch3 = p.Branch3,
                WgCd = p.WgCd,
                MachineOrVendor = p.MachineOrVendor,
                MachineFixedFlg = p.MachineFixedFlg,
                CpDeliveryDiv = p.CpDeliveryDiv,
                Spec01 = p.Specs.ElementAtOrDefault(0),
                Spec02 = p.Specs.ElementAtOrDefault(1),
                Spec03 = p.Specs.ElementAtOrDefault(2),
                Spec04 = p.Specs.ElementAtOrDefault(3),
                Spec05 = p.Specs.ElementAtOrDefault(4),
                Spec06 = p.Specs.ElementAtOrDefault(5),
                Spec07 = p.Specs.ElementAtOrDefault(6),
                Spec08 = p.Specs.ElementAtOrDefault(7),
                Spec09 = p.Specs.ElementAtOrDefault(8),
                Spec10 = p.Specs.ElementAtOrDefault(9),
                PlateNo1 = p.PlateNo1,
                PlateNo2 = p.PlateNo2,
                PlateNo3 = p.PlateNo3,
                Consumable1 = p.Consumable1,
                Consumable2 = p.Consumable2,
                Consumable3 = p.Consumable3,
                PurchasePrice = p.PurchasePrice,
                FixedPrice = p.FixedPrice,
                LossRate = p.LossRate,
                MachineCount = p.MachineCount,
                LeadTime = p.LeadTime,
                ProcessNote1 = p.ProcessNote1,
                ProcessNote2 = p.ProcessNote2,
                StorageDest = p.StorageDest,
                SortOrder = p.SortOrder > 0 ? p.SortOrder : sortIdx,
                ManufOrderPrio1 = p.ManufOrderPrios.ElementAtOrDefault(0),
                ManufOrderPrio2 = p.ManufOrderPrios.ElementAtOrDefault(1),
                ManufOrderPrio3 = p.ManufOrderPrios.ElementAtOrDefault(2),
                ManufOrderPrio4 = p.ManufOrderPrios.ElementAtOrDefault(3),
                ManufOrderPrio5 = p.ManufOrderPrios.ElementAtOrDefault(4),
                ManufOrderPrio6 = p.ManufOrderPrios.ElementAtOrDefault(5),
                ManufOrderPrio7 = p.ManufOrderPrios.ElementAtOrDefault(6),
                ManufOrderPrio8 = p.ManufOrderPrios.ElementAtOrDefault(7),
                Creator = userName,
                CreateDate = DateTime.Now,
            };
            _db.ProductProcesses.Add(entity);
        }
    }

    private void AddMaterials(string productCd, List<ProductMaterialDto> list, string? userName)
    {
        var sortIdx = 0;
        foreach (var m in list)
        {
            sortIdx += 10;
            _db.ProductMaterials.Add(new ProductMaterial
            {
                ProductCd = productCd,
                ProcessCd = m.ProcessCd,
                MaterialCd = m.MaterialCd,
                MaterialTypeDiv = m.MaterialTypeDiv,
                ItemCd = m.ItemCd,
                Branch1 = m.Branch1,
                Branch2 = m.Branch2,
                Branch3 = m.Branch3,
                SupplyDiv = m.SupplyDiv,
                SupplyPrice = m.SupplyPrice,
                SortOrder = m.SortOrder > 0 ? m.SortOrder : sortIdx,
                Creator = userName,
                CreateDate = DateTime.Now,
            });
        }
    }

    private void AddLotPrices(string productCd, List<ProductLotPriceDto> list, string? userName)
    {
        var detailNo = 0;
        foreach (var l in list)
        {
            detailNo++;
            _db.ProductLotPrices.Add(new ProductLotPrice
            {
                ProductCd = productCd,
                DetailNo = l.DetailNo > 0 ? l.DetailNo : detailNo,
                ItemCd = l.ItemCd,
                Branch1 = l.Branch1,
                Branch2 = l.Branch2,
                Branch3 = l.Branch3,
                CurrentPriceDate = l.CurrentPriceDate,
                NewPriceDate = l.NewPriceDate,
                CurrentBranchDate = l.CurrentBranchDate,
                NewBranchDate = l.NewBranchDate,
                LotQty = l.LotQty,
                CurrentSetPrice = l.CurrentSetPrice,
                NewSetPrice = l.NewSetPrice,
                CurrentUnitPrice = l.CurrentUnitPrice,
                NewUnitPrice = l.NewUnitPrice,
                CurrentBranchPrice = l.CurrentBranchPrice,
                NewBranchPrice = l.NewBranchPrice,
                PurchasePrice = l.PurchasePrice,
                PriceApplyBasis = l.PriceApplyBasis,
                Creator = userName,
                CreateDate = DateTime.Now,
            });
        }
    }

    private void AddCoProducts(string productCd, List<ProductCoProductDto> list, string? userName)
    {
        foreach (var c in list)
        {
            _db.ProductCoProducts.Add(new ProductCoProduct
            {
                ProductCd = productCd,
                ProcessCd = c.ProcessCd,
                RowNo = c.RowNo,
                CoProductName = c.CoProductName,
                QtyRatio = c.QtyRatio,
                NextProcessCd = c.NextProcessCd,
                Creator = userName,
                CreateDate = DateTime.Now,
            });
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  Mapping (Entity ⇄ DTO)
    // ═══════════════════════════════════════════════════════════

    private static ProductBasicInfoDto MapToBasicInfo(ProductMaster e) => new()
    {
        CustomerCd = e.CustomerCd,
        SetProductCd = e.SetProductCd,
        SetProductName = e.SetProductName,
        ParentChildDiv = e.ParentChildDiv,
        CustomerItemName1 = e.CustomerItemName1,
        CustomerItemName2 = e.CustomerItemName2,
        CustomerPartNo = e.CustomerPartNo,
        CpItemName1 = e.CpItemName1,
        CpItemName2 = e.CpItemName2,
        JanCode = e.JanCode,
        SetRatio = e.SetRatio,
        ProductUsage = e.ProductUsage,
        DistributionDiv = e.DistributionDiv,
        ConfidentialInfo = e.ConfidentialInfo,
        SeizureDiv = e.SeizureDiv,
        ImportanceDiv = e.ImportanceDiv,
        MChange = e.MChange,
        QualityDiv = e.QualityDiv,
        ShipInspection = e.ShipInspection,
        ProductShape = e.ProductShape,
        UnescoMark = e.UnescoMark,
        OrigamiMark = e.OrigamiMark,
        FourMContract = e.FourMContract,
        TkpWrinkleStd = e.TkpWrinkleStd,
        FoodSafety = e.FoodSafety,
        AdShape = e.AdShape,
        StrategicDivs = new[]
        {
            e.StrategicDiv01 == "1", e.StrategicDiv02 == "1", e.StrategicDiv03 == "1",
            e.StrategicDiv04 == "1", e.StrategicDiv05 == "1", e.StrategicDiv06 == "1",
            e.StrategicDiv07 == "1", e.StrategicDiv08 == "1", e.StrategicDiv09 == "1",
            e.StrategicDiv10 == "1",
        },
        FscProductDiv = e.FscProductDiv,
        FscMaterialDiv = e.FscMaterialDiv,
        FscManagementNo = e.FscManagementNo,
        RecyclingPayment = e.RecyclingPayment,
        IdMark = e.IdMark,
        PaperUsageG = e.PaperUsageG,
        PlasticUsageG = e.PlasticUsageG,
        GlassUsageG = e.GlassUsageG,
        PetUsageG = e.PetUsageG,
        PackPaperUsageG = e.PackPaperUsageG,
        PackPlasticUsageG = e.PackPlasticUsageG,
        DesignProposalNo = e.DesignProposalNo,
        OrderType = e.OrderType,
        SheetFlute = e.SheetFlute,
        PaperCdF = e.PaperCdF,
        PrintCdF = e.PrintCdF,
        EmbossCdF = e.EmbossCdF,
        MakerCdF = e.MakerCdF,
        PaperCdC = e.PaperCdC,
        PrintCdC = e.PrintCdC,
        EmbossCdC = e.EmbossCdC,
        PaperCdB = e.PaperCdB,
        PrintCdB = e.PrintCdB,
        EmbossCdB = e.EmbossCdB,
        MakerCdB = e.MakerCdB,
        SheetPrint = e.SheetPrint,
        BladeWidth = e.BladeWidth,
        BladeFlow = e.BladeFlow,
        GutterFb = e.GutterFb,
        GutterLr = e.GutterLr,
        SheetDimW = e.SheetDimW,
        SheetDimF = e.SheetDimF,
        FinalMachineProcess = e.FinalMachineProcess,
        PrintNote = e.PrintNote,
        MfgNote = e.MfgNote,
        SlipNote = e.SlipNote,
        DeliveryNote = e.DeliveryNote,
        ShipNote1 = e.ShipNote1,
        ShipNote2 = e.ShipNote2,
        ProductCatBig = e.ProductCatBig,
        ProductCatMid = e.ProductCatMid,
        ProductCatSml = e.ProductCatSml,
        SalesPriceDiv = e.SalesPriceDiv,
        QtyUnit = e.QtyUnit,
        UnitPriceUnit = e.UnitPriceUnit,
        PurchaseVendor = e.PurchaseVendor,
        FreightBilling = e.FreightBilling,
        FixedShipment = e.FixedShipment,
        DeliveryReserve = e.DeliveryReserve,
        SalesSample = e.SalesSample,
    };

    private static ProductMaster MapFromBasicInfo(ProductBasicInfoDto d, ProductMaster e)
    {
        e.CustomerCd = d.CustomerCd;
        e.SetProductCd = d.SetProductCd;
        e.SetProductName = d.SetProductName;
        e.ParentChildDiv = d.ParentChildDiv;
        e.CustomerItemName1 = d.CustomerItemName1;
        e.CustomerItemName2 = d.CustomerItemName2;
        e.CustomerPartNo = d.CustomerPartNo;
        e.CpItemName1 = d.CpItemName1;
        e.CpItemName2 = d.CpItemName2;
        e.JanCode = d.JanCode;
        e.SetRatio = d.SetRatio == 0 ? 1m : d.SetRatio;
        e.ProductUsage = d.ProductUsage;
        e.DistributionDiv = d.DistributionDiv;
        e.ConfidentialInfo = d.ConfidentialInfo;
        e.SeizureDiv = d.SeizureDiv;
        e.ImportanceDiv = d.ImportanceDiv;
        e.MChange = d.MChange;
        e.QualityDiv = d.QualityDiv;
        e.ShipInspection = d.ShipInspection;
        e.ProductShape = d.ProductShape;
        e.UnescoMark = d.UnescoMark;
        e.OrigamiMark = d.OrigamiMark;
        e.FourMContract = d.FourMContract;
        e.TkpWrinkleStd = d.TkpWrinkleStd;
        e.FoodSafety = d.FoodSafety;
        e.AdShape = d.AdShape;
        var s = d.StrategicDivs ?? Array.Empty<bool>();
        e.StrategicDiv01 = s.ElementAtOrDefault(0) ? "1" : "0";
        e.StrategicDiv02 = s.ElementAtOrDefault(1) ? "1" : "0";
        e.StrategicDiv03 = s.ElementAtOrDefault(2) ? "1" : "0";
        e.StrategicDiv04 = s.ElementAtOrDefault(3) ? "1" : "0";
        e.StrategicDiv05 = s.ElementAtOrDefault(4) ? "1" : "0";
        e.StrategicDiv06 = s.ElementAtOrDefault(5) ? "1" : "0";
        e.StrategicDiv07 = s.ElementAtOrDefault(6) ? "1" : "0";
        e.StrategicDiv08 = s.ElementAtOrDefault(7) ? "1" : "0";
        e.StrategicDiv09 = s.ElementAtOrDefault(8) ? "1" : "0";
        e.StrategicDiv10 = s.ElementAtOrDefault(9) ? "1" : "0";
        e.FscProductDiv = d.FscProductDiv;
        e.FscMaterialDiv = d.FscMaterialDiv;
        e.FscManagementNo = d.FscManagementNo;
        e.RecyclingPayment = d.RecyclingPayment;
        e.IdMark = d.IdMark;
        e.PaperUsageG = d.PaperUsageG;
        e.PlasticUsageG = d.PlasticUsageG;
        e.GlassUsageG = d.GlassUsageG;
        e.PetUsageG = d.PetUsageG;
        e.PackPaperUsageG = d.PackPaperUsageG;
        e.PackPlasticUsageG = d.PackPlasticUsageG;
        e.DesignProposalNo = d.DesignProposalNo;
        e.OrderType = d.OrderType;
        e.SheetFlute = d.SheetFlute;
        e.PaperCdF = d.PaperCdF;
        e.PrintCdF = d.PrintCdF;
        e.EmbossCdF = d.EmbossCdF;
        e.MakerCdF = d.MakerCdF;
        e.PaperCdC = d.PaperCdC;
        e.PrintCdC = d.PrintCdC;
        e.EmbossCdC = d.EmbossCdC;
        e.PaperCdB = d.PaperCdB;
        e.PrintCdB = d.PrintCdB;
        e.EmbossCdB = d.EmbossCdB;
        e.MakerCdB = d.MakerCdB;
        e.SheetPrint = d.SheetPrint;
        e.BladeWidth = d.BladeWidth;
        e.BladeFlow = d.BladeFlow;
        e.GutterFb = d.GutterFb;
        e.GutterLr = d.GutterLr;
        e.SheetDimW = d.SheetDimW;
        e.SheetDimF = d.SheetDimF;
        e.FinalMachineProcess = d.FinalMachineProcess;
        e.PrintNote = d.PrintNote;
        e.MfgNote = d.MfgNote;
        e.SlipNote = d.SlipNote;
        e.DeliveryNote = d.DeliveryNote;
        e.ShipNote1 = d.ShipNote1;
        e.ShipNote2 = d.ShipNote2;
        e.ProductCatBig = d.ProductCatBig;
        e.ProductCatMid = d.ProductCatMid;
        e.ProductCatSml = d.ProductCatSml;
        e.SalesPriceDiv = string.IsNullOrEmpty(d.SalesPriceDiv) ? "2" : d.SalesPriceDiv;
        e.QtyUnit = d.QtyUnit;
        e.UnitPriceUnit = d.UnitPriceUnit;
        e.PurchaseVendor = d.PurchaseVendor;
        e.FreightBilling = string.IsNullOrEmpty(d.FreightBilling) ? "0" : d.FreightBilling;
        e.FixedShipment = d.FixedShipment;
        e.DeliveryReserve = d.DeliveryReserve;
        e.SalesSample = d.SalesSample;
        return e;
    }

    private static ProductProcessDto MapToProcessDto(ProductProcess e) => new()
    {
        TaskCd = e.TaskCd,
        ProcessCd = e.ProcessCd,
        TopItemCd = e.TopItemCd,
        TopBranch1 = e.TopBranch1,
        TopBranch2 = e.TopBranch2,
        TopBranch3 = e.TopBranch3,
        ItemCd = e.ItemCd,
        Branch1 = e.Branch1,
        Branch2 = e.Branch2,
        Branch3 = e.Branch3,
        WgCd = e.WgCd,
        MachineOrVendor = e.MachineOrVendor,
        MachineFixedFlg = e.MachineFixedFlg,
        CpDeliveryDiv = e.CpDeliveryDiv,
        Specs = new[] { e.Spec01, e.Spec02, e.Spec03, e.Spec04, e.Spec05, e.Spec06, e.Spec07, e.Spec08, e.Spec09, e.Spec10 },
        PlateNo1 = e.PlateNo1,
        PlateNo2 = e.PlateNo2,
        PlateNo3 = e.PlateNo3,
        Consumable1 = e.Consumable1,
        Consumable2 = e.Consumable2,
        Consumable3 = e.Consumable3,
        PurchasePrice = e.PurchasePrice,
        FixedPrice = e.FixedPrice,
        LossRate = e.LossRate,
        MachineCount = e.MachineCount,
        LeadTime = e.LeadTime,
        ProcessNote1 = e.ProcessNote1,
        ProcessNote2 = e.ProcessNote2,
        StorageDest = e.StorageDest,
        SortOrder = e.SortOrder,
        ManufOrderPrios = new[]
        {
            e.ManufOrderPrio1, e.ManufOrderPrio2, e.ManufOrderPrio3, e.ManufOrderPrio4,
            e.ManufOrderPrio5, e.ManufOrderPrio6, e.ManufOrderPrio7, e.ManufOrderPrio8,
        },
    };

    private static ProductMaterialDto MapToMaterialDto(ProductMaterial e) => new()
    {
        ProcessCd = e.ProcessCd,
        MaterialCd = e.MaterialCd,
        MaterialTypeDiv = e.MaterialTypeDiv,
        ItemCd = e.ItemCd,
        Branch1 = e.Branch1,
        Branch2 = e.Branch2,
        Branch3 = e.Branch3,
        SupplyDiv = e.SupplyDiv,
        SupplyPrice = e.SupplyPrice,
        SortOrder = e.SortOrder,
    };

    private static ProductLotPriceDto MapToLotPriceDto(ProductLotPrice e) => new()
    {
        DetailNo = e.DetailNo,
        ItemCd = e.ItemCd,
        Branch1 = e.Branch1,
        Branch2 = e.Branch2,
        Branch3 = e.Branch3,
        CurrentPriceDate = e.CurrentPriceDate,
        NewPriceDate = e.NewPriceDate,
        CurrentBranchDate = e.CurrentBranchDate,
        NewBranchDate = e.NewBranchDate,
        LotQty = e.LotQty,
        CurrentSetPrice = e.CurrentSetPrice,
        NewSetPrice = e.NewSetPrice,
        CurrentUnitPrice = e.CurrentUnitPrice,
        NewUnitPrice = e.NewUnitPrice,
        CurrentBranchPrice = e.CurrentBranchPrice,
        NewBranchPrice = e.NewBranchPrice,
        PurchasePrice = e.PurchasePrice,
        PriceApplyBasis = e.PriceApplyBasis,
    };

    private static ProductCoProductDto MapToCoProductDto(ProductCoProduct e) => new()
    {
        ProcessCd = e.ProcessCd,
        RowNo = e.RowNo,
        CoProductName = e.CoProductName,
        QtyRatio = e.QtyRatio,
        NextProcessCd = e.NextProcessCd,
    };
}
