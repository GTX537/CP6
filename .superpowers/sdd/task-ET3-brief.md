# Task E-T3: gstack QA harness(只写不跑)

（摘自 docs/superpowers/plans/2026-06-29-wfs-service-task.md）

**Files:**
- Create: `docs/superpowers/qa/wfs-service-task/README.md`(剧本)
- Create: `docs/superpowers/qa/wfs-service-task/seed.sql`(含 serviceTask 节点的 FlowDef + 表单)
- Create: `docs/superpowers/qa/wfs-service-task/qa_service_task.ps1`(HTTP e2e 脚本,ASCII 数据)

- [ ] **Step 1: 写 harness**(参既有 `docs/superpowers/qa/wfs-serial-signing/` / `wfs-approver-resolution/` README+seed+ps1 模式)。剧本覆盖:
  1. **sync 数据回写**:设计 start→svc(dataWriteback sync, sampleWriteback)→end;发起→实例直接 Approved,VarsJson 含 writebackEcho。
  2. **async webApi**:start→svc(webApi async, erpEcho)→end;发起→实例 Running + 1 Pending job;跑/等 worker(或手调 ScanOnce 端点?无则等 20s)→实例 Approved,VarsJson 含 echo。
  3. **timer 纯等待**:short duration(如 PT10S)→等到点→advance。
  4. **timer 到点动作**:duration + erpEcho → 到点执行 echo → advance。
  5. **失败→重试→错误边**:配 EchoConnector 失败模式(或不存在 connector)+ IsError 边→耗尽走错误边,下游 human 节点出现 + VarsJson 含 `wf.serviceError`。
  6. **失败→挂起**:同上但无 IsError 边→实例 Suspended。
  - 真浏览器:设计器 3 调色板入口、按 kind 属性面板、错误边复选。
  - seed.sql 对 OA 表用单数表名(`Wf_FlowDef`/`Wf_FormDef`),`SET QUOTED_IDENTIFIER ON`。
- [ ] **Step 2: commit** — `git commit -m "test(wfs-service-task): E-T3 gstack QA harness(6 剧本+seed+e2e 脚本)"`
- [ ] **Step 3: 末期 live QA(用户在场)** — 本任务只写不跑；live QA 由主控代理另行安排（隔离库 `CP6DB_OA` 起后端+前端 → ps1 HTTP e2e + gstack 真浏览器），不在本任务范围。

## 已知坑（既往 QA 经验，写 seed 时避雷）
- 表名单数：`Wf_FlowDef`/`Wf_FormDef`（wfs-serial QA 曾因复数表名翻车，f90a138）。
- `SET QUOTED_IDENTIFIER ON` 必带。
- ps1 里数据用 ASCII，避免编码问题。

## 落码纪律
- 工作目录 `C:\CP6`，分支 `feat/wfs-service-task-finish`。本地 commit 不 push。只写文档/脚本，不改产品代码。

## QA 重点路径补充（D-T2 审查 2026-07-05 提出）
- 真浏览器剧本必须含：从调色板分别拖 dataWriteback/webApi/timer 三项落画布 → 保存 → 重新加载流程 → 三节点 serviceKind 各自正确（拖拽落点与 round-trip 无组件单测，全靠这条 QA 兜底）。
- 观感确认项：ServiceTaskNode 图标 chip 与节点同底色（brand-bg on brand-bg）是否可接受；不可接受则 chip 底改 color-mix(in srgb, var(--cp-brand) 12%, var(--cp-brand-bg))。
