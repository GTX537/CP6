### Task E-T2: gstack QA harness（只写不跑；live QA 用户在场时执行）

**Files:**
- Create: `docs/superpowers/qa/wfs-inbox-ux/README.md`
- Create: `docs/superpowers/qa/wfs-inbox-ux/seed.sql`
- Create: `docs/superpowers/qa/wfs-inbox-ux/qa_inbox_ux.ps1`

照 `docs/superpowers/qa/wfs-service-task/`（README+seed+ps1）与 `wfs-serial-signing` 既有模式：seed.sql 用单数表名（`Wf_FlowDef`/`Wf_FlowTask`…）+ `SET QUOTED_IDENTIFIER ON`；ps1 走 HTTP e2e（登录取 Cookie + CSRF 双提交头）；隔离库 `CP6DB_OA`（真 SQL Server），**harness 只写不跑服务器**。

- [ ] **Step 1: 写 README 剧本**（六幕，覆盖 spec §7 QA 行）：
  1. **通知矩阵→跳过验证**：设置页关 `flowRejected`×`email` → 发起并驳回一单 → `Wf_Notification` 有 Type=3 行、**无邮件**（Dev 环境 `LogEmailSender` 日志无 send 记录）；再关 `flowRejected`×`inApp` → 再驳回 → 无新 Type=3 行。timeout 行双格灰置禁用可见（tooltip 文案）。恢复默认后全通道恢复。
  2. **遗留数据兼容**：SQL 直写旧扁平 `{"notify":{"todo":false}}` → 触发新待办 → 无 Type=1 行且无邮件（C2 兼容回归）；打开设置页看矩阵 todoCreated 行显示双关（回落解析）。
  3. **批量改派全流程含失败重试**：seed 30 条 Pending 压给 from（其中 1 条办结制造脏数据）→ FlowAdmin「批量改派」→ 预览（29 条 + 抽样 10）→ 确认 → 报告 29 成功 0 失败；再点名已办结 task（SQL 复原一条为 Pending 后由第二会话抢先办结）演示失败明细 + 单条重试同口径。校验 `Wf_FlowHistory(action=transfer, ActorId=admin)` 与 FormTo 双行、`Sys_OperLog` 有 POST 行。无权限用户（RoleId≠1 测试角色）调端点 → 403 `无权限：oa-inbox:batch-transfer`。
  4. **rowMode**：seed 并行三分支同审批人实例 → 列表 merged=1 行、切 expanded=3 行 → 刷新页面偏好持久（PrefsJson.rowMode）→ 详情页操作不受显示偏好影响。
  5. **移动端 375px 三页走查**（gstack browse 真浏览器，viewport 375×812）：列表卡片流+文件夹横滑条+筛选抽屉、详情堆叠+钉底操作栏（同意/驳回/转交/退回可点）、转交对话框全屏；截图存本目录。
  6. **桌面 1280px 像素走查**：同三页 + 设置页，对照改造前（零回归；重点：表格列宽、action-bar 非 sticky、抽屉 60%）。
- [ ] **Step 2: 写 seed.sql** — 幂等（`IF NOT EXISTS`）：QA 租户下 from/to/admin 三用户、`leave` 线性流程与 `par3` 并行三分支流程 FlowDef、30 单 Pending 数据（存储过程式 WHILE 循环 INSERT `Wf_FlowInstance`/`Wf_FlowToken`/`Wf_FlowTask`/`Wf_FlowFormTo`，字段口径照 `wfs-serial-signing/seed.sql` 既有列清单）。
- [ ] **Step 3: 写 qa_inbox_ux.ps1** — 幕 1~4 的 HTTP e2e：登录 → `POST /api/oa/pref/save`（merge 矩阵）→ 发起/驳回（`/api/oa/inbox/batch` reject）→ 查 `/api/oa/notify/list` 断言；`batch-transfer/preview` + `batch-transfer` + 断言报告数字；`pending?rowMode=` 两态行数断言。ASCII 数据、`-SkipCertificateCheck`、失败 `exit 1`。
- [ ] **Step 4: commit** — `git add -A && git commit -m "test(wfs-inbox): E-T2 gstack QA harness(6幕剧本+seed+e2e脚本,只写不跑)"`

---

