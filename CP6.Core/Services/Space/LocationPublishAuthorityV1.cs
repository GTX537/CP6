using System.Data.Common;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Space;

public sealed class LocationPublishAuthorityV1(
    CP6Context db, IamAdministrationV1 administration, TenantManagementSessionV1? session,
    CancellationToken requestCancellation = default)
{
    internal async Task<T> ExecuteFloorAsync<T>(CP6Context context, Func<CancellationToken, Task<T>> body)
    {
        if (!ReferenceEquals(context, db) || !administration.Owns(db) || session is null)
            throw new OrderPriceCorrectionException("SPACE_PUBLISH_AUTHORITY_UNAVAILABLE", 503);
        try
        {
            return await administration.ExecuteCurrentAsync("space-publish:publish", session.CurrentActor,
                async (actor, token) =>
                {
                    // Same acquisition order as code generation and geometry writes.
                    await db.Database.ExecuteSqlRawAsync("""
                        LOCK TABLE public."Space_CodeRule", public."Space_Site", public."Space_Floor",
                            public."Space_Zone", public."Space_Aisle", public."Space_Rack" IN SHARE MODE;
                        LOCK TABLE public."Space_Location", public."T_WmsBin" IN SHARE ROW EXCLUSIVE MODE
                        """, token);
                    var result = await body(token);
                    if (await session.CurrentActor(token) != actor.UserId)
                        throw new OrderPriceCorrectionException("REAUTH_REQUIRED", 401);
                    return result;
                }, requestCancellation);
        }
        catch (IamAdministrationExceptionV1 error)
        {
            throw new OrderPriceCorrectionException(error.Message == "IAM_OPERATION_UNKNOWN"
                ? "SPACE_PUBLISH_OUTCOME_UNKNOWN" : error.Message, error.Status, error);
        }
        catch (Exception error) when ((error is DbException or DbUpdateException) && error is not DbUpdateConcurrencyException)
        {
            throw new OrderPriceCorrectionException("SPACE_PUBLISH_AUTHORITY_UNAVAILABLE", 503, error);
        }
    }
}
