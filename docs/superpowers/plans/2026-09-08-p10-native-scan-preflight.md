# P10 native scanner compatibility implementation plan

> Execute inline with executing-plans, honoring the owner's no-subagent instruction.

**Goal:** Fix the actual Trivy CLI failure and verify the remaining native report/command boundaries against the already built image before another protected validation.

**Architecture:** Preserve pinned tool versions, full raw reports, image digest identity, vulnerability thresholds, signing separation and fail-closed verification. Use Trivy's actual `remote` image source; Syft independently uses its `registry:` URI scheme. Read the existing failed-run image without rebuilding or publishing it. Local probes are diagnostic evidence only, not candidate acceptance.

**Tech stack:** .NET 8/xUnit, GitHub Actions Bash, Trivy 0.74.0, Syft 1.51.1, cosign 3.1.3.

## Tasks

- [x] Add a failing wiring regression in `tools/p10/ReleaseVerifier.Tests/S06WorkflowWiringTests.cs` requiring `trivy" image --image-src remote` and rejecting `--image-src registry`, while retaining Syft `registry:$image`, all severities, raw SARIF and the HIGH/CRITICAL gate.
- [x] Run `dotnet test tools/p10/ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~S06WorkflowWiringTests`; confirm the new assertion fails against the current workflow (1 expected failure, 7 passes).
- [x] Replace only Trivy's `--image-src registry` with `--image-src remote` in `.github/workflows/p10-platform-validation.yml`, then run the regression again (8/8 passes).
- [x] Download the exact native tools from their official release assets, check SHA-256, reproduce the invalid option with the native CLI, then generate unmodified full SARIF/SPDX for `ghcr.io/gtx537/cp6-p10-verifier@sha256:9e43db543651f35503def911d6cd7bf919fbc54c714a1dbaaa639ca94b93abe8`. Do not start Docker Desktop, build locally, suppress vulnerabilities or reuse this failed run as accepted evidence.
- [x] Pass the actual report bytes through the existing parsers; no format incompatibility was found, so no parser change is needed. Native cosign accepts all three selected signing flags (the two legacy flags emit deprecation warnings); container commands were statically reviewed, not executed. Do not change signing keys or grant more permissions.
- [x] Run the full real-input suite (**1635/1635, zero failures/skips**), format, three-workflow actionlint and complete diff review. Record actual run 34234554610 attempt 2 (CRM/package/tests/image passed, Trivy argument failed, signing/finalization skipped), along with local native evidence in the operator docs and four project ledgers.
- [ ] Commit scoped files and complete normal PR/checks/merge after the new cosign supply-chain blocker has an approved resolution. Do not dispatch another known-failing validation. After remediation, verify remote main ancestry and merged smoke/all main checks, dispatch one new exact-main validation, and ask owner for Environment approval; continue actual validation to its full outcome.

## Initial evidence

Run 34234554610 attempt 2 uses source `a41711dd55a093ab0ed127d599e0e7bcf11d548d`. Trivy's actual error lists only `docker`, `containerd`, `podman`, `remote`. The digest already exists in GHCR; no signature or validation handoff was produced. The exact Windows Trivy archive SHA-256 is `94c40e0696e4b907a74b7b2e1438d5d72ebaca83115817407f568a002d520842`, and Syft archive SHA-256 is `5e4bc3e6b6344b4625de0f7aa5351aaa72856d11d78462972de0a101ee2c1c8f`, verified against official release metadata. Windows native probes do not replace the required protected Linux execution.

## Actual native preflight and newly established blocker

The hosted log confirms `prepared` at `2026-09-08T14:10:37.9288149Z`, then **1634/1634 tests, zero failures/skips**, and the image push before Trivy's argument error. This establishes the actual package/CRM-input stages for this attempt, not for earlier failures.

Both diagnostic commands read the existing digest directly from GHCR, using the exact pinned releases; no image was built, signed or republished locally:

```text
syft registry:ghcr.io/gtx537/cp6-p10-verifier@sha256:9e43db543651f35503def911d6cd7bf919fbc54c714a1dbaaa639ca94b93abe8 -o spdx-json=actual-image.spdx.json
trivy image --image-src remote --scanners vuln --severity UNKNOWN,LOW,MEDIUM,HIGH,CRITICAL --exit-code 0 --format sarif --output actual-image.sarif.json ghcr.io/gtx537/cp6-p10-verifier@sha256:9e43db543651f35503def911d6cd7bf919fbc54c714a1dbaaa639ca94b93abe8
```

Native SPDX has 673490 bytes, SHA-256 `d71e413cb57bd399e29f8cca55c972ab14f7c0bb42f031d35d626045b4e62050`, creation `2026-09-08T14:16:13Z`. The unchanged `SpdxImageReport.Read` accepts these exact bytes and observes 276 non-root packages including `CP6.Platform.Release 0.10.1`.

Native SARIF has 121385 bytes, SHA-256 `903598a2d583a9f430461e05b22b97dc8be5245d2694b45fa9839c575d1ac632`, 26 rules and 31 results: UNKNOWN 3, LOW 7, MEDIUM 6, HIGH 14, CRITICAL 1. Trivy DB `UpdatedAt=2026-09-08T07:08:01.235696926Z`. The unchanged `SarifImageReport.Read` validates the actual format and manifest/config identity (`sha256:ed81f5f8098d0ef5ca6db20663ffa49676476dfc5bdecab43f1d8c153de1cbc7`), then correctly rejects with `image-vulnerability`. This is a real dependency gate, not a parser incompatibility. Raw reports are not rewritten or filtered.

All blocking results identify `/opt/cp6/cosign`: `golang.org/x/crypto v0.53.0` (CRITICAL CVE-2026-56854, fixed 0.55.0), `golang.org/x/mod v0.37.0` (2 HIGH, fixed 0.40.0), `golang.org/x/text v0.38.0` (1 HIGH, fixed 0.39.0), `google.golang.org/grpc v1.82.0` (2 HIGH, fixes 1.82.1/1.83.1), and Go stdlib `v1.26.4` (9 HIGH, report lists 1.26.6 or later fixes). These are scanner findings, not a claim that every vulnerable path is exploitable by this verifier.

At the September 8 preflight, both GitHub's release API and the [official cosign release page](https://github.com/sigstore/cosign/releases/tag/v3.1.3) identify `v3.1.3` as the latest release; the release list contains no newer published binary. The existing pinned Linux SHA-256 matches the official asset. Rebuilding or adopting another distributor's security-patched cosign changes the approved binary supply chain and needs an owner decision; it must not be silently substituted or represented as the unmodified official release. No vulnerability exception, signing-key change, signature publication, candidate publication, audit acceptance or production deployment is authorized by this diagnostic result.
