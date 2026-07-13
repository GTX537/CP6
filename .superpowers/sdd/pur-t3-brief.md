# M-PUR T3 任务简报：反射 fail-closed 测试 + 403 拒绝用例

## 背景与位置
M-PUR 横切接线波末任务。T1 真相源与 T2（14 贴点 + 705/706/707 MenuKey 显式锚定 + 逐租户 PurPermissionSeed 全 24 键）均已过审（分别 opus 独立复核零必修）。T3 = 把 Pur 权限面锁进测试闸门，防未来端点漏贴与键漂移；此后即全支 fable 终审。

## 必读（按顺序）
1. `docs/seeds/pur-permission-keys.md` —— 真相源（§一键表 24、§四豁免 1=reconcile 已按 view **贴点**而非旁路、§七计数）。oracle 依此**独立写死在测试里**，勿引用生产常量。
2. 前波反射测试先例：M-OA/WF T4（commit 1e75f38，双命名空间反射 fail-closed，含精确计数断言+反向验证+action 词表核对+基类 DeclaredOnly 处理）与 M-WMS/M-ERP/M-MES 同型测试——`git show 1e75f38 --stat` 找到测试文件后精读，结构照抄并按 Pur 实况调整。
3. 403 拒绝用例先例：仓内已有的无认证/无权限 403 断言测试（搜 `403`/`Forbidden` 的既有测试写法，照抄其宿主/管道构造方式）。
4. T2 报告 concerns：`C:\CP6\.superpowers\sdd\pur-t2-report.md`（尤其 reconcile 是 attr-view 非豁免表条目）。

## 需求
1. **反射 fail-closed 测试**（命名空间 `CP6.WebApi.Controllers.Pur`，8 控制器）：
   - 全部非 GET action 必须带 `[RequirePermission]`，缺一即红；**豁免表 = 空**（reconcile 已贴 view，不进旁路）。
   - 精确计数断言：24 贴点（与真相源 §七一致），并做反向验证（临时移除任一贴点应双重失败——照 1e75f38 先例的结构自洽手法）。
   - 键面断言：24 个 (menu-key, action) 元组与测试内独立 oracle 集合双向相等；全连字符零下划线；资源键 ∈ {pur-supplier-price, pur-po, pur-gr, pur-match, pur-pr, pur-rfq, pur-subcontract}。
   - action 词表逐词核对（真相源 §一的动作词全集）。
   - 基类扫描口径据实（LocalizedControllerBase 是否声明 HttpXxx 自查后写注释，DeclaredOnly 使用与否照先例并注明依据——勿抄失实注释，M-MES 有过教训）。
2. **403 拒绝用例**：计划原文「权限拒绝用例(403 断言)」。至少覆盖真相源 §三高危 7 键对应端点：无认证（或无权限身份，照仓内既有 403 测试的可行口径）请求 → 断言 401/403 拒绝（与既有先例断言口径一致）。若仓内此前波次以反射测试+部署冒烟替代了进程内 403 集成测试且无可复用宿主，如实报告并按可行的最小口径落地（例如授权过滤器单元级拒绝断言），在报告中写明取舍依据——不许静默缩水。
3. **纯测试任务**：零生产代码改动。若测试揭示生产缺陷（漏贴/键错）→ 停下报 BLOCKED 附证据，勿自行改生产码。
4. **全量绿**：基线 1802 绿。迭代跑聚焦，提交前全量一次。

## 全局约束
- oracle 独立（测试内字面量），与 PurPermissionSeed.Actions/控制器常量零引用。
- 每 commit 立即 push。
- 不动真相源、不动 T2 交付物。

## 报告契约
报告写入 `C:\CP6\.superpowers\sdd\pur-t3-report.md`（实现清单、RED/GREEN 证据、403 用例口径与依据、自审、concerns）。回复只返回（15 行内）：状态、commits、一行测试摘要（全量数）、403 口径一行、concerns。
