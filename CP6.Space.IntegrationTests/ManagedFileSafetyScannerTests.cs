using System.IO.Compression;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;

namespace CP6.Space.IntegrationTests;

public sealed class ManagedFileSafetyScannerTests
{
    private static readonly Guid TenantId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Malware_hit_is_rejected_with_the_frozen_security_code()
    {
        var scanner = NewScanner(
            "%PDF-malicious"u8.ToArray(),
            SpaceMalwareScanResult.Detected("clamav", "daily-20260726"));

        var result = await scanner.ScanAsync(
            NewRequest(".pdf", "application/pdf"));

        Assert.Equal(FileSafetyDisposition.Rejected, result.Disposition);
        Assert.Equal(SpaceErrorCodes.FileMalwareDetected, result.ResultCode);
        Assert.Equal(SpaceJobFailureKind.Security, result.FailureKind);
    }

    [Fact]
    public async Task Archive_bomb_is_rejected_without_extracting_it()
    {
        var expanded = new byte[4 * 1024 * 1024];
        var bytes = BuildArchive(("xl/worksheets/sheet1.xml", expanded));
        var scanner = NewScanner(bytes);

        var result = await scanner.ScanAsync(NewRequest());

        Assert.Equal(SpaceErrorCodes.FileArchiveBomb, result.ResultCode);
        Assert.Equal(FileSafetyDisposition.Rejected, result.Disposition);
    }

    [Fact]
    public async Task Encrypted_archive_is_rejected()
    {
        var bytes = BuildArchive(("xl/workbook.xml", "<workbook/>"u8.ToArray()));
        MarkArchiveEncrypted(bytes);
        var scanner = NewScanner(bytes);

        var result = await scanner.ScanAsync(NewRequest());

        Assert.Equal(
            SpaceErrorCodes.FileEncryptedUnsupported,
            result.ResultCode);
    }

    [Fact]
    public async Task Archive_path_traversal_is_rejected_as_active_content()
    {
        var bytes = BuildArchive(("../payload.exe", "bad"u8.ToArray()));
        var scanner = NewScanner(bytes);

        var result = await scanner.ScanAsync(NewRequest());

        Assert.Equal(SpaceErrorCodes.FileActiveContent, result.ResultCode);
        Assert.Equal(FileSafetyDisposition.Rejected, result.Disposition);
    }

    [Fact]
    public async Task Office_macro_or_embedded_content_is_rejected()
    {
        var bytes = BuildArchive(
            ("xl/vbaProject.bin", "macro"u8.ToArray()));
        var scanner = NewScanner(bytes);

        var result = await scanner.ScanAsync(NewRequest());

        Assert.Equal(SpaceErrorCodes.FileActiveContent, result.ResultCode);
    }

    [Fact]
    public async Task Corrupt_xlsx_is_an_input_failure()
    {
        var scanner = NewScanner("PK-not-a-valid-archive"u8.ToArray());

        var result = await scanner.ScanAsync(NewRequest());

        Assert.Equal(SpaceErrorCodes.FileCorrupt, result.ResultCode);
        Assert.Equal(SpaceJobFailureKind.Input, result.FailureKind);
    }

    [Fact]
    public async Task Clean_xlsx_passes_after_malware_and_container_checks()
    {
        var bytes = BuildArchive(
            ("[Content_Types].xml", "<Types/>"u8.ToArray()),
            ("xl/workbook.xml", "<workbook/>"u8.ToArray()));
        var scanner = NewScanner(bytes);

        var result = await scanner.ScanAsync(NewRequest());

        Assert.Equal(FileSafetyDisposition.Safe, result.Disposition);
        Assert.Equal("CLEAN", result.ResultCode);
    }

    [Fact]
    public async Task Malware_engine_outage_never_promotes_the_file()
    {
        var bytes = BuildArchive(
            ("xl/workbook.xml", "<workbook/>"u8.ToArray()));
        var scanner = NewScanner(
            bytes,
            SpaceMalwareScanResult.Unavailable("clamav", "unavailable"));

        var result = await scanner.ScanAsync(NewRequest());

        Assert.Equal(FileSafetyDisposition.Deferred, result.Disposition);
        Assert.Equal(SpaceErrorCodes.FileQuarantined, result.ResultCode);
    }

    private static ManagedFileSafetyScanner NewScanner(
        byte[] bytes,
        SpaceMalwareScanResult? malware = null) =>
        new(
            new MemoryFileStore(bytes),
            new FixedMalwareScanner(
                malware ??
                SpaceMalwareScanResult.Clean(
                    "clamav",
                    "daily-20260726")));

    private static FileScanRequest NewRequest(
        string extension = ".xlsx",
        string contentType =
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet") =>
        new(
            TenantId,
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "quarantine/server-generated-key",
            $"input{extension}",
            contentType,
            extension,
            1024,
            new string('a', 64),
            SpaceWorkerSandboxPolicy.FileSafetyDefault);

    private static byte[] BuildArchive(
        params (string Name, byte[] Content)[] entries)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(
                   output,
                   ZipArchiveMode.Create,
                   leaveOpen: true))
        {
            foreach (var item in entries)
            {
                var entry = archive.CreateEntry(
                    item.Name,
                    CompressionLevel.SmallestSize);
                using var content = entry.Open();
                content.Write(item.Content);
            }
        }

        return output.ToArray();
    }

    private static void MarkArchiveEncrypted(byte[] bytes)
    {
        for (var index = 0; index <= bytes.Length - 10; index++)
        {
            if (bytes[index] != 0x50 || bytes[index + 1] != 0x4b)
                continue;
            if (bytes[index + 2] == 0x03 && bytes[index + 3] == 0x04)
                bytes[index + 6] |= 0x01;
            if (bytes[index + 2] == 0x01 && bytes[index + 3] == 0x02)
                bytes[index + 8] |= 0x01;
        }
    }

    private sealed class MemoryFileStore : ISpaceFileStore
    {
        private readonly byte[] _bytes;

        public MemoryFileStore(byte[] bytes)
        {
            _bytes = bytes;
        }

        public Task<Stream> OpenQuarantinedReadAsync(
            Guid tenantId,
            Guid fileId,
            string storageKey,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(
                new MemoryStream(_bytes, writable: false));

        public Task DeleteAsync(
            Guid tenantId,
            Guid fileId,
            string storageKey,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FixedMalwareScanner : ISpaceMalwareScanner
    {
        private readonly SpaceMalwareScanResult _result;

        public FixedMalwareScanner(SpaceMalwareScanResult result)
        {
            _result = result;
        }

        public Task<SpaceMalwareScanResult> ScanAsync(
            FileScanRequest request,
            Stream content,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_result);
    }
}
