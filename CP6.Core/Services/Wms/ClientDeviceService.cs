using System.Security.Cryptography;
using System.Text;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Wms;
using CP6.Entity.DTOs.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace CP6.Core.Services.Wms;

public sealed class ClientDeviceService : IClientDeviceService
{
    private readonly CP6Context _db;
    private readonly ITenantContext _tenant;
    private readonly IRefreshTokenService _refreshTokens;
    private readonly IDistributedCache _cache;

    public ClientDeviceService(
        CP6Context db,
        ITenantContext tenant,
        IRefreshTokenService refreshTokens,
        IDistributedCache cache)
    {
        _db = db;
        _tenant = tenant;
        _refreshTokens = refreshTokens;
        _cache = cache;
    }

    public async Task<DeviceActivationTicket> CreateActivationAsync(
        CreateDeviceActivationRequest request,
        string? userName,
        CancellationToken ct = default)
    {
        var platform = NormalizePlatform(request.Platform);
        var mode = NormalizeMode(request.DeviceMode);
        if (!string.IsNullOrWhiteSpace(request.WarehouseCd)
            && !await _db.Warehouses.AnyAsync(x => !x.IsDeleted
                && x.WarehouseCd == request.WarehouseCd, ct))
            throw new ArgumentException("WM-V2-WAREHOUSE-NOT-FOUND");

        var raw = Base64Url(RandomNumberGenerator.GetBytes(32));
        var expires = DateTime.UtcNow.AddMinutes(
            Math.Clamp(request.ValidMinutes, 2, 120));
        _db.DeviceActivations.Add(new DeviceActivation
        {
            TokenHash = Hash(raw),
            Platform = platform,
            DeviceMode = mode,
            WarehouseCd = NullIfWhiteSpace(request.WarehouseCd),
            AreaCd = NullIfWhiteSpace(request.AreaCd),
            ExpiresAt = expires,
            Creator = userName
        });
        await _db.SaveChangesAsync(ct);
        return new DeviceActivationTicket
        {
            ActivationToken = raw,
            ExpiresAt = expires,
            Platform = platform,
            DeviceMode = mode,
            WarehouseCd = NullIfWhiteSpace(request.WarehouseCd),
            AreaCd = NullIfWhiteSpace(request.AreaCd)
        };
    }

    public async Task<ActivatedClientDeviceDto> ActivateAsync(
        ActivateClientDeviceRequest request,
        CancellationToken ct = default)
    {
        ValidateActivation(request);
        var hash = Hash(request.ActivationToken);
        var activation = await _db.DeviceActivations.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.TokenHash == hash, ct)
            ?? throw new InvalidOperationException("WM-DEVICE-ACTIVATION-INVALID");
        if (activation.ConsumedAt.HasValue || activation.ExpiresAt <= DateTime.UtcNow)
            throw new InvalidOperationException("WM-DEVICE-ACTIVATION-EXPIRED");
        var tenant = await _db.Sys_Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == activation.TenantId
                                      && x.TenantCode == request.TenantCode, ct)
            ?? throw new InvalidOperationException("WM-DEVICE-ACTIVATION-TENANT");
        if (!tenant.Enable)
            throw new InvalidOperationException("WM-DEVICE-TENANT-DISABLED");
        if (!string.Equals(activation.Platform, NormalizePlatform(request.Platform),
                StringComparison.Ordinal))
            throw new InvalidOperationException("WM-DEVICE-PLATFORM-MISMATCH");

        _tenant.CurrentTenantId = activation.TenantId;
        if (await _db.ClientDevices.IgnoreQueryFilters()
            .AnyAsync(x => x.TenantId == activation.TenantId
                           && x.DeviceId == request.DeviceId, ct))
            throw new InvalidOperationException("WM-DEVICE-ALREADY-ACTIVATED");

        var now = DateTime.UtcNow;
        activation.ConsumedAt = now;
        activation.ConsumedByDeviceId = request.DeviceId.Trim();
        var device = new ClientDevice
        {
            TenantId = activation.TenantId,
            DeviceId = request.DeviceId.Trim(),
            DeviceMode = activation.DeviceMode,
            Platform = activation.Platform,
            PublicKey = request.PublicKey.Trim(),
            WarehouseCd = activation.WarehouseCd,
            AreaCd = activation.AreaCd,
            AppVersion = request.AppVersion.Trim(),
            PlatformVersion = NullIfWhiteSpace(request.PlatformVersion),
            Status = ClientDeviceStatus.Active,
            ActivatedAt = now,
            LastSeenAt = now
        };
        _db.ClientDevices.Add(device);
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvalidOperationException("WM-DEVICE-ACTIVATION-USED");
        }
        return new ActivatedClientDeviceDto
        {
            DeviceId = device.DeviceId,
            TenantCode = tenant.TenantCode,
            DeviceMode = device.DeviceMode,
            WarehouseCd = device.WarehouseCd,
            AreaCd = device.AreaCd,
            ActivatedAt = device.ActivatedAt
        };
    }

    public async Task<ClientDeviceDto> HeartbeatAsync(
        ClientDeviceHeartbeatRequest request,
        string? userName,
        CancellationToken ct = default)
    {
        var device = await _db.ClientDevices.FirstOrDefaultAsync(
            x => !x.IsDeleted && x.DeviceId == request.DeviceId, ct)
            ?? throw new InvalidOperationException("WM-DEVICE-NOT-FOUND");
        EnsureActive(device);
        await VerifyHeartbeatAsync(device, request, ct);
        device.LastSeenAt = DateTime.UtcNow;
        device.AppVersion = request.AppVersion.Trim();
        device.PlatformVersion = NullIfWhiteSpace(request.PlatformVersion);
        device.BatteryPercent = request.BatteryPercent.HasValue
            ? Math.Clamp(request.BatteryPercent.Value, 0, 100)
            : null;
        device.NetworkType = NullIfWhiteSpace(request.NetworkType);
        device.CurrentUser = NullIfWhiteSpace(userName);
        device.CurrentTaskNo = NullIfWhiteSpace(request.CurrentTaskNo);
        device.Modifier = userName;
        device.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync(ct);
        return Map(device);
    }

    public async Task<PagedResult<ClientDeviceDto>> GetDevicesAsync(
        string? warehouseCd,
        string? areaCd,
        string? status,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var query = _db.ClientDevices.AsNoTracking().Where(x => !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(warehouseCd))
            query = query.Where(x => x.WarehouseCd == warehouseCd);
        if (!string.IsNullOrWhiteSpace(areaCd))
            query = query.Where(x => x.AreaCd == areaCd);
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(x => x.Status == status);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.LastSeenAt)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<ClientDeviceDto>
        {
            Items = rows.Select(Map).ToList(),
            Total = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<ClientDeviceDto> UpdateAsync(
        string deviceId,
        UpdateClientDeviceRequest request,
        string? userName,
        CancellationToken ct = default)
    {
        var device = await _db.ClientDevices.FirstOrDefaultAsync(
            x => !x.IsDeleted && x.DeviceId == deviceId, ct)
            ?? throw new InvalidOperationException("WM-DEVICE-NOT-FOUND");
        ApplyRowVersion(device, request.RowVersion);
        var wasActive = device.Status == ClientDeviceStatus.Active;
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var status = request.Status.Trim();
            if (status is not (ClientDeviceStatus.Active or ClientDeviceStatus.Disabled))
                throw new ArgumentException("WM-DEVICE-STATUS-INVALID");
            device.Status = status;
        }
        if (!string.IsNullOrWhiteSpace(request.DeviceMode))
            device.DeviceMode = NormalizeMode(request.DeviceMode);
        device.WarehouseCd = NullIfWhiteSpace(request.WarehouseCd);
        device.AreaCd = NullIfWhiteSpace(request.AreaCd);
        device.Modifier = userName;
        device.ModifyDate = DateTime.Now;
        if (wasActive && device.Status == ClientDeviceStatus.Disabled)
        {
            device.DisabledAt = DateTime.UtcNow;
            device.DisabledBy = userName;
            device.FullAuthExpiresAt = null;
            device.CurrentUser = null;
            await _refreshTokens.RevokeAllForDeviceAsync(
                device.TenantId, device.DeviceId, saveChanges: false);
        }
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvalidOperationException("WM-CONFLICT-ROW-VERSION");
        }
        return Map(device);
    }

    public async Task EnsureLoginAllowedAsync(
        ClientContextDto client,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var enabled = await _db.WmsFeatureFlags.IgnoreQueryFilters()
            .AnyAsync(x => x.TenantId == tenantId
                           && !x.IsDeleted
                           && x.ProductionMoveEnabled, ct);
        if (!enabled) return;
        var device = await _db.ClientDevices.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId
                                      && !x.IsDeleted
                                      && x.DeviceId == client.DeviceId, ct)
            ?? throw new InvalidOperationException("WM-DEVICE-NOT-ACTIVATED");
        EnsureActive(device);
        if (!string.Equals(device.Platform, NormalizePlatform(client.ClientKind),
                StringComparison.Ordinal))
            throw new InvalidOperationException("WM-DEVICE-PLATFORM-MISMATCH");
    }

    public async Task MarkFullAuthenticationAsync(
        ClientContextDto client,
        Guid tenantId,
        string userName,
        CancellationToken ct = default)
    {
        var device = await _db.ClientDevices.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId
                                      && !x.IsDeleted
                                      && x.DeviceId == client.DeviceId, ct);
        if (device is null) return;
        EnsureActive(device);
        device.FullAuthExpiresAt = DateTime.UtcNow.AddHours(12);
        device.QuickSwitchFailureCount = 0;
        device.CurrentUser = userName;
        device.LastSeenAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<ClientDevice> GetQuickSwitchDeviceAsync(
        ClientContextDto client,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var device = await _db.ClientDevices.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId
                                      && !x.IsDeleted
                                      && x.DeviceId == client.DeviceId, ct)
            ?? throw new InvalidOperationException("WM-DEVICE-NOT-ACTIVATED");
        EnsureActive(device);
        if (device.DeviceMode != ClientDeviceMode.Shared)
            throw new InvalidOperationException("WM-DEVICE-NOT-SHARED");
        if (!device.FullAuthExpiresAt.HasValue
            || device.FullAuthExpiresAt <= DateTime.UtcNow
            || device.QuickSwitchFailureCount >= 5)
            throw new InvalidOperationException("WM-DEVICE-FULL-AUTH-REQUIRED");
        return device;
    }

    private async Task VerifyHeartbeatAsync(
        ClientDevice device,
        ClientDeviceHeartbeatRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Nonce)
            || string.IsNullOrWhiteSpace(request.Signature)
            || Math.Abs((DateTimeOffset.UtcNow - request.Timestamp).TotalMinutes) > 5)
            throw new InvalidOperationException("WM-DEVICE-SIGNATURE-INVALID");
        var nonceKey = $"wms:device-nonce:{device.TenantId:N}:{device.DeviceId}:{request.Nonce}";
        if (await _cache.GetStringAsync(nonceKey, ct) is not null)
            throw new InvalidOperationException("WM-DEVICE-REPLAY");

        var payload = Encoding.UTF8.GetBytes(
            $"{request.DeviceId}|{request.Timestamp.ToUnixTimeSeconds()}|{request.Nonce}|{request.AppVersion}");
        byte[] signature;
        try { signature = Convert.FromBase64String(request.Signature); }
        catch (FormatException)
        {
            throw new InvalidOperationException("WM-DEVICE-SIGNATURE-INVALID");
        }
        var valid = VerifyRsa(device.PublicKey, payload, signature)
                    || VerifyEcdsa(device.PublicKey, payload, signature);
        if (!valid) throw new InvalidOperationException("WM-DEVICE-SIGNATURE-INVALID");
        await _cache.SetStringAsync(nonceKey, "1",
            new DistributedCacheEntryOptions
                { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10) }, ct);
    }

    private static bool VerifyRsa(string pem, byte[] data, byte[] signature)
    {
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(pem);
            return rsa.VerifyData(data, signature, HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            return false;
        }
    }

    private static bool VerifyEcdsa(string pem, byte[] data, byte[] signature)
    {
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(pem);
            return ecdsa.VerifyData(data, signature, HashAlgorithmName.SHA256);
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            return false;
        }
    }

    private void ApplyRowVersion(ClientDevice device, string encoded)
    {
        byte[] supplied;
        try { supplied = Convert.FromBase64String(encoded); }
        catch (FormatException)
        {
            throw new InvalidOperationException("WM-CONFLICT-ROW-VERSION");
        }
        var current = device.RowVersion ?? Array.Empty<byte>();
        if (current.Length > 0
            && !CryptographicOperations.FixedTimeEquals(current, supplied))
            throw new InvalidOperationException("WM-CONFLICT-ROW-VERSION");
        if (current.Length > 0)
            _db.Entry(device).Property(x => x.RowVersion).OriginalValue = supplied;
    }

    private static void ValidateActivation(ActivateClientDeviceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.TenantCode)
            || string.IsNullOrWhiteSpace(request.ActivationToken)
            || string.IsNullOrWhiteSpace(request.DeviceId)
            || request.DeviceId.Length > 128
            || string.IsNullOrWhiteSpace(request.PublicKey)
            || request.PublicKey.Length > 2048
            || string.IsNullOrWhiteSpace(request.AppVersion))
            throw new ArgumentException("WM-DEVICE-ACTIVATION-DATA-REQUIRED");
        _ = NormalizePlatform(request.Platform);
        if (!request.PublicKey.Contains("PUBLIC KEY", StringComparison.Ordinal))
            throw new ArgumentException("WM-DEVICE-PUBLIC-KEY-INVALID");
    }

    private static void EnsureActive(ClientDevice device)
    {
        if (device.Status != ClientDeviceStatus.Active)
            throw new InvalidOperationException("WM-DEVICE-DISABLED");
    }

    private static string NormalizePlatform(string value)
        => value.Trim().ToUpperInvariant() switch
        {
            "WINDOWS" => "Windows",
            "ANDROID" => "Android",
            _ => throw new ArgumentException("WM-DEVICE-PLATFORM-INVALID")
        };

    private static string NormalizeMode(string value)
        => value.Trim().ToUpperInvariant() switch
        {
            "SHARED" => ClientDeviceMode.Shared,
            "PERSONAL" => ClientDeviceMode.Personal,
            _ => throw new ArgumentException("WM-DEVICE-MODE-INVALID")
        };

    private static ClientDeviceDto Map(ClientDevice x) => new()
    {
        DeviceId = x.DeviceId,
        DeviceMode = x.DeviceMode,
        Platform = x.Platform,
        Status = x.Status,
        WarehouseCd = x.WarehouseCd,
        AreaCd = x.AreaCd,
        AppVersion = x.AppVersion,
        PlatformVersion = x.PlatformVersion,
        ActivatedAt = x.ActivatedAt,
        LastSeenAt = x.LastSeenAt,
        BatteryPercent = x.BatteryPercent,
        NetworkType = x.NetworkType,
        CurrentUser = x.CurrentUser,
        CurrentTaskNo = x.CurrentTaskNo,
        FullAuthExpiresAt = x.FullAuthExpiresAt,
        RowVersion = x.RowVersion is { Length: > 0 }
            ? Convert.ToBase64String(x.RowVersion)
            : string.Empty
    };

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static string? NullIfWhiteSpace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
