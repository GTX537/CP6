using System.Buffers.Binary;
using System.Text;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;

namespace CP6.Space.Infrastructure;

public sealed class ManagedFileSafetyScanner : IFileSafetyScanner
{
    private readonly ISpaceFileStore _files;
    private readonly ISpaceMalwareScanner _malware;

    public ManagedFileSafetyScanner(
        ISpaceFileStore files,
        ISpaceMalwareScanner malware)
    {
        _files = files;
        _malware = malware;
    }

    public async Task<FileSafetyResult> ScanAsync(
        FileScanRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        await using var malwareInput = await _files.OpenQuarantinedReadAsync(
            request.TenantId,
            request.FileId,
            request.StorageKey,
            cancellationToken);
        if (!malwareInput.CanRead)
            return Deferred("The isolated file cannot be read.");

        var malware = await _malware.ScanAsync(
            request,
            malwareInput,
            cancellationToken);
        if (malware.Disposition == SpaceMalwareDisposition.Unavailable)
        {
            return FileSafetyResult.Defer(
                malware.Engine,
                malware.SignatureVersion,
                malware.SanitizedSummary);
        }
        if (malware.Disposition == SpaceMalwareDisposition.Detected)
        {
            return FileSafetyResult.Reject(
                SpaceErrorCodes.FileMalwareDetected,
                SpaceJobFailureKind.Security,
                malware.Engine,
                malware.SignatureVersion,
                malware.SanitizedSummary);
        }

        if (!string.Equals(
                request.Extension,
                ".xlsx",
                StringComparison.OrdinalIgnoreCase))
        {
            return FileSafetyResult.Clean(
                malware.Engine,
                malware.SignatureVersion);
        }

        await using var archiveInput = await _files.OpenQuarantinedReadAsync(
            request.TenantId,
            request.FileId,
            request.StorageKey,
            cancellationToken);
        var archive = await SpaceZipSafetyInspector.InspectAsync(
            archiveInput,
            cancellationToken);
        return archive switch
        {
            SpaceZipSafetyResult.Clean => FileSafetyResult.Clean(
                $"{malware.Engine}+managed-zip",
                malware.SignatureVersion),
            SpaceZipSafetyResult.Encrypted => FileSafetyResult.Reject(
                SpaceErrorCodes.FileEncryptedUnsupported,
                SpaceJobFailureKind.Security,
                $"{malware.Engine}+managed-zip",
                malware.SignatureVersion,
                "Encrypted archive content is not supported."),
            SpaceZipSafetyResult.ArchiveBomb => FileSafetyResult.Reject(
                SpaceErrorCodes.FileArchiveBomb,
                SpaceJobFailureKind.Security,
                $"{malware.Engine}+managed-zip",
                malware.SignatureVersion,
                "Archive expansion limits were exceeded."),
            SpaceZipSafetyResult.ActiveContent => FileSafetyResult.Reject(
                SpaceErrorCodes.FileActiveContent,
                SpaceJobFailureKind.Security,
                $"{malware.Engine}+managed-zip",
                malware.SignatureVersion,
                "The archive contains active or unsafe path content."),
            _ => FileSafetyResult.Reject(
                SpaceErrorCodes.FileCorrupt,
                SpaceJobFailureKind.Input,
                $"{malware.Engine}+managed-zip",
                malware.SignatureVersion,
                "The archive container is corrupt."),
        };
    }

    private static FileSafetyResult Deferred(string summary) =>
        FileSafetyResult.Defer(
            "managed-file-safety",
            "v1",
            summary);
}

internal enum SpaceZipSafetyResult
{
    Clean,
    Encrypted,
    ArchiveBomb,
    ActiveContent,
    Corrupt,
}

internal static class SpaceZipSafetyInspector
{
    private const int EndRecordMinimumBytes = 22;
    private const int MaximumCommentBytes = ushort.MaxValue;
    private const int MaximumEntries = 10_000;
    private const long MaximumCentralDirectoryBytes = 32L * 1024 * 1024;
    private const long MaximumExpandedBytes = 1024L * 1024 * 1024;
    private const long MaximumEntryExpansionRatio = 1_000;
    private const long MaximumAggregateExpansionRatio = 200;
    private const int MaximumFileNameBytes = 4_096;

    private static readonly string[] ActiveExtensions =
    [
        ".exe", ".dll", ".com", ".bat", ".cmd", ".ps1", ".js", ".vbs",
        ".scr", ".msi", ".jar",
    ];

    public static async Task<SpaceZipSafetyResult> InspectAsync(
        Stream content,
        CancellationToken cancellationToken)
    {
        if (!content.CanRead || !content.CanSeek)
            return SpaceZipSafetyResult.Corrupt;
        if (content.Length < EndRecordMinimumBytes)
            return SpaceZipSafetyResult.Corrupt;

        try
        {
            var end = await ReadEndRecordAsync(content, cancellationToken);
            if (end is null)
                return SpaceZipSafetyResult.Corrupt;
            if (end.Value.DiskNumber != 0 ||
                end.Value.CentralDirectoryDisk != 0 ||
                end.Value.EntriesOnDisk != end.Value.TotalEntries)
            {
                return SpaceZipSafetyResult.Corrupt;
            }
            if (end.Value.TotalEntries > MaximumEntries ||
                end.Value.CentralDirectorySize > MaximumCentralDirectoryBytes)
            {
                return SpaceZipSafetyResult.ArchiveBomb;
            }
            if ((long)end.Value.CentralDirectoryOffset +
                end.Value.CentralDirectorySize >
                content.Length)
            {
                return SpaceZipSafetyResult.Corrupt;
            }

            content.Position = end.Value.CentralDirectoryOffset;
            long compressedTotal = 0;
            long expandedTotal = 0;
            var fixedHeader = new byte[46];
            for (var index = 0; index < end.Value.TotalEntries; index++)
            {
                await content.ReadExactlyAsync(fixedHeader, cancellationToken);
                if (BinaryPrimitives.ReadUInt32LittleEndian(fixedHeader) !=
                    0x02014b50)
                {
                    return SpaceZipSafetyResult.Corrupt;
                }

                var flags = BinaryPrimitives.ReadUInt16LittleEndian(
                    fixedHeader.AsSpan(8));
                if ((flags & 0x0001) != 0)
                    return SpaceZipSafetyResult.Encrypted;

                var compressed = BinaryPrimitives.ReadUInt32LittleEndian(
                    fixedHeader.AsSpan(20));
                var expanded = BinaryPrimitives.ReadUInt32LittleEndian(
                    fixedHeader.AsSpan(24));
                var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(
                    fixedHeader.AsSpan(28));
                var extraLength = BinaryPrimitives.ReadUInt16LittleEndian(
                    fixedHeader.AsSpan(30));
                var commentLength = BinaryPrimitives.ReadUInt16LittleEndian(
                    fixedHeader.AsSpan(32));
                if (nameLength == 0 || nameLength > MaximumFileNameBytes)
                    return SpaceZipSafetyResult.ArchiveBomb;

                var nameBytes = new byte[nameLength];
                await content.ReadExactlyAsync(nameBytes, cancellationToken);
                var name = Encoding.UTF8.GetString(nameBytes);
                if (IsUnsafePath(name) || HasActiveContent(name))
                    return SpaceZipSafetyResult.ActiveContent;

                content.Seek((long)extraLength + commentLength, SeekOrigin.Current);
                compressedTotal = checked(compressedTotal + compressed);
                expandedTotal = checked(expandedTotal + expanded);
                if (expandedTotal > MaximumExpandedBytes ||
                    ExpansionRatioExceeded(
                        expanded,
                        compressed,
                        MaximumEntryExpansionRatio))
                {
                    return SpaceZipSafetyResult.ArchiveBomb;
                }
            }

            return ExpansionRatioExceeded(
                    expandedTotal,
                    compressedTotal,
                    MaximumAggregateExpansionRatio)
                ? SpaceZipSafetyResult.ArchiveBomb
                : SpaceZipSafetyResult.Clean;
        }
        catch (Exception exception)
            when (exception is
                EndOfStreamException or
                IOException or
                OverflowException or
                ArgumentOutOfRangeException)
        {
            return SpaceZipSafetyResult.Corrupt;
        }
    }

    private static async Task<SpaceZipEndRecord?> ReadEndRecordAsync(
        Stream content,
        CancellationToken cancellationToken)
    {
        var tailLength = (int)Math.Min(
            content.Length,
            EndRecordMinimumBytes + MaximumCommentBytes);
        var tail = new byte[tailLength];
        content.Position = content.Length - tailLength;
        await content.ReadExactlyAsync(tail, cancellationToken);
        for (var index = tail.Length - EndRecordMinimumBytes; index >= 0; index--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(index)) !=
                0x06054b50)
            {
                continue;
            }

            var commentLength = BinaryPrimitives.ReadUInt16LittleEndian(
                tail.AsSpan(index + 20));
            if (index + EndRecordMinimumBytes + commentLength != tail.Length)
                continue;

            var totalEntries = BinaryPrimitives.ReadUInt16LittleEndian(
                tail.AsSpan(index + 10));
            var centralSize = BinaryPrimitives.ReadUInt32LittleEndian(
                tail.AsSpan(index + 12));
            var centralOffset = BinaryPrimitives.ReadUInt32LittleEndian(
                tail.AsSpan(index + 16));
            if (totalEntries == ushort.MaxValue ||
                centralSize == uint.MaxValue ||
                centralOffset == uint.MaxValue)
            {
                return new SpaceZipEndRecord(
                    0,
                    0,
                    ushort.MaxValue,
                    ushort.MaxValue,
                    uint.MaxValue,
                    uint.MaxValue);
            }

            return new SpaceZipEndRecord(
                BinaryPrimitives.ReadUInt16LittleEndian(
                    tail.AsSpan(index + 4)),
                BinaryPrimitives.ReadUInt16LittleEndian(
                    tail.AsSpan(index + 6)),
                BinaryPrimitives.ReadUInt16LittleEndian(
                    tail.AsSpan(index + 8)),
                totalEntries,
                centralSize,
                centralOffset);
        }

        return null;
    }

    private static bool ExpansionRatioExceeded(
        long expanded,
        long compressed,
        long maximumRatio)
    {
        if (expanded == 0)
            return false;
        if (compressed == 0)
            return true;
        return expanded > compressed * maximumRatio;
    }

    private static bool IsUnsafePath(string value)
    {
        var normalized = value.Replace('\\', '/');
        if (normalized.StartsWith('/') ||
            normalized.Contains(':', StringComparison.Ordinal))
        {
            return true;
        }

        return normalized.Split(
                '/',
                StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment is "." or "..");
    }

    private static bool HasActiveContent(string value)
    {
        var normalized = value.Replace('\\', '/').ToLowerInvariant();
        if (normalized.EndsWith("/vbaproject.bin", StringComparison.Ordinal) ||
            normalized.StartsWith("xl/embeddings/", StringComparison.Ordinal) ||
            normalized.StartsWith("xl/externallinks/", StringComparison.Ordinal))
        {
            return true;
        }

        return ActiveExtensions.Any(
            extension => normalized.EndsWith(
                extension,
                StringComparison.Ordinal));
    }

    private readonly record struct SpaceZipEndRecord(
        ushort DiskNumber,
        ushort CentralDirectoryDisk,
        ushort EntriesOnDisk,
        ushort TotalEntries,
        uint CentralDirectorySize,
        uint CentralDirectoryOffset);
}
