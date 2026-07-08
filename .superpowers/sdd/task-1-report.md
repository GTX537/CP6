# Task P0-T1 Report: DataProtection 密钥环持久化到数据库（EF）

## What I implemented

- **Step 1 — Package**: Added `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore` `8.0.12` to `CP6.Core/CP6.Core.csproj` (DbContext lives in CP6.Core; the reference flows transitively to CP6.WebApi and CP6.Tests, so no separate add needed there).
- **Step 2 — DbContext**: `CP6Context` now implements `IDataProtectionKeyContext` and exposes `public DbSet<DataProtectionKey> DataProtectionKeys { get; set; }` (added `using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;`). Confirmed the reflection tenant filter at `CP6Context.cs:2062` only scans `BaseTenantEntity` subclasses (`typeof(BaseTenantEntity).IsAssignableFrom(...) && t.BaseType is null`), so `DataProtectionKey` is untouched — asserted by the `DataProtectionKey_Is_Not_TenantFiltered` test (`GetQueryFilter()` is null).
- **Step 3 — Migration**: `20260708085236_PersistDataProtectionKeys` — contains ONLY the `DataProtectionKeys` table (Id identity PK / FriendlyName / Xml, all nvarchar(max)). No model drift.
- **Step 4 — Program.cs**: `builder.Services.AddDataProtection().PersistKeysToDbContext<CP6Context>().SetApplicationName("CP6");` at ~line 519. Added `using Microsoft.AspNetCore.DataProtection;` for the `PersistKeysToDbContext` / `SetApplicationName` extension methods.
- **Step 5 — Tests**: pre-existing 5-test suite verifies interface contract, no tenant filter, key row lands in table after first Protect, Protect/Unprotect roundtrip, and cross-ServiceProvider (restart-sim, shared InMemory root) decrypt. Assertions match the brief; no changes needed.
- **Step 6 — Ops note**: captured in the commit body (SSO ClientSecret existing ciphertext was encrypted by the old ephemeral key and will NOT decrypt after switch — must be re-saved once on the PMS SsoConfig page post-deploy).

## What I tested and results

- Focused: `dotnet test CP6.Tests/CP6.Tests.csproj --filter DataProtectionPersistenceTests` -> **Passed! Failed: 0, Passed: 5, Total: 5**.
- Full suite: `dotnet test CP6.slnx` -> **Passed! Failed: 0, Passed: 1570, Skipped: 5, Total: 1575** (baseline 1565+ held; +5 new tests).

## TDD Evidence

### RED (before implementation — source changes stashed, untracked test kept)
Command: `dotnet build CP6.Tests/CP6.Tests.csproj --no-incremental`
```
CP6.Tests\Platform\DataProtectionPersistenceTests.cs(4,43): error CS0234: The type or namespace name
'EntityFrameworkCore' does not exist in the namespace 'Microsoft.AspNetCore.DataProtection'
Build FAILED. 1 Error(s)
```
The test does not compile without the package + interface — this is the RED state written last session.

### GREEN (after implementation)
Command: `dotnet test CP6.Tests/CP6.Tests.csproj --filter DataProtectionPersistenceTests`
```
Passed!  - Failed: 0, Passed: 5, Skipped: 0, Total: 5, Duration: 8 s - CP6.Tests.dll (net8.0)
```

## Files changed

- `CP6.Core/CP6.Core.csproj` — package reference
- `CP6.Core/EFDbContext/CP6Context.cs` — interface + DbSet + using
- `CP6.WebApi/Program.cs` — PersistKeysToDbContext + SetApplicationName + using
- `CP6.Core/Migrations/20260708085236_PersistDataProtectionKeys.cs` (+ .Designer.cs) — new migration
- `CP6.Core/Migrations/CP6ContextModelSnapshot.cs` — snapshot updated with DataProtectionKeys
- `CP6.Tests/Platform/DataProtectionPersistenceTests.cs` — test suite (was untracked, now committed)

## Self-review findings

- Migration verified to contain exactly one table, no unrelated changes -> no model drift.
- Package added only in CP6.Core (single source of truth); transitively available downstream — no redundant refs.
- Followed existing code/comment conventions in CP6Context and Program.cs.

## Issues / concerns

- Ops follow-up (not a code issue): after deploy, SSO ClientSecret must be re-saved once (old ciphertext undecryptable). Documented in commit body.
