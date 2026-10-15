# WP01-01 Issue #211 focused correction review

**PASS for the corrected PREPARATION_ONLY snapshot. No remaining blocker.**

This followup reviews Issue #211 and its direct effects only. It does not restart or replace the original full-task review. The original missing-identity finding and the intermediate unsupported global-scope finding are both closed in this snapshot. Issue closure still requires normal coordinator integration and remote-main verification.

## Exact reviewed snapshot

Worktree: `D:\CP6-worktrees\bug-211-plm-identity-20261014`. Source base `b2dc9387e35124622c18ec154cecb2d8db87d938` merges fresh Main `e82175d630211c3b8a3cb495fff13959f3dd8152` with original candidate `6d09479f2f44eb604ef69993b370f2f7cacde980`. The correction is staged and uncommitted: all 72 staged blobs were independently compared to exact working bytes.

Current manifest SHA-256: `7eaa62f8e2904978f7440de5a15c8e0d9bc8328ffe12199dfee99ebe098ab839`. All 141 manifest artifacts and 90 protected input hashes verified.

| Input | SHA-256 |
|---|---|
| `eng/wp01-01/decision-closure/mapping_constraints.py` | `a334c995dab31006f38bf1f4448f99bd44da0078da109f1d3e556504bbe9dd6c` |
| `eng/wp01-01/decision-closure/build_candidate.py` | `5453bcce0c17d3a147517585060724dafa636715cd107e47e76596245c6ea305` |
| `eng/wp01-01/decision-closure/tests/test_original_identity_mapping.py` | `cea6d60e9c5e0be1fcf54e10a336c3aa759b4dd5c0e3e30203b353e8870d9942` |
| `docs/development/evidence/WP01-01/decision-closure/postgresql-mapping.json` | `c2d336b0cc3550f0d61b951359b60fca6a22237719e1a4fce88424139e7df6c6` |
| `docs/development/evidence/WP01-01/decision-closure/coord-corrections/issue-211/verify-correction.py` | `402bc3e3a77b0aede88fd6baf360f318dc3dd4b4cb5e74f809f86462c60f7f67` |

## Original identity and scope

Each Id is now required `bytea`, with a required `original_id_key`, exact raw/schema/domain binding and a one-row-per-registered-identity unique key. Existing ProjectNo, IterationNo, input-version, reference-tuple, RoundNo and command uniqueness remain independent and unchanged.

| Entity | Original ITER line | Preserved identity |
|---|---:|---|
| Project (Project) | 1580 | Bare Id global PK |
| Iteration (Iteration) | 1602 | Bare Id global PK |
| IterationInput (IterationInput) | 1632 | Id + TenantId + IterationId |
| CandidateRef (CandidateObjectRef) | 1664 | Id + TenantId |
| ReviewRound (ReviewRound) | 1692 | Id + TenantId + IterationId |
| ExternalRequestReceipt (ExternalRequestReceipt) | 1738 | Id + TenantId + IterationId |

Source raw SHA-256 is `eb94f563f6f5ff08a710881deb07a1815c537a0c09115253d9b62da578405eae`; all six cited excerpts were checked against the original archive file. Only Project/Iteration have explicit bare-Id PK statements. The four grouped entries explicitly retain their original source tuple and `BARE_ID_GLOBAL_UNPROVEN_DO_NOT_NARROW`; no additional global guarantee is inferred.

The interning tuple remains `(domain_key, schema_bytes, scope_bytes, identity_bytes)`. Global PK scope is empty, so TenantId cannot split those two identities. Grouped scope uses exact original tenant/parent components; the same raw Id in a different valid source tuple remains distinct. The parent component resolves original `Iteration.id` through the same-tenant FK, never the surrogate row UUID. Exact tuple reuse returns one key; `UNIQUE(original_id_key)` rejects a duplicate row even if business keys differ. Equal digests cause full byte comparison, preserving distinct values and scopes.

Proof: `build_candidate.py:113`, `mapping_constraints.py:176`, `:184`, `:197`, `:204`, `:247` under `eng/wp01-01/decision-closure/`. Only the six entities and OwnerKey source profiles changed; the remaining 24 mappings, original business UQs and parent FKs are unchanged.

## Closed findings and retained failures

- **Original P2 — missing original Id authority:** closed by required raw bytes, required separate key, exact source binding and independent identity uniqueness for all six entities.
- **Focused P2 — all six assumed global:** the intermediate version narrowed unproven scope for four grouped identities. It was rejected and preserved in `issue-211-scope-finding.json` and the source package’s `superseded-all-global/` tree. The final version distinguishes the two explicit global PKs from the four grouped source identities. No remaining scope-narrowing finding was established.

## Validation provenance

The same final test source produced scope RED **8 tests / 18 assertion failures**, followed by scope GREEN **8/8**. Original missing-binding RED **6 tests / 37 failures** and the superseded all-global GREEN **6/6** remain intact. All run source hashes and stdout/stderr byte counts/hashes were independently verified against current or explicitly retained snapshots.

The final separate static verifier records **1,000 checks PASS**. This review independently confirmed final manifest/input hashes, six original source pins, preserved constraints, exact staged bytes, and `git diff --check HEAD`. It did **not** rerun tests or claim a new 1,000-check execution. The original 54 synthetic gate tests and read-only manager run retain exact read-input/log hashes and are reused; the original 12,251-check result describes only its historical mapping. The original 16 snapshots and rejected intermediate 14 snapshots were hash-verified.

Supporting files: [final focused checks](checks-WP01-01-fixes-final.json), [retained all-global finding](issue-211-scope-finding.json), [original complete task review](review-WP01-01.md).

## Boundaries

The output stays `CANDIDATE_NOT_DDL`; the task stays `PLANNED / NOT_CHECKED`. `production_enabled`, `actual_adoption` and `actual_completion` remain false. OwnerKey interning, lock atomicity, raw/key/parent checks and uniqueness are design requirements. Synthetic tests interpret those declarations; no database registry, DDL, concurrency, provider/business behavior, authority authentication, remote ancestry or production adoption was executed or proven. No source, ledger, commit, Actions or remote write was performed by this reviewer.
