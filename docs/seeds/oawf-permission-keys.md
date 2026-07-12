# OA/WF 写端点 × 権限键清单（M-OA/WF Task 1 真相源）

> 生成于 2026-07-12。本表是 **M-OA/WF 横切接线波的唯一真相源**：T2（`Sys_MenuAction`/`Sys_RoleAction` 逐租户种子 + 菜单 MenuKey 回填/显式赋值）、T3（逐端点贴 `[RequirePermission("menu-key","action")]`）、T4（反射 fail-closed 测试）均以本表为准。
> 依据：`docs/00-横切接线规范.md` 第一章（功能级四粒度）+ 同型先例 `docs/seeds/mes-permission-keys.md` / `docs/seeds/erp-permission-keys.md`（格式与结构基准，§一~§七 照抄）+ 现有 OA 菜单种子 `CP6.WebApi/Program.cs` MenuId 733–740 + 逐 Service 实现读证的只读 POST 豁免判定。
> 扫描范围：`CP6.WebApi/Controllers/Oa/`（11 控制器）+ `CP6.WebApi/Controllers/Wf/`（5 控制器），**共 16 个控制器全量**。
> **本任务只产出本文档，不改任何控制器/种子/测试/前端代码。**

## 约定

- **资源键 = `{menu-key}:{action}`**，**menu-key 一律连字符小写、绝对禁止下划线**（全仓 100% RequirePermission 用连字符）。本波统一冠 `oa-` 业务域前缀（`oa-inbox`、`oa-form-catalog`…）。
- **键锚定「消费页菜单」而非「控制器路由段」**：`/api/wf/*` 五个引擎控制器（Flow/Form/Task/AdvancedFlow/Approval）及 Notification 均**无自己的菜单行**，是 OA 前端页面（信箱/填單/设计器）的后端 REST 面 —— 其键锚定到调用它的 OA 菜单（同 MES `quality-inspection`≠控制器 `inspections` 先例）。
- **资源键必须能锚定到一个 `Sys_Menu` 行**（`PermissionAggregator = Sys_RoleActions join Sys_Menus on MenuId where MenuKey!=null → {MenuKey}:{ActionCode}`）。逐键给出锚定菜单 MenuId/RoutePath。
- **`高危?` 列三值**（沿用 WMS/ERP/MES 定义）：
  - `是` = 触及**不可逆写/审批放行/权限授予/流程定义变更（影响所有在途或未来流程）**。审批动作（approve/reject/sendback/transfer/addsign）+ 委托授予 + 流程/表单定义保存 = 全系统最高危写路径，T3 贴点与 T2 审计**最高优先级**，绝不可与 view/edit 混授。
  - `状态` = 独立工作流状态流转（起流程/提交/撤回/启停），单独成键、不塞 edit/view。
  - `否` = 四基粒度 `view/add/edit/del`（+ `favorite`/`read` 等低危个性化写）之一。
- **只读 POST 豁免**：纯查询/预览类 POST 归 `view`，表内标 `只读POST→view`，§四逐条附**读 Service 实现证得的**无写副作用依据。GET 端点一律不列。

---

## 一、写端点映射表（POST/PUT/DELETE，共 33 行）

| # | 控制器 | HTTP方法 + 路由 | 方法名 | 建议 menu-key | action | 高危? | 备注 |
|---|---|---|---|---|---|---|---|
| 1 | ApproverMapController | POST `/api/oa/approver-map` | Create | `oa-approver-map` | add | 否 | 审批人映射登録。菜单739 |
| 2 | ApproverMapController | PUT `/api/oa/approver-map/{id}` | Update | `oa-approver-map` | edit | 否 | 映射订正 |
| 3 | ApproverMapController | DELETE `/api/oa/approver-map/{id}` | Delete | `oa-approver-map` | del | 否 | 删除映射 |
| 4 | CatalogController | POST `/api/oa/catalog/favorite` | Favorite | `oa-form-catalog` | favorite | 否 | 收藏/取消表单（写 Wf_FormFavorite，FavoriteService.cs:12-25 SaveChanges）。个人化写、低危。菜单735 |
| 5 | DelegateController | POST `/api/oa/delegate/add` | Add | `oa-settings` | delegate | **是** | **代理授权授予**：授出后被代理人可以你身份审批（act-as）。安全敏感、权限授予（详§三）。菜单737 设定页 |
| 6 | DelegateController | POST `/api/oa/delegate/remove` | Remove | `oa-settings` | delegate | **是** | 撤销代理授权，归并入 delegate（§五归并1，授予/撤销同权限） |
| 7 | DesignerController | POST `/api/oa/designer/save` | Save | `oa-designer` | edit | **是** | **流程定义保存**（Wf_FlowDef upsert，经 IDesignerService 校验）。不可逆影响所有在途/未来实例（详§三）。计划点名。菜单738 |
| 8 | DesignerController | POST `/api/oa/designer/clone` | Clone | `oa-designer` | add | **是** | **克隆流程定义**（新建 Wf_FlowDef）。同属流程定义变更高危，单独 add 键（§五归并2） |
| 9 | DraftController | POST `/api/oa/draft/save` | Save | `oa-form-catalog` | add | 否 | 新建草稿（Wf_FlowInstance.Status=Draft）。菜单735 填單 |
| 10 | DraftController | POST `/api/oa/draft/update` | Update | `oa-form-catalog` | edit | 否 | 更新草稿 VarsJson |
| 11 | DraftController | POST `/api/oa/draft/submit` | Submit | `oa-form-catalog` | submit | 状态 | 提交草稿→经 L0 引擎推进起流程（DraftService.SubmitDraftAsync）。状态流转，归并入 submit（§五归并3） |
| 12 | DraftController | POST `/api/oa/draft/delete` | Delete | `oa-form-catalog` | del | 否 | 删除草稿 |
| 13 | FlowAdminController | POST `/api/oa/flow-admin/enable` | Enable | `oa-flow-admin` | enable | 状态 | **流程启停干预**（SetEnabledAsync，控制流程能否被起）。计划点名「FlowAdmin 干预」——单独成键、绝不塞 edit。可逆、不动在途，故归 `状态`（若审计要求提级 `是` 见§五归并6）。菜单734 |
| 14 | ForecastController | POST `/api/oa/forecast/preview` | Preview | `oa-form-catalog` | view | 只读POST→view | 发起前预览审批路径，**不产生实例**，ForecastService 全类无写（§四）。菜单735 |
| 15 | InboxController | POST `/api/oa/inbox/task/read` | MarkTaskRead | `oa-inbox` | read | 否 | **标记待办已读=写**（InboxService.cs:89 SaveChanges）。低危。菜单733 |
| 16 | InboxController | POST `/api/oa/inbox/cc/read` | MarkCcRead | `oa-inbox` | read | 否 | 标记抄送已读=写（:97 SaveChanges），归并入 read（§五归并4） |
| 17 | InboxController | POST `/api/oa/inbox/batch` | Batch | `oa-inbox` | approve | **是** | **批量审批办理（承认/否认）**——全系统最高危写路径（ActBatchAsAsync 经 L0 引擎流转+回调，可触发业务单据状态/预算激活/PO 确认等级联，详§三）。act-as 版记实际执行人+onBehalfOf |
| 18 | InboxController | POST `/api/oa/inbox/transfer` | Transfer | `oa-inbox` | transfer | **是** | **转交待办**（IFlowEngine.TransferAsync 改派处理人）。审批权转移、不可逆 |
| 19 | InboxController | POST `/api/oa/inbox/sendback` | SendBack | `oa-inbox` | sendback | **是** | **退回**（IFlowEngine.SendBackAsync 回退到目标节点，作废已产生审批痕迹）。不可逆流程回退 |
| 20 | NotificationController | POST `/api/oa/notification/read` | Read | `oa-inbox` | read | 否 | 通知标记已读=写（MarkReadAsync）。归并入 oa-inbox:read（§五归并4，同「标记已读」低危语义；Notification 无自己菜单，锚 733，§六注3） |
| 21 | NotificationController | POST `/api/oa/notification/read-all` | ReadAll | `oa-inbox` | read | 否 | 全部标记已读=写，归并入 read（§五归并4） |
| 22 | PrefController | POST `/api/oa/pref/save` | Save | `oa-settings` | edit | 否 | **保存个人偏好=写**（PrefService.SaveAsync，主题/列宽/排序）。计划明示**不豁免**。菜单737 设定页 |
| 23 | QueryController | POST `/api/oa/query/search` | Search | `oa-form-search` | view | 只读POST→view | 跨流程多条件查询，InboxService.QueryAsync 纯读（§四）。POST 仅为传 FormQueryFilter 复杂体。菜单736 |
| 24 | AdvancedFlowController | POST `/api/wf/advanced/sendback` | SendBack | `oa-inbox` | sendback | **是** | 退回到目标节点（章07 §2，IFlowEngine.SendBackAsync）。归并入 oa-inbox:sendback（§五归并5，与 #19 同引擎同语义） |
| 25 | AdvancedFlowController | POST `/api/wf/advanced/addsign` | AddSign | `oa-inbox` | addsign | **是** | **加签**（章07 §3，AddSignAsync 动态插入审批人，改变审批链）。不可逆流程结构变更 |
| 26 | AdvancedFlowController | POST `/api/wf/advanced/delegate` | Delegate | `oa-settings` | delegate | **是** | **登记委派**（章07 §5，SetDelegateAsync 授予代理审批权）。与 OA `/api/oa/delegate/add`(#5) 同语义——**T2 主控拍板：委派双键合一为 `oa-settings:delegate`**（原 `oa-inbox:delegate` 退役，权限面统一防一处授一处漏；§六注4 裁决记录） |
| 27 | ApprovalController | POST `/api/wf/approval/submit` | Submit | `oa-form-catalog` | submit | 状态 | **起业务审批**（业务模块接入 OA 入口，按 bizType 绑定流程起实例）。归并入 submit（§五归并3）。无自己菜单，锚填單页（§六注3） |
| 28 | FlowController | POST `/api/wf/flow/def` | SaveDef | `oa-designer` | edit | **是** | **流程定义保存（旧栈设计器）**——与新栈 DesignerController.Save(#7) **同写 Wf_FlowDef**，归并入 oa-designer:edit（§五归并2）。对应孤儿路由 `/wf/flow-designer`，**待用户裁决退役/收编（§六头号裁决点）** |
| 29 | FlowController | POST `/api/wf/flow/submit` | Submit | `oa-form-catalog` | submit | 状态 | 起流程（IFlowEngine.SubmitAsync），归并入 submit（§五归并3） |
| 30 | FlowController | POST `/api/wf/task/{id}/act` | Act | `oa-inbox` | approve | **是** | **单件审批办理（承认/否认）**（IFlowEngine.ActAsync）。与 #17 批量同语义，归并入 oa-inbox:approve（§五归并5）。最高危 |
| 31 | FormController | POST `/api/wf/form/def` | SaveDef | `oa-designer` | form-save | **是** | **表单定义保存（旧栈设计器）**（Wf_FormDef schema upsert，影响所有引用该 formKey 的表单渲染）。对应孤儿路由 `/wf/form-designer`，**待用户裁决（§六头号裁决点）**。暂锚 oa-designer |
| 32 | FormController | POST `/api/wf/form/data` | SubmitData | `oa-form-catalog` | submit | 状态 | 提交表单数据（服务端 schema 复核后落库），归并入 submit（§五归并3） |
| 33 | TaskController | POST `/api/wf/flow/{id}/withdraw` | Withdraw | `oa-inbox` | withdraw | 状态 | **撤回申请**（ITaskCenterService.WithdrawAsync 撤销在途流程）。状态流转、单独成键 |

> **GET-only 控制器（无 POST/PUT/DELETE，不在上表）**：无（16 控制器每个至少 1 个非 GET）。
>
> **有 POST 端点但全豁免（真写=0，不占「含真写控制器」计数，见 §七）**：
> - `ForecastController`（1 POST，preview 只读豁免→view）。
> - `QueryController`（1 POST，search 只读豁免→view）。

---

## 二、menu-key 汇总清单（去重，共 7 个）

| # | menu-key | 锚定菜单（Program.cs MenuId / RoutePath） | 说明 |
|---|---|---|---|
| 1 | `oa-inbox` | 733 电子表单信箱 `/oa/inbox` | ✅有菜单行。回填自 RoutePath = `oa-inbox` ✅一致。承载 Inbox + /wf 引擎的审批动作（approve/transfer/sendback/addsign/withdraw/read）。**委派动作已合一迁出至 `oa-settings:delegate`**（T2 拍板1） |
| 2 | `oa-flow-admin` | 734 流程管理 `/oa/flow-admin` | ✅。回填 = `oa-flow-admin` ✅一致。承载 FlowAdmin 启停 |
| 3 | `oa-form-catalog` | 735 填單 `/oa/form-catalog` | ✅。回填 = `oa-form-catalog` ✅一致。承载 Catalog 收藏 + Draft CRUD + Forecast 预览 + 起流程/提交（submit） |
| 4 | `oa-form-search` | 736 表单查询 `/oa/form-search` | ✅。仅 view（唯一端点只读豁免）。回填 = `oa-form-search` ✅一致 |
| 5 | `oa-settings` | 737 设定 `/oa/settings` | ✅。回填 = `oa-settings` ✅一致。承载 Delegate 授权（**委派双键合一后唯一 delegate 锚**：OA #5/#6 + AdvancedFlow #26）+ Pref 偏好 |
| 6 | `oa-designer` | 738 流程设计器 `/oa/designer` | ✅。回填 = `oa-designer` ✅一致。承载 Designer save/clone（新栈）+ Flow/Form def（旧栈，§六裁决） |
| 7 | `oa-approver-map` | 739 审批人映射 `/oa/approver-map` | ✅。回填 = `oa-approver-map` ✅一致 |

> **全部 7 个 menu-key 均有对应菜单行（733–739），回填派生键与本表逐字一致，零错配**（不同于 MES machine-list 命门；OA RoutePath 与键天然对齐）。**但存在洁净首启回填时序命门**（§六头号命门）与 **2 条前端孤儿路由**（`/wf/form-designer`、`/wf/flow-designer`，§六头号裁决点）。
> `/api/wf/*` 五控制器与 Notification 无自己的菜单行——锚定到其**消费页**菜单（引擎审批动作→733、起流程/提交→735、通知已读→733），非控制器路由段。此为「键锚定菜单」原则的必然结果，T2/T3/前端须逐字用 `oa-*` 消费页键。

---

## 三、高危动作清单（`是`：审批放行/权限授予/流程定义变更/不可逆，共 8 个资源键；T2 委派合一后 9→8）

> 生成于 2026-07-12；**2026-07-12 T2 更新**：委派双键合一（`oa-inbox:delegate` 退役并入 `oa-settings:delegate`），高危键 9→8、资源键 23→22（详§六注4、§七）。
> T3 贴 `[RequirePermission]` 与 T2 审计的**第一优先级**，**绝不可**与 view/edit 混授。OA/WF 域高危集中在**审批写路径**与**流程/表单定义变更**——前者一次误授即他人可越权承认单据（触发预算激活/PO 确认/成本结转等业务级联），后者一次误改即所有在途/未来流程受影响。

| 资源键 | 为何高危独立 |
|---|---|
| `oa-inbox:approve` | **审批办理（承认/否认）**，覆盖 Inbox 批量(#17)+Flow 单件 act(#30)。经 L0 引擎流转并触发 IApprovalCallback 级联（预算版本激活/PO 转 Confirmed/PR 转 Approved，Program.cs:124-126）。全系统最高危写路径，会话记忆 P0「审批油路」核心 |
| `oa-inbox:transfer` | **转交**：改派待办处理人，审批权转移、不可逆 |
| `oa-inbox:sendback` | **退回**：回退到目标节点、作废已产生审批痕迹（Inbox #19 + AdvancedFlow #24 同引擎归并）。不可逆流程回退 |
| `oa-inbox:addsign` | **加签**：动态插入审批人、改变审批链结构（章07 §3）。不可逆 |
| `oa-settings:delegate` | **代理授权授予/撤销 + 委派登记（合一键）**：**T2 主控拍板1——委派双键合一**，收编 OA DelegateController Add/Remove(#5/#6) + AdvancedFlow SetDelegate(#26)。授予他人以你身份审批的代理权（act-as），安全敏感权限授予。原 `oa-inbox:delegate` 退役（§六注4） |
| `oa-designer:edit` | **流程定义保存**：新栈 DesignerController.Save(#7) + 旧栈 FlowController.SaveDef(#28) 同写 Wf_FlowDef。改流程定义不可逆影响所有在途/未来实例。计划点名 |
| `oa-designer:add` | **克隆流程定义**（#8）：新建 Wf_FlowDef，同属流程定义变更 |
| `oa-designer:form-save` | **表单定义保存**（旧栈 FormController.SaveDef #31）：改 Wf_FormDef schema，影响所有引用该 formKey 的表单渲染。**对应孤儿路由 /wf/form-designer，待裁决（§六）** |

### 3b. 独立状态流转动作键（`状态`，共 3 个，仍单独成键、不塞 edit）

`oa-form-catalog:submit`（起流程/起审/提交草稿/提交表单数据，归并 Draft.submit + Flow.submit + Approval.submit + Form.form-data）· `oa-flow-admin:enable`（流程启停干预，计划点名，见§五归并6）· `oa-inbox:withdraw`（撤回申请）

> 非四基粒度独立动作键合计 = 8（高危，T2 委派合一后 9→8）+ 3（状态）= **11 个**。其余端点走 `add/edit/del/view` 四基粒度 + `favorite`/`read` 两个低危个性化写键。

---

## 四、只读 POST 豁免清单（归 view，共 2 个 —— 均逐条读 Service 实现证得无写）

| # | 端点（方法） | 豁免依据（读 Service 实现，文件:行） |
|---|---|---|
| 1 | POST `/api/oa/forecast/preview`（ForecastService.ForecastAsync） | 仅 `_db.Wf_FlowDefs.FirstOrDefaultAsync` 读定义→内存遍历 schema 节点/边计算预计审批路径，返回 ForecastResult DTO，**不产生实例、全类无 `Add/Update/Remove/SaveChanges`**（ForecastService.cs:18-70；ResolveRuleNamesAsync 仅 _approver.ResolveAsync + OaUserNames 读）。POST 仅为传 varsJson 复杂体 |
| 2 | POST `/api/oa/query/search`（InboxService.QueryAsync） | 仅 `_db.Wf_FlowInstances.AsQueryable()` 多条件筛选 + join Wf_FlowDefs/Sys_Users，`.Take(500).ToListAsync()` 投影 DTO，**无写**（InboxService.cs:255-278）。POST 仅为传 FormQueryFilter 复杂体 |

> **复核结论（防望文生义）**：以下「看似查询/预览/通知」的 POST **确为写端点，不豁免**——
> - `POST /api/oa/inbox/task/read`、`/cc/read`、`/api/oa/notification/read`、`/read-all`：**「标记已读」是写**（InboxService.cs:89/97 SaveChanges；NotificationService.MarkRead*）→ 归低危 `read` 键（计划明示）。
> - `POST /api/oa/catalog/favorite`：写 Wf_FormFavorite（FavoriteService.cs:12-25 Add/Remove+SaveChanges）→ `favorite` 键。
> - `POST /api/oa/pref/save`：写用户偏好（计划明示不豁免）→ `oa-settings:edit`。
> - `POST /api/oa/draft/*`、`/api/wf/flow/submit`、`/api/wf/approval/submit`、`/api/wf/form/data`：均经引擎/服务写实例/定义/数据 → 已按上表贴权限。

---

## 五、命名归并判断与疑点（供 T2/T3 复核）

1. **`oa-settings:delegate` 归并 Add+Remove**：代理授权授予与撤销为同一「代理管理」权限的正/反操作，归一键（对标 MES `suspend` 归并中断/解除）。若审计要求「可授不可撤」更细授权，T3 可拆——**当前不拆**。
2. **`oa-designer` 三键（edit/add/form-save）**：`edit`=流程定义保存（新栈 Save #7 + 旧栈 flow/def #28 同写 Wf_FlowDef，天然合一）；`add`=克隆流程定义（#8 新建 Wf_FlowDef）；`form-save`=表单定义保存（#31 旧栈 Wf_FormDef，异实体故独立）。若审计认为「设计器」应单一 `edit` 通吃，可归并——**当前按写入实体/语义分三键**。
3. **`oa-form-catalog:submit` 归并起流程全家**：Draft.submit(#11) + Flow.flow/submit(#29) + Approval.submit(#27) + Form.form/data(#32) 均为「填單→提交/起流程/起审」同一发起权限，归一 `submit` 状态键。若审计要求区分「起草提交」与「业务集成起审」，T3 可拆——**当前归一**。
4. **`oa-inbox:read` 归并四个已读端点**：Inbox task/read(#15)+cc/read(#16) + Notification read(#20)+read-all(#21) 为同一「标记已读」低危写，归一键。Notification 无自己菜单，锚 oa-inbox（§六注3）。若通知中心后续独立菜单，可迁 `oa-notification:read`。
5. **`oa-inbox:approve`/`:sendback` 跨控制器归并**：approve = Inbox 批量(#17) + Flow 单件 act(#30)；sendback = Inbox(#19) + AdvancedFlow(#24)。均同引擎(IFlowEngine)同语义，归一键。审批「批量 vs 单件」若需分权，T3 可拆 `approve`/`approve-batch`——**当前归一**。
6. **`oa-flow-admin:enable` 归 `状态` vs 提级 `是`**：流程启停控制「流程能否被起」，可逆、不动在途实例，故本表归 `状态`。但计划点名「FlowAdmin 干预」为高危候选——若 T2 审计认为「停用生产流程=业务中断」应提级 `是`，可改判。**当前判定 `状态`，待 T2 审计拍板。**
7. **`del` 一致语义**：ApproverMap/Draft 删除按各自 Service 实现（草稿为记录删、映射为行删），统一 `del`。

---

## 六、命门与遗留（T2/T4 硬前置 + 用户裁决点）

### 头号命门·回填时序（洁净首启 OA 全 403）

**OA 菜单 733–740 在 Program.cs :1446–1496 才 Add，且 Add 时均未设 MenuKey（如 :1454 `new Sys_Menu{...RoutePath="/oa/inbox"...}` 无 MenuKey 字段）；而唯一的 MenuKey 回填块在 :908**（`menusNoKey = Sys_Menus.Where(MenuKey==null && RoutePath!=null)` → :912 `MenuKey = RoutePath.Trim('/').Replace('/','-')`）。回填块(:908)在 OA 菜单插入(:1446)**之前**执行。
- 洁净库首启：回填块先跑（OA 菜单尚不存在）→ 跳过；OA 菜单随后插入，MenuKey 留 **null** → `PermissionAggregator` 过滤 MenuKey==null → **OA/WF 全 action 键 join 不出 → 首启即 fail-closed 403，须二次重启回填才生效**（对标 MES 命门#1、WMS「TenantAdmin 新租户重启前 403」平台票）。
- **注**：ERP(:827)/MES(:845) 各有「回填前显式赋 MenuKey」的 T2 块，OA 尚无——OA 完全依赖 :908 回填，故首启失配确凿。
- → **T2 必须在 OA 菜单插入块（:1446–1496）对 733–739 各行显式赋 `MenuKey="oa-*"`**（与 WMS/ERP/MES T2 同型，置于回填逻辑之前/内联），或在 OA 菜单插入后补一次回填 pass。**这是 T2 不做则洁净部署首启失配的硬前置。**

### 注·派生键一致性（无错配，但仍建议显式化）

733–739 全部 RoutePath 派生键与本表 menu-key **逐字一致**（`/oa/inbox`→`oa-inbox` … `/oa/approver-map`→`oa-approver-map`），无 MES `machine-list` 那种错配。回填即正确——**但仍受头号命门时序影响**，故 T2 显式赋值须随头号命门一并做。

### 注3·无菜单锚控制器（判断非硬事实，T2 复核）

`/api/wf/*` 五引擎控制器（Flow/Form/Task/AdvancedFlow/Approval）+ Notification + ApprovalController 均无自己菜单行，本表按「消费页」锚定（审批动作→733；起流程/提交→735；通知已读→733）。此为「键锚定菜单」原则的必然，非漏配。若未来给通知中心/业务审批集成单独建菜单，相关键随迁。

### ⚠ 头号用户裁决点·双栈孤儿路由（`/wf/form-designer`、`/wf/flow-designer`）—— **2026-07-12 已裁决=收编（见下方追记）**

**证据链**：
- 前端：`router/index.ts:46-47` 的 `viewModules` 组件映射表登记 `'/wf/form-designer' → views/wf/designer/FormDesigner.vue`、`'/wf/flow-designer' → views/wf/designer/FlowDesigner.vue`（注释「OA 章09 旧设计器保留」）。**二者无对应 Sys_Menu 菜单行**（OA 菜单 733–740 无 `/wf/*-designer` 路径），且不在 `platformRoutePaths`(:303) / `oaSubRoutePaths`(:319) / 静态路由(:190-288) 之列。
- `addDynamicRoutes`(:332-343) 仅注册「有菜单行且 routePath 命中 viewModules」的项 → **这两条路由永不被注册 → 洁净部署下不可达（暗物质路由）**。旁证：:188-189 旧 `/wf/todo`、`/wf/my-applications` 已 redirect 至 `/oa/inbox`（旧 /wf/* UI 已被 Phase B 迁移收编），唯独这两个设计器未处置。
- 后端：旧栈 def 保存端点 = FormController `POST /api/wf/form/def`（Wf_FormDef #31）+ FlowController `POST /api/wf/flow/def`（Wf_FlowDef #28）。
- 新栈（Phase C′/SSO 后）：DesignerController `POST /api/oa/designer/save`（**同写 Wf_FlowDef**，经 IDesignerService 校验+upsert）+ `/clone`，菜单 738 `/oa/designer` 有行、有页 `DesignerView.vue`、可达。
- 结论：**流程定义保存存在双后端路径**（新 `/oa/designer/save` 与旧 `/wf/flow/def` 同写 Wf_FlowDef）；**表单定义保存仅旧栈** `/wf/form/def`（新栈 DesignerView 是否覆盖表单设计需另确认；FormDesigner.vue 仍在但入口不可达）。

**两案影响面（各一句，不做决定）**：
- **退役案**（删 `/wf/*-designer` 两路由 + FormDesigner/FlowDesigner.vue + 视引用情况删 FormController.SaveDef/FlowController.SaveDef）：消除旧栈重复的高危「流程/表单定义保存」入口，权限面收敛到单一 `oa-designer:*`；**风险**=若两 SaveDef 仍被种子/测试/外部集成调用需先查引用，否则退役断链。
- **收编案**（给两路由补 Sys_Menu 行 + MenuKey，或并入 738 designer 菜单）：两路由变可达并纳入权限体系，但与新栈 `/oa/designer` 功能重叠形成双维护面与用户困惑；新增 `wf-*` 菜单键还与「OA 域统一 `oa-*` 前缀」不一致。

**T1 处置（待裁决前的占位）**：本表将旧栈 `flow/def`(#28) 归并入 `oa-designer:edit`（与新栈同写 Wf_FlowDef、同权限语义），`form/def`(#31) 记为 `oa-designer:form-save`（暂锚 738）。**T2 贴权限前须由用户裁定退役/收编**，否则旧栈 def 端点将挂一个「概念上锚定不可达路由」的键。

**2026-07-12 用户裁决 = 收编，已落地**：补 Sys_Menu 741（フォームデザイナー(旧)，`/wf/form-designer`）、742（フローデザイナー(旧)，`/wf/flow-designer`），ParentId=740，MenuKey 留 null（权限维持锚 738，不与之共键），RoutePath 与 `router/index.ts:46-47` viewModules 键逐字一致，两页收编后前端可达；旧栈端点不删，双栈并存。落地见 `CP6.WebApi/Seed/OawfMenuSeed.cs`（Rows 741/742）+ `docs/seeds/oawf-key-menu-anchor.md`「T2 追补」节 + `docs/seeds/oawf-menu-seed.sql`。上文「两案影响面」中的「收编案」为最终选定方案。

### 注4·委派双端点合一裁决（T2 主控拍板1，2026-07-12 已裁决）

AdvancedFlow `/api/wf/advanced/delegate`(#26) 与 OA `/api/oa/delegate/add`/`remove`(#5/#6) 语义相同（均授予/撤销代理审批权，act-as），T1 曾按锚定页各成 `oa-inbox:delegate` 与 `oa-settings:delegate` 两键。**T1 审查建议合一**（引用：委派授权面若分散于信箱与设定两键，则「一处授一处漏」，安全敏感权限授予绝不应双面维护）。
**T2 主控裁决：合一为 `oa-settings:delegate`（设定页为委派管理归属地）**。`oa-inbox:delegate` **退役**；#26 改锚 `oa-settings:delegate`（§一表已改写）。影响：高危资源键 9→8、资源键去重 23→22（§七同步）。T3 贴点时 AdvancedFlow.Delegate、OA DelegateController.Add/Remove 三端点统一贴 `[RequirePermission("oa-settings","delegate")]`。

---

## 七、计数收口

- **扫描控制器**：16（OA 11：ApproverMap / Catalog / Delegate / Designer / Draft / FlowAdmin / Forecast / Inbox / Notification / Pref / Query；WF 5：AdvancedFlow / Approval / Flow / Form / Task）。与计划口径 16 精确吻合。
- **GET-only 控制器（0 非 GET）**：0。
- **有 POST 但全豁免（真写=0）**：2（Forecast、Query，各 1 端点只读 POST→view）。
- **含真写端点控制器**：14（除 Forecast、Query）。
- **POST/PUT/DELETE 端点行总数**：**33**（= §一表行数，精确吻合）。
  - 其中**只读 POST 豁免（→view）**：**2**。
  - **真·写端点**：**31**。
- **menu-key（去重）**：**7**（全部有菜单行 733–739，零孤儿 menu-key）。另有 2 条前端孤儿*路由* /wf/*-designer **已于 2026-07-12 裁决收编**（补 Sys_Menu 741/742，MenuKey 留 null 不新增 menu-key，权限仍锚 738，§六）。
- **资源键（去重，含 view）**：**22**（T2 委派合一后 23→22，`oa-inbox:delegate` 退役并入 `oa-settings:delegate`）。
- **高危键（是）**：**8**（T2 委派合一后 9→8）：`oa-inbox:approve/transfer/sendback/addsign` + `oa-settings:delegate`（合一 OA #5/#6 + AdvancedFlow #26）+ `oa-designer:edit/add/form-save`。
- **状态键**：**3**（`oa-form-catalog:submit`、`oa-flow-admin:enable`、`oa-inbox:withdraw`）。

### 逐控制器双向核对（控制器→表 / 表→控制器，零缺漏零 GET 误列）

| 控制器 | POST/PUT/DELETE 端点数 | 其中豁免 | 真写 | 表内 # |
|---|---|---|---|---|
| ApproverMapController | 3（Create/Update/Delete；GET list/keys 不列） | 0 | 3 | 1–3 |
| CatalogController | 1（Favorite；GET tree 不列） | 0 | 1 | 4 |
| DelegateController | 2（Add/Remove；GET my-grants/list 不列） | 0 | 2 | 5–6 |
| DesignerController | 2（Save/Clone；GET list/service-catalog/load 不列） | 0 | 2 | 7–8 |
| DraftController | 4（Save/Update/Submit/Delete；GET list 不列） | 0 | 4 | 9–12 |
| FlowAdminController | 1（Enable；GET list/{flowKey} 不列） | 0 | 1 | 13 |
| ForecastController | 1（Preview，只读豁免） | 1 | 0 | 14 |
| InboxController | 5（MarkTaskRead/MarkCcRead/Batch/Transfer/SendBack；GET pending/pending-cc/running/done/stats/detail 不列） | 0 | 5 | 15–19 |
| NotificationController | 2（Read/ReadAll；GET list/unread-count 不列） | 0 | 2 | 20–21 |
| PrefController | 1（Save；GET get 不列） | 0 | 1 | 22 |
| QueryController | 1（Search，只读豁免） | 1 | 0 | 23 |
| AdvancedFlowController | 3（SendBack/AddSign/Delegate） | 0 | 3 | 24–26 |
| ApprovalController | 1（Submit；GET status 不列） | 0 | 1 | 27 |
| FlowController | 3（SaveDef/Submit/Act；GET flow/def/{k}、flow/instance/{id} 不列） | 0 | 3 | 28–30 |
| FormController | 2（SaveDef/SubmitData；GET form/def/{k} 不列） | 0 | 2 | 31–32 |
| TaskController | 1（Withdraw；GET my-todos/my-applications 不列） | 0 | 1 | 33 |
| **合计** | **33** | **2** | **31** | **33 ✅** |

> 自洽核验：总非 GET 端点 33 = 只读豁免 2 + 真写 31 ✅；逐控制器真写累加 3+1+2+2+4+1+0+5+2+1+0+3+1+3+2+1 = 31 ✅；表行 #1–#33 连续无跳号 ✅。
