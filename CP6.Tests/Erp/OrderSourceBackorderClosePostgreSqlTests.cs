using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Entity.DTOs;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task Sales_source_backorder_close_preserves_legacy_http_and_atomically_commits_or_rolls_back_originals()
    {
        var fault = new SalesCloseCommitFault();
        await using var h = await Seed(fault);
        await GrantOrderReads(h);
        await GrantSyntheticBackorderClose(h);
        var id = await h.Db.Orders.Where(o => o.WebOrderNo == h.Number).Select(o => o.Id).SingleAsync();
        await h.Db.OrderDetails.Where(o => o.WebOrderNo == h.Number && o.WebOrderDetailNo == 1)
            .ExecuteUpdateAsync(set => set.SetProperty(o => o.ShippedQty, 1m));
        var initial = (await OrderSourceFacts.ReadNative(h.Db, id, default))!;
        var line = initial.Lines[0];
        Assert.Null(await OrderSourceFacts.ReadOriginal(h.Db, id, null, default));

        fault.Target = (h.Tenant, id, line.Id);
        fault.ObserveBeforeCommit = AssertInitialState;
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new BackorderService(h.Db).CloseRemainingAsync(h.Number, line.Number,
                new BackorderActionRequest { Reason = "synthetic pre-commit abort" }, "price-owner"));
        Assert.Equal(SalesCloseCommitFault.Message, failure.Message);
        AssertCloseWrites(fault.Rows!, line.Id);
        Assert.All(fault.Rows!, row => Assert.Equal(row.OriginalXid, row.ActiveTransactionXid));
        Assert.True(fault.ObservedBeforeCommit);
        h.Db.ChangeTracker.Clear();
        await AssertInitialState();

        await using var api = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(api, h);
        var url = $"api/backorder/{h.Number}/{line.Number}/close-remaining";
        var accepted = await CloseRequest(url);
        Assert.Equal(HttpStatusCode.OK, accepted.Status);
        using var response = JsonDocument.Parse(accepted.Text);
        var dto = response.RootElement.GetProperty("data");
        Assert.Equal(0, response.RootElement.GetProperty("code").GetInt32());
        Assert.Equal("OK", response.RootElement.GetProperty("message").GetString());
        Assert.Equal(h.Number, dto.GetProperty("webOrderNo").GetString());
        Assert.Equal(line.Number, dto.GetProperty("detailNo").GetInt32());
        Assert.Equal(1m, dto.GetProperty("backorderQty").GetDecimal());
        Assert.Equal(0m, dto.GetProperty("remainingQty").GetDecimal());
        Assert.True(!dto.TryGetProperty("newWebOrderNo", out var successor) || successor.ValueKind == JsonValueKind.Null);

        var repeated = await CloseRequest(url);
        Assert.Equal(HttpStatusCode.BadRequest, repeated.Status);
        using var alreadyClosed = JsonDocument.Parse(repeated.Text);
        Assert.Equal(400, alreadyClosed.RootElement.GetProperty("code").GetInt32());
        Assert.Equal("Order detail is already closed for backorder.", alreadyClosed.RootElement.GetProperty("message").GetString());
        var absent = await CloseRequest($"api/backorder/MISSING-SYNTHETIC-42/{line.Number}/close-remaining");
        Assert.Equal(HttpStatusCode.NotFound, absent.Status);
        using var notFound = JsonDocument.Parse(absent.Text);
        Assert.Equal(404, notFound.RootElement.GetProperty("code").GetInt32());

        var reader = SalesReader(h);
        var before = await reader.ReadLegacyAsync(id, 1);
        var after = await reader.ReadLegacyAsync(id, 2);
        Assert.Equal("Backorder.BeforeClose", before.Writer);
        Assert.Equal("Backorder.Close", after.Writer);
        Assert.Equal(line, before.Original!.Lines[0]);
        var closed = after.Original!.Lines[0];
        Assert.Equal(line.Id, closed.Id);
        Assert.Equal(line.Quantity, closed.Quantity);
        Assert.Equal(1m, closed.BackorderQuantity);
        Assert.Equal(2m, closed.ShippedQuantity);
        Assert.NotEqual(line.NativeVersion, closed.NativeVersion);
        Assert.Equal(closed, after.Current.Lines[0]);
        Assert.Equal("UNPROVEN", after.ScheduleCoverage);
        Assert.Equal(1, await h.Db.Orders.CountAsync());

        await using var readTx = await h.Db.Database.BeginTransactionAsync();
        var committed = await ReadSalesCloseWrites(readTx.GetDbTransaction(), h.Tenant, id, line.Id, default);
        AssertCloseWrites(committed, line.Id);
        Assert.Equal(before.OriginalDigest, committed[0].OriginalDigest);
        Assert.Equal(after.OriginalDigest, committed[1].OriginalDigest);
        Assert.NotEqual(fault.Rows![0].LineXid, committed[0].LineXid);
        await readTx.CommitAsync();
        output.WriteLine(JsonSerializer.Serialize(new
        {
            httpPort = api.Origin.Port, httpProcessId = api.ProcessId, orderId = id, lineId = line.Id,
            status = (int)accepted.Status, body = response.RootElement,
            repeated = (int)repeated.Status, absent = (int)absent.Status,
            beforeOrder = before.Original, afterOrder = after.Original, rolledBack = fault.Rows, committed,
            otherConnectionSawOnlyInitialBeforeCommit = fault.ObservedBeforeCommit
        }));

        async Task AssertInitialState()
        {
            await using var observer = SalesContext(h.Tenant);
            Assert.Null(await OrderSourceFacts.ReadOriginal(observer, id, null, default));
            var current = (await OrderSourceFacts.ReadNative(observer, id, default))!.Lines[0];
            Assert.Equal(line, current);
        }

        async Task<(HttpStatusCode Status, string Text)> CloseRequest(string path)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path)
            { Content = JsonContent.Create(new BackorderActionRequest { Reason = "synthetic backorder close" }) };
            request.Headers.Add("Cookie", string.Join("; ", cookies.Select(p => p.Key + "=" + p.Value)));
            request.Headers.Add("X-CSRF-Token", cookies["cp6_csrf"]);
            using var result = await api.Send(request);
            return (result.StatusCode, await result.Content.ReadAsStringAsync());
        }
    }

    [CurrentPermissionPostgresFact]
    public async Task Sales_source_backorder_close_keeps_historical_release_and_leaves_current_supply_unproven()
    {
        await using var h = await Seed();
        await GrantOrderReads(h);
        var store = new OrderSourceParticipant(h.Db);
        var release = await SeedSalesRelease(h, store);
        var scope = release.Receipt.Scope;
        var reader = SalesReader(h);
        var before = await reader.ReadResultAsync(scope.OrderId, scope.LineId, SalesProducer, release.Receipt.OperationKey);
        Assert.Equal("CURRENT", before.Current!.State);

        var close = await new BackorderService(h.Db).CloseRemainingAsync(h.Number, 1,
            new BackorderActionRequest { Reason = "synthetic release source closure" }, "price-owner");
        h.Db.ChangeTracker.Clear();
        Assert.Equal(2m, close.BackorderQty);
        var after = await reader.ReadResultAsync(scope.OrderId, scope.LineId, SalesProducer, release.Receipt.OperationKey);
        Assert.Equal(before.Original, after.Original);
        Assert.Equal("UNPROVEN", after.Current!.State);
        Assert.Equal(1L, after.Current.CurrentSourceVersion);
        Assert.Equal("Backorder.Close", (await reader.ReadLegacyAsync(scope.OrderId)).Writer);
        await AssertOriginalSalesEvents(store, release);
        output.WriteLine(JsonSerializer.Serialize(new { before, after, close, original = release }));
    }

    private static async Task GrantSyntheticBackorderClose(Harness h)
    {
        var menu = await h.Db.Sys_Menus.SingleOrDefaultAsync(m => m.MenuKey == "erp-backorder");
        if (menu is null)
        {
            var menuId = 1 + await h.Db.Sys_Menus.MaxAsync(m => m.MenuId);
            menu = new() { MenuId = menuId, MenuKey = "erp-backorder", MenuName = "synthetic backorder", Enable = true };
            h.Db.Sys_Menus.Add(menu);
        }
        if (!await h.Db.Sys_MenuActions.AnyAsync(a => a.MenuId == menu.MenuId && a.ActionCode == "close"))
            h.Db.Sys_MenuActions.Add(new() { MenuId = menu.MenuId, ActionCode = "close", ActionName = "close" });
        h.Db.Sys_RoleActions.Add(new() { RoleId = 72, MenuId = menu.MenuId, ActionCode = "close" });
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
    }

    private sealed class SalesCloseCommitFault : DbTransactionInterceptor
    {
        internal const string Message = "Synthetic pre-commit rollback";
        internal (Guid Tenant, Guid Order, Guid Line)? Target { get; set; }
        internal SalesCloseWrite[]? Rows { get; private set; }
        internal Func<Task>? ObserveBeforeCommit { get; set; }
        internal bool ObservedBeforeCommit { get; private set; }

        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (Target is not { } target) return result;
            Target = null;
            Rows = await ReadSalesCloseWrites(transaction, target.Tenant, target.Order, target.Line, cancellationToken);
            if (ObserveBeforeCommit is not null)
            {
                await ObserveBeforeCommit();
                ObservedBeforeCommit = true;
            }
            throw new InvalidOperationException(Message);
        }
    }

    private sealed record SalesCloseWrite(Guid LineId, decimal BackorderQuantity, decimal ShippedField,
        string LineXid, long OriginalRevision, string Writer, string OriginalDigest, string OriginalXid,
        string? ActiveTransactionXid, byte[] OriginalBody);

    private static async Task<SalesCloseWrite[]> ReadSalesCloseWrites(DbTransaction tx, Guid tenant, Guid orderId,
        Guid lineId, CancellationToken ct)
    {
        await using var command = tx.Connection!.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            SELECT d."Id",d."BackorderQty",d."ShippedQty",d.xmin::text,
                   original.revision,original.writer,original.digest,original.xmin::text,
                   pg_current_xact_id_if_assigned()::text,original.body
            FROM public."T_OrderDetail" d
            JOIN cp6_sales_order_source_v1.legacy_originals original ON original.tenant_id=d."TenantId"
            WHERE d."TenantId"=@tenant AND d."Id"=@line AND original.order_id=@order
            ORDER BY original.revision
            """;
        foreach (var pair in new[] { ("tenant", tenant), ("line", lineId), ("order", orderId) })
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = pair.Item1;
            parameter.Value = pair.Item2;
            command.Parameters.Add(parameter);
        }

        var results = new List<SalesCloseWrite>();
        await using var rows = await command.ExecuteReaderAsync(ct);
        while (await rows.ReadAsync(ct))
            results.Add(new(rows.GetGuid(0), rows.GetDecimal(1), rows.GetDecimal(2), rows.GetString(3),
                rows.GetInt64(4), rows.GetString(5), rows.GetString(6), rows.GetString(7),
                rows.IsDBNull(8) ? null : rows.GetString(8), rows.GetFieldValue<byte[]>(9)));
        return results.ToArray();
    }

    private static void AssertCloseWrites(SalesCloseWrite[] rows, Guid expectedLine)
    {
        Assert.Equal(2, rows.Length);
        Assert.Equal("Backorder.BeforeClose", rows[0].Writer);
        Assert.Equal("Backorder.Close", rows[1].Writer);
        Assert.Equal(new long[] { 1, 2 }, rows.Select(row => row.OriginalRevision).ToArray());
        // SaveChanges 的保存点可以在业务行上留下子事务 XID，保留该原值。
        Assert.Equal(rows[0].OriginalXid, rows[1].OriginalXid);
        Assert.All(rows, row =>
        {
            Assert.Equal(expectedLine, row.LineId);
            Assert.Equal(1m, row.BackorderQuantity);
            Assert.Equal(2m, row.ShippedField);
            Assert.Equal(row.OriginalDigest, SalesDigest(row.OriginalBody));
        });
    }
}
