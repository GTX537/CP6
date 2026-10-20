using System.Data;
using System.Text.Json;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DTOs;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Erp;

public sealed partial class OrderPriceCorrectionOwnerV1
{
    internal Task<(List<OrderPriceCorrectionItemDto>, int)> ReadListAsync(OrderPriceCorrectionQueryDto input,
        Func<OrderPriceCorrectionQueryDto, IQueryable<OrderDetail>, bool, bool, bool, CancellationToken,
            Task<(List<OrderPriceCorrectionItemDto>, int)>> project, CancellationToken ct = default)
    {
        if (input == null) throw new OrderPriceCorrectionException("PRICE_CORRECTION_INVALID", 400);
        // A service caller must not change filters after field-use validation while a lock awaits.
        var query = JsonSerializer.Deserialize<OrderPriceCorrectionQueryDto>(JsonSerializer.SerializeToUtf8Bytes(input))!;
        return ReadAsync(async (authority, token) =>
        {
            var price = authority.Decision.Fields["UnitPrice"].CanRead;
            var amount = authority.Decision.Fields["Amount"].CanRead;
            if ((!amount && (query.AmountFrom != null || query.AmountTo != null))
                || (!price && query.OnlyProvisional == true))
                throw new OrderPriceCorrectionException("FIELD_READ_FORBIDDEN", 403);
            return await project(query, OrderDepartmentScopeV1.Query(db, authority.Actor, authority.Scope).AsNoTracking(),
                price, amount, price && authority.Decision.Fields["UnitPrice"].CanWrite
                && authority.Decision.Fields["Amount"].CanWrite, token);
        }, ct);
    }

    internal Task<decimal> CalculateAsync(string number, int detailNo, decimal? individual, decimal? set,
        CancellationToken ct = default) => ReadAsync(async (authority, token) =>
    {
        if (!authority.Decision.Fields["UnitPrice"].CanRead || !authority.Decision.Fields["Amount"].CanRead)
            throw new OrderPriceCorrectionException("FIELD_READ_FORBIDDEN", 403);
        var row = await OrderDepartmentScopeV1.Query(db, authority.Actor, authority.Scope).AsNoTracking()
            .Where(d => d.WebOrderNo == number && d.WebOrderDetailNo == detailNo)
            .Select(d => new { d.Quantity, d.SalesPriceDiv }).SingleOrDefaultAsync(token);
        if (row == null) throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN", 403);
        return (row.Quantity ?? 0m) * (row.SalesPriceDiv == "1" ? individual ?? 0m : set ?? 0m);
    }, ct);

    private async Task<T> ReadAsync<T>(Func<Authority, CancellationToken, Task<T>> body, CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct,
            http.HttpContext?.RequestAborted ?? CancellationToken.None);
        ct = linked.Token;
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction != null
            || System.Transactions.Transaction.Current != null || db.ChangeTracker.HasChanges())
            throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE", 503);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            await AuthenticationCompletionGateV1.AcquireAsync(db, ct);
            var authority = await ReadAuthority(ct, true);
            var result = await body(authority, ct);
            var final = await ReadAuthority(ct, true);
            if (final.Source.SourcePermissionVersion != authority.Source.SourcePermissionVersion
                || final.Source.RegistryVersion != authority.Source.RegistryVersion
                || final.OrganizationVersion != authority.OrganizationVersion || final.Scope != authority.Scope)
                throw new OrderPriceCorrectionException("AUTHORITY_CHANGED", 403);
            if (await currentActor(ct) != authority.Source.SubjectId)
                throw new OrderPriceCorrectionException("REAUTH_REQUIRED", 401);
            if (final.Source.TenantExpiresAt <= DateTime.Now)
                throw new OrderPriceCorrectionException("AUTHORITY_CHANGED", 403);
            await tx.CommitAsync(ct);
            return result;
        }
        catch (OrderPriceCorrectionException) { throw; }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (InvalidOperationException error) when (error.Message.StartsWith("E10022:", StringComparison.Ordinal)
            || error.Message.StartsWith("E10013:", StringComparison.Ordinal))
        { throw; } // Preserve established PA090 validation messages only, not provider faults.
        catch (Exception error) { throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE", 503, error); }
    }
}
