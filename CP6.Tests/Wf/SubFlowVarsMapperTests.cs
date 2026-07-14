using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Core.Services.Wf;

namespace CP6.Tests.Wf;

public class SubFlowVarsMapperTests
{
    [Fact]
    public void TryParseMap_ValidStringMap_True()
    {
        Assert.True(SubFlowVarsMapper.TryParseMap("{\"a\":\"$.x\",\"b\":\"$.y.z\"}", out var map));
        Assert.Equal(2, map.Count);
        Assert.Equal("$.x", map["a"]);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("[1,2]")]
    [InlineData("{\"a\":1}")]          // 值必须是字符串路径
    public void TryParseMap_Invalid_False(string bad)
        => Assert.False(SubFlowVarsMapper.TryParseMap(bad, out _));

    [Fact]
    public void TryParseMap_NullOrBlank_TrueEmpty()
    {
        Assert.True(SubFlowVarsMapper.TryParseMap(null, out var m1));
        Assert.Empty(m1);
        Assert.True(SubFlowVarsMapper.TryParseMap("  ", out var m2));
        Assert.Empty(m2);
    }

    [Fact]
    public void ResolveNode_DotPath_PreservesType()
    {
        const string vars = "{\"amount\":42,\"o\":{\"name\":\"zed\",\"ok\":true},\"list\":[1,2]}";
        Assert.Equal(42, SubFlowVarsMapper.ResolveNode("$.amount", vars)!.GetValue<int>());
        Assert.Equal("zed", SubFlowVarsMapper.ResolveNode("$.o.name", vars)!.GetValue<string>());
        Assert.True(SubFlowVarsMapper.ResolveNode("$.o.ok", vars)!.GetValue<bool>());
        Assert.IsType<JsonArray>(SubFlowVarsMapper.ResolveNode("$.list", vars));
        Assert.Null(SubFlowVarsMapper.ResolveNode("$.missing", vars));
        Assert.Null(SubFlowVarsMapper.ResolveNode("$.o.missing.deep", vars));
    }

    [Fact]
    public void BuildChildVars_MapsAndInjectsItem()
    {
        const string parent = "{\"seed\":\"OK\",\"n\":7}";
        var item = JsonNode.Parse("{\"sku\":\"A1\"}");
        var json = SubFlowVarsMapper.BuildChildVars("{\"result\":\"$.seed\",\"num\":\"$.n\"}", parent, item, 2);
        var o = JsonNode.Parse(json)!.AsObject();
        Assert.Equal("OK", o["result"]!.GetValue<string>());
        Assert.Equal(7, o["num"]!.GetValue<int>());
        Assert.Equal("A1", o["item"]!["sku"]!.GetValue<string>());
        Assert.Equal(2, o["itemIndex"]!.GetValue<int>());
    }

    [Fact]
    public void BuildChildVars_SingleInstance_NoItemKeys()
    {
        var json = SubFlowVarsMapper.BuildChildVars(null, "{\"seed\":1}", item: null, itemIndex: null);
        var o = JsonNode.Parse(json)!.AsObject();
        Assert.False(o.ContainsKey("item"));
        Assert.False(o.ContainsKey("itemIndex"));
        Assert.Empty(o);   // null 映射=不传(spec §2.1),子 vars 从空对象起
    }

    [Fact]
    public void BuildOutMerge_Aggregate_ArrayBySubIndex_MissingAsNull()
    {
        var children = new List<(int, string)> { (1, "{\"v\":20}"), (0, "{\"v\":10}"), (2, "{}") };
        var outVars = SubFlowVarsMapper.BuildOutMerge("{\"results\":\"$.v\"}", children, aggregateAsArray: true);
        var arr = Assert.IsType<JsonArray>(outVars["results"]);
        Assert.Equal(10, arr[0]!.GetValue<int>());
        Assert.Equal(20, arr[1]!.GetValue<int>());
        Assert.Null(arr[2]);
    }

    [Fact]
    public void BuildOutMerge_Scalar_SingleChild()
    {
        var outVars = SubFlowVarsMapper.BuildOutMerge("{\"r\":\"$.v\"}", new List<(int, string)> { (0, "{\"v\":\"win\"}") }, aggregateAsArray: false);
        Assert.Equal("win", ((JsonNode)outVars["r"]!).GetValue<string>());
    }

    [Fact]
    public void BuildOutMerge_NullMap_Empty()
        => Assert.Empty(SubFlowVarsMapper.BuildOutMerge(null, new List<(int, string)> { (0, "{}") }, false));
}
