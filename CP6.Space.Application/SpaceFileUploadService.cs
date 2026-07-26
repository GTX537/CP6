using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using CP6.Space.Contracts;
using CP6.Space.Domain;

namespace CP6.Space.Application;

public sealed record SpaceFileUploadRequest(
    SpaceSourceType SourceType,
    string OriginalName,
    string? DeclaredContentType,
    SpaceFileRetentionClass RetentionClass = SpaceFileRetentionClass.Source);

public sealed record SpaceFileUploadResult(
    SpaceFile File,
    bool Reused);

public sealed class SpaceFileUploadLimits
{
    public const long OneMiB = 1024L * 1024L;

    public long PlatformMaxBytes { get; init; } = 200 * OneMiB;
    public long TenantMaxBytes { get; init; } = 100 * OneMiB;
    public long ExcelMaxBytes { get; init; } = 50 * OneMiB;

    public long GetEffectiveLimit(SpaceSourceType sourceType)
    {
        if (PlatformMaxBytes <= 0 || TenantMaxBytes <= 0 || ExcelMaxBytes <= 0)
            throw new InvalidOperationException("Space upload limits must be positive.");

        var typeLimit = sourceType == SpaceSourceType.Excel
            ? Math.Min(TenantMaxBytes, ExcelMaxBytes)
            : TenantMaxBytes;
        return Math.Min(PlatformMaxBytes, typeLimit);
    }
}

public sealed class SpaceFileValidationException : InvalidOperationException
{
    public SpaceFileValidationException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}

public sealed class SpaceFileUploadService
{
    private const int BufferSize = 64 * 1024;
    private const int SignatureBytes = 4096;

    private readonly ISpaceExecutionContext _execution;
    private readonly ISpaceQuarantineStore _quarantine;
    private readonly ISpaceFileCatalog _catalog;
    private readonly SpaceFileUploadLimits _limits;

    public SpaceFileUploadService(
        ISpaceExecutionContext execution,
        ISpaceQuarantineStore quarantine,
        ISpaceFileCatalog catalog,
        SpaceFileUploadLimits? limits = null)
    {
        _execution = execution;
        _quarantine = quarantine;
        _catalog = catalog;
        _limits = limits ?? new SpaceFileUploadLimits();
    }

    public async Task<SpaceFileUploadResult> UploadAsync(
        SpaceFileUploadRequest request,
        Stream input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(input);
        if (_execution.TenantId == Guid.Empty)
            throw new SpaceTenantScopeException(
                "A verified Space tenant context is required.");
        if (!input.CanRead)
            throw new ArgumentException("Upload stream must be readable.", nameof(input));

        var originalName = SanitizeDisplayName(request.OriginalName);
        var expected = SpaceFileTypePolicy.For(request.SourceType, originalName);
        SpaceFileTypePolicy.ValidateDeclaredContentType(
            expected,
            request.DeclaredContentType);

        var fileId = Guid.NewGuid();
        await using var session = await _quarantine.OpenWriteAsync(
            _execution.TenantId,
            fileId,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(session.StorageKey))
            throw new InvalidOperationException(
                "The quarantine store must generate an unpredictable storage key.");
        if (!session.Content.CanWrite)
            throw new InvalidOperationException(
                "The quarantine store returned a non-writable stream.");

        try
        {
            var limit = _limits.GetEffectiveLimit(request.SourceType);
            var streamed = await StreamAndHashAsync(
                input,
                session.Content,
                limit,
                cancellationToken);
            SpaceFileTypePolicy.ValidateSignature(expected, streamed.Signature);

            var duplicate = await _catalog.FindReusableAsync(
                _execution.TenantId,
                streamed.Sha256,
                request.RetentionClass,
                cancellationToken);
            if (duplicate is not null)
            {
                await session.AbortAsync(cancellationToken);
                return new SpaceFileUploadResult(duplicate, Reused: true);
            }

            var file = SpaceFile.CreateUploading(
                fileId,
                _execution.TenantId,
                session.StorageKey,
                originalName,
                NormalizeContentType(request.DeclaredContentType),
                request.RetentionClass);
            file.CompleteQuarantine(
                expected.DetectedContentType,
                expected.Extension,
                streamed.SizeBytes,
                streamed.Sha256);

            await session.CommitAsync(cancellationToken);
            _catalog.Add(file);
            await _catalog.SaveChangesAsync(cancellationToken);

            return new SpaceFileUploadResult(file, Reused: false);
        }
        catch
        {
            await session.AbortAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task<StreamedUpload> StreamAndHashAsync(
        Stream source,
        Stream destination,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        var signature = new byte[SignatureBytes];
        var signatureLength = 0;
        long size = 0;

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        try
        {
            while (true)
            {
                var read = await source.ReadAsync(
                    buffer.AsMemory(0, BufferSize),
                    cancellationToken);
                if (read == 0)
                    break;

                size = checked(size + read);
                if (size > maxBytes)
                {
                    throw new SpaceFileValidationException(
                        SpaceErrorCodes.FileTooLarge,
                        $"The upload exceeded the {maxBytes}-byte limit.");
                }

                hash.AppendData(buffer, 0, read);
                if (signatureLength < signature.Length)
                {
                    var copy = Math.Min(read, signature.Length - signatureLength);
                    buffer.AsSpan(0, copy).CopyTo(
                        signature.AsSpan(signatureLength, copy));
                    signatureLength += copy;
                }

                await destination.WriteAsync(
                    buffer.AsMemory(0, read),
                    cancellationToken);
            }

            await destination.FlushAsync(cancellationToken);
            return new StreamedUpload(
                size,
                Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(),
                signature.AsMemory(0, signatureLength).ToArray());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    private static string SanitizeDisplayName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new SpaceFileValidationException(
                SpaceErrorCodes.FileTypeMismatch,
                "A file name is required.");

        var segments = value.Replace('\\', '/').Split(
            '/',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var name = segments.LastOrDefault();
        if (string.IsNullOrWhiteSpace(name) || name is "." or "..")
        {
            throw new SpaceFileValidationException(
                SpaceErrorCodes.FileTypeMismatch,
                "A valid display file name is required.");
        }

        var sanitized = new string(
            name.Select(character => char.IsControl(character) ? '_' : character)
                .ToArray());
        if (sanitized.Length > 260)
        {
            throw new SpaceFileValidationException(
                SpaceErrorCodes.FileTypeMismatch,
                "The display file name cannot exceed 260 characters.");
        }

        return sanitized;
    }

    private static string? NormalizeContentType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return value.Split(';', 2)[0].Trim().ToLowerInvariant();
    }

    private sealed record StreamedUpload(
        long SizeBytes,
        string Sha256,
        byte[] Signature);

    private delegate bool SignatureMatcher(ReadOnlySpan<byte> bytes);

    private sealed record ExpectedFileType(
        string Extension,
        string DetectedContentType,
        IReadOnlySet<string> DeclaredContentTypes,
        SignatureMatcher SignatureMatches);

    private static class SpaceFileTypePolicy
    {
        public static ExpectedFileType For(
            SpaceSourceType sourceType,
            string originalName)
        {
            var extension = Path.GetExtension(originalName).ToLowerInvariant();
            var expected = sourceType switch
            {
                SpaceSourceType.Dwg => new ExpectedFileType(
                    ".dwg",
                    "application/vnd.autocad.dwg",
                    MimeSet(
                        "application/vnd.autocad.dwg",
                        "application/acad",
                        "application/x-acad",
                        "application/octet-stream"),
                    HasDwgSignature),
                SpaceSourceType.Dxf => new ExpectedFileType(
                    ".dxf",
                    "application/vnd.autocad.dxf",
                    MimeSet(
                        "application/vnd.autocad.dxf",
                        "application/dxf",
                        "application/x-dxf",
                        "text/plain",
                        "application/octet-stream"),
                    HasDxfSignature),
                SpaceSourceType.Pdf => new ExpectedFileType(
                    ".pdf",
                    "application/pdf",
                    MimeSet("application/pdf"),
                    bytes => bytes.StartsWith("%PDF-"u8)),
                SpaceSourceType.Png => new ExpectedFileType(
                    ".png",
                    "image/png",
                    MimeSet("image/png"),
                    bytes => bytes.StartsWith(
                        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })),
                SpaceSourceType.Jpg => new ExpectedFileType(
                    extension == ".jpeg" ? ".jpeg" : ".jpg",
                    "image/jpeg",
                    MimeSet("image/jpeg", "image/jpg"),
                    bytes => bytes.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF })),
                SpaceSourceType.Excel => new ExpectedFileType(
                    ".xlsx",
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    MimeSet(
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"),
                    HasZipSignature),
                _ => throw new SpaceFileValidationException(
                    SpaceErrorCodes.FileTypeMismatch,
                    "Editor and Template sources do not accept file uploads."),
            };

            var extensionMatches = sourceType == SpaceSourceType.Jpg
                ? extension is ".jpg" or ".jpeg"
                : extension == expected.Extension;
            if (!extensionMatches)
            {
                throw new SpaceFileValidationException(
                    SpaceErrorCodes.FileTypeMismatch,
                    $"Extension '{extension}' does not match source type {sourceType}.");
            }

            return expected;
        }

        public static void ValidateDeclaredContentType(
            ExpectedFileType expected,
            string? declaredContentType)
        {
            var normalized = NormalizeContentType(declaredContentType);
            if (normalized is not null &&
                !expected.DeclaredContentTypes.Contains(normalized))
            {
                throw new SpaceFileValidationException(
                    SpaceErrorCodes.FileTypeMismatch,
                    $"Declared content type '{normalized}' does not match {expected.Extension}.");
            }
        }

        public static void ValidateSignature(
            ExpectedFileType expected,
            ReadOnlySpan<byte> signature)
        {
            if (!expected.SignatureMatches(signature))
            {
                throw new SpaceFileValidationException(
                    SpaceErrorCodes.FileTypeMismatch,
                    $"File signature does not match {expected.Extension}.");
            }
        }

        private static IReadOnlySet<string> MimeSet(params string[] values) =>
            new HashSet<string>(values, StringComparer.OrdinalIgnoreCase);

        private static bool HasDwgSignature(ReadOnlySpan<byte> bytes) =>
            bytes.Length >= 6 &&
            bytes[0] == (byte)'A' &&
            bytes[1] == (byte)'C' &&
            bytes[2] == (byte)'1' &&
            bytes[3] == (byte)'0' &&
            bytes[4] is >= (byte)'0' and <= (byte)'9' &&
            bytes[5] is >= (byte)'0' and <= (byte)'9';

        private static bool HasDxfSignature(ReadOnlySpan<byte> bytes)
        {
            if (bytes.StartsWith("AutoCAD Binary DXF"u8))
                return true;

            var text = Encoding.ASCII.GetString(bytes);
            var compact = new string(text.Take(256).Where(c => !char.IsWhiteSpace(c)).ToArray());
            return compact.StartsWith("0SECTION", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasZipSignature(ReadOnlySpan<byte> bytes) =>
            bytes.Length >= 4 &&
            bytes[0] == 0x50 &&
            bytes[1] == 0x4B &&
            ((bytes[2] == 0x03 && bytes[3] == 0x04) ||
             (bytes[2] == 0x05 && bytes[3] == 0x06) ||
             (bytes[2] == 0x07 && bytes[3] == 0x08));
    }
}
