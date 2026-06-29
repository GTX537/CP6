# Space P3 · SP1 — P1 Viewer e2e 跑通 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development 或 superpowers:executing-plans 逐任务实施。步骤用 `- [ ]` 勾选。本计划**建议 inline 执行**(依赖本机已起的真实栈 + 跑-修迭代,subagent 难共享 live 浏览器/栈)。

**Goal:** 把既有 Space viewer e2e(`tests/space-viewer.e2e.ts`,14 用例)整合进既有 Playwright 框架并在真实栈上跑绿(0 失败 = 通过 + 有意 skip)。

**Architecture:** 整合而非重建——迁入 `e2e/`(进 config 的 testDir)+ 改 `.spec.ts` 命名被 `chromium` project 收录 + 复用既有 `auth.setup.ts` storageState 鉴权(删自带 cp6_at 说明)。唯一真选择器漂移=`FloorList` 缺 `data-floor-id`(+1 行)。env 默认 IDs 改真实 QA 值。

**Tech Stack:** `@playwright/test ^1.60.0`(已装)/ `playwright.config.ts`(testDir=`./e2e`,projects `setup`→`chromium`+storageState)/ Vue3 前端 5173 / 后端 5177→`CP6DB_SpaceQA`。

**配套 spec(必读):** `docs/superpowers/specs/2026-06-28-space-p3-sp1-viewer-e2e-design.md`(§范围=(a)最小;选择器映射表;IDs 决策 D1)。

---

## 通用约定

- **分支/worktree**:`D:\CP6-space-backend` @ `feat/space-p3-hardening`(基于 Space 00~08 的 `2fba946`;`feat/space-p1-backend` 冻结作落 main 之用,别动)。Bash cwd 每次重置回 `D:\CP6`,前端命令前缀 `cd /d/CP6-space-backend/cp6.web && ...`。
- **真实栈**:后端 5177→`CP6DB_SpaceQA`(已含 Space 演示数据:Site `F31F48C2-81D5-4BA7-AFF1-83DA8D87C2FE` / Floor `5C92E6A8-C4C8-4D91-9DDC-EA9C54B6961F` / 已发布编码 `A-01-01-01`)、前端 5173,均需在跑。
- **范围铁律**:(a)最小——只让既有 14 用例跑绿;不扩 07/08,不把软检查改硬(仅做让其稳过所必需的最小放宽)。
- **commit**:每 Task 末本地 commit(不 push;push 由用户自跑)。

---

## File Structure

- **Move**: `cp6.web/tests/space-viewer.e2e.ts` → `cp6.web/e2e/space-viewer.spec.ts`(进 testDir + 改名被 chromium project 收录;删自带鉴权说明;默认 IDs 改真实 QA;`openViewer` 用相对路径走 config baseURL)
- **Modify**: `cp6.web/src/views/space/viewer/FloorList.vue`(列表项加 `:data-floor-id`)
- **Create**: `docs/superpowers/qa/space-p3-sp1/README.md`(跑绿记录)

---

## Task 1：迁移并适配 Space viewer spec 进 e2e/

**Files:**
- Move: `cp6.web/tests/space-viewer.e2e.ts` → `cp6.web/e2e/space-viewer.spec.ts`
- (无新代码逻辑——既有 14 用例体原样保留,仅改头部/默认值/openViewer)

- [ ] **Step 1: git mv 到 e2e/**

```bash
cd /d/CP6-space-backend && git mv cp6.web/tests/space-viewer.e2e.ts cp6.web/e2e/space-viewer.spec.ts
```

- [ ] **Step 2: 替换文件头(删自带鉴权/前提说明 + import,保留为整合版)**

把文件**开头到 `const VIEWER_URL = ...` 行(含)** 整段替换为:
```typescript
/**
 * Space Viewer P1 Closed-Loop E2E (05 渲染 / 06 定位)
 *
 * 整合进既有 Playwright 框架:由 playwright.config.ts 的 chromium project 收录,
 * 鉴权复用 e2e/auth.setup.ts 的 storageState(admin 已登录),无需本文件自行登录。
 *
 * 前提(同既有 e2e):前端 5173 + 后端 5177(→ CP6DB_SpaceQA)已运行。
 * 默认 IDs 指向 QA 演示数据,可经 env 覆盖(SPACE_SITE_ID / SPACE_FLOOR_ID /
 * SPACE_LOCATION_CODE / SPACE_OTHER_FLOOR_CODE)。
 * 运行:npx playwright test e2e/space-viewer.spec.ts --project=chromium
 */

import { test, expect, type Page } from '@playwright/test'

const SITE_ID = process.env['SPACE_SITE_ID'] ?? 'F31F48C2-81D5-4BA7-AFF1-83DA8D87C2FE'
const FLOOR_ID = process.env['SPACE_FLOOR_ID'] ?? '5C92E6A8-C4C8-4D91-9DDC-EA9C54B6961F'
const LOCATION_CODE = process.env['SPACE_LOCATION_CODE'] ?? 'A-01-01-01'
```

(即:删掉原 6~33 行的大段前提注释 + `BASE_URL` 常量 + `VIEWER_URL` 常量;`BASE_URL` 改由 config baseURL 提供。)

- [ ] **Step 3: openViewer 改相对路径(走 config baseURL)**

把 `openViewer` 改为:
```typescript
async function openViewer(page: Page, floorId = FLOOR_ID): Promise<void> {
  await page.goto(`/space/viewer/${SITE_ID}?floorId=${floorId}`)
  await page.waitForSelector('.viewer-loading', { state: 'hidden', timeout: 20000 })
}
```

- [ ] **Step 4: 修复对 `VIEWER_URL` 的引用(底部 Manual inspection 块)**

底部 `Manual inspection` 块里 `await page.goto(VIEWER_URL)` 改为:
```typescript
    await page.goto(`/space/viewer/${SITE_ID}?floorId=${FLOOR_ID}`)
```

- [ ] **Step 5: 类型校验(确认无悬空引用)**

Run: `cd /d/CP6-space-backend/cp6.web && npx tsc --noEmit -p tsconfig.json 2>&1 | grep -i "space-viewer" | head` (或 `npx vue-tsc --noEmit` 若 e2e 纳入)
Expected: 无 `space-viewer.spec.ts` 相关 `VIEWER_URL`/`BASE_URL` 未定义错误。

> 注:若 tsconfig 不含 `e2e/`,Playwright 用自带 ts 转译运行,跳过此步,改在 Task 3 由 playwright 报错暴露。

- [ ] **Step 6: Commit**

```bash
cd /d/CP6-space-backend && git add cp6.web/e2e/space-viewer.spec.ts && git commit -m "test(space-p3-sp1): T1 Space viewer e2e 迁入 e2e/ 整合既有框架(复用 auth.setup + 真实 QA 默认 IDs)"
```

---

## Task 2：FloorList 加 `data-floor-id`(N1-c 切层用)

**Files:**
- Modify: `cp6.web/src/views/space/viewer/FloorList.vue`

> N1-c 切层用例靠 `data-floor-id` 找非活动楼层。当前 QA 仅 1 楼层 → 该用例本就 skip,但加此属性使其在 ≥2 楼层下诚实。1 行加法。

- [ ] **Step 1: 读 FloorList.vue 定位列表项**

Run: `cd /d/CP6-space-backend && grep -nE "floor-list__item|v-for|floor\." cp6.web/src/views/space/viewer/FloorList.vue | head`
确认列表项元素(带 `class="floor-list__item"` 的 `v-for` 项)及楼层对象字段名(应为 `floor.id`)。

- [ ] **Step 2: 加 `:data-floor-id`**

在 `.floor-list__item` 元素上加属性绑定(示例,按实际 v-for 变量名落地):
```html
:data-floor-id="floor.id"
```
（即该 `<div class="floor-list__item" ...>` 增加 `:data-floor-id="floor.id"`;`--active` 项亦自然带上。）

- [ ] **Step 3: 类型/构建校验**

Run: `cd /d/CP6-space-backend/cp6.web && npx vue-tsc --noEmit 2>&1 | tail -3`
Expected: 0 error。

- [ ] **Step 4: Commit**

```bash
cd /d/CP6-space-backend && git add cp6.web/src/views/space/viewer/FloorList.vue && git commit -m "test(space-p3-sp1): T2 FloorList 列表项加 data-floor-id(e2e 切层用例定位)"
```

---

## Task 3：装 chromium + 对真实栈跑绿(跑-修迭代)

**Files:**
- (修复期可能微调 `cp6.web/e2e/space-viewer.spec.ts` 的等待/选择器,仅放宽不改语义)

- [ ] **Step 1: 确认真实栈在跑**

后端:`curl -s -m4 -o /dev/null -w "%{http_code}" http://localhost:5177/api/space/floor/5C92E6A8-C4C8-4D91-9DDC-EA9C54B6961F/scene`(需带 admin cookie;或确认 `dotnet run` 进程在,5177 LISTENING)。前端:`netstat -ano | grep :5173`。
缺则起:后端 `cd /d/CP6-space-backend && dotnet run --project CP6.WebApi --urls http://localhost:5177`(env `ConnectionStrings__DefaultConnection` 指 `CP6DB_SpaceQA` 或读 appsettings.Local.json);前端 `cd /d/CP6-space-backend/cp6.web && npm run dev`。

- [ ] **Step 2: 装 Playwright chromium**

Run: `cd /d/CP6-space-backend/cp6.web && npx playwright install chromium`
Expected: 下载/已存在 chromium。

- [ ] **Step 3: 首跑 Space viewer spec**

Run: `cd /d/CP6-space-backend/cp6.web && npx playwright test e2e/space-viewer.spec.ts --project=chromium --reporter=list 2>&1 | tail -40`
Expected: 自动先跑 `setup`(auth.setup 登录 admin/123456 存 storageState)→ 再跑 14 用例。**目标 0 失败**;`N1-c`(1 楼层)/`CROSS`(无 env)/`Manual`(无 env)有意 skip。

- [ ] **Step 4: 跑-修迭代(若有失败)**

按失败类型最小放宽(**不改用例语义**):
- **时序超时**(冷后端 ~5s 首调):该用例 `waitForSelector`/`expect` 超时已 20s/15s 多够;若仍超时,确认后端就绪(先 curl 预热一次)再跑。
- **选择器找不到**:对照真实组件改选择器(spec 映射表已核;若 `.search-box .el-input__inner` 在某 Element Plus 版本下结构不同,改用 `.viewer-searchbox input`)。
- **InfoCard 未现**(N3-a 定位后):确认 `LOCATION_CODE=A-01-01-01` 在 QA 库已发布(已验);flyTo ~800ms,`expect(infoCard).toBeVisible({timeout:3000})` 够。
重跑 Step 3 至 0 失败。

- [ ] **Step 5: 确认既有 e2e 不受影响(可选抽查)**

Run: `cd /d/CP6-space-backend/cp6.web && npx playwright test e2e/smoke-all-screens.spec.ts --project=chromium --reporter=line 2>&1 | tail -5`
Expected: 既有 smoke 仍按其原状(本变更只 +1 spec 复用 setup,不应影响)。若既有 spec 因栈/数据原因本就不绿,记录但不归因本任务。

- [ ] **Step 6: Commit(若修复期有改动)**

```bash
cd /d/CP6-space-backend && git add cp6.web/e2e/space-viewer.spec.ts && git commit -m "test(space-p3-sp1): T3 Space viewer e2e 真实栈跑绿(修选择器/时序)"
```
(无改动则跳过 commit。)

---

## Task 4：固化跑绿记录

**Files:**
- Create: `docs/superpowers/qa/space-p3-sp1/README.md`

- [ ] **Step 1: 写记录**

`docs/superpowers/qa/space-p3-sp1/README.md`:环境(5173/5177/CP6DB_SpaceQA)、运行命令(`npx playwright test e2e/space-viewer.spec.ts --project=chromium`)、结果(N 通过 / M skip[列出 N1-c/CROSS/Manual 原因] / 0 失败)、修复点(若有)、已知留点(IDs 绑 QA GUID,重灌需改默认或传 env——见 spec D1)。

- [ ] **Step 2: Commit**

```bash
cd /d/CP6-space-backend && git add docs/superpowers/qa/space-p3-sp1/ && git commit -m "test(space-p3-sp1): T4 viewer e2e 跑绿记录固化"
```

---

## Self-Review(对照 spec)

- **spec §范围(a)最小** → T1~T4 只迁移+对账+跑绿,无 07/08 扩展、无软改硬。✅
- **spec 整合既有框架** → T1 迁入 e2e/ + 复用 auth.setup storageState(删自带鉴权)。✅
- **spec 选择器对账(唯一漂移 data-floor-id)** → T2 加属性;搜索/工具栏/info-card 已核无需改(T3 修复期兜底放宽)。✅
- **spec IDs 决策 D1(真实默认+可覆盖)** → T1 Step 2 默认改 QA 值 + env 覆盖。✅
- **spec 预期 0 失败(有意 skip)** → T3 目标 + skip 清单(N1-c/CROSS/Manual)。✅
- **Placeholder 扫描**:无 TBD;命令/路径具体;T1 用 git mv + 具体 edit(既有用例体原样移动,非占位)。✅
- **一致性**:`SITE_ID/FLOOR_ID/LOCATION_CODE` 常量名贯穿 T1 头部与 openViewer/Manual 引用一致;`--project=chromium` 全程一致。✅

---

## 执行顺序

T1(迁移适配)→ T2(data-floor-id)→ T3(装 chromium + 跑绿迭代,**核心**)→ T4(固化)。**建议 inline 执行**(用本机已起真实栈跑 Playwright 跑-修循环)。
