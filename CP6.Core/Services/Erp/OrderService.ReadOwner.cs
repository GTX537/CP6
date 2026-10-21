using System.Text.Json;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace CP6.Core.Services.Erp;

public partial class OrderService
{
    private OrderReadOwnerV1 ReadOwner => _readOwner?.Owns(_db) == true ? _readOwner
        : throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE", 503);

    private static OrderQueryDto Freeze(OrderQueryDto input)
    {
        if (input == null || input.Page < 1 || input.PageSize < 1 || input.MaxRows < 1)
            throw new OrderPriceCorrectionException("ORDER_QUERY_INVALID", 400);
        return JsonSerializer.Deserialize<OrderQueryDto>(JsonSerializer.SerializeToUtf8Bytes(input))!;
    }

    private static void GuardSort(OrderQueryDto query, OrderReadOwnerV1.Access access)
    {
        if ((!access.Price && (string.Equals(query.SortField, "individualUnitPrice", StringComparison.OrdinalIgnoreCase)
                              || string.Equals(query.SortField, "setUnitPrice", StringComparison.OrdinalIgnoreCase)))
            || (!access.Amount && string.Equals(query.SortField, "amount", StringComparison.OrdinalIgnoreCase)))
            throw new OrderPriceCorrectionException("FIELD_READ_FORBIDDEN", 403);
    }

    private static void MaskOrderRows(IEnumerable<OrderListItemDto> rows, bool price, bool amount)
    {
        foreach (var row in rows)
        {
            row.PriceReadable = price; row.AmountReadable = amount;
            if (!price) { row.IndividualUnitPrice = null; row.SetUnitPrice = null; row.UnitPriceUnit = null; }
            if (!amount) row.Amount = null;
        }
    }

    private Task<(List<OrderListItemDto>, int)> ReadOrderListAsync(OrderQueryDto input)
    {
        var query = Freeze(input);
        return ReadOwner.ReadAsync("order-list", FieldPermissionUseV1.Read, query.IncludeDeleted == true, async (access, ct) =>
        {
            GuardSort(query, access);
            var (rows, total) = await SearchOrdersCoreAsync(query, access.Rows);
            MaskOrderRows(rows, access.Price, access.Amount);
            return (rows, total);
        });
    }

    private Task<byte[]> ReadOrderCsvAsync(OrderQueryDto input)
    {
        var query = Freeze(input);
        return ReadOwner.ReadAsync("order-list", FieldPermissionUseV1.Export, query.IncludeDeleted == true, (access, ct) =>
        {
            GuardSort(query, access);
            return ExportListCsvCoreAsync(query, access.Rows, access.Price, access.Amount);
        });
    }

    private Task<OrderDto?> ReadOrderAsync(string number, bool deleted) =>
        ReadOwner.ReadAsync("erp-order", FieldPermissionUseV1.Read, deleted,
            (access, ct) => ReadWholeOrder(number, deleted, access, ct));

    private Task<OrderDto?> ReadHaibaiAsync(string? no1, string? no2, string? no3) =>
        ReadOwner.ReadAsync("erp-order", FieldPermissionUseV1.Read, false, async (access, ct) =>
        {
            RequireFullFields(access);
            var rows = access.Rows;
            if (!string.IsNullOrWhiteSpace(no1)) rows = rows.Where(x => x.HaibaiNo1 == no1);
            if (!string.IsNullOrWhiteSpace(no2)) rows = rows.Where(x => x.HaibaiNo2 == no2);
            if (!string.IsNullOrWhiteSpace(no3)) rows = rows.Where(x => x.HaibaiNo3 == no3);
            var number = await rows.OrderBy(d => d.WebOrderNo).ThenBy(d => d.WebOrderDetailNo)
                .Select(d => d.WebOrderNo).FirstOrDefaultAsync(ct);
            return number == null ? null : await ReadWholeOrder(number, false, access, ct);
        });

    private static void RequireFullFields(OrderReadOwnerV1.Access access)
    {
        if (!access.Price || !access.Amount) throw new OrderPriceCorrectionException("FIELD_READ_FORBIDDEN", 403);
    }

    private async Task<OrderDto?> ReadWholeOrder(string number, bool deleted, OrderReadOwnerV1.Access access, CancellationToken ct)
    {
        RequireFullFields(access);
        await LockOrderRead(number, ct);
        // Full edit graph, including header and every requested detail. Never a misleading partial document.
        if (!await access.Headers.AnyAsync(o => o.WebOrderNo == number, ct))
            throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN", 403);
        var all = _db.OrderDetails.AsNoTracking().Where(d => d.WebOrderNo == number && (deleted || !d.IsDeleted));
        if (await all.AnyAsync(d => !access.Rows.Select(r => r.Id).Contains(d.Id), ct))
            throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN", 403);
        return await GetByWebOrderNoCoreAsync(number, deleted);
    }

    private Task<byte[]> ReadOrderReportAsync(IEnumerable<string> input)
    {
        if (input == null) throw new OrderPriceCorrectionException("ORDER_REPORT_INVALID", 400);
        var keys = input.ToArray(); // Freeze before awaiting authority.
        var parsed = keys.Select(key =>
        {
            var split = key?.LastIndexOf('-') ?? -1;
            if (split <= 0 || !int.TryParse(key![(split + 1)..], out var detail))
                throw new OrderPriceCorrectionException("ORDER_REPORT_INVALID", 400);
            return (Number: key![..split], Detail: detail);
        }).ToArray();
        return ReadOwner.ReadAsync("order-list", FieldPermissionUseV1.Download, false, async (access, ct) =>
        {
            RequireFullFields(access);
            foreach (var number in parsed.Select(k => k.Number).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
                await LockOrderRead(number, ct);
            foreach (var key in parsed)
                if (!await access.Rows.AnyAsync(d => d.WebOrderNo == key.Number && d.WebOrderDetailNo == key.Detail, ct))
                    throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN", 403);
            return await ExportOrderReportCoreAsync(keys, access.Rows);
        });
    }

    private async Task LockOrderRead(string number, CancellationToken ct)
    {
        string Table<T>()
        {
            var entity = _db.Model.FindEntityType(typeof(T))!;
            return _db.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.GetTableName()!, entity.GetSchema() ?? "public");
        }
        // Lock parent first, including deleted rows. Blocks FK insertion and header department moves;
        // locking all detail siblings also prevents a deleted row being resurrected during the graph read.
        await _db.Orders.FromSqlRaw($"SELECT * FROM {Table<Order>()} WHERE \"TenantId\"=@tenant AND \"WebOrderNo\"=CAST(@number AS bpchar) FOR UPDATE",
            new NpgsqlParameter("tenant", _db.CurrentTenantId), new NpgsqlParameter("number", number)).AsNoTracking().ToArrayAsync(ct);
        await _db.OrderDetails.FromSqlRaw($"SELECT * FROM {Table<OrderDetail>()} WHERE \"TenantId\"=@tenant AND \"WebOrderNo\"=CAST(@number AS bpchar) ORDER BY \"WebOrderDetailNo\" FOR SHARE",
            new NpgsqlParameter("tenant", _db.CurrentTenantId), new NpgsqlParameter("number", number)).AsNoTracking().ToArrayAsync(ct);
    }
}
