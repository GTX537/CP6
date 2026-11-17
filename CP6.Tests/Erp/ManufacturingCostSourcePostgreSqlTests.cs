using System.Net;
using System.Text.Json;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task W697_professionally_published_cost_policy_and_rate_basis_drive_actual_collection_by_date()
    {
        await using var h = await Seed();
        await using var platform = await TenantManagementSeed();
        var number = await SeedCompletedFinanceFlow(h);
        await GrantNativeCostCollect(h);
        foreach (var action in new[] { "view", "settle", "rate-draft", "rate-review", "rate-release", "qualification-admin",
            "center-submit", "center-review", "center-release", "cost-master-view",
            "policy-submit", "policy-review", "policy-release", "rate-basis-submit", "rate-basis-review", "rate-basis-release" })
            await GrantLegacyAuthorityAction(h, "fin-cost", action);
        var reviewer = NewOperator("center-reviewer");
        var publisher = NewOperator("center-publisher");
        var issuer = NewOperator("qualification-issuer");
        var printCenter = new CostCenter { Code = "W691-PRINT", Name = "印刷中心", Type = CostCenterType.Process };
        var packCenter = new CostCenter { Code = "W691-PACK", Name = "包装中心", Type = CostCenterType.Process };
        h.Db.AddRange(reviewer, publisher, issuer, printCenter, packCenter);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        var order = await h.Db.WorkOrders.AsNoTracking().SingleAsync(x => x.WorkOrderNo == number);
        var print = await h.Db.ProductProcesses.AsNoTracking().SingleAsync(x => x.ProductCd == order.ProductCd && x.ProcessCd == "PRINT");
        var pack = await h.Db.ProductProcesses.AsNoTracking().SingleAsync(x => x.ProductCd == order.ProductCd && x.ProcessCd == "PACK");
        var press = await h.Db.WorkCenters.AsNoTracking().SingleAsync(x => x.WgCd == "W686-A");
        var packing = await h.Db.WorkCenters.AsNoTracking().SingleAsync(x => x.WgCd == "W686-B");
        var ledger = Guid.NewGuid();
        var foreignLedger = Guid.NewGuid();
        var day = new DateOnly(2026, 6, 30);
        var nextDay = day.AddDays(1);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_fin_mfg_v1.ledgers
                (tenant_id,id,environment_id,mode,legal_entity_key,ledger_key,currency,business_date)
            VALUES ({h.Tenant},{ledger},'w691-installed','REAL','LE1','COST-BOOK','CNY',{day}),
                   ({h.Tenant},{foreignLedger},'w691-installed','REAL','LE1','OTHER-BOOK','CNY',{day});
            INSERT INTO cp6_fin_mfg_v1.members (tenant_id,ledger_id,subject_id) VALUES
                ({h.Tenant},{ledger},{h.User}),({h.Tenant},{ledger},{reviewer.Id}),({h.Tenant},{ledger},{publisher.Id}),
                ({h.Tenant},{ledger},{issuer.Id}),({h.Tenant},{foreignLedger},{h.User});
            INSERT INTO cp6_fin_mfg_v1.work_orders (tenant_id,work_order_id,ledger_id) VALUES ({h.Tenant},{order.Id},{ledger});
            """);

        var environment = new Dictionary<string, string>(IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(h.Tenant))
        {
            ["FinanceCosting__EnvironmentId"] = "w691-installed",
            ["StockFinBridge__Enabled"] = "false",
        };
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, environment);
        var maker = await Login(host, h);
        var checker = await Login(host, reviewer.UserName, "Price1!Password", h.Tenant.ToString("N"));
        var releaser = await Login(host, publisher.UserName, "Price1!Password", h.Tenant.ToString("N"));
        var registrar = await Login(host, issuer.UserName, "Price1!Password", h.Tenant.ToString("N"));
        var makerContext = await Context(maker);
        var reviewerContext = await Context(checker);
        var publisherContext = await Context(releaser);
        var issuerContext = await Context(registrar);
        var foreignContext = await Context(maker, "OTHER-BOOK");
        const string qualifications = "/api/fin/cost/owner-v1/qualifications";
        const string centers = "/api/fin/cost/owner-v1/center-versions";
        const string selections = "/api/fin/cost/v1/selections?kind=COST_CENTER";
        const string rates = "/api/fin/cost/owner-v1/rates";
        const string masters = "/api/fin/cost/owner-v1/cost-master-versions";
        const string c28 = "/api/fin/cost/v1/commands";

        var issueReview = Grant(reviewer.Id, "center-review");
        using (await Call(registrar, issuerContext, qualifications, issueReview with { SubjectId = issuer.Id },
            HttpStatusCode.Forbidden, "FINANCE_QUALIFICATION_INDEPENDENCE_REQUIRED")) { }
        var sourceDraft = new ManufacturingCenterSubmissionV1(Guid.NewGuid(), printCenter.Id, print.Id, day, null, "工序中心专业登记");
        var original = await Submit(sourceDraft);
        Assert.Equal(original, await Submit(sourceDraft));
        var approval = new ManufacturingCenterReviewV1(Guid.NewGuid(), 1, "APPROVE", "核准精确印刷工序");
        using (await Call(maker, makerContext, $"{centers}/{original}/review", approval,
            HttpStatusCode.Forbidden, "FINANCE_REVIEW_INDEPENDENCE_REQUIRED")) { }
        using (await Call(checker, reviewerContext, $"{centers}/{original}/review", approval,
            HttpStatusCode.Forbidden, "FINANCE_QUALIFICATION_REQUIRED")) { }

        var proof = await Decide(issueReview);
        Assert.Equal(proof, await Decide(issueReview));
        await Decide(Grant(publisher.Id, "center-release"));
        await Decide(Grant(reviewer.Id, "rate-review"));
        await Decide(Grant(publisher.Id, "rate-release"));
        using (await Call(checker, reviewerContext, $"{centers}/{original}/review", approval)) { }
        using (var beforeRelease = await Call(maker, makerContext, selections))
            Assert.Equal(0, beforeRelease.RootElement.GetProperty("masters").GetArrayLength());

        var revoke = issueReview with { CommandId = Guid.NewGuid(), Grant = false, ExpectedQualificationId = proof, ExpiresAt = null };
        Assert.Equal(proof, await Decide(revoke));
        var failedRelease = new ManufacturingCenterPublishV1(Guid.NewGuid(), 2);
        using (await Call(releaser, publisherContext, $"{centers}/{original}/publish", failedRelease,
            HttpStatusCode.Forbidden, "FINANCE_QUALIFICATION_REQUIRED")) { }
        Assert.NotEqual(proof, await Decide(Grant(reviewer.Id, "center-review") with { ExpectedQualificationId = proof }));
        using (await Call(releaser, publisherContext, $"{centers}/{original}/publish", failedRelease,
            HttpStatusCode.Forbidden, "FINANCE_QUALIFICATION_REQUIRED")) { }

        var printEdition = await ApproveAndPublish(sourceDraft with { CommandId = Guid.NewGuid() });
        var packEdition = await ApproveAndPublish(sourceDraft with { CommandId = Guid.NewGuid(), RootId = packCenter.Id, OperationId = pack.Id });
        using var options = await Call(maker, makerContext, selections);
        var locators = options.RootElement.GetProperty("masters").EnumerateArray().Select(ReadLocator).ToDictionary(row => row.RootId);
        Assert.Equal(printEdition, locators[printCenter.Id].VersionId);
        Assert.Equal(packEdition, locators[packCenter.Id].VersionId);

        var policyInput = new ManufacturingCostPolicySubmitV1(Guid.NewGuid(), null, day, nextDay, 2, "HALF_UP",
            "APPROVED_ACCOUNTING_DATE", "STANDARD_OUTPUT_WITH_VARIANCE", "VAR_TO_EXPENSE", false,
            "FIN-POLICY-LOCAL-202606", "按批准会计日归集工费");
        var pendingPolicy = await SubmitMaster("policies", policyInput);
        var reviewPolicy = new ManufacturingCostMasterDecisionV1(Guid.NewGuid(), "1", "专业核准成本政策原件");
        var pendingReviewPath = $"{masters}/policies/{pendingPolicy}/review";
        using (await Call(maker, makerContext, pendingReviewPath, reviewPolicy,
            HttpStatusCode.Forbidden, "FINANCE_REVIEW_INDEPENDENCE_REQUIRED")) { }
        using (await Call(checker, reviewerContext, pendingReviewPath, reviewPolicy,
            HttpStatusCode.Forbidden, "FINANCE_QUALIFICATION_REQUIRED")) { }
        var policyReview = Grant(reviewer.Id, "policy-review");
        var reviewGrant = await Decide(policyReview);
        await Decide(Grant(publisher.Id, "policy-release"));
        await Decide(Grant(reviewer.Id, "rate-basis-review"));
        await Decide(Grant(publisher.Id, "rate-basis-release"));
        using (await Call(checker, reviewerContext, pendingReviewPath, reviewPolicy)) { }

        var pendingBasis = new ManufacturingRateBasisSubmitV1(Guid.NewGuid(), null, pendingPolicy, press.Id, print.Id,
            day, nextDay, "FIN-RATE-BASIS-PRINT", null, "已提交印刷测算依据");
        using (await Call(maker, makerContext, $"{masters}/rate-bases", pendingBasis,
            HttpStatusCode.UnprocessableEntity, "APPROVED_RATE_BASIS_POLICY_REQUIRED")) { }
        await Decide(policyReview with { CommandId = Guid.NewGuid(), Grant = false, ExpectedQualificationId = reviewGrant, ExpiresAt = null });
        using (await Call(releaser, publisherContext, $"{masters}/policies/{pendingPolicy}/publish",
            new ManufacturingCostMasterDecisionV1(Guid.NewGuid(), "2", "发布前复核原批准资格"), HttpStatusCode.Forbidden,
            "FINANCE_QUALIFICATION_REQUIRED")) { }
        await Decide(policyReview with { CommandId = Guid.NewGuid(), ExpectedQualificationId = reviewGrant });

        policyInput = policyInput with { CommandId = Guid.NewGuid() };
        var policy = await SubmitMaster("policies", policyInput);
        Assert.Equal(policy, await SubmitMaster("policies", policyInput));
        using (await Call(maker, foreignContext, $"{masters}/{policy}", expected: HttpStatusCode.NotFound,
            error: "COST_MASTER_VERSION_NOT_FOUND")) { }
        using (var absent = await Call(maker, foreignContext, $"{c28}/{policyInput.CommandId}"))
            Assert.Equal("NOT_OBSERVED", absent.RootElement.GetProperty("kind").GetString());
        using (await Call(maker, makerContext, $"{masters}/rate-bases", pendingBasis with { CommandId = policyInput.CommandId },
            HttpStatusCode.Conflict, "COMMAND_IDENTITY_CONFLICT")) { }
        await ApproveAndReleaseMaster("policies", policy, checkRollback: true);
        using (var originalSubmission = await Call(maker, makerContext, $"{c28}/{policyInput.CommandId}/result"))
            Assert.Equal("SUBMITTED", originalSubmission.RootElement.GetProperty("state").GetString());
        var costMenu = await h.Db.Sys_Menus.Where(row => row.MenuKey == "fin-cost").Select(row => row.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(row => row.RoleId == 72 && row.MenuId == costMenu &&
            row.ActionCode == "cost-master-view").ExecuteDeleteAsync();
        using (await Call(maker, makerContext, $"{c28}/{Guid.NewGuid()}", expected: HttpStatusCode.NotFound,
            error: "NOT_FOUND_OR_NOT_VISIBLE")) { }
        await GrantLegacyAuthorityAction(h, "fin-cost", "cost-master-view");

        var printInput = pendingBasis with { CommandId = Guid.NewGuid(), PolicyVersionId = policy };
        using (await Call(maker, makerContext, $"{masters}/rate-bases", printInput with { ZeroJustification = "实测没有工费" },
            HttpStatusCode.UnprocessableEntity, "APPROVED_RATE_BASIS_POLICY_REQUIRED")) { }
        var printBasis = await SubmitMaster("rate-bases", printInput);
        var packInput = printInput with { CommandId = Guid.NewGuid(), WorkCenterId = packing.Id, OperationProfileId = pack.Id,
            EvidenceReference = "FIN-RATE-BASIS-PACK" };
        var packBasis = await SubmitMaster("rate-bases", packInput);
        var unopenedRate = new ManufacturingRateDraftV1(press.Id, print.Id, locators[printCenter.Id], policy, printBasis,
            "100", "45.12345678", day, nextDay);
        using (await Call(maker, makerContext, rates, unopenedRate, HttpStatusCode.UnprocessableEntity,
            "APPROVED_RATE_BASIS_REQUIRED")) { }
        await ApproveAndReleaseMaster("rate-bases", printBasis);
        await ApproveAndReleaseMaster("rate-bases", packBasis);
        await h.Db.Sys_RoleDataScopes.Where(row => row.RoleId == 72 && row.ResourceKey == "mes-process-cost-rate")
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.ScopeType, 1));
        using (await Call(checker, reviewerContext, $"{c28}/{printInput.CommandId}", expected: HttpStatusCode.NotFound,
            error: "NOT_FOUND_OR_NOT_VISIBLE")) { }
        await h.Db.Sys_RoleDataScopes.Where(row => row.RoleId == 72 && row.ResourceKey == "mes-process-cost-rate")
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.ScopeType, 5));

        var printRate = new ManufacturingRateDraftV1(press.Id, print.Id, locators[printCenter.Id], policy, printBasis,
            "100", "45.12345678", day, nextDay);
        var packRate = new ManufacturingRateDraftV1(packing.Id, pack.Id, locators[packCenter.Id], policy, packBasis,
            "10.00000001", "3.125", day, nextDay);
        using (await Call(maker, makerContext, rates, printRate with { ToExclusive = nextDay.AddDays(1) },
            HttpStatusCode.UnprocessableEntity, "APPROVED_RATE_BASIS_REQUIRED")) { }
        using (await Call(maker, makerContext, rates, packRate with { CostCenter = locators[printCenter.Id] },
            HttpStatusCode.Conflict, "COST_CENTER_VERSION_UNAVAILABLE")) { }
        var originalRelease = await PublishRate(printRate, checkOriginalActor: true);
        await PublishRate(packRate, checkOriginalActor: false);
        var originalUse = await h.Db.Database.SqlQuery<string>($"""
            SELECT r.adopted::text AS "Value" FROM cp6_fin_mfg_v1.releases e
            JOIN cp6_fin_mfg_v1.center_use_receipts r
              ON (r.tenant_id,r.ledger_id,r.effect_id)=(e.tenant_id,e.ledger_id,e.center_use_effect_id)
            WHERE (e.tenant_id,e.ledger_id,e.rate_id)=({h.Tenant},{ledger},{originalRelease.RateId})
            """).SingleAsync();
        using (var use = JsonDocument.Parse(originalUse))
            Assert.Equal(printEdition, use.RootElement[0].GetProperty("Version").GetProperty("version_id").GetGuid());

        using (var completed = await CompleteForFinance(host, maker, number))
            Assert.True(completed.StatusCode == HttpStatusCode.OK, await completed.Content.ReadAsStringAsync());
        var outcome = await h.Db.IntegrationEvents.AsNoTracking().SingleAsync(row => row.TargetModule == "FIN" && row.SourceNo == number);
        Assert.Equal("COST_LEDGER_SETTLEMENT_ADOPTION_REQUIRED", outcome.LastError);
        var sheet = await h.Db.CostSheets.AsNoTracking().SingleAsync();
        Assert.Equal(213.35m, sheet.LaborActual);
        var originalBasis = await ReadW689Basis(h, sheet.Id, 1);
        AssertAdopted(originalBasis, policy, printBasis, packBasis);
        using (var basis = JsonDocument.Parse(originalBasis))
            Assert.Equal(new[] { printEdition, packEdition }.Order(),
                basis.RootElement.GetProperty("Rates").EnumerateArray().Select(row => row.GetProperty("CostCenterVersionId").GetGuid()).Order());
        Assert.Equal(4, await h.Db.Database.SqlQuery<int>($"""
            SELECT e.item_count AS "Value" FROM cp6_fin_cost_basis_v1.versions v
            JOIN cp6_fin_mfg_v1.center_use_effects e ON (e.tenant_id,e.id)=(v.tenant_id,v.center_use_effect_id)
            WHERE (v.tenant_id,v.cost_sheet_id,v.version)=({h.Tenant},{sheet.Id},1)
            """).SingleAsync());

        await SetBusinessDate(nextDay);
        using (await Call(maker, makerContext, $"/api/fin/cost/{number}/collect", new { },
            HttpStatusCode.Conflict, "PUBLISHED_RATE_UNAVAILABLE")) { }
        await SetBusinessDate(day);
        await h.Db.ProductProcesses.Where(row => row.Id == print.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.ProcessNote1, "工序原件已改版"));
        using (var afterDrift = await Call(maker, makerContext, selections))
            Assert.Equal(packEdition, ReadLocator(Assert.Single(afterDrift.RootElement.GetProperty("masters").EnumerateArray())).VersionId);
        using (await Call(maker, makerContext, $"/api/fin/cost/{number}/collect", new { },
            HttpStatusCode.Conflict, "PUBLISHED_RATE_UNAVAILABLE")) { }
        using (await Call(releaser, publisherContext, $"{rates}/{originalRelease.RateId}/publish",
            new ManufacturingRatePublishV1(originalRelease.PreviewToken))) { }
        using (await Call(maker, makerContext, $"{rates}/{originalRelease.RateId}/publish",
            new ManufacturingRatePublishV1(originalRelease.PreviewToken), HttpStatusCode.NotFound, "RATE_RELEASE_PREVIEW_NOT_FOUND")) { }
        Assert.Equal(originalUse, await h.Db.Database.SqlQuery<string>($"""
            SELECT r.adopted::text AS "Value" FROM cp6_fin_mfg_v1.releases e
            JOIN cp6_fin_mfg_v1.center_use_receipts r
              ON (r.tenant_id,r.ledger_id,r.effect_id)=(e.tenant_id,e.ledger_id,e.center_use_effect_id)
            WHERE (e.tenant_id,e.ledger_id,e.rate_id)=({h.Tenant},{ledger},{originalRelease.RateId})
            """).SingleAsync());
        Assert.Equal(originalBasis, await ReadW689Basis(h, sheet.Id, 1));

        var noReplacement = await Submit(sourceDraft with { CommandId = Guid.NewGuid() });
        using (await Call(checker, reviewerContext, $"{centers}/{noReplacement}/review", approval with { CommandId = Guid.NewGuid() })) { }
        using (await Call(releaser, publisherContext, $"{centers}/{noReplacement}/publish", new ManufacturingCenterPublishV1(Guid.NewGuid(), 2),
            HttpStatusCode.Conflict, "COST_CENTER_RELEASE_INTERVAL_CONFLICT")) { }

        var successor = await ApproveAndPublish(sourceDraft with { CommandId = Guid.NewGuid(), SupersedesVersionId = printEdition });
        using var afterRelease = await Call(maker, makerContext, selections);
        var locator = afterRelease.RootElement.GetProperty("masters").EnumerateArray().Select(ReadLocator)
            .Single(row => row.RootId == printCenter.Id);
        Assert.Equal(successor, locator.VersionId);
        var nextPolicy = await SubmitMaster("policies", policyInput with { CommandId = Guid.NewGuid(), PreviousVersionId = policy,
            From = nextDay, ToExclusive = null, AllowZeroRate = true });
        await ApproveAndReleaseMaster("policies", nextPolicy);
        using (var edition = await Call(maker, makerContext, $"{masters}/{nextPolicy}"))
        {
            Assert.Equal(policy, edition.RootElement.GetProperty("rootId").GetGuid());
            Assert.Equal("2", edition.RootElement.GetProperty("version").GetString());
        }
        var nextPrint = await SubmitMaster("rate-bases", printInput with { CommandId = Guid.NewGuid(), PreviousVersionId = printBasis,
            PolicyVersionId = nextPolicy, From = nextDay, ToExclusive = null });
        var nextPack = await SubmitMaster("rate-bases", packInput with { CommandId = Guid.NewGuid(), PreviousVersionId = packBasis,
            PolicyVersionId = nextPolicy, From = nextDay, ToExclusive = null, ZeroJustification = "设备不分摊包装制造费用" });
        await ApproveAndReleaseMaster("rate-bases", nextPrint);
        await ApproveAndReleaseMaster("rate-bases", nextPack);
        await SetBusinessDate(nextDay);
        await PublishRate(printRate with { CostCenter = locator, PolicyVersionId = nextPolicy, BasisId = nextPrint,
            From = nextDay, ToExclusive = null, LaborRate = "80" }, checkOriginalActor: false);
        await PublishRate(packRate with { PolicyVersionId = nextPolicy, BasisId = nextPack,
            From = nextDay, ToExclusive = null, OverheadRate = "0" }, checkOriginalActor: false);
        using (await Call(maker, makerContext, $"/api/fin/cost/{number}/collect", new { })) { }
        Assert.Equal(170.88m, await h.Db.CostSheets.Where(row => row.Id == sheet.Id).Select(row => row.LaborActual).SingleAsync());
        Assert.Equal(originalBasis, await ReadW689Basis(h, sheet.Id, 1));
        var nextEvidence = await ReadW689Basis(h, sheet.Id, 2);
        AssertAdopted(nextEvidence, nextPolicy, nextPrint, nextPack);
        using var nextBasis = JsonDocument.Parse(nextEvidence);
        Assert.Contains(nextBasis.RootElement.GetProperty("Rates").EnumerateArray(),
            row => row.GetProperty("CostCenterVersionId").GetGuid() == successor);
        Assert.Equal(0m, Assert.Single(nextBasis.RootElement.GetProperty("Rates").EnumerateArray(),
            row => row.GetProperty("RateBasisId").GetGuid() == nextPack).GetProperty("OverheadRate").GetDecimal());
        Assert.False(await h.Db.JournalEntries.AnyAsync());

        async Task<Guid> SubmitMaster(string resource, object body)
        {
            using var response = await Call(maker, makerContext, $"{masters}/{resource}", body, HttpStatusCode.Created);
            Assert.Equal("SUBMITTED", response.RootElement.GetProperty("state").GetString());
            return response.RootElement.GetProperty("versionId").GetGuid();
        }

        async Task ApproveAndReleaseMaster(string resource, Guid id, bool checkRollback = false)
        {
            var review = new ManufacturingCostMasterDecisionV1(Guid.NewGuid(), "1", "独立核准精确版本");
            using var approved = await Call(checker, reviewerContext, $"{masters}/{resource}/{id}/review", review);
            var approveId = approved.RootElement.GetProperty("approvalId").GetGuid();
            var release = new ManufacturingCostMasterDecisionV1(Guid.NewGuid(), "2", "发布已核准版本");
            var path = $"{masters}/{resource}/{id}/publish";
            if (checkRollback)
            {
                await h.Db.Database.ExecuteSqlRawAsync("""
                    CREATE FUNCTION cp6_fin_mfg_v1.w697_commit_fault() RETURNS trigger LANGUAGE plpgsql AS $$
                    BEGIN RAISE EXCEPTION 'W697_COMMIT_FAULT'; END; $$;
                    CREATE CONSTRAINT TRIGGER w697_commit_fault AFTER INSERT ON cp6_fin_mfg_v1.finance_command_receipts
                        DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_fin_mfg_v1.w697_commit_fault();
                    """);
                try
                {
                    using var uncertain = await Call(releaser, publisherContext, path, release, HttpStatusCode.Accepted);
                    var query = uncertain.RootElement.GetProperty("queryPath").GetString();
                    Assert.Equal($"{c28}/{release.CommandId}", query);
                    using var observed = await Call(releaser, publisherContext, query!);
                    Assert.Equal("NOT_OBSERVED", observed.RootElement.GetProperty("kind").GetString());
                    using var unchanged = await Call(maker, makerContext, $"{masters}/{id}");
                    Assert.Equal("APPROVED", unchanged.RootElement.GetProperty("state").GetString());
                    Assert.False(await h.Db.Database.SqlQuery<bool>($"""
                        SELECT EXISTS(SELECT FROM cp6_fin_mfg_v1.policies
                            WHERE (tenant_id,ledger_id,version_id)=({h.Tenant},{ledger},{id})) AS "Value"
                        """).SingleAsync());
                }
                finally
                {
                    await h.Db.Database.ExecuteSqlRawAsync("""
                        DROP TRIGGER w697_commit_fault ON cp6_fin_mfg_v1.finance_command_receipts;
                        DROP FUNCTION cp6_fin_mfg_v1.w697_commit_fault();
                        """);
                }
            }
            using var published = await Call(releaser, publisherContext, path, release);
            using var replay = await Call(releaser, publisherContext, path, release);
            Assert.Equal("RELEASED", published.RootElement.GetProperty("state").GetString());
            Assert.Equal(approveId, published.RootElement.GetProperty("approvalId").GetGuid());
            var releaseId = published.RootElement.GetProperty("releaseId").GetGuid();
            Assert.Equal(releaseId, replay.RootElement.GetProperty("releaseId").GetGuid());
            using var original = await Call(maker, makerContext, $"{c28}/{release.CommandId}");
            Assert.Equal("FOUND", original.RootElement.GetProperty("kind").GetString());
            Assert.Equal(releaseId, original.RootElement.GetProperty("reply").GetProperty("receipt").GetProperty("id").GetGuid());
        }

        Task SetBusinessDate(DateOnly date) => h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_fin_mfg_v1.ledgers SET business_date={date} WHERE (tenant_id,id)=({h.Tenant},{ledger})
            """);

        static void AssertAdopted(string bytes, Guid policyVersion, params Guid[] rateBases)
        {
            using var snapshot = JsonDocument.Parse(bytes);
            var selected = snapshot.RootElement.GetProperty("Rates").EnumerateArray().ToArray();
            Assert.All(selected, row => Assert.Equal(policyVersion, row.GetProperty("PolicyVersionId").GetGuid()));
            Assert.Equal(rateBases.Order(), selected.Select(row => row.GetProperty("RateBasisId").GetGuid()).Order());
        }

        ManufacturingQualificationCommandV1 Grant(Guid subject, string purpose) => new(Guid.NewGuid(), subject, purpose,
            true, null, DateTimeOffset.UtcNow.AddDays(1), "TEST-QUALIFICATION-691", "审查专业资格资料后签发");

        async Task<Guid> Decide(ManufacturingQualificationCommandV1 command)
        {
            using var response = await Call(registrar, issuerContext, qualifications, command);
            return response.RootElement.GetProperty("qualificationId").GetGuid();
        }

        async Task<Guid> Submit(ManufacturingCenterSubmissionV1 command)
        {
            using var response = await Call(maker, makerContext, centers, command, HttpStatusCode.Created);
            return response.RootElement.GetProperty("versionId").GetGuid();
        }

        async Task<Guid> ApproveAndPublish(ManufacturingCenterSubmissionV1 command)
        {
            var id = await Submit(command);
            using (await Call(checker, reviewerContext, $"{centers}/{id}/review", approval with { CommandId = Guid.NewGuid() })) { }
            var publish = new ManufacturingCenterPublishV1(Guid.NewGuid(), 2);
            using (await Call(releaser, publisherContext, $"{centers}/{id}/publish", publish)) { }
            using (await Call(releaser, publisherContext, $"{centers}/{id}/publish", publish)) { }
            return id;
        }

        async Task<(Guid RateId, Guid PreviewToken)> PublishRate(ManufacturingRateDraftV1 draft, bool checkOriginalActor)
        {
            using var drafted = await Call(maker, makerContext, rates, draft, HttpStatusCode.Created);
            var id = drafted.RootElement.GetProperty("rateId").GetGuid();
            using (await Call(maker, makerContext, $"{rates}/{id}/submit", new ManufacturingRateTransitionV1(1))) { }
            if (checkOriginalActor)
            {
                var admin = await Login(host, platform);
                using var entered = await host.Send(OperationsRequest(admin, HttpMethod.Post, "/api/platform/impersonation/start",
                    new { TenantId = h.Tenant, UserId = reviewer.Id, Reason = "财务原操作者边界验证" }));
                Assert.True(entered.StatusCode == HttpStatusCode.OK, await OperationsFailure(entered));
                var token = entered.Headers.GetValues("Set-Cookie").Single(header => header.StartsWith("cp6_at=", StringComparison.Ordinal))
                    .Split(';', 2)[0]["cp6_at=".Length..];
                var simulated = new Dictionary<string, string> { ["cp6_at"] = token, ["cp6_csrf"] = admin["cp6_csrf"] };
                using (await Call(simulated, reviewerContext, $"{rates}/{id}/review", new ManufacturingRateTransitionV1(2),
                    HttpStatusCode.Forbidden, "FINANCE_ORIGINAL_ACTOR_REQUIRED")) { }
            }
            using (await Call(checker, reviewerContext, $"{rates}/{id}/review", new ManufacturingRateTransitionV1(2))) { }
            if (checkOriginalActor)
            {
                // 按上轮审批记录的实际持久字段构造迁移兼容数据，保留缺失的本人会话证据。
                var historicalApproval = Guid.NewGuid();
                await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO cp6_fin_mfg_v1.rate_versions
                        (tenant_id,ledger_id,id,work_center_id,operation_id,center_id,center_version_id,policy_version_id,basis_id,
                         labor_rate,overhead_rate,valid_from,to_exclusive,maker,state,revision,reviewer,review_qualification_id,submitted_at,reviewed_at)
                    SELECT tenant_id,ledger_id,{historicalApproval},work_center_id,operation_id,center_id,center_version_id,policy_version_id,basis_id,
                         labor_rate,overhead_rate,valid_from,to_exclusive,maker,state,revision,reviewer,review_qualification_id,submitted_at,reviewed_at
                    FROM cp6_fin_mfg_v1.rate_versions WHERE tenant_id={h.Tenant} AND ledger_id={ledger} AND id={id}
                    """);
                using (await Call(releaser, publisherContext, $"{rates}/{historicalApproval}/release-preview", new ManufacturingRateTransitionV1(3),
                    HttpStatusCode.Conflict, "FINANCE_RATE_REVIEW_ORIGIN_UNPROVEN")) { }
            }
            using var preview = await Call(releaser, publisherContext, $"{rates}/{id}/release-preview", new ManufacturingRateTransitionV1(3));
            var previewToken = preview.RootElement.GetProperty("token").GetGuid();
            if (checkOriginalActor)
            {
                await h.Db.Database.ExecuteSqlRawAsync("""
                    CREATE FUNCTION cp6_fin_mfg_v1.w692_drop_release() RETURNS trigger LANGUAGE plpgsql AS $$
                    BEGIN
                        IF EXISTS(SELECT 1 FROM cp6_fin_mfg_v1.ledgers WHERE tenant_id=NEW.tenant_id AND id=NEW.ledger_id
                            AND environment_id='w691-installed') THEN RETURN NULL; END IF;
                        RETURN NEW;
                    END; $$;
                    CREATE TRIGGER w692_drop_release BEFORE INSERT ON cp6_fin_mfg_v1.releases
                        FOR EACH ROW EXECUTE FUNCTION cp6_fin_mfg_v1.w692_drop_release();
                    """);
                try
                {
                    using (await Call(releaser, publisherContext, $"{rates}/{id}/publish", new ManufacturingRatePublishV1(previewToken),
                        HttpStatusCode.ServiceUnavailable, "OPERATIONS_OUTCOME_UNKNOWN")) { }
                }
                finally
                {
                    await h.Db.Database.ExecuteSqlRawAsync("""
                        DROP TRIGGER w692_drop_release ON cp6_fin_mfg_v1.releases;
                        DROP FUNCTION cp6_fin_mfg_v1.w692_drop_release();
                        """);
                }
                Assert.False(await h.Db.Database.SqlQuery<bool>($"""
                    SELECT EXISTS(SELECT 1 FROM cp6_fin_mfg_v1.center_use_effects WHERE tenant_id={h.Tenant}
                        AND ledger_id={ledger} AND result_id={id}) AS "Value"
                    """).SingleAsync());
            }
            using (await Call(releaser, publisherContext, $"{rates}/{id}/publish",
                new ManufacturingRatePublishV1(previewToken))) { }
            return (id, previewToken);
        }

        async Task<Guid> Context(Dictionary<string, string> cookies, string ledgerKey = "COST-BOOK")
        {
            using var response = await Call(cookies, null, $"/api/fin/cost/v1/options?legalEntityKey=LE1&ledgerKey={ledgerKey}");
            return response.RootElement.GetProperty("contextToken").GetGuid();
        }

        async Task<JsonDocument> Call(Dictionary<string, string> cookies, Guid? context, string path, object? body = null,
            HttpStatusCode expected = HttpStatusCode.OK, string? error = null)
        {
            using var request = OperationsRequest(cookies, body == null ? HttpMethod.Get : HttpMethod.Post, path, body);
            if (context.HasValue)
                request.Headers.Add("X-Finance-Context", context.Value.ToString());
            using var response = await host.Send(request);
            var content = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == expected, $"{path}: {response.StatusCode} {content}");
            if (error != null)
                Assert.Contains(error, content);
            return JsonDocument.Parse(content);
        }
    }
}
