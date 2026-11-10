using System.Data.Common;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Space;

public sealed class SceneGeometryAuthorityV1(CP6Context db, IamAdministrationV1? administration,
    TenantManagementSessionV1? session, CancellationToken requestCancellation = default)
{
    public async Task<T> ExecuteAsync<T>(CP6Context context, bool importing, Func<CancellationToken, Task<T>> body)
    {
        if (!ReferenceEquals(context, db) || administration?.Owns(db) != true || session == null)
            throw new OrderPriceCorrectionException("SPACE_SCENE_AUTHORITY_UNAVAILABLE", 503);
        try
        {
            return await administration.ExecuteCurrentAsync("space-floor:edit", session.CurrentActor,
                async (actor, token) =>
                {
                    await db.Database.ExecuteSqlRawAsync(importing ? """
                        LOCK TABLE public."Space_Site" IN SHARE MODE;
                        LOCK TABLE public."Space_Floor", public."Space_Zone", public."Space_Aisle", public."Space_Rack",
                            public."Space_Location" IN SHARE ROW EXCLUSIVE MODE
                        """ : """
                        LOCK TABLE public."Space_Rack" IN SHARE MODE;
                        LOCK TABLE public."Space_Location" IN SHARE ROW EXCLUSIVE MODE
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
                ? "SPACE_SCENE_OUTCOME_UNKNOWN" : error.Message, error.Status, error);
        }
        catch (Exception error) when (error is DbException or DbUpdateException)
        { throw new OrderPriceCorrectionException("SPACE_SCENE_AUTHORITY_UNAVAILABLE", 503, error); }
    }
}
