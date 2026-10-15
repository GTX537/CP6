# WP02-12 independent integration review

**Decision: PASS WITH BOUNDARIES. No confirmed material blocker; findings: 0.**

No confirmed material correctness/security/contract/provenance blocker was found in the bounded library and named native local fixture scope. This is not production or business acceptance. Normal coordinator integration gates remain required.

Reviewed `b28db763f70ab6f164cb7c87d6ce49a6d97267f1` → `b35d0646767fc7e876e92158c582573500e0a637` in `D:\CP6.Platform-worktrees\wp02-12-20261014`. The complete 186-file task diff is within its allocated scope. Worktree remained clean. Source and ledger were read-only; only external review reports were written.

## Exact review inputs

- `D:\CP6\AGENTS.md` — SHA-256 `69cf203897e0b7a1b6eeb37895b74918db1162b14544e103ddf9361c87bbc299`.
- `D:\CP6\docs\development\dispatch\20261014-wave7\WP02-12.md` — SHA-256 `f98d30d1d006b41a87e193a3a17873f579b92b4747f6d28355879c9ed5f8caa3`.
- `D:\CP6\docs\development\dispatch\20261014-wave7\dispatch.json` — SHA-256 `791f019dc0fb20e698b60a8e53d968512b5c278564bec4c762b691f55b388d5f`.
- `D:\CP6\docs\development\task-plans\WP02.md` — SHA-256 `c2a475589063bf7f28ebfe3860e0dd136955d7399359d5c13f0269d518de6239`.

No relevant Platform AGENTS.md was found at the worktree root, parent worktree directory, D:/CP6.Platform, or D:/; D:/CP6/AGENTS.md and the exact wave7 dispatch govern this review.

## Security and correctness review

### Producer and trusted context

Registry invokes the fixed Host authority before selecting the untrusted schema tuple. Producer/topic/raw hash/native reference/media and tenant identity gates precede capabilities. Dedicated SASL publisher ACL, app token, and independent topic/key/partition/offset/body observation provide a bounded fixture entitlement boundary. Topic/key/offset are native evidence; aggregate/version/id are explicitly body claims. Host deployment admin and private observer files remain trusted.

Source proof: `src/CP6.Platform.Messaging/TransportV1/TransportContractsV1.cs:7`, `src/CP6.Platform.Messaging/TransportV1/TransportRegistryV1.cs:97`, `tests/CP6.Platform.WP0212.Fixture/FixtureTransport.cs:60`, `tests/CP6.Platform.WP0212.Fixture/Program.cs:19`, `tests/wp02-12-runtime/runtime.py:183`.

### Route, version, C02 and compatibility

Only the generic opt-in profile and isolated pinned bundle are accepted. Exact five-tuple/hash registration and duplicate rejection, independent validators, and same-byte schema hash/parse are enforced. External schema references are rejected before schema build. Native C02 adoption is closed; native colon keys are never substituted with generic slash keys. Legacy default loaders retain their behavior.

Source proof: `src/CP6.Platform.Messaging/TransportV1/TransportRouteV1.cs:5`, `src/CP6.Platform.Messaging/TransportV1/TransportRegistryV1.cs:63`, `src/CP6.Platform.Messaging/Cp6ContractBundle.cs:49`, `src/CP6.Platform.Messaging/Cp6ContractBundle.cs:116`.

### Original bytes, key and safe diagnostics

The effective byte limit is min(profile, Host, broker), checked before authority; library owns and exposes copies of bytes. The canonical key and envelope identity must match independent facts before qualification. Diagnostics are fixed safe codes. Existing strict JSON/root trace handling is reused. Runtime console observer stores broker output bytes/key in Base64 and hashes original body; its delimiter/one-partition/100-record limitations are explicit.

Source proof: `src/CP6.Platform.Messaging/TransportV1/TransportRegistryV1.cs:84`, `src/CP6.Platform.Messaging/TransportV1/TransportRegistryV1.cs:115`, `src/CP6.Platform.Messaging/TransportV1/TransportRegistryV1.cs:17`, `tests/wp02-12-runtime/runtime.py:197`.

### Publish confirmation and Unknown

Instance/purpose-bound capability and valid contract are required. Exceptions, cancellation, timeout and unknown decisions remain Unknown. The bridge defensively owns bytes and does not create owner authority. Existing corrected OutboxV2 supplies committed send intent and original mutation recovery. Confirmed means transport accepted; native ACK-suppression case is explicitly application injection after actual Dapr success.

Source proof: `src/CP6.Platform.Messaging/TransportV1/TransportRegistryV1.cs:132`, `src/CP6.Platform.EntityFramework/TransportV1/TransportOutboxPublisherV1.cs:14`, `tests/CP6.Platform.WP0212.Fixture/FixtureTransport.cs:100`.

### Durable quarantine and read-only recovery

Drop requires persist completion plus independent committed readback of exact profile/route/policy/id/hash/native reference/facts/body. Fixture uses separate PostgreSQL connections/transactions and read-only recovery. NativeReference is included in the durable key, preserving repeated invalid bytes at distinct offsets without overwriting. Wrong-key messages stay unqualified and preserve their actual key for quarantine only. Interfaces and in-memory fakes alone do not prove durability.

Source proof: `src/CP6.Platform.Messaging/TransportV1/TransportRegistryV1.cs:148`, `tests/CP6.Platform.WP0212.Fixture/FixtureDatabase.cs:74`, `tests/CP6.Platform.WP0212.Fixture/FixtureDatabase.cs:84`, `tests/CP6.Platform.WP0212.Fixture/FixtureDatabase.cs:100`.

### Runtime provenance and business separation

Final 275 source/input and 131 artifact hashes match retained files, as do 13 TRX records and 310 private native log streams. Final fixture process record binds the final DLL hash to recorded process startup. All five owned containers remain exited; assigned loopback ports have no listeners. Fixture SUCCESS commits ReceiveObservation only. Forty original business AC remain NOT_RUN; C02 and Main/CRM adoption are not inferred.

Source proof: `tests/CP6.Platform.WP0212.Fixture/Program.cs:48`, `docs/development/evidence/WP02-12/REPORT.md:19`, `docs/development/evidence/WP02-12/runtime/reuse-boundaries.json:1`.

## Reused validation and actual independent checks

No tests or builds were executed during this review. There was no concrete failing hypothesis that justified rerunning already successful inputs.

| Evidence | Reused result | Exact original artifact | Limit |
|---|---:|---|---|
| Library complete snapshot | 44/44 | `tests/library/20261015T003737829Z/green-native-quarantine.trx` | Original 44-test snapshot before final NativeReference/wrong-key corrections; reuse only unaffected assertions. |
| Targeted final correction regression | 27/27 | `tests/library/20261015T004920812Z/green-native-reference-key-quarantine.trx` | Final 11 source hashes match. Includes three added tests and overlaps previous 44; totals must not be added. |
| Affected legacy contracts | 101/101 | `tests/library/20261015T003413218Z/legacy-transport-regression.trx` | CloudEvent/DaprKafka/NativeProfile/TraceContext tests only; subsequent changes confined to TransportV1. |
| Architecture | 98/98 | `tests/architecture/wp02-12-architecture.trx` | One task-level architecture run, not a full release gate. |
| Native initial runtime | 22/22 | `runtime/acceptance.json` | Owned Kafka/Dapr/PostgreSQL fixture; original wrong-key RETRY assertion superseded. Native provider ACK-loss not proven. |
| Native targeted runtime | 7/7 | `runtime/targeted-review-regression.json` | Two unknown-schema delivery offsets prove committed quarantine and observed DROP. Wrong-key case proves durable row only, no independent exported DROP observation. |

Artifact paths in the table are relative to `docs/development/evidence/WP02-12/`. TRX hashes/counters were independently recalculated; failure runs remain present, including the red native-reference/key regressions.

Independent read-only validation completed: 275/275 source/input hashes; 131/131 retained artifact hashes; 11/11 final targeted-source hashes; all 13 TRX hashes/counters; 116 exported native command streams; 310/310 retained private native log streams; 8/8 final original Kafka body hashes. No mismatch, missing file, JSON/Python syntax error, local Markdown link error, or scope violation was found. `git -c core.longpaths=true diff --check <base> <head>` exited 0. Full check data is in `checks-WP02-12.json` and the JSON review.

Current read-only Docker inspection found all five owned containers exited. No listener exists on 19092, 13500, 15001, 18012, 18013, or 55430. Final retained process record binds PID 27084/start 2026-10-15T00:53:50.9985975Z to fixture DLL SHA-256 `075b9360818b5279e892602bb633e72df91d2a2e285999a344c8c54131629820`. No service was started and no database session was opened.

## Findings

No actionable P0/P1/P2 finding was established. The prior distinct-offset quarantine issue is corrected in source and has both red/green and real targeted evidence. Authenticated wrong-key handling is receive-only quarantine; it does not gain valid publish or business capability.

## Acceptance limits

- No new tests, builds, database connections, containers, services, remote writes, pushes, merges, Actions, or source/ledger edits were performed by this review.
- ITransportAuthorityV1 and ITransportQuarantineStoreV1 are trusted Host contracts. A malicious or incorrectly composed implementation can fabricate facts/readback; DTOs and callback completion cannot independently prove trust.
- SASL/PLAIN and local API/app tokens establish the named local fixture boundary only. Deployment admin, controller network peers, and local owner/files are trusted. No production TLS/mTLS, hostile local administrator, or production lifecycle proof is claimed.
- Receive authority reads known structured fixture bodies for business identity claims. Unit malformed-raw quarantine behavior is not a native arbitrary-poison corpus guarantee. Wrong-topic receiver handling was not a positive business route; producer ACL denial was the actual negative proof.
- Independent observer is deliberately bounded to the controlled single-partition corpus with reserved delimiters; it is not a production Kafka adapter.
- Unknown ACK is application suppression after a genuine successful send; broker/sidecar-unavailable cases use controlled owned service stops. Neither is provider-native ACK packet-loss proof.
- Receiver SUCCESS proves only PostgreSQL ReceiveObservation commit. Real Owner Inbox/business atomicity, same-business-key dedupe, native S6 precedence, Main/CRM/C02 adoption, production retention/recovery/SLO, release trust and deployment remain NOT_RUN.
- Successful historical validation is reused with original scope; SHA change was not treated as a rerun. Initial fixture DLL historical hash was C044BCE941636C027E64B45AAEDD771D26B20A24BB5104C69A71B6E2E47A0485; final targeted DLL was 075b9360818b5279e892602bb633e72df91d2a2e285999a344c8c54131629820.

**Original business acceptance stays 40/40 NOT_RUN and 0 PASS.** This review supports coordinator consideration of this bounded task only. It does not authorize automatic downstream adoption or remove normal workflow/remote-main integration checks.
