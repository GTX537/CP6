# WP01-01 decision closure candidate

Identity: `WP01-01-DECISION-CLOSURE/20261014-R1`. Prospective basis: `CP6-WP05-01-ACCEPTANCE-BASIS/1.2.0-proposed`. Main source baseline `99fc0ce383d2d02c1404a8adbb3abe971b41d332`; product baseline `1129953156b7775152a74ad2f51cb95ef430ee56`. Platform `b28db763f70ab6f164cb7c87d6ce49a6d97267f1`; CRM `1e4eea61c31d7547c3b3495d392eefbed99b7339`.

This package makes concrete technical choices for review. It is unsigned and unadopted. The private gate cannot enable production, establish adoption or complete a task/target. WP01-01 remains PLANNED / NOT_CHECKED. Actual actor/signature/time are not inferred from transport, ACK, task assignment, fixtures or a coordinator recommendation.

- [decisions.json](decisions.json) gives clause-level inherited rules, new choices, configuration requirements and provider/runtime gaps for all eight decision points.
- [postgresql-mapping.json](postgresql-mapping.json) gives ITER/REL fields, source pins, exact raw representations, PostgreSQL projections, FK/index/CAS and Owner write sets. It is a physical design candidate, with no DDL or shared EF changes.
- [current-source-audit.json](current-source-audit.json) fixes current symbols/routes/models/writers and REUSE/CHANGE/NEW successors. Historical `b7ec269` is reference only.
- [provider-consumer-closure.json](provider-consumer-closure.json) preserves all 214 nodes / 1256 declared dependency edges, including 17 cross-repository edges, reachable from the eleven distinct ITER/REL implementation tasks. It does not demand this entire graph for each runtime scenario; conditional scope needs exact authorized binding. No existing task dependency is privately removed.
- [basis-delta.json](basis-delta.json) adds only named candidate obligations. [gate-contract.candidate.json](gate-contract.candidate.json) and [gate-design.md](gate-design.md) distinguish task delivery, package inventory closure and target DONE.
- [manifest.json](manifest.json) pins the new core snapshot and immutable inherited inputs. Parent intake/review/publication metadata is outside this snapshot; review records do not recursively change the reviewed bytes.

Wave6 is reused at its recorded hashes: 577 navigation rows, 221 recovered scenarios, 295 body locators, 43 supplemental mappings; the 8 target R1 rows and 10 detailed mappings remain distinct overlapping views. The recorded 3509 static checks, 23 comparator tests and 10 bundle tests were run in wave6, not rerun here. Their source time, scripts, raw logs and input-lock remain in the preparation package. These numbers are neither new business passes nor restored legacy cases.

Legacy ITER30/REL48 remain NOT_INVENTORIED / UNRECOVERABLE / UNPROVABLE, with member IDs and missing-original hashes null. Package accounting remains **123 original inventories + 2 prospective new bases**, never 125 recovered originals. Recorded business state stays 0/125, 247 NOT_RUN, 41 REFERENCE_ONLY; task state stays 22/375 DONE.

The concrete residuals are the exact ENG technical algorithm adoption/compatibility decision; selected real project membership/roles/delegations/SOD and published OA policy; engineering retention/legal-hold policy values; actual workload/SLO/capacity evidence; genuine VER/MES safe termination implementations; reviewed Japanese/Korean content and client adoption; PostgreSQL role/RLS/precision/transaction/recovery evidence; and exact package/provider/consumer/remote delivery. The candidate does not turn provider implementation gaps into user approval questions. `decisions.json` names each successor task and scoped blocking effect.

Only private Python checks and one read-only roadmap check are applicable. No product build, DB connection/migration, Office recomputation, service deployment, Actions run or remote workflow change is performed. No service/database resource is assigned to lane A. Final evidence and limitations are in [validation.json](validation.json) and [delivery.md](delivery.md).
