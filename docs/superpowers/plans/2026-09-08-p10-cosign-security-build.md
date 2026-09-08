# P10 cosign security build implementation plan

> Execute inline with `executing-plans`; the owner has prohibited subagents and has now approved the reviewed, reproducible security build plus local Docker diagnostics.

**Goal:** Remove the actual cosign dependency findings without weakening scanning, key separation, signature verification or candidate acceptance.

**Architecture:** Build an explicitly named `3.1.3-cp6.1` derivative of the signed upstream `v3.1.3` commit, with only reviewed dependency lock changes and the Go 1.26 security patch toolchain. Fetch immutable, independently checked source/toolchain inputs; compile static Linux/Windows amd64 binaries with deterministic flags, then require the reviewed output hashes before use. All three hosted workflows reproduce the identical helper binary without rebuilding the candidate image during publication/audit. Local Docker is diagnostic only; formal candidates still require approved hosted validation.

**Tech stack:** Go 1.26.8, Bash, Docker Linux amd64, .NET 8/xUnit, GitHub Actions, pinned Syft 1.51.1 and Trivy 0.74.0.

## Approved inputs and scope

- Existing task branch `codex/p10-native-scan-preflight` is isolated and clean at checkpoint `6e34683b3c33102e28a4da7f10b95255cf103236`; freshly fetched remote main remains `a41711dd55a093ab0ed127d599e0e7bcf11d548d`. Keep the prior parameter fix in this same failure-remediation task. Do not touch the root worktree or its stale local main.
- Upstream annotated tag object `2f3a85b04907df5b770eb049d7e4d08d4b018d86` is verified and points to `11926fa5bbbbde47e88fc006b625a17769b743b2`. Go proxy `v3.1.3.info` independently identifies that commit.
- Official module zip SHA-256: `fbf05afe62db35ca00129ff65fab6f7ad8b7851ae1cf7dbb4b1789b6ccd65db2`; sumdb module checksum: `h1:001JQRI/PJ/5T+g/kJ1KTvKFbb322+fomc+pHDZ/6sg=`. Go Linux amd64 archive SHA-256: `d0f743b33e8d8945e6b1f432edd15785c70507121d6e2a723b21285eddf8b57b`.
- Local clean-room builder: `docker.io/library/golang@sha256:bc6beb46032d45f421cf400036bf031cdc64f683ba9cdc124e31d063e71670bd`, selected from the official `1.26.8-bookworm` Linux amd64 manifest. Limit memory/CPU and do not stop existing business containers, prune their caches, mount the Docker socket into builds or expose ports.
- Initial minimal dependency targets from the actual report: `golang.org/x/crypto@v0.55.0`, `golang.org/x/mod@v0.40.0`, `golang.org/x/text@v0.39.0`, `google.golang.org/grpc@v1.83.1`. Review Go's required transitive changes before locking them. Keep original upstream source/cryptographic code unchanged.
- No production/private signing keys enter local containers. Ephemeral crypto fixtures and local images are diagnostic evidence, never candidate acceptance. No R2 publication or authoritative registry write is added to local tests.

## Task 1: Require the reviewed build path before changing implementation

**Files:** add `tools/p10/ReleaseVerifier.Tests/CosignSecurityBuildTests.cs`; retain existing workflow/image/signature tests.

- [x] Add a theory over `validation`, `candidate`, `audit` that reads each workflow and requires `bash "$GITHUB_WORKSPACE/eng/p10/cosign/build.sh" "$RUNNER_TEMP/p10-tools"`, rejects the old upstream binary download, and keeps `ImageBuildProfile.CosignSha256` checking before use.
- [x] Add an assertion that `ImageBuildProfile.CosignVersion` is `3.1.3-cp6.1`, not `3.1.3`, and that the selected Linux hash is not the known vulnerable upstream hash.
- [x] Run `dotnet test tools/p10/ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~CosignSecurityBuildTests`. Initial RED: four expected workflow/version failures. Adding the recipe test gives five expected failures; after the recipe/locks exist, two recipe tests pass and the four policy plus one distribution-notice check remain expected RED until actual binary pins/workflow wiring are updated.

## Task 2: Resolve and reproduce the minimal security build

**Files:** add `eng/p10/cosign/build.sh` (verified handoff), `compile.sh` (unverified deterministic compiler), `test-inputs.sh`, `go.mod`, `go.sum`, `lock-checksums.sha256`, `.gitattributes`, `README.md`, `UPSTREAM-LICENSE`, `NOTICE`, and `checksums.sha256` (observed binary pins).

- [x] In a task-only scratch source directory, run the pinned Go toolchain command `go get golang.org/x/crypto@v0.55.0 golang.org/x/mod@v0.40.0 golang.org/x/text@v0.41.0 google.golang.org/grpc@v1.83.1`, then `go mod tidy` and inspect the entire upstream-relative `go.mod`/`go.sum` diff. The initial `x/text 0.39.0` resolution was rejected: upstream crypto/mod require 0.41.0. Exactly nine modules change (crypto/mod/net/sync/sys/term/text/tools/grpc), with no cryptographic source changes. `go mod verify` passes. Copied lock bytes match: go.mod SHA-256 `10021c26ab98a414a32d052df89953d9f423e86f67051eba5b57a5876fd2c78a`; go.sum `18360c2957f1f8adfa400c8d9b9101c89a48ba10e7c5617676e9b2cafeb251af`.
- [x] Implement the fixed build sequence: refuse an existing output directory; create a new work directory; download/verify the exact Go archive; set isolated `GOPATH`, `GOMODCACHE`, `GOCACHE`, `GOWORK=off`, `GOTOOLCHAIN=local`, authenticated public sumdb and a single HTTPS proxy; obtain/check the exact module zip and source commit; copy only the reviewed lock files; `go mod download` then `go mod verify`; compile with `CGO_ENABLED=0`, `GOARCH=amd64`, `GOAMD64=v1`, `-mod=readonly`, `-trimpath`, `-buildvcs=false`, and `-buildid=`. Supply fixed version, upstream commit, `modified` tree state and source commit timestamp using upstream release-utils ldflags.
- [x] Compile `./cmd/cosign` for `GOOS=linux` and `GOOS=windows`, preserving Go dependency build information. Do not strip or conceal dependencies to affect scanners. Emit hashes and dependency metadata; do not pass signing or registry credentials into the compiler.
- [x] Produce the two platform binaries in two isolated containers, using separate output directories and no shared compiler cache. First run uses `compile.sh /out/one`; second uses `build.sh /out/two` and must match the first observed pins before handoff. Both binary hashes and both build-metadata files are identical. The frozen hashes are recorded below and in `checksums.sha256`.
- [x] Run `go test -p=1 -parallel=2 -mod=readonly -count=1 -json ./pkg/cosign/... ./cmd/cosign/cli/sign ./cmd/cosign/cli/verify` against a private writable copy of the exact source/locks with the pinned Go toolchain and fresh caches. Final result: 12 tested packages pass; 429 test/subtest passes, no test failures/skips. Four packages have no tests (`git`, `git/github`, `pivkey`, `pkcs11key`) and are not coverage claims. Native versions and dependency metadata match the fixed build.

## Task 3: Bind the new helper to the existing verifier and workflows

**Files:** `CosignBlobVerifier.cs`, `ImageBuildProfile.cs`, the three `p10-platform-*.yml` workflows, and existing signature/wiring tests.

- [x] Replace only the selected binary hashes/version with the observed reproducible outputs; share the Linux pin between the image policy and blob verifier. Keep architecture/path/symlink/hash checks, process isolation, timeouts, exact key trust and bundle checks unchanged.
- [x] Replace each upstream binary download with the fixed script invocation; continue checking the exact Linux output hash in the workflow. Compile the helper before supplying signing/publisher secrets. Keep audit/publication free of candidate-image builds and registry writes.
- [x] Re-run Task 1 tests to GREEN: 8 security-build policy tests plus 8 scanner-workflow regressions pass. Assertions bind both runtime platform pins to the two output checksums. Real `test-inputs.sh` passes 10 offline refusal/preservation checks. Add an actual `generate-key-pair` / `sign-blob` / pinned verifier round trip, including changed-byte rejection; only task-local ephemeral keys are used.
- [ ] Run the complete Windows suite with the actual patched Windows binary, including existing real cryptographic fixtures; run the Linux suite using the actual patched Linux binary. Treat old official/damaged binaries as rejected tools, not accepted fallbacks.

## Task 4: Exercise actual Linux scan and handoff boundaries

**Files:** diagnostic outputs under ignored `artifacts/p10-cosign-security/`; new automated regressions only if real failures are found.

- [x] Scan the full patched binary bytes and an actual runtime-only diagnostic image with all severities. HIGH=0 and CRITICAL=0 for both. Preserve every finding. Pass unmodified native local-archive SPDX/SARIF to the production parsers: both correctly reject the local archive identity, rather than accepting it as hosted GHCR evidence. The earlier real GHCR report-format probe remains valid; formal new-image acceptance still requires the protected hosted run.
- [x] In the actual runtime image, load the verifier from `/app`, run seven maintained crypto tests with `/opt/cp6/cosign`, and require UID 1654, read-only `/app` and `/opt/cp6`, dropped capabilities, no-new-privileges, no network and tmpfs-only ephemeral keys. All seven pass, including native signing, exact bytes, wrong key, changed payload, untrusted tool and DSSE checks; local report identity rejection also passes. No protected key or acceptance override is used.

## Task 5: Review, deliver and resume actual validation

**Files:** P10 reference/HOWTO, this plan/previous preflight record, `PROJECT_STATE.md`, `05-Completed.md`, `06-Todo.md`, `CHANGELOG-AI.md`.

- [x] Record owner authorization, actual reproducibility/scan/crypto evidence and the explicit non-upstream binary identity; update all four ledgers, reference/HOWTO and preflight without rewriting old failures or claiming production acceptance. The remaining local Linux clock blocker is explicit below.
- [ ] Run full tests, format, actionlint and complete branch diff review, then scoped commits and a normal PR. Require all PR checks, normal merge, merged smoke and exact-main checks; preserve unrelated changes and all remote histories.
- [ ] Dispatch one new validation against the verified current main, ask owner for the actual Environment approval, and inspect its complete outcome. Continue publication/audit only after successful validation and the owner's candidate-tag choice. No self-approval or production deployment.

## Actual local reproducibility and scan evidence

Both clean-room builds use the same fixed compiler recipe, different containers and fresh module/compiler caches. Output SHA-256:

| Output | Both builds |
| --- | --- |
| Linux amd64 | `a2bcc99765d97f1b7db0cf22afc0d2dd523e94c250900e9d8e4710b2a4a35740` |
| Windows amd64 | `06b2ce427089b842c7bf64fb2c22c8173f15b2f49244cf9a152fe9d95ed28d0b` |

Native version output identifies `v3.1.3-cp6.1`, upstream commit `11926fa5bbbbde47e88fc006b625a17769b743b2`, modified tree, Go 1.26.8 and the correct platform. Build metadata is retained, not stripped. The second policy entry point verifies both hashes before producing its handoff, including license/notice files.

Diagnostic outputs are retained under ignored `artifacts/p10-cosign-security/`. The runtime-only image is local `cp6-p10-cosign-diagnostic:20260908`, manifest `sha256:e4b5aa0c9741edb107b923c0f4251279aa2bafc8eddc29f13eb9e9ef0bde129e`, config `sha256:65168ac74f05aa6efe671630f4dce4adfbaff4289343f6e7ee22888938fbc196`. It uses the unchanged pinned Dockerfile/base, actual published verifier and patched Linux cosign, includes license/notice, and is labelled `diagnostic-uncommitted` / `deployable=false`. It was not pushed or represented as a hosted source build.

Exact official Linux scanner archives are checked against the workflow pins before execution. Commands inside the isolated scanner container:

```text
trivy rootfs --scanners vuln --severity UNKNOWN,LOW,MEDIUM,HIGH,CRITICAL --exit-code 0 --format json --list-all-pkgs --no-progress --cache-dir /cache --output /reports/linux-binary-native.trivy.json /scan
syft docker-archive:/reports/runtime-image.tar -o spdx-json=/reports/runtime-native.spdx.json
trivy image --input /reports/runtime-image.tar --scanners vuln --severity UNKNOWN,LOW,MEDIUM,HIGH,CRITICAL --exit-code 0 --format sarif --no-progress --cache-dir /cache --output /reports/runtime-native.sarif.json
trivy image --input /reports/runtime-image.tar --scanners vuln --severity UNKNOWN,LOW,MEDIUM,HIGH,CRITICAL --exit-code 0 --format json --list-all-pkgs --no-progress --cache-dir /cache --output /reports/runtime-native.trivy.json
```

The standalone binary scan recognizes one Go executable / 253 packages, including stdlib 1.26.8 and the reviewed module versions. All original HIGH/CRITICAL findings are absent; three UNKNOWN remain: `CVE-2026-56855`, `CVE-2026-78662`, `GO-2026-5932`. UNKNOWN is not a clean bill of health, and none of these findings is suppressed. Report SHA-256: `9631c6f525b8585bb63c781016d925302f3ceb72953e0f602da7e54362df486e`.

The complete image scan covers 9 Ubuntu packages, the app's .NET dependency file, the .NET 8.0.30 runtime dependency file and 253 Go packages. Native SPDX contains 277 packages including the image root and actual `CP6.Platform.Release 0.10.1`. Complete SARIF: UNKNOWN 3, LOW 7, MEDIUM 5, HIGH 0, CRITICAL 0. DB update time remains `2026-09-08T07:08:01.235696926Z`. Raw SPDX SHA-256 `54c61cea6c0b55cf6330e125c5cf685fd5dbd8ce6d9c35efdb83e35c9152a58a`; raw SARIF `b02aa88eadd5bd283a55927e895f868923d60ed35546765e9238eb56379d46cc`.

The actual-image probe loads verifier DLL SHA-256 `4b8a42c498d5e71417a6988d4f7e0d8e5b0182c1e00a895203ff14d854ee64d2` from `/app`, invokes seven maintained tests from an immutable test-output snapshot (`b8f354bc9135858ecc4cc026a51b90dd9f0df06f9a74d97cb5550405742e97ff`), and feeds the above unmodified reports to existing parsers. Expected local rejections are `spdx-document-binding` and `sarif-image-binding`; no report identity is rewritten. Reproducer source is retained at `runtime-probe/Program.cs` (SHA-256 `500cc327615c36bc7bf62457423545f379e208a7f661f6e3a61ae06c6e7949c0`) beneath the diagnostic output directory.

## Test-harness observations (not candidate evidence)

- An initial Windows `trivy fs` probe recognized zero language files. It is explicitly excluded as vulnerability evidence; the Linux native rootfs/image scans above establish actual coverage.
- Docker Desktop retained an old view of a live, rebuilt test-output directory. The runtime probe was repeated against a new immutable snapshot; that snapshot succeeds. Never rebuild binaries while a mounted test run is using them.
- The first SDK-suite optimization copied cosign into Docker's default `noexec` `/tmp`; actual execution returned permission denied. That diagnostic run was stopped, not counted as a pass. Keep production `/tmp` non-executable and `/opt/cp6/cosign` unchanged; the SDK-only harness uses a separate executable `/tool` tmpfs for the verified binary.
- The broad upstream unit suite initially passed core cosign, sign and verify but failed CUE/Rego tests that create `tmp-policy` files in their package directories on a read-only source mount. The final run uses an internal writable copy of the same pinned source/locks; it does not patch or skip those tests. Packages without tests are reported separately, not counted as test coverage.
- Final Windows full suite: **1644/1644, zero failures/skips**, with the second verified binary and actual formal-package/feed/signature inputs; format passes. Final upstream results and the still-failing Linux full gate are recorded below; there is no merge authorization while that required gate remains unresolved.

Final upstream JSONL: 468105 bytes, SHA-256 `867511b519d21f4e99a1b3b74db7cfbf598375014f39764619c60426db38c639`; the corrected writable-copy run exits 0 with the results above. The initial read-only-source failure remains separately retained and is not counted as a successful run.

## Remaining local Linux clock blocker

The corrected SDK-only full-suite harness uses the pinned .NET 8.0.424 Linux amd64 image (`mcr.microsoft.com/dotnet/sdk@sha256:237133a0ea20cffcfaa92588e1c8a56d58fe99f44da72dcff35aeb017119abcf`), an immutable test-output snapshot, actual formal-package/feed inputs, read-only source, non-executable `/tmp`, and a separate executable `/tool` tmpfs holding the pinned cosign. It finishes **1589 passes / 55 failures / 0 skips**. Every failure is in `FormalVerificationEvidenceTests`, sharing a failed actual package collection at `FormalVerificationEvidence.Read` line 59 (`s06-packages-time`), before its individual mutation checks. Crypto checks pass. The required full suite is not green and the branch must not merge yet.

A separate diagnostic probe in the actual runtime image observes wall clock against a monotonic stopwatch, without changing either clock or policy. It captures `2026-09-08T15:27:44.4702065Z` going backwards to `15:27:28.1445678Z` (about 16.426 seconds), then forwards by about 14.091 seconds. The feed/time probe itself fails and is not acceptance evidence. An isolated, network-disabled, single-CPU clock-only run independently sees a roughly 16.275-second forward discontinuity. A later four-second native `date` sample sees none; the problem is intermittent, not a claim that every clock read is wrong. Docker reports WSL2 kernel `6.6.87.2-microsoft-standard-WSL2`.

These observations are consistent with the local time-order failure; no time tolerance, fake clock, skipped test or acceptance override is added. At this pre-restart checkpoint, Docker Desktop restart was proposed as a troubleshooting step, not a proven cure. It affects seven running business containers (`cp6-web`, `cp6-api`, `cp6-cloudflared`, `cp6-db`, `cp6-mq`, `cp6-redis`, `cp6-kafka`, all `unless-stopped`), so the agent requested separate owner approval. No restart, global WSL shutdown, system-time change, PR/merge or new validation dispatch had been performed at that checkpoint. The subsequent authorized action and actual outcome follow.

## Authorized Docker-only restart and still-failing revalidation

The owner separately approved restarting Docker Desktop. One `docker desktop restart --detach` was issued at approximately `2026-09-08T15:35Z`; Desktop returned to `running`, and all seven business containers recovered. DB/MQ/Redis/Kafka report healthy. Read-only HTTP probes of the existing Web endpoint and API `/health/live` and `/health/ready` all return 200. No global WSL shutdown, host clock adjustment, clocksource change or WSL configuration edit was performed.

The first isolated, network-disabled, single-CPU ten-second .NET clock-only sample reported zero discontinuities. That short observation did not establish a stable environment: the same frozen SDK test inputs were then run in a new container, without rebuilding or changing any production/test code, and completed **1643 passes / 1 failure / 0 skips**. The failure is `FormalPackageSourceTests.Actual_GitHub_Packages_download_passes_independent_hash_author_and_timestamp_checks`, for `CP6.Platform.Messaging`. Its actual times are:

- Test start: `2026-09-08T15:42:39.0718617Z`.
- Package retrieval: `2026-09-08T15:42:29.8588917Z`.
- Test end: `2026-09-08T15:42:29.8770831Z`.

The end is approximately 9.195 seconds before the start. The required test correctly fails; fewer failures do not mean the clock was repaired. Crypto tests and the previous shared formal-evidence checks pass in this run, but the full suite remains red.

All TRX files remain separate under `artifacts/p10-cosign-security/test-results/`:

| Report | Actual result | SHA-256 |
| --- | --- | --- |
| `windows-security-build-final.trx` | 1644 passed / 0 failed / 0 skipped | `d542387dbc93e905dfff0c791f1956a66f2b3f7bc9143c65bc790f3cc39f1c8d` |
| `linux-security-build-final.trx` | 1589 passed / 55 failed / 0 skipped | `ef6885ca0571bdd98d9a17cae689ecc201c14927ca7296a33b81db0034e90451` |
| `linux-security-build-after-restart.trx` | 1643 passed / 1 failed / 0 skipped | `13f54f8697eb9149a7b88b94db8e964621bf1efac4d404b27775f7f41c4ea47f` |

A subsequent independent Linux native probe compares `date +%s%N` with `/proc/uptime` for 300 samples, 100 ms apart, in the same pinned SDK image with one CPU, no network, read-only filesystem and dropped capabilities. It observes two wall/monotonic discontinuities: about **-13027 ms** (`1788882255071261099` to `1788882242143850152` nanoseconds since epoch) and **+14108 ms** (`1788882242551208204` to `1788882256759609009`). The failure therefore is not confined to the .NET clock API. A concurrent Windows UTC/Stopwatch probe of 300 samples reports zero discontinuities, ending at `2026-09-08T15:44:36.0419834Z`; this is a bounded observation, not a universal guarantee about the host.

Read-only environment checks show WSL `2.6.1.0`, kernel `6.6.87.2-1`, and both `Ubuntu` and `docker-desktop` running. Linux currently selects `tsc`; available sources also include `hyperv_clocksource_tsc_page`, `hyperv_clocksource_msr` and `acpi_pm`. Filtered kernel logs show the boot transition from `tsc-early` to `tsc`; the user's `.wslconfig` is absent. These are observations, not proof of a specific kernel defect or permission to change the source.

The Docker-only restart has not resolved the required gate. A full WSL2 restart is a possible next environment diagnostic, not a promised cure. Microsoft's [WSL shutdown documentation](https://learn.microsoft.com/en-us/windows/wsl/basic-commands#shutdown) states that `wsl --shutdown` terminates all running distributions and the WSL2 utility VM. It would affect the currently running Ubuntu as well as Docker and its business containers, so it requires a new explicit owner decision beyond the Docker-only approval. No PR, merge, protected validation dispatch, candidate publication or production deployment is performed while this required gate remains unresolved.
