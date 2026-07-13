### Task 9: 单格补码 UI

**Files:**
- Modify: `cp6.web\src\views\space\editor\panels\BindCodesDialog.vue`(unplaced 列表行加「补码」按钮)
- Modify: `cp6.web\src\api\space\codeRule.ts`(`genSingle:93-97` 已封装,零改动,仅引用)
- Test: `cp6.web\src\views\space\editor\__tests__\`(挨着既有)

**要点:** 行内按钮 `v-permission="'space-code-rule:generate'"`,点击 `codeRuleApi.genSingle(row.id)` → 成功后行内展示新码+刷新 unplaced 列表;失败靠 http 拦截器(照三生命周期页 catch 静默范式)。加载态防连点。

- [ ] **Step 1: 失败测试**(mock genSingle,点击后调用一次且行更新)
- [ ] **Step 2: 红 → 实现 → vitest 绿 + type-check 0 错**
- [ ] **Step 3: Commit + push**(`feat(space-web): 波5 单格补码UI——BindCodesDialog行内gen-code接线`)

---

