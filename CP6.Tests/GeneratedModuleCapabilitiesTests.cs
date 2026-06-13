using CP6.Core.Services.Pub;
using CP6.Entity.DomainModels.Pub;

namespace CP6.Tests;

/// <summary>
/// D-4 章08 §6：验证"生成模块开箱带 PUB 全套能力"。
/// 用生成器产出 Demo 模块，逐项断言八大能力已装配（+ 大括号配平作可编译性代理）。
/// </summary>
public class GeneratedModuleCapabilitiesTests
{
    private static Dictionary<string, string> GenerateDemo()
    {
        var t = new GenTable
        {
            EntityName = "Demo", Module = "Erp", TableName = "Erp_Demo", ResourceKey = "demo",
            SeqBizKey = "DEMO", CodeField = "DemoNo", RoutePath = "api/erp/demo"
        };
        var cols = new List<GenColumn>
        {
            new() { Name = "DemoNo", ClrType = "string", Label = "编号", Required = true, Sort = 1 },
            new() { Name = "Qty", ClrType = "int", Label = "数量", Sort = 2 },
        };
        return new CodeGenService().Generate(t, cols);
    }

    [Fact]
    public void GeneratedModule_CarriesAllCapabilities()
    {
        var f = GenerateDemo();
        var entity = f["Demo.cs"];
        var svc = f["DemoService.cs"];
        var ctrl = f["DemoController.cs"];
        var view = f["DemoView.vue"];

        // ① 部门归属 + 数据权限载体：实体实现 IDataScoped + DeptId
        Assert.Contains("IDataScoped", entity);
        Assert.Contains("DeptId", entity);
        // ② 数据范围注入：Service 继承 BaseCrudService（QueryAsync 自动 Apply 数据范围）
        Assert.Contains("BaseCrudService<Demo>", svc);
        Assert.Contains("ResourceKey => \"demo\"", svc);
        // ③ 采番
        Assert.Contains("SeqBizKey => \"DEMO\"", svc);
        Assert.Contains("CodeField => \"DemoNo\"", svc);
        // ④ 操作权限强校验：增改删均带 [RequirePermission]
        Assert.Contains("[RequirePermission(\"demo\", \"add\")]", ctrl);
        Assert.Contains("[RequirePermission(\"demo\", \"edit\")]", ctrl);
        Assert.Contains("[RequirePermission(\"demo\", \"delete\")]", ctrl);
        // ⑤ 字段权限掩码
        Assert.Contains("[FieldMask(\"demo\")]", ctrl);
        // ⑥ 路由 + 鉴权
        Assert.Contains("[Route(\"api/erp/demo\")]", ctrl);
        Assert.Contains("[Authorize]", ctrl);
        // ⑦ 前端列表页
        Assert.Contains("VolTable", view);
        // ⑧ 二次生成保护
        Assert.Contains("// <custom>", svc);
    }

    [Theory]
    [InlineData("Demo.cs")]
    [InlineData("DemoService.cs")]
    [InlineData("DemoController.cs")]
    public void GeneratedCSharp_HasBalancedBraces(string file)
    {
        var content = GenerateDemo()[file];
        Assert.Equal(content.Count(ch => ch == '{'), content.Count(ch => ch == '}'));   // 大括号配平
    }
}
