using CP6.Core.Services.Sys;

namespace CP6.Tests;

public class PermissionServiceTests
{
    /// <summary>返回固定上下文的桩（不查库）。</summary>
    private sealed class StubCurrent : ICurrentPermissionContext
    {
        private readonly UserPermissionContext _ctx;
        public StubCurrent(UserPermissionContext ctx) => _ctx = ctx;
        public Task<UserPermissionContext> GetAsync() => Task.FromResult(_ctx);
        public void Invalidate(Guid userId) { }
        public void InvalidateByRole(int roleId) { }
    }

    [Fact]
    public async Task HasAction_And_HasMenu_HitAndMiss()
    {
        var ctx = new UserPermissionContext
        {
            ActionKeys = { "order:export" },
            MenuKeys = { "order" }
        };
        var svc = new PermissionService(new StubCurrent(ctx));

        Assert.True(await svc.HasActionAsync("order", "export"));
        Assert.False(await svc.HasActionAsync("order", "delete"));
        Assert.True(await svc.HasMenuAsync("order"));
        Assert.False(await svc.HasMenuAsync("ship"));
    }
}
