# WP02-27 independent candidate review

Review target **0057261a9b3506484afcc981f513381b9268da81** against Platform main **9259a45d39bae7e98ca804a74778a588c1ad4619**. Source freeze: **77fbf0311ffa4d4b1220861fd7624c7a1d20c405**. Handoff messages read as exact Git objects at **d5104d2590e514983e2e770c8e9b58b6c6949b81**. Review only; this report does not mark the task DONE.

**Verdict: blocked pending one P2 contract correction and successful mandatory Architecture gate.** The proposed two asset-array updates are justified and preserve the policy; they do not conceal a product bug. A separate schema/API edge was found during full candidate review.

## P2: valid single-character archive path rejected by schema

`contracts/release/capabilities/v1/capability-matrix.v1.schema.json:666` ends the path pattern with `.+[^/]$`, requiring at least two characters. `Cp6CapabilityValidator.cs:95` permits any nonempty relative canonical archive path, including `x`. A valid schema ZIP entry need not have an extension or two-character name. Thus a matrix accepted by the public semantic API can be rejected by the packed JSON Schema.

A read-only in-memory probe loaded the existing candidate assemblies, created the existing test Fixture through reflection, replaced its path with `x`, and called `ValidateShape`: accepted 4519 bytes. The declared regex returned false. The first wrapper compile failed on the .NET8/.NET10 reference-assumption warning; after suppressing only CS1701, the focused observation succeeded. This was not a test suite, package verifier or fresh signature run.

Minimal correction: change the final `.+[^/]$` to `.*[^/]$`; retain the absolute-path, backslash, dot-segment and empty-segment guards. Add actual schema/API regression for a single-character valid entry and ordinary/invalid paths. Do not fix by narrowing the semantic API's legal inputs. Run the focused test RED before applying the regex correction, then GREEN. Because schema raw bytes change, record a new source freeze and unique seven-package local build/inspection; the old local pack remains valid evidence only for the old bytes.

## Mandatory gate and precise architecture proposal

The retained `release-architecture-1` TRX records **98 total, 96 passed, 2 failed, 0 skipped**. Both failures compare exact assets:

- `Release_package_is_independent_and_owns_only_release_contract_assets`, source line 183.
- `P08_PackageEvidenceAndProductionSafetyGuards_AreEncoded`, source line 418.

The dispatch already authorizes `contracts/release/capabilities/v1/` and Release csproj packaging registration. Proposal `docs/development/evidence/WP02-27/architecture-window-proposal.patch` (SHA256 `4487024d9d5382464798a554ed7b48b7d1eb7cfe68d116193205bca04d863622`, Git blob `40083361277a02dc2b8009db462b63c6490f407c`) passes `git apply --check`. It adds only the matching capability directory to both arrays and keeps the old release/v1 entry. Dependency independence, exclusive Release ownership, other package restrictions and production safety guards remain unchanged. Approve a coordinator-controlled window for exactly these two arrays, then execute the actual full Architecture gate. The candidate remains unmergeable until it passes.

## Complete diff and evidence assessment

The entire baseline-to-candidate diff contains **200 files**; all original paths fall within the dispatch window. Production changes are isolated CapabilitiesV1 contracts/validator and a three-line csproj asset registration. No existing formal v1 validator, CLI, trust root, workflow, dependency, version baseline, database or shared state changed. Post-freeze changes under src/contracts/eng/tests are empty. Full changed-path inventory and binary-diff digest are in the companion JSON.

All **174 retained evidence files** and **29 local-only archives** match inventory size and SHA256, with no missing bytes or mismatches. Final Release/formal/local/negative run artifact references match retained or actual local files. All 15 pinned baseline source anchors, the fixed Main admission object and original design bytes were independently rehashed.

- Release `release-publication-fix-2`: **113/113**, zero failures/skips, SDK8.0.424. Capability `green-publication-evidence`: **58/58**. Current code/test/tool source matches those snapshots; only the two schema files later normalized CRLF to LF. Reuse is valid for semantics, not a claim of new execution at candidate SHA.
- Formal `formal-current-2`: retained evidence contains seven actual Current CLI invocations, each exit0, no timeout and checked identities at **2026-10-13T12:46:51.582Z**. Original seven cached archives were rehashed and their nuspec source/version/repository read. Policy SHA256 `da359e3a8e9be2220541c53613d2da277cb2bb9a22a8770df30c808a033b953f`; publication SHA256 `17fd39f5724ba118771eb95aeb18d78045ea2e576431957ab7c24b8d13719181`; original 0.10.2 source `fbcd21528078a04e5b53c42c5fdfebe6ffa9655f`. Tool binary matches the preceding Release build evidence. The unmodified verifier still requests Online revocation and strict RFC3161. This review did not repeat signature/network verification or authenticate remote feed/workflow APIs.
- Local `local-pack-1`: seven packages at `0.10.3-wp02.27.g77fbf0311ffa4d4b1220861fd7624c7a1d20c405.20261013124916`, with common source/version and independently rehashed archives. Current frozen source snapshot is exact. Both new packed schemas are byte-identical to current source. State stays **NOT_PUBLISHED / NOT_FORMALLY_TRUSTED**.
- Trust negatives preserve the initial tampered-case FAIL (expected exit2, actual exit1) and the justified focused retry. The malformed timestamp and altered ZIP return generic internal-error/reject1; they prove rejection rather than a particular crypto-error branch. Other negative results and HistoricalAudit-only success are accurately bounded.

Default diff whitespace checking reports retained CRLF log/TRX lines only; `core.whitespace=cr-at-eol` passes. Preserve original evidence bytes.

## Trust and compatibility conclusion

Independent approval pins matrix/source and, for formal evaluation, current policy plus original publication evidence. Actual seven archive identities and source/packaged schema bytes are checked. No unknown request can match an approved exact compatibility row accidentally; compatibility Allowed is separate from trust and adoption. Existing formal 0.10.0/.1/.2 cannot claim the new capabilities. The callback is explicitly a trusted host integration boundary, with no production adapter supplied. Real current/formal and local matrices contain no measured consumer rows, so their compatibility and all six adoption stages remain Unknown.

Native profile schemas are not packaged, API inspection proves names only, and Main/CRM adoption, host/driver/schema/transaction/fence, new formal publication and business acceptance remain unverified. **WP02-02 remains BLOCKED.** No Actions, deployment, database operation, remote-main inclusion or DONE is claimed. New fixes must preserve the original evidence and only repeat checks whose relevant inputs changed.
