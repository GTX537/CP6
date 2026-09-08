# P10 S06 Publisher Environment Binding Correction Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans sequentially in this task. The user already selected no delegation.

**Goal:** Bind the evidence graph to the approved P10 publisher Environment, excluding the historical R2 Environment.

**Architecture:** Correct one fixed profile value and its unpublished fixture. Add independent positive/negative assertions so an old parent-design value cannot silently return. No Environment, secret, approval, workflow or candidate object is mutated.

**Tech Stack:** .NET 8, CP6.Platform.Release [0.10.1], xUnit.

---

## Evidence and scope

The completed prerequisite plan fixes public CP6 Environment p10-platform-candidate with reviewer GTX537 and main-only branch policy; this supersedes the old parent design's r2-candidate name. A fresh read-only GitHub API query confirms that named Environment still has required_reviewers and custom branch-policy protection. The graph implementation incorrectly retained the older name. The source of truth is the approved prerequisite, not a new choice or permission change.

Only the fixed profile, unpublished fixture, their prior plan's corresponding complete-code blocks, a new two-case test file and this correction plan change. The existing r2-candidate workflow path and rejection-test mutations remain valid negative inputs and are not renamed.

## Task 1: Red regression

- [x] Add tools/p10/ReleaseVerifier.Tests/PublisherEnvironmentTests.cs before changing the profile:

```csharp
using CP6.P10.ReleaseVerifier;
using CP6.Platform.Release;
using static CP6.P10.ReleaseVerifier.Tests.EvidenceGraphFixture;

namespace CP6.P10.ReleaseVerifier.Tests;

public sealed class PublisherEnvironmentTests
{
    [Fact]
    public void Approved_P10_environment_is_the_only_publisher_identity()
    {
        var input = Build(candidateChange: root => root["publisher"]!["environment"] = "p10-platform-candidate");
        var result = EvidenceGraphInspection.Inspect(input.Candidate, input.Objects);
        Assert.Equal("p10-platform-candidate", result.Candidate.GetProperty("publisher").GetProperty("environment").GetString());
        Assert.False(result.CandidateAccepted);
    }

    [Fact]
    public void Historical_R2_environment_cannot_substitute_for_the_P10_publisher()
    {
        var input = Build(candidateChange: root => root["publisher"]!["environment"] = "r2-candidate");
        var failure = Assert.Throws<Cp6ReleaseContractException>(() =>
            EvidenceGraphInspection.Inspect(input.Candidate, input.Objects));
        Assert.Equal("graph-workflow-identity", failure.Code);
    }
}
```

- [x] Run from tools/p10 using the configured approved .NET 8 host:

```powershell
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --no-restore --configuration Release --filter FullyQualifiedName~PublisherEnvironmentTests
```

Expected: both fail with current behavior; the approved environment throws graph-workflow-identity and the historical environment incorrectly passes.

## Task 2: Correct the exact profile and fixture

- [x] In tools/p10/ReleaseVerifier/S06ReleaseIdentity.cs replace the publisher check with:

```csharp
        RequireWorkflow(publisher, "GTX537/CP6", PublicationPath, "p10-platform-candidate");
```

- [x] In tools/p10/ReleaseVerifier.Tests/EvidenceGraphFixture.cs replace the complete publisher member with:

```csharp
            ["publisher"] = Workflow("GTX537/CP6", ".github/workflows/p10-platform-candidate.yml", PublicSource,
                new string('4', 40), 999992, "p10-platform-candidate"),
```

- [x] Apply these identical two replacements to docs/superpowers/plans/2026-09-07-p10-s06-evidence-graph.md, and replace its corresponding scope bullet with:

```text
- Public workflow paths are .github/workflows/p10-platform-validation.yml and .github/workflows/p10-platform-candidate.yml. Their commits must agree, their run IDs must differ, and only the publisher declares environment p10-platform-candidate, as fixed by the completed prerequisite plan; the read-only validation identity uses environment none. External proof must establish completed successful validation before publication; a candidate never predicts publisher completion.
```

- [x] Rerun the focused tests and all existing tests with required real cosign/package inputs:

```powershell
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --no-restore --configuration Release --filter FullyQualifiedName~PublisherEnvironmentTests
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --no-restore --configuration Release
dotnet format whitespace ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --no-restore --verify-no-changes
```

Expected: 2 focused and 208 total pass, none skipped; format exits 0.

## Task 3: Review and checkpoint

- [ ] Review all five files, confirm no actual Environment/workflow/trust change, record observed results and stage only those files:

```powershell
git diff --check
git add -- docs/superpowers/plans/2026-09-07-p10-s06-publisher-environment.md docs/superpowers/plans/2026-09-07-p10-s06-evidence-graph.md tools/p10/ReleaseVerifier/S06ReleaseIdentity.cs tools/p10/ReleaseVerifier.Tests/EvidenceGraphFixture.cs tools/p10/ReleaseVerifier.Tests/PublisherEnvironmentTests.cs
git diff --cached --check
git commit -m "fix(p10): bind publisher to approved P10 environment"
```

Full S06 network/proof/publication/confirmation/audit work remains after this correction; no candidate acceptance is claimed.

## Observed results

- Both regression tests failed for the predicted behavioral reasons before the correction (2 failed, 0 skipped).
- After the exact profile/fixture change, focused tests passed 2/2 and the complete Release suite passed 208/208, no skips or build warnings/errors. Whitespace verification exited 0.
- Reviewed the five-file scope and verified that old R2 workflow-path rejection tests remain unchanged. No remote configuration, signing secret, trust instance, workflow or candidate was changed.
