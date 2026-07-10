# 模块修复波执行计划（M-WMS → M-ERP → M-MES → M-OA/WF → M-PUR → M-PLAN/PUB）

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development，逐任务派发，编码代理=Opus 4.8。**每个任务实现前必读 `docs/00-横切接线规范.md`（P1）——本计划的所有横切任务都是它的机械执行，样板文件索引在其第七章。**

**Goal:** 按模块收口全局审计的 T2（授权崩塌）/T3（可达性）/T5（审计与测试盲区）缺陷。每波交付后该模块过 P1 第六章 DoD。

**依据：** `cp6-global-audit-2026-07-07`。已移出本计划：盘点冻结、MES 反冲（→F1 spec §3/§5）；波次拣货（后置合流，M-WMS 完成后重排旧 22 任务计划）。

## Global Constraints

- 基线不许跌（后端 1565+/前端 369+/type-check 0）；每 commit 立即 push。
- 权限贴点节奏：**每波先出"端点×权限键"清单表**（波内 Task 1）经用户或审查代理确认后再批量贴——防止键名各波发明；MenuKey 命名与 MenuId 段位先登记再播种。
- 每波结尾跑该模块 fail-closed 反射测试 + 全量回归。

---

## 波序与内容

### M-WMS（欠账最大，先行）

- [ ] T1 权限键清单：29 个控制器全部写端点 × `wms_xxx:action` 映射表（高危独立：出库确定/盘点调整/移库/RMA 处置）。
- [ ] T2 菜单种子重做：`docs/seeds/wms-menu-seed.sql` 48 条补 MenuKey（对照 T1 键表）+ 补 `/wms/*` 进 Program.cs 主种子链（审计 T3/#2：当前洁净部署 WMS 39 页不可达）。
- [ ] T3 控制器贴 `[RequirePermission]`（29 控制器，照 T1 表）+ Sys_MenuAction/RoleAction 逐租户种子。
- [ ] T4 fail-closed 反射测试（照 SpacePermissionAttributeTests 泛化到 Wms 命名空间）。
- [ ] T5 前端 v-permission 贴点（高危按钮优先）+ 硬编码 CJK 清理（审计抽样 43 处起步，全目录 grep 收尽）。
- [ ] T6 IAuditable：库存移动/盘点/入出库单实体贴点（钱与库存优先原则）；高频列豁免裁决走用户。
- [ ] T7 测试补网：StockMovement/StockTake/Outbound 核心路径回归测试。

### M-ERP

- [ ] T1 权限键清单（13 控制器；高危：订单取消/价格修正/信用单）→ T2 贴点+种子 → T3 反射测试。
- [ ] T4 孤儿路由收编：`/erp/order-trace`、`/erp/credit-note`、`/erp/backorder`、`/erp/otd-report`、`/erp/fx-rate` 五条入 Sys_Menu 种子（含 MenuKey）。
- [ ] T5 IAuditable：BusinessPartner/Product/价表/Order 贴点。
- [ ] T6 测试补网（审计 T5/#6 零→有）：QuotationService 报价计算、OrderService 建单算价核心用例。

### M-MES

- [ ] T1 权限键清单（10 控制器；高危：报工修正/工单强制关闭）→ T2 贴点+种子 → T3 反射测试。
- [ ] T4 IAuditable：WorkOrder/ProductionResult 贴点。
- [ ] T5 测试补网（审计 T5/#7 零→有）：报工状态机（开始/中断/完工/全工序完工触发入库）、PlanningBoard 排产/改期核心用例。（反冲的测试在 F1 包。）

### M-OA/WF

- [ ] T1 权限键清单（Oa 11 + Wf 5 控制器；高危：流程定义保存/发布、委托授予、FlowAdmin 干预）→ T2 贴点+种子 → T3 反射测试。**执行窗口与挂单的审批解耦包对齐（改同一片控制器，先后皆可但不并行）。**
- [ ] T4 孤儿路由：`/wf/form-designer`、`/wf/flow-designer` 入菜单或显式登记 standalone 豁免（新旧双栈裁决：若确认旧栈退役则删路由而非补菜单——**问用户**）。
- [ ] T5 IAuditable：Wf_FlowDef/Wf_ApprovalBinding 贴点。

### M-PUR

- [ ] T1 补齐三个裸控制器（审计 HIGH#2）：PurchaseRequest（create/submit/convert）、Rfq（7 个 POST）、Subcontract（4 个 POST）逐端点贴 `[RequirePermission]`——键名对齐同目录 PurchaseOrderController 既有风格 + MenuAction 种子 + 权限拒绝用例（403 断言）。

### M-PLAN/PUB

- [ ] T1 Plan 2 控制器 + Pub 3 控制器权限贴点+种子+反射测试（半天量级，收尾波）。

---

## 每波 DoD（统一）

```
□ 该模块 fail-closed 反射测试绿（新写端点漏贴即红）
□ P1 规范第六章逐项过
□ 全量后端+前端测试+type-check 绿
□ 真库冒烟：无权限角色对 2 个高危端点 403；菜单登录可达
□ commit 即 push；审计记忆对应缺陷条目标记关闭
```

## 执行顺序与派单纪律

M-WMS → M-ERP → M-MES → M-OA/WF → M-PUR → M-PLAN/PUB。波间不并行（权限种子表全局唯一索引易冲突）；波内 T 任务按依赖串行、测试补网类可并行派单。每波完成即更新 `cp6-global-audit-2026-07-07` 记忆对应条目。

---

## M-WMS 完成记录 + 跟踪票（2026-07-10）

**M-WMS 已完成并入 main**（feat/m-wms-crosscutting，7 任务 T1-T7 逐任务审查 + 全支终审 Ready=Yes）：29 控制器 125 写端点贴 [RequirePermission] + 逐租户 MenuAction/RoleAction 种子（112 条/30 键）+ 菜单启动种子链接入(含 MenuKey 锚定) + fail-closed 反射测试 + 前端高危按钮 v-permission(44 条) + 9 实体 IAuditable + 5 账实回归测试。全量 1589 绿。

**🔴 平台级跟踪票（全支终审 Important，影响全模块非仅 WMS）**：`TenantAdminService.CreateAsync`（CP6.Core/Services/Platform/TenantAdminService.cs:121-135）复制了 Sys_Role+Sys_RoleMenu 但**从不复制 Sys_RoleAction** → 运行时经 UI 新建的租户，其 admin 在下次应用重启前对**全部 [RequirePermission] 端点 403**（WMS/Space/Fin 皆中招）。重启自愈（各模块 PermissionSeed 启动扫全租户补种），现有租户全部正确。修法：仿菜单复制逻辑，在 CreateAsync 内从默认租户复制 Sys_RoleAction 到新租户。**属平台层，应随双模认证/平台包处理，不在模块波内。**

**M-WMS follow-up 票（前端完整性，非 DoD 阻断）**：
1. WMS 前端 add/edit/状态流转按钮 v-permission 全覆盖（本波只做高危+删除 44 条；约 30 状态键 + 全部 add/edit 待补）。
2. WMS 前端硬编码 CJK 清理转 i18n 五语（发现 `t('急')`/`t('至急')`/`t('ヤマト運輸')` 等把 CJK 字面当 key 传 t() 的反模式）。
3. 5 处「后端有 DELETE 端点但前端无删除按钮」缺口（remnant/plate-mold/sample-stock/wcs-task/iot 的 del）——产品拍板补按钮或收敛端点。
4. 审计明细不对称（OutboundOrder 贴 IAuditable、其 Detail 未贴）——终审判非缺陷，如需明细留痕追加 `, IAuditable` 即可零迁移。
