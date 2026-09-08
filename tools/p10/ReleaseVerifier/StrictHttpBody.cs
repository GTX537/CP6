using System.Net;

namespace CP6.P10.ReleaseVerifier;

// Binary transfer bounds differ from the 4 MiB CP6 control-object contract.
internal static class StrictHttpBody
{
    internal static async Task<byte[]> ReadAsync(HttpResponseMessage response, string mediaType, int maximumBytes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (maximumBytes is < 1 or > FormalNuGetVerifier.MaximumPackageBytes) throw FormalFeedPolicy.Error("feed-size");
        if (response.StatusCode != HttpStatusCode.OK) throw FormalFeedPolicy.Error("feed-http-status");
        var content = response.Content;
        if (content.Headers.ContentType?.MediaType != mediaType || content.Headers.ContentEncoding.Count != 0)
            throw FormalFeedPolicy.Error("feed-media-type");
        var declared = content.Headers.ContentLength;
        if (declared is not null && (declared < 1 || declared > maximumBytes)) throw FormalFeedPolicy.Error("feed-size");
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[maximumBytes + 1];
        var count = 0;
        while (count < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(count), cancellationToken);
            if (read == 0) break;
            count += read;
        }
        if (count == 0 || count > maximumBytes || (declared is not null && declared != count))
            throw FormalFeedPolicy.Error("feed-size");
        return buffer.AsSpan(0, count).ToArray();
    }
}
