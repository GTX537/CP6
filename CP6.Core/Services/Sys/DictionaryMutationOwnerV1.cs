using System.Data.Common;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Sys;

public sealed class DictionaryMutationOwnerV1(CP6Context db, IamAdministrationV1? administration,
    TenantManagementSessionV1? session, CancellationToken requestCancellation = default)
{
    public bool Owns(CP6Context context) => ReferenceEquals(context, db);

    public async Task<T> ExecuteAsync<T>(string action, Func<CancellationToken, Task<T>> body,
        CancellationToken ct = default)
    {
        if (administration?.Owns(db) != true || session == null)
            throw new OrderPriceCorrectionException("DICT_AUTHORITY_UNAVAILABLE", 503);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, requestCancellation);
        try
        {
            return await administration.ExecuteCurrentAsync("dict:" + action, session.CurrentActor,
                async (actor, token) =>
                {
                    await db.Database.ExecuteSqlRawAsync("""
                        LOCK TABLE public."Sys_DictTypes", public."Sys_DictDatas" IN SHARE ROW EXCLUSIVE MODE
                        """, token);
                    var result = await body(token);
                    if (await session.CurrentActor(token) != actor.UserId)
                        throw new OrderPriceCorrectionException("REAUTH_REQUIRED", 401);
                    return result;
                }, linked.Token);
        }
        catch (IamAdministrationExceptionV1 error)
        {
            throw new OrderPriceCorrectionException(error.Message == "IAM_OPERATION_UNKNOWN"
                ? "DICT_OUTCOME_UNKNOWN" : error.Message, error.Status, error);
        }
        catch (Exception error) when (error is DbException or DbUpdateException)
        { throw new OrderPriceCorrectionException("DICT_AUTHORITY_UNAVAILABLE", 503, error); }
    }
}
