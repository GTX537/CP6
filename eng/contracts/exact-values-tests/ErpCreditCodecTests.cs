using System.Text;
using System.Text.Json;
using CP6.Entity.Contracts.ErpCredit;
using Cp6.ErpCredit.CurrentV1.Generated;
using Cp6.ErpCredit.Generated;
using Xunit;
using Xunit.Abstractions;

namespace CP6.ExactValues.Tests;

public sealed class ErpCreditCodecTests(ITestOutputHelper output)
{
    private static readonly string Repo = FindRepo();
    private static readonly string TaskDir = Path.Combine(Repo, "eng/contracts/s01-11");
    private static string FindRepo()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "eng/contracts/s01-11/fixtures/cases.json"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository fixtures missing");
    }
    private static JsonElement Json(string path) => JsonDocument.Parse(File.ReadAllBytes(path)).RootElement.Clone();
    private static IReadOnlyDictionary<string, ReadOnlyMemory<byte>> Bundle(string profile) => new Dictionary<string, ReadOnlyMemory<byte>>
    {
        [CreditIdentity.WmsSchema] = File.ReadAllBytes(Path.Combine(TaskDir, "assets/native", CreditIdentity.WmsSchema)),
        [CreditIdentity.FinArSchema] = File.ReadAllBytes(Path.Combine(TaskDir, "assets/native", CreditIdentity.FinArSchema)),
        [CreditIdentity.RegistryFile] = File.ReadAllBytes(Path.Combine(TaskDir, "assets", profile, CreditIdentity.RegistryFile))
    };
    private static CreditCodecSet Set(string profile) => profile == "current" ? new CurrentCreditCodec(Bundle(profile)) : new FrozenR8CreditCodec(Bundle(profile));
    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);
    private static byte[] ObjectBytes(string root) => Bytes(Json(Path.Combine(TaskDir, "fixtures/native-objects.json")).GetProperty("objects").GetProperty(root).GetRawText());
    public static IEnumerable<object[]> NativeCases() => Json(Path.Combine(TaskDir, "fixtures/cases.json")).GetProperty("cases").EnumerateArray().Select(c => new object[] { c.GetProperty("id").GetString()!, c.Clone() });

    [Theory, MemberData(nameof(NativeCases))]
    public void Full_native_contract(string id, JsonElement item)
    {
        Assert.Equal(id, item.GetProperty("id").GetString());
        var set = Set(item.GetProperty("profile").GetString()!);
        var root = item.GetProperty("root").GetString()!;
        var raw = Bytes(item.GetProperty("raw").GetString()!);
        if (!item.GetProperty("accepted").GetBoolean())
            Assert.ThrowsAny<JsonException>(() => set.Decode(set.IdentityFor(root), raw));
        else
        {
            var value = set.Decode(set.IdentityFor(root), raw);
            Assert.Equal(raw, value.GetOriginalUtf8());
            Assert.Equal(CreditIdentity.Sha256(raw), value.Sha256);
            Assert.Equal(root, value.Identity.Root);
            Assert.Equal(set.CodecNamespace, value.Identity.CodecNamespace);
        }
    }

    [Fact]
    public void Executes_on_actual_net8_runtime()
    {
        output.WriteLine($"Environment.Version={Environment.Version}; {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
        Assert.Equal(8, Environment.Version.Major);
    }

    public static IEnumerable<object[]> SharedCases() => Json(Path.Combine(Repo, "eng/contracts/s01/vectors.json")).GetProperty("cases").EnumerateArray()
        .Where(c => c.GetProperty("id").GetString() is string id && (id.StartsWith("VERSION-", StringComparison.Ordinal) || id.StartsWith("BYTES-", StringComparison.Ordinal)))
        .Select(c => new object[] { c.GetProperty("id").GetString()!, c.Clone() });
    [Theory, MemberData(nameof(SharedCases))]
    public void Fixed_input_case(string id, JsonElement item)
    {
        var input = item.GetProperty("input"); var expected = item.GetProperty("expected");
        if (id.StartsWith("VERSION-", StringComparison.Ordinal))
            Assert.Equal(expected.GetProperty("accepted").GetBoolean(), CreditIdentity.AcceptsRegistry(item.GetProperty("profile_id").GetString()!, input.GetProperty("codec_namespace").GetString()!, input.GetProperty("schema_file").GetString()!, input.GetProperty("schema_sha256").GetString()!));
        else
        {
            // The shared BYTES inputs intentionally do not satisfy native PostInput.RV.
            // Capture asserts exact original bytes, never native validation or Owner provenance.
            var set = Set("frozen"); var identity = set.IdentityFor(input.GetProperty("native_schema_root").GetString()!);
            var original = FrozenOriginalBytes.Capture(identity, Bytes(input.GetProperty("original_utf8").GetString()!), expected.GetProperty("original_sha256").GetString()!);
            var replay = FrozenOriginalBytes.Capture(identity, Bytes(input.GetProperty("replay_utf8").GetString()!), expected.GetProperty("replay_sha256").GetString()!);
            Assert.Equal(expected.GetProperty("bytes_equal").GetBoolean(), original.GetOriginalUtf8().SequenceEqual(replay.GetOriginalUtf8()));
            Assert.Equal(expected.GetProperty("digest_equal").GetBoolean(), original.Sha256 == replay.Sha256);
            Assert.False(original.NativeSchemaValidated);
            Assert.ThrowsAny<JsonException>(() => set.Decode(identity, original.GetOriginalUtf8()));
        }
    }

    [Theory]
    [InlineData("current")]
    [InlineData("frozen")]
    public void Pins_routes_and_defensive_snapshots(string profile)
    {
        var set = Set(profile);
        Assert.Equal(14, set.PublishedCodecs.Count);
        var surface = Json(Path.Combine(TaskDir, "surface.json")).GetProperty("published_factories").EnumerateArray();
        Assert.Equal(surface.Select(v => v.GetProperty("root").GetString()).OrderBy(v => v), set.PublishedCodecs.Select(v => v.Identity.Root).OrderBy(v => v));
        var bytes = ObjectBytes("PostInput"); var identity = set.IdentityFor("PostInput");
        var value = set.FinArPostInput.Parse(bytes);
        bytes[0] = 0; var copy = value.GetOriginalUtf8(); copy[0] = 0;
        Assert.Equal((byte)'{', value.GetOriginalUtf8()[0]);
        Assert.Equal("fixture", value.Value.GetProperty("reason").GetString());
        Assert.ThrowsAny<JsonException>(() => set.Decode(identity with { CodecNamespace = "unknown" }, ObjectBytes("PostInput")));
        Assert.ThrowsAny<JsonException>(() => set.Decode(identity with { SchemaSha256 = new string('0', 64) }, ObjectBytes("PostInput")));
        Assert.ThrowsAny<JsonException>(() => set.Decode(identity with { Root = "WholeC06PublicRequest" }, ObjectBytes("PostInput")));
        Assert.ThrowsAny<JsonException>(() => set.Decode(identity with { Root = "EC_C06_200_StoredTerminal" }, ObjectBytes("PostInput")));
        Assert.ThrowsAny<JsonException>(() => set.Decode(identity with { Root = "EC_C06_202_StoredAdmission" }, ObjectBytes("PostInput")));
        Assert.ThrowsAny<JsonException>(() => set.Decode(identity, [(byte)'"', 0xff, (byte)'"']));
        var wrong = Bundle(profile).ToDictionary(x => x.Key, x => x.Value);
        wrong[CreditIdentity.RegistryFile] = Bundle(profile == "current" ? "frozen" : "current")[CreditIdentity.RegistryFile];
        Assert.ThrowsAny<JsonException>(() => profile == "current" ? (CreditCodecSet)new CurrentCreditCodec(wrong) : new FrozenR8CreditCodec(wrong));
        wrong = Bundle(profile).ToDictionary(x => x.Key, x => x.Value);
        wrong[CreditIdentity.WmsSchema] = Bytes("{}");
        Assert.ThrowsAny<JsonException>(() => profile == "current" ? (CreditCodecSet)new CurrentCreditCodec(wrong) : new FrozenR8CreditCodec(wrong));
    }

    [Theory]
    [InlineData("C01")]
    [InlineData("C02")]
    public void Frozen_HTTP_replay_returns_original_full_201_body(string operation)
    {
        var set = (FrozenR8CreditCodec)Set("frozen");
        var command = Bytes("{ \"commandId\": \"fixture\" }\r\n");
        var body = Bytes(" " + Encoding.UTF8.GetString(ObjectBytes($"EC_{operation}_COMMITTED_CommercialDraftView")) + "\r\n");
        var saved = set.CaptureReplay(operation, "scope/command", command, CreditIdentity.Sha256(command), 201, body, CreditIdentity.Sha256(body));
        var expected = body.ToArray(); body[0] = 0;
        var replay = saved.Replay("scope/command", command);
        Assert.Equal(200, replay.StatusCode); Assert.Equal(expected, replay.GetBodyUtf8());
        Assert.Equal("COMMITTED", replay.Value.GetProperty("resultKind").GetString());
        Assert.Equal(CreditIdentity.FrozenNamespace, saved.ResponseIdentity.CodecNamespace);
        var copy = replay.GetBodyUtf8(); copy[0] = 0; Assert.Equal(expected, saved.Replay("scope/command", command).GetBodyUtf8());
        Assert.ThrowsAny<JsonException>(() => saved.Replay("scope/other", command));
        Assert.ThrowsAny<JsonException>(() => saved.Replay("scope/command", Bytes("{}")));
        var renamed = Bytes(Encoding.UTF8.GetString(expected).Replace("COMMITTED", "COMMITTED_REPLAY", StringComparison.Ordinal));
        Assert.ThrowsAny<JsonException>(() => set.CaptureReplay(operation, "scope/command", command, CreditIdentity.Sha256(command), 201, renamed, CreditIdentity.Sha256(renamed)));
        Assert.ThrowsAny<JsonException>(() => set.CaptureReplay(operation, "scope/command", command, CreditIdentity.Sha256(command), 200, expected, CreditIdentity.Sha256(expected)));
        Assert.ThrowsAny<JsonException>(() => set.CaptureReplay(operation, "scope/command", command, CreditIdentity.Sha256(command), 201, expected, new string('0', 64)));
    }

    [Theory]
    [InlineData("CommandResult")]
    [InlineData("FinArObservation")]
    public void Wire_success_never_authorizes_adaptation_or_release(string root)
    {
        var set = Set("current");var native = set.Decode(set.IdentityFor(root), ObjectBytes(root));
        var assessment = CurrentCreditCodec.AssessAdaptation(native);
        Assert.Equal("READ_ONLY_UNPROVEN", assessment.Status);
        Assert.Equal("MANUAL_RECONCILIATION", assessment.NextAction);
        Assert.Equal("UNKNOWN", assessment.EffectKnowledge);
        Assert.False(assessment.CanRelease); Assert.False(assessment.CanDispatch); Assert.False(assessment.EstablishesFound);
        Assert.Equal(native.GetOriginalUtf8(), assessment.Original.GetOriginalUtf8());
    }
}
