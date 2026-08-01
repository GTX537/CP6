# E08-S04 unified task path acceptance plan

## Goal

Move the 3D Viewer pick-path acceptance flow from the legacy floor task query to
the E08-S01 WMS runtime boundary. The flow must explain the WMS actual sequence,
a presentation-only optimized sequence, cross-zone/cross-floor transitions, and
task workload without writing an optimized order back to WMS.

## Contract

- Add `GET /api/space/design/v1/sites/{siteId}/runtime/tasks/path?taskId=...`.
- Treat `taskId` as a required query value so business identifiers containing
  `/` or other path characters are not truncated by routing or gateways.
- Normalize the task identity once, pass it into `SpaceWmsTaskQuery.TaskIds`,
  and reject adapter rows outside that filter.
- Reuse the current Published/Active Space scope, adopted WMS logical identities,
  500-location chunks, 10,000-location bound, and E08-S02 source/freshness DTO.
- Return the WMS actual stops plus floor, zone-workload, and aisle topology needed
  for a read-only Viewer comparison.
- Report stop/floor/zone totals, coordinate coverage, total quantity, and actual
  floor/zone transition counts. Duplicate WMS sequence numbers fail closed as a
  runtime contract violation.
- An available source with no matching task is an authoritative empty result;
  `Unavailable` remains a separate state.

## Viewer behavior

- Query only the unified runtime task-path endpoint; do not use the legacy
  `/api/space/floor/{floorId}/pick-path` task source.
- Show task type/status, WMS actual order, optimized presentation order, source
  delay, cross-floor/cross-zone transitions, and workload by floor/zone.
- Use the existing aisle planner for one-floor tasks and the existing time-based
  multi-floor planner for cross-floor tasks.
- The current Design Revision has no connector topology. Cross-floor segments
  therefore degrade visibly to approximate direct segments with the existing
  warning instead of being presented as exact routes.
- Never optimize a partial task when any authoritative stop lacks coordinates.
  Keep actual order and workload visible while explaining why a route was not
  generated.
- Clicking an actual stop reuses Locator for asynchronous floor switching,
  fly-to, and highlighting, then restores the task acceptance overlay.
- The optimized order is explicitly labelled as visualization-only and is never
  written back to WMS.

## Verification

- Runtime DTO/service contract tests and task-filter propagation tests.
- Runtime service tests for cross-floor summaries, workload, empty/unavailable
  distinction, and duplicate-sequence failure closing.
- Frozen OpenAPI and generated C#/TypeScript SDK compilation and drift checks.
- Frontend API serialization, single/multi-floor planning, partial-coordinate,
  panel visibility, empty/unavailable, and stop-navigation interaction tests.
- Full Space unit/integration suites, full frontend tests, type-check, production
  build, and WebApi/SDK Release builds.
