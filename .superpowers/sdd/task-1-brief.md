### Task 1: 审计接线（11 实体 + 行为测试）

**Files:** 11 个 Space 实体（追加 `, IAuditable`，using CP6.Entity 已在基类同 namespace 无需加）；Test: `CP6.Tests/Space/SpaceAuditTests.cs`（新建，照 `CP6.Tests/Sys/FieldAuditCaptureTests.cs` 的桩与断言范式）

- [ ] Step 1: 测试先行——`Space_Site` create→Operation=1 行；update SiteName→Operation=2 且 diff Field/Old/New 断言；（RED：无 IAuditable 时 `Assert.Empty` 反向前提先确认现状零行）
- [ ] Step 2: 11 实体逐个追加接口；跑新测试 GREEN + 全量 1557→1559 级（+2 测试）
- [ ] Step 3: Commit `feat(space): 11 实体接入字段级审计（IAuditable）`

---

