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
    // Same acquisition order as code generation and geometry writes.
    private const string FloorLocks = """
        LOCK TABLE public."Space_CodeRule", public."Space_Site", public."Space_Floor",
            public."Space_Zone", public."Space_Aisle", public."Space_Rack" IN SHARE MODE;
        LOCK TABLE public."Space_Location", public."T_WmsBin" IN SHARE ROW EXCLUSIVE MODE
        """;
    private const string AdoptLocks = """LOCK TABLE public."Space_Location" IN SHARE ROW EXCLUSIVE MODE""";

    internal Task<T> ExecuteFloorAsync<T>(CP6Context context, Func<CancellationToken, Task<T>> body)
        => ExecuteAsync(context, "space-publish:publish", FloorLocks, "SPACE_PUBLISH", body);

    internal Task<T> ExecuteAdoptAsync<T>(CP6Context context, Func<CancellationToken, Task<T>> body)
        => ExecuteAsync(context, "space-publish:adopt", AdoptLocks, "SPACE_ADOPT", body);

    private async Task<T> ExecuteAsync<T>(CP6Context context, string permission, string locks,
        string outcomePrefix, Func<CancellationToken, Task<T>> body)
    {
        if (!ReferenceEquals(context, db) || !administration.Owns(db) || session is null)
            throw new OrderPriceCorrectionException(outcomePrefix + "_AUTHORITY_UNAVAILABLE", 503);
        try
        {
            return await administration.ExecuteCurrentAsync(permission, session.CurrentActor,
                async (actor, token) =>
                {
                    await db.Database.ExecuteSqlRawAsync(locks, token);
                    var result = await body(token);
                    if (await session.CurrentActor(token) != actor.UserId)
                        throw new OrderPriceCorrectionException("REAUTH_REQUIRED", 401);
                    return result;
                }, requestCancellation);
        }
        catch (IamAdministrationExceptionV1 error)
        {
            throw new OrderPriceCorrectionException(error.Message == "IAM_OPERATION_UNKNOWN"
                ? outcomePrefix + "_OUTCOME_UNKNOWN" : error.Message, error.Status, error);
        }
        catch (Exception error) when ((error is DbException or DbUpdateException) && error is not DbUpdateConcurrencyException)
        {
            throw new OrderPriceCorrectionException(outcomePrefix + "_AUTHORITY_UNAVAILABLE", 503, error);
        }
    }
}
