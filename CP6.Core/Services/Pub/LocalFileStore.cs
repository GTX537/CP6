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

    public async Task<Stream> OpenReadBoundedAsync(string storePath, long expectedLength, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var stream = await OpenReadAsync(storePath);
        if (stream.Length != expectedLength) { await stream.DisposeAsync(); throw new IOException("File length differs from the original content reference."); }
        return stream;
    }

    internal string ManagedNativePath(string configuredRoot, Guid tenant, string key)
    {
        if (!Path.IsPathFullyQualified(configuredRoot))
            throw new IOException("文件处置存储根目录必须使用绝对路径。");
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_root));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(root, Path.TrimEndingDirectorySeparator(Path.GetFullPath(configuredRoot)), comparison))
            throw new IOException("本地存储路径与独占文件处置目录不匹配。");
        var parts = key.Split('/');
        if (parts.Length != 3 || parts[0] != "native-content-v1" || parts[1] != tenant.ToString("N")
            || !Guid.TryParseExact(parts[2], "N", out var physicalId) || parts[2] != physicalId.ToString("N"))
            throw new IOException("原件不在当前租户的受控文件存储命名空间。");

        var path = Path.Combine(root, parts[0], parts[1], parts[2]);
        for (var directory = new DirectoryInfo(Path.GetDirectoryName(path)!); directory is not null; directory = directory.Parent)
        {
            if (!directory.Exists || directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new IOException("文件处置目录不可用或包含链接。");
        }
        return path;
    }

    internal static Guid ManagedStorageId(string root, bool create)
    {
        var marker = Path.Combine(root, ".cp6-file-disposition-id");
        if (!ExistsManagedFile(marker))
        {
            if (!create) throw new IOException("缺少独占处置目录的存储身份。");
            using var writer = new StreamWriter(new FileStream(marker, FileMode.CreateNew, FileAccess.Write, FileShare.None));
            writer.Write(Guid.NewGuid().ToString("D"));
        }
        if (new FileInfo(marker).Length != 36 || !Guid.TryParseExact(File.ReadAllText(marker), "D", out var id) || id == Guid.Empty)
            throw new IOException("独占处置目录的存储身份无效。");
        return id;
    }

    internal static bool ExistsManagedFile(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                throw new IOException("受控文件定位指向了目录或链接。");
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
    }

}
