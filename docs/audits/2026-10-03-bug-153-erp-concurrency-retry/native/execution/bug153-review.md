# BUG153 task review

Reviewed the complete task diff in `D:/CP6/tmp/worktrees/bug-153-erp-concurrency-retry` relative to `cbbb7fc8e99290f6aba7589830a98a98726280e9`: the two OrderSqlTests callers/new delivery helper, audit README, four project-memory additions and exact audit-native attributes. Read unchanged SqlFailureProbe, ErpScenario and the relevant ErpRequestHandler retry boundary as context. No source edits, .NET/test execution, database access, credentials, remote operation or broader scan was performed by this reviewer.

## Conclusion

No unresolved substantive blocking or nonblocking finding remains in this task scope. A documentation precision suggestion was raised and resolved before this final review: the README now limits its unknown-error rejection claim to native database errors, notes SQL 3903 companion rollback codes, and explicitly excludes proof of all managed unavailable causes. This wording correction changes no tested source input.

## Functional assessment

- The first delivery batch remains genuinely concurrent. Each retry invokes the real handler through a fresh Handler call using the original envelope payload/topic/partition; messages are not regenerated, identifiers are not changed, and no failed SQL statement is retried in an aborted transaction. The unchanged handler owns rollback/disposal and its transport RetryScheduled contract.
- A batch needing retry first requires an observed typed native transaction conflict. The unchanged observer rejects other native provider codes; SQL 3903 is its documented cleanup companion allowance. This is not a generic exception-swallowing loop. As documented, the first-chance observer does not identify every managed unavailable cause, and this test does not claim that broader capability.
- At most five bounded replay rounds follow, only for RetryScheduled results and its existing C03_ERP_UNAVAILABLE/C03_RETRY_NOT_DUE codes. Advancing the fixture's production-injected clock by nine seconds exceeds its configured eight-second maximum backoff. It does not fabricate an operating-system wait or claim an actual external broker delivery.
- Applied/Duplicate are the only accepted final dispositions. Unknown native failures, other dispositions and exhausted retries fail. All original single order/detail, unique bridge/request and terminal-journal assertions remain intact; each distinct original message additionally requires a persisted Processed Inbox and byte-identical payload. The loop neither resets tables nor broadens a business predicate.
- No production handler, public interface, schema, migration, dependency or workflow changes occur. State documents distinguish the verified local bug scope from pending delivery and unfinished WP6; legacy historical sections remain. The attributes change is limited to preserving raw bytes under this bug's native audit path.

## Actual evidence inspected

Root executed these runs; this reviewer only read their public result metadata and TRX:

| Run | Actual result | Result SHA-256 | TRX SHA-256 |
| --- | --- | --- | --- |
| bug153-pg-targeted-green | 2/2, no skip; both cases observed native 40001; exit 0 | 2F76595D0647B3BBC7CF621DADBFDAD443F717DC23016F981F23DEBBB756A70A | F536D0DF9B809006559FC24FCF1A9246EDA9A51767D6D42A9990E035CC855FCF |
| bug153-pg-erp-full-green | original 95/95, no skip, exit 0 | 7CBF6DC3251119F8B0511C7DF554BDC75D8E482A66E8ECF19D864A6C54E22D06 | DF03365A99E69ACB0C0E066C1BD6B4F81DD632E65CD67AE257580DB34F9AFBB7 |
| bug153-sql-erp-full-green | original 95/95, no skip, exit 0 | CBDFF77DAB773DDC6E8D44EDF3FC6C69CFE5558A3C5159BF4F86FB3E010EECFD | 999EA628DFDFCB367D6EDE924D9D4151EC64EB4A86CCA34E4CE3EEE9B84D5D22 |

All three result source hashes equal the reviewed OrderSqlTests SHA-256 `29FF304B2B719A05A1E8E8368FC47844851C7B6950B6097D9E6442FBB576CEBD`; their recorded TRX hashes match actual files. Both full-run display-name sets exactly equal the original 95-case required manifest, without extra/missing names. The targeted two are a subset, not two additional unique business cases. The preserved prior diagnostic TRX is 1 passed / 1 failed, zero skipped, native 40001 observed (SHA `0DD95F6E79FC9296F9F497E61EFF92401BE3F5E1F5C68F4F6EBC6B68ED0BFFA1`); no earlier failure is rewritten.

`git diff --check` returned 0 (existing LF/CRLF notices are not functional failures). Evidence archiving, commit/normal PR, remote-main containment, cleanup and post-merge smoke remain root's delivery steps; this report does not close Issue153 or assert WP6 completion. No new complete review/test cycle is requested for the resolved wording change.

## Final reviewed source fingerprint

RecordedUtc: 2026-10-03T16:45:01.1758701Z. Files: 7. Sorted path|bytes|SHA256 UTF-8, LF between rows/no final LF fingerprint: 5711459D90D9B8B972B8B29C55CE461D267C2D738C29F081E69B693BD6F1502A.

| Path | Bytes | SHA-256 |
| --- | ---: | --- |
| .gitattributes | 7524 | 2F8FDD0C3B17A8FE6C89DF7267D7BDA75EE8CC1F6860938030DE8DB3AA533DA5 |
| docs/audits/2026-10-03-bug-153-erp-concurrency-retry/README.md | 2605 | 6090CD1E08E7EE98F216748A14256CC1AD77021801157E549F0381A6D924DE6B |
| docs/project-memory/05-Completed.md | 260799 | 20CC4642AE0A2D0BBB92D049813BAC3F9DCD15444CDE58871D1FE2EC6ACC035F |
| docs/project-memory/06-Todo.md | 123347 | 96AD94D7DDC7244BEC67E2C0661438157C56C9B3A1F205DF3A0C9BC311E18A0C |
| docs/project-memory/CHANGELOG-AI.md | 239484 | 1D2478347960946F17E4BE33EB831CDE1C5E08D017C2E9744490848374E896D7 |
| docs/project-memory/PROJECT_STATE.md | 410818 | 5C504C39FEE9E398155B6DB8C74F6A211ECFB569A56113312907DDC30876450D |
| eng/crm/erp-integration-tests/OrderSqlTests.cs | 24872 | 29FF304B2B719A05A1E8E8368FC47844851C7B6950B6097D9E6442FBB576CEBD |
