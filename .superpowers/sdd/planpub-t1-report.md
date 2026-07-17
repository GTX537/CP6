# M-PLAN/PUB T1 状态报告

**状态**：DONE

**交付物**：`docs/seeds/planpub-permission-keys.md`（§一~§七，计数自洽，纯文档零代码）

## 计数摘要
5 控制器 / 14 非-GET 写端点 / 豁免 4（1 只读 view + 3 attachment 组件豁免）/ 资源键 11（4 menu-key：plan-mrp/plan-item-policy/pub-codegen/pub-seq）/ 高危 3（plan-mrp:run、plan-mrp:convert、pub-codegen:save）。5 控制器全裸，既有 [RequirePermission] 贴点 = 0，全 11 键为新键。

## §六 硬前置（一行）
🔴 Plan 731/732 无显式 MenuKey、无 Plan 局部回填、插入(:1534/1540)晚于全局回填(:1008) → 洁净首启 plan-mrp/plan-item-policy 全 403，T2 须显式赋 MenuKey（照 Pur 705-707 终审后定论）；另 731/732/112/113 四菜单零 RoleAction，须新建 PlanPubPermissionSeed 逐租户播 11 键（照 PurPermissionSeed）。

## PrGenerationService 现状结论（一行）
MRP convert 端点(#3)当前委托 PlanToPrServiceStub（Program.cs:327，返回 PR-STUB-* 不写 PurchaseRequests），与 M-PUR 点名的 Pur PrGenerationService（:263，死代码）未接线；但 convert 正是闸门形态，T3 须现在就为 plan-mrp:convert 贴 RequirePermission（已判高危），桩换真实实现后即跨命名空间建采购承诺。

## Concerns
- Pub 112/113 与 Plan 731/732 首启就位性**不同**：Pub 两菜单插入早于全局回填(:1008)→MenuKey 首启就位；Plan 两菜单晚于→首启 null。命门只在 Plan 侧，但四菜单**全部**零 RoleAction（就位≠授权），T2 须一并播。
- 跨波票：SeqController.Update 为 HttpPut（本波唯一 PUT），T4 反射测试须显式覆盖 HttpPut，否则漏贴 pub-seq:edit 不报红。
- Attachment 裁定为组件豁免（无菜单不铸键，采纳方案A）；其 delete 属高危形态但当前仅登录闸，follow-up = 扩 Attachment:EnforceBizPermission 至 upload/delete/rebind 写端点。
