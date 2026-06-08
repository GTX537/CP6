using CP6.Core.EFDbContext;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests;

public class MaterialShortageServiceTests
{
    private static CP6Context NewDb()
    {
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new CP6Context(options);
    }

    [Fact]
    public async Task Create_StoresOpenStatus_AndPersistsKeyFields()
    {
        using var db = NewDb();
        var svc = new MaterialShortageService(db);

        var entity = await svc.CreateAsync(new MaterialShortage
        {
            WorkOrderNo = "WO-001",
            RelatedOutboundNo = "OUT-001",
            ProductCd = "MAT-001",
            LotNo = "LOT-001",
            RequiredQty = 25m,
            AvailableQty = 0m,
            Creator = "u",
        });

        var saved = await db.MaterialShortages.SingleAsync();
        Assert.Equal(entity.Id, saved.Id);
        Assert.Equal(MaterialShortageStatus.Open, saved.Status);
        Assert.Equal("WO-001", saved.WorkOrderNo);
        Assert.Equal("OUT-001", saved.RelatedOutboundNo);
        Assert.Equal("MAT-001", saved.ProductCd);
        Assert.Equal("LOT-001", saved.LotNo);
        Assert.Equal(25m, saved.RequiredQty);
        Assert.True(saved.DetectedAt > DateTime.MinValue);
    }

    [Fact]
    public async Task Resolve_TransitionsStatus_SetsResolvedAtAndUserStamp()
    {
        using var db = NewDb();
        var svc = new MaterialShortageService(db);
        var shortage = await svc.CreateAsync(NewShortage("WO-RES", MaterialShortageStatus.Open));

        var resolved = await svc.ResolveAsync(shortage.Id, "filled", "resolver");

        Assert.Equal(MaterialShortageStatus.Resolved, resolved.Status);
        Assert.Equal("filled", resolved.Remark);
        Assert.NotNull(resolved.ResolvedAt);
        Assert.Equal("resolver", resolved.Modifier);
        Assert.NotNull(resolved.ModifyDate);
    }

    [Fact]
    public async Task Resolve_AlreadyTerminal_ThrowsInvalidOperation()
    {
        using var db = NewDb();
        var svc = new MaterialShortageService(db);
        var shortage = await svc.CreateAsync(NewShortage("WO-DIS", MaterialShortageStatus.Dismissed));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.ResolveAsync(shortage.Id, null, "resolver"));
        Assert.Contains("WM-MSG-SHORTAGE-409", ex.Message);
    }

    [Fact]
    public async Task Search_FiltersOpenStatus_ReturnsOnlyOpen()
    {
        using var db = NewDb();
        var svc = new MaterialShortageService(db);
        await svc.CreateAsync(NewShortage("WO-1", MaterialShortageStatus.Open));
        await svc.CreateAsync(NewShortage("WO-2", MaterialShortageStatus.Open));
        await svc.CreateAsync(NewShortage("WO-3", MaterialShortageStatus.Resolved));
        await svc.CreateAsync(NewShortage("WO-4", MaterialShortageStatus.Dismissed));

        var result = await svc.SearchAsync(new MaterialShortageQuery
        {
            Status = MaterialShortageStatus.Open,
            Page = 1,
            PageSize = 50,
        });

        Assert.Equal(2, result.Total);
        Assert.Equal(1, result.PageIndex);
        Assert.Equal(50, result.PageSize);
        Assert.All(result.Items, x => Assert.Equal(MaterialShortageStatus.Open, x.Status));
    }

    private static MaterialShortage NewShortage(string workOrderNo, string status)
        => new()
        {
            WorkOrderNo = workOrderNo,
            ProductCd = "MAT-001",
            RequiredQty = 10m,
            AvailableQty = 0m,
            DetectedAt = DateTime.UtcNow,
            Status = status,
        };
}
