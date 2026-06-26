using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Sys;
using CP6.WebApi.Controllers.Platform;
using CP6.WebApi.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Platform;

/// <summary>
/// 块① TenantController 端到端（直构 new TenantController(svc) 绕过 [RequirePlatformAdmin] 过滤器）。
/// 真实 TenantAdminService + InMemory，跑一遍 5 端点 happy path + 错误码转 BizException。
/// </summary>
public class TenantControllerTests
{
    private sealed class RealHasher : IPasswordHasher
    {
        public string Hash(string plain) => BCrypt.Net.BCrypt.HashPassword(plain);
        public bool Verify(string plain, string hash) => BCrypt.Net.BCrypt.Verify(plain, hash);
        public bool IsHashed(string value) => value.StartsWith("$2");
    }

    private static (TenantController ctrl, ITenantAdminService svc, CP6Context db) Make()
    {
        var tenant = new TenantContext();
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new CP6Context(options, tenant);
        var svc = new TenantAdminService(db, new RealHasher(), new SecurityAuditService(db), tenant);
        return (new TenantController(svc), svc, db);
    }

    private static T OkValue<T>(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return Assert.IsType<T>(ok.Value);
    }

    [Fact]
    public async Task FiveEndpoints_HappyPath()
    {
        var (ctrl, _, _) = Make();

        // POST / → 建租户 → 返 CreateTenantResult。
        var createResult = await ctrl.Create(
            new TenantController.CreateTenantRequest("CTRL", "Ctrl Corp", null, "r", "ctrl_admin"));
        var created = OkValue<CreateTenantResult>(createResult);
        Assert.Equal("ctrl_admin", created.AdminUserName);
        Assert.Equal(16, created.TempPassword.Length);
        var id = created.TenantId;

        // GET / → 列表含该租户。
        var listResult = await ctrl.List(null, null, 1, 20);
        var list = Assert.IsType<OkObjectResult>(listResult).Value!;
        var total = (int)list.GetType().GetProperty("total")!.GetValue(list)!;
        Assert.Equal(1, total);

        // GET /{id} → 详情。
        var getResult = await ctrl.Get(id);
        var detail = OkValue<TenantDetail>(getResult);
        Assert.Equal("CTRL", detail.TenantCode);

        // PUT /{id} → 改名。
        var updateResult = await ctrl.Update(id, new TenantController.UpdateTenantRequest("Ctrl Renamed", null, null));
        Assert.IsType<OkObjectResult>(updateResult);
        Assert.Equal("Ctrl Renamed", (await ctrl.Get(id)) is OkObjectResult ok2
            ? ((TenantDetail)ok2.Value!).TenantName : null);

        // POST /{id}/suspend → 停用。
        Assert.IsType<OkObjectResult>(await ctrl.Suspend(id));
        Assert.False(((TenantDetail)((OkObjectResult)await ctrl.Get(id)).Value!).Enable);

        // POST /{id}/reactivate → 重启用。
        Assert.IsType<OkObjectResult>(await ctrl.Reactivate(id));
        Assert.True(((TenantDetail)((OkObjectResult)await ctrl.Get(id)).Value!).Enable);
    }

    [Fact]
    public async Task Get_NotFound_Returns404()
    {
        var (ctrl, _, _) = Make();
        Assert.IsType<NotFoundResult>(await ctrl.Get(Guid.NewGuid()));
    }

    [Fact]
    public async Task Create_DuplicateCode_ThrowsBizException_ESec033()
    {
        var (ctrl, _, _) = Make();
        await ctrl.Create(new TenantController.CreateTenantRequest("DUP", "a", null, null, "a1"));

        var ex = await Assert.ThrowsAsync<BizException>(
            () => ctrl.Create(new TenantController.CreateTenantRequest("DUP", "b", null, null, "a2")));
        Assert.Equal("E-SEC-033", ex.Code);
    }

    [Fact]
    public async Task Update_NotFound_ThrowsBizException_ESec032()
    {
        var (ctrl, _, _) = Make();
        var ex = await Assert.ThrowsAsync<BizException>(
            () => ctrl.Update(Guid.NewGuid(), new TenantController.UpdateTenantRequest("x", null, null)));
        Assert.Equal("E-SEC-032", ex.Code);
    }

    [Fact]
    public async Task Suspend_NotFound_ThrowsBizException_ESec032()
    {
        var (ctrl, _, _) = Make();
        var ex = await Assert.ThrowsAsync<BizException>(() => ctrl.Suspend(Guid.NewGuid()));
        Assert.Equal("E-SEC-032", ex.Code);
    }
}
