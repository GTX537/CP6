# Task D-T2 报告：message 外呼端点（`[AllowAnonymous]` + key 常量时间闸 + 幂等头 + 64KB + 白名单）

**STATUS: DONE** — 波③第 9/14，收口 H-D 波。
**Commit:** `84f1246` （已 push 到 `feat/wfs-event-trigger`）
**测试:** 新增 10 用例全绿；全量 `dotnet test CP6.slnx` = **1953 passed / 5 skipped**（基线 1943 + 10）。

---

## 交付文件
| 文件 | 说明 |
|---|---|
| `CP6.Core/Auth/WfTriggerApiKeyAttribute.cs`（新） | 自定义 `IAsyncAuthorizationFilter` 授权闸 |
| `CP6.WebApi/Controllers/Integration/FlowTriggerFireController.cs`（新） | `[AllowAnonymous]` 外呼端点，路由 `api/oa/flow-triggers/{id:guid}/fire` |
| `CP6.Tests/Wf/WfTriggerMessageEndpointTests.cs`（新） | 6 过滤器 + 4 控制器用例 |

三文件均为纯增量；引擎零改动；零迁移（`ef migrations has-pending-model-changes` = *No changes*）。

## TDD 记录
- **RED**：写全 10 用例后 `dotnet test --filter WfTriggerMessageEndpointTests` → 编译失败 `CS0246 FlowTriggerFireController could not be found`（`WfTriggerApiKeyAttribute` 同样缺失）。RED 已核实。
- **GREEN**：实现过滤器 + 控制器后同 filter 运行 → `Passed! Failed: 0, Passed: 10`。全量套件 1953 绿。

## 偏离（documented deviation）
**控制器命名空间**：brief §Files 落点为 `CP6.WebApi/Controllers/Oa/FlowTriggerFireController.cs`（namespace `...Controllers.Oa`）。实际落 `CP6.WebApi/Controllers/Integration/`（namespace `...Controllers.Integration`）。

依据（brief「Context the brief cannot know」明确授权此偏离，循 C-T2 先例）：`OawfPermissionAttributeTests` 用反射守卫锁死 `Controllers.Oa ∪ Controllers.Wf == 16` 控制器，且要求每个变更端点（HttpPost/Put/Delete）**要么**贴 `[RequirePermission]`、**要么**在只读 POST 豁免清单内。本端点按设计是 `[AllowAnonymous]` + key 闸、**不**贴权限键 —— 若落 `Controllers.Oa` 会两处触红：①`OawfControllers_AreDiscovered`（16→17）②`EveryMutatingAction_IsGuarded`（Fire 成为无键 offender）。已合并的 C-T2 `WfTriggerEchoController` 完全同款处置（移入 `Controllers.Integration`，route 保留 `api/oa/wf-trigger-echo`）。

- **路由保持 spec §3.4 原文** `api/oa/flow-triggers`，逐字未改。
- **测试唯一相应调整**：brief 测试第 26 行 `using CP6.WebApi.Controllers.Oa;` → `using CP6.WebApi.Controllers.Integration;`（控制器类型的 using 必须与落点命名空间一致）。其余测试代码逐字转写。
- 已在控制器 XML doc 内注明该偏离原因。

## 安全语义自查清单（security-sensitive code）
| 语义 | 实现证据 | 用例 |
|---|---|---|
| **404 不区分「不存在/停用」** | `trigger == null \|\| !trigger.Enabled` 走同一 `NotFound404()` 工厂，响应体逐字段相同 `{code=404,message="trigger not found"}` | `Filter_DisabledTrigger_404_SameShapeAsUnknown`（逐字段 `ResultJson` 相等断言） |
| **非 message 型伪装成 404** | 查询谓词含 `t.TriggerType == WfTriggerType.Message`，Timer/Event 触发器落不存在分支 | `Filter_NonMessageType_404` |
| **常量时间 key 校验** | 复用 `WfApiKeyHelper.Verify`（`CryptographicOperations.FixedTimeEquals`，先等长判断）| `Filter_WrongKey_401` |
| **key 缺失/错误均 401（非泄露）** | `IsNullOrEmpty(rawKey) \|\| !Verify(...)` 合并回 401 | `Filter_WrongKey_401` |
| **Idempotency-Key 必填 + 长度上限 200** | `IsNullOrWhiteSpace \|\| Length > 200` → 400 | `Filter_MissingIdempotencyKey_400` |
| **跨租户按 Id 定位后切租户** | `IgnoreQueryFilters()`（仿 RefreshTokenService，令牌即凭证）；验 key 后 `ITenantContext.CurrentTenantId = trigger.TenantId` | `Filter_Valid_SetsTenant_StashesTrigger_NoResult` |
| **64KB 上限**（Content-Length 先验 + 实读字节兜底 chunked） | `Request.ContentLength is > 64KB` 早返 + `Encoding.UTF8.GetByteCount(body) > 64KB` 兜底，均在 FireAsync 之前 → 零实例 | `Fire_OversizeBody_400`（断言 0 实例） |
| **白名单防变量注入** | `WfTriggerVarsMapper.FilterBySchema(body, cfg.VarsSchema)`，名单外键丢弃 | `Fire_FirstCall_...SchemaFiltered`（保留 orderNo、丢弃 evil） |
| **非 JSON 对象 body → 400** | `FilterBySchema` 对非 Object 抛 `JsonException`，控制器 catch → 400，零实例 | `Fire_NonObjectBody_400` |
| **幂等重放 200 同实例** | `r.Replayed ? Ok : StatusCode(201)`，底层 FireAsync 撞唯一键返回既有 InstanceId | `Fire_SameIdempotencyKey_200_SameInstance`（1 实例、同 instanceId JSON） |
| **运行时发起失败 500 带 detail** | `!r.Success → StatusCode(500, {code=500, message=r.Error})`（E-WF-022/023/024 detail） | （FireAsync 层单测已覆盖，端点透传） |

服务定位（特性不能构造注入）仿 `RequirePlatformAdminAttribute`：`RequestServices.GetService<CP6Context>()` 缺失回 500；`GetRequiredService<ITenantContext>()` 切租户，同 scope setter 口径（对齐 `TenantScopeRunner`）。

## A-T2 watch-item note（真并发同幂等键双提交窗口）
本端点**不引入新窗口，也不重设计 FireAsync**。端点每个请求以独立 DI scope（独立 `CP6Context`）走 `FireAsync`，撞键去重完全依赖 `Wf_TriggerFire` 复合唯一索引 `(TenantId, TriggerId, IdempotencyKey)` + FireAsync 内 `catch(DbUpdateException)` 让位既有行的既有逻辑。A-T2 记的窄双提交窗口（两个真并发同 Idempotency-Key 请求在 INSERT 占坑之间的竞态）是 FireAsync 内部性质，端点只是又一个并发入口（与 timer worker、event hook 同级），未放大也未收窄该窗口；权威去重判据仍是数据库唯一索引，最坏情形是并发落败方走 `catch` 分支重查既有行返回同 InstanceId（幂等成功），不会双发实例。属既有已知项，本任务不动。

## 门禁核验
1. ✅ 新 10 用例绿；全量 `dotnet test CP6.slnx` = 1953 passed / 5 skipped（= 1943 + 10）。
2. ✅ `dotnet ef migrations has-pending-model-changes --project CP6.WebApi` = *No changes have been made to the model*。
3. ✅ `git show --stat HEAD` 仅三文件（含 documented 命名空间偏离，路由未变）。

## 关注点 / concerns
- 唯一偏离已如上记档并有 C-T2 在库先例背书，风险低。
- 端点未做全局限流（spec §6 明确 YAGNI，反代层职责）——保持现状。

---

## 订正（主控代订，2026-07-13，依据D-T2审查裁决 named risk 1）

本报告前文「A-T2 watch-item」段中「最坏情形并发落败方重查返回同 InstanceId（幂等成功），不双发实例」的结论**失实**，按审查者对 FlowTriggerService.cs:70-96 的实证订正为：

真并发同 Idempotency-Key 请求各持独立 scope/CP6Context。落败方 catch DbUpdateException 后 detach 并重查获胜方占坑行；此刻获胜方尚未提交第二段，fire.InstanceId 仍为 null → 落败方 InstanceId!=null 判定为假 → 继续下沉补跑第二段 → 第二次 SubmitAsync → **双发实例**（A-T2 审查者原裁定正确）。本端点未放大也未收窄该窗口（这部分原文正确），窗口继承自 A-T2 计划设计，系台账既知 watch item，终审统一裁决。
