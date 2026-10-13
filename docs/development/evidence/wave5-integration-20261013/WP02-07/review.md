# WP02-07 independent concentrated pre-merge review

**ACCEPT — approve the library-only merge; no blocking correctness or evidence findings.** Real Owner adoption and all original business acceptance remain NOT_RUN/UNPROVEN. This does not certify a package, host rollout or production use.

- Candidate: `872acec044eb09bc96660f696844c86127b508bd`
- Confirmed base / independently verified merge base: `9259a45d39bae7e98ca804a74778a588c1ad4619`
- Implementation: `ed6ee2972d16ea747e706be3dce258dc299cd1b8`
- Worktree: `D:\CP6.Platform-worktrees\wp02-07-20261013`; HEAD exact, clean; 72 added files, all within the approved window.
- Complete diff inspected: all four production files, all private test/helper files, documentation, retained logs/TRX and parsed/hash-verified evidence manifests. `git diff --check` passed. No legacy/shared model, dependency root, solution, workflow or retention edits.
- Review uses the `review` checklist under the task-specific read-only and concentrated cadence. No source/git/network/database mutation; no tests rerun.

## Source and test provenance

The two historical/final manifests were independently checked against their exact Git objects. Final capture matches all 255 working source/config/contract files, 9 resolved dependency graphs and 153 current artifacts. The earlier capture differs only by the documented unit assertion and Python launcher source changes, plus 14 unit-output DLL/PDB files replaced by the targeted rebuild. No PG/architecture artifact or dependency drift. Production source has not changed after the implementation pin.

Portable PDB document SHA-256 values independently match all 4 InboxV1 production sources, all 3 PostgreSQL C# sources and the final unit source. Git blob and working-byte hashes are recorded separately (line-ending differences are not mislabeled). Full path/hash lists and every retained run are in the sibling JSON.

| Reused evidence | Actual finish time | Passed / total | Scope |
|---|---|---:|---|
| `inbox-architecture.trx` | 2026-10-13T08:33:49.9256058-04:00 | 98 / 98 | Unchanged required architecture gate |
| `inbox-postgresql-reviewed.trx` | 2026-10-13T08:36:53.5171352-04:00 | 20 / 20 | Full task-private real PG fixture run |
| `inbox-unit-final.trx` | 2026-10-13T08:33:43.3660555-04:00 | 18 / 18 | Original unit run; 4 changed byte-copy theory cases superseded below |
| `inbox-review-regression.trx` | 2026-10-13T08:41:49.2088934-04:00 | 4 / 4 | 4 repeated byte-array mutation theory cases, final assertion source |
| `inbox-resource-reproduction.trx` | 2026-10-13T08:46:28.7706010-04:00 | 2 / 2 | 2 repeated PG cases, fresh helper-provisioned cluster |

**136 distinct cases = 18 + 20 + 98; 4 + 2 repeats add no new business AC.** All 14 TRX files match recorded hashes, counters and individual results. Expected RED runs and genuine fixture/cross-tenant failures are retained. No failure was omitted or relabeled as accepted success.

The 36 fixed input identities match their Git blobs/raw archive hashes. The acceptance map reproduces all 60 original foundation assertions exactly at `a1824fec3d4824d45e27693f2e715b311ee5f665`; every original business assertion remains NOT_RUN and host adoption UNPROVEN.

| Evidence file | SHA-256 |
|---|---|
| `source-inputs.json` | `db122c8681541d6157d2554821c4f4576d1e6d914c26894bc26b04a2a470ad55` |
| `validation-inputs.json` | `ce79f13225ab1d8196a668023661d315c7c74ad1189b9dcdfcbcedea689b8b08` |
| `validation-inputs-before-review-fix.json` | `26d0391923fe6c37ea92d88ad46eaedb80ac6313caedb76290242767bf634829` |
| `test-runs.json` | `67715dc71de2771705c021cba8d424a6d50c7d9c2d0b79498274e8991732d37c` |
| `acceptance-map.json` | `ceeb7545ba09a8fa406e52b32072771b62a4a12d7e94097fba97cabb4939bbe1` |
| `REPORT.md` | `72de7d2ac6beb2a2afdf1ffc31ced53173d88f8403ec5fba45e1c1978d16f1d1` |
| `PLAN.md` | `9fae1eab3ed9338e2b21b37ebb6a3334b0d97471752633c5c0d252118ca93e7f` |
| `REVIEW.md` | `fbd0b7a70f73af8adbc560f4efdf76f45af34f665735d69459b8890efd041bb2` |
| `prepublication-workflows.json` | `fb8840ed2ac8251c04dbd2cbbb57dc3aaff366a934906b7d8199a0645fff9349` |
| `resources/resources.json` | `2f9c4ccdb5959d5d298a7234ce14fa96e97843a38d1f1287dd2516e191b77a0f` |

## Required risk scenarios

| Scenario | Reviewed evidence and limit |
|---|---|
| Native caller transaction and current tenant | `InboxTransactionV1.cs:26-66; CallerTransactionInboxV1.cs:19-27`. Tests: `Missing_wrong_connection_ended_transaction_and_wrong_tenant_are_rejected; Read_confirmation_rejects_write_transaction_and_missing_trusted_context`. Real Npgsql handles, Serializable write / ReadCommitted READ ONLY read, backend/database/principal/xid, trusted-scope identity checked. |
| Seven ordered transaction locks | `InboxTransactionV1.cs:70-87`. Tests: `Wrong_lock_order_and_preheld_late_lock_are_not_accepted_as_proof; Real_transaction_locks_are_held_until_caller_rollback`. Actual pg_advisory_xact_lock and pg_locks proof; contender cannot acquire until caller rollback. Only fixture writer inventory is proven. |
| Atomic commit and full rollback | `CallerTransactionInboxV1.cs:56-112`. Tests: `Pending_is_invisible_until_caller_commit_and_all_nine_tables_commit_together; Explicit_rollback_or_failure_after_owner_save_leaves_no_partial_graph`. Nine fixture tables observed from separate connections; caller COMMIT/ROLLBACK; no library transaction lifecycle ownership. |
| Content admission before duplicate/old/equal | `CallerTransactionInboxV1.cs:24-36; InboxContractsV1.cs:167-185`. Tests: `Duplicate_old_equal_and_known_historical_conflict_all_pass_the_content_gate`. Authority/history read and closed seven-decision classifier before all technical early exits; known historical conflict beats duplicate/old. |
| Exact raw/data bytes and operation identity | `InboxContractsV1.cs:66-114; InboxModelV1.cs:26-45`. Tests: `Bytes_and_digest_domains_remain_exact; Malformed_or_ambiguous_original_json_is_rejected; Same_transport_cannot_be_rebound_to_another_original_operation`. Defensive copies, UTF8 slice digests, escaped duplicate-name rejection, independent Owner domain/codec/digest and immutable transport-to-operation binding. |
| Transport conflict and foreign tenant | `CallerTransactionInboxV1.cs:22-24,56-60; InboxContractsV1.cs:172-180`. Tests: `Same_message_different_raw_or_binding_has_priority_and_preserves_original_result; Cross_tenant_transport_collision_never_invokes_owner_mutation_on_incoming_aggregate`. Raw/binding mismatch wins before authority unavailable. Foreign transport collision creates technical evidence only; no incoming-selected Owner mutation. |
| Conflict atomicity and no drift clearing | `CallerTransactionInboxV1.cs:64-100; FixtureOwner.cs:70-115`. Tests: `Conflict_write_set_rolls_back_together_with_published_health_and_original_bytes; Duplicate_old_equal_and_known_historical_conflict_all_pass_the_content_gate`. Fixture conflict observation, projection health, Published withdrawal and receipt rollback together. Original bytes retained; current equal event does not clear drift. |
| Unavailable dependency and write denial | `CallerTransactionInboxV1.cs:35-37,50-52`. Tests: `Unavailable_anchor_never_completes_duplicate_or_checkpoint; Final_write_denial_and_cancellation_do_not_create_success_receipts`. No success receipt/checkpoint on unavailable; trusted fixture final-write gate/cancellation tested. Actual IAM shared fence remains unproven. |
| Same-key concurrency and actual 23505 | `CallerTransactionInboxV1.cs:15-112; AtomicInboxTests.cs:227-309`. Tests: `Same_key_competition_recovers_whole_original_input_through_a_new_content_gate; Actual_unique_violation_is_rolled_back_then_original_reenters_admission`. Actual competing backend blocked on native advisory lock. Provider failure propagates; fresh whole-original-input retry passes admission; one business effect/journal. |
| Actual 40001 serialization failure | `AtomicInboxTests.cs:366-398`. Tests: `Actual_serialization_failure_preserves_original_version_key_on_retry`. Real simultaneous different-version transactions, exact SQLSTATE40001, fresh transaction retry with same original version key; monotonic projection and one original journal. |
| Pending versus committed receipt | `InboxContractsV1.cs:117-165; CallerTransactionInboxV1.cs:115-158`. Tests: `Pending_is_invisible_until_caller_commit_and_all_nine_tables_commit_together; Decisions_are_closed_and_pending_cannot_mint_committed_transport_success`. Pending has no result/transport mapping. Separate read-only current-authorized receipt visibility mints final mapping; missing receipt stays unknown. |
| Commit confirmation and entire response loss | `CallerTransactionInboxV1.cs:119-158`. Tests: `Lost_commit_confirmation_queries_original_bytes_without_a_new_write_grant; Lost_entire_response_recovers_by_original_key_only_and_cannot_ack_a_delivery`. Real COMMIT followed by application-level discarded confirmation; exact receipt or original Owner-key read with current read rights, no new-write requirement. No actual network-outage injection. |
| Actual PG xmin and shared contexts | `InboxModelV1.cs:102-124`. Tests: `Real_xmin_token_rejects_stale_checkpoint_update; Two_contexts_can_participate_only_when_the_actual_connection_and_transaction_are_shared`. Generated uint xmin changes and stale update rejected; two contexts accepted only using same native connection/transaction. |
| Legacy compatibility and retention separation | `Cp6MessageRetentionService.cs:17-47; InboxModelV1.cs:102-124`. Tests: `Static full-diff and legacy-read audit`. All legacy/shared files byte-identical to base; old retention selects Cp6* types, never new InboxV1 tables. No production migration/retention execution claim. |

## Resource cleanup and workflow boundary

All three owned resources have matching evidence hashes, consistent native system ID/data path/database OID/principal across ownership and stop records, and actual smart-shutdown completion in PostgreSQL logs. The first resource preserves startup/setup failure diagnostics; the second was launcher diagnosis only; the final resource supplied the two successful smoke cases. At `2026-10-13T13:35:00.6490509Z`, an independent OS listener probe found zero listeners on 55427. No database connection or restart was made for this review. Data/logs are retained; no recursive deletion.

Candidate and pinned protected-base bytes of all 8 workflow files match the recorded guard. Seven are manual only; the remaining RFC3161 pull_request filter covers two paths absent from this diff. COORD must perform its fresh remote/workflow/run-attribution checks before publication/merge; this review made no network call and triggered/cancelled no Action.

## Held Owner and production boundaries

- Main/CRM real Owner adoption, authentic contract/catalogue+registration implementation, final IAM/fence and complete receive/reconciliation/publish/revocation writer coverage remain UNPROVEN/NOT_RUN.
- The concrete profile supports Owner-delegated seven transaction-scoped advisory locks with no prior advisory locks. Existing row-lock/pre-held registration protocols require separately reviewed support and must not be enabled by this result.
- Fixture Owner, authentication, registration catalogue, journal/projection/Published model and simple authorization booleans are synthetic infrastructure; real PostgreSQL atomicity is proven only for that fixture and new technical tables.
- Reconciliation authority selection/CAS, ERP CommandInbox adoption, full write-set audit/Outbox/requeue/lease/retention/hold/cutover, raw replay retention and current field/PII policy stay with original Owners/follow-up tasks.
- Commit-confirmation loss is explicit application fault injection after actual COMMIT, not an actual network fault. No full original business AC is PASS.
- No package publication, remote Actions execution, migration of an existing database, deployment, production enablement or remote-main integration is performed or certified by this review.
- Current remote-main freshness, Handoff raw remote equality and active GitHub run attribution were not re-queried by this read-only subreview; COORD must retain its own exact integration/publication evidence.

## Findings and integration decision

No verified blocking or nonblocking defect was found in the authorized library delivery. Approve serial normal integration with the existing evidence and unchanged-result reuse. The coordinator still owns latest-main drift checks, post-merge integration scope, remote-main containment, roadmap/generated views and the four shared project-state files. Keep the delivered wording explicitly library/isolated PostgreSQL; do not mark original business AC or real Owner adoption complete.

## Full changed-path inventory

- `docs/WP02-07-CALLER-TRANSACTION-INBOX.md`
- `docs/development/evidence/WP02-07/.gitattributes`
- `docs/development/evidence/WP02-07/PLAN.md`
- `docs/development/evidence/WP02-07/REPORT.md`
- `docs/development/evidence/WP02-07/REVIEW.md`
- `docs/development/evidence/WP02-07/acceptance-map.json`
- `docs/development/evidence/WP02-07/architecture.log`
- `docs/development/evidence/WP02-07/collect_evidence.py`
- `docs/development/evidence/WP02-07/operation-binding-red.log`
- `docs/development/evidence/WP02-07/original-key-red.log`
- `docs/development/evidence/WP02-07/pg-first.log`
- `docs/development/evidence/WP02-07/pg-second.log`
- `docs/development/evidence/WP02-07/postgresql-final.log`
- `docs/development/evidence/WP02-07/postgresql-reviewed.log`
- `docs/development/evidence/WP02-07/postgresql.log`
- `docs/development/evidence/WP02-07/prepublication-workflows.json`
- `docs/development/evidence/WP02-07/resource-reproduction.log`
- `docs/development/evidence/WP02-07/resources/7b2e37eff86b2d6c6400dd000951d3e5-init.log`
- `docs/development/evidence/WP02-07/resources/7b2e37eff86b2d6c6400dd000951d3e5-launcher.log`
- `docs/development/evidence/WP02-07/resources/7b2e37eff86b2d6c6400dd000951d3e5-ownership.json`
- `docs/development/evidence/WP02-07/resources/7b2e37eff86b2d6c6400dd000951d3e5-server.log`
- `docs/development/evidence/WP02-07/resources/7b2e37eff86b2d6c6400dd000951d3e5-start.stderr.log`
- `docs/development/evidence/WP02-07/resources/7b2e37eff86b2d6c6400dd000951d3e5-start.stdout.log`
- `docs/development/evidence/WP02-07/resources/7b2e37eff86b2d6c6400dd000951d3e5-stopped.json`
- `docs/development/evidence/WP02-07/resources/96a5576f1545a120a8e9c07ad86cc42a-init.log`
- `docs/development/evidence/WP02-07/resources/96a5576f1545a120a8e9c07ad86cc42a-ownership.json`
- `docs/development/evidence/WP02-07/resources/96a5576f1545a120a8e9c07ad86cc42a-server.log`
- `docs/development/evidence/WP02-07/resources/96a5576f1545a120a8e9c07ad86cc42a-start.stderr.log`
- `docs/development/evidence/WP02-07/resources/96a5576f1545a120a8e9c07ad86cc42a-start.stdout.log`
- `docs/development/evidence/WP02-07/resources/96a5576f1545a120a8e9c07ad86cc42a-stopped.json`
- `docs/development/evidence/WP02-07/resources/e8b467c96024233c0cf94af6e3654fd2-init.log`
- `docs/development/evidence/WP02-07/resources/e8b467c96024233c0cf94af6e3654fd2-ownership.json`
- `docs/development/evidence/WP02-07/resources/e8b467c96024233c0cf94af6e3654fd2-server.log`
- `docs/development/evidence/WP02-07/resources/e8b467c96024233c0cf94af6e3654fd2-start.stderr.log`
- `docs/development/evidence/WP02-07/resources/e8b467c96024233c0cf94af6e3654fd2-start.stdout.log`
- `docs/development/evidence/WP02-07/resources/e8b467c96024233c0cf94af6e3654fd2-stopped.json`
- `docs/development/evidence/WP02-07/resources/resources.json`
- `docs/development/evidence/WP02-07/review-regression.log`
- `docs/development/evidence/WP02-07/source-inputs.json`
- `docs/development/evidence/WP02-07/start-transport.json`
- `docs/development/evidence/WP02-07/test-results/inbox-architecture.trx`
- `docs/development/evidence/WP02-07/test-results/inbox-first-green.trx`
- `docs/development/evidence/WP02-07/test-results/inbox-operation-binding-red.trx`
- `docs/development/evidence/WP02-07/test-results/inbox-original-key-red.trx`
- `docs/development/evidence/WP02-07/test-results/inbox-pg-first.trx`
- `docs/development/evidence/WP02-07/test-results/inbox-pg-second.trx`
- `docs/development/evidence/WP02-07/test-results/inbox-postgresql-final.trx`
- `docs/development/evidence/WP02-07/test-results/inbox-postgresql-reviewed.trx`
- `docs/development/evidence/WP02-07/test-results/inbox-postgresql.trx`
- `docs/development/evidence/WP02-07/test-results/inbox-red.trx`
- `docs/development/evidence/WP02-07/test-results/inbox-resource-reproduction.trx`
- `docs/development/evidence/WP02-07/test-results/inbox-review-regression.trx`
- `docs/development/evidence/WP02-07/test-results/inbox-unit-final.trx`
- `docs/development/evidence/WP02-07/test-results/inbox-unit.trx`
- `docs/development/evidence/WP02-07/test-runs.json`
- `docs/development/evidence/WP02-07/unit-final.log`
- `docs/development/evidence/WP02-07/unit-first-green.log`
- `docs/development/evidence/WP02-07/unit-red.log`
- `docs/development/evidence/WP02-07/unit.log`
- `docs/development/evidence/WP02-07/validation-inputs-before-review-fix.json`
- `docs/development/evidence/WP02-07/validation-inputs.json`
- `src/CP6.Platform.EntityFramework/InboxV1/CallerTransactionInboxV1.cs`
- `src/CP6.Platform.EntityFramework/InboxV1/InboxContractsV1.cs`
- `src/CP6.Platform.EntityFramework/InboxV1/InboxModelV1.cs`
- `src/CP6.Platform.EntityFramework/InboxV1/InboxTransactionV1.cs`
- `tests/CP6.Platform.UnitTests/InboxV1/InboxContractTests.cs`
- `tests/CP6.Platform.WP0207.PostgreSqlTests/AtomicInboxTests.cs`
- `tests/CP6.Platform.WP0207.PostgreSqlTests/CP6.Platform.WP0207.PostgreSqlTests.csproj`
- `tests/CP6.Platform.WP0207.PostgreSqlTests/Directory.Packages.props`
- `tests/CP6.Platform.WP0207.PostgreSqlTests/FixtureOwner.cs`
- `tests/CP6.Platform.WP0207.PostgreSqlTests/PgFixture.cs`
- `tests/CP6.Platform.WP0207.PostgreSqlTests/pg_resource.py`
