### Task 8: 编辑器属性面板(Zone 选中编辑 + Marker 编辑 + Aisle 一览)

**Files:**
- Create: `cp6.web\src\views\space\editor\panels\PropertiesPanel.vue`
- Modify: `cp6.web\src\views\space\editor\FloorEditor.vue`(右侧 aside `:639-648` 挂第三面板;选中态解析 `:59-63` 旁扩 selectedZone/selectedMarker)
- Modify(若 Zone 不可选中): `cp6.web\src\space-editor\interact\tools\SelectTool.ts`(允许点选 zone 图形,与 rack 同 selectionIds 语义)
- Test: `cp6.web\src\views\space\editor\__tests__\PropertiesPanel.spec.ts`

**Interfaces:**
- Consumes: 既有命令层——Zone 改名/改码走**新建** `EditZoneCmd`(照 `space-editor\command\commands\EditMarkerCmd.ts` 逐字同构:prev/next 快照 do/undo);Marker 编辑复用 `EditMarkerCmd`;Aisle 只读一览(方向/所属 Zone/命中库位数),**不做 Aisle 手绘**(生成模型是模板阵列副产物,波5 不动)。
- Produces: `PropertiesPanel` props `{ selection: SelectionInfo }`,内部三分支(zone/marker/rack);rack 分支只读展示尺寸+「反向建模」入口保持原工具栏不动。

**要点:** 面板变更全部走命令栈(undo/redo 生效);保存仍走既有场景保存(命令改 store,`spaceEditor.ts` deletes/updates 已有通道)。无选中时显示 Aisle 一览 tab。

- [ ] **Step 1: 失败测试**(EditZoneCmd do/undo 往返;PropertiesPanel 选中 zone 渲染名称输入、blur 后 store 值变)
- [ ] **Step 2: 红 → 实现 → vitest 绿 + type-check 0 错**
- [ ] **Step 3: Commit + push**(`feat(space-web): 波5 编辑器属性面板——Zone选中编辑(EditZoneCmd)/Marker编辑/Aisle一览`)

---

