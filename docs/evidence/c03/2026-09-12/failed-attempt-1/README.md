# C03 actual ERP transport: failed attempt 1

The cases ran on **2026-09-12, 08:18:12–08:20:28 UTC**; the report was finalized at 08:20:31 UTC.
The unmodified [summary](summary.json) and [JUnit report](junit.xml) record **1 passed, 1 failed, 0 skipped**.
Execution stopped at `crm-outbox-to-real-erp-business-partner-and-result-inbox`; the remaining five full-scope cases
were not executed and are not counted as passes.

The investigation identified CSRF rejection of the actual Dapr callback. The callback boundary was corrected before
the next attempt. This public report preserves the failure outcome; private diagnostics are not included.
[Attempt 2](../failed-attempt-2/README.md) records the subsequent retry-configuration failure, and
[attempt 3](../transport-attempt-3/README.md) contains the later seven-case success.

Only the original `public-all-attempt-1/summary.json` and `junit.xml` were copied, without modifying their bytes or results.
Both were inspected for credentials, connection strings, raw event bodies, real personal data and machine paths;
none were found. [SHA256SUMS](SHA256SUMS) covers these reports and archive metadata; local Git attributes preserve bytes.
This failed attempt establishes neither complete transport acceptance nor CRM product, Won, UAT or production acceptance.
