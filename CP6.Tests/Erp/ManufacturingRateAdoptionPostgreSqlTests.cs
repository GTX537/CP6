using System.Net;
using System.Text.Json;
using CP6.Core.Services.Fin;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Fin;
using CP6.Entity.DomainModels.Integration;
using CP6.Entity.DomainModels.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task W690_manufacturing_rate_adoption_uses_scoped_center_and_approved_released_versions()
    {
        await using var h = await Seed();
        var number = await SeedCompletedFinanceFlow(h);
        await GrantNativeCostCollect(h);
        foreach (var action in new[] { "view", "settle", "rate-draft", "rate-review", "rate-release" })
            await GrantLegacyAuthorityAction(h, "fin-cost", action);
        var reviewer = NewOperator("rate-reviewer");
        var publisher = NewOperator("rate-publisher");
        h.Db.Sys_Users.AddRange(reviewer, publisher);
        var center = new CostCenter { Code = "CC-A", Name = "Current preset", Type = CostCenterType.Process };
        h.Db.CostCenters.Add(center);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        var order = await h.Db.WorkOrders.AsNoTracking().SingleAsync(w => w.WorkOrderNo == number);
        var print = await h.Db.ProductProcesses.AsNoTracking().SingleAsync(x => x.ProductCd == order.ProductCd && x.ProcessCd == "PRINT");
        var pack = await h.Db.ProductProcesses.AsNoTracking().SingleAsync(x => x.ProductCd == order.ProductCd && x.ProcessCd == "PACK");
        var press = await h.Db.WorkCenters.AsNoTracking().SingleAsync(x => x.WgCd == "W686-A");
        var packing = await h.Db.WorkCenters.AsNoTracking().SingleAsync(x => x.WgCd == "W686-B");

        var ledger = Guid.NewGuid();
        var other = Guid.NewGuid();
        var demo = Guid.NewGuid();
        var centerVersion = Guid.NewGuid();
        var futureVersion = Guid.NewGuid();
        var policy = Guid.NewGuid();
        var printBasis = Guid.NewGuid();
        var packBasis = Guid.NewGuid();
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_fin_mfg_v1.ledgers
                (tenant_id,id,environment_id,mode,legal_entity_key,ledger_key,currency,business_date)
            VALUES ({h.Tenant},{ledger},'w690-installed','REAL','LE1','COST-BOOK','CNY','2026-06-30'),
                   ({h.Tenant},{other},'w690-installed','REAL','LE2','OTHER-BOOK','CNY','2026-06-30'),
                   ({h.Tenant},{demo},'w690-installed','DEMO','LE3','SIM-BOOK','CNY','2026-06-30');
            INSERT INTO cp6_fin_mfg_v1.members (tenant_id,ledger_id,subject_id) VALUES
                ({h.Tenant},{ledger},{h.User}),({h.Tenant},{ledger},{reviewer.Id}),({h.Tenant},{ledger},{publisher.Id}),
                ({h.Tenant},{other},{h.User}),({h.Tenant},{demo},{h.User});
            INSERT INTO cp6_fin_mfg_v1.center_versions
                (tenant_id,ledger_id,root_id,version_id,version,code,name,valid_from,to_exclusive,approval_id)
            VALUES ({h.Tenant},{ledger},{center.Id},{centerVersion},1,'CC-A','Approved center','2026-01-01','2026-07-01',{Guid.NewGuid()}),
                   ({h.Tenant},{ledger},{center.Id},{futureVersion},2,'CC-A','Future center','2026-07-01',NULL,{Guid.NewGuid()});
            INSERT INTO cp6_fin_mfg_v1.policies
                (tenant_id,ledger_id,version_id,approval_id,currency,currency_scale,rounding,rate_date_basis,valuation,variance,valid_from)
            VALUES ({h.Tenant},{ledger},{policy},{Guid.NewGuid()},'CNY',2,'HALF_UP','APPROVED_ACCOUNTING_DATE',
                'STANDARD_OUTPUT_WITH_VARIANCE','VAR_TO_EXPENSE','2026-01-01');
            INSERT INTO cp6_fin_mfg_v1.rate_bases
                (tenant_id,ledger_id,id,policy_version_id,approval_id,work_center_id,operation_id,valid_from)
            VALUES ({h.Tenant},{ledger},{printBasis},{policy},{Guid.NewGuid()},{press.Id},{print.Id},'2026-01-01'),
                   ({h.Tenant},{ledger},{packBasis},{policy},{Guid.NewGuid()},{packing.Id},{pack.Id},'2026-01-01');
            INSERT INTO cp6_fin_mfg_v1.qualifications (tenant_id,ledger_id,subject_id,action,qualification_id,expires_at)
            VALUES ({h.Tenant},{ledger},{h.User},'rate-review',{Guid.NewGuid()},clock_timestamp()+interval '1 day'),
                   ({h.Tenant},{ledger},{publisher.Id},'rate-release',{Guid.NewGuid()},clock_timestamp()+interval '1 day');
            INSERT INTO cp6_fin_mfg_v1.work_orders (tenant_id,work_order_id,ledger_id) VALUES ({h.Tenant},{order.Id},{ledger});
            """);

        var environment = new Dictionary<string, string>(IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(h.Tenant))
        {
            ["FinanceCosting__EnvironmentId"] = "w690-installed",
            ["StockFinBridge__Enabled"] = "false",
        };
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, environment);
        var makerCookies = await Login(host, h);
        var reviewCookies = await Login(host, reviewer.UserName, "Price1!Password", h.Tenant.ToString("N"));
        var publishCookies = await Login(host, publisher.UserName, "Price1!Password", h.Tenant.ToString("N"));
        const string options = "/api/fin/cost/v1/options";
        const string selections = "/api/fin/cost/v1/selections?kind=COST_CENTER";
        const string rates = "/api/fin/cost/owner-v1/rates";
        using (var unselected = await Call(makerCookies, HttpMethod.Get, options, HttpStatusCode.OK))
        {
            Assert.Equal(JsonValueKind.Null, unselected.RootElement.GetProperty("contextToken").ValueKind);
            Assert.Equal(2, unselected.RootElement.GetProperty("contexts").GetArrayLength());
        }

        var makerContext = await Context(makerCookies);
        var reviewerContext = await Context(reviewCookies);
        var publisherContext = await Context(publishCookies);
        using (await Call(reviewCookies, HttpMethod.Get, selections, HttpStatusCode.NotFound, makerContext)) { }
        using (await Call(reviewCookies, HttpMethod.Get, options + "?legalEntityKey=LE2&ledgerKey=OTHER-BOOK", HttpStatusCode.NotFound)) { }
        using (await Call(makerCookies, HttpMethod.Get, options + "?legalEntityKey=LE3&ledgerKey=SIM-BOOK", HttpStatusCode.NotFound)) { }

        CostCenterVersionLocatorV1 centerLocator;
        using (var selected = await Call(makerCookies, HttpMethod.Get, selections, HttpStatusCode.OK, makerContext))
        {
            var item = Assert.Single(selected.RootElement.GetProperty("masters").EnumerateArray());
            Assert.Equal("Approved center", item.GetProperty("name").GetString());
            centerLocator = ReadLocator(item);
            Assert.Equal((center.Id, centerVersion), (centerLocator.RootId, centerLocator.VersionId));
        }
        using (var selected = await Call(makerCookies, HttpMethod.Get, selections + "&businessDate=2026-07-01", HttpStatusCode.OK, makerContext))
            Assert.Equal(futureVersion, ReadLocator(Assert.Single(selected.RootElement.GetProperty("masters").EnumerateArray())).VersionId);

        var sample = new ManufacturingRateDraftV1(press.Id, print.Id, centerLocator, policy, printBasis,
            "100.00000000", "45.12345678", new(2026, 6, 1), new(2026, 7, 1));
        using (var alternate = await Call(makerCookies, HttpMethod.Get, options + "?legalEntityKey=LE2&ledgerKey=OTHER-BOOK", HttpStatusCode.OK))
            using (await Call(makerCookies, HttpMethod.Post, rates, HttpStatusCode.NotFound,
                alternate.RootElement.GetProperty("contextToken").GetGuid(), sample)) { }
        using (await Call(reviewCookies, HttpMethod.Post, rates, HttpStatusCode.NotFound, reviewerContext, sample)) { }

        var cost = $"/api/fin/cost/{number}";
        using (var missing = await Call(makerCookies, HttpMethod.Post, cost + "/collect", HttpStatusCode.Conflict, body: new { }))
            Assert.Contains("PUBLISHED_RATE_UNAVAILABLE", missing.RootElement.ToString());
        Assert.False(await h.Db.CostSheets.AnyAsync());
        var printRate = await Draft(sample);
        await Transition(makerCookies, makerContext, printRate, "submit", 1);
        using (var self = await Call(makerCookies, HttpMethod.Post, rates + $"/{printRate}/review", HttpStatusCode.Forbidden,
            makerContext, new ManufacturingRateTransitionV1(2)))
            Assert.Contains("RATE_REVIEW_INDEPENDENCE_REQUIRED", self.RootElement.ToString());
        using (var unqualified = await Call(reviewCookies, HttpMethod.Post, rates + $"/{printRate}/review", HttpStatusCode.Forbidden,
            reviewerContext, new ManufacturingRateTransitionV1(2)))
            Assert.Contains("FINANCE_QUALIFICATION_REQUIRED", unqualified.RootElement.ToString());
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_fin_mfg_v1.qualifications (tenant_id,ledger_id,subject_id,action,qualification_id,expires_at)
            VALUES ({h.Tenant},{ledger},{reviewer.Id},'rate-review',{Guid.NewGuid()},clock_timestamp()+interval '1 day')
            """);
        await Transition(reviewCookies, reviewerContext, printRate, "review", 2);

        var preview = await Preview(printRate);
        using (await Call(makerCookies, HttpMethod.Post, rates + $"/{printRate}/publish", HttpStatusCode.NotFound,
            makerContext, new ManufacturingRatePublishV1(preview))) { }
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_fin_mfg_v1.qualifications SET enabled=false
            WHERE tenant_id={h.Tenant} AND ledger_id={ledger} AND subject_id={reviewer.Id} AND action='rate-review'
            """);
        using (var denied = await Call(publishCookies, HttpMethod.Post, rates + $"/{printRate}/publish", HttpStatusCode.Forbidden,
            publisherContext, new ManufacturingRatePublishV1(preview)))
            Assert.Contains("FINANCE_QUALIFICATION_REQUIRED", denied.RootElement.ToString());
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_fin_mfg_v1.qualifications SET enabled=true
            WHERE tenant_id={h.Tenant} AND ledger_id={ledger} AND subject_id={reviewer.Id} AND action='rate-review'
            """);
        await Task.WhenAll(Publish(printRate, preview), Publish(printRate, preview));

        var packRate = await Approve(sample with { WorkCenterId = packing.Id, OperationProfileId = pack.Id, BasisId = packBasis,
            LaborRate = "10.00000001", OverheadRate = "3.125" });
        await Publish(packRate, await Preview(packRate));
        using (var completed = await CompleteForFinance(host, makerCookies, number))
            Assert.True(completed.StatusCode == HttpStatusCode.OK, await completed.Content.ReadAsStringAsync());
        var first = await h.Db.CostSheets.AsNoTracking().SingleAsync();
        Assert.Equal((213.35m, center.Id), (first.LaborActual, first.CostCenterId));
        var eventRow = await h.Db.IntegrationEvents.AsNoTracking().SingleAsync(x => x.TargetModule == "FIN" && x.SourceNo == number);
        Assert.Equal((IntegrationEventStatus.Skipped, "COST_LEDGER_SETTLEMENT_ADOPTION_REQUIRED"), (eventRow.Status, eventRow.LastError));
        Assert.False(await h.Db.JournalEntries.AnyAsync());
        using (var basis = JsonDocument.Parse(await ReadW689Basis(h, first.Id, 1)))
        {
            Assert.Equal(ledger, basis.RootElement.GetProperty("LedgerId").GetGuid());
            var used = basis.RootElement.GetProperty("Rates").EnumerateArray()
                .Single(row => row.GetProperty("RateId").GetGuid() == printRate);
            Assert.Equal(centerVersion, used.GetProperty("CostCenterVersionId").GetGuid());
            Assert.Equal(policy, used.GetProperty("PolicyVersionId").GetGuid());
        }

        var replace = await Approve(sample with { From = new(2026, 6, 23), LaborRate = "80" });
        var competing = await Approve(sample with { From = new(2026, 6, 25), LaborRate = "50" });
        var stale = await Preview(competing);
        await Publish(replace, await Preview(replace));
        using (var staleReply = await Call(publishCookies, HttpMethod.Post, rates + $"/{competing}/publish",
            HttpStatusCode.PreconditionFailed, publisherContext, new ManufacturingRatePublishV1(stale)))
            Assert.Contains("RATE_RELEASE_PREVIEW_STALE", staleReply.RootElement.ToString());
        Assert.Equal(3, await h.Db.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM cp6_fin_mfg_v1.releases WHERE tenant_id={h.Tenant} AND ledger_id={ledger}
            """).SingleAsync());
        Assert.True(await h.Db.Database.SqlQuery<bool>($"""
            SELECT EXISTS(SELECT 1 FROM cp6_fin_mfg_v1.timeline WHERE tenant_id={h.Tenant} AND ledger_id={ledger}
              AND rate_id={printRate} AND valid_from='2026-06-01' AND to_exclusive='2026-06-23') AS "Value"
            """).SingleAsync());

        using (await Call(makerCookies, HttpMethod.Post, cost + "/collect", HttpStatusCode.OK, body: new { })) { }
        Assert.Equal(170.88m, await h.Db.CostSheets.Where(x => x.Id == first.Id).Select(x => x.LaborActual).SingleAsync());
        using (var basis = JsonDocument.Parse(await ReadW689Basis(h, first.Id, 2)))
            Assert.Contains(basis.RootElement.GetProperty("Rates").EnumerateArray(),
                item => item.GetProperty("RateId").GetGuid() == replace);
        using (var settle = await Call(makerCookies, HttpMethod.Post, cost + "/settle", HttpStatusCode.BadRequest))
            Assert.Contains("COST_LEDGER_SETTLEMENT_ADOPTION_REQUIRED", settle.RootElement.ToString());
        Assert.Equal(CostSheetStatus.Collected, await h.Db.CostSheets.Where(x => x.Id == first.Id).Select(x => x.Status).SingleAsync());

        var sequence = new FinSequenceService(h.Db);
        var journal = new JournalEntryService(h.Db, new FiscalPeriodService(h.Db, 1), sequence);
        var invoices = new ArInvoiceService(h.Db, new AutoVoucherEngine(h.Db, journal), journal, sequence);
        var shipment = await invoices.CreateFromShipmentAsync(new()
        {
            ShipmentId = "W690-shipment", WorkOrderNo = number, CustomerId = "W690-customer", EstimatedCost = 1m,
            Lines = [new() { ItemId = "W690-item", Qty = 1m, UnitPrice = 10m }],
        }, "w690-shipper");
        Assert.Equal("COST_LEDGER_SHIPMENT_ADOPTION_REQUIRED", shipment.Result.Code);
        Assert.False(await h.Db.ArInvoices.AnyAsync());

        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_fin_mfg_v1.members SET enabled=false WHERE tenant_id={h.Tenant} AND ledger_id={ledger} AND subject_id={h.User}
            """);
        using (await Call(makerCookies, HttpMethod.Get, selections, HttpStatusCode.NotFound, makerContext)) { }
        using (await Call(makerCookies, HttpMethod.Post, cost + "/collect", HttpStatusCode.NotFound, body: new { })) { }
        using (await Call(makerCookies, HttpMethod.Get, cost, HttpStatusCode.BadRequest)) { }
        using (var settle = await Call(makerCookies, HttpMethod.Post, cost + "/settle", HttpStatusCode.BadRequest))
            Assert.Contains("FINANCE_CONTEXT_NOT_FOUND", settle.RootElement.ToString());
        Assert.False(await h.Db.JournalEntries.AnyAsync());

        const string legacyOrder = "W690-legacy";
        h.Db.CostSheets.Add(new()
        {
            No = "CS-W690-legacy", WorkOrderNo = legacyOrder, CompletedQty = 4m,
            MaterialStandard = 10m, MaterialActual = 11m, Status = CostSheetStatus.Settled,
        });
        await h.Db.SaveChangesAsync();
        await PostingRuleSeed.EnsureSeededAsync(h.Db);
        var legacyShipment = await invoices.CreateFromShipmentAsync(new()
        {
            ShipmentId = "W690-legacy-ship", WorkOrderNo = legacyOrder, CustomerId = "W690-customer",
            InvoiceDate = DateTime.Today, DueDate = DateTime.Today,
            Lines = [new() { ItemId = "W690-item", Qty = 2m, UnitPrice = 10m }],
        }, "w690-shipper");
        Assert.True(legacyShipment.Result.Ok, legacyShipment.Result.Code);
        var legacyInvoice = await h.Db.ArInvoices.AsNoTracking().SingleAsync();
        Assert.Equal(5m, legacyInvoice.CostAmount);
        Assert.NotNull(legacyInvoice.CostJournalEntryId);

        async Task<Guid> Context(Dictionary<string, string> cookies)
        {
            using var response = await Call(cookies, HttpMethod.Get, options + "?legalEntityKey=LE1&ledgerKey=COST-BOOK", HttpStatusCode.OK);
            Assert.Equal("w690-installed", response.RootElement.GetProperty("scope").GetProperty("environmentId").GetString());
            return response.RootElement.GetProperty("contextToken").GetGuid();
        }
        async Task<Guid> Draft(ManufacturingRateDraftV1 body)
        {
            using var reply = await Call(makerCookies, HttpMethod.Post, rates, HttpStatusCode.Created, makerContext, body);
            return reply.RootElement.GetProperty("rateId").GetGuid();
        }
        async Task<Guid> Approve(ManufacturingRateDraftV1 body)
        {
            var id = await Draft(body);
            await Transition(makerCookies, makerContext, id, "submit", 1);
            await Transition(reviewCookies, reviewerContext, id, "review", 2);
            return id;
        }
        async Task Transition(Dictionary<string, string> cookies, Guid context, Guid id, string action, long revision)
        {
            using var reply = await Call(cookies, HttpMethod.Post, rates + $"/{id}/{action}", HttpStatusCode.OK,
                context, new ManufacturingRateTransitionV1(revision));
            Assert.Equal(revision + 1, reply.RootElement.GetProperty("revision").GetInt64());
        }
        async Task<Guid> Preview(Guid id)
        {
            using var reply = await Call(publishCookies, HttpMethod.Post, rates + $"/{id}/release-preview", HttpStatusCode.OK,
                publisherContext, new ManufacturingRateTransitionV1(3));
            return reply.RootElement.GetProperty("token").GetGuid();
        }
        async Task Publish(Guid id, Guid key)
        {
            using var reply = await Call(publishCookies, HttpMethod.Post, rates + $"/{id}/publish", HttpStatusCode.OK,
                publisherContext, new ManufacturingRatePublishV1(key));
            Assert.Equal("RELEASED", reply.RootElement.GetProperty("state").GetString());
        }
        async Task<JsonDocument> Call(Dictionary<string, string> cookies, HttpMethod method, string path,
            HttpStatusCode expected, Guid? context = null, object? body = null)
        {
            using var request = OperationsRequest(cookies, method, path, body);
            if (context != null)
                request.Headers.Add("X-Finance-Context", context.Value.ToString());
            using var response = await host.Send(request);
            var content = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == expected, $"{path}: {response.StatusCode} {content}");
            return JsonDocument.Parse(content);
        }
    }

    private static CostCenterVersionLocatorV1 ReadLocator(JsonElement item)
    {
        var locator = item.GetProperty("locator");
        return new(locator.GetProperty("rootId").GetGuid(), locator.GetProperty("versionId").GetGuid(),
            locator.GetProperty("opaqueToken").GetGuid());
    }

    private static Sys_User NewOperator(string name) => new()
    {
        UserName = name, RoleId = 72, Password = new BCryptPasswordHasher(4).Hash("Price1!Password"),
    };
}
