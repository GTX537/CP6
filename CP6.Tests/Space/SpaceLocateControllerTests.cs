using CP6.Core.EFDbContext;
using CP6.Core.Services.Space;
using CP6.Entity.DomainModels.Space;
using CP6.WebApi.Controllers.Space;
using CP6.WebApi.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CP6.Tests.Space;

/// <summary>
/// SpaceLocateController 端到端（直构 new SpaceLocateController(svc) 绕过 [Authorize]）。
/// 波5：未命中不再裸 BadRequest(code=400,message="E-SPACE-xxx")，而是 throw BizException
/// 走 BizExceptionMiddleware 按 culture 翻译。断言抛出 + Code 正确。
/// </summary>
public class SpaceLocateControllerTests
{
    private static (SpaceLocateController ctrl, CP6Context db) Make()
    {
        var db = new CP6Context(new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        return (new SpaceLocateController(new SpaceLocateService(db)), db);
    }

    [Fact]
    public async Task Locate_NotFound_ThrowsBizException_601()
    {
        var (ctrl, _) = Make();
        var ex = await Assert.ThrowsAsync<BizException>(() => ctrl.Locate("NOPE-999"));
        Assert.Equal("E-SPACE-601", ex.Code);
    }

    [Fact]
    public async Task Detail_NotFound_ThrowsBizException_004()
    {
        var (ctrl, _) = Make();
        var ex = await Assert.ThrowsAsync<BizException>(() => ctrl.Detail(Guid.NewGuid()));
        Assert.Equal("E-SPACE-004", ex.Code);
    }

    [Fact]
    public async Task Locate_Found_ReturnsOkEnvelope()
    {
        var (ctrl, db) = Make();
        db.Space_Locations.Add(new Space_Location
        {
            Id = Guid.NewGuid(), FloorId = Guid.NewGuid(), LocationCode = "A-03-02-05",
            Placed = true, Status = 1, AbsX = 100, AbsY = 200, AbsZ = 300
        });
        await db.SaveChangesAsync();

        var result = await ctrl.Locate("A-03-02-05");
        Assert.IsType<OkObjectResult>(result);
    }
}
