using System.Data;
using System.Data.Common;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Options;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels;
using CP6.Entity.DomainModels.Integration;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CP6.Core.Services.Wms;

public partial class ErpBridgeHook
{
    private static readonly JsonSerializerOptions ShipmentJson = new() { PropertyNameCaseInsensitive = true };

    internal static bool IsShipmentProjectionEvent(IntegrationEvent evt) => evt.SourceModule == "WMS"
        && evt.TargetModule == "ERP" && evt.HookName == nameof(OnShipmentConfirmedAsync);

    internal Guid StageShipmentProjection(CP6Context owner, OutboundOrder outbound, string? actor)
    {
        if (!ReferenceEquals(Db, owner) || !Db.Database.IsNpgsql() || Db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("WMS_ERP_OWNER_TRANSACTION_REQUIRED");

        var payload = new ShipmentProjectionPayload(1, outbound.Id, outbound.OutboundNo, outbound.WebOrderNo, actor);
        var evt = new IntegrationEvent
        {
            Id = Guid.NewGuid(), TenantId = Db.CurrentTenantId, CorrelationId = Guid.NewGuid(),
            SourceModule = "WMS", TargetModule = "ERP", HookName = nameof(OnShipmentConfirmedAsync),
            SourceNo = outbound.OutboundNo, Status = IntegrationEventStatus.Pending, NextRetryAt = DateTime.UtcNow,
            PayloadJson = JsonSerializer.Serialize(payload), Creator = string.IsNullOrWhiteSpace(actor) ? "system" : actor,
            CreateDate = DateTime.Now
        };
        // The caller saves the intent with the original completed outbound and package.
        Db.IntegrationEvents.Add(evt);
        return evt.Id;
    }

    internal async Task ApplyCommittedShipmentProjectionAsync(Guid eventId)
    {
        try { await ProcessShipmentProjectionAsync(eventId, DateTime.UtcNow, null, CancellationToken.None); }
        catch (Exception error)
        {
            // The original intent is already committed. An interrupted outcome is resolved from that row.
            Logger.LogError(error, "Committed ERP shipment event {EventId} did not return a result", eventId);
        }
    }

    internal async Task<IntegrationEvent?> ProcessShipmentProjectionAsync(Guid eventId, DateTime dueBefore,
        IntegrationEventOptions? retry, CancellationToken ct)
    {
        if (!Db.Database.IsNpgsql() || Db.Database.CurrentTransaction is not null
            || System.Transactions.Transaction.Current is not null || Db.ChangeTracker.HasChanges())
            throw new InvalidOperationException("WMS_ERP_OWNER_TRANSACTION_REQUIRED");

        var tenantId = Db.CurrentTenantId;
        await using var transaction = await Db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            await AuthenticationCompletionGateV1.AcquireAsync(Db, ct);
            var tenant = await Db.Sys_Tenants.AsNoTracking().Where(row => row.Id == tenantId)
                .Select(row => new { row.Enable, row.ExpireDate }).SingleOrDefaultAsync(ct);
            if (tenant is not { Enable: true } || tenant.ExpireDate <= DateTime.Now) return null;

            var evt = await Db.IntegrationEvents.FromSqlInterpolated($"""
                SELECT * FROM public."T_IntegrationEvent" WHERE "TenantId"={tenantId} AND "Id"={eventId} FOR UPDATE
                """).SingleOrDefaultAsync(ct);
            if (evt is null || evt.IsDeleted || !IsShipmentProjectionEvent(evt)
                || evt.NextRetryAt is not { } due || due > dueBefore
                || (evt.Status != IntegrationEventStatus.Pending || evt.Attempts != 0)
                    && (retry is null || evt.Status != IntegrationEventStatus.Failed || evt.Attempts >= retry.MaxAttempts))
                return null;

            const string projection = "wms_erp_shipment";
            await transaction.CreateSavepointAsync(projection, ct);
            try
            {
                var result = await ProjectOriginalShipmentAsync(evt, ct);
                RecordShipmentAttempt(evt, result, retry);
                await Db.SaveChangesAsync(ct);
            }
            catch (Exception error) when (error is DbException or DbUpdateException or JsonException or InvalidOperationException or ArithmeticException)
            {
                Logger.LogError(error, "ERP shipment projection failed for {EventId}", evt.Id);
                await transaction.RollbackToSavepointAsync(projection, ct);
                Db.ChangeTracker.Clear();
                evt = await Db.IntegrationEvents.SingleAsync(row => row.Id == eventId, ct);
                RecordShipmentAttempt(evt, ShipmentProjectionResult.Failed(error is JsonException
                    ? "DISPATCH-400" : error.ToString()), retry);
                await Db.SaveChangesAsync(ct);
            }
            await transaction.ReleaseSavepointAsync(projection, ct);

            if (tenant.ExpireDate <= DateTime.Now) return null;
            await transaction.CommitAsync(ct);
            return evt.Status == IntegrationEventStatus.DeadLetter ? evt : null;
        }
        finally
        {
            Db.ChangeTracker.Clear();
        }
    }

    private Task<ShipmentProjectionResult> ProjectOriginalShipmentAsync(IntegrationEvent evt, CancellationToken ct)
    {
        var original = JsonSerializer.Deserialize<ShipmentProjectionPayload>(evt.PayloadJson, ShipmentJson);
        if (original is null || string.IsNullOrWhiteSpace(evt.SourceNo) || original.OutboundNo != evt.SourceNo
            || (original.Version, original.OutboundId, original.OrderNo) is not ((null, null, null) or (1, not null, not null))
            || original.OutboundId == Guid.Empty)
            return Task.FromResult(ShipmentProjectionResult.Failed("DISPATCH-400"));

        return ProjectShipmentAsync(evt.SourceNo, original.UserName, original, ct);
    }

    private static void RecordShipmentAttempt(IntegrationEvent evt, ShipmentProjectionResult result, IntegrationEventOptions? retry)
    {
        evt.Attempts++;
        evt.Status = result.Status == IntegrationEventStatus.Failed && retry is not null && evt.Attempts >= retry.MaxAttempts
            ? IntegrationEventStatus.DeadLetter : result.Status;
        evt.TargetNo = result.OrderNo;
        evt.LastError = result.Error;
        evt.NextRetryAt = evt.Status == IntegrationEventStatus.Failed
            ? DateTime.UtcNow.AddSeconds(retry?.GetBackoffSeconds(evt.Attempts) ?? 60) : null;
    }

    private sealed record ShipmentProjectionPayload(int? Version, Guid? OutboundId, string? OutboundNo,
        string? OrderNo, string? UserName);

    private sealed record ShipmentProjectionResult(string Status, string? OrderNo, string? Error, int? ShipStatus = null)
    {
        internal static ShipmentProjectionResult Skipped(string error) => new(IntegrationEventStatus.Skipped, null, error);
        internal static ShipmentProjectionResult Failed(string error) => new(IntegrationEventStatus.Failed, null, error);
    }
}
