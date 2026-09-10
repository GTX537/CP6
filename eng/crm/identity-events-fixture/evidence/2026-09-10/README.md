# C02 Core local component evidence

The final batch passed **23/23**, with no failures or skipped cases. It used actual SQL Server forward migrations, Core business/grant services and real HTTPS Discovery/JWKS/service-token/controller requests. The fixture created and removed its own uniquely named database. No deployed database or development stack was changed.

These are original working-tree reports, not remote-main or cross-repository acceptance. The final assembly SHA-256 is `9737b19b9a0c7995ef0196a303d9bd6400216fae62e55e045bfa26d8637040e2`; the contract index is `24df72e9446723fa4cf3ad12b7e273ff937ef9b0ed154b8c926b1bac6c7eab63`. The source commit containing this directory freezes the reviewed producer and contract. Later documentation changes reuse the existing results.

All attempts are preserved without editing their JSON/XML bytes. Smaller successful batches demonstrate only their listed scope. Cases not reached after a failure do not count as passed or as completed acceptance.

| Attempt | Executed | Passed | Failed | Result |
| --- | ---: | ---: | ---: | --- |
| 1 | 6 | 5 | 1 | failure |
| 2 | 6 | 5 | 1 | failure |
| 3 | 13 | 13 | 0 | success |
| 4 | 15 | 14 | 1 | failure |
| 5 | 21 | 21 | 0 | success |
| 6 | 2 | 1 | 1 | failure |
| 7 | 2 | 1 | 1 | failure |
| 8 | 2 | 1 | 1 | failure |
| 9 | 22 | 21 | 1 | failure |
| 10 | 2 | 2 | 0 | success |
| 11 | 2 | 1 | 1 | failure |
| 12 | 23 | 23 | 0 | success |

Early fixture failures exposed an EF `OUTPUT`/SQL trigger incompatibility in fault injection, an incorrect fixture property name, duplicate test controller registration and a Windows TLS certificate storage issue. Fault injection now uses a tenant-scoped SQL CHECK constraint in the owned database. HTTP client Basic values are form-encoded before Base64 encoding. Private exception details remain outside the repository.

The caller-owned transaction case found a production defect: after catching a rejected event, a caller could commit a business-only write. Core now saves and restores a savepoint around the entire command. Async rollback ignores caller cancellation. Unsupported savepoints fail before writes; C02 activation rejects MARS. The final batch covers both supported savepoint rollback and the unsupported-connection rejection.

Other completed local validation: 123 focused tests across the identity contract, change-capture, revocation and affected service-token classes; contract-index consistency; workflow restore-command checks; Linux Docker build with locked signed-package restore and BuildKit secret isolation. Those outputs are retained in the task's local audit directory. Existing unrelated compiler warnings were not changed. The image was neither pushed nor deployed.

Still open: Actions `GITHUB_TOKEN` package ACL, required protected-main integration, CRM projections and actual management-request denial, Dapr/Kafka replay/crash recovery, priority backlog progress and the separate real-time 30-second propagation samples. No Actions rerun or indirect trigger is authorized under the user's current budget policy. Successful local component evidence does not close C02.
