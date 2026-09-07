# P10 S06 Package-Fed Verifier Foundation Implementation Plan

> **For agentic workers:** Use `superpowers:executing-plans` inline, as already selected by the user. Do not delegate. Execute the checkboxes in order.

**Goal:** Add a bounded, read-only contract-inspection CLI that consumes the corrected formal `CP6.Platform.Release 0.10.1` package without copying Platform schemas or source into public CP6.

**Architecture:** Keep local canonicalization and contract inspection separate from candidate acceptance. Inspection requires an independently supplied expected SHA-256 and delegates contract semantics to the formal package. The subsequent S06 evidence-graph and publication subplans must add real NuGet/OCI/signature/workflow/R2 checks before any candidate can be accepted or published.

**Tech Stack:** .NET SDK 8.0.424, .NET 8, exact NuGet PackageReference, xUnit 2.9.3, PowerShell 7.

## Scope, baseline and prerequisite

- Isolated worktree branch `codex/p10-s06-platform-candidate`, fetched public `origin/main@6f9d09f4e3b1627a25ec7859b748eba8cd66f621`.
- Clean baseline: public CRM PRD and SaaS contract checks, Release Shadow S0, R2 deployment-contract and R2 operations-contract suites passed. Their fixture results are regression tests, not real deployment evidence.
- Begin implementation only after the actual seven-package `0.10.1` S04 publication succeeds and its immutable evidence is recorded. Never substitute `0.10.0`, a local producer build, ProjectReference to Platform, a copied Schema, or a synthetic formal package.
- This foundation does not publish a NuGet package, image, R2 object or Locator. It does not modify the CP6 application solution, runtime registration, old R2 workflows, production deployment, or the hash-bound R00 payload.
- The owner authorized Platform main protection; its five existing GitHub Actions contexts now require PR merge and reject force-push/deletion, including administrators, with zero required approving reviews. Do not claim that earlier source references were protected retroactively.
- The CRM read token is now bound to the existing `p10-platform-candidate` Environment. Actual private-repository read permission still requires a workflow test. Never copy the token into this plan or a repository file.

## File map

| File | Responsibility |
| --- | --- |
| `tools/p10/global.json` | Exact SDK selection isolated from existing CP6 projects |
| `eng/p10/NuGet.formal.config` | Credential-free source mapping and pinned signer policy |
| `tools/p10/ReleaseVerifier/CP6.P10.ReleaseVerifier.csproj` | Formal package-fed executable, not packable |
| `tools/p10/ReleaseVerifier/ContractInspection.cs` | Bounded file I/O, expected-hash check and supported package API dispatch |
| `tools/p10/ReleaseVerifier/Program.cs` | Narrow CLI with sanitized errors and explicit non-acceptance output |
| `tools/p10/ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj` | Tests of the CP6 adapter, not a Platform source reference |
| `tools/p10/ReleaseVerifier.Tests/ContractInspectionTests.cs` | Hash, canonical-byte, lane and file-size regressions |
| `tools/p10/ReleaseVerifier.Tests/ProgramProcessTests.cs` | Real child-process exit codes, sanitized failures and create-only output |
| Both project `packages.lock.json` files | Generated exact restore graphs, reviewed after real restore |

## Task 1: Set up the isolated formal-package consumer

- [x] Create `tools/p10/global.json` with these exact contents:

```json
{"sdk":{"version":"8.0.424","rollForward":"disable","allowPrerelease":false}}
```

- [x] Create `eng/p10/NuGet.formal.config` with this credential-free policy. The uppercase author fingerprint is intentional; NuGet author matching is case-sensitive.

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <config><add key="signatureValidationMode" value="require" /></config>
  <packageSources>
    <clear />
    <add key="github" value="https://nuget.pkg.github.com/GTX537/index.json" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
  <packageSourceMapping>
    <clear />
    <packageSource key="github"><package pattern="CP6.Platform.*" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
  <trustedSigners>
    <clear />
    <author name="CP6-Platform-P10">
      <certificate fingerprint="1DEBFB8FF286EA51192B7F259D1AC823C105C4188EAC40148598D37F0E20FF0D" hashAlgorithm="SHA256" allowUntrustedRoot="true" />
    </author>
    <repository name="nuget.org" serviceIndex="https://api.nuget.org/v3/index.json">
      <certificate fingerprint="0e5f38f57dc1bcc806d8494f4f90fbcedd988b46760709cbeec6f4219aa6157d" hashAlgorithm="SHA256" allowUntrustedRoot="false" />
      <certificate fingerprint="5a2901d6ada3d18260b9c6dfe2133c95d74b9eef6ae0e5dc334c8454d1477df4" hashAlgorithm="SHA256" allowUntrustedRoot="false" />
      <certificate fingerprint="1f4b311d9acc115c8dc8018b5a49e00fce6da8e2855f9f014ca6f34570bc482d" hashAlgorithm="SHA256" allowUntrustedRoot="false" />
    </repository>
  </trustedSigners>
</configuration>
```

- [x] Create `tools/p10/ReleaseVerifier/CP6.P10.ReleaseVerifier.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="CP6.Platform.Release" Version="[0.10.1]" GeneratePathProperty="true" />
  </ItemGroup>
</Project>
```

- [x] Create `tools/p10/ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsTestProject>true</IsTestProject>
    <IsPackable>false</IsPackable>
    <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../ReleaseVerifier/CP6.P10.ReleaseVerifier.csproj" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" PrivateAssets="all" />
    <Using Include="Xunit" />
  </ItemGroup>
  <ItemGroup>
    <None Include="../../../eng/p10/trust/pinned-trust-store.v1.json"
          Link="trust/pinned-trust-store.v1.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

- [x] Restore only after the real corrected publication exists. Run from `tools/p10` so the exact SDK is selected; credentials must be injected only through the process-scoped `NuGetPackageSourceCredentials_github` environment variable. Do not add a password to the config or command arguments.

```powershell
dotnet --version
dotnet restore ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --configfile ../../eng/p10/NuGet.formal.config
dotnet restore ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --configfile ../../eng/p10/NuGet.formal.config --locked-mode
```

Expected: SDK `8.0.424`; both restores pass against real GitHub Packages and the second does not change the lock file. Compare the restored Release `.nupkg` SHA-256 with the frozen S04 publication before executing it. A missing version, signer failure or mismatch stops this subplan.

## Task 2: Write failing adapter tests

- [x] Create `ContractInspectionTests.cs` with these tests before the adapter implementation:

```csharp
using System.Text;
using CP6.P10.ReleaseVerifier;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier.Tests;

public sealed class ContractInspectionTests
{
    private const string TrustHash = "0a6e72951c196e612a593cc8831e294bb538c9ba8a79eada4538771a3811d8e9";

    [Fact]
    public void Public_trust_bytes_match_the_pinned_hash_and_formal_contract()
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "trust", "pinned-trust-store.v1.json"));
        var result = ContractInspection.Inspect(bytes, Cp6ReleaseContractIds.PinnedTrustStore, TrustHash);
        Assert.Equal(TrustHash, result.Sha256);
        Assert.False(result.CandidateAccepted);
        Assert.Null(result.Deployable);
    }

    [Fact]
    public void Changed_bytes_fail_the_independent_expected_hash()
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "trust", "pinned-trust-store.v1.json"));
        bytes[0] = (byte)' ';
        Assert.Equal("input-hash", Assert.Throws<Cp6ReleaseContractException>(() =>
            ContractInspection.Inspect(bytes, Cp6ReleaseContractIds.PinnedTrustStore, TrustHash)).Code);
    }

    [Fact]
    public void Matching_hash_does_not_authorize_an_unsupported_contract()
    {
        var bytes = "{}"u8.ToArray();
        Assert.Equal("inspection-schema", Assert.Throws<Cp6ReleaseContractException>(() =>
            ContractInspection.Inspect(bytes, Cp6ReleaseContractIds.SystemManifest,
                Cp6DeterministicJson.Sha256Hex(bytes))).Code);
    }

    [Fact]
    public void Canonicalization_rejects_duplicate_members()
    {
        Assert.Throws<Cp6ReleaseContractException>(() =>
            Cp6DeterministicJson.Canonicalize(Encoding.UTF8.GetBytes("{\"a\":1,\"a\":2}")));
    }

    [Fact]
    public void Empty_or_oversize_inputs_fail_before_contract_parsing()
    {
        foreach (var bytes in new[] { Array.Empty<byte>(), new byte[Cp6DeterministicJson.MaximumBytes + 1] })
            Assert.Equal("input-size", Assert.Throws<Cp6ReleaseContractException>(() =>
                ContractInspection.Inspect(bytes, Cp6ReleaseContractIds.PinnedTrustStore, TrustHash)).Code);
    }
}
```

- [x] Create `tools/p10/ReleaseVerifier.Tests/ProgramProcessTests.cs` before implementing the CLI. The System Locator below is explicitly a negative test fixture, not a candidate or acceptance record.

```csharp
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CP6.P10.ReleaseVerifier;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier.Tests;

public sealed class ProgramProcessTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "cp6-p10-inspection-test-" + Guid.NewGuid().ToString("N"));

    public ProgramProcessTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task Missing_arguments_return_usage_without_acceptance()
    {
        var result = await Run();
        Assert.Equal(2, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.StartsWith("usage:", result.Error);
    }

    [Fact]
    public async Task Valid_trust_inspection_is_explicitly_not_candidate_acceptance()
    {
        var input = TrustBytes();
        var result = await Run("inspect", Cp6ReleaseContractIds.PinnedTrustStore,
            Cp6DeterministicJson.Sha256Hex(input), Write("trust.json", input));
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using var document = JsonDocument.Parse(result.Output);
        Assert.False(document.RootElement.GetProperty("candidateAccepted").GetBoolean());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("deployable").ValueKind);
        Assert.Equal(Cp6DeterministicJson.Sha256Hex(input), document.RootElement.GetProperty("sha256").GetString());
    }

    [Fact]
    public async Task Noncanonical_trust_fails_even_when_its_raw_hash_matches()
    {
        var input = TrustBytes().Concat(new byte[] { (byte)'\n' }).ToArray();
        var result = await Run("inspect", Cp6ReleaseContractIds.PinnedTrustStore,
            Cp6DeterministicJson.Sha256Hex(input), Write("noncanonical.json", input));
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Equal("non-canonical-json", result.Error.Trim());
    }

    [Fact]
    public async Task Structurally_valid_System_locator_is_refused_by_the_Platform_adapter()
    {
        var input = Encoding.UTF8.GetBytes("""
            {"$schemaId":"https://schemas.cp6.dev/release/candidate-locator.v1","createdAtUtc":"2026-09-01T00:00:00.000Z","releaseTag":"v0.10.1-test.1","signerKeyId":"sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","subject":{"byteLength":1,"key":"objects/sha256/aa/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/candidate-result.v2.json","mediaType":"application/vnd.cp6.candidate-result.v2+json","sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","storageAuthority":"cp6-release-r2-v1"},"subjectKind":"SystemCandidateResult","trustPolicyVersion":1}
            """);
        Assert.Equal("SystemCandidateResult", Cp6ReleaseValidator.ValidateCandidateLocator(input).SubjectKind);
        var result = await Run("inspect", Cp6ReleaseContractIds.CandidateLocator,
            Cp6DeterministicJson.Sha256Hex(input), Write("system-locator.fixture.json", input));
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Equal("inspection-lane", result.Error.Trim());
    }

    [Fact]
    public async Task Canonicalize_creates_once_and_never_overwrites_existing_bytes()
    {
        var input = Write("input.json", Encoding.UTF8.GetBytes("{ \"z\": 1, \"a\": 2 }"));
        var output = Path.Combine(_directory, "output.json");
        var first = await Run("canonicalize", input, output);
        Assert.Equal(0, first.ExitCode);
        Assert.Equal("{\"a\":2,\"z\":1}", File.ReadAllText(output));
        var original = File.ReadAllBytes(output);
        Assert.Equal(Cp6DeterministicJson.Sha256Hex(original), first.Output.Trim());
        File.WriteAllText(input, "{\"different\":true}");
        var second = await Run("canonicalize", input, output);
        Assert.Equal(1, second.ExitCode);
        Assert.Equal("inspection-io", second.Error.Trim());
        Assert.Empty(second.Output);
        Assert.Equal(original, File.ReadAllBytes(output));
    }

    [Fact]
    public async Task Missing_file_error_never_discloses_the_private_path()
    {
        var path = Path.Combine(_directory, "private-marker-do-not-disclose.json");
        var result = await Run("inspect", Cp6ReleaseContractIds.PinnedTrustStore, new string('a', 64), path);
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Equal("inspection-io", result.Error.Trim());
        Assert.DoesNotContain("private-marker", result.Error);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(4 * 1024 * 1024, true)]
    [InlineData(4 * 1024 * 1024 + 1, false)]
    public void Read_bound_enforces_exact_boundary(int size, bool allowed)
    {
        var path = Write("size.bin", new byte[size]);
        if (allowed) Assert.Equal(size, ContractInspection.ReadBounded(path).Length);
        else Assert.Equal("input-size", Assert.Throws<Cp6ReleaseContractException>(() => ContractInspection.ReadBounded(path)).Code);
    }

    private static byte[] TrustBytes() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "trust", "pinned-trust-store.v1.json"));

    private string Write(string name, byte[] bytes)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static async Task<(int ExitCode, string Output, string Error)> Run(params string[] arguments)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in new[]
        {
            "exec", "--runtimeconfig", Path.Combine(AppContext.BaseDirectory, "CP6.P10.ReleaseVerifier.Tests.runtimeconfig.json"),
            "--depsfile", Path.Combine(AppContext.BaseDirectory, "CP6.P10.ReleaseVerifier.Tests.deps.json"),
            typeof(ContractInspection).Assembly.Location
        }.Concat(arguments)) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start inspection process.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        return (process.ExitCode, await output, await error);
    }

    public void Dispose()
    {
        var expectedParent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
        var actual = Path.GetFullPath(_directory);
        if (Path.GetDirectoryName(actual) != expectedParent || !Path.GetFileName(actual).StartsWith("cp6-p10-inspection-test-", StringComparison.Ordinal))
            throw new InvalidOperationException("Test cleanup escaped its private temporary directory.");
        Directory.Delete(actual, recursive: true);
    }
}
```

- [x] Run `dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --no-restore` from `tools/p10`. Expected: compilation fails because the CLI entry point and `ContractInspection` have not been implemented. Record the failure; do not add a passing stub.

## Task 3: Implement bounded inspection and the narrow CLI

- [x] Create `tools/p10/ReleaseVerifier/ContractInspection.cs`:

```csharp
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier;

public sealed record InspectionResult(string SchemaId, string Sha256, string? CandidateKind,
    bool? Deployable, bool CandidateAccepted = false);

public static class ContractInspection
{
    public static InspectionResult Inspect(byte[] bytes, string schemaId, string expectedSha256)
    {
        if (bytes.Length is < 1 or > Cp6DeterministicJson.MaximumBytes)
            throw new Cp6ReleaseContractException("input-size", "Input size is outside policy.");
        var hash = Cp6DeterministicJson.Sha256Hex(bytes);
        if (!string.Equals(hash, expectedSha256, StringComparison.Ordinal))
            throw new Cp6ReleaseContractException("input-hash", "Input hash differs from the trusted expectation.");
        var result = schemaId switch
        {
            Cp6ReleaseContractIds.PlatformCandidate => Cp6ReleaseValidator.ValidatePlatformCandidate(bytes),
            Cp6ReleaseContractIds.CandidateLocator => ValidatePlatformLocator(bytes),
            Cp6ReleaseContractIds.ReleaseGateResult => Cp6SupportingContractValidator.ValidateReleaseGateResult(bytes),
            Cp6ReleaseContractIds.EvidenceRecord => Cp6SupportingContractValidator.ValidateEvidenceRecord(bytes),
            Cp6ReleaseContractIds.BuildProvenance => Cp6SupportingContractValidator.ValidateBuildInvocationProvenance(bytes),
            Cp6ReleaseContractIds.PinnedTrustStore => Cp6PinnedTrustPolicy.Parse(bytes).ValidatedDocument,
            _ => throw new Cp6ReleaseContractException("inspection-schema", "Contract is not supported by this inspection command.")
        };
        return new(result.SchemaId, result.Sha256, result.CandidateKind, result.Deployable);
    }

    private static Cp6ValidatedReleaseDocument ValidatePlatformLocator(byte[] bytes)
    {
        var result = Cp6ReleaseValidator.ValidateCandidateLocator(bytes);
        if (result.SubjectKind != "PlatformReleaseCandidate")
            throw new Cp6ReleaseContractException("inspection-lane", "Only the non-deployable Platform lane is supported.");
        return result;
    }

    public static byte[] ReadBounded(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var buffer = new byte[Cp6DeterministicJson.MaximumBytes + 1];
        var count = 0;
        while (count < buffer.Length)
        {
            var read = input.Read(buffer, count, buffer.Length - count);
            if (read == 0) break;
            count += read;
        }
        if (count is < 1 or > Cp6DeterministicJson.MaximumBytes)
            throw new Cp6ReleaseContractException("input-size", "Input size is outside policy.");
        return buffer.AsSpan(0, count).ToArray();
    }
}
```

- [x] Confirm the restored assembly exposes the already verified producer API `Cp6ValidatedReleaseDocument(SchemaId, CandidateKind, SubjectKind, Deployable, Sha256, RepositoryNames, PackageIds, SubjectHashes, CanonicalUtf8)`. The projection above uses its actual `SchemaId`, `Sha256`, `CandidateKind` and `Deployable` properties; no shadow Platform DTO is introduced.
- [x] Create `tools/p10/ReleaseVerifier/Program.cs`:

```csharp
using System.Text.Json;
using CP6.P10.ReleaseVerifier;
using CP6.Platform.Release;

try
{
    if (args is ["canonicalize", var inputPath, var outputPath])
    {
        var bytes = Cp6DeterministicJson.Canonicalize(ContractInspection.ReadBounded(inputPath));
        using var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        output.Write(bytes);
        Console.WriteLine(Cp6DeterministicJson.Sha256Hex(bytes));
        return 0;
    }
    if (args is ["inspect", var schemaId, var expectedHash, var path])
    {
        var result = ContractInspection.Inspect(ContractInspection.ReadBounded(path), schemaId, expectedHash);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(result,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        Console.WriteLine(System.Text.Encoding.UTF8.GetString(Cp6DeterministicJson.Canonicalize(bytes)));
        return 0;
    }
    Console.Error.WriteLine("usage: canonicalize INPUT NEW_OUTPUT | inspect SCHEMA_ID EXPECTED_SHA256 INPUT");
    return 2;
}
catch (Cp6ReleaseContractException error)
{
    Console.Error.WriteLine(error.Code);
    return 1;
}
catch (Exception)
{
    Console.Error.WriteLine("inspection-io");
    return 1;
}
```

- [x] Run `dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --no-restore`; expect all 14 cases to pass, including actual child-process tests. A compiler, host-selection or missing runtime configuration failure is not a passing CLI regression; correct the adapter/test harness and rerun before checkpointing.
- [x] Keep `candidateAccepted=false` in every inspection result. A matching hash and valid contract do not prove a signature, current trust, a completed workflow, storage availability, package integrity or image provenance.

## Task 4: Review and checkpoint the foundation

- [x] Run the five baseline public/legacy contract commands again:

```powershell
pwsh -NoProfile -File tools/Test-CrmV1Prd.ps1
pwsh -NoProfile -File tools/Test-CrmSaasPublicContract.ps1
pwsh -NoProfile -File scripts/test-release-shadow-contract.ps1
pwsh -NoProfile -File scripts/test-r2-deployment-contract.ps1
pwsh -NoProfile -File scripts/test-r2-operations-contract.ps1
```

- [x] Review the actual lock files: the sole Platform dependency in the tool must be exact `CP6.Platform.Release 0.10.1` from the mapped GitHub feed. Its restored archive must match the frozen publication hash. No application ProjectReference or copied Platform source/Schema is permitted.
- [ ] Review `git diff --check` and the complete diff, then stage only the file-map entries and this plan. Commit with `feat(p10): add package-fed read-only contract inspection`.
- [ ] Do not open a completion PR for this foundation alone. Continue the separate evidence-graph and publication plans on this same S06 task branch, wire the new tests into CI, update the four public project-memory ledgers, and require real S05/S06 evidence before the overall P10 final decision.

## Planning coverage and remaining S06 boundaries

This subplan covers only exact package consumption, bounded canonical bytes and local API delegation. The S06 parent design still requires a separately reviewed complete graph verifier, real seven-package signature checks, actual CRM evidence, OCI digest/signature/SBOM/scan proof, immutable GitHub workflow bindings, authenticated create-only R2 transport, a signed Locator, clean pre-commit verification, the one final Locator write, clean post-commit verification, and a cross-repository audit. No partial implementation here is a substitute for those acceptance requirements.

The later transport plan must follow the approved distinct-key/Environment amendment and current [R2 temporary-credential documentation](https://developers.cloudflare.com/r2/api/s3/temporary-credentials/): 900-second local JWT credentials, explicit approved actions and prefixes, no `scope` claim alongside `actions`, no deletion or multipart operations. Locator signing follows the [cosign blob-signing interface](https://docs.sigstore.dev/cosign/signing/signing_with_blobs/) with the already pinned v3.1.3 binary and distinct Locator key.

## Local foundation checkpoint (2026-09-07)

- Actual S04 run `34126521193`, attempt `1`, succeeded on both OS jobs. Its publication record hash is `8b5fc47fd77902a3433d61b4cf181b67ef61ee994b60ea8286a690ab5084c961`; Platform evidence PR #52 merged as `421951a44f1dcc05b7aeb51c24a0ccaf2f03ef5f`. That evidence merge's exact-main CI remains a separate requirement.
- Fresh authenticated GitHub Packages restore obtained `CP6.Platform.Release 0.10.1`; the actual `.nupkg` SHA-256 equals `afe85eabe78965e0d7967f3f2cf54574c8adcb02141523aecd8a8bafc6d700e0` and cache metadata names the approved GitHub feed. Both subsequent lock files remained byte-identical under locked restore.
- The expected missing-entry-point compiler error was only a scaffold check. A minimal throwing API/exit-2 CLI scaffold then produced 13 actual test failures and one existing-library success. After implementation, all 14 tests passed in both Debug and Release, with zero skipped. No throwing scaffold remains.
- `dotnet format --verify-no-changes --no-restore` passed. All five retained public/legacy contract suites passed, including unchanged private R00 payload SHA-256 `64a53dd895aedc20a51288ad0ffdb69f60ddc7c22012c1df83984efba5adbc03`. Their synthetic scenarios are regression coverage, not deployment evidence.
- This checkpoint is local foundation work only. It has not run a complete candidate graph, accessed CRM through the Environment credential, built/pushed an OCI image, written R2, signed a Locator, or completed S06. Continue the remaining graph/publication subplans and CI wiring before an S06 completion PR.
