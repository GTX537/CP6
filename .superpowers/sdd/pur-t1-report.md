# M-PUR T1 报告：权限键清单(真相源)

## 交付
- `docs/seeds/pur-permission-keys.md`（§一~§七，纯文档零代码），双向计数闭环自洽。

## 做了什么
1. 按简报必读序读齐：`docs/00-横切接线规范.md`、同型先例 `docs/seeds/oawf-permission-keys.md`（§一~§七结构照抄）、4 个既贴控制器键面。
2. 全量扫描 Pur 8 控制器全部非 GET 端点 = **24 行**（既有贴点 10 + 裸控制器新增 14）。逐 Service 实读判定写/只读。
3. 审计既有 10 贴点键面（格式/锚定/种子范围），定 4 裸控制器（PR/Rfq/Subcontract/PurReconcile）的键，含高危独立佐证与只读 POST 豁免读证。

## 证据高亮（均实读 file:line）
- **既有 10 键面**：全连字符、锚定 701–704、回填派生键与贴点键+内联种子(Program.cs:1520-1523)三方 1:1 吻合，零错配。
- **只读 POST 豁免 1 个**：`pur-subcontract:view`（SubcontractService.ReconcileConsignAsync :185-235 全方法无 Add/Update/Remove/SaveChanges，纯读+内存反推）。
- **防望文生义**：Rfq `rank` 看似只读实为写（持久化 Rank，RfqService.cs:246/257+263）→ 不豁免。Subcontract `finished-cost` 本体无 SaveChanges 但下沉 `_finCost` 写成本凭证 → 高危真写。
- **高危 7 个**：`pur-gr:add`（不可逆入库）、`pur-match:add`/`pur-match:release`（建 AP 财务负债）、`pur-pr:convert`/`pur-rfq:convert`（转单建 PO 承诺）、`pur-subcontract:issue`（WMS 实物出库）、`pur-subcontract:cost`（成本入账）。逐条附 Service 读证。
- **PurReconcile GET-only**：仅 1 GET，0 写端点 → 0 权限键（菜单708 靠可见性控制）。

## §六 硬前置（两条，均带 file:line）
1. **MenuKey 回填范围漏 705/706/707**：Pur 局部回填 Program.cs:1513 只覆盖 701–704，全局回填 :922 在 Pur 菜单插入(:1385)之前跑 → 洁净首启 pur-rfq/pur-pr/pur-subcontract 三菜单 MenuKey=null → 全键 join 不出 → 首启 fail-closed 403。T2 须对 705/706/707 显式赋 MenuKey 或扩回填范围至 701..707。
2. **既有种子仅默认租户**：Program.cs:1518-1531 只 `RoleId=1` 无 TenantId/无租户循环 → StampTenant 落默认租户一份 → 其余 3 租户 admin 也 403（Sys_RoleAction:BaseTenantEntity）。T2 须建 `PurPermissionSeed` 照 WmsPermissionSeed 逐租户模式，一次覆盖既有 10 + 新 14 键。

## 自查
- 完整性：每个非 GET 端点恰好计一次（§七逐控制器双向核对表，控制器→表/表→控制器双向零缺漏）。
- 双向计数：非 GET 24 = 豁免 1 + 真写 23；逐控制器真写累加 23、非 GET 累加 24；既有 10 + 新 14 = 24；资源键 24 = 高危7+状态5+view1+基础/个性化11；表行 #1–#24 无跳号。全部对平。
- 豁免均有 Service 级读证（file:line）；每条硬前置均有 file:line 证据。

## Concerns
- 高危/状态分级对既有 5 个端点（gr:qc、po:submit/cancel、match:reject）为判断非硬事实，§五已给可改判理由，留 T2 审计复核（尤其 gr:qc「验收即锁定可付款量」是否提级）。
- Rfq 7 动作按操作分权（未聚合），若 T2 认为过细可归并，§五注2 已留口径。
- 租户数以「全 4 租户」记忆为据；T2 以运行时 `Sys_Tenants` 枚举为准（WmsPermissionSeed 先例，无需硬编码）。
