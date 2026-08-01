# Space E08-S02 Runtime Freshness Implementation Plan

Date: 2026-08-01
Design: `docs/superpowers/specs/2026-08-01-space-e08-s02-runtime-freshness-design.md`

## Task 1: Extend runtime freshness metadata

Files:

- `CP6.Space.Contracts/SpaceWmsRuntimeContracts.cs`
- `CP6.Space.Infrastructure/SpaceWmsRuntimeService.cs`
- `CP6.Space.IntegrationTests/SpaceWmsRuntimeServiceTests.cs`
- `CP6.Space.UnitTests/SpaceWmsRuntimeContractTests.cs`
- `CP6.WebApi/OpenApi/SpaceDesignV1OpenApi.cs`
- `CP6.Tests/Space/SpaceDesignV1OpenApiTests.cs`
- `docs/space/contracts/design-v1.openapi.json` (generated)
- `CP6.Space.Client/SpaceDesignV1Client.g.cs` (generated)
- `sdk/typescript/space-design-v1/spaceDesignV1Client.ts` (generated)

Steps:

1. Add `AdapterId`, `ReceivedAtUtc`, `DelayMilliseconds`, and `ClockSkewMilliseconds` to `SpaceWmsRuntimeSourceDto`.
2. Capture the UTC server receive time once when a complete inventory/task response is produced.
3. Compute non-negative delay/skew safely and include declared adapter identity.
4. Add integration tests for identity, delay, skew, unavailable, multi-chunk, and non-UTC clock behavior.
5. Regenerate the authoritative OpenAPI contract and both SDKs.
6. Update and run runtime shape/OpenAPI freeze tests.

## Task 2: Add typed Viewer runtime inventory client and model

Files:

- `cp6.web/src/types/space/runtime.ts`
- `cp6.web/src/api/space/runtime.ts`
- `cp6.web/src/api/space/__tests__/runtime.spec.ts`
- `cp6.web/src/space-viewer/overlay/runtimeStockModel.ts`
- `cp6.web/src/space-viewer/overlay/runtimeStockModel.spec.ts`

Steps:

1. Mirror the additive runtime response contract in TypeScript.
2. Serialize current-floor IDs as repeated `locationLogicalId` parameters.
3. Aggregate runtime rows per requested logical identity.
4. Create explicit empty rows only inside the requested/available scope.
5. Restrict derived status to empty/occupied and leave capacity null.
6. Test serialization, aggregation, empty locations, and code mismatch behavior.

## Task 3: Add refresh-history model

Files:

- `cp6.web/src/space-viewer/overlay/runtimeRefreshState.ts`
- `cp6.web/src/space-viewer/overlay/runtimeRefreshState.spec.ts`

Steps:

1. Define `never`, `active`, and `recovered` failure states.
2. Record success from server receive time.
3. Record explicit unavailable and thrown request failures without mutating snapshot metadata.
4. Extract only safe problem/HTTP codes.
5. Test all state transitions.

## Task 4: Migrate the stock overlay to the unified runtime source

Files:

- `cp6.web/src/space-viewer/api/ViewerHandle.ts`
- `cp6.web/src/space-viewer/SpaceViewer.ts`
- `cp6.web/src/space-viewer/overlay/StockOverlay.ts`
- `cp6.web/src/space-viewer/overlay/StockOverlay.spec.ts`
- `cp6.web/src/views/space/viewer/FloorViewer.vue`

Steps:

1. Expose a defensive array of rendered location logical IDs from the Viewer.
2. Change `StockOverlay.refresh` to call the runtime endpoint with site ID and location IDs.
3. Apply aggregation output only for a usable source.
4. Update `FloorViewer` to track refresh success/failure while retaining the last good snapshot on thrown refresh errors.
5. Confirm polling and floor switching always use the current floor's ID list.

## Task 5: Display the full trust state

Files:

- `cp6.web/src/views/space/viewer/StockLegend.vue`
- `cp6.web/src/views/space/viewer/__tests__/StockLegend.spec.ts`

Steps:

1. Display source system, adapter/connection, observation time, receive time, and delay.
2. Display clock skew separately from delay.
3. Display last success and recent failure state/time/code.
4. Limit the status legend to empty/occupied and label utilization as an estimate.
5. Add component tests for real, simulated, unavailable, active failure, recovered failure, and skew.

## Task 6: Verify and integrate

Steps:

1. Run targeted backend runtime tests.
2. Run targeted frontend unit/component tests.
3. Run frontend type-check.
4. Run the relevant .NET build and broader tests proportional to touched projects.
5. Inspect the diff for trust-boundary regressions and accidental legacy endpoint deletion.
6. Commit the feature branch.
7. Merge into `integration/space-v1-20260730`, rerun verification, and add completion evidence.
