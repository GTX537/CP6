using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Fin;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Mes;
using CP6.Core.Services.Plm;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Fin;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static OperationsOwnerV1 CostPolicyAuthority(Harness h)
    {
        var catalog = new AuthorizationCatalogStoreV1();
        foreach (var key in new[] { "fin-cost", "mes-work-order", "product", "mes-process-cost-rate",
            "quality-specification", "plm-controlled-document", "file-content", "plm-engineering-revision",
            "plm-engineering-product", "space-site", "uom-profile" })
        {
            catalog.RegisterScope(key, key, [1, 2, 3, 4, 5], 5);
            catalog.RegisterFields(key, key switch
            {
                "quality-specification" => [new("Metadata", "元数据"), new("Requirements", "质量要求")],
                "file-content" => [new("Content", "内容"), new("FileName", "名称")],
                "plm-controlled-document" => [new("Content", "内容"), new("Metadata", "元数据")],
                "mes-process-cost-rate" => [new("LaborRate", "人工费率"), new("OverheadRate", "制造费率")],
                _ => [],
            });
        }
        catalog.Seal();
        return new(h.Db, new OperationsHttp { HttpContext = h.Http }, _ => Task.FromResult(h.User), catalog.Capture);
    }

    // GL 尚无 R2 原生参与者。仅在测试中安装此模拟 Owner；真实 PostgreSQL 行锁、原件及撤销状态。
    private sealed class PolicyGlSimulation : ICostPolicyExternalSourceV1
    {
        public string Owner => "TEST.Finance.GL";
        internal bool FailAtCommit;
        internal Func<Task>? AfterHold;
        public async Task<CostPolicySourceOriginalV1> ReadInUowAsync(CostPolicySourceContextV1 context,
            CostPolicyRefV1 reference, string role, CancellationToken ct)
        {
            Assert.NotNull(context.Database.Database.CurrentTransaction);
            var scope = JsonSerializer.Serialize(context.Scope, ManufacturingCostMasterStoreV1.Wire);
            var rows = await context.Database.Database.SqlQuery<PolicySimRow>($"""
                SELECT reference AS "Reference", scope AS "Scope", role AS "Role", original AS "Original", enabled AS "Enabled"
                FROM public.w698_policy_source WHERE tenant_id={context.Scope.TenantId} AND id={reference.Id}
                    AND version={reference.Version} AND scope::jsonb=CAST({scope} AS jsonb) FOR SHARE
                """).ToArrayAsync(ct);
            var row = rows.SingleOrDefault() ?? throw new OrderPriceCorrectionException("SIM_SOURCE_NOT_FOUND", 404);
            if (context.ForAdoption)
            {
                if (!row.Enabled) throw new OrderPriceCorrectionException("SIM_SOURCE_REVOKED", 422);
                context.AtCommit(_ => FailAtCommit
                    ? throw new OrderPriceCorrectionException("SIM_FINAL_SOURCE_REVOKED", 422) : Task.CompletedTask);
                if (AfterHold != null) { var callback = AfterHold; AfterHold = null; await callback(); }
            }
            return new(JsonSerializer.Deserialize<CostPolicyRefV1>(row.Reference, ManufacturingCostMasterStoreV1.Wire)!,
                JsonSerializer.Deserialize<FinanceScopeV1>(row.Scope, ManufacturingCostMasterStoreV1.Wire)!, row.Role, row.Original);
        }
    }

    private sealed class PolicySimRow
    {
        public string Reference { get; set; } = null!;
        public string Scope { get; set; } = null!;
        public string Role { get; set; } = null!;
        public string Original { get; set; } = null!;
        public bool Enabled { get; set; }
    }

    [CurrentPermissionPostgresFact]
    public async Task W698_full_policy_native_quality_external_originals_and_actual_PostgreSql_collection()
    {
        await using var h = await Seed();
        await GrantNativeCostCollect(h);
        var f = await QualitySetup(h);
        var quality = Specifications(h, f.Files, "REAL");
        await QualityReady(quality, f.Create);
        foreach (var actor in new[] { f.First, f.Second })
        {
            var q = Specifications(DecisionActor(h, actor), f.Files, "REAL");
            await q.CommandAsync(QualityCommand(await q.ReadAsync(f.Create.SpecificationId), "APPROVE"));
        }
        var approvedQuality = await quality.ReadAsync(f.Create.SpecificationId);
        var number = await SeedNativeCostCollect(h);
        foreach (var action in new[] { "view", "cost-master-view", "qualification-admin", "policy-submit", "policy-review", "policy-release" })
            await GrantLegacyAuthorityAction(h, "fin-cost", action);
        var environment = ScopeFence(h).EnvironmentId.ToString("D");
        var ledger = Guid.NewGuid();
        var day = new DateOnly(2026, 6, 30);
        var nextDay = day.AddDays(1);
        var order = await h.Db.WorkOrders.AsNoTracking().SingleAsync(row => row.WorkOrderNo == number);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_fin_mfg_v1.ledgers (tenant_id,id,environment_id,mode,legal_entity_key,ledger_key,currency,business_date)
            VALUES ({h.Tenant},{ledger},{environment},'REAL','LE1','COST-BOOK','CNY',{day});
            INSERT INTO cp6_fin_mfg_v1.members (tenant_id,ledger_id,subject_id)
            VALUES ({h.Tenant},{ledger},{h.User}),({h.Tenant},{ledger},{f.First}),({h.Tenant},{ledger},{f.Second});
            INSERT INTO cp6_fin_mfg_v1.work_orders (tenant_id,work_order_id,ledger_id) VALUES ({h.Tenant},{order.Id},{ledger});
            """);
        await h.Db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS public.w698_policy_source (
                tenant_id uuid NOT NULL,id text NOT NULL,version text NOT NULL,scope text NOT NULL,
                reference text NOT NULL,role text NOT NULL,original text NOT NULL,enabled boolean NOT NULL DEFAULT true,
                PRIMARY KEY(tenant_id,id,version));
            """);
        var financeScope = new FinanceScopeV1(environment, h.Tenant, "LE1", "COST-BOOK", "REAL");
        var simulation = new PolicyGlSimulation();
        CostPolicySourcesV1 Sources(Harness actor, bool install = true) => new(actor.Db,
            Specifications(actor, f.Files, "REAL"), Documents(actor, f.Files), install ? [simulation] : [], CostPolicyAuthority(actor));
        ManufacturingFinanceScopeV1 Scope(Harness actor) => new(actor.Db, environment);
        ManufacturingCostMasterOwnerV1 Masters(Harness actor, bool install = true)
            => new(actor.Db, CostPolicyAuthority(actor), Scope(actor), Sources(actor, install));
        var checker = DecisionActor(h, f.First);
        var publisher = DecisionActor(h, f.Second);
        async Task<Guid> Context(Harness actor)
        {
            var result = await new ManufacturingRateOwnerV1(actor.Db, CostPolicyAuthority(actor), Scope(actor))
                .OptionsAsync("LE1", "COST-BOOK", default);
            return JsonSerializer.SerializeToElement(result).GetProperty("contextToken").GetGuid();
        }
        var makerToken = await Context(h);
        var checkerToken = await Context(checker);
        var publisherToken = await Context(publisher);
        var qualifications = new ManufacturingQualificationOwnerV1(h.Db, CostPolicyAuthority(h), Scope(h));
        foreach (var (actor, purpose) in new[] { (f.First, "policy-review"), (f.Second, "policy-release") })
            await qualifications.DecideAsync(makerToken, new(Guid.NewGuid(), actor, purpose, true, null,
                DateTimeOffset.UtcNow.AddDays(1), "模拟专业资格原件", "独立签发"), default);

        var doc = new CostPolicyRefV1(f.Document.Reference.Owner, f.Document.Reference.Id.ToString("D"),
            f.Document.Reference.Version, f.Document.Reference.Digest, "COST_POLICY_BASIS");
        var qa = new CostPolicyRefV1(approvedQuality.Version.Reference.Owner, approvedQuality.Version.Reference.Id.ToString("D"),
            approvedQuality.Version.Reference.Version, approvedQuality.Version.Reference.Digest, "QUALITY_REQUIREMENTS");
        var gl1 = await Source("policy", "1", "GL_ACCOUNTING_POLICY", "第一代会计政策模拟原件");
        var gl2 = await Source("policy", "2", "GL_ACCOUNTING_POLICY", "第二代会计政策模拟原件");
        var contract = await Source("contract", "1", "SOURCE_CONTRACT", "成本来源与 GL 双方合同模拟原件");
        var definition = new CostPolicyDefinitionV1("TEST-JURISDICTION", "SIMULATED-STANDARD", 4, "UNIT_PRICE", "EXACT_TRACE",
            "DIRECT_TRACE", "LAST_POSITIVE_WEIGHT", "TRIAL_EXPENSE", "REQUIRE_REVIEW", "REQUIRED", doc,
            ["ACTUAL", "STANDARD"], ["LABEL"], doc, doc, null, gl1, contract, doc, [new("LABEL", qa)]);
        var input = new ManufacturingCostPolicySubmitV1(Guid.NewGuid(), null, day, nextDay, 2, "HALF_UP",
            "APPROVED_ACCOUNTING_DATE", "STANDARD_OUTPUT_WITH_VARIANCE", "VAR_TO_EXPENSE", false, "受控原件", "完整政策", definition);
        await Error("COST_POLICY_SOURCE_OWNER_UNAVAILABLE", () => Masters(h, false).SubmitPolicyAsync(makerToken, input, default));
        await Error("COST_POLICY_DEFINITION_INVALID", () => Masters(h).SubmitPolicyAsync(makerToken,
            input with { Definition = definition with { QualitySources = [] } }, default));
        await Error("COST_POLICY_SOURCE_IDENTITY_MISMATCH", () => Masters(h).SubmitPolicyAsync(makerToken,
            input with { Definition = definition with { GlAccountingPolicyRef = gl1 with { Digest = new('a', 64) } } }, default));
        var first = await Publish(input, true);
        await Error("COST_POLICY_CONTRACT_DOWNGRADE", () => Masters(h).SubmitPolicyAsync(makerToken,
            input with { CommandId = Guid.NewGuid(), PreviousVersionId = first, From = nextDay,
                ToExclusive = null, Definition = null }, default));
        await Rates(first, day, nextDay, 100m);
        var collect = new CostCollectService(h.Db, new FinSequenceService(h.Db), new ProcessCostRateService(h.Db),
            CostPolicyAuthority(h), Scope(h), Sources(h));
        Assert.True((await collect.CollectAsync(number, 0m, 0m, "ignored-client-name")).Ok);
        var sheet = await h.Db.CostSheets.AsNoTracking().SingleAsync();
        Assert.Equal(213.35m, sheet.LaborActual);
        var before = await ReadW689Basis(h, sheet.Id, 1);
        AssertPolicy(before, "1", gl1, approvedQuality.Version.OriginalJson);
        await h.Db.WorkOrderProcesses.Where(row => row.WorkOrderNo == number && row.ProcessCd == "PRINT")
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.ActualLaborHour, (decimal?)null));
        var missingHours = await collect.CollectAsync(number, 0m, 0m, "ignored-client-name");
        Assert.False(missingHours.Ok);
        Assert.Equal("COST_ACTUAL_HOURS_REQUIRED", missingHours.Code);
        Assert.Equal(1, await VersionCount());
        await h.Db.WorkOrderProcesses.Where(row => row.WorkOrderNo == number && row.ProcessCd == "PRINT")
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.ActualLaborHour, 2.12345678m));
        var next = await Publish(input with { CommandId = Guid.NewGuid(), PreviousVersionId = first,
            From = nextDay, ToExclusive = null, Definition = definition with { GlAccountingPolicyRef = gl2 } });
        await Rates(next, nextDay, null, 80m);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_fin_mfg_v1.ledgers SET business_date={nextDay} WHERE (tenant_id,id)=({h.Tenant},{ledger})
            """);
        simulation.FailAtCommit = true;
        await Error("SIM_FINAL_SOURCE_REVOKED", () => collect.CollectAsync(number, 0m, 0m, "ignored-client-name"));
        simulation.FailAtCommit = false;
        Assert.Equal(1, await VersionCount());
        Assert.Equal(213.35m, await h.Db.CostSheets.Where(row => row.Id == sheet.Id).Select(row => row.LaborActual).SingleAsync());
        Assert.True((await collect.CollectAsync(number, 0m, 0m, "ignored-client-name")).Ok);
        Assert.Equal(170.88m, await h.Db.CostSheets.Where(row => row.Id == sheet.Id).Select(row => row.LaborActual).SingleAsync());
        AssertPolicy(await ReadW689Basis(h, sheet.Id, 2), "2", gl2, approvedQuality.Version.OriginalJson);
        Assert.Equal(before, await ReadW689Basis(h, sheet.Id, 1));

        // 真实 PostgreSQL 撤销 writer 必须等实际归集持有的来源行锁，提交后的下一次归集拒绝。
        Task? revocation = null;
        simulation.AfterHold = async () =>
        {
            var application = "w698-revoke-" + h.Tenant.ToString("N");
            var started = new TaskCompletionSource();
            revocation = Task.Run(async () =>
            {
                await using var conn = new NpgsqlConnection(h.Db.Database.GetConnectionString());
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT set_config('application_name',@application,false)";
                cmd.Parameters.AddWithValue("application", application); await cmd.ExecuteNonQueryAsync();
                cmd.CommandText = "UPDATE public.w698_policy_source SET enabled=false WHERE tenant_id=@tenant AND id='policy' AND version='2'";
                cmd.Parameters.AddWithValue("tenant", h.Tenant); started.SetResult(); await cmd.ExecuteNonQueryAsync();
            });
            await started.Task;
            var waiting = false; var until = DateTime.UtcNow.AddSeconds(8);
            while (!waiting && DateTime.UtcNow < until)
            {
                waiting = await h.Db.Database.SqlQuery<bool>($"""
                    SELECT EXISTS(SELECT FROM pg_stat_activity WHERE application_name={application} AND wait_event_type='Lock') AS "Value"
                    """).SingleAsync();
                if (!waiting) await Task.Delay(25);
            }
            Assert.True(waiting); Assert.False(revocation.IsCompleted);
        };
        Assert.True((await collect.CollectAsync(number, 0m, 0m, "ignored-client-name")).Ok);
        await revocation!;
        await Error("SIM_SOURCE_REVOKED", () => collect.CollectAsync(number, 0m, 0m, "ignored-client-name"));
        Assert.Equal(3, await VersionCount());
        var history = await Masters(h).ReadAsync(makerToken, first, default);
        Assert.Contains("COST-POLICY-1", JsonSerializer.Serialize(history));
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE public.w698_policy_source SET enabled=true WHERE tenant_id={h.Tenant}
            """);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_fin_mfg_v1.qualifications SET enabled=false
            WHERE tenant_id={h.Tenant} AND ledger_id={ledger} AND action='policy-review'
            """);
        await Error("FINANCE_QUALIFICATION_REQUIRED", () => collect.CollectAsync(number, 0m, 0m, "ignored-client-name"));
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_fin_mfg_v1.qualifications SET enabled=true
            WHERE tenant_id={h.Tenant} AND ledger_id={ledger} AND action='policy-review'
            """);
        await quality.CommandAsync(QualityCommand(await quality.ReadAsync(f.Create.SpecificationId), "WITHDRAW"));
        await Error("QUALITY_SPEC_SOURCE_UNAVAILABLE", () => collect.CollectAsync(number, 0m, 0m, "ignored-client-name"));
        Assert.Equal(before, await ReadW689Basis(h, sheet.Id, 1));
        h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = "quality-specification", FieldName = "Requirements", Access = 3 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        await Error("FIELD_READ_FORBIDDEN", () => Masters(h).ReadAsync(makerToken, first, default));
        await Assert.ThrowsAsync<PostgresException>(() => h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_fin_mfg_v1.policy_originals SET original=original WHERE tenant_id={h.Tenant}
            """));

        async Task<CostPolicyRefV1> Source(string id, string version, string role, string description)
        {
            var body = JsonSerializer.Serialize(new { scope = financeScope, id, version, role, description });
            var reference = new CostPolicyRefV1(simulation.Owner, id, version,
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(body))).ToLowerInvariant(), role);
            var scopeJson = JsonSerializer.Serialize(financeScope, ManufacturingCostMasterStoreV1.Wire);
            var refJson = JsonSerializer.Serialize(reference, ManufacturingCostMasterStoreV1.Wire);
            await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO public.w698_policy_source (tenant_id,id,version,scope,reference,role,original)
                VALUES ({h.Tenant},{id},{version},{scopeJson},{refJson},{role},{body})
                """);
            return reference;
        }
        async Task<Guid> Publish(ManufacturingCostPolicySubmitV1 command, bool fail = false)
        {
            var submitted = await Masters(h).SubmitPolicyAsync(makerToken, command, default);
            var id = submitted.GetProperty("versionId").GetGuid();
            await Masters(checker).ReviewAsync(checkerToken, "policies", id, new(Guid.NewGuid(), "1", "独立复核完整原件"), default);
            var release = new ManufacturingCostMasterDecisionV1(Guid.NewGuid(), "2", "发布完整原件");
            if (fail)
            {
                simulation.FailAtCommit = true;
                await Error("SIM_FINAL_SOURCE_REVOKED", () => Masters(publisher).PublishAsync(publisherToken, "policies", id, release, default));
                simulation.FailAtCommit = false;
                Assert.False(await h.Db.Database.SqlQuery<bool>($"""
                    SELECT EXISTS(SELECT FROM cp6_fin_mfg_v1.policy_originals WHERE tenant_id={h.Tenant}) AS "Value"
                    """).SingleAsync());
            }
            var result = await Masters(publisher).PublishAsync(publisherToken, "policies", id, release, default);
            var replay = await Masters(publisher).PublishAsync(publisherToken, "policies", id, release, default);
            Assert.Equal(FinanceCostCommandCanonicalV1.Bytes(result), FinanceCostCommandCanonicalV1.Bytes(replay));
            return id;
        }
        Task Rates(Guid policy, DateOnly from, DateOnly? until, decimal labor)
            => SeedApprovedPolicyRates(h, number, ledger, f.First, f.Second, policy, from, until, labor);
        Task<int> VersionCount() => h.Db.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM cp6_fin_cost_basis_v1.versions WHERE tenant_id={h.Tenant}
            """).SingleAsync();
        static async Task Error(string code, Func<Task> act)
            => Assert.Equal(code, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(act)).Message);
        static void AssertPolicy(string json, string version, CostPolicyRefV1 gl, string qualityOriginal)
        {
            var snapshot = JsonSerializer.Deserialize<CostCollectionBasisV1>(json)!;
            Assert.All(snapshot.Rates, rate =>
            {
                var original = Assert.IsType<CostPolicyAdoptionV1>(rate.FullPolicy);
                Assert.Equal("COST-POLICY-1", original.Policy.Schema);
                Assert.Equal(version, original.Policy.Version); Assert.Equal(gl, original.Policy.GlAccountingPolicyRef);
                Assert.Equal(qualityOriginal, Assert.Single(original.Sources, s => s.Role == "QUALITY:LABEL").OriginalJson);
                Assert.Equal(gl, Assert.Single(original.Sources, s => s.Role == "GL_ACCOUNTING_POLICY").Reference);
                Assert.NotEmpty(original.QualificationOriginal);
                using var wire = JsonDocument.Parse(original.PolicyOriginalJson);
                Assert.Equal("COST-POLICY-1", wire.RootElement.GetProperty("schema").GetString());
                Assert.Equal(version, wire.RootElement.GetProperty("version").GetString());
            });
        }
    }
    private static async Task SeedApprovedPolicyRates(Harness h, string number, Guid ledger, Guid reviewer, Guid publisher,
        Guid policy, DateOnly from, DateOnly? until, decimal labor)
    {
        var order = await h.Db.WorkOrders.AsNoTracking().SingleAsync(row => row.WorkOrderNo == number);
            // 工序/费率是显式模拟上游批准输入；政策的送审、独立批准、发布及成本归集由真实 Owner 完成。
            var steps = await (from p in h.Db.ProductProcesses where p.ProductCd == order.ProductCd
                join s in h.Db.WorkOrderProcesses.Where(s => s.WorkOrderNo == number) on p.ProcessCd equals s.ProcessCd
                join c in h.Db.WorkCenters on s.WgCd equals c.WgCd select new { Operation = p.Id, Center = c.Id, p.ProcessCd }).ToArrayAsync();
            foreach (var step in steps)
            {
                var center = new CostCenter { Code = "W698-" + Guid.NewGuid().ToString("N")[..12], Name = "模拟批准中心", Type = CostCenterType.Process };
                h.Db.CostCenters.Add(center); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
                var edition = Guid.NewGuid(); var rate = Guid.NewGuid(); var basis = Guid.NewGuid();
                var actualRate = step.ProcessCd == "PRINT" ? labor : 10.00000001m;
                await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO cp6_fin_mfg_v1.center_versions (tenant_id,ledger_id,root_id,version_id,version,code,name,valid_from,to_exclusive,approval_id)
                    VALUES ({h.Tenant},{ledger},{center.Id},{edition},1,{center.Code},{center.Name},{from},{until},{Guid.NewGuid()});
                    INSERT INTO cp6_fin_mfg_v1.rate_bases (tenant_id,ledger_id,id,policy_version_id,approval_id,work_center_id,operation_id,valid_from,to_exclusive)
                    VALUES ({h.Tenant},{ledger},{basis},{policy},{Guid.NewGuid()},{step.Center},{step.Operation},{from},{until});
                    INSERT INTO cp6_fin_mfg_v1.rate_heads (tenant_id,ledger_id,work_center_id,operation_id)
                    VALUES ({h.Tenant},{ledger},{step.Center},{step.Operation}) ON CONFLICT DO NOTHING;
                    INSERT INTO cp6_fin_mfg_v1.rate_versions (tenant_id,ledger_id,id,work_center_id,operation_id,center_id,center_version_id,
                        policy_version_id,basis_id,labor_rate,overhead_rate,valid_from,to_exclusive,maker,state,revision,reviewer,
                        review_qualification_id,publisher,release_qualification_id,submitted_at,reviewed_at,review_original_actor)
                    VALUES ({h.Tenant},{ledger},{rate},{step.Center},{step.Operation},{center.Id},{edition},{policy},{basis},{actualRate},3,
                        {from},{until},{h.User},'RELEASED',4,{reviewer},{Guid.NewGuid()},{publisher},{Guid.NewGuid()},clock_timestamp(),clock_timestamp(),true);
                    INSERT INTO cp6_fin_mfg_v1.timeline (tenant_id,ledger_id,work_center_id,operation_id,rate_id,valid_from,to_exclusive)
                    VALUES ({h.Tenant},{ledger},{step.Center},{step.Operation},{rate},{from},{until});
                    """);
            }
    }

}
