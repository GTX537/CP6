using System.Text;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Pub;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests;

public class AttachmentServiceTests
{
    private static CP6Context Db()
    {
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new CP6Context(options);
    }

    private sealed class FakeStore : IFileStore
    {
        public int SaveCalls;
        public int DeleteCalls;
        public readonly Dictionary<string, byte[]> Files = new();

        public Task<string> SaveAsync(Stream content, string storeName)
        {
            SaveCalls++;
            using var ms = new MemoryStream();
            content.CopyTo(ms);
            Files[storeName] = ms.ToArray();
            return Task.FromResult(storeName);
        }
        public Task<Stream> OpenReadAsync(string storePath) =>
            Files.TryGetValue(storePath, out var b) ? Task.FromResult<Stream>(new MemoryStream(b)) : throw new FileNotFoundException();
        public Task DeleteAsync(string storePath) { DeleteCalls++; Files.Remove(storePath); return Task.CompletedTask; }
        public bool Exists(string storePath) => Files.ContainsKey(storePath);
    }

    private static MemoryStream Bytes(string s) => new(Encoding.UTF8.GetBytes(s));

    [Fact]
    public async Task Upload_SameHash_ReusesStorePath_SaveOnce()
    {
        using var db = Db();
        var store = new FakeStore();
        var svc = new AttachmentService(db, store);

        var a1 = await svc.UploadAsync(Bytes("same"), "a.txt", "text/plain", "order", "o1", null, "u");
        var a2 = await svc.UploadAsync(Bytes("same"), "b.txt", "text/plain", "order", "o1", null, "u");   // 同内容

        Assert.Equal(a1.StorePath, a2.StorePath);   // 秒传：共享物理路径
        Assert.Equal(1, store.SaveCalls);            // 物理只存一次
        Assert.Equal(2, await db.Pub_Attachments.CountAsync());
    }

    [Fact]
    public async Task Delete_KeepsPhysical_UntilLastRefRemoved()
    {
        using var db = Db();
        var store = new FakeStore();
        var svc = new AttachmentService(db, store);
        var a1 = await svc.UploadAsync(Bytes("same"), "a.txt", null, "order", "o1", null, "u");
        var a2 = await svc.UploadAsync(Bytes("same"), "b.txt", null, "order", "o1", null, "u");

        await svc.DeleteAsync(a1.Id);
        Assert.Equal(0, store.DeleteCalls);          // 还有 a2 引用 → 不物理删
        Assert.True(store.Exists(a1.StorePath));

        await svc.DeleteAsync(a2.Id);
        Assert.Equal(1, store.DeleteCalls);          // 最后一条 → 物理删
        Assert.False(store.Exists(a1.StorePath));
    }

    [Fact]
    public async Task Upload_Oversize_Throws_E061()
    {
        using var db = Db();
        var svc = new AttachmentService(db, new FakeStore(), maxSizeMb: 0);   // 0MB → 任何非空必超
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.UploadAsync(Bytes("x"), "a.txt", null, "order", "o1", null, "u"));
        Assert.Equal("E-PUB-061", ex.Message);
    }

    [Fact]
    public async Task Upload_BadExt_Throws_E062()
    {
        using var db = Db();
        var svc = new AttachmentService(db, new FakeStore(), allowedExt: new[] { "png", "jpg" });
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.UploadAsync(Bytes("x"), "a.exe", null, "order", "o1", null, "u"));
        Assert.Equal("E-PUB-062", ex.Message);
    }

    [Fact]
    public async Task Rebind_FillsBizId_ClearsToken()
    {
        using var db = Db();
        var svc = new AttachmentService(db, new FakeStore());
        await svc.UploadAsync(Bytes("x"), "a.txt", null, "order", null, "draft-123", "u");

        await svc.RebindAsync("draft-123", "o99");

        var att = await db.Pub_Attachments.SingleAsync();
        Assert.Equal("o99", att.BizId);
        Assert.Null(att.DraftToken);
    }
}
