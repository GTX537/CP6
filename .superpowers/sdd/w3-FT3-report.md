# Task F-T3 Report -- gstack QA harness (write-only) + DoD self-check

**Branch:** `feat/wfs-event-trigger`  **Date:** 2026-07-13  **Task:** 14/14 (final).
**Deliverables:** `docs/superpowers/qa/wfs-flow-trigger/{README.md, seed.sql, qa_flow_trigger.ps1}`.
**STATUS of harness:** written, **not run** (live QA runs later with the user present).

## Scenario <-> code cross-check

Every assertion was verified against the *shipped* controllers/services (not plan text).
Several plan anchors had drifted; the table cites the authoritative file:line.

| Brief scenario | Harness assertion | Verified against (file:line) |
| --- | --- | --- |
| 5 message: create returns one-time key | `data.{id, apiKeyPlain}`, plain only on create | `FlowTriggerAdminController.cs:47-48` (Ok2 `{id, apiKeyPlain}`); `FlowTriggerAdminService.cs:66-76` (plain only for Message) |
| 5 message: fire 201 new / 200 replay | raw body `{instanceId}`, 201 vs 200 | `FlowTriggerFireController.cs:59-61` (201 new, 200 `Replayed`); `FlowTriggerService.cs:79-80,101` |
| 5 message: wrong key 401 | `X-Api-Key` mismatch -> 401 | `WfTriggerApiKeyAttribute.cs:48-52` (Verify -> 401) |
| 5 message: missing Idempotency-Key 400 | header absent -> 400 (after key check) | `WfTriggerApiKeyAttribute.cs:55-61` (checked AFTER key verify) |
| 5 message: 65KB body 400 | oversize -> 400 (inside controller) | `FlowTriggerFireController.cs:21,36-42` (`MaxPayloadBytes=64*1024`, Content-Length + byte-count) |
| 5 message: disabled == unknown 404 byte-identical | both `{code:404,message:"trigger not found"}` | `WfTriggerApiKeyAttribute.cs:31,42-46` (single `NotFound404` for both) |
| 5 message: whitelist drops non-schema | `secret` dropped; 201 | `WfTriggerVarsMapper.cs:22-32` (FilterBySchema); `FlowTriggerFireController.cs:48-49` |
| 6 key reset: old 401 / new 201 | reset -> new plain; old key fails | `FlowTriggerAdminController.cs:69-75`; `FlowTriggerAdminService.cs:106-116` |
| 4 event: echo linkage + idempotent replay | `matchedCount/firedCount`; no 2nd fire row | `WfTriggerEchoController.cs:20-29`; `WfTriggerBridgeHook.cs:53-63` (idem key `{eventId}:{trig.Id}`); `FlowTriggerService.cs:79-80` |
| 4 event: varsMap `$.OutboundNo` -> `outboundNo` | DB check note | `WfTriggerVarsMapper.cs:10-19`; `ServiceVarsHelper.cs:62-64,151-181` (dot path) |
| 2 manual test-fire timer | `manual-fire` 200 `{instanceId}` + success fire row | `FlowTriggerAdminController.cs:77-89`; `FlowTriggerAdminService.cs:118-129` |
| 8 fire-log failure (disabled starter -> E-WF-022) | 400 + fire row Error `E-WF-022`, InstanceId null | `FlowTriggerService.cs:84-85,202-207` (runtime double-check -> FailFireAsync) |
| 3 timer short-cycle self-fires | `NextDueUtc<=now` scan; <=90s | `WfTriggerWorker.cs:9` (30s); `FlowTriggerService.cs:144-155`; `WfCronHelper.cs:14-25` |
| 7 save validation cron E-WF-022 / disabled flow E-WF-023 | create -> 400 with code in body | `FlowTriggerValidator.cs:31-32,63-64`; `FlowTriggerAdminController.cs:43-51,29` (Err = `{code,message}`) |
| 1 cron preview (5 rows) / invalid 400 | preview 5 future; bad -> 400 E-WF-022 | `FlowTriggerAdminController.cs:95-100` (View perm); `WfCronHelper.cs:39-51` (PreviewUtc count=5) |
| 1 admin page three types + one-time key modal | manual browser (README sec 5) | i18n `I18nOaFlowTriggerScreenSeed.cs:33-36` (keyOnce/keyCreateHint/resetKeyConfirm) |

### Route drift confirmed and honoured
- Admin controller lives in `Controllers/Oa` (`api/oa/flow-triggers`), GETs `[Authorize]`-only,
  writes need `FlowTrigger.Edit`, cron-preview needs `FlowTrigger.View` -- so the ps1 caller
  `qa_wtr_starter` is seeded **RoleId=1** (the role `FlowTriggerPermissionSeed` grants to).
- Message `/fire` and Echo live in `Controllers/Integration` (moved off the OA/WF permission
  guard scan face); `/fire` is `[AllowAnonymous]` + api-key filter; Echo is `[Authorize]`.
- Message `/fire` returns **no envelope** (raw `{instanceId}`); admin endpoints do -- the ps1
  reads `.instanceId` vs `.data.*` accordingly.

### Concurrency honesty
No exactly-once concurrency script is included. Per the A-T2 ledger the message/event
`FireAsync` carries a real double-submit window (two truly-concurrent same-key calls can both
submit); README section 4 records this and states any future concurrency test must assert
"no duplicate after the race resolves", not exactly-once.

## DoD checklist status (harness-scope items; full-wave DoD is the controller's)

| DoD item | Status |
| --- | --- |
| gstack QA harness present (8 scenarios: README + seed.sql + ps1) | DONE -- 7 scenarios HTTP-covered in ps1 + scenario 1 browser (sec 5); isolation DB `CP6DB_OA`; STATUS: written not run |
| Harness structure mirrors `wfs-kernel-hardening` three-file template | DONE |
| Zero code changes (docs-only task) | DONE -- `git status` shows only the 3 new files under `docs/superpowers/qa/wfs-flow-trigger/`; no `.cs/.ts/.vue` modified. Full suite untouched, **not re-run** (nothing to re-run). |
| Scenario API paths/status/assertions cross-checked vs actual controllers/services | DONE -- table above, each with file:line |
| Live QA (user present, isolation DB) | PENDING -- runs later (Step 3), out of this write-only task's scope |

## Concerns / notes for live QA
- Scenario 3 requires the `WfTriggerWorker` hosted service running (normal boot); if absent the
  ps1 emits WARN, not FAIL.
- Whitelist (5f) and varsMap (4) value assertions are DB-check notes (VarsJson is not exposed
  over an HTTP read) -- SQL provided in README section 6 and echoed by the ps1 summary.
- `qa_wtr_starter` RoleId=1 assumes role 1 = admin role exists in `CP6DB_OA` (startup-seeded);
  if a fresh DB lacks it, create/preview calls 403 -- treat as a seed/setup issue, not a bug.
