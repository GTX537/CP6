# P10 S06 Operator Documentation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans inline. No delegation.

**Goal:** Document the implemented verifier, the three real workflow entry points and the remaining acceptance boundary without turning local test results into a Frozen claim.

**Architecture:** A reference page owns CLI, credentials and immutable identities. A separate how-to owns validate/publish/audit operation and failure handling. DevOps navigation and the four project-memory ledgers link to those pages. Historical dated prerequisites stay intact; the active Todo replaces the obsolete next-step instruction.

**Tech Stack:** Markdown and the existing read-only documentation link checker.

---

## Tasks

- [ ] Write docs/devops/P10-PLATFORM-REFERENCE.md from the actual Program, three command classes, release/workflow profiles, NuGet/trust files and three YAML files. Cover all ten command forms; distinguish local inspection, hosted preparation, publication and completed verification. Do not document copied Platform schemas or private credential values.
- [ ] Write docs/devops/HOWTO-P10-PLATFORM-CANDIDATE.md with exact gh commands for current-main validation, metadata-based artifact selection, separate publication, read-only audit and final append-only ledger retention. State that commands need a merged/verified main and owner approvals; they have not run for this candidate.
- [ ] Add two rows in docs/devops/README.md. Keep GitHub R2/GHCR authority and WMS/Azure boundaries unchanged.
- [ ] Add scoped implementation/pending-acceptance entries to docs/project-memory/PROJECT_STATE.md, 05-Completed.md and CHANGELOG-AI.md; replace only the active stale P10 next-step bullets in 06-Todo.md. Record exact corrected 0.10.1 source/publication/CRM pins, self-signed trust meaning and local 1,528-test result. Preserve other tasks and historical dated entries.
- [ ] Run python -X utf8 scripts/check-docs-links.py on both new pages, docs/devops/README.md and all four touched ledgers. Expected: all selected local links resolve. Check shell snippets against the actual workflow input names and CLI source.
- [ ] Run git diff --check and a staged credential/private-material scan; inspect the complete eight-file diff, then stage only these eight files and commit docs(p10): document candidate operation and pending acceptance.

No credential is provisioned or exported. No workflow is dispatched by writing this documentation. No R2 or GHCR write, production deploy, approval or Frozen decision is inferred. The next execution step is full-branch review and required integration checks.

## Execution evidence — 2026-09-08

Implemented the two new reference/how-to pages, DevOps navigation and all four ledgers. The local checker verified seven Markdown files and 72 local file links with zero errors. All four PowerShell blocks parsed without errors; workflow file/input names and all ten CLI command forms were checked against actual source. Dispatch commands were not executed as a documentation test. Scoped review and the credential/private-key/presigned-URL pattern scan found no private material. Historical dated facts and other task records were preserved. The unavailable document-generate interaction dependency was not worked around by changing tool/platform settings; existing repository conventions and the approved scope were followed directly.
