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
    private async Task<(RmaFormalOwnerV1 Owner, RmaDeliveryEvidenceRecordV1 Input)> DeliveryEvidenceSetup(Harness h, IDataProtectionProvider? keys = null)
    {
        await FormalShipment(h);
        foreach (var action in new[] { "evidence-record", "evidence-withdraw" }) await EngineeringAction(h, "wms-rma", action);
        byte[] bytes = [65,66,67]; var upload = (await ContentInput(h, bytes)) with { FileName = "SIMULATED-POD.txt" };
        var file = ContentOwner(h, new ContentStore(), new ContentInspector()); await file.UploadAsync(upload, new MemoryStream(bytes));
        var saved = (await file.ReadAsync(upload.FileId)).ContentRef;
        var owner = FormalRma(h, keys); var context = await owner.ContextAsync(); var original = (await owner.SourcesAsync(FormalSearch(context))).Items[0].Original;
        return (owner, new(context.ScopeOptions[0].OptionRef, original.ShipmentRef, Guid.Parse(original.ShipmentLineId), Guid.NewGuid(),
            "SIMULATED-POD-001", DateTimeOffset.UtcNow.AddMinutes(-1), [new("0", "1.12345678")], [new(saved.Owner, saved.Id.ToString(), saved.Version, saved.Digest)], "Simulated customer receipt"));
    }
    private static RmaDeliveryEvidenceQueryV1 DeliveryQuery(RmaDeliveryEvidenceRecordV1 input, int page = 1, int size = 20) => new(input.ScopeOptionRef, input.ShipmentRef, input.ShipmentLineId, page, size);
    private static Task<int> DeliveryCount(Harness h) => h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_rma_formal_v1.delivery_evidence WHERE tenant_id={h.Tenant}").SingleAsync();

    [CurrentPermissionPostgresFact]
    public async Task RmaDeliveryEvidence_simulated_receipt_preserves_exact_source_quantity_files_history_and_original_commands()
    {
        await using var h = await Seed(); var (o, input) = await DeliveryEvidenceSetup(h); var command = Guid.NewGuid();
        var stock = await h.Db.Stocks.Select(x => x.PhysicalQty).SingleAsync(); var transactions = await h.Db.StockTransactions.CountAsync();
        var first = await o.RecordDeliveryEvidenceAsync(command, input);
        Assert.Equal("1.12345678", first.Evidence.ReportedQuantity.Value); Assert.Equal(input.ShipmentRef, first.Evidence.Original.ShipmentRef);
        Assert.Null(first.Evidence.Original.DeliveryProofRef); Assert.Equal(input.FileRefs, first.Evidence.FileRefs);
        Assert.Equal(FormalJson(first), FormalJson(await o.RecordDeliveryEvidenceAsync(command, input)));
        Assert.Equal(FormalJson(first), FormalJson(await o.RecordDeliveryEvidenceAsync(Guid.NewGuid(), input)));
        Assert.Equal(1, await DeliveryCount(h));
        var withdrawalCommand = Guid.NewGuid(); var withdrawal = new RmaDeliveryEvidenceWithdrawV1(input.ScopeOptionRef, first.Evidence.Reference, "Simulated correction: wrong receipt");
        var withdrawn = await o.WithdrawDeliveryEvidenceAsync(withdrawalCommand, withdrawal);
        Assert.Equal(FormalJson(first.Evidence), FormalJson(withdrawn.Evidence)); Assert.Equal(withdrawal.Reason, withdrawn.Withdrawal!.Reason);
        Assert.Equal(FormalJson(withdrawn), FormalJson(await o.WithdrawDeliveryEvidenceAsync(withdrawalCommand, withdrawal)));
        Assert.Equal(FormalJson(first), FormalJson((await o.QueryDeliveryEvidenceCommandAsync(new(input.ScopeOptionRef, command))).Value));
        var page = await o.DeliveryEvidenceAsync(DeliveryQuery(input)); Assert.False(page.EstablishesDeliveryProof); Assert.Equal("COLLECTED_EVIDENCE_ONLY", page.Coverage);
        Assert.Equal(FormalJson(withdrawn), FormalJson(Assert.Single(page.Items)));
        Assert.NotNull((await o.RecordDeliveryEvidenceAsync(Guid.NewGuid(), input)).Withdrawal);
        Assert.Equal("DELIVERY_EVIDENCE_ID_CONFLICT", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.RecordDeliveryEvidenceAsync(Guid.NewGuid(), input with { DocumentReference = "Different" }))).Message);
        Assert.Equal("COMMAND_BINDING_CONFLICT", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.RecordDeliveryEvidenceAsync(command, input with { Notes = "Different" }))).Message);
        Assert.Equal(stock, await h.Db.Stocks.Select(x => x.PhysicalQty).SingleAsync()); Assert.Equal(transactions, await h.Db.StockTransactions.CountAsync()); Assert.Equal(0, await FormalCount(h));
        await Assert.ThrowsAsync<PostgresException>(() => h.Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM cp6_rma_formal_v1.delivery_evidence WHERE tenant_id={h.Tenant}"));
    }
    [CurrentPermissionPostgresFact]
    public async Task RmaDeliveryEvidence_current_authority_controls_registration_original_disclosure_and_file_picker()
    {
        await using var h = await Seed(); var (o, input) = await DeliveryEvidenceSetup(h); var command = Guid.NewGuid();
        var files = await o.DeliveryEvidenceFilesAsync(new(input.ScopeOptionRef, null, 1)); Assert.Contains(files.Items, x => x.Reference == input.FileRefs[0]);
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == 426 && x.ActionCode == "evidence-record").ExecuteDeleteAsync();
        Assert.DoesNotContain("wms.rma.evidence.record", (await o.ContextAsync()).Capabilities);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.RecordDeliveryEvidenceAsync(command, input))).Status);
        await EngineeringAction(h, "wms-rma", "evidence-record"); var saved = await o.RecordDeliveryEvidenceAsync(command, input);
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == 426 && x.ActionCode == "evidence-record").ExecuteDeleteAsync();
        Assert.Equal(FormalJson(saved), FormalJson(await o.RecordDeliveryEvidenceAsync(command, input)));
        h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = "wms-rma", FieldName = "Remarks", Access = 3 }); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        Assert.Equal("FIELD_READ_FORBIDDEN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.QueryDeliveryEvidenceCommandAsync(new(input.ScopeOptionRef, command)))).Message);
        await h.Db.Sys_RoleFieldPerms.Where(x => x.ResourceKey == "wms-rma" && x.FieldName == "Remarks").ExecuteDeleteAsync();
        var menu = await h.Db.Sys_Menus.Where(x => x.MenuKey == "file-content").Select(x => x.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == menu && x.ActionCode == "query").ExecuteDeleteAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.DeliveryEvidenceAsync(DeliveryQuery(input)))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.DeliveryEvidenceFilesAsync(new(input.ScopeOptionRef, null, 1)))).Status);
        await using var other = await Seed(); var (foreign, otherInput) = await DeliveryEvidenceSetup(other);
        Assert.Equal("DELIVERY_EVIDENCE_NOT_FOUND", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => foreign.WithdrawDeliveryEvidenceAsync(Guid.NewGuid(), new(otherInput.ScopeOptionRef, saved.Evidence.Reference, "Foreign")))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task RmaDeliveryEvidence_external_ranges_source_and_file_versions_fail_without_writing()
    {
        await using var h = await Seed(); var (o, input) = await DeliveryEvidenceSetup(h);
        foreach (var ranges in new RmaDeliveryRangeV1[][] { [new("1", "1")], [new("-1", "1")], [new("0", "0.123456789")], [new("0", "1"), new("0.5", "2")] })
            Assert.Equal("DELIVERY_EVIDENCE_RANGE_INVALID", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.RecordDeliveryEvidenceAsync(Guid.NewGuid(), input with { Ranges = ranges }))).Message);
        Assert.Equal("DELIVERY_EVIDENCE_OUTSIDE_SHIPMENT", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.RecordDeliveryEvidenceAsync(Guid.NewGuid(), input with { Ranges = [new("0", "9999999999999")] }))).Message);
        Assert.Equal("SOURCE_VERSION_STALE", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.RecordDeliveryEvidenceAsync(Guid.NewGuid(), input with { ShipmentRef = input.ShipmentRef with { Digest = new string('b', 64) } }))).Message);
        Assert.Equal("EVIDENCE_REFERENCE_CHANGED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.RecordDeliveryEvidenceAsync(Guid.NewGuid(), input with { FileRefs = [input.FileRefs[0] with { Version = "99" }] }))).Message);
        Assert.Equal("DELIVERY_EVIDENCE_TIME_INVALID", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.RecordDeliveryEvidenceAsync(Guid.NewGuid(), input with { OccurredAt = DateTimeOffset.UtcNow.AddDays(1) }))).Message);
        Assert.Equal(0, await DeliveryCount(h));
        var missing = await o.QueryDeliveryEvidenceCommandAsync(new(input.ScopeOptionRef, Guid.NewGuid())); Assert.Equal("NOT_OBSERVED", missing.Status); Assert.False(missing.CanInferNoEffect);
    }
    private sealed class FailDeliveryEvidenceJournal : DbCommandInterceptor
    {
        public bool Armed, Reached;
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (Armed && command.CommandText.Contains("INSERT INTO cp6_main_owner_v1.operations", StringComparison.Ordinal)) { Reached = true; throw new InvalidOperationException("INJECTED_DELIVERY_JOURNAL_FAILURE"); }
            return ValueTask.FromResult(result);
        }
    }
    [CurrentPermissionPostgresFact]
    public async Task RmaDeliveryEvidence_held_attachment_blocks_new_registration_but_preserves_history_and_withdrawal()
    {
        await using var h = await Seed(); var (o, input) = await DeliveryEvidenceSetup(h); var saved = await o.RecordDeliveryEvidenceAsync(Guid.NewGuid(), input);
        var file = ContentOwner(h, new ContentStore(), new ContentInspector()); var fileId = Guid.Parse(input.FileRefs[0].Id);
        await file.CommandAsync(ContentCommand(await file.ReadAsync(fileId), "HOLD"));
        Assert.Equal("HELD", Assert.Single((await o.DeliveryEvidenceFilesAsync(new(input.ScopeOptionRef, "SIMULATED-POD", 1))).Items).Availability);
        Assert.Equal("FILE_CONTENT_NOT_AVAILABLE", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.RecordDeliveryEvidenceAsync(Guid.NewGuid(), input with { EvidenceId = Guid.NewGuid() }))).Message);
        Assert.Equal(saved.Evidence.Reference, Assert.Single((await o.DeliveryEvidenceAsync(DeliveryQuery(input))).Items).Evidence.Reference);
        Assert.NotNull((await o.WithdrawDeliveryEvidenceAsync(Guid.NewGuid(), new(input.ScopeOptionRef, saved.Evidence.Reference, "Attachment held"))).Withdrawal);
        await h.Db.WmsRoleScopes.ExecuteDeleteAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.DeliveryEvidenceAsync(DeliveryQuery(input)))).Status);
        Assert.Equal(1, await DeliveryCount(h));
    }
    [CurrentPermissionPostgresFact]
    public async Task RmaDeliveryEvidence_failure_after_record_insert_rolls_back_evidence_and_original_together()
    {
        var fault = new FailDeliveryEvidenceJournal(); await using var h = await Seed(fault); var (o, input) = await DeliveryEvidenceSetup(h); var command = Guid.NewGuid(); fault.Armed = true;
        Assert.Equal("INJECTED_DELIVERY_JOURNAL_FAILURE", (await Assert.ThrowsAsync<InvalidOperationException>(() => o.RecordDeliveryEvidenceAsync(command, input))).Message);
        Assert.True(fault.Reached); Assert.Equal(0, await DeliveryCount(h)); Assert.Equal("NOT_OBSERVED", (await o.QueryDeliveryEvidenceCommandAsync(new(input.ScopeOptionRef, command))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task RmaDeliveryEvidence_lost_commit_ack_recovers_the_original_record_and_withdrawal_once()
    {
        var fault = new CommitFault(); await using var h = await Seed(fault); var (o, input) = await DeliveryEvidenceSetup(h); var command = Guid.NewGuid(); fault.Armed = true;
        Assert.Equal("OPERATIONS_OUTCOME_UNKNOWN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.RecordDeliveryEvidenceAsync(command, input))).Message);
        var original = (await o.QueryDeliveryEvidenceCommandAsync(new(input.ScopeOptionRef, command))).Value!;
        Assert.Equal(FormalJson(original), FormalJson(await o.RecordDeliveryEvidenceAsync(command, input))); Assert.Equal(1, await DeliveryCount(h));
        var withdraw = new RmaDeliveryEvidenceWithdrawV1(input.ScopeOptionRef, original.Evidence.Reference, "Correction"); var withdrawalCommand = Guid.NewGuid(); fault.Armed = true;
        Assert.Equal("OPERATIONS_OUTCOME_UNKNOWN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.WithdrawDeliveryEvidenceAsync(withdrawalCommand, withdraw))).Message);
        var recovered = (await o.QueryDeliveryEvidenceCommandAsync(new(input.ScopeOptionRef, withdrawalCommand))).Value!;
        Assert.Equal(FormalJson(recovered), FormalJson(await o.WithdrawDeliveryEvidenceAsync(withdrawalCommand, withdraw)));
        Assert.Equal("DELIVERY_EVIDENCE_ALREADY_WITHDRAWN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.WithdrawDeliveryEvidenceAsync(Guid.NewGuid(), withdraw))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task RmaDeliveryEvidence_two_connections_record_one_client_identity_and_preserve_independent_observations()
    {
        await using var h = await Seed(); var keys = new EphemeralDataProtectionProvider(); var (first, input) = await DeliveryEvidenceSetup(h, keys);
        await using var secondDb = server.Context(new TenantContext { CurrentTenantId = h.Tenant }); var second = FormalRma(h with { Db = secondDb }, keys);
        var results = await Task.WhenAll(first.RecordDeliveryEvidenceAsync(Guid.NewGuid(), input), second.RecordDeliveryEvidenceAsync(Guid.NewGuid(), input));
        Assert.Equal(FormalJson(results[0]), FormalJson(results[1])); Assert.Equal(1, await DeliveryCount(h));
        await first.RecordDeliveryEvidenceAsync(Guid.NewGuid(), input with { EvidenceId = Guid.NewGuid(), DocumentReference = "Second observation of same goods" });
        var page = await first.DeliveryEvidenceAsync(DeliveryQuery(input, size: 1)); Assert.True(page.HasMore); var next = await first.DeliveryEvidenceAsync(DeliveryQuery(input, 2, 1)); Assert.False(next.HasMore);
        Assert.NotEqual(Assert.Single(page.Items).Evidence.Reference, Assert.Single(next.Items).Evidence.Reference); Assert.False(page.EstablishesDeliveryProof);
    }
    [CurrentPermissionPostgresFact]
    public async Task RmaDeliveryEvidence_actual_HTTP_records_simulated_documents_and_recovers_with_closed_inputs()
    {
        await using var h = await Seed(); var (_, seed) = await DeliveryEvidenceSetup(h); var coords = Assert.Single(FormalCoordinates(h).WarehouseSites);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, new Dictionary<string,string> {
            ["ScopeAuthorization__EnvironmentId"] = ScopeEnvironment.ToString(), ["QualitySpecification__EvidenceMode"] = "DEMO",
            ["RmaApplication__WarehouseSites__0__TenantId"] = h.Tenant.ToString(), ["RmaApplication__WarehouseSites__0__WarehouseId"] = coords.WarehouseId.ToString(), ["RmaApplication__WarehouseSites__0__SiteId"] = coords.SiteId.ToString() });
        var cookies = await Login(host,h); using var ctx = await host.Send(FormalRequest(cookies,HttpMethod.Get,"api/wms/rma/v1/context")); ctx.EnsureSuccessStatusCode();
        var context = JsonSerializer.Deserialize<RmaFormalContextV1>(await ctx.Content.ReadAsStringAsync(), RmaJson)!; var input = seed with { ScopeOptionRef = context.ScopeOptions[0].OptionRef }; var command = Guid.NewGuid(); const string root = "api/wms/rma/v1/delivery-evidence";
        using var write = await host.Send(FormalRequest(cookies,HttpMethod.Post,root+"/record",input,command)); Assert.True(write.IsSuccessStatusCode,await OperationsFailure(write)); Assert.True(write.Headers.CacheControl?.NoStore);
        var saved = JsonSerializer.Deserialize<RmaDeliveryEvidenceViewV1>(await write.Content.ReadAsStringAsync(),RmaJson)!;
        using var query = await host.Send(FormalRequest(cookies,HttpMethod.Post,root+"/commands/query",new RmaPolicyCommandQueryV1(input.ScopeOptionRef,command))); Assert.True(query.IsSuccessStatusCode,await OperationsFailure(query));
        var result = JsonSerializer.Deserialize<RmaDeliveryEvidenceCommandV1>(await query.Content.ReadAsStringAsync(),RmaJson)!; Assert.Equal(saved.Evidence.Reference,result.Value!.Evidence.Reference);
        using var search = await host.Send(FormalRequest(cookies,HttpMethod.Post,root+"/search",DeliveryQuery(input))); Assert.True(search.IsSuccessStatusCode,await OperationsFailure(search));
        var malformed = JsonSerializer.SerializeToNode(input,RmaJson)!; malformed["approved"] = true;
        using var bad = await host.Send(FormalRequest(cookies,HttpMethod.Post,root+"/record",malformed,Guid.NewGuid())); Assert.Equal(HttpStatusCode.BadRequest,bad.StatusCode);
    }
}
