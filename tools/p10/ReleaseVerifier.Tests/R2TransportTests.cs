using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier.Tests;

// Real request construction and response parsing, with in-memory protocol inputs only.
// These tests do not emulate service authorization or claim a successful R2 publication.
public sealed class R2TransportTests
{
    private static readonly string FixtureId = new('a', 32);
    private static readonly string FixtureSecret = new('b', 64);
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 0, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Raw = "{ \"score\": 9.8 }\n"u8.ToArray();
    private const string Endpoint = "https://30c4a8d1697ffd3de6a1e0a88376607c.r2.cloudflarestorage.com/cp6-release/";

    [Theory]
    [InlineData(1, "candidate-locator.v1.json", Cp6ReleaseMediaTypes.CandidateLocator)]
    [InlineData(2, "candidate-locator.v1.sigstore.json", Cp6ReleaseMediaTypes.SigstoreBundle)]
    public void Discovery_uses_only_the_package_derived_fixed_adjacent_keys(int part, string name, string media)
    {
        var target = R2ObjectTarget.Discovery("v0.10.1-s06.1", (R2DiscoveryPart)part);
        Assert.Equal("candidates/platform/v0.10.1-s06.1/" + name, target.Key);
        Assert.Equal(media, target.MediaType);
        Assert.Null(target.Reference);
    }

    [Theory]
    [InlineData("")]
    [InlineData("v0.10.1\n")]
    [InlineData("v0.10.1/other")]
    [InlineData("https://attacker.example")]
    [InlineData("../v0.10.1")]
    [InlineData("v00.10.1")]
    public void Discovery_rejects_noncanonical_tags(string tag) =>
        Assert.Throws<Cp6ReleaseContractException>(() => R2ObjectTarget.Discovery(tag, R2DiscoveryPart.Locator));

    [Fact]
    public void Discovery_rejects_unknown_part_and_oversized_tag()
    {
        Error("r2-discovery-part", () => R2ObjectTarget.Discovery("v0.10.1", (R2DiscoveryPart)99));
        Error("r2-discovery-tag", () => R2ObjectTarget.Discovery("v0.10.1-" + new string('a', 128), R2DiscoveryPart.Locator));
    }

    [Fact]
    public void Addressed_target_preserves_the_validated_content_reference()
    {
        var reference = Reference();
        var target = R2ObjectTarget.Addressed(reference);
        Assert.Same(reference, target.Reference);
        Assert.Equal(reference.Key, target.Key);
        Assert.Equal(reference.MediaType, target.MediaType);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    public void Read_request_is_fixed_signed_HTTP11_without_body_or_ambient_headers(string method)
    {
        using var credentials = R2Credentials.Consumer(FixtureId, FixtureSecret);
        var target = Discovery();
        using var request = R2WirePolicy.Request(new HttpMethod(method), target, credentials, ReadOnlyMemory<byte>.Empty, Now);
        Assert.Equal(Endpoint + target.Key, request.RequestUri!.AbsoluteUri);
        Assert.Equal(string.Empty, request.RequestUri.Query);
        Assert.Equal(string.Empty, request.RequestUri.UserInfo);
        Assert.Equal(string.Empty, request.RequestUri.Fragment);
        Assert.Equal(HttpVersion.Version11, request.Version);
        Assert.Equal(HttpVersionPolicy.RequestVersionExact, request.VersionPolicy);
        Assert.Null(request.Content);
        Assert.False(request.Headers.ExpectContinue);
        Assert.False(request.Headers.TransferEncodingChunked);
        Assert.False(request.Headers.Contains("Cookie"));
        Assert.False(request.Headers.Contains("Referer"));
        Assert.False(request.Headers.Contains("x-amz-security-token"));
        Assert.Equal(Cp6DeterministicJson.Sha256Hex([]), request.Headers.GetValues("x-amz-content-sha256").Single());
        Assert.Equal("20260907T000000Z", request.Headers.GetValues("x-amz-date").Single());
        Assert.Contains("/auto/s3/aws4_request,", request.Headers.GetValues("Authorization").Single());
    }

    [Fact]
    public async Task Conditional_PUT_snapshots_exact_body_and_signs_what_HTTP_will_send()
    {
        using var credentials = R2Credentials.PublisherAt(FixtureId, FixtureSecret, Now);
        var target = R2ObjectTarget.Addressed(Reference());
        var body = Raw.ToArray();
        using var request = R2WirePolicy.Request(HttpMethod.Put, target, credentials, body, Now);
        body[0] ^= 1;
        Assert.Equal(Raw, await request.Content!.ReadAsByteArrayAsync());
        Assert.Equal(Raw.Length, request.Content.Headers.ContentLength);
        Assert.Equal(target.MediaType, request.Content.Headers.ContentType!.ToString());
        Assert.Equal("*", request.Headers.IfNoneMatch.Single().Tag);
        Assert.Equal(credentials.SessionToken, request.Headers.GetValues("x-amz-security-token").Single());
        Assert.Equal(target.Reference!.Sha256, request.Headers.GetValues("x-amz-content-sha256").Single());
        var signed = request.Headers.Where(p => p.Key.StartsWith("x-amz-", StringComparison.OrdinalIgnoreCase) ||
            p.Key.Equals("Host", StringComparison.OrdinalIgnoreCase) || p.Key.Equals("If-None-Match", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(p => p.Key.ToLowerInvariant(), p => p.Value.Single(), StringComparer.Ordinal);
        signed.Add("content-type", request.Content.Headers.ContentType.ToString());
        Assert.Equal(credentials.Sign("PUT", request.RequestUri!.AbsolutePath, signed, Now),
            request.Headers.GetValues("Authorization").Single());
        Assert.False(request.Headers.TransferEncodingChunked);
        Assert.False(request.Headers.ExpectContinue);
    }

    [Fact]
    public void PUT_validates_expected_hash_and_length_before_any_network()
    {
        using var credentials = R2Credentials.PublisherAt(FixtureId, FixtureSecret, Now);
        var target = R2ObjectTarget.Addressed(Reference());
        var changed = Raw.ToArray();
        changed[0] ^= 1;
        Error("r2-write-binding", () => R2WirePolicy.Request(HttpMethod.Put, target, credentials, changed, Now));
        Error("r2-write-binding", () => R2WirePolicy.Request(HttpMethod.Put, target, credentials, "{}"u8.ToArray(), Now));
    }

    [Theory]
    [InlineData("POST", 0, "r2-method")]
    [InlineData("DELETE", 0, "r2-method")]
    [InlineData("GET", 1, "r2-request-body")]
    [InlineData("HEAD", 1, "r2-request-body")]
    [InlineData("PUT", 0, "r2-request-body")]
    [InlineData("PUT", 4194305, "r2-request-body")]
    public void Rejects_unsupported_operations_and_body_sizes(string method, int length, string code)
    {
        using var credentials = R2Credentials.PublisherAt(FixtureId, FixtureSecret, Now);
        Error(code, () => R2WirePolicy.Request(new HttpMethod(method), Discovery(), credentials, new byte[length], Now));
    }

    [Fact]
    public async Task Every_supported_media_type_round_trips_exact_raw_bytes_without_extra_parameters()
    {
        using var credentials = R2Credentials.PublisherAt(FixtureId, FixtureSecret, Now);
        foreach (var media in Cp6ReleaseMediaTypes.All)
        {
            var target = R2ObjectTarget.Addressed(ContentAddress.Create(Raw, media, "proof.json"));
            using var request = R2WirePolicy.Request(HttpMethod.Put, target, credentials, Raw, Now);
            using var response = Response(HttpStatusCode.OK, Raw, request.Content!.Headers.ContentType!.ToString(), Raw.Length);
            Assert.Equal(Raw, await R2WirePolicy.ReadResponseAsync(response, target, CancellationToken.None));
        }
    }

    [Fact]
    public async Task Discovery_returns_bounded_bytes_without_parsing_or_trusting_them()
    {
        using var response = Response(HttpStatusCode.OK, Raw, Cp6ReleaseMediaTypes.CandidateLocator, Raw.Length);
        Assert.Equal(Raw, await R2WirePolicy.ReadResponseAsync(response, Discovery(), CancellationToken.None));
    }

    [Fact]
    public async Task Only_404_means_missing_and_error_bodies_are_not_read()
    {
        using var stream = new CountingStream("private server body"u8.ToArray());
        using var response = Response(HttpStatusCode.NotFound, stream, "application/xml", 19);
        Assert.Null(await R2WirePolicy.ReadResponseAsync(response, Discovery(), CancellationToken.None));
        Assert.Equal(0, stream.BytesRead);
    }

    [Theory]
    [InlineData(201)]
    [InlineData(204)]
    [InlineData(206)]
    [InlineData(301)]
    [InlineData(302)]
    [InlineData(307)]
    [InlineData(308)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(409)]
    [InlineData(412)]
    [InlineData(429)]
    [InlineData(500)]
    public async Task Non_GET_success_or_redirect_is_neither_missing_nor_followed(int status)
    {
        using var stream = new CountingStream("private server body"u8.ToArray());
        using var response = Response((HttpStatusCode)status, stream, "application/xml", 19);
        response.Headers.Location = new Uri("https://attacker.example/private-token");
        var error = await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            R2WirePolicy.ReadResponseAsync(response, Discovery(), CancellationToken.None));
        Assert.Equal("r2-read-status", error.Code);
        Assert.Null(error.InnerException);
        Assert.Equal("R2 transfer violates the fixed object policy.", error.Message);
        Assert.Equal(0, stream.BytesRead);
    }

    [Theory]
    [InlineData("application/json", 16L)]
    [InlineData(Cp6ReleaseMediaTypes.CandidateLocator + "; charset=utf-8", 16L)]
    [InlineData(Cp6ReleaseMediaTypes.CandidateLocator, null)]
    [InlineData(Cp6ReleaseMediaTypes.CandidateLocator, 0L)]
    [InlineData(Cp6ReleaseMediaTypes.CandidateLocator, 4194305L)]
    public async Task Invalid_read_metadata_is_rejected_before_body_access(string media, long? length)
    {
        using var stream = new CountingStream(Raw);
        using var response = Response(HttpStatusCode.OK, stream, media, length);
        await ErrorAsync("r2-read-metadata", () => R2WirePolicy.ReadResponseAsync(response, Discovery(), CancellationToken.None));
        Assert.Equal(0, stream.BytesRead);
    }

    [Fact]
    public async Task Encoding_partial_content_and_address_length_mismatch_are_rejected()
    {
        foreach (var mode in new[] { "encoding", "range", "length" })
        {
            using var stream = new CountingStream(Raw);
            using var response = Response(HttpStatusCode.OK, stream, Cp6ReleaseMediaTypes.Sarif, Raw.Length);
            if (mode == "encoding") response.Content.Headers.ContentEncoding.Add("gzip");
            if (mode == "range") response.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, Raw.Length - 1, Raw.Length);
            if (mode == "length") response.Content.Headers.ContentLength = Raw.Length + 1;
            await ErrorAsync("r2-read-metadata", () => R2WirePolicy.ReadResponseAsync(response,
                R2ObjectTarget.Addressed(Reference()), CancellationToken.None));
            Assert.Equal(0, stream.BytesRead);
        }
    }

    [Fact]
    public async Task Exact_metadata_cannot_hide_changed_addressed_bytes()
    {
        var changed = Raw.ToArray();
        changed[0] ^= 1;
        using var response = Response(HttpStatusCode.OK, changed, Cp6ReleaseMediaTypes.Sarif, changed.Length);
        await ErrorAsync("object-hash", () => R2WirePolicy.ReadResponseAsync(response,
            R2ObjectTarget.Addressed(Reference()), CancellationToken.None));
    }

    [Theory]
    [InlineData(0, 1, "object-size")]
    [InlineData(1, 2, "r2-read-length")]
    [InlineData(3, 2, "object-size")]
    public async Task Discovery_actual_length_must_equal_declared_length(int actual, long declared, string code)
    {
        using var stream = new CountingStream(new byte[actual]);
        using var response = Response(HttpStatusCode.OK, stream, Cp6ReleaseMediaTypes.CandidateLocator, declared);
        await ErrorAsync(code, () => R2WirePolicy.ReadResponseAsync(response, Discovery(), CancellationToken.None));
        Assert.True(stream.BytesRead <= declared + 1);
    }

    [Fact]
    public async Task Maximum_object_passes_and_oversized_stream_stops_at_limit_plus_one()
    {
        using var exact = Response(HttpStatusCode.OK, new byte[Cp6DeterministicJson.MaximumBytes],
            Cp6ReleaseMediaTypes.CandidateLocator, Cp6DeterministicJson.MaximumBytes);
        Assert.Equal(Cp6DeterministicJson.MaximumBytes,
            (await R2WirePolicy.ReadResponseAsync(exact, Discovery(), CancellationToken.None))!.Length);
        using var stream = new CountingStream(new byte[Cp6DeterministicJson.MaximumBytes + 64]);
        using var response = Response(HttpStatusCode.OK, stream, Cp6ReleaseMediaTypes.CandidateLocator, Cp6DeterministicJson.MaximumBytes);
        await ErrorAsync("object-size", () => R2WirePolicy.ReadResponseAsync(response, Discovery(), CancellationToken.None));
        Assert.Equal(Cp6DeterministicJson.MaximumBytes + 1, stream.BytesRead);
    }

    [Theory]
    [InlineData(200, 1)]
    [InlineData(412, 2)]
    public void Conditional_create_status_does_not_claim_idempotence(int status, int expected)
    {
        using var response = new HttpResponseMessage((HttpStatusCode)status);
        Assert.Equal((R2CreateStatus)expected, R2WirePolicy.CreateResponse(response));
    }

    [Theory]
    [InlineData(201)]
    [InlineData(204)]
    [InlineData(301)]
    [InlineData(307)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(409)]
    [InlineData(500)]
    public void Other_create_responses_are_not_success_or_retry_authorization(int status)
    {
        using var response = new HttpResponseMessage((HttpStatusCode)status);
        Error("r2-create-status", () => R2WirePolicy.CreateResponse(response));
    }

    [Fact]
    public void Handler_cannot_redirect_use_ambient_credentials_decompress_or_drain_error_bodies()
    {
        using var handler = R2WirePolicy.CreateHandler();
        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseProxy);
        Assert.False(handler.UseCookies);
        Assert.False(handler.PreAuthenticate);
        Assert.Null(handler.Credentials);
        Assert.Null(handler.ActivityHeadersPropagator);
        Assert.Null(handler.SslOptions.RemoteCertificateValidationCallback);
        Assert.Equal(X509RevocationMode.Online, handler.SslOptions.CertificateRevocationCheckMode);
        Assert.Equal(DecompressionMethods.None, handler.AutomaticDecompression);
        Assert.Equal(TimeSpan.FromSeconds(10), handler.ConnectTimeout);
        Assert.Equal(16, handler.MaxResponseHeadersLength);
        Assert.Equal(0, handler.MaxResponseDrainSize);
        Assert.Equal(TimeSpan.Zero, handler.ResponseDrainTimeout);
    }

    [Fact]
    public void Request_deadline_cannot_outlive_the_session()
    {
        using var consumer = R2Credentials.Consumer(FixtureId, FixtureSecret);
        using var publisher = R2Credentials.PublisherAt(FixtureId, FixtureSecret, Now);
        Assert.Equal(TimeSpan.FromSeconds(60), R2WirePolicy.RequestLifetime(consumer, Now));
        Assert.Equal(TimeSpan.FromSeconds(60), R2WirePolicy.RequestLifetime(publisher, Now));
        Assert.Equal(TimeSpan.FromSeconds(10), R2WirePolicy.RequestLifetime(publisher, Now.AddSeconds(890)));
        Error("r2-session-time", () => R2WirePolicy.RequestLifetime(publisher, Now.AddSeconds(-1)));
        Error("r2-session-time", () => R2WirePolicy.RequestLifetime(publisher, Now.AddSeconds(900)));
    }

    [Fact]
    public async Task Precancelled_reads_and_creates_do_not_send_any_request()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var consumer = R2ObjectClient.Consumer(FixtureId, FixtureSecret);
        using var publisher = R2ObjectClient.Publisher(FixtureId, FixtureSecret);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => consumer.ReadAsync(Discovery(), cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => publisher.CreateAsync(Discovery(), Raw, cancellation.Token));
        using var stream = new CountingStream(Raw);
        using var response = Response(HttpStatusCode.OK, stream, Cp6ReleaseMediaTypes.CandidateLocator, Raw.Length);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => R2WirePolicy.ReadResponseAsync(response, Discovery(), cancellation.Token));
        Assert.Equal(0, stream.BytesRead);
    }

    [Fact]
    public async Task Consumer_write_and_disposed_client_fail_locally()
    {
        var client = R2ObjectClient.Consumer(FixtureId, FixtureSecret);
        await ErrorAsync("r2-read-only", () => client.CreateAsync(Discovery(), Raw));
        client.Dispose();
        await ErrorAsync("r2-client-disposed", () => client.ReadAsync(Discovery()));
        client.Dispose();
        Error("r2-credential-format", () => R2ObjectClient.Consumer("invalid", FixtureSecret));
        Error("r2-credential-format", () => R2ObjectClient.Publisher("invalid", FixtureSecret));
    }

    private static ContentAddress Reference() => ContentAddress.Create(Raw, Cp6ReleaseMediaTypes.Sarif, "scan.json");
    private static R2ObjectTarget Discovery() => R2ObjectTarget.Discovery("v0.10.1", R2DiscoveryPart.Locator);
    private static HttpResponseMessage Response(HttpStatusCode status, byte[] bytes, string media, long? length) =>
        Response(status, new CountingStream(bytes), media, length);
    private static HttpResponseMessage Response(HttpStatusCode status, Stream stream, string media, long? length)
    {
        var response = new HttpResponseMessage(status) { Content = new StreamContent(stream) };
        response.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(media);
        response.Content.Headers.ContentLength = length;
        return response;
    }
    private static void Error(string code, Action action) => Assert.Equal(code, Assert.Throws<Cp6ReleaseContractException>(action).Code);
    private static async Task ErrorAsync(string code, Func<Task> action) => Assert.Equal(code, (await Assert.ThrowsAsync<Cp6ReleaseContractException>(action)).Code);

    private sealed class CountingStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream _inner = new(bytes, writable: false);
        internal int BytesRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = _inner.Read(buffer, offset, count);
            BytesRead += read;
            return read;
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await _inner.ReadAsync(buffer, cancellationToken);
            BytesRead += read;
            return read;
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
