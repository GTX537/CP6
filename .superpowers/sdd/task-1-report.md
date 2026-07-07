# 波4 Task 1 Report: 审计接线（11 实体 + 行为测试）

## Status
DONE — commit `a66fb1f` on `feat/space-wave4-crosscutting`.

## Implemented
- 11 个 Space 主数据实体追加 `, IAuditable`（标记接口，接入 `CP6Context.SaveChanges` 字段级审计管道）：
  Space_Site / Space_Floor / Space_Zone / Space_Aisle / Space_Rack / Space_Location /
  Space_Template / Space_CodeRule / Space_Marker / Space_Connector / Space_ConnectorStop。
- 全部继承 `BaseBizEntity`，namespace `CP6.Entity.DomainModels.Space` 嵌套于 `CP6.Entity`，
  IAuditable 无需额外 using（同 GlAccount.cs:16 先例）。
- WmsBin **未挂**（机器写入消费表，审计噪音）——符合约束。
- 新建 `CP6.Tests/Space/SpaceAuditTests.cs`（照 FieldAuditCaptureTests 桩：FakeUser /
  TestHelper.CreateInMemoryContext / ParseChanges），2 个 Fact。

## TDD Evidence
- RED：挂接口前跑 SpaceAuditTests → 2 fail，`Assert.Single() Failure: The collection was empty`
  （无 IAuditable → 零审计行，确认现状）。
- GREEN：11 实体挂接口后 → `Passed! Failed: 0, Passed: 2`。
- EntityName 断言用 `nameof(Space_Site)`（核实既有测试同写法，取实体类名非表名）。

## Tests
- 新测试：2 passed。
- 全量 `dotnet test CP6.Tests/CP6.Tests.csproj`：**1559 passed / 5 skipped / 0 failed**（基线 1557 → +2）。
- `dotnet build CP6.slnx`：**Build succeeded，0 error**。

## Files changed (12)
- CP6.Entity/DomainModels/Space/Space_{Site,Floor,Zone,Aisle,Rack,Location,Template,CodeRule,Marker,Connector,ConnectorStop}.cs（各 +1 接口）
- CP6.Tests/Space/SpaceAuditTests.cs（新建，2 Fact）

## Self-review
- 接口机制为 opt-in 标记，无列映射，无迁移影响。
- 测试 Fact①覆盖 create/Op1/EntityName/EntityKey；Fact②覆盖 update/Op2/diff Field-Old-New。
- 未触碰 WmsBin；未改 DbContext（Space_Site 等已注册）。
- 仅一个 commit，含 Co-Authored-By trailer。
