# BUG #161 task-level source review

Verdict: **NoSubstantiveBlockers**. No P0, P1 or P2 finding in the reviewed source changes. This is a scoped technical review, not a production acceptance or completed-delivery claim.

## Applicability

- Worktree: `D:\CP6\tmp\worktrees\bug-161-cad-provider-recovery`.
- Confirmed base: `4a654320c7c41cbdf6ed5bf7bd4fcba459f720f0`.
- Reviewed the full tracked source diff against that base and read the untracked recovery partial explicitly. Read supporting transaction classifier, resource locks, domain entities, relevant `SpaceContext` guards/query filters/indexes, fixture/provider selection, controller and existing CAD tests.
- Applied the requesting-code-review skill as one task-level review, respecting the repository cadence. Per assignment, the reviewer ran no .NET, build, API or native database operation and edited only this review file. Existing run artifacts were inspected read-only.
- Exact reviewed source bytes, rehashed after review and matching both native GREEN source-input manifests:

| Repository-relative source | SHA-256 |
| --- | --- |
| `CP6.Space.Infrastructure/SpaceCadProviderCapabilityService.cs` | `7B0AB71E6829471DD89806C0C5A9A5FE4418CC82EB68AA2818AF9E8AD682F837` |
| `CP6.Space.IntegrationTests/SpaceCadProviderSqlServerTests.cs` | `DE6434E142F24E4355F958E6F4214DF48C98E832D2A872F0486EE5F33EBB6A93` |
| `CP6.Space.IntegrationTests/SpaceCadProviderSqlServerTests.RecoveryTests.cs` | `61D2F8222E3A59C4D68EB1E25F8261C523E2274D18E0F95FADDCE4796711278D` |

## Technical assessment

The relational entry guard rejects caller-owned explicit, ambient and explicitly enlisted transactions before starting service work. It explicitly detects and rejects pending tracked changes before any service save or recovery clear. The pending-change and explicit/ambient guard tests confirm that the caller can continue owning its state and transaction; the additional explicitly enlisted branch is covered by source inspection only.

Each attempt owns a Serializable transaction. Its catch performs rollback with `CancellationToken.None`; the `await using` method scope finishes asynchronous disposal before the outer method receives the attempt failure. The outer recovery path checks that no current transaction remains before clearing the tracker. This removes both accepted tracked writes from earlier saves and the pending idempotency insert, allowing a new transaction and fresh configuration/replay reads. The classifier follows nested exceptions, including the EF wrapper observed in the original native failure, and only permits deadlock or serialization retry. Unknown unique failures propagate without retry. Cancellation is checked before each attempt and after cleanup of a retryable failure; the third failed attempt is rethrown, bounding attempts at three.

The retry re-enters the complete protocol, including access checks, normalized request hashing, principal-scoped replay and the site lock. Different-key contention resolves to the existing revision-conflict 409. Same actor/key contention replays the winner rather than inserting another configuration, certification set or idempotency record. Existing tenant query filters, tenant/site lock identity, operation/site hash and principal predicates remain unchanged. Certification writes remain inserts; only the previous configuration's current flag is superseded, and existing history immutability guards remain active.

The controlled tests fail at the third/final save of a seeded replacement, after superseding the old configuration and persisting the new configuration/certifications inside the attempt. They require three saves per attempt, distinct transaction IDs, an empty current transaction and no pending tracker writes. Fresh-context checks require one retained current revision and no leaked attempt certifications/idempotency rows on failure, or exactly one additional committed revision on success. These assertions meaningfully test whole-transaction rollback rather than a failure before business writes.

## Evidence inspected

- Original matrix failure: `D:\CP6\tmp\wp6-pg-formal-matrix-claim-fix\entries\space-cad-assets-collaboration\entry-result.json` and its `private\trx\results.trx`. The 15-case entry has 14 passes and the original concurrent CAD replacement fails with nested native PostgreSQL `40001`. This historical failure remains a failure; it is not replaced by the targeted GREEN result.
- Coordinated native RED: `D:\CP6\tmp\bug161-pg-coordinated-native-red\result.json`, source-input manifest and `private\trx\results.trx`. Both named waiter cases fail, each with actual native `40001` in error/observer evidence. The RED manifest identifies the unpatched service; the expanded final recovery partial has a different hash, so the RED result is not asserted to cover all final control tests.
- Native PostgreSQL GREEN: `D:\CP6\tmp\bug161-pg-cad-exact-green\result.json`, source-input/runtime manifests and `private\trx\results.trx`: 25 total, 25 passed, zero failed/other, process exit 0. Both coordinated waiter cases pass and each includes actual `SQLSTATE=40001; Kind=SerializationFailure` observer output. This run records `BuildExecuted=true`.
- Native SQL Server GREEN: `D:\CP6\tmp\bug161-sql-cad-exact-green\result.json`, source-input/runtime manifests and `private\trx\results.trx`: 25 total, 25 passed, zero failed/other, process exit 0. This run records `BuildExecuted=false` and reuses the same source/runtime manifests as the PostgreSQL run.
- Supplemental routing regression: `D:\CP6\tmp\bug161-inmemory-routing-green\result.json` and `private\trx\results.trx`: 17 total, 17 passed, zero failed/other, process exit 0. Its explicit Scope correctly identifies InMemory despite the inherited selected-provider label; it is not native database acceptance.

Both native GREEN results report unchanged sources/runtime. Direct file hashes match the result receipts: source-input manifest `3D8A0E51E1C015F6F10830A9B75D68C34390876DAAD07965ADE160EE391BFE60`; runtime-before manifest `455ACB762F61A88D7F7DA7ED17B7558FC3EA624BFE81B3D440DB290991AFDB18`; PostgreSQL TRX `9598DD054712780D5654A509CE0F44911005B2F8EC0ACA71BCE45BBE0DA99BAB`; SQL Server TRX `56455CC70EF54EF1C9981F1534D012571375355880BB572AEB29E28B9C2B8C27`. Supplemental TRX hash: `EF7E68334B97F9004F0DB2D5F873FFC5BD6B0DD6BB19E3DC807B9AF53CAEBCC5`.

## Coverage limits and handoff

- Explicitly enlisted caller transaction preservation has no dedicated new native test. The source guard is present; do not claim separately executed enlisted coverage.
- Serialization/deadlock/unknown-error/exhaustion/cancellation controls inject exceptions while exercising actual rollback in both databases. They do not prove a real native SQL Server deadlock, native exhaustion, cancellation during a network/commit operation, or cleanup after a provider rollback/disposal failure. The coordinated PostgreSQL cases independently prove actual native `40001` recovery.
- CAD-specific tenant/site/principal scoping is checked in the unchanged source protocol; this review does not claim new dedicated runtime isolation scenarios for every combination.
- Runtime/source manifests and TRX receipts were inspected and selected hashes verified. The reviewer did not independently execute the runner, rebuild binaries or perform an exhaustive archive audit.
- Documentation/project-memory changes, final evidence packaging, complete WP6 matrix rerun, PR/remote workflow checks, commit/merge/remote-main verification and release/deployment acceptance are outside this source review and remain for the parent task. The targeted native and supplemental passes do not establish full WP6 or production acceptance.

Assessment: the hash-bound source change satisfies the scoped BUG #161 recovery requirements with the stated coverage limits. No substantive source-review fix is required before the parent completes its remaining delivery checks.
