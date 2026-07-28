using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Pub;
using CP6.WebApi.Localization;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Services;

/// <summary>
/// Publishes immutable language packs and their mutable manifest through the
/// configured shared file store. Production therefore has no pod-local state.
/// </summary>
public sealed class LangPublishService
{
    private const string Root = "i18n";
    private const string ManifestPath = Root + "/manifest.json";
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IFileStore _store;
    private readonly IConfiguration _configuration;

    public LangPublishService(
        IServiceScopeFactory scopeFactory,
        IFileStore store,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _store = store;
        _configuration = configuration;
    }

    public async Task<string> PublishAsync(string? publishedBy, DateTime now)
    {
        var version = "v" + now.ToString("yyyyMMddHHmmss");
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CP6Context>();
        var query = context.Sys_Langs.AsNoTracking().Where(x => x.TenantId == null);
        if (_configuration.GetValue<bool>("I18n:ServeReviewedOnly"))
            query = query.Where(x => x.Status == "reviewed");
        var items = await query.ToListAsync();

        foreach (var lang in LangColumn.Codes)
            await SaveJsonAsync(
                $"{Root}/{version}/{lang}.json",
                LangColumn.ToDict(items, lang));

        var manifest = new LangManifest
        {
            Version = version,
            Langs = LangColumn.Codes,
            PublishedAt = now,
            PublishedBy = publishedBy,
            Count = items.Count,
        };
        // Write the pointer last, so readers never observe a partial version.
        await SaveJsonAsync(ManifestPath, manifest);
        return version;
    }

    public async Task<LangManifest?> GetManifestAsync()
    {
        try
        {
            await using var stream = await _store.OpenReadAsync(ManifestPath);
            return await JsonSerializer.DeserializeAsync<LangManifest>(stream);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task<string?> ReadPublishedAsync(string version, string lang)
    {
        if (!IsSafeSegment(version) || !LangColumn.Codes.Contains(lang))
            return null;
        try
        {
            await using var stream =
                await _store.OpenReadAsync($"{Root}/{version}/{lang}.json");
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return await reader.ReadToEndAsync();
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    public async Task<bool> RollbackAsync(
        string version,
        string? operatedBy,
        DateTime now)
    {
        if (!IsSafeSegment(version)
            || !_store.Exists($"{Root}/{version}/{LangColumn.Codes[0]}.json"))
            return false;
        var manifest = await GetManifestAsync() ?? new LangManifest();
        manifest.Version = version;
        manifest.PublishedAt = now;
        manifest.PublishedBy = operatedBy;
        manifest.Langs = LangColumn.Codes;
        await SaveJsonAsync(ManifestPath, manifest);
        return true;
    }

    private async Task SaveJsonAsync<T>(string key, T value)
    {
        await using var content = new MemoryStream(
            JsonSerializer.SerializeToUtf8Bytes(value));
        await _store.SaveAsync(content, key);
    }

    private static bool IsSafeSegment(string value)
        => !string.IsNullOrEmpty(value)
           && value.All(character =>
               char.IsLetterOrDigit(character)
               || character is '-' or '_');
}

public sealed class LangManifest
{
    public string Version { get; set; } = string.Empty;
    public string[] Langs { get; set; } = [];
    public DateTime PublishedAt { get; set; }
    public string? PublishedBy { get; set; }
    public int Count { get; set; }
}
