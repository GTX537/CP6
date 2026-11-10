using System.Data.Common;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

// Existing tenant-wide barcode configuration. Target/warehouse admission belongs to its consumers.
internal sealed class BarcodeWriteAuthority(CP6Context db, IamAdministrationV1? administration,
    TenantManagementSessionV1? session)
{
    internal async Task<T> Execute<T>(string? userName,
        Func<string?, CancellationToken, Task<T>> body, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql()) return await body(userName, ct);
        if (administration?.Owns(db) != true || session == null)
            throw new OrderPriceCorrectionException("BARCODE_AUTHORITY_UNAVAILABLE", 503);
        try
        {
            return await administration.ExecuteCurrentAsync("wms-mobile:barcode-manage", session.CurrentActor,
                async (actor, token) =>
                {
                    await db.Database.ExecuteSqlRawAsync("""
                        LOCK TABLE public."T_BarcodeAlias", public."T_BarcodeProfile" IN SHARE ROW EXCLUSIVE MODE
                        """, token);
                    var result = await body(actor.UserName, token);
                    if (await session.CurrentActor(token) != actor.UserId)
                        throw new OrderPriceCorrectionException("REAUTH_REQUIRED", 401);
                    return result;
                }, ct);
        }
        catch (IamAdministrationExceptionV1 error)
        {
            throw new OrderPriceCorrectionException(error.Message == "IAM_OPERATION_UNKNOWN"
                ? "BARCODE_OUTCOME_UNKNOWN" : error.Message, error.Status, error);
        }
        catch (Exception error) when (error is DbException or DbUpdateException)
        { throw new OrderPriceCorrectionException("BARCODE_AUTHORITY_UNAVAILABLE", 503, error); }
    }
}
