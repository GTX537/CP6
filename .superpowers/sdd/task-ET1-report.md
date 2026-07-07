# Task E-T1 报告：FlowSchemaValidator serviceTask 规则 + DesignerService.save 注册校验

状态：完成。TDD 红→绿。E-WF-016 / E-WF-017 / E-WF-018 全部落地并测试覆盖。

## 改动文件清单

| 文件 | 改动 |
|------|------|
| `CP6.Core/Services/Wf/FlowSchemaValidator.cs` | 新增 `KnownServiceKinds`(引用 `ServiceKind` 常量，序数比较对齐引擎)；新增 ⑧ serviceTask 配置完整性+P2-3 成功出边(E-WF-016)、⑨ 错误出边规则(E-WF-017)。 |
| `CP6.Core/Services/Oa/DesignerService.cs` | `SaveAsync` 在 schema 校验通过后加 ①b 注册名校验：dataWriteback ActionName 对照 `Kind==dataWriteback` 执行器 Key 集合、webApi ConnectorName 对照连接器 Name 集合，未注册 → E-WF-018。复用 C-T3 已注入的 `_execs`/`_connectors`。 |
| `CP6.Tests/Wf/ServiceTaskValidatorTests.cs`(新增) | 11 个测试：8 静态验证器 + 3 save 注册校验。 |

零 Space 污染。未新建分支，未 push。既有校验的抛错/收集风格沿用（验证器 collect+Distinct，save 抛 `InvalidOperationException`）。

## 测试命令与输出

```
dotnet test CP6.Tests/CP6.Tests.csproj --filter ServiceTaskValidatorTests
  → Passed! Failed: 0, Passed: 11, Total: 11   (红阶段先 9 failed/2 passed)

dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf
  → Passed! Failed: 0, Passed: 148, Total: 148   (Wf 闸全绿，字节等价)

dotnet test --filter "~DesignerServiceTests|~FlowSchemaValidatorTests"
  → Passed! Failed: 0, Passed: 13, Total: 13   (Oa 命名空间既有验证器/save 测试无回归)
```

## 触发矩阵

### E-WF-016（FlowSchemaValidator，serviceTask 配置不完整/非法）
| 场景 | 触发 | 测试 |
|------|:---:|------|
| ServiceKind 不在 {dataWriteback,webApi,timer}（序数精确） | ✓ | （隐含于 kind 分支） |
| webApi 缺 ServiceConnectorName | ✓ | `WebApi_MissingConnector_E_WF_016` |
| webApi 缺 ServicePath | ✓ | （同分支覆盖） |
| dataWriteback 缺 ServiceActionName | ✓ | `DataWriteback_MissingAction_E_WF_016` |
| timer 缺 DelayMode 且缺 DelayValue | ✓ | `Timer_MissingDelay_E_WF_016` |
| timer 只缺 DelayMode（DelayValue 有）| ✓ | `Timer_MissingOnlyDelayMode_E_WF_016`（后端双字段严查，防线分层） |
| **非 end serviceTask 无非错误出边（P2-3，最危险洞）** | ✓ | `ServiceTask_NonEnd_NoSuccessEdge_E_WF_016` |

### E-WF-017（FlowSchemaValidator，错误边非法）
| 场景 | 触发 | 测试 |
|------|:---:|------|
| 一节点 >1 条 IsError 出边 | ✓ | `MoreThanOneErrorEdge_E_WF_017` |
| IsError 边来源节点非 serviceTask（如 approval） | ✓ | `ErrorEdge_FromNonServiceTask_E_WF_017` |

### E-WF-018（DesignerService.SaveAsync，引用名未注册）
| 场景 | 触发 | 测试 |
|------|:---:|------|
| dataWriteback ActionName 不在 Kind==dataWriteback 执行器 Key 集合 | ✓ | `Save_DataWriteback_UnregisteredAction_ThrowsE_WF_018` |
| webApi ConnectorName 不在连接器 Name 集合 | ✓ | `Save_WebApi_UnregisteredConnector_ThrowsE_WF_018` |
| 引用名均已注册 → 正常落库 | 不触发 | `Save_RegisteredNames_Persists` |
| 合法 serviceTask（webApi 配齐 + 1 成功边 + ≤1 错误边） | 不触发 | `ValidServiceTask_Passes` |

## 自查发现

- **kind 比较用序数（Ordinal）而非忽略大小写**：刻意为之。`ServiceTaskNodeHandler` 用 `kind == ServiceKind.WebApi/Timer` 精确匹配来分派 mode/executor；若验证器放行大小写不符的 kind，运行期会被当成非该 kind 静默降级（如 "WebApi" 被当 sync 无连接器）。验证器同用序数，把这类漂移在设计期拦成 E-WF-016，与运行期语义对齐。
- **timer 后端双字段严查**：前端 `validateClient` 只查 delayValue；后端按 brief 对 DelayMode|DelayValue 任一缺失都判 E-WF-016（`Timer_MissingOnlyDelayMode` 专测此分层防线）。
- **E-WF-018 只查"引用了但未注册"，不查缺名**：缺 ActionName/ConnectorName 属 E-WF-016，在 SaveAsync 上一步 schema 校验已拦并抛出，走不到 018 检查；018 的 `!IsNullOrWhiteSpace` 守卫是防御性冗余。校验顺序：schema(016/017) → 注册名(018) → 身份码唯一(009)。
- **E-WF-018 作用域**：按 brief 精确采用 dataWriteback ActionName + webApi ConnectorName 两类，timer 的连接器/动作引用不在本任务范围。
- **验证器插入位置**：⑧⑨ 放在并行网关校验之后、④可达性 BFS 之前，不影响既有规则求值；收集式 `errs.Add` + 末尾 `Distinct()`，E-WF-017 两条独立 if 即便同时命中也去重。

## Fix Round 1

审查 Approved 后顺手修 P2：E-WF-018 注册名校验的两个 HashSet 原用 `StringComparer.Ordinal`，与运行时三处解析字典（ServiceTaskNodeHandler / WfServiceJobService 的 executor Key、WebApiExecutor 的 connector Name，均 `OrdinalIgnoreCase`）不一致——save 比运行时严，仅大小写不符的注册名会被 save 误拒（运行时其实找得到）。

**修法**：`DesignerService.cs:54-55` 两处 `ToHashSet(StringComparer.Ordinal)` → `ToHashSet(StringComparer.OrdinalIgnoreCase)`，镜像运行时字典比较语义。

**回归测试**：`ServiceTaskValidatorTests.cs` 新增 `Save_ConnectorName_CaseInsensitive_Persists`——注册连接器名 "erpEcho"，schema 引用 "ErpEcho"（仅大小写不同），断言 save 成功落库（照 `Save_RegisteredNames_Persists` 写法）。

**验证**：

```
$ dotnet test CP6.Tests/CP6.Tests.csproj --filter ServiceTaskValidatorTests
Passed!  - Failed:     0, Passed:    12, Skipped:     0, Total:    12, Duration: 3 s - CP6.Tests.dll (net8.0)

$ dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf
Passed!  - Failed:     0, Passed:   149, Skipped:     0, Total:   149, Duration: 5 s - CP6.Tests.dll (net8.0)
```
