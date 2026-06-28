# OA 电子表单信箱 Phase C′ — 流程设计器 QA Runbook

**Branch:** `feat/oa-inbox-core`  
**Worktree:** `D:\CP6-oa-core`  
**Date authored:** 2026-06-28  
**Backend port:** `http://localhost:5177`  
**Frontend port:** `http://localhost:5173`

> **⚠️ LIVE QA 待用户在场用隔离 DB 执行。**  
> 本文件随 T10 commit 落库，供用户在有空时按步骤跑通。  
> 在此之前，自动化闸（dotnet build / type-check / vitest / npm build）已全绿。

---

## Why live QA is run separately

A concurrent "Space 3D" session occupies the same development environment and connects to `CP6DB` on the shared `localhost\KOUSQLSERVER` instance. Running the OA backend against `CP6DB` at the same time risks schema conflicts and test-data pollution.

**Solution:** point the OA backend at an isolated database `CP6DB_OA` via an environment variable override. On first boot EF applies all migrations (including Phase C′ designer migrations for `Wf_FlowDef`/`Wf_FlowNode`/`Wf_FlowEdge`), then Program.cs seeds menus 733–738 and i18n automatically — no manual DDL needed.

---

## Automated gate status (verified at T10 commit)

| Gate | Result |
|------|--------|
| `dotnet build CP6.WebApi` | **green (0 errors, 1 pre-existing warning)** |
| `npm run type-check` (vue-tsc) | **green (exit 0)** |
| `npx vitest run` | **7 files / 39 tests passed** |
| `npm run build` (Vite/Rolldown) | **green — DesignerView-*.js 182 kB gzip:57 kB (Vue Flow bundle, expected)** |

---

## Step 0 — Prerequisites

1. SQL Server `localhost\KOUSQLSERVER` is running; login has `dbcreator` rights (or DBA creates `CP6DB_OA` manually first).
2. The Space session (if running) uses its own connection string; it will **not** touch `CP6DB_OA`.
3. Node ≥ 18 on PATH.
4. `CP6DB_OA` may already exist from Phase B/C QA — that is fine; migrations are idempotent.

---

## Step 1 — Start the OA backend against the isolated DB

Open **a dedicated PowerShell window** in `D:\CP6-oa-core`:

```powershell
$env:ConnectionStrings__DefaultConnection = "Server=localhost\KOUSQLSERVER;Database=CP6DB_OA;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True"
dotnet run --project D:\CP6-oa-core\CP6.WebApi
```

**What happens on first run:**

1. EF `db.Database.Migrate()` applies every migration in order up to Phase C′ designer migrations.
2. Program.cs seed block runs:
   - i18n词条入库（含 `nav.738` + `oa.designer.*` 70条）
   - 菜单 740/733/734/735/736/737/**738** 幂等插入
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

## QA Scripts (6 剧本)

### 剧本 1 — 新建 → 拖节点 → 连边 → 设策略 → 校验 → 保存落库

**Goal:** 端到端设计一条完整可运行的审批流并保存到 DB。

1. 侧栏点 **OA工作流 → 流程设计器**（菜单 738）。
2. 点工具条 **新建流程**。
3. 填写元数据：
   - 流程标识：`qa-designer-001`
   - 流程名称：`QA设计器测试流`
   - 表单标识：`leave` （与 Phase B 信箱已有表单匹配）
   - 功能 ID（可选）：`MSBBPA010`
   - 流程编号（可选）：`FLOW-001`
4. 调色板拖出 **开始** 节点 → **审批** 节点 → **结束** 节点。
5. 连线：开始 → 审批 → 结束。
6. 点击审批节点 → 属性面板：
   - 审批人策略选 **指定人员**
   - 指定审批人填 `1`（admin userId）
   - 会签规则：任一同意
7. 点 **校验** 按钮 → 提示「校验通过」。
8. 点 **保存** → 提示「保存成功」。

**DB 验证：**

```sql
SELECT FlowKey, FlowName, FormKey, FunctionId, FlowCode, SchemaJson
FROM   wf.Wf_FlowDef
WHERE  FlowKey = 'qa-designer-001';

-- schema 应含 nodes/edges JSON
SELECT FlowKey, NodeId, NodeType, PositionX, PositionY
FROM   wf.Wf_FlowNode
WHERE  FlowKey = 'qa-designer-001';

SELECT FlowKey, EdgeId, [From], [To]
FROM   wf.Wf_FlowEdge
WHERE  FlowKey = 'qa-designer-001';
```

Expected: 1行 Wf_FlowDef + 3行 Wf_FlowNode (start/approval/end) + 2行 Wf_FlowEdge.

---

### 剧本 2 — 重开 load 还原（持久化验证）

**Goal:** 刷新页面后选回同一流程，画布完整还原。

1. 刷新浏览器（F5）。
2. 顶部下拉选 `qa-designer-001 — QA设计器测试流`。
3. 验证：
   - 3个节点出现在画布、位置大致还原
   - 审批节点属性面板显示策略=指定人员、审批人=1
   - 元数据栏流程名称/表单标识/功能ID等均回填

---

### 剧本 3 — 重复 FunctionId → E-WF-009 本地化报错

**Goal:** 验证 FunctionId 唯一约束服务层校验 + 前端 toast。

1. 新建另一流程，填：
   - 流程标识：`qa-designer-002`
   - 流程名称：`QA冲突测试`
   - 功能 ID：`MSBBPA010`（与剧本1相同）
2. 拖出 开始→审批→结束，设审批人，校验通过，保存。
3. 预期：后端返回 E-WF-009 错误，前端弹 toast 显示本地化文案「功能 ID 已被其他流程占用」（ZhCN），不保存成功。

---

### 剧本 4 — 故意删 end 节点后保存 → E-WF-010 / 客户端校验报错

**Goal:** 验证客户端 `validateClient` 拦截 + 服务端双重守卫。

1. 打开 `qa-designer-001`，删除 **结束** 节点（选中→工具条删除选中）。
2. 点 **校验**：
   - 预期客户端弹 toast「必须有至少一个结束节点」（`oa.designer.errNoEnd`）。
3. 若强制跳过校验点 **保存**：
   - 预期后端 `FlowSchemaValidator` 返回 E-WF-010，前端 toast 显示服务端错误文案。

---

### 剧本 5 — 另存副本（clone）→ 新 FlowKey 独立副本

**Goal:** 验证克隆不共享，副本 FunctionId/FlowCode 清空，状态为草稿。

1. 打开 `qa-designer-001`。
2. 点工具条 **另存副本**，填：
   - 新流程标识：`qa-designer-003`
   - 新流程名称：`QA副本测试`
3. 点 **确认另存** → 提示「副本已创建」。
4. 下拉选 `qa-designer-003`：
   - 元数据中 功能ID / 流程编号 应已清空（IsActive=false 草稿）
   - 节点/边结构与 `qa-designer-001` 一致
5. DB 验证：`qa-designer-001` 的 FunctionId 仍为 `MSBBPA010`，`qa-designer-003` 的 FunctionId 为 null。

---

### 剧本 6 — 闭环验证（设计器产物 → FlowAdmin 启用 → 信箱填單 → 真跑审批）

**Goal:** 验证设计器产物是能被引擎实际执行的真实流程定义，非孤立 JSON。

1. 菜单 **OA工作流 → 流程管理**（734）→ 找 `qa-designer-001`，点 **启用**（IsActive=true）。
2. 菜单 **OA工作流 → 填單**（735）→ 找关联 FormKey=`leave` 的表单，点 **填写**。
3. 填写请假天数等表单字段，点 **提交**。
4. 切换为 admin 用户（或刷新信箱）→ 菜单 **电子表单信箱**（733）→ **待处理** tab。
5. 找到刚发起的待办，点 **同意**。
6. 预期：流程进入「已完成」，申请人的「已处理」tab 能看到该记录。

**验证要点：**
- 引擎真正执行了 `qa-designer-001` 定义的审批流（strategy=Specified, approverUserId=1）
- `Wf_FlowInstance` / `Wf_FlowToken` / `Wf_FlowFormTo` 均有对应记录
- 与手写 JSON 流程行为一致（设计器产物=能跑的真实流程）

---

## Teardown

```sql
-- 清理 QA 流程定义（不影响已有业务数据）
DELETE FROM wf.Wf_FlowEdge WHERE FlowKey IN ('qa-designer-001','qa-designer-002','qa-designer-003');
DELETE FROM wf.Wf_FlowNode WHERE FlowKey IN ('qa-designer-001','qa-designer-002','qa-designer-003');
DELETE FROM wf.Wf_FlowDef  WHERE FlowKey IN ('qa-designer-001','qa-designer-002','qa-designer-003');
```

---

## Acceptance Criteria Summary

| # |剧本 | Pass 条件 |
|---|------|-----------|
| 1 | 新建保存 | DB Wf_FlowDef/Node/Edge 均落库，SchemaJson 非空 |
| 2 | Load 还原 | 刷新后画布节点/边/属性完整恢复 |
| 3 | FunctionId 冲突 | E-WF-009 本地化 toast，不保存 |
| 4 | 缺 end 节点 | 客户端 errNoEnd toast / 服务端 E-WF-010 守卫 |
| 5 | 另存副本 | 新 FlowKey 独立，FunctionId 清空，状态草稿 |
| 6 | 闭环执行 | 设计器定义的流程能在引擎中真实跑通审批 |
