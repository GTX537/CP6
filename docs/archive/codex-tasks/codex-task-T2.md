# Task: CP6 Phase 7 Gap 1.3 - Auto QC Linkage

## Mission

Wire `QualityInspectionService.CreateAsync` to automatically mark related Stock rows as `FAILED` when an inspection result is NG (`OverallResult == 2`). Stock marking is best-effort and must not roll back the quality inspection save.

## Files to modify

| File | Change |
|---|---|
| `CP6.Core/Services/Mes/QualityInspectionService.cs` | Inject `IStockQcService`; after saving an NG inspection, call `MarkLinkedStockByWorkOrderAsync(workOrderNo, "FAILED", $"QC NG: inspection {no}", userName)` and log failures. |

## Tests to add

| File | New tests |
|---|---|
| `CP6.Tests/QualityInspection_AutoQcLinkTests.cs` | NG marks linked Stock FAILED; PASS does not change Stock; NG with no linked Stock still saves QI successfully. |

## Acceptance criteria

```bash
cd D:\CP6
dotnet build CP6.Tests/CP6.Tests.csproj --nologo -v quiet
dotnet test CP6.Tests/CP6.Tests.csproj --no-build --nologo -v quiet
```
