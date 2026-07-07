# Task C-T3: 服务目录端点 GET /api/oa/designer/service-catalog

（摘自 docs/superpowers/plans/2026-06-29-wfs-service-task.md；spec 章节 docs/superpowers/specs/2026-06-29-wfs-service-task-design.md §6.2/P1-6）

> **P1-6(过滤)落点。**

**Files:**
- Modify: DesignerController(执行时 Glob `**/DesignerController.cs` 确认路径)— 加 action
- Modify: DesignerService(Glob `**/DesignerService*.cs`)— 加 `GetServiceCatalog()` 返回 actions/connectors(注入 `IEnumerable<IServiceTaskExecutor>` + `IEnumerable<IWfConnector>`)
- Test: `CP6.Tests/Wf/ServiceCatalogTests.cs`(服务层)

- [ ] **Step 1: 写失败测试**(服务层,注入 fake executors/connectors)

```csharp
// 断言:actions 只含 Kind=="dataWriteback" && VisibleInDesigner==true 的(WebApiExecutor 不出现);
//       connectors 含全部;每项 {name,label(DisplayName)}
[Fact] public void GetServiceCatalog_FiltersWebApiExecutor_From_Actions() { ... }
```

- [ ] **Step 2: FAIL**(`--filter ServiceCatalogTests`)。
- [ ] **Step 3: 实现**
  - `DesignerService.GetServiceCatalog()` → `{ actions = execs.Where(e => e.Kind=="dataWriteback" && e.VisibleInDesigner).Select(e => new {name=e.Key, label=e.DisplayName}), connectors = conns.Select(c => new {name=c.Name, label=c.DisplayName}) }`。
  - `DesignerController` 加 `[HttpGet("service-catalog")]`,照既有 action 模式(`LocalizedControllerBase` / `Ok2(...)` / `ICurrentPermissionContext`)。
- [ ] **Step 4: PASS + Wf 闸**。
- [ ] **Step 5: commit** — `git commit -m "feat(wfs-service-task): C-T3 service-catalog 端点(按 Kind/VisibleInDesigner 过滤)"`

## 注意（架构审查 2026-07-05 补充）
- `CatalogController` 是表单分类目录，与服务目录无关，勿混。
- C-T2 的 SampleDataWritebackExecutor（Key="sampleWriteback", VisibleInDesigner=true）落地后，actions 应恰好含它一项；WebApiExecutor（VisibleInDesigner=false）被过滤。

## 共享契约（精确名字）
- `IServiceTaskExecutor { string Key; string Kind; bool VisibleInDesigner; string DisplayName; ... }`
- `IWfConnector { string Name; string DisplayName; ... }`

## 落码纪律（每 Task 都遵守）
- 工作目录 `C:\CP6`，分支 `feat/wfs-service-task-finish`。本地 commit 不 push。
- 零 Space 污染；完成后 `git show --stat` 复核。
- Wf 闸：`dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf` 既有测试字节等价全绿。
- TDD 节奏：失败测试→FAIL→最小实现→PASS→commit。
- 不重新设计：spec 决策 D1~D11 全锁。
