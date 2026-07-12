# M-PUR T1 任务简报：权限键清单(真相源)

## 背景与位置
M-PUR 横切接线波首任务(第五波,前四波已上线)。计划原文: 「补齐三个裸控制器(PurchaseRequest create/submit/convert、Rfq 7 POST、Subcontract 4 POST)逐端点贴 [RequirePermission]——键名对齐同目录 PurchaseOrderController 既有风格 + MenuAction 种子 + 权限拒绝用例(403 断言)」。实况: Pur 目录 8 控制器,4 个已部分贴点(10 处),裸的还有计划未点名的 PurReconcileController——一并入面。

## 必读(按顺序)
1. `docs/00-横切接线规范.md`
2. 同型先例: `docs/seeds/oawf-permission-keys.md`(§一~§七 结构照抄)
3. 既有风格: CP6.WebApi/Controllers/Pur/PurchaseOrderController.cs 等 4 个已贴控制器的键面(先读,新键对齐其风格)

## 需求
1. **全量扫描**: 8 控制器全部非 GET 端点(含已贴 10 处)逐方法列表,双向计数闭环。
2. **既有 10 贴点键面审计**: 键格式(连字符?)、是否锚定到有 MenuKey 的菜单行(Pur 菜单 701-704,局部回填块在 Program.cs :1499 一带——查这些行的 MenuKey 现状与派生键是否与既有贴点键一致,前波命门式排查)、是否有对应 MenuAction/RoleAction 种子(已知存量 Fin/Sys C#种子仅默认租户是缺口——Pur 既有键有没有种子?哪种范围?)。发现失配/无种子即记 §六 硬前置。
3. **裸 4 控制器逐端点定键**: 键名对齐既有风格;高危独立佐证(convert 转单/发注确定类);只读 POST 豁免逐条 Service 读证。
4. **交付**: `docs/seeds/pur-permission-keys.md`(§一~§七,计数自洽)。纯文档零代码。

## 报告契约
报告写入 `C:\CP6\.superpowers\sdd\pur-t1-report.md`。回复只返回: 状态、commit、一行计数摘要(8控制器/既有N+新M真写/豁免K/键J/高危数)、§六硬前置一行、concerns(15 行内)。单 commit 即 push。