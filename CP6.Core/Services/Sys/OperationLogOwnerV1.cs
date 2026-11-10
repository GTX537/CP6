using System.Data.Common;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Sys;

public sealed class OperationLogOwnerV1(CP6Context db, IamAdministrationV1? administration,
    TenantManagementSessionV1? session, CancellationToken requestCancellation = default)
{
    public Task<object> ListAsync(int page, int pageSize, string? keyword, CancellationToken ct = default)
        => Execute<object>("query", true, async token =>
        {
            var query = db.Sys_OperLogs.AsQueryable();
            if (!string.IsNullOrEmpty(keyword))
                query = query.Where(l => (l.UserName != null && l.UserName.Contains(keyword))
                    || (l.RequestUrl != null && l.RequestUrl.Contains(keyword))
                    || (l.Controller != null && l.Controller.Contains(keyword)));
            var total = await query.CountAsync(token);
            var data = await query.OrderByDescending(l => l.CreateDate)
                .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(token);
            return new { rows = data, total };
        }, ct);

    public Task<int> ClearAsync(CancellationToken ct = default)
        => Execute("delete", false, async token =>
        {
            db.Sys_OperLogs.RemoveRange(db.Sys_OperLogs);
            return await db.SaveChangesAsync(token);
        }, ct);

    private async Task<T> Execute<T>(string action, bool read, Func<CancellationToken, Task<T>> body,
        CancellationToken ct)
    {
        if (administration?.Owns(db) != true || session == null)
            throw new OrderPriceCorrectionException("OPERLOG_AUTHORITY_UNAVAILABLE", 503);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, requestCancellation);
        try
        {
            return await administration.ExecuteCurrentAsync("operlog:" + action, session.CurrentActor,
                async (actor, token) =>
                {
                    await db.Database.ExecuteSqlRawAsync("LOCK TABLE public.\"Sys_OperLogs\" IN "
                        + (read ? "SHARE" : "SHARE ROW EXCLUSIVE") + " MODE", token);
                    var result = await body(token);
                    if (await session.CurrentActor(token) != actor.UserId)
                        throw new OrderPriceCorrectionException("REAUTH_REQUIRED", 401);
                    return result;
                }, linked.Token);
        }
        catch (IamAdministrationExceptionV1 error)
        {
            throw new OrderPriceCorrectionException(error.Message == "IAM_OPERATION_UNKNOWN"
                ? "OPERLOG_OUTCOME_UNKNOWN" : error.Message, error.Status, error);
        }
        catch (Exception error) when (error is DbException or DbUpdateException)
        { throw new OrderPriceCorrectionException("OPERLOG_AUTHORITY_UNAVAILABLE", 503, error); }
    }
}
