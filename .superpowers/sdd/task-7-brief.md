### Task 7: 生命周期页错误呈现统一(前端)

**Files:**
- Create: `cp6.web\src\i18n\tOr.ts`(回退助手)
- Modify: `cp6.web\src\views\space\lifecycle\SpacePublishView.vue`(47-54 统计卡区、181-183 watcher else、198-234 runPrecheck/onGenerate)
- Modify: `cp6.web\src\views\space\lifecycle\SpaceEventsView.vue`(108-110 showError)
- Test: `cp6.web\src\views\space\lifecycle\__tests__\`(挨着既有测试)

**要点(四项):**
1. `tOr(key, fallback?)` 助手:`te(key) ? t(key) : (fallback ?? key)`(vue-i18n `te` 判存;导出纯函数,组件内用 `useI18n` 组合)。
2. **duplicateGroups 明细展开**:统计卡下加 `<el-collapse>`(仅 `pc.duplicateGroups.length>0` 时渲染),逐组列出重复码与所在库位 id 列表(数据已在 precheck 响应,照后端 DTO 字段名)。
3. **precheckErrors 旁「去编码规则页」链接**:`<router-link :to="'/space/code-rule'">`(路由 `:180`),文案走 i18n key `space.publish.goCodeRule`(五语补 `cp6.web\src\i18n\locales\*` 的 space 命名空间,照波3 键组织)。
4. **onGenerate/watcher seq 守卫**:`onGenerate` 响应处理前比对 `pcSeq` 快照(照 `runPrecheck:198-212` 的守卫写法);watcher else 分支(181-183)清空 `precheck` 时同步 `pcSeq++`,防在途 precheck 回填复活。
5. EventsView `showError`:`row.lastError` 经 `tOr` 后展示(E-SPACE 码词条化,非码原样)。

- [ ] **Step 1: 失败测试**(tOr 三例:已注册 key→译文/未注册→fallback/未注册无 fallback→key;PublishView 挂载态:dupGroups>0 渲染 collapse、errors>0 渲染 router-link)
- [ ] **Step 2: 红 → 实现 → `npm run test` 绿 + `npm run type-check` 0 错**
- [ ] **Step 3: Commit + push**(`feat(space-web): 波5 生命周期页错误呈现统一(tOr/重复组展开/去规则页链接/生码seq守卫)`)

---

