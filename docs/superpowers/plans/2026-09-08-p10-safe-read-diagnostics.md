# P10 safe read diagnostics implementation plan

> Execute inline with executing-plans; the user has requested no subagents.

**Goal:** Locate failed GitHub reads without disclosing credentials, URLs, response data or weakening acceptance.

**Architecture:** Keep `github-http-status` and nonzero exit unchanged. Emit a bounded stderr diagnostic containing a fixed target category and numeric status at the HTTP rejection boundary. Emit fixed preparation-stage markers before each awaited collection operation. Neither diagnostic is release evidence.

**Tech stack:** .NET 8, xUnit, existing fixed GitHub transport and protected GitHub Actions.

## Tasks

- [ ] Add `SafeReadDiagnosticsTests.cs`: capture stderr in an exclusive xUnit collection; exercise actual non-200 `HttpResponseMessage` rejection with hostile headers/body/reason text and assert only `p10-github-read target=unspecified status=403`. Check cancellation remains silent. Exercise the real client with an intentionally invalid, non-secret token to assert a bounded target/status line. Assert preparation stage markers surround the existing ordered collectors.
- [ ] Run `dotnet test tools/p10/ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~SafeReadDiagnosticsTests`; confirm assertion failures against the current implementation, not build errors.
- [ ] Add fixed `DiagnosticCategory` classification to `GitHubReadTarget`; no path interpolation enters output. Pass the selected target from `GitHubReadClient` to the optional diagnostic context on `GitHubWirePolicy.ReadResponseAsync`. Before the unchanged HTTP exception, write `p10-github-read target={fixed-category} status={invariant-integer}`. Do not read headers, reason, request metadata or body for this diagnostic.
- [ ] Add literal `p10-validation-stage ...` stderr markers immediately before current-workflow, platform-source, formal-packages, crm-consumer, publication-archive, package-provenance, final-current-workflow, and after successful local output creation (`prepared`). No success marker precedes its completed operation.
- [ ] Add category coverage for every existing target factory and status coverage for 2xx-not-200, redirects, authorization errors, rate limit and server errors. Keep stdout, original exception code, inner exception and cancellation behavior unchanged.
- [ ] Run targeted tests, full real-input verifier suite and `dotnet format ... --no-restore --verify-no-changes`. Review complete base diff, synchronize four project ledgers and operator how-to with actual failed run 34229610628 and pending hosted diagnosis.
- [ ] Commit only scoped files, push task branch, open normal PR, require all PR checks, merge without bypass, fetch and verify remote main ancestry, run merged smoke and require exact-main CI.
- [ ] Dispatch one new exact-main P10 validation and ask owner to approve. Do not rerun old source, self-approve, modify secrets/permissions, deploy, or claim P10 Frozen.

## Evidence boundary

Local execution: RED 12 assertion failures / 1 pass against unchanged production code; initial GREEN 13/13. Expanded transport/process/wiring regression 175/175. Full suite with actual formal package/signature/GitHub inputs 1634/1634, zero skipped; format verification passed. Reviewed fixed-label classification, no raw response diagnostic, unchanged stdout/nonzero failures and stage order. User-requested inline review found no merge-blocking issue. PR/main/hosted steps remain pending.

Run 34229610628 at main 96c5f71e3c493c61a09d5b667dc32b966508631e failed in preparation with only `github-http-status`. SDK/tools/restore/build succeeded; tests and all image/publication steps were skipped. Local owner-authenticated reads establish the pinned CRM archive exists, not that the Environment token can read it. No particular HTTP status or failed repository is established yet.
