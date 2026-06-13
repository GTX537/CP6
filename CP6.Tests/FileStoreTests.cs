using System.Text;
using CP6.Core.Services.Pub;

namespace CP6.Tests;

public class FileStoreTests
{
    [Fact]
    public async Task Save_Read_Delete_RoundTrip()
    {
        var root = Path.Combine(Path.GetTempPath(), "cp6test_" + Guid.NewGuid());
        try
        {
            var store = new LocalFileStore(root);
            using var content = new MemoryStream(Encoding.UTF8.GetBytes("hello"));
            var path = await store.SaveAsync(content, "ab/cd/h1.txt");

            Assert.True(store.Exists(path));
            using (var read = await store.OpenReadAsync(path))
            using (var sr = new StreamReader(read))
                Assert.Equal("hello", await sr.ReadToEndAsync());

            await store.DeleteAsync(path);
            Assert.False(store.Exists(path));
            await Assert.ThrowsAsync<FileNotFoundException>(() => store.OpenReadAsync(path));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Delete_Missing_NoThrow()
    {
        var root = Path.Combine(Path.GetTempPath(), "cp6test_" + Guid.NewGuid());
        try
        {
            var store = new LocalFileStore(root);
            await store.DeleteAsync("nope/x.txt");   // 不存在 → 不抛
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
