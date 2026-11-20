using System.Net;
using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.Fin;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Erp;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static async Task GrantRetirementAdmin(Harness h)
    {
        h.Db.Sys_Roles.Add(new() { RoleId = 1, RoleName = "模拟退休管理员" });
        h.Db.Sys_UserRoles.Add(new() { UserId = h.User, RoleId = 1 });
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
    }

    private static Task<BusinessPartner> RetirementPartnerAsync(Harness h, Guid partner)
        => h.Db.BusinessPartners.AsNoTracking().SingleAsync(row => row.Id == partner);

    private static IQueryable<CP6.Entity.DomainModels.Sys.Sys_FieldAuditLog> RetirementAudits(Harness h, Guid partner)
        => h.Db.Sys_FieldAuditLogs.AsNoTracking()
            .Where(row => row.EntityName == nameof(BusinessPartner) && row.EntityKey == partner.ToString("D") && row.Operation == 2);

    private static HttpRequestMessage RetirementRequest(Dictionary<string, string> cookies, BusinessPartner row,
        bool csrf = true)
        => OperationsRequest(cookies, HttpMethod.Delete, $"{PartnerApi}/{Uri.EscapeDataString(row.BpCd)}",
            new { row.RowVersion }, csrf);

    [CurrentPermissionPostgresFact]
    public async Task Business_partner_retirement_http_current_role_1_can_retire_and_read_original_status_version_and_audit()
    {
        await using var h = await Seed();
        await GrantBusinessPartnerIdentity(h);
        await GrantPartnerActions(h, "del", "profile-query");
        await GrantRetirementAdmin(h);
        await h.Db.Sys_RoleDataScopes.Where(scope => scope.ResourceKey == PartnerResource)
            .ExecuteUpdateAsync(set => set.SetProperty(scope => scope.ScopeType, 1));
        var id = await SeedIdentityPartner(h);
        var before = await RetirementPartnerAsync(h, id);
        Assert.False(before.IsDeleted);
        Assert.Equal(1, before.Status);
        var access = await PostgreSqlPermissionReader.ReadAsync(h.Db, h.User, default);
        Assert.Contains(1, access.RoleIds);
        Assert.Contains(PartnerResource + ":del", access.ActionKeys);
        Assert.Equal(1, access.DataScopes[PartnerResource]);
        Assert.Equal(access.UserName, before.Creator);
        Assert.Empty(await RetirementAudits(h, id).ToArrayAsync());

        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h);
        using var retired = await host.Send(RetirementRequest(cookies, before));
        var after = await RetirementPartnerAsync(h, id);
        var audits = await RetirementAudits(h, id).ToArrayAsync();
        await DeviceEvidence("bug-1277-http.json", new
        {
            simulated = "用户、设备配置、租户及交易伙伴资料",
            signedIn = true, currentRole1 = true, currentDel = true, ownerScope = 1,
            http = (int)retired.StatusCode,
            before = new { before.Status, before.IsDeleted, version = Convert.ToHexString(before.RowVersion!) },
            after = new { after.Status, after.IsDeleted, version = Convert.ToHexString(after.RowVersion!) },
            fieldAudits = audits.Length, actorMatches = audits.Length == 1 && audits[0].UserId == h.User
        });
        Assert.Equal(HttpStatusCode.OK, retired.StatusCode);
        var result = await PartnerResponse(retired, HttpStatusCode.OK);
        Assert.Equal(0, result.GetProperty("code").GetInt32());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("data").ValueKind);
        Assert.Equal(9, after.Status);
        Assert.True(after.IsDeleted);
        Assert.False(before.RowVersion!.SequenceEqual(after.RowVersion!));
        Assert.Equal(access.UserName, after.Modifier);

        using var read = await host.Send(OperationsRequest(cookies, HttpMethod.Get,
            $"{PartnerApi}/{Uri.EscapeDataString(before.BpCd)}?includeDeleted=true"));
        var profile = (await PartnerResponse(read, HttpStatusCode.OK)).GetProperty("data");
        Assert.Equal(9, profile.GetProperty("status").GetInt32());
        Assert.Equal(after.RowVersion, profile.GetProperty("rowVersion").GetBytesFromBase64());
        using var source = await host.Send(OperationsRequest(cookies, HttpMethod.Get,
            $"{PartnerApi}/identity-owner-v1/{id:D}"));
        var original = (await PartnerResponse(source, HttpStatusCode.OK)).GetProperty("data");
        Assert.Equal("2", original.GetProperty("reference").GetProperty("version").GetString());
        Assert.True(original.GetProperty("source").GetProperty("isDeleted").GetBoolean());

        var audit = Assert.Single(audits);
        Assert.Equal(h.Tenant, audit.TenantId);
        Assert.Equal(h.User, audit.UserId);
        Assert.Equal(access.UserName, audit.UserName);
        Assert.NotNull(audit.AuditAtUtc);
        using var changes = JsonDocument.Parse(audit.Changes);
        var fields = changes.RootElement.EnumerateArray().ToDictionary(item => item.GetProperty("Field").GetString()!);
        Assert.Equal("1", fields["Status"].GetProperty("Old").GetString());
        Assert.Equal("9", fields["Status"].GetProperty("New").GetString());
        Assert.Equal("False", fields["IsDeleted"].GetProperty("Old").GetString());
        Assert.Equal("True", fields["IsDeleted"].GetProperty("New").GetString());
        var evidence = AuditEvidenceV1.Decode(audit.AuditEvidenceJson);
        Assert.NotNull(evidence);
        Assert.Equal("USER", evidence.ActorKind);
        Assert.Equal(h.User, evidence.ActualActor);
        Assert.Equal(h.User, evidence.EffectiveActor);
        Assert.Equal("OWNER_RECORDED", evidence.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(evidence.TransactionRef));
        Assert.False(string.IsNullOrWhiteSpace(evidence.EntityVersion));
        await DeviceEvidence("bug-1277-audit.json", new
        {
            status = after.Status, deleted = after.IsDeleted,
            originalRevision = original.GetProperty("reference").GetProperty("version").GetString(),
            actorMatches = audit.UserId == h.User && evidence.ActualActor == h.User && evidence.EffectiveActor == h.User,
            audit.Operation, evidence.Outcome, hasVersion = !string.IsNullOrEmpty(evidence.EntityVersion),
            hasTransaction = !string.IsNullOrEmpty(evidence.TransactionRef)
        });
    }

    private static async Task AssertRetirementUnchanged(Harness h, BusinessPartner before)
    {
        var after = await RetirementPartnerAsync(h, before.Id);
        Assert.Equal(before.Status, after.Status);
        Assert.Equal(before.IsDeleted, after.IsDeleted);
        Assert.Equal(before.IsFrozen, after.IsFrozen);
        Assert.Equal(before.RowVersion, after.RowVersion);
        Assert.Equal(before.Modifier, after.Modifier);
        Assert.Equal(before.ModifyDate, after.ModifyDate);
        Assert.Empty(await RetirementAudits(h, before.Id).ToArrayAsync());
    }

    [TenantManagementPostgresTheory]
    [InlineData("missing-role", HttpStatusCode.Forbidden)]
    [InlineData("missing-action", HttpStatusCode.Forbidden)]
    [InlineData("out-of-scope", HttpStatusCode.NotFound)]
    [InlineData("role-revoked", HttpStatusCode.Forbidden)]
    [InlineData("action-revoked", HttpStatusCode.Forbidden)]
    public async Task Business_partner_retirement_http_denies_missing_or_revoked_current_authority_without_writes(
        string scenario, HttpStatusCode expected)
    {
        await using var h = await Seed();
        await GrantBusinessPartnerIdentity(h);
        if (scenario != "missing-action")
            await GrantPartnerActions(h, "del");
        if (scenario != "missing-role")
            await GrantRetirementAdmin(h);
        if (scenario == "out-of-scope")
            await h.Db.Sys_RoleDataScopes.Where(scope => scope.ResourceKey == PartnerResource)
                .ExecuteUpdateAsync(set => set.SetProperty(scope => scope.ScopeType, 1));
        var id = await SeedIdentityPartner(h, scenario == "out-of-scope" ? "other-owner" : "price-owner");
        var before = await RetirementPartnerAsync(h, id);

        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h);
        if (scenario is "role-revoked" or "action-revoked")
        {
            var granted = await PostgreSqlPermissionReader.ReadAsync(h.Db, h.User, default);
            Assert.Contains(1, granted.RoleIds);
            Assert.Contains(PartnerResource + ":del", granted.ActionKeys);
            var removed = scenario == "role-revoked"
                ? await h.Db.Sys_UserRoles.Where(role => role.UserId == h.User && role.RoleId == 1).ExecuteDeleteAsync()
                : await h.Db.Sys_RoleActions.Where(action => action.MenuId == 212 && action.RoleId == 72 && action.ActionCode == "del").ExecuteDeleteAsync();
            Assert.Equal(1, removed);
            using var current = await host.Send(OperationsRequest(cookies, HttpMethod.Get, $"{PartnerApi}/identity-owner-v1/{id:D}"));
            Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        }

        using var denied = await host.Send(RetirementRequest(cookies, before));
        Assert.Equal(expected, denied.StatusCode);
        if (scenario is "missing-role" or "role-revoked")
            Assert.Contains("BUSINESS_PARTNER_ADMIN_REQUIRED", await denied.Content.ReadAsStringAsync());
        if (scenario == "out-of-scope")
            Assert.Contains("BUSINESS_PARTNER_NOT_FOUND", await denied.Content.ReadAsStringAsync());
        await AssertRetirementUnchanged(h, before);
        await DeviceEvidence($"bug-1277-{scenario}.json", new
        {
            scenario, http = (int)denied.StatusCode, unchanged = true,
            sameSession = scenario is "role-revoked" or "action-revoked"
        });
    }

    [CurrentPermissionPostgresFact]
    public async Task Business_partner_retirement_http_keeps_existing_cas_and_csrf_protection()
    {
        await using var h = await Seed();
        await GrantBusinessPartnerIdentity(h);
        await GrantPartnerActions(h, "del");
        await GrantRetirementAdmin(h);
        var id = await SeedIdentityPartner(h);
        var stale = await RetirementPartnerAsync(h, id);
        await h.Db.BusinessPartners.Where(row => row.Id == id)
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.BpName, "模拟并发变更"));
        var current = await RetirementPartnerAsync(h, id);
        Assert.False(stale.RowVersion!.SequenceEqual(current.RowVersion!));

        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h);
        using var noCsrf = await host.Send(RetirementRequest(cookies, current, csrf: false));
        Assert.Equal(HttpStatusCode.Forbidden, noCsrf.StatusCode);
        await AssertRetirementUnchanged(h, current);

        using var conflict = await host.Send(RetirementRequest(cookies, stale));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        await AssertRetirementUnchanged(h, current);
        await DeviceEvidence("bug-1277-cas-csrf.json", new
        {
            csrf = (int)noCsrf.StatusCode, staleVersion = (int)conflict.StatusCode, unchanged = true
        });
    }

    [CurrentPermissionPostgresFact]
    public async Task Business_partner_retirement_http_works_with_finance_offline_and_preserves_frozen_state()
    {
        await using var h = await Seed();
        var (actors, context) = await SeedBpFinance(h, "bug-1277-offline");
        await GrantPartnerActions(h, "del");
        await GrantRetirementAdmin(h);
        var id = await SeedIdentityPartner(h);
        await h.Db.BusinessPartners.Where(row => row.Id == id)
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.IsFrozen, true));
        var before = await RetirementPartnerAsync(h, id);
        var reference = await SimulatedBankPublication(h, actors.Ledger, Guid.NewGuid(), Guid.NewGuid(), 1, "****1234");
        await RegisterFinanceScope(h, actors.Ledger, reference, id);
        await StoreFinanceLink(h, id, actors.Ledger, BusinessPartnerFinanceReferenceReader.PaymentBank, reference);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, GlHostSettings());
        var cookies = await Login(host, h);
        var available = await FinanceReference(host, cookies, id, BusinessPartnerFinanceReferenceReader.PaymentBank, context);
        Assert.Equal("FOUND", available.Availability);

        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_bp_finance_ref_v1.frontiers SET availability='OFFLINE', generation=generation+1
            WHERE tenant_id={h.Tenant} AND owner={reference.Owner} AND source_id={reference.Id}
            """);
        var offline = await FinanceReference(host, cookies, id, BusinessPartnerFinanceReferenceReader.PaymentBank, context);
        Assert.Equal("UNPROVEN", offline.Availability);

        await using var outage = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        await using var locked = await outage.Database.BeginTransactionAsync();
        await outage.Database.ExecuteSqlRawAsync("LOCK TABLE cp6_bp_finance_ref_v1.frontiers IN ACCESS EXCLUSIVE MODE");
        using var retired = await host.Send(RetirementRequest(cookies, before)).WaitAsync(TimeSpan.FromSeconds(8));
        var result = await PartnerResponse(retired, HttpStatusCode.OK);
        Assert.Equal(0, result.GetProperty("code").GetInt32());
        await locked.RollbackAsync();

        var after = await RetirementPartnerAsync(h, id);
        Assert.Equal(9, after.Status);
        Assert.True(after.IsDeleted);
        Assert.True(after.IsFrozen);
        Assert.False(before.RowVersion!.SequenceEqual(after.RowVersion!));
        var audit = Assert.Single(await RetirementAudits(h, id).ToArrayAsync());
        Assert.Equal(h.User, audit.UserId);
        using var changes = JsonDocument.Parse(audit.Changes);
        Assert.DoesNotContain(changes.RootElement.EnumerateArray(), field => field.GetProperty("Field").GetString() == "IsFrozen");
        using var source = await host.Send(OperationsRequest(cookies, HttpMethod.Get, $"{PartnerApi}/identity-owner-v1/{id:D}"));
        var recorded = (await PartnerResponse(source, HttpStatusCode.OK)).GetProperty("data").GetProperty("source");
        Assert.True(recorded.GetProperty("isFrozen").GetBoolean());
        Assert.True(recorded.GetProperty("isDeleted").GetBoolean());
        await DeviceEvidence("bug-1277-offline.json", new
        {
            simulated = "用户、设备配置、伙伴原冻结事实、账套、已存发布原件及财务离线",
            observedSource = available.Availability, offlineSource = offline.Availability,
            sourceLockHeldDuringRetirement = true, http = (int)retired.StatusCode,
            after.Status, after.IsDeleted, after.IsFrozen, actorMatches = audit.UserId == h.User
        });
    }
}
