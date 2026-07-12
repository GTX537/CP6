# M-PUR T3 报告：反射 fail-closed 测试 + 403 拒绝用例

分支 `feat/m-pur-crosscutting`。纯测试任务，**零生产代码改动**（RED 反向验证的临时注释已还原，`git status CP6.WebApi/` 干净）。真相源 `docs/seeds/pur-permission-keys.md` 未动，T2 交付物未动，无 BLOCKED。

## 必读完成（按 brief 顺序）
1. 真相源 §一(24 行)/§二(7 键)/§三(高危 7)/§四(reconcile 已按 view **贴点** 非旁路)/§七(计数) 全读。
2. 前波先例 `OawfPermissionAttributeTests`（commit 1e75f38）+ `MesPermissionAttributeTests` 精读，结构照抄按 Pur 实况调整。
3. 403 先例 `RequirePermissionFilterTests`（filter 单元）/`PermissionChainIntegrationTests` + `FourGranularityIntegrationTests`（真实聚合链 InMemory DB → 403）读透，403 口径照抄后者。
4. T2 报告 concerns（reconcile = attr-view 非豁免表条目）落实到测试：豁免表 = 空。

## 实现清单

### 需求1 · 反射 fail-closed 测试 —— `CP6.Tests/PurPermissionAttributeTests.cs`（4 用例）
- **discovery 守卫**：`CP6.WebApi.Controllers.Pur` 下继承 ControllerBase 非抽象类 == **8**（防空扫假绿）。
- **fail-closed 核心闸** `EveryMutatingAction_IsGuarded_WithConventionalKey`：每个非 GET 端点必须带 `[RequirePermission]`；**豁免表 = 空**（reconcile 已贴 view，走核心闸，不进旁路——与 MES/OA 波 2 豁免不同，源码内 factual 注明）。贴点精确 == **24**。menu 匹配 `^pur-[a-z0-9-]+$` + ∈ 7 键白名单；action 逐词落 `ActionVocabulary`（17 词，**含 view**——本波 view 是贴点 action）。
- **键面 oracle 双向相等** `ResourceKeys_MatchIndependentOracle_Exactly`：24 收集集 ↔ 测试内独立 `ExpectedResourceKeys`（24，誊自 §一，零引用生产常量）双向 Except 相等 + 计数 24 + 资源键 1:1 无重复 + 前缀 ∈ 7 键 + 零下划线。
- **只读 GET 误贴防护** `NoReadOnlyGetAction_HasRequirePermission`。
- **基类扫描口径据实**：8 控制器**全部直接 `: ControllerBase`**（逐类 grep 核实，见源码 XML 注释），ControllerBase 无 [HttpXxx] 声明 → DeclaredOnly 不漏扫；注释写明未来引入共享基类需改策略（照先例，非失实抄袭）。

### 需求2 · 403 拒绝用例 —— `CP6.Tests/Pur/PurHighRiskDenialTests.cs`（2 用例）
- 覆盖真相源 §三**高危 7 键全部**：`pur-gr:add`/`pur-match:add`/`pur-match:release`/`pur-pr:convert`/`pur-rfq:convert`/`pur-subcontract:issue`/`pur-subcontract:cost`。
- `UnauthorizedUser_Is403_OnEveryHighRiskEndpoint`：走**真实后端聚合链**（PermissionAggregator→CurrentPermissionContext→PermissionService，InMemory DB），登录用户 "u" **仅授 pur-po:add** 一个良性键 → 对每个高危键经生产 `RequirePermissionAttribute.OnAuthorizationAsync` 请求 → 断言 `StatusCodes.Status403Forbidden`。外加**反射交叉核验**：每个 (控制器.方法) 确携该 (menu,action)，端点改名/改键则 403 oracle 亦破。
- `BenignGrantedAction_PassesChain`：正控——有 pur-po:add 的请求放行（证明链非全盘拒绝假绿）。

## TDD 证据

**RED**（临时注释 `RfqController.Convert` 的 `[RequirePermission("pur-rfq","convert")]`）：
```
dotnet test --filter "...PurPermissionAttributeTests|...PurHighRiskDenialTests"
Failed! - Failed: 3, Passed: 3, Total: 6
  EveryMutatingAction_IsGuarded_WithConventionalKey [FAIL]   （taggedCount 24→23 + offender）
  ResourceKeys_MatchIndependentOracle_Exactly [FAIL]         （oracle 有但源码缺（漏贴/改键））
  UnauthorizedUser_Is403_OnEveryHighRiskEndpoint [FAIL]
    RfqController.Convert：生产端点 [RequirePermission] = 无，与高危 oracle (pur-rfq,convert) 不符
```
即 brief 要求的「移除任一贴点 → 双重失败」：反射测试的计数闸 + oracle 闸同破，且 403 交叉核验亦捕获。三闸皆有牙。

**GREEN**（还原该贴点后）：
```
dotnet test --filter "...PurPermissionAttributeTests|...PurHighRiskDenialTests"
Passed! - Failed: 0, Passed: 6, Skipped: 0, Total: 6
```
还原后 `git status CP6.WebApi/` 空——最终交付零生产改动。

## 403 用例口径与依据
口径 = **已登录但无该操作权的用户 → 真实聚合链 → 生产 RequirePermissionAttribute → 403**，逐字照 `PermissionChainIntegrationTests.UngrantedAction_Returns403ThroughWholeChain` / `FourGranularityIntegrationTests`（授一无关键放行 + 目标键 403）。
**「无认证 401」取舍**：本仓 [Authorize] 认证层（401）在 HTTP 传输层，进程内 RequirePermission 过滤器不经它；且 `CurrentPermissionContext.GetAsync` 对无 Identity.Name 会 throw "未登录"（非返回 401）。故按 brief 明列的可行口径「无权限身份 → 403」落地，与既有两先例断言口径一致，非静默缩水。

## 全量结果
```
dotnet test CP6.Tests/CP6.Tests.csproj
Passed! - Failed: 0, Passed: 1808, Skipped: 5, Total: 1813, Duration: 57 s
```
基线 1802 + 新增 6 = **1808 绿**。5 skip 为既存结构性跳过，非本任务引入。构建 0 error；测试输出无本任务引入的 xUnit 警告（初版 xUnit1031 阻塞警告已改 async 消除）。

## 文件变更
- 新增 `CP6.Tests/PurPermissionAttributeTests.cs`（反射 fail-closed，4 用例）
- 新增 `CP6.Tests/Pur/PurHighRiskDenialTests.cs`（403 高危拒绝，2 用例）

## 自审
- **oracle 独立**：`ExpectedResourceKeys`/`ActionVocabulary`/`MenuKeyWhitelist`/`HighRiskKeys` 全为测试内字面量，零引用 PurPermissionSeed.Actions 或控制器常量。
- **豁免表空且断言**：核心闸无豁免旁路分支，reconcile 走 view 贴点被核心闸校验；注释显式记「豁免 0」。
- **计数精确 24 + 反向验证**：taggedCount==24 与 oracle==24 双闸，RED 实证移除即双破。
- **基类口径据实**：grep 核实 8 控制器全 `: ControllerBase`，注释与源一致（无 MES 那类失实注释）。
- **403 覆盖 7 高危键**：与先例断言口径（ObjectResult.StatusCode==403）一致 + 交叉核验绑真实端点。
- 测试输出干净。

## Concerns
1. **403 为进程内链路测**（非 HTTP e2e）：与本仓既有 403 先例同构，不经 Kestrel/[Authorize]；线上 401/403 由部署冒烟另证（沿用本项目历波「反射闸 + 部署冒烟」分工）。这是既定口径而非本任务缺口。
2. 无其它 concern。反射闸此后锁死 Pur 权限面，可交 fable 终审。
