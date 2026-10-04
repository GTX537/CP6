# WP6 fixture/results review: scoped P2 follow-up

The initial independent review remains unchanged: `D:/CP6/tmp/wp6-review-fixtures-results.md`, SHA `C18EA7CD9B5F5FF1EB2C5221DF80E87A9CF8E41283640649B81058256B006836`. This is an incremental correction/validation record, not a second complete review or a claim of full WP6 acceptance.

Root authorized the reviewer to correct the explicit-WP6 fixture selection gap. The new `OwnedTestDatabase.IsRequestedForRole` selects a matching role without the old aliases, keeps valid unrelated roles inactive during filtered discovery, and keeps a missing/invalid modern name required. Final strict provider/name/owner parsing and actual task/owner verification remain unchanged. Space, SpaceHistory, CoreWms (Core/WMS), Reports and the additional legacy ERP `SqlDatabaseFixture` selector now use that predicate. No production business handler, unchanged ERP95 test body, AppProbe, migration gate, build dependency or legacy schema workflow is modified by this correction.

The original unselected legacy branches remain; no global scope OR indiscriminately activates all Space migration attributes. Independent executable test inputs call the actual fact selectors/fixture constructors or their no-configuration initialization boundaries and reject omissions before any connection. Environment changes are confined to nonparallel test collections and restored in Dispose. The new inputs are selection/unit contracts, not live database or physical owner verification evidence.

Frozen source: `D:/CP6/tmp/wp6-selector-partial-config-source-inputs.json`, SHA `5F2931B3EB38FF8AEE6B6143CE7C132A3BD9E9FC5986D682EE780115A0596FFA`. All 11 recorded source-file hashes still match; shared helper SHA `98625EA55407AA8168F008383F3EFC4010D29E79D6A1E00580F4908CA4AD23BA`. Static `git diff --check` returned zero.

Root serially built consumers and executed the following real .NET test processes. This agent only read the public result metadata and original TRX after execution; no .NET or database operation was executed by this agent.

| Root run | Result / exit | Public result SHA-256 | Original TRX SHA-256 |
| --- | --- | --- | --- |
| wp6-selection-core-green | 58/58, zero skip / 0 | 3817E8C1F2E5E8164DB82ACC0097F6E2BF48D3BB306B47D6D0D3C529DD71363C | 3D4E8B52AABD88F7C1F64D3DCBDBD40B481D4112093D9C371553595D28BE426A |
| wp6-selection-space-green | 4/4, zero skip / 0 | 56DC89D988E0D45319ABF150A3339497E9FEE8AD5507656BA5B8C03586700847 | 5331D496C228C7DD4288D4D87611EE4689216F7B4B19FD81A821ABCA84D20738 |
| wp6-selection-erp-green | 4/4, zero skip / 0 | 38B35A725DA0B46404C48C6CC655966A54D90BD17CA18ED31140C16448E501D0 | E2A9A5C3ADC17B2422A62E74B75D8EBFE517EC79FC31B4C3C40285AC9CDE4AB7 |

Reports are `D:/CP6/tmp/<run>/result.json`; TRX files are `D:/CP6/tmp/<run>/private/trx/results.trx`. All report InputManifestSha256 values and recorded TrxSha256 values must bind the frozen input/original TRX. Inspected actual TRX counters show no failed, notExecuted, skipped or other nonpassing result. The 66 executed cases comprise the prior 45 helper cases rerun plus 21 new helper/fixture selection cases; they are not 66 newly introduced business cases.

The scoped P2 is corrected and locally validated. The initial scope's other code findings remain unchanged. Root's full application/matrix, SignalR/restore worker, SQL136 forward upgrade and delivery evidence remain separate; neither this note nor the selector tests declare WP6 or Issue134 complete. Root retains responsibility for independent inspection of the correction and final source/evidence applicability before delivery.
