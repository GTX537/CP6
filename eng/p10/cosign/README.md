# P10 cosign security derivative

This directory defines `3.1.3-cp6.2`, an owner-authorized security rebuild of upstream cosign `v3.1.3`, not an official Sigstore binary. Source code for signing/verification is unchanged. Existing P10 key trust, P-256 use, digest verification and HIGH/CRITICAL gates are unchanged. The `cp6.2` follow-up changes only gRPC from `1.83.1` to `1.83.2` relative to `cp6.1`, addressing the actual hosted `CVE-2026-84445` finding; historical `cp6.1` evidence remains in the linked plan.

The initial native scan and immutable report identities are recorded in [the preflight](../../../docs/superpowers/plans/2026-09-08-p10-native-scan-preflight.md). The implementation and actual test status are recorded in [the security-build plan](../../../docs/superpowers/plans/2026-09-08-p10-cosign-security-build.md); do not infer candidate acceptance from build success.

## Reviewed inputs

| Input | Fixed identity |
| --- | --- |
| Upstream source | `11926fa5bbbbde47e88fc006b625a17769b743b2`, signed tag `v3.1.3` |
| Source module | `github.com/sigstore/cosign/v3@v3.1.3`, sumdb `h1:001JQRI/PJ/5T+g/kJ1KTvKFbb322+fomc+pHDZ/6sg=` |
| Source ZIP SHA-256 | `fbf05afe62db35ca00129ff65fab6f7ad8b7851ae1cf7dbb4b1789b6ccd65db2` |
| Go Linux SDK | `1.26.8`, archive SHA-256 `d0f743b33e8d8945e6b1f432edd15785c70507121d6e2a723b21285eddf8b57b` |
| JSON reader | jq `1.8.2`, official Linux asset SHA-256 `b1c22172dd303f3be49e935aa56aa48a8b7a46e0bc838b4997d3bb451495870f` |
| Dependency and compiler recipe | Exact files named in `lock-checksums.sha256` |

The complete Go lock files differ from upstream only for nine modules: crypto `0.55.0`, mod `0.40.0`, net `0.58.0`, sync `0.22.0`, sys `0.47.0`, term `0.45.0`, text `0.41.0`, tools `0.49.0`, and grpc `1.83.2`. The first eight paths are under `golang.org/x`; grpc is `google.golang.org/grpc`. `x/text 0.39.0` alone cannot satisfy `x/crypto 0.55.0`; the reviewed dependency graph requires `0.41.0`. No mutable dependency update command runs in the maintained build.

Current output pins are Linux SHA-256 `2d46e35a21ecbe8219ef5e1dd64b302c3208ddd0e21cf1353b21bd3ac9e510f2` and Windows SHA-256 `14fbf7035b47dcc09a7e3bca8cc7a27b487d20009b3226d821876c2917b0765d`. Two independent clean-room builds reproduced both binaries and dependency metadata; actual evidence is recorded in the linked plan. These pins are frozen in `checksums.sha256`; neither the upstream nor the previous `cp6.1` binary hashes are accepted fallbacks.

## Build and verification boundary

On Linux amd64 with Bash, curl, tar, coreutils and HTTPS access to the official Go/proxy/sumdb/GitHub asset endpoints:

```bash
bash eng/p10/cosign/build.sh /absolute/new/output-directory
```

`build.sh` is the policy entry point. It refuses an existing output path, checks recipe/lock hashes, invokes the compiler with an empty inherited environment, then requires the frozen Linux and Windows binary hashes before copying the handoff. It never signs or uploads anything. Failed scratch data is retained inside the process's temporary environment for diagnosis; no recursive cleanup of caller directories is performed.

`compile.sh` only produces **unverified** compiler outputs. It is used to establish the two independent build hashes; it is not a substitute for `build.sh`. Every invocation uses fresh module/compiler caches, verifies the exact Go SDK, source ZIP, sumdb checksum and source commit, validates all modules, and refuses rewritten lock files. `-trimpath`, `-buildvcs=false`, `-buildid=` and fixed version metadata remove machine/time-dependent fields without stripping dependency information.

Both platforms use the upstream default static file-key configuration (`CGO_ENABLED=0`), with no optional PIV/PKCS11 hardware tags. The fixed source timestamp in version metadata is explicitly not a claim about when CP6 compiled it. The output includes the upstream license and a modification notice; include them with redistributed binaries.

The validation/publication/audit workflows each compile this helper and demand the same output digest. This does not create a second candidate-image build: only approved hosted validation builds the authoritative image. Local clean-room tests, temporary images and ephemeral keys remain diagnostic; they cannot supply an accepted candidate handoff.

Run `bash eng/p10/cosign/test-inputs.sh` without network for the ten input-boundary checks. The .NET suite also tests native key generation/signing with ephemeral keys, exact-byte verification and tampering rejection. Native diagnostic image scanning of `cp6.2` on September 9 retained UNKNOWN 3 / LOW 7 / MEDIUM 5 / HIGH 0 / CRITICAL 0, using the same DB update timestamp as failed hosted run `34303646636`; see the linked plan for report hashes and coverage. This is not a claim of zero vulnerabilities or formal candidate acceptance.
