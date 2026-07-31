# E08-S01 统一运行态数据源交付报告

- 状态：已完成，待合入 Space 受控集成分支
- 功能分支：`codex/space-e08-s01-runtime-source`
- 设计提交：`636eb6d5`
- 实施计划提交：`9d4383c1`
- 当前代码实现 HEAD：`15f6a296`
- 集成提交：尚不存在；Task 6 完成受控集成后补录实际 merge hash

本报告记录 E08-S01 的代码实现和功能分支验证结果。`15f6a296` 是
Task 1–4 的代码实现 HEAD，不包含本 Task 5 交付文档提交；本报告不声明
尚未发生的集成合并或合并态测试。

## 1. 交付范围

E08-S01 交付统一的 Published 运行态读取竖切：

1. `ISpaceWmsRuntimeService` 统一查询库存与任务；Space 提供当前
   Published/Active 空间与身份上下文，WMS 运行源提供实时库存和任务事实。
2. 每条结果同时公开 Space `LocationLogicalId` 与 WMS `WmsLogicalId`，
   以及 Space/WMS 双编码和 `CodeMatches`，不以编码漂移覆盖稳定身份。
3. WMS 查询按 500 个位置分块；请求去重后及当前 Published 映射后均执行
   10,000 个位置的硬上限。
4. 库存按 Space 库位编码、物料、批次、容器稳定排序；任务按 TaskId、
   SequenceNo、Space LogicalId 稳定排序。
5. 来源元数据、数据源身份、返回集合、空项、位置身份、任务必填字段和分块
   越界返回均失败关闭；适配器合同违例返回 502。
6. 新增两个只读 Design V1 API，并同步 DI、OpenAPI、C# SDK 和
   TypeScript SDK。
7. 两个端点都使用 `space:model:read`，并选择安全的 Problem Details
   权限响应。`RequirePermissionAttribute.UseProblemDetails` 为显式 opt-in，
   默认仍为 `false`，因此不会改写旧控制器的默认权限响应合同。

## 2. 数据与信任边界

- Design Revision 不保存库存、批次、容器、货主或任务事实；它只继续承载
  空间几何、生命周期和稳定身份。
- 当前 Published 且 Active 的 Space 模型是运行查询的空间/身份权威；
  生产 DI 默认把 `ISpaceWmsRuntimeSource` 解析到 `Cp6SpaceWmsAdapter`，
  CP6 WMS 适配器是库存/任务运行态权威。
- 标准模拟器只允许显式选择或测试使用；公开来源会以 `IsSimulated` 标识，
  不得冒充真实 WMS。
- 来源公开字段固定为 `Kind`、`DataSourceId`、`ObservedAtUtc`、
  `IsSimulated`、`IsAvailable`。
- `Unavailable` 不等于真实空库存/空任务。来源明确返回 `Unavailable` 时，
  API 返回空 `Items`，同时 `Kind=Unavailable`、`IsAvailable=false`；
  真实或模拟来源的真实空结果仍保持 `IsAvailable=true`。
- 适配器抛出异常时返回可重试 503；来源或输出违反运行态合同则返回 502，
  不把不可信数据继续投影给客户端。
- 本卡只读取当前 Published/Active 快照，不交付 Viewer UI、数据源/接收时间
  展示、延迟展示、健康状态展示或历史记录。

## 3. API 与合同

两个端点均为只读 GET，要求登录和 `space:model:read`：

- `GET /api/space/design/v1/sites/{siteId}/runtime/inventory`
- `GET /api/space/design/v1/sites/{siteId}/runtime/tasks`

可选筛选参数使用重复 query key：
`?locationLogicalId={id1}&locationLogicalId={id2}`。返回项同时包含
Space/WMS 双 LogicalId、双编码与编码一致性标记；库存项还公开楼层和
decimal 数量，任务项还公开楼层、可选分区/货架/空间锚点和可选数量。

错误合同区分如下：

- 502 `SPACE_WMS_RUNTIME_CONTRACT_VIOLATION`：来源声明、数据源身份、
  观测时间、集合/项或返回位置/任务字段违反合同，失败关闭且不可重试标记
  不会伪装为成功数据。
- 503 `SPACE_WMS_UNAVAILABLE`：适配器未能完成查询，Problem Details
  `recovery.action=retry-runtime-query` 且 `retryable=true`。
- 200 + 空 `Items` + `Source.IsAvailable=false`：适配器成功返回明确的
  `Unavailable` 来源状态，与真实空结果保持可观察区别。

OpenAPI 对运行态 DTO 的 required/non-null 保证只由
`SpaceWmsRuntimeSchemaFilter` 处理；过滤器只匹配五个运行态 DTO 类型，
不改变其他 Design V1 schema。`physicalQuantity`、`allocatedQuantity`
和可选 `quantity` 均保留 OpenAPI `number/decimal`；C# SDK 对应为
`decimal`、`decimal`、`decimal?`。TypeScript SDK 受 JavaScript 数值模型
限制使用 `number`，但响应、来源、双身份、双编码等必填字段保持无 `?` 的
required/non-null 生成保证，可选字段继续显式可选。

## 4. 验证证据

起始状态已核对为功能分支 `codex/space-e08-s01-runtime-source`、干净
HEAD `15f6a296c4bbee0ff528c39c3ccb71917d81339f`。验证前 D 盘可用空间为
3.02 GB；所有门禁串行执行，未删除缓存或执行破坏性清理。

| 门禁 | 实际命令 | 实际结果 | 实测耗时 | warnings / errors |
|---|---|---|---:|---:|
| Space UnitTests 全量 | `dotnet test CP6.Space.UnitTests/CP6.Space.UnitTests.csproj -c Release` | 220 passed / 0 failed / 0 skipped / 220 total；runner 942 ms | 6.982 s | 0 / 0 |
| Space IntegrationTests 默认全集 | `dotnet test CP6.Space.IntegrationTests/CP6.Space.IntegrationTests.csproj -c Release` | 94 passed / 0 failed / 48 skipped / 142 total；runner 7 s | 11.921 s | 0 / 0 |
| OpenAPI/权限/数据源合同聚焦 | <code>dotnet test CP6.Tests/CP6.Tests.csproj -c Release --filter "FullyQualifiedName~SpaceDesignV1OpenApiTests&#124;FullyQualifiedName~SpacePermissionAttributeTests&#124;FullyQualifiedName~SpaceDataSourceContractTests"</code> | 45 passed / 0 failed / 0 skipped / 45 total；runner 1 s | 7.112 s | 0 / 0 |
| Release 完整 solution build | `dotnet build CP6.slnx -c Release --no-incremental` | succeeded；0 errors；MSBuild `00:03:07.05` | 187.373 s | 10 / 0 |
| SDK/OpenAPI drift | `powershell -ExecutionPolicy Bypass -File tools/generate-space-design-sdk.ps1 -Check` | exit 0；OpenAPI、C#、TypeScript 生成物无 drift | 7.032 s | 0 / 0 |
| EF 模型/迁移一致性 | `dotnet ef migrations has-pending-model-changes --project CP6.Space.Infrastructure/CP6.Space.Infrastructure.csproj --startup-project CP6.WebApi/CP6.WebApi.csproj --context SpaceContext` | exit 0；`No changes have been made to the model since the last migration.` | 11.041 s | 0 / 0 |
| feature range whitespace | `git -c safe.directory=D:/CP6/tmp/worktrees/space-e04-s03-elements diff --check 636eb6d5..HEAD` | exit 0；silent | 0.087 s | 0 / 0 |

执行环境说明：UnitTests 命令首次在桌面受限沙箱中调用时，restore 在
1.232 s 后因无权读取
`C:\Users\tt\AppData\Roaming\NuGet\NuGet.Config` 而退出 1，尚未进入
测试执行。随后以相同命令在获批的非沙箱进程中重跑，得到表内 220/220
结果；该 ACL 阻断不是代码或测试失败。

48 个 skip 全部由 SQL Server 环境门禁产生。共同门禁变量为
`CP6_TEST_SQLSERVER`，代码中的精确原因是：
`Set CP6_TEST_SQLSERVER to run SQL Server integration tests.`；没有新增失败。

完整 solution build 的 10 个 warning 均来自 feature range 未修改文件，
因此属于既有基线：

- `FlowAdminService.cs:13,18`，CS8604：`Possible null reference argument for parameter 'FormKey' in 'FlowAdminItem.FlowAdminItem(string FlowKey, string FlowName, string FormKey, int Version, bool Enable)'.`
- `InboundService.cs:369`，CS8601：`Possible null reference assignment.`
- `SpaceRetryLeaseMigrationTests.cs:268,279`，CS8604：`Possible null reference argument for parameter 'actualArray' in 'void Assert.Equal<bool>(ReadOnlySpan<bool> expectedSpan, bool[] actualArray)'.`
- `PendingCookieTests.cs:42,43,56`，CS8602：`Dereference of a possibly null reference.`
- `BudgetVsActualTests.cs:105`，xUnit2012：`Do not use Assert.True() to check if a value exists in a collection. Use Assert.Contains instead.`
- `InboxServiceTests.cs:173`，xUnit2031：`Do not use a Where clause to filter before calling Assert.Single. Use the overload of Assert.Single that accepts a filtering function.`

## 5. 差异与边界

实际审计命令及结果：

| 审计 | 结果 |
|---|---|
| `git -c safe.directory=D:/CP6/tmp/worktrees/space-e04-s03-elements diff --stat 636eb6d5..HEAD` | 17 files changed / 5,781 insertions / 12 deletions |
| `git -c safe.directory=D:/CP6/tmp/worktrees/space-e04-s03-elements diff --name-only 636eb6d5..HEAD` | 下列 17 个文件，未发现范围外实现 |

```text
CP6.Core/Auth/RequirePermissionAttribute.cs
CP6.Space.Application/SpaceWmsRuntime.cs
CP6.Space.Client/SpaceDesignV1Client.g.cs
CP6.Space.Contracts/SpaceErrorCodes.cs
CP6.Space.Contracts/SpaceWmsRuntimeContracts.cs
CP6.Space.Infrastructure/SpaceInfrastructureRegistration.cs
CP6.Space.Infrastructure/SpaceWmsRuntimeService.cs
CP6.Space.IntegrationTests/SpaceWmsRuntimeServiceTests.cs
CP6.Space.IntegrationTests/StandardSpaceWmsSimulatorTests.cs
CP6.Space.UnitTests/SpaceWmsRuntimeContractTests.cs
CP6.Tests/Space/SpaceDesignV1OpenApiTests.cs
CP6.Tests/Space/SpacePermissionAttributeTests.cs
CP6.WebApi/Controllers/Space/SpaceWmsRuntimeController.cs
CP6.WebApi/OpenApi/SpaceDesignV1OpenApi.cs
docs/space/contracts/design-v1.openapi.json
docs/superpowers/plans/2026-07-31-space-e08-s01-unified-runtime-source.md
sdk/typescript/space-design-v1/spaceDesignV1Client.ts
```

设计文档位于锚点提交 `636eb6d5`，因此按定义不会再次出现在
`636eb6d5..HEAD` 的独占差异中；实施计划位于 `9d4383c1` 并出现在上表。
最终授权范围包括设计/计划文档、Task 1 合同/接口/测试、Task 2–3
服务/测试、Task 4 控制器/DI/OpenAPI/C#/TypeScript 客户端，以及两项
质量修复：

- `RequirePermissionAttribute.cs` 的安全 Problem Details 为 opt-in、
  default-off；
- `SpaceDesignV1OpenApi.cs` 的 required/nullability/decimal 修正只定位到
  运行态 DTO。

范围内没有 EF Migration、模型快照或增量 SQL，没有前端 Viewer 组件，
没有旧 Stock/Task controller 变更，也没有 Design Revision 持久化变更。

## 6. 后续边界

下一张建议卡为 E08-S02：在既有运行态 API 之上交付库存数据源、接收时间、
延迟和健康状态的展示/历史。上述展示、历史和 Viewer UI 均不属于 S01，
不得以本报告替代 E08-S02 的实现与验证。
