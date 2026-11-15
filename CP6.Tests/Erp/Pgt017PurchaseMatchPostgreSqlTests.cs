using System.Net;
using System.Text.Json;
using CP6.Core.Services.Fin;
using CP6.Core.Services.Platform;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DomainModels.Fin;
using CP6.Entity.DomainModels.Pub;
using CP6.Entity.DomainModels.Pur;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static readonly DateTime PurchaseInvoiceDate = new(2026, 6, 15);
    private const string PurchaseItem = "W631-PAPER";
    private const decimal PurchaseQty = 2.5m;
    private const decimal PurchasePrice = 12.34m;
    private const decimal PurchaseVariancePrice = 13.67m;
    private const string PurchaseMatchPath = "/api/pur/match";

    [TenantManagementPostgresTheory]
    [InlineData("add", false)]
    [InlineData("add", true)]
    [InlineData("release", false)]
    [InlineData("release", true)]
    [InlineData("reject", false)]
    [InlineData("reject", true)]
    public async Task PGT017_purchase_match_and_pause_share_native_commit_order(string action, bool pauseFirst)
    {
        await using var h = await Seed();
        await using var admin = await TenantManagementSeed();
        var fixture = await SeedPurchaseMatch(h);
        await GrantLegacyAuthorityAction(h, "pur-match", "add");
        if (action != "add") await GrantLegacyAuthorityAction(h, "pur-match", action);
        await using var host = await StartPurchaseMatchApi(h);
        var actor = await Login(host, h);
        var manager = await Login(host, admin);
        var path = PurchaseMatchPath;
        var body = PurchaseMatchRequest(fixture, PurchasePrice);
        if (action != "add")
        {
            var suspended = await SubmitPurchaseMatch(host, actor, fixture, PurchaseVariancePrice);
            Assert.Equal((int)MatchStatus.Suspended, suspended.GetProperty("status").GetInt32());
            Assert.Equal("variance over tolerance", suspended.GetProperty("note").GetString());
            path = $"{PurchaseMatchPath}/{PurchaseMatchNumber(suspended)}/{action}";
            body = new { Note = $"reviewed {action}" };
        }
        using var tenant = await host.Send(OperationsRequest(manager, HttpMethod.Get, ManagementPath(h.Tenant)));
        var active = await NumberHttpData<TenantManagementSnapshotV1>(tenant);
        var pause = new TenantManagementCommandV1("purchase-match-pause", active.Revision, "suspend", null);
        var authority = await ReadPauseAuthority(h, admin.User);
        var connection = h.Db.Database.GetConnectionString()!;
        await using var observer = new NpgsqlConnection(connection);
        await observer.OpenAsync();
        var before = await ReadPurchaseMatch(observer, h.Tenant);
        Assert.Empty(before.Rows("ap"));
        Assert.Empty(before.Rows("journals"));
        Assert.Equal(0m, AssetDecimal(Assert.Single(before.Rows("po-lines")), "InvoicedQty"));

        var race = await ObserveAssetRace(connection, observer, "Pur_ThreeWayMatch", action == "add" ? "INSERT" : "UPDATE",
            "Sys_Tenants", "UPDATE", pauseFirst, Write,
            () => host.Send(OperationsRequest(manager, HttpMethod.Post, ManagementPath(h.Tenant, "/commands"), pause)),
            async () =>
            {
                Assert.Equal(before, await ReadPurchaseMatch(observer, h.Tenant));
                Assert.Equal(authority, await ReadPauseAuthority(h, admin.User));
            });
        using var paused = race.Authority;
        using var business = race.Business;
        var receipt = await NumberHttpData<TenantManagementReceiptV1>(paused);
        CheckPause(authority, await ReadPauseAuthority(h, admin.User), receipt, pause);
        var afterRace = await ReadPurchaseMatch(observer, h.Tenant);
        if (pauseFirst)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, business.StatusCode);
            Assert.Equal(before, afterRace);
            await ResumeAssetTenant(host, manager, h.Tenant, receipt);
            actor = await Login(host, h);
            using var resumed = await Write();
            await CheckPurchaseOrderResult(resumed, action, fixture, before, await ReadPurchaseMatch(observer, h.Tenant));
        }
        else
        {
            await CheckPurchaseOrderResult(business, action, fixture, before, afterRace);
            using var stale = await Write();
            Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode);
            Assert.Equal(afterRace, await ReadPurchaseMatch(observer, h.Tenant));
        }

        output.WriteLine(JsonSerializer.Serialize(new
        {
            action, pauseFirst, race.FirstWait, race.RootWait, Status = (int)business.StatusCode,
            Before = PurchaseState(before), DuringPause = PurchaseState(afterRace),
            Final = PurchaseState(await ReadPurchaseMatch(observer, h.Tenant)),
        }));

        Task<HttpResponseMessage> Write() => host.Send(OperationsRequest(actor, HttpMethod.Post, path, body));
    }

    [CurrentPermissionPostgresFact]
    public async Task W631_same_match_number_is_independent_across_two_tenants()
    {
        var first = await MatchForTenant();
        var second = await MatchForTenant();
        Assert.Equal(first.Number, second.Number);
        Assert.NotEqual(first.Tenant, second.Tenant);
        Assert.NotEqual(first.MatchId, second.MatchId);
        Assert.NotEqual(first.Ap, second.Ap);
        output.WriteLine(JsonSerializer.Serialize(new { first.Number, first.Tenant, FirstAp = first.Ap, OtherTenant = second.Tenant, OtherAp = second.Ap }));

        async Task<(string Number, Guid Tenant, Guid MatchId, Guid Ap)> MatchForTenant()
        {
            await using var h = await Seed();
            var fixture = await SeedPurchaseMatch(h);
            await GrantLegacyAuthorityAction(h, "pur-match", "add");
            await using var host = await StartPurchaseMatchApi(h);
            var actor = await Login(host, h);
            var created = await SubmitPurchaseMatch(host, actor, fixture, PurchasePrice);
            var matchNo = PurchaseMatchNumber(created);
            using var fetched = await host.Send(OperationsRequest(actor, HttpMethod.Get, $"{PurchaseMatchPath}/{matchNo}"));
            var data = await ReadApSuccessData(fetched);
            Assert.Equal(fixture.PoNo, data.GetProperty("poNo").GetString());
            Assert.Equal(fixture.Tenant, data.GetProperty("tenantId").GetGuid());
            Assert.Equal(fixture.Tenant, Assert.Single(data.GetProperty("lines").EnumerateArray()).GetProperty("tenantId").GetGuid());
            await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString());
            await observer.OpenAsync();
            var state = await ReadPurchaseMatch(observer, h.Tenant);
            CheckPurchaseMatch(state, fixture, data.GetProperty("id").GetGuid(), matchNo, MatchStatus.Passed, PurchasePrice, true);
            var match = Assert.Single(state.Rows("matches"));
            CheckPurchasePostedAp(state, fixture, match, PurchasePrice, 30.85m, 3.09m, 33.94m);
            return (matchNo, h.Tenant, AssetId(match), AssetId(match, "ApInvoiceId"));
        }
    }

    [CurrentPermissionPostgresFact]
    public async Task W631_late_po_save_failure_rolls_back_purchase_ap_gl_and_numbers()
    {
        await using var h = await Seed();
        var fixture = await SeedPurchaseMatch(h);
        await GrantLegacyAuthorityAction(h, "pur-match", "add");
        await using var host = await StartPurchaseMatchApi(h);
        var actor = await Login(host, h);
        await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await observer.OpenAsync();
        var before = await ReadPurchaseMatch(observer, h.Tenant);
        await using var install = new NpgsqlCommand($"""
            CREATE FUNCTION public.w631_fail_after_match() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
              IF NOT EXISTS (
                SELECT 1 FROM public."Pur_ThreeWayMatch" m
                JOIN public."Pur_PurchaseOrderLine" l ON l."TenantId"=m."TenantId" AND l."PoNo"=m."PoNo"
                JOIN public."Fin_ApInvoice" a ON a."Id"=m."ApInvoiceId"
                JOIN public."Fin_JournalEntry" j ON j."Id"=a."JournalEntryId"
                WHERE m."TenantId"=NEW."TenantId" AND m."PoNo"=NEW."PoNo" AND m."Status"=0
                  AND l."InvoicedQty"=2.5 AND a."Status"=1 AND j."Status"=2
              ) THEN
                RAISE EXCEPTION 'W631 expected match, PO quantity, AP and journal inside this transaction';
              END IF;
              RAISE check_violation USING MESSAGE = 'W631 intentional final PO update failure';
            END $$;
            CREATE TRIGGER w631_fail_after_match BEFORE UPDATE OF "Status" ON public."Pur_PurchaseOrder"
            FOR EACH ROW WHEN (NEW."Id" = '{fixture.Po:D}'::uuid AND NEW."Status" = 6)
            EXECUTE FUNCTION public.w631_fail_after_match();
            """, observer);
        await install.ExecuteNonQueryAsync();
        try
        {
            using var failed = await host.Send(OperationsRequest(actor, HttpMethod.Post, PurchaseMatchPath,
                PurchaseMatchRequest(fixture, PurchasePrice)));
            await CheckPurchaseError(failed, HttpStatusCode.ServiceUnavailable, "AUTHORITY_FENCE_UNAVAILABLE");
            Assert.Equal(before, await ReadPurchaseMatch(observer, h.Tenant));
        }
        finally
        {
            await using var remove = new NpgsqlCommand("""
                DROP TRIGGER w631_fail_after_match ON public."Pur_PurchaseOrder";
                DROP FUNCTION public.w631_fail_after_match();
                """, observer);
            await remove.ExecuteNonQueryAsync();
        }

        var matched = await SubmitPurchaseMatch(host, actor, fixture, PurchasePrice);
        var final = await ReadPurchaseMatch(observer, h.Tenant);
        Assert.Equal(matched.GetProperty("id").GetGuid(), AssetId(Assert.Single(final.Rows("matches"))));
        CheckPurchasePostedAp(final, fixture, Assert.Single(final.Rows("matches")), PurchasePrice, 30.85m, 3.09m, 33.94m);
        Assert.Equal(1, AssetInt(Assert.Single(final.Rows("pub-numbers")), "CurrentValue"));
        output.WriteLine(JsonSerializer.Serialize(new { Before = PurchaseState(before), AfterRetry = PurchaseState(final) }));
    }

    [CurrentPermissionPostgresFact]
    public async Task W631_business_suspension_preserves_only_match_until_successful_release()
    {
        await using var h = await Seed();
        var fixture = await SeedPurchaseMatch(h);
        await GrantLegacyAuthorityAction(h, "pur-match", "add");
        await GrantLegacyAuthorityAction(h, "pur-match", "release");
        await h.Db.FiscalPeriods.Where(p => p.Id == fixture.Period)
            .ExecuteUpdateAsync(set => set.SetProperty(p => p.Status, PeriodStatus.Closed));
        await using var host = await StartPurchaseMatchApi(h);
        var actor = await Login(host, h);
        await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await observer.OpenAsync();
        var before = await ReadPurchaseMatch(observer, h.Tenant);
        var created = await SubmitPurchaseMatch(host, actor, fixture, PurchasePrice);
        var matchNo = PurchaseMatchNumber(created);
        var suspended = await ReadPurchaseMatch(observer, h.Tenant);
        CheckPurchaseMatch(suspended, fixture, created.GetProperty("id").GetGuid(), matchNo, MatchStatus.Suspended, PurchasePrice, true);
        Assert.Equal("AP build failed: E-FIN-112", created.GetProperty("note").GetString());
        Assert.Equal(JsonValueKind.Null, Assert.Single(suspended.Rows("matches")).GetProperty("ApInvoiceId").ValueKind);
        Assert.Equal(1, AssetInt(Assert.Single(suspended.Rows("pub-numbers")), "CurrentValue"));
        CheckPurchaseTables(before, suspended, "po", "po-lines", "receipts", "receipt-lines", "ap", "ap-lines",
            "journals", "journal-lines", "fin-numbers", "periods");
        using (var failed = await Release())
            await CheckPurchaseError(failed, HttpStatusCode.BadRequest, "E-FIN-112");
        Assert.Equal(suspended, await ReadPurchaseMatch(observer, h.Tenant));

        await h.Db.FiscalPeriods.Where(p => p.Id == fixture.Period)
            .ExecuteUpdateAsync(set => set.SetProperty(p => p.Status, PeriodStatus.Open));
        using var released = await Release();
        var data = await ReadApSuccessData(released);
        Assert.Equal(created.GetProperty("id").GetGuid(), data.GetProperty("match").GetProperty("id").GetGuid());
        var final = await ReadPurchaseMatch(observer, h.Tenant);
        Assert.Equal((int)MatchStatus.Released, AssetInt(Assert.Single(final.Rows("matches")), "Status"));
        CheckPurchaseTables(suspended, final, "match-lines", "pub-numbers");
        CheckPurchasePostedAp(final, fixture, Assert.Single(final.Rows("matches")), PurchasePrice, 30.85m, 3.09m, 33.94m);
        output.WriteLine(JsonSerializer.Serialize(new
        {
            Before = PurchaseState(before), Suspended = PurchaseState(suspended), Released = PurchaseState(final),
        }));

        Task<HttpResponseMessage> Release() => host.Send(OperationsRequest(actor, HttpMethod.Post,
            $"{PurchaseMatchPath}/{matchNo}/release", new { Note = "period reopened" }));
    }

    [TenantManagementPostgresTheory]
    [InlineData("posted")]
    [InlineData("draft")]
    [InlineData("draft-with-voucher")]
    [InlineData("unlinked-invoice")]
    [InlineData("ambiguous-line")]
    public async Task W631_historical_ap_reuses_only_exact_purchase_and_original_finance_keys(string scenario)
    {
        await using var h = await Seed();
        var fixture = await SeedPurchaseMatch(h);
        await GrantLegacyAuthorityAction(h, "pur-match", "add");
        var seedInvoice = new ApInvoice
        {
            SupplierId = fixture.Supplier, SupplierInvoiceNo = fixture.Invoice,
            PurchaseOrderId = scenario == "unlinked-invoice" ? null : fixture.Po, InvoiceDate = PurchaseInvoiceDate,
            DueDate = PurchaseInvoiceDate.AddDays(30),
            Lines = [new() { ItemId = PurchaseItem, Qty = PurchaseQty, UnitPrice = PurchasePrice, Amount = 30.85m,
                TaxCodeId = fixture.TaxCode, ExpenseAccountId = fixture.Inventory }],
        };
        var sequence = new FinSequenceService(h.Db);
        var voucher = new JournalEntryService(h.Db, new FiscalPeriodService(h.Db, 1), sequence);
        var ap = new ApInvoiceService(h.Db, new AutoVoucherEngine(h.Db, voucher), sequence);
        Assert.True((await ap.CreateAsync(seedInvoice, "historical-fixture")).Ok);
        if (scenario is "posted" or "draft-with-voucher")
            Assert.True((await ap.PostAsync(seedInvoice.Id, "historical-fixture")).Ok);
        if (scenario == "draft-with-voucher")
            await h.Db.ApInvoices.Where(row => row.Id == seedInvoice.Id).ExecuteUpdateAsync(set => set
                .SetProperty(row => row.Status, ApInvoiceStatus.Draft).SetProperty(row => row.JournalEntryId, (Guid?)null));
        if (scenario == "ambiguous-line")
        {
            h.Db.PurchaseOrderLines.Add(new PurchaseOrderLine
            {
                PoNo = fixture.PoNo, LineNo = 2, ItemId = PurchaseItem, Qty = 1m, UnitPrice = 2m,
            });
            await h.Db.SaveChangesAsync();
        }
        h.Db.ChangeTracker.Clear();

        await using var host = await StartPurchaseMatchApi(h);
        var actor = await Login(host, h);
        await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await observer.OpenAsync();
        var before = await ReadPurchaseMatch(observer, h.Tenant);
        var data = await SubmitPurchaseMatch(host, actor, fixture, PurchasePrice, includeDate: scenario != "posted");
        var final = await ReadPurchaseMatch(observer, h.Tenant);
        var match = Assert.Single(final.Rows("matches"));
        if (scenario is "unlinked-invoice" or "ambiguous-line")
        {
            Assert.Equal((int)MatchStatus.Suspended, AssetInt(match, "Status"));
            Assert.Equal("AP build failed: E-FIN-201", match.GetProperty("Note").GetString());
            Assert.Equal(JsonValueKind.Null, match.GetProperty("ApInvoiceId").ValueKind);
            CheckPurchaseTables(before, final, "po", "po-lines", "ap", "ap-lines", "journals", "journal-lines", "fin-numbers");
        }
        else
        {
            Assert.Equal((int)MatchStatus.Passed, AssetInt(match, "Status"));
            Assert.Equal(PurchaseInvoiceDate, match.GetProperty("MatchDate").GetDateTime());
            Assert.Equal(seedInvoice.Id, data.GetProperty("apInvoiceId").GetGuid());
            Assert.Equal(seedInvoice.No, data.GetProperty("apInvoiceNo").GetString());
            CheckPurchasePostedAp(final, fixture, match, PurchasePrice, 30.85m, 3.09m, 33.94m);
            CheckPurchaseTables(before, final, "ap-lines");
            if (scenario == "posted") CheckPurchaseTables(before, final, "ap");
            if (scenario is "posted" or "draft-with-voucher")
                CheckPurchaseTables(before, final, "journals", "journal-lines", "fin-numbers");
            using var duplicate = await host.Send(OperationsRequest(actor, HttpMethod.Post, PurchaseMatchPath,
                PurchaseMatchRequest(fixture, PurchasePrice)));
            await CheckPurchaseError(duplicate, HttpStatusCode.BadRequest, "E-PUR-043");
            Assert.Equal(final, await ReadPurchaseMatch(observer, h.Tenant));
        }
        output.WriteLine(JsonSerializer.Serialize(new { scenario, Before = PurchaseState(before), Final = PurchaseState(final) }));
    }

    [CurrentPermissionPostgresFact]
    public async Task W631_forward_match_migration_retains_existing_row_keys_and_validates_tenant_relationship()
    {
        await using var h = await Seed();
        var connection = new NpgsqlConnectionStringBuilder(h.Db.Database.GetConnectionString()) { Database = "w631_match_upgrade" };
        await using (var owner = new NpgsqlConnection(h.Db.Database.GetConnectionString()))
        {
            await owner.OpenAsync();
            await using var create = new NpgsqlCommand("CREATE DATABASE w631_match_upgrade", owner);
            await create.ExecuteNonQueryAsync();
        }
        await using var core = server.Context(new CP6.Core.Services.Common.TenantContext { CurrentTenantId = h.Tenant });
        core.Database.SetConnectionString(connection.ConnectionString);
        await core.GetService<IMigrator>().MigrateAsync("20261115140500_FinJournalMakerIdentityV1");
        core.Sys_Tenants.Add(new() { Id = h.Tenant, TenantCode = h.Tenant.ToString("N"), TenantName = "historical purchase" });
        var old = new ThreeWayMatch
        {
            TenantId = h.Tenant, MatchNo = "TWM-LEGACY", PoNo = "PO-LEGACY", SupplierInvoiceNo = "SI-LEGACY",
            MatchDate = PurchaseInvoiceDate, Status = MatchStatus.Suspended,
            Lines = [new() { TenantId = h.Tenant, MatchNo = "TWM-LEGACY", LineNo = 1, PoLineNo = 7, ItemId = PurchaseItem,
                Qty = 0.12345678m, UnitPrice = 23.4567m }],
        };
        core.ThreeWayMatches.Add(old);
        await core.SaveChangesAsync();
        await using var observer = new NpgsqlConnection(connection.ConnectionString);
        await observer.OpenAsync();
        var before = await ReadPurchaseMatch(observer, h.Tenant);
        await core.Database.MigrateAsync();
        Assert.Equal(before, await ReadPurchaseMatch(observer, h.Tenant));
        await using var check = new NpgsqlCommand("""
            SELECT array_agg(conname || ':' || convalidated::text ORDER BY conname)
            FROM pg_constraint WHERE conrelid IN ('public."Pur_ThreeWayMatch"'::regclass, 'public."Pur_ThreeWayMatchLine"'::regclass)
              AND (contype = 'u' OR confrelid = 'public."Pur_ThreeWayMatch"'::regclass)
            """, observer);
        var constraints = (string[])(await check.ExecuteScalarAsync())!;
        Assert.Equal(new[] { "AK_Pur_ThreeWayMatch_TenantId_MatchNo:true", "FK_Pur_ThreeWayMatchLine_TenantId_MatchNo:true" }, constraints);
    }

    private static async Task CheckPurchaseOrderResult(HttpResponseMessage response, string action, PurchaseFixture f,
        AssetRows before, AssetRows after)
    {
        var data = await ReadApSuccessData(response);
        var match = action == "reject" ? data : data.GetProperty("match");
        var status = action switch { "add" => MatchStatus.Passed, "release" => MatchStatus.Released, _ => MatchStatus.Rejected };
        var price = action == "add" ? PurchasePrice : PurchaseVariancePrice;
        CheckPurchaseMatch(after, f, match.GetProperty("id").GetGuid(), PurchaseMatchNumber(match), status, price, action == "add");
        CheckPurchaseTables(before, after, "receipts", "receipt-lines", "periods");
        Assert.Equal(1, AssetInt(Assert.Single(after.Rows("pub-numbers")), "CurrentValue"));
        if (action != "add")
        {
            var handled = Assert.Single(after.Rows("matches"));
            Assert.Equal(match.GetProperty("id").GetGuid(), AssetId(Assert.Single(before.Rows("matches"))));
            Assert.Equal("price-owner", handled.GetProperty("HandledBy").GetString());
            Assert.Equal($"reviewed {action}", handled.GetProperty("Note").GetString());
            CheckPurchaseTables(before, after, "match-lines", "pub-numbers");
        }
        if (action == "reject")
        {
            CheckPurchaseTables(before, after, "po", "po-lines", "ap", "ap-lines", "journals", "journal-lines", "fin-numbers");
            Assert.Equal(JsonValueKind.Null, match.GetProperty("apInvoiceId").ValueKind);
            return;
        }
        Assert.True(data.GetProperty("apCreated").GetBoolean());
        CheckPurchasePostedAp(after, f, Assert.Single(after.Rows("matches")), price,
            action == "add" ? 30.85m : 34.18m, action == "add" ? 3.09m : 3.42m, action == "add" ? 33.94m : 37.60m);
    }

    private static string PurchaseMatchNumber(JsonElement match) => match.GetProperty("matchNo").GetString()!;

    private static object PurchaseMatchRequest(PurchaseFixture fixture, decimal unitPrice, bool includeDate = true)
        => new
        {
            fixture.PoNo, SupplierInvoiceNo = fixture.Invoice, InvoiceDate = includeDate ? PurchaseInvoiceDate : (DateTime?)null,
            Lines = new[] { new { PoLineNo = 1, Qty = PurchaseQty, UnitPrice = unitPrice } },
        };

    private static async Task<JsonElement> SubmitPurchaseMatch(AuthenticationApiProcess host, Dictionary<string, string> actor,
        PurchaseFixture fixture, decimal unitPrice, bool includeDate = true)
    {
        using var response = await host.Send(OperationsRequest(actor, HttpMethod.Post, PurchaseMatchPath,
            PurchaseMatchRequest(fixture, unitPrice, includeDate)));
        return (await ReadApSuccessData(response)).GetProperty("match");
    }

    private static async Task CheckPurchaseError(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.True(response.StatusCode == status, await OperationsFailure(response));
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal((int)status, payload.RootElement.GetProperty("code").GetInt32());
        Assert.Equal(code, payload.RootElement.GetProperty("message").GetString());
    }

    private sealed record PurchaseFixture(Guid Tenant, Guid Po, string PoNo, string Supplier, string Receipt, string Invoice, Guid Period,
        Guid Inventory, Guid Ap, Guid InputTax, Guid TaxCode);

    private static async Task<PurchaseFixture> SeedPurchaseMatch(Harness h)
    {
        var db = h.Db;
        await new GlAccountService(db).ImportTemplateAsync(FinCoaTemplate.CnGaap, "fixture");
        await PostingRuleSeed.EnsureSeededAsync(db);
        var accounts = await db.GlAccounts.AsNoTracking().Where(row => row.Role == "INVENTORY" || row.Role == "AP_CONTROL"
            || row.Role == "TAX_INPUT").ToDictionaryAsync(row => row.Role!, row => row.Id);
        var invoiceDate = PurchaseInvoiceDate;
        var period = await new FiscalPeriodService(db, 1).EnsureOpenAsync(invoiceDate, "fixture");
        var key = h.Tenant.ToString("N")[..8];
        var supplier = $"W631S-{key}";
        var poNo = $"W631PO-{key}";
        var receivedNo = $"W631GR-{key}";
        var invoice = $"W631SI-{key}";
        db.BusinessPartners.Add(new BusinessPartner
        {
            BpCd = supplier, BpName = "W631 simulated paper supplier", SupplierFlg = true,
            BaseCd = "W631", PurchasePostingDiv = "1",
        });
        var tax = new TaxCode
        {
            Id = Guid.NewGuid(), Code = "W631T10", Name = "recoverable input tax", Rate = 0.10m,
            Direction = TaxDirection.Input, Recoverable = true,
        };
        db.TaxCodes.Add(tax);
        db.MatchTolerances.Add(new MatchTolerance { SupplierId = supplier, PriceTolPct = 0.03m, AmountTolAbs = 0.25m });
        db.Pub_DocSequences.Add(new Pub_DocSequence { BizKey = "TWM", Prefix = "TWM", DateFormat = "yyyyMMdd", SeqLength = 4 });
        const decimal taxAmount = 3.09m;
        var po = new PurchaseOrder
        {
            Id = Guid.NewGuid(), PoNo = poNo, SupplierId = supplier, SupplierName = "W631 simulated paper supplier",
            Type = 1, PostingBasis = "1", Status = PoStatus.Received, OrderDate = invoiceDate.AddDays(-3),
            NetAmount = 30.85m, TaxAmount = taxAmount, GrossAmount = 30.85m + taxAmount,
        };
        db.PurchaseOrders.Add(po);
        db.PurchaseOrderLines.Add(new PurchaseOrderLine
        {
            PoNo = poNo, LineNo = 1, ItemId = PurchaseItem, Qty = PurchaseQty, UnitPrice = PurchasePrice, NetAmount = 30.85m,
            TaxAmount = taxAmount, TaxCodeId = tax.Id, TaxRate = tax.Rate, ReceivedQty = PurchaseQty, AcceptedQty = PurchaseQty,
        });
        db.GoodsReceipts.Add(new GoodsReceipt
        {
            GrNo = receivedNo, PoNo = poNo, SupplierId = supplier, ReceiptDate = invoiceDate.AddDays(-1),
            Status = GrStatus.Received, PostingBasis = "1",
        });
        db.GoodsReceiptLines.Add(new GoodsReceiptLine
        {
            GrNo = receivedNo, LineNo = 1, PoLineNo = 1, ItemId = PurchaseItem,
            ReceivedQty = PurchaseQty, AcceptedQty = PurchaseQty, QcStatus = "NONE",
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return new(h.Tenant, po.Id, poNo, supplier, receivedNo, invoice, period.Id,
            accounts["INVENTORY"], accounts["AP_CONTROL"], accounts["TAX_INPUT"], tax.Id);
    }

    private static Task<AuthenticationApiProcess> StartPurchaseMatchApi(Harness h)
    {
        var environment = IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(h.Tenant);
        var port = Environment.GetEnvironmentVariable("CP6_PURCHASE_MATCH_HTTP_PORT");
        if (port != null) environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
        return AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, environment);
    }

    private static async Task<AssetRows> ReadPurchaseMatch(NpgsqlConnection observer, Guid tenant)
    {
        await using var command = new NpgsqlCommand("""
            SELECT jsonb_build_object(
              'po', ARRAY(SELECT to_jsonb(r) FROM public."Pur_PurchaseOrder" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'po-lines', ARRAY(SELECT to_jsonb(r) FROM public."Pur_PurchaseOrderLine" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'receipts', ARRAY(SELECT to_jsonb(r) FROM public."Pur_GoodsReceipt" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'receipt-lines', ARRAY(SELECT to_jsonb(r) FROM public."Pur_GoodsReceiptLine" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'matches', ARRAY(SELECT to_jsonb(r) FROM public."Pur_ThreeWayMatch" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'match-lines', ARRAY(SELECT to_jsonb(r) FROM public."Pur_ThreeWayMatchLine" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'ap', ARRAY(SELECT to_jsonb(r) FROM public."Fin_ApInvoice" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'ap-lines', ARRAY(SELECT to_jsonb(r) FROM public."Fin_ApInvoiceLine" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'journals', ARRAY(SELECT to_jsonb(r) FROM public."Fin_JournalEntry" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'journal-lines', ARRAY(SELECT to_jsonb(r) FROM public."Fin_JournalLine" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'fin-numbers', ARRAY(SELECT to_jsonb(r) FROM public."Fin_Sequence" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'periods', ARRAY(SELECT to_jsonb(r) FROM public."Fin_FiscalPeriod" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'pub-numbers', ARRAY(SELECT to_jsonb(r) FROM public."Pub_DocSequence" r WHERE r."TenantId"=@tenant ORDER BY r."Id")
            )::text
            """, observer);
        command.Parameters.AddWithValue("tenant", tenant);
        return new((string)(await command.ExecuteScalarAsync())!);
    }

    private static JsonElement PurchaseState(AssetRows rows) => JsonSerializer.Deserialize<JsonElement>(rows.Data);

    private static void CheckPurchaseTables(AssetRows before, AssetRows after, params string[] tables)
    {
        foreach (var table in tables)
            Assert.Equal(before.Rows(table).Select(row => row.GetRawText()), after.Rows(table).Select(row => row.GetRawText()));
    }

    private static void CheckPurchaseMatch(AssetRows rows, PurchaseFixture f, Guid id, string matchNo, MatchStatus status,
        decimal unitPrice, bool within)
    {
        var match = Assert.Single(rows.Rows("matches"));
        Assert.Equal(id, AssetId(match));
        Assert.Equal(f.Tenant, AssetId(match, "TenantId"));
        Assert.Equal(f.PoNo, match.GetProperty("PoNo").GetString());
        Assert.Equal(f.Invoice, match.GetProperty("SupplierInvoiceNo").GetString());
        Assert.Equal(matchNo, match.GetProperty("MatchNo").GetString());
        Assert.Equal((int)status, AssetInt(match, "Status"));
        var line = Assert.Single(rows.Rows("match-lines"));
        Assert.Equal(matchNo, line.GetProperty("MatchNo").GetString());
        Assert.Equal(f.Tenant, AssetId(line, "TenantId"));
        Assert.Equal(1, AssetInt(line, "PoLineNo"));
        Assert.Equal(PurchaseQty, AssetDecimal(line, "Qty"));
        Assert.Equal(PurchaseQty, AssetDecimal(line, "RemainAccepted"));
        Assert.Equal(unitPrice, AssetDecimal(line, "UnitPrice"));
        Assert.Equal(within, line.GetProperty("WithinTolerance").GetBoolean());
    }

    private static void CheckPurchasePostedAp(AssetRows rows, PurchaseFixture f, JsonElement match, decimal unitPrice, decimal net,
        decimal tax, decimal gross)
    {
        Assert.Equal((int)PoStatus.Closed, AssetInt(Assert.Single(rows.Rows("po")), "Status"));
        var po = Assert.Single(rows.Rows("po-lines"));
        Assert.Equal(PurchaseQty, AssetDecimal(po, "ReceivedQty"));
        Assert.Equal(PurchaseQty, AssetDecimal(po, "AcceptedQty"));
        Assert.Equal(PurchaseQty, AssetDecimal(po, "InvoicedQty"));
        Assert.Equal(2, AssetInt(po, "MatchStatus"));
        var ap = Assert.Single(rows.Rows("ap"));
        Assert.Equal(AssetId(match, "ApInvoiceId"), AssetId(ap));
        Assert.Equal(match.GetProperty("ApInvoiceNo").GetString(), ap.GetProperty("No").GetString());
        Assert.Equal(f.Tenant, AssetId(ap, "TenantId"));
        Assert.Equal(f.Po, AssetId(ap, "PurchaseOrderId"));
        Assert.Equal(f.Supplier, ap.GetProperty("SupplierId").GetString());
        Assert.Equal(f.Invoice, ap.GetProperty("SupplierInvoiceNo").GetString());
        Assert.Equal((int)ApInvoiceStatus.Posted, AssetInt(ap, "Status"));
        Assert.Equal(PurchaseInvoiceDate.AddDays(30), ap.GetProperty("DueDate").GetDateTime());
        Assert.Equal((net, tax, gross), (AssetDecimal(ap, "NetAmount"), AssetDecimal(ap, "TaxAmount"), AssetDecimal(ap, "GrossAmount")));
        var line = Assert.Single(rows.Rows("ap-lines"));
        Assert.Equal(AssetId(ap), AssetId(line, "InvoiceId"));
        Assert.Equal(f.Tenant, AssetId(line, "TenantId"));
        Assert.Equal(f.Inventory, AssetId(line, "ExpenseAccountId"));
        Assert.Equal(f.TaxCode, AssetId(line, "TaxCodeId"));
        Assert.Equal((PurchaseQty, unitPrice, net, tax),
            (AssetDecimal(line, "Qty"), AssetDecimal(line, "UnitPrice"), AssetDecimal(line, "Amount"), AssetDecimal(line, "TaxAmount")));
        var entry = Assert.Single(rows.Rows("journals"));
        Assert.Equal(AssetId(ap, "JournalEntryId"), AssetId(entry));
        Assert.Equal(ap.GetProperty("No").GetString(), entry.GetProperty("SourceDocNo").GetString());
        CheckPurchaseVoucher(rows, f, entry, VoucherSource.AP, gross, 3);
        CheckPurchaseLeg(rows, f.Inventory, net, 0m);
        CheckPurchaseLeg(rows, f.InputTax, tax, 0m);
        Assert.Equal(f.Supplier, CheckPurchaseLeg(rows, f.Ap, 0m, gross).GetProperty("PartnerId").GetString());
        var sequence = rows.Rows("fin-numbers").ToDictionary(row => row.GetProperty("SeqKey").GetString()!, row => AssetInt(row, "CurrentValue"));
        Assert.Equal(2, sequence.Count);
        Assert.Equal(1, sequence["AP"]);
        Assert.Equal(1, sequence["GL"]);
    }

    private static void CheckPurchaseVoucher(AssetRows rows, PurchaseFixture f, JsonElement entry, VoucherSource source, decimal total, int count)
    {
        Assert.Equal(f.Tenant, AssetId(entry, "TenantId"));
        Assert.Equal(f.Period, AssetId(entry, "PeriodId"));
        Assert.Equal((int)source, AssetInt(entry, "Source"));
        Assert.Equal((int)JournalStatus.Posted, AssetInt(entry, "Status"));
        Assert.True(entry.GetProperty("AutoPosted").GetBoolean());
        var lines = rows.Rows("journal-lines");
        Assert.Equal(count, lines.Length);
        Assert.All(lines, row =>
        {
            Assert.Equal(AssetId(entry), AssetId(row, "EntryId"));
            Assert.Equal(f.Tenant, AssetId(row, "TenantId"));
        });
        Assert.Equal(total, lines.Sum(row => AssetDecimal(row, "Debit")));
        Assert.Equal(total, lines.Sum(row => AssetDecimal(row, "Credit")));
    }

    private static JsonElement CheckPurchaseLeg(AssetRows rows, Guid accountId, decimal debit, decimal credit)
    {
        var line = Assert.Single(rows.Rows("journal-lines"), row => AssetId(row, "AccountId") == accountId);
        Assert.Equal((debit, credit), (AssetDecimal(line, "Debit"), AssetDecimal(line, "Credit")));
        return line;
    }
}
