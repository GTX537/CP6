# Task 2 报告：OA + WF v-permission 铺设

分支 `feat/general-role-vperm`。真相源 `docs/seeds/oawf-permission-keys.md` + oracle `CP6.Tests/OawfPermissionAttributeTests.cs`。
只加 `v-permission` 模板属性，零脚本/样式/结构/i18n 改动。**40 按钮 × 17 视图**。

## 1. 按钮→键映射清单（视图 → 元素 → 键）

### inbox/
| # | 文件 | 元素 | 键 |
|---|---|---|---|
| 1 | inbox/InboxPending.vue | 批量「承认」按钮 doBatch(true) | `oa-inbox:approve` |
| 2 | inbox/InboxPending.vue | 批量「否认」按钮 doBatch(false) | `oa-inbox:approve` |
| 3 | inbox/FormDetail.vue | 承认 doAction(true) | `oa-inbox:approve` |
| 4 | inbox/FormDetail.vue | 否认 doAction(false) | `oa-inbox:approve` |
| 5 | inbox/FormDetail.vue | 转交入口 transferVisible=true | `oa-inbox:transfer` |
| 6 | inbox/FormDetail.vue | 退回入口 sendbackVisible=true | `oa-inbox:sendback` |
| 7 | inbox/InboxDraft.vue | 编辑草稿入口 openEdit | `oa-form-catalog:edit` |
| 8 | inbox/InboxDraft.vue | 提交草稿 submitDraft | `oa-form-catalog:submit` |
| 9 | inbox/InboxDraft.vue | 删除草稿 removeDraft | `oa-form-catalog:del` |

### catalog/
| # | 文件 | 元素 | 键 |
|---|---|---|---|
| 10 | catalog/FormCatalog.vue | 收藏切换（收藏区卡片） | `oa-form-catalog:favorite` |
| 11 | catalog/FormCatalog.vue | 收藏切换（分类树卡片） | `oa-form-catalog:favorite` |
| 12 | catalog/FormInitiate.vue | 存暂存 doSave（draft.save） | `oa-form-catalog:add` |
| 13 | catalog/FormInitiate.vue | 提交 doSubmit（主动作=submit；先 save 后 submit，取主动作键） | `oa-form-catalog:submit` |

### notification/
| # | 文件 | 元素 | 键 |
|---|---|---|---|
| 14 | notification/NotificationBell.vue | 全部标记已读 handleReadAll | `oa-inbox:read` |

### settings/
| # | 文件 | 元素 | 键 |
|---|---|---|---|
| 15 | settings/InboxSettings.vue | 新增代理入口 openAddDialog | `oa-settings:delegate` |
| 16 | settings/InboxSettings.vue | 删除代理 removeDelegate | `oa-settings:delegate` |
| 17 | settings/InboxSettings.vue | 保存显示偏好 savePref（Pref.Save） | `oa-settings:edit` |
| 18 | settings/InboxSettings.vue | 保存通知矩阵 saveNotifyMatrix（Pref.Save） | `oa-settings:edit` |
| 19 | settings/InboxSettings.vue | 重置通知矩阵 resetNotifyMatrix（Pref.Save） | `oa-settings:edit` |

### admin/
| # | 文件 | 元素 | 键 |
|---|---|---|---|
| 20 | admin/FlowAdmin.vue | 批量改派入口 batchTransferVisible=true | `oa-inbox:batch-transfer` |
| 21 | admin/FlowAdmin.vue | 流程启停 el-switch toggleEnable | `oa-flow-admin:enable` |
| 22 | admin/ApproverMapView.vue | 保存行 save（create/update 双端点，取主动作 edit） | `oa-approver-map:edit` |
| 23 | admin/ApproverMapView.vue | 删除行 del | `oa-approver-map:del` |
| 24 | admin/WorkCalendar.vue | 导入日本假日 importJp | `oa-work-calendar:Calendar.Edit` |
| 25 | admin/WorkCalendar.vue | 反转对话框「确定」saveDay（clear/toggle） | `oa-work-calendar:Calendar.Edit` |
| 26 | admin/FlowTriggerPanel.vue | 新建触发器入口 openCreate | `oa-flow-admin:FlowTrigger.Edit` |
| 27 | admin/FlowTriggerPanel.vue | 启停 el-switch toggleEnable | `oa-flow-admin:FlowTrigger.Edit` |
| 28 | admin/FlowTriggerPanel.vue | 编辑入口 openEdit | `oa-flow-admin:FlowTrigger.Edit` |
| 29 | admin/FlowTriggerPanel.vue | 手动试发 manualFire | `oa-flow-admin:FlowTrigger.Edit` |
| 30 | admin/FlowTriggerPanel.vue | 重置 key resetKey | `oa-flow-admin:FlowTrigger.Edit` |
| 31 | admin/WfConnectorPanel.vue | 新建连接器入口 openCreate | `oa-flow-admin:Connector.Edit` |
| 32 | admin/WfConnectorPanel.vue | 启停 el-switch toggleEnable | `oa-flow-admin:Connector.Edit` |
| 33 | admin/WfConnectorPanel.vue | 编辑入口 openEdit | `oa-flow-admin:Connector.Edit` |

### designer/
| # | 文件 | 元素 | 键 |
|---|---|---|---|
| 34 | designer/DesignerView.vue | 保存流程定义 doSave | `oa-designer:edit` |
| 35 | designer/DesignerView.vue | 克隆入口 openCloneDialog（clone=新建 Wf_FlowDef） | `oa-designer:add` |

### views/wf/
| # | 文件 | 元素 | 键 |
|---|---|---|---|
| 36 | wf/TodoCenter.vue | 办理对话框「驳回」act(false)（Flow.Act） | `oa-inbox:approve` |
| 37 | wf/TodoCenter.vue | 办理对话框「同意」act(true)（Flow.Act） | `oa-inbox:approve` |
| 38 | wf/MyApplications.vue | 撤回 withdraw（Task.Withdraw） | `oa-inbox:withdraw` |
| 39 | wf/designer/FlowDesigner.vue | 保存（旧栈 Flow.SaveDef → Wf_FlowDef） | `oa-designer:edit` |
| 40 | wf/designer/FormDesigner.vue | 保存（旧栈 Form.SaveDef → Wf_FormDef） | `oa-designer:form-save` |

用到的 22 个资源键均在真相源 §一/§二/§8 及 oracle `ActionVocabulary` 内逐字命中。

## 2. 豁免小节（看似变更但不贴指令的按钮/元素及理由）

- **catalog/FormInitiate.vue「预览」doPreview** → `ForecastController.Preview`，真相源 §四#1 只读 POST 豁免（归 view，后端未贴键），不贴。
- **query/FormQuery.vue「查询」onSearch** → `QueryController.Search`，真相源 §四#2 只读 POST 豁免，不贴。
- **inbox/InboxView.vue「新建」openNewDialog** → 打开占位对话框（模板注释「起草功能后续版本实现」），当前不调任何写端点，无对应键，不贴。
- **notification/NotificationBell.vue 通知条目点击 handleItemClick** → 语义主体是导航跳转（附带 read 标记），非独立按钮（`<li>`），贴则隐藏整条通知，不贴（已在「全部标记已读」按钮贴 `oa-inbox:read`）。
- **inbox 行点击 markTaskRead / markCcRead、InboxDone/InboxRunning 行点击** → 行点击=打开详情（附带已读），非按钮、纯读导向，不贴。
- **admin/ApproverMapView.vue「新增行」addRow** → 仅本地 push 空行，无 API，不贴。
- **各 Refresh 刷新圈按钮** → 只读刷新，不贴。
- **designer/DesignerView.vue「校验」doValidateClick、「新建」newFlow** → 纯客户端校验/本地重置，无 API，不贴。
- **designer/DesignerCanvas.vue（撤销/重做/自动布局/网格/删除选中/调色板拖拽）、NodePropertyPanel.vue（档位/成员增删移位）** → 本地内存 schema 编辑，持久化统一走 DesignerView doSave，不贴。
- **wf/designer FlowDesigner+FormDesigner（加载/撤销/重做/加节点/连线/删除/加字段/移位/删字段/选项增删）** → 本地画布编辑，持久化走已贴的 save 按钮，不贴。
- **对话框内确认按钮不重复贴（入口已守）**：TransferDialog/SendBackDialog 确认（入口 FormDetail 转交/退回）、BatchTransferDialog 预览/确认/重试（入口 FlowAdmin 批量改派）、FlowTriggerDialog/WfConnectorDialog 保存（入口 openCreate/openEdit）、InboxSettings confirmAdd（入口 openAddDialog）、InboxDraft saveEdit（入口 openEdit）、DesignerView doClone（入口 openCloneDialog）。

### 判定说明
- **#13 FormInitiate 提交**：doSubmit 依次 `draft.save`(add) + `draft.submit`(submit)，取主动作 submit（§一#11/#33 归并语义）。
- **#22 ApproverMapView 保存**：单一「保存」按钮据行 id 走 create(add) 或 update(edit) 双端点；取「修改/持久化」主动作 `oa-approver-map:edit`（`oa-approver-map:add` 亦为合法键，此处不额外拆）。
- **#25 WorkCalendar 保存日**：反转入口是日历单元格 `<div @click>`（贴指令会破坏日历栅格），故按「入口非离散按钮」例外，改在对话框「确定」按钮贴 `Calendar.Edit`（唯一可离散隐藏的实际写按钮）。

## 3. 验证输出

- `npx vue-tsc --noEmit` → TSC_EXIT=0（零类型错误）
- `npx vitest run` → 71 files / 481 tests passed（VITEST_EXIT=0）
- `npm run build` → built in 9.11s（BUILD_EXIT=0；仅既有 chunk 体积告警，非本波引入）

## 4. 自查

- `git diff` 仅 17 个 `.vue`（views/oa 13 + views/wf 4），无 views 外文件。
- 22 键全部命中 oracle ActionVocabulary + menu-key `^oa-[a-z0-9-]+$`。
- 无 `<script>`/`<style>` 改动 hunk（全为 `<template>` 属性追加）。
- 既有 `v-if` 业务条件保留并列（FlowAdmin `v-if="activeTab==='flows'"`、MyApplications `v-if="row.status===0"`、FlowTrigger resetKey `v-if="row.triggerType===2"`）。

## 5. 关注点 / 待议

- **#22 ApproverMap 保存键**：一按钮双端点（create=add / update=edit），选 edit 为主动作；若审查倾向 add 可单点改（不影响后端 fail-closed）。
- **wf/ 旧栈页可达性**：TodoCenter/MyApplications 路由已 redirect 至 /oa/inbox；FlowDesigner/FormDesigner 为收编旧设计器（Sys_Menu 741/742，权限仍锚 738）。指令 UX-only 且 fail-open，不可达时无害，已按 views/wf 在范围内一并铺设。
- v-permission 纯 UX 层（store 未加载 fail-open，admin 持全键可见），后端 `[RequirePermission]`+403 才是强校验。
