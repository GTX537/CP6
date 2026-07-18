### Task 2-6: v-permission 铺设（每模块一任务，共同规程）

**模块分工与真相源：**

| Task | 模块/视图目录 | 真相源键表 | oracle 测试 |
|---|---|---|---|
| T2 | `cp6.web/src/views/oa` + `views/wf` | `docs/seeds/oawf-permission-keys.md` | `OawfPermissionAttributeTests` |
| T3 | `views/erp` | `docs/seeds/erp-permission-keys.md`（无则 grep docs/seeds erp） | `ErpPermissionAttributeTests` |
| T4 | `views/mes` | `docs/seeds/mes-*.md` | `MesPermissionAttributeTests` |
| T5 | `views/fin` | `docs/seeds/fin-*.md`（无则以 oracle 测试内清单为准） | `Fin*PermissionAttributeTests` |
| T6 | `views/pur` + `views/plan`（PLAN/PUB 前端页在 plan 目录；pub-codegen/pub-seq 若有独立页一并） | `docs/seeds/pur-*.md` + `plan-pub-*.md` | `PurPermissionAttributeTests` + `PlanPubPermissionAttributeTests` |

**共同规程（每个任务逐字适用；示例为形态说明，键名以各模块真相源为准）：**

- [ ] **Step 1: 建按钮-键映射清单（先盘后改）**

读真相源键表，得该模块全部 `menu-key:action` 集。逐视图文件扫描**变更动作触发点**：调用 POST/PUT/PATCH/DELETE API 的按钮、菜单项、开关、行内操作链接。产出映射清单（视图文件 → 元素 → 键）写入任务报告。规则：
1. 键**只取自真相源清单**，与控制器贴点逐字一致；找不到对应键的按钮=该端点未贴键（组件/只读POST豁免面），**不贴指令并在报告豁免小节列明**；
2. 纯读操作（查询/翻页/导出预览/刷新/展开）不贴；
3. 入口按钮贴，已被入口守住的对话框内确认按钮不重复贴；
4. 一个按钮对应多端点取**主动作**的键（报告注明）。

- [ ] **Step 2: 加指令**

WMS 样板形态（`views/wms/StocktakeView.vue:86`）：
```html
<el-button v-if="canApprove" v-permission="'wms-stocktake:approve'" type="success" @click="onApprove">
```
既有 `v-if` 业务条件保留并列；只加 `v-permission="'<key>'"` 字面量，零脚本/样式/结构改动。

- [ ] **Step 3: 前端三连验证**

```
cd cp6.web
npx vue-tsc --noEmit
npx vitest run
npm run build
```
预期：type-check 零错 / vitest 全绿（基线数在任务报告记录）/ build 过。

- [ ] **Step 4: Commit + push**

```
git add cp6.web/src/views/<mod>
git commit -m "feat(web): <MOD> v-permission 铺设——<N>按钮×<M>视图, 键与后端贴点逐字对齐"
git push
```

---

