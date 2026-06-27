using CP6.Core.EFDbContext;
using CP6.Core.Services.Space;
using CP6.Entity.DTOs.Space;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CP6.Tests;

/// <summary>
/// F-1 模板服务测试：编码唯一 + clone（新 Id / 同 Params / 不同编码）。
/// </summary>
public class TemplateServiceTests
{
    private static (CP6Context db, TemplateService svc) Make()
    {
        var db = new CP6Context(new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        return (db, new TemplateService(db));
    }

    [Fact]
    public async Task CreateTemplate_DuplicateCode_Throws_E001()
    {
        var (_, svc) = Make();
        await svc.CreateAsync(new TemplateDto { TemplateCode = "T1", TemplateName = "tmpl1", TemplateType = 1, Params = "{}" }, "u");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.CreateAsync(new TemplateDto { TemplateCode = "T1", TemplateName = "tmpl2", TemplateType = 1, Params = "{}" }, "u"));
        Assert.Equal("E-SPACE-001", ex.Message);
    }

    [Fact]
    public async Task CloneTemplate_ProducesNewId_SameParams_DifferentCode()
    {
        var (_, svc) = Make();
        var id = await svc.CreateAsync(
            new TemplateDto { TemplateCode = "T1", TemplateName = "tmpl1", TemplateType = 2, Params = "{\"cols\":4}" }, "u");
        var cloneId = await svc.CloneAsync(id, "u");
        Assert.NotEqual(id, cloneId);
        var list = await svc.ListAsync();
        var clone = list.First(t => t.Id == cloneId);
        Assert.Equal("{\"cols\":4}", clone.Params);
        Assert.Equal(2, clone.TemplateType);
        Assert.Equal("T1-COPY", clone.TemplateCode);
    }

    [Fact]
    public async Task CloneTemplate_CodeCollision_IncrementsNumber()
    {
        var (_, svc) = Make();
        var id = await svc.CreateAsync(
            new TemplateDto { TemplateCode = "T1", TemplateName = "tmpl1", TemplateType = 1, Params = "{}" }, "u");
        await svc.CreateAsync(
            new TemplateDto { TemplateCode = "T1-COPY", TemplateName = "copy", TemplateType = 1, Params = "{}" }, "u");
        var cloneId = await svc.CloneAsync(id, "u");
        var list = await svc.ListAsync();
        var clone = list.First(t => t.Id == cloneId);
        Assert.Equal("T1-COPY2", clone.TemplateCode);
    }
}
