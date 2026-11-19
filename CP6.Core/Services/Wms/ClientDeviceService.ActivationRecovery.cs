using CP6.Core.Services.Erp;
using CP6.Entity.DomainModels.Wms;
using CP6.Entity.DTOs.Client;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public sealed partial class ClientDeviceService
{
    public async Task<DeviceActivationRecoveryDto> RecoverActivationAsync(RecoverDeviceActivationRequest request, CancellationToken ct = default)
    {
        if (!_db.Database.IsNpgsql())
            throw new OrderPriceCorrectionException("DEVICE_AUTHORITY_UNAVAILABLE", 503);
        if (request.ActivationOperationId == Guid.Empty || string.IsNullOrWhiteSpace(request.DeviceId) ||
            request.DeviceId.Length > 128 || string.IsNullOrWhiteSpace(request.TenantCode) ||
            !DeviceProofV1.IsFresh(request.Timestamp))
            return new("unavailable", null);

        var now = DateTime.Now;
        var original = await (
            from ticket in _db.DeviceActivations.IgnoreQueryFilters().AsNoTracking()
            join tenant in _db.Sys_Tenants.IgnoreQueryFilters().AsNoTracking() on ticket.TenantId equals tenant.Id
            join device in _db.ClientDevices.IgnoreQueryFilters().AsNoTracking()
                on new { ticket.TenantId, DeviceId = ticket.ConsumedByDeviceId } equals new { device.TenantId, DeviceId = (string?)device.DeviceId }
            where ticket.Id == request.ActivationOperationId && !ticket.IsDeleted
                && tenant.TenantCode == request.TenantCode && tenant.Enable
                && (tenant.ExpireDate == null || tenant.ExpireDate > now)
                && !device.IsDeleted && device.Status == ClientDeviceStatus.Active
                && ticket.ConsumedAt == device.ActivatedAt && ticket.Platform == device.Platform
                && device.DeviceId == request.DeviceId
            select new { Ticket = ticket, Device = device, tenant.TenantCode }).SingleOrDefaultAsync(ct);
        if (original == null || original.Device.DeviceId != request.DeviceId ||
            !DeviceProofV1.IsFresh(request.Timestamp) ||
            !VerifyDeviceSignature(original.Device.PublicKey,
                DeviceProofV1.ActivationResult(request.ActivationOperationId, request.TenantCode, request.DeviceId, request.Timestamp),
                request.Signature))
            return new("unavailable", null);

        var issued = original.Ticket;
        var registered = original.Device;
        // 调整仓区或模式以后，原票据的登记范围不再披露。
        if (issued.WarehouseCd != registered.WarehouseCd || issued.AreaCd != registered.AreaCd || issued.DeviceMode != registered.DeviceMode)
            return new("unavailable", null);
        return new("registered", new ActivatedClientDeviceDto
        {
            DeviceId = registered.DeviceId,
            TenantCode = original.TenantCode,
            DeviceMode = issued.DeviceMode,
            WarehouseCd = issued.WarehouseCd,
            AreaCd = issued.AreaCd,
            ActivatedAt = new DateTimeOffset(DateTime.SpecifyKind(registered.ActivatedAt, DateTimeKind.Utc)),
        });
    }

    internal static bool VerifyDeviceSignature(string key, byte[] payload, string? proof)
    {
        if (string.IsNullOrWhiteSpace(proof) || proof.Length > 2048) return false;
        byte[] signature;
        try { signature = Convert.FromBase64String(proof); }
        catch (FormatException) { return false; }
        return VerifyRsa(key, payload, signature) || VerifyEcdsa(key, payload, signature);
    }
}
