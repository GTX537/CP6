using CP6.Core.Services.Sys;

namespace CP6.Tests;

public class FieldPermServiceTests
{
    private sealed class StubCurrent : ICurrentPermissionContext
    {
        private readonly UserPermissionContext _ctx;
        public StubCurrent(UserPermissionContext ctx) => _ctx = ctx;
        public Task<UserPermissionContext> GetAsync() => Task.FromResult(_ctx);
        public void Invalidate(Guid userId) { }
        public void InvalidateByRole(int roleId) { }
    }

    private sealed class OrderDto
    {
        public decimal? Cost { get; set; }
        public decimal Price { get; set; }
        public string? Memo { get; set; }
    }

    private static UserPermissionContext Ctx(Dictionary<string, int> orderFields) =>
        new() { FieldPerms = { ["order"] = orderFields } };

    [Fact]
    public void MaskHidden_NullsAccess3_OnObjectAndList()
    {
        var svc = new FieldPermService(new StubCurrent(Ctx(new() { ["Cost"] = 3, ["Price"] = 1 })));

        var dto = new OrderDto { Cost = 100, Price = 50 };
        svc.MaskHidden(dto, "order", Ctx(new() { ["Cost"] = 3, ["Price"] = 1 }));
        Assert.Null(dto.Cost);          // 隐藏 → null
        Assert.Equal(50, dto.Price);    // 可读写 → 不动

        var list = new List<OrderDto> { new() { Cost = 9 }, new() { Cost = 7 } };
        svc.MaskHidden(list, "order", Ctx(new() { ["Cost"] = 3 }));
        Assert.Null(list[0].Cost);
        Assert.Null(list[1].Cost);
    }

    [Fact]
    public void MaskHidden_NonNullableHidden_SetsDefault()
    {
        var dto = new OrderDto { Price = 50 };
        new FieldPermService(new StubCurrent(Ctx(new())))
            .MaskHidden(dto, "order", Ctx(new() { ["Price"] = 3 }));
        Assert.Equal(0m, dto.Price);    // 非可空值类型 → default(0)
    }

    [Fact]
    public async Task MaskHiddenAsync_UsesCurrentContext()
    {
        var dto = new OrderDto { Cost = 100, Memo = "x" };
        var svc = new FieldPermService(new StubCurrent(Ctx(new() { ["Cost"] = 3 })));
        await svc.MaskHiddenAsync(dto, "order");
        Assert.Null(dto.Cost);
        Assert.Equal("x", dto.Memo);
    }

    [Fact]
    public void StripReadOnly_RestoresReadonlyAndHidden_FromOriginal()
    {
        var original = new OrderDto { Cost = 100, Price = 50, Memo = "db" };
        var incoming = new OrderDto { Cost = 999, Price = 888, Memo = "hacked" };   // 试图改

        // Cost=只读(2)，Memo=隐藏(3)，Price=可读写(1)
        var ctx = Ctx(new() { ["Cost"] = 2, ["Memo"] = 3, ["Price"] = 1 });
        new FieldPermService(new StubCurrent(ctx)).StripReadOnly(incoming, original, "order", ctx);

        Assert.Equal(100, incoming.Cost);    // 只读 → 还原
        Assert.Equal("db", incoming.Memo);   // 隐藏 → 还原
        Assert.Equal(888, incoming.Price);   // 可读写 → 保留用户输入
    }

    [Fact]
    public void MaskHidden_NoPermsForResource_NoOp()
    {
        var dto = new OrderDto { Cost = 100 };
        new FieldPermService(new StubCurrent(new UserPermissionContext()))
            .MaskHidden(dto, "order", new UserPermissionContext());
        Assert.Equal(100, dto.Cost);   // 无配置 → 不动
    }
}
