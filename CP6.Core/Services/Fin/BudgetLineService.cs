using ClosedXML.Excel;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Fin;

public class BudgetLineService : IBudgetLineService
{
    private readonly CP6Context _db;
    public BudgetLineService(CP6Context db) { _db = db; }

    public async Task<FinResult> UpsertLineAsync(BudgetLineDto dto)
    {
        var v = await _db.BudgetVersions.FindAsync(dto.VersionId);
        if (v == null) return FinResult.Fail("E-A5-VERSION-404");
        if (v.Status != BudgetVersionStatus.Draft) return FinResult.Fail("E-A5-VERSION-005");

        var acct = await _db.GlAccounts.FindAsync(dto.AccountId);
        if (acct == null || !acct.IsLeaf || (acct.Type != AccountType.Expense && acct.Type != AccountType.Revenue))
            return FinResult.Fail("E-A5-LINE-002");

        if ((dto.CostObjectType == null) != (dto.CostObjectId == null))
            return FinResult.Fail("E-A5-LINE-004");

        var periods = SpreadPeriods(dto);
        var annual = periods.Sum();

        var ccKey = dto.CostCenterId ?? Guid.Empty;
        var coTypeKey = dto.CostObjectType ?? "";
        var coIdKey = dto.CostObjectId ?? "";

        var line = await _db.BudgetLines.FirstOrDefaultAsync(l =>
            l.VersionId == dto.VersionId && l.AccountId == dto.AccountId &&
            l.CostCenterKey == ccKey && l.CostObjectTypeKey == coTypeKey && l.CostObjectIdKey == coIdKey);

        if (line == null)
        {
            line = new BudgetLine
            {
                VersionId = dto.VersionId,
                AccountId = dto.AccountId,
                CostCenterId = dto.CostCenterId,
                CostObjectType = dto.CostObjectType,
                CostObjectId = dto.CostObjectId,
            };
            _db.BudgetLines.Add(line);
        }

        line.CostCenterId = dto.CostCenterId;
        line.CostObjectType = dto.CostObjectType;
        line.CostObjectId = dto.CostObjectId;
        line.NormalizeKeys();
        line.AnnualAmount = annual;
        line.ControlMode = dto.ControlMode;
        line.ControlBasis = dto.ControlBasis;
        line.Memo = dto.Memo;

        await _db.SaveChangesAsync();   // persist line to get Id

        _db.BudgetLinePeriods.RemoveRange(_db.BudgetLinePeriods.Where(p => p.BudgetLineId == line.Id));
        for (int i = 0; i < 12; i++)
            _db.BudgetLinePeriods.Add(new BudgetLinePeriod { BudgetLineId = line.Id, PeriodNo = i + 1, Amount = periods[i] });

        await _db.SaveChangesAsync();
        return FinResult.Pass();
    }

    /// <summary>
    /// 按 SpreadMode 计算 12 期金额。余数进末期（spec §5.3）。
    /// even   : 年金额 ÷ 12，四舍五入到分。
    /// seasonal: 按 Periods 权重分配，权重和为 0 时退化为 even。
    /// manual : 直接使用 Periods，AnnualAmount 取 Sum。
    /// </summary>
    internal static decimal[] SpreadPeriods(BudgetLineDto dto)
    {
        static decimal[] EvenSpread(decimal annual)
        {
            var r = new decimal[12];
            var each = Math.Round(annual / 12m, 2);
            for (int i = 0; i < 12; i++) r[i] = each;
            r[11] += annual - each * 12;
            return r;
        }

        switch (dto.SpreadMode)
        {
            case "manual":
            {
                var r = new decimal[12];
                var src = dto.Periods ?? new decimal[12];
                for (int i = 0; i < 12; i++) r[i] = i < src.Length ? src[i] : 0m;
                return r;
            }
            case "seasonal":
            {
                var w = dto.Periods ?? Enumerable.Repeat(1m, 12).ToArray();
                var wsum = w.Sum();
                if (wsum == 0) return EvenSpread(dto.AnnualAmount);
                var r = new decimal[12];
                decimal acc = 0;
                for (int i = 0; i < 12; i++) { r[i] = Math.Round(dto.AnnualAmount * w[i] / wsum, 2); acc += r[i]; }
                r[11] += dto.AnnualAmount - acc;
                return r;
            }
            default:   // even
                return EvenSpread(dto.AnnualAmount);
        }
    }

    public async Task<FinResult> DeleteLineAsync(Guid lineId)
    {
        var line = await _db.BudgetLines.FindAsync(lineId);
        if (line == null) return FinResult.Fail("E-A5-LINE-404");
        var v = await _db.BudgetVersions.FindAsync(line.VersionId);
        if (v?.Status != BudgetVersionStatus.Draft) return FinResult.Fail("E-A5-VERSION-005");
        _db.BudgetLinePeriods.RemoveRange(_db.BudgetLinePeriods.Where(p => p.BudgetLineId == lineId));
        _db.BudgetLines.Remove(line);
        await _db.SaveChangesAsync();
        return FinResult.Pass();
    }

    public async Task<List<BudgetLineGridRow>> ListLinesAsync(Guid versionId)
    {
        var lines = await _db.BudgetLines.AsNoTracking().Where(l => l.VersionId == versionId).ToListAsync();
        var ids = lines.Select(l => l.Id).ToList();
        var periods = await _db.BudgetLinePeriods.AsNoTracking().Where(p => ids.Contains(p.BudgetLineId)).ToListAsync();
        return lines.Select(l => new BudgetLineGridRow
        {
            Id = l.Id,
            AccountId = l.AccountId,
            CostCenterId = l.CostCenterId,
            CostObjectType = l.CostObjectType,
            CostObjectId = l.CostObjectId,
            AnnualAmount = l.AnnualAmount,
            ControlMode = l.ControlMode,
            ControlBasis = l.ControlBasis,
            Memo = l.Memo,
            Periods = Enumerable.Range(1, 12)
                .Select(n => periods.FirstOrDefault(p => p.BudgetLineId == l.Id && p.PeriodNo == n)?.Amount ?? 0m)
                .ToArray(),
            RowVersion = l.RowVersion,
        }).ToList();
    }

    // C-3: Excel 导入 — Preview / Confirm
    public async Task<BudgetImportPreviewResult> PreviewImportAsync(Guid versionId, Stream excel)
        => await ParseAndValidateAsync(excel);

    public async Task<FinResult> ConfirmImportAsync(Guid versionId, Stream excel)
    {
        var v = await _db.BudgetVersions.FindAsync(versionId);
        if (v == null) return FinResult.Fail("E-A5-VERSION-404");
        if (v.Status != BudgetVersionStatus.Draft) return FinResult.Fail("E-A5-VERSION-005");
        var preview = await ParseAndValidateAsync(excel);
        if (preview.HasFatal) return FinResult.Fail("E-A5-IMPORT-001");
        foreach (var row in preview.Rows)
        {
            var acct = await _db.GlAccounts.FirstAsync(a => a.Code == row.AccountCode);
            Guid? ccId = row.CostCenterCode == null ? null
                : (await _db.CostCenters.FirstOrDefaultAsync(c => c.Code == row.CostCenterCode))?.Id;
            await UpsertLineAsync(new BudgetLineDto {
                VersionId = versionId, AccountId = acct.Id, CostCenterId = ccId,
                CostObjectType = string.IsNullOrEmpty(row.CostObjectType) ? null : row.CostObjectType,
                CostObjectId = string.IsNullOrEmpty(row.CostObjectId) ? null : row.CostObjectId,
                SpreadMode = "manual", Periods = row.Periods, AnnualAmount = row.Periods.Sum(),
            });
        }
        return FinResult.Pass();
    }

    private async Task<BudgetImportPreviewResult> ParseAndValidateAsync(Stream excel)
    {
        var result = new BudgetImportPreviewResult();
        using var wb = new XLWorkbook(excel);
        var ws = wb.Worksheet(1);
        var acctCodes = await _db.GlAccounts.AsNoTracking()
            .Where(a => a.IsLeaf && (a.Type == AccountType.Expense || a.Type == AccountType.Revenue))
            .Select(a => a.Code).ToListAsync();
        var ccCodes = await _db.CostCenters.AsNoTracking().Select(c => c.Code).ToListAsync();
        foreach (var xlRow in ws.RowsUsed().Skip(1))   // skip header
        {
            var row = new BudgetImportRow { RowNo = xlRow.RowNumber(), Ok = true };
            row.AccountCode = xlRow.Cell(1).GetString().Trim();
            row.CostCenterCode = EmptyToNull(xlRow.Cell(2).GetString());
            row.CostObjectType = EmptyToNull(xlRow.Cell(3).GetString());
            row.CostObjectId = EmptyToNull(xlRow.Cell(4).GetString());
            for (int i = 0; i < 12; i++) row.Periods[i] = xlRow.Cell(5 + i).GetValue<decimal>();
            if (!acctCodes.Contains(row.AccountCode)) { row.Ok = false; row.Error = "E-A5-LINE-002"; }
            else if (row.CostCenterCode != null && !ccCodes.Contains(row.CostCenterCode)) { row.Ok = false; row.Error = "E-A5-LINE-005"; }
            else if ((row.CostObjectType == null) != (row.CostObjectId == null)) { row.Ok = false; row.Error = "E-A5-LINE-004"; }
            result.Rows.Add(row);
        }
        return result;
        static string? EmptyToNull(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }
}
