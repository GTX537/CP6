# C03 actual ERP transport: attempt 3

The seven cases ran on **2026-09-12, 08:42:50–08:44:04 UTC**; the report was finalized at 08:44:07 UTC.
The runner exited 0. [summary.json](summary.json) and [junit.xml](junit.xml) record **7 passed, 0 failed, 0 skipped**.

| Executed case | Result |
| --- | --- |
| Actual ERP SQL, C01 service grants and authenticated Dapr reads | Pass |
| CRM probe Outbox to real ERP business partner and result Inbox | Pass |
| Order commit survives a lost transport acknowledgment without a duplicate | Pass |
| Terminal errors and authentic results delivered out of order | Pass |
| Persisted CRM probe Outbox recovers after the owned Kafka broker restarts | Pass |
| Owned SQL storage fault, deadletter and audited original-message replay | Pass |
| Canonical ERP/probe SQL reconciliation | Pass |

The [ERP baseline](baseline-erp-sql.json) and [final ERP snapshot](final-erp-sql.json) show the actual isolated SQL state.
There are **3 real ERP orders**: 2 JPY orders with line amounts totaling **JPY 600**, and 1 USD order totaling **USD 300**.
The currencies are reconciled separately. The lost acknowledgment caused two deliveries of the original request and
one order. Both Outboxes drained; ERP command receipts completed. The [probe baseline](baseline-crm-probe-sql.json)
and [final probe snapshot](final-crm-probe-sql.json) preserve request, Inbox and received-result metadata.

Database initialization and executed binaries have separate identities. The ERP snapshots' unchanged `authority`
records initialization at **08:36:08 UTC**, 134 applied migrations, the database baseline hash and the API/Core assembly
hashes used when the database was prepared. The successful report's `observations.runtime-binaries` records the actual
API/Core/fixture hashes executed for this attempt; its top-level fields also record runner, CRM infrastructure and
contract hashes. Initialization and execution assembly hashes differ and must not be treated as interchangeable.
The report explicitly identifies working-tree runtime code. Caller-supplied source SHAs are context, not immutable
release identities, and these records do not certify later source changes or production image digests.

Earlier failures remain unchanged in [failed attempt 1](../failed-attempt-1/README.md) and
[failed attempt 2](../failed-attempt-2/README.md). The first exposed CSRF handling of the Dapr callback; the second
exposed missing component inbound retry for a subscription with a deadletter topic. Attempt 3 includes the corrected
callback boundary and a Dapr retry policy with a constant 1-second interval and `maxRetries: 5`.
Dapr documents that a deadletter topic without a retry policy can receive a failed message immediately;
see [Dead Letter Topics](https://docs.dapr.io/developing-applications/building-blocks/pubsub/pubsub-deadletter/).

This is real transport through an **isolated, seeded ERP fixture and CRM SQL transport probe**. The CRM product API,
Accounts, Opportunities and Won workflow are not exercised. CRM06/CRM12, user-journey, UAT and production acceptance
remain outside this evidence. The replay case invokes the real service through the fixture CLI; it does not establish
HTTP operator authorization. WMS/MES bridges remain pending, and their downstream effects are not exercised.

The six JSON/XML files are byte-for-byte copies of `public-all-attempt-3`. Each was inspected for credentials,
connection strings, raw event bodies, real personal data and machine paths before copying; none were found.
Only fixture identifiers, status/version/hash metadata and allowlisted ERP business columns are included.
[SHA256SUMS](SHA256SUMS) covers the copied reports and archive metadata. Local Git attributes preserve report bytes.
