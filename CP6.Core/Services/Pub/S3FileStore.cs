using System.Net;
using Amazon.S3;
using Amazon.S3.Model;

namespace CP6.Core.Services.Pub;

/// <summary>
/// S3-compatible shared file storage for stateless API replicas.
/// Object keys are always relative to a configured prefix and path traversal is rejected.
/// </summary>
public sealed class S3FileStore : IFileStore, IDisposable
{
    private readonly IAmazonS3 _client;
    private readonly string _bucket;
    private readonly string _prefix;
    private readonly string _serverSideEncryption;
    private readonly string? _kmsKeyId;

    public S3FileStore(
        IAmazonS3 client,
        string bucket,
        string? prefix = null,
        string serverSideEncryption = "AES256",
        string? kmsKeyId = null)
    {
        _client = client;
        _bucket = string.IsNullOrWhiteSpace(bucket)
            ? throw new ArgumentException("S3 bucket is required", nameof(bucket))
            : bucket.Trim();
        _prefix = NormalizePrefix(prefix);
        _serverSideEncryption = serverSideEncryption.Trim();
        _kmsKeyId = string.IsNullOrWhiteSpace(kmsKeyId) ? null : kmsKeyId.Trim();
    }

    public async Task<string> SaveAsync(Stream content, string storeName)
    {
        var key = Key(storeName);
        var request = new PutObjectRequest
        {
            BucketName = _bucket,
            Key = key,
            InputStream = content,
            AutoCloseStream = false,
            ServerSideEncryptionMethod = EncryptionMethod(),
        };
        if (request.ServerSideEncryptionMethod == ServerSideEncryptionMethod.AWSKMS)
            request.ServerSideEncryptionKeyManagementServiceKeyId = _kmsKeyId;
        await _client.PutObjectAsync(request);
        return NormalizeStorePath(storeName);
    }

    public async Task<Stream> OpenReadAsync(string storePath)
    {
        try
        {
            using var response = await _client.GetObjectAsync(_bucket, Key(storePath));
            var copy = new MemoryStream();
            await response.ResponseStream.CopyToAsync(copy);
            copy.Position = 0;
            return copy;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new FileNotFoundException("Object does not exist", storePath, ex);
        }
    }

    public async Task DeleteAsync(string storePath)
        => await _client.DeleteObjectAsync(_bucket, Key(storePath));

    public bool Exists(string storePath)
    {
        try
        {
            _client.GetObjectMetadataAsync(_bucket, Key(storePath))
                .GetAwaiter()
                .GetResult();
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public void Dispose() => _client.Dispose();

    private ServerSideEncryptionMethod EncryptionMethod()
        => _serverSideEncryption.Equals("aws:kms", StringComparison.OrdinalIgnoreCase)
            ? ServerSideEncryptionMethod.AWSKMS
            : ServerSideEncryptionMethod.AES256;

    private string Key(string storePath)
    {
        var normalized = NormalizeStorePath(storePath);
        return string.IsNullOrEmpty(_prefix) ? normalized : $"{_prefix}/{normalized}";
    }

    private static string NormalizeStorePath(string storePath)
    {
        var normalized = storePath?.Replace('\\', '/').Trim('/') ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized)
            || normalized.Split('/').Any(segment =>
                segment is "" or "." or ".."))
            throw new ArgumentException("Invalid object store path", nameof(storePath));
        return normalized;
    }

    private static string NormalizePrefix(string? prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix)) return string.Empty;
        return NormalizeStorePath(prefix);
    }
}
