### Task 11: WMS 试点——出库指示一览迁移到 CpListPage

**Files:**
- Modify: `cp6.web/src/views/wms/` 下出库指示一览页（执行时 `grep -rn "出庫指示\|出库指示" src/views/wms src/router` 定位精确文件）

- [ ] **Step 1:** 读原页面，列出：API 调用、列定义、搜索字段、批量操作、行操作、权限指令（v-permission）——迁移中一项不许丢。
- [ ] **Step 2:** 用 CpPageShell + CpListPage 重写模板：columns 数组（单号 kind=mono、数量 kind=num、状态 kind=tag）、searchFields、statusTabs（若原页有状态筛选）、`fetch` 包装原 API、toolbar slot 放批量按钮、col-操作 slot 放行按钮。scoped style 只剩布局（若还有视觉规则=模板缺口，回补模板而不是页内写死）。
- [ ] **Step 3:** gstack 真栈验收：查询/翻页/状态切换/多选/批量按钮/行跳转全走一遍，`console` 无错误，截图对照 mockup-final-b。
- [ ] **Step 4:** Commit：`refactor(ui): WMS 出库指示一览迁移 CpListPage（模板首个真实消费者）`。
- [ ] **Step 5:** 试点复盘：模板 API 不够用的地方（列合并、行内编辑等）记入 `docs/superpowers/plans/2026-07-04-ui-restyle.md` 末尾「模板缺口」清单，扩 CpListPage 后再进 Milestone C。**Milestone B 在此合并回 main。**

---

# Milestone C：分模块批量迁移（每模块一分支一 PR）

