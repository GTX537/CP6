using System.Security.Cryptography;
using System.Text.Json;
using CP6.Platform.Release;
using CP6.Platform.Release.CapabilitiesV1;

// Task-private identity inspection. The caller supplies independently reviewed pins.
// No signature callback, capability qualification, host registration or enablement.
if (args.Length != 10)
{
    Console.Error.WriteLine("MATRIX EXPECTED_MATRIX_SHA SOURCE_SHA PUBLICATION EXPECTED_PUBLICATION_SHA POLICY EXPECTED_POLICY_SHA CERT_DIR PACKAGE_DIR NEW_OUTPUT_DIR");
    return 64;
}

try
{
    var matrix = File.ReadAllBytes(args[0]);
    var publication = File.ReadAllBytes(args[3]);
    var policyBytes = File.ReadAllBytes(args[5]);
    Require(Hash(matrix) == args[1], "matrix pin mismatch");
    Require(Hash(publication) == args[4], "publication pin mismatch");
    Require(Hash(policyBytes) == args[6], "policy pin mismatch");
    using var document = JsonDocument.Parse(matrix);
    var root = document.RootElement;
    Require(root.GetProperty("capabilities").GetArrayLength() == 0 &&
            root.GetProperty("compatibility").GetArrayLength() == 0,
        "This identity-only tool does not approve capability or compatibility rows.");
    var certificates = Directory.GetFiles(args[7], "*.cer").ToDictionary(
        path => "certificates/" + Path.GetFileName(path),
        path => (ReadOnlyMemory<byte>)File.ReadAllBytes(path), StringComparer.Ordinal);
    var policy = Cp6PinnedNuGetTrustPolicy.Parse(policyBytes, certificates);
    _ = Cp6CapabilityValidator.ValidateShape(matrix);
    var packages = new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal);
    foreach (var package in root.GetProperty("packages").EnumerateArray())
    {
        var id = package.GetProperty("packageId").GetString()!;
        // Paths come from the fixed catalog after the authoritative shape check.
        packages.Add(id, File.ReadAllBytes(Path.Combine(args[8], id + ".0.10.3.nupkg")));
    }
    var evidence = new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal)
    {
        [args[4]] = publication,
        [args[6]] = policyBytes
    };
    var evaluation = Cp6CapabilityValidator.Evaluate(matrix,
        new Cp6CapabilityApproval(args[1], args[2], args[6]), packages, evidence,
        "{}"u8, DateTimeOffset.UtcNow, publication, policy);
    Require(evaluation.Compatibility == Cp6CompatibilityDecision.Unknown &&
            evaluation.TrustCurrentlyAccepted == Cp6CapabilityState.Unknown,
        "Identity inspection must not claim runtime compatibility or fresh cryptographic trust.");
    var output = Path.GetFullPath(args[9]);
    Require(!Directory.Exists(output) && !File.Exists(output), "output already exists");
    Directory.CreateDirectory(output);
    File.WriteAllBytes(Path.Combine(output, "consumer-lock.json"), evaluation.ConsumerLock);
    var state = new
    {
        sourceFrozen = evaluation.SourceFrozen.ToString(),
        contractShapeValid = evaluation.ContractShapeValid.ToString(),
        artifactBytesVerified = evaluation.ArtifactBytesVerified.ToString(),
        trustCurrentlyAccepted = evaluation.TrustCurrentlyAccepted.ToString(),
        compatibility = evaluation.Compatibility.ToString(),
        consumerQualified = evaluation.ConsumerQualified.ToString(),
        hostRegistered = evaluation.HostRegistered.ToString(),
        configEnabled = evaluation.ConfigEnabled.ToString(),
        runtimeObserved = evaluation.RuntimeObserved.ToString(),
        businessApplied = evaluation.BusinessApplied.ToString(),
        independentlyAccepted = evaluation.IndependentlyAccepted.ToString(),
        releaseAssemblySha256 = Hash(File.ReadAllBytes(typeof(Cp6CapabilityValidator).Assembly.Location)),
        scope = "Exact formal identity only; signature results remain in the separately recorded authorized release run."
    };
    var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(Path.Combine(output, "identity-state.json"), json + "\n");
    Console.WriteLine(json);
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error is Cp6ReleaseContractException contract
        ? contract.Code + ": " + contract.Message : error.Message);
    return 2;
}

static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidDataException(message);
}
