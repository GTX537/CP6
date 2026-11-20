using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Common;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static readonly string[] WorkspaceRoles =
    [
        "Customer", "AccountsReceivable", "Billing", "Receipt", "Delivery", "Supplier", "AccountsPayable",
        "PaymentSchedule", "Payment", "CreditMgmt", "Maker", "PaidSupply", "RebuyObligation"
    ];

    private static readonly string[] WorkspaceSlots =
    [
        "AP_SCHEDULE", "AR_BILLING", "BILLING_CREDIT_AR", "BILLING_RECEIPT", "CUSTOMER_AR", "CUSTOMER_CREDIT",
        "PARENT_CUSTOMER", "SCHEDULE_PAYEE", "SUPPLIER_AP"
    ];

    private static async Task ProvisionWorkspace(Harness h, bool alternate = false, bool admin = false, string primaryLegal = "JP-A")
    {
        await GrantBusinessPartnerIdentity(h);
        await GrantPartnerActions(h, "profile-query", "add", "edit", "register", "status", "duplicate-review", "history",
            "command-result.read", "sensitive.read", "sensitive.write");
        if (admin)
        {
            await GrantPartnerActions(h, "del");
            await GrantRetirementAdmin(h);
        }
        await h.Db.Sys_RoleDataScopes.Where(scope => scope.ResourceKey == PartnerResource)
            .ExecuteUpdateAsync(set => set.SetProperty(scope => scope.ScopeType, 5));

        foreach (var (legal, site) in alternate ? new[] { (primaryLegal, "BPA"), ("JP-B", "BPB") } : new[] { (primaryLegal, "BPA") })
        {
            h.Db.MasterBases.Add(new MasterBase { BaseCd = site, BaseName = "模拟经营据点 " + site });
            await h.Db.SaveChangesAsync();
            await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO main_bp.scopes VALUES({h.Tenant},{legal},{legal},true,1,{"BP-NORM-U15.0-1"},true);
                INSERT INTO main_bp.bases VALUES({h.Tenant},{legal},{site},true,'1');
                INSERT INTO main_bp.base_grants VALUES({h.Tenant},{legal},{site},72,true,true);
                """);
            foreach (var (field, value) in new[] { ("commercialCurrency", "JPY"), ("salesStaffCd", "STA"), ("businessStaffCd", "STB"),
                ("bpClass01", "CLASS-A"), ("supplierPattern", "5"), ("logisticsGroupCd", "LOG-A"),
                ("paymentScheduleDeptCd", "DEP") })
            {
                var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(field + "/" + value))).ToLowerInvariant();
                await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO main_bp.option_values VALUES({h.Tenant},{legal},{field},{value},{value},
                      {BusinessPartnerWorkspaceV1.DirectoryOwner},{field + "/" + value},'1',{digest},true)
                    """);
            }
        }
        h.Db.ChangeTracker.Clear();
    }

    private sealed record WorkspaceResponse(JsonElement Body, string? ETag, int Status);

    private static async Task<WorkspaceResponse> WorkspaceCall(AuthenticationApiProcess host, Dictionary<string, string> cookies,
        string suffix, HttpMethod? method = null, object? input = null, string? etag = null, Guid? key = null, int? status = 200,
        bool csrf = true, Guid? financeContext = null)
    {
        using var req = OperationsRequest(cookies, method ?? HttpMethod.Get, FormalPartnerApi + suffix, input, csrf);
        if (etag is not null) req.Headers.TryAddWithoutValidation("If-Match", etag);
        if (key is not null) req.Headers.TryAddWithoutValidation("Idempotency-Key", key.Value.ToString("D"));
        if (financeContext is not null) req.Headers.Add("X-Finance-Context", financeContext.Value.ToString("D"));
        using var response = await host.Send(req);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.True(status is null || (int)response.StatusCode == status, $"{suffix} {response.StatusCode} {raw}");
        return new(await FormalPartnerJson(response), response.Headers.ETag?.ToString(), (int)response.StatusCode);
    }

    private static object WorkspaceInput(WorkspaceResponse options, string code, string name, object? choice = null,
        string registration = "PRE_REGISTERED", bool complete = false, string legal = "JP-A", string site = "BPA",
        Dictionary<string, object?>? overrides = null, string[]? useRoles = null, string[]? useSelf = null,
        IReadOnlyDictionary<string, Guid>? useTargets = null)
    {
        var rules = options.Body.GetProperty("fieldRules");
        Assert.Equal(128, rules.GetArrayLength());
        var profile = rules.EnumerateArray().ToDictionary(rule => rule.GetProperty("field").GetString()!,
            rule => rule.GetProperty("type").GetString() == "BOOL" ? (object?)false : null);
        profile["tel"] = "+81-12-3456";
        if (complete)
        {
            profile["salesStaffCd"] = "STA";
            profile["businessStaffCd"] = "STB";
        }
        foreach (var (field, value) in overrides ?? []) profile[field] = value;
        var chosen = useRoles ?? ["Customer", "AccountsReceivable", "Billing", "Receipt"];
        var self = useSelf ?? ["CUSTOMER_AR", "AR_BILLING", "BILLING_RECEIPT"];
        var roles = WorkspaceRoles.ToDictionary(role => role, role => chosen.Contains(role, StringComparer.Ordinal));
        var relations = WorkspaceSlots.Select(slot => new
        {
            slot, target = useTargets is not null && useTargets.TryGetValue(slot, out var id) ? (object)new { kind = "BP", bpId = id }
                : self.Contains(slot, StringComparer.Ordinal) ? new { kind = "SELF" } : null
        });
        var financeLinks = new[] { "CREDIT_MASTER", "PAYMENT_BANK", "RECEIPT_BANK" }.Select(kind => new { kind, reference = (object?)null });
        return new { legalEntityKey = legal, registration, identity = new { bpCd = code, bpName = name, bpAbbrev = (string?)null,
            baseCd = site, commercialCurrency = "JPY", roles, profile, relations, financeLinks },
            duplicateDecision = choice ?? new { kind = "NEW_DISTINCT", acknowledged = Array.Empty<object>(), reason = (string?)null } };
    }

    private static Task<WorkspaceResponse> WorkspacePreview(AuthenticationApiProcess host, Dictionary<string, string> cookies, object input, int status = 200)
        => WorkspaceCall(host, cookies, "/create-previews", HttpMethod.Post, input, status: status);

    private static Task<WorkspaceResponse> ApplyWorkspace(AuthenticationApiProcess host, Dictionary<string, string> cookies,
        WorkspaceResponse preview, Guid key, int? status = 200, string path = "/create", Guid? financeContext = null)
        => WorkspaceCall(host, cookies, path, HttpMethod.Post, new { preview = preview.Body.GetProperty("reference") }, preview.ETag, key, status,
            financeContext: financeContext);

    private static Guid PartnerId(WorkspaceResponse result) => result.Body.GetProperty("receipt").GetProperty("bp").GetProperty("bpId").GetGuid();
    private static string CurrentRevision(WorkspaceResponse result) => result.Body.GetProperty("state").GetProperty("revision").GetString()!;

    private static Task<WorkspaceResponse> WorkspaceStatus(AuthenticationApiProcess host, Dictionary<string, string> cookies, Guid bp)
        => WorkspaceCall(host, cookies, $"/{bp:D}/status-context");

    private static Task<WorkspaceResponse> StateCommand(AuthenticationApiProcess host, Dictionary<string, string> cookies, Guid bp,
        WorkspaceResponse state, string action, Guid key, int status = 200, string reason = "模拟当前状态操作", bool csrf = true,
        Guid? financeContext = null)
    {
        var body = new Dictionary<string, object> { ["bpId"] = bp, ["expectedRevision"] = CurrentRevision(state), ["reason"] = reason };
        if (action is "freeze" or "unfreeze") body["hold"] = action == "freeze" ? "FROZEN" : "CLEAR";
        if (action == "restore") body["confirmRegistration"] = false;
        var endpoint = action is "freeze" or "unfreeze" ? "hold" : action;
        return WorkspaceCall(host, cookies, "/" + endpoint, HttpMethod.Post, body, state.ETag, key, status, csrf, financeContext);
    }

    private static async Task<int[]> FormalCounts(Harness h)
        => await h.Db.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM main_bp.heads WHERE tenant_id={h.Tenant}
            UNION ALL SELECT count(*)::int FROM main_bp.profile_revisions WHERE tenant_id={h.Tenant}
            UNION ALL SELECT count(*)::int FROM main_bp.relations WHERE tenant_id={h.Tenant}
            UNION ALL SELECT count(*)::int FROM main_bp.commands WHERE tenant_id={h.Tenant}
            UNION ALL SELECT count(*)::int FROM main_bp.audit WHERE tenant_id={h.Tenant}
            UNION ALL SELECT count(*)::int FROM main_bp.state_history WHERE tenant_id={h.Tenant}
            """).ToArrayAsync();

    [CurrentPermissionPostgresFact]
    public async Task Business_partner_wp04_01_create_previews_keep_identities_and_restore_original_results()
    {
        await using var h = await Seed();
        await ProvisionWorkspace(h, alternate: true);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h);
        var options = await WorkspaceCall(host, cookies, "/options?legalEntityKey=JP-A");
        Assert.Equal(WorkspaceRoles, options.Body.GetProperty("roles").EnumerateArray().Select(x => x.GetString()));
        var draft = WorkspaceInput(options, "BP-01", "ＡＣＭＥ　会社", overrides: new()
        {
            ["slitterBillingUnitPrice"] = "123456789.1234", ["bpClass01"] = "CLASS-A"
        });
        var preview = await WorkspacePreview(host, cookies, draft);
        Assert.True(preview.Body.GetProperty("canApply").GetBoolean(), preview.Body.GetRawText());
        Assert.StartsWith("\"BP-PREVIEW/1:", preview.ETag);
        Assert.Equal(new[] { 0, 0, 0, 0, 0, 0 }, await FormalCounts(h));
        var key = Guid.NewGuid();
        var first = await ApplyWorkspace(host, cookies, preview, key);
        var id = PartnerId(first);
        Assert.Equal("1", first.Body.GetProperty("receipt").GetProperty("bp").GetProperty("revision").GetString());
        var detail = await WorkspaceCall(host, cookies, $"/{id:D}");
        Assert.Equal(128, detail.Body.GetProperty("profile").EnumerateObject().Count());
        Assert.Equal(15, detail.Body.GetProperty("legacyFields").GetArrayLength());
        Assert.Equal("123456789.1234", detail.Body.GetProperty("profile").GetProperty("slitterBillingUnitPrice").GetString());
        Assert.Equal(WorkspaceSlots, detail.Body.GetProperty("relations").EnumerateArray().Select(x => x.GetProperty("slot").GetString()));
        foreach (var slot in detail.Body.GetProperty("relations").EnumerateArray()
            .Where(x => x.GetProperty("slot").GetString() is "CUSTOMER_AR" or "AR_BILLING" or "BILLING_RECEIPT"))
            Assert.Equal(id, slot.GetProperty("target").GetProperty("bpId").GetGuid());
        Assert.Equal(new[] { 1, 1, 9, 1, 1, 1 }, await FormalCounts(h));
        var samePreview = await ApplyWorkspace(host, cookies, preview, Guid.NewGuid(), 409);
        Assert.Equal("PREVIEW_ALREADY_USED", samePreview.Body.GetProperty("code").GetString());

        var sameName = await WorkspacePreview(host, cookies, WorkspaceInput(options, "BP-02", "acme 会社"));
        Assert.False(sameName.Body.GetProperty("canApply").GetBoolean());
        var candidate = Assert.Single(sameName.Body.GetProperty("candidates").GetProperty("items").EnumerateArray());
        Assert.Equal(id, candidate.GetProperty("reference").GetProperty("bpId").GetGuid());
        var acknowledged = candidate.GetProperty("reference");
        var secondPreview = await WorkspacePreview(host, cookies, WorkspaceInput(options, "BP-02", "acme 会社",
            new { kind = "NEW_DISTINCT", acknowledged = new[] { acknowledged }, reason = "模拟两个独立主体" }));
        Assert.True(secondPreview.Body.GetProperty("canApply").GetBoolean(), secondPreview.Body.GetRawText());
        var second = await ApplyWorkspace(host, cookies, secondPreview, Guid.NewGuid());
        Assert.NotEqual(id, PartnerId(second));
        var reuse = await WorkspacePreview(host, cookies, WorkspaceInput(options, "BP-03", "acme 会社",
            new { kind = "USE_EXISTING", reference = acknowledged }));
        Assert.True(reuse.Body.GetProperty("canApply").GetBoolean(), reuse.Body.GetRawText());
        var used = await ApplyWorkspace(host, cookies, reuse, Guid.NewGuid());
        Assert.Equal("NO_CHANGE", used.Body.GetProperty("receipt").GetProperty("outcome").GetString());
        Assert.Equal(id, PartnerId(used));
        Assert.Equal(new[] { 2, 2, 18, 3, 3, 2 }, await FormalCounts(h));

        var state = await WorkspaceStatus(host, cookies, id);
        await StateCommand(host, cookies, id, state, "freeze", Guid.NewGuid());
        var replay = await ApplyWorkspace(host, cookies, preview, key);
        Assert.True(replay.Body.GetProperty("replayed").GetBoolean());
        Assert.Equal(first.Body.GetProperty("receipt").GetRawText(), replay.Body.GetProperty("receipt").GetRawText());
        var wrong = await ApplyWorkspace(host, cookies, reuse, key, 409);
        Assert.Equal("COMMAND_IDEMPOTENCY_CONFLICT", wrong.Body.GetProperty("code").GetString());
        var changedTag = await WorkspaceCall(host, cookies, "/create", HttpMethod.Post, new { preview = preview.Body.GetProperty("reference") },
            reuse.ETag, key, 409);
        Assert.Equal("COMMAND_IDEMPOTENCY_CONFLICT", changedTag.Body.GetProperty("code").GetString());
        var current = await WorkspaceStatus(host, cookies, id);
        Assert.Equal("2", CurrentRevision(current));
        Assert.Equal("FROZEN", current.Body.GetProperty("state").GetProperty("hold").GetString());
        var other = await WorkspaceCall(host, cookies, "/options?legalEntityKey=JP-B");
        var competing = await WorkspacePreview(host, cookies,
            WorkspaceInput(other, "BP-01", "仅另法人可读", legal: "JP-B", site: "BPB"));
        Assert.False(competing.Body.GetProperty("canApply").GetBoolean());
        Assert.Contains(competing.Body.GetProperty("issues").EnumerateArray(), x => x.GetProperty("code").GetString() == "BP_CODE_EXISTS");
        Assert.Empty(competing.Body.GetProperty("candidates").GetProperty("items").EnumerateArray());
        var crossLegal = await ApplyWorkspace(host, cookies, competing, Guid.NewGuid(), 409);
        Assert.Equal("BP_CODE_EXISTS", crossLegal.Body.GetProperty("code").GetString());
        Assert.DoesNotContain(id.ToString("D"), crossLegal.Body.GetRawText());
        var secondId = PartnerId(second);
        await StateCommand(host, cookies, secondId, await WorkspaceStatus(host, cookies, secondId), "retire", Guid.NewGuid());
        var retiredCode = await WorkspacePreview(host, cookies, WorkspaceInput(options, "BP-02", "退休伙伴编号不得重用"));
        Assert.False(retiredCode.Body.GetProperty("canApply").GetBoolean());
        var historical = Assert.Single(retiredCode.Body.GetProperty("candidates").GetProperty("items").EnumerateArray());
        Assert.Equal(secondId, historical.GetProperty("reference").GetProperty("bpId").GetGuid());
        Assert.Equal("RETIRED", historical.GetProperty("lifecycle").GetString());
        Assert.Equal("BP_CODE_EXISTS", (await ApplyWorkspace(host, cookies, retiredCode, Guid.NewGuid(), 409)).Body.GetProperty("code").GetString());
        await h.Db.Sys_RoleActions.Where(row => row.MenuId == 212 && row.RoleId == 72 &&
            (row.ActionCode == "add" || row.ActionCode == "register")).ExecuteDeleteAsync();
        var afterRevocation = await ApplyWorkspace(host, cookies, preview, key);
        Assert.True(afterRevocation.Body.GetProperty("replayed").GetBoolean());
        Assert.Equal(first.Body.GetProperty("receipt").GetRawText(), afterRevocation.Body.GetProperty("receipt").GetRawText());
        Assert.Equal(new[] { 2, 4, 18, 5, 5, 4 }, await FormalCounts(h));
        await DeviceEvidence("wp04-01-create.json", new { id, second = PartnerId(second), fields = 128, originalFields = 143, slots = 9,
            scope = "JP-A", previewWrites = 0, sameNameNewDistinct = 1, reuse = "NO_CHANGE", crossLegalCode = "BP_CODE_EXISTS",
            historicalCodeReserved = true, differentIfMatch = 409, createRightRevokedReplay = true,
            replayOriginal = true, currentAfterOriginalRead = "FROZEN", counts = await FormalCounts(h) });
    }

    [CurrentPermissionPostgresFact]
    public async Task Business_partner_wp04_01_revalidates_writes_and_preserves_unicode_history()
    {
        await using var h = await Seed();
        await ProvisionWorkspace(h);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h);
        var options = await WorkspaceCall(host, cookies, "/options?legalEntityKey=JP-A");
        var draft = WorkspaceInput(options, "BP-U15", new string('\ufdfa', 100));
        var first = await WorkspacePreview(host, cookies, draft);
        var second = await WorkspacePreview(host, cookies, draft);
        Assert.True(first.Body.GetProperty("canApply").GetBoolean());
        Assert.True(second.Body.GetProperty("canApply").GetBoolean());
        var race = await Task.WhenAll(ApplyWorkspace(host, cookies, first, Guid.NewGuid(), null),
            ApplyWorkspace(host, cookies, second, Guid.NewGuid(), null));
        var winner = Assert.Single(race, x => x.Status == 200);
        var loser = Assert.Single(race, x => x.Status == 409);
        Assert.Equal("BP_CODE_EXISTS", loser.Body.GetProperty("code").GetString());
        var id = PartnerId(winner);
        Assert.Equal(new[] { 1, 1, 9, 1, 1, 1 }, await FormalCounts(h));
        var current = await WorkspaceCall(host, cookies, $"/{id:D}");
        Assert.Equal(new string('\ufdfa', 100), current.Body.GetProperty("name").GetString());
        var normalized = await h.Db.Database.SqlQuery<string>($"""
            SELECT name_norm AS "Value" FROM main_bp.normalized_identity WHERE tenant_id={h.Tenant} AND bp_id={id}
            """).SingleAsync();
        Assert.Equal(1800, normalized.Length);
        Assert.Equal(BusinessPartnerUnicode15.Normalize(new string('\ufdfa', 100)), normalized);
        var exact = await WorkspacePreview(host, cookies, WorkspaceInput(options, "BP-EXACT", new string('\ufdfa', 100)));
        Assert.Equal(id, Assert.Single(exact.Body.GetProperty("candidates").GetProperty("items").EnumerateArray())
            .GetProperty("reference").GetProperty("bpId").GetGuid());

        var artificialHash = SHA256.HashData(Encoding.UTF8.GetBytes("collision"));
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE main_bp.normalized_identity SET name_hash={artificialHash} WHERE tenant_id={h.Tenant} AND bp_id={id}
            """);
        var collision = await WorkspacePreview(host, cookies, WorkspaceInput(options, "BP-COLLISION", "collision"));
        Assert.True(collision.Body.GetProperty("canApply").GetBoolean());
        Assert.Empty(collision.Body.GetProperty("candidates").GetProperty("items").EnumerateArray());
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE main_bp.scopes SET norm_version='模拟旧版本',revision=revision+1 WHERE tenant_id={h.Tenant} AND legal_entity_key='JP-A'
            """);
        var stale = await ApplyWorkspace(host, cookies, collision, Guid.NewGuid(), 412);
        Assert.Equal("PREVIEW_STALE", stale.Body.GetProperty("code").GetString());
        var unproven = await WorkspacePreview(host, cookies, WorkspaceInput(options, "BP-U15B", "另一名称"));
        Assert.False(unproven.Body.GetProperty("canApply").GetBoolean());
        Assert.Equal("REVIEW_REQUIRED", unproven.Body.GetProperty("candidates").GetProperty("visibility").GetString());
        Assert.Contains(unproven.Body.GetProperty("issues").EnumerateArray(), x => x.GetProperty("code").GetString() == "REFERENCE_UNPROVEN");
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE main_bp.scopes SET norm_version={BusinessPartnerUnicode15.Version},revision=revision+1
            WHERE tenant_id={h.Tenant} AND legal_entity_key='JP-A'
            """);

        foreach (var (field, value, error) in new (string, object, string)[]
        {
            ("bpId", Guid.NewGuid(), "BP_SCOPE_IMMUTABLE"), ("legalEntityKey", "OTHER", "BP_SCOPE_IMMUTABLE"),
            ("bpCd", "OTHER", "BP_SCOPE_IMMUTABLE"), ("customerFlg", false, "ROLE_IMMUTABLE"), ("supplierFlg", true, "ROLE_IMMUTABLE"),
            ("paidEachTimeFlg", "false", "FIELD_INVALID"), ("slitterBillingUnitPrice", 1.2m, "FIELD_INVALID"),
            ("tel", new string('1', 70), "FIELD_INVALID"), ("unknownProperty", 1, "UNKNOWN_FIELD"),
            ("remittanceAccount", "INVALID", "OWNER_FIELD_READ_ONLY"), ("crmAccountId", Guid.NewGuid(), "OWNER_FIELD_READ_ONLY"),
            ("status", 1, "OWNER_FIELD_READ_ONLY")
        })
        {
            var result = await WorkspaceCall(host, cookies, "/profile-previews", HttpMethod.Post,
                new { bpId = id, expectedRevision = "1", changes = new[] { new { field, value } }, relationChanges = Array.Empty<object>(),
                    financeLinkChanges = Array.Empty<object>(), reason = "模拟无效字段" }, status: 400);
            Assert.Equal(error, result.Body.GetProperty("code").GetString());
        }

        foreach (var roles in new[] { Array.Empty<string>(), new[] { "CreditMgmt", "Maker" } })
        {
            var refused = await WorkspacePreview(host, cookies, WorkspaceInput(options, "BP-ROLE", "缺主角色", useRoles: roles), 400);
            Assert.Equal("ROLE_SET_INVALID", refused.Body.GetProperty("code").GetString());
        }
        var incomplete = await WorkspacePreview(host, cookies, WorkspaceInput(options, "BP-REQUIRED", "注册资料缺少员工和关系",
            registration: "REGISTERED", useSelf: ["AR_BILLING", "BILLING_RECEIPT"]));
        Assert.False(incomplete.Body.GetProperty("canApply").GetBoolean());
        var issues = incomplete.Body.GetProperty("issues").EnumerateArray().Where(x => x.GetProperty("code").GetString() == "PROFILE_INCOMPLETE")
            .Select(x => x.GetProperty("field").GetString()).ToArray();
        Assert.Contains("CUSTOMER_AR", issues);
        Assert.Contains("salesStaffCd", issues);
        await ApplyWorkspace(host, cookies, incomplete, Guid.NewGuid(), 422);

        var prereg = WorkspaceInput(options, "BP-PREREG", "可预登记缺完整性", useSelf: ["AR_BILLING", "BILLING_RECEIPT"]);
        await h.Db.Sys_RoleActions.Where(row => row.MenuId == 212 && row.RoleId == 72 && row.ActionCode == "register").ExecuteDeleteAsync();
        var currentOptions = await WorkspaceCall(host, cookies, "/options?legalEntityKey=JP-A");
        Assert.Equal("PRE_REGISTERED", Assert.Single(Assert.Single(currentOptions.Body.GetProperty("bases").EnumerateArray())
            .GetProperty("createModes").EnumerateArray()).GetString());
        await WorkspacePreview(host, cookies, WorkspaceInput(options, "BP-NOREG", "当前无注册能力", registration: "REGISTERED", complete: true), 403);
        var allowed = await WorkspacePreview(host, cookies, prereg);
        Assert.True(allowed.Body.GetProperty("canApply").GetBoolean());
        var resultId = PartnerId(await ApplyWorkspace(host, cookies, allowed, Guid.NewGuid()));
        var preregDetail = await WorkspaceCall(host, cookies, $"/{resultId:D}");
        Assert.Contains(preregDetail.Body.GetProperty("issues").EnumerateArray(), x => x.GetProperty("field").GetString() == "CUSTOMER_AR");
        var invalidDraft = WorkspaceInput(options, "BP-FORMAT", "已填字段仍需合法", overrides: new() { ["tel"] = "BAD" });
        await WorkspacePreview(host, cookies, invalidDraft, 400);
        Assert.Equal(new[] { 2, 2, 18, 2, 2, 2 }, await FormalCounts(h));
        await DeviceEvidence("wp04-01-constraints.json", new { id, originalLength = 100, normalizedLength = normalized.Length,
            exactCandidate = true, collisionComparedFullText = true, versionUnproven = true, stale = stale.Status,
            concurrentCreate = race.Select(response => response.Status).Order().ToArray(), invalidFields = 12,
            registeredRequirements = issues, preregWithoutRegister = true, counts = await FormalCounts(h) });
    }

    [CurrentPermissionPostgresFact]
    public async Task Business_partner_wp04_01_persists_all_relation_slots_and_rejects_unproven_targets()
    {
        await using var h = await Seed();
        await ProvisionWorkspace(h, alternate: true);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h);
        var options = await WorkspaceCall(host, cookies, "/options?legalEntityKey=JP-A");
        var parentPreview = await WorkspacePreview(host, cookies, WorkspaceInput(options, "BP-PARENT", "模拟独立母伙伴",
            useRoles: ["Customer"], useSelf: []));
        var parent = PartnerId(await ApplyWorkspace(host, cookies, parentPreview, Guid.NewGuid()));
        var childDraft = WorkspaceInput(options, "BP-ALL", "模拟九槽伙伴", useRoles: WorkspaceRoles.Take(11).ToArray(),
            useSelf: WorkspaceSlots.Where(slot => slot != "PARENT_CUSTOMER").ToArray(), useTargets: new Dictionary<string, Guid> { ["PARENT_CUSTOMER"] = parent },
            overrides: new() { ["supplierPattern"] = "5", ["deliveryConfirmFlg"] = true, ["paymentClosingDay1"] = 99,
                ["deliveryTimeFrom"] = "08:00:00", ["deliveryTimeTo"] = "18:15:00" });
        var preview = await WorkspacePreview(host, cookies, childDraft);
        Assert.True(preview.Body.GetProperty("canApply").GetBoolean(), preview.Body.GetRawText());
        var id = PartnerId(await ApplyWorkspace(host, cookies, preview, Guid.NewGuid()));
        var detail = await WorkspaceCall(host, cookies, $"/{id:D}");
        var relations = detail.Body.GetProperty("relations").EnumerateArray().ToArray();
        Assert.Equal(WorkspaceSlots, relations.Select(item => item.GetProperty("slot").GetString()));
        foreach (var relation in relations)
        {
            Assert.Equal("AVAILABLE", relation.GetProperty("availability").GetString());
            Assert.Equal(relation.GetProperty("slot").GetString() == "PARENT_CUSTOMER" ? parent : id,
                relation.GetProperty("target").GetProperty("bpId").GetGuid());
        }
        Assert.Equal(99, detail.Body.GetProperty("profile").GetProperty("paymentClosingDay1").GetInt32());
        Assert.Equal("18:15:00", detail.Body.GetProperty("profile").GetProperty("deliveryTimeTo").GetString());

        var alternate = await WorkspaceCall(host, cookies, "/options?legalEntityKey=JP-B");
        var crossLegal = await WorkspacePreview(host, cookies, WorkspaceInput(alternate, "BP-BAD-L", "模拟跨法人目标", legal: "JP-B", site: "BPB",
            useRoles: ["Customer"], useSelf: [], useTargets: new Dictionary<string, Guid> { ["PARENT_CUSTOMER"] = parent }));
        Assert.Contains(crossLegal.Body.GetProperty("issues").EnumerateArray(), issue => issue.GetProperty("code").GetString() == "RELATION_SCOPE_MISMATCH");
        var missingRole = await WorkspacePreview(host, cookies, WorkspaceInput(options, "BP-BAD-R", "模拟缺角色目标",
            useRoles: ["Customer"], useSelf: [], useTargets: new Dictionary<string, Guid> { ["CUSTOMER_AR"] = parent }));
        Assert.Contains(missingRole.Body.GetProperty("issues").EnumerateArray(), issue => issue.GetProperty("code").GetString() == "REFERENCE_INVALID");
        var badSelf = await WorkspacePreview(host, cookies, WorkspaceInput(options, "BP-BAD-SELF", "模拟缺 AR 的自关系",
            useRoles: ["Customer"], useSelf: ["CUSTOMER_AR"]));
        Assert.False(badSelf.Body.GetProperty("canApply").GetBoolean());
        Assert.Contains(badSelf.Body.GetProperty("issues").EnumerateArray(), issue => issue.GetProperty("code").GetString() == "REFERENCE_INVALID");
        var cycle = await WorkspaceCall(host, cookies, "/profile-previews", HttpMethod.Post, new
        {
            bpId = parent, expectedRevision = "1", changes = Array.Empty<object>(), financeLinkChanges = Array.Empty<object>(),
            relationChanges = new[] { new { slot = "PARENT_CUSTOMER", target = new { kind = "BP", bpId = id } } }, reason = "模拟父伙伴成环"
        });
        Assert.Contains(cycle.Body.GetProperty("issues").EnumerateArray(), issue => issue.GetProperty("code").GetString() == "RELATION_CYCLE");
        var invalidTime = await WorkspacePreview(host, cookies, WorkspaceInput(options, "BP-BAD-TIME", "模拟异常时间窗",
            overrides: new() { ["deliveryTimeFrom"] = "20:00:00", ["deliveryTimeTo"] = "08:00:00" }));
        Assert.False(invalidTime.Body.GetProperty("canApply").GetBoolean());
        Assert.Contains(invalidTime.Body.GetProperty("issues").EnumerateArray(), issue => issue.GetProperty("code").GetString() == "TIME_WINDOW_INVALID");
        var wrongPattern = await WorkspacePreview(host, cookies, WorkspaceInput(options, "BP-BAD-PATTERN", "模拟非法外协资料",
            useRoles: ["Supplier"], useSelf: [], overrides: new() { ["supplierPattern"] = "4", ["deliveryConfirmFlg"] = true, ["subcontractTargetFlg"] = true }));
        Assert.Contains(wrongPattern.Body.GetProperty("issues").EnumerateArray(), issue => issue.GetProperty("field").GetString() == "supplierPattern" &&
            issue.GetProperty("code").GetString() == "FIELD_INVALID");
        Assert.Equal(new[] { 2, 2, 18, 2, 2, 2 }, await FormalCounts(h));
        await DeviceEvidence("wp04-01-relations.json", new { id, parent, slots = WorkspaceSlots, persistedTargets = 9,
            crossLegal = "RELATION_SCOPE_MISMATCH", missingRole = "REFERENCE_INVALID", cycle = "RELATION_CYCLE", clock = "TIME_WINDOW_INVALID",
            closingDay = 99, counts = await FormalCounts(h) });
    }

    [CurrentPermissionPostgresFact]
    public async Task Business_partner_wp04_01_validates_exact_finance_owner_within_profile_and_registration_transactions()
    {
        const string legal = "SIM-LEGAL";
        await using var h = await Seed();
        await ProvisionWorkspace(h, primaryLegal: legal);
        await GrantPartnerActions(h, BusinessPartnerFinanceReferenceReader.ReadAction);
        var actors = await SeedGlBoard(h, "bp04-workspace");
        var scope = new ManufacturingFinanceScopeV1(h.Db, ScopeEnvironment.ToString("D"));
        var context = await scope.IssueContextAsync(actors.Ledger, h.User, default);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, GlHostSettings());
        var cookies = await Login(host, h);
        var options = await WorkspaceCall(host, cookies, "/options?legalEntityKey=" + legal);
        var original = await WorkspacePreview(host, cookies, WorkspaceInput(options, "BP-FIN-01", "模拟财务引用伙伴", complete: true, legal: legal));
        var id = PartnerId(await ApplyWorkspace(host, cookies, original, Guid.NewGuid()));
        var source = await SimulatedBankPublication(h, actors.Ledger, Guid.NewGuid(), Guid.NewGuid(), 1, "****0456");
        await RegisterFinanceScope(h, actors.Ledger, source, id);
        await StoreFinanceLink(h, id, actors.Ledger, BusinessPartnerFinanceReferenceReader.PaymentBank, source);

        var edit = await WorkspaceCall(host, cookies, "/profile-previews", HttpMethod.Post, new
        {
            bpId = id, expectedRevision = "1", changes = Array.Empty<object>(), relationChanges = Array.Empty<object>(),
            financeLinkChanges = new[] { new { kind = "PAYMENT_BANK", reference = source } }, reason = "模拟接入既有准确银行引用"
        }, financeContext: context);
        Assert.True(edit.Body.GetProperty("canApply").GetBoolean(), edit.Body.GetRawText());
        await ApplyWorkspace(host, cookies, edit, Guid.NewGuid(), path: "/profile-apply", financeContext: context);
        var detail = await WorkspaceCall(host, cookies, $"/{id:D}", financeContext: context);
        var exact = Assert.Single(detail.Body.GetProperty("finance").EnumerateArray(), item => item.GetProperty("kind").GetString() == "PAYMENT_BANK");
        Assert.Equal("AVAILABLE", exact.GetProperty("availability").GetString());
        Assert.Equal(source.Version, exact.GetProperty("reference").GetProperty("version").GetString());
        Assert.Equal(source.Digest, exact.GetProperty("reference").GetProperty("digest").GetString());
        Assert.Equal("****0456", exact.GetProperty("maskedAccount").GetString());

        await using var offline = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await offline.OpenAsync();
        await using var blockSource = await offline.BeginTransactionAsync();
        await using (var lockSource = new NpgsqlCommand("LOCK TABLE cp6_bp_finance_ref_v1.frontiers IN ACCESS EXCLUSIVE MODE", offline, blockSource))
            await lockSource.ExecuteNonQueryAsync();
        await StateCommand(host, cookies, id, detail, "freeze", Guid.NewGuid(), financeContext: context);
        var frozen = await WorkspaceStatus(host, cookies, id);
        var denied = await StateCommand(host, cookies, id, frozen, "register", Guid.NewGuid(), 422, financeContext: context);
        Assert.Equal("REFERENCE_UNPROVEN", denied.Body.GetProperty("code").GetString());
        var missing = await WorkspaceCall(host, cookies, $"/{id:D}", financeContext: context);
        Assert.Equal("UNPROVEN", Assert.Single(missing.Body.GetProperty("finance").EnumerateArray(),
            item => item.GetProperty("kind").GetString() == "PAYMENT_BANK").GetProperty("availability").GetString());
        Assert.Equal(new[] { 1, 3, 9, 3, 3, 2 }, await FormalCounts(h));
        await blockSource.RollbackAsync();

        await StateCommand(host, cookies, id, frozen, "register", Guid.NewGuid(), financeContext: context);
        var registered = await WorkspaceStatus(host, cookies, id);
        Assert.Equal("REGISTERED", registered.Body.GetProperty("state").GetProperty("registration").GetString());
        Assert.Equal("FROZEN", registered.Body.GetProperty("state").GetProperty("hold").GetString());
        await h.Db.Sys_RoleActions.Where(row => row.MenuId == 212 && row.RoleId == 72 &&
            row.ActionCode == BusinessPartnerFinanceReferenceReader.ReadAction).ExecuteDeleteAsync();
        var masked = await WorkspaceCall(host, cookies, $"/{id:D}", financeContext: context);
        var maskedBank = Assert.Single(masked.Body.GetProperty("finance").EnumerateArray(), item => item.GetProperty("kind").GetString() == "PAYMENT_BANK");
        Assert.Equal("NOT_AUTHORIZED", maskedBank.GetProperty("availability").GetString());
        Assert.Equal(JsonValueKind.Null, maskedBank.GetProperty("reference").ValueKind);
        await DeviceEvidence("wp04-01-finance.json", new { source.Owner, source.Version, source.Digest, mask = "****0456",
            offlineRegistration = 422, offlineFreeze = 200, registeredFrozen = true, actionRevoked = "NOT_AUTHORIZED",
            counts = await FormalCounts(h) });
    }

    [CurrentPermissionPostgresFact]
    public async Task Business_partner_wp04_01_formal_state_and_profile_preserve_hold_and_legacy_delete()
    {
        await using var h = await Seed();
        await ProvisionWorkspace(h, admin: true);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h);
        var options = await WorkspaceCall(host, cookies, "/options?legalEntityKey=JP-A");
        var preview = await WorkspacePreview(host, cookies, WorkspaceInput(options, "BP-LIFE", "模拟三轴伙伴", complete: true));
        Assert.True(preview.Body.GetProperty("canApply").GetBoolean(), preview.Body.GetRawText());
        var id = PartnerId(await ApplyWorkspace(host, cookies, preview, Guid.NewGuid()));
        var firstRevision = h.Db.Database.SqlQuery<string>($"""
            SELECT content_hash AS "Value" FROM main_bp.profile_revisions WHERE tenant_id={h.Tenant} AND bp_id={id} AND revision=1
            """);
        var firstHash = await firstRevision.SingleAsync();

        await h.Db.Sys_RoleActions.Where(x => x.MenuId == 212 && x.RoleId == 72 &&
            (x.ActionCode == "sensitive.read" || x.ActionCode == "sensitive.write")).ExecuteDeleteAsync();
        var readable = await WorkspaceCall(host, cookies, $"/{id:D}");
        Assert.Equal(JsonValueKind.Null, readable.Body.GetProperty("profile").GetProperty("tel").ValueKind);
        Assert.Contains(readable.Body.GetProperty("redactedFields").EnumerateArray(), x => x.GetString() == "tel");
        var hiddenPatch = new { bpId = id, expectedRevision = CurrentRevision(readable),
            changes = new[] { new { field = "tel", value = (string?)null } }, relationChanges = Array.Empty<object>(),
            financeLinkChanges = Array.Empty<object>(), reason = "模拟无权清除" };
        await WorkspaceCall(host, cookies, "/profile-previews", HttpMethod.Post, hiddenPatch, status: 403);

        await using var offline = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await offline.OpenAsync();
        await using var exclusive = await offline.BeginTransactionAsync();
        await using (var lockSource = new NpgsqlCommand("LOCK TABLE cp6_bp_finance_ref_v1.frontiers IN ACCESS EXCLUSIVE MODE", offline, exclusive))
            await lockSource.ExecuteNonQueryAsync();
        var state = await WorkspaceStatus(host, cookies, id);
        await h.Db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION main_bp.simulate_audit_storage_error() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION '模拟审计存储失败'; END $$;
            CREATE TRIGGER simulate_audit_storage_error BEFORE INSERT ON main_bp.audit FOR EACH ROW
                WHEN (NEW.reason = '模拟审计持久化失败') EXECUTE FUNCTION main_bp.simulate_audit_storage_error();
            """);
        await StateCommand(host, cookies, id, state, "freeze", Guid.NewGuid(), 503, "模拟审计持久化失败");
        var rollback = await WorkspaceStatus(host, cookies, id);
        Assert.Equal(state.ETag, rollback.ETag);
        Assert.Equal("CLEAR", rollback.Body.GetProperty("state").GetProperty("hold").GetString());
        Assert.Equal(new[] { 1, 1, 9, 1, 1, 1 }, await FormalCounts(h));

        var freezeKey = Guid.NewGuid();
        await StateCommand(host, cookies, id, state, "freeze", freezeKey);
        var frozen = await WorkspaceStatus(host, cookies, id);
        Assert.Equal("FROZEN", frozen.Body.GetProperty("state").GetProperty("hold").GetString());
        var freezeAtomic = await h.Db.Database.SqlQuery<bool>($"""
            SELECT (p.revision=2 AND s.hold='FROZEN' AND r."IsFrozen" AND r."RowVersion"=p.row_version
                AND a.xmin=p.xmin AND a.xmin=c.xmin AND a.xmin=s.xmin) AS "Value"
            FROM main_bp.audit a JOIN main_bp.profile_revisions p USING(tenant_id,legal_entity_key,bp_id,revision)
              JOIN main_bp.commands c USING(tenant_id,command_id)
              JOIN main_bp.state_history s ON s.tenant_id=a.tenant_id AND s.command_id=a.command_id
              JOIN public."T_WebBusinessPartner" r ON r."TenantId"=a.tenant_id AND r."Id"=a.bp_id
            WHERE a.tenant_id={h.Tenant} AND a.command_id={freezeKey}
            """).SingleAsync();
        Assert.True(freezeAtomic);
        var before = await FormalCounts(h);
        await StateCommand(host, cookies, id, frozen, "retire", Guid.NewGuid(), 400, " ");
        var retireInput = new { bpId = id, expectedRevision = CurrentRevision(frozen), reason = "模拟缺少准确前置" };
        await WorkspaceCall(host, cookies, "/retire", HttpMethod.Post, retireInput, key: Guid.NewGuid(), status: 428);
        await WorkspaceCall(host, cookies, "/retire", HttpMethod.Post, retireInput, "*", Guid.NewGuid(), 400);
        await WorkspaceCall(host, cookies, "/retire", HttpMethod.Post, new { bpId = id, reason = "模拟缺少正文版本" }, frozen.ETag, Guid.NewGuid(), 400);
        await StateCommand(host, cookies, id, state, "retire", Guid.NewGuid(), 412);
        await StateCommand(host, cookies, id, frozen, "retire", Guid.NewGuid(), 403, csrf: false);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE main_bp.base_grants SET can_manage=false WHERE tenant_id={h.Tenant} AND role_id=72
            """);
        await StateCommand(host, cookies, id, frozen, "retire", Guid.NewGuid(), 403);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE main_bp.base_grants SET can_manage=true WHERE tenant_id={h.Tenant} AND role_id=72
            """);
        Assert.Equal(before, await FormalCounts(h));
        var retireKey = Guid.NewGuid();
        var retired = await StateCommand(host, cookies, id, frozen, "retire", retireKey);
        Assert.Equal("APPLIED", retired.Body.GetProperty("receipt").GetProperty("outcome").GetString());
        Assert.Equal("RETIRE", retired.Body.GetProperty("receipt").GetProperty("operation").GetString());
        var retiredContext = await WorkspaceStatus(host, cookies, id);
        Assert.Equal("RETIRED", retiredContext.Body.GetProperty("state").GetProperty("lifecycle").GetString());
        await exclusive.RollbackAsync();

        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE main_bp.option_values SET enabled=false WHERE tenant_id={h.Tenant} AND field='commercialCurrency'
            """);
        var edit = await WorkspaceCall(host, cookies, "/profile-previews", HttpMethod.Post,
            new { bpId = id, expectedRevision = CurrentRevision(retiredContext), changes = new[] { new { field = "bpName", value = "模拟新名称" } },
                relationChanges = Array.Empty<object>(), financeLinkChanges = Array.Empty<object>(), reason = "模拟只改名称" });
        Assert.True(edit.Body.GetProperty("canApply").GetBoolean(), edit.Body.GetRawText());
        await ApplyWorkspace(host, cookies, edit, Guid.NewGuid(), path: "/profile-apply");
        Assert.Equal("+81-12-3456", (await RetirementPartnerAsync(h, id)).Tel?.TrimEnd());
        var afterEdit = await WorkspaceStatus(host, cookies, id);
        Assert.Equal("RETIRED", afterEdit.Body.GetProperty("state").GetProperty("lifecycle").GetString());
        Assert.Equal("FROZEN", afterEdit.Body.GetProperty("state").GetProperty("hold").GetString());
        await StateCommand(host, cookies, id, afterEdit, "restore", Guid.NewGuid());
        var restored = await WorkspaceStatus(host, cookies, id);
        Assert.Equal("ACTIVE", restored.Body.GetProperty("state").GetProperty("lifecycle").GetString());
        Assert.Equal("FROZEN", restored.Body.GetProperty("state").GetProperty("hold").GetString());
        Assert.Equal("PRE_REGISTERED", restored.Body.GetProperty("state").GetProperty("registration").GetString());
        var blocked = await StateCommand(host, cookies, id, restored, "register", Guid.NewGuid(), 422);
        Assert.Equal("REFERENCE_UNPROVEN", blocked.Body.GetProperty("code").GetString());

        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE main_bp.option_values SET enabled=true WHERE tenant_id={h.Tenant} AND field='commercialCurrency'
            """);
        await StateCommand(host, cookies, id, restored, "register", Guid.NewGuid());
        var registered = await WorkspaceStatus(host, cookies, id);
        Assert.Equal("REGISTERED", registered.Body.GetProperty("state").GetProperty("registration").GetString());
        Assert.Equal("FROZEN", registered.Body.GetProperty("state").GetProperty("hold").GetString());
        await StateCommand(host, cookies, id, registered, "unfreeze", Guid.NewGuid());
        var clear = await WorkspaceStatus(host, cookies, id);
        Assert.Equal("CLEAR", clear.Body.GetProperty("state").GetProperty("hold").GetString());
        Assert.Equal("ACTIVE", clear.Body.GetProperty("state").GetProperty("lifecycle").GetString());
        Assert.Equal("REGISTERED", clear.Body.GetProperty("state").GetProperty("registration").GetString());
        await StateCommand(host, cookies, id, clear, "freeze", Guid.NewGuid());
        var oldReceipt = await WorkspaceCall(host, cookies, $"/commands/{retireKey:D}");
        Assert.Equal(retired.Body.GetProperty("receipt").GetRawText(), oldReceipt.Body.GetProperty("receipt").GetRawText());
        await h.Db.Sys_RoleActions.Where(row => row.MenuId == 212 && row.RoleId == 72 && row.ActionCode == "status").ExecuteDeleteAsync();
        await StateCommand(host, cookies, id, await WorkspaceStatus(host, cookies, id), "retire", Guid.NewGuid(), 403);
        var replay = await StateCommand(host, cookies, id, frozen, "retire", retireKey);
        Assert.True(replay.Body.GetProperty("replayed").GetBoolean());
        Assert.Equal(retired.Body.GetProperty("receipt").GetRawText(), replay.Body.GetProperty("receipt").GetRawText());
        await h.Db.Sys_RoleActions.Where(row => row.MenuId == 212 && row.RoleId == 72 && row.ActionCode == "command-result.read").ExecuteDeleteAsync();
        await WorkspaceCall(host, cookies, $"/commands/{retireKey:D}", status: 403);
        await StateCommand(host, cookies, id, frozen, "retire", retireKey, 403);

        var legacyRow = await RetirementPartnerAsync(h, id);
        using var legacy = await host.Send(RetirementRequest(cookies, legacyRow));
        Assert.True(legacy.StatusCode == HttpStatusCode.OK, await legacy.Content.ReadAsStringAsync());
        var latest = await WorkspaceCall(host, cookies, $"/{id:D}");
        Assert.Equal("RETIRED", latest.Body.GetProperty("state").GetProperty("lifecycle").GetString());
        Assert.Equal("9", CurrentRevision(latest));
        Assert.Equal("FROZEN", latest.Body.GetProperty("state").GetProperty("hold").GetString());
        Assert.Equal(firstHash, await firstRevision.SingleAsync());
        var immutable = await Assert.ThrowsAsync<PostgresException>(() => h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE main_bp.profile_revisions SET recorded_at=clock_timestamp() WHERE tenant_id={h.Tenant} AND bp_id={id} AND revision=1
            """));
        Assert.Equal(PostgresErrorCodes.CheckViolation, immutable.SqlState);
        Assert.Equal("BP_HISTORY_IMMUTABLE", immutable.MessageText);

        var audit = await h.Db.Database.SqlQuery<bool>($"""
            SELECT (a.actor_id={h.User} AND a.operation='RETIRE' AND a.reason='模拟当前状态操作'
                AND a.xmin=p.xmin AND a.xmin=c.xmin AND a.xmin=s.xmin) AS "Value"
            FROM main_bp.audit a JOIN main_bp.profile_revisions p USING(tenant_id,legal_entity_key,bp_id,revision)
              JOIN main_bp.commands c USING(tenant_id,command_id)
              JOIN main_bp.state_history s ON s.tenant_id=a.tenant_id AND s.command_id=a.command_id
            WHERE a.tenant_id={h.Tenant} AND a.command_id={retireKey}
            """).SingleAsync();
        Assert.True(audit);
        Assert.Equal(new[] { 1, 9, 9, 9, 9, 8 }, await FormalCounts(h));
        await DeviceEvidence("wp04-01-status.json", new { id, retireKey, officialPost = 200, missingCause = 400, missingCsrf = 403,
            stale = 412, scopeDenied = 403, financeSourceLocked = true, maskedPreserved = true, registrationUnproven = 422,
            restoredHold = "FROZEN", unfreeze = "CLEAR", legacyDelete = 200, originalReplayedAfterStatusRevoked = true,
            freezeAtomic, auditFailureRolledBack = true, commandReadRevoked = 403, immutableInitialProfile = true, sameXmin = audit,
            keys = await FormalCounts(h) });
    }
}
