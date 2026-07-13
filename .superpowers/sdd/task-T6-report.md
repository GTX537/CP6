# Task T6 报告：E-WF-018 异步路径错误码结构化

**Status: DONE** — commit `ca0dd45`（分支 feat/wfs-cleanup-tickets，已 push）

## 缺陷核实（票面缺陷仍在，证据）

T2 改动后重新以实际行号核实，四处自由中文散文均在：

- `WfServiceJobService.cs:121`（异步引擎主循环，票面写 :117 已漂移）
  `Fail($"E-WF-018 动作/连接器未注册:{key}")` → 落 `Wf_ServiceJob.LastError` + `FailServiceTokenAsync` 错误路由
- `WebApiExecutor.cs:35` `Fail("E-WF-018 ActionRefJson 为空，无法解析连接器")`
- `WebApiExecutor.cs:40` `Fail($"E-WF-018 ActionRefJson 解析失败: {ex.Message}")`
- `WebApiExecutor.cs:44` `Fail($"E-WF-018 连接器未注册:{connectorName}")`

散文与真实 detail（连接器名/异常）混在一起不可 `Split('|')` 解析，前端无法按码 i18n。

## TDD 红绿

- **红**：新增 2 测试（`UnknownConnector_Fails_WithStructuredCode_NoProse`、`EmptyActionRef_Fails_WithStructuredCode`）
  → `Assert.StartsWith("E-WF-018|")` / `DoesNotContain("未注册"/"连接器"/"为空")` 失败，实际串为
  `"E-WF-018 连接器未注册:ghost"` / `"E-WF-018 ActionRefJson 为空，无法解析连接器"`（Failed 2/Passed 3）。
- **绿**：实现后 `--filter WebApiExecutorTests` 全过；`--filter Wf` 191 全绿；全量 **1829 passed / 5 skipped**（基线 1827 + 2 新测）。

## 实现（结构化格式 `E-WF-018|<机读明细>`）

`WebApiExecutor.cs`：
- 空 ActionRef → `E-WF-018|actionRefEmpty`
- 解析异常 → `E-WF-018|parseError:{ex.GetType().Name}`（去 `ex.Message` 本地化散文，仅异常类型名）
- 连接器未注册 → `E-WF-018|{connectorName}`

`WfServiceJobService.cs:121`（异步路径 executor 未注册）→ `E-WF-018|{key}`

管道前=可翻译码（前端 `Error.Split('|')[0]` 取码 i18n），管道后=无空格机读 token。T2 语义（尝试计数前移/reaper 去自增/幂等退避路由）全未触碰。

## 疑虑 / 跨波票候选

- **同源姊妹缺陷（同步路径，本票范围外）**：`ServiceTaskNodeHandler.cs:57`（sync 内联路径）
  仍有 `Fail($"E-WF-018 动作/连接器未注册:{key ?? "(none)"}")` 中文散文。本票标题明确限「异步路径」，
  brief 仅列两文件，故未改（避免 scope 蔓延/未验证的测试面）。**建议开跟踪票**：同步路径统一为
  `E-WF-018|{key ?? "(none)"}`，与异步路径口径对齐。此散文经 sync 失败降级异步或 catch 路径同样可能进 LastError。

## 零污染核对

`git show --stat`：仅 3 文件（WebApiExecutor.cs / WfServiceJobService.cs / WebApiExecutorTests.cs），
45 insert / 4 delete。零迁移、零 i18n 新键（结构化码非新增可翻译键，E-WF-018 seed 已存在）、零 Space/跨模块触碰。
