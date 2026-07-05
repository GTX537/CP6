# WFS 版本治理 + 流程运维驾驶舱 设计

> 生成于 2026-07-05（brainstorming 已确认，WFS 深化四期 Spec ①）。背景：`Wf_FlowDef.cs:8` 自注「阶段1 简化：实例不存 schema 快照，按 FlowKey 取最新」——本 spec 还这笔正确性债，并补齐管理员运维面（当前 admin 仅 FlowAdmin/ApproverMap 两页，零实例运维能力）。
> 落码位：`CP6.Entity/DomainModels/Wf`、`CP6.Core/Services/Wf`、`CP6.WebApi`、`cp6.web/src/views/oa/{admin,designer}`。

---

## §0 背景、范围与决策

### §0.1 债的形态

引擎 `LoadSchemaAsync(inst.FlowKey)` 在 6+ 处调用点**每次动作重读最新 schema**（`FlowEngine.cs:85/160/194/322/361/433`）。改版发布即污染全部在途实例：删节点 → token 停在不存在的节点上卡死；改审批人/边 → 跑到一半行为漂移。一二三期后设计器越好用改版越频繁，债必须在编码期一并还掉。

### §0.2 范围（In / Out）

**In**：版本 pin + 发布语义（Def 多版本行、草稿/发布状态机、实例 pin）；设计器发布流（保存草稿/发布冻结/版本历史/State-Path 表级版本 diff）；驾驶舱三 tab（实例检索+版本分布 / job 运维 / 分析报表）；干预四动作（job 重放/取消、强制终止、重解析审批人、强制推进）；一次迁移。

**Out（→ §9 YAGNI）**：在途实例跨版本迁移（节点映射语义深坑，收敛=等自然终态或强制终止）；版本回滚为新发布（可用「从历史版本另存草稿」达成，不做一键回滚）；分析报表的自定义维度/导出（固定四报表先行）。

### §0.3 锁定决策（用户已拍板 2026-07-05）

| # | 决策 | 依据 |
|---|------|------|
| D1 | **版本 pin + 发布语义**：`Wf_FlowDef` 每次发布产生新版本行（`(TenantId,FlowKey,Version)` 唯一，已发布行 SchemaJson **不可变**）；设计器编辑草稿行，显式「发布」冻结；实例存 pinned `FlowDefId`，LoadSchema 按 pin 取 | 否决实例级快照（库体积每实例+一份 schema、版本分布统计靠 hash、发布语义仍缺失）；pin 让版本清单/对比/分布统计天然成立 |
| D2 | 驾驶舱干预 = **四动作全做**：job 重放/取消、实例强制终止、挂起单重解析审批人、强制推进；强制推进限 **platform-admin + 理由必填 + 系统代办双痕** | 用户选项确认 |
| D3 | 旧版本在途收敛 = **观察 + 强制终止**，不做跨版本迁移 | 节点映射 YAGNI |
| D4 | 版本对比 = **复用三期状态机投影**做 State/Path 表级 diff（比 JSON diff 可读），并行结构版本退化为 JSON 视图 diff | 双模式设计器的自然复用 |
| D5 | 分析报表并入驾驶舱「分析」tab，固定四报表（时长/瓶颈/超时率/驳回率），数据源 = `Wf_FlowFormTo` 读模型聚合 | 报表与运维共享检索基建 |

---

## §1 现状锚点（逆向真实，不编造）

- **Def 现状**：`Wf_FlowDef { FlowKey, FlowName, FormKey, SchemaJson, Version(int,=1), Enable, FunctionId?, FlowCode? }`（波③ plan 侦察 + 本期实读）；`Version` 字段已有但**从未多行**——`FlowEngine.cs:47` 按 `FlowKey && Enable` 取单行、`:435` 按 FlowKey 取单行（无版本过滤）。
- **实例**：`Wf_FlowInstance` 只存 `FlowKey`（无 DefId/Version）；RowVersion 已有（三期 subflow plan 已核）。
- **设计器保存**：`DesignerService.save` 直接改该 FlowKey 单行的 SchemaJson（保存即生效——这正是债）。
- **admin 现状**：`views/oa/admin/` 仅 `FlowAdmin.vue`（定义管理，菜单 734）与 `ApproverMapView.vue`；触发器 tab（波③ E-T2）落于 FlowAdmin。
- **强制终止可复用**：撤回清场语义 + 三期级联子实例（subflow spec §3.3）；**job 重放/取消可复用**：`Wf_ServiceJob` 状态机 + 错误路由 `FailServiceTokenAsync`。
- **审计**：OperLogFilter + FlowHistory 追加式 + 字段审计基建——干预双痕的落点。
- **权限**：MenuAction (menuKey, action) 二元组（波③ plan 已核）。
- **错误码水位**：三期用到 E-WF-028，本 spec 从 **E-WF-029** 起。
- **老化占坑**：基建 spec §4 已定义 OperLog 告警口径——驾驶舱 job tab 是其消费端 UI。

---

## §2 版本 pin 数据模型（一次迁移 `WfsVersionPin`）

### §2.1 `Wf_FlowDef` 改造

```csharp
/// <summary>版本状态：0=Draft(草稿,可编辑) / 1=Published(已发布,SchemaJson 不可变)。</summary>
public int Status { get; set; }   // WfFlowDefStatus 常量
/// <summary>发布时刻（Published 时置，Draft 为 null）。</summary>
public DateTime? PublishedAtUtc { get; set; }
/// <summary>发布人。</summary>
public Guid? PublishedBy { get; set; }
```

- 唯一索引改 `(TenantId, FlowKey, Version)`；辅助索引 `(TenantId, FlowKey, Status)`。
- **数据迁移**：既有行全部标 `Published`（`PublishedAtUtc = 迁移时刻`）——既有单行即 v1 已发布，行为无缝。
- `Enable` 语义收窄为 **FlowKey 级发起开关**（读口径：取「最新 Published 且 Enable」；关掉最新 published 的 Enable = 停止发起，在途不受影响）。
- **不可变铁律**：`Status==Published` 行的 SchemaJson/FlowName/FormKey 拒绝更新（服务层守卫 + 测试锁定）；可变的只有 Enable。

### §2.2 `Wf_FlowInstance` pin 列

```csharp
/// <summary>版本 pin：发起时刻固定的流程定义版本行。实例全生命周期按此行取 schema。</summary>
public Guid FlowDefId { get; set; }
```

索引 `(FlowDefId)`（版本分布统计键）。**数据迁移回填**：既有实例按 FlowKey 关联现存单行 Def 回填（迁移 SQL 内完成，无孤儿——若有孤儿 FlowKey 迁移即失败快速暴露）。

### §2.3 引擎改造

- `LoadSchemaAsync(string flowKey)` → **`LoadSchemaAsync(Wf_FlowInstance inst)`**（按 `inst.FlowDefId` 取行；全部 6+ 调用点改造，签名收敛防漏改）。
- `SubmitAsync`：解析「FlowKey 最新 Published 且 Enable」→ pin `FlowDefId` 进实例；无可用版本 → **E-WF-029**（结构化码）。子流程/触发器发起都经 SubmitAsync，自动继承 pin。
- 校验/设计器读取（validateClient 的服务目录等）不受影响——它们面向草稿。

---

## §3 设计器发布流

1. **打开**：加载该 FlowKey 的**最新草稿**；无草稿 → 从最新 Published **copy-on-write** 衍生草稿行（Version = 最大值+1，Status=Draft）；从未有任何行（新流程）→ 空白草稿 v1。
2. **保存**：只写草稿行 SchemaJson（现 save 行为收窄到 Draft，写 Published 行被 §2.1 守卫拒绝）。
3. **发布**：新端点/按钮——全族校验（FlowSchemaValidator + DesignerService 规则）通过 → 草稿 `Status=Published` + `PublishedAtUtc/By`；发布后再编辑 → 回到步骤 1 的 copy-on-write。发布并发冲突走 Def RowVersion（加 `[Timestamp]`，迁移含）。无草稿可发布/校验未过 → **E-WF-030**（聚合校验错误随响应返回）。
4. **版本历史**：设计器顶部版本下拉（vN·发布时间·发布人）——选历史版本**只读查看**（画布/状态机两模式均可）+「从此版本另存草稿」（回滚的达成方式，D4/§0.2 Out）。
5. **版本对比**（D4）：任选两版本 → **State/Path 表级 diff**（复用三期 `schemaToStateMachine` 投影：状态行/路径行的增删改三色标注）；任一版本含并行结构（capability=readonly）→ 退化为 SchemaJson 格式化 diff 视图。
6. 触发器（波③）与发布联动：触发器 FireAsync 的「FlowKey 无 enabled 流程」检查（E-WF-023）口径同步为「无 Published+Enable 版本」。

---

## §4 运维驾驶舱（`views/oa/admin/FlowOps.vue`，新菜单+三 tab）

### §4.1 实例检索 tab

- 筛选：状态（Running/Suspended/终态）/ FlowKey / **版本**（Def 版本下拉）/ 停泊超龄（停在同节点 > N 天）/ 发起人 / 日期范围。
- 列表列：单号/流程/版本/当前关卡/停留时长/发起人/状态；行点开 → FormDetail 跳转。
- **版本分布视图**：按 FlowKey 分组的「版本 × 在途数」矩阵（D3 收敛决策的观察面）。
- 后端：`IFlowOpsService.SearchInstancesAsync(filter, page)`（专用聚合查询，不复用收件箱查询——视角不同：全租户 vs 个人）。

### §4.2 job 运维 tab

- 筛选：状态（Failed/Running/Pending 退避中）/ **老化占坑**（TriggerFire InstanceId 与 Error 均空超宽限——基建 spec §4 告警的消费端）/ Kind / 日期。
- 动作：**重放**（Failed job → AttemptCount=0、Status=Pending、清 LastError——executor 幂等是铁律，重放安全）；**取消**（Failed/Pending job → Cancelled + `FailServiceTokenAsync` 走错误路由，等价重试耗尽处置）。job 处于 Running（有 lease）时两动作均拒（400，防与 worker 竞争）。

### §4.3 分析 tab（D5）

固定四报表，按 FlowKey + 日期范围：
1. **平均审批时长**（实例发起→终态，按流程分组，趋势折线）；
2. **瓶颈关卡 Top**（FlowFormTo 按 (FlowKey,NodeId) 聚合平均停留，条形图）；
3. **超时率**（超时动作触发数 / 关卡办结数）；
4. **驳回率**（Rejected 实例 / 终态实例，按流程）。

后端 `GetAnalyticsAsync(flowKey?, fromUtc, toUtc)` 一次返回四块聚合；前端图表遵循 Design System + dataviz 规范（执行时用 dataviz skill 校色/形制）；空数据空态。**只读，不引 BI 依赖**。

### §4.4 干预四动作（全部：独立权限 action + 双痕 + 理由）

| 动作 | 前置态 | 行为 | 权限 action |
|---|---|---|---|
| job 重放/取消 | §4.2 | §4.2 | `job-ops` |
| **强制终止** | 实例 Running/Suspended | 走撤回清场语义 + **级联取消子实例**（三期 subflow §3.3 复用）+ FlowHistory 行 `action="forceTerminate"`（操作者+理由） | `terminate` |
| **重解析审批人** | 实例 Suspended | 对挂起节点重跑 `IApproverResolver`——解析成功 → 生成待办、实例回 Running；仍失败 → 保持 Suspended + 返回原因 | `re-resolve` |
| **强制推进** | token 停泊/待办 Pending | 当前关卡按「系统代办通过」处置：在途待办 Cancelled、FormTo 行 Skipped、FlowHistory `action="forceAdvance"`（操作者+**理由必填**）→ AdvanceToken 沿正常出边 | `force-advance`，**仅 platform-admin**（复用 RequirePlatformAdmin 先例） |

全部动作：入参理由必填（重放/取消可选）、OperLog + FlowHistory 双痕、并发安全走实例/job RowVersion。

### §4.5 权限与菜单

新菜单 `oa-flow-ops`（OA 组，platform/租户管理员向）；actions：`view`（三 tab 查看）/ `job-ops` / `terminate` / `re-resolve` / `force-advance`。种子照波③ MenuAction 口径。

---

## §5 错误码

| 码 | 场景 | 层 |
|---|---|---|
| **E-WF-029** | 发起失败：该 FlowKey 无「Published 且 Enable」版本 | SubmitAsync 运行时统一抛出。**与波③ E-WF-023 的边界**：触发器**保存时**校验仍报 E-WF-023（配置语境）；**运行时** FireAsync 调 SubmitAsync 撞 E-WF-029 → 原码透传写入 TriggerFire.Error（不翻译成 023，保留根因） |
| **E-WF-030** | 发布失败：无草稿可发布 / 校验未过（聚合校验错误随响应） | 发布端点 |

驾驶舱干预动作的前置态不符走 HTTP 400 + 明细，不占 E-WF 码。

---

## §6 安全 / 多租户 / 向后兼容

- TenantId 全贯穿；驾驶舱查询严格租户内（platform-admin 亦然，跨租户运维不在本期）。
- **行为无缝迁移**：数据迁移把既有单行 Def 标 Published、在途实例回填 pin——迁移后所有既有流程行为与迁移前逐字节一致（回归锁定）；改动只在「下一次设计器编辑/发布」时显现。
- 27+ Wf 不变量测试全绿保持；`LoadSchemaAsync` 签名收敛使漏改点编译期暴露。

---

## §7 测试策略

- **pin 语义**：发布 v2 后在途 v1 单按 v1 跑到终态（删节点/改审批人两变体的定点回归——正是债的两个事故形态）；新单 pin v2；Enable 关闭停发起在途不受影响。
- **发布流**：copy-on-write 衍生、Published 不可变守卫、校验未过 E-WF-030、并发发布 RowVersion、「从历史另存草稿」。
- **驾驶舱**：检索过滤矩阵、版本分布计数、job 重放后 worker 正常拾起、取消走错误路由、Running job 拒操作、强制终止级联子实例+双痕、重解析成功/仍失败两态、强制推进的 FormTo/History/权限矩阵（非 platform-admin 403）。
- **分析**：四聚合的已知数据集断言、空态、日期边界。
- **QA harness**：gstack 剧本（发布 v2→旧单继续走 v1 实况走查、版本 diff 视图、驾驶舱四动作全流程、老化占坑筛选）。
- 基线全绿；EF 迁移恰一次 `WfsVersionPin`（含数据回填）。

---

## §8 分期 / 任务波次（供 writing-plans 细化）

- **V-A pin 内核**：Def/Instance 迁移+数据回填 + 引擎 LoadSchema 签名收敛 + SubmitAsync pin + E-WF-029 +「删节点/改审批人」定点回归。
- **V-B 发布流**：草稿/发布状态机 + 不可变守卫 + copy-on-write + E-WF-030 + 设计器版本下拉/发布按钮/历史只读。
- **V-C 版本 diff**：State/Path 表级 diff（消费三期 stateMachineModel）+ 并行退化 JSON diff。
- **V-D 驾驶舱检索+job 运维**：FlowOps 页 + SearchInstances/版本分布 + job 重放/取消 + 老化占坑消费端 + 权限种子。
- **V-E 干预三动作**：强制终止/重解析/强制推进（各自服务方法+端点+双痕+权限）。
- **V-F 分析 tab**：聚合端点 + 四报表前端。
- **V-G i18n + QA**：五语 seed（估 ~45 键）+ harness + DoD。

依赖：V-A → V-B → V-C；V-D → {V-E ‖ V-F}；V-C/V-F 依赖三期状态机投影/图表规范。**前置：二三期全部计划先行**（V-C 消费 stateMachineModel；强制终止级联消费 subflow §3.3）。

---

## §9 YAGNI / 留后

- 在途实例跨版本迁移（节点映射）；一键回滚（用「从历史另存草稿+发布」达成）。
- 分析自定义维度/导出 Excel/定时报表推送。
- 跨租户运维视图；发布审批流（流程的流程）。
- 版本保留策略（历史版本永久保留，体积可忽略——schema 是 KB 级）。

---

*生成于 2026-07-05。执行遵守铁律：引擎内写路径三律；E 波紧跟 D 波；干预动作全双痕；零跨模块污染。*
