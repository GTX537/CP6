# P10 S06 Validation Environment Identity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans sequentially in this task. The user already selected no delegation.

**Goal:** Truthfully bind the S06 validation identity to the existing protected P10 Environment.

**Architecture:** Validation needs the already-approved CRM read credential and OCI signing identity, all scoped to p10-platform-candidate. Correct the local graph profile assumption, not the external protection. Validation and publication stay separate main-only workflows with distinct run IDs and the same source commit; parent design section 12.1 still requires validation to complete before publication starts.

**Tech Stack:** .NET 8, CP6.Platform.Release [0.10.1], xUnit.

---

## Scope and authority

The completed prerequisite plan fixes p10-platform-candidate and its owner reviewer/main-only policy. No new Environment, credential, approval or production authority is requested. Validation may build/sign only the separately authorized ghcr.io/gtx537/cp6-p10-verifier non-deployable tool image; that operation belongs to validation, not publication. R2 publication cannot begin before the validation workflow completes.

The earlier graph plan's environment none was an implementation assumption, not a parent-design requirement. The CRM S05 historical identity remains none. Tests use an explicitly unpublished graph and never establish real workflow approval or candidate acceptance.

Files: this plan; new tools/p10/ReleaseVerifier.Tests/ValidationEnvironmentTests.cs; tools/p10/ReleaseVerifier/S06ReleaseIdentity.cs; tools/p10/ReleaseVerifier.Tests/EvidenceGraphFixture.cs; docs/superpowers/plans/2026-09-07-p10-s06-evidence-graph.md. The old publisher-correction plan remains a historical record.

## Task 1: Write and observe the red regression

- [x] Add the test file with the complete code below.

```csharp
using CP6.P10.ReleaseVerifier;
using CP6.Platform.Release;
using static CP6.P10.ReleaseVerifier.Tests.EvidenceGraphFixture;

namespace CP6.P10.ReleaseVerifier.Tests;

// Unpublished graph regression only; this is not proof of an approved workflow run.
public sealed class ValidationEnvironmentTests
{
    [Fact]
    public void Validation_declares_the_existing_protected_P10_environment()
    {
        var input = Build(candidateChange: root => root["verifier"]!["environment"] = "p10-platform-candidate");
        var result = EvidenceGraphInspection.Inspect(input.Candidate, input.Objects);
        Assert.Equal("p10-platform-candidate",
            result.Candidate.GetProperty("verifier").GetProperty("environment").GetString());
        Assert.NotEqual(result.Candidate.GetProperty("verifier").GetProperty("runId").GetInt64(),
            result.Candidate.GetProperty("publisher").GetProperty("runId").GetInt64());
        Assert.Equal("none", result.Candidate.GetProperty("crmConsumer").GetProperty("environment").GetString());
        Assert.False(result.CandidateAccepted);
    }

    [Fact]
    public void Validation_cannot_claim_an_unprotected_environment()
    {
        var input = Build(candidateChange: root => root["verifier"]!["environment"] = "none");
        var failure = Assert.Throws<Cp6ReleaseContractException>(() =>
            EvidenceGraphInspection.Inspect(input.Candidate, input.Objects));
        Assert.Equal("graph-workflow-identity", failure.Code);
    }
}
```

- [x] Run from tools/p10 with the configured .NET 8 host.

```powershell
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --no-restore -c Release --filter FullyQualifiedName~ValidationEnvironmentTests
```

Expected: two behavioral failures. Protected identity is rejected; none is incorrectly accepted.

## Task 2: Correct the profile and unpublished fixture

- [x] Replace the verifier line in S06ReleaseIdentity.cs with:

```csharp
        RequireWorkflow(verifier, "GTX537/CP6", ValidationPath, "p10-platform-candidate");
```

- [x] Replace the verifier member in EvidenceGraphFixture.cs with:

```csharp
            ["verifier"] = Workflow("GTX537/CP6", ".github/workflows/p10-platform-validation.yml", PublicSource,
                new string('5', 40), 999991, "p10-platform-candidate"),
```

- [x] Apply the same two exact code replacements in the evidence-graph plan and replace its workflow scope bullet with:

```text
- Public workflow paths are .github/workflows/p10-platform-validation.yml and .github/workflows/p10-platform-candidate.yml. Their commits must agree, their run IDs must differ, and both declare the existing protected environment p10-platform-candidate. Validation requires protected CRM read and OCI signing inputs; this corrects the initial environment none assumption. External proof must establish completed successful validation before publication starts; a candidate never predicts publisher completion.
```

- [x] Rerun focused and full tests, with the existing actual cosign, formal package and read-only feed/GitHub inputs.

```powershell
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --no-restore -c Release --filter FullyQualifiedName~ValidationEnvironmentTests
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --no-restore -c Release
dotnet format ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --verify-no-changes --no-restore
```

Expected: 2 focused, 732 total, no skips; formatter exit 0.

## Task 3: Review and checkpoint

- [x] Inspect all five scoped files; verify old R2 rejection cases, source pins, signing policy, package locks and runtime code are unchanged.
- [x] Record observed test results here, then stage only the five files and commit normally.

```powershell
git diff --check
git add -- docs/superpowers/plans/2026-09-08-p10-s06-validation-environment.md docs/superpowers/plans/2026-09-07-p10-s06-evidence-graph.md tools/p10/ReleaseVerifier/S06ReleaseIdentity.cs tools/p10/ReleaseVerifier.Tests/EvidenceGraphFixture.cs tools/p10/ReleaseVerifier.Tests/ValidationEnvironmentTests.cs
git diff --cached --check
git commit -m "fix(p10): declare protected validation environment"
```

- [ ] Complete S06 live workflows, evidence publication, confirmation and cross-repository audit; this correction alone cannot close P10.

## Observed results (2026-09-08 UTC)

- Baseline: 730/730 passed, no skips.
- Both new tests failed for the predicted behavior before implementation; the saved red TRX reports two failures and zero skips.
- The corrected profile passed 2/2 focused and 732/732 full Release tests, zero skips, no build warnings/errors; complete format verification exited 0.
- Reviewed the five-file diff and exact plan/test parity. The only production change is the verifier Environment identity; no workflow, external protection, signing key, trust hash, package lock, CRM behavior or R2 gate changed.
