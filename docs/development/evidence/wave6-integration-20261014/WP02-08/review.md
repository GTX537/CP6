# WP02-08 concentrated premerge review

**REQUEST_CHANGES — two P2 library defects; this candidate is not ready to merge.** Missing real Owner/broker adoption is not the reason for this decision.

Candidate `3e4c078434ff4ae066f20d83e134fb268663700c`, source commit `495a41f7a238e6af5d19e4daa1e3880c3ff33f4c`, base `e5c63a28c94bae4cc66ec50fbe6079553c03d0d0`. Worktree `D:/CP6.Platform-worktrees/wp02-08-20261013` was clean at the candidate. The complete 66-file diff stays within the assigned implementation/test/evidence paths. Review uses the fixed wave6 card, PLT-DEL v0.2.1 (raw SHA256 `642a2fbc35d17fbfaa0bcc51617e24654cbe193e0f907674a3e6f4c41c47eb84`) and applicable R1 native transaction/Owner boundaries.

## WP0208-R1 — [P2] Persist the Blocked epoch transition under its original mutation ID

Location: [OutboxLifecycleV2.cs](D:/CP6.Platform-worktrees/wp02-08-20261013/src/CP6.Platform.EntityFramework/OutboxV2/OutboxLifecycleV2.cs:48), lines 48-51.

The unresolved/missing handover branch changes RouteControl.Phase to Blocked and inserts EpochBlocked audit, then returns PendingCommit without inserting OutboxLeaseMutationReceiptV2 for mutationId. A committed state change therefore has no original mutation result. Repeating the identical input reaches the changed root xmin check at line 37 and returns Conflict instead of the prior result; an original-key receipt lookup remains absent/Unknown.

Reproduction:

1. Seed a Pending row; BeginDrainV2 with switchId S and commit. Read the resulting control token V.
2. Make the trusted fixture Owner return UnknownTransactionsAuthoritativelyResolved=false. Call AdvanceEpochV2(M,S,epoch=1,V), observe PendingCommit/Blocked, actually commit, and suppress the application acknowledgement.
3. In a new transaction repeat exactly AdvanceEpochV2(M,S,1,V). SwitchReplay finds no receipt for M and the control-token gate returns Conflict. The root/audit committed, but the original mutation receipt does not exist.

Minimal fix: Record the Blocked result, exact tuple and mutationId through the same atomic immutable switch-receipt path used by successful epoch advance. Preserve the original Blocked output on exact replay without additional audit/state writes; changed input under the same ID must conflict. A later new advance uses a separate new mutation after the blocked result is resolved.

Targeted regression: Extend Unknown_handover_blocks_epoch_and_caller_rollback_restores_root_and_history with the committed/suppressed-ack branch, exact-key replay, changed-tuple conflict and unchanged complete snapshot after replay. Existing test lines 302-320 only rolls back the Blocked branch before successful advance.

Evidence boundary: Read-only source trace and existing-test inspection. New regression NOT_RUN; no PostgreSQL resource restart.

## WP0208-R2 — [P2] Do not restart a full publish budget after a delayed send-intent commit

Location: [OutboxDispatcherV2.cs](D:/CP6.Platform-worktrees/wp02-08-20261013/src/CP6.Platform.EntityFramework/OutboxV2/OutboxDispatcherV2.cs:59), lines 59-70.

The DB-clock lease-budget check runs inside PrepareSendV2 (CallerTransactionOutboxV2.cs 204-221), but Write then awaits CommitAsync and transaction/context disposal. DispatchClaimV2 starts a new full PublishBudget only after that await and unconditionally invokes the publisher. A slow successful commit acknowledgement can consume the remaining lease, so this worker starts a new network send after expiry or after another worker has reclaimed the row. The later Complete CAS refuses the write, but cannot undo the newly initiated delivery.

Reproduction:

1. Use a committed claim with a 2-second lease and a publish budget smaller than the lease.
2. Configure a test-only EF transaction interceptor on the dispatcher factory to delay successful completion of the first send-intent COMMIT until more than 2 seconds after the DB budget check; do not throw or change the database outcome.
3. At publisher entry compare PostgreSQL clock_timestamp() with the original ticket expiry. Current code still calls the publisher (Calls=1) when the lease is already expired. Completion later returns Expired/uncertainty. A second worker can also reclaim during the delayed acknowledgement.

Minimal fix: Carry a conservative send deadline derived from the authoritative DB-clock lease check and account for elapsed monotonic time across commit/disposal. Before invoking publisher, reject when the required budget is no longer available and bound cancellation by the remaining authorized window; retain the original send-intent/mutation identity. Do not substitute worker wall-clock time for DB authority or publish while holding locks.

Targeted regression: Add real-PG delayed-successful-COMMIT regression asserting publisher Calls=0 after the deadline, plus a delayed-but-still-valid case with the reduced remaining budget and existing zero-lock publisher checks. Existing dispatcher tests cover transport exceptions/write revocation, not this pre-send delay.

Evidence boundary: Read-only source trace and deterministic timing schedule. New regression NOT_RUN; no PostgreSQL resource restart.

## Evidence and reviewed safeguards

The reviewer verified 278 current working/Git source pairs, 9 dependency graphs, 153 current compiled artifact hashes and all 11 retained TRX hashes/counters without mismatches. Selected worker results contain exactly **153 distinct successful cases: 15 unit/related, 40 PostgreSQL, 98 architecture**, with no skips. The full corrected PG run is 38/38; mapping-final09 is 5/5 with only two additional distinct cases. Expiry-final08 is a repeat. These results are reused from the worker's recorded inputs, not described as newly executed at the candidate SHA.

`validation-reuse.json` records the full PG source/loaded-assembly snapshot and later strict-time-helper, mandatory-property and test changes. Final current hashes match `validation-inputs.json`. The final PG test DLL is `00029e4f394cb63af0ec26f57837e01e3fffbd13e1148c503714985af283818f`; its copied EntityFramework DLL is `ee481a168b633e0db49eae351038174145153c714454bde1b8302744a254ec34`. Builds predated the source commit, so baseline+dirty embedded identity remains honestly documented. Architecture reuse covers unchanged structural/dependency scope, not the new review defects.

The 34 fixed Git source blobs matched their pinned hashes; the two original archive entries retain explicit non-Git raw provenance. All 60 original AC IDs, assertion texts, source coordinates and hashes match the foundation catalogue; all original business states remain NOT_RUN.

Source review covered actual caller connection/transaction identity and Serializable write isolation; read-only ReadCommitted receipt recovery; trusted tenant/worker registration and current Owner row fences; Owner -> route -> native -> binding order; actual post-write xmin mapping and immutable tickets/seals; final native CAS using DB wall-clock strict expiry; epoch/drain gates; exact mutation replay for completion/renewal; transport/completion split; native/Binding/DLQ legacy fences; retention policy/hold/reference/consumer/archive closure and caller-transaction audit; and task resource/scripts. No additional material issue was established in these inspected paths beyond the two findings above. The worker's earlier three review fixes remain present.

Retained failures include the initial missing-protocol test, unsupported-provider exception normalization, wrapped native40001 assertion, EF1002 build failure and resource-preparation failures. Successful later runs do not erase them. Nonblocking evidence note: REPORT.md line33 also mentions a retained unavailable-project-assets failure, but its raw failure log was not located in this candidate; retain it or narrow that wording when delivering the fixes.

Reviewer validation is read-only static/source/evidence inspection. No new .NET tests or PG runs were executed. A reviewer metadata script initially decoded UTF-8 JSON using Windows GBK and failed after the completed hash/TRX checks; the explicit UTF-8 follow-up passed the 60-case comparison. This was not a product test failure.

## Resource, integration and acceptance boundaries

The task PG identity is owner `dcdfb04dda48edc0d52d638b5e7bd2f0`, system identifier `7696392773160066692`, database `cp6_wp0208_20261014_dcdfb04d` / OID16385, port55428. Recorded stop verifies those identities and a runtime role without superuser/CREATEDB/CREATEROLE/INHERIT/BYPASSRLS/schema-create. Smart shutdown retained data/logs. Reviewer independently observed zero listeners on55428 and did not start or alter the cluster.

All eight workflows are byte-identical between base and candidate. The RFC3161 PR filter's two paths are untouched. No Actions was triggered, cancelled, rerun or reconfigured, and no remote/Git/database mutation was performed by this review. Fresh remote integration checks remain COORD's responsibility.

Main/CRM real Owner/host/complete-writer adoption, actual migration/cutover, broker/network delivery, legal policy/archive restore and all original business acceptance remain UNPROVEN/NOT_RUN. Existing fixture results do not authorize these activities or certify exactly-once delivery. No SQL Server expansion or schema-per-tenant rollout is included.

Fix the two P2 defects within the task window, preserve the old evidence, add the indicated targeted regressions under COORD resource ownership, and request focused rereview. A repeated whole-repository review/test cycle is not required unless the correction expands scope.
