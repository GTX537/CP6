# Space 波3：生命周期 UI（编码规则管理 / 发布中心 / 集成事件监视）实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把「生码→预检→发布→停用→采纳→事件追踪」整条生命周期做出 UI——后端 CodeRuleController/LocationPublishController 全就绪、前端调用率 0% 的最后一块拼图。

**Architecture:** 基线 main=4fba486（波1/1.5/2+热修已并入）。三个新菜单页（904-906 续 900 段）走波2 建立的全套范式：viewModules 动态路由 + Cp* 模板 + `space.*` 点分 i18n + MERGE 种子。API 层新建 codeRule/publish/zone 三个封装（locate.ts 复用于停用搜索）。关键契约怪癖（探查已核实，计划内固化）：① GET code-rule 直出实体（Segments 是 JSON **字符串**）而 POST/PUT 收 DTO（Segments 是**数组**）——api 层做双向转换；② publish/events **无 total**——事件页用 paginated=false + 自制上下页；③ **409 E-SPACE-009 拦截器不 toast**——发布/停用调用点自行捕获弹「数据已被修改，请刷新」；④ 其余 E-SPACE-xxx 中文串经拦截器 `t(raw)` miss 回退原样展示，无需页面处理。

**Tech Stack:** 同波2（Vue3 + Cp* + vitest jsdom）。零后端改动。

## Global Constraints

- **照抄源**（先全文读）：列表页=`cp6.web/src/views/space/master/SpaceSiteView.vue`（波2 产物，同域最新范式）；表单弹窗=同文件 CpFormDialog 用法；只读列表=`cp6.web/src/views/wms/VmiView.vue`（paginated=false + #toolbar）；API 封装=`cp6.web/src/api/space/floor.ts`/`site.ts`；组件测试=波2 三页 spec（`src/views/space/**/__tests__/`）。
- **后端契约表以探查报告为准**（本计划各任务内嵌了关键形状；executor 不确定时直接读 Controller/DTO 源文件：CodeRuleController.cs / LocationPublishController.cs / CodeRuleDtos.cs / LocateDtos.cs）。
- Source 值域 12 个（下拉用，含分类）：码源 `fixed/site-code/floor-level/zone-code/aisle-code/rack-code`；序号源 `zone-seq/aisle-seq/rack-seq/col/level/depth`。巷道段（aisle-*）必须 Optional=true（E-305）；规则须含 Zone 区分段（zone-code|zone-seq|site-code+floor-level，E-303）与库位粒度段（col|level|depth，E-306）——**前端建规则时本地镜像校验**（提示不阻断，权威校验靠 preview 的 Precheck）。
- i18n：新键 `space.rule.*`/`space.publish.*`/`space.events.*`（+新增 common），每键进 Task 5 种子（五语）；**key 唯一权威=组件代码 grep**。
- 前端命令（cp6.web/）：type-check `NODE_OPTIONS=--max-old-space-size=8192 npm run type-check`；`npm run test`（基线 337）；`npm run build`（提交前必跑——vue-tsc --build 比 --noEmit 严）。
- 前车之鉴（波2 审查记档，全部适用）：slot 内 mono 需本组件补 `.cp-mono` scoped 类；create 类 api 签名用 `Partial<VO>`；`Promise.all` 聚合建议 allSettled；el-select 选项数组若含 `t()` 用 computed 包（切语言更新）。
- 菜单种子续 900 段（904/905/906，MenuKey space-code-rule/space-publish/space-events）；种子头注释连接串照 `docs/seeds/space-i18n-seed.sql`（-d CP6DB，波2 Task 7 实际执行的那套——**不要照 menu-seed 的 -d CP6 旧注释**，那是波2 记档的不一致点，本波顺手把旧文件注释也改了）。
- 提交 `feat(space):`/`docs(space):` + `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`，每 Task 一个 commit。

---

### Task 1: API 层（codeRule.ts / publish.ts / zone.ts + 类型 + Segments 转换单测）

**Files:**
- Create: `cp6.web/src/api/space/codeRule.ts`、`cp6.web/src/api/space/publish.ts`、`cp6.web/src/api/space/zone.ts`
- Modify: `cp6.web/src/types/space/scene.ts`（或就近新建 `types/space/lifecycle.ts`，取项目惯例）——新增本任务全部 VO
- Test: `cp6.web/src/api/space/__tests__/codeRule.spec.ts`（Segments 双向转换纯函数）

**Interfaces（Produces，后续任务全依赖）:**

```ts
// 类型（camelCase 对应后端 DTO）
export interface CodeSegmentDef { key: string; name: string; source: string; width: number; pad: string; start: number; step: number; sep: string; upper: boolean; fixedValue: string; optional: boolean }
export interface CodeRuleVO { id?: string; ruleName: string; scopeType: number; scopeId?: string | null; segments: CodeSegmentDef[]; isDefault: boolean }
export interface CodePreviewResp { structure: { key: string; name: string; source: string; optional: boolean }[]; samples: string[]; variableLen: { withAisle: string; withoutAisle: string }; precheck: { ok: boolean; errors: string[] } }
export interface CodePrecheckResp { emptyCodeCount: number; duplicateGroups: string[][]; precheckErrors: string[]; unplacedDraftCount: number }
export interface SpaceEventVO { id: string; hookName: string; sourceNo: string; targetModule: string; status: string; attempts: number; createDate: string; lastError?: string | null }

// codeRule.ts —— GET 直出实体（segments 为 JSON 字符串）→ list() 内部 JSON.parse 归一为 CodeRuleVO[]；
// create/update 提交 DTO（segments 数组直发）。抽两个可测纯函数：
export function parseRuleEntity(raw: { segments?: string; Segments?: string; [k: string]: unknown }): CodeRuleVO   // 实体→VO（segments JSON.parse，容错 "[]"/空/非法→[]）
export const codeRuleApi = { list(): CodeRuleVO[]; create(d: CodeRuleVO): {id}; update(id, d): void; remove(id): void;
  preview(segments: CodeSegmentDef[]): CodePreviewResp;                     // POST code-rule/preview（只发 segments，其余字段服务不读）
  generate(floorId: string, mode: 'fill-empty'|'rebuild', scopeZoneId?: string): string[];   // POST floor/{id}/generate-codes
  precheck(floorId: string, zoneId?: string): CodePrecheckResp;             // GET floor/{id}/code-precheck
  genSingle(locationId: string): { code: string } }                         // POST location/{id}/gen-code

// publish.ts
export const publishApi = { publishFloor(floorId: string, zoneId?: string): { published: number };   // POST floor/{id}/publish, body {zoneId}
  deactivate(locationId: string): void;                                     // PUT location/{id}/deactivate
  adopt(items: { code: string; attrs?: Record<string, unknown> }[]): { imported: number; skipped: string[] };   // POST location/adopt
  events(page: number, pageSize: number): SpaceEventVO[] }                  // GET publish/events（无 total）

// zone.ts
export const zoneApi = { list(floorId: string): ZoneVO[] }                  // GET /space/zone?floorId（ZoneVO 已有）
```

（全部返回 `Promise<Envelope<…>>`，Envelope 泛型写法照 floor.ts；上面是 data 形状速记。）

- [ ] **Step 1: 转换纯函数失败测试**（parseRuleEntity：合法 JSON→数组 / "[]"→[] / 非法串→[] 且不抛 / 大小写键容错——GET 直出实体经全局 camelCase 序列化，segments 键实际为小写 `segments`，测试锁定）
- [ ] **Step 2: RED 确认 → 实现三个 api 文件 + 类型**
- [ ] **Step 3: GREEN + type-check(8192) 0 + `npm run test` 全绿（337+新）**
- [ ] **Step 4: Commit** `feat(space): 生命周期 API 层（codeRule/publish/zone + Segments 双向转换）`（+尾注）

---

### Task 2: 编码规则管理页 SpaceCodeRuleView（本波最大单任务）

**Files:**
- Create: `cp6.web/src/views/space/lifecycle/SpaceCodeRuleView.vue`（+如需拆子组件 `SegmentsEditor.vue` 放同目录——超 300 行建议拆）
- Test: `cp6.web/src/views/space/lifecycle/__tests__/SpaceCodeRuleView.spec.ts`

**Interfaces:**
- Consumes: Task 1 `codeRuleApi`/类型；`siteApi.list`+`floorApi.list`+`zoneApi.list`（作用域级联下拉）
- Produces: 路由组件 `/space/code-rule`。行为规格：

| 项 | 规格 |
|---|---|
| 列表 | CpPageShell+CpListPage：ruleName / scopeType(kind:tag map 0→{租户默认,brand} 1→{楼层,info} 2→{库区,warn}) / scopeId 显示名（楼层/库区名——list 后并发解析，解析失败显示裸 id 截断）/ segments 段数 / isDefault(tag ok/muted) / _action(编辑/预览/削除) |
| fetch | `codeRuleApi.list()` 前端切片 |
| 表单弹窗 | ruleName(必填)；scopeType 下拉(0/1/2)；scopeId 级联：type=1→站点→楼层两级下拉、type=2→站点→楼层→库区三级、type=0 隐藏；isDefault 勾选（提示「设为默认将自动取消同作用域其他默认」——后端自动清，无冲突报错）；**SegmentsEditor**（内嵌） |
| SegmentsEditor | el-table 行内编辑或逐行 el-form：列=key(必填字母)/name/source(下拉 12 值域，分「码源/序号源/固定」组)/width(int)/pad(单字符)/start/step(仅序号源启用)/sep/upper(仅码源启用)/fixedValue(仅 fixed 启用)/optional(checkbox)；行操作 上移/下移/删除；「添加段」按钮；**本地镜像校验**（E-303/305/306 三条，页脚黄条提示不阻断保存——权威靠 preview） |
| 预览 | 弹窗内「预览」按钮（编辑中的 segments 直接 POST preview）+ 列表行「预览」（该规则 segments）：展示 samples 数组、variableLen 有/无巷道对比（mono 字体，**本地补 .cp-mono 类**）、precheck.ok 绿/errors 红列表 |
| i18n | `space.rule.*`（+common 复用），key 清单+五语进报告 |
| 测试 | ①mock list（含 segments 已归一数组）→ 渲染规则名与段数 ②新建弹窗添加两段→本地校验提示出现（缺库位粒度段时）③preview mock→samples 文本渲染 |

- [ ] **Step 1-5**：TDD（RED 组件不存在）→ 实现 → GREEN + type-check + build → Commit `feat(space): 编码规则管理页（段编辑器+实时预览+作用域级联）`（+尾注）

---

### Task 3: 发布中心页 SpacePublishView

**Files:**
- Create: `cp6.web/src/views/space/lifecycle/SpacePublishView.vue`
- Test: `cp6.web/src/views/space/lifecycle/__tests__/SpacePublishView.spec.ts`

**Interfaces:**
- Consumes: Task 1 `codeRuleApi.precheck/generate`、`publishApi.publishFloor/deactivate/adopt`、`zoneApi.list`；既有 `siteApi.list`/`floorApi.list`/`locateApi.search`（api/space/locate.ts:16，含 Status 字段）
- Produces: 路由组件 `/space/publish`。行为规格（自上而下五个区块）：

| 区块 | 规格 |
|---|---|
| ①作用域选择 | 站点→楼层级联下拉（顶栏；未选楼层则下方区块 CpEmpty 提示）+ 库区下拉（可空=整层，zoneApi.list(floorId) 填充）——三下拉同用于预检/生码/发布 |
| ②预检卡片 | 选楼层即自动 `codeRuleApi.precheck(floorId, zoneId?)`：CpStatCard×4（空码数[>0 danger 否则 ok]/重复码组数[同]/规则错误数[同]/未落位数[warn 中性——不挡发布]）+ precheckErrors 明细红列表 + 「重新预检」按钮 |
| ③生码 | mode 单选（fill-empty 默认/rebuild）+「生成编码」按钮：rebuild 先 ElMessageBox.confirm 警示「全量重排将清空并重生成所有草稿码」；成功 ElMessage.success 显示生成条数（返回 string[] 的 length）+ 自动重跑预检 |
| ④发布 | 「发布」按钮（zoneId 空=整层，有=按库区）：预检三门任一非零时按钮禁用+提示；成功显示 `published` 条数并重跑预检；**409 捕获**：`catch (e) { if (e?.response?.status === 409) ElMessageBox.alert(t('space.publish.conflict409')) }`（其余错误拦截器已 toast，勿重复）；旁挂「存量采纳」按钮→弹窗 textarea（每行一码）→ adopt → 结果显示 imported/skipped（skipped 列表逐码展示） |
| ⑤停用小节 | 码前缀搜索框（`locateApi.search(prefix, floorId)`，返回含 Status）→ 结果 el-table：code(mono，本地类)/Status(tag 0草稿 muted/1已发布 ok/2停用 danger)/「停用」按钮（仅 Status=1 显示）→ ElMessageBox.confirm → `publishApi.deactivate(id)` → 409 同④处理、其余靠拦截器 toast（E-401/W-404/E-405/E-004 原串中文展示）→ 成功后重搜刷新 |
| i18n | `space.publish.*`；key 清单+五语进报告 |
| 测试 | ①mock precheck→四卡数值渲染 ②预检非零时发布按钮 disabled ③mock deactivate 409 拒绝→alert 调用（spy ElMessageBox.alert）④adopt mock→imported/skipped 渲染 |

- [ ] **Step 1-5**：TDD → 实现 → GREEN + type-check + build → Commit `feat(space): 发布中心页（预检/生码/发布/采纳/停用五区块）`（+尾注）

---

### Task 4: 集成事件监视页 SpaceEventsView

**Files:**
- Create: `cp6.web/src/views/space/lifecycle/SpaceEventsView.vue`
- Test: `cp6.web/src/views/space/lifecycle/__tests__/SpaceEventsView.spec.ts`

**Interfaces:**
- Consumes: Task 1 `publishApi.events(page, pageSize)`（**无 total**）
- Produces: 路由组件 `/space/events`。规格：

| 项 | 规格 |
|---|---|
| 列表 | CpListPage `paginated=false`（fetch 返回当前页 rows，total=rows.length）：sourceNo(mono)/hookName(overflowTooltip)/targetModule/status(kind:tag map 六值：SUCCESS→ok/SKIPPED→muted/PENDING→info/FAILED→warn/DEAD→danger/COMPENSATED→muted)/attempts(num)/createDate(date)/lastError(#col slot：有值显示「詳細」按钮→ElMessageBox.alert 全文，无值 —) |
| 翻页 | #toolbar 自制：「前页」（page>1 启用）/「次页」（本页 rows.length==pageSize 才启用）/当前页码显示/pageSize 固定 50；page 变更重 fetch |
| 刷新 | 工具栏「刷新」按钮（FAILED 重试是 Worker 自动的，页面只读） |
| i18n | `space.events.*` |
| 测试 | ①mock events 两行→状态 tag 与行渲染 ②满页 50 行→次页按钮启用、点击后 page=2 二次调用 ③空页→次页禁用 |

- [ ] **Step 1-5**：TDD → 实现 → GREEN + type-check + build → Commit `feat(space): 集成事件监视页（六态标签+无total翻页）`（+尾注）

---

### Task 5: 路由登记 + 菜单种子（904-906）+ i18n 种子二弹

**Files:**
- Modify: `cp6.web/src/router/index.ts`（viewModules 加 3 条，追加在波2 Space 组内）
- Create: `docs/seeds/space-menu-seed-2.sql`（904-906，照 space-menu-seed.sql 骨架与显式列清单含 MenuKey）
- Create: `docs/seeds/space-i18n-seed-2.sql`（Task 2/3/4 全部新 key，grep 三组件为唯一权威，五语 MERGE）
- Modify: `docs/seeds/space-menu-seed.sql`（仅头注释连接串 -d CP6 → 与 i18n-seed 一致的 -d CP6DB 写法——波2 记档不一致点顺手修）

viewModules：

```ts
  '/space/code-rule': () => import('@/views/space/lifecycle/SpaceCodeRuleView.vue'),
  '/space/publish': () => import('@/views/space/lifecycle/SpacePublishView.vue'),
  '/space/events': () => import('@/views/space/lifecycle/SpaceEventsView.vue'),
```

菜单 INSERT（照波2 显式列清单变体）：904 `コード規則` `/space/code-rule` `space-code-rule` 图标 `Collection`；905 `発行センター` `/space/publish` `space-publish` 图标 `Promotion`；906 `連携イベント` `/space/events` `space-events` 图标 `List`（图标以 wms/oa/space 种子已出现过的名字优先，不确定换用已有先例名）。RoleMenus 900-919 区间授权语句波2 种子已含（BETWEEN 900 AND 919 幂等）——**新种子仍需自带同款授权块**（波2 种子只在它自己执行时授权当时存在的行）。

- [ ] **Step 1-4**：登记 → 两种子 → key 对账（grep 三组件全量，与 Task 2/3/4 报告清单 diff，差异以代码为准）→ type-check + test + build → Commit `feat(space): 生命周期三页路由+菜单904-906/i18n种子二弹`（+尾注）

---

### Task 6: 回归 + 真库 QA（波3 DoD）

**Files:** 无代码变更预期；缺陷则修复+测试单独 commit

- [ ] **Step 1: 回归门**：type-check(8192) 0 / `npm run test`（337+波3新增）/ `npm run build` / 后端 `dotnet test`（1557/5，本波零后端改动应不变）
- [ ] **Step 2: 真库种子**（CP6DB，容器内 curl/sqlcmd 模式照波2 Task 7 先例）：904-906 空段复核 → 执行两种子 → 验证（菜单 3 行/RoleMenus 3 行/新 i18n 键数与种子一致）
- [ ] **Step 3: API 级端到端**（容器网络内 curl，照波2 先例；发布链后端已有波1 冒烟 7/7 证据，本波验证**新 UI 封装对应的调用序列**）：登录（菜单含 904-906）→ POST code-rule（租户默认规则，segments 含 zone-code/col/level）→ POST preview（samples 非空 + precheck.ok）→ 选波2 QA 造的楼层 POST generate-codes（fill-empty，返回码数>0）→ GET code-precheck（emptyCodeCount=0）→ POST publish（published>0）→ GET publish/events（有 SUCCESS 行）→ POST adopt（1 码，imported=1）→ GET location/search?prefix= →（挑一 Status=1）PUT deactivate → 再 search 确认 Status=2
- [ ] **Step 4: 证据入报告 + 遗留记台账**；缺陷则 `fix(space): 波3走查修复——<问题>` 单独 commit

---

## 自检记录（写计划时已核）

- **波3 范围覆盖**：编码规则管理+预览（Task 2）/ 批量生码+预检（Task 3 ②③，单格 gen-code 端点封装在 Task 1 但 UI 不做——单格补码是编辑器域，记波5 票）/ 发布+停用+采纳（Task 3 ④⑤）/ 事件查看（Task 4）。明确不做：viewer/编辑器内的生命周期入口（波5）；FAILED 事件手动重试按钮（后端无端点，Worker 自动）；reconcile 采纳对账端点（后端未实现，契约 §8.2 记票依旧）。
- **契约怪癖四条**全部固化进 Global Constraints 与对应任务（Segments 双向转换/无 total/409 静默/错误串原样展示）。
- **决策留档（自洽）**：三页放 `views/space/lifecycle/` 新子目录；菜单续 904-906；events 用 paginated=false+自制翻页（CpListPage 无 total 契约的最小适配）；停用搜索用 location/search（探查推荐，含 Status）；本地镜像校验提示不阻断（权威=preview）。
- **类型一致性**：CodeSegmentDef 11 字段 camelCase ↔ 后端 CodeRuleDtos.cs:7；CodeRuleVO.segments 恒为数组（转换在 api 层，页面不见 JSON 字符串）；SpaceEventVO 八字段 ↔ events 匿名投影。
- **执行顺序**：1→6 串行（2/3/4 依赖 1；5 依赖 2/3/4 的 key；6 收尾）。
