using System.Linq;
using CP6.Core.Services.Wf;
using Xunit;

namespace CP6.Tests;

public class ErrorEdgeSourceValidatorTests
{
    private static FlowSchema Schema(string fromType, bool errorEdgeFromNode, string? timeoutAction = null)
    {
        var s = new FlowSchema
        {
            Start = "s",
            Nodes =
            {
                new FlowNode { Id = "s", Type = "start" },
                new FlowNode { Id = "n", Type = fromType, TimeoutAction = timeoutAction },
                new FlowNode { Id = "h", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = System.Guid.NewGuid() },
                new FlowNode { Id = "end", Type = "end" },
            },
            Edges =
            {
                new FlowEdge { From = "s", To = "n" },
                new FlowEdge { From = "n", To = "end" },   // 非错误出边（满足 E-WF-016）
            },
        };
        if (errorEdgeFromNode) s.Edges.Add(new FlowEdge { From = "n", To = "h", IsError = true });
        return s;
    }

    [Fact]
    public void ApprovalErrorEdge_NowAllowed_NoE017()
    {
        var errs = FlowSchemaValidator.Validate(Schema("approval", errorEdgeFromNode: true, timeoutAction: "errorEdge"));
        Assert.DoesNotContain("E-WF-017", errs);   // approval 现允许 IsError 出边
    }

    [Fact]
    public void SubFlowErrorEdge_NowAllowed_NoE017()
    {
        var errs = FlowSchemaValidator.Validate(Schema("subFlow", errorEdgeFromNode: true));
        Assert.DoesNotContain("E-WF-017", errs);   // 跨 spec 契约：来源集合含 subFlow
    }

    [Fact]
    public void StartErrorEdge_StillRejected_E017()
    {
        var errs = FlowSchemaValidator.Validate(Schema("start", errorEdgeFromNode: true));
        Assert.Contains("E-WF-017", errs);   // 非法来源仍拦
    }

    [Fact]
    public void ApprovalTimeoutErrorEdge_WithoutErrorEdge_E027()
    {
        var errs = FlowSchemaValidator.Validate(Schema("approval", errorEdgeFromNode: false, timeoutAction: "errorEdge"));
        Assert.Contains("E-WF-027", errs);   // 配 errorEdge 但无 IsError 出边
    }

    [Fact]
    public void ApprovalTimeoutErrorEdge_WithErrorEdge_NoE027()
    {
        var errs = FlowSchemaValidator.Validate(Schema("approval", errorEdgeFromNode: true, timeoutAction: "errorEdge"));
        Assert.DoesNotContain("E-WF-027", errs);
    }
}
