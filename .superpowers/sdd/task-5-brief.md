# GR-VP Task 5：FIN v-permission 铺设

## 目标

以 `CP6.WebApi/Controllers/Fin` 的 `[RequirePermission]` 与 `CP6.WebApi/Program.cs` FIN action seed 为真源，为 `cp6.web/src/views/fin` 的真实写入口添加 `v-permission`，不发明权限键，不给纯读动作加权限。

## 范围与约束

- 只处理 FIN 视图；不修改后端权限语义、API 或种子。
- 对话框确认按钮若其打开入口已受控，不重复贴点。
- `BankStatementController` 当前 POST 新建贴点是 `fin-bank-reconciliation:view`，前端必须逐字跟随。
- 预算草稿的金额、控制模式、控制口径是行内写入口：有 `fin-budget:edit` 才能编辑；无编辑权仍须显示只读值。
- 权限 store 未加载时保持既有 fail-open 首屏语义，后端继续负责强校验。

## 验收

- [x] 16 个 FIN 视图共 66 个指令目标，覆盖 51 个真实权限键。
- [x] 所有键逐字存在于 FIN Controller 贴点，写 API 入口反向扫描无遗漏。
- [x] `vue-tsc`、Vitest、生产 build 通过。
- [x] 系统 Chrome 覆盖 denied、单权限和预算 view-only/edit-only 场景，console error 为 0。
- [x] 独立复审 blocker 清零。
- [x] 代码提交并推送：`5732057`。
