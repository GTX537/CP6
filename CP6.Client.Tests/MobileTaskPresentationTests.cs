using CP6.Client.Api;

namespace CP6.Client.Tests;

public sealed class MobileTaskPresentationTests
{
    [Fact]
    public void SourceReference_DistinguishesManualAndLinkedTasks()
    {
        var manual = new MobileTask();
        var replenishment = new MobileTask
        {
            SourceType = "REPLENISH",
            SourceNo = "RPL2026070001"
        };

        Assert.Equal("Manual", manual.SourceReference);
        Assert.Equal("Unlinked", manual.SourceLinkState);
        Assert.Equal(
            "REPLENISH / RPL2026070001",
            replenishment.SourceReference);
        Assert.Equal("Linked", replenishment.SourceLinkState);
    }

    [Fact]
    public void SourceLinkState_ShowsPartialCompletionLineage()
    {
        var original = new MobileTask
        {
            RemainderTaskNo = "MOV2026070002"
        };
        var remainder = new MobileTask
        {
            ParentTaskNo = "MOV2026070001"
        };

        Assert.Equal(
            "Remainder task MOV2026070002",
            original.SourceLinkState);
        Assert.Equal(
            "Remainder of MOV2026070001",
            remainder.SourceLinkState);
    }
}
