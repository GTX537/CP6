using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier.Tests;

public sealed class S06LocalFilesTests : IDisposable
{
    private readonly string _parent = Path.Combine(Path.GetTempPath(), "cp6-p10-local-test-" + Guid.NewGuid().ToString("N"));
    public S06LocalFilesTests() => Directory.CreateDirectory(_parent);
    private string Stage => Path.Combine(_parent, "stage");

    [Fact]
    public void Public_raw_bytes_roundtrip_without_canonicalization()
    {
        var bytes = "{ \"native\": true }\n"u8.ToArray();
        S06LocalFiles.WriteNew(Stage, new Dictionary<string, ReadOnlyMemory<byte>>
        {
            ["producer.json"] = bytes,
            ["objects/abc"] = new byte[] { 1, 2, 3 }
        });
        var read = S06LocalFiles.ReadExact(Stage, ["producer.json", "objects/abc"]);
        Assert.Equal(bytes, read["producer.json"].ToArray());
        Assert.Equal(new byte[] { 1, 2, 3 }, read["objects/abc"].ToArray());
    }

    [Fact]
    public void Existing_directory_is_never_reused_or_overwritten()
    {
        S06LocalFiles.WriteNew(Stage, Files());
        Assert.Equal("local-stage-exists", Assert.Throws<Cp6ReleaseContractException>(() =>
            S06LocalFiles.WriteNew(Stage, new Dictionary<string, ReadOnlyMemory<byte>> { ["data.json"] = "{\"new\":true}"u8.ToArray() })).Code);
        Assert.Equal("{}"u8.ToArray(), File.ReadAllBytes(Path.Combine(Stage, "data.json")));
    }

    [Fact]
    public void Existing_file_is_never_replaced_by_a_directory()
    {
        File.WriteAllBytes(Stage, new byte[] { 42 });
        Assert.Equal("local-stage-exists", Assert.Throws<Cp6ReleaseContractException>(() => S06LocalFiles.WriteNew(Stage, Files())).Code);
        Assert.Equal(new byte[] { 42 }, File.ReadAllBytes(Stage));
    }

    [Theory]
    [InlineData("../escape.json")]
    [InlineData("/escape.json")]
    [InlineData("x\\escape.json")]
    [InlineData("x/y/z.json")]
    [InlineData("x//y.json")]
    [InlineData("data.json\n")]
    [InlineData("data..json")]
    [InlineData("data.json.")]
    [InlineData("con.json")]
    [InlineData("x/LPT1")]
    public void Unsafe_names_fail_before_creating_the_directory(string name)
    {
        var files = new Dictionary<string, ReadOnlyMemory<byte>> { [name] = "{}"u8.ToArray() };
        Assert.Equal("local-stage-name", Assert.Throws<Cp6ReleaseContractException>(() => S06LocalFiles.WriteNew(Stage, files)).Code);
        Assert.False(Directory.Exists(Stage));
    }

    [Theory]
    [InlineData("case-collision")]
    [InlineData("file-directory-collision")]
    [InlineData("empty")]
    [InlineData("too-many")]
    public void Inconsistent_entry_sets_fail_before_writing(string mutation)
    {
        var files = Files();
        if (mutation == "case-collision") files.Add("Data.json", "{}"u8.ToArray());
        if (mutation == "file-directory-collision") files.Add("data.json/child", "{}"u8.ToArray());
        if (mutation == "empty") files.Clear();
        if (mutation == "too-many")
            for (var i = 0; i < 32; i++) files.Add("extra" + i + ".json", "{}"u8.ToArray());
        Assert.Equal("local-stage-files", Assert.Throws<Cp6ReleaseContractException>(() => S06LocalFiles.WriteNew(Stage, files)).Code);
        Assert.False(Directory.Exists(Stage));
    }

    [Theory]
    [InlineData("data.json", 0)]
    [InlineData("data.json", 4194305)]
    [InlineData("packages/package.nupkg", 8388609)]
    public void Per_file_byte_limits_fail_before_writing(string name, int length)
    {
        Assert.Equal("local-stage-size", Assert.Throws<Cp6ReleaseContractException>(() => S06LocalFiles.WriteNew(Stage,
            new Dictionary<string, ReadOnlyMemory<byte>> { [name] = new byte[length] })).Code);
        Assert.False(Directory.Exists(Stage));
    }

    [Fact]
    public void Aggregate_byte_limit_is_checked_before_copying_or_writing()
    {
        var payload = new byte[4194304];
        var files = Enumerable.Range(0, 17).ToDictionary(i => "file" + i + ".json",
            _ => (ReadOnlyMemory<byte>)payload);
        Assert.Equal("local-stage-size", Assert.Throws<Cp6ReleaseContractException>(() => S06LocalFiles.WriteNew(Stage, files)).Code);
        Assert.False(Directory.Exists(Stage));
    }

    [Fact]
    public void NuGet_payloads_use_the_existing_eight_MiB_bound()
    {
        var bytes = new byte[4194305];
        S06LocalFiles.WriteNew(Stage, new Dictionary<string, ReadOnlyMemory<byte>> { ["packages/package.nupkg"] = bytes });
        Assert.Equal(bytes, S06LocalFiles.ReadExact(Stage, ["packages/package.nupkg"])["packages/package.nupkg"].ToArray());
    }

    [Theory]
    [InlineData("extra-file")]
    [InlineData("missing-file")]
    [InlineData("empty-directory")]
    [InlineData("nested-directory")]
    [InlineData("oversized-file")]
    public void Read_requires_the_exact_bounded_entry_set(string mutation)
    {
        S06LocalFiles.WriteNew(Stage, Files());
        if (mutation == "extra-file") File.WriteAllText(Path.Combine(Stage, "extra.json"), "{}");
        if (mutation == "missing-file") File.Delete(Path.Combine(Stage, "data.json"));
        if (mutation == "empty-directory") Directory.CreateDirectory(Path.Combine(Stage, "empty"));
        if (mutation == "nested-directory") Directory.CreateDirectory(Path.Combine(Stage, "nested", "deep"));
        if (mutation == "oversized-file") File.WriteAllBytes(Path.Combine(Stage, "data.json"), new byte[4194305]);
        Assert.Throws<Cp6ReleaseContractException>(() => S06LocalFiles.ReadExact(Stage, ["data.json"]));
    }

    [Fact]
    public void Relative_output_paths_are_not_resolved_against_ambient_working_directories() =>
        Assert.Equal("local-stage-path", Assert.Throws<Cp6ReleaseContractException>(() => S06LocalFiles.WriteNew("stage", Files())).Code);

    [Fact]
    public void Missing_input_paths_are_sanitized()
    {
        var error = Assert.Throws<Cp6ReleaseContractException>(() => S06LocalFiles.ReadExact(
            Path.Combine(_parent, "private-marker"), ["data.json"]));
        Assert.Equal("local-stage-missing", error.Code);
        Assert.DoesNotContain("private-marker", error.Message);
        Assert.Null(error.InnerException);
    }

    private static Dictionary<string, ReadOnlyMemory<byte>> Files() => new() { ["data.json"] = "{}"u8.ToArray() };

    public void Dispose()
    {
        var actual = Path.GetFullPath(_parent);
        var parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
        if (Path.GetDirectoryName(actual) != parent || !Path.GetFileName(actual).StartsWith("cp6-p10-local-test-", StringComparison.Ordinal))
            throw new InvalidOperationException("Test cleanup escaped its owned directory.");
        Directory.Delete(actual, recursive: true);
    }
}
