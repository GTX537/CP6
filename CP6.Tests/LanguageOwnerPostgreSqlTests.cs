using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Entity.DomainModels.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private const string LanguagePublication = "api/lang/publication-v1";
    private static readonly string[] AcceptedLanguages = ["zh-CN", "zh-TW", "en", "ja", "ko"];

    [CurrentPermissionPostgresFact]
    public async Task Language_owner_five_packs_original_ack_exact_bytes_and_concurrent_rollback()
    {
        await using var admin = await TenantManagementSeed();
        await LanguageActions(admin);
        var prefix = "wms.lang-proof-" + Guid.NewGuid().ToString("N");
        var approvedKey = prefix + ".approved";
        var draftKey = prefix + ".not-reviewed";
        var legacyKey = prefix + ".tenant-int";
        admin.Db.Sys_Langs.AddRange(
            new Sys_Lang { LangKey = approvedKey, Status = "reviewed", ZhCN = "编号 {code}", ZhTW = "編號 {code}", En = "Reference {code}", Ja = "番号 {code}", Ko = "번호 {code}" },
            new Sys_Lang { LangKey = draftKey, Status = "draft", En = "Unapproved fixture" },
            new Sys_Lang { LangKey = legacyKey, TenantId = 705, Status = "reviewed", En = "Legacy tenant fixture" });
        await admin.Db.SaveChangesAsync();

        var root = Path.Combine(Environment.GetEnvironmentVariable("CP6_PERMISSION_PG_ROOT")!, "language-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var environment = new Dictionary<string, string> { ["Storage__LocalRoot"] = root, ["I18n__ServeReviewedOnly"] = "true" };
        var connection = admin.Db.Database.GetConnectionString()!;
        await using var api = await AuthenticationApiProcess.Start(connection, environment);
        await using var replica = await AuthenticationApiProcess.Start(connection, environment);
        var first = await Login(api, admin);
        var second = await Login(replica, admin);

        async Task<JsonElement> Read(AuthenticationApiProcess host, Dictionary<string, string> cookies, string path)
        {
            using var result = await host.Send(OperationsRequest(cookies, HttpMethod.Get, path));
            return await LanguageJson(result, HttpStatusCode.OK);
        }

        var start = await Read(api, first, LanguagePublication);
        var operation = Guid.NewGuid();
        object Command(JsonElement snapshot, Guid key, string kind = "publish", string? target = null) => new
        {
            operationRef = key, kind, expectedRevision = snapshot.GetProperty("revision").GetString(),
            sourceVersion = kind == "publish" ? snapshot.GetProperty("sourceVersion").GetString() : null, version = target,
            reason = "五语全包与原操作验收"
        };
        var original = Command(start, operation);
        using (var committed = await api.Send(OperationsRequest(first, HttpMethod.Post, LanguagePublication + "/commands", original)))
            Assert.Equal(HttpStatusCode.OK, committed.StatusCode);
        // 丢弃提交回执，只用原操作号在另一宿主读取。
        var recovered = await Read(replica, second, LanguagePublication + "/operations/" + operation);
        var version = recovered.GetProperty("version").GetString()!;
        using (var replay = await replica.Send(OperationsRequest(second, HttpMethod.Post, LanguagePublication + "/commands", original)))
            Assert.Equal(recovered.GetRawText(), (await LanguageJson(replay, HttpStatusCode.OK)).GetRawText());
        using (var changed = await replica.Send(OperationsRequest(second, HttpMethod.Post, LanguagePublication + "/commands", Command(start, operation, "rollback", version))))
            Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);

        var manifest = await Read(api, first, "api/lang/manifest");
        Assert.Equal(version, manifest.GetProperty("version").GetString());
        Assert.Equal(AcceptedLanguages, manifest.GetProperty("langs").Deserialize<string[]>());
        var packs = manifest.GetProperty("packs");
        var published = new Dictionary<string, byte[]>();
        foreach (var code in AcceptedLanguages)
        {
            using var pack = await api.Send(new HttpRequestMessage(HttpMethod.Get, $"api/lang/published/{version}/{code}"));
            Assert.Equal(HttpStatusCode.OK, pack.StatusCode);
            Assert.Contains("immutable", pack.Headers.CacheControl!.ToString());
            var bytes = published[code] = await pack.Content.ReadAsByteArrayAsync();
            var descriptor = packs.GetProperty(code);
            Assert.Equal(bytes.Length, descriptor.GetProperty("length").GetInt64());
            Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), descriptor.GetProperty("sha256").GetString());
            using var file = JsonDocument.Parse(bytes);
            Assert.True(file.RootElement.TryGetProperty(approvedKey, out _));
            Assert.False(file.RootElement.TryGetProperty(draftKey, out _));
            Assert.False(file.RootElement.TryGetProperty(legacyKey, out _));
        }
        using (var pointer = await api.Send(new HttpRequestMessage(HttpMethod.Get, "api/lang/manifest")))
            Assert.True(pointer.Headers.CacheControl!.NoCache);

        var next = await Read(api, first, LanguagePublication);
        using (var released = await api.Send(OperationsRequest(first, HttpMethod.Post, LanguagePublication + "/commands", Command(next, Guid.NewGuid()))))
            Assert.NotEqual(version, (await LanguageJson(released, HttpStatusCode.OK)).GetProperty("version").GetString());
        var live = await Read(api, first, LanguagePublication);
        var unavailableRoot = Path.Combine(root, "storage-unavailable");
        Directory.CreateDirectory(unavailableRoot);
        await File.WriteAllTextAsync(Path.Combine(unavailableRoot, "i18n"), "模拟语言包目录不可写");
        await using (var unavailable = await AuthenticationApiProcess.Start(connection, new Dictionary<string, string>
        {
            ["Storage__LocalRoot"] = unavailableRoot
        }))
        {
            var session = await Login(unavailable, admin);
            var unavailableCommand = Command(live, Guid.NewGuid());
            using var failed = await unavailable.Send(OperationsRequest(session, HttpMethod.Post, LanguagePublication + "/commands", unavailableCommand));
            await LanguageStatus(failed, HttpStatusCode.ServiceUnavailable);
            Assert.Equal(live.GetProperty("currentVersion").GetString(),
                (await Read(api, first, "api/lang/manifest")).GetProperty("version").GetString());
        }
        var korean = Path.Combine(root, "i18n", version, "ko.json");
        Assert.Equal(published["ko"], await File.ReadAllBytesAsync(korean));
        var beforeReviewHistory = await admin.Db.Database.SqlQuery<int>($"""
            SELECT COUNT(*)::integer AS "Value" FROM cp6_lang_v1.entry_events
            """).SingleAsync();
        var badRollback = Command(live, Guid.NewGuid(), "rollback", version);
        File.Move(korean, korean + ".fixture-missing");
        try
        {
            using var missing = await replica.Send(OperationsRequest(second, HttpMethod.Post, LanguagePublication + "/commands", badRollback));
            Assert.Equal(HttpStatusCode.Conflict, missing.StatusCode);
        }
        finally { File.Move(korean + ".fixture-missing", korean); }
        await File.WriteAllBytesAsync(korean, published["ko"].Select((b, index) => index == 0 ? (byte)'[' : b).ToArray());
        try
        {
            using var corrupt = await replica.Send(OperationsRequest(second, HttpMethod.Post, LanguagePublication + "/commands", badRollback));
            Assert.Equal(HttpStatusCode.Conflict, corrupt.StatusCode);
        }
        finally { await File.WriteAllBytesAsync(korean, published["ko"]); }
        Assert.Equal(live.GetProperty("currentVersion").GetString(), (await Read(api, first, "api/lang/manifest")).GetProperty("version").GetString());

        var concurrent = await Task.WhenAll(
            api.Send(OperationsRequest(first, HttpMethod.Post, LanguagePublication + "/commands", Command(live, Guid.NewGuid()))),
            replica.Send(OperationsRequest(second, HttpMethod.Post, LanguagePublication + "/commands", Command(live, Guid.NewGuid(), "rollback", version))));
        using var publication = concurrent[0];
        using var rollback = concurrent[1];
        Assert.Single(concurrent, result => result.StatusCode == HttpStatusCode.OK);
        Assert.Single(concurrent, result => result.StatusCode == HttpStatusCode.PreconditionFailed);
        var rolledSnapshot = await Read(api, first, LanguagePublication);
        using (var rolled = await api.Send(OperationsRequest(first, HttpMethod.Post, LanguagePublication + "/commands", Command(rolledSnapshot, Guid.NewGuid(), "rollback", version))))
            Assert.Equal(version, (await LanguageJson(rolled, HttpStatusCode.OK)).GetProperty("version").GetString());
        foreach (var code in AcceptedLanguages)
            Assert.Equal(published[code], await File.ReadAllBytesAsync(Path.Combine(root, "i18n", version, code + ".json")));
        Assert.Equal("draft", await admin.Db.Sys_Langs.Where(x => x.LangKey == draftKey).Select(x => x.Status).SingleAsync());
        Assert.Equal(beforeReviewHistory, await admin.Db.Database.SqlQuery<int>($"""
            SELECT COUNT(*)::integer AS "Value" FROM cp6_lang_v1.entry_events
            """).SingleAsync());
        using var bootstrap = await api.Send(new HttpRequestMessage(HttpMethod.Get, "api/client/bootstrap?platform=windows&currentVersion=1.0.0"));
        Assert.Equal(version, (await LanguageJson(bootstrap, HttpStatusCode.OK)).GetProperty("languageManifestVersion").GetString());

        await DeviceEvidence("wp02-17-five-pack-release.json", new
        {
            Version = version, Manifest = manifest, MissingLastPack = 409, HashMismatch = 409,
            CrossHostOriginal = true, OriginalBytesUnchanged = true, StageWriteUnavailable = 503, ReviewHistoryUnchanged = true, BootstrapVersion = version, Race = concurrent.Select(x => (int)x.StatusCode)
        });
    }

    [CurrentPermissionPostgresFact]
    public async Task Language_owner_scoped_fields_and_explicit_review_use_current_revisions()
    {
        await using var admin = await TenantManagementSeed();
        await using var tenant = await Seed();
        await LanguageActions(admin);
        await LanguageActions(tenant);
        var root = Path.Combine(Environment.GetEnvironmentVariable("CP6_PERMISSION_PG_ROOT")!, "entries-" + Guid.NewGuid().ToString("N"));
        await using var api = await AuthenticationApiProcess.Start(admin.Db.Database.GetConnectionString()!, new Dictionary<string, string> { ["Storage__LocalRoot"] = root });
        var a = await Login(api, admin);
        var b = await Login(api, tenant);
        var key = "erp.lang-proof." + Guid.NewGuid().ToString("N");
        Task<HttpResponseMessage> Command(Dictionary<string, string> cookies, string scope, object body) =>
            api.Send(OperationsRequest(cookies, HttpMethod.Post, $"api/lang/entries-v1/{scope}/commands", body));
        Task<HttpResponseMessage> Review(Dictionary<string,string> cookies, string scope, object items) =>
            api.Send(OperationsRequest(cookies, HttpMethod.Post, $"api/lang/entries-v1/{scope}/review", new { entries = items, reason = "校对最新版词条" }));
        var translations = new Dictionary<string, string> { ["ZhCN"] = "单号 {code}", ["En"] = "Reference {code}" };
        object Add(object values) => new { kind = "create", langKey = key, translations = values, reason = "维护词条" };
        using (var denied = await Command(b, "global", Add(translations)))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        JsonElement entry;
        using (var added = await Command(a, "global", Add(translations)))
            entry = await LanguageJson(added, HttpStatusCode.OK);
        using (var hiddenAdd = await Command(b, "tenant", Add(translations)))
            Assert.Equal(HttpStatusCode.OK, hiddenAdd.StatusCode);
        using (var duplicate = await Command(a, "global", Add(translations)))
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        int id = entry.GetProperty("id").GetInt32();
        string? revision = entry.GetProperty("version").GetString();
        using (var empty = await Review(a, "global", Array.Empty<object>()))
            Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        JsonElement edited;
        var inconsistent = new { kind = "update", id, expectedVersion = revision, translations = new Dictionary<string,string> { ["En"] = "Ref {different}" }, reason = "模拟占位符不一致" };
        using (var changed = await Command(a, "global", inconsistent))
            edited = await LanguageJson(changed, HttpStatusCode.OK);
        using (var stale = await Review(a, "global", new[] { new { id, version = revision } }))
            Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        using (var bad = await Review(a, "global", new[] { new { id, version = edited.GetProperty("version").GetString() } }))
            Assert.Equal(HttpStatusCode.UnprocessableEntity, bad.StatusCode);
        var corrected = new { kind = "update", id, expectedVersion = edited.GetProperty("version").GetString(), translations, reason = "校对占位符" };
        using (var changed = await Command(a, "global", corrected))
            edited = await LanguageJson(changed, HttpStatusCode.OK);
        using (var reviewed = await Review(a, "global", new[] { new { id, version = edited.GetProperty("version").GetString() } }))
            Assert.Equal(HttpStatusCode.OK, reviewed.StatusCode);
        Assert.Equal("reviewed", await admin.Db.Sys_Langs.Where(x => x.Id == id).Select(x => x.Status).SingleAsync());

        tenant.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = "lang", FieldName = "En", Access = 3 });
        await tenant.Db.SaveChangesAsync();
        using (var deniedExport = await api.Send(OperationsRequest(b, HttpMethod.Get, "api/lang/entries-v1/tenant/export?fields=En")))
            Assert.Equal(HttpStatusCode.Forbidden, deniedExport.StatusCode);
        using (var masked = await api.Send(OperationsRequest(b, HttpMethod.Get, "api/lang/entries-v1/tenant?keyword=" + key)))
        {
            var view = await LanguageJson(masked, HttpStatusCode.OK);
            var row = Assert.Single(view.GetProperty("rows").EnumerateArray());
            Assert.False(row.GetProperty("translations").TryGetProperty("En", out _));
        }
        using (var publicFeed = await api.Send(new HttpRequestMessage(HttpMethod.Get, "api/lang/en")))
        {
            var dictionary = await LanguageJson(publicFeed, HttpStatusCode.OK);
            Assert.Equal("Reference {code}", dictionary.GetProperty(key).GetString());
        }
        await DeviceEvidence("wp02-17-language-authority.json", new
        {
            TenantGlobalDenied = true, DuplicateGlobal = 409, EmptyVersionedReview = 400,
            StaleEntryReview = 412, PlaceholderMismatch = 422, HiddenFieldExportDenied = true
        });
    }

    [CurrentPermissionPostgresFact]
    public async Task Language_owner_legacy_review_namespace_publication_and_tenant_import_are_independent()
    {
        await using var admin = await TenantManagementSeed();
        await using var editor = await Seed();
        await using var reader = await Seed();
        await LanguageActions(admin);
        await LanguageActions(editor);
        await LanguageActions(reader);
        var code = Guid.NewGuid().ToString("N");
        var oldKey = "旧言語キー" + code;
        var newNamespace = "s5lang" + code;
        var publicKeys = new[] { oldKey, "wms." + code, "sales." + code, "erp." + code, "mes." + code, newNamespace + ".notice" };
        var existing = publicKeys.Select(key => new Sys_Lang { LangKey = key, Ja = "原参照 {ref}", ZhCN = "原引用 {ref}", Status = "draft" });
        admin.Db.Sys_Langs.AddRange(existing);
        await admin.Db.SaveChangesAsync();
        var root = Path.Combine(Environment.GetEnvironmentVariable("CP6_PERMISSION_PG_ROOT")!, "lang-namespaces-" + code);
        await using var api = await AuthenticationApiProcess.Start(admin.Db.Database.GetConnectionString()!,
            new Dictionary<string,string> { ["Storage__LocalRoot"] = root });
        var a = await Login(api, admin);
        var b = await Login(api, editor);
        var c = await Login(api, reader);

        async Task<JsonElement> Get(string path) {
            using var response = await api.Send(new HttpRequestMessage(HttpMethod.Get, path));
            return await LanguageJson(response, HttpStatusCode.OK);
        }
        async Task<JsonElement> Read(Dictionary<string,string> cookies, string path) {
            using var response = await api.Send(OperationsRequest(cookies, HttpMethod.Get, path));
            return await LanguageJson(response, HttpStatusCode.OK);
        }

        Assert.False((await Get("api/lang/ja/ns/" + newNamespace)).TryGetProperty(publicKeys[5], out _));
        using (var compatible = await api.Send(OperationsRequest(a, HttpMethod.Post, "api/lang/review", Array.Empty<int>())))
            Assert.True((await LanguageJson(compatible, HttpStatusCode.OK)).GetProperty("data").GetProperty("count").GetInt32() >= publicKeys.Length);
        Assert.Equal("原参照 {ref}", (await Get("api/lang/ja/ns/_core")).GetProperty(oldKey).GetString());
        foreach (var key in publicKeys.Skip(1))
            Assert.Equal("原参照 {ref}", (await Get("api/lang/ja/ns/" + key.Split('.')[0])).GetProperty(key).GetString());
        Assert.Equal("原参照 {ref}", (await Get("api/lang/ja")).GetProperty(oldKey).GetString());

        const string entries = "api/lang/entries-v1/tenant";
        var privateKey = newNamespace + ".tenant";
        object Create(string key, string field, string value) => new { kind = "create", langKey = key,
            translations = new Dictionary<string,string> { [field] = value }, reason = "模拟词条" };
        Task<HttpResponseMessage> Import(Dictionary<string,string> user, params object[] commands) =>
            api.Send(OperationsRequest(user, HttpMethod.Post, entries + "/import", commands));
        using (var good = await Import(b, Create(privateKey, "Ja", "組織の文面")))
            Assert.Equal(1, (await LanguageJson(good, HttpStatusCode.OK)).GetProperty("count").GetInt32());
        using (var other = await Import(c, Create(privateKey, "Ja", "別の組織の文面")))
            await LanguageStatus(other, HttpStatusCode.OK);
        using (var bodyScope = await api.Send(OperationsRequest(b, HttpMethod.Post, entries + "/commands", new
               { kind = "create", langKey = "blocked." + code, tenantId = reader.Tenant, reason = "验收范围", translations = new { Ja = "不可写入" } })))
            await LanguageStatus(bodyScope, HttpStatusCode.BadRequest);

        // 旧管理入口的空 body 也审校当前可管理范围全部合格草稿。
        using (var omitted = await api.Send(OperationsRequest(b, HttpMethod.Post, "api/lang/review", null)))
            Assert.Equal(1, (await LanguageJson(omitted, HttpStatusCode.OK)).GetProperty("data").GetProperty("count").GetInt32());
        using (var omitted = await api.Send(OperationsRequest(c, HttpMethod.Post, "api/lang/review", null)))
            Assert.Equal(1, (await LanguageJson(omitted, HttpStatusCode.OK)).GetProperty("data").GetProperty("count").GetInt32());

        using (var exported = await api.Send(OperationsRequest(b, HttpMethod.Get, entries + "/export?fields=Ja")))
        {
            var row = Assert.Single((await LanguageJson(exported, HttpStatusCode.OK)).GetProperty("rows").EnumerateArray());
            Assert.Equal("組織の文面", row.GetProperty("translations").GetProperty("Ja").GetString());
        }

        var preview = await Read(a, LanguagePublication);
        object Publish(JsonElement source) => new
        {
            operationRef = Guid.NewGuid(), kind = "publish", expectedRevision = source.GetProperty("revision").GetString(),
            sourceVersion = source.GetProperty("sourceVersion").GetString(), reason = "发布经过审校的全局词条"
        };
        string version;
        using (var posted = await api.Send(OperationsRequest(a, HttpMethod.Post, LanguagePublication + "/commands", Publish(preview))))
            version = (await LanguageJson(posted, HttpStatusCode.OK)).GetProperty("version").GetString()!;
        var overlay = "api/lang/overlay-v1/ja?version=" + version;
        Assert.Equal("組織の文面", (await Read(b, overlay)).GetProperty("translations").GetProperty(privateKey).GetString());
        Assert.Equal("別の組織の文面", (await Read(c, overlay)).GetProperty("translations").GetProperty(privateKey).GetString());
        using (var anonymous = await api.Send(new HttpRequestMessage(HttpMethod.Get, overlay)))
            await LanguageStatus(anonymous, HttpStatusCode.Unauthorized);
        Assert.False((await Get("api/lang/published/" + version + "/ja")).TryGetProperty(privateKey, out _));

        var tenantRow = Assert.Single((await Read(b, entries + "?keyword=" + privateKey)).GetProperty("rows").EnumerateArray());
        var tenantId = tenantRow.GetProperty("id").GetInt32();
        var entryVersion = tenantRow.GetProperty("version").GetString();
        editor.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = "lang", FieldName = "En", Access = 3 });
        await editor.Db.SaveChangesAsync();
        using (var rejected = await Import(b, Create(newNamespace + ".first", "Ja", "この変更も取り消す"),
               Create(newNamespace + ".denied", "En", "Hidden")))
            await LanguageStatus(rejected, HttpStatusCode.Forbidden);
        Assert.Equal(0, (await Read(b, entries + "?keyword=" + newNamespace + ".first")).GetProperty("total").GetInt32());

        var update = new { kind = "update", id = tenantId, expectedVersion = entryVersion,
            translations = new Dictionary<string, string> { ["Ja"] = "新しい下書き" }, reason = "修改已审校词条" };
        using (var edited = await api.Send(OperationsRequest(b, HttpMethod.Post, entries + "/commands", update)))
            Assert.Equal("draft", (await LanguageJson(edited, HttpStatusCode.OK)).GetProperty("status").GetString());
        Assert.False((await Read(b, overlay)).GetProperty("translations").TryGetProperty(privateKey, out _));
        Assert.Equal("別の組織の文面", (await Read(c, overlay)).GetProperty("translations").GetProperty(privateKey).GetString());

        var reviewedScope = await Read(a, "api/lang/entries-v1/global?keyword=" + oldKey);
        var globalRow = Assert.Single(reviewedScope.GetProperty("rows").EnumerateArray());
        var eligible = await Read(a, LanguagePublication);
        var manual = new Sys_Lang { LangKey = newNamespace + ".mismatch", Status = "reviewed", En = "{amount}", Ja = "{other}" };
        admin.Db.Sys_Langs.Add(manual);
        await admin.Db.SaveChangesAsync();
        using (var stale = await api.Send(OperationsRequest(a, HttpMethod.Post, LanguagePublication + "/commands", Publish(eligible))))
            await LanguageStatus(stale, HttpStatusCode.PreconditionFailed);
        var mismatch = await Read(a, LanguagePublication);
        using (var mixed = await api.Send(OperationsRequest(a, HttpMethod.Post, LanguagePublication + "/commands", Publish(mismatch))))
            Assert.Equal("LANG_PLACEHOLDER_MISMATCH", (await LanguageJson(mixed, HttpStatusCode.UnprocessableEntity)).GetProperty("code").GetString());
        Assert.Equal(version, (await Get("api/lang/manifest")).GetProperty("version").GetString());
        admin.Db.Sys_Langs.Remove(manual);
        await admin.Db.SaveChangesAsync();

        async Task<string> DeniedCode(string language)
        {
            using var request = OperationsRequest(a, HttpMethod.Post, LanguagePublication + "/commands", Publish(eligible));
            request.Headers.Remove("X-CSRF-Token");
            request.Headers.Add("Accept-Language", language);
            using var reply = await api.Send(request);
            return (await LanguageJson(reply, HttpStatusCode.Forbidden)).GetProperty("errorCode").GetString()!;
        }
        Assert.Equal("E-SEC-010", await DeniedCode("ja"));
        Assert.Equal("E-SEC-010", await DeniedCode("zh-CN"));
        await DeviceEvidence("wp02-17-language-compatibility.json", new
        {
            legacyKey = oldKey, compatibleNamespaces = publicKeys, EmptyIdsAll = true, MissingIdsAll = true,
            sameKeyAcrossOrganizations = true, version, allForbiddenImportRolledBack = true,
            publicNeverContainsTenant = true, reviewedToDraft = true, ignoredPendingGlobal = true,
            sourceChanged = 412, badPlaceholderAtPublish = 422, stableErrorCode = "E-SEC-010",
            OriginalGlobalVersion = globalRow.GetProperty("version").GetString()
        });
    }

    private static async Task LanguageStatus(HttpResponseMessage result, HttpStatusCode expected)
        => Assert.True(result.StatusCode == expected, $"预期 {(int)expected}，实际 {(int)result.StatusCode} · {await OperationsFailure(result)}");

    private static async Task<JsonElement> LanguageJson(HttpResponseMessage result, HttpStatusCode expected)
    {
        Assert.True(result.StatusCode == expected, $"预期 {(int)expected}，实际 {(int)result.StatusCode} · {await OperationsFailure(result)}");
        return await result.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task LanguageActions(Harness h)
    {
        var menu = await h.Db.Sys_Menus.SingleOrDefaultAsync(x => x.MenuKey == "lang");
        if (menu is null)
        {
            menu = new() { MenuId = 581431970, MenuName = "语言维护", MenuKey = "lang", RoutePath = "/lang", Enable = true };
            h.Db.Sys_Menus.Add(menu);
            await h.Db.SaveChangesAsync();
        }
        foreach (var action in new[] { "query", "update", "delete", "review", "publish" })
        {
            if (!await h.Db.Sys_MenuActions.AnyAsync(x => x.MenuId == menu.MenuId && x.ActionCode == action))
                h.Db.Sys_MenuActions.Add(new() { MenuId = menu.MenuId, ActionCode = action, ActionName = action });
            h.Db.Sys_RoleActions.Add(new() { RoleId = 72, MenuId = menu.MenuId, ActionCode = action });
        }
        h.Db.Sys_RoleMenus.Add(new() { RoleId = 72, MenuId = menu.MenuId });
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
    }
}
