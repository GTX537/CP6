# Actual local source inspection

The separate [actual-source freezer](ACTUAL-FREEZE.md) consumes this inspection through a request-bound entry. The inspector itself remains read-only and does not authorize that operation.

`ActualSourceInspector.InspectAsync` and CLI `inspect-actual` collect the source facts needed to prepare C04A actual execution. They only read SQL data/catalogs and acquire temporary shared locks. They cannot initialize a control schema, change permissions, freeze, reopen, seal or authorize target writes. The existing `SourceFence` mutation methods still reject an actual database name.

Use the same build and credential handling as [the tool README](README.md). Set `C04A_SQL_CONNECTION`, exact `C04A_EXPECTED_DATABASE`, independently observed `C04A_EXPECTED_DATABASE_GUID` (broker GUID), and exact `C04A_EXPECTED_SERVER_NAME` (`SERVERPROPERTY('ServerName')`). Optional timeouts are unchanged. Only a local, nonclustered SQL Server user database is accepted; local `tcp:` and `lpc:` endpoints are supported. Connections cannot attach files, select a failover partner or join an ambient transaction. The inspector disables pooling and identifies its own connection as `CP6.C04A.ActualSourceInspector`.

```powershell
dotnet tools/CP6.Crm.SourceFence/bin/Release/net8.0/CP6.Crm.SourceFence.dll inspect-actual
```

The inspector requires a non-impersonated sysadmin observer because SQL Server otherwise silently filters server, session and Agent catalogs. A filtered inventory must not be reported as an empty inventory. This requirement grants no permissions: use an existing authorized observer. Its administrative privilege is reported as a remaining isolation problem, not evidence that this identity can be fenced by public DENY.

Success is exit 0 and one JSON observation. Errors return exit 2 and a sanitized `C04A_*` code, without connection strings or SQL exception text. Raw reports contain login names/SIDs and session host/program metadata; keep them in an access-controlled local location and publish only a reviewed summary. SQL module bodies, Agent step commands and assembly bytes are hashed inside the process, not returned. Source field values and session SQL text are never selected.

## Captured facts and scope

The identity includes server/machine/catalog, broker/database/family/recovery-fork GUIDs, database creation time in server-local time, and the observer's original login/SID. Broker GUID alone is not proof of a unique restore. The original Core source and any preview Core database require separate observations.

All 20 exact `dbo.Crm_*` tables must exist with supported table types and no extras. The tool retains shared table locks in ordinal order in a serializable transaction and counts all rows. Nonempty tables are reported with a blocker; they do not prevent a read-only diagnostic. The source schema digest covers captured columns/defaults/computed and identity expressions, indexes, FK/check state and relevant table flags. It is a drift digest, not a comparison to the approved column contract.

The security digest covers database owner/trust/chaining, server and database principals, roles and permissions, schema/object ownership and module signatures. The programs digest covers SQL modules including views, database triggers, synonyms, assembly bytes and CLR method bindings, and Service Broker activation settings. The Agent digest covers all instance jobs, owners, enablement, steps and schedules, including command bytes internally. The migration digest covers `dbo.__EFMigrationsHistory` if present.

`ScopeSha256` binds a versioned UTF-8 JSON encoding of the identity, sorted source row counts and five component digests. Observation times and transient sessions are excluded. Internal SQL JSON arrays are ordered; changes in captured IDs, fields, bodies or permissions change the corresponding digest. Setting optional `C04A_EXPECTED_SCOPE_SHA256` requires exact hex SHA-256 matching and rejects drift with `C04A_SCOPE_CHANGED`. Do not automatically replace an expected digest when this fails. A digest is a comparison input, **not a signature, approval record or complete execution plan**.

Other SQL sessions are listed separately with a bounded observation interval. Their reported program/host names are client-supplied hints, not verified process identities, and a session's current database does not enumerate all databases it can access. Locks retain the source counts while metadata is collected. Security/program/Agent/migration/identity are read again to reject observed changes during collection; this is neither an instance-wide atomic snapshot nor protection against a change and reversal between reads.

## Remaining execution requirements

Every report keeps `mutationAuthorized`, `completeWriteFenceVerified`, `writerInventoryComplete` and `fullSourceColumnSchemaVerified` false. It explicitly retains approval binding, privileged-identity isolation, external writer inventory and target-first-write blockers. Encrypted module bodies and enabled unclassified Agent jobs add blockers.

The collected SQL metadata does not fully resolve encrypted bodies, Windows group membership, Agent proxy/credential permissions, server-level triggers, cross-database entry points, external schedulers/services or deployment identities. SQL Agent existence is not proof of an active scheduler, and an empty session/job list is not proof of no writers. Runtime paths such as the Core GDPR tenant purge can reach mapped CRM tables without an explicit `Crm_*` SQL string. Actual execution must classify and isolate those identities and paths, bind the approved source/target/schema/release/calendar scope, and coordinate drain, source freeze, target first write and routing. No report from this command closes C04A or C04B.
