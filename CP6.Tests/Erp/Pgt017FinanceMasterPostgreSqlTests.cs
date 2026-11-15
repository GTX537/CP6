using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.Fin;
using CP6.Core.Services.Platform;
using CP6.Entity.DomainModels.Fin;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private const string FinanceAccountMenu = "fin-account";
    private const string FinancePaymentMenu = "fin-ap-payment";
    private const decimal FinanceFixtureRate = 0.123456m;

    [TenantManagementPostgresTheory]
    [InlineData("account-create", true)]
    [InlineData("account-create", false)]
    [InlineData("account-update", true)]
    [InlineData("account-update", false)]
    [InlineData("account-deactivate", true)]
    [InlineData("account-deactivate", false)]
    [InlineData("account-import", true)]
    [InlineData("account-import", false)]
    [InlineData("bank-create", true)]
    [InlineData("bank-create", false)]
    [InlineData("tax-create", true)]
    [InlineData("tax-create", false)]
    public async Task PGT017_finance_master_write_and_pause_share_commit_order(string operation, bool pauseFirst)
    {
        await using var h = await Seed();
        await using var admin = await TenantManagementSeed();
        var fixture = await SeedFinanceMasterAsync(h);
        var command = FinanceMasterCommand(operation, fixture);
        await GrantLegacyAuthorityAction(h, command.Menu, command.Action);
        var connection = h.Db.Database.GetConnectionString()!;
        await using var host = await StartFinanceMasterApiAsync(h);
        var actor = await Login(host, h);
        var manager = await Login(host, admin);
        using var read = await host.Send(OperationsRequest(manager, HttpMethod.Get, ManagementPath(h.Tenant)));
        var active = await NumberHttpData<TenantManagementSnapshotV1>(read);
        var suspend = new TenantManagementCommandV1("finance-master-pause", active.Revision, "suspend", null);
        var authorityBefore = await ReadPauseAuthority(h, admin.User);

        await using var barrier = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await barrier.OpenAsync();
        await observer.OpenAsync();
        var before = await ReadFinanceMasterRowsAsync(observer, h.Tenant);
        Task<HttpResponseMessage> pause;
        Task<HttpResponseMessage> write;
        FenceWait first;
        FenceWait second;
        await using (var hold = await barrier.BeginTransactionAsync())
        {
            var locked = pauseFirst ? "Sys_Tenants" : command.Table;
            await using var lockTable = new NpgsqlCommand($"LOCK TABLE public.\"{locked}\" IN SHARE MODE", barrier, hold);
            await lockTable.ExecuteNonQueryAsync();
            if (pauseFirst)
            {
                pause = Pause();
                first = await WaitForFence(observer, barrier.ProcessID, "%UPDATE%Sys_Tenants%", null, pause);
                write = Write();
                second = await WaitForFence(observer, first.Backend, "%cp6_authentication_v1.acquire()%", null, write);
            }
            else
            {
                write = Write();
                first = await WaitForFence(observer, barrier.ProcessID, $"%{command.SqlVerb}%{command.Table}%", null, write);
                pause = Pause();
                second = await WaitForFence(observer, first.Backend, "%cp6_authentication_v1.acquire()%", null, pause);
            }
            Assert.Equal(before, await ReadFinanceMasterRowsAsync(observer, h.Tenant));
            Assert.Equal(authorityBefore, await ReadPauseAuthority(h, admin.User));
            await hold.RollbackAsync();
        }

        using var paused = await pause;
        using var result = await write;
        var receipt = await NumberHttpData<TenantManagementReceiptV1>(paused);
        CheckPause(authorityBefore, await ReadPauseAuthority(h, admin.User), receipt, suspend);
        if (pauseFirst)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
            Assert.Equal(before, await ReadFinanceMasterRowsAsync(observer, h.Tenant));
            var reactivate = new TenantManagementCommandV1("finance-master-resume", receipt.Snapshot.Revision, "reactivate", null);
            using var resumed = await host.Send(OperationsRequest(manager, HttpMethod.Post, ManagementPath(h.Tenant, "/commands"), reactivate));
            Assert.Equal(HttpStatusCode.OK, resumed.StatusCode);
            actor = await Login(host, h);
            using var allowed = await Write();
            await ReadFinanceMasterSuccessAsync(allowed, operation, fixture);
        }
        else
        {
            await ReadFinanceMasterSuccessAsync(result, operation, fixture);
            var committed = await ReadFinanceMasterRowsAsync(observer, h.Tenant);
            using var stale = await Write();
            Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode);
            Assert.Equal(committed, await ReadFinanceMasterRowsAsync(observer, h.Tenant));
        }

        AssertFinanceMasterEffect(operation, h.Tenant, fixture, before, await ReadFinanceMasterRowsAsync(observer, h.Tenant));
        output.WriteLine(JsonSerializer.Serialize(new { Operation = operation, PauseFirst = pauseFirst,
            FirstWait = first, RootWait = second, Status = (int)result.StatusCode }));

        Task<HttpResponseMessage> Write() =>
            host.Send(OperationsRequest(actor, command.Method, command.Path, command.Body));
        Task<HttpResponseMessage> Pause() =>
            host.Send(OperationsRequest(manager, HttpMethod.Post, ManagementPath(h.Tenant, "/commands"), suspend));
    }

    [TenantManagementPostgresTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PGT017_finance_master_import_and_role_revocation_share_commit_order(bool revokeFirst)
    {
        const string operation = "account-import";
        await using var h = await Seed();
        await SeedFenceAdministrator(h);
        var fixture = await SeedFinanceMasterAsync(h);
        var command = FinanceMasterCommand(operation, fixture);
        await GrantLegacyAuthorityAction(h, command.Menu, command.Action);
        var connection = h.Db.Database.GetConnectionString()!;
        await using var host = await StartFinanceMasterApiAsync(h);
        var actor = await Login(host, h);
        var manager = await Login(host, "fence-manager", "Fence1!Password", h.Tenant.ToString("N"));
        var revoke = await RevokeActions(host, manager, command.Action);
        var authorityBefore = await ReadPolicyWrites(h, revoke.OperationId);

        await using var barrier = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await barrier.OpenAsync();
        await observer.OpenAsync();
        var before = await ReadFinanceMasterRowsAsync(observer, h.Tenant);
        Task<HttpResponseMessage> change;
        Task<HttpResponseMessage> write;
        FenceWait first;
        FenceWait second;
        await using (var hold = await barrier.BeginTransactionAsync())
        {
            using var lockTable = new NpgsqlCommand(revokeFirst
                ? """LOCK TABLE public."Sys_RoleAction" IN SHARE MODE"""
                : """LOCK TABLE public."Fin_GlAccount" IN SHARE MODE""", barrier, hold);
            await lockTable.ExecuteNonQueryAsync();
            if (revokeFirst)
            {
                change = Revoke();
                first = await WaitForFence(observer, barrier.ProcessID, "%DELETE%Sys_RoleAction%", null, change);
                write = Write();
                second = await WaitForFence(observer, first.Backend, "%cp6_authentication_v1.acquire()%", null, write);
            }
            else
            {
                write = Write();
                first = await WaitForFence(observer, barrier.ProcessID, "%INSERT%Fin_GlAccount%", null, write);
                change = Revoke();
                second = await WaitForFence(observer, first.Backend, "%cp6_authentication_v1.acquire()%", null, change);
            }
            Assert.Equal(before, await ReadFinanceMasterRowsAsync(observer, h.Tenant));
            Assert.Equal(authorityBefore, await ReadPolicyWrites(h, revoke.OperationId));
            await hold.RollbackAsync();
        }

        using var changed = await change;
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var policy = await ReadPolicyWrites(h, revoke.OperationId);
        Assert.NotEqual(authorityBefore.RoleRevision, policy.RoleRevision);
        Assert.Equal(authorityBefore.Generation + 1, policy.Generation);
        Assert.Equal(authorityBefore.Snapshot + 1, policy.Snapshot);
        Assert.Equal(authorityBefore.Outbox + 1, policy.Outbox);
        Assert.Equal(1, policy.CurrentOutbox);
        Assert.Equal(1, policy.Audits);
        Assert.Equal(1, policy.Receipts);
        Assert.DoesNotContain(command.Action, policy.Actions);

        using var result = await write;
        if (revokeFirst)
        {
            Assert.Equal(HttpStatusCode.Forbidden, result.StatusCode);
            Assert.Equal(before, await ReadFinanceMasterRowsAsync(observer, h.Tenant));
            await GrantLegacyAuthorityAction(h, command.Menu, command.Action);
            using var current = await Write();
            await ReadFinanceMasterSuccessAsync(current, operation, fixture);
        }
        else
        {
            await ReadFinanceMasterSuccessAsync(result, operation, fixture);
            var committed = await ReadFinanceMasterRowsAsync(observer, h.Tenant);
            using var stale = await Write();
            Assert.Equal(HttpStatusCode.Forbidden, stale.StatusCode);
            Assert.Equal(committed, await ReadFinanceMasterRowsAsync(observer, h.Tenant));
        }

        AssertFinanceMasterEffect(operation, h.Tenant, fixture, before, await ReadFinanceMasterRowsAsync(observer, h.Tenant));
        output.WriteLine(JsonSerializer.Serialize(new { RevokeFirst = revokeFirst, FirstWait = first,
            RootWait = second, Status = (int)result.StatusCode }));

        Task<HttpResponseMessage> Write() => host.Send(OperationsRequest(actor, command.Method, command.Path, command.Body));
        Task<HttpResponseMessage> Revoke() => host.Send(OperationsRequest(manager, HttpMethod.Put, PolicyWritePath, revoke));
    }

    [TenantManagementPostgresTheory]
    [InlineData("account-create")]
    [InlineData("bank-create")]
    [InlineData("tax-create")]
    public async Task PGT017_finance_master_create_binds_rows_to_the_authenticated_tenant(string operation)
    {
        await using var h = await Seed();
        await using var other = await Seed();
        var fixture = await SeedFinanceMasterAsync(h);
        await SeedFinanceMasterAsync(other);
        var command = FinanceMasterCommand(operation, fixture, tenantInBody: other.Tenant);
        await GrantLegacyAuthorityAction(h, command.Menu, command.Action);
        await using var host = await StartFinanceMasterApiAsync(h);
        var actor = await Login(host, h);
        await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await observer.OpenAsync();
        var before = await ReadFinanceMasterRowsAsync(observer, h.Tenant);
        var otherBefore = await ReadFinanceMasterRowsAsync(observer, other.Tenant);

        using var result = await host.Send(OperationsRequest(actor, command.Method, command.Path, command.Body));
        await ReadFinanceMasterSuccessAsync(result, operation, fixture);
        Assert.Equal(otherBefore, await ReadFinanceMasterRowsAsync(observer, other.Tenant));
        AssertFinanceMasterEffect(operation, h.Tenant, fixture, before, await ReadFinanceMasterRowsAsync(observer, h.Tenant));
    }

    [TenantManagementPostgresTheory]
    [InlineData("account-create")]
    [InlineData("account-update")]
    [InlineData("bank-create")]
    public async Task PGT017_finance_master_rejects_new_links_to_another_tenants_account(string operation)
    {
        await using var h = await Seed();
        await using var other = await Seed();
        var fixture = await SeedFinanceMasterAsync(h);
        var foreign = await SeedFinanceMasterAsync(other);
        var command = FinanceMasterCommand(operation, fixture, linkedAccount: foreign.Account);
        await GrantLegacyAuthorityAction(h, command.Menu, command.Action);
        await using var host = await StartFinanceMasterApiAsync(h);
        var actor = await Login(host, h);
        await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await observer.OpenAsync();
        var before = await ReadFinanceMasterRowsAsync(observer, h.Tenant);
        var otherBefore = await ReadFinanceMasterRowsAsync(observer, other.Tenant);

        using var denied = await host.Send(OperationsRequest(actor, command.Method, command.Path, command.Body));
        await ReadFinanceMasterErrorAsync(denied, "E-FIN-003");
        Assert.Equal(before, await ReadFinanceMasterRowsAsync(observer, h.Tenant));
        Assert.Equal(otherBefore, await ReadFinanceMasterRowsAsync(observer, other.Tenant));
    }

    [CurrentPermissionPostgresFact]
    public async Task PGT017_finance_master_preserves_business_errors_and_optional_root_parent()
    {
        await using var h = await Seed();
        var fixture = await SeedFinanceMasterAsync(h);
        foreach (var action in new[] { "add", "edit", "deactivate", "import" })
            await GrantLegacyAuthorityAction(h, FinanceAccountMenu, action);
        foreach (var action in new[] { "bank", "tax" })
            await GrantLegacyAuthorityAction(h, FinancePaymentMenu, action);
        await new GlAccountService(h.Db).ImportTemplateAsync(FinCoaTemplate.Intl, "fixture");
        await using var host = await StartFinanceMasterApiAsync(h);
        var actor = await Login(host, h);
        await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await observer.OpenAsync();
        var before = await ReadFinanceMasterRowsAsync(observer, h.Tenant);

        await Reject(HttpMethod.Post, "api/fin/account", "E-FIN-001", new { Code = "W600-GL", Name = "Duplicate" });
        await Reject(HttpMethod.Put, $"api/fin/account/{fixture.NewRow:D}", "E-FIN-003", new { Code = "W600-GL", Name = "Absent" });
        await Reject(HttpMethod.Post, $"api/fin/account/{fixture.NewRow:D}/deactivate", "E-FIN-003");
        await Reject(HttpMethod.Post, "api/fin/account/import?scheme=INTL", "E-FIN-002");
        await Reject(HttpMethod.Post, "api/fin/account/import?scheme=invalid", "E-FIN-010");
        await Reject(HttpMethod.Post, "api/fin/ap/master/bank-account", "E-FIN-230", new { Code = "W600-BK", Name = "Duplicate" });
        await Reject(HttpMethod.Post, "api/fin/ap/master/tax-code", "E-FIN-231", new { Code = "W600-TX", Name = "Duplicate" });
        Assert.Equal(before, await ReadFinanceMasterRowsAsync(observer, h.Tenant));

        using var allowed = await host.Send(OperationsRequest(actor, HttpMethod.Post, "api/fin/account",
            new { Id = fixture.NewRow, Code = "W600-ROOT", Name = "Root account", ParentId = (Guid?)null }));
        await ReadFinanceMasterSuccessAsync(allowed, "account-create", fixture);
        var row = (await ReadFinanceMasterRowsAsync(observer, h.Tenant)).Accounts[fixture.NewRow];
        Assert.Equal(JsonValueKind.Null, row.GetProperty("ParentId").ValueKind);

        async Task Reject(HttpMethod method, string path, string code, object? body = null)
        {
            using var denied = await host.Send(OperationsRequest(actor, method, path, body));
            await ReadFinanceMasterErrorAsync(denied, code);
        }
    }

    private sealed record FinanceMasterFixture(Guid Account, Guid Parent, Guid NewRow);
    private sealed record FinanceMasterWrite(string Menu, string Action, HttpMethod Method, string Path, object? Body,
        string Table, string SqlVerb);

    private static async Task<FinanceMasterFixture> SeedFinanceMasterAsync(Harness h)
    {
        var parent = new GlAccount { Id = Guid.NewGuid(), Code = "W600-PARENT", Name = "Local parent",
            StandardScheme = "fixture", Type = AccountType.Asset, NormalSide = AccountSide.Debit, IsLeaf = false };
        var account = new GlAccount { Id = Guid.NewGuid(), Code = "W600-GL", Name = "Local original",
            StandardScheme = "fixture", Type = AccountType.Asset, NormalSide = AccountSide.Debit };
        h.Db.GlAccounts.AddRange(parent, account);
        h.Db.BankAccounts.Add(new() { Code = "W600-BK", Name = "Original bank", GlAccountId = account.Id });
        h.Db.TaxCodes.Add(new() { Code = "W600-TX", Name = "Original tax", Rate = 0.1m, Direction = TaxDirection.Input });
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        return new(account.Id, parent.Id, Guid.NewGuid());
    }

    private static FinanceMasterWrite FinanceMasterCommand(string operation, FinanceMasterFixture fixture,
        Guid tenantInBody = default, Guid? linkedAccount = null) => operation switch
    {
        "account-create" => new(FinanceAccountMenu, "add", HttpMethod.Post, "api/fin/account", new GlAccount
        {
            Id = fixture.NewRow, TenantId = tenantInBody, Code = "W600-NEW", Name = "New account", Type = AccountType.Asset,
            NormalSide = AccountSide.Debit, ParentId = linkedAccount ?? fixture.Parent, Level = 2, CurrencyCd = "CHF"
        }, "Fin_GlAccount", "INSERT"),
        "account-update" => new(FinanceAccountMenu, "edit", HttpMethod.Put, $"api/fin/account/{fixture.Account:D}", new GlAccount
        {
            Code = "W600-NOT-SAVED", Name = "Renamed account", Type = AccountType.Liability, NormalSide = AccountSide.Credit,
            ParentId = linkedAccount ?? fixture.Parent, Level = 2, IsControl = true, SubLedgerType = "AP",
            RequirePartner = true, Role = "W600-ROLE", StandardScheme = FinCoaTemplate.Intl, CurrencyCd = "CHF"
        }, "Fin_GlAccount", "UPDATE"),
        "account-deactivate" => new(FinanceAccountMenu, "deactivate", HttpMethod.Post, $"api/fin/account/{fixture.Account:D}/deactivate",
            null, "Fin_GlAccount", "UPDATE"),
        "account-import" => new(FinanceAccountMenu, "import", HttpMethod.Post, "api/fin/account/import?scheme=INTL",
            null, "Fin_GlAccount", "INSERT"),
        "bank-create" => new(FinancePaymentMenu, "bank", HttpMethod.Post, "api/fin/ap/master/bank-account", new BankAccount
        {
            Id = fixture.NewRow, TenantId = tenantInBody, Code = "W600-NEW", Name = "New bank", BankName = "Fixture bank",
            AccountNo = "SIM-00123", CurrencyCd = "CHF", GlAccountId = linkedAccount ?? fixture.Account, IsActive = false
        }, "Fin_BankAccount", "INSERT"),
        "tax-create" => new(FinancePaymentMenu, "tax", HttpMethod.Post, "api/fin/ap/master/tax-code", new TaxCode
        {
            Id = fixture.NewRow, TenantId = tenantInBody, Code = "W600-NEW", Name = "New tax",
            Rate = FinanceFixtureRate, Direction = TaxDirection.Output, Recoverable = false
        }, "Fin_TaxCode", "INSERT"),
        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };

    private static Task<AuthenticationApiProcess> StartFinanceMasterApiAsync(Harness h) =>
        AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(h.Tenant));

    private static async Task ReadFinanceMasterSuccessAsync(HttpResponseMessage response, string operation, FinanceMasterFixture fixture)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, json.GetProperty("code").GetInt32());
        Assert.Equal("OK", json.GetProperty("message").GetString());
        var data = json.GetProperty("data");
        if (operation.EndsWith("create", StringComparison.Ordinal))
            Assert.Equal(fixture.NewRow, data.GetProperty("id").GetGuid());
        else if (operation == "account-import")
            Assert.Equal(FinCoaTemplate.IntlRows.Count, data.GetProperty("count").GetInt32());
        else
            Assert.Equal(JsonValueKind.Null, data.ValueKind);
    }

    private static async Task ReadFinanceMasterErrorAsync(HttpResponseMessage response, string code)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(400, json.GetProperty("code").GetInt32());
        Assert.Equal(code, json.GetProperty("message").GetString());
    }

    private sealed record FinanceMasterRows(string AccountsJson, string BanksJson, string TaxesJson, string AuditJson)
    {
        public Dictionary<Guid, JsonElement> Accounts => FinanceRowsById(AccountsJson);
        public Dictionary<Guid, JsonElement> Banks => FinanceRowsById(BanksJson);
        public Dictionary<Guid, JsonElement> Taxes => FinanceRowsById(TaxesJson);
        public Dictionary<Guid, JsonElement> Audit => FinanceRowsById(AuditJson);
    }

    private static Dictionary<Guid, JsonElement> FinanceRowsById(string json)
    {
        using var parsed = JsonDocument.Parse(json);
        return parsed.RootElement.EnumerateArray().ToDictionary(row => row.GetProperty("Id").GetGuid(), row => row.Clone());
    }

    private static async Task<FinanceMasterRows> ReadFinanceMasterRowsAsync(NpgsqlConnection observer, Guid tenant)
    {
        await using var command = new NpgsqlCommand("""
            SELECT
              coalesce((SELECT jsonb_agg(to_jsonb(t) ORDER BY t."Id") FROM public."Fin_GlAccount" t WHERE t."TenantId"=@tenant),'[]')::text,
              coalesce((SELECT jsonb_agg(to_jsonb(t) ORDER BY t."Id") FROM public."Fin_BankAccount" t WHERE t."TenantId"=@tenant),'[]')::text,
              coalesce((SELECT jsonb_agg(to_jsonb(t) ORDER BY t."Id") FROM public."Fin_TaxCode" t WHERE t."TenantId"=@tenant),'[]')::text,
              coalesce((SELECT jsonb_agg(to_jsonb(t) ORDER BY t."Id") FROM public."Sys_FieldAuditLogs" t
                        WHERE t."TenantId"=@tenant AND t."EntityName"='GlAccount'),'[]')::text
            """, observer);
        command.Parameters.AddWithValue("tenant", tenant);
        await using var read = await command.ExecuteReaderAsync();
        Assert.True(await read.ReadAsync());
        return new(read.GetString(0), read.GetString(1), read.GetString(2), read.GetString(3));
    }

    private static void AssertFinanceMasterEffect(string operation, Guid tenant, FinanceMasterFixture fixture,
        FinanceMasterRows before, FinanceMasterRows after)
    {
        if (operation == "bank-create")
        {
            Assert.Equal(before.AccountsJson, after.AccountsJson);
            Assert.Equal(before.TaxesJson, after.TaxesJson);
            Assert.Equal(before.AuditJson, after.AuditJson);
            var bank = Assert.Single(Apply(before.Banks, after.Banks));
            Assert.Equal(fixture.NewRow, bank.GetProperty("Id").GetGuid());
            Assert.Equal(fixture.Account, bank.GetProperty("GlAccountId").GetGuid());
            Assert.Equal("Fixture bank", bank.GetProperty("BankName").GetString());
            Assert.Equal("SIM-00123", bank.GetProperty("AccountNo").GetString());
            Assert.Equal("CHF", bank.GetProperty("CurrencyCd").GetString());
            Assert.False(bank.GetProperty("IsActive").GetBoolean());
            AssertOwner(bank);
            return;
        }
        if (operation == "tax-create")
        {
            Assert.Equal(before.AccountsJson, after.AccountsJson);
            Assert.Equal(before.BanksJson, after.BanksJson);
            Assert.Equal(before.AuditJson, after.AuditJson);
            var tax = Assert.Single(Apply(before.Taxes, after.Taxes));
            Assert.Equal(fixture.NewRow, tax.GetProperty("Id").GetGuid());
            Assert.Equal(FinanceFixtureRate, tax.GetProperty("Rate").GetDecimal());
            Assert.Equal((int)TaxDirection.Output, tax.GetProperty("Direction").GetInt32());
            Assert.False(tax.GetProperty("Recoverable").GetBoolean());
            AssertOwner(tax);
            return;
        }

        Assert.Equal(before.BanksJson, after.BanksJson);
        Assert.Equal(before.TaxesJson, after.TaxesJson);
        var changedId = operation is "account-update" or "account-deactivate" ? (Guid?)fixture.Account : null;
        var affected = Apply(before.Accounts, after.Accounts, changedId);
        var audits = Apply(before.Audit, after.Audit);
        Assert.Equal(affected.Length, audits.Length);
        var affectedKeys = affected.Select(row => row.GetProperty("Id").GetGuid().ToString()).Order();
        Assert.Equal(affectedKeys, audits.Select(row => row.GetProperty("EntityKey").GetString()).Order());
        Assert.All(audits, row =>
        {
            Assert.Equal("price-owner", row.GetProperty("UserName").GetString());
            Assert.Equal(changedId.HasValue ? 2 : 1, row.GetProperty("Operation").GetInt32());
        });
        if (operation == "account-import")
        {
            var imported = affected.ToDictionary(row => row.GetProperty("Code").GetString()!);
            Assert.Equal(FinCoaTemplate.IntlRows.Count, imported.Count);
            foreach (var template in FinCoaTemplate.IntlRows)
            {
                var row = imported[template.Code];
                AssertOwner(row);
                Assert.Equal(FinCoaTemplate.Intl, row.GetProperty("StandardScheme").GetString());
                Assert.Equal(template.Role, row.GetProperty("Role").GetString());
                if (template.ParentCode is { } parent && imported.TryGetValue(parent, out var parentRow))
                    Assert.Equal(parentRow.GetProperty("Id").GetGuid(), row.GetProperty("ParentId").GetGuid());
                else
                    Assert.Equal(JsonValueKind.Null, row.GetProperty("ParentId").ValueKind);
            }
            return;
        }
        var account = Assert.Single(affected);
        if (operation == "account-create")
        {
            Assert.Equal(fixture.NewRow, account.GetProperty("Id").GetGuid());
            Assert.Equal(fixture.Parent, account.GetProperty("ParentId").GetGuid());
            Assert.Equal("CHF", account.GetProperty("CurrencyCd").GetString());
            AssertOwner(account);
        }
        else
        {
            Assert.Equal("price-owner", account.GetProperty("Modifier").GetString());
            Assert.Equal("W600-GL", account.GetProperty("Code").GetString());
            Assert.Equal("fixture", account.GetProperty("StandardScheme").GetString());
            if (operation == "account-update")
            {
                Assert.Equal("Renamed account", account.GetProperty("Name").GetString());
                Assert.Equal(fixture.Parent, account.GetProperty("ParentId").GetGuid());
                Assert.Equal("CHF", account.GetProperty("CurrencyCd").GetString());
                Assert.Equal("W600-ROLE", account.GetProperty("Role").GetString());
                Assert.True(account.GetProperty("RequirePartner").GetBoolean());
            }
            else
            {
                Assert.False(account.GetProperty("IsActive").GetBoolean());
                Assert.Equal("Local original", account.GetProperty("Name").GetString());
            }
        }

        void AssertOwner(JsonElement row)
        {
            Assert.Equal(tenant, row.GetProperty("TenantId").GetGuid());
            Assert.Equal("price-owner", row.GetProperty("Creator").GetString());
        }

        static JsonElement[] Apply(Dictionary<Guid, JsonElement> original, Dictionary<Guid, JsonElement> current, Guid? modified = null)
        {
            foreach (var (id, row) in original)
                if (id != modified)
                    Assert.Equal(row.GetRawText(), current[id].GetRawText());
            if (modified is Guid changed)
            {
                Assert.Equal(original.Count, current.Count);
                Assert.NotEqual(original[changed].GetRawText(), current[changed].GetRawText());
                return [current[changed]];
            }
            return current.Where(row => !original.ContainsKey(row.Key)).Select(row => row.Value).ToArray();
        }
    }
}
