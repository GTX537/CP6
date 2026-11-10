using System.Data.Common;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Sys;

public sealed class FieldAuditListOwnerV1(CP6Context db, IamAdministrationV1? administration,
    TenantManagementSessionV1? session, CancellationToken requestCancellation = default)
{
    public async Task<object> ListAsync(string? entityName, string? entityKey, Guid? userId,
        DateTime? from, DateTime? to, int page, int pageSize, CancellationToken ct = default)
    {
        if (administration?.Owns(db) != true || session == null)
            throw new OrderPriceCorrectionException("FIELD_AUDIT_LIST_AUTHORITY_UNAVAILABLE", 503);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, requestCancellation);
        try
        {
            return await administration.ExecuteCurrentAsync<object>("sys-field-audit:query", session.CurrentActor,
                async (actor, token) =>
                {
                    await db.Database.ExecuteSqlRawAsync("""LOCK TABLE public."Sys_FieldAuditLogs" IN SHARE MODE""", token);
        page = Math.Max(1, page);                 // 防 page<=0 → 负数 Skip 抛异常
        pageSize = Math.Clamp(pageSize, 1, 200);  // 防一次拉全表

        var q = db.Sys_FieldAuditLogs.AsQueryable();

        if (!string.IsNullOrWhiteSpace(entityName))
            q = q.Where(x => x.EntityName == entityName);
        if (!string.IsNullOrWhiteSpace(entityKey))
            q = q.Where(x => x.EntityKey == entityKey);
        if (userId.HasValue)
            q = q.Where(x => x.UserId == userId.Value);
        if (from.HasValue)
            q = q.Where(x => x.ChangedAt >= from.Value);
        if (to.HasValue)
            q = q.Where(x => x.ChangedAt < to.Value.AddDays(1));   // to 含当日（右开到次日零点）

        var total = await q.CountAsync(token);
        var raw = await q
            .OrderByDescending(x => x.ChangedAt)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new { x.Id, x.EntityName, x.EntityKey, x.Operation, x.Changes, x.UserId, x.UserName, x.ChangedAt })
            .ToListAsync(token);

        // changeCount 摘要：不返完整 changes（防 200 行×大文本负载，评审 R7）。Count 在内存（JSON 反序列化）。
        var rows = raw.Select(x =>
        {
            var summary = FieldAuditChangesV1.Summarize(x.Changes);
            return new {
            x.Id,
            x.EntityName,
            x.EntityKey,
            x.Operation,
            changeCount = summary.ChangeCount,
            evidenceStatus = summary.EvidenceStatus,
            x.UserId,
            x.UserName,
            x.ChangedAt
            };
        }).ToList();

        var result = new { rows, total };
                    if (await session.CurrentActor(token) != actor.UserId)
                        throw new OrderPriceCorrectionException("REAUTH_REQUIRED", 401);
                    return result;
                }, linked.Token);
        }
        catch (IamAdministrationExceptionV1 error)
        {
            throw new OrderPriceCorrectionException(error.Message == "IAM_OPERATION_UNKNOWN"
                ? "FIELD_AUDIT_LIST_OUTCOME_UNKNOWN" : error.Message, error.Status, error);
        }
        catch (Exception error) when (error is DbException or DbUpdateException)
        { throw new OrderPriceCorrectionException("FIELD_AUDIT_LIST_AUTHORITY_UNAVAILABLE", 503, error); }
    }
}
