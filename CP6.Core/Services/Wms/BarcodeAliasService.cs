using System.Globalization;
using System.Security.Cryptography;
using ClosedXML.Excel;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Core.Services.Wms;

public sealed class BarcodeAliasService : IBarcodeAliasService
{
    private static readonly HashSet<string> AllowedTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            BarcodeTargetType.Product,
            BarcodeTargetType.Lot,
            BarcodeTargetType.Location,
            BarcodeTargetType.Package,
            BarcodeTargetType.Serial,
            BarcodeTargetType.Lpn
        };

    private readonly CP6Context _db;

    public BarcodeAliasService(CP6Context db) => _db = db;

    public async Task<PagedResult<BarcodeAliasDto>> GetAsync(
        string? search,
        string? barcodeType,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var query = _db.BarcodeAliases.AsNoTracking().Where(x => !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var value = search.Trim();
            query = query.Where(x => x.Barcode.Contains(value)
                                     || x.TargetKey.Contains(value)
                                     || (x.ProductCd != null && x.ProductCd.Contains(value)));
        }
        if (!string.IsNullOrWhiteSpace(barcodeType))
        {
            var type = barcodeType.Trim().ToUpperInvariant();
            query = query.Where(x => x.BarcodeType == type);
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.Barcode)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        return new PagedResult<BarcodeAliasDto>
        {
            Items = rows.Select(Map).ToList(),
            Total = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<BarcodeAliasDto> UpsertAsync(
        UpsertBarcodeAliasRequest request,
        string? userName,
        CancellationToken ct = default)
    {
        Validate(request);
        var barcode = request.Barcode.Trim();
        var row = await _db.BarcodeAliases.FirstOrDefaultAsync(
            x => x.Barcode == barcode && !x.IsDeleted, ct);
        if (row is null)
        {
            row = new BarcodeAlias { Barcode = barcode, Creator = userName };
            _db.BarcodeAliases.Add(row);
        }
        else
        {
            ApplyRowVersion(row, request.RowVersion);
            row.Modifier = userName;
            row.ModifyDate = DateTime.Now;
        }
        Apply(row, request);
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            throw new MobileTaskConflictException("WM-CONFLICT-ROW-VERSION");
        }
        return Map(row);
    }

    public async Task<BarcodeImportResult> ImportAsync(
        Stream workbook,
        bool commit,
        string? userName,
        CancellationToken ct = default)
    {
        using var book = new XLWorkbook(workbook);
        var sheet = book.Worksheets.FirstOrDefault()
            ?? throw new ArgumentException("WM-BARCODE-IMPORT-SHEET");
        var last = sheet.LastRowUsed()?.RowNumber() ?? 1;
        var parsed = new List<(int row, UpsertBarcodeAliasRequest? request, string? error)>();
        var duplicates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var number = 2; number <= last; number++)
        {
            ct.ThrowIfCancellationRequested();
            var barcode = sheet.Cell(number, 1).GetString().Trim();
            if (string.IsNullOrWhiteSpace(barcode)) continue;
            var request = new UpsertBarcodeAliasRequest
            {
                Barcode = barcode,
                BarcodeType = sheet.Cell(number, 2).GetString().Trim(),
                TargetKey = sheet.Cell(number, 3).GetString().Trim(),
                ProductCd = NullIfWhiteSpace(sheet.Cell(number, 4).GetString()),
                LotNo = NullIfWhiteSpace(sheet.Cell(number, 5).GetString()),
                LocationCd = NullIfWhiteSpace(sheet.Cell(number, 6).GetString()),
                PackageUnitCd = NullIfWhiteSpace(sheet.Cell(number, 7).GetString()),
                ConversionRate = ParseDecimal(sheet.Cell(number, 8).GetFormattedString(), 1m),
                ValidFrom = ParseDate(sheet.Cell(number, 9)),
                ValidUntil = ParseDate(sheet.Cell(number, 10)),
                IsEnabled = ParseBool(sheet.Cell(number, 11).GetFormattedString(), true)
            };
            string? error = null;
            if (!duplicates.Add(barcode)) error = "WM-BARCODE-IMPORT-DUPLICATE";
            try { Validate(request); }
            catch (ArgumentException ex) { error ??= ex.Message; }
            parsed.Add((number, request, error));
        }

        if (commit && parsed.All(x => x.error is null))
        {
            IDbContextTransaction? tx = _db.Database.IsRelational()
                ? await _db.Database.BeginTransactionAsync(ct)
                : null;
            try
            {
                foreach (var item in parsed)
                    await UpsertAsync(item.request!, userName, ct);
                if (tx is not null) await tx.CommitAsync(ct);
            }
            catch
            {
                if (tx is not null) await tx.RollbackAsync(ct);
                throw;
            }
            finally
            {
                if (tx is not null) await tx.DisposeAsync();
            }
        }

        var rows = parsed.Select(item => new BarcodeImportRow
        {
            RowNumber = item.row,
            Valid = item.error is null,
            ErrorCode = item.error,
            Item = item.error is null
                ? MapPreview(item.request!)
                : null
        }).ToList();
        return new BarcodeImportResult
        {
            Committed = commit && rows.All(x => x.Valid),
            ValidCount = rows.Count(x => x.Valid),
            InvalidCount = rows.Count(x => !x.Valid),
            Rows = rows
        };
    }

    private static void Validate(UpsertBarcodeAliasRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Barcode)
            || request.Barcode.Length > 256
            || string.IsNullOrWhiteSpace(request.TargetKey)
            || request.TargetKey.Length > 128)
            throw new ArgumentException("WM-BARCODE-DATA-REQUIRED");
        request.BarcodeType = request.BarcodeType.Trim().ToUpperInvariant();
        if (!AllowedTypes.Contains(request.BarcodeType))
            throw new ArgumentException("WM-BARCODE-TYPE-INVALID");
        if (request.ConversionRate <= 0m)
            throw new ArgumentException("WM-BARCODE-CONVERSION-INVALID");
        if (request.ValidUntil.HasValue && request.ValidFrom.HasValue
            && request.ValidUntil < request.ValidFrom)
            throw new ArgumentException("WM-BARCODE-VALIDITY-INVALID");
        if (request.BarcodeType == BarcodeTargetType.Package
            && string.IsNullOrWhiteSpace(request.ProductCd))
            throw new ArgumentException("WM-BARCODE-PACKAGE-PRODUCT-REQUIRED");
    }

    private static void Apply(BarcodeAlias row, UpsertBarcodeAliasRequest request)
    {
        row.BarcodeType = request.BarcodeType.Trim().ToUpperInvariant();
        row.TargetKey = request.TargetKey.Trim();
        row.ProductCd = NullIfWhiteSpace(request.ProductCd);
        row.LotNo = NullIfWhiteSpace(request.LotNo);
        row.LocationCd = NullIfWhiteSpace(request.LocationCd);
        row.PackageUnitCd = NullIfWhiteSpace(request.PackageUnitCd);
        row.ConversionRate = request.ConversionRate;
        row.ValidFrom = request.ValidFrom;
        row.ValidUntil = request.ValidUntil;
        row.IsEnabled = request.IsEnabled;
    }

    private void ApplyRowVersion(BarcodeAlias row, string? encoded)
    {
        var current = row.RowVersion ?? Array.Empty<byte>();
        if (current.Length == 0) return;
        byte[] supplied;
        try { supplied = Convert.FromBase64String(encoded ?? string.Empty); }
        catch (FormatException)
        {
            throw new MobileTaskConflictException("WM-CONFLICT-ROW-VERSION");
        }
        if (!CryptographicOperations.FixedTimeEquals(current, supplied))
            throw new MobileTaskConflictException("WM-CONFLICT-ROW-VERSION");
        _db.Entry(row).Property(x => x.RowVersion).OriginalValue = supplied;
    }

    private static BarcodeAliasDto Map(BarcodeAlias x) => new()
    {
        Id = x.Id,
        Barcode = x.Barcode,
        BarcodeType = x.BarcodeType,
        TargetKey = x.TargetKey,
        ProductCd = x.ProductCd,
        LotNo = x.LotNo,
        LocationCd = x.LocationCd,
        PackageUnitCd = x.PackageUnitCd,
        ConversionRate = x.ConversionRate,
        ValidFrom = x.ValidFrom,
        ValidUntil = x.ValidUntil,
        IsEnabled = x.IsEnabled,
        RowVersion = x.RowVersion is { Length: > 0 }
            ? Convert.ToBase64String(x.RowVersion)
            : string.Empty
    };

    private static BarcodeAliasDto MapPreview(UpsertBarcodeAliasRequest x) => new()
    {
        Barcode = x.Barcode,
        BarcodeType = x.BarcodeType,
        TargetKey = x.TargetKey,
        ProductCd = x.ProductCd,
        LotNo = x.LotNo,
        LocationCd = x.LocationCd,
        PackageUnitCd = x.PackageUnitCd,
        ConversionRate = x.ConversionRate,
        ValidFrom = x.ValidFrom,
        ValidUntil = x.ValidUntil,
        IsEnabled = x.IsEnabled
    };

    private static DateTime? ParseDate(IXLCell cell)
    {
        if (cell.IsEmpty()) return null;
        if (cell.TryGetValue<DateTime>(out var value)) return value;
        return DateTime.TryParse(cell.GetFormattedString(),
            CultureInfo.InvariantCulture, DateTimeStyles.None, out value)
            ? value
            : null;
    }

    private static decimal ParseDecimal(string value, decimal fallback)
        => decimal.TryParse(value, NumberStyles.Number,
            CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    private static bool ParseBool(string value, bool fallback)
        => string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Trim().ToUpperInvariant() is "TRUE" or "1" or "YES" or "Y";

    private static string? NullIfWhiteSpace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
