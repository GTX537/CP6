using System.Collections.Concurrent;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Pub;
using CP6.Entity.DomainModels.Sys;
using CP6.WebApi.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CP6.Tests.Sys;

public sealed class LangPublishServiceTests
{
    [Fact]
    public async Task TwoInstances_PublishAndReadThroughSharedStore()
    {
        await using var db = TestHelper.CreateInMemoryContext();
        db.Sys_Langs.Add(new Sys_Lang
        {
            LangKey = "wms.release.ready",
            ZhCN = "就绪",
            En = "Ready",
            Ja = "準備完了",
            Ko = "준비됨",
            Status = "reviewed",
        });
        await db.SaveChangesAsync();

        var services = new ServiceCollection();
        services.AddSingleton(db);
        await using var provider = services.BuildServiceProvider();
        var scopes = provider.GetRequiredService<IServiceScopeFactory>();
        var store = new SharedMemoryFileStore();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["I18n:ServeReviewedOnly"] = "true",
            })
            .Build();
        var first = new LangPublishService(scopes, store, configuration);
        var second = new LangPublishService(scopes, store, configuration);

        var version = await first.PublishAsync(
            "publisher",
            new DateTime(2026, 7, 28, 12, 0, 0, DateTimeKind.Utc));
        var manifest = await second.GetManifestAsync();
        var english = await second.ReadPublishedAsync(version, "en");

        Assert.Equal(version, manifest?.Version);
        Assert.NotNull(english);
        Assert.Contains("\"wms.release.ready\":\"Ready\"", english);
        Assert.True(await second.RollbackAsync(
            version,
            "operator",
            new DateTime(2026, 7, 28, 13, 0, 0, DateTimeKind.Utc)));
    }

    private sealed class SharedMemoryFileStore : IFileStore
    {
        private readonly ConcurrentDictionary<string, byte[]> _objects = new();

        public async Task<string> SaveAsync(Stream content, string storeName)
        {
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy);
            _objects[storeName] = copy.ToArray();
            return storeName;
        }

        public Task<Stream> OpenReadAsync(string storePath)
        {
            if (!_objects.TryGetValue(storePath, out var value))
                throw new FileNotFoundException("Object not found", storePath);
            return Task.FromResult<Stream>(new MemoryStream(value, writable: false));
        }

        public Task DeleteAsync(string storePath)
        {
            _objects.TryRemove(storePath, out _);
            return Task.CompletedTask;
        }

        public bool Exists(string storePath) => _objects.ContainsKey(storePath);
    }
}
