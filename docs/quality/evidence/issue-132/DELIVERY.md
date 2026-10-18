# BUG-132 — wave16 实施交付

## 2026-10-18：COORD接手R132-01修正，待原D局部复查

所有权通过[PR264](https://github.com/GTX537/CP6/pull/264)和[协调96a625d4](https://github.com/GTX537/CP6.AI-Handoff/blob/96a625d40bee0f775e6e699d4765a51f86e0a93b/handoffs/CP6-DEV-20261012/waves/20261017-wave16.md)转为COORD独占。原A的325dc769分支及干净工作树保留。新分支 `codex/bug-132-http413-correction-20261018` 从main `24f97b03011dc09c741c5f25d7463bd9f54a1fbc` 创建，以 `5db1cb56c527a42bfe9a8c6fadd4fd15894c46d5` 可审计cherry-pick承接A候选。本次修正只改原Controller、HTTP测试和本报告；Program、公共中间件与大小上限不变。

[D集中审查](https://github.com/GTX537/CP6.AI-Handoff/blob/88c89e639cfa1b1da6d98ed84d96f172cf6022f3/handoffs/CP6-DEV-20261012/tasks/REVIEW-BUG-132/REVIEW.md)指出的绑定超限异常原由Space中间件改为500。现库存POST安装局部异常过滤器，在响应尚未开始且BadHttpRequestException状态为413时返回413/application/problem+json；其他异常继续原Space映射。过滤器覆盖绑定阶段，旧GET和其他端点不安装它，权限/CSRF/精确集合语义不变。

HTTP夹具加入实际Space错误层，保持BizException → SpaceDesignProblemDetails → CSRF → Authorization/MVC相对顺序。仍为真实Kestrel/JWT Cookie/绑定及受控权限、runtime数据，不称整个应用或持久权限/数据库验收。

私有证据 `D:/CP6-local-test/BUG-132/coord-r132-01/`：

| 来源 | 本次实际执行结果 |
|---|---|
| results/red-wire-boundaries.trx / red-wire-boundaries.log | 2失败：声明长度与chunked合法边界均先通过200；超1传输字节均预期413、实际500。修复前RED。 |
| results/green-http-contract.trx / green-http-contract.log | **65通过、0失败、0跳过**：7项HTTP行为及58项OpenAPI/旧客户端合同。两种传输合法/超限、413内容类型/body/status、超限无runtime调用均通过；非413 BadHttpRequestException和普通runtime异常保留500/SPACE_INTERNAL_ERROR，原权限/CSRF/JSON/精确集合/GET及请求行边界通过。 |
| restore.log | RestoreLockedMode=true、NuGetAudit=false恢复成功，之后--no-restore；编译受影响项目及必要依赖，保留既有analyzer warnings。 |

额外边界事实：最初 `red-actual-pipeline` 与 `red-pipeline-diagnostic` 均2失败，其中新加chunked正向曾把1MiB纯载荷误当1MiB传输量。核对[.NET 8 Kestrel源码](https://github.com/dotnet/aspnetcore/blob/v8.0.0/src/Servers/Kestrel/Core/src/Internal/Http/Http1ChunkedEncodingMessageBody.cs)确认其计数包括chunk格式。本夹具一次Write发送一个chunk，五位十六进制长度与格式共14字节，所以chunked合法边界为1,048,562载荷字节+14格式字节，超1载荷字节即超传输上限；声明长度合法边界仍为1,048,576载荷字节。未改既有上限或缩小10,000唯一GUID合法集合，原失败保留且不当作最终正例。

```powershell
dotnet test CP6.Tests/CP6.Tests.csproj --no-restore --filter 'FullyQualifiedName~SpaceWmsInventoryQueryTests|FullyQualifiedName~SpaceDesignV1OpenApiTests' --logger 'trx;LogFileName=green-http-contract.trx' --results-directory D:/CP6-local-test/BUG-132/coord-r132-01/results
```

复用原A锁定Vitest4.1.11前端35、InMemory服务9及D对未变范围的审查，未重跑前端/内存或全仓。原61项通过保留，但其中413结果限遗漏Space层的简化宿主，现由定向结果补齐。未变产品和依赖相对325dc769核对一致；文档变化不触发产品重跑。

HTTP18232已停止，15232未启动，两个端口释放。无数据库、实际浏览器/DEV、Actions或部署；#132保持Open，产品未合并，R132-01等待原D局部复查，不自批Ready to merge。以下是原A交付历史，当前修正以本节及固定Handoff为准。

## 原A交付记录

2026-10-18，原 A 唯一实施 Owner；[Issue132](https://github.com/GTX537/CP6/issues/132) 保持 Open。候选为本报告所在提交，完整固定 SHA 由 A Handoff DELIVERY 指向。待原 D 一次集中 SPEC/QUALITY，再由 COORD 正常集成、核对远端与按 Issue 原条件关闭。

## 输入与修改

产品分支 `codex/bug-132-inventory-transport-20261017`，工作树 `D:/CP6-worktrees/bug-132-inventory-transport-20261017`。开工与交付前 fetch 后本地 main/origin/main 均为 `1e29931374626a492ca92e848b8d8fcaf871eddf`，也是任务父基线。已读 [A 包](../../../development/dispatch/20261017-wave16/A-BUG-132.md)、dispatch、roadmap、Issue、旧补丁撤回原因、协调 Handoff `25fc72a6c5e40862ae50364592450eb4b393f106`；真实 START 仅一次，A Handoff `b7b618df6d0b771ab58382aa0a6444d2ac3e1d77`。D 准备 `843dcc0` / 消息 `603df33` 已读并纳入边界核对，不冒充最终审查。

前端 `spaceRuntimeApi.inventory` 改为同一路由的单次 POST JSON，携带去重后的完整 `locationLogicalIds`；URL 不随集合增长。不截断、不拆成多个 API 请求、不在失败时改查整站。现有 axios unsafe-method CSRF 注入继续适用。

新增兼容 `QueryInventory` 操作和请求 DTO；旧 GET 路由、operationId `GetInventory`、默认查询及响应均保留。新入口沿用 `[Authorize]`、`space:model:read` 与已有 CSRF，不改公共权限、CSRF、Program 或全局 Kestrel 配置。控制器仅将集合一次传给原 `QueryInventoryAsync`，不复制或绕过服务的 tenant/SiteAccess/Current Published/来源校验。WMS 原有内部 500 项取数及来源合并语义未变。

| 新 POST 输入/边界 | 行为 |
|---|---|
| 空 body、JSON null、缺字段或字段 null、非法 GUID/数组元素 | MVC 400，未调用 runtime |
| `locationLogicalIds: []` | 精确空选择，仍经过服务的 SiteAccess/Published 校验，不转成 null/整站 |
| 空 GUID、缺失位置、跨租户/无站点权限、旧 Published 位置 | 沿用现有服务拒绝；无部分成功 |
| 重复 GUID | 服务按 GUID 值 Distinct 后计数；不是原始数组长度上限，未加 `MaxLength/maxItems:10000` |
| 10,000 唯一选择 | 保留合法上限；即使站点有 10,001 个位置，也只查询该集合 |
| 10,001 唯一选择 | 原服务 400，WMS 未调用 |
| 原始请求体 | 最多 1,048,576 字节（1 MiB）；可容纳紧凑的 10,000 GUID JSON。Content-Length 与 chunked 超界均 413，不依赖声明长度单独判断 |

OpenAPI 由现有 `tools/CP6.Space.OpenApiGenerator` 生成，仅增加该 POST、必填请求 schema 和其 413 ProblemDetails schema；原 GET/响应 schema 不变。仓库旧 C#/TS 生成 SDK 保持 GET 兼容，本轮手写 Web API/types 使用新 POST，不扩写未登记 SDK 文件。

实际十个登记文件：controller、SpaceWmsRuntimeContracts、Web runtime API/types/test、新 HTTP 测试、既有 OpenAPI 测试、InMemory 服务测试、生成 OpenAPI、本报告。StockOverlay 源码/原测试与 Application/Infrastructure 服务实现只读；未修改共享台账/四状态、模型/迁移、项目/依赖/锁、其他接口。

## 实际验证与证据

证据目录 `D:/CP6-local-test/BUG-132/wave16-a/`，不是生产制品。未启动数据库、连接既有 5432/19991/18080 或替换任何既有服务。

| 执行 | 结果及适用范围 |
|---|---|
| 前端 RED：`frontend-red.log` | 修改产品前新增四项失败、15 项通过；原 GET 没有执行所需 POST |
| URL 定向 RED：`url-red.log` | 实际构造的 10,000 GUID 请求目标 **550,077 字符 > 8,192**；1 项失败，其余19项按筛选未运行。不是把历史275KB/414写成本次浏览器现场 |
| 最终前端：`frontend-locked-final.log` | **35 passed / 0 failed / 0 skipped**，Vitest 4.1.11；runtime.spec + StockOverlay.spec。0/101/10,000 GUID、重复项、常量URL/完整body、失败无fallback、Overlay真实入口到单次POST；原Overlay空范围、过时响应、失败保留快照与来源状态回归 |
| 最终 HTTP/OpenAPI：`results/http-contract-final.trx`、同名log | **61 passed / 0 failed / 0 skipped**，含3个真实Kestrel HTTP测试及58个OpenAPI/既有生成客户端合同检查；受影响.NET项目及必要依赖编译成功 |
| 服务：`results/runtime-inmemory.trx`、同名log | **9 passed / 0 failed / 0 skipped**；真实SpaceWmsRuntimeService + EF InMemory +记录型WMS source，不是PostgreSQL/真实WMS验收 |
| OpenAPI生成与完整diff | 生成器成功；最终仅相关操作/schema增量，`git diff --check`通过 |

HTTP fixture 使用自有 loopback18232、真实 JWT 验签（Cookie 携带）、TenantMiddleware、MVC JSON绑定、RequirePermission 和启用的 CsrfMiddleware。权限数据与 runtime 使用受控 mock，因此它证明路由/请求/安全过滤器接线，**不冒充真实持久权限或整条数据库链**。匿名401、无权限403、缺/错CSRF403、正确CSRF200、空/坏JSON400、空与20,000重复项的完整传递、兼容GET均执行。精确1MiB body200、加一字节Content-Length/chunked413；原Kestrel请求行8,192字节（含CRLF）可进入认证返回401，加一字节414。超界/绑定/授权拒绝均未调用runtime。

新增InMemory大站点测试在同一Published下实际建立10,001个有效位置，并给记录型source配置对应库存，选择0/101/10,000并重复一遍，核对返回ID集合、source、Published与decimal `1.23456789`；源调用总数只含所选位置。验证缺失ID/空GUID、SiteAccess拒绝、另一tenant上下文不可见；切换Current Published后旧位置拒绝，空选择返回新版本。另运行现有唯一数超界/大量重复、源越界、来源切换、后块Unavailable丢弃前块、传输失败等相关用例，未执行无关MES/全仓或数据库测试。

初次前端34项成功使用了复用目录中的Vitest4.1.9，收尾发现它与锁文件4.1.11不一致，保留原日志但不用它代替最终结果。在独立 `frontend-locked` 源码快照以 `npm ci --ignore-scripts --no-audit --no-fund` 安装现有锁文件版本，补上D准备指出的Overlay到真实API入口场景后重跑35项。API/types/两份测试/Overlay源码/package与lock/config八个输入的文件哈希已核对与任务工作树相同；未更新共享依赖包或锁文件。首次HTTP3项已成功，随后只因补齐请求体必填OpenAPI元数据而复查HTTP并集中跑合同61项，不重复计算为额外成果。无未解决测试失败；既有编译/analyzer及npm依赖弃用warning未扩展为无关修复。

复现命令（任务工作树；使用现有本地NuGet缓存，测试不需要数据库变量）：

```powershell
dotnet run --project tools/CP6.Space.OpenApiGenerator/CP6.Space.OpenApiGenerator.csproj -- docs/space/contracts/design-v1.openapi.json
dotnet test CP6.Tests/CP6.Tests.csproj --no-restore --filter 'FullyQualifiedName~SpaceWmsInventoryQueryTests|FullyQualifiedName~SpaceDesignV1OpenApiTests'
dotnet test CP6.Space.IntegrationTests/CP6.Space.IntegrationTests.csproj --no-restore --filter 'FullyQualifiedName~SpaceWmsRuntimeServiceTests.Large_site_exact|FullyQualifiedName~SpaceWmsRuntimeServiceTests.More_than_10000|FullyQualifiedName~SpaceWmsRuntimeServiceTests.Repeated_requested|FullyQualifiedName~SpaceWmsRuntimeServiceTests.Multi_chunk|FullyQualifiedName~SpaceWmsRuntimeServiceTests.Later_unavailable|FullyQualifiedName~SpaceWmsRuntimeServiceTests.Returned_identity_outside|FullyQualifiedName~SpaceWmsRuntimeServiceTests.Empty_published|FullyQualifiedName~SpaceWmsRuntimeServiceTests.Transport_failure'
# 在按现有lock安装的cp6.web目录（本次为frontend-locked快照）
npm test -- src/api/space/__tests__/runtime.spec.ts src/space-viewer/overlay/StockOverlay.spec.ts
```

初次.NET restore使用 `RestoreLockedMode=true`、`NuGetAudit=false`；最终复用未变依赖 `--no-restore`。没有新建第二份验证报告或上传凭据/JWT。

## 收尾与剩余范围

A已集中核对完整diff、白名单、必填与空选择语义、字节/去重计数分离、单服务调用、GET/响应兼容、安全过滤、生成增量及证据适用范围。最终D审查尚待固定候选，不在此预判可合并。

fixture已停止并释放18232；15232从未启动，两个端口均无监听。无PG/SQL Server、实际浏览器/DEV、完整前端type-check/build、全仓回归或生产验收；本Issue的源码/受影响前端与HTTP合同范围不需要额外数据库部署门禁。旧#257交付和历史33项结果未冒充本轮依据。

push前核对候选/main工作流与相关依赖未变；普通入口仍workflow_dispatch，R2 candidate仅`v*.*.*` tag，本次只推任务分支。任务Actions列表空，取消0、触发变更0、远程运行0。Handoff private/Actions disabled/default与自身无workflow的检查继续适用。COORD负责共享状态、正常合并、远端包含性和Issue原条件收尾；A不创建PR、不合并main、不关闭Issue、不部署。
