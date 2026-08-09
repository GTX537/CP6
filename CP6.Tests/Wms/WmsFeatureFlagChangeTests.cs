using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Wf;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;

namespace CP6.Tests.Wms;

public sealed class WmsFeatureFlagChangeTests
{
    private static readonly Guid Requester =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Approver =
        Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task Submit_RequiresR2AExitEvidenceBeforeSerialEnablement()
    {
        await using var db = NewDb();
        db.Warehouses.Add(new Warehouse
        {
            WarehouseCd = "W01",
            WarehouseName = "Pilot",
        });
        db.WmsFeatureFlags.Add(new WmsFeatureFlag
        {
            WarehouseCd = "W01",
            ProductionMoveEnabled = true,
            SerialLpnEnabled = false,
        });
        await db.SaveChangesAsync();
        var service = Service(db);

        var error = await Assert.ThrowsAsync<WmsFeatureFlagChangeException>(() =>
            service.SubmitAsync(new CreateWmsFeatureFlagChangeRequest
            {
                OperationId = Guid.NewGuid(),
                WarehouseCd = "W01",
                ProductionMoveEnabled = true,
                SerialLpnEnabled = true,
                ScanRetentionDays = 180,
                Reason = "R2B pilot conversion",
                ChangeTicket = "CHG-100",
            }, Requester, "supervisor"));

        Assert.Equal("WM-FEATURE-R2A-EVIDENCE-REQUIRED", error.Code);
        Assert.Empty(db.WmsFeatureFlagChanges);
    }

    [Fact]
    public async Task ApprovedChange_AppliesFlagsAndAuditWithDifferentApprover()
    {
        await using var db = NewDb();
        var flowId = Guid.NewGuid();
        var change = Pending(flowId);
        db.WmsFeatureFlagChanges.Add(change);
        db.WmsFeatureFlags.Add(new WmsFeatureFlag
        {
            WarehouseCd = "W01",
            ProductionMoveEnabled = false,
            SerialLpnEnabled = false,
            ScanRetentionDays = 180,
        });
        await db.SaveChangesAsync();
        var service = Service(db);

        await service.ApplyApprovedAsync(change.Id, Context(flowId, Approver));
        await db.SaveChangesAsync();

        var feature = await db.WmsFeatureFlags.SingleAsync();
        Assert.True(feature.ProductionMoveEnabled);
        Assert.False(feature.SerialLpnEnabled);
        Assert.Equal(365, feature.ScanRetentionDays);
        Assert.Equal(WmsFeatureFlagChangeStatus.Applied, change.Status);
        Assert.Equal(Approver, change.DecidedById);
        Assert.NotNull(change.AppliedAtUtc);
    }

    [Fact]
    public async Task ApprovedChange_WithChangedFeatureVersion_IsMarkedStale()
    {
        await using var db = NewDb();
        var flowId = Guid.NewGuid();
        var change = Pending(flowId);
        change.BaseFeatureRowVersion = Convert.ToBase64String([1]);
        db.WmsFeatureFlagChanges.Add(change);
        db.WmsFeatureFlags.Add(new WmsFeatureFlag
        {
            WarehouseCd = "W01",
            ProductionMoveEnabled = false,
            SerialLpnEnabled = false,
        });
        await db.SaveChangesAsync();
        var service = Service(db);

        await service.ApplyApprovedAsync(change.Id, Context(flowId, Approver));
        await db.SaveChangesAsync();

        Assert.Equal(WmsFeatureFlagChangeStatus.Stale, change.Status);
        Assert.Equal("WM-FEATURE-CHANGE-STALE", change.FailureCode);
        Assert.False((await db.WmsFeatureFlags.SingleAsync()).ProductionMoveEnabled);
    }

    [Fact]
    public async Task Approval_RejectsRequesterAsApprover()
    {
        await using var db = NewDb();
        var flowId = Guid.NewGuid();
        var change = Pending(flowId);
        db.WmsFeatureFlagChanges.Add(change);
        await db.SaveChangesAsync();
        var service = Service(db);

        var error = await Assert.ThrowsAsync<WmsFeatureFlagChangeException>(() =>
            service.ApplyApprovedAsync(change.Id, Context(flowId, Requester)));

        Assert.Equal("WM-FEATURE-APPROVER-SEPARATION", error.Code);
        Assert.Equal(WmsFeatureFlagChangeStatus.Pending, change.Status);
    }

    [Fact]
    public async Task TargetState_RequiresSerialToBeDisabledBeforeMove()
    {
        await using var db = NewDb();
        db.Warehouses.Add(new Warehouse
        {
            WarehouseCd = "W01",
            WarehouseName = "Pilot",
        });
        db.WmsFeatureFlags.Add(new WmsFeatureFlag
        {
            WarehouseCd = "W01",
            ProductionMoveEnabled = true,
            SerialLpnEnabled = true,
        });
        await db.SaveChangesAsync();
        var service = Service(db);

        var error = await Assert.ThrowsAsync<WmsFeatureFlagChangeException>(() =>
            service.SubmitAsync(new CreateWmsFeatureFlagChangeRequest
            {
                OperationId = Guid.NewGuid(),
                WarehouseCd = "W01",
                ProductionMoveEnabled = false,
                SerialLpnEnabled = true,
                ScanRetentionDays = 180,
                Reason = "Stop rollout",
                ChangeTicket = "CHG-101",
            }, Requester, "supervisor"));

        Assert.Equal("WM-FEATURE-SERIAL-REQUIRES-MOVE", error.Code);
    }

    private static WmsFeatureFlagChange Pending(Guid flowId) => new()
    {
        Id = Guid.NewGuid(),
        OperationId = Guid.NewGuid(),
        WarehouseCd = "W01",
        BaseProductionMoveEnabled = false,
        BaseSerialLpnEnabled = false,
        BaseScanRetentionDays = 180,
        BaseFeatureRowVersion = string.Empty,
        TargetProductionMoveEnabled = true,
        TargetSerialLpnEnabled = false,
        TargetScanRetentionDays = 365,
        Reason = "Enable controlled R2A pilot",
        ChangeTicket = "CHG-100",
        Status = WmsFeatureFlagChangeStatus.Pending,
        RequestedById = Requester,
        RequestedAtUtc = DateTime.UtcNow,
        FlowInstanceId = flowId,
    };

    private static ApprovalCallbackContext Context(Guid flowId, Guid actor) => new()
    {
        BizType = WmsFeatureFlagChangeService.ApprovalBizType,
        InstanceId = flowId,
        StarterId = Requester,
        DecidedById = actor,
    };

    private static WmsFeatureFlagChangeService Service(CP6Context db)
        => new(
            db,
            Mock.Of<IApprovalService>(),
            Mock.Of<ITaskCenterService>());

    private static CP6Context NewDb()
    {
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new CP6Context(options, new TenantContext
        {
            CurrentTenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        });
    }
}
