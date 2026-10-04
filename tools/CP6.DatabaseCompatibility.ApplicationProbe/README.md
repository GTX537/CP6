# WP6 ApplicationProbe

Local preparation and state verification for the task-owned application and restore databases. This executable does not create databases, apply migrations, start API processes, run backup/restore, or contact an external transport. Root runs all builds and native acceptance steps.

## Inputs

The first positional argument is `histories`, `enqueue-notification`, `notification-status`, `capture`, `verify`, `signalr-verification`, or `replay-notification`. Every command requires `--provider SqlServer|PostgreSql --report <new-public.json>`. The selected provider reads only its `CP6_TEST_SQLSERVER` or `CP6_TEST_POSTGRES` environment variable. Connections and passwords never appear in command arguments or reports.

The linked [OwnedTestDatabase helper](../../eng/database-compatibility/OwnedTestDatabase.cs) requires `CP6_COMPAT_SCOPE=WP6`, an exact `CP6_COMPAT_DATABASE_NAME`, and `CP6_TEST_DATABASE_OWNER`. Only `Application` and `Restore` roles are accepted. The helper validates the loopback/name/owner contract and reads the actual database marker before any command; notification creation verifies it again before saving. There is no legacy or unselected-provider fallback.

The public report is created exclusively before work. Existing reports are refused. Failure returns exit code 1 and a sanitized failure kind/native SQL code or SQLSTATE; exception messages, native details and inner exceptions are not printed. `--timeout-seconds` is bounded to 10–600 (default 300). Native commands have a 180-second command timeout and receive cancellation. Pressing Ctrl+C cancels the probe.

```text
histories --provider PostgreSql --report <new-public.json>
enqueue-notification --provider PostgreSql --report <new-public.json>
  --tenant <nonempty-guid> --user <nonempty-guid> --event-key wp6-<lowercase-nonce>
notification-status --provider PostgreSql --report <new-public.json>
  --tenant <same-guid> --user <same-guid> --event-key <same-event-key>
  --expect queued --expected-attempts 0 --wait-seconds 0
notification-status --provider PostgreSql --report <new-public.json>
  --tenant <same-guid> --user <same-guid> --event-key <same-event-key>
  --expect dispatched --expected-attempts 1 --wait-seconds 30
capture --provider PostgreSql --report <new-public.json> --state <new-state.private.json>
verify --provider PostgreSql --report <new-public.json> --state <same-state.private.json>
```

`SqlServer` uses the same command shape. The notification tenant/user must already exist and be enabled. Creation calls the real `NotificationService.CreateOutboxAsync`, then the production context's `SaveChangesAsync`; observation uses a fresh context and the exact tenant/user/event key. `queued` means status 0. `dispatched` means status 1, the exact requested attempt count, a persisted dispatch timestamp, no next attempt and no last error. A notification already being dispatched during creation is allowed; use the status command for the explicit worker assertion. Polling is bounded to 0–60 seconds and does not retry or simulate dispatch.

## State capture and restoration

Stop the API and every fixture writer before capture. Restore into the independently owned `Restore` database, then run `verify` **before** restarting its API. Changing processes or workers can legitimately change data. The probe does not suspend writers or filter such changes away.

Capture reads effective migration histories and all persistent user base tables in one read transaction: PostgreSQL `RepeatableRead`, SQL Server `Serializable`. Four reported profiles are checked against actual known/applied migrations with no pending or unknown entries. SQL Server Core owns the IdentityPriority and ERP migrations, so those two effective profiles reference the actual Core history rather than pretending four distinct histories exist.

The table catalog retains ordered column definitions and hashes them. Every actual cell is converted to native bytes: PostgreSQL uses the type's catalogued binary send function; SQL Server uses `CONVERT(varbinary(max), column)`. Nulls have a distinct marker; non-null cells carry a byte length and exact bytes. A row digest covers every column. Sorted complete row digests, including duplicates, form the table digest, with the row count recorded separately. No trimming or textual CLR normalization hides trailing spaces, passwords, DP XML, binary concurrency tokens or generated values. No raw cell is written to a file or report. PostgreSQL partition/inheritance rows are read with `ONLY` per catalogued table, preventing double counting. A zero-column table or unsupported binary send function fails the completeness check rather than being silently omitted.

Sequence definitions/current state are captured as well, including SQL Server identity counters and PostgreSQL `last_value`/`is_called`. PostgreSQL sequences are nontransactional; consistent sequence verification requires all writers to be quiescent. These digests are for a native backup/restore on the same provider and compatible server encoding, not for SQL Server-to-PostgreSQL data migration equivalence. Views, foreign tables, indexes, constraints, triggers and server/cluster configuration are outside this row/column snapshot's schema scope.

Private state contains database identity, catalog metadata, sequence state, history names, counts and hashes, but no raw cells, passwords, connections, tokens or DP XML. Its path must contain a `private` segment or end in `.private.json`; capture requires a new file and verifies its written bytes. Verification reads that same immutable baseline. Restored database name/role can differ; provider, native encoding, table/column membership, exact row counts/content, sequence state and histories must match. Every difference remains a failure, including changes to generated tokens. Table comparisons report only names, counts and hashes for diagnosis.

This verifies preservation of actual rows in the captured tables, including business/permission/message/DataProtection tables when present. It does not prove a business operation, authentication, authorization or DataProtection decryption merely from a digest. Root separately exercises real HTTP endpoints, saved opaque cursors, background-worker startup/restart and recovery against the same published API.

## Real SignalR transport and restored-event replay

```text
signalr-verification --provider PostgreSql --report <new-public.json>
  --base-uri http://127.0.0.1:<owned-api-port>/
  --tenant <tenant-guid> --user <admin-guid> --event-key wp6-<new-signalr-nonce>
replay-notification --provider PostgreSql --report <new-public.json>
  --tenant <tenant-guid> --user <recipient-guid> --event-key wp6-<existing-dispatched-nonce>
```

SignalR requires the three nonempty environment values `CP6_COMPAT_ADMIN_PASSWORD`, `CP6_COMPAT_ORDINARY_USERNAME` and `CP6_COMPAT_ORDINARY_PASSWORD`. The admin username is `admin`. There is no password fallback. Both users must already exist and be enabled in the explicit tenant; the ordinary user must be distinct. Only a literal loopback HTTP base URI with an explicit port and no extra path/userinfo/query is accepted. Root independently checks the actual API PID/listener/database binding. The probe performs real HTTP logins, validates profiles and obtains authentication cookies in memory. Two real `Microsoft.AspNetCore.SignalR.Client` 8.0.12 connections select **WebSockets only** at `/hubs/notify`; no test publisher or transport double is used. Cookies, passwords, token bodies and received payloads are never persisted or printed.

After both clients connect, the actual production notification service commits the target event. The probe receives the exact `WfNotification` ID/user/type on the admin connection and observes actual status 1/attempt 1 in the database. To prove subsequent worker progress, it creates two additional task-owned marker events sequentially. Each is created only after the prior row's dispatched status has committed at the end of its production batch; that marker therefore cannot be in the prior batch's captured candidate list. Each marker must also arrive over WebSockets and persist status 1/attempt 1. This proves two actual later batches; fixed sleeps do not stand in for worker cycles. The ordinary connection receives none of these three fixture IDs, and the original target is observed exactly once throughout. A bounded 45-second polling budget applies per dispatch, under the command's overall cancellation budget (at least 60 seconds, default 300). All subscriptions/clients are stopped and disposed before completion, including failure paths.

The case writes three fixture rows: one target and two markers. Public counts distinguish them. It finally calls the same production service again for the target event, with different title/body input, and requires zero saved changes, one physical event-key row, the original ID and unchanged complete entity hash/state. `replay-notification` performs this last step independently for **any** already dispatched event, including a pending notification that was captured in backup and first dispatched by the restored application's real worker. Replay is allowed for Application and Restore roles and does not generate extra marker rows. These observations prove the bounded local SignalR/user-routing and worker/replay behavior; they do not establish external CRM/email delivery or global exactly-once delivery under all failures.

## Public result contract

`SchemaVersion=1`, `Task=DB-COMPAT-01-WP6`, `Provider=SqlServer|PostgreSql`, `Role=Application|Restore`, `Status=Passed|Failed`. `DatabaseName` is the verified actual database. The report includes fixed named boolean assertions, counts, digests, migration profiles, table summaries, optional restoration comparisons and actual probe/Core/Space assembly hashes. No dynamic IDs occur in assertion names.

| Command | Exact successful assertion names, after the common `owner-and-physical-database-verified` |
| --- | --- |
| `histories` | `four-effective-history-profiles-complete` |
| `enqueue-notification` | `production-outbox-service-committed-one-row` |
| `notification-status` | `notification-one-owned-row`, `notification-status-exact`, `notification-attempts-exact`, `notification-success-persisted` |
| `capture` | `four-effective-history-profiles-complete`, `native-all-tables-captured`, `capture-single-consistent-transaction`, `private-state-created-exclusive` |
| `verify` | `baseline-provider-matches`, `four-effective-history-profiles-complete`, `table-catalog-and-columns-match`, `all-table-counts-and-content-match`, `sequence-state-matches`, `history-state-matches` |
| `signalr-verification` | `signalr-two-http-users-authenticated`, `signalr-two-websocket-clients-connected`, `signalr-target-delivery-exact`, `signalr-other-user-excluded`, `signalr-worker-two-subsequent-batches-completed`, `signalr-target-not-repeated`, `signalr-production-replay-kept-row-and-state`, `signalr-clients-stopped` |
| `replay-notification` | `replay-existing-dispatched-row`, `replay-production-service-no-duplicate`, `replay-kept-original-id-and-worker-state` |

Counts: `EffectiveProfiles=4` for histories/capture/verify; `NotificationRows=1`, `DispatchStatus`, `DispatchAttempts` for notification commands; `Tables`, `Rows`, `Sequences` for capture/verify. Notification hashes are `EventKeySha256`, `TenantSha256`, `UserSha256`, `NotificationIdSha256`. Snapshot hashes are `StateSha256`, `CatalogSha256`, `ContentSha256`, `SequenceSha256`, `HistorySha256`. During verify, `StateSha256` identifies the original private baseline's exact bytes; the other hashes describe the restored database. Assertions include both expected and actual digests/counts where comparisons apply.

SignalR adds counts `LoginProfiles=2`, `SignalRClients=2`, `AdminTargetEvents=1`, `OrdinaryTargetEvents=0`, `SignalRMarkerRows=2`, `AdminMarkerEvents=2`, `OrdinaryMarkerEvents=0`, `CompletedFollowupWorkerBatches=2`. Its additional hashes are `TargetPayloadSha256`, `FollowupBatchEvidenceSha256`, `OriginalNotificationStateSha256`, `ReplayedNotificationStateSha256`; the last two must be equal. Independent replay adds `ReplaySaveChanges=0` and the two equal state hashes to the usual notification counts/hashes.

Prepared source is not evidence of execution. Root must record locked restore/build outcomes and actual command/API/backup/restore results before declaring WP6 acceptance.
