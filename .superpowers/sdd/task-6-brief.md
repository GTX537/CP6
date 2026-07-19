# GR-VP Task 6：PUR / PLAN / PUB v-permission 铺设

## 目标

以 PUR、PLAN/PUB Controller 的 `[RequirePermission]`、权限种子文档和反射 oracle 为真相源，为真实前端写动作补齐 `v-permission`，不发明权限键，不限制纯读动作。

## 范围

- 扫描 `cp6.web/src/views/pur`、`cp6.web/src/views/plan`、`CodeGenView.vue`、`SeqView.vue`。
- 覆盖 33 个唯一写权限键；`pur-subcontract:view`、`pub-codegen:view` 两个只读 POST 明确豁免。
- 对话框确认按钮由入口权限守住时不重复贴点。
- `SeqView` 的 CRUD 由通用 `VolTable` 提供，因此通过可选的 add/edit/delete permission props 接线；未传 props 的既有调用保持原行为。

## 验收

- [x] 12 个目标视图完成扫描，11 个视图有权限变更，`PurReconcileView` 无写动作贴点。
- [x] 37 个页面级权限声明覆盖 33 个唯一后端写权限键，无缺失、无额外键。
- [x] `VolTable` 桌面端与移动端新增/编辑/删除/批量入口均守权。
- [x] 移动端无编辑/删除权限时不显示空“更多”菜单，点击卡片不会打开编辑框。
- [x] 后端权限 oracle、类型检查、全量 Vitest、生产构建、真实浏览器抽样与正式复审通过。

