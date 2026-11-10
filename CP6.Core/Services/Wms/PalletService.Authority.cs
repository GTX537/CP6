using System.Data.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public partial class PalletService
{
    private readonly IamAdministrationV1? _administration;
    private readonly TenantManagementSessionV1? _session;
    private readonly CancellationToken _requestCancellation;

    private async Task<T> ExecutePalletCommand<T>(string action, string? userName,
        Func<UserPermissionContext?, string?, CancellationToken, Task<T>> body)
    {
        if (!_db.Database.IsNpgsql()) return await body(null, userName, CancellationToken.None);
        if (_administration?.Owns(_db) != true || _session == null)
            throw new OrderPriceCorrectionException("PALLET_AUTHORITY_UNAVAILABLE", 503);
        try
        {
            return await _administration.ExecuteCurrentAsync("wms-pallet:" + action, _session.CurrentActor,
                async (actor, token) =>
                {
                    // The existing authentication root owns identity and action eligibility.
                    // Native table fences also serialize direct pallet/location/scope writers.
                    await _db.Database.ExecuteSqlRawAsync("""
                        LOCK TABLE public."T_Pallet" IN SHARE ROW EXCLUSIVE MODE;
                        LOCK TABLE public."T_Location", public."T_WmsRoleScope" IN SHARE MODE
                        """, token);
                    var result = await body(actor, actor.UserName, token);
                    if (await _session.CurrentActor(token) != actor.UserId)
                        throw new OrderPriceCorrectionException("REAUTH_REQUIRED", 401);
                    return result;
                }, _requestCancellation);
        }
        catch (IamAdministrationExceptionV1 error)
        {
            throw new OrderPriceCorrectionException(error.Message == "IAM_OPERATION_UNKNOWN"
                ? "PALLET_OUTCOME_UNKNOWN" : error.Message, error.Status, error);
        }
        catch (Exception error) when (error is DbException or DbUpdateException)
        { throw new OrderPriceCorrectionException("PALLET_AUTHORITY_UNAVAILABLE", 503, error); }
    }

    private async Task RequirePalletPosition(UserPermissionContext? actor, string warehouse,
        string location, bool newUse, CancellationToken ct)
    {
        if (actor == null) return;
        var position = await _db.Locations.AsNoTracking()
            .SingleOrDefaultAsync(p => p.WarehouseCd == warehouse && p.LocationCd == location, ct);
        // Historical metadata can still be administered with a warehouse-wide grant.
        // An area-restricted grant requires the actual currently classified position.
        var area = position is { IsDeleted: false, IsInitializationQuarantined: false } ? position.AreaCd : null;
        if (!actor.RoleIds.Contains(1) && !await _db.WmsRoleScopes.AnyAsync(g => actor.RoleIds.Contains(g.RoleId)
            && (g.WarehouseCd == "*" || g.WarehouseCd == warehouse)
            && (g.AreaCd == "" || g.AreaCd == "*" || area != null && g.AreaCd == area), ct))
            throw new OrderPriceCorrectionException("PALLET_SCOPE_FORBIDDEN", 403);
        if (newUse)
        {
            if (position == null || position.IsDeleted || position.IsInitializationQuarantined)
                throw new OrderPriceCorrectionException("WM-V2-LOCATION-NOT-FOUND", 400);
            if (position.IsBlocked)
                throw new OrderPriceCorrectionException("LOCATION_OPERATION_BLOCKED", 409);
        }
    }

    private static void RequirePalletField(UserPermissionContext? actor, string field, bool changed)
    {
        if (changed && actor != null && actor.FieldPerms.TryGetValue("wms-pallet", out var fields)
            && fields.TryGetValue(field, out var access) && access != 1)
            throw new OrderPriceCorrectionException("FIELD_WRITE_FORBIDDEN", 403);
    }

    private static void RequirePalletFields(UserPermissionContext? actor, PalletDto dto, Pallet? before)
    {
        before ??= new Pallet();
        RequirePalletField(actor, nameof(dto.ProductCd), dto.ProductCd != before.ProductCd);
        RequirePalletField(actor, nameof(dto.ProductName), dto.ProductName != before.ProductName);
        RequirePalletField(actor, nameof(dto.LotNo), dto.LotNo != before.LotNo);
        RequirePalletField(actor, nameof(dto.CartonQty), dto.CartonQty != before.CartonQty);
        RequirePalletField(actor, nameof(dto.WeightKg), dto.WeightKg != before.WeightKg);
        RequirePalletField(actor, nameof(dto.HeightMm), dto.HeightMm != before.HeightMm);
        RequirePalletField(actor, nameof(dto.MaxStackLayers), dto.MaxStackLayers != before.MaxStackLayers);
        RequirePalletField(actor, nameof(dto.WarehouseCd), dto.WarehouseCd != before.WarehouseCd);
        RequirePalletField(actor, nameof(dto.LocationCd), dto.LocationCd != before.LocationCd);
        RequirePalletField(actor, nameof(dto.Remarks), dto.Remarks != before.Remarks);
    }
}
