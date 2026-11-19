using System.Net;
using System.Text.Json;
using CP6.Core.Services.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static readonly string[] GlActions =
    ["view", "add", "edit", "submit", "review", "release", "deactivate", "restore", "retire", "history", "import"];

    private sealed record GlActors(Guid Ledger, Guid Checker);
    private static Dictionary<string, string> GlHostSettings() =>
        new() { ["FinanceCosting__EnvironmentId"] = ScopeEnvironment.ToString("D") };

    private async Task<GlActors> SeedGlBoard(Harness h, string tag)
    {
        foreach (var action in GlActions) await GrantLegacyAuthorityAction(h, "fin-account", action);
        var menu = await h.Db.Sys_Menus.SingleAsync(m => m.MenuKey == "fin-account");
        menu.RoutePath = "/fin/account";
        h.Db.Sys_RoleMenus.Add(new() { RoleId = 72, MenuId = menu.MenuId });
        await h.Db.SaveChangesAsync();

        var checker = await GrantDelegate(h, "gl-checker-" + tag);
        var secret = new BCryptPasswordHasher(4).Hash("Price1!Password");
        await h.Db.Sys_Users.Where(user => user.Id == checker)
            .ExecuteUpdateAsync(set => set.SetProperty(user => user.Password, secret));
        var ledger = Guid.NewGuid();
        var currency = new[] { "CNY", "USD" };
        var roles = new[] { "AR_CONTROL", "REVENUE", "EXPENSE" };
        var env = ScopeEnvironment.ToString("D");
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_fin_mfg_v1.ledgers(tenant_id,id,environment_id,mode,legal_entity_key,ledger_key,currency,business_date)
            VALUES ({h.Tenant},{ledger},{env},'REAL','SIM-LEGAL','SIM-BOOK','CNY',{GlBusinessDay});
            INSERT INTO cp6_fin_mfg_v1.members(tenant_id,ledger_id,subject_id)
            VALUES ({h.Tenant},{ledger},{h.User}),({h.Tenant},{ledger},{checker});
            INSERT INTO cp6_fin_gl_v1.books(tenant_id,ledger_id,scheme,basis_key,roles,currencies,professional_available,policy_from)
            VALUES ({h.Tenant},{ledger},'CN-GAAP','SIM-PROFESSIONAL-COA-01',{roles},{currency},true,{GlBusinessDay});
            WITH grants AS MATERIALIZED (
                SELECT a.id,a.issuer,p.action,gen_random_uuid() reference
                FROM (VALUES ({h.User},{checker}),({checker},{h.User})) a(id,issuer)
                CROSS JOIN (VALUES ('gl-account-review'),('gl-account-release')) p(action)
            ), accepted AS (
                INSERT INTO cp6_fin_mfg_v1.qualification_decisions
                    (tenant_id,ledger_id,command_id,id,issuer_id,subject_id,action,qualification_id,enabled,request)
                SELECT {h.Tenant},{ledger},gen_random_uuid(),reference,issuer,id,action,reference,true,jsonb_build_object('fixture','simulated profession')
                FROM grants RETURNING subject_id,action,qualification_id
            )
            INSERT INTO cp6_fin_mfg_v1.qualifications(tenant_id,ledger_id,subject_id,action,qualification_id,expires_at)
            SELECT {h.Tenant},{ledger},subject_id,action,qualification_id,clock_timestamp()+interval '1 hour' FROM accepted
            """);
        return new(ledger, checker);
    }

    private sealed class GlClient(AuthenticationApiProcess host, Dictionary<string, string> cookies)
    {
        internal Guid Context { get; set; }

        internal Task<HttpResponseMessage> Send(HttpMethod method, string route, object? data = null)
        {
            var message = OperationsRequest(cookies, method, GlOwnerRoute + route, data);
            if (Context != Guid.Empty) message.Headers.Add("X-Finance-Context", Context.ToString("D"));
            return host.Send(message);
        }

        internal async Task<JsonElement> Read(string route)
        {
            using var response = await Send(HttpMethod.Get, route);
            return await GlJson(response, HttpStatusCode.OK);
        }

        internal async Task<JsonElement> Write(string route, object data, HttpStatusCode status = HttpStatusCode.OK)
        {
            using var response = await Send(HttpMethod.Post, route, data);
            return await GlJson(response, status);
        }

        internal async Task AuthenticateBook()
        {
            var choices = (await Read("/options")).GetProperty("choices").EnumerateArray();
            var book = Assert.Single(choices);
            Assert.Equal("CN-GAAP", book.GetProperty("scheme").GetString());
            Assert.Equal("SIM-BOOK", book.GetProperty("scope").GetProperty("ledgerKey").GetString());
            Assert.Equal("CNY", book.GetProperty("baseCurrency").GetString());
            Context = book.GetProperty("contextToken").GetGuid();
        }

        internal Task<JsonElement> Head(Guid root) => Read($"/roots/{root:D}/workflow");
    }

    private static object GlContent(string name, DateOnly from, DateOnly? until = null, Guid? parentRootId = null,
        string postingClass = "POSTABLE", string? roleKey = null, int type = 1, int normalSide = 1,
        string currencyMode = "BASE_ONLY", string[]? currencies = null, string? subLedgerType = null, bool requirePartner = false)
        => new
        {
            name, type, normalSide, parentRootId, postingClass, isControl = subLedgerType is not null,
            subLedgerType, requirePartner, roleKey, currency = new { mode = currencyMode, codes = currencies ?? [] },
            from, toExclusive = until
        };

    private static object GlInput(JsonElement head, string kind, Guid? root = null, object? content = null,
        Guid? previewId = null, Guid? decisionId = null, string? code = null, bool? activate = null, Guid? operation = null, JsonElement? timelineSubject = null)
        => new
        {
            operationRef = operation ?? Guid.NewGuid(), kind, rootId = root,
            expectedScopeRevision = head.GetProperty("scopeRevision").GetInt64(),
            expectedHeads = root.HasValue ? head.GetProperty("heads") : (JsonElement?)null,
            content, previewId, decisionId, code, activate, reason = "隔离环境科目专业验收",
            timelineSubject = (kind is "restore" or "approve-restore") ? timelineSubject ?? head.GetProperty("timelineReference") : (JsonElement?)null
        };

    private static async Task<Guid> GlCreate(GlClient client, string code, object content, bool activate)
    {
        var snapshot = await client.Read("/roots");
        var result = await client.Write("/commands", GlInput(snapshot, "create", content: content, code: code, activate: activate));
        return result.GetProperty("rootId").GetGuid();
    }

    private static async Task GlStep(GlClient client, Guid root, string kind, object? content = null, bool? activate = null)
        => await client.Write("/commands", GlInput(await client.Head(root), kind, root, content: content, activate: activate));

    private static Task<JsonElement> GlPreview(GlClient client, Guid root, JsonElement head)
        => client.Write($"/roots/{root:D}/publish-preview", GlPreviewInput(head));

    [CurrentPermissionPostgresFact]
    public async Task Gl_owner_exact_preview_single_use_and_current_restore_keep_original_history()
    {
        await using var h = await Seed();
        var actors = await SeedGlBoard(h, "lifecycle");
        var connection = h.Db.Database.GetConnectionString()!;
        await using var host = await AuthenticationApiProcess.Start(connection, GlHostSettings());
        await using var replica = await AuthenticationApiProcess.Start(connection, GlHostSettings());
        var maker = new GlClient(host, await Login(host, h));
        var reviewer = new GlClient(replica, await Login(replica, "gl-checker-lifecycle", "Price1!Password", h.Tenant.ToString("N")));
        await maker.AuthenticateBook();
        await reviewer.AuthenticateBook();

        var root = await GlCreate(maker, "sim-contra", GlContent("模拟备抵科目", GlBusinessDay, normalSide: 2, roleKey: "REVENUE"), true);
        var first = await maker.Head(root);
        Assert.Equal("SUSPENDED", first.GetProperty("controlState").GetString());
        Assert.Equal(JsonValueKind.Null, first.GetProperty("timeline").ValueKind);
        Assert.Equal(JsonValueKind.Null, first.GetProperty("timelineReference").ValueKind);
        Assert.Equal(JsonValueKind.Null, first.GetProperty("heads").GetProperty("timelineRevision").ValueKind);
        var version1 = first.GetProperty("proposal").GetProperty("versionId").GetGuid();
        var proposal1 = first.GetProperty("proposal").GetProperty("id").GetGuid();
        Assert.Equal(2, first.GetProperty("version").GetProperty("content").GetProperty("normalSide").GetInt32());

        await GlStep(maker, root, "submit");
        var submitted = await reviewer.Head(root);
        Assert.Equal(proposal1, submitted.GetProperty("proposal").GetProperty("id").GetGuid());
        var own = await maker.Write("/commands", GlInput(submitted, "approve", root), HttpStatusCode.Forbidden);
        Assert.Equal("MAKER_CANNOT_REVIEW", own.GetProperty("message").GetString());
        await GlStep(reviewer, root, "approve");
        var reviewed = await reviewer.Head(root);
        Assert.Equal("SUSPENDED", reviewed.GetProperty("controlState").GetString());
        var firstPreview = await GlPreview(reviewer, root, reviewed);
        Assert.Empty(firstPreview.GetProperty("before").EnumerateArray());
        Assert.True(firstPreview.GetProperty("canApply").GetBoolean());
        Assert.True(firstPreview.GetProperty("expiresAt").GetDateTimeOffset() > DateTimeOffset.UtcNow);
        Assert.Equal(JsonValueKind.Null, firstPreview.GetProperty("segments")[0].GetProperty("releaseId").ValueKind);
        var preview = firstPreview.GetProperty("previewId").GetGuid();
        var operation = Guid.NewGuid();
        var original = GlInput(reviewed, "publish", root, previewId: preview, operation: operation);
        // 两个宿主以同一复核者提交同一预览。业务操作键有意不同。
        var concurrent = await Task.WhenAll(reviewer.Send(HttpMethod.Post, "/commands", original),
            new GlClient(host, await Login(host, "gl-checker-lifecycle", "Price1!Password", h.Tenant.ToString("N")))
                { Context = reviewer.Context }.Send(HttpMethod.Post, "/commands", GlInput(reviewed, "publish", root, previewId: preview)));
        using var firstWrite = concurrent[0];
        using var secondWrite = concurrent[1];
        Assert.Single(concurrent, r => r.StatusCode == HttpStatusCode.OK);
        var denied = Assert.Single(concurrent, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal("PREVIEW_ALREADY_USED", (await GlJson(denied, HttpStatusCode.Conflict)).GetProperty("message").GetString());
        var applied = await GlJson(Assert.Single(concurrent, r => r.StatusCode == HttpStatusCode.OK), HttpStatusCode.OK);
        var same = await reviewer.Read("/commands/" + applied.GetProperty("operationRef").GetGuid());
        Assert.Equal(applied.GetRawText(), same.GetRawText());
        var published = await reviewer.Head(root);
        Assert.Equal("ENABLED", published.GetProperty("controlState").GetString());
        Assert.Equal(version1, published.GetProperty("timeline").GetProperty("segments")[0].GetProperty("versionId").GetGuid());

        var pausedReceipt = await maker.Write("/commands", GlInput(await maker.Head(root), "suspend", root));
        var oldPaused = await reviewer.Head(root);
        var restoreOld = await reviewer.Write("/commands", GlInput(oldPaused, "approve-restore", root));
        var oldDecision = restoreOld.GetProperty("decisionId").GetGuid();
        var oldRef = oldPaused.GetProperty("timelineReference");
        Assert.Equal(oldRef.GetRawText(), pausedReceipt.GetProperty("timelineReference").GetRawText());
        await GlReject(maker, "/commands", GlInput(await maker.Head(root), "successor", root,
            GlContent("模拟伪首次启用", GlBusinessDay), activate: true), HttpStatusCode.UnprocessableEntity, "INITIAL_ENABLE_NOT_FIRST_RELEASE");
        await GlReject(reviewer, $"/roots/{root:D}/publish-preview", GlPreviewInput(await reviewer.Head(root), activate: true),
            HttpStatusCode.UnprocessableEntity, "INITIAL_ENABLE_NOT_FIRST_RELEASE");

        await GlStep(maker, root, "successor", GlContent("未来期间备抵科目", GlBusinessDay.AddDays(7), GlBusinessDay.AddDays(12), normalSide: 2, roleKey: "REVENUE"), false);
        await GlStep(maker, root, "submit");
        await GlStep(reviewer, root, "approve");
        var next = await reviewer.Head(root);
        var replacing = await GlPreview(reviewer, root, next);
        Assert.Single(replacing.GetProperty("before").EnumerateArray());
        Assert.Equal(3, replacing.GetProperty("segments").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, replacing.GetProperty("segments")[1].GetProperty("releaseId").ValueKind);
        var nextPreview = replacing.GetProperty("previewId").GetGuid();
        await reviewer.Write("/commands", GlInput(next, "publish", root, previewId: nextPreview));
        var paused = await reviewer.Head(root);
        Assert.Equal("SUSPENDED", paused.GetProperty("controlState").GetString());
        var parts = paused.GetProperty("timeline").GetProperty("segments");
        Assert.Equal(3, parts.GetArrayLength());
        Assert.Equal(version1, parts[0].GetProperty("versionId").GetGuid());
        Assert.Equal(version1, parts[2].GetProperty("versionId").GetGuid());
        Assert.Equal(GlBusinessDay.AddDays(7).ToString("yyyy-MM-dd"), parts[0].GetProperty("toExclusive").GetString());
        Assert.Equal(GlBusinessDay.AddDays(12).ToString("yyyy-MM-dd"), parts[2].GetProperty("from").GetString());
        Assert.NotEqual(proposal1, paused.GetProperty("proposal").GetProperty("id").GetGuid());
        Assert.Equal(2, paused.GetProperty("timelineReference").GetProperty("revision").GetInt32());
        Assert.Equal(pausedReceipt.GetRawText(), (await maker.Read("/commands/" + pausedReceipt.GetProperty("operationRef").GetGuid())).GetRawText());
        await GlReject(reviewer, "/commands", GlInput(paused, "approve-restore", root, timelineSubject: oldRef),
            HttpStatusCode.PreconditionFailed, "TIMELINE_SUBJECT_STALE");
        var stale = await reviewer.Write("/commands", GlInput(paused, "restore", root, decisionId: oldDecision), HttpStatusCode.PreconditionFailed);
        Assert.Equal("TIMELINE_SUBJECT_STALE", stale.GetProperty("message").GetString());
        var fresh = await reviewer.Write("/commands", GlInput(paused, "approve-restore", root));
        await maker.Write("/commands", GlInput(await maker.Head(root), "restore", root, decisionId: fresh.GetProperty("decisionId").GetGuid()));
        Assert.Equal("ENABLED", (await maker.Head(root)).GetProperty("controlState").GetString());

        var historical = await reviewer.Read($"/roots/{root:D}/history?proposalId={proposal1:D}&revision=1");
        Assert.Equal(version1, historical.GetProperty("proposal").GetProperty("versionId").GetGuid());
        Assert.Empty(historical.GetProperty("allowedActions").EnumerateArray());
        await GlStep(maker, root, "retire");
        var terminal = await maker.Head(root);
        Assert.Equal("RETIRED", terminal.GetProperty("controlState").GetString());
        var retireDenied = await maker.Write("/commands", GlInput(terminal, "restore", root, decisionId: oldDecision), HttpStatusCode.Conflict);
        Assert.Equal("ROOT_RETIRED", retireDenied.GetProperty("message").GetString());

        await DeviceEvidence("wp11-01-gl-lifecycle.json", new
        {
            root, version1, MakerCannotReview = 403, OnePreviewStatuses = concurrent.Select(r => (int)r.StatusCode),
            OriginalMatches = true, newTimelinePreservesSuspension = true, originalPausePreservesTimeline = true,
            OldRestore = 412, OldTimelineSubject = 412, ActivateAfterFirst = 422, RetiredTerminal = true
        });
    }
}
