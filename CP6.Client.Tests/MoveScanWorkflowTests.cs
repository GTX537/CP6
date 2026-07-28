using CP6.Client.Api;
using CP6.Client.Core;

namespace CP6.Client.Tests;

public sealed class MoveScanWorkflowTests
{
    private static readonly MobileTask Task = new()
    {
        FromLocationCd = "A-01",
        ProductCd = "P-100",
        ToLocationCd = "B-02",
        Qty = 5,
    };

    [Fact]
    public void Requires_Source_Product_Target_Then_Quantity()
    {
        var workflow = new MoveScanWorkflow();

        workflow.AcceptBarcode(Task, "A-01");
        Assert.Equal(MoveScanStep.Product, workflow.Step);
        workflow.AcceptBarcode(Task, "P-100");
        Assert.Equal(MoveScanStep.TargetLocation, workflow.Step);
        workflow.AcceptBarcode(Task, "B-02");
        Assert.Equal(MoveScanStep.Quantity, workflow.Step);
        workflow.ConfirmQuantity(Task, 5);

        Assert.Equal(MoveScanStep.ReadyToComplete, workflow.Step);
        Assert.Equal(5, workflow.ConfirmedQuantity);
    }

    [Fact]
    public void Allows_Partial_Quantity_And_Optional_Lot()
    {
        var workflow = new MoveScanWorkflow();
        var lotTask = new MobileTask
        {
            FromLocationCd = Task.FromLocationCd,
            ProductCd = Task.ProductCd,
            LotNo = "LOT-01",
            ToLocationCd = Task.ToLocationCd,
            Qty = Task.Qty,
        };
        workflow.Reset(requiresLot: true);
        workflow.AcceptBarcode(lotTask, "A-01");
        workflow.AcceptBarcode(lotTask, "P-100");
        Assert.Equal(MoveScanStep.Lot, workflow.Step);
        workflow.AcceptBarcode(lotTask, "LOT-01");
        workflow.AcceptBarcode(lotTask, "B-02");
        workflow.ConfirmQuantity(lotTask, 3);

        Assert.Equal(3, workflow.ConfirmedQuantity);
        Assert.Equal(MoveScanStep.ReadyToComplete, workflow.Step);
    }

    [Fact]
    public void Mismatch_Does_Not_Advance()
    {
        var workflow = new MoveScanWorkflow();
        Assert.Throws<ScanMismatchException>(() => workflow.AcceptBarcode(Task, "B-02"));
        Assert.Equal(MoveScanStep.SourceLocation, workflow.Step);
    }
}
