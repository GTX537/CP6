using System.Net;
using System.Text.Json;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;
using CP6.Tests.Platform;
using CP6.WebApi.Seed;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static async Task<long> GlScopeRevision(GlClient client)
        => (await client.Read("/roots")).GetProperty("scopeRevision").GetInt64();

    private async Task<(Guid Parent, Guid Child)> SeedLegacyGl(Harness h)
    {
        var legacy = new GlAccountService(h.Db);
        var group = await legacy.CreateAsync(new() { Code = "1000", Name = "模拟旧资产组", Type = AccountType.Asset,
            NormalSide = AccountSide.Debit, IsLeaf = false }, "simulated");
        var leaf = await legacy.CreateAsync(new() { Code = "1601", Name = "模拟旧备抵科目", Type = AccountType.Asset,
            NormalSide = AccountSide.Credit, ParentId = group, Level = 2, CurrencyCd = "USD" }, "simulated");
        return (group, leaf);
    }

    [CurrentPermissionPostgresFact]
    public async Task Gl_owner_template_and_legacy_sources_are_atomically_suspended_and_legacy_writes_fail()
    {
        await using var h = await Seed();
        var old = await SeedLegacyGl(h);
        var actual = await SeedGlBoard(h, "template");
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, GlHostSettings());
        var cookies = await Login(host, h);
        var maker = new GlClient(host, cookies);
        var checker = new GlClient(host, await Login(host, "gl-checker-template", "Price1!Password", h.Tenant.ToString("N")));
        await maker.AuthenticateBook();
        await checker.AuthenticateBook();

        var day = GlBusinessDay;
        var empty = await maker.Read("/roots");
        var rowCount = empty.GetProperty("rows").GetArrayLength();
        var revision = await GlScopeRevision(maker);
        object Template(params object[] rows) => new { expectedScopeRevision = revision, from = day, standardTemplate = false, rows };
        object Line(string code, string? parent = null, string postingClass = "POSTABLE")
            => new { code, parentCode = parent, content = GlContent(code, day, postingClass: postingClass) };
        await GlReject(maker, "/templates/preview", Template(Line("A"), Line("B", "MISSING")),
            HttpStatusCode.UnprocessableEntity, "TEMPLATE_PARENT_UNPROVEN");
        await GlReject(maker, "/templates/preview", Template(Line("A"), Line("a")), HttpStatusCode.Conflict, "CODE_CONFLICT");
        var tooDeep = Enumerable.Range(0, 33)
            .Select(i => Line($"D{i:D2}", i == 0 ? null : $"D{i - 1:D2}", "GROUP")).ToArray();
        await GlReject(maker, "/templates/preview", Template(tooDeep), HttpStatusCode.UnprocessableEntity, "DEPENDENCY_LIMIT");
        Assert.Equal(rowCount, (await maker.Read("/roots")).GetProperty("rows").GetArrayLength());
        Assert.Equal(revision, await GlScopeRevision(maker));

        // 使用当前 IAM 字段权拒绝整批，即使客户端已成功读取备选行。
        var field = new CP6.Entity.DomainModels.Sys.Sys_RoleFieldPerm { RoleId = 72, ResourceKey = "fin-account",
            FieldName = "Name", Access = 2 };
        h.Db.Sys_RoleFieldPerms.Add(field);
        await h.Db.SaveChangesAsync();
        await GlReject(maker, "/templates/preview", Template(Line("SIM-READONLY")),
            HttpStatusCode.Forbidden, "GL_FIELD_WRITE_DENIED");
        h.Db.Sys_RoleFieldPerms.Remove(field);
        await h.Db.SaveChangesAsync();

        var standard = await maker.Write("/templates/preview", new { expectedScopeRevision = revision, from = day, standardTemplate = true });
        Assert.InRange(standard.GetProperty("drafts").GetArrayLength(), 20, 100);
        Assert.All(standard.GetProperty("drafts").EnumerateArray(), draft =>
            Assert.Equal(JsonValueKind.Null, draft.GetProperty("content").GetProperty("currency").ValueKind));

        var stale = await maker.Write("/templates/preview", Template(Line("SIM-STALE")));
        await GlCreate(maker, "sim-other", GlContent("模拟并行草稿", day), false);
        var staleInput = GlInput(await maker.Read("/roots"), "import-template", previewId: stale.GetProperty("previewId").GetGuid());
        await GlReject(maker, "/commands", staleInput, HttpStatusCode.PreconditionFailed, "STALE");
        Assert.DoesNotContain((await maker.Read("/roots")).GetProperty("rows").EnumerateArray(), row =>
            row.GetProperty("code").GetString() == "SIM-STALE");

        var oldRows = (await maker.Read("/templates/legacy")).GetProperty("rows");
        var oldLeaf = Assert.Single(oldRows.EnumerateArray(), row => row.GetProperty("id").GetGuid() == old.Child);
        Assert.Equal(14, oldLeaf.EnumerateObject().Count() - 1);
        Assert.Equal("USD", oldLeaf.GetProperty("currencyCd").GetString());
        Assert.True(oldLeaf.GetProperty("isActive").GetBoolean());
        Assert.Equal(2, oldLeaf.GetProperty("level").GetInt32());
        revision = await GlScopeRevision(maker);
        await GlReject(maker, "/templates/preview", new { expectedScopeRevision = revision, from = day, legacyIds = new[] { old.Child } },
            HttpStatusCode.UnprocessableEntity, "TEMPLATE_PARENT_UNPROVEN");
        var mapped = await maker.Write("/templates/preview",
            new { expectedScopeRevision = revision, from = day, legacyIds = new[] { old.Parent, old.Child } });
        var applyKey = Guid.NewGuid();
        var applyRequest = GlInput(await maker.Read("/roots"), "import-template",
            previewId: mapped.GetProperty("previewId").GetGuid(), operation: applyKey);
        await h.Db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION cp6_fin_gl_v1.gl_test_reject_one_draft() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF NEW.payload::jsonb->>'name'='模拟旧备抵科目'
                THEN RAISE EXCEPTION 'SIMULATED_GL_DRAFT_STORAGE_FAILURE' USING ERRCODE='23514'; END IF;
                RETURN NEW;
            END $$;
            CREATE TRIGGER gl_test_reject_one_draft BEFORE INSERT ON cp6_fin_gl_v1.versions
                FOR EACH ROW EXECUTE FUNCTION cp6_fin_gl_v1.gl_test_reject_one_draft();
            """);
        try
        {
            await GlReject(maker, "/commands", applyRequest, HttpStatusCode.ServiceUnavailable, "GL_STORE_UNAVAILABLE");
            Assert.Single((await maker.Read("/roots")).GetProperty("rows").EnumerateArray());
            Assert.Equal("NOT_OBSERVED", (await maker.Read($"/commands/{applyKey:D}")).GetProperty("kind").GetString());
        }
        finally
        {
            await h.Db.Database.ExecuteSqlRawAsync("""
                DROP TRIGGER gl_test_reject_one_draft ON cp6_fin_gl_v1.versions;
                DROP FUNCTION cp6_fin_gl_v1.gl_test_reject_one_draft();
                """);
        }
        var drafts = await maker.Write("/commands", applyRequest);
        Assert.Equal(2, drafts.GetProperty("drafts").GetArrayLength());
        var rootList = (await maker.Read("/roots")).GetProperty("rows");
        var newParent = Assert.Single(rootList.EnumerateArray(), row => row.GetProperty("code").GetString() == "1000");
        var newLeaf = Assert.Single(rootList.EnumerateArray(), row => row.GetProperty("code").GetString() == "1601");
        var parent = newParent.GetProperty("id").GetGuid();
        var child = newLeaf.GetProperty("id").GetGuid();
        Assert.NotEqual(old.Child, child);
        var draftHead = await maker.Head(child);
        Assert.Equal("SUSPENDED", draftHead.GetProperty("controlState").GetString());
        Assert.Equal("DRAFT", draftHead.GetProperty("proposal").GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, draftHead.GetProperty("timeline").ValueKind);
        var body = draftHead.GetProperty("version").GetProperty("content");
        Assert.Equal(2, body.GetProperty("normalSide").GetInt32());
        Assert.Equal(parent, body.GetProperty("parentRootId").GetGuid());
        Assert.Equal("USD", body.GetProperty("currency").GetProperty("codes")[0].GetString());
        Assert.Equal(JsonValueKind.Null, (await maker.Head(parent)).GetProperty("version").GetProperty("content").GetProperty("currency").ValueKind);

        // 实际模板草稿经过专业复核才会发布。初次批准不启用，必须另走恢复批准。
        var qualifiedGroup = GlContent("模拟旧资产组", day, postingClass: "GROUP");
        await GlStep(maker, parent, "revise", qualifiedGroup, true);
        var firstChoice = await maker.Head(parent);
        await GlStep(maker, parent, "revise", qualifiedGroup, false);
        var finalChoice = await maker.Head(parent);
        Assert.Equal(firstChoice.GetProperty("proposal").GetProperty("revision").GetInt64() + 1,
            finalChoice.GetProperty("proposal").GetProperty("revision").GetInt64());
        var parentApproved = await GlApproved(maker, checker, parent);
        await GlReject(checker, $"/roots/{parent:D}/publish-preview", GlPreviewInput(parentApproved, activate: true),
            HttpStatusCode.UnprocessableEntity, "ACTIVATION_SUBJECT_MISMATCH");
        var validParent = await GlPreview(checker, parent, parentApproved);
        Assert.False(validParent.GetProperty("activate").GetBoolean());
        var releaseParent = await checker.Write("/commands", GlInput(parentApproved, "publish", parent,
            previewId: validParent.GetProperty("previewId").GetGuid()));
        Assert.Equal("SUSPENDED", releaseParent.GetProperty("controlState").GetString());
        var restoreParent = await checker.Write("/commands", GlInput(await checker.Head(parent), "approve-restore", parent));
        await maker.Write("/commands", GlInput(await maker.Head(parent), "restore", parent, decisionId: restoreParent.GetProperty("decisionId").GetGuid()));
        var noAuto = await GlRelease(maker, checker, child);
        Assert.Equal("SUSPENDED", noAuto.GetProperty("controlState").GetString());

        async Task LegacyDenied(HttpMethod method, string route, object? payload = null)
        {
            using var response = await host.Send(OperationsRequest(cookies, method, route, payload));
            var denied = await GlJson(response, HttpStatusCode.Conflict);
            Assert.Equal("GL_VERSION_WORKFLOW_REQUIRED", denied.GetProperty("message").GetString());
        }
        await LegacyDenied(HttpMethod.Post, "api/fin/account/import?scheme=CN-GAAP");
        await LegacyDenied(HttpMethod.Post, "api/fin/account", new GlAccount { Code = "SIM-LEGACY-WRITE", Name = "模拟旧写入" });
        await LegacyDenied(HttpMethod.Put, $"api/fin/account/{old.Child:D}", new GlAccount { Code = "1601", Name = "模拟覆盖" });
        await LegacyDenied(HttpMethod.Post, $"api/fin/account/{old.Child:D}/deactivate");
        var raw = await Assert.ThrowsAsync<PostgresException>(() => h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE public."Fin_GlAccount" SET "Name"='模拟旁路' WHERE "Id"={old.Child}
            """));
        Assert.Equal("GL_VERSION_WORKFLOW_REQUIRED", raw.MessageText);
        await A3AccountSeed.EnsureAsync(h.Db);
        FinCoaBackfillSeed.EnsureSeeded(h.Db);
        var unchanged = await h.Db.GlAccounts.AsNoTracking().OrderBy(row => row.Code).ToArrayAsync();
        Assert.Equal(2, unchanged.Length);
        Assert.Null(Assert.Single(unchanged, row => row.Id == old.Child).Role);
        Assert.Equal(noAuto.GetProperty("releaseId").GetGuid(),
            (await maker.Head(child)).GetProperty("timeline").GetProperty("releaseId").GetGuid());

        await DeviceEvidence("wp11-01-gl-legacy-template.json", new
        {
            actual.Ledger, oldFields = 14, invalidParent = 422, duplicate = 409, depth33 = 422, readOnlyField = 403,
            standardDraftCount = standard.GetProperty("drafts").GetArrayLength(), stalePreview = 412, draftCount = 2, batchRollback = 503,
            initialActivationFalse = "SUSPENDED", changedActivationPreview = 422, activatedProposalRevision = true, legacyHttpPaths = 4, directWrite = raw.SqlState, seedsLeftLegacyRows = 2
        });
    }

    [CurrentPermissionPostgresFact]
    public async Task Gl_owner_refuses_other_user_book_context_and_isolates_legal_entity_book_code()
    {
        await using var h = await Seed();
        var first = await SeedGlBoard(h, "scope");
        var other = Guid.NewGuid();
        var glEnvironment = ScopeEnvironment.ToString("D");
        string[] roles = ["REVENUE"];
        string[] currencies = ["USD"];
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_fin_mfg_v1.ledgers(tenant_id,id,environment_id,mode,legal_entity_key,ledger_key,currency,business_date)
            VALUES ({h.Tenant},{other},{glEnvironment},'REAL','SIM-OTHER-LEGAL','SIM-OTHER-BOOK','USD',{GlBusinessDay});
            INSERT INTO cp6_fin_mfg_v1.members(tenant_id,ledger_id,subject_id) VALUES ({h.Tenant},{other},{h.User});
            INSERT INTO cp6_fin_gl_v1.books(tenant_id,ledger_id,scheme,basis_key,roles,currencies,professional_available,policy_from)
            VALUES ({h.Tenant},{other},'INTL','SIM-OTHER-BASIS',{roles},{currencies},true,{GlBusinessDay})
            """);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, GlHostSettings());
        var maker = new GlClient(host, await Login(host, h));
        var checker = new GlClient(host, await Login(host, "gl-checker-scope", "Price1!Password", h.Tenant.ToString("N")));
        await checker.AuthenticateBook();
        var choices = (await maker.Read("/options")).GetProperty("choices");
        Assert.Equal(2, choices.GetArrayLength());
        var china = Assert.Single(choices.EnumerateArray(), book => book.GetProperty("scheme").GetString() == "CN-GAAP");
        var intl = Assert.Single(choices.EnumerateArray(), book => book.GetProperty("scheme").GetString() == "INTL");
        maker.Context = china.GetProperty("contextToken").GetGuid();
        var firstRoot = await GlCreate(maker, "SAME-CODE", GlContent("第一模拟法人", GlBusinessDay), false);
        maker.Context = intl.GetProperty("contextToken").GetGuid();
        var anotherRoot = await GlCreate(maker, "SAME-CODE", GlContent("第二模拟法人", GlBusinessDay), false);
        Assert.NotEqual(firstRoot, anotherRoot);
        await GlReject(maker, "/commands", GlInput(await maker.Read("/roots"), "create", code: "CROSS-PARENT",
            content: GlContent("跨账套父标识", GlBusinessDay, parentRootId: firstRoot), activate: false),
            HttpStatusCode.NotFound, "NOT_FOUND_OR_NOT_VISIBLE");
        checker.Context = maker.Context;
        using var otherActor = await checker.Send(HttpMethod.Get, "/roots");
        Assert.Equal("LEDGER_CONTEXT_UNPROVEN", GlMessage(await GlJson(otherActor, HttpStatusCode.NotFound), "message"));
        using var otherRoot = await maker.Send(HttpMethod.Get, $"/roots/{firstRoot:D}/workflow");
        Assert.Equal("NOT_FOUND_OR_NOT_VISIBLE", GlMessage(await GlJson(otherRoot, HttpStatusCode.NotFound), "message"));
        maker.Context = Guid.Empty;
        using var absent = await maker.Send(HttpMethod.Get, "/roots");
        Assert.Equal("LEDGER_CONTEXT_UNPROVEN", GlMessage(await GlJson(absent, HttpStatusCode.NotFound), "message"));

        await DeviceEvidence("wp11-01-gl-scopes.json", new
        {
            first.Ledger, other, code = "SAME-CODE", firstRoot, anotherRoot, actorMismatch = 404, crossRoot = 404, unprovenContext = 404
        });
    }
}
