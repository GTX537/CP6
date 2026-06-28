using CP6.Core.Services.Wf;

namespace CP6.Tests;

public class FlowSchemaValidatorTests
{
    private static FlowSchema Linear() => new()
    {
        Start = "s",
        Nodes =
        {
            new FlowNode { Id = "s", Type = "start" },
            new FlowNode { Id = "a", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = Guid.NewGuid() },
            new FlowNode { Id = "e", Type = "end" },
        },
        Edges = { new FlowEdge { From = "s", To = "a" }, new FlowEdge { From = "a", To = "e" } },
    };

    [Fact]
    public void Valid_Linear_NoErrors() => Assert.Empty(FlowSchemaValidator.Validate(Linear()));

    [Fact]
    public void MissingStart_Reported()
    {
        var s = Linear(); s.Nodes.RemoveAll(n => n.Type == "start"); s.Start = null;
        s.Edges.RemoveAll(e => e.From == "s");
        Assert.Contains("E-WF-010", FlowSchemaValidator.Validate(s));
    }

    [Fact]
    public void EdgeToUnknownNode_Reported()
    {
        var s = Linear(); s.Edges.Add(new FlowEdge { From = "a", To = "ghost" });
        Assert.Contains("E-WF-010", FlowSchemaValidator.Validate(s));
    }

    [Fact]
    public void ApprovalWithoutStrategy_Reported()
    {
        var s = Linear(); s.Nodes.First(n => n.Id == "a").ApproverStrategy = null;
        Assert.Contains("E-WF-010", FlowSchemaValidator.Validate(s));
    }

    [Fact]
    public void EndUnreachable_Reported()
    {
        var s = Linear(); s.Edges.RemoveAll(e => e.From == "a");   // a 到不了 e
        Assert.Contains("E-WF-010", FlowSchemaValidator.Validate(s));
    }
}
