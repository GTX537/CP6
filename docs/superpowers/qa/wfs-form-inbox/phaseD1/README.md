# OA Phase D-1（通知中心 + 邮件投递）QA Runbook

**Branch:** `feat/oa-inbox-core`  
**Worktree:** `D:\CP6-oa-core`  
**Date authored:** 2026-06-28  
**Backend port:** `http://localhost:5177`  
**Frontend port:** `http://localhost:5173`

> **⚠️ LIVE QA 待用户在场用隔离 DB 执行。**  
> 本文件随 N-T10 commit 落库，供用户在有空时按步骤跑通。  
> 在此之前，自动化闸（dotnet build / type-check / vitest / npm build）已全绿。

---

## Why live QA is run separately

A concurrent "Space 3D" session occupies the same development environment and connects to `CP6DB` on the shared `localhost\KOUSQLSERVER` instance. Running the OA backend against `CP6DB` at the same time risks schema conflicts and test-data pollution.

**Solution:** point the OA backend at the isolated database `CP6DB_OA` via an environment variable override. On first boot EF applies all migrations (including Phase D-1 `Wf_Notification` migration), then Program.cs seeds `oa.notify.*` i18n词条 (11条) automatically — no manual DDL needed.

---

## Automated gate status (verified at N-T10 commit)

| Gate | Result |
|------|--------|
| `dotnet build CP6.WebApi` | **green** |
| `npm run type-check` (vue-tsc) | **green (exit 0)** |
| `npx vitest run` | **39 tests passed** |
| `npm run build` (Vite/Rolldown) | **green** |

---

## Step 0 — Prerequisites

1. SQL Server `localhost\KOUSQLSERVER` is running; login has `dbcreator` rights (or DBA creates `CP6DB_OA` manually first).
2. The Space session (if running) uses its own connection string; it will **not** touch `CP6DB_OA`.
3. `CP6DB_OA` may already exist from Phase B/C/C′ QA — that is fine; EF migrations are idempotent.
4. Node ≥ 18 on PATH.

---

## Step 1 — Start the OA backend against the isolated DB

Open **a dedicated PowerShell window** in `D:\CP6-oa-core`:

```powershell
$env:ConnectionStrings__DefaultConnection = "Server=localhost\KOUSQLSERVER;Database=CP6DB_OA;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True"
dotnet run --project D:\CP6-oa-core\CP6.WebApi
```

**What happens on first run:**

1. EF `db.Database.Migrate()` applies every migration in order, including `OaPhaseD1Notification` which creates `Wf_Notification` table + composite index.
2. Program.cs seed block runs:
   - i18n词条入库（含 `oa.notify.*` 11条 + 之前所有 Phase B/C/C′ 词条）
   - 菜单 740/733/734/735/736/737/738 幂等插入
3. Wait for `Application started. Press Ctrl+C to shut down.` before proceeding.

**Pull i18n into frontend (run once per fresh CP6DB_OA):**

```powershell
cd D:\CP6-oa-core\cp6.web
$env:VITE_API_BASE_URL = "http://localhost:5177"
npm run i18n:pull
```

---

## Step 2 — Start the frontend dev server

Open a **second PowerShell window**:

```powershell
cd D:\CP6-oa-core\cp6.web
npm run dev
```

Navigate to `http://localhost:5173`, log in as `admin / 123456`.

---

## Step 3 — Seed test users (if fresh CP6DB_OA)

Create at least two users so we can test approver ≠ starter:

```sql
-- Run in CP6DB_OA (if not already present from Phase B/C QA)
-- User "testuser" (id = known value) should exist from Phase B seed;
-- if not, register a second account via the UI: 系统管理 → 用户管理 → 新建
```

---

## QA Scripts（6 剧本）

### 剧本 1 — 填单发起 → 处理人收站内通知 + 铃铛角标

**Goal:** 验证新待办事件触发持久化通知 + 铃铛未读角标增加。

**前置：** 存在一条审批流（e.g. FlowKey=`leave`），处理人用户 A（admin）已登录。

1. 以用户 B（非 admin）登录，菜单 **填單**（735），选 `leave` 流程，填写内容，点 **提交**。
2. 切换至用户 A（admin）浏览器 tab（或在同一账号如 admin 既是审批人）。
3. 观察页面头部铃铛图标：**未读角标数字应 +1**。
4. 点击铃铛 → 弹出通知面板 → 应看到一条通知，标题类似「您有新的待办：{流程名}」。

**DB 验证：**
```sql
SELECT UserId, [Type], Title, Body, IsRead, CreateDate
FROM   wf.Wf_Notification
WHERE  [Type] = 1   -- TodoCreated
ORDER BY CreateDate DESC;
```
Expected: 至少 1 行，IsRead=0，Title 含流程名。

**SignalR 验证（浏览器控制台）：**
打开 F12 Console，提交时可见 `WfNotification` WebSocket 消息帧（Network → WS 标签）。

---

### 剧本 2 — 批准 → 发起人收「签核完成」通知

**Goal:** 验证 FlowApprovedAsync 钩子触发，通知类型=FlowApproved(2)。

1. 继续剧本 1 的流程：以 admin（处理人）打开待办，点 **同意**。
2. 切换至用户 B（发起人）的浏览器 tab。
3. 观察铃铛：未读角标应新增。
4. 点铃铛 → 通知列表中有一条「您的申请已通过」（或类似文案）。

**DB 验证：**
```sql
SELECT UserId, [Type], Title, IsRead
FROM   wf.Wf_Notification
WHERE  [Type] = 2   -- FlowApproved
ORDER BY CreateDate DESC;
```
Expected: UserId = 发起人 ID，IsRead=0。

---

### 剧本 3 — 驳回另一单 → 发起人收「被驳回」通知

**Goal:** 验证 FlowRejectedAsync 钩子，通知类型=FlowRejected(3)，comment 体现在 Body 中。

1. 以用户 B 新发起另一张 leave 申请。
2. 以 admin 打开该待办，填写驳回意见「测试驳回」，点 **拒绝**。
3. 切换至用户 B，点击铃铛：应有一条「您的申请被驳回：测试驳回」通知。

**DB 验证：**
```sql
SELECT [Type], Title, Body, IsRead
FROM   wf.Wf_Notification
WHERE  [Type] = 3   -- FlowRejected
ORDER BY CreateDate DESC;
```
Expected: Body 含「测试驳回」。

---

### 剧本 4 — 关掉某事件偏好 → 不再收该类通知

**Goal:** 验证 per-user 通知偏好开关生效。

1. 以用户 B 登录，菜单 **设定**（737）→ **通知设定** tab。
2. 关闭「签核完成」开关，点 **保存**。
3. 重复剧本 2（发起 → admin 批准）。
4. 切换至用户 B → 铃铛列表：**不应**出现新的「签核完成」通知（之前的保留）。

**DB 验证：**
```sql
SELECT PrefsJson
FROM   wf.Wf_InboxPref
WHERE  UserId = '<userId-B>';
-- notify.approved 应为 false
```

---

### 剧本 5 — 邮件 LogEmailSender 日志可见（dev）

**Goal:** 验证邮件渠道在 dev 环境走 LogEmailSender，日志输出可见（无真实发信）。

1. 在后端 PowerShell 窗口实时观察控制台输出。
2. 发起一张新申请（剧本 1 重复）→ 触发 TodoCreated 事件。
3. **预期控制台日志** 出现类似：
   ```
   [LogEmailSender] To: <处理人Email> Subject: 您有新的待办 Body: ...
   ```
   （前提：处理人账号在 Sys_Users 中有 Email 字段且通知偏好 email=true）
4. 若处理人无 Email，无日志输出——属设计如此（email only when user has Email）。

**注意：** dev 环境 `LogEmailSender` 不真实发邮件，仅写日志。prod 需换 `SmtpEmailSender`。

---

### 剧本 6 — SignalR 实时铃铛角标跳动

**Goal:** 验证 SignalR WfNotification 事件实时触发铃铛更新（无需刷新页面）。

1. 打开两个浏览器 tab：Tab A（admin 已登录），Tab B（用户 B 已登录）。
2. Tab B 发起申请。
3. 观察 Tab A（不刷新页面）：铃铛未读角标应 **自动跳动 +1**（不需 F5）。
4. Tab A 铃铛弹窗（若 popover 打开）：新通知实时出现在列表顶部。
5. 可选：关掉 Tab A 的 SignalR 连接（F12 Network → WS → 右键 Close），等 60s 轮询兜底刷新后角标应也能更新。

**技术验证（F12 Network → WS）：**
- WS 消息应含：`{ "type": "WfNotification", "userId": "<adminId>", "instanceId": "...", ... }`

---

## Teardown（可选，幂等）

```sql
-- 清除本次 QA 产生的通知记录
DELETE FROM wf.Wf_Notification WHERE CreateDate >= DATEADD(hour, -2, GETUTCDATE());

-- 如需重置偏好
DELETE FROM wf.Wf_InboxPref WHERE UserId IN ('<userId-B>');
```

---

## Acceptance Criteria Summary

| # | 剧本 | Pass 条件 |
|---|------|-----------|
| 1 | 填单→收站内通知+铃铛角标 | Wf_Notification Type=1 落库，铃铛 badge +1，面板可见 |
| 2 | 批准→发起人收签核完成 | Wf_Notification Type=2 落库，发起人铃铛有通知 |
| 3 | 驳回→发起人收被驳回 | Wf_Notification Type=3 落库，Body 含 comment |
| 4 | 关偏好→不再收该类 | InboxPref.PrefsJson.notify.approved=false，不新增 Type=2 通知 |
| 5 | 邮件日志可见 | LogEmailSender 控制台日志含收件人/主题 |
| 6 | SignalR 实时角标跳动 | 无刷新页面铃铛 badge 自动更新 |
