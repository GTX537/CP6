using System.Security.Cryptography;
using System.Text.Json;
using CP6.Entity.Contracts.Wire;

namespace CP6.Entity.Contracts.ErpCredit;

/// <summary>Exact profile, schema and root identity. No "latest" or fallback route exists.</summary>
public sealed record CreditWireIdentity(string ProfileId, string CodecNamespace, string SchemaFile,
    string SchemaSha256, string RegistrySha256, string Root);

public static class CreditIdentity
{
    public const string WmsSchema = "ERP-CREDIT-01_WMS-RMA-R3-EXACT.schema.json";
    public const string FinArSchema = "ERP-CREDIT-01_FIN-AR-R2-EXACT.schema.json";
    public const string RegistryFile = "ERP-CREDIT-01_TYPE-REGISTRY.json";
    public const string CurrentNamespace = "Cp6.ErpCredit.CurrentV1.Generated";
    public const string FrozenNamespace = "Cp6.ErpCredit.Generated";
    public const string CurrentProfile = "erp-credit-current-v1", FrozenProfile = "erp-credit-frozen-r8";
    public const string WmsSha256 = "b324a9b62d888f268a514de4266ecff70783e80d30c39eb5b82e7fffaf62af3a";
    public const string FinArSha256 = "8a7864b91e2bd19f9102abcb9eea756d7f6df33ad84e7b852f24a9bd669c56ca";
    public const string CurrentRegistrySha256 = "564706ce0247d84dc8056002d540f17eccdcab7c5b5e4b123c09121ad407fbda";
    public const string FrozenRegistrySha256 = "62effa1e0542784a5fa936d11961465db4da522ed4d28fc9c9bb5351cf40b6f1";

    public static string Sha256(ReadOnlySpan<byte> raw) => Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant();
    public static bool AcceptsRegistry(string profile, string ns, string file, string hash) => file == RegistryFile &&
        ((profile == CurrentProfile && ns == CurrentNamespace && hash == CurrentRegistrySha256) ||
         (profile == FrozenProfile && ns == FrozenNamespace && hash == FrozenRegistrySha256));

    internal static string FileFor(string root, bool replay = false) => root switch
    {
        "CreditOwnerRequest" or "CreditOwnerQueryRequest" or "CreditOwnerQueryResult" or "CommercialOutcomeRef" or "Money" => WmsSchema,
        "CreditAuthorityReadRequest" or "CreditAuthorityReadResult" or "CreditSelectionsQuery" or "CreditSelectionPage" or "CreditPrepare" or "PostInput" or "SafeOperationDetail" or "CommandResult" or "D8" => FinArSchema,
        "TerminalCommandReceipt" or "FinArObservation" => RegistryFile,
        "EC_C01_COMMITTED_CommercialDraftView" or "EC_C02_COMMITTED_CommercialDraftView" when replay => RegistryFile,
        _ => throw new JsonException("UNPUBLISHED_NATIVE_ROOT")
    };
    internal static CreditWireIdentity For(bool current, string root, bool replay = false)
    {
        var file = FileFor(root, replay);
        var registry = current ? CurrentRegistrySha256 : FrozenRegistrySha256;
        return new(current ? CurrentProfile : FrozenProfile, current ? CurrentNamespace : FrozenNamespace,
            file, file == WmsSchema ? WmsSha256 : file == FinArSchema ? FinArSha256 : registry, registry, root);
    }
    internal static void Require(CreditWireIdentity identity, bool current, bool replay = false)
    {
        if (identity != For(current, identity.Root, replay)) throw new JsonException("WIRE_IDENTITY");
    }
}

/// <summary>Complete validated native JSON, an independent element lifetime and immutable original bytes.</summary>
/// <remarks>Validation proves the wire contract, never Owner provenance or authority to write/release.</remarks>
public sealed class NativeCreditDocument
{
    private readonly byte[] raw;
    public JsonElement Value { get; }
    public CreditWireIdentity Identity { get; }
    public string Sha256 { get; }
    internal NativeCreditDocument(CreditWireIdentity identity, byte[] ownedRaw, JsonElement value)
    {
        raw = ownedRaw; Identity = identity; Value = value.Clone(); Sha256 = CreditIdentity.Sha256(raw);
    }
    public byte[] GetOriginalUtf8() => raw.ToArray();
}

public sealed class NativeCreditCodec
{
    private readonly CreditCodecSet set;
    public CreditWireIdentity Identity { get; }
    internal NativeCreditCodec(CreditCodecSet set, string root) { this.set = set; Identity = set.IdentityFor(root); }
    public NativeCreditDocument Parse(ReadOnlySpan<byte> raw) => set.Decode(Identity, raw);
}

/// <summary>Only the published full native roots and two explicitly named scalar helpers are callable.</summary>
public abstract class CreditCodecSet
{
    private readonly NativeSchemaValidator validator;
    private readonly bool current;
    public string CodecNamespace => current ? CreditIdentity.CurrentNamespace : CreditIdentity.FrozenNamespace;
    private protected CreditCodecSet(bool current, IReadOnlyDictionary<string, ReadOnlyMemory<byte>> files)
    {
        this.current = current;
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [CreditIdentity.WmsSchema] = CreditIdentity.WmsSha256,
            [CreditIdentity.FinArSchema] = CreditIdentity.FinArSha256,
            [CreditIdentity.RegistryFile] = current ? CreditIdentity.CurrentRegistrySha256 : CreditIdentity.FrozenRegistrySha256
        };
        if (files.Count != expected.Count || files.Keys.Any(key => !expected.ContainsKey(key))) throw new JsonException("SCHEMA_BUNDLE");
        // Hash and parse the same private snapshot, never caller-owned mutable memory.
        var snapshots = files.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal);
        var docs = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var pair in expected)
        {
            if (!snapshots.TryGetValue(pair.Key, out var raw) || CreditIdentity.Sha256(raw) != pair.Value) throw new JsonException("SCHEMA_IDENTITY");
            using var parsed = StrictJson.Parse(raw);
            docs.Add(pair.Key, parsed.RootElement.Clone());
        }
        validator = new(docs, current);
    }
    public CreditWireIdentity IdentityFor(string root) => CreditIdentity.For(current, root);
    public NativeCreditDocument Decode(CreditWireIdentity identity, ReadOnlySpan<byte> raw)
    {
        CreditIdentity.Require(identity, current);
        return DecodeCore(identity, raw);
    }
    internal NativeCreditDocument DecodeReplay(string root, ReadOnlySpan<byte> raw) => DecodeCore(CreditIdentity.For(current, root, true), raw);
    private NativeCreditDocument DecodeCore(CreditWireIdentity identity, ReadOnlySpan<byte> raw)
    {
        var stable = raw.ToArray();
        using var parsed = StrictJson.Parse(stable);
        validator.ValidateRoot(identity.SchemaFile, identity.Root, parsed.RootElement);
        return new(identity, stable, parsed.RootElement);
    }
    public NativeCreditDocument ParseWmsMoney(ReadOnlySpan<byte> raw) => Decode(IdentityFor("Money"), raw);
    public NativeCreditDocument ParseFinArD8(ReadOnlySpan<byte> raw) => Decode(IdentityFor("D8"), raw);
 public NativeCreditCodec WmsCreditOwnerRequest=>new(this,"CreditOwnerRequest");
 public NativeCreditCodec WmsCreditOwnerQueryRequest=>new(this,"CreditOwnerQueryRequest");
 public NativeCreditCodec WmsCreditOwnerQueryResult=>new(this,"CreditOwnerQueryResult");
 public NativeCreditCodec WmsCommercialOutcomeRef=>new(this,"CommercialOutcomeRef");
 public NativeCreditCodec FinArCreditAuthorityReadRequest=>new(this,"CreditAuthorityReadRequest");
 public NativeCreditCodec FinArCreditAuthorityReadResult=>new(this,"CreditAuthorityReadResult");
 public NativeCreditCodec FinArCreditSelectionsQuery=>new(this,"CreditSelectionsQuery");
 public NativeCreditCodec FinArCreditSelectionPage=>new(this,"CreditSelectionPage");
 public NativeCreditCodec FinArCreditPrepare=>new(this,"CreditPrepare");
 public NativeCreditCodec FinArPostInput=>new(this,"PostInput");
 public NativeCreditCodec FinArSafeOperationDetail=>new(this,"SafeOperationDetail");
 public NativeCreditCodec FinArCommandResult=>new(this,"CommandResult");
 public NativeCreditCodec TerminalCommandReceipt=>new(this,"TerminalCommandReceipt");
 public NativeCreditCodec FinArObservation=>new(this,"FinArObservation");
 public IReadOnlyList<NativeCreditCodec> PublishedCodecs=>[WmsCreditOwnerRequest,WmsCreditOwnerQueryRequest,WmsCreditOwnerQueryResult,WmsCommercialOutcomeRef,FinArCreditAuthorityReadRequest,FinArCreditAuthorityReadResult,FinArCreditSelectionsQuery,FinArCreditSelectionPage,FinArCreditPrepare,FinArPostInput,FinArSafeOperationDetail,FinArCommandResult,TerminalCommandReceipt,FinArObservation];
}

/// <summary>Byte retention only; deliberately cannot masquerade as a NativeCreditDocument.</summary>
public sealed class FrozenOriginalBytes
{
    private readonly byte[] raw;
    public CreditWireIdentity Identity { get; }
    public string Sha256 { get; }
    public bool NativeSchemaValidated => false;
    private FrozenOriginalBytes(CreditWireIdentity identity, byte[] raw) { Identity = identity; this.raw = raw; Sha256 = CreditIdentity.Sha256(raw); }
    public static FrozenOriginalBytes Capture(CreditWireIdentity identity, ReadOnlySpan<byte> raw, string expectedSha256)
    {
        CreditIdentity.Require(identity, false);
        var stable = raw.ToArray();
        if (CreditIdentity.Sha256(stable) != expectedSha256) throw new JsonException("ORIGINAL_BYTES_DIGEST");
        using var parsed = StrictJson.Parse(stable);
        return new(identity, stable);
    }
    public byte[] GetOriginalUtf8() => raw.ToArray();
}

public sealed record AdaptationAssessment(string Status, string NextAction, string EffectKnowledge,
    bool CanRelease, bool CanDispatch, bool EstablishesFound, NativeCreditDocument Original);

/// <summary>An in-memory byte replay guard. No Store/issuer attestation, HTTP handler or business writer.</summary>
public sealed class FrozenHttpReplay
{
    private readonly string commandKey;
    private readonly byte[] request;
    private readonly NativeCreditDocument response;
    public CreditWireIdentity ResponseIdentity => response.Identity;
    public string RequestSha256 => CreditIdentity.Sha256(request);
    internal FrozenHttpReplay(string key, byte[] request, NativeCreditDocument response)
    { commandKey = key; this.request = request; this.response = response; }
    public FrozenReplayResponse Replay(string key, ReadOnlySpan<byte> originalRequest)
    {
        if (key != commandKey || !originalRequest.SequenceEqual(request)) throw new JsonException("COMMAND_PAYLOAD_CONFLICT");
        return new(response);
    }
    public byte[] GetOriginalRequestUtf8() => request.ToArray();
}

public sealed class FrozenReplayResponse
{
    private readonly NativeCreditDocument response;
    internal FrozenReplayResponse(NativeCreditDocument response) { this.response = response; }
    public int StatusCode => 200;
    public JsonElement Value => response.Value;
    public byte[] GetBodyUtf8() => response.GetOriginalUtf8();
}
