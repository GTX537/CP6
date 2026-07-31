using CP6.Space.Infrastructure;

namespace CP6.Space.IntegrationTests;

public sealed class FileSystemSpaceFileStoreTests
{
    [Fact]
    public async Task Store_commits_reads_and_deletes_only_the_scoped_file()
    {
        var root = NewRoot();
        try
        {
            var store = new FileSystemSpaceFileStore(
                new SpaceFileStorageOptions { RootPath = root });
            var tenantId = Guid.NewGuid();
            var fileId = Guid.NewGuid();
            string storageKey;

            await using (var session = await store.OpenWriteAsync(
                             tenantId,
                             fileId))
            {
                storageKey = session.StorageKey;
                await session.Content.WriteAsync("safe underlay"u8.ToArray());
                await session.CommitAsync();
            }

            Assert.DoesNotContain("safe underlay", storageKey);
            await using (var content =
                         await store.OpenQuarantinedReadAsync(
                             tenantId,
                             fileId,
                             storageKey))
            {
                using var reader = new StreamReader(content);
                Assert.Equal("safe underlay", await reader.ReadToEndAsync());
            }

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => store.OpenQuarantinedReadAsync(
                    Guid.NewGuid(),
                    fileId,
                    storageKey));

            await store.DeleteAsync(tenantId, fileId, storageKey);
            await Assert.ThrowsAsync<FileNotFoundException>(
                () => store.OpenQuarantinedReadAsync(
                    tenantId,
                    fileId,
                    storageKey));
            await store.DeleteAsync(tenantId, fileId, storageKey);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task Aborted_session_does_not_leave_readable_content()
    {
        var root = NewRoot();
        try
        {
            var store = new FileSystemSpaceFileStore(
                new SpaceFileStorageOptions { RootPath = root });
            var tenantId = Guid.NewGuid();
            var fileId = Guid.NewGuid();
            string storageKey;

            await using (var session = await store.OpenWriteAsync(
                             tenantId,
                             fileId))
            {
                storageKey = session.StorageKey;
                await session.Content.WriteAsync("partial"u8.ToArray());
                await session.AbortAsync();
            }

            await Assert.ThrowsAsync<FileNotFoundException>(
                () => store.OpenQuarantinedReadAsync(
                    tenantId,
                    fileId,
                    storageKey));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static string NewRoot() =>
        Path.Combine(
            Path.GetTempPath(),
            $"cp6-space-underlay-{Guid.NewGuid():N}");

    private static void DeleteRoot(string root)
    {
        var fullRoot = Path.GetFullPath(root);
        var tempRoot = Path.GetFullPath(Path.GetTempPath())
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        if (!fullRoot.StartsWith(
                tempRoot,
                StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(fullRoot).StartsWith(
                "cp6-space-underlay-",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Refusing to remove an unexpected test directory.");
        }
        if (Directory.Exists(fullRoot))
            Directory.Delete(fullRoot, recursive: true);
    }
}
