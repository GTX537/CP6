# WFS 波③ 终审修复轮报告（feat/wfs-event-trigger）

日期：2026-07-13 ｜ 实施者：终审修复轮 subagent ｜ 范围：C-1 / I-1 / M-4 / M-2 / harness 勘误（严格五项，零越界）

## 一、TDD RED 实证

靶向命令：`dotnet test CP6.Tests --filter "FullyQualifiedName~CsrfHubExemptionTests|FullyQualifiedName~FlowTriggerValidatorTests"`

修复前（RED）：**Failed: 3, Passed: 22, Total: 25**，红的恰好是三个新用例：

| 用例 | RED 失败点 |
| --- | --- |
| `CsrfHubExemptionTests.FlowTriggerFirePath_ShapeExactExemption("/api/oa/flow-triggers/3fa85f64-…-2c963f66afa6/fire", true)` | 现行 IsExempt 只豁免 /api/auth/login 与 /hubs → 返回 false，断言失败（= 生产 403 E-SEC-010 的测试面复现） |
| 同上大写变体 `/API/OA/FLOW-TRIGGERS/…/FIRE` | 同上（大小写不敏感一并锁定） |
| `FlowTriggerValidatorTests.Timer_NeverFiringCron_EWF022`（cron `0 0 30 2 *`） | 校验器只查 IsValid，语法合法即放行，不抛 E-WF-022 → Assert.ThrowsAsync 失败 |

其余 7 个负例（裸集合 / {guid} / manual-fire / enable / reset-key / 非 GUID id / fire 后多余段）在 RED 轮即绿——证明它们是防回归护栏而非现状描述。

## 二、修复内容

### C-1（Critical）CSRF 形状精确豁免 fire 端点
`CP6.WebApi/Middleware/CsrfMiddleware.cs`：
- `IsExempt` 增加第三分支 `IsFlowTriggerFirePath(path)`；
- 新增 `internal static bool IsFlowTriggerFirePath`：前缀 `/api/oa/flow-triggers/` + 字面尾 `/fire`（均 OrdinalIgnoreCase，与既有 PathMatches 风格一致）+ 中段 `Guid.TryParse` 判据（与路由约束 `{id:guid}` 同判据；含 `/` 必然解析失败，杜绝多段穿透）+ 长度护栏（防 `/api/oa/flow-triggers/fire` 这类重叠路径的负长 Substring）；
- 注释落安全论据：fire 端点以自定义头 X-Api-Key 认证（跨站 HTML 无法设置自定义头）且无环境 cookie 凭据，CSRF 攻击模型不成立；同级管理端点（create/update/enable/reset-key/manual-fire）cookie 认证，严禁放宽为前缀豁免。

### I-1（Important）永不触发 cron 保存拒绝
`CP6.Core/Services/Wf/FlowTriggerValidator.cs` Timer 分支 IsValid 之后追加：
```csharp
if (WfCronHelper.NextUtc(cfg.Cron, DateTime.UtcNow) == null)
    throw new InvalidOperationException("E-WF-022: cron 表达式永不触发");
```
堵死「语法合法但 NextDueUtc=null 入库静默死掉（无流水、无报错、扫描永不拾取）」路径。

### M-4 陈旧计数注释（纯注释）
- `WfTriggerEchoController.cs:10`：计数锁 16 → **17**；
- `OawfPermissionAttributeTests.cs:33-34`：16 个控制器 → **17**、其余 15 个（Oa 全 11…）→ **其余 16 个（Oa 全 12…）**。断言零改动。

### M-2 resetKey 未捕获 rejection（纯前端）
`cp6.web/src/views/oa/admin/FlowTriggerPanel.vue`：`ElMessageBox.confirm` 包 try/catch，**catch 即 return**（不能用 `.catch(() => {})` 吞掉后继续——取消后绝不能继续 reset-key）。

### Harness 勘误（纯注释/文档）
- `qa_flow_trigger.ps1:30-35`：改述为「fire 端点在生产 CSRF 开启下能通，靠的是中间件形状精确豁免（终审 C-1），CSRF 对兄弟管理端点仍然适用」；
- `README.md` §2.3：同口径修正 + 新增**部署冒烟必做项**：部署后从无 cookie 外部进程（curl 全新会话，仅 X-Api-Key + Idempotency-Key 头，真实 API key）打一发期望 201——这正是三层测试全部漏掉的生产路径。

## 三、GREEN 全闸实证

| 闸 | 结果 |
| --- | --- |
| 靶向两测试类 | 25/25 绿（RED 时 22/25） |
| 全量 `dotnet test CP6.slnx` | **Passed 1979, Failed 0, Skipped 5**（1969 + 新增 10 = 9 CSRF InlineData + 1 校验器用例） |
| `bun run test -- --run` | **425/425 绿**（64 文件） |
| `bun run type-check`（NODE_OPTIONS=8192） | exit 0 |
| `bun run build` | exit 0（仅既有 chunk>500kB 警告） |
| `dotnet ef migrations has-pending-model-changes` | No changes（零迁移） |

## 四、改动面核对

git diff --stat = 恰好简报允许的 9 文件，77 插入 / 8 删除；引擎零 diff、零迁移、既有断言零改动（仅注释）。

## 五、遗留关注

- C-1 的**线上实证**待部署后按 README 新增冒烟项执行（无 cookie curl → 201）——本轮只有单元层面（IsExempt 纯函数）证据，与波① T11 hub 豁免同款局限。
- `IsFlowTriggerFirePath` 用 `Guid.TryParse`（接受 N/B/P 格式），与 ASP.NET `{id:guid}` 路由约束判据一致；若未来路由约束收紧为 D 格式，此处须同步。
