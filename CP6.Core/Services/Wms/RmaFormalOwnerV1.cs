using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Mes;
using CP6.Core.Services.Pub;
using CP6.Core.Services.Sys;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using static CP6.Core.Services.Wms.RmaFormalStoreV1;

namespace CP6.Core.Services.Wms;

/// <summary>Formal return applications and independent authorization/claims. No stock or financial effect.</summary>
public sealed partial class RmaFormalOwnerV1(CP6Context db, OperationsOwnerV1 owner, ScopeAuthorizationFenceV1 environment,
    QualitySpecificationRuntimeV1 runtime, IDataProtectionProvider protection, FileContentOwnerV1 files, IOptions<RmaApplicationOptionsV1> options,
    IRmaReturnSourcesV1? returnSources = null)
{
    private const string JournalOwner = "Main.Wms.RmaFormalCommand.v1", Contract = "WMS-RMA-01.APPLICATION/1";
    public const string CreateRoute = "/api/wms/rma/v1/applications", EditRoute = CreateRoute + "/{applicationId}",
        SubmitRoute = EditRoute + "/submit", WithdrawRoute = EditRoute + "/withdrawals";
    private readonly IDataProtector protector = protection.CreateProtector("CP6.Rma.ImmutableSelection.v1");
    private Task<T> Run<T>(string action, bool write, Func<RmaFormalStoreV1, OperationsAccessV1, CancellationToken, Task<T>> body,
        CancellationToken ct, Func<CancellationToken, Task<string>>? actionAtFence = null) =>
        owner.ExecuteAsync(OperationsOwnerV1.Rma, action, write, async (a, t) =>
        {
            if (runtime.EvidenceMode is not ("REAL" or "DEMO")) throw Error("RMA_ENVIRONMENT_UNAVAILABLE", 503);
            var store = new RmaFormalStoreV1(db, protector); await store.Fence(environment.EnvironmentId, runtime.EvidenceMode, write, t);
            return await body(store, a, t);
        }, ct, actionAtFence: actionAtFence);
    private static void ReadFields(OperationsAccessV1 a)
    {
        var fields = a.Authorities[0].Fields.Fields;
        if (!fields["OperatorCd"].CanRead || !fields["ReturnReason"].CanRead) throw Error("FIELD_READ_FORBIDDEN", 403);
    }
    private static void WriteFields(OperationsAccessV1 a, bool remarks)
    {
        var fields = a.Authorities[0].Fields.Fields;
        if (!fields["ReturnReason"].CanWrite || remarks && !fields["Remarks"].CanWrite) throw Error("FIELD_WRITE_FORBIDDEN", 403);
    }
    private static decimal Quantity(FulfilmentQtyV1 quantity)
    {
        if (quantity == null || quantity.Uom == null || quantity.Value == null
            || !Regex.IsMatch(quantity.Value, @"\A(0|[1-9][0-9]{0,12})(\.[0-9]{1,8})?\z", RegexOptions.CultureInvariant))
            throw Error("VALIDATION_FAILED", 400);
        var result = decimal.Parse(quantity.Value, CultureInfo.InvariantCulture);
        return result > 0 ? result : throw Error("VALIDATION_FAILED", 400);
    }
    private static string Text(decimal value) => value.ToString("0.########", CultureInfo.InvariantCulture);
    private static void Intent(FulfilmentRefV1 customer, string reason, string outcome, RmaRequestedLineV1[] lines, string? remarks)
    {
        Reference(customer);
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 200 || remarks is { Length: < 1 or > 1000 }
            || outcome is not ("CREDIT" or "REFUND" or "REPLACEMENT" or "SUPPLEMENTARY_SHIPMENT" or "RETURN_ONLY" or "TO_BE_DECIDED")
            || lines == null || lines.Length is < 1 or > 100 || lines.Any(x => x == null || x.LineId == Guid.Empty)
            || lines.Select(x => x.LineId).Distinct().Count() != lines.Length) throw Error("VALIDATION_FAILED", 400);
        foreach (var line in lines)
        {
            Reference(line.OriginalSelectionRef); _ = Quantity(line.RequestedQuantity);
            if (line.ReasonDetail is { Length: < 1 or > 1000 } || line.EvidenceRefs == null || line.EvidenceRefs.Length > 100) throw Error("VALIDATION_FAILED", 400);
            foreach (var evidence in line.EvidenceRefs) Reference(evidence);
        }
        if (lines.SelectMany(x => x.EvidenceRefs).Distinct().Count() > 100) throw Error("VALIDATION_FAILED", 400);
    }
    private static void Transition(int version, FulfilmentRefV1 head, string reason)
    { Reference(head); if (version < 1 || string.IsNullOrWhiteSpace(reason) || reason.Length > 500) throw Error("VALIDATION_FAILED", 400); }
    private static void Expected(RmaFormalOriginalV1 before, int version, FulfilmentRefV1 head, string? etag)
    {
        if (string.IsNullOrWhiteSpace(etag)) throw Error("IF_MATCH_REQUIRED", 428);
        var current = View(before).ApplicationRef;
        if (before.Version != version || current != head || etag != '"' + current.Digest + '"') throw Error("CAS_CONFLICT", 412);
    }
    private async Task<RmaFormalOriginalV1> Current(RmaFormalStoreV1 store, OperationsAccessV1 a, Guid id, CancellationToken ct, int? version = null)
    {
        var head = await store.Heads().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw Error("RMA_APPLICATION_NOT_FOUND", 404);
        if (a.RmaApplicationSelfOnly && head.Actor != a.ActorId) throw Error("OBJECT_FORBIDDEN", 403);
        if (version is < 1 || version > head.Version) throw Error("RMA_APPLICATION_VERSION_NOT_FOUND", 404);
        var value = await store.Read(id, version ?? head.Version, ct); await Access(a, value, ct); return value;
    }
    private async Task Access(OperationsAccessV1 a, RmaFormalOriginalV1 o, CancellationToken ct)
    {
        ReadFields(a); await RequireScope(a, o.Scope, ct);
        foreach (var id in o.Lines.Select(x => x.OutboundId).Distinct()) await RequireOutbound(a, id, ct);
    }
    private async Task<RmaFormalLineOriginalV1[]> Lines(RmaFormalStoreV1 store, OperationsAccessV1 a, RmaTrustedScopeV1 scope,
        FulfilmentRefV1 customer, RmaRequestedLineV1[] input, int version, CancellationToken ct)
    {
        await Customer(a, customer, ct); var result = new List<RmaFormalLineOriginalV1>();
        var used = new Dictionary<(FulfilmentRefV1 Shipment, string Line, string Version), decimal>();
        foreach (var line in input)
        {
            var selected = await store.Selection<SourceSelectionBody>(line.OriginalSelectionRef, SourceSelection, a.ActorId, ct);
            if (selected.Scope.EnvironmentId != scope.EnvironmentId || selected.Scope.Mode != scope.Mode
                || selected.Scope.SiteId != scope.SiteId || selected.Scope.WarehouseId != scope.WarehouseId) throw Error("SCOPE_MISMATCH", 403);
            if (selected.Customer != customer) throw Error("RMA_CUSTOMER_MISMATCH", 409);
            await RequireOutbound(a, selected.OutboundId, ct);
            var source = selected.Original; var requested = decimal.Parse(line.RequestedQuantity.Value, CultureInfo.InvariantCulture);
            if (line.RequestedQuantity.Uom != source.BaseQty.Uom) throw Error("RMA_UOM_MISMATCH", 409);
            var key = (source.ShipmentRef, source.ShipmentLineId, source.ShipmentLineVersion);
            var skip = used.GetValueOrDefault(key); var remaining = requested; var ranges = new List<FulfilmentRangeV1>();
            foreach (var range in source.SourceShareRanges)
            {
                var start = decimal.Parse(range.From, CultureInfo.InvariantCulture); var end = decimal.Parse(range.To, CultureInfo.InvariantCulture);
                var width = end - start;
                if (skip >= width) { skip -= width; continue; }
                start += skip; skip = 0; var take = Math.Min(end - start, remaining);
                ranges.Add(new(Text(start), Text(start + take))); remaining -= take; if (remaining == 0) break;
            }
            if (remaining != 0) throw Error("RANGE_MEASURE_MISMATCH", 409);
            if (ranges.Count > 100) throw Error("RANGE_LIMIT_EXCEEDED", 400);
            used[key] = used.GetValueOrDefault(key) + requested;
            result.Add(new(line.LineId, version, selected.OutboundId, line.OriginalSelectionRef, source, ranges.ToArray(),
                new(Text(requested), source.BaseQty.Uom), line.ReasonDetail, line.EvidenceRefs));
        }
        foreach (var reference in input.SelectMany(x => x.EvidenceRefs).Distinct()) await Evidence(a, reference, ct);
        return result.ToArray();
    }
    public Task<RmaFormalApplicationV1> CreateAsync(Guid command, RmaFormalCreateV1 c, CancellationToken ct = default)
    {
        Reference(c.ScopeOptionRef); Intent(c.CustomerRef, c.ReasonCode, c.RequestedOutcome, c.Lines, c.Remarks);
        if (c.ClientApplicationId == Guid.Empty) throw Error("VALIDATION_FAILED", 400);
        return Command(command, "APP_CREATE", "application-write", "POST", CreateRoute, c.ClientApplicationId, c, null,
            async (store, a, t) =>
            {
                ReadFields(a); WriteFields(a, c.Remarks != null); var selected = await ResolveScope(store, a, c.ScopeOptionRef, t);
                if (await store.Heads().AnyAsync(x => x.Id == c.ClientApplicationId, t)) throw Error("RMA_APPLICATION_EXISTS", 409);
                var lines = await Lines(store, a, selected.Scope, c.CustomerRef, c.Lines, 1, t);
                return new(c.ClientApplicationId, 1, a.ActorId, selected.Scope, c.CustomerRef, c.ReasonCode, c.RequestedOutcome,
                    DateTimeOffset.UtcNow, await Actor(store, a, t), lines, c.Remarks, "DRAFT", null);
            }, [], ct);
    }
    public Task<RmaFormalApplicationV1> EditAsync(Guid id, Guid command, RmaFormalEditV1 c, string? etag, CancellationToken ct = default)
    {
        Intent(c.CustomerRef, c.ReasonCode, c.RequestedOutcome, c.Lines, c.Remarks); Transition(c.ExpectedApplicationVersion, c.ExpectedApplicationHeadRef, c.Reason);
        return Command(command, "APP_EDIT", "application-write", "PUT", EditRoute, id, c, etag, async (store, a, t) =>
        {
            var before = await Current(store, a, id, t); WriteFields(a, c.Remarks != before.Remarks);
            Expected(before, c.ExpectedApplicationVersion, c.ExpectedApplicationHeadRef, etag);
            if (before.State != "DRAFT") throw Error("RMA_APPLICATION_NOT_DRAFT", 409);
            var next = checked(before.Version + 1); var lines = await Lines(store, a, before.Scope, c.CustomerRef, c.Lines, next, t);
            return before with { Version = next, Customer = c.CustomerRef, ReasonCode = c.ReasonCode, Outcome = c.RequestedOutcome,
                Lines = lines, Remarks = c.Remarks, Previous = View(before).ApplicationRef };
        }, [c.ExpectedApplicationHeadRef], ct);
    }
    public Task<RmaFormalApplicationV1> SubmitAsync(Guid id, Guid command, RmaFormalTransitionV1 c, string? etag, CancellationToken ct = default) =>
        Change(id, command, c, etag, false, ct);
    public Task<RmaFormalApplicationV1> WithdrawAsync(Guid id, Guid command, RmaFormalTransitionV1 c, string? etag, CancellationToken ct = default) =>
        Change(id, command, c, etag, true, ct);
    private Task<RmaFormalApplicationV1> Change(Guid id, Guid command, RmaFormalTransitionV1 c, string? etag, bool withdraw, CancellationToken ct)
    {
        Transition(c.ExpectedApplicationVersion, c.ExpectedApplicationHeadRef, c.Reason);
        return Command(command, withdraw ? "APP_WITHDRAW" : "APP_SUBMIT", withdraw ? "application-withdraw" : "application-submit", "POST",
            withdraw ? WithdrawRoute : SubmitRoute, id, c, etag, async (store, a, t) =>
            {
                var before = await Current(store, a, id, t); Expected(before, c.ExpectedApplicationVersion, c.ExpectedApplicationHeadRef, etag);
                if (withdraw ? before.State is not ("DRAFT" or "SUBMITTED") : before.State != "DRAFT") throw Error("RMA_APPLICATION_STATE_CONFLICT", 409);
                if (withdraw && (await AuthorizationCurrent(id,t))?.Outcome == "APPROVED") throw Error("AUTHORIZATION_ACTIVE",409);
                if (!withdraw)
                {
                    await Customer(a, before.Customer, t);
                    foreach (var reference in before.Lines.SelectMany(x => x.Evidence).Distinct()) await Evidence(a, reference, t);
                }
                return before with { Version = checked(before.Version + 1), State = withdraw ? "WITHDRAWN" : "SUBMITTED", Previous = View(before).ApplicationRef };
            }, [c.ExpectedApplicationHeadRef], ct);
    }
    private Task<RmaFormalApplicationV1> Command<T>(Guid command, string operation, string action, string method, string route, Guid id,
        T input, string? etag, Func<RmaFormalStoreV1, OperationsAccessV1, CancellationToken, Task<RmaFormalOriginalV1>> apply,
        FulfilmentRefV1[] expected, CancellationToken ct)
    {
        if (command == Guid.Empty || id == Guid.Empty) throw Error("VALIDATION_FAILED", 400);
        var bytes = Encode(new { Id = id, Method = method, Route = route, Input = input, IfMatch = etag });
        var journal = new MainOwnerOperationJournalV1(db); MainOwnerOperationJournalV1.Original? original = null;
        return Run(action, true, async (store, a, t) =>
        {
            if (original != null) return (await Disclose(store, a, original, bytes, t)).Application;
            var value = await apply(store, a, t); var view = await store.Append(value, t); var at = DateTimeOffset.UtcNow;
            var actor = await Actor(store, a, t); var effect = operation + ":" + id.ToString("N") + ":" + view.ApplicationVersion;
            var binding = new RmaCommandBindingV1(command, view.Scope, actor, method, route, "wms.rma." + action.Replace('-', '.'),
                operation, view.ApplicationRef, view.RequestedLines.Select(x => x.OriginalSelectionRef).Distinct().ToArray(),
                MainOwnerOperationJournalV1.Digest(bytes), [effect], expected, at);
            var reference = await store.Fact("Main.Wms.RmaCommandBinding.v1", a.ActorId, binding, false, t);
            var work = new RmaApplicationWorkV1(command, id, operation, 1, 0, 0, "SUCCEEDED", reference, [], [], at, at);
            var receipt = new RmaApplicationReceiptV1(Guid.NewGuid(), reference, "SUCCEEDED", [view.ApplicationRef], [effect], null, at, false);
            var result = new RmaFormalOriginalResultV1(view, binding, reference, work, receipt);
            await journal.Store(JournalOwner, Contract, command, a.ActorId, bytes, result, [id], new { binding, work, receipt, action = operation }, t);
            return view;
        }, ct, async t => { original = await journal.Read(JournalOwner, command, t); return original == null ? action : "query"; });
    }
    private async Task<RmaFormalOriginalResultV1> Disclose(RmaFormalStoreV1 store, OperationsAccessV1 a,
        MainOwnerOperationJournalV1.Original original, byte[]? input, CancellationToken ct)
    {
        if (original.Actor != a.ActorId) throw Error("OBJECT_FORBIDDEN", 403);
        if (input != null && !MainOwnerOperationJournalV1.Matches(original, input)) throw Error("COMMAND_BINDING_CONFLICT", 409);
        var result = JsonSerializer.Deserialize<RmaFormalOriginalResultV1>(original.Result)!;
        await Current(store, a, result.Application.ApplicationId, ct, result.Application.ApplicationVersion); return result;
    }
    public Task<RmaFormalApplicationV1> ReadAsync(Guid id, int? version = null, CancellationToken ct = default) =>
        Run("query", false, async (store, a, t) =>
        {
            return View(await Current(store, a, id, t, version));
        }, ct);
    public Task<object> QueryCommandAsync(RmaCommandQueryV1 query, CancellationToken ct = default)
    {
        Reference(query.ScopeOptionRef);
        if (query.CommandId == Guid.Empty || query.Method is not ("POST" or "PUT" or "PATCH" or "DELETE")
            || string.IsNullOrWhiteSpace(query.RouteTemplate) || query.RouteTemplate.Length > 200) throw Error("VALIDATION_FAILED", 400);
        return Run<object>("query", true, async (store, a, t) =>
        {
            var scope = (await ResolveScope(store, a, query.ScopeOptionRef, t)).Scope;
            var original = await new MainOwnerOperationJournalV1(db).Read(JournalOwner, query.CommandId, t);
            var at = DateTimeOffset.UtcNow;
            if (original == null)
            {
                var read = await store.Fact("Main.Wms.RmaCommandObservation.v1", a.ActorId, new { query, scope, Status = "NOT_OBSERVED", CheckedAt = at }, false, t);
                return new { status = "NOT_OBSERVED", query, checkedAt = at, ownerReadRef = read, canInferNoEffect = false, reasonCodes = Array.Empty<string>() };
            }
            var result = await Disclose(store, a, original, null, t);
            if (result.Binding.Method != query.Method || result.Binding.RouteTemplate != query.RouteTemplate
                || result.Binding.Scope.EnvironmentId != scope.EnvironmentId || result.Binding.Scope.Mode != scope.Mode
                || result.Binding.Scope.SiteId != scope.SiteId || result.Binding.Scope.WarehouseId != scope.WarehouseId)
                throw Error("COMMAND_BINDING_CONFLICT", 409);
            return new { status = "FOUND", query, bindingRef = result.BindingRef, work = result.Work, terminalReceipt = result.Receipt,
                resultRefs = result.Receipt.ResultRefs, observedAt = at, canInferNoEffect = false, reasonCodes = Array.Empty<string>() };
        }, ct);
    }
}
