using CP6.Core.Services.Pub;
using CP6.Entity.DomainModels.Pub;

namespace CP6.Tests;

public class CodeGenServiceTests
{
    private static (GenTable t, List<GenColumn> cols) DemoMeta() => (
        new GenTable
        {
            EntityName = "Demo", Module = "Erp", TableName = "Erp_Demo", ResourceKey = "demo",
            SeqBizKey = "DEMO", CodeField = "DemoNo", RoutePath = "api/erp/demo"
        },
        new List<GenColumn>
        {
            new() { Name = "DemoNo", ClrType = "string", Label = "编号", Required = true, Sort = 1 },
            new() { Name = "Amount", ClrType = "decimal?", Label = "金额", Sort = 2 },
        });

    [Fact]
    public void Generate_Entity_ImplementsIDataScoped_WithProps()
    {
        var (t, cols) = DemoMeta();
        var files = new CodeGenService().Generate(t, cols);
        var entity = files["Demo.cs"];

        Assert.Contains("class Demo : BaseEntity, IDataScoped", entity);
        Assert.Contains("public Guid? DeptId { get; set; }", entity);
        Assert.Contains("public string DemoNo { get; set; } = \"\";", entity);
        Assert.Contains("public decimal? Amount { get; set; }", entity);
        Assert.Contains("[Table(\"Erp_Demo\")]", entity);
    }

    [Fact]
    public void Generate_Service_InheritsBaseCrud_WithResourceAndSeq()
    {
        var (t, cols) = DemoMeta();
        var svc = new CodeGenService().Generate(t, cols)["DemoService.cs"];

        Assert.Contains("class DemoService : BaseCrudService<Demo>", svc);
        Assert.Contains("ResourceKey => \"demo\"", svc);
        Assert.Contains("SeqBizKey => \"DEMO\"", svc);
        Assert.Contains("CodeField => \"DemoNo\"", svc);
        Assert.Contains("// <custom>", svc);   // 二次生成保护区块
    }

    [Fact]
    public void Generate_Controller_HasConstPermissionAttributes_AndRoute()
    {
        var (t, cols) = DemoMeta();
        var ctrl = new CodeGenService().Generate(t, cols)["DemoController.cs"];

        Assert.Contains("[Route(\"api/erp/demo\")]", ctrl);
        Assert.Contains("BaseCrudController<Demo, DemoService>", ctrl);
        Assert.Contains("[RequirePermission(\"demo\", \"add\")]", ctrl);
        Assert.Contains("[RequirePermission(\"demo\", \"delete\")]", ctrl);
        Assert.Contains("[FieldMask(\"demo\")]", ctrl);
    }

    [Fact]
    public void Generate_FrontendColumns_CamelCaseProps()
    {
        var (t, cols) = DemoMeta();
        var ts = new CodeGenService().Generate(t, cols)["demo.ts"];

        Assert.Contains("demoColumns", ts);
        Assert.Contains("prop: 'demoNo'", ts);   // camelCase
        Assert.Contains("label: '金额'", ts);
    }

    [Fact]
    public void MergeCustomBlocks_PreservesHandWrittenCode()
    {
        var oldFile = "a\n// <custom>\nMY HAND CODE\n// </custom>\nb";
        var newFile = "x\n// <custom>\n// 自定义业务方法写在此区块内\n// </custom>\ny";

        var merged = CodeGenService.MergeCustomBlocks(oldFile, newFile);

        Assert.Contains("MY HAND CODE", merged);   // 旧手写代码保留
        Assert.Contains("x", merged);               // 新框架代码生效
        Assert.Contains("y", merged);
    }
}
