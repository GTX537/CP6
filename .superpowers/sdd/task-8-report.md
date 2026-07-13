# Task 8 报告：编辑器属性面板(Zone 选中编辑 + Marker 编辑 + Aisle 一览)

分支 `feat/space-wave5`，commit `50b05e1`（已 push）。

## 交付物

| 文件 | 动作 |
|---|---|
| `cp6.web/src/space-editor/command/commands/EditZoneCmd.ts` | **新建** —— 照 EditMarkerCmd 同构（prev/next 快照 `apply(after)`/`apply(before)`），`ZonePatch` 覆盖 zoneName/zoneCode/zoneType/color，**polygon 不在补丁内** |
| `cp6.web/src/views/space/editor/panels/PropertiesPanel.vue` | **新建** —— 四态面板：zone 编辑 / marker 编辑 / rack 只读 / 空态 Aisle 一览 |
| `cp6.web/src/views/space/editor/__tests__/PropertiesPanel.spec.ts` | **新建** —— 5 用例 |
| `cp6.web/src/space-editor/command/commands.spec.ts` | 追加 EditZoneCmd 3 用例 |
| `cp6.web/src/views/space/editor/FloorEditor.vue` | selectedZone/selectedMarker/selectionInfo computed + aside 挂 PropertiesPanel |
| `cp6.web/src/space-editor/SceneStage.ts` | renderZone 的 Konva.Line 补 `id`+`name:'zone'`（使 zone 可命中） |
| `cp6.web/src/space-editor/interact/tools/SelectTool.ts` | onClick 空击分支加 zone 点选（findZoneShapeId） |

## 选中态接线方式

- **selectionIds 单源**：zone/marker 与 rack 同用 `store.selectionIds`（扁平 id 数组）。FloorEditor 新增 `selectedZone`/`selectedMarker` computed（与既有 `selectedRack` 同构：单选时按 id 反查 scene.zones/markers），再合成 `selectionInfo: SelectionInfo`（判别联合，rack 优先→zone→marker→none）传入面板。
- **画布点选**：zone 原渲染为无 id 的 Konva.Line（不可命中）。已补 `id`+`name:'zone'`；SelectTool.onClick 在"无 rack 命中"分支沿父链找 `name==='zone'` 的图形取其 id → `setSelection([zoneId])`（Ctrl 切换）。rack 仍在更高图层，rack 点击不受影响；仅 zone 空白区点击才落到 zone。
- Marker **未加**画布点选（见下"没做的事"）。

## 命令栈接入证据（undo/redo 生效）

- 面板内直接持 `useSpaceEditorStore()`，提交走 `store.stack.exec(cmd, store.buildEditorContext())` + `store.updateUndoRedo()`，与 FloorEditor 既有命令调用完全一致 → Ctrl+Z/Ctrl+Shift+Z 天然生效。
- 提交后 `emit('changed')`，FloorEditor `@changed="afterCommand"` 触发重渲染+碰撞刷新。**保存仍走既有场景保存通道**（EditZoneCmd/EditMarkerCmd 只改 store + markDirty，`store.save()` 差量上行不变）。
- 单测实证：`选中 zone → setValue → blur → store.scene.zones[0].zoneName 已变 + store.canUndo===true + stack.undo 还原`。EditZoneCmd 单测覆盖全字段 do/undo 往返、部分补丁、目标不存在静默。

## i18n 先例

**跟随 FloorEditor / TemplatePanel 的编辑器先例 = 中文字面量作 i18n key**（如 `t('保存')`、`t('模板库')`）。seed-2.sql 只覆盖 `space.rule.*/publish.*/events.*`（生命周期三画面），编辑器不在其列。故新面板文案用 `t('库区属性')` 等中文字面量，**未追加 SQL 种子行**（字面量即回退，五语由既有 Sys_Langs 命中则译、未命中显中文，与编辑器现状一致）。

## 零硬编码色

新 UI 全部用 Design System token：`var(--cp-line)`/`--cp-ink`/`--cp-text`/`--cp-muted`/`--cp-bg-hover`/`--cp-fs-*` 等（注：既有 TemplatePanel 用裸 hex，本任务不回改旧文件）。

## Aisle 一览口径

只读三列 + 巷道码。**方向** = 由 centerline 首尾点 |dx| vs |dy| 判横向/纵向；**所属库区** = zoneId 反查 zoneName；**命中库位** = 库位中心 (absX,absY) 落入巷道 polygon 的几何计数（`pointInPolygon`）。无手绘/编辑入口。

## 没做的事（范围铁律照单确认）

1. **不做 Zone 几何编辑** —— EditZoneCmd 补丁仅 name/code/type/color，polygon 绝不动；面板注明"几何形状请在画布上调整"。
2. **Marker 复用 EditMarkerCmd** —— 面板只是入口，未新建 marker 命令。
3. **Aisle 只读** —— 一览无新增/编辑/删除，不做手绘。
4. **rack 只读展示尺寸** —— 反向建模入口保持在工具栏（未搬入面板），面板仅提示。
5. **Marker 画布点选未加** —— 现状 markers 渲染为无 id 的 Circle/Text，本就不可点选。brief 铁律仅授权"若 zone 不可选中加最小 zone 点选"，marker"若已可选中就不动"。marker 既不可选中且非授权范围，故**未动 marker 选中链**；PropertiesPanel 的 marker 分支与 FloorEditor 的 selectedMarker computed 已就位，一旦将来接入 marker 点选即自动可用。当前 marker 编辑通过面板可达性受限于此——如需放开属后续小票（加 marker id/name + SelectTool marker 命中，与本波 zone 同法）。
6. **aisle 图层 listening 未改** —— 保守起见未把 aisle Line 设为 non-listening；后果：点击恰好落在"排间巷道"填充上会清选而非选中所属 zone（zone 空白区点击正常选中）。属可接受的最小行为，未做以免触碰 aisle 渲染。

## 验证

- `npm run test`：**59 files / 385 passed**（基线 377 + 新增 8：EditZoneCmd 3 + 面板 5）。
- `NODE_OPTIONS=--max-old-space-size=8192 npm run type-check`：**0 错**。
- `npm run build`：**通过**（FloorEditor chunk 242 kB）。
