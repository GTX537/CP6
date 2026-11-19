using System.Text.Json.Serialization;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using CP6.WebApi.Localization;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.WebApi.Services;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LanguageEntryCommand(string? Kind, int? Id, Guid? ExpectedVersion, string? LangKey,
    Dictionary<string, string?>? Translations, string? Reason);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LanguageReviewSelection(int Id, Guid Version);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LanguageReviewCommand(LanguageReviewSelection[]? Entries, string? Reason);

public sealed class LanguageEntriesV1(CP6Context db, IamAdministrationV1 authority)
{
    private readonly LanguageAuthorityV1 access = new(db, authority);
    private static readonly string[] Fields = LanguageEntryRulesV1.Fields.Select(field => field.Name).ToArray();
    private sealed record Row(int Id, string LangKey, string Status, Guid Version, bool EverReviewed,
        string? ZhCN, string? ZhTW, string? En, string? Ja, string? Ko)
    {
        public string? Column(string name) => name switch
        {
            "ZhCN" => ZhCN, "ZhTW" => ZhTW, "En" => En, "Ja" => Ja, "Ko" => Ko,
            _ => throw new InvalidOperationException("语言字段未登记。")
        };
    }

    private IQueryable<Row> Rows(bool global) => global
        ? db.Database.SqlQuery<Row>($"""
            SELECT "Id", "LangKey", "Status", "LanguageRevision" AS "Version", "EverReviewed", "ZhCN", "ZhTW", "En", "Ja", "Ko"
            FROM public."Sys_Langs" WHERE "TenantId" IS NULL
            """)
        : db.Database.SqlQuery<Row>($"""
            SELECT "Id", "LangKey", "Status", "LanguageRevision" AS "Version", "EverReviewed", "ZhCN", "ZhTW", "En", "Ja", "Ko"
            FROM cp6_lang_v1.tenant_entries WHERE "TenantId" = {db.CurrentTenantId}
            """);

    public Task<object> List(HttpContext http, bool global, string? keyword, int page, int pageSize) =>
        access.Execute<object>(http, global, "query", async (actor, ct) =>
        {
            if (page is < 1 or > 1000000 || pageSize is < 1 or > 200 || keyword?.Length > 200)
                throw new LanguageProblemV1(400, "LANG_QUERY_INVALID");
            var rows = Rows(global);
            if (!string.IsNullOrEmpty(keyword))
            {
                var cn = LanguageAuthorityV1.Access(actor, "ZhCN") < 3;
                var tw = LanguageAuthorityV1.Access(actor, "ZhTW") < 3;
                var en = LanguageAuthorityV1.Access(actor, "En") < 3;
                var ja = LanguageAuthorityV1.Access(actor, "Ja") < 3;
                var ko = LanguageAuthorityV1.Access(actor, "Ko") < 3;
                rows = rows.Where(row => row.LangKey.Contains(keyword) ||
                    cn && row.ZhCN != null && row.ZhCN.Contains(keyword) ||
                    tw && row.ZhTW != null && row.ZhTW.Contains(keyword) ||
                    en && row.En != null && row.En.Contains(keyword) ||
                    ja && row.Ja != null && row.Ja.Contains(keyword) ||
                    ko && row.Ko != null && row.Ko.Contains(keyword));
            }
            var total = await rows.CountAsync(ct);
            var shown = await rows.OrderBy(row => row.LangKey).ThenBy(row => row.Id)
                .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
            var visibleFields = Fields.Where(field => LanguageAuthorityV1.Access(actor, field) < 3).ToArray();
            return new
            {
                total, rows = shown.Select(row => View(row, visibleFields)),
                fields = visibleFields.Select(field => new { name = field, access = LanguageAuthorityV1.Access(actor, field) })
            };
        });

    public Task<object> Export(HttpContext http, bool global, string[] fields) =>
        access.Execute<object>(http, global, "query", async (actor, ct) =>
        {
            if (fields.Length == 0 || fields.Length != fields.Distinct(StringComparer.Ordinal).Count() ||
                fields.Any(field => !Fields.Contains(field, StringComparer.Ordinal)))
                throw new LanguageProblemV1(400, "LANG_EXPORT_FIELDS_REQUIRED");
            if (fields.Any(field => LanguageAuthorityV1.Access(actor, field) >= 3))
                throw new LanguageProblemV1(403, "LANG_FIELD_FORBIDDEN");
            var rows = await Rows(global).OrderBy(row => row.LangKey).ToListAsync(ct);
            return new { scope = global ? "global" : "tenant", fields, rows = rows.Select(row => View(row, fields)) };
        });

    public Task<object> Apply(HttpContext http, bool global, LanguageEntryCommand command) =>
        access.Execute<object>(http, global, command.Kind == "delete" ? "delete" : "update", async (actor, ct) =>
        {
            var result = await Write(global, actor, command, ct);
            return result is null ? new { deleted = true } : View(result, Fields.Where(field => LanguageAuthorityV1.Access(actor, field) < 3));
        });

    public Task<object> Import(HttpContext http, bool global, LanguageEntryCommand?[]? commands) =>
        access.Execute<object>(http, global, "update", async (actor, ct) =>
        {
            if (commands is null or { Length: 0 } or { Length: > 500 } ||
                commands.Any(command => command?.Kind is not ("create" or "update")))
                throw new LanguageProblemV1(400, "LANG_IMPORT_COMMANDS_REQUIRED");
            foreach (var command in commands)
                await Write(global, actor, command!, ct);
            return new { count = commands.Length };
        });

    public Task<object> Review(HttpContext http, bool global, LanguageReviewCommand command) =>
        access.Execute<object>(http, global, "review", async (actor, ct) =>
        {
            ValidateReason(command.Reason);
            if (command.Entries is null or { Length: 0 } or { Length: > 500 } ||
                command.Entries.Any(item => item is null || item.Id <= 0 || item.Version == Guid.Empty) ||
                command.Entries.Select(item => item.Id).Distinct().Count() != command.Entries.Length)
                throw new LanguageProblemV1(400, "LANG_REVIEW_SELECTION_REQUIRED");
            var ids = command.Entries.Select(entry => entry.Id).ToArray();
            var rows = await Rows(global).Where(row => ids.Contains(row.Id)).ToListAsync(ct);
            var revisions = command.Entries.ToDictionary(item => item.Id, item => item.Version);
            if (rows.Count != revisions.Count || rows.Any(row => revisions[row.Id] != row.Version || row.Status != "draft"))
                throw new LanguageProblemV1(412, "LANG_ENTRY_REVISION_CHANGED");
            await Approve(global, actor, rows, command.Reason!, ct);
            return new { count = rows.Count };
        });

    public Task<object> ReviewLegacy(HttpContext http, bool global, int[]? ids) =>
        access.Execute<object>(http, global, "review", async (actor, ct) =>
        {
            var query = Rows(global).Where(row => row.Status != "reviewed");
            if (ids is { Length: > 0 }) query = query.Where(row => ids.Contains(row.Id));
            var rows = await query.ToListAsync(ct);
            await Approve(global, actor, rows, "旧版审校入口", ct);
            return new { code = 0, message = "OK", data = new { count = rows.Count } };
        });

    public Task<object> Overlay(HttpContext http, string language, string version, LanguagePublicationV1 publication) =>
        access.Consume<object>(http, async (actor, ct) =>
        {
            if (!LangColumn.Codes.Contains(language, StringComparer.Ordinal) ||
                await publication.ReadManifest(version, ct) is null)
                throw new LanguageProblemV1(404, "LANG_RELEASE_NOT_FOUND");
            var field = Fields[Array.IndexOf(LangColumn.Codes, language)];
            var translations = new Dictionary<string, string>();
            if (LanguageAuthorityV1.Access(actor, field) < 3)
            {
                var rows = await Rows(false).Where(row => row.Status == "reviewed").ToListAsync(ct);
                foreach (var row in rows)
                {
                    if (row.Column(field) is { Length: > 0 } value) translations.Add(row.LangKey, value);
                }
            }
            return new { language, version, translations };
        });

    private async Task<Row?> Write(bool global, UserPermissionContext actor, LanguageEntryCommand command, CancellationToken ct)
    {
        ValidateReason(command.Reason);
        var translations = command.Translations ?? new Dictionary<string, string?>();
        if (translations.Any(pair => !Fields.Contains(pair.Key, StringComparer.Ordinal) ||
            pair.Value is { Length: > 500 } || pair.Value?.Contains('\0') == true))
            throw new LanguageProblemV1(400, "LANG_TRANSLATION_INVALID");
        if (translations.Keys.Any(field => LanguageAuthorityV1.Access(actor, field) != 1))
            throw new LanguageProblemV1(403, "LANG_FIELD_FORBIDDEN");

        if (command.Kind == "create")
        {
            LanguageEntryRulesV1.ValidateKey(command.LangKey);
            if (command.Id is not null || command.ExpectedVersion is not null || translations.Count == 0)
                throw new LanguageProblemV1(400, "LANG_CREATE_INVALID");
            var row = new Row(0, command.LangKey!, "draft", Guid.Empty, false,
                translations.GetValueOrDefault("ZhCN"), translations.GetValueOrDefault("ZhTW"),
                translations.GetValueOrDefault("En"), translations.GetValueOrDefault("Ja"), translations.GetValueOrDefault("Ko"));
            int id;
            try
            {
                id = global
                    ? (await db.Database.SqlQuery<int>($"""
                        INSERT INTO public."Sys_Langs" ("LangKey","Status","ZhCN","ZhTW","En","Ja","Ko","UpdatedBy","UpdatedAt")
                        VALUES ({row.LangKey},'draft',{row.ZhCN},{row.ZhTW},{row.En},{row.Ja},{row.Ko},{actor.UserName},LOCALTIMESTAMP)
                        RETURNING "Id" AS "Value"
                        """).ToListAsync(ct)).Single()
                    : (await db.Database.SqlQuery<int>($"""
                        INSERT INTO cp6_lang_v1.tenant_entries ("TenantId","LangKey","Status","ZhCN","ZhTW","En","Ja","Ko")
                        VALUES ({db.CurrentTenantId},{row.LangKey},'draft',{row.ZhCN},{row.ZhTW},{row.En},{row.Ja},{row.Ko})
                        RETURNING "Id" AS "Value"
                        """).ToListAsync(ct)).Single();
            }
            catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.UniqueViolation)
            { throw new LanguageProblemV1(409, "LANG_KEY_EXISTS"); }
            var saved = await Rows(global).SingleAsync(row => row.Id == id, ct);
            await Audit(global, actor, "create", saved, command.Reason!, ct);
            return saved;
        }

        if (command.Kind is not ("update" or "delete") || command.Id is null or <= 0 ||
            command.ExpectedVersion is null || command.ExpectedVersion == Guid.Empty || command.LangKey is not null ||
            (command.Kind == "delete" ? translations.Count != 0 : translations.Count == 0))
            throw new LanguageProblemV1(400, "LANG_ENTRY_COMMAND_REQUIRED");
        var current = await Rows(global).SingleOrDefaultAsync(row => row.Id == command.Id, ct);
        if (current is null) throw new LanguageProblemV1(404, "LANG_ENTRY_NOT_FOUND");
        if (current.Version != command.ExpectedVersion)
            throw new LanguageProblemV1(412, "LANG_ENTRY_REVISION_CHANGED");
        if (command.Kind == "delete")
        {
            if (Fields.Any(field => LanguageAuthorityV1.Access(actor, field) != 1))
                throw new LanguageProblemV1(403, "LANG_FIELD_FORBIDDEN");
            if (current.EverReviewed)
                throw new LanguageProblemV1(409, "LANG_REVIEWED_ENTRY_PROTECTED");
            if (global)
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    DELETE FROM public."Sys_Langs" WHERE "TenantId" IS NULL AND "Id" = {current.Id}
                    """, ct);
            else
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    DELETE FROM cp6_lang_v1.tenant_entries WHERE "TenantId" = {db.CurrentTenantId} AND "Id" = {current.Id}
                    """, ct);
            await Audit(global, actor, "delete", current, command.Reason!, ct);
            return null;
        }
        string? Text(string field) => translations.TryGetValue(field, out var value) ? value : current.Column(field);
        var next = current with { Status = "draft", ZhCN = Text("ZhCN"), ZhTW = Text("ZhTW"), En = Text("En"), Ja = Text("Ja"), Ko = Text("Ko") };
        var updated = await Save(global, actor, next, ct);
        await Audit(global, actor, "update", updated, command.Reason!, ct);
        return updated;
    }

    private async Task Approve(bool global, UserPermissionContext actor, List<Row> rows, string reason, CancellationToken ct)
    {
        foreach (var row in rows)
        {
            if (Fields.Any(field => row.Column(field) is not null && LanguageAuthorityV1.Access(actor, field) >= 3))
                throw new LanguageProblemV1(403, "LANG_FIELD_FORBIDDEN");
            LanguageEntryRulesV1.ValidateKey(row.LangKey);
            LanguageEntryRulesV1.ValidatePlaceholders(Fields.Select(row.Column));
        }
        foreach (var row in rows)
        {
            await Save(global, actor, row with { Status = "reviewed" }, ct);
            await Audit(global, actor, "review", row, reason, ct);
        }
    }

    private async Task<Row> Save(bool global, UserPermissionContext actor, Row row, CancellationToken ct)
    {
        var changed = global
            ? await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE public."Sys_Langs" SET "Status" = {row.Status}, "ZhCN" = {row.ZhCN}, "ZhTW" = {row.ZhTW},
                    "En" = {row.En}, "Ja" = {row.Ja}, "Ko" = {row.Ko}, "UpdatedBy" = {actor.UserName}, "UpdatedAt" = LOCALTIMESTAMP
                WHERE "TenantId" IS NULL AND "Id" = {row.Id} AND "LanguageRevision" = {row.Version}
                """, ct)
            : await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE cp6_lang_v1.tenant_entries SET "Status" = {row.Status}, "ZhCN" = {row.ZhCN}, "ZhTW" = {row.ZhTW},
                    "En" = {row.En}, "Ja" = {row.Ja}, "Ko" = {row.Ko}
                WHERE "TenantId" = {db.CurrentTenantId} AND "Id" = {row.Id} AND "LanguageRevision" = {row.Version}
                """, ct);
        if (changed != 1) throw new LanguageProblemV1(412, "LANG_ENTRY_REVISION_CHANGED");
        var saved = await Rows(global).SingleAsync(current => current.Id == row.Id, ct);
        if (saved.Version == row.Version) throw new LanguageProblemV1(503, "LANG_ENTRY_REVISION_UNAVAILABLE");
        return saved;
    }

    private Task Audit(bool global, UserPermissionContext actor, string action, Row row, string reason, CancellationToken ct)
        => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_lang_v1.entry_events (tenant_id,actor_id,action,entry_id,entry_revision,reason)
            VALUES ({(global ? (Guid?)null : db.CurrentTenantId)},{actor.UserId},{action},{row.Id},{row.Version},{reason})
            """, ct);

    private static object View(Row row, IEnumerable<string> fields) => new
    {
        row.Id, row.LangKey, row.Status, row.Version, row.EverReviewed,
        translations = fields.ToDictionary(field => field, row.Column, StringComparer.Ordinal)
    };

    private static void ValidateReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 256 || reason.Contains('\0'))
            throw new LanguageProblemV1(400, "LANG_REASON_REQUIRED");
    }
}
