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

---

## M-ERP 完成记录 + 跟踪票（2026-07-12）

**M-ERP 已完成并入 main**（feat/m-erp-crosscutting，6 任务 T1-T6 逐任务过审 + fable 终审 With fixes→Ready，必修项已修）：基线 1683→1716 绿，四层键一致性（RequirePermission 贴点 / MenuKey 锚定 / MenuAction 种子 / RoleAction 种子）全量对账零失配。

**M-ERP 跟踪票**：
1. 🔴 **ExecuteUpdateAsync 审计盲区**（终审 Important#1）：`OrderService.cs:992-996` 単価訂正セット単価一括伝播直写 `OrderDetail.SetUnitPrice`，绕过 ChangeTracker，零审计行；同类 `OrderService.cs:354-373` 受注删除级联软删、`ProductService.cs:502-521` 製品子表软删。修法＝改 tracked 更新或手工补审计行。与 T6 遗留的 relational 集成测试票（ExecuteUpdate 分支覆盖）并为一票两面。
2. 🔴 **Sys_FieldAuditLog 保全策略缺失**（终审 Important#2）：Added 实体记全字段快照，OrderDetail 宽表一笔受注多条大 JSON；全仓无 retention/purge 策略。上线后须监控增速+立归档策略，或裁决 Added 不记全量快照。结合 7/12 磁盘满停机事故史一并考量。
3. 前端 v-permission 不对称（非 DoD 缺口）：本计划 M-ERP 段无前端任务，但高危按钮（受注取消/単価訂正）可见点击 403；与 cp6-web 镜像 stale 重建并为一张 UX 票。
4. 跨波票：Wms 域 `PlateMoldStock` 含 `MadeCost decimal(18,2)` 未贴 IAuditable（M-WMS T6 未圈入），随 M-WMS 补丁或 M-MES 波顺手收。
5. 🔴 **用户裁决点**：`EstimateCalcController.Calculate` 现挂 `[AllowAnonymous]`——终审建议撤销匿名改 `[Authorize]`（计算读真实定价主数据且 API 经公网隧道暴露，匿名可探测成本函数）。T4 测试已锁现状（豁免清单+ AllowAnonymous 断言），变更须走独立 commit。
6. 部署清单（合并后）：重建 cp6-api 镜像→线上验两高危端点（`POST /api/orders/{no}/cancel`、`PUT /api/orders/price-correction/batch`）无权限 403 / 非默认租户 admin ERP 写端点放行 / 菜单 216-220 可达；部署前跑 `SELECT * FROM Sys_Menus WHERE MenuId BETWEEN 216 AND 220` 排除手工占用（终审 Minor#7）。既有边界周知：TenantAdminService 不复制 RoleAction（平台票，见上）/ 存量非默认租户 admin 看不到 216-220 导航（CFG-T#6）。

---

## M-MES 完成记录 + 跟踪票（2026-07-12）

**M-MES 已完成并入 main**（feat/m-mes-crosscutting，6 任务 T1-T6 逐任务过审 + fable 终审 Ready=Yes 零必修）：基线 1716→1758 绿。四层键一致性（28 贴点→25 元组→10 MenuKey 锚定→反射测试）全量对账零失配；洁净首启命门（MES 菜单在回填块后插入致 MenuKey=null 全 403）与 310 机台键错配（mes-machine-list≠mes-machine）双双解除；IAuditable 15 实体全量对账 13 纳入+2 豁免（字段级实查一次过审）+ 跨波票 PlateMoldStock.MadeCost 收口（M-ERP 票#4 关闭）；MES 服务全 tracked SaveChanges **无 ExecuteUpdate 审计盲区**、11 控制器零 AllowAnonymous。

**M-MES 跟踪票**：
1. 🔴 **Sys_FieldAuditLog retention 票升级**（终审 Important#1，升级自 M-ERP 票#2）：本波把全系统最高频写路径（报工 start/suspend/resume/complete/report，每动作≥3 审计行）纳入字段级审计，车间节拍下增速远超受注。措辞升级为「**MES 上线前须有 retention/归档策略或至少容量告警**」，结合 7/12 磁盘满停机事故史。（部署时已加最低限容量监控，见部署记录；真正的归档/purge 策略待用户裁决。）
2. 🔴 **排产两缺口（产品裁决）**（T6 审查裁定为真实缺口非 by-design）：①`RescheduleAsync` 无过去日期/号机时间冲突检知（PlanningBoardService.cs:125-145 仅三道校验）②`AutoArrange` 从 baseTime 空白重建 machinePointer，不避让已占用机台时段（:174-195）。测试已 pin 现状语义，裁决改语义后测试会红提醒同步。
3. **「贴点⊆种子」互锁测试跨模块补强**（终审 Minor#2）：现有测试锁「漏贴」与「种子偏离 oracle」，但「新端点贴了键忘更新种子」无测试红只会现网 admin 403。反射读贴点→锚定表映射→断言 ⊆ 种子 Actions；WMS/ERP/MES 三模块统一补一张票（吸收 T3b「加端点须同步 oracle」提示票）。
4. MES 前端 0 条 v-permission：并入既有「前端 v-permission 不对称 + cp6-web 镜像 stale」UX 票（M-WMS 票#1 / M-ERP 票#3 同族）。
5. Minor 随手项：Program.cs 种子接线注释与 T1/T2 文档的绝对行号自漂移——后续统一去行号化改内容锚定；MesPermissionSeed 枚举租户不过滤 Enable（与 Erp/Wms 种子同型，如收紧三个种子一起改）。
6. 部署清单（合并后）：重建 cp6-api 镜像→首启 SQL 验证 300-315 十锚定键就位（尤其 310=mes-machine）+ Sys_MenuAction/RoleAction MES 段=25×租户数→高危端点 403 冒烟（production-results complete / process-cost-rate upsert）+ admin 放行→非默认租户 admin 放行+菜单可达→开始监控 Sys_FieldAuditLog 增速。既有边界周知：TenantAdminService 不复制 RoleAction（平台票）/ cp6-web stale。
