using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests;

public sealed class WmsMobileTaskV1Tests
{
    [Fact]
    public async Task Claim_Is_Atomic_From_Business_State_Perspective()
    {
        var service = Create(out _);
        var task = await service.CreateAsync(Move(), "manager");

        var claimed = await service.ClaimAsync(task.TaskNo, new ClaimTaskRequest
        {
            DeviceId = "device-a",
            RowVersion = task.RowVersion,
        }, "alice");

        Assert.Equal(MobileTaskStatus.InProgress, claimed.Status);
        Assert.Equal("alice", claimed.AssignedTo);
        var conflict = await Assert.ThrowsAsync<MobileTaskConflictException>(() =>
            service.ClaimAsync(task.TaskNo, new ClaimTaskRequest
            {
                DeviceId = "device-b",
                RowVersion = task.RowVersion,
            }, "bob"));
        Assert.Equal("WM-CONFLICT-TASK-CLAIMED", conflict.Code);
    }

    [Fact]
    public async Task Completion_OperationId_Replay_Does_Not_Move_Stock_Twice()
    {
        var service = Create(out var db);
        var created = await service.CreateAsync(Move(), "manager");
        var task = await service.ClaimAsync(created.TaskNo, new ClaimTaskRequest
        {
            DeviceId = "device-a",
            RowVersion = created.RowVersion,
        }, "alice");
        var operationId = Guid.NewGuid();
        var request = new CompleteMoveRequest
        {
            OperationId = operationId,
            RowVersion = task.RowVersion,
            ScannedQty = 5,
            ToLocationCd = "B-02",
        };

        var first = await service.CompleteAsync(task.TaskNo, request, "alice");
        var replay = await service.CompleteAsync(task.TaskNo, request, "alice");

        Assert.Equal(operationId, replay.CompletionOperationId);
        Assert.Equal(first.TaskNo, replay.TaskNo);
        Assert.Equal(2, await db.StockTransactions.CountAsync());
        Assert.Equal(5, (await db.Stocks.SingleAsync(x => x.LocationCd == "A-01")).PhysicalQty);
        Assert.Equal(5, (await db.Stocks.SingleAsync(x => x.LocationCd == "B-02")).PhysicalQty);
    }

    [Fact]
    public async Task Different_Completion_Operation_On_Completed_Task_Is_Stable_Conflict()
    {
        var service = Create(out _);
        var created = await service.CreateAsync(Move(), "manager");
        var task = await service.ClaimAsync(created.TaskNo, new ClaimTaskRequest
        {
            DeviceId = "device-a",
            RowVersion = created.RowVersion,
        }, "alice");
        await service.CompleteAsync(task.TaskNo, new CompleteMoveRequest
        {
            OperationId = Guid.NewGuid(),
            RowVersion = task.RowVersion,
            ScannedQty = 5,
            ToLocationCd = "B-02",
        }, "alice");

        var conflict = await Assert.ThrowsAsync<MobileTaskConflictException>(() =>
            service.CompleteAsync(task.TaskNo, new CompleteMoveRequest
            {
                OperationId = Guid.NewGuid(),
                RowVersion = task.RowVersion,
                ScannedQty = 5,
                ToLocationCd = "B-02",
            }, "alice"));
        Assert.Equal("WM-CONFLICT-TASK-ALREADY-COMPLETED", conflict.Code);
    }

    [Fact]
    public async Task Completion_Rejects_Quantity_Mismatch_Before_Moving_Stock()
    {
        var service = Create(out var db);
        var created = await service.CreateAsync(Move(), "manager");
        var task = await service.ClaimAsync(created.TaskNo, new ClaimTaskRequest
        {
            DeviceId = "device-a",
            RowVersion = created.RowVersion,
        }, "alice");

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CompleteAsync(task.TaskNo, new CompleteMoveRequest
            {
                OperationId = Guid.NewGuid(),
                RowVersion = task.RowVersion,
                ScannedQty = 4,
                ToLocationCd = "B-02",
            }, "alice"));

        Assert.Equal("WM-SCAN-QTY-MISMATCH", error.Message);
        Assert.Empty(await db.StockTransactions.ToListAsync());
    }

    private static MobileTaskV1Service Create(out CP6.Core.EFDbContext.CP6Context db)
    {
        db = TestHelper.CreateInMemoryContext();
        db.Warehouses.Add(new Warehouse
        {
            WarehouseCd = "W01",
            WarehouseName = "Main",
            AllowNegative = false,
        });
        db.Stocks.Add(new Stock
        {
            WarehouseCd = "W01",
            LocationCd = "A-01",
            ProductCd = "P-100",
            LotNo = string.Empty,
            PhysicalQty = 10,
            AvailableQty = 10,
        });
        db.SaveChanges();

        var sequence = new WmsSequenceService(db);
        var stock = new StockMovementService(db, sequence);
        var legacy = new MobileService(db, sequence, stock);
        return new MobileTaskV1Service(db, sequence, stock, legacy);
    }

    private static CreateMoveTaskRequest Move() => new()
    {
        WarehouseCd = "W01",
        FromLocationCd = "A-01",
        ToLocationCd = "B-02",
        ProductCd = "P-100",
        Qty = 5,
    };
}
