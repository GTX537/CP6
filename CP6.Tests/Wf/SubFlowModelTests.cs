using System.Text.Json;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;

namespace CP6.Tests.Wf;

public class SubFlowModelTests
{
    [Fact]
    public void WfJobKind_SubFlowResume_Constant()
        => Assert.Equal("subFlowResume", WfJobKind.SubFlowResume);

    [Fact]
    public void SubFlowCompletionPolicy_Constants()
    {
        Assert.Equal("all", SubFlowCompletionPolicy.All);
        Assert.Equal("any", SubFlowCompletionPolicy.Any);
    }

    [Fact]
    public void SubFlowLimits_Constants()
    {
        Assert.Equal(8, SubFlowLimits.MaxDepth);
        Assert.Equal(100, SubFlowLimits.DefaultMaxInstances);
    }

    [Fact]
    public void FlowNode_SubFlowFields_DefaultNull()
    {
        var n = new FlowNode();
        Assert.Null(n.SubFlowKey);
        Assert.Null(n.SubVarsInJson);
        Assert.Null(n.SubVarsOutJson);
        Assert.Null(n.SubCollectionVar);
        Assert.Null(n.SubCompletionPolicy);
    }

    [Fact]
    public void Wf_FlowInstance_ParentColumns_DefaultNull()
    {
        var i = new Wf_FlowInstance();
        Assert.Null(i.ParentInstanceId);
        Assert.Null(i.ParentTokenId);
        Assert.Null(i.SubIndex);
    }

    [Fact]
    public void FlowSchema_SubFlowNode_JsonRoundTrip()
    {
        var schema = new FlowSchema
        {
            Start = "s",
            Nodes =
            {
                new FlowNode { Id = "s", Type = "start" },
                new FlowNode
                {
                    Id = "sub", Type = "subFlow", SubFlowKey = "fk-child",
                    SubVarsInJson = "{\"childVar\":\"$.parentVar\"}",
                    SubVarsOutJson = "{\"parentOut\":\"$.childOut\"}",
                    SubCollectionVar = "items", SubCompletionPolicy = "any",
                },
                new FlowNode { Id = "e", Type = "end" },
            },
            Edges = { new FlowEdge { From = "s", To = "sub" }, new FlowEdge { From = "sub", To = "e" } },
        };
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var back = JsonSerializer.Deserialize<FlowSchema>(JsonSerializer.Serialize(schema), opts)!;
        var sub = back.Nodes.Single(n => n.Id == "sub");
        Assert.Equal("fk-child", sub.SubFlowKey);
        Assert.Equal("{\"childVar\":\"$.parentVar\"}", sub.SubVarsInJson);
        Assert.Equal("{\"parentOut\":\"$.childOut\"}", sub.SubVarsOutJson);
        Assert.Equal("items", sub.SubCollectionVar);
        Assert.Equal("any", sub.SubCompletionPolicy);
    }
}
