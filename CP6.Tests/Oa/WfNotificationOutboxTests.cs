using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Oa;

public sealed class WfNotificationOutboxTests
{
    [Fact]
    public async Task Duplicate_event_key_creates_one_pending_outbox_row()
    {
        await using var connection = WfTestDb.NewSqliteWithSchema();
        await using var db = new CP6Context(new DbContextOptionsBuilder<CP6Context>()
            .UseSqlite(connection).Options);
        var service = new NotificationService(db);
        var user = Guid.NewGuid();
        var instance = Guid.NewGuid();

        await service.CreateOutboxAsync(user, 1, "title", "body", instance, null, "leave",
            "todo:stable", true, true);
        await service.CreateOutboxAsync(user, 1, "title", "body", instance, null, "leave",
            "todo:stable", true, true);
        await db.SaveChangesAsync();

        var row = await db.Wf_Notifications.SingleAsync();
        Assert.Equal("todo:stable", row.EventKey);
        Assert.Equal(0, row.DispatchStatus);
        Assert.True(row.InAppRequested);
        Assert.True(row.EmailRequested);
    }

    [Fact]
    public async Task Rolled_back_operation_exposes_no_outbox_row()
    {
        await using var connection = WfTestDb.NewSqliteWithSchema();
        var options = new DbContextOptionsBuilder<CP6Context>().UseSqlite(connection).Options;

        await using (var db = new CP6Context(options))
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            await new NotificationService(db).CreateOutboxAsync(
                Guid.NewGuid(), 1, "title", "body", Guid.NewGuid(), null, "leave",
                "rolled-back", true, true);
            await db.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        await using var check = new CP6Context(options);
        Assert.False(await check.Wf_Notifications.AnyAsync());
    }
}
