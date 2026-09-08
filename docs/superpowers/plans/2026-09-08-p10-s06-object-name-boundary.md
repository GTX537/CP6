# P10 S06 Content Address Filename Boundary Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans inline and sequentially; the owner already selected this mode.

**Goal:** Reject a terminal newline in content-address filenames at both creation and parsing.

**Architecture:** Reuse the existing filename profile with a strict end-of-input regex anchor. This fixes the same .NET end-anchor edge case discovered in the workflow archive reader without changing any allowed normal filename or storage authority.

**Tech Stack:** .NET 8, xUnit, existing content-address adapter.

## Task 1 — Regression first

- [x] Insert these tests before `Invalid_declared_lengths_are_rejected`'s theory attributes in `tools/p10/ReleaseVerifier.Tests/ContentAddressedObjectsTests.cs` and run the focused test. Both cases must fail because no exception is thrown.

```csharp
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Terminal_newline_is_not_a_valid_content_address_file_name(bool parse)
    {
        Assert.Throws<Cp6ReleaseContractException>(() =>
        {
            if (!parse) ContentAddress.Create(Bytes, Cp6ReleaseMediaTypes.InToto, "proof.json\n");
            else
            {
                var node = JsonNode.Parse(Address().ToJson().GetRawText())!.AsObject();
                node["key"] = Address().Key + "\n";
                ContentAddress.Parse(JsonSerializer.SerializeToElement(node));
            }
        });
    }
```

## Task 2 — Exact minimal fix

- [x] In `tools/p10/ReleaseVerifier/ContentAddressedObjects.cs`, replace the filename regex declaration with:

```csharp
    private static readonly Regex FileNamePattern = new(
        @"^[a-z0-9][a-z0-9.-]{0,127}\.json\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
```

## Task 3 — Verify and checkpoint

- [x] Run all content-address tests, full Release verifier tests with required actual test inputs, and formatting verification.
- [ ] Save the reviewed three-file scope as a local commit.

From `tools/p10` in the existing task worktree and .NET 8.0.424 environment:

```powershell
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --no-restore -c Release --filter FullyQualifiedName~Terminal_newline_is_not_a_valid_content_address_file_name
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --no-restore -c Release --filter FullyQualifiedName~ContentAddressedObjectsTests
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --no-restore -c Release
dotnet format ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --no-restore --verify-no-changes
```

From the repository root:

```powershell
git diff --check
git add -- tools/p10/ReleaseVerifier/ContentAddressedObjects.cs tools/p10/ReleaseVerifier.Tests/ContentAddressedObjectsTests.cs docs/superpowers/plans/2026-09-08-p10-s06-object-name-boundary.md
git diff --cached --check
git commit -m "fix(p10): reject terminal newlines in object names"
```

## Review

Both creator and parser use the same strict filename check; the tests exercise each entry. No schema/trust/network/gate policy, external object or published package changes. Component verification does not mark S06 complete.

## Execution record — 2026-09-08

Both new regressions first failed because a terminal-newline filename was incorrectly accepted. After the strict anchor change, all 37 content-address tests passed and formatting passed. The initial full run had 1,180 passes and one real feed-read timeout at the existing 60-second boundary; no code or timeout was changed to handle that result. The unchanged isolated real-read test then passed, followed by a fresh full Release run of 1,181/1,181 passed, zero skipped. Exact diff scope is the shared regex, two regressions and this plan. No remote mutation occurred.
