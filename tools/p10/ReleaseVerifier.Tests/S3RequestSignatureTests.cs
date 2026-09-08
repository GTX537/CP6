using System.Text;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier.Tests;

public sealed class S3RequestSignatureTests
{
    private const string EmptyHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
    // Public AWS documentation example, not an operational credential.
    private const string ExampleId = "AKIAIOSFODNN7EXAMPLE";
    private const string ExampleSecret = "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY";

    [Fact]
    public void Matches_the_official_AWS_S3_single_chunk_GET_vector()
    {
        var headers = Headers();
        headers.Add("range", "bytes=0-9");
        var result = Sign("GET", "/test.txt", headers);
        Assert.Equal("AWS4-HMAC-SHA256 Credential=AKIAIOSFODNN7EXAMPLE/20130524/us-east-1/s3/aws4_request," +
            "SignedHeaders=host;range;x-amz-content-sha256;x-amz-date," +
            "Signature=f0e8bdb87c964420e857bd35b5d6ed310bd44f0170aba48dd91039c6036bdb41", result);
    }

    [Fact]
    public void Header_insertion_order_does_not_change_signature()
    {
        var headers = Headers();
        headers.Add("range", "bytes=0-9");
        var reversed = headers.Reverse().ToDictionary(pair => pair.Key, pair => pair.Value);
        Assert.Equal(Sign("GET", "/test.txt", headers), Sign("GET", "/test.txt", reversed));
    }

    [Theory]
    [InlineData("POST", "/test.txt")]
    [InlineData("DELETE", "/test.txt")]
    [InlineData("GET", "/test.txt?location")]
    [InlineData("GET", "/test%2Ftxt")]
    [InlineData("GET", "/a/../test.txt")]
    [InlineData("GET", "/a//test.txt")]
    [InlineData("GET", "/test txt")]
    [InlineData("GET", "/测试")]
    [InlineData("GET", "test.txt")]
    [InlineData("GET", "/a\\b")]
    public void Rejects_unsupported_methods_and_noncanonical_paths(string method, string path) =>
        Error("r2-signature-input", () => Sign(method, path, Headers()));

    [Theory]
    [InlineData("Host", "examplebucket.s3.amazonaws.com")]
    [InlineData("x-amz-date", "20130524T000000Z\r\nx-injected: yes")]
    [InlineData("x-amz-date", "20130230T000000Z")]
    [InlineData("host", "examplebucket.s3.amazonaws.com:443")]
    [InlineData("range", " bytes=0-9")]
    [InlineData("range", "bytes=0-9  ")]
    [InlineData("range", "bytes=0-9\t")]
    [InlineData("x-amz-content-sha256", "UNSIGNED-PAYLOAD")]
    [InlineData("authorization", "already-present")]
    public void Rejects_ambiguous_or_unsupported_headers(string name, string value)
    {
        var headers = Headers();
        headers[name] = value;
        Error("r2-signature-headers", () => Sign("GET", "/test.txt", headers));
    }

    [Theory]
    [InlineData("")]
    [InlineData("0123")]
    [InlineData("UNSIGNED-PAYLOAD")]
    public void Rejects_non_SHA256_payload_hashes(string hash) =>
        Error("r2-signature-input", () => S3RequestSignature.Authorization("GET", "/test.txt", Headers(),
            hash, "us-east-1", ExampleId, Encoding.ASCII.GetBytes(ExampleSecret)));

    [Fact]
    public void Requires_conditional_create_for_every_PUT()
    {
        Error("r2-create-condition", () => Sign("PUT", "/test.txt", Headers()));
        var headers = Headers();
        headers["if-none-match"] = "\"some-etag\"";
        Error("r2-create-condition", () => Sign("PUT", "/test.txt", headers));
    }

    [Fact]
    public void Signed_PUT_binds_condition_media_body_and_session_token()
    {
        var headers = Headers();
        headers["if-none-match"] = "*";
        headers["content-type"] = "application/json";
        headers["x-amz-security-token"] = "unit-fixture-session";
        var first = Sign("PUT", "/test.txt", headers);
        Assert.Contains("SignedHeaders=content-type;host;if-none-match;x-amz-content-sha256;x-amz-date;x-amz-security-token,", first);
        headers["x-amz-security-token"] = "different-unit-session";
        Assert.NotEqual(first, Sign("PUT", "/test.txt", headers));
        headers["x-amz-security-token"] = "unit-fixture-session";
        headers["content-type"] = "application/sarif+json";
        Assert.NotEqual(first, Sign("PUT", "/test.txt", headers));
        headers["content-type"] = "application/json";
        var changed = Cp6DeterministicJson.Sha256Hex("{}"u8);
        headers["x-amz-content-sha256"] = changed;
        Assert.NotEqual(first, Sign("PUT", "/test.txt", headers, changed));
    }

    [Fact]
    public void HEAD_and_object_path_are_bound()
    {
        var first = Sign("GET", "/test.txt", Headers());
        Assert.NotEqual(first, Sign("HEAD", "/test.txt", Headers()));
        Assert.NotEqual(first, Sign("GET", "/other.txt", Headers()));
    }

    [Fact]
    public void Reads_cannot_hide_a_payload()
    {
        var hash = Cp6DeterministicJson.Sha256Hex("{}"u8);
        var headers = Headers();
        headers["x-amz-content-sha256"] = hash;
        Error("r2-read-payload", () => Sign("GET", "/test.txt", headers, hash));
    }

    [Theory]
    [InlineData("host")]
    [InlineData("x-amz-date")]
    [InlineData("x-amz-content-sha256")]
    public void Required_headers_cannot_be_missing(string name)
    {
        var headers = Headers();
        headers.Remove(name);
        Error("r2-signature-headers", () => Sign("GET", "/test.txt", headers));
    }

    [Fact]
    public void Rejects_oversized_values_and_invalid_credentials()
    {
        var headers = Headers();
        headers["range"] = new string('a', 4097);
        Error("r2-signature-headers", () => Sign("GET", "/test.txt", headers));
        Error("r2-signature-input", () => Sign("GET", "/" + new string('a', 512), Headers()));
        Error("r2-signature-input", () => S3RequestSignature.Authorization("GET", "/test.txt", Headers(),
            EmptyHash, "other-region", ExampleId, Encoding.ASCII.GetBytes(ExampleSecret)));
        Error("r2-signature-input", () => S3RequestSignature.Authorization("GET", "/test.txt", Headers(),
            EmptyHash, "auto", "invalid/id", Encoding.ASCII.GetBytes(ExampleSecret)));
        Error("r2-signature-input", () => S3RequestSignature.Authorization("GET", "/test.txt", Headers(),
            EmptyHash, "auto", ExampleId, "short"u8));
    }

    private static Dictionary<string, string> Headers() => new()
    {
        ["x-amz-date"] = "20130524T000000Z",
        ["host"] = "examplebucket.s3.amazonaws.com",
        ["x-amz-content-sha256"] = EmptyHash
    };
    private static string Sign(string method, string path, IReadOnlyDictionary<string, string> headers, string hash = EmptyHash) =>
        S3RequestSignature.Authorization(method, path, headers, hash, "us-east-1", ExampleId, Encoding.ASCII.GetBytes(ExampleSecret));
    private static void Error(string code, Action action) => Assert.Equal(code, Assert.Throws<Cp6ReleaseContractException>(action).Code);
}
