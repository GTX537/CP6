# Space P3 · SP1 — P1 Viewer e2e 跑通(整合进既有 Playwright 框架) 设计

> 日期 2026-06-28。Space P3「运行态收尾 + 路径规划做真」三子项目之 **SP1**(SP1→SP2→SP3 顺序)。范围由用户拍板 = **(a) 最小**:让既有 14 个 viewer e2e 测试在真实栈上跑绿,**不扩 07/08、不把软断言改硬**(超出最小范围的留 SP1.5/后续)。

## 背景

- 既有 Space e2e:`cp6.web/tests/space-viewer.e2e.ts`(14 个 viewer 用例 = 加载/楼层列表/切层/点击/搜索定位/工具栏预设/跨层),**写了但从未跑**,且**在 testDir 之外**(`tests/` 非 `e2e/`),`npx playwright test` 不会发现它。它自带一套臆测的 `cp6_at` cookie 鉴权说明。
- **既有 Playwright 框架已就位**:`cp6.web/playwright.config.ts`(testDir=`./e2e`,baseURL=`http://localhost:5173`,串行 workers=1,两 project=`setup`→`chromium`[依赖 setup + `storageState: e2e/.auth/admin.json`])+ `e2e/auth.setup.ts`(admin/123456 登录→存 storageState,校验 `localStorage.token`)+ 既有业务 spec(golden-path / smoke-all-screens / erp-*)。
- 真实栈现成:后端 5177→`CP6DB_SpaceQA`(含 Space 演示数据)、前端 5173、`@playwright/test ^1.60.0` 已装。

## 目标

把 Space viewer e2e **整合进既有框架**并跑绿——复用既有登录会话与配置,而非另起炉灶或重复鉴权。交付物 = `npx playwright test`(chromium project)下 Space viewer 用例 **0 失败**(通过 + 有意 skip)。

## 范围

**做(In)**
1. 把 `tests/space-viewer.e2e.ts` 迁入 `e2e/space-viewer.spec.ts`(进 testDir + 改 `.spec.ts` 命名以被 `chromium` project 收录),**删除自带鉴权说明**,改为继承既有 `setup`→storageState 会话(同 golden-path 等既有 spec)。
2. **选择器对账**:把臆测选择器与真实组件对齐(详见映射表),修已知漂移点。
3. **IDs**:把 env 默认值改成**真实 QA 值**,使 `npx playwright test` 对 QA 库开箱即跑;仍可经 env 覆盖。
4. **跑绿**:对真实栈执行(必要时 `npx playwright install chromium`),确认 0 失败。

**不做(Out,因(a)最小)**
- 不扩 07 库存叠加 / 08 拣货路径·热图覆盖(留 SP1.5)。
- 不把「无 JS 错误 + waitForTimeout」软检查改成硬断言(仅做让其通过所必需的最小改动)。
- 不加 `webServer` 自动起栈(沿用既有配置「前提:5173/5177 已运行」)。
- 不引入 API 动态发现 site/floor(IDs 鲁棒性升级留后续;见决策 D1)。

## 架构 / 做法

**整合而非重建。** Space viewer spec 进入 `e2e/`,被既有 `chromium` project(`dependencies: ['setup']` + `storageState`)自动收录并鉴权:
- 删去 spec 内 `cp6_at` cookie 相关说明与对 `process.env` 鉴权的依赖;`openViewer()` 直接 `page.goto(viewer url)`,会话由 storageState 提供(admin 已登录)。
- `baseURL` 由 config 提供(`http://localhost:5173`),spec 内 `BASE_URL` 默认沿用但以 config 为准。
- 既有 ERP/golden-path spec **零改**——本变更只新增一个 spec + 复用既有 setup,串行(workers=1)互不干扰。

## 选择器对账(映射表)

| e2e 选择器 | 真实组件 | 状态 / 动作 |
|---|---|---|
| `canvas.viewer-canvas` | `FloorViewer.vue` | ✅ 存在 |
| `.viewer-loading` | `FloorViewer.vue`(`v-if="loading"`) | ✅ 存在(`openViewer` 等其隐藏判就绪) |
| `.viewer-toolbar .tb-btn` + 文本 `⌂ ≡ ⊕ ⬡ ⟳` | `FloorViewer.vue` 工具栏 | ✅ 文本符号与 setPreset/onHome/onOverview/onFocusSelected/toggleProjection 一致 |
| `.info-card` | `InfoCard.vue` | ✅ 存在 |
| `.floor-list` / `.floor-list__item` / `--active` | `FloorList.vue` | ✅ 类存在 |
| `data-floor-id`(N1-c 切层取非活动楼层用) | `FloorList.vue` | ❌ **不存在** → 加 `:data-floor-id="floor.id"` 到列表项(1 行;否则 ≥2 楼层时切层逻辑失效。当前 QA 仅 1 楼层 → N1-c 本就 skip,加之使测试在多楼层下诚实) |
| `.search-box .el-input__inner`(搜索输入) | `SearchBox.vue`(FloorViewer 包在 `.viewer-searchbox`) | ✅ **已核**:SearchBox 用 `el-input`(Element Plus),`.search-box .el-input__inner` 正确。注:SearchBox 有 `.sb-mode` 模式选择(code/物料),默认 code,e2e 填编码+Enter 即走编码定位 |
| `.search-candidates` / `.search-candidate-item` | `SearchBox.vue` 候选下拉 | ✅ 类存在(N3-b/c 为软检查,无数据不失败) |

> **唯一确定漂移点 = `data-floor-id`(新增 1 行)**;搜索/工具栏/info-card/canvas/loading/floor-list 选择器均已核对齐。

## IDs(决策 D1)

- e2e 当前默认 `SPACE_SITE_ID='test-site-id'` 等占位 → 改默认为真实 QA 值:`SPACE_SITE_ID=F31F48C2-81D5-4BA7-AFF1-83DA8D87C2FE`、`SPACE_FLOOR_ID=5C92E6A8-C4C8-4D91-9DDC-EA9C54B6961F`、`SPACE_LOCATION_CODE=A-01-01-01`;仍可经 env 覆盖。
- **权衡**:开箱即跑(对 QA 库)vs 绑定 QA 专属 GUID(若 QA 重灌则需改默认或传 env)。最小范围取「真实默认 + 可覆盖」;**API 动态发现**(setup 步查首个已发布 site/floor/code,免 GUID 维护)为后续鲁棒性升级,不在 SP1。

## 运行前提与预期

- 后端 5177→`CP6DB_SpaceQA`(已有 Space 演示数据)、前端 5173 均在跑;`npx playwright install chromium`(若未装)。
- 运行:`npx playwright test e2e/space-viewer.spec.ts`(经 `chromium` project,自动先跑 `setup` 鉴权)。
- **预期 = 0 失败**:通过的用例 + **有意 skip**:`N1-c`(仅 1 楼层 → 跳切层)、`CROSS`(无 `SPACE_OTHER_FLOOR_CODE` env → 跳跨层)、`Manual inspection`(无 `SPACE_MANUAL` → 跳)。N3-b/c 软检查无候选数据时不失败。

## 测试 / 验证

- 交付物本身即「测试跑绿」。验证 = Playwright 运行 0 失败 + 既有 ERP/golden-path/smoke spec 不受影响(本变更只 +1 spec、复用 setup、串行)。
- 失败兜底:若个别用例因真实栈时序(冷后端 ~5s)超时,放宽该用例等待/选择器至能稳过即可(不改其语义)。固化运行记录于 `docs/superpowers/qa/space-p3-sp1/`。

## 决策记录

- **D1 IDs**:真实 QA 默认 + env 可覆盖(非 API 动态发现)——最小范围。
- **D2 位置**:迁入 `e2e/` 复用既有 config/auth(非留 `tests/` 自建)——无悬念。
- **D3 起栈**:假定 5173/5177 已运行(沿用既有 config),不加 webServer。
- **范围**:(a) 最小;不扩 07/08、不软改硬。

## 分支 / 落地

- 在 `feat/space-p3-hardening`(基于 Space 00~08 的 `2fba946`)。`feat/space-p1-backend` 冻结于 `2fba946` 作 00~08 落 main 之用,本 P3 工作不污染它。
