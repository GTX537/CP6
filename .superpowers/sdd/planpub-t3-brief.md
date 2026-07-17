# M-PLAN/PUB T3 任务简报：反射 fail-closed 测试 + 403 拒绝用例

## 背景与位置
M-PLAN/PUB 横切接线波末任务（六模块波收官）。T1 真相源与 T2（11 贴点 + 731/732 MenuKey 显式锚定 + 逐租户 PlanPubPermissionSeed 全 11 键）均已过审（opus 独立复核零 issue）。T3 = 把 Plan/Pub 权限面锁进测试闸门，防未来端点漏贴与键漂移；此后即全支 fable 终审。

## 必读（按顺序）
1. `docs/seeds/planpub-permission-keys.md` —— 真相源（§一键表 11、§四豁免=PreviewInline 已按 view **贴点**而非旁路、§五 Attachment 3 端点组件豁免、§七计数）。oracle 依此**独立写死在测试里**，勿引用生产常量。
2. 前波反射测试先例：**M-OA/WF T4（commit 1e75f38，双命名空间反射 fail-closed）——本波同为双命名空间（Plan+Pub），最贴合的结构样板**；M-PUR T3 为最近一波（搜 `Pur` 反射测试文件），其 HttpPut/豁免表处理口径也参照。`git show 1e75f38 --stat` 找到测试文件后精读，结构照抄并按 Plan/Pub 实况调整。
3. 403 拒绝用例先例：M-PUR T3 的 403 用例口径（读 `C:\CP6\.superpowers\sdd\pur-t3-report.md` 的「403 口径」段），照其已裁定的可行口径落地。
4. T2 报告：`C:\CP6\.superpowers\sdd\planpub-t2-report.md`。

## 需求
1. **反射 fail-closed 测试**（双命名空间 `CP6.WebApi.Controllers.Plan` + `CP6.WebApi.Controllers.Pub`，5 控制器：Mrp/ItemPlanningPolicy/CodeGen/Seq/Attachment）：
   - 全部非 GET action 必须带 `[RequirePermission]` 或在**显式豁免表**中，缺一即红。豁免表 = Attachment 3 端点（upload/delete/rebind，§五.4 组件豁免，逐条 (Controller, Action) 显式登记附豁免理由注释）；PreviewInline 已贴 view **不进豁免表**。
   - **HTTP 动词覆盖面必须显式含 HttpPut**（pub-seq:edit=SeqController.Update 是本波唯一 PUT，T1 concern 点名：漏扫 PUT 则漏贴不报红）。HttpPatch 全仓五波谓词均未含、已立跨波 sweep 票——本波**不扩**，但在测试注释中注明该票，防误读为遗漏。
   - 精确计数断言：11 贴点（与真相源 §七一致），并做反向验证（临时移除任一贴点应双重失败——照 1e75f38 先例的结构自洽手法，RED 实录进报告）。
   - 键面断言：11 个 (menu-key, action) 元组与测试内独立 oracle 集合**双向相等**；全连字符零下划线；资源键 ∈ {plan-mrp, plan-item-policy, pub-codegen, pub-seq}。
   - action 词表逐词核对（真相源 §一动作词全集：run/confirm/convert/ignore/add/delete/save/view/edit）。
   - 基类扫描口径据实（Seq 继承 LocalizedControllerBase、其余 ControllerBase——是否声明 HttpXxx 自查后写注释，DeclaredOnly 使用与否照先例并注明依据——勿抄失实注释，M-MES 有过教训）。
2. **403 拒绝用例**：至少覆盖真相源 §三高危 3 键对应端点（plan-mrp:run、plan-mrp:convert、pub-codegen:save）：无认证（或无权限身份，照 M-PUR T3 已裁定口径）请求 → 断言 401/403 拒绝。口径与先例一致即可，不许静默缩水；若口径需偏离先例，报告写明依据。
3. **纯测试任务**：零生产代码改动。若测试揭示生产缺陷（漏贴/键错）→ 停下报 BLOCKED 附证据，勿自行改生产码。
4. **全量绿**：基线 2181 绿/5 skip。迭代跑聚焦，提交前全量一次（建议前台串行，本机 8GB）。

## 全局约束
- oracle 独立（测试内字面量），与 PlanPubPermissionSeed.Actions/控制器常量零引用。
- 每 commit 立即 push。
- 不动真相源、不动 T2 交付物。

## 报告契约
报告写入 `C:\CP6\.superpowers\sdd\planpub-t3-report.md`（实现清单、RED/GREEN 证据、403 用例口径与依据、自审、concerns），`git add -f` 入库。回复只返回（15 行内）：状态、commits、一行测试摘要（全量数）、403 口径一行、concerns。
