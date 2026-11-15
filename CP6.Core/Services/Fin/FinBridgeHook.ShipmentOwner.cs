using System.Data;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Options;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Integration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CP6.Core.Services.Fin;

public partial class FinBridgeHook
{
    private static readonly JsonSerializerOptions ShipmentPayload = new() { PropertyNameCaseInsensitive = true };

    internal static bool IsShipmentEvent(IntegrationEvent evt) => evt.SourceModule == "WMS" && evt.TargetModule == "FIN"
        && evt.HookName is nameof(OnShipmentConfirmedAsync) or nameof(OnShipmentCancelledAsync);

    internal Task<Guid> StageShipmentConfirmationAsync(CP6Context owner, FinShipmentInvoiceRequest request, string actor)
        => StageShipmentAsync(owner, nameof(OnShipmentConfirmedAsync), request.ShipmentId, request, actor);

    internal Task<Guid> StageShipmentCancellationAsync(CP6Context owner, string shipmentId, string actor)
        => StageShipmentAsync(owner, nameof(OnShipmentCancelledAsync), shipmentId, new { shipmentId, userName = actor }, actor);

    private async Task<Guid> StageShipmentAsync(CP6Context owner, string hook, string shipmentId, object payload, string actor)
    {
        if (!ReferenceEquals(owner, Db) || !Db.Database.IsNpgsql() || Db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("WMS_FIN_OWNER_TRANSACTION_REQUIRED");
        var evt = new IntegrationEvent
        {
            Id = Guid.NewGuid(), TenantId = Db.CurrentTenantId, CorrelationId = Guid.NewGuid(),
            SourceModule = "WMS", TargetModule = "FIN", HookName = hook, SourceNo = shipmentId,
            Status = IntegrationEventStatus.Pending, Attempts = 0, NextRetryAt = DateTime.UtcNow,
            PayloadJson = JsonSerializer.Serialize(payload), Creator = actor, CreateDate = DateTime.Now
        };
        Db.IntegrationEvents.Add(evt);
        await Db.SaveChangesAsync();
        return evt.Id;
    }

    internal async Task ApplyCommittedShipmentAsync(Guid eventId)
    {
        try { await ProcessShipmentAsync(eventId, DateTime.UtcNow, null, CancellationToken.None); }
        catch (Exception error)
        {
            // The producer has already committed the original event. The due worker can resolve
            // an interrupted or uncertain attempt by rereading that row.
            Logger.LogError(error, "Committed shipment financial event {EventId} did not return a result", eventId);
        }
    }

    internal async Task<IntegrationEvent?> ProcessShipmentAsync(Guid eventId, DateTime dueBefore,
        IntegrationEventOptions? retry, CancellationToken ct)
    {
        if (!Db.Database.IsNpgsql() || Db.Database.CurrentTransaction is not null ||
            System.Transactions.Transaction.Current is not null || Db.ChangeTracker.HasChanges() ||
            _ar is not ArInvoiceService ar || !ar.Owns(Db))
            throw new InvalidOperationException("WMS_FIN_OWNER_TRANSACTION_REQUIRED");
        var tenantId = Db.CurrentTenantId;
        await using var tx = await Db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await AuthenticationCompletionGateV1.AcquireAsync(Db, ct);
        if (!await TenantIsActive(tenantId, ct)) return null;
        var evt = await Db.IntegrationEvents.FromSqlInterpolated($"""
            SELECT * FROM public."T_IntegrationEvent" WHERE "TenantId"={tenantId} AND "Id"={eventId} FOR UPDATE
            """).AsNoTracking().SingleOrDefaultAsync(ct);
        if (evt is null || !IsShipmentEvent(evt) || evt.NextRetryAt is not { } due || due > dueBefore
            || (evt.Status != IntegrationEventStatus.Pending || evt.Attempts != 0)
                && (retry is null || evt.Status != IntegrationEventStatus.Failed || evt.Attempts >= retry.MaxAttempts))
            return null;

        const string effects = "wms_fin_shipment";
        try
        {
            await tx.CreateSavepointAsync(effects, ct);
            ShipmentResult result;
            try { result = await ApplyShipmentEffects(ar, evt, ct); }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                Logger.LogError(error, "Shipment financial effects failed for {EventId}", evt.Id);
                result = new(IntegrationEventStatus.Failed, null,
                    error is JsonException ? "DISPATCH-400" : error.ToString());
            }
            if (result.Status == IntegrationEventStatus.Failed)
            {
                await tx.RollbackToSavepointAsync(effects, ct);
                Db.ChangeTracker.Clear();
            }
            await tx.ReleaseSavepointAsync(effects, ct);

            Db.IntegrationEvents.Attach(evt);
            evt.Attempts++;
            var dead = result.Status == IntegrationEventStatus.Failed && retry is not null && evt.Attempts >= retry.MaxAttempts;
            evt.Status = dead ? IntegrationEventStatus.DeadLetter : result.Status;
            evt.TargetNo = result.InvoiceNo;
            evt.LastError = result.Error;
            evt.NextRetryAt = evt.Status == IntegrationEventStatus.Failed
                ? DateTime.UtcNow.AddSeconds(retry?.GetBackoffSeconds(evt.Attempts) ?? 60) : null;
            await Db.SaveChangesAsync(ct);
            if (!await TenantIsActive(tenantId, ct)) return null;
            await tx.CommitAsync(ct);
            return dead ? evt : null;
        }
        finally
        {
            Db.ChangeTracker.Clear();
        }
    }

    private async Task<bool> TenantIsActive(Guid tenantId, CancellationToken ct)
    {
        var tenant = await Db.Sys_Tenants.AsNoTracking().Where(t => t.Id == tenantId)
            .Select(t => new { t.Enable, t.ExpireDate }).SingleOrDefaultAsync(ct);
        return tenant is { Enable: true } && (tenant.ExpireDate is null || tenant.ExpireDate > DateTime.Now);
    }

    private sealed record ShipmentResult(string Status, string? InvoiceNo, string? Error)
    {
        internal static ShipmentResult From(FinResult result, string? invoiceNo) =>
            result.Ok ? new(IntegrationEventStatus.Success, invoiceNo, null)
                : new(IntegrationEventStatus.Failed, invoiceNo, result.Code);
    }

    private sealed record CancellationPayload(string ShipmentId, string? UserName);

    private async Task<ShipmentResult> ApplyShipmentEffects(ArInvoiceService ar, IntegrationEvent evt, CancellationToken ct)
    {
        if (evt.HookName == nameof(OnShipmentConfirmedAsync))
        {
            var request = JsonSerializer.Deserialize<FinShipmentInvoiceRequest>(evt.PayloadJson, ShipmentPayload);
            if (request is null || string.IsNullOrWhiteSpace(evt.SourceNo) || request.ShipmentId != evt.SourceNo)
                return new(IntegrationEventStatus.Failed, null, "DISPATCH-400");
            // Historical confirmation retries used system. A newly staged first attempt carries its producer.
            var actor = evt.Status == IntegrationEventStatus.Pending ? evt.Creator ?? "system" : "system";
            var (result, _, invoiceNo) = await ar.CreateFromShipmentAsync(request, actor);
            return ShipmentResult.From(result, result.Ok ? invoiceNo : null);
        }

        var cancellation = JsonSerializer.Deserialize<CancellationPayload>(evt.PayloadJson, ShipmentPayload);
        if (cancellation is null || string.IsNullOrWhiteSpace(evt.SourceNo) || cancellation.ShipmentId != evt.SourceNo)
            return new(IntegrationEventStatus.Failed, null, "DISPATCH-400");
        var invoice = await Db.ArInvoices.SingleOrDefaultAsync(x => x.TenantId == evt.TenantId
            && x.ShipmentId == evt.SourceNo && !x.IsCreditMemo, ct);
        if (invoice is null)
            return new(IntegrationEventStatus.Skipped, null, "no invoice for shipment");
        return ShipmentResult.From(await ar.ReverseShipmentAsync(invoice, cancellation.UserName ?? "system", ct), invoice.No);
    }
}
