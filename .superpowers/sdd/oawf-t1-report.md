# M-OA/WF T1 报告：权限键清单（端点 × 权限键真相源）

**任务**：M-OA/WF 横切接线波 T1，纯文档零代码。
**交付**：`docs/seeds/oawf-permission-keys.md`（§一~§七 照 MES 版结构）。
**分支**：feat/m-oawf-crosscutting。

## 一行计数摘要
16 控制器 / 31 真写 / 2 豁免 / 7 menu-key / 23 资源键 / **9 高危键** / 3 状态键。
非 GET 端点 33 = 豁免 2 + 真写 31（自洽 ✅）。

## 逐控制器计数表

| 控制器 | 非GET端点 | 豁免 | 真写 | menu-key |
|---|---|---|---|---|
| ApproverMap | 3 | 0 | 3 | oa-approver-map |
| Catalog | 1 | 0 | 1 | oa-form-catalog |
| Delegate | 2 | 0 | 2 | oa-settings |
| Designer | 2 | 0 | 2 | oa-designer |
| Draft | 4 | 0 | 4 | oa-form-catalog |
| FlowAdmin | 1 | 0 | 1 | oa-flow-admin |
| Forecast | 1 | 1 | 0 | oa-form-catalog |
| Inbox | 5 | 0 | 5 | oa-inbox |
| Notification | 2 | 0 | 2 | oa-inbox |
| Pref | 1 | 0 | 1 | oa-settings |
| Query | 1 | 1 | 0 | oa-form-search |
| AdvancedFlow | 3 | 0 | 3 | oa-inbox |
| Approval | 1 | 0 | 1 | oa-form-catalog |
| Flow | 3 | 0 | 3 | oa-inbox / oa-form-catalog / oa-designer |
| Form | 2 | 0 | 2 | oa-designer / oa-form-catalog |
| Task | 1 | 0 | 1 | oa-inbox |
| **合计** | **33** | **2** | **31** | **7 去重** |

## 高危键清单（9，均逐个佐证）
1. `oa-inbox:approve` — 审批办理（承认/否认），Inbox 批量#17+Flow 单件 act#30，触发 IApprovalCallback 级联（预算激活/PO 确认/PR 批准，Program.cs:124-126）。全系统最高危。
2. `oa-inbox:transfer` — 转交（改派处理人，审批权转移）。
3. `oa-inbox:sendback` — 退回（作废审批痕迹，Inbox#19+AdvancedFlow#24 归并）。
4. `oa-inbox:addsign` — 加签（改审批链结构，章07§3）。
5. `oa-inbox:delegate` — 委派登记（授代理审批权，章07§5）。
6. `oa-settings:delegate` — 代理授权授予/撤销（OA DelegateController，计划点名「委托授予」）。
7. `oa-designer:edit` — 流程定义保存（新栈 Designer.Save#7 + 旧栈 Flow.SaveDef#28 同写 Wf_FlowDef，计划点名）。
8. `oa-designer:add` — 克隆流程定义（新建 Wf_FlowDef）。
9. `oa-designer:form-save` — 表单定义保存（旧栈 Form.SaveDef#31，孤儿路由，待裁决）。

## 豁免论证索引（2，逐条读 Service 证无写）
- `oa-forecast/preview` → **view**：ForecastService.ForecastAsync（ForecastService.cs:18-70）仅读 Wf_FlowDefs + 内存遍历 schema，全类无 Add/Update/Remove/SaveChanges。不产生实例。
- `oa-query/search` → **view**：InboxService.QueryAsync（InboxService.cs:255-278）仅 Wf_FlowInstances 多条件查询 + join，.Take(500).ToListAsync 投影，无写。
- 反向复核（确为写、不豁免）：4 个「标记已读」端点（InboxService.cs:89/97 SaveChanges + NotificationService.MarkRead*）、Catalog favorite（FavoriteService.cs:12-25 Add/Remove+SaveChanges）、Pref save（计划明示不豁免）——均按写贴权限。

## §六 悬案摘要

### 头号命门·回填时序（洁净首启 OA 全 403）
OA 菜单 733–740 在 Program.cs :1446–1496 才 Add 且未设 MenuKey；唯一回填块在 :908（在 OA 菜单插入之前）。洁净首启 OA 菜单 MenuKey=null → PermissionAggregator 过滤 → OA/WF 全 403，须二次重启。ERP(:827)/MES(:845) 有「回填前显式赋值」T2 块，OA 尚无。→ **T2 硬前置**：在 733–739 插入块显式赋 `MenuKey="oa-*"` 或插入后补回填 pass。

### 派生键一致性
733–739 RoutePath 派生键与本表 menu-key 逐字一致（无 MES machine-list 错配），但仍受时序命门影响，须随头号命门一并显式化。

### ⚠ 用户裁决点·双栈孤儿路由（只记录不裁决）
- 前端 `/wf/form-designer`、`/wf/flow-designer`（router/index.ts:46-47）仅在 viewModules 组件表，**无 Sys_Menu 菜单行**、不在 platform/oaSub/静态路由列 → `addDynamicRoutes`(:332-343) 永不注册 → **洁净部署不可达（暗物质）**。旁证 :188-189 旧 /wf/todo 等已 redirect 到 /oa/inbox，唯二设计器未处置。
- 后端旧栈 def 保存：FormController `/api/wf/form/def`(#31 Wf_FormDef) + FlowController `/api/wf/flow/def`(#28 Wf_FlowDef)。新栈：DesignerController `/api/oa/designer/save`（同写 Wf_FlowDef）+ menu 738 可达。→ **流程定义保存双后端路径；表单定义保存仅旧栈**。
- 退役案：收敛权限面到单一 oa-designer:*；风险=须先查 SaveDef 是否被种子/测试/集成引用。
- 收编案：补菜单行可达，但与新栈功能重叠、双维护，且 wf-* 键破坏 oa-* 前缀统一。
- T1 占位：flow/def 归并 oa-designer:edit、form/def 记 oa-designer:form-save，T2 贴权限前须用户裁定。

### 注·委派双端点
`oa-inbox:delegate`(AdvancedFlow#26) 与 `oa-settings:delegate`(OA#5) 同语义、不同锚定页，T2 复核是否合一。

### 注·无菜单锚控制器
/api/wf/* 五引擎控制器 + Notification 无自己菜单行，按「消费页」锚定（判断非硬事实，T2 复核）。

## Self-review 结论
- 计数自洽 ✅（33=2+31；逐控制器真写累加=31；表 #1–33 连续）。
- 键全连字符小写、无下划线 ✅。
- 9 高危键逐个佐证（含 Service/Program.cs 行号）✅。
- 2 豁免逐条读 Service 证无写（文件:行）✅。
- §六 头号命门（回填时序）+ 双栈裁决点证据齐（router 行号 + addDynamicRoutes 逻辑 + 双后端路径 + 两案影响面）✅。

## Concerns
1. **命门未修**（T1 仅记录）：洁净部署 OA 全 403 的时序命门确凿，T2 若不显式赋 MenuKey 则首启失配。
2. **双栈裁决未决**：旧栈 2 个 def 端点的键（oa-designer:edit 合并项 + form-save）悬于「不可达路由概念」，须用户在 T2 前裁定退役/收编。
3. **委派双键**：oa-inbox:delegate 与 oa-settings:delegate 可能应合一，留 T2。
4. **FlowAdmin enable 归 状态 vs 是**：计划点名高危，本表判 状态（可逆、不动在途），待 T2 审计拍板。
5. 计划简报提及「500 段已见 501/502」与实际不符——OA 菜单实为 733–740 段，本表按实际锚定。
