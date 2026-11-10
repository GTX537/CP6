using CP6.Core.Services.Erp;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Security.Cryptography;

namespace CP6.Core.Services.Wms;

public sealed partial class ClientDeviceService
{
    private readonly IamAdministrationV1? _administration;
    private readonly TenantManagementSessionV1? _session;
    private readonly IPasswordHasher? _hasher;

    private async Task<T> Administer<T>(Func<UserPermissionContext, CancellationToken, Task<T>> body, CancellationToken ct)
    {
        if (_administration?.Owns(_db) != true || _session == null)
            throw new OrderPriceCorrectionException("DEVICE_AUTHORITY_UNAVAILABLE", 503);
        try
        {
            return await _administration.ExecuteCurrentAsync("wms-mobile:device-manage", _session.CurrentActor,
                async (actor, token) =>
                {
                    var result = await body(actor, token);
                    // This operation may deliberately revoke its own device or credential version.
                    await _session.Complete(token);
                    await RequireTenantLifetime(false, token);
                    return result;
                }, ct);
        }
        catch (IamAdministrationExceptionV1 error)
        {
            throw new OrderPriceCorrectionException(error.Message == "IAM_OPERATION_UNKNOWN" ? "DEVICE_OUTCOME_UNKNOWN" : error.Message,
                error.Status, error);
        }
        catch (Exception error) when (!DeviceBusinessFailure(error, ct))
        { throw new OrderPriceCorrectionException("DEVICE_AUTHORITY_UNAVAILABLE", 503, error); }
    }

    private async Task<T> DeviceTransaction<T>(Func<string?, CancellationToken, Task<T>> body, bool authenticated, CancellationToken ct)
    {
        if (!_db.Database.IsNpgsql() || _db.Database.CurrentTransaction != null || _db.ChangeTracker.HasChanges()
            || System.Transactions.Transaction.Current != null || authenticated && _session == null)
            throw new OrderPriceCorrectionException("DEVICE_AUTHORITY_UNAVAILABLE", 503);
        IDbContextTransaction? tx = null;
        var attempted = false;
        try
        {
            tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct);
            await AuthenticationCompletionGateV1.AcquireAsync(_db, ct);
            string? name = null;
            if (authenticated)
            {
                var actor = await _session!.CurrentActor(ct);
                name = await _db.Sys_Users.AsNoTracking().Where(u => u.Id == actor).Select(u => u.UserName).SingleAsync(ct);
            }
            _db.ChangeTracker.Clear();
            var result = await body(name, ct);
            if (authenticated) await _session!.Complete(ct);
            await RequireTenantLifetime(!authenticated, ct);
            ct.ThrowIfCancellationRequested(); attempted = true;
            await tx.CommitAsync(ct);
            return result;
        }
        catch (Exception error)
        {
            Exception? rollback = null;
            if (tx != null) try { await tx.RollbackAsync(CancellationToken.None); } catch (Exception failure) { rollback = failure; }
            _db.ChangeTracker.Clear();
            if (attempted || rollback != null)
                throw new OrderPriceCorrectionException("DEVICE_OUTCOME_UNKNOWN", 503, rollback == null ? error : new AggregateException(error, rollback));
            if (error is IamAdministrationExceptionV1 identity)
                throw new OrderPriceCorrectionException(identity.Message, identity.Status, identity);
            if (DeviceBusinessFailure(error, ct)) throw;
            throw new OrderPriceCorrectionException("DEVICE_AUTHORITY_UNAVAILABLE", 503, error);
        }
        finally { if (tx != null) await tx.DisposeAsync(); }
    }

    private static bool DeviceBusinessFailure(Exception error, CancellationToken ct)
        => error is OrderPriceCorrectionException or ArgumentException
            || error is InvalidOperationException && error.Message.StartsWith("WM-", StringComparison.Ordinal)
            || error is OperationCanceledException && ct.IsCancellationRequested;

    private async Task RequireTenantLifetime(bool anonymous, CancellationToken ct)
    {
        var tenant = await _db.Sys_Tenants.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(t => t.Id == _db.CurrentTenantId, ct);
        if (tenant is { Enable: true } && (tenant.ExpireDate == null || tenant.ExpireDate > DateTime.Now)) return;
        if (anonymous) throw new InvalidOperationException("WM-DEVICE-TENANT-DISABLED");
        throw new OrderPriceCorrectionException("REAUTH_REQUIRED", 401);
    }

    private async Task RequireDeviceScope(UserPermissionContext actor, string? warehouse, string? area, CancellationToken ct)
    {
        if (actor.RoleIds.Contains(1)) return;
        warehouse = NullIfWhiteSpace(warehouse); area = NullIfWhiteSpace(area);
        if (!await _db.WmsRoleScopes.AnyAsync(g => actor.RoleIds.Contains(g.RoleId)
            && (g.WarehouseCd == "*" || warehouse != null && g.WarehouseCd == warehouse)
            && (g.AreaCd == "" || g.AreaCd == "*" || area != null && g.AreaCd == area), ct))
            throw new OrderPriceCorrectionException("DEVICE_SCOPE_FORBIDDEN", 403);
    }

    public async Task SetQuickPinAsync(SetWarehouseQuickPinRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.UserName) || string.IsNullOrWhiteSpace(request.BadgeNo)
            || request.Pin is not { Length: 6 } || request.Pin.Any(ch => !char.IsDigit(ch)))
            throw new ArgumentException("WM-DEVICE-QUICK-PIN-DATA");
        var hasher = _hasher ?? throw new OrderPriceCorrectionException("DEVICE_AUTHORITY_UNAVAILABLE", 503);
        _ = await Administer(async (actor, token) =>
        {
            var user = await _db.Sys_Users.FirstOrDefaultAsync(u => u.UserName == request.UserName, token)
                ?? throw new OrderPriceCorrectionException("WM-DEVICE-USER-NOT-FOUND", 404);
            user.BadgeNo = request.BadgeNo.Trim(); user.QuickPinHash = hasher.Hash(request.Pin);
            user.Modifier = actor.UserName; user.ModifyDate = DateTime.Now;
            await _db.SaveChangesAsync(token); return true;
        }, ct);
    }

    private static void ValidateDevicePublicKey(string raw)
    {
        ReadOnlySpan<char> pem = raw;
        if (!PemEncoding.TryFind(pem, out var fields)) throw new ArgumentException("WM-DEVICE-PUBLIC-KEY-INVALID");
        var label = pem[fields.Label];
        if (!(label.SequenceEqual("PUBLIC KEY") || label.SequenceEqual("RSA PUBLIC KEY"))
            || PemEncoding.TryFind(pem[fields.Location.End.GetOffset(pem.Length)..], out _))
            throw new ArgumentException("WM-DEVICE-PUBLIC-KEY-INVALID");
        try { using var rsa = RSA.Create(); rsa.ImportFromPem(pem[fields.Location]); return; }
        catch (Exception error) when (error is CryptographicException or ArgumentException) { }
        try { using var ec = ECDsa.Create(); ec.ImportFromPem(pem[fields.Location]); }
        catch (Exception error) when (error is CryptographicException or ArgumentException)
        { throw new ArgumentException("WM-DEVICE-PUBLIC-KEY-INVALID", error); }
    }
}
