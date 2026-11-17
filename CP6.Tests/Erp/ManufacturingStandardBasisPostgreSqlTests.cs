using System.Net;
using System.Text.Json;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task W694_standard_documents_freeze_approved_basis_with_original_command_and_preserve_actual_history()
    {
        await using var h = await Seed();
        var number = await SeedCompletedFinanceFlow(h);
        await GrantNativeCostCollect(h);
        foreach (var action in new[] { "view", "settle", "qualification-admin", "basis-submit", "basis-review",
            "basis-release", "basis-revoke", "basis-view" })
            await GrantLegacyAuthorityAction(h, "fin-cost", action);

        var reviewer = NewOperator("standard-reviewer");
        var publisher = NewOperator("standard-publisher");
        var issuer = NewOperator("standard-issuer");
        h.Db.AddRange(reviewer, publisher, issuer);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        var order = await h.Db.WorkOrders.AsNoTracking().SingleAsync(row => row.WorkOrderNo == number);
        await h.Db.ProductMasters.Where(row => row.ProductCd == order.ProductCd)
            .ExecuteUpdateAsync(change => change.SetProperty(row => row.QtyUnit, "EA"));
        await h.Db.WorkOrderMaterials.Where(row => row.WorkOrderNo == number)
            .ExecuteUpdateAsync(change => change.SetProperty(row => row.Unit, "EA"));
        var material = await h.Db.WorkOrderMaterials.AsNoTracking().SingleAsync(row => row.WorkOrderNo == number);
        var price = await h.Db.ProductMaterials.AsNoTracking().SingleAsync(row => row.ProductCd == order.ProductCd);
        var steps = await h.Db.WorkOrderProcesses.AsNoTracking().Where(row => row.WorkOrderNo == number)
            .OrderBy(row => row.SortOrder).ToArrayAsync();
        var operations = await h.Db.ProductProcesses.AsNoTracking().Where(row => row.ProductCd == order.ProductCd).ToArrayAsync();

        var day = new DateOnly(2026, 6, 30);
        var successorDay = day.AddDays(1);
        var expiry = day.AddDays(2);
        var ledger = Guid.NewGuid();
        var otherLedger = Guid.NewGuid();
        var policy = Guid.NewGuid();
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_fin_mfg_v1.ledgers (tenant_id,id,environment_id,mode,legal_entity_key,ledger_key,currency,business_date)
            VALUES ({h.Tenant},{ledger},'w693-standard','REAL','LE1','MFG','CNY',{day}),
                   ({h.Tenant},{otherLedger},'w693-standard','REAL','LE1','OTHER','CNY',{day});
            INSERT INTO cp6_fin_mfg_v1.members (tenant_id,ledger_id,subject_id) VALUES
                ({h.Tenant},{ledger},{h.User}),({h.Tenant},{ledger},{reviewer.Id}),({h.Tenant},{ledger},{publisher.Id}),
                ({h.Tenant},{ledger},{issuer.Id}),({h.Tenant},{otherLedger},{h.User});
            INSERT INTO cp6_fin_mfg_v1.work_orders (tenant_id,work_order_id,ledger_id) VALUES ({h.Tenant},{order.Id},{ledger});
            INSERT INTO cp6_fin_mfg_v1.policies
                (tenant_id,ledger_id,version_id,approval_id,currency,currency_scale,rounding,rate_date_basis,valuation,variance,valid_from)
            VALUES ({h.Tenant},{ledger},{policy},{Guid.NewGuid()},'CNY',2,'HALF_UP','APPROVED_ACCOUNTING_DATE',
                'STANDARD_OUTPUT_WITH_VARIANCE','VAR_TO_EXPENSE',{day});
            """);

        // 模拟已批准的上游政策、成本中心和费率，实际完工、标准批准和归集均走应用入口。
        var originalPressRate = Guid.NewGuid();
        var nextPressRate = Guid.NewGuid();
        foreach (var step in steps)
        {
            var center = new CostCenter { Code = $"W693-{step.ProcessCd}", Name = step.ProcessCd, Type = CostCenterType.Process };
            h.Db.CostCenters.Add(center);
            await h.Db.SaveChangesAsync();
            var op = operations.Single(row => row.ProcessCd == step.ProcessCd && row.TaskCd == step.TaskCd);
            var workCenter = await h.Db.WorkCenters.AsNoTracking().SingleAsync(row => row.WgCd == step.WgCd);
            var edition = Guid.NewGuid();
            var rateBasis = Guid.NewGuid();
            var isPress = step.ProcessCd == "PRINT";
            await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_fin_mfg_v1.center_submissions
                    (tenant_id,ledger_id,id,root_id,operation_id,operation_version,code,name,valid_from,maker,submission_key,submission,
                     state,revision,reviewer,review_qualification_id,review_key,review,reviewed_at,publisher,release_qualification_id,release_key,released_at)
                VALUES ({h.Tenant},{ledger},{edition},{center.Id},{op.Id},{op.RowVersion},{center.Code},{center.Name},{day},
                    {h.User},{Guid.NewGuid()},jsonb_build_object('Fixture','W693'),'RELEASED',3,{reviewer.Id},{Guid.NewGuid()},
                    {Guid.NewGuid()},jsonb_build_object('Decision','APPROVE'),clock_timestamp(),{publisher.Id},{Guid.NewGuid()},
                    {Guid.NewGuid()},clock_timestamp());
                INSERT INTO cp6_fin_mfg_v1.center_versions
                    (tenant_id,ledger_id,root_id,version_id,version,code,name,valid_from,approval_id,submission_id)
                VALUES ({h.Tenant},{ledger},{center.Id},{edition},1,{center.Code},{center.Name},{day},{edition},{edition});
                INSERT INTO cp6_fin_mfg_v1.rate_bases (tenant_id,ledger_id,id,policy_version_id,approval_id,work_center_id,operation_id,valid_from)
                VALUES ({h.Tenant},{ledger},{rateBasis},{policy},{Guid.NewGuid()},{workCenter.Id},{op.Id},{day});
                INSERT INTO cp6_fin_mfg_v1.rate_heads (tenant_id,ledger_id,work_center_id,operation_id,revision)
                VALUES ({h.Tenant},{ledger},{workCenter.Id},{op.Id},1);
                """);
            await Rate(isPress ? originalPressRate : Guid.NewGuid(), day, isPress ? successorDay : null,
                isPress ? 100m : 10.00000001m, isPress ? 45.12345678m : 3.125m);
            if (isPress)
                await Rate(nextPressRate, successorDay, null, 80m, 45.12345678m);

            Task Rate(Guid id, DateOnly from, DateOnly? to, decimal labor, decimal overhead)
                => h.Db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO cp6_fin_mfg_v1.rate_versions
                        (tenant_id,ledger_id,id,work_center_id,operation_id,center_id,center_version_id,policy_version_id,basis_id,
                         labor_rate,overhead_rate,valid_from,to_exclusive,maker,state,revision,reviewer,review_qualification_id,publisher,
                         release_qualification_id,submitted_at,reviewed_at,review_original_actor)
                    VALUES ({h.Tenant},{ledger},{id},{workCenter.Id},{op.Id},{center.Id},{edition},{policy},{rateBasis},
                        {labor},{overhead},{from},{to},{h.User},'RELEASED',4,{reviewer.Id},{Guid.NewGuid()},
                        {publisher.Id},{Guid.NewGuid()},clock_timestamp(),clock_timestamp(),true);
                    INSERT INTO cp6_fin_mfg_v1.timeline (tenant_id,ledger_id,work_center_id,operation_id,rate_id,valid_from,to_exclusive)
                    VALUES ({h.Tenant},{ledger},{workCenter.Id},{op.Id},{id},{from},{to});
                    """);
        }

        var environment = new Dictionary<string, string>(IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(h.Tenant))
        {
            ["FinanceCosting__EnvironmentId"] = "w693-standard",
            ["StockFinBridge__Enabled"] = "false",
        };
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, environment);
        var maker = await Session("price-owner");
        var checker = await Session(reviewer.UserName);
        var releaser = await Session(publisher.UserName);
        var registrar = await Session(issuer.UserName);
        var foreign = await Session("price-owner", "OTHER");
        const string basis = "/api/fin/cost/owner-v1/standard-bases";
        const string qualification = "/api/fin/cost/owner-v1/qualifications";
        var collect = $"/api/fin/cost/{number}/collect";
        var comparison = $"{basis}/comparisons/{order.Id}";
        var command = new StandardBasisSubmissionV1(Guid.NewGuid(), order.Id, policy, null, "4.12345678", "EA", day, expiry,
            [new(material.Id, "2", "EA")], [new(steps[0].Id, "2", "1"), new(steps[1].Id, "1", "2")], null, "批准整单标准用量与工时");

        using (await Call(foreign, basis, command, HttpStatusCode.NotFound, "STANDARD_BASIS_WORK_ORDER_NOT_FOUND")) { }
        using (await Call(maker, basis, command with { OutputUnit = "KG" }, HttpStatusCode.UnprocessableEntity,
            "STANDARD_BASIS_UNIT_MISMATCH")) { }
        using (await Call(maker, basis, command with { TargetQuantity = "4.123456789" }, HttpStatusCode.UnprocessableEntity,
            "STANDARD_BASIS_QUANTITY_INVALID")) { }
        var first = await Submit(command);
        Assert.Equal(first, await Submit(command));
        using (await Call(maker, basis, command with { Reason = "更改相同请求" }, HttpStatusCode.Conflict, "FINANCE_COMMAND_CONFLICT")) { }
        using (await Call(foreign, $"{basis}/{first}", expected: HttpStatusCode.NotFound)) { }
        var review = new StandardBasisReviewV1(Guid.NewGuid(), 1, "独立复核材料单价、工时与批准费率");
        using (await Call(maker, $"{basis}/{first}/review", review, HttpStatusCode.Forbidden, "FINANCE_REVIEW_INDEPENDENCE_REQUIRED")) { }
        using (await Call(checker, $"{basis}/{first}/review", review, HttpStatusCode.Forbidden, "FINANCE_QUALIFICATION_REQUIRED")) { }
        var reviewGrant = new ManufacturingQualificationCommandV1(Guid.NewGuid(), reviewer.Id, "basis-review", true,
            null, DateTimeOffset.UtcNow.AddDays(1), "SIM-W693-QUALIFIED", "已核对标准成本专业资格");
        using (await Call(registrar, qualification, reviewGrant)) { }
        using (await Call(registrar, qualification, reviewGrant with
        {
            CommandId = Guid.NewGuid(), SubjectId = publisher.Id, Purpose = "basis-release",
        })) { }
        using (await Call(checker, $"{basis}/{first}/review", review, HttpStatusCode.NotFound, "STANDARD_BASIS_WORK_ORDER_NOT_FOUND")) { }
        h.Db.Sys_RoleDataScopes.Add(new() { RoleId = 72, ResourceKey = "mes-work-order", ScopeType = 5 });
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        using (await Call(checker, $"{basis}/{first}/review", review)) { }
        using (var pending = await Call(maker, $"{basis}/{first}"))
            Assert.False(pending.RootElement.GetProperty("selected").GetBoolean());
        await Publish(first);
        using (await Call(maker, collect, new { }, HttpStatusCode.Forbidden, "ACTION_FORBIDDEN")) { }
        await GrantLegacyAuthorityAction(h, "fin-cost", "basis-use");
        using (await Call(maker, collect, new { }, HttpStatusCode.Conflict, "STANDARD_BASIS_TARGET_MISMATCH")) { }
        Assert.False(await h.Db.CostSheets.AnyAsync());

        const string documents = "/api/fin/cost/owner-v1/standard-documents";
        const string commands = "/api/fin/cost/owner-v1/standard-document-commands";
        var create = new { CommandId = Guid.NewGuid(), WorkOrderId = order.Id, Reason = "按批准标准建立独立成本单" };
        using (await Call(foreign, documents, create, HttpStatusCode.NotFound, "STANDARD_BASIS_WORK_ORDER_NOT_FOUND")) { }
        using var created = await Call(maker, documents, create, HttpStatusCode.Created);
        var document = created.RootElement.GetProperty("documentId").GetGuid();
        var documentPath = $"{documents}/{document}";
        var prepare = new { CommandId = Guid.NewGuid(), ExpectedVersion = (string?)null,
            StandardBasisVersionId = first, Reason = "冻结批准整单标准" };
        using (await Call(maker, $"{documentPath}/collection-plans", prepare, HttpStatusCode.Conflict, "STANDARD_BASIS_TARGET_MISMATCH")) { }
        using (await Call(maker, $"{documentPath}/collection-plans", prepare with { CommandId = create.CommandId },
            HttpStatusCode.Conflict, "COMMAND_IDENTITY_CONFLICT")) { }

        using (var complete = await CompleteForFinance(host, maker.Cookies, number))
            Assert.True(complete.StatusCode == HttpStatusCode.OK, await complete.Content.ReadAsStringAsync());
        var finance = await h.Db.IntegrationEvents.AsNoTracking().SingleAsync(row => row.TargetModule == "FIN" && row.SourceNo == number);
        Assert.Equal("COST_LEDGER_SETTLEMENT_ADOPTION_REQUIRED", finance.LastError);
        var firstSheet = await h.Db.CostSheets.AsNoTracking().SingleAsync();
        Assert.Equal(213.35m, firstSheet.LaborActual);
        Assert.Equal(11.36m, firstSheet.MaterialStandard);
        Assert.Equal(210m, firstSheet.LaborStandard);
        Assert.Equal(51.37m, firstSheet.OverheadStandard);
        var firstRecord = await ReadW689Basis(h, firstSheet.Id, 1);
        // 独立标准单据按审批原件取值，实绩工时临时缺失仍可冻结。
        await h.Db.WorkOrderProcesses.Where(row => row.Id == steps[0].Id)
            .ExecuteUpdateAsync(change => change.SetProperty(row => row.ActualMachineHour, (decimal?)null));
        using var prepared = await Call(maker, $"{documentPath}/collection-plans", prepare, HttpStatusCode.Created);
        var plan = prepared.RootElement.GetProperty("planId").GetGuid();
        using var competingPlan = await Call(maker, $"{documentPath}/collection-plans",
            prepare with { CommandId = Guid.NewGuid() }, HttpStatusCode.Created);
        var freeze = new { CommandId = Guid.NewGuid(), PlanId = plan, ExpectedVersion = (string?)null, Reason = "确认标准成本" };
        using (await Call(checker, $"{documentPath}/freeze", freeze, HttpStatusCode.NotFound, "STANDARD_DOCUMENT_PLAN_NOT_FOUND")) { }
        await SetBusinessDate(successorDay);
        using (await Call(maker, $"{documentPath}/freeze", freeze, HttpStatusCode.PreconditionFailed, "STANDARD_DOCUMENT_PLAN_STALE")) { }
        await SetBusinessDate(day);
        await h.Db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION cp6_fin_mfg_v1.w694_drop_standard_command() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF NEW.operation='FREEZE' THEN RETURN NULL; END IF;
                RETURN NEW;
            END; $$;
            CREATE TRIGGER w694_drop_standard_command BEFORE INSERT ON cp6_fin_mfg_v1.standard_document_commands
                FOR EACH ROW EXECUTE FUNCTION cp6_fin_mfg_v1.w694_drop_standard_command();
            """);
        try
        {
            using (await Call(maker, $"{documentPath}/freeze", freeze, HttpStatusCode.Conflict, "STANDARD_DOCUMENT_COMMAND_NOT_WRITTEN")) { }
            using var query = await Call(maker, $"{commands}/{freeze.CommandId}");
            Assert.Equal("NOT_OBSERVED", query.RootElement.GetProperty("kind").GetString());
            Assert.False(query.RootElement.GetProperty("provesPermanentNoEffect").GetBoolean());
            using var draft = await Call(maker, documentPath);
            Assert.Equal("DRAFT", draft.RootElement.GetProperty("state").GetString());
        }
        finally
        {
            await h.Db.Database.ExecuteSqlRawAsync("""
                DROP TRIGGER w694_drop_standard_command ON cp6_fin_mfg_v1.standard_document_commands;
                DROP FUNCTION cp6_fin_mfg_v1.w694_drop_standard_command();
                """);
        }
        await h.Db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION cp6_fin_mfg_v1.w694_reject_standard_commit() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                RAISE EXCEPTION 'W694_DEFERRED_COMMIT_FAILURE';
            END; $$;
            CREATE CONSTRAINT TRIGGER w694_reject_standard_commit AFTER INSERT ON cp6_fin_mfg_v1.standard_document_commands
                DEFERRABLE INITIALLY DEFERRED FOR EACH ROW WHEN (NEW.operation='FREEZE')
                EXECUTE FUNCTION cp6_fin_mfg_v1.w694_reject_standard_commit();
            """);
        try
        {
            using var uncertain = await Call(maker, $"{documentPath}/freeze", freeze, HttpStatusCode.Accepted);
            Assert.Equal("OUTCOME_UNKNOWN", uncertain.RootElement.GetProperty("kind").GetString());
            var queryPath = uncertain.RootElement.GetProperty("queryPath").GetString();
            Assert.Equal($"{commands}/{freeze.CommandId}", queryPath);
            using var after = await Call(maker, queryPath!);
            Assert.Equal("NOT_OBSERVED", after.RootElement.GetProperty("kind").GetString());
        }
        finally
        {
            await h.Db.Database.ExecuteSqlRawAsync("""
                DROP TRIGGER w694_reject_standard_commit ON cp6_fin_mfg_v1.standard_document_commands;
                DROP FUNCTION cp6_fin_mfg_v1.w694_reject_standard_commit();
                """);
        }
        var repeatedFreeze = await Task.WhenAll(Call(maker, $"{documentPath}/freeze", freeze),
            Call(maker, $"{documentPath}/freeze", freeze));
        Assert.Single(repeatedFreeze, reply => reply.RootElement.GetProperty("replayed").GetBoolean());
        Assert.Single(repeatedFreeze.Select(reply => reply.RootElement.GetProperty("receiptId").GetGuid()).Distinct());
        foreach (var reply in repeatedFreeze)
        {
            using (reply)
                Assert.Equal("1", reply.RootElement.GetProperty("version").GetString());
        }
        using (await Call(maker, $"{documentPath}/freeze", freeze with { Reason = "同键改变原内容" },
            HttpStatusCode.Conflict, "COMMAND_IDENTITY_CONFLICT")) { }
        using (await Call(maker, $"{documentPath}/freeze", freeze with { CommandId = Guid.NewGuid(),
                PlanId = competingPlan.RootElement.GetProperty("planId").GetGuid() },
            HttpStatusCode.PreconditionFailed, "HEAD_CHANGED")) { }
        using (var stored = await Call(maker, $"{documentPath}/versions/1"))
        {
            var result = stored.RootElement;
            Assert.Equal("STANDARD", result.GetProperty("basisClass").GetString());
            Assert.Equal(first, result.GetProperty("standardBasis").GetProperty("versionId").GetGuid());
            Assert.Equal("272.73000000", result.GetProperty("body").GetProperty("total").GetString());
        }
        using (var found = await Call(maker, $"{commands}/{freeze.CommandId}"))
        {
            Assert.Equal("FOUND", found.RootElement.GetProperty("kind").GetString());
            Assert.Equal("FREEZE", found.RootElement.GetProperty("reply").GetProperty("operation").GetString());
        }
        using (var unobserved = await Call(foreign, $"{commands}/{freeze.CommandId}"))
            Assert.Equal("NOT_OBSERVED", unobserved.RootElement.GetProperty("kind").GetString());
        using (await Call(foreign, documentPath, expected: HttpStatusCode.NotFound)) { }
        await h.Db.WorkOrderProcesses.Where(row => row.Id == steps[0].Id)
            .ExecuteUpdateAsync(change => change.SetProperty(row => row.ActualMachineHour, steps[0].ActualMachineHour));
        Assert.Equal(firstRecord, await ReadW689Basis(h, firstSheet.Id, 1));

        using (var original = await Call(maker, $"{comparison}/1"))
        {
            var result = original.RootElement;
            Assert.Equal(first, result.GetProperty("standardBasis").GetProperty("versionId").GetGuid());
            Assert.Equal("272.73000000", result.GetProperty("standard").GetString());
            Assert.Equal(StandardBasisBodyV1.DecimalText(firstSheet.Variance), result.GetProperty("variance").GetString());
            var lines = result.GetProperty("body").GetProperty("lines").EnumerateArray().ToArray();
            var materialLine = Assert.Single(lines, row => row.GetProperty("inputId").GetGuid() == material.Id);
            Assert.Equal(price.Id, materialLine.GetProperty("sourceId").GetGuid());
            Assert.Equal(price.RowVersion, materialLine.GetProperty("sourceRowVersion").GetBytesFromBase64());
            Assert.Contains(lines, row => row.GetProperty("sourceId").GetGuid() == originalPressRate);
        }
        var edit = await Assert.ThrowsAsync<PostgresException>(() => h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_fin_mfg_v1.standard_basis_versions SET body=jsonb_build_object()
            WHERE tenant_id={h.Tenant} AND id={first}
            """));
        Assert.Equal("STANDARD_BASIS_IMMUTABLE", edit.MessageText);

        await SetBusinessDate(successorDay);
        await h.Db.ProductMaterials.Where(row => row.Id == price.Id)
            .ExecuteUpdateAsync(change => change.SetProperty(row => row.SupplyPrice, 9m).SetProperty(row => row.UsageUnit, "KG"));
        using (await Call(maker, collect, new { }, HttpStatusCode.Conflict, "STANDARD_BASIS_UNIT_MISMATCH")) { }
        await h.Db.ProductMaterials.Where(row => row.Id == price.Id).ExecuteUpdateAsync(change => change.SetProperty(row => row.UsageUnit, "EA"));
        await h.Db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION cp6_fin_mfg_v1.w693_drop_comparison() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF NEW.standard_basis_id IS NOT NULL AND EXISTS(SELECT 1 FROM cp6_fin_mfg_v1.ledgers
                    WHERE tenant_id=NEW.tenant_id AND environment_id='w693-standard') THEN RETURN NULL; END IF;
                RETURN NEW;
            END; $$;
            CREATE TRIGGER w693_drop_comparison BEFORE INSERT ON cp6_fin_cost_basis_v1.versions
                FOR EACH ROW EXECUTE FUNCTION cp6_fin_mfg_v1.w693_drop_comparison();
            """);
        try
        {
            using (await Call(maker, collect, new { }, HttpStatusCode.Conflict, "COST_COLLECTION_VERSION_NOT_WRITTEN")) { }
            Assert.Equal(firstSheet.LaborActual, await h.Db.CostSheets.Select(row => row.LaborActual).SingleAsync());
            Assert.Equal(1L, await h.Db.Database.SqlQuery<long>($"""
                SELECT count(*) AS "Value" FROM cp6_fin_mfg_v1.center_use_effects WHERE tenant_id={h.Tenant}
                """).SingleAsync());
        }
        finally
        {
            await h.Db.Database.ExecuteSqlRawAsync("""
                DROP TRIGGER w693_drop_comparison ON cp6_fin_cost_basis_v1.versions;
                DROP FUNCTION cp6_fin_mfg_v1.w693_drop_comparison();
                """);
        }
        using (await Call(maker, collect, new { })) { }
        var secondSheet = await h.Db.CostSheets.AsNoTracking().SingleAsync();
        Assert.Equal(firstSheet.Id, secondSheet.Id);
        Assert.Equal(170.88m, secondSheet.LaborActual);
        Assert.Equal(firstSheet.StandardCost, secondSheet.StandardCost);
        using (var next = JsonDocument.Parse(await ReadW689Basis(h, firstSheet.Id, 2)))
            Assert.Contains(next.RootElement.GetProperty("Rates").EnumerateArray(), row => row.GetProperty("RateId").GetGuid() == nextPressRate);

        var zeroDraft = command with { CommandId = Guid.NewGuid(), PreviousVersionId = first, From = successorDay, Materials = [], Times = [],
            ZeroBasisReference = "SIM-W693-ZERO" };
        var competing = await Submit(command with { CommandId = Guid.NewGuid(), PreviousVersionId = first, From = successorDay });
        var zero = await Submit(zeroDraft);
        await Approve(competing);
        await Approve(zero);
        await Publish(zero);
        var zeroPrepare = prepare with { CommandId = Guid.NewGuid(), ExpectedVersion = "1", StandardBasisVersionId = zero };
        using var zeroPrepared = await Call(maker, $"{documentPath}/collection-plans", zeroPrepare, HttpStatusCode.Created);
        var zeroFreeze = freeze with { CommandId = Guid.NewGuid(), ExpectedVersion = "1",
            PlanId = zeroPrepared.RootElement.GetProperty("planId").GetGuid() };
        using (await Call(maker, $"{documentPath}/freeze", zeroFreeze)) { }
        using (var zeroDocument = await Call(maker, $"{documentPath}/versions/2"))
            Assert.Equal("0.00000000", zeroDocument.RootElement.GetProperty("body").GetProperty("total").GetString());
        using (await Call(releaser, $"{basis}/{competing}/publish", new StandardBasisReleaseV1(Guid.NewGuid(), 2),
            HttpStatusCode.PreconditionFailed, "STANDARD_BASIS_HEAD_STALE")) { }
        using (await Call(maker, collect, new { })) { }
        using (var quote = await Call(maker, $"/api/fin/cost/{number}/fg-unit-cost"))
            Assert.Equal(0m, quote.RootElement.GetProperty("data").GetProperty("fgUnitCost").GetDecimal());
        using (var zeroResult = await Call(maker, $"{comparison}/3"))
        {
            var result = zeroResult.RootElement;
            Assert.Equal(zero, result.GetProperty("standardBasis").GetProperty("versionId").GetGuid());
            Assert.Equal("0.00000000", result.GetProperty("standard").GetString());
            Assert.Equal(result.GetProperty("actual").GetString(), result.GetProperty("variance").GetString());
            Assert.Equal("SIM-W693-ZERO", result.GetProperty("body").GetProperty("zeroBasisReference").GetString());
        }

        await SetBusinessDate(expiry);
        using (await Call(maker, collect, new { }, HttpStatusCode.Conflict, "STANDARD_BASIS_NOT_EFFECTIVE")) { }
        var revoke = new StandardBasisReviewV1(Guid.NewGuid(), 3, "撤销当前标准，等待后续版本");
        using (await Call(releaser, $"{basis}/{zero}/revoke", revoke)) { }
        using (await Call(releaser, $"{basis}/{zero}/revoke", revoke)) { }
        using (var originalReply = await Call(maker, $"{documentPath}/freeze", zeroFreeze))
            Assert.True(originalReply.RootElement.GetProperty("replayed").GetBoolean());
        using (await Call(maker, $"{documentPath}/versions/1")) { }
        using (await Call(maker, $"{documentPath}/collection-plans",
            zeroPrepare with { CommandId = Guid.NewGuid(), ExpectedVersion = "2" },
            HttpStatusCode.Conflict, "STANDARD_BASIS_REVOKED")) { }
        using (await Call(maker, collect, new { }, HttpStatusCode.Conflict, "STANDARD_BASIS_REVOKED")) { }
        using (var historical = await Call(maker, $"{comparison}/1"))
            Assert.Equal("272.73000000", historical.RootElement.GetProperty("standard").GetString());
        using (await Call(maker, $"{comparison}/3")) { }
        using (await Call(maker, $"{comparison}/4", expected: HttpStatusCode.NotFound, error: "STANDARD_COMPARISON_NOT_FOUND")) { }
        Assert.Equal(firstRecord, await ReadW689Basis(h, firstSheet.Id, 1));
        Assert.False(await h.Db.JournalEntries.AnyAsync());
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_fin_mfg_v1.members SET enabled=false WHERE (tenant_id,ledger_id,subject_id)=({h.Tenant},{ledger},{h.User})
            """);
        using (await Call(maker, $"{comparison}/1", expected: HttpStatusCode.NotFound, error: "FINANCE_CONTEXT_NOT_FOUND")) { }
        using (await Call(maker, $"{commands}/{freeze.CommandId}", expected: HttpStatusCode.NotFound, error: "FINANCE_CONTEXT_NOT_FOUND")) { }

        async Task<Guid> Submit(StandardBasisSubmissionV1 draft)
        {
            using var response = await Call(maker, basis, draft, HttpStatusCode.Created);
            return response.RootElement.GetProperty("versionId").GetGuid();
        }

        async Task Approve(Guid id)
        {
            using var response = await Call(checker, $"{basis}/{id}/review", review with { CommandId = Guid.NewGuid() });
            Assert.Equal("APPROVED", response.RootElement.GetProperty("state").GetString());
        }

        async Task Publish(Guid id)
        {
            var release = new StandardBasisReleaseV1(Guid.NewGuid(), 2);
            using var published = await Call(releaser, $"{basis}/{id}/publish", release);
            using var replay = await Call(releaser, $"{basis}/{id}/publish", release);
            Assert.Equal("PUBLISHED", published.RootElement.GetProperty("state").GetString());
            Assert.Equal(published.RootElement.ToString(), replay.RootElement.ToString());
        }

        Task SetBusinessDate(DateOnly date) => h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_fin_mfg_v1.ledgers SET business_date={date} WHERE (tenant_id,id)=({h.Tenant},{ledger})
            """);

        async Task<(Dictionary<string, string> Cookies, Guid? Context)> Session(string name, string ledgerKey = "MFG")
        {
            var cookies = name == "price-owner" ? await Login(host, h) : await Login(host, name, "Price1!Password", h.Tenant.ToString("N"));
            using var options = await Call((cookies, null), $"/api/fin/cost/v1/options?legalEntityKey=LE1&ledgerKey={ledgerKey}");
            return (cookies, options.RootElement.GetProperty("contextToken").GetGuid());
        }

        async Task<JsonDocument> Call((Dictionary<string, string> Cookies, Guid? Context) actor, string path, object? body = null,
            HttpStatusCode expected = HttpStatusCode.OK, string? error = null)
        {
            using var request = OperationsRequest(actor.Cookies, body == null ? HttpMethod.Get : HttpMethod.Post, path, body);
            if (actor.Context != null)
                request.Headers.Add("X-Finance-Context", actor.Context.Value.ToString());
            using var response = await host.Send(request);
            var content = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == expected, $"{path}: {response.StatusCode} {content}");
            if (error != null)
                Assert.Contains(error, content);
            return JsonDocument.Parse(content);
        }
    }
}
