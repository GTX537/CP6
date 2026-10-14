# Wave 6 coordinator task review

**Ready for document delivery: true. No actionable P1/P2 findings.**

Reviewed the complete staged 32-file task on `D:/CP6-worktrees/fw-s02-1012`, branch `codex/wave6-dispatch-20261013`, against Main `9a0155dec1c3366df8a6631f3d8fdb086aa32fc8`. This is a single consolidated task review, not merely a review of the earlier consumption report. Exact index identities for every file are in `review.json`.

Raw `git -c core.longpaths=true diff --cached` SHA-256:

`6fb881fed367bc32a454c15b70b4f8462bc13c1724ad6d63ec4be4094dc069e1`

## Conclusion and scope

- A/WP01-01 changes assignment only and retains PLANNED/NOT_CHECKED. Its private preparation directories cannot change the immutable proposed acceptance artifacts, original gap history, adoption record, ledger tool or completion rules. The future decision is concrete and remains unsigned; ordinary continuation is not represented as adoption.
- B/WP02-02 changes assignment only and retains BLOCKED/NOT_CHECKED. It may prepare private inventory/checker/probe evidence and a publication plan, without production project/lock/trust/DI/storage changes. Source experiments and unsigned local packages cannot become formal feed adoption. A separate future producer window is not granted to this Main worker.
- C/WP02-08 and D/WP02-25 alone become READY/CONFIRMED. Their predecessors are delivered; actual write paths do not overlap. Task-card locks are retained, including read-only entry points. C owns versioned EF Outbox functionality and narrow legacy bypass guards; D owns versioned observation/health/export behavior and narrow opt-in registration. Shared telemetry, Context, Inbox, public release trust and dependency surfaces are explicitly fenced.
- C's design retains original native Outbox authority, global MessageId/TenantId, caller transaction/root locking, post-write native/Binding token identity, strict DB-time lease fence, Unknown versus definite rejection, drain/epoch semantics and conservative retention closure. The current source confirms the gaps motivating this task. PostgreSQL-only adaptation is explicit, with no assumption that legacy byte[] rowversion already works on PostgreSQL. Original DEL acceptance and actual Owner adoption remain separate.
- D's scope matches the original OBS runtime identity, telemetry privacy and health-inventory obligations. It requires a real isolated host/dependency and final exported evidence, separates declared from observed/trusted/qualified identity, preserves old technical counters and SLO semantics, and leaves SLO qualification/recorder repair and Main/CRM adoption to their own tasks. It does not gain EF or Outbox writes.
- PG port 55428 and task-root ownership are reserved only for C. Fresh private credentials, least-privilege runtime identity, canonical path/marker/system identifier/OID/principal checks and own-cluster shutdown are required. Provisioning remains false; occupancy must be checked again before use. D may use its own loopback dependency but cannot borrow C's database. No existing business DB or environment operation is granted.

## Independent checks

- All 32 staged paths are documentation/evidence. Every staged JSON parsed; source reports, dispatch cards, intake/setup, admission, structural evidence, generated view and four status-file changes were assessed as one task.
- Compared full before/after roadmap objects: only an appended change-log record and four task objects differ. A/B change only assignment; C/D change only state, entry_gate and assignment. Every other task and all business/acceptance/source/coverage sections are unchanged. DONE remains the same 20 IDs of 375; no START, delivery evidence or business PASS is fabricated.
- Independently verified **122 committed source pins** by Git blob and raw SHA-256, **8 copied report files** against their dispatch hash and scratch original bytes, and **54 new relative links**. No errors. The two original untracked design bodies' raw hashes match the catalog identities; they are not falsely described as Git-tracked inputs.
- Four task worktrees match their specified branches and source baselines and have no tracked modifications. Assignment paths match dispatch paths; pairwise path overlap check found none. Working/index content agrees after explicitly allowing checkout newline conversion. Evidence raw-byte equality was checked without newline normalization.
- `git -c core.longpaths=true -c core.whitespace=cr-at-eol diff --cached --check` passed. The local evidence-only `.gitattributes` exception for blank-at-EOF preserves copied report bytes; it does not disable other whitespace checks or product gates. Original formatting failure is retained.
- The first reviewer invocation of `python docs/development/tools/manage_roadmap.py` failed because PATH Python lacks `Path.is_relative_to`. This is preserved as an environment failure, not a failed ledger or business test. A single recovery with `C:/Users/tt/AppData/Roaming/uv/python/cpython-3.13-windows-x86_64-none/python.exe -X utf8` passed: generated views/structural checks consistent, 125 targets with 0 delivered, 247 runtime NOT_RUN, 41 historical entries. No product build or runtime test was run.

## Nonblocking wording and remaining boundaries

The duplicated WP02-02 `rev-parse` command would be clearer with explicit per-repository `-C`. The copied Outbox assessment's phrase “remote writes” is broader than its intended external business/runtime-write exclusion, while the task's delivery section explicitly grants Git/Handoff publication. The copied observability recommendation says it is not itself a roadmap assignment, whereas the surrounding final COORD card and roadmap do make the assignment. These wording details do not expand or invalidate the explicit final grants and do not require another review cycle.

This approval covers document delivery and the recorded bounded dispatch. It does not verify future implementation, production acceptance, formal publishing, current remote feed trust or database behavior. Future push/PR workflow preflight and normal protected delivery remain required. Intake's remote observations are retained at their actual timestamp; this review did not re-query GitHub or run/cancel Actions.

No repository/Git mutation, database access, network write, signing, package publication or deployment was performed. Only scratch review files were written. The staged diff stayed unchanged during the review.
