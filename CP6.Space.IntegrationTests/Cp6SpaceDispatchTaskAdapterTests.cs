using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using CP6.Space.Application;
using CP6.Space.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.IntegrationTests;

public sealed class Cp6SpaceDispatchTaskAdapterTests
{
    private static readonly Guid TenantId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTime Now =
        new(2026, 8, 2, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Batch_stages_assignments_events_and_idempotency_receipts()
    {
        await using var db = NewDb();
        db.MobileTasks.AddRange(Task("TASK-1", [1, 2, 3, 4]),
            Task("TASK-2", [5, 6, 7, 8]));
        await db.SaveChangesAsync();
        var adapter = new Cp6SpaceDispatchTaskAdapter(
            db,
            new FixedWmsAccessScopeProvider(WmsAccessScope.All));

        var result = await adapter.StageAssignmentsAsync(Command(
            Assignment(1, "TASK-1", "USER-1", [1, 2, 3, 4]),
            Assignment(2, "TASK-2", "USER-2", [5, 6, 7, 8])));
        await db.SaveChangesAsync();

        Assert.Equal(Cp6SpaceDispatchTaskAdapter.AdapterVersion, result.AdapterId);
        Assert.Equal(2, result.Receipts.Count);
        Assert.Equal("USER-1", (await db.MobileTasks.SingleAsync(
            value => value.MobileTaskNo == "TASK-1")).AssignedTo);
        Assert.Equal("USER-2", (await db.MobileTasks.SingleAsync(
            value => value.MobileTaskNo == "TASK-2")).AssignedTo);
        Assert.Equal(2, await db.MobileTaskEvents.CountAsync());
        Assert.Equal(2, await db.TaskCommandReceipts.CountAsync());
        Assert.All(await db.MobileTaskEvents.ToArrayAsync(), value =>
        {
            Assert.Equal("Assigned", value.EventType);
            Assert.Equal("approver", value.UserName);
        });
    }

    [Fact]
    public async Task Stale_second_task_has_zero_effect_on_first_task()
    {
        await using var db = NewDb();
        db.MobileTasks.AddRange(Task("TASK-1", [1, 2, 3, 4]),
            Task("TASK-2", [5, 6, 7, 8]));
        await db.SaveChangesAsync();
        var adapter = new Cp6SpaceDispatchTaskAdapter(
            db,
            new FixedWmsAccessScopeProvider(WmsAccessScope.All));

        var error = await Assert.ThrowsAsync<SpaceDispatchTaskAdapterException>(() =>
            adapter.StageAssignmentsAsync(Command(
                Assignment(1, "TASK-1", "USER-1", [1, 2, 3, 4]),
                Assignment(2, "TASK-2", "USER-2", [9, 9, 9, 9]))));

        Assert.True(error.Stale);
        Assert.Null((await db.MobileTasks.SingleAsync(
            value => value.MobileTaskNo == "TASK-1")).AssignedTo);
        Assert.Null((await db.MobileTasks.SingleAsync(
            value => value.MobileTaskNo == "TASK-2")).AssignedTo);
        Assert.Empty(db.ChangeTracker.Entries<
            CP6.Entity.DomainModels.Wms.MobileTaskEvent>());
        Assert.Empty(db.ChangeTracker.Entries<TaskCommandReceipt>());
    }

    [Fact]
    public async Task Missing_wms_scope_fails_before_any_task_is_changed()
    {
        await using var db = NewDb();
        db.MobileTasks.Add(Task("TASK-1", [1, 2, 3, 4]));
        await db.SaveChangesAsync();
        var adapter = new Cp6SpaceDispatchTaskAdapter(
            db,
            new FixedWmsAccessScopeProvider(WmsAccessScope.None));

        var error = await Assert.ThrowsAsync<SpaceDispatchTaskAdapterException>(() =>
            adapter.StageAssignmentsAsync(Command(
                Assignment(1, "TASK-1", "USER-1", [1, 2, 3, 4]))));

        Assert.False(error.Stale);
        Assert.Equal("SPACE_DISPATCH_TASK_SCOPE_DENIED", error.Code);
        Assert.Null((await db.MobileTasks.SingleAsync()).AssignedTo);
    }

    private static SpaceDispatchTaskAdapterCommand Command(
        params SpaceDispatchTaskAssignmentCommand[] assignments) =>
        new(Guid.NewGuid(), "WH-01", "approver", Now, assignments);

    private static SpaceDispatchTaskAssignmentCommand Assignment(
        int rank,
        string taskId,
        string assignedTo,
        byte[] rowVersion) =>
        new(
            rank,
            Guid.NewGuid(),
            taskId,
            MobileTaskType.Pick,
            2,
            3,
            Convert.ToBase64String(rowVersion),
            "WH-01",
            "A-01",
            assignedTo,
            $"PERSON-{rank}");

    private static MobileTask Task(string taskId, byte[] rowVersion) =>
        new()
        {
            Id = Guid.NewGuid(),
            MobileTaskNo = taskId,
            TaskType = MobileTaskType.Pick,
            WarehouseCd = "WH-01",
            AreaCd = "A-01",
            Status = MobileTaskStatus.Pending,
            ContractVersion = 2,
            ExecutionVersion = 3,
            RowVersion = rowVersion,
        };

    private static CP6Context NewDb()
    {
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new CP6Context(options, new TenantContext
        {
            CurrentTenantId = TenantId,
        });
    }
}
