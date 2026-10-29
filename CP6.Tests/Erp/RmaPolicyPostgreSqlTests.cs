using System.Data.Common;
using System.Net;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Wms;
using CP6.Tests.Platform;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static RmaPolicyPublishV1 PolicyInput(RmaFormalContextV1 context) => new(context.ScopeOptions[0].OptionRef, null,
        new("Customer returns", true, [new("DAMAGED", "Damaged goods"), new("WRONG", "Wrong product")], ["RETURN_ONLY", "REPLACEMENT"], 30, true), "Initial business policy");
    private static Task<int> PolicyCount(Harness h) => h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_rma_formal_v1.policy_versions WHERE tenant_id={h.Tenant}").SingleAsync();
    private async Task<(RmaFormalOwnerV1 Owner,RmaFormalContextV1 Context)> PolicySetup(Harness h)
    {
        await FormalShipment(h); await EngineeringAction(h, "wms-rma", "policy-publish");
        var owner = FormalRma(h); return (owner,await owner.ContextAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task RmaPolicy_explicit_publication_versions_disable_and_original_recovery_do_not_authorize_returns()
    {
        await using var h = await Seed(); var (o, context) = await PolicySetup(h); var input = PolicyInput(context);
        Assert.Contains("wms.rma.policy.publish", context.Capabilities);
        Assert.Equal(new RmaPolicyLookupV1("NOT_CONFIGURED", null), await o.ReadPolicyAsync(new(input.ScopeOptionRef, null)));
        var stock = await h.Db.Stocks.Select(x => x.PhysicalQty).SingleAsync(); var transactions = await h.Db.StockTransactions.CountAsync();
        var command = Guid.NewGuid(); var first = await o.PublishPolicyAsync(command, input);
        var second = await o.PublishPolicyAsync(Guid.NewGuid(), input with { ExpectedPolicyRef = first.Reference,
            Policy = input.Policy with { Enabled = false, ReturnWindowDaysAfterDelivery = null, RequireIndependentApprover = false }, Reason = "Disable" });
        Assert.Equal(first.Reference, second.PreviousRef); Assert.Equal("2", second.Reference.Version);
        Assert.Equal("DISABLED", (await o.ReadPolicyAsync(new(input.ScopeOptionRef, null))).State);
        Assert.Equal(FormalJson(first), FormalJson((await o.ReadPolicyAsync(new(input.ScopeOptionRef, first.Reference))).Value));
        Assert.Equal(FormalJson(first), FormalJson(await o.PublishPolicyAsync(command, input)));
        var query = await o.QueryPolicyCommandAsync(new(input.ScopeOptionRef, command)); Assert.Equal("FOUND", query.Status); Assert.Equal(FormalJson(first), FormalJson(query.Value));
        var absent = await o.QueryPolicyCommandAsync(new(input.ScopeOptionRef, Guid.NewGuid())); Assert.Equal("NOT_OBSERVED", absent.Status); Assert.False(absent.CanInferNoEffect); Assert.Null(absent.Value);
        Assert.Equal("CAS_CONFLICT", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.PublishPolicyAsync(Guid.NewGuid(), input))).Message);
        Assert.Equal("COMMAND_BINDING_CONFLICT", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.PublishPolicyAsync(command, input with { Reason = "Different request" }))).Message);
        Assert.Equal(2, await PolicyCount(h)); Assert.Equal(0, await FormalCount(h)); Assert.Equal(stock, await h.Db.Stocks.Select(x => x.PhysicalQty).SingleAsync()); Assert.Equal(transactions, await h.Db.StockTransactions.CountAsync());
        await Assert.ThrowsAsync<PostgresException>(() => h.Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM cp6_rma_formal_v1.policy_versions WHERE tenant_id={h.Tenant}"));
    }
    [CurrentPermissionPostgresFact]
    public async Task RmaPolicy_no_implicit_grants_and_current_field_scope_tenant_checks_apply_to_old_results()
    {
        await using var h = await Seed(); await FormalShipment(h); var o = FormalRma(h); var context = await o.ContextAsync(); var input = PolicyInput(context);
        Assert.DoesNotContain("wms.rma.policy.publish", context.Capabilities);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.PublishPolicyAsync(Guid.NewGuid(), input))).Status);
        await EngineeringAction(h, "wms-rma", "policy-publish"); var command = Guid.NewGuid(); var value = await o.PublishPolicyAsync(command, input);
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == 426 && x.ActionCode == "policy-publish").ExecuteDeleteAsync();
        Assert.Equal(FormalJson(value), FormalJson(await o.PublishPolicyAsync(command, input)));
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.PublishPolicyAsync(Guid.NewGuid(), input with { ExpectedPolicyRef = value.Reference }))).Status);
        h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = "wms-rma", FieldName = "ReturnReason", Access = 3 }); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        Assert.Equal("FIELD_READ_FORBIDDEN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.QueryPolicyCommandAsync(new(input.ScopeOptionRef, command)))).Message);
        await h.Db.Sys_RoleFieldPerms.Where(x => x.ResourceKey == "wms-rma" && x.FieldName == "ReturnReason").ExecuteDeleteAsync();
        await h.Db.WmsRoleScopes.ExecuteDeleteAsync(); Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.ReadPolicyAsync(new(input.ScopeOptionRef, value.Reference)))).Status);
        await using var other = await Seed(); var (foreign, otherContext) = await PolicySetup(other);
        Assert.Equal("NOT_OBSERVED", (await foreign.QueryPolicyCommandAsync(new(otherContext.ScopeOptions[0].OptionRef, command))).Status);
        Assert.Equal("RMA_POLICY_VERSION_NOT_FOUND", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => foreign.ReadPolicyAsync(new(otherContext.ScopeOptions[0].OptionRef, value.Reference)))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task RmaPolicy_invalid_policy_is_rejected_before_any_version_is_written()
    {
        await using var h = await Seed(); var (o, context) = await PolicySetup(h); var input = PolicyInput(context);
        foreach (var definition in new[] { input.Policy with { Name = " " }, input.Policy with { ReasonOptions = [] },
            input.Policy with { ReasonOptions = [new("D", "One"),new("D", "Two")] }, input.Policy with { AllowedOutcomes = ["UNKNOWN"] },
            input.Policy with { AllowedOutcomes = [] }, input.Policy with { ReturnWindowDaysAfterDelivery = 0 } })
            Assert.Equal("RMA_POLICY_INVALID", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.PublishPolicyAsync(Guid.NewGuid(), input with { Policy = definition }))).Message);
        Assert.Equal(0, await PolicyCount(h));
    }
    private sealed class FailRmaPolicyWrite : DbCommandInterceptor
    {
        public bool Reached;
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (command.CommandText.StartsWith("INSERT INTO cp6_rma_formal_v1.policy_versions", StringComparison.Ordinal))
            { Reached = true; throw new InvalidOperationException("INJECTED_POLICY_FAILURE"); }
            return ValueTask.FromResult(result);
        }
    }
    [CurrentPermissionPostgresFact]
    public async Task RmaPolicy_failure_rolls_back_head_version_actor_fact_and_original_command()
    {
        var fault = new FailRmaPolicyWrite(); await using var h = await Seed(fault); var (o, context) = await PolicySetup(h); var input = PolicyInput(context); var command = Guid.NewGuid();
        var facts = await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_rma_formal_v1.facts WHERE tenant_id={h.Tenant}").SingleAsync();
        Assert.Equal("INJECTED_POLICY_FAILURE", (await Assert.ThrowsAsync<InvalidOperationException>(() => o.PublishPolicyAsync(command, input))).Message);
        Assert.True(fault.Reached); Assert.Equal(0, await PolicyCount(h));
        Assert.Equal("NOT_CONFIGURED", (await o.ReadPolicyAsync(new(input.ScopeOptionRef, null))).State);
        Assert.Equal("NOT_OBSERVED", (await o.QueryPolicyCommandAsync(new(input.ScopeOptionRef, command))).Status);
        Assert.Equal(facts, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_rma_formal_v1.facts WHERE tenant_id={h.Tenant}").SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task RmaPolicy_lost_commit_ack_recovers_original_once_and_CAS_rejects_a_stale_head()
    {
        var fault = new CommitFault(); await using var h = await Seed(fault); var (o, context) = await PolicySetup(h); var input = PolicyInput(context); var command = Guid.NewGuid();
        fault.Armed = true;
        Assert.Equal("OPERATIONS_OUTCOME_UNKNOWN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.PublishPolicyAsync(command, input))).Message);
        Assert.Equal(1, await PolicyCount(h));
        var original = (await o.QueryPolicyCommandAsync(new(input.ScopeOptionRef, command))).Value!;
        Assert.Equal(FormalJson(original), FormalJson(await o.PublishPolicyAsync(command, input)));
        Assert.Equal("CAS_CONFLICT", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.PublishPolicyAsync(Guid.NewGuid(), input))).Message);
        Assert.Equal(1, await PolicyCount(h));
    }
    [CurrentPermissionPostgresFact]
    public async Task RmaPolicy_two_connections_competing_for_the_same_head_publish_one_version()
    {
        await using var h = await Seed(); await FormalShipment(h); await EngineeringAction(h, "wms-rma", "policy-publish");
        var keys = new EphemeralDataProtectionProvider(); var first = FormalRma(h, keys);
        var input = PolicyInput(await first.ContextAsync());
        await using var secondDb = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        var second = FormalRma(h with { Db = secondDb }, keys);
        async Task<(Guid Command, RmaPolicyVersionV1? Value, string? Error)> Publish(RmaFormalOwnerV1 owner)
        {
            var command = Guid.NewGuid();
            try { return (command, await owner.PublishPolicyAsync(command, input), null); }
            catch (OrderPriceCorrectionException e) { return (command, null, e.Message); }
        }
        var results = await Task.WhenAll(Publish(first), Publish(second));
        var winner = Assert.Single(results.Where(x => x.Value != null));
        var loser = Assert.Single(results.Where(x => x.Error != null)); Assert.Equal("CAS_CONFLICT", loser.Error);
        Assert.Equal(1, await PolicyCount(h));
        Assert.Equal(winner.Value!.Reference, (await first.ReadPolicyAsync(new(input.ScopeOptionRef, null))).Value!.Reference);
        Assert.Equal("FOUND", (await first.QueryPolicyCommandAsync(new(input.ScopeOptionRef, winner.Command))).Status);
        Assert.Equal("NOT_OBSERVED", (await first.QueryPolicyCommandAsync(new(input.ScopeOptionRef, loser.Command))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task RmaPolicy_actual_HTTP_publishes_reads_original_and_denies_stale_or_unknown_fields()
    {
        await using var h = await Seed(); var (_, context) = await PolicySetup(h); var coords = Assert.Single(FormalCoordinates(h).WarehouseSites);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, new Dictionary<string,string> {
            ["ScopeAuthorization__EnvironmentId"] = ScopeEnvironment.ToString(), ["QualitySpecification__EvidenceMode"] = "DEMO",
            ["RmaApplication__WarehouseSites__0__TenantId"] = h.Tenant.ToString(), ["RmaApplication__WarehouseSites__0__WarehouseId"] = coords.WarehouseId.ToString(), ["RmaApplication__WarehouseSites__0__SiteId"] = coords.SiteId.ToString() });
        var cookies = await Login(host,h);
        using var ctx = await host.Send(FormalRequest(cookies,HttpMethod.Get,"api/wms/rma/v1/context")); ctx.EnsureSuccessStatusCode();
        var actual = JsonSerializer.Deserialize<RmaFormalContextV1>(await ctx.Content.ReadAsStringAsync(), RmaJson)!;
        var input = PolicyInput(actual); var command = Guid.NewGuid(); const string root = "api/wms/rma/v1/policies";
        using var write = await host.Send(FormalRequest(cookies,HttpMethod.Post,root+"/publish",input,command)); Assert.True(write.IsSuccessStatusCode, await OperationsFailure(write));
        var original = await write.Content.ReadAsStringAsync();
        using var query = await host.Send(FormalRequest(cookies,HttpMethod.Post,root+"/commands/query",new RmaPolicyCommandQueryV1(input.ScopeOptionRef,command)));
        Assert.True(query.IsSuccessStatusCode,await OperationsFailure(query)); using var json = JsonDocument.Parse(await query.Content.ReadAsStringAsync()); Assert.Equal("FOUND",json.RootElement.GetProperty("status").GetString());
        Assert.Equal(JsonDocument.Parse(original).RootElement.GetProperty("reference").GetProperty("digest").GetString(),json.RootElement.GetProperty("value").GetProperty("reference").GetProperty("digest").GetString());
        using var stale = await host.Send(FormalRequest(cookies,HttpMethod.Post,root+"/publish",input,Guid.NewGuid())); Assert.Equal(HttpStatusCode.PreconditionFailed,stale.StatusCode);
        var malformed = JsonSerializer.SerializeToNode(input,RmaJson)!; malformed["approved"] = true;
        using var bad = await host.Send(FormalRequest(cookies,HttpMethod.Post,root+"/publish",malformed,Guid.NewGuid())); Assert.Equal(HttpStatusCode.BadRequest,bad.StatusCode);
    }
}
