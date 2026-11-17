using System.Net;
using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Fin;
using CP6.Core.Services.Mes;
using CP6.Core.Services.Oa;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task W699_native_finance_policy_bilateral_contract_and_cost_adoption_hold_current_PostgreSql_sources()
    {
        var hook = new StartWriteHook();
        await using var h = await Seed(hook);
        await GrantNativeCostCollect(h);
        var f = await QualitySetup(h);
        var quality = Specifications(h, f.Files, "REAL");
        await QualityReady(quality, f.Create);
        foreach (var actor in new[] { f.First, f.Second })
        {
            var q = Specifications(DecisionActor(h, actor), f.Files, "REAL");
            await q.CommandAsync(QualityCommand(await q.ReadAsync(f.Create.SpecificationId), "APPROVE"));
        }
        var qa = (await quality.ReadAsync(f.Create.SpecificationId)).Version.Reference;
        var number = await SeedNativeCostCollect(h);
        foreach (var action in new[] { "view", "cost-master-view", "qualification-admin", "policy-submit", "policy-review", "policy-release",
            "accounting-source-view", "accounting-policy-submit", "accounting-policy-review", "accounting-policy-release", "accounting-policy-revoke",
            "source-contract-submit", "source-contract-source-review", "source-contract-gl-review", "source-contract-release", "source-contract-revoke" })
            await GrantLegacyAuthorityAction(h,"fin-cost",action);
        var environment = ScopeFence(h).EnvironmentId.ToString("D");
        var ledger = Guid.NewGuid(); var other = Guid.NewGuid();
        var day = new DateOnly(2026,6,30); var next = day.AddDays(1);
        var order = await h.Db.WorkOrders.AsNoTracking().SingleAsync(w => w.WorkOrderNo == number);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_fin_mfg_v1.ledgers(tenant_id,id,environment_id,mode,legal_entity_key,ledger_key,currency,business_date)
            VALUES ({h.Tenant},{ledger},{environment},'REAL','LE1','COST-BOOK','CNY',{day}),
                ({h.Tenant},{other},{environment},'REAL','LE2','OTHER','CNY',{day});
            INSERT INTO cp6_fin_mfg_v1.members(tenant_id,ledger_id,subject_id)
            VALUES ({h.Tenant},{ledger},{h.User}),({h.Tenant},{ledger},{f.First}),({h.Tenant},{ledger},{f.Second}),({h.Tenant},{other},{h.User});
            INSERT INTO cp6_fin_mfg_v1.work_orders(tenant_id,work_order_id,ledger_id) VALUES ({h.Tenant},{order.Id},{ledger});
            """);
        var checker = DecisionActor(h,f.First); var publisher = DecisionActor(h,f.Second);
        ManufacturingFinanceScopeV1 Scope(Harness actor) => new(actor.Db,environment);
        FinancePolicyOwnerV1 Finance(Harness actor) => new(actor.Db,CostPolicyAuthority(actor),Scope(actor),Documents(actor,f.Files));
        CostPolicySourcesV1 Sources(Harness actor) => new(actor.Db,Specifications(actor,f.Files,"REAL"),Documents(actor,f.Files),[Finance(actor)],CostPolicyAuthority(actor));
        ManufacturingCostMasterOwnerV1 Masters(Harness actor) => new(actor.Db,CostPolicyAuthority(actor),Scope(actor),Sources(actor));
        async Task<Guid> Context(Harness actor, string entity="LE1",string book="COST-BOOK")
            => JsonSerializer.SerializeToElement(await new ManufacturingRateOwnerV1(actor.Db,CostPolicyAuthority(actor),Scope(actor))
                .OptionsAsync(entity,book,default)).GetProperty("contextToken").GetGuid();
        var makerToken=await Context(h); var checkerToken=await Context(checker); var publisherToken=await Context(publisher);
        var qualificationOwner = new ManufacturingQualificationOwnerV1(h.Db,CostPolicyAuthority(h),Scope(h));
        Guid policyQualification = default;
        foreach (var (actor,purpose) in new[] { (f.First,"accounting-policy-review"),(f.First,"source-contract-source-review"),
            (f.Second,"source-contract-gl-review"),(f.Second,"source-contract-release"),(f.Second,"accounting-policy-release"),
            (f.First,"policy-review"),(f.Second,"policy-release") })
        {
            var granted = await qualificationOwner.DecideAsync(makerToken,new(Guid.NewGuid(),actor,purpose,true,null,
                DateTimeOffset.UtcNow.AddDays(1),"模拟专业组织资格资料","独立授予精确专业资格"),default);
            if (purpose=="accounting-policy-review") policyQualification=granted.QualificationId;
        }
        var basis = new CostPolicyRefV1(f.Document.Reference.Owner,f.Document.Reference.Id.ToString("D"),
            f.Document.Reference.Version,f.Document.Reference.Digest,"FINANCE_POLICY_BASIS");
        // 封闭输入边界拒绝伪造服务端批准、重复属性和缺少显式 nullable 字段。
        var malformed = new FinancePolicySubmitV1(Guid.NewGuid(),null,"SOURCE_CONTRACT",
            JsonSerializer.SerializeToElement(new { sourceOwner="Finance Costing",sourceSchema="COST-GL-SOURCE-1",
                eventTypes=new[] {"COST_SETTLEMENT"},interval=new {from=day},requiredEvidenceKeys=Array.Empty<string>() }),basis,"边界验证");
        await Error("FINANCE_POLICY_INPUT_INVALID",()=>Finance(h).SubmitAsync(makerToken,malformed,default));
        var validJson=JsonSerializer.Serialize(new FinanceSourceContractInputV1("Finance Costing","COST-GL-SOURCE-1",
            ["COST_SETTLEMENT"],new(day,null),[]),FinancePolicyValuesV1.Wire);
        foreach (var (extra,code) in new[] { ("\"state\":\"APPROVED\",","FINANCE_POLICY_INPUT_INVALID"),
            ("\"SourceOwner\":\"FAKE\",","FINANCE_POLICY_INPUT_INVALID"),
            ("\"sourceOwner\":\"FAKE\",","FINANCE_POLICY_DUPLICATE_PROPERTY") })
        {
            using var bad=JsonDocument.Parse("{"+extra+validJson[1..]);
            await Error(code,()=>Finance(h).SubmitAsync(makerToken,malformed with {Definition=bad.RootElement.Clone()},default));
        }
        var firstContract=await PublishSource("SOURCE_CONTRACT",new FinanceSourceContractInputV1("Finance Costing","COST-GL-SOURCE-1",
            ["COST_SETTLEMENT"],new(day,next),["COST_VERSION","QUALITY"]));
        var secondContract=await PublishSource("SOURCE_CONTRACT",new FinanceSourceContractInputV1("Finance Costing","COST-GL-SOURCE-1",
            ["COST_SETTLEMENT"],new(next,null),["COST_VERSION","QUALITY"]),firstContract.Id);
        var cyclic=Policy(firstContract.Reference,day,next) with {
            LegRules=[new("A",["B"],basis,[],[]),new("B",["A"],basis,[],[])] };
        await Error("FINANCE_POLICY_INPUT_INVALID",()=>Finance(h).SubmitAsync(makerToken,malformed with {
            Kind="ACCOUNTING_POLICY",Definition=JsonSerializer.SerializeToElement(cyclic,FinancePolicyValuesV1.Wire)},default));
        var firstPolicy=await PublishSource("ACCOUNTING_POLICY",Policy(firstContract.Reference,day,next),failCommit:true);
        var secondPolicy=await PublishSource("ACCOUNTING_POLICY",Policy(secondContract.Reference,next,null),firstPolicy.Id);
        var otherToken=await Context(h,"LE2","OTHER");
        await Error("NOT_FOUND_OR_NOT_VISIBLE",() => Finance(h).ReadAsync(otherToken,firstPolicy.Id,default));

        var documentBasis=basis with { Purpose="COST_POLICY_BASIS" };
        var definition=new CostPolicyDefinitionV1("TEST-JURISDICTION","SIMULATED-STANDARD",4,"UNIT_PRICE","EXACT_TRACE","DIRECT_TRACE",
            "LAST_POSITIVE_WEIGHT","TRIAL_EXPENSE","REQUIRE_REVIEW","REQUIRED",documentBasis,["ACTUAL","STANDARD"],["LABEL"],
            documentBasis,documentBasis,null,firstPolicy.Reference,firstContract.Reference,documentBasis,
            [new("LABEL",new(qa.Owner,qa.Id.ToString("D"),qa.Version,qa.Digest,"QUALITY_REQUIREMENTS"))]);
        var input=new ManufacturingCostPolicySubmitV1(Guid.NewGuid(),null,day,next,2,"HALF_UP","APPROVED_ACCOUNTING_DATE",
            "STANDARD_OUTPUT_WITH_VARIANCE","VAR_TO_EXPENSE",false,"已批准原件","正式参与者采用",definition);
        await Error("FINANCE_POLICY_REFERENCE_CHANGED",()=>Masters(h).SubmitPolicyAsync(makerToken,input with {
            Definition=definition with {GlAccountingPolicyRef=firstPolicy.Reference with {Digest=new('b',64)}} },default));
        var mismatched=await Masters(h).SubmitPolicyAsync(makerToken,input with { Definition=definition with {SourceContractRef=secondContract.Reference}},default);
        await Error("FINANCE_POLICY_SOURCE_CONTRACT_MISMATCH",() => Masters(checker).ReviewAsync(checkerToken,"policies",
            mismatched.GetProperty("versionId").GetGuid(),new(Guid.NewGuid(),"1","不匹配的双方合同必须拒绝"),default));
        var firstCost=await Cost(input with {CommandId=Guid.NewGuid()});
        await SeedApprovedPolicyRates(h,number,ledger,f.First,f.Second,firstCost,day,next,100m);
        var collect=new CostCollectService(h.Db,new FinSequenceService(h.Db),new ProcessCostRateService(h.Db),CostPolicyAuthority(h),Scope(h),Sources(h));
        Assert.True((await collect.CollectAsync(number,0m,0m,"untrusted-label")).Ok);
        var sheet=await h.Db.CostSheets.AsNoTracking().SingleAsync(); Assert.Equal(213.35m,sheet.LaborActual);
        var original=await ReadW689Basis(h,sheet.Id,1); AssertOriginal(original,firstPolicy,firstContract);
        var secondCost=await Cost(input with {CommandId=Guid.NewGuid(),PreviousVersionId=firstCost,From=next,ToExclusive=null,
            Definition=definition with {GlAccountingPolicyRef=secondPolicy.Reference,SourceContractRef=secondContract.Reference}});
        await SeedApprovedPolicyRates(h,number,ledger,f.First,f.Second,secondCost,next,null,80m);
        await SetDay(next);
        Assert.True((await collect.CollectAsync(number,0m,0m,"untrusted-label")).Ok);
        Assert.Equal(170.88m,await h.Db.CostSheets.Where(s=>s.Id==sheet.Id).Select(s=>s.LaborActual).SingleAsync());
        AssertOriginal(await ReadW689Basis(h,sheet.Id,2),secondPolicy,secondContract);

        Task<JsonElement>? revoke=null;
        hook.Before=async command =>
        {
            if (!command.CommandText.Contains("INSERT INTO cp6_fin_cost_basis_v1.versions",StringComparison.Ordinal)) return;
            hook.Before=null;
            var application="w699-native-revoke-"+h.Tenant.ToString("N"); var started=new TaskCompletionSource();
            revoke=Task.Run(async()=>
            {
                await using var peer=server.Context(new TenantContext {CurrentTenantId=h.Tenant});
                await peer.Database.OpenConnectionAsync();
                await peer.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('application_name',{application},false)");
                started.SetResult();
                return await Finance(publisher with {Db=peer}).RevokeAsync(publisherToken,secondPolicy.Id,
                    new(Guid.NewGuid(),secondPolicy.Revision,"撤销真实会计政策"),default);
            });
            await started.Task; var waiting=false; var deadline=DateTime.UtcNow.AddSeconds(8);
            while (!waiting && DateTime.UtcNow<deadline)
            {
                waiting=await h.Db.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT FROM pg_stat_activity WHERE application_name={application} AND wait_event_type='Lock') AS \"Value\"").SingleAsync();
                if (!waiting) await Task.Delay(25);
            }
            Assert.True(waiting); Assert.False(revoke.IsCompleted);
        };
        Assert.True((await collect.CollectAsync(number,0m,0m,"untrusted-label")).Ok);
        Assert.NotNull(revoke); Assert.Equal("REVOKED",(await revoke!).GetProperty("state").GetString());
        await Error("FINANCE_POLICY_SOURCE_UNAVAILABLE",() => collect.CollectAsync(number,0m,0m,"untrusted-label"));
        Assert.Equal(original,await ReadW689Basis(h,sheet.Id,1));
        Assert.Equal(secondPolicy.Original,(await Finance(h).ReadAsync(makerToken,secondPolicy.Id,default)).GetProperty("originalJson").GetString());

        // 实际 Program/登录读取正式 Owner 原件和命令结果；只有参数/规则内容为模拟业务输入。
        var settings=await DocumentHostSettings(f.Files); settings["FinanceCosting__EnvironmentId"]=environment;
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,settings);
        var cookies=await Login(host,h);
        using (var response=await Http($"/api/fin/policy/owner-v1/{secondPolicy.Id:D}"))
        { Assert.Equal(HttpStatusCode.OK,response.StatusCode); Assert.Contains(secondPolicy.Reference.Digest,await response.Content.ReadAsStringAsync()); }
        using (var response=await Http($"/api/fin/policy/owner-v1/commands/{secondPolicy.Command:D}")) Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        await h.Db.Sys_RoleActions.Where(r=>r.ActionCode=="accounting-policy-release").ExecuteDeleteAsync();
        var history=await Finance(publisher).PublishAsync(publisherToken,secondPolicy.Id,new(secondPolicy.Command,"2","发布精确原件"),default);
        Assert.Equal(secondPolicy.Original,history.GetProperty("originalJson").GetString());
        await Error("FINANCE_COMMAND_CONFLICT",()=>Finance(publisher).PublishAsync(publisherToken,secondPolicy.Id,
            new(secondPolicy.Command,"2","相同键的不同请求"),default));
        await SetDay(day);
        await qualificationOwner.DecideAsync(makerToken,new(Guid.NewGuid(),f.First,"accounting-policy-review",false,policyQualification,null,
            "资格撤销资料","撤销原专业批准资格"),default);
        await Error("FINANCE_QUALIFICATION_REQUIRED",() => collect.CollectAsync(number,0m,0m,"untrusted-label"));
        await h.Db.Sys_RoleActions.Where(r=>r.ActionCode=="accounting-source-view").ExecuteDeleteAsync();
        using (var response=await Http($"/api/fin/policy/owner-v1/commands/{secondPolicy.Command:D}")) Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
        await Assert.ThrowsAsync<PostgresException>(()=>h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_fin_policy_v1.originals SET original=original WHERE tenant_id={h.Tenant}"));

        FinanceAccountingPolicyInputV1 Policy(CostPolicyRefV1 contract,DateOnly from,DateOnly? until)
            => new(["COST_SETTLEMENT"],new(from,until),string.Concat(Enumerable.Repeat("𠀀",128)),"SIMULATED-STANDARD",2,"CNY",2,12,"HALF_UP","LINE","REQUIRE_REVIEW",
                "OA_DECISION",new(new("TEST.OA.Policy","SIMULATED-APPROVAL","1"),"1",new("sha256","test-policy/1",new('a',64))),
                [new("WIP",[],basis,[basis],["COST_VERSION"]),new("FG",["WIP"],basis,[basis],["QUALITY"])],
                [new("PERIOD","PERIOD",basis,"REQUIRED",basis),new("BANK_RECON","BANK",basis,"NOT_APPLICABLE",basis),
                 new("BUDGET","BUDGET",basis,"NOT_APPLICABLE",basis),new("SOURCE","SOURCE",basis,"REQUIRED",basis)],contract,basis);
        async Task<NativeFinancePolicy> PublishSource(string kind,object body,Guid? previous=null,bool failCommit=false)
        {
            var command=new FinancePolicySubmitV1(Guid.NewGuid(),previous,kind,JsonSerializer.SerializeToElement(body,FinancePolicyValuesV1.Wire),basis,
                string.Concat(Enumerable.Repeat("𠀀",500)));
            var submitted=await Finance(h).SubmitAsync(makerToken,command,default); var id=submitted.GetProperty("id").GetGuid();
            await Error("FINANCE_REVIEW_INDEPENDENCE_REQUIRED",()=>Finance(h).ApproveAsync(makerToken,id,new(Guid.NewGuid(),"1","GL","不能自批"),default));
            var review=await Finance(checker).ApproveAsync(checkerToken,id,new(Guid.NewGuid(),"1",kind=="SOURCE_CONTRACT"?"SOURCE":"GL","独立专业复核"),default);
            if (kind=="SOURCE_CONTRACT")
            {
                await Error("FINANCE_REVIEW_INDEPENDENCE_REQUIRED",()=>Finance(checker).ApproveAsync(checkerToken,id,new(Guid.NewGuid(),"2","GL","同一人不得代表双方"),default));
                await Error("FINANCE_POLICY_STATE_CONFLICT",()=>Finance(publisher).PublishAsync(publisherToken,id,new(Guid.NewGuid(),"2","缺另一方批准"),default));
                review=await Finance(publisher).ApproveAsync(publisherToken,id,new(Guid.NewGuid(),"2","GL","GL 方独立批准合同"),default);
            }
            var release=new FinancePolicyControlV1(Guid.NewGuid(),review.GetProperty("revision").GetString()!,"发布精确原件");
            if (failCommit)
            {
                await h.Db.Database.ExecuteSqlRawAsync("""
                    CREATE FUNCTION cp6_fin_policy_v1.w699_fault() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'W699_FINAL_COMMIT_FAULT'; END; $$;
                    CREATE CONSTRAINT TRIGGER w699_fault AFTER INSERT ON cp6_fin_policy_v1.commands DEFERRABLE INITIALLY DEFERRED
                        FOR EACH ROW EXECUTE FUNCTION cp6_fin_policy_v1.w699_fault();
                    """);
                try
                {
                    await Error("OPERATIONS_OUTCOME_UNKNOWN",()=>Finance(publisher).PublishAsync(publisherToken,id,release,default));
                    Assert.Equal("NOT_OBSERVED",(await Finance(h).CommandAsync(makerToken,release.CommandId,default)).GetProperty("kind").GetString());
                    Assert.Equal("APPROVED",(await Finance(h).ReadAsync(makerToken,id,default)).GetProperty("state").GetString());
                }
                finally { await h.Db.Database.ExecuteSqlRawAsync("DROP TRIGGER w699_fault ON cp6_fin_policy_v1.commands; DROP FUNCTION cp6_fin_policy_v1.w699_fault();"); }
            }
            var published=await Finance(publisher).PublishAsync(publisherToken,id,release,default);
            var replay=await Finance(publisher).PublishAsync(publisherToken,id,release,default);
            Assert.Equal(ApprovalCanonicalV2.Bytes(published),ApprovalCanonicalV2.Bytes(replay));
            return new(id,published.GetProperty("reference").Deserialize<CostPolicyRefV1>(FinancePolicyValuesV1.Wire)!,
                published.GetProperty("originalJson").GetString()!,published.GetProperty("revision").GetString()!,release.CommandId);
        }
        async Task<Guid> Cost(ManufacturingCostPolicySubmitV1 command)
        {
            var submitted=await Masters(h).SubmitPolicyAsync(makerToken,command,default); var id=submitted.GetProperty("versionId").GetGuid();
            await Masters(checker).ReviewAsync(checkerToken,"policies",id,new(Guid.NewGuid(),"1","复核完整政策和正式外域原件"),default);
            await Masters(publisher).PublishAsync(publisherToken,"policies",id,new(Guid.NewGuid(),"2","发布成本政策"),default); return id;
        }
        Task SetDay(DateOnly value) => h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_fin_mfg_v1.ledgers SET business_date={value} WHERE (tenant_id,id)=({h.Tenant},{ledger})");
        Task<HttpResponseMessage> Http(string path)
        { var request=OperationsRequest(cookies,HttpMethod.Get,path); request.Headers.Add("X-Finance-Context",makerToken.ToString("D")); return host.Send(request); }
        static async Task Error(string code,Func<Task> work) => Assert.Equal(code,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(work)).Message);
        static void AssertOriginal(string snapshot,NativeFinancePolicy policy,NativeFinancePolicy contract)
        {
            Assert.All(JsonSerializer.Deserialize<CostCollectionBasisV1>(snapshot)!.Rates,rate=>
            {
                Assert.Equal(policy.Original,Assert.Single(rate.FullPolicy!.Sources,s=>s.Role=="GL_ACCOUNTING_POLICY").OriginalJson);
                Assert.Equal(contract.Original,Assert.Single(rate.FullPolicy.Sources,s=>s.Role=="SOURCE_CONTRACT").OriginalJson);
            });
        }
    }
    private sealed record NativeFinancePolicy(Guid Id,CostPolicyRefV1 Reference,string Original,string Revision,Guid Command);
}
