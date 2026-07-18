### Task 4 报告：MES v-permission 铺设

**Status:** ✅ 完成并推送至 `origin/main`

**Commit:** `6e4ade1`

## 覆盖结果

| 视图 | 指令数 | 权限动作 |
|---|---:|---|
| `DefectManagementView.vue` | 3 | add / edit / del |
| `MachineListView.vue` | 5 | add / edit（入口+编辑态保存）/ downtime / del |
| `OeeAnalysisView.vue` | 1 | recalculate |
| `PlanningBoardView.vue` | 2 | arrange / reschedule |
| `ProcessCostRateView.vue` | 3 | edit（新增+编辑）/ del |
| `ProductionResultEntryView.vue` | 5 | start / suspend（中断+恢复）/ complete / report |
| `QualityInspectionEntryView.vue` | 2 | add / edit（按模式拆分保存） |
| `QualityInspectionListView.vue` | 1 | add |
| `WorkCenterView.vue` | 3 | edit（新增+编辑）/ del |
| `WorkOrderEntryView.vue` | 3 | add / edit（按模式拆分保存）/ issue |
| `WorkOrderListView.vue` | 2 | add / del |
| `WoStep1BasicInfo.vue` | 1 | work-order add |

合计 **31 条指令 × 12 个视图 × 24 个唯一权限键**。全部键均与 MES Controller、种子键表和 `MesPermissionAttributeTests` 逐字一致。

## 关键判断与豁免

- 设备状态卡同时承载 OEE、状态、当前工单等只读信息，因此不隐藏卡片；只在 `isEdit` 分支给弹窗保存贴 `mes-machine:edit`。
- 工单和质检的共享保存按钮按 `isEdit` 拆成两个静态字面量分支，避免 edit-only/add-only 角色按钮与后端权限不一致。
- `PlanAchievement` 的 summary/export-csv 是事实源明确标记的只读 POST，不贴。
- Control Tower、MES Dashboard、Production Result List 的纯读动作不贴；搜索、刷新、本地导出、导航和本地数组编辑不贴。
- `mes-machine:status` 与 downtime close 当前没有 Vue 触发点，正确不贴。
- 已由受权入口守住的普通弹窗确认按钮不重复贴。

## 验证证据

- `npx vue-tsc --noEmit`：通过，0 error。
- `npx vitest run`：71/71 files，481/481 tests passed。
- `npm run build`：通过；仅既有的 >500 kB chunk 警告。
- 真实 Chrome + Playwright 三角色场景：
  - denied：表格动作 0、新增按钮隐藏、设备卡仍可读、编辑弹窗仅取消；
  - edit-only：仅编辑动作可见，编辑弹窗保存可见；
  - add-only：仅新增可见，从设备卡进入编辑后保存不可见；
  - 三场景 console errors 均为 0。
- 独立 diff review：0 blockers，0 findings；`git diff --check` 通过。

## 工具说明

GStack 的 Windows 浏览器安装包只有 Linux ELF，且缺少 `server.ts/setup`，无法启动；按 skill 回退规则改用项目现成 Playwright 驱动系统 Chrome 完成同等真实浏览器验证。该环境问题未引入项目文件或依赖清单变更。
