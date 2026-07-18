### Task 4：MES v-permission 铺设

**权威计划：** `docs/superpowers/plans/2026-07-17-general-role-vperm.md`

**真相源：**

- `docs/seeds/mes-permission-keys.md`
- `CP6.Tests/MesPermissionAttributeTests.cs`
- MES Controller 的 `[RequirePermission]` 贴点

**范围：** 仅修改 `cp6.web/src/views/mes` 的 template；不改 API、script、style、后端权限或种子。

**验收口径：**

- [x] 11 个 Controller 的 30 个非 GET 端点完成盘点：28 个写端点、2 个只读 POST 豁免。
- [x] 纯查询、CSV、导航、本地数组编辑不贴指令。
- [x] 设备状态卡保留只读信息，编辑态保存按 `mes-machine:edit` 守权。
- [x] 工单和质检保存按 `isEdit` 拆成静态 add/edit 字面量分支。
- [x] 31 条 `v-permission` × 12 个视图，覆盖 24 个存在前端触发点的真实权限键。
- [x] `vue-tsc --noEmit`、Vitest、生产构建、真实 Chrome 三角色场景和独立 diff review 全部通过。
- [x] 代码 commit + push：`6e4ade1`。

