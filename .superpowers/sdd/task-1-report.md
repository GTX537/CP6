# Task 1 Report — StandardRoleSeed 标准角色种子（TDD）

STATUS: **DONE**
Branch: feat/general-role-vperm  Commit: `ddcfa1ac` (pushed)

## What was built (3 files, exactly)
- **Create** `CP6.WebApi/Seed/StandardRoleSeed.cs` — per-tenant idempotent insert-only seed for 「一般用户」(RoleId=10).
- **Modify** `CP6.WebApi/Program.cs` — registered `StandardRoleSeed.EnsureSeeded(db);` immediately after the OawfPermissionSeed chain.
- **Test** `CP6.Tests/StandardRoleSeedTests.cs` — 7 Facts.

Seed content (decisions verbatim):
- Role: (RoleId=10, RoleName="一般用户") per tenant.
- Menus: {740, 733, 735, 737}.
- Actions (8): (733 read/approve/transfer/sendback/withdraw), (735 submit/favorite), (737 delegate). NOT addsign.

## Entity-shape adaptations
- **Method name = `EnsureSeeded(CP6Context db)` (synchronous), NOT `SeedAsync`.** The brief skeleton showed `await StandardRoleSeed.SeedAsync(db)`, but the stronger/repeated instruction ("same call shape as neighbors", "mirror their exact mechanics") governs: every sibling seed (Pur/Oawf/Erp/Mes/Wms/…) and the Program.cs neighbors use synchronous `EnsureSeeded(db)` + single `db.SaveChanges()`. I mirrored that exactly; no `await` at the call site.
- Sys_Role is composite-PK (TenantId, RoleId), int RoleId, does NOT inherit BaseTenantEntity — sets `TenantId`+`RoleId`+`RoleName` explicitly (existence guard on `TenantId==tid && RoleId==10`).
- Sys_RoleMenu: int identity Id PK + explicit TenantId/RoleId/MenuId.
- Sys_RoleAction: BaseTenantEntity (Guid Id) + RoleId/MenuId/ActionCode.
- Tenant iteration/stamping/idempotency copied verbatim from Pur/OawfPermissionSeed: enumerate `db.Sys_Tenants.Select(t=>t.Id)`; explicit `TenantId=tid` (StampTenant fills only Guid.Empty); `IgnoreQueryFilters()` on existence checks; single `SaveChanges()` when changed; NoTenants → no-op.

## Program.cs insertion point
After `WorkCalendarConnectorPermissionSeed.EnsureSeeded(db);` (last of the Oawf chain: OawfPermissionSeed → FlowTrigger → InboxBatchTransfer → WorkCalendarConnector), before the "缺用户管理菜单" block. Depends on menus 740/733/735/737 + MenuActions catalog seeded upstream.

## Fact #7 (aggregator end-to-end)
PermissionAggregator resolves RoleIds = Sys_UserRoles ∪ Sys_User.RoleId. Constructed user with `RoleId=StandardRoleSeed.GeneralRoleId`. The aggregator's Sys_RoleActions query obeys the tenant query filter (`TenantId==CurrentTenantId`; single-arg ctx → `TenantContext.DefaultTenant` = 00000000-…-A1), so Fact #7 seeds under a Sys_Tenant with Id=DefaultTenant (not TenantA/B). Asserts ActionKeys == exactly 8 "menu-key:action", incl. "oa-inbox:approve", excludes "oa-inbox:addsign", count==8. Menu 740 (MenuKey=null parent) contributes nothing to ActionKeys — consistent with 8.

## RED evidence
`--filter StandardRoleSeedTests` on empty-shell seed:
`Failed! - Failed: 5, Passed: 2, Total: 7`. The 5 content Facts fail (empty/null results); Facts 5 (DoesNotTouchAdminRole) and 6 (ActionsSubsetOfCatalog) pass trivially — they don't require the seed to act.

## GREEN evidence
- New class: `Passed! - Failed: 0, Passed: 7, Skipped: 0, Total: 7`.
- Full suite: `Passed! - Failed: 0, Passed: 2220, Skipped: 5, Total: 2225` (= baseline 2213 + 7). Target met exactly.

## Self-review
- Commit diff = exactly the 3 deliverable files (seed / Program.cs / test), 318 insertions.
- Seed touches ONLY RoleId=10 rows (all guards + inserts pinned to GeneralRoleId=10). Fact #5 proves admin RoleId=1 three-table rows have zero diff across the seed call.
- Program.cs insertion after the Oawf chain. No entity/migration changes.
- Idempotency proven (Fact #4: second call → three-table counts unchanged; 2 roles / 8 menus / 16 actions across 2 tenants).

## Concerns / notes
- **task-1-brief.md was modified in the working tree** (previous Task 1 "引擎归属闸" content replaced with this StandardRoleSeed brief) — the parent's handoff edit, NOT mine. I deliberately left it UNSTAGED / uncommitted. Likewise the stale `task-1-report.md` (old engine-ownership report) — this file — was overwritten with the current report. If the parent wants those doc changes to land, they need a separate commit.
- Pre-existing untracked `CP6.Tests/PermissionSeedInterlockTests.cs` reflects only on the 9 named module seeds; StandardRoleSeed is not in its list → no interference.
- RoleId=10 is a per-tenant convention number for this wave. Seed is insert-only: if a tenant already had a RoleId=10 under a different name, the seed skips it (leaves it intact) and would NOT create 一般用户. No such collision in current data per plan; flagging the assumption.
