# OA/WF menu-key → 锚定 MenuId 映射（M-OA/WF Task 2 交付，T3b 输入）

> 生成于 2026-07-12。**正本 = `CP6.WebApi/Seed/OawfMenuSeed.cs`**（本表与 `oawf-menu-seed.sql` 均为其对照/派生）。
> 依据真相源 `docs/seeds/oawf-permission-keys.md` §二（7 menu-key）/§六（硬前置·回填时序命门）。
> 用途：T3b `Sys_MenuAction`/`Sys_RoleAction` 逐租户种子以本表「键→MenuId」为锚。

## 7 键锚定表

| # | menu-key | 锚定 MenuId | RoutePath | 菜单名 | 裁决 |
|---|---|---|---|---|---|
| 1 | `oa-inbox` | 733 | `/oa/inbox` | 电子表单信箱 | 承载 Inbox + /wf 引擎审批动作（approve/transfer/sendback/addsign/withdraw/read）。回填即一致，仍显式锚定规避时序 |
| 2 | `oa-flow-admin` | 734 | `/oa/flow-admin` | 流程管理 | 承载 FlowAdmin `enable`（**维持状态级**，主控拍板2） |
| 3 | `oa-form-catalog` | 735 | `/oa/form-catalog` | 填單 | 承载 Catalog 收藏 + Draft CRUD + Forecast 预览 + 起流程/提交（submit） |
| 4 | `oa-form-search` | 736 | `/oa/form-search` | 表单查询 | 仅 view（唯一端点只读豁免） |
| 5 | `oa-settings` | 737 | `/oa/settings` | 设定 | 承载 Delegate 授权（**委派双键合一后唯一 delegate 锚**，主控拍板1）+ Pref 偏好 |
| 6 | `oa-designer` | 738 | `/oa/designer` | 流程设计器 | 承载 Designer save/clone（新栈）+ Flow/Form def（旧栈）。**双栈已裁决=收编（2026-07-12 用户拍板，见下方 T2 追补节），双栈并存，旧栈键照锚 738 不变** |
| 7 | `oa-approver-map` | 739 | `/oa/approver-map` | approverMap | 审批人映射维护 |

## 非锚定行（MenuKey 留 null，不承载权限）

| MenuId | 菜单名 | RoutePath | 类别 |
|---|---|---|---|
| 740 | OA工作流 | (null) | 父组行（无 RoutePath，回填亦跳过） |

## 段位与孤儿

- MenuId 段位 = 既有 733–740（8 行），**全部已由 Program.cs（:1446–1496）播种，OawfMenuSeed 无新建缺行**（缺行补建逻辑保留作防御）。
- OA/WF **零孤儿 menu-key**（7 键全落 733–739）。
- 唯一索引安全：7 锚定键互不相同（真相源 §二，OA RoutePath 与键天然对齐），不撞 `Sys_Menus.MenuKey IS NOT NULL` 过滤唯一索引（`CP6Context.cs:602`）。

## T2 追补：双栈孤儿路由收编（用户裁决 2026-07-12，已落地）

真相源 §六头号裁决点 `/wf/form-designer`、`/wf/flow-designer` 两条前端孤儿*路由*（router/index.ts:46-47 viewModules 已注册组件映射但无 Sys_Menu 行 → 洁净部署下不可达）：**用户裁决=收编**（旧设计器可达，双栈并存，不删旧栈端点）。

| MenuId | 菜单名 | RoutePath | ParentId | MenuKey | 裁决 |
|---|---|---|---|---|---|
| 741 | フォームデザイナー(旧) | `/wf/form-designer` | 740 | **null** | 权限已锚 738（`oa-designer:form-save`，旧栈 FormController.SaveDef，真相源 §一 #31）；本行赋非空键会与 738 撞 `MenuKey IS NOT NULL` 过滤唯一索引，故留 null（回填得 `wf-form-designer`，无 RoleAction 引用，无害） |
| 742 | フローデザイナー(旧) | `/wf/flow-designer` | 740 | **null** | 权限已锚 738（`oa-designer:edit`，旧栈 FlowController.SaveDef，真相源 §一 #28）；同上理由留 null（回填得 `wf-flow-designer`，无害） |

- **段位查证**：全仓 grep `CP6.WebApi/Seed/*.cs` + `Program.cs` + `Migrations/*.cs`（InsertData/MenuId 字面量）确认 741/742 无占用（OA 段止于 740，PLAN 段 730–732 不重叠，迁移文件名内 `741`/`742` 子串均为时间戳误命中，非 MenuId）。取号照 `ErpMenuSeed` 五孤儿收编先例（216–220）就近连续取号。
- **前端可达性核对**：RoutePath `/wf/form-designer`、`/wf/flow-designer` 与 `cp6.web/src/router/index.ts:46-47` viewModules 键逐字一致（`'/wf/form-designer': () => import('@/views/wf/designer/FormDesigner.vue')` / `'/wf/flow-designer': () => import('@/views/wf/designer/FlowDesigner.vue')`）——`addDynamicRoutes` 匹配条件满足，两页收编后前端可达。
- RoleMenu 均授 admin（RoleId=1），照收编先例。
- 唯一索引安全：741/742 MenuKey 均 null，不与 7 锚定键（含 738 `oa-designer`）冲突。

## 硬前置落实（真相源 §六头号命门）

1. **回填时序**：`OawfMenuSeed.EnsureSeeded` 接入 Program.cs 紧随 `MesPermissionSeed` 之后（`~:857`），
   **先于全局回填块 :908** 执行 → 洁净首启即赋 7 个 `oa-*` MenuKey，消除 null-全-403 窗口。
2. **零错配（与 MES 差异）**：OA 733–739 RoutePath 派生键与真相源 menu-key **逐字一致**（`/oa/inbox`→`oa-inbox` …），
   命门纯为**时序**（非 MES machine-list 那种键值错配）。防御矫正块（按 MenuId 定位、严限 7 锚定行）正常恒为 no-op，
   仅为结构对齐与防御历史/异常写坏而保留。
3. **主控三拍板**：①委派双键合一→`oa-settings:delegate`（menu-key 集不变，action 层合一）；②`oa-flow-admin:enable` 维持状态级；③双栈原留待用户裁决；**2026-07-12 用户拍板=收编，T2 追补已落地**（741/742 两收编行使 /wf/*-designer 可达，见下方 T2 追补节），权限面不变。
