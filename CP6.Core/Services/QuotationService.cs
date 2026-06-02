using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels;
using CP6.Entity.DTOs;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services;

/// <summary>
/// MSBBPA030/040 - 御見積書 業務服務
/// </summary>
/// <remarks>
/// - 软删除：IsDeleted 标记，不物理删除
/// - 乐观锁：依赖 EF RowVersion；冲突抛 DbUpdateConcurrencyException
/// - 采番：QTN + 年(4) + 月(2) + 自増(4) = 13桁 + 枝番 "-01"（永不重置；DocNumber 経由）
/// - 確定登録済的記录拒绝訂正/削除（MSG-004）
/// </remarks>
public class QuotationService : IQuotationService
{
    private readonly CP6Context _db;

    /// <summary>見積計算書决定状态：QtnDiv = "20"</summary>
    private const string QtnDivDecided = "20";

    /// <summary>一覧排序白名单：前端列 prop → 实体属性（仅主表字段，明细派生列不支持）</summary>
    private static readonly IReadOnlyDictionary<string, string> QuotationSortMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["qtnNo"] = nameof(Quotation.QtnNo),
            ["qtnIssueDate"] = nameof(Quotation.QtnIssueDate),
            ["baseCd"] = nameof(Quotation.BaseCd),
            ["staffCd"] = nameof(Quotation.StaffCd),
            ["customerCd"] = nameof(Quotation.CustomerCd),
            ["customerName"] = nameof(Quotation.CustomerName),
            ["projectNoParent"] = nameof(Quotation.ProjectNoParent),
            ["projectNoChild"] = nameof(Quotation.ProjectNoChild),
            ["totalAmount"] = nameof(Quotation.TotalAmount),
        };

    public QuotationService(CP6Context db)
    {
        _db = db;
    }

    // ═══════════════════════════════════════════════════════════
    //  分页一覧（MSBBPA040）
    // ═══════════════════════════════════════════════════════════

    public async Task<(List<QuotationListItem>, int)> GetPageListAsync(QuotationQuery query)
    {
        var q = _db.Quotations.AsNoTracking().Where(x => !x.IsDeleted);

        if (!string.IsNullOrWhiteSpace(query.BaseCd))
            q = q.Where(x => x.BaseCd == query.BaseCd);
        if (!string.IsNullOrWhiteSpace(query.StaffCd))
            q = q.Where(x => x.StaffCd == query.StaffCd);
        if (query.IssueDateFrom.HasValue)
            q = q.Where(x => x.QtnIssueDate >= query.IssueDateFrom.Value);
        if (query.IssueDateTo.HasValue)
            q = q.Where(x => x.QtnIssueDate <= query.IssueDateTo.Value);
        if (!string.IsNullOrWhiteSpace(query.QtnNoFrom))
            q = q.Where(x => string.Compare(x.QtnNo, query.QtnNoFrom) >= 0);
        if (!string.IsNullOrWhiteSpace(query.QtnNoTo))
            q = q.Where(x => string.Compare(x.QtnNo, query.QtnNoTo) <= 0);
        if (!string.IsNullOrWhiteSpace(query.CustomerCd))
            q = q.Where(x => x.CustomerCd == query.CustomerCd);
        if (!string.IsNullOrWhiteSpace(query.ProjectNoParent))
            q = q.Where(x => x.ProjectNoParent == query.ProjectNoParent);
        if (!string.IsNullOrWhiteSpace(query.ProjectNoChild))
            q = q.Where(x => x.ProjectNoChild == query.ProjectNoChild);
        if (!string.IsNullOrWhiteSpace(query.ProjectNoMaterial))
            q = q.Where(x => x.ProjectNoMaterial == query.ProjectNoMaterial);

        // ステータス 组合过滤：0=未承認、9=承認済、C=見積確定済（MasterConfirmFlg==9）
        if (query.Statuses != null && query.Statuses.Count > 0)
        {
            q = q.Where(x =>
                (query.Statuses.Contains("0") && x.EstimateCheckFlg == 0 && x.MasterConfirmFlg != 9)
                || (query.Statuses.Contains("9") && x.EstimateCheckFlg == 9 && x.MasterConfirmFlg != 9)
                || (query.Statuses.Contains("C") && x.MasterConfirmFlg == 9));
        }

        // 顧客品名 LIKE：JOIN 明細(DetailNo=1)
        var detailFirst = _db.QuotationDetails.AsNoTracking().Where(d => !d.IsDeleted && d.DetailNo == 1);
        if (!string.IsNullOrWhiteSpace(query.CustomerProductName1))
        {
            var p = query.CustomerProductName1;
            q = q.Where(x => detailFirst.Any(d => d.QtnNo == x.QtnNo && d.ItemName1 != null && d.ItemName1.Contains(p)));
        }
        if (!string.IsNullOrWhiteSpace(query.CustomerProductName2))
        {
            var p = query.CustomerProductName2;
            q = q.Where(x => detailFirst.Any(d => d.QtnNo == x.QtnNo && d.ItemName2 != null && d.ItemName2.Contains(p)));
        }

        var total = await q.CountAsync();

        // 先取分页主表记录，再批量拉第 1 行明细做展示
        q = QuerySort.Apply(q, query.SortField, query.SortOrder, QuotationSortMap,
            s => s.OrderBy(x => x.StaffCd).ThenBy(x => x.CustomerCd).ThenBy(x => x.QtnNo));
        var page = await q
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(x => new
            {
                x.QtnNo,
                x.QtnIssueDate,
                x.BaseCd,
                x.StaffCd,
                x.CustomerCd,
                x.CustomerName,
                x.ProjectNoParent,
                x.ProjectNoChild,
                x.ProjectNoMaterial,
                x.TotalAmount,
                x.EstimateCheckFlg,
                x.MasterConfirmFlg,
            })
            .ToListAsync();

        var qtnNos = page.Select(p => p.QtnNo).ToList();
        var firstDetails = await _db.QuotationDetails.AsNoTracking()
            .Where(d => !d.IsDeleted && d.DetailNo == 1 && qtnNos.Contains(d.QtnNo))
            .ToDictionaryAsync(d => d.QtnNo);

        var staffCds = page.Select(p => p.StaffCd).Distinct().ToList();
        var staffMap = await _db.MasterStaffs.AsNoTracking()
            .Where(s => staffCds.Contains(s.StaffCd))
            .ToDictionaryAsync(s => s.StaffCd, s => s.StaffName);

        var rows = page.Select(p =>
        {
            firstDetails.TryGetValue(p.QtnNo, out var d);
            staffMap.TryGetValue(p.StaffCd ?? "", out var staffName);
            return new QuotationListItem
            {
                QtnNo = p.QtnNo,
                QtnIssueDate = p.QtnIssueDate,
                BaseCd = p.BaseCd,
                StaffCd = p.StaffCd,
                StaffName = staffName,
                CustomerCd = p.CustomerCd,
                CustomerName = p.CustomerName,
                ProjectNoParent = p.ProjectNoParent,
                ProjectNoChild = p.ProjectNoChild,
                ProjectNoMaterial = p.ProjectNoMaterial,
                ItemName1 = d?.ItemName1,
                ItemName2 = d?.ItemName2,
                FirstQuantity = d?.Quantity,
                FirstUnitPrice = d?.UnitPrice,
                FirstAmount = d?.Amount,
                TotalAmount = p.TotalAmount,
                EstimateCheckFlg = p.EstimateCheckFlg,
                MasterConfirmFlg = p.MasterConfirmFlg,
                Status = BuildStatusText(p.EstimateCheckFlg, p.MasterConfirmFlg),
            };
        }).ToList();

        return (rows, total);
    }

    // ═══════════════════════════════════════════════════════════
    //  按 NO 查询详情
    // ═══════════════════════════════════════════════════════════

    public async Task<QuotationDto?> GetByNoAsync(string qtnNo, bool includeDeleted = false)
    {
        var q = _db.Quotations.AsNoTracking()
            .Include(x => x.Calcs.Where(c => !c.IsDeleted))
            .Include(x => x.Details.Where(d => !d.IsDeleted))
            .AsQueryable();
        if (!includeDeleted) q = q.Where(x => !x.IsDeleted);

        var entity = await q.FirstOrDefaultAsync(x => x.QtnNo == qtnNo);
        if (entity == null) return null;

        // 从 EstimateCalc 补充 Calc 展示字段
        var calcNos = entity.Calcs.Select(c => c.QtnCalcNo).ToList();
        var calcMap = await _db.EstimateCalcs.AsNoTracking()
            .Where(e => calcNos.Contains(e.QtnCalcNo))
            .Select(e => new CalcSnapshot(
                e.QtnCalcNo, e.QtnDate,
                e.CustomerProductName1, e.CustomerProductName2,
                e.DecidedQty, e.ConfirmedUnitPrice, e.Unit, e.QtnDiv))
            .ToDictionaryAsync(e => e.QtnCalcNo);

        return ToDto(entity, calcMap);
    }

    // ═══════════════════════════════════════════════════════════
    //  新建（登録）
    // ═══════════════════════════════════════════════════════════

    public async Task<string> CreateAsync(QuotationDto dto, string? userName)
    {
        // 采番：機能コード(QTN)+年(4)+月(2)+自増(4)=13桁；訂正/コピー用に枝番 -01 を付与
        var (mainNo, nextMain) = await DocNumber.NextAsync(_db, "QTN");
        var qtnNo = $"{mainNo}-01";

        var entity = new Quotation
        {
            QtnNo = qtnNo,
            QtnNoMain = nextMain,
            QtnNoBranch = 1,
            Creator = userName,
            CreateDate = DateTime.Now,
        };
        ApplyDto(entity, dto);

        // 関連計算書：全部新增
        foreach (var c in dto.Calcs)
        {
            entity.Calcs.Add(new QuotationCalc
            {
                QtnNo = qtnNo,
                QtnCalcNo = c.QtnCalcNo,
                EstimateCheckFlg = c.EstimateCheckFlg,
                EstimateCheckDate = c.EstimateCheckDate,
                MasterConfirmFlg = c.MasterConfirmFlg,
                MasterConfirmDate = c.MasterConfirmDate,
                Creator = userName,
                CreateDate = DateTime.Now,
            });
        }

        // 打印用明细：全部新增，合計金額 = 各行金額之和
        int detailSeq = 1;
        decimal total = 0m;
        foreach (var d in dto.Details.OrderBy(d => d.DetailNo))
        {
            var amount = (d.Quantity ?? 0) * (d.UnitPrice ?? 0);
            d.Amount = amount;
            entity.Details.Add(new QuotationDetail
            {
                QtnNo = qtnNo,
                DetailNo = detailSeq++,
                ItemName1 = d.ItemName1,
                ItemName2 = d.ItemName2,
                Quantity = d.Quantity,
                UnitPrice = d.UnitPrice,
                Unit = d.Unit,
                Amount = amount,
                PrintTotalFlg = d.PrintTotalFlg,
                QtnCalcNo = d.QtnCalcNo,
                Creator = userName,
                CreateDate = DateTime.Now,
            });
            total += amount;
        }
        entity.TotalAmount = total;

        _db.Quotations.Add(entity);
        await _db.SaveChangesAsync();
        return qtnNo;
    }

    // ═══════════════════════════════════════════════════════════
    //  修改（訂正）
    // ═══════════════════════════════════════════════════════════

    public async Task UpdateAsync(string qtnNo, QuotationDto dto, string? userName)
    {
        var entity = await _db.Quotations
            .Include(x => x.Calcs)
            .Include(x => x.Details)
            .FirstOrDefaultAsync(x => x.QtnNo == qtnNo && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"御見積書NO 不存在或已删除: {qtnNo}");

        // 確定登録済チェック（MSG-004）
        if (entity.MasterConfirmFlg != 0)
            throw new InvalidOperationException("確定登録済のデータとなります。編集する場合は、確定取消を実施ください。");

        // 乐观锁
        if (dto.RowVersion != null && dto.RowVersion.Length > 0)
        {
            _db.Entry(entity).Property(x => x.RowVersion).OriginalValue = dto.RowVersion;
        }

        ApplyDto(entity, dto);
        entity.Modifier = userName;
        entity.ModifyDate = DateTime.Now;

        // 関連計算書 diff：按 QtnCalcNo
        var incomingCalcs = dto.Calcs.ToDictionary(c => c.QtnCalcNo);
        var existingCalcs = entity.Calcs.Where(c => !c.IsDeleted).ToDictionary(c => c.QtnCalcNo);
        foreach (var (no, old) in existingCalcs)
        {
            if (!incomingCalcs.ContainsKey(no)) old.IsDeleted = true;
        }
        foreach (var (no, c) in incomingCalcs)
        {
            if (existingCalcs.TryGetValue(no, out var cur))
            {
                cur.EstimateCheckFlg = c.EstimateCheckFlg;
                cur.EstimateCheckDate = c.EstimateCheckDate;
                cur.MasterConfirmFlg = c.MasterConfirmFlg;
                cur.MasterConfirmDate = c.MasterConfirmDate;
                cur.IsDeleted = false;
                cur.Modifier = userName;
                cur.ModifyDate = DateTime.Now;
            }
            else
            {
                entity.Calcs.Add(new QuotationCalc
                {
                    QtnNo = qtnNo,
                    QtnCalcNo = c.QtnCalcNo,
                    EstimateCheckFlg = c.EstimateCheckFlg,
                    EstimateCheckDate = c.EstimateCheckDate,
                    MasterConfirmFlg = c.MasterConfirmFlg,
                    MasterConfirmDate = c.MasterConfirmDate,
                    Creator = userName,
                    CreateDate = DateTime.Now,
                });
            }
        }

        // 打印用明细 diff：按 DetailNo，同时重算 合計金額
        var incomingDetails = dto.Details.ToDictionary(d => d.DetailNo);
        var existingDetails = entity.Details.Where(d => !d.IsDeleted).ToDictionary(d => d.DetailNo);
        foreach (var (no, old) in existingDetails)
        {
            if (!incomingDetails.ContainsKey(no)) old.IsDeleted = true;
        }
        decimal total = 0m;
        foreach (var (no, d) in incomingDetails)
        {
            var amount = (d.Quantity ?? 0) * (d.UnitPrice ?? 0);
            if (existingDetails.TryGetValue(no, out var cur))
            {
                cur.ItemName1 = d.ItemName1;
                cur.ItemName2 = d.ItemName2;
                cur.Quantity = d.Quantity;
                cur.UnitPrice = d.UnitPrice;
                cur.Unit = d.Unit;
                cur.Amount = amount;
                cur.PrintTotalFlg = d.PrintTotalFlg;
                cur.QtnCalcNo = d.QtnCalcNo;
                cur.IsDeleted = false;
                cur.Modifier = userName;
                cur.ModifyDate = DateTime.Now;
            }
            else
            {
                entity.Details.Add(new QuotationDetail
                {
                    QtnNo = qtnNo,
                    DetailNo = no,
                    ItemName1 = d.ItemName1,
                    ItemName2 = d.ItemName2,
                    Quantity = d.Quantity,
                    UnitPrice = d.UnitPrice,
                    Unit = d.Unit,
                    Amount = amount,
                    PrintTotalFlg = d.PrintTotalFlg,
                    QtnCalcNo = d.QtnCalcNo,
                    Creator = userName,
                    CreateDate = DateTime.Now,
                });
            }
            total += amount;
        }
        entity.TotalAmount = total;

        await _db.SaveChangesAsync();
    }

    // ═══════════════════════════════════════════════════════════
    //  删除（削除）
    // ═══════════════════════════════════════════════════════════

    public async Task DeleteAsync(string qtnNo, byte[]? rowVersion, string? userName)
    {
        var entity = await _db.Quotations
            .FirstOrDefaultAsync(x => x.QtnNo == qtnNo && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"御見積書NO 不存在或已删除: {qtnNo}");

        if (entity.MasterConfirmFlg != 0)
            throw new InvalidOperationException("確定登録済のデータとなります。編集する場合は、確定取消を実施ください。");

        if (rowVersion != null && rowVersion.Length > 0)
        {
            _db.Entry(entity).Property(x => x.RowVersion).OriginalValue = rowVersion;
        }

        entity.IsDeleted = true;
        entity.Modifier = userName;
        entity.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    // ═══════════════════════════════════════════════════════════
    //  复制新建（コピー）
    // ═══════════════════════════════════════════════════════════

    public async Task<string> CopyAsync(string sourceQtnNo, string? userName)
    {
        var source = await _db.Quotations.AsNoTracking()
            .Include(x => x.Calcs.Where(c => !c.IsDeleted))
            .Include(x => x.Details.Where(d => !d.IsDeleted))
            .FirstOrDefaultAsync(x => x.QtnNo == sourceQtnNo && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"源御見積書不存在: {sourceQtnNo}");

        var (mainNo, nextMain) = await DocNumber.NextAsync(_db, "QTN");
        var newNo = $"{mainNo}-01";

        var clone = new Quotation
        {
            QtnNo = newNo,
            QtnNoMain = nextMain,
            QtnNoBranch = 1,
            RefQtnNo = source.QtnNo,
            BaseCd = source.BaseCd,
            StaffCd = source.StaffCd,
            CustomerCd = source.CustomerCd,
            CustomerName = source.CustomerName,
            ProjectNoParent = source.ProjectNoParent,
            ProjectNoChild = source.ProjectNoChild,
            ProjectNoMaterial = source.ProjectNoMaterial,
            FscMgmtNo = null,
            FscChecklistDate = null,
            ContactPerson = source.ContactPerson,
            DeliveryLocation = source.DeliveryLocation,
            DeliveryDeadline = source.DeliveryDeadline,
            Freight = source.Freight,
            PaymentCondition = source.PaymentCondition,
            ValidityPeriod = source.ValidityPeriod,
            QtnNote01 = source.QtnNote01, QtnNote02 = source.QtnNote02, QtnNote03 = source.QtnNote03,
            QtnNote04 = source.QtnNote04, QtnNote05 = source.QtnNote05, QtnNote06 = source.QtnNote06,
            QtnNote07 = source.QtnNote07, QtnNote08 = source.QtnNote08, QtnNote09 = source.QtnNote09,
            QtnNote10 = source.QtnNote10, QtnNote11 = source.QtnNote11, QtnNote12 = source.QtnNote12,
            QtnNote13 = source.QtnNote13, QtnNote14 = source.QtnNote14, QtnNote15 = source.QtnNote15,
            DimensionPrint = source.DimensionPrint,
            CalcNote01 = source.CalcNote01, CalcNote02 = source.CalcNote02, CalcNote03 = source.CalcNote03,
            CalcNote04 = source.CalcNote04, CalcNote05 = source.CalcNote05, CalcNote06 = source.CalcNote06,
            CalcNote07 = source.CalcNote07, CalcNote08 = source.CalcNote08,
            PrintTotalFlg = source.PrintTotalFlg,
            TotalAmount = source.TotalAmount,
            EstimateCheckFlg = 0,
            EstimateCheckDate = null,
            MasterConfirmFlg = 0,
            MasterConfirmDate = null,
            QtnIssueDate = null,
            CalcIssueDate = null,
            Creator = userName,
            CreateDate = DateTime.Now,
        };

        foreach (var c in source.Calcs)
        {
            clone.Calcs.Add(new QuotationCalc
            {
                QtnNo = newNo,
                QtnCalcNo = c.QtnCalcNo,
                EstimateCheckFlg = 0,
                EstimateCheckDate = null,
                MasterConfirmFlg = 0,
                MasterConfirmDate = null,
                Creator = userName,
                CreateDate = DateTime.Now,
            });
        }

        foreach (var d in source.Details)
        {
            clone.Details.Add(new QuotationDetail
            {
                QtnNo = newNo,
                DetailNo = d.DetailNo,
                ItemName1 = d.ItemName1,
                ItemName2 = d.ItemName2,
                Quantity = d.Quantity,
                UnitPrice = d.UnitPrice,
                Unit = d.Unit,
                Amount = d.Amount,
                PrintTotalFlg = d.PrintTotalFlg,
                QtnCalcNo = d.QtnCalcNo,
                Creator = userName,
                CreateDate = DateTime.Now,
            });
        }

        _db.Quotations.Add(clone);
        await _db.SaveChangesAsync();
        return newNo;
    }

    // ═══════════════════════════════════════════════════════════
    //  確定登録 / 確定取消
    // ═══════════════════════════════════════════════════════════

    public async Task ConfirmAsync(string qtnNo, ConfirmRequest req, string? userName)
    {
        if (req.QtnCalcNos == null || req.QtnCalcNos.Count == 0)
            throw new InvalidOperationException("確定登録するデータが1件もありません。");

        var entity = await _db.Quotations
            .Include(x => x.Calcs)
            .FirstOrDefaultAsync(x => x.QtnNo == qtnNo && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"御見積書NO 不存在或已删除: {qtnNo}");

        if (req.RowVersion != null && req.RowVersion.Length > 0)
        {
            _db.Entry(entity).Property(x => x.RowVersion).OriginalValue = req.RowVersion;
        }

        // 校验：所有勾选的 見積計算書 QtnDiv 必须 = 決定（20）
        var targetCalcs = await _db.EstimateCalcs.AsNoTracking()
            .Where(e => req.QtnCalcNos.Contains(e.QtnCalcNo) && !e.IsDeleted)
            .Select(e => new { e.QtnCalcNo, e.QtnDiv })
            .ToListAsync();

        if (targetCalcs.Count == 0)
            throw new InvalidOperationException("確定登録可能のデータが存在しません。");

        if (targetCalcs.Any(c => c.QtnDiv != QtnDivDecided))
            throw new InvalidOperationException("決定見積登録されていないデータが含まれます。");

        var now = DateTime.Now;

        // 更新主表 + 选中行
        entity.MasterConfirmFlg = 9;
        entity.MasterConfirmDate = now;
        entity.Modifier = userName;
        entity.ModifyDate = now;

        foreach (var calc in entity.Calcs.Where(c => !c.IsDeleted))
        {
            if (req.QtnCalcNos.Contains(calc.QtnCalcNo))
            {
                calc.MasterConfirmFlg = 9;
                calc.MasterConfirmDate = now;
                calc.Modifier = userName;
                calc.ModifyDate = now;
            }
        }

        await _db.SaveChangesAsync();
    }

    public async Task CancelConfirmAsync(string qtnNo, byte[]? rowVersion, string? userName)
    {
        var entity = await _db.Quotations
            .Include(x => x.Calcs)
            .FirstOrDefaultAsync(x => x.QtnNo == qtnNo && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"御見積書NO 不存在或已删除: {qtnNo}");

        if (entity.MasterConfirmFlg == 0 && !entity.Calcs.Any(c => !c.IsDeleted && c.MasterConfirmFlg != 0))
            throw new InvalidOperationException("確定取消可能のデータが存在しません。");

        if (rowVersion != null && rowVersion.Length > 0)
        {
            _db.Entry(entity).Property(x => x.RowVersion).OriginalValue = rowVersion;
        }

        var now = DateTime.Now;
        entity.MasterConfirmFlg = 0;
        entity.MasterConfirmDate = null;
        entity.Modifier = userName;
        entity.ModifyDate = now;

        foreach (var calc in entity.Calcs.Where(c => !c.IsDeleted))
        {
            calc.MasterConfirmFlg = 0;
            calc.MasterConfirmDate = null;
            calc.Modifier = userName;
            calc.ModifyDate = now;
        }

        await _db.SaveChangesAsync();
    }

    // ═══════════════════════════════════════════════════════════
    //  発行帳票
    // ═══════════════════════════════════════════════════════════

    public async Task<List<string>> IssueAsync(string qtnNo, IssueRequest req, string? userName)
    {
        var entity = await _db.Quotations
            .FirstOrDefaultAsync(x => x.QtnNo == qtnNo && !x.IsDeleted)
            ?? throw new KeyNotFoundException($"御見積書NO 不存在或已删除: {qtnNo}");

        var now = DateTime.Now;
        var result = new List<string>();

        if (req.IssueQuotation)
        {
            entity.QtnIssueDate = now;
            result.Add($"御見積書_{qtnNo}.pdf");
        }

        if (req.IssueSubmitCalc || req.IssueCalc)
        {
            entity.CalcIssueDate = now;
            if (req.IssueSubmitCalc) result.Add($"提出用見積計算書_{qtnNo}.pdf");
            if (req.IssueCalc) result.Add($"見積計算書_{qtnNo}.pdf");
        }

        entity.Modifier = userName;
        entity.ModifyDate = now;
        await _db.SaveChangesAsync();

        // Phase 5 会替换为真实 PDF 字节流
        return result;
    }

    // ═══════════════════════════════════════════════════════════
    //  関連見積計算書 候选（案件NO联动）
    // ═══════════════════════════════════════════════════════════

    public async Task<List<QuotationCalcCandidate>> GetCalcCandidatesAsync(
        string customerCd, string? projectNoParent, string? projectNoChild, string? projectNoMaterial,
        string? currentQuotationNo)
    {
        var q = _db.EstimateCalcs.AsNoTracking().Where(e => !e.IsDeleted && e.CustomerCd == customerCd);

        if (!string.IsNullOrWhiteSpace(projectNoParent))
            q = q.Where(e => e.ProjectNoParent == projectNoParent);
        if (!string.IsNullOrWhiteSpace(projectNoChild))
            q = q.Where(e => e.ProjectNoChild == projectNoChild);
        if (!string.IsNullOrWhiteSpace(projectNoMaterial))
            q = q.Where(e => e.ProjectNoMaterial == projectNoMaterial);

        var calcs = await q
            .OrderBy(e => e.QtnCalcNo)
            .Select(e => new
            {
                e.QtnCalcNo,
                e.QtnDate,
                e.CustomerProductName1,
                e.CustomerProductName2,
                e.DecidedQty,
                e.ConfirmedUnitPrice,
                e.Unit,
                e.QtnDiv,
            })
            .ToListAsync();

        HashSet<string> linkedSet = new();
        if (!string.IsNullOrWhiteSpace(currentQuotationNo))
        {
            var linked = await _db.QuotationCalcs.AsNoTracking()
                .Where(c => !c.IsDeleted && c.QtnNo == currentQuotationNo)
                .Select(c => c.QtnCalcNo)
                .ToListAsync();
            linkedSet = linked.ToHashSet();
        }

        return calcs.Select(c => new QuotationCalcCandidate
        {
            QtnCalcNo = c.QtnCalcNo,
            QtnCalcDate = c.QtnDate,
            CustomerProductName1 = c.CustomerProductName1,
            CustomerProductName2 = c.CustomerProductName2,
            EstimateQty = c.DecidedQty,
            ConfirmedUnitPrice = c.ConfirmedUnitPrice,
            Unit = c.Unit,
            Amount = (c.DecidedQty ?? 0) * (c.ConfirmedUnitPrice ?? 0),
            QtnDiv = c.QtnDiv,
            IsLinked = linkedSet.Contains(c.QtnCalcNo),
        }).ToList();
    }

    // ═══════════════════════════════════════════════════════════
    //  私有辅助
    // ═══════════════════════════════════════════════════════════

    private static string BuildStatusText(int estCheckFlg, int masterConfirmFlg)
    {
        if (masterConfirmFlg == 9) return "見積確定済";
        if (estCheckFlg == 9) return "承認済";
        return "未承認";
    }

    /// <summary>
    /// 将 DTO 的主表字段拷贝到 Entity（不包括主键、状态、审计字段）
    /// </summary>
    private static void ApplyDto(Quotation e, QuotationDto dto)
    {
        e.BaseCd = dto.BaseCd;
        e.StaffCd = dto.StaffCd;
        e.CustomerCd = dto.CustomerCd;
        e.CustomerName = dto.CustomerName;
        e.ProjectNoParent = dto.ProjectNoParent;
        e.ProjectNoChild = dto.ProjectNoChild;
        e.ProjectNoMaterial = dto.ProjectNoMaterial;
        e.FscMgmtNo = dto.FscMgmtNo;
        e.FscChecklistDate = dto.FscChecklistDate;
        e.ContactPerson = dto.ContactPerson;
        e.DeliveryLocation = dto.DeliveryLocation;
        e.DeliveryDeadline = dto.DeliveryDeadline;
        e.Freight = dto.Freight;
        e.PaymentCondition = dto.PaymentCondition;
        e.ValidityPeriod = dto.ValidityPeriod;

        // 15 行見積書備考
        var qn = dto.QtnNotes;
        e.QtnNote01 = At(qn, 0);  e.QtnNote02 = At(qn, 1);  e.QtnNote03 = At(qn, 2);
        e.QtnNote04 = At(qn, 3);  e.QtnNote05 = At(qn, 4);  e.QtnNote06 = At(qn, 5);
        e.QtnNote07 = At(qn, 6);  e.QtnNote08 = At(qn, 7);  e.QtnNote09 = At(qn, 8);
        e.QtnNote10 = At(qn, 9);  e.QtnNote11 = At(qn, 10); e.QtnNote12 = At(qn, 11);
        e.QtnNote13 = At(qn, 12); e.QtnNote14 = At(qn, 13); e.QtnNote15 = At(qn, 14);

        e.DimensionPrint = dto.DimensionPrint;
        var cn = dto.CalcNotes;
        e.CalcNote01 = At(cn, 0); e.CalcNote02 = At(cn, 1); e.CalcNote03 = At(cn, 2);
        e.CalcNote04 = At(cn, 3); e.CalcNote05 = At(cn, 4); e.CalcNote06 = At(cn, 5);
        e.CalcNote07 = At(cn, 6); e.CalcNote08 = At(cn, 7);

        e.PrintTotalFlg = dto.PrintTotalFlg;
        e.EstimateCheckFlg = dto.EstimateCheckFlg;
        e.EstimateCheckDate = dto.EstimateCheckDate;

        e.Memo1 = dto.Memo1;
        e.Memo2 = dto.Memo2;
        e.Memo3 = dto.Memo3;
    }

    private static string? At(string?[]? arr, int i) => (arr != null && i < arr.Length) ? arr[i] : null;

    /// <summary>
    /// 用于 ToDto 的 EstimateCalc 字段快照（避免 dynamic 绑定）
    /// </summary>
    private record CalcSnapshot(
        string QtnCalcNo,
        DateTime QtnDate,
        string? CustomerProductName1,
        string? CustomerProductName2,
        decimal? DecidedQty,
        decimal? ConfirmedUnitPrice,
        string? Unit,
        string? QtnDiv);

    private static QuotationDto ToDto(Quotation e,
        Dictionary<string, CalcSnapshot>? calcMap = null)
    {
        var dto = new QuotationDto
        {
            QtnNo = e.QtnNo,
            RefQtnNo = e.RefQtnNo,
            BaseCd = e.BaseCd,
            StaffCd = e.StaffCd,
            CustomerCd = e.CustomerCd,
            CustomerName = e.CustomerName,
            ProjectNoParent = e.ProjectNoParent,
            ProjectNoChild = e.ProjectNoChild,
            ProjectNoMaterial = e.ProjectNoMaterial,
            FscMgmtNo = e.FscMgmtNo,
            FscChecklistDate = e.FscChecklistDate,
            ContactPerson = e.ContactPerson,
            DeliveryLocation = e.DeliveryLocation,
            DeliveryDeadline = e.DeliveryDeadline,
            Freight = e.Freight,
            PaymentCondition = e.PaymentCondition,
            ValidityPeriod = e.ValidityPeriod,
            QtnNotes = new[]
            {
                e.QtnNote01, e.QtnNote02, e.QtnNote03, e.QtnNote04, e.QtnNote05,
                e.QtnNote06, e.QtnNote07, e.QtnNote08, e.QtnNote09, e.QtnNote10,
                e.QtnNote11, e.QtnNote12, e.QtnNote13, e.QtnNote14, e.QtnNote15,
            },
            DimensionPrint = e.DimensionPrint,
            CalcNotes = new[]
            {
                e.CalcNote01, e.CalcNote02, e.CalcNote03, e.CalcNote04,
                e.CalcNote05, e.CalcNote06, e.CalcNote07, e.CalcNote08,
            },
            QtnIssueDate = e.QtnIssueDate,
            CalcIssueDate = e.CalcIssueDate,
            TotalAmount = e.TotalAmount,
            PrintTotalFlg = e.PrintTotalFlg,
            EstimateCheckFlg = e.EstimateCheckFlg,
            EstimateCheckDate = e.EstimateCheckDate,
            MasterConfirmFlg = e.MasterConfirmFlg,
            MasterConfirmDate = e.MasterConfirmDate,
            Memo1 = e.Memo1,
            Memo2 = e.Memo2,
            Memo3 = e.Memo3,
            CreateDate = e.CreateDate,
            ModifyDate = e.ModifyDate,
            RowVersion = e.RowVersion,
        };

        foreach (var c in e.Calcs.Where(x => !x.IsDeleted).OrderBy(x => x.QtnCalcNo))
        {
            var item = new QuotationCalcDto
            {
                QtnCalcNo = c.QtnCalcNo,
                EstimateCheckFlg = c.EstimateCheckFlg,
                EstimateCheckDate = c.EstimateCheckDate,
                MasterConfirmFlg = c.MasterConfirmFlg,
                MasterConfirmDate = c.MasterConfirmDate,
            };
            if (calcMap != null && calcMap.TryGetValue(c.QtnCalcNo, out var src))
            {
                item.QtnCalcDate = src.QtnDate;
                item.CustomerProductName1 = src.CustomerProductName1;
                item.CustomerProductName2 = src.CustomerProductName2;
                item.EstimateQty = src.DecidedQty;
                item.ConfirmedUnitPrice = src.ConfirmedUnitPrice;
                item.Unit = src.Unit;
                item.Amount = (src.DecidedQty ?? 0m) * (src.ConfirmedUnitPrice ?? 0m);
                item.QtnDiv = src.QtnDiv;
            }
            dto.Calcs.Add(item);
        }

        foreach (var d in e.Details.Where(x => !x.IsDeleted).OrderBy(x => x.DetailNo))
        {
            dto.Details.Add(new QuotationDetailDto
            {
                DetailNo = d.DetailNo,
                ItemName1 = d.ItemName1,
                ItemName2 = d.ItemName2,
                Quantity = d.Quantity,
                UnitPrice = d.UnitPrice,
                Unit = d.Unit,
                Amount = d.Amount,
                PrintTotalFlg = d.PrintTotalFlg,
                QtnCalcNo = d.QtnCalcNo,
            });
        }

        return dto;
    }
}
