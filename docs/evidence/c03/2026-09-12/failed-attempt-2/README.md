# C03 actual ERP transport: failed attempt 2

The cases ran on **2026-09-12, 08:31:40–08:34:04 UTC**; the report was finalized at 08:34:08 UTC.
The unmodified [summary](summary.json) and [JUnit report](junit.xml) record **2 passed, 1 failed, 0 skipped**.
Execution stopped at `order-commit-survives-lost-transport-ack-without-duplicate`; the remaining four full-scope cases
were not executed and are not counted as passes.

After the [attempt 1 CSRF fix](../failed-attempt-1/README.md), the lost-ack case exposed the missing Dapr component
inbound retry policy. A configured deadletter topic alone does not provide retry-before-deadletter delivery;
see Dapr's [Dead Letter Topics](https://docs.dapr.io/developing-applications/building-blocks/pubsub/pubsub-deadletter/).
The next attempt added a constant 1-second inbound retry policy with `maxRetries: 5` for `cp6-kafka-pubsub`.
[Attempt 3](../transport-attempt-3/README.md) records the later seven-case success. Private diagnostics are not included.

Only the original `public-all-attempt-2/summary.json` and `junit.xml` were copied, without modifying their bytes or results.
Both were inspected for credentials, connection strings, raw event bodies, real personal data and machine paths;
none were found. [SHA256SUMS](SHA256SUMS) covers these reports and archive metadata; local Git attributes preserve bytes.
This failed attempt establishes neither complete transport acceptance nor CRM product, Won, UAT or production acceptance.
