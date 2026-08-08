using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public sealed class BarcodeProfileService : IBarcodeProfileService
{
    private readonly CP6Context _db;
    public BarcodeProfileService(CP6Context db) => _db = db;

    public async Task<IReadOnlyList<BarcodeProfileDto>> GetAsync(
        CancellationToken ct = default)
        => (await _db.BarcodeProfiles.AsNoTracking()
                .Where(x => !x.IsDeleted)
                .OrderBy(x => x.Priority)
                .ThenBy(x => x.ProfileName)
                .ToListAsync(ct))
            .Select(Map).ToList();

    public async Task<BarcodeProfileDto> UpsertAsync(
        UpsertBarcodeProfileRequest request,
        string? userName,
        CancellationToken ct = default)
    {
        Validate(request);
        BarcodeProfile? row = null;
        if (request.Id.HasValue)
            row = await _db.BarcodeProfiles.FirstOrDefaultAsync(
                x => x.Id == request.Id && !x.IsDeleted, ct);
        row ??= await _db.BarcodeProfiles.FirstOrDefaultAsync(
            x => x.ProfileName == request.ProfileName && !x.IsDeleted, ct);
        if (row is null)
        {
            row = new BarcodeProfile { Creator = userName };
            _db.BarcodeProfiles.Add(row);
        }
        else
        {
            ApplyRowVersion(row, request.RowVersion);
            row.Modifier = userName;
            row.ModifyDate = DateTime.Now;
        }
        row.ProfileName = request.ProfileName.Trim();
        row.Format = request.Format.Trim().ToUpperInvariant();
        row.Pattern = request.Pattern;
        row.MappingJson = request.MappingJson;
        row.Priority = request.Priority;
        row.IsEnabled = request.IsEnabled;
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            throw new MobileTaskConflictException("WM-CONFLICT-ROW-VERSION");
        }
        return Map(row);
    }

    public async Task<CompoundBarcodeResult> ParseAsync(
        ParseCompoundBarcodeRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.RawBarcode)
            || request.RawBarcode.Length > 2048)
            throw new ArgumentException("WM-BARCODE-COMPOUND-DATA");
        var profiles = await _db.BarcodeProfiles.AsNoTracking()
            .Where(x => !x.IsDeleted && x.IsEnabled)
            .OrderBy(x => x.Priority).ToListAsync(ct);
        foreach (var profile in profiles)
        {
            var regex = new Regex(profile.Pattern,
                RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
                TimeSpan.FromMilliseconds(100));
            var match = regex.Match(request.RawBarcode);
            if (!match.Success) continue;
            var mapping = JsonSerializer.Deserialize<Dictionary<string, string>>(
                              profile.MappingJson)
                          ?? new Dictionary<string, string>();
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (output, groupName) in mapping)
            {
                var group = match.Groups[groupName];
                if (group.Success && !string.IsNullOrEmpty(group.Value))
                    values[output] = group.Value;
            }
            return new CompoundBarcodeResult
            {
                Matched = true,
                ProfileName = profile.ProfileName,
                RawBarcode = request.RawBarcode,
                Values = values
            };
        }
        return new CompoundBarcodeResult
        {
            Matched = false,
            RawBarcode = request.RawBarcode
        };
    }

    private static void Validate(UpsertBarcodeProfileRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ProfileName)
            || string.IsNullOrWhiteSpace(request.Pattern)
            || request.Pattern.Length > 1000)
            throw new ArgumentException("WM-BARCODE-PROFILE-DATA");
        request.Format = request.Format.Trim().ToUpperInvariant();
        if (request.Format is not ("GS1" or "CUSTOM"))
            throw new ArgumentException("WM-BARCODE-PROFILE-FORMAT");
        try
        {
            _ = new Regex(request.Pattern,
                RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
                TimeSpan.FromMilliseconds(100));
            var mapping = JsonSerializer.Deserialize<Dictionary<string, string>>(
                request.MappingJson);
            if (mapping is null || mapping.Count == 0)
                throw new JsonException();
        }
        catch (Exception ex) when (ex is ArgumentException or JsonException)
        {
            throw new ArgumentException("WM-BARCODE-PROFILE-SYNTAX");
        }
    }

    private void ApplyRowVersion(BarcodeProfile row, string? encoded)
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

    private static BarcodeProfileDto Map(BarcodeProfile x) => new()
    {
        Id = x.Id,
        ProfileName = x.ProfileName,
        Format = x.Format,
        Pattern = x.Pattern,
        MappingJson = x.MappingJson,
        Priority = x.Priority,
        IsEnabled = x.IsEnabled,
        RowVersion = x.RowVersion is { Length: > 0 }
            ? Convert.ToBase64String(x.RowVersion)
            : string.Empty
    };
}
