using CP6.Core.Services.Pub;
using CP6.Core.Services.Sys;
using CP6.Entity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests;

public class BaseCrudServiceTests
{
    // 测试用最小实体 + 上下文（避免污染 CP6Context）
    private sealed class FakeDoc : BaseEntity, IDataScoped
    {
        public Guid? DeptId { get; set; }
        public string? Code { get; set; }
        public decimal? Cost { get; set; }
        public string? Memo { get; set; }
    }

    private sealed class TestContext : DbContext
    {
        public TestContext(DbContextOptions<TestContext> o) : base(o) { }
        public DbSet<FakeDoc> Docs => Set<FakeDoc>();
    }

    private static TestContext NewDb()
    {
        var options = new DbContextOptionsBuilder<TestContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new TestContext(options);
    }

    // 自包含范围过滤桩：本人（Creator==UserName），证明 QueryAsync 走过 scope+ctx
    private sealed class SelfScope : IDataScopeFilter
    {
        public IQueryable<TE> Apply<TE>(IQueryable<TE> query, string resource, UserPermissionContext ctx) where TE : class, IDataScoped
            => query.Where(x => x.Creator == ctx.UserName);
    }

    private sealed class StubCurrent : ICurrentPermissionContext
    {
        private readonly UserPermissionContext _ctx;
        public StubCurrent(UserPermissionContext ctx) => _ctx = ctx;
        public Task<UserPermissionContext> GetAsync() => Task.FromResult(_ctx);
        public Task<UserPermissionContext> PrewarmAsync(Guid userId) => Task.FromResult(_ctx);
        public void Invalidate(Guid userId) { }
        public void InvalidateByRole(int roleId) { }
    }

    private sealed class FakeSeq : ISeqService
    {
        public Task<string> NextAsync(string bizKey) => Task.FromResult($"{bizKey}0001");
    }

    private sealed class DocCrud : BaseCrudService<FakeDoc>
    {
        public DocCrud(DbContext db, IDataScopeFilter s, IFieldPermService f, ICurrentPermissionContext c, ISeqService q)
            : base(db, s, f, c, q) { }
        protected override string ResourceKey => "doc";
        protected override string? SeqBizKey => "DOC";
        protected override string? CodeField => "Code";
    }

    private static DocCrud Make(TestContext db, UserPermissionContext ctx)
    {
        var cur = new StubCurrent(ctx);
        return new DocCrud(db, new SelfScope(), new FieldPermService(cur), cur, new FakeSeq());
    }

    [Fact]
    public async Task Create_AssignsSeqCode_DeptId_Creator()
    {
        using var db = NewDb();
        var dept = Guid.NewGuid();
        var ctx = new UserPermissionContext { UserName = "alice", DeptId = dept };
        var svc = Make(db, ctx);

        var doc = await svc.CreateAsync(new FakeDoc { Memo = "m" });

        Assert.Equal("DOC0001", doc.Code);      // 自动采番
        Assert.Equal(dept, doc.DeptId);          // 部门归属
        Assert.Equal("alice", doc.Creator);      // 创建人
    }

    [Fact]
    public async Task Query_InjectsDataScope_SelfOnly()
    {
        using var db = NewDb();
        db.Docs.AddRange(
            new FakeDoc { Id = Guid.NewGuid(), Creator = "alice" },
            new FakeDoc { Id = Guid.NewGuid(), Creator = "bob" });
        await db.SaveChangesAsync();
        var svc = Make(db, new UserPermissionContext { UserName = "alice" });

        var (rows, total) = await svc.QueryAsync(1, 10);

        Assert.Equal(1, total);
        Assert.Equal("alice", rows[0].Creator);
    }

    [Fact]
    public async Task Update_StripsReadonlyField()
    {
        using var db = NewDb();
        var id = Guid.NewGuid();
        db.Docs.Add(new FakeDoc { Id = id, Creator = "alice", Cost = 100, Memo = "old" });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        // Cost 只读(2) → 用户改 Cost 应被还原；Memo 可写
        var ctx = new UserPermissionContext
        {
            UserName = "alice",
            FieldPerms = { ["doc"] = new() { ["Cost"] = 2 } }
        };
        var svc = Make(db, ctx);

        var updated = await svc.UpdateAsync(new FakeDoc { Id = id, Cost = 999, Memo = "new" });

        Assert.NotNull(updated);
        Assert.Equal(100, updated!.Cost);     // 只读 → 还原
        Assert.Equal("new", updated.Memo);    // 可写 → 生效
        Assert.Equal("alice", updated.Creator); // 创建人保留
    }
}
