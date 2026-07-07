# Task C-T3 Report — 服务目录端点 GET /api/oa/designer/service-catalog

**STATUS:** DONE
**Commit:** 7d9d61a — `feat(wfs-service-task): C-T3 service-catalog 端点（按 Kind/VisibleInDesigner 过滤）`
**Branch:** feat/wfs-service-task-finish (本地 commit，未 push)

## 改动文件清单（6 个，102 insertions / 2 deletions）
- **Test（新增）** `CP6.Tests/Wf/ServiceCatalogTests.cs` — 服务层测试 `GetServiceCatalog_FiltersWebApiExecutor_From_Actions`。用真实 `SampleDataWritebackExecutor`（正例）+ `WebApiExecutor`（反例，注入空连接器）+ 2 个 fake（dataWriteback-但不可见 / 非 dataWriteback）覆盖过滤矩阵；连接器用真实 `EchoConnector` + 1 个 fake。
- **Modify** `CP6.Core/Services/Oa/DesignerModels.cs` — 新增 `record ServiceCatalogItem(Name, Label)` + `record ServiceCatalog(Actions, Connectors)`；补 `using System.Collections.Generic;`。
- **Modify** `CP6.Core/Services/Oa/IDesignerService.cs` — 接口加 `ServiceCatalog GetServiceCatalog();`。
- **Modify** `CP6.Core/Services/Oa/DesignerService.cs` — 构造函数注入 `IEnumerable<IServiceTaskExecutor>` + `IEnumerable<IWfConnector>`（DI 自动解析已注册的两类）；实现 `GetServiceCatalog()` 按 brief Step 3 精确谓词：`actions = execs.Where(e => e.Kind == ServiceKind.DataWriteback && e.VisibleInDesigner).Select(e => new ServiceCatalogItem(e.Key, e.DisplayName))`，`connectors = conns.Select(c => new ServiceCatalogItem(c.Name, c.DisplayName))`。补 `using System.Collections.Generic; using System.Linq;`。
- **Modify** `CP6.WebApi/Controllers/Oa/DesignerController.cs` — 新增 `[HttpGet("service-catalog")] public IActionResult ServiceCatalog() => Ok2(_designer.GetServiceCatalog());`。
- **Modify** `CP6.Tests/Oa/DesignerServiceTests.cs` — 既有 `Svc(db)` 测试辅助改用新的 4-参构造（传 `Array.Empty<IServiceTaskExecutor>()` / `Array.Empty<IWfConnector>()`），仅为保持编译，语义不变。

## Controller action 照哪个既有 action 的模式
照同 controller 的 `List` action（`[HttpGet("list")]` → `Ok2(...)`）：简单读取端点，`LocalizedControllerBase` 基类 + `Ok2(...)` 统一包壳（`{code,message,data}`），类级 `[Authorize]`。与 `List` 一致，未额外用 `_ctx`（`ICurrentPermissionContext` 只在写操作 Save/Clone 用于取 UserId，目录查询无此需要）。返回值同步（`GetServiceCatalog()` 是纯内存过滤，无 DB/await），故 action 无 `async`。

## 测试命令与输出摘要
- Step 2 FAIL（实现前）：`dotnet test ... --filter ServiceCatalogTests` → CS1729（无 4-参构造）+ CS1061（无 GetServiceCatalog），编译失败即红。
- Step 4 PASS：
  - `--filter ServiceCatalogTests` → **Passed! Failed: 0, Passed: 1**
  - `--filter Wf`（Wf 闸）→ **Passed! Failed: 0, Passed: 137**（含本任务新增 1；既有全绿）
  - `--filter DesignerServiceTests`（改了辅助）→ **Passed! Failed: 0, Passed: 5**
  - `dotnet build CP6.WebApi` → **Build succeeded, 0 Error**

## 自查发现
- `git show --stat 7d9d61a` 确认仅 6 个目标文件入 commit；`picture/`、`shots/` 等 untracked 为会话开始前既存，未 staged → 零 Space 污染。
- 过滤谓词用常量 `ServiceKind.DataWriteback`（== "dataWriteback"）而非字面量，与 `SampleDataWritebackExecutor` 保持同源，语义与 brief 的 `e.Kind=="dataWriteback"` 等价。
- DI：`DesignerService` 以 `AddScoped<IDesignerService, DesignerService>()` 注册，新增的两个 `IEnumerable<>` 依赖由容器自动从已注册的 `IServiceTaskExecutor`（WebApi/SampleWriteback）与 `IWfConnector`（EchoConnector）集合解析，无需改 Program.cs。
- 无 concerns。spec D1~D11 未触碰；未重新设计。
