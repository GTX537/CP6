# WP6 concentrated review: Lifecycle and Entries

Reviewed HEAD: `cbbb7fc8e99290f6aba7589830a98a98726280e9` plus the current dirty working-tree inputs. This is one independent partition of the task review, not a new full-task review.

Reviewed files:

- `scripts/database-compatibility/DatabaseCompatibilityLifecycle.psm1`, SHA-256 `BF192F54A51140FB3162BDDD15553EA565972CA31A105B77C026A7EC4E972D39`.
- `scripts/database-compatibility/DatabaseCompatibilityEntries.psm1`, SHA-256 `D8D7F1E10CF5860CE95EBFF61C9E7EB7BE7C22DD0CA73D70A480A9919ACE428D`.

## Finding

**P2 — Bind all actually executed runtime files, including dependencies.** At `DatabaseCompatibilityEntries.psm1:118-125` and `:133`, the entry records only the primary project DLL's `BinarySha256`. With `-SkipBuild`, no build record supplies the omitted dependency hashes, and source collection deliberately excludes `bin`/`obj`. Changing `CP6.Core.dll`, a provider DLL, a `.deps.json`/`.runtimeconfig.json`, or a nested native runtime dependency while retaining the same primary test DLL and source files therefore does not change the recorded entry artifact identity. The entry can still return `Success`, and an in-flight dependency change is not rejected. This makes evidence insufficient to identify the actual runtime and to establish unchanged-input reuse. The build collection at line 213 is also top-level and extension-limited. Record the complete recursive runtime file manifest (relative path, length, SHA-256) before each entry, compare it again after process completion, retain the existing primary `BinarySha256`, and reuse the same collection for build output. An offline process fake should prove that a dependency modification during execution fails.

Root accepted this finding and authorized a targeted fix. This document preserves the original review inputs; follow-up results will be recorded separately below.

## Scope and evidence

Read-only review covered connection routing and secret separation, receipt/owner/physical identity checks, all-target cleanup preflight followed by per-target recheck and ordinary DROP/absence verification, SQL backup checksum/archive binding and explicit restore target files, PostgreSQL transactional restore and database ACL/settings reconciliation, entry selection/environment, process failure propagation, exact result validation, source input traversal and binary scope. No additional actionable correctness finding was established in Lifecycle.

Ran only existing offline self-checks: Lifecycle **42 checks passed**; Entries **501 checks passed**. These use module-local fakes and are not native acceptance evidence. No .NET CLI, database tool, API, remote action, or database mutation was executed by this reviewer.

Read existing root-created public evidence (not rerun):

- `D:/CP6/tmp/db-compat-wp6-pg-application/pg-native-backup.json`, SHA-256 `DB94CE053DB2BB518AA677077F3492FA8F111E1D35F45F544983DC56FC3EE528`: `BackedUp`, native exit 0.
- `D:/CP6/tmp/db-compat-wp6-pg-application/pg-native-restore.json`, SHA-256 `B5DD93C8A12C2CE577A673C13C28C8D909A80C4A9ED3B4DC8EDDEEA422BA09F0`: `Restored`, native exit 0.
- `D:/CP6/tmp/db-compat-wp6-sql-application/sql-native-backup.json`, SHA-256 `79CA6802634BEDCACBE8A86AA9DCCAEF9908AC63297DBC001120016EE02C1D06`: `BackedUp`, native exit 0.
- `D:/CP6/tmp/db-compat-wp6-sql-application/sql-native-restore.json`, SHA-256 `D21876609BEB888324D55792E285713DF772E5EA9F28087B1392F7867AD52AA5`: `Restored`, native exit 0.

Those runs explicitly retain cleanup as pending task-level review. They do not prove the new formal entry's full dependency binding, SignalR/recovered pending-worker additions, or final native cleanup. No archived report has been rewritten or relabelled.

## Targeted follow-up — resolved locally

The dependency-mutation regression first failed against the reviewed implementation: the module-local fake returned exit 0 and a valid report after changing `CP6.Core.dll`, and the entry incorrectly succeeded. After the fix, the entry retains both real fake-process/report results but fails with `CP6_COMPAT_RUNTIME_ARTIFACT_CHANGED`.

Entries now writes `runtime-artifact-before.json` and `runtime-artifact-after.json`, each containing every runtime file's relative path, length and SHA-256, including nested native files and other extensions. Public entry results retain the original `BinarySha256` and add both manifest paths/hashes and `RuntimeArtifactsUnchanged`. The primary DLL hash comes from the same before-manifest snapshot. Build output uses the same recursive collector. Enumeration/hash errors and runtime reparse points fail closed, and entry output directories inside the runtime directory are rejected.

Final targeted verification: **510 offline checks passed**, including unchanged runtime success, modified dependency failure, added/removed nested runtime resource failure, preserving exit-0/passing-report evidence when artifact binding fails, and preventing logs inside the runtime directory. Both changed PowerShell files parse with **0 errors**. No .NET CLI, native database, API or remote command was executed. Existing native evidence remains unchanged and cannot be relabelled as using this new complete runtime binding.

Frozen follow-up inputs:

- `DatabaseCompatibilityEntries.psm1`: SHA-256 `5CF53766FEAC54CEF69C6F1768C75D508EC99EA1DB34D80E85DF52647AF94299`.
- `Test-DatabaseCompatibilityEntries.ps1`: SHA-256 `1987DC5CAD3D5CADEC373677E81045734FBBDEE3FC0BABB314027AB83F13116C`.
- Lifecycle was not edited; its earlier 42 offline checks and scoped review remain applicable.

Finding status: **Resolved locally; next native executions must use the revised entry helper.** Root retains the final integration/native verification responsibility.
