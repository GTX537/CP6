using CP6.Core.Services.Integration.StockOutbox;
using CP6.Platform.EntityFramework;
using CP6.Platform.EntityFramework.OutboxV2;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [TenantManagementPostgresTheory]
    [InlineData("lf", true)]
    [InlineData("crlf", true)]
    [InlineData("tampered", false)]
    public async Task Stock_outbox_guard_accepts_original_line_endings_and_rejects_changed_body(string variant, bool accepted)
    {
        await using var h = await Seed();
        var owner = await GrantStocktakeDashboard(h);
        await SeedInventory(h);
        var operation = StockApply();
        await InventoryMover(h, owner).ApplyCommandAsync(operation);
        await using var queue = StockQueue(h);
        var original = await queue.Set<Cp6OutboxMessage>().AsNoTracking().SingleAsync(x => x.TenantId == h.Tenant);
        await using var connection = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await connection.OpenAsync();
        const string function = "cp6_stock_outbox_v1.cp6_outbox_v2_guard()";
        await using var read = new NpgsqlCommand("SELECT pg_get_functiondef(@function::regprocedure), prosrc FROM pg_proc WHERE oid=@function::regprocedure", connection);
        read.Parameters.AddWithValue("function", function);
        string definition;
        string body;
        await using (var row = await read.ExecuteReaderAsync())
        {
            Assert.True(await row.ReadAsync());
            definition = row.GetString(0);
            body = row.GetString(1);
        }
        var lf = body.Replace("\r\n", "\n", StringComparison.Ordinal);
        var replacement = variant switch
        {
            "lf" => lf,
            "crlf" => lf.Replace("\n", "\r\n", StringComparison.Ordinal),
            "tampered" => lf.Replace("CP6_OUTBOX_V2_LEGACY_BYPASS", "CP6_OUTBOX_V2_CHANGED", StringComparison.Ordinal),
            _ => throw new ArgumentOutOfRangeException(nameof(variant))
        };
        if (!accepted) Assert.NotEqual(lf, replacement);
        var publisher = new StockPublisher(OutboxTransportDecisionV2.Confirmed);
        try
        {
            await using (var change = new NpgsqlCommand(definition.Replace(body, replacement, StringComparison.Ordinal), connection))
                await change.ExecuteNonQueryAsync();
            var worker = new StockOutboxWorkerV1(h.Db, StockHost(), publisher);
            if (accepted)
            {
                var result = await worker.RunAsync(h.Tenant, default);
                Assert.Equal("RESOLVED", result.State);
                Assert.Equal(1, result.Completed);
                Assert.Single(publisher.Messages);
                var current = await queue.Set<Cp6OutboxMessage>().AsNoTracking().SingleAsync(x => x.Id == original.Id);
                Assert.Equal(Cp6OutboxStatus.Published, current.Status);
                Assert.Equal(original.MessageId, current.MessageId);
                Assert.Equal(original.Payload.ToArray(), current.Payload.ToArray());
                Assert.NotNull((await queue.Set<StockOutboxRunV1>().SingleAsync(x => x.RequestId == result.RequestId)).CompletedAtUtc);
            }
            else
            {
                var error = await Assert.ThrowsAsync<OutboxProtocolExceptionV2>(() => worker.RunAsync(h.Tenant, default));
                Assert.Equal("CP6_OUTBOX_GUARD_MIGRATION_REQUIRED", error.Code);
                Assert.Empty(publisher.Messages);
                var current = await queue.Set<Cp6OutboxMessage>().AsNoTracking().SingleAsync(x => x.Id == original.Id);
                Assert.Equal(Cp6OutboxStatus.Pending, current.Status);
                Assert.Equal(original.RowVersion, current.RowVersion);
                Assert.Equal(original.MessageId, current.MessageId);
                Assert.Equal("COMMITTED", (await InventoryMover(h, owner).ReadOperationAsync(operation.OperationId)).State);
            }
        }
        finally
        {
            await using var restore = new NpgsqlCommand(definition, connection);
            await restore.ExecuteNonQueryAsync();
        }
    }
}
