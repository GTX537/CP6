# P10 S06 cross-platform timestamp Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Execute inline, following the user's sequential-work instruction.

**Goal:** Accept the two independently recorded S04 system-trusted TSA paths without requiring producer and consumer operating systems to build identical paths.

**Architecture:** Keep real NuGet signature verification and `X509Chain.Build` with system trust, online entire-chain revocation, timestamp verification time and no ignored errors. One internal allowlist checks both the freshly verified chain and the historical statement chain. Compare every other immutable package proof exactly; retain each producer's recorded path in the signed observation.

**Tech Stack:** .NET 8, NuGet.Packaging 6.11.2, xUnit, existing deterministic in-toto statements.

---

## Evidence and scope

Review found that `NuGetPackageChecks` pins only the Windows four-certificate path. `FormalVerificationEvidence.Read` also compares the complete historical path to the consumer's system-selected path. The actual S04 records were rehashed on 2026-09-08:

| Record | Raw SHA-256 |
| --- | --- |
| `formal-package-verification.windows.v1.json` | `2ce1772d60c1514fac2f1cc6c68e305eadfb7e8fcd0cd7267fb7c9286ea7fe5f` |
| `formal-package-verification.linux.v1.json` | `64aac879a0591116dcbbeaa790becd739934851a22e7d196c078cae2f2f85fd5` |

All seven entries in each record have the same ordered chain for that OS. Both paths start with leaf `2da09da7f4131f9fe72db6c5e6e9c9656755af043f1ea742cc0d2120e141ebfc` and intermediate `ca0b1554ecd901ea19dcad8749e9f2648c8d6dfcea1add9d2c2109415bb82ccd`. Windows continues through `33846b545a49c9be4903c60e01713c1bd4e4ef31ea65cd95d69e62794f30b941` to root `3e9099b5015e8f486c00bcea9d111ee721faba355a89bcf1df69561e3dc6325c`; Linux terminates at root `552f7bdcf1a7af9e6ce672017f4f12abf77240c78e761ac203d1d9d20ac89988`.

The old S04 records and formal packages are immutable and are not rewritten. Unit statements with alternate approved paths are policy vectors, not authenticated hosted-run evidence. A passing Windows suite cannot be reported as a fresh Linux runtime verification.

## Task 1: Reproduce and fix the path assumption

**Files:**

- Modify: `tools/p10/ReleaseVerifier.Tests/FormalVerificationEvidenceTests.cs`
- Create: `tools/p10/ReleaseVerifier/NuGetTimestampPaths.cs`
- Modify: `tools/p10/ReleaseVerifier/NuGetPackageChecks.cs`
- Modify: `tools/p10/ReleaseVerifier/FormalVerificationEvidence.cs`
- Modify: `tools/p10/ReleaseVerifier.Tests/FormalNuGetVerifierTests.cs`

- [x] Add a theory for `windows` and `linux` historical paths. Start from `Actual.Value` (seven authenticated downloads and real crypto proofs); replace only each statement's `timestampCertificateChainSha256` with the exact reviewed path and call the existing `FormalVerificationEvidence.Read`. Confirm the other-platform path fails with `s06-packages-proof` before implementation.

```csharp
var actual = await Actual.Value;
var root = JsonNode.Parse(actual.CopyBytes())!;
foreach (var package in root["predicate"]!["details"]!["packages"]!.AsArray())
    package!["timestampCertificateChainSha256"] = JsonSerializer.SerializeToNode(ReviewedPath(path));
_ = FormalVerificationEvidence.Read(Canonical(root), Producer, DateTimeOffset.UtcNow, actual.Packages);
```

- [x] Add both-path negative theories for missing, changed leaf, intermediate or root, mixed path, reversed path, duplicate root, uppercase hash, null and non-array shapes. Use the same real package set and require `Cp6ReleaseContractException`; none are release evidence.
- [x] Run `dotnet test tools/p10/ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~FormalVerificationEvidenceTests`; supply the existing in-memory `P10_FEED_READ_TOKEN` without printing it. Expect exactly the cross-platform positive vector to fail on Windows.
- [x] Create an internal `NuGetTimestampPaths.Require(IReadOnlyList<string> hashes)` with two private exact arrays from the evidence table. Its implementation is:

```csharp
PinnedNuGetTrust.Require(hashes.SequenceEqual(Windows, StringComparer.Ordinal) ||
    hashes.SequenceEqual(Linux, StringComparer.Ordinal), "nuget-timestamp-chain-binding");
```

- [x] Remove the old single `TimestampChain` field. After the unchanged successful `chain.Build(certificate!)` and hash calculation, replace the old equality check with `NuGetTimestampPaths.Require(hashes);`.
- [x] In `FormalVerificationEvidence.Read`, before canonical claim comparison, read and validate the historical array and pass it to `Claim`:

```csharp
var historicalPath = claims[index].GetProperty("timestampCertificateChainSha256")
    .EnumerateArray().Select(value => value.GetString()!).ToArray();
NuGetTimestampPaths.Require(historicalPath);
Require(Canonical(claims[index]).AsSpan().SequenceEqual(Canonical(Claim(actual[index], retrieved, historicalPath))),
    "s06-packages-proof");
```

- [x] Extend private `Claim` with `IReadOnlyList<string>? historicalPath = null` and serialize `timestampCertificateChainSha256 = historicalPath ?? proof.TimestampCertificateChainSha256`. `CollectAsync` retains the actually verified path. Validate actual proof paths in `Select` using `NuGetTimestampPaths.Require(package.Proof.TimestampCertificateChainSha256)` alongside the existing exact package hash/feed checks.
- [x] Replace the OS-specific assertion in the real seven-package test with `NuGetTimestampPaths.Require(result.TimestampCertificateChainSha256);` plus exact equality of the published and freshly verified leaf certificate; retain all other real crypto assertions and mutation tests.
- [x] Rerun the focused suite and real `FormalNuGetVerifierTests`: all pass, no skips. Run the entire ReleaseVerifier test suite with existing real feed/GitHub/package/cosign inputs and `dotnet format ... --no-restore --verify-no-changes`.

## Task 2: Review and record

- [x] Add the two-path policy and link this evidence analysis from `docs/devops/P10-PLATFORM-REFERENCE.md`; do not claim hosted Linux success from a local policy vector.
- [x] Review `git diff --check` and the complete scoped diff; ensure no trust/revocation bypass, secrets or changes to existing R2/deploy workflows.
- [ ] Explicitly stage only the five code/test files, this plan, reference documentation and four current project ledgers; commit `fix(p10): accept reviewed cross-platform timestamp paths` after fresh verification.
- [ ] Resume full S06 branch review, protected integration and actual hosted validation. P10 remains incomplete until genuine publication, normal read-only audit and the append-only project closeout are complete.

## Execution evidence

- The first invocation had an incorrect project filename and returned MSB1009; it is not counted as RED evidence. The corrected focused run compiled successfully and produced exactly one expected Linux-path failure, 56 passed and zero skipped.
- GREEN: the formal evidence and real NuGet suites passed 92/92, zero skipped. The full Release suite then passed 1550/1550, zero failed/skipped; `dotnet format --no-restore --verify-no-changes` returned zero. Three P10 YAML files also passed actionlint.
- Review outcome: the Windows-only path assumption was the blocking finding and is now covered by both-path positive and 20 negative policy vectors. Actual chain building, CMS/RFC3161 checks, online revocation, fixed package hashes and trust bootstrap remain unchanged. No remaining blocking code-review findings were identified in the scoped fix.
- Docker's Linux engine was unavailable locally. This record does not claim a new Linux runtime pass. The protected hosted validation, publication and normal audit remain required and unexecuted at this checkpoint.
