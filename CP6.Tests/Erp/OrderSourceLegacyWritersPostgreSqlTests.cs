using System.Data.Common;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.ErpIntegration;
using CP6.Entity.DomainModels.Erp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private const string ToolCreateOwner = "Main.Erp.ToolOrderLink.v1";

    [CurrentPermissionPostgresFact]
    public async Task Sales_source_tool_create_commits_and_rolls_back_with_native_order_and_original_receipt()
    {
        var probe = new SalesWriterCommitProbe();
        await using var h = await Seed(probe);
        var (authority, service) = await GrantToolLink(h);
        var toolId = await SeedPlate(h);
        var command = await ToolCommand(h, authority, toolId);

        ToolWriterEvidence? aborted = null;
        probe.Observe = async (db, ct) =>
        {
            aborted = await ReadToolSalesWriter(db, command, ct);
            await AssertToolWriterAbsent(h.Tenant, command, aborted);
            throw new InvalidOperationException("模拟版模建单提交前的事务中止");
        };
        Assert.Equal("OPERATIONS_OUTCOME_UNKNOWN",
            (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.CreateOrLinkAsync(command))).Message);
        Assert.NotNull(aborted);
        await AssertToolWriterAbsent(h.Tenant, command, aborted);
        Assert.Equal("UNKNOWN", (await service.OperationAsync(command.OperationId, false)).State);

        await h.Db.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS pg_walinspect SCHEMA public");
        var beforeLsn = await h.Db.Database.SqlQueryRaw<string>("""
            SELECT pg_current_wal_insert_lsn()::text AS "Value"
            """).SingleAsync();
        ToolWriterEvidence? staged = null;
        probe.Observe = async (db, ct) =>
        {
            staged = await ReadToolSalesWriter(db, command, ct);
            await AssertToolWriterAbsent(h.Tenant, command, staged);
        };
        var result = await service.CreateOrLinkAsync(command);
        Assert.NotNull(staged);

        await using var observer = SalesContext(h.Tenant);
        var committed = await ReadToolSalesWriter(observer, command);
        Assert.Equal(result, committed.Receipt);
        Assert.Equal(staged.Receipt, committed.Receipt);
        AssertSameSalesWriter(staged.Source, committed.Source);
        Assert.Equal(staged.Xids, committed.Xids);
        var outer = await UseOuterCommit(observer, beforeLsn, committed.Xids);
        Assert.Equal("COMMITTED", (await service.OperationAsync(command.OperationId, false)).State);
        Assert.Equal(result, await service.CreateOrLinkAsync(command));
        AssertSameSalesWriter(committed.Source, (await ReadToolSalesWriter(observer, command)).Source);
        Assert.Equal(2, await observer.Orders.CountAsync());
        Assert.Equal(1, await ToolLinkCount(h));
        output.WriteLine(JsonSerializer.Serialize(new
        {
            scenario = "tool-sales-commit-and-rollback", command.OperationId, toolId,
            outerCommit = outer, aborted, committed
        }));
    }

    private sealed class SalesWriterCommitProbe : DbTransactionInterceptor
    {
        internal Func<CP6Context, CancellationToken, Task>? Observe { get; set; }

        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (Observe is not { } observe) return result;
            Observe = null;
            var db = Assert.IsType<CP6Context>(eventData.Context);
            Assert.Same(transaction, db.Database.CurrentTransaction!.GetDbTransaction());
            await observe(db, cancellationToken);
            return result;
        }
    }

    private sealed class SalesWriterRow
    {
        public long Revision { get; init; }
        public string Writer { get; init; } = "";
        public byte[] Origin { get; init; } = [];
        public byte[] Body { get; init; } = [];
        public string Digest { get; init; } = "";
        public string Xid { get; init; } = "";
    }

    private sealed record SalesWriterEvidence(SalesWriterRow Row, LegacyOrderSourceSnapshot Snapshot);

    private static async Task<SalesWriterEvidence> ReadSalesWriter(CP6Context db, Order order, string writer,
        CancellationToken ct)
    {
        var row = Assert.Single(await db.Database.SqlQuery<SalesWriterRow>($"""
            SELECT revision AS "Revision",writer AS "Writer",origin AS "Origin",body AS "Body",digest AS "Digest",
              xmin::text AS "Xid"
            FROM cp6_sales_order_source_v1.legacy_originals
            WHERE tenant_id={db.CurrentTenantId} AND order_id={order.Id}
            """).ToArrayAsync(ct));
        Assert.Equal(1L, row.Revision);
        Assert.Equal(writer, row.Writer);
        Assert.Equal(row.Digest, SalesDigest(row.Body));

        var snapshot = JsonSerializer.Deserialize<LegacyOrderSourceSnapshot>(row.Body)!;
        Assert.Equal(order.Id, snapshot.Id);
        Assert.Equal(order.WebOrderNo, snapshot.Number);
        Assert.Equal(Convert.ToBase64String(order.RowVersion!), snapshot.NativeVersion);
        var actualLines = await db.OrderDetails.AsNoTracking().Where(d => d.WebOrderNo == order.WebOrderNo)
            .OrderBy(d => d.WebOrderDetailNo).ThenBy(d => d.Id).ToArrayAsync(ct);
        Assert.Equal(actualLines.Select(d => (d.Id, d.WebOrderDetailNo, Convert.ToBase64String(d.RowVersion!))),
            snapshot.Lines.Select(d => (d.Id, d.Number, d.NativeVersion!)));
        Assert.NotEmpty(snapshot.Lines);
        return new(row, snapshot);
    }

    private static void AssertSameSalesWriter(SalesWriterEvidence original, SalesWriterEvidence current)
    {
        Assert.Equal(original.Row.Revision, current.Row.Revision);
        Assert.Equal(original.Row.Writer, current.Row.Writer);
        Assert.Equal(original.Row.Digest, current.Row.Digest);
        Assert.Equal(original.Row.Xid, current.Row.Xid);
        Assert.Equal(original.Row.Body, current.Row.Body);
        Assert.Equal(original.Row.Origin, current.Row.Origin);
    }

    private static async Task<SalesWriterEvidence> ReadC03SalesWriter(CP6Context db, ErpOrderRequested request,
        CancellationToken ct = default)
    {
        var order = await db.Orders.AsNoTracking().SingleAsync(o => o.CrmRequestId == request.RequestId, ct);
        var quote = await db.Quotations.AsNoTracking().SingleAsync(q => q.QtnNo == request.QuotationKey, ct);
        var inputHash = ErpEventContracts.OrderInputSha256(request);
        Assert.Equal(quote.Id, order.CrmQuotationId);
        Assert.Equal(request.OpportunityId, order.CrmOpportunityId);
        Assert.Equal(request.AccountId, order.CrmAccountId);
        Assert.Equal(request.RequestVersion, order.CrmRequestVersion);
        Assert.Equal(inputHash, order.CrmRequestSha256);
        Assert.Equal(Convert.ToBase64String(quote.RowVersion!), request.QuotationVersion);
        var source = await ReadSalesWriter(db, order, "Order.C03QuotationDraft", ct);
        var origin = JsonSerializer.Deserialize<ErpOrderOrigin>(source.Row.Origin);
        Assert.Equal(new ErpOrderOrigin(request.TenantId, request.OpportunityId, request.AccountId, request.RequestId,
            request.RequestVersion, quote.Id, request.QuotationVersion, inputHash, request.Currency), origin);
        return source;
    }

    private sealed class ToolWrites
    {
        public byte[] Link { get; init; } = [];
        public byte[] Journal { get; init; } = [];
        public string LinkDigest { get; init; } = "";
        public Guid OrderId { get; init; }
        public Guid LineId { get; init; }
        public string ArrangeNo { get; init; } = "";
        public byte[] ToolRowVersion { get; init; } = [];
        public string[] Xids { get; init; } = [];
    }

    private sealed record ToolWriterEvidence(ToolOrderLinkReceiptV1 Receipt, SalesWriterEvidence Source, string[] Xids);

    private static async Task<ToolWriterEvidence> ReadToolSalesWriter(CP6Context db, ToolOrderLinkCommandV1 command,
        CancellationToken ct = default)
    {
        var writes = await db.Database.SqlQuery<ToolWrites>($"""
            SELECT link.body_bytes AS "Link",op.result_bytes AS "Journal",link.body_digest AS "LinkDigest",
                   ord."Id" AS "OrderId",d."Id" AS "LineId",p."ArrangeNo" AS "ArrangeNo",p."RowVersion" AS "ToolRowVersion",
                   ARRAY[link.xmin::text,op.xmin::text,ord.xmin::text,d.xmin::text,p.xmin::text] AS "Xids"
            FROM cp6_tool_order_link_v1.links link
            JOIN public."T_Order" ord ON ord."TenantId"=link.tenant_id AND ord."Id"=link.order_id
            JOIN public."T_OrderDetail" d ON d."TenantId"=link.tenant_id AND d."Id"=link.line_id
            JOIN public."T_PlateMold" p ON p."TenantId"=link.tenant_id AND p."Id"=link.tool_id
            JOIN cp6_main_owner_v1.operations op ON op.tenant_id=link.tenant_id
            WHERE link.tenant_id={db.CurrentTenantId} AND link.tool_id={command.Tool.Id} AND op.owner={ToolCreateOwner}
              AND op.operation_id={command.OperationId} AND op.state='COMMITTED'
            """).SingleAsync(ct);
        Assert.Equal(writes.LinkDigest, SalesDigest(writes.Link));
        Assert.Equal(writes.Link, writes.Journal);
        var receipt = JsonSerializer.Deserialize<ToolOrderLinkReceiptV1>(writes.Link)!;
        Assert.Equal(command.OperationId, receipt.OperationId);
        Assert.Equal(db.CurrentTenantId, receipt.TenantId);
        Assert.Equal(command.Tool, receipt.Tool);
        Assert.Equal("ORDER_CREATED", receipt.Effect);
        Assert.Equal("NATIVE_ORDER_LINK_ONLY", receipt.SourceKind);
        Assert.Equal("NOT_EXECUTED", receipt.PeState);
        Assert.Equal(writes.OrderId, receipt.Order.OrderId);
        Assert.Equal(writes.LineId, receipt.Order.LineId);
        Assert.Equal(writes.ArrangeNo, receipt.Order.ArrangeNumber);
        Assert.Equal(Convert.ToBase64String(writes.ToolRowVersion), receipt.NativeToolRowVersion);

        var order = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == writes.OrderId, ct);
        var source = await ReadSalesWriter(db, order, "Tool.OrderCreate", ct);
        Assert.Equal(receipt.Order.OrderRowVersion, source.Snapshot.NativeVersion);
        var line = Assert.Single(source.Snapshot.Lines);
        Assert.Equal(receipt.Order.LineId, line.Id);
        Assert.Equal(receipt.Order.LineNumber, line.Number);
        Assert.Equal(receipt.Order.LineRowVersion, line.NativeVersion);
        Assert.Equal(writes.Link, source.Row.Origin);
        return new(receipt, source, [..writes.Xids, source.Row.Xid]);
    }

    private async Task AssertToolWriterAbsent(Guid tenant, ToolOrderLinkCommandV1 command, ToolWriterEvidence staged)
    {
        await using var observer = SalesContext(tenant);
        var order = staged.Receipt.Order;
        Assert.False(await observer.Orders.AnyAsync(o => o.Id == order.OrderId));
        Assert.False(await observer.OrderDetails.AnyAsync(d => d.Id == order.LineId));
        Assert.Null(await OrderSourceFacts.ReadOriginal(observer, order.OrderId, null, default));
        var tool = await observer.PlateMolds.AsNoTracking().SingleAsync(p => p.Id == command.Tool.Id);
        Assert.Null(tool.ArrangeNo);
        Assert.Equal(command.ExpectedNativeRowVersion, Convert.ToBase64String(tool.RowVersion!));
        Assert.False(await observer.Database.SqlQuery<bool>($"""
            SELECT (EXISTS(SELECT 1 FROM cp6_tool_order_link_v1.links WHERE tenant_id={tenant} AND tool_id={command.Tool.Id})
                 OR EXISTS(SELECT 1 FROM cp6_main_owner_v1.operations WHERE tenant_id={tenant}
                           AND owner={ToolCreateOwner} AND operation_id={command.OperationId})) AS "Value"
            """).SingleAsync());
    }
}
