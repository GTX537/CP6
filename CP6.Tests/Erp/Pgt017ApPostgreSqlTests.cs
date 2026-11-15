using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.Fin;
using CP6.Core.Services.Platform;
using CP6.Entity.DomainModels.Fin;
using CP6.Entity.DomainModels.Pur;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private const string ApInvoicePath = "/api/fin/ap/invoice";
    private const string ApPaymentPath = "/api/fin/ap/payment";
    private const string ApInvoiceMenu = "fin-ap-invoice";
    private const string ApPaymentMenu = "fin-ap-payment";
    private static readonly DateTime ApDate = new(2026, 6, 15);
    private const decimal ApApplied = 12.34m;
    private const decimal ApFxDifference = 3.08m;

    [TenantManagementPostgresTheory]
    [InlineData("invoice-create", false)]
    [InlineData("invoice-create", true)]
    [InlineData("invoice-post", false)]
    [InlineData("invoice-post", true)]
    [InlineData("payment-pay", false)]
    [InlineData("payment-pay", true)]
    [InlineData("payment-settle", false)]
    [InlineData("payment-settle", true)]
    [InlineData("payment-reverse", false)]
    [InlineData("payment-reverse", true)]
    public async Task PGT017_ap_HTTP_write_and_pause_share_commit_order(string operation, bool pauseFirst)
    {
        await using var h = await Seed();
        await using var admin = await TenantManagementSeed();
        var f = await SeedAp(h, operation);
        var command = ApCommand(operation, f);
        await GrantLegacyAuthorityAction(h, command.Menu, command.Action);
        var connection = h.Db.Database.GetConnectionString()!;
        await using var host = await StartAssetApi(h);
        var actor = await Login(host, h);
        var manager = await Login(host, admin);
        using var read = await host.Send(OperationsRequest(manager, HttpMethod.Get, ManagementPath(h.Tenant)));
        var active = await NumberHttpData<TenantManagementSnapshotV1>(read);
        var suspend = new TenantManagementCommandV1("ap-pause", active.Revision, "suspend", null);
        var authority = await ReadPauseAuthority(h, admin.User);
        await using var observer = new NpgsqlConnection(connection);
        await observer.OpenAsync();
        var before = await ReadAp(observer, h.Tenant);

        var race = await ObserveAssetRace(connection, observer, command.Table, command.Verb, "Sys_Tenants", "UPDATE", pauseFirst,
            Write, () => host.Send(OperationsRequest(manager, HttpMethod.Post, ManagementPath(h.Tenant, "/commands"), suspend)),
            async () =>
            {
                Assert.Equal(before, await ReadAp(observer, h.Tenant));
                Assert.Equal(authority, await ReadPauseAuthority(h, admin.User));
            });
        using var paused = race.Authority;
        using var business = race.Business;
        var receipt = await NumberHttpData<TenantManagementReceiptV1>(paused);
        CheckPause(authority, await ReadPauseAuthority(h, admin.User), receipt, suspend);
        var after = await ReadAp(observer, h.Tenant);
        if (pauseFirst)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, business.StatusCode);
            Assert.Equal(before, after);
        }
        else
        {
            await CheckApSuccess(business, operation, f, before, after);
        }

        using var stale = await Write();
        Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode);
        Assert.Equal(after, await ReadAp(observer, h.Tenant));
        if (pauseFirst)
        {
            var resume = new TenantManagementCommandV1("ap-resume", receipt.Snapshot.Revision, "reactivate", null);
            using var resumed = await host.Send(OperationsRequest(manager, HttpMethod.Post, ManagementPath(h.Tenant, "/commands"), resume));
            Assert.Equal(HttpStatusCode.OK, resumed.StatusCode);
            actor = await Login(host, h);
            using var current = await Write();
            await CheckApSuccess(current, operation, f, before, await ReadAp(observer, h.Tenant));
        }
        output.WriteLine(JsonSerializer.Serialize(new
        {
            Operation = operation, PauseFirst = pauseFirst, race.FirstWait, race.RootWait,
            Status = (int)business.StatusCode, WriteSets = ApCounts(after),
        }));

        Task<HttpResponseMessage> Write() => host.Send(OperationsRequest(actor, HttpMethod.Post, command.Path, command.Body));
    }

    [TenantManagementPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PGT017_ap_settlement_and_role_removal_share_commit_order(bool revokeFirst)
    {
        const string operation = "payment-settle";
        await using var h = await Seed();
        await SeedFenceAdministrator(h);
        var f = await SeedAp(h, operation);
        var command = ApCommand(operation, f);
        await GrantLegacyAuthorityAction(h, command.Menu, command.Action);
        await using var host = await StartAssetApi(h);
        var actor = await Login(host, h);
        var manager = await Login(host, "fence-manager", "Fence1!Password", h.Tenant.ToString("N"));
        var revoke = await RevokeActions(host, manager, command.Action);
        var authority = await ReadPolicyWrites(h, revoke.OperationId);
        var connection = h.Db.Database.GetConnectionString()!;
        await using var observer = new NpgsqlConnection(connection);
        await observer.OpenAsync();
        var before = await ReadAp(observer, h.Tenant);

        var race = await ObserveAssetRace(connection, observer, command.Table, command.Verb, "Sys_RoleAction", "DELETE", revokeFirst,
            Write, () => host.Send(OperationsRequest(manager, HttpMethod.Put, PolicyWritePath, revoke)),
            async () =>
            {
                Assert.Equal(before, await ReadAp(observer, h.Tenant));
                Assert.Equal(authority, await ReadPolicyWrites(h, revoke.OperationId));
            });
        using var changed = race.Authority;
        using var business = race.Business;
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var policy = await ReadPolicyWrites(h, revoke.OperationId);
        Assert.NotEqual(authority.RoleRevision, policy.RoleRevision);
        Assert.Equal((authority.Generation + 1, authority.Snapshot + 1, authority.Outbox + 1, 1L, 1L, 1L),
            (policy.Generation, policy.Snapshot, policy.Outbox, policy.CurrentOutbox, policy.Audits, policy.Receipts));
        Assert.DoesNotContain(command.Action, policy.Actions.Split(','));
        var after = await ReadAp(observer, h.Tenant);
        if (revokeFirst)
        {
            await CheckApDenied(business, "IAM_FORBIDDEN");
            Assert.Equal(before, after);
        }
        else
        {
            await CheckApSuccess(business, operation, f, before, after);
        }
        using var stale = await Write();
        await CheckApDenied(stale, $"无权限：{command.Menu}:{command.Action}");
        Assert.Equal(after, await ReadAp(observer, h.Tenant));
        output.WriteLine(JsonSerializer.Serialize(new { RevokeFirst = revokeFirst, race.FirstWait, race.RootWait, Status = (int)business.StatusCode }));

        Task<HttpResponseMessage> Write() => host.Send(OperationsRequest(actor, HttpMethod.Post, command.Path, command.Body));
    }

    [TenantManagementPostgresTheory]
    [InlineData("invoice-post", "non-leaf", "E-FIN-104")]
    [InlineData("payment-pay", "non-leaf", "E-FIN-104")]
    [InlineData("payment-settle", "second-invoice", "E-FIN-202")]
    [InlineData("payment-reverse", "primary-voucher", "E-FIN-120")]
    [InlineData("payment-reverse", "diff-voucher", "E-FIN-120")]
    public async Task PGT017_ap_late_fin_error_rolls_back_every_write(string operation, string fault, string code)
    {
        await using var h = await Seed();
        var f = await SeedAp(h, operation, fault);
        var command = ApCommand(operation, f, fault);
        await GrantLegacyAuthorityAction(h, command.Menu, command.Action);
        await using var host = await StartAssetApi(h);
        var actor = await Login(host, h);
        await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await observer.OpenAsync();
        var before = await ReadAp(observer, h.Tenant);

        using var result = await host.Send(OperationsRequest(actor, HttpMethod.Post, command.Path, command.Body));
        var after = await ReadAp(observer, h.Tenant);
        output.WriteLine(JsonSerializer.Serialize(new
        {
            Operation = operation, Fault = fault, Status = (int)result.StatusCode,
            Before = ApCounts(before), After = ApCounts(after),
            SequencesBefore = ApSequences(before), SequencesAfter = ApSequences(after),
            InvoiceBefore = before.Rows("invoices").Select(x => AssetDecimal(x, "SettledAmount")),
            InvoiceAfter = after.Rows("invoices").Select(x => AssetDecimal(x, "SettledAmount")),
            OriginalJournals = before.Rows("journals").Select(x => new { Id = AssetId(x), Status = AssetInt(x, "Status") }),
            FinalJournals = after.Rows("journals").Select(x => new { Id = AssetId(x), Status = AssetInt(x, "Status") }),
        }));
        await CheckApError(result, code, fault == "non-leaf" ? [f.NonLeafCode] : []);
        Assert.Equal(before, after);
    }

    [TenantManagementPostgresTheory]
    [InlineData("invoice-create")]
    [InlineData("payment-pay")]
    public async Task PGT017_ap_new_rows_use_signed_tenant_and_generated_links(string operation)
    {
        await using var h = await Seed();
        await using var foreign = await Seed();
        var f = await SeedAp(h, operation);
        var other = await SeedAp(foreign, "invoice-create");
        var journal = new JournalEntryService(foreign.Db, new FiscalPeriodService(foreign.Db, 1), new FinSequenceService(foreign.Db));
        var foreignVoucher = new JournalEntry
        {
            VoucherDate = ApDate, Source = VoucherSource.AP, SourceDocNo = "FOREIGN-AP-SOURCE",
            Lines =
            [
                new JournalLine { AccountId = other.Expense, Debit = 42.34m },
                new JournalLine { AccountId = other.BankGl, Credit = 42.34m },
            ],
        };
        Assert.True((await journal.AutoPostAsync(foreignVoucher)).Ok);
        var command = ApCommand(operation, f);
        await GrantLegacyAuthorityAction(h, command.Menu, command.Action);
        var body = operation == "invoice-create"
            ? (object)ApDraft(f, foreign.Tenant, foreignVoucher.Id)
            : ApPay(f, foreign.Tenant, foreignVoucher.Id);
        await using var host = await StartAssetApi(h);
        var actor = await Login(host, h);
        await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await observer.OpenAsync();
        var before = await ReadAp(observer, h.Tenant);
        var foreignBefore = await ReadAp(observer, foreign.Tenant);

        using var result = await host.Send(OperationsRequest(actor, HttpMethod.Post, command.Path, body));
        var after = await ReadAp(observer, h.Tenant);
        var foreignAfter = await ReadAp(observer, foreign.Tenant);
        output.WriteLine(JsonSerializer.Serialize(new { Operation = operation, Status = (int)result.StatusCode,
            OwnBefore = ApCounts(before), OwnAfter = ApCounts(after), ForeignBefore = ApCounts(foreignBefore), ForeignAfter = ApCounts(foreignAfter) }));
        await CheckApSuccess(result, operation, f, before, after);
        Assert.Equal(foreignBefore, foreignAfter);
    }

    [TenantManagementPostgresTheory]
    [InlineData("expense-account", "E-FIN-108")]
    [InlineData("tax-code", "E-FIN-205")]
    [InlineData("cost-center", "E-FIN-206")]
    [InlineData("historical-cost-center", "E-FIN-206")]
    [InlineData("purchase-order", "E-PUR-027")]
    [InlineData("original-invoice", "E-FIN-202")]
    [InlineData("bank-account", "E-FIN-210")]
    [InlineData("unused-discount-account", "E-FIN-003")]
    public async Task PGT017_ap_new_store_links_require_current_tenant(string link, string code)
    {
        await using var h = await Seed();
        await using var foreign = await Seed();
        var operation = link switch
        {
            "historical-cost-center" => "invoice-post",
            "bank-account" => "payment-pay",
            "unused-discount-account" => "payment-settle",
            _ => "invoice-create",
        };
        var f = await SeedAp(h, operation);
        var other = await SeedAp(foreign, "payment-settle");
        var command = ApCommand(operation, f);
        await GrantLegacyAuthorityAction(h, command.Menu, command.Action);
        var draft = ApDraft(f);
        object? body = draft;
        string[] args = [];
        switch (link)
        {
            case "expense-account":
                draft.Lines[0].ExpenseAccountId = other.Expense;
                args = ["1"];
                break;
            case "tax-code":
                draft.Lines[0].TaxCodeId = other.Tax;
                break;
            case "cost-center":
                draft.Lines[0].CostCenterId = other.Center;
                break;
            case "historical-cost-center":
                var existing = await h.Db.ApInvoiceLines.SingleAsync();
                existing.CostCenterId = other.Center;
                await h.Db.SaveChangesAsync();
                body = null;
                break;
            case "purchase-order":
                draft.PurchaseOrderId = await SeedApPurchase(foreign);
                break;
            case "original-invoice":
                draft.IsCreditMemo = true;
                draft.OriginInvoiceId = other.Invoice;
                break;
            case "bank-account":
                var pay = ApPay(f);
                pay.BankAccountId = other.Bank;
                body = pay;
                break;
            case "unused-discount-account":
                var apply = ApApply(f);
                apply.DiscountAccountId = other.Expense;
                body = new { Applies = new[] { apply } };
                break;
        }
        await using var host = await StartAssetApi(h);
        var actor = await Login(host, h);
        await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await observer.OpenAsync();
        var before = await ReadAp(observer, h.Tenant);
        var foreignBefore = await ReadAp(observer, foreign.Tenant);

        using var result = await host.Send(OperationsRequest(actor, HttpMethod.Post, command.Path, body));
        var after = await ReadAp(observer, h.Tenant);
        var foreignAfter = await ReadAp(observer, foreign.Tenant);
        output.WriteLine(JsonSerializer.Serialize(new { Link = link, Status = (int)result.StatusCode,
            OwnBefore = ApCounts(before), OwnAfter = ApCounts(after), ForeignUnchanged = foreignBefore == foreignAfter }));
        await CheckApError(result, code, args);
        Assert.Equal(before, after);
        Assert.Equal(foreignBefore, foreignAfter);
    }

    [CurrentPermissionPostgresFact]
    public async Task PGT017_ap_same_tenant_document_links_and_empty_optional_keys_remain_valid()
    {
        await using var h = await Seed();
        var f = await SeedAp(h, "payment-settle");
        await GrantLegacyAuthorityAction(h, ApInvoiceMenu, "add");
        var purchase = await SeedApPurchase(h);
        var body = ApDraft(f);
        body.Id = Guid.NewGuid();
        body.IsCreditMemo = true;
        body.OriginInvoiceId = f.Invoice;
        body.PurchaseOrderId = purchase;
        body.Lines[0].Id = Guid.NewGuid();
        body.Lines[0].TaxCodeId = Guid.Empty;
        body.Lines[0].CostCenterId = Guid.Empty;
        body.Lines[0].Amount = 27.15m;
        await using var host = await StartAssetApi(h);
        var actor = await Login(host, h);
        await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await observer.OpenAsync();

        using var result = await host.Send(OperationsRequest(actor, HttpMethod.Post, ApInvoicePath, body));
        var payload = await ReadApSuccessData(result);
        Assert.Equal(body.Id, payload.GetProperty("id").GetGuid());
        var after = await ReadAp(observer, h.Tenant);
        var invoice = after.Row("invoices", body.Id);
        Assert.True(invoice.GetProperty("IsCreditMemo").GetBoolean());
        Assert.Equal(purchase, AssetId(invoice, "PurchaseOrderId"));
        Assert.Equal(f.Invoice, AssetId(invoice, "OriginInvoiceId"));
        Assert.Equal(27.15m, AssetDecimal(invoice, "GrossAmount"));
        Assert.Equal(0m, AssetDecimal(invoice, "TaxAmount"));
        var line = after.Row("invoiceLines", body.Lines[0].Id);
        Assert.Equal(Guid.Empty, AssetId(line, "TaxCodeId"));
        Assert.Equal(Guid.Empty, AssetId(line, "CostCenterId"));
        Assert.Equal(h.Tenant, AssetId(line, "TenantId"));
    }

    private static async Task<Guid> SeedApPurchase(Harness h)
    {
        var order = new PurchaseOrder { Id = Guid.NewGuid(), PoNo = "W608-PO-" + h.Tenant.ToString("N")[..10],
            SupplierId = "SUP-AP", OrderDate = ApDate, Status = PoStatus.Confirmed };
        h.Db.PurchaseOrders.Add(order);
        await h.Db.SaveChangesAsync();
        return order.Id;
    }

    private sealed record ApFixture(Guid Tenant, Guid Invoice, Guid Payment, Guid Line, Guid Expense, Guid Tax, Guid Center,
        Guid Bank, Guid BankGl, Guid ApGl, Guid InputGl, Guid GainGl, string NonLeafCode, Guid PrimaryJournal, Guid DiffJournal);

    private static async Task<ApFixture> SeedAp(Harness h, string operation, string? fault = null)
    {
        var db = h.Db;
        await new GlAccountService(db).ImportTemplateAsync(FinCoaTemplate.CnGaap, "fixture");
        await PostingRuleSeed.EnsureSeededAsync(db);
        var accounts = await db.GlAccounts.AsNoTracking().ToListAsync();
        var expense = accounts.Single(a => a.Code == "1401");
        var bankGl = accounts.Single(a => a.Code == "1002");
        var ap = accounts.Single(a => a.Role == "AP_CONTROL");
        var taxGl = accounts.Single(a => a.Role == "TAX_INPUT");
        var gain = accounts.Single(a => a.Role == "FX_GAIN");
        var nonLeaf = accounts.Where(a => !a.IsLeaf).OrderBy(a => a.Code).First();
        var bank = new BankAccount { Id = Guid.NewGuid(), Code = "W608", Name = "AP fixture bank",
            GlAccountId = operation == "payment-pay" && fault == "non-leaf" ? nonLeaf.Id : bankGl.Id };
        var tax = new TaxCode { Id = Guid.NewGuid(), Code = "AP12", Name = "Recoverable input", Rate = 0.125m,
            Direction = TaxDirection.Input, Recoverable = true };
        var center = new CostCenter { Id = Guid.NewGuid(), Code = "AP-CC", Name = "AP cost center" };
        db.AddRange(bank, tax, center);
        await db.SaveChangesAsync();
        var f = new ApFixture(h.Tenant, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), expense.Id, tax.Id, center.Id,
            bank.Id, bankGl.Id, ap.Id, taxGl.Id, gain.Id, nonLeaf.Code, Guid.Empty, Guid.Empty);
        var seq = new FinSequenceService(db);
        var journal = new JournalEntryService(db, new FiscalPeriodService(db, 1), seq);
        var engine = new AutoVoucherEngine(db, journal);
        var invoices = new ApInvoiceService(db, engine, seq);
        var payments = new PaymentService(db, engine, journal, seq);
        var settlement = new ApSettlementService(db, journal);
        if (operation is "invoice-post" or "payment-settle" or "payment-reverse")
        {
            var invoice = ApDraft(f);
            invoice.FxRate = 1.25m;
            if (fault == "non-leaf") invoice.Lines[0].ExpenseAccountId = nonLeaf.Id;
            Assert.True((await invoices.CreateAsync(invoice, "fixture")).Ok);
            if (operation != "invoice-post")
            {
                var posted = await invoices.PostAsync(invoice.Id, "fixture");
                Assert.True(posted.Ok, posted.Code);
                var payment = ApPay(f);
                var paid = await payments.PayAsync(payment, "fixture");
                Assert.True(paid.Ok, paid.Code);
                f = f with { PrimaryJournal = payment.JournalEntryId!.Value };
                if (operation == "payment-reverse")
                {
                    var applied = await settlement.SettleAsync(payment.Id, [ApApply(f)], "fixture");
                    Assert.True(applied.Ok, applied.Code);
                    var diff = (await db.ApSettlements.SingleAsync()).DiffJournalEntryId!.Value;
                    f = f with { DiffJournal = diff };
                    if (fault is "primary-voucher" or "diff-voucher")
                    {
                        var malformed = await db.JournalEntries.SingleAsync(
                            j => j.Id == (fault == "primary-voucher" ? f.PrimaryJournal : f.DiffJournal));
                        malformed.Status = JournalStatus.PendingReview;
                        await db.SaveChangesAsync();
                    }
                }
            }
        }
        return f;
    }

    private static ApInvoice ApDraft(ApFixture f, Guid requestTenant = default, Guid? requestJournal = null) => new()
    {
        Id = f.Invoice, TenantId = requestTenant, JournalEntryId = requestJournal,
        SupplierId = "SUP-AP", SupplierInvoiceNo = "FIXTURE-AP", InvoiceDate = ApDate, DueDate = ApDate.AddDays(30),
        CurrencyCd = "USD", FxRate = 1.234567m, RmaId = "RAW-EXTERNAL-RMA",
        Lines =
        [
            new ApInvoiceLine { Id = f.Line, TenantId = requestTenant, InvoiceId = Guid.NewGuid(), ItemId = "RAW-SKU",
                Qty = 12.34567891m, UnitPrice = 100.0123m, Amount = 1234.56m,
                ExpenseAccountId = f.Expense, TaxCodeId = f.Tax, CostCenterId = f.Center },
        ],
    };

    private static Payment ApPay(ApFixture f, Guid requestTenant = default, Guid? requestJournal = null) => new()
    {
        Id = f.Payment, TenantId = requestTenant, JournalEntryId = requestJournal, SupplierId = "SUP-AP",
        PayDate = ApDate, CurrencyCd = "USD", FxRate = 1.000001m, Amount = 78.90m, BankAccountId = f.Bank,
        Method = PaymentMethod.Check,
    };

    private static SettlementApply ApApply(ApFixture f) => new() { InvoiceId = f.Invoice, AppliedAmount = ApApplied };

    private sealed record ApWrite(string Path, string Menu, string Action, string Table, string Verb, object? Body);

    private static ApWrite ApCommand(string operation, ApFixture f, string? fault = null) => operation switch
    {
        "invoice-create" => new(ApInvoicePath, ApInvoiceMenu, "add", "Fin_ApInvoice", "INSERT", ApDraft(f)),
        "invoice-post" => new($"{ApInvoicePath}/{f.Invoice}/post", ApInvoiceMenu, "post", "Fin_ApInvoice", "UPDATE", null),
        "payment-pay" => new(ApPaymentPath, ApPaymentMenu, "add", "Fin_Payment", "INSERT", ApPay(f)),
        "payment-settle" => new($"{ApPaymentPath}/{f.Payment}/settle", ApPaymentMenu, "settle", "Fin_ApSettlement", "INSERT",
            new { Applies = fault == "second-invoice"
                ? new[] { ApApply(f), new SettlementApply { InvoiceId = Guid.NewGuid(), AppliedAmount = 1.23m } }
                : new[] { ApApply(f) } }),
        "payment-reverse" => new($"{ApPaymentPath}/{f.Payment}/reverse", ApPaymentMenu, "reverse", "Fin_Payment", "UPDATE",
            new { Reason = "Duplicate bank transfer" }),
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };

    private static async Task<AssetRows> ReadAp(NpgsqlConnection observer, Guid tenant)
    {
        await using var command = new NpgsqlCommand("""
            SELECT jsonb_build_object(
              'invoices', ARRAY(SELECT to_jsonb(r) FROM public."Fin_ApInvoice" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'invoiceLines', ARRAY(SELECT to_jsonb(r) FROM public."Fin_ApInvoiceLine" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'payments', ARRAY(SELECT to_jsonb(r) FROM public."Fin_Payment" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'settlements', ARRAY(SELECT to_jsonb(r) FROM public."Fin_ApSettlement" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'journals', ARRAY(SELECT to_jsonb(r) FROM public."Fin_JournalEntry" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'lines', ARRAY(SELECT to_jsonb(r) FROM public."Fin_JournalLine" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'periods', ARRAY(SELECT to_jsonb(r) FROM public."Fin_FiscalPeriod" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'numbers', ARRAY(SELECT to_jsonb(r) FROM public."Fin_Sequence" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'audit', ARRAY(SELECT to_jsonb(r) FROM public."Sys_FieldAuditLogs" r WHERE r."TenantId"=@tenant
                AND r."EntityName" IN ('ApInvoice','ApInvoiceLine','Payment','ApSettlement','JournalEntry','JournalLine','FiscalPeriod','FinSequence')
                ORDER BY r."Id")
            )::text
            """, observer);
        command.Parameters.AddWithValue("tenant", tenant);
        return new((string)(await command.ExecuteScalarAsync())!);
    }

    private static readonly string[] ApTables = ["invoices", "invoiceLines", "payments", "settlements", "journals", "lines", "periods", "numbers", "audit"];
    private static Dictionary<string, int> ApCounts(AssetRows rows) => ApTables.ToDictionary(t => t, t => rows.Rows(t).Length);
    private static Dictionary<string, int> ApSequences(AssetRows rows) => rows.Rows("numbers")
        .ToDictionary(r => r.GetProperty("SeqKey").GetString()!, r => AssetInt(r, "CurrentValue"));

    private static async Task CheckApError(HttpResponseMessage result, string code, string[] args)
    {
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        var json = await result.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(400, json.GetProperty("code").GetInt32());
        Assert.Equal(code, json.GetProperty("message").GetString());
        Assert.Equal(args, json.GetProperty("args").EnumerateArray().Select(x => x.ToString()).ToArray());
    }

    private static async Task CheckApDenied(HttpResponseMessage result, string code)
    {
        Assert.Equal(HttpStatusCode.Forbidden, result.StatusCode);
        var json = await result.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(403, json.GetProperty("code").GetInt32());
        Assert.Equal(code, json.GetProperty("message").GetString());
    }

    private static async Task<JsonElement> ReadApSuccessData(HttpResponseMessage result)
    {
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        var envelope = await result.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, envelope.GetProperty("code").GetInt32());
        Assert.Equal("OK", envelope.GetProperty("message").GetString());
        return envelope.GetProperty("data");
    }

    private static async Task CheckApSuccess(HttpResponseMessage result, string operation, ApFixture f, AssetRows before, AssetRows after)
    {
        var payload = await ReadApSuccessData(result);
        if (operation is not ("invoice-create" or "payment-pay")) Assert.Equal(JsonValueKind.Null, payload.ValueKind);
        var expectedChanges = operation switch
        {
            "invoice-create" => new[] { ("invoices", 1), ("invoiceLines", 1), ("numbers", 1) },
            "invoice-post" => new[] { ("journals", 1), ("lines", 3), ("periods", 1), ("numbers", 1) },
            "payment-pay" => new[] { ("payments", 1), ("journals", 1), ("lines", 2), ("periods", 1), ("numbers", 2) },
            "payment-settle" => new[] { ("settlements", 1), ("journals", 1), ("lines", 2) },
            "payment-reverse" => new[] { ("settlements", -1), ("journals", 2), ("lines", 4) },
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };
        var deltas = expectedChanges.ToDictionary(x => x.Item1, x => x.Item2);
        foreach (var table in ApTables)
        {
            Assert.Equal(before.Rows(table).Length + deltas.GetValueOrDefault(table), after.Rows(table).Length);
            Assert.All(after.Rows(table), row => Assert.Equal(f.Tenant, AssetId(row, "TenantId")));
            var prior = before.Rows(table).Where(row => !ApRowCanChange(operation, f, table, row));
            var current = after.Rows(table).ToDictionary(row => AssetId(row), row => row.GetRawText());
            foreach (var row in prior) Assert.Equal(row.GetRawText(), current[AssetId(row)]);
        }
        var sequence = ApSequences(after);
        switch (operation)
        {
            case "invoice-create":
                var draft = after.Row("invoices", f.Invoice);
                Assert.Equal(f.Invoice, payload.GetProperty("id").GetGuid());
                Assert.Equal("AP-2026-06-00001", payload.GetProperty("no").GetString());
                Assert.Equal(payload.GetProperty("no").GetString(), draft.GetProperty("No").GetString());
                Assert.Equal("price-owner", draft.GetProperty("Creator").GetString());
                Assert.Equal((int)ApInvoiceStatus.Draft, AssetInt(draft, "Status"));
                Assert.Equal(JsonValueKind.Null, draft.GetProperty("JournalEntryId").ValueKind);
                Assert.Equal(("SUP-AP", "USD", "RAW-EXTERNAL-RMA"),
                    (draft.GetProperty("SupplierId").GetString(), draft.GetProperty("CurrencyCd").GetString(), draft.GetProperty("RmaId").GetString()));
                Assert.Equal((1234.56m, 154.32m, 1388.88m, 1.234567m),
                    (AssetDecimal(draft, "NetAmount"), AssetDecimal(draft, "TaxAmount"), AssetDecimal(draft, "GrossAmount"), AssetDecimal(draft, "FxRate")));
                var line = after.Row("invoiceLines", f.Line);
                Assert.Equal((f.Invoice, f.Expense, f.Tax, f.Center),
                    (AssetId(line, "InvoiceId"), AssetId(line, "ExpenseAccountId"), AssetId(line, "TaxCodeId"), AssetId(line, "CostCenterId")));
                Assert.Equal((12.34567891m, 100.0123m, 1234.56m, 154.32m),
                    (AssetDecimal(line, "Qty"), AssetDecimal(line, "UnitPrice"), AssetDecimal(line, "Amount"), AssetDecimal(line, "TaxAmount")));
                Assert.Equal("RAW-SKU", line.GetProperty("ItemId").GetString());
                Assert.Equal(1, sequence["AP"]);
                break;
            case "invoice-post":
                var invoice = after.Row("invoices", f.Invoice);
                Assert.Equal((int)ApInvoiceStatus.Posted, AssetInt(invoice, "Status"));
                var invLines = CheckApJournal(after, AssetId(invoice, "JournalEntryId"), invoice.GetProperty("No").GetString()!, 1736.10m);
                var expenseLine = Assert.Single(invLines, row => AssetId(row, "AccountId") == f.Expense);
                Assert.Equal(f.Center, AssetId(expenseLine, "CostCenterId"));
                Assert.Equal(1543.20m, AssetDecimal(expenseLine, "Debit"));
                Assert.Equal(1234.56m, AssetDecimal(expenseLine, "OrigAmount"));
                Assert.Equal(192.90m, AssetDecimal(Assert.Single(invLines, row => AssetId(row, "AccountId") == f.InputGl), "Debit"));
                Assert.Equal("SUP-AP", Assert.Single(invLines, row => AssetId(row, "AccountId") == f.ApGl).GetProperty("PartnerId").GetString());
                Assert.Equal(1, sequence["AP"]);
                Assert.Equal(1, sequence["GL"]);
                break;
            case "payment-pay":
                var pay = after.Row("payments", f.Payment);
                Assert.Equal(f.Payment, payload.GetProperty("id").GetGuid());
                Assert.Equal("PAY-2026-06-00001", payload.GetProperty("no").GetString());
                Assert.Equal(payload.GetProperty("no").GetString(), pay.GetProperty("No").GetString());
                Assert.Equal("price-owner", pay.GetProperty("Creator").GetString());
                Assert.Equal(f.Bank, AssetId(pay, "BankAccountId"));
                Assert.Equal((int)PaymentStatus.Posted, AssetInt(pay, "Status"));
                Assert.Equal((int)PaymentMethod.Check, AssetInt(pay, "Method"));
                Assert.Equal((78.90m, 1.000001m, "USD"),
                    (AssetDecimal(pay, "Amount"), AssetDecimal(pay, "FxRate"), pay.GetProperty("CurrencyCd").GetString()));
                var payLines = CheckApJournal(after, AssetId(pay, "JournalEntryId"), pay.GetProperty("No").GetString()!, 78.90m);
                Assert.Equal(78.90m, AssetDecimal(Assert.Single(payLines, row => AssetId(row, "AccountId") == f.ApGl), "Debit"));
                Assert.Equal(78.90m, AssetDecimal(Assert.Single(payLines, row => AssetId(row, "AccountId") == f.BankGl), "Credit"));
                Assert.Equal(1, sequence["PAY"]);
                Assert.Equal(1, sequence["GL"]);
                break;
            case "payment-settle":
                var matched = Assert.Single(after.Rows("settlements"));
                Assert.Equal((f.Payment, f.Invoice), (AssetId(matched, "PaymentId"), AssetId(matched, "ApInvoiceId")));
                Assert.Equal((ApApplied, ApFxDifference, (int)SettlementDiffType.FxDiff),
                    (AssetDecimal(matched, "SettledAmount"), AssetDecimal(matched, "DiffAmount"), AssetInt(matched, "DiffType")));
                var diffLines = CheckApJournal(after, AssetId(matched, "DiffJournalEntryId"), "PAY-2026-06-00001#AP-2026-06-00001#DIFF", ApFxDifference);
                Assert.Equal(ApFxDifference, AssetDecimal(Assert.Single(diffLines, row => AssetId(row, "AccountId") == f.ApGl), "Debit"));
                Assert.Equal(ApFxDifference, AssetDecimal(Assert.Single(diffLines, row => AssetId(row, "AccountId") == f.GainGl), "Credit"));
                CheckApBalance(after, f, ApApplied, ApInvoiceStatus.PartiallySettled, PaymentStatus.Posted);
                Assert.Equal(ApSequences(before)["GL"] + 1, sequence["GL"]);
                break;
            case "payment-reverse":
                CheckApBalance(after, f, 0m, ApInvoiceStatus.Posted, PaymentStatus.Reversed);
                Assert.Empty(after.Rows("settlements"));
                foreach (var id in new[] { f.PrimaryJournal, f.DiffJournal })
                {
                    var origin = after.Row("journals", id);
                    Assert.Equal((int)JournalStatus.Reversed, AssetInt(origin, "Status"));
                    var reversal = after.Row("journals", AssetId(origin, "ReversedById"));
                    Assert.Equal(id, AssetId(reversal, "ReverseOfId"));
                    var reverseLines = CheckApJournal(after, AssetId(reversal), origin.GetProperty("No").GetString()!,
                        id == f.PrimaryJournal ? 78.90m : ApFxDifference, VoucherSource.Reversal);
                    Assert.Equal(ApJournalLines(before, id).Select(row => (AssetId(row, "AccountId"), AssetDecimal(row, "Debit"), AssetDecimal(row, "Credit"))).Order(),
                        reverseLines.Select(row => (AssetId(row, "AccountId"), AssetDecimal(row, "Credit"), AssetDecimal(row, "Debit"))).Order());
                }
                Assert.Equal(ApSequences(before)["GL"] + 2, sequence["GL"]);
                break;
        }
        if (operation != "invoice-create")
        {
            var period = Assert.Single(after.Rows("periods"));
            Assert.Equal((2026, 6, (int)PeriodStatus.Open), (AssetInt(period, "Year"), AssetInt(period, "Month"), AssetInt(period, "Status")));
        }
    }

    private static bool ApRowCanChange(string operation, ApFixture f, string table, JsonElement row) =>
        (operation, table) switch
        {
            ("invoice-post" or "payment-settle" or "payment-reverse", "invoices") => AssetId(row) == f.Invoice,
            ("payment-settle" or "payment-reverse", "payments") => AssetId(row) == f.Payment,
            ("payment-reverse", "settlements") =>
                AssetId(row, "PaymentId") == f.Payment && AssetId(row, "ApInvoiceId") == f.Invoice,
            ("payment-reverse", "journals") => AssetId(row) == f.PrimaryJournal || AssetId(row) == f.DiffJournal,
            ("payment-settle" or "payment-reverse", "numbers") => row.GetProperty("SeqKey").GetString() == "GL",
            _ => false,
        };

    private static void CheckApBalance(AssetRows rows, ApFixture f, decimal applied, ApInvoiceStatus invoiceStatus, PaymentStatus paymentStatus)
    {
        var invoice = rows.Row("invoices", f.Invoice);
        var payment = rows.Row("payments", f.Payment);
        Assert.Equal((applied, (int)invoiceStatus), (AssetDecimal(invoice, "SettledAmount"), AssetInt(invoice, "Status")));
        Assert.Equal((applied, (int)paymentStatus), (AssetDecimal(payment, "SettledAmount"), AssetInt(payment, "Status")));
        Assert.Equal(f.PrimaryJournal, AssetId(payment, "JournalEntryId"));
    }

    private static JsonElement[] ApJournalLines(AssetRows rows, Guid journal) =>
        rows.Rows("lines").Where(row => AssetId(row, "EntryId") == journal).ToArray();

    private static JsonElement[] CheckApJournal(AssetRows rows, Guid journal, string sourceNo, decimal amount, VoucherSource source = VoucherSource.AP)
    {
        var entry = rows.Row("journals", journal);
        Assert.Equal((int)source, AssetInt(entry, "Source"));
        Assert.Equal(sourceNo, entry.GetProperty("SourceDocNo").GetString());
        Assert.Equal((int)JournalStatus.Posted, AssetInt(entry, "Status"));
        Assert.True(entry.GetProperty("AutoPosted").GetBoolean());
        Assert.Equal(AssetId(Assert.Single(rows.Rows("periods"))), AssetId(entry, "PeriodId"));
        var lines = ApJournalLines(rows, journal);
        Assert.Equal(amount, lines.Sum(row => AssetDecimal(row, "Debit")));
        Assert.Equal(amount, lines.Sum(row => AssetDecimal(row, "Credit")));
        return lines;
    }
}
