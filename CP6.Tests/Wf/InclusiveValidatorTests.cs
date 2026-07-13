using CP6.Core.Services.Wf;

namespace CP6.Tests.Wf;

/// <summary>E-WF-020/021 静态校验（hardening spec §6）。构造模式沿既有 ServiceTaskValidatorTests。</summary>
public class InclusiveValidatorTests
{
    // 合法基准：isplit → a["x > 0"], d[default] → ijoin → end
    private static FlowSchema Valid()
    {
        var s = new FlowSchema
        {
            Start = "s",
            Nodes =
            {
                new FlowNode { Id = "s", Type = "start" },
                new FlowNode { Id = "isplit", Type = "inclusiveSplit" },
                new FlowNode { Id = "a", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = Guid.NewGuid() },
                new FlowNode { Id = "d", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = Guid.NewGuid() },
                new FlowNode { Id = "ijoin", Type = "inclusiveJoin" },
                new FlowNode { Id = "end", Type = "end" },
            },
            Edges =
            {
                new FlowEdge { From = "s", To = "isplit" },
                new FlowEdge { From = "isplit", To = "a", Condition = "x > 0" },
                new FlowEdge { From = "isplit", To = "d" },
                new FlowEdge { From = "a", To = "ijoin" }, new FlowEdge { From = "d", To = "ijoin" },
                new FlowEdge { From = "ijoin", To = "end" },
            },
        };
        return s;
    }

    [Fact]
    public void ValidInclusivePair_Passes()
        => Assert.Empty(FlowSchemaValidator.Validate(Valid()));

    [Fact]
    public void NoDefaultEdge_E_WF_020()
    {
        var s = Valid();
        s.Edges.First(e => e.From == "isplit" && e.To == "d").Condition = "y > 0";   // 两条全带条件，无 default
        Assert.Contains("E-WF-020", FlowSchemaValidator.Validate(s));
    }

    [Fact]
    public void TwoDefaultEdges_E_WF_020()
    {
        var s = Valid();
        s.Edges.First(e => e.From == "isplit" && e.To == "a").Condition = null;      // 两条都无条件
        Assert.Contains("E-WF-020", FlowSchemaValidator.Validate(s));
    }

    [Fact]
    public void SingleOutEdge_E_WF_020()
    {
        var s = Valid();
        s.Edges.RemoveAll(e => e.From == "isplit" && e.To == "a");
        s.Edges.RemoveAll(e => e.From == "a");
        s.Nodes.RemoveAll(n => n.Id == "a");
        Assert.Contains("E-WF-020", FlowSchemaValidator.Validate(s));
    }

    [Fact]
    public void PairedWithParallelJoin_E_WF_021()
    {
        var s = Valid();
        s.Nodes.First(n => n.Id == "ijoin").Type = "parallelJoin";   // 最近公共汇聚类型错
        Assert.Contains("E-WF-021", FlowSchemaValidator.Validate(s));
    }

    [Fact]
    public void OrphanInclusiveJoin_E_WF_021()
    {
        // 无 split 配对的 inclusiveJoin（线性抵达）
        var s = new FlowSchema
        {
            Start = "s",
            Nodes =
            {
                new FlowNode { Id = "s", Type = "start" },
                new FlowNode { Id = "a", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = Guid.NewGuid() },
                new FlowNode { Id = "b", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = Guid.NewGuid() },
                new FlowNode { Id = "ijoin", Type = "inclusiveJoin" },
                new FlowNode { Id = "end", Type = "end" },
            },
            Edges =
            {
                new FlowEdge { From = "s", To = "a" },
                new FlowEdge { From = "a", To = "ijoin" }, new FlowEdge { From = "b", To = "ijoin" },
                new FlowEdge { From = "ijoin", To = "end" },
            },
        };
        Assert.Contains("E-WF-021", FlowSchemaValidator.Validate(s));
    }

    [Fact]
    public void OnBranchReject_BadValue_E_WF_021()
    {
        var s = Valid();
        s.Nodes.First(n => n.Id == "isplit").OnBranchReject = "explode";
        Assert.Contains("E-WF-021", FlowSchemaValidator.Validate(s));
    }

    [Fact]
    public void OnBranchReject_OnNonSplitNode_E_WF_021()
    {
        var s = Valid();
        s.Nodes.First(n => n.Id == "a").OnBranchReject = "prune";    // 写在 approval 上
        Assert.Contains("E-WF-021", FlowSchemaValidator.Validate(s));
    }

    [Fact]
    public void OnBranchReject_ValidValues_Pass()
    {
        var s = Valid();
        s.Nodes.First(n => n.Id == "isplit").OnBranchReject = "prune";
        Assert.Empty(FlowSchemaValidator.Validate(s));
        s.Nodes.First(n => n.Id == "isplit").OnBranchReject = "cascade";
        Assert.Empty(FlowSchemaValidator.Validate(s));
    }
}
