namespace CP6.Core.Services.Pub;

/// <summary>本地磁盘文件存储 —— PUB 章06 §3 v1 实现（根目录 Storage:LocalRoot）。云存储留接口另实现。</summary>
public class LocalFileStore : IFileStore
{
    private readonly string _root;

    public LocalFileStore(string root)
    {
        _root = root;
        Directory.CreateDirectory(_root);
    }

    public async Task<string> SaveAsync(Stream content, string storeName)
    {
        var full = Path.Combine(_root, storeName);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        using var fs = File.Create(full);
        await content.CopyToAsync(fs);
        return storeName;   // DB 存相对路径
    }

    public Task<Stream> OpenReadAsync(string storePath)
    {
        var full = Path.Combine(_root, storePath);
        if (!File.Exists(full)) throw new FileNotFoundException("附件不存在", storePath);
        return Task.FromResult<Stream>(File.OpenRead(full));
    }

    public Task DeleteAsync(string storePath)
    {
        var full = Path.Combine(_root, storePath);
        if (File.Exists(full)) File.Delete(full);
        return Task.CompletedTask;
    }

    public bool Exists(string storePath) => File.Exists(Path.Combine(_root, storePath));
}
