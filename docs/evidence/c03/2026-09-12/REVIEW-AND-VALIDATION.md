# C03 concentrated review and local validation

The complete Core/CRM task received one concentrated review, with security, API/contract, SQL migration/data,
performance, testing, maintainability and adversarial checks. Subsequent work rechecked the findings and their
effects. No unrelated refactor or repeated per-commit full review was required.

| Substantive finding | Final behavior and evidence |
| --- | --- |
| Accepted quote could exceed the existing 500-line order limit | Acceptance and conversion share the ERP limit. Boundary unit regression first failed; final Core run passes. |
| Source quote amount fits `decimal(18,2)` but overflows destination `decimal(21,8)` | Reject line amount at `10^13` before acceptance/conversion. Real SQL verifies `10^13 - 0.01` succeeds exactly and oversized legacy acceptance remains terminal on replay. |
| Product quantity/price units could reinterpret accepted prices and quantities | Exact explicit unit compatibility is required before numbering. Six invalid SQL cases first created incorrect orders; the final run rejects them and preserves matching kg/PCS units and amounts. No conversion or alias map is invented. |
| Concurrent BP binding surfaced an index collision as HTTP 500 | Only the exact Account-binding unique-index violation becomes the existing stable `C03_ACCOUNT_ALREADY_BOUND` conflict. Concurrent SQL verifies one winner and unchanged losing row/audit. |
| Removed tenants could occupy the global retry batch indefinitely | The worker reads due batches independently for registered tenants. Real SQL first observed zero active-tenant progress, then verified fair progress and untouched removed-tenant receipts. |
| CRM token refresh blocked unrelated tenants, and headers-only timeout left body reads unbounded | Independent tenant locks and one bounded deadline cover lock/token/response body; caller cancellation remains cancellation. Delayed-body and blocked-tenant regressions passed. |
| Contract-valid colon keys were rejected by CRM reads | Keys are consistently validated and URL-encoded; reader regressions pass. |
| Permission regression expected the old controller/attribute counts | Explicit route/method/menu/action assertions cover all four new ERP authority operations; seed permission tuples are unchanged. |

Final local executions: **95 SQL cases**, **153 Core unit/HTTP cases**, and **114 CRM ERP unit cases**, all passed
with zero failures or skips. [local-test-results.json](local-test-results.json) contains allowlisted case outcomes,
original timestamps and hashes of unchanged retained TRX files, including failures. Raw TRX remain local because
they include machine paths. The [SQL suite](../../../../eng/crm/erp-integration-tests/README.md) explains commands,
isolated database ownership and earlier scoped evidence.

The trusted-main documentation gate required a preparatory exact-set registration, delivered by
[Core PR #104](https://github.com/GTX537/CP6/pull/104) at `cee28b8faecb2e0c839c3e39f2d62dbc48a4f1a2`.
Its 174 disclosure tests and 59 PRD negative regressions passed locally. It registers the exact engineering guide/index
from documentation commit `6cc6e10fae6b097735a94645d00af68b6cc5f989`; frozen product payloads and whole-set matching remain intact.

The unit/master changes were independently rechecked and the original P1 was closed. The
[fresh seven-case transport attempt 4](transport-attempt-4/README.md) passed against actual published Release binaries,
including final quantity/price/unit SQL reconciliation. [Local publication evidence](local-release/README.md) binds
864 unchanged published file hashes and three additional actual CRM API HTTP checks. The earlier
[attempt 3](transport-attempt-3/README.md) remains unchanged historical execution evidence. Remote-main delivery
requires separate merge and ancestry/tree verification.

The existing per-line ERP product/process/material queries are bounded by 500 lines but have no large-history/throughput
production performance acceptance. Batch optimization remains a follow-up. CRM Account/ErpLink/IntegrationProcess and
Opportunity/Won product transitions belong to CRM05/06/07; the transport probe does not implement them. Real WMS/MES
downstream effects, CRM12 user journeys, C04A/B migration/cutover and R2 production candidate/deployment gates remain open.
