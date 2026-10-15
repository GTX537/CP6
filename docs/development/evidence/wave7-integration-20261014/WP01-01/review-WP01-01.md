# WP01-01 independent review — CHANGES REQUIRED

Reviewed `99fc0ce383d2d02c1404a8adbb3abe971b41d332` → `6d09479f2f44eb604ef69993b370f2f7cacde980` (PR #209), PREPARATION_ONLY. One **P2 blocker** tracked by [Issue #211](https://github.com/GTX537/CP6/issues/211), no optional findings. Source and ledger are unchanged.

## Blocking finding

**[P2, confidence 10/10] Preserve original ITER row identities** — `eng/wp01-01/decision-closure/mapping_constraints.py:64`.

The generator substitutes a new `storage_id` PK and declares business-number/version/command uniqueness, but does not preserve six original row IDs through an authoritative identity binding. `Project.Id` becomes nullable `bytea` (`postgresql-mapping.json:423–425`), while the original ITER specification line 1580 says `Project.Id`: ID, non-null, PK/stable identity. The Project mapping has only `storage_id`, `(tenant_id, storage_id)` and `(tenant_id, project_no_key)` unique keys (`postgresql-mapping.json:702–728`), no Id binding and no check. Iteration and ReviewRound have the same gap; three other original IDs are absent from the field list.

Motivating generator lines:

```python
# mapping_constraints.py:57
add_column(e,name,typ,nullable=field.get('nullability')!='NOT NULL',source=field['source_pin'])
# mapping_constraints.py:64
e['primary_key']={'columns':['storage_id'],'scope':'global internal row identity; original identity stays in separately scoped OwnerKey'}
# mapping_constraints.py:98
'Project':(['project_no_key'],'TenantId + exact ProjectNo; original tenant-wide business uniqueness')
```

The separate OwnerKey mechanism was inspected: it stores **other** identities for the following six cases, so it does not cure the missing original Id binding.

| Entity | Original ITER line | Candidate original Id storage | Actual OwnerKey bindings |
|---|---:|---|---|
| Project | 1580 | nullable bytea id | project_no_key |
| Iteration | 1602 | nullable bytea id | iteration_no_key |
| IterationInput | 1632 | omitted | input_version_key |
| CandidateRef (original CandidateObjectRef) | 1664 | omitted | reference_tuple_key |
| ReviewRound | 1692 | nullable bytea id | round_no_key |
| ExternalRequestReceipt | 1738 | omitted | operation_type_key, aggregate_source_scope_key, request_key_id |

Original source: `D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/eb/eb94f563f6f5ff08__02_ITER_当前阅读主册.md`; hash and exact source lines are recorded in the JSON report. `build_candidate.py:113,115,120` omit the three IDs. This is not a blanket complaint about nullable projection columns: Candidate, EngineeringTask, ReviewIssue, Evaluation and AuditClosureAmendment have explicit identity-bearing keys and were distinguished from these six cases.

**Minimal static counterexample:** two same-tenant Project rows with the same original `Id`, different generated `storage_id` values and different valid `project_no_key` values satisfy every declared UQ/FK. Project has no check involving Id. A read-only Python check confirmed `original_id_equal=true`, `all_declared_unique_keys_pass=true`, `project_checks=[]`. No database was run.

**Required correction:** explicitly bind each of the six non-null original IDs to exact original bytes/schema/scope, through a required OwnerKey or an equivalent lossless representation, and preserve the original uniqueness domain separately from business-number/version/command keys. Do not infer tenant-local scope for a global identity. Fix the generator and regenerate the mapping; add targeted static checks for original-ID coverage, required authority binding, duplicate original IDs under different business keys and scope behavior. Reuse unchanged gate/manager evidence. No DB/full build/Actions is needed for this correction.

The blocker is candidate design correctness under DP03; production remains disabled.

## Evidence and remaining boundaries

- All 86 changed files are within the two assigned directories. Read all changed blobs (7,961,543 bytes), parsed 29 JSON files strictly, verified all 80 manifest core artifacts, four inherited pins and 90 protected-file hashes. Worktree bytes match the reviewed candidate; `git diff --check` passed.
- Verified all 22 fixed source-audit records and 138 anchors against their declared raw/Git representations. DP08 does not promote historical `b7ec269` to current implementation.
- Independently reconstructed exact target reachability: 214 tasks, 1,256 edges, 17 cross-repository edges. Required chains `WP05-18→WP02-10→WP02-07` and `WP05-18→WP05-14→WP05-13→WP03-17` are present. Null contract/package/remote selections remain unresolved evidence requirements.
- DP01/02/04/05/06/07 retain distinct digests, actual authorization/configuration gaps, retention/hold and language governance, capacity-as-candidate and real VER/MES requirements. No additional blocker found in those dispositions.
- Gate flags are hard-coded false; actual mode cannot authenticate authority or remote ancestry and always rejects. Production manager has no private-gate import. Actor and signed time remain null; task stays **PLANNED / NOT_CHECKED**. Original 30/48 gaps remain null/unrecoverable; accounting remains 123 originals + 2 proposed bases.

## Reused validation and limits

The 54-test run of `2026-10-15T01:29:23.391898+00:00` and the single read-only roadmap run of `2026-10-15T00:48:55.484689+00:00` were **reused, not rerun**. Their exact source and raw log hashes match the final candidate. The recorded 12,251 static checks and wave6 3,509 checks / 23 comparator / 10 bundle tests retain their original provenance. These successes do not test semantic completeness of the missing Id mappings. Prior red logs and scoped repair evidence remain preserved.

Fresh work consisted of diff/source/hash/graph inspection and the targeted identity counterexample. No source/ledger edits, push, merge, PR change, Actions trigger/cancellation, workflow change, DB/DDL, product build, real runtime acceptance, authority authentication or remote-ancestry proof occurred. No optional findings.
