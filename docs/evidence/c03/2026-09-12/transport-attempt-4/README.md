# C03 actual Release transport: attempt 4

This independent attempt ran on **2026-09-12**, completing at **09:54:56 UTC**, against freshly published Release
binaries and a newly initialized isolated ERP SQL database. The runner exited 0. [summary.json](summary.json) and
[junit.xml](junit.xml) record **7 passed, 0 failed, 0 skipped**.

It exercised actual C01 service grants and Dapr reads, BP promotion, order commit with lost acknowledgement,
authentic out-of-order results and terminal errors, an owned Kafka restart, an SQL storage fault followed by
audited original-message replay, and final ERP/probe SQL reconciliation.

[Final ERP SQL](final-erp-sql.json) contains **three unique orders**, grouped as **JPY 600** and **USD 300**.
All three lines preserve quantity **2 PCS**, individual price **150 per PCS**, and amount **300**. Both Outboxes
drained and all ERP command receipts completed. [The baseline](baseline-erp-sql.json) records 135 actual Core
migrations through `20260912085508_CrmErpDeliveryReplayAudit` and database baseline SHA-256
`bcb431cc98c27da4cafabaa91eb2233f9ac88c9cd88f9db243c1a392f61c97b1`.

Execution used Core source `a51938f8ab055e54ebbedfec3b810e44c82e1020` and CRM source
`b3adb17dd0087c99c2d82a759d560d4bbf79a7ed`. These were committed source inputs, with no uncommitted source changes
when published. The runner's generic `sourceState` label still says working-tree runtime; this run selected the
published API and fixture through explicit runtime paths. The report records the actual executing assembly hashes.
[Local publication evidence](../local-release/README.md) records all 864 published file hashes and actual CRM API HTTP checks.

This fresh execution includes the concentrated-review unit and retry fixes. Earlier
[attempt 3](../transport-attempt-3/README.md), [failure 1](../failed-attempt-1/README.md) and
[failure 2](../failed-attempt-2/README.md) remain unchanged. No previous result was relabeled as a rerun.

The runner stopped its owned processes, removed its Compose project/volumes and dropped its CRM probe database;
the ERP fixture database and private diagnostics are retained for inspection. Existing previews were untouched.
The six JSON/XML reports are byte-for-byte copies of this attempt's output, inspected for private values and machine
paths before publication. [SHA256SUMS](SHA256SUMS) identifies the archived bytes.

This proves isolated ERP SQL/API and CRM transport-probe behavior. It does not start the CRM product journey or
implement Account/ErpLink/IntegrationProcess/Opportunity/Won transitions. Product master inputs are fixture seeds;
product approval and WMS/MES downstream effects are not exercised. CRM06/07/12, C04A/B and R2 production candidate,
UAT and deployment requirements retain their own acceptance gates.
