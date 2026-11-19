using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Pub;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using CP6.WebApi.Localization;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Services;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LanguagePublicationCommand(Guid OperationRef, string? Kind, string? ExpectedRevision,
    string? SourceVersion, string? Version, string? Reason);

public sealed record LanguagePackIdentity(int Length, string Sha256);
public sealed record LanguageReleaseManifest(string Version, string[] Langs, Dictionary<string, LanguagePackIdentity> Packs,
    DateTimeOffset CreatedAtUtc, string SourceVersion, string? PreviousVersion, int Count, string[] Namespaces);
public sealed record LanguageReleaseReceipt(Guid OperationRef, string Kind, string Revision, string Version, DateTimeOffset AppliedAtUtc);

public sealed class LanguagePublicationV1(CP6Context db, IamAdministrationV1 authority, IFileStore store)
{
    private readonly LanguageAuthorityV1 access = new(db, authority);
    private readonly MainOwnerOperationJournalV1 journal = new(db);
    private const string Owner = "Main.Sys.LanguagePublication.v1";
    private const string Contract = "CP6.LanguagePublication.v1";

    private sealed record PointerRow(long Revision, string? Version)
    {
        internal string RevisionText => Revision.ToString(CultureInfo.InvariantCulture);
    }
    private sealed record ApprovedRow(Guid Revision, string LangKey, string? ZhCN, string? ZhTW, string? En, string? Ja, string? Ko);
    private sealed record Source(string Version, string[] Namespaces, Dictionary<string, string>[] Messages, ApprovedRow[] Rows);
    private sealed record Plan(LanguageReleaseReceipt? Original, PointerRow Pointer, Source? Source, LanguageReleaseManifest? Target);

    public Task<object> Preview(HttpContext http) => access.Execute<object>(http, true, "publish", async (_, ct) =>
    {
        var pointer = await Pointer(ct);
        var source = await CurrentSource(ct);
        var versions = await db.Database.SqlQuery<string>($"""
            SELECT version AS "Value" FROM cp6_lang_v1.releases
            """).ToListAsync(ct);
        return new
        {
            revision = pointer.RevisionText, currentVersion = pointer.Version, sourceVersion = source.Version,
            languages = LangColumn.Codes, namespaces = source.Namespaces, count = source.Rows.Length,
            missing = LangColumn.Codes.Select((code, index) => new { code, count = source.Rows.Length - source.Messages[index].Count }),
            versions
        };
    });

    public Task<LanguageReleaseReceipt> Recover(HttpContext http, Guid operation) =>
        access.Execute(http, true, "publish", async (actor, ct) =>
            await ReadOriginal(actor.UserId, operation, null, ct) ?? throw new LanguageProblemV1(404, "LANG_ORIGINAL_NOT_FOUND"));

    public async Task<LanguageReleaseReceipt> Apply(HttpContext http, LanguagePublicationCommand command)
    {
        Validate(command);
        var input = MainOwnerOperationJournalV1.Encode(command);
        var plan = await access.Execute(http, true, "publish", async (actor, ct) =>
        {
            var original = await ReadOriginal(actor.UserId, command.OperationRef, input, ct);
            var pointer = await Pointer(ct);
            if (original is not null) return new Plan(original, pointer, null, null);
            RequireRevision(command, pointer);
            if (command.Kind == "rollback")
            {
                var target = await ReadManifest(command.Version!, ct)
                    ?? throw new LanguageProblemV1(404, "LANG_RELEASE_NOT_FOUND");
                return new Plan(null, pointer, null, target);
            }
            var source = await CurrentSource(ct);
            RequireSource(command, source);
            foreach (var row in source.Rows)
            {
                LanguageEntryRulesV1.ValidateKey(row.LangKey);
                LanguageEntryRulesV1.ValidatePlaceholders([row.ZhCN, row.ZhTW, row.En, row.Ja, row.Ko]);
            }
            return new Plan(null, pointer, source, null);
        });
        if (plan.Original is not null) return plan.Original;

        var ct = http.RequestAborted;
        var manifest = plan.Target ?? await Stage(plan.Source!, plan.Pointer.Version, ct);

        return await access.Execute(http, true, "publish", async (actor, token) =>
        {
            var original = await ReadOriginal(actor.UserId, command.OperationRef, input, token);
            if (original is not null) return original;
            var pointer = await Pointer(token);
            RequireRevision(command, pointer);
            foreach (var language in LangColumn.Codes)
                await Pack(manifest, language, token);
            if (command.Kind == "publish")
            {
                RequireSource(command, await CurrentSource(token));
                var encoded = MainOwnerOperationJournalV1.Encode(manifest);
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO cp6_lang_v1.releases(version, manifest) VALUES ({manifest.Version}, {encoded})
                    """, token);
            }
            var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE cp6_lang_v1.pointer SET version = {manifest.Version}, revision = revision + 1
                WHERE singleton AND revision = {pointer.Revision}
                """, token);
            if (changed != 1) throw new LanguageProblemV1(412, "LANG_POINTER_CHANGED");
            var result = new LanguageReleaseReceipt(command.OperationRef, command.Kind!,
                (pointer.Revision + 1).ToString(CultureInfo.InvariantCulture), manifest.Version, DateTimeOffset.UtcNow);
            await journal.Store(Owner, Contract, command.OperationRef, actor.UserId, input, result, [],
                new { command.Kind, command.Reason, pointer.Version, LanguageVersion = manifest.Version, manifest.SourceVersion }, token);
            return result;
        });
    }

    internal bool IsPostgreSql => db.Database.IsNpgsql();

    internal async Task<LanguageReleaseManifest?> CurrentManifest(CancellationToken ct)
    {
        var pointer = await Pointer(ct);
        return pointer.Version is null ? null : await ReadManifest(pointer.Version, ct);
    }

    internal async Task<LanguageReleaseManifest?> ReadManifest(string version, CancellationToken ct)
    {
        var payload = await db.Database.SqlQuery<byte[]>($"""
            SELECT manifest AS "Value" FROM cp6_lang_v1.releases WHERE version = {version}
            """).SingleOrDefaultAsync(ct);
        return payload is null ? null : JsonSerializer.Deserialize<LanguageReleaseManifest>(payload);
    }

    internal async Task<byte[]> Pack(LanguageReleaseManifest manifest, string language, CancellationToken ct)
    {
        var identity = manifest.Packs[language];
        if (identity.Length is < 2 or > FileContentBytesV1.MaxBytes)
            throw new LanguageProblemV1(409, "LANG_PACK_LENGTH");
        try
        {
            await using var file = await store.OpenReadBoundedAsync(PackPath(manifest.Version, language), identity.Length, ct);
            var bytes = new byte[identity.Length];
            await file.ReadExactlyAsync(bytes, ct);
            if (await file.ReadAsync(new byte[1], ct) != 0 ||
                MainOwnerOperationJournalV1.Digest(bytes) != identity.Sha256)
                throw new LanguageProblemV1(409, "LANG_PACK_INTEGRITY");
            return bytes;
        }
        catch (FileNotFoundException) { throw new LanguageProblemV1(409, "LANG_PACK_MISSING"); }
        catch (IOException) { throw new LanguageProblemV1(409, "LANG_PACK_INTEGRITY"); }
    }

    private Task<PointerRow> Pointer(CancellationToken ct) => db.Database.SqlQuery<PointerRow>($"""
        SELECT revision AS "Revision", version AS "Version" FROM cp6_lang_v1.pointer WHERE singleton
        """).SingleAsync(ct);

    private async Task<Source> CurrentSource(CancellationToken ct)
    {
        var rows = await db.Database.SqlQuery<ApprovedRow>($"""
            SELECT "LanguageRevision" AS "Revision", "LangKey", "ZhCN", "ZhTW", "En", "Ja", "Ko"
            FROM public."Sys_Langs" WHERE "TenantId" IS NULL AND "Status" = 'reviewed'
            """).ToListAsync(ct);
        var ordered = rows.OrderBy(row => row.LangKey, StringComparer.Ordinal).ToArray();
        var items = ordered.Select(row => new Sys_Lang
        {
            LangKey = row.LangKey, ZhCN = row.ZhCN, ZhTW = row.ZhTW, En = row.En, Ja = row.Ja, Ko = row.Ko
        }).ToArray();
        var messages = LangColumn.Codes.Select(language => LangColumn.ToDict(items, language)).ToArray();
        var fingerprint = MainOwnerOperationJournalV1.Digest(MainOwnerOperationJournalV1.Encode(ordered));
        var namespaces = ordered.Select(row => LangColumn.Namespace(row.LangKey))
            .Where(space => space.Length != 0).Append("_core").Distinct(StringComparer.Ordinal).ToArray();
        return new Source(fingerprint, namespaces, messages, ordered);
    }

    private async Task<LanguageReleaseManifest> Stage(Source source, string? previousVersion, CancellationToken ct)
    {
        var version = "v" + Guid.NewGuid().ToString("N");
        var packs = new Dictionary<string, LanguagePackIdentity>();
        for (var i = 0; i < LangColumn.Codes.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            var code = LangColumn.Codes[i];
            var bytes = MainOwnerOperationJournalV1.Encode(source.Messages[i]);
            if (bytes.Length > FileContentBytesV1.MaxBytes) throw new LanguageProblemV1(422, "LANG_PACK_TOO_LARGE");
            var path = PackPath(version, code);
            using var content = new MemoryStream(bytes, writable: false);
            try
            {
                if (await store.SaveAsync(content, path) != path)
                    throw new LanguageProblemV1(503, "LANG_PACK_WRITE_UNAVAILABLE");
            }
            catch (IOException) { throw new LanguageProblemV1(503, "LANG_PACK_WRITE_UNAVAILABLE"); }
            packs.Add(code, new LanguagePackIdentity(bytes.Length, MainOwnerOperationJournalV1.Digest(bytes)));
        }
        return new LanguageReleaseManifest(version, LangColumn.Codes, packs, DateTimeOffset.UtcNow,
            source.Version, previousVersion, source.Rows.Length, source.Namespaces);
    }

    private async Task<LanguageReleaseReceipt?> ReadOriginal(Guid actor, Guid operation, byte[]? input, CancellationToken ct)
    {
        var original = await journal.Read(Owner, operation, ct);
        if (original is null) return null;
        if (original.Actor != actor) throw new LanguageProblemV1(404, "LANG_ORIGINAL_NOT_FOUND");
        if (input is not null && !MainOwnerOperationJournalV1.Matches(original, input))
            throw new LanguageProblemV1(409, "LANG_OPERATION_CONFLICT");
        return JsonSerializer.Deserialize<LanguageReleaseReceipt>(original.Result)
            ?? throw new InvalidOperationException("语言发布回执无法读取。");
    }

    private static void Validate(LanguagePublicationCommand command)
    {
        if (command.OperationRef == Guid.Empty ||
            !long.TryParse(command.ExpectedRevision, NumberStyles.None, CultureInfo.InvariantCulture, out _) ||
            string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Length > 256 || command.Reason.Contains('\0') ||
            (command.Kind == "publish" ? !IsDigest(command.SourceVersion) || command.Version is not null
             : command.Kind != "rollback" || !IsNativeVersion(command.Version) || command.SourceVersion is not null))
            throw new LanguageProblemV1(400, "LANG_RELEASE_COMMAND_REQUIRED");
    }

    private static void RequireRevision(LanguagePublicationCommand command, PointerRow pointer)
    {
        if (command.ExpectedRevision != pointer.RevisionText) throw new LanguageProblemV1(412, "LANG_POINTER_CHANGED");
    }

    private static void RequireSource(LanguagePublicationCommand command, Source source)
    {
        if (command.SourceVersion != source.Version) throw new LanguageProblemV1(412, "LANG_REVIEWED_SOURCE_CHANGED");
    }

    private static bool IsDigest(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigitLower);
    private static bool IsNativeVersion(string? value) =>
        value is { Length: 33 } && value[0] == 'v' && value.Skip(1).All(char.IsAsciiHexDigitLower);
    private static string PackPath(string version, string language) => $"i18n/{version}/{language}.json";
}
