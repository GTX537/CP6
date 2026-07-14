# F-T2 完成报告 — WFS 三期波⑥「子流程」gstack QA harness（只写不跑）

**分支** `feat/wfs-subflow`　**日期** 2026-07-14　**类型** docs-only（零代码改动）

## 交付物

| 文件 | 内容 |
| --- | --- |
| `docs/superpowers/qa/wfs-subflow/seed.sql` | 4 用户 + 1 FormDef + 5 FlowDef（1 子 + 4 父），CP6DB_OA 单数表名 / `SET QUOTED_IDENTIFIER ON` / 幂等 / RowVersion 不插 / SchemaJson camelCase。 |
| `docs/superpowers/qa/wfs-subflow/qa_subflow.ps1` | PS5.1 HTTP e2e，覆盖剧本 1/3/4/5/6/8，ASCII 全程，真实状态码捕获，Chk/ChkTrue/ChkContains/Warn 断言风格照 kernel 先例。 |
| `docs/superpowers/qa/wfs-subflow/README.md` | runbook：7 剧本矩阵 + B-T3 剧本8 + 环境 + ps1 端点/信封 + DB/worker 兜底 drill + 浏览器走查 2/7 + 17 键 i18n 参照 + DoD 自检。 |

三处 STATUS 均标 `written, not run`，零执行痕迹。

## 剧本 ↔ 代码 cross-check（file:line 全部实读核实，非 plan 文本）

| 剧本 | 覆盖 | 关键代码锚点（已实读） |
| --- | --- | --- |
| 1 单实例全链 + fast/worker 兜底 | ps1 + DB drill 4.1 | `SubFlowNodeHandler.OnEnterAsync`（停泊）；`FlowEngine.cs:74/102/122/140/358/399` 六处 fast path 尾调；`FastPathSubFlowResumeAsync`（FlowEngine.SubFlow.cs:170）扫 Local `Kind==WfJobKind.SubFlowResume && Pending`。 |
| 2 父子互链（浏览器） | README 5.1 | `InboxService.cs:261-276` `SubFlowParentRow`/`SubFlowChildRow`；`InboxModels.cs:44-50`；detail 端点 `InboxController.cs:124`；键 `oa.detail.parentFlow`/`oa.detail.subFlows`。 |
| 3 多实例 all + 数组回注 | ps1 | `SubFlowVarsMapper.BuildOutMerge`（:84-104，`aggregateAsArray` 按 SubIndex 升序）；`CheckSubFlowGroupAsync`（:137 `aggregate: SubCollectionVar!=null`）；回注读 `data.currentDataJson`（`InboxService.cs:279`=VarsJson）。 |
| 4 all 任一驳 + 级联 | ps1 | `CheckSubFlowGroupAsync`（:130-135 dead→cascade inFlight + SubFlowErrorDispose）；`SubFlowCascade.CancelInstanceTree`（Withdrawn）；无错边+无 ForkId→`FlowEngine.SubFlow.cs:66` Rejected；`subFlowError` 顶层写 VarsJson（:72-93）。 |
| 5 any 首过 | ps1 | `CheckSubFlowGroupAsync`（:143-146 approved→cascade inFlight + ResumeSubFlow 首个 SubIndex 最小）。 |
| 6 组合 prune | ps1 | `SubFlowErrorDisposeAsync`（:64 `token.ForkId is not null && TryPruneBranchAsync`）；`sf-combo-prune` parallelSplit `onBranchReject=prune`。 |
| 7 设计器真浏览器 | README 5.2 | `SubFlowRefValidator`（E-WF-025/026 DFS）；`DesignerController.cs:33` list 下拉源；`DesignerController.Err`（:31 裸码入 message）；键 `oa.designer.subflow.*` 12 + `errSubFlowConfig`。 |
| 8 撤回子→父 fast path（B-T3 追补） | ps1 + README 4.2 | `TaskCenterService.cs:12` ctor `FlowEngine? engine=null`；:75 `EnqueueIfChild`；:86 `_engine.FastPathSubFlowResumeAsync`；DI `Program.cs:142`+`:126`；撤回端点 `TaskController.cs:34`（`oa-inbox:withdraw`）。 |

其余核实：迁移 `20260714075419_WfsSubFlow`（三列 `ParentInstanceId/ParentTokenId/SubIndex` + `IX_Wf_FlowInstance_Parent` + 过滤唯一 `UX_Wf_FlowInstance_SubSlot`）；`FlowInstanceStatus` 0/1/2/3=Running/Approved/Rejected/Withdrawn（WfStatus.cs:6-9）；提交端点 `/api/wf/flow/submit`（FlowController.cs:53）；`Kind` 为**字符串**列 `"subFlowResume"`（WfStatus.cs:86，非整型——README DB drill 已按此写 `Kind='subFlowResume'`）；i18n 17 键（`I18nOaSubFlowScreenSeed`，含 12 面板 + errSubFlowConfig + 2 互链 + E-WF-025/026）。

## 偏离声明

1. **剧本 8 系追补**：brief 7 剧本未含撤回场景；按派单 Context 的 B-T3 追补令，将「生产 DI 注入 scoped FlowEngine 到 TaskCenterService 撤回 fast path 无 live 实证」落为 ps1 剧本 8（撤回子实例→父 all 计票见 Withdrawn→错误处置 Rejected，同请求 fast path 完成）。README §1 表下有专段说明。
2. **ps1 未覆盖 2/7**：拖拽/画布/五语文案属真浏览器 gstack 走查（README 5.1/5.2 手工），与 kernel/infra 先例同口径。
3. **停 worker 场景以 DB drill 呈现**（README 4.1）：scan worker 是与 web 同进程 `IHostedService`，无运行时开关；给出两条可操作路径——① 注释 `Program.cs:160` 源码禁用（真「停 worker」，QA 本地改勿提交）验证 fast path 自足；② 手插 Pending `subFlowResume` 凭据模拟「提交后 crash」由 20s worker 兜底（`Kind='subFlowResume'` 字符串）。fast-path 实证＝凭据 `Succeeded` 且 `LockedBy IS NULL`（worker 未租）。
4. **回注顺序断言用下标序**（ps1 剧本3）：以 currentDataJson 去空白后 `itemA<itemB<itemC` 位置序 + 精确 `["itemA","itemB","itemC"]` 双断言证 SubIndex 排序（乱序办结 2→0→1 仍有序），避免序列化空格脆性。
5. **撤回权限前置**（README 4.2）：`oa-inbox:withdraw` 若未授 role 1，ps1 剧本8 发 WARN 跳过而非硬 FAIL（数据前置，非缺陷）；提示逐租户 RoleAction 补授。

## Watch items（留给 live QA / 主代理）

- **剧本8 撤回 403 风险**：种子用户克隆 admin RoleId，但 `PermissionService` 无 admin 旁路；`oa-inbox:withdraw` 是否授予 role 1 需 live 验证，未授则补 RoleAction 再跑。
- **回注数组序列化格式**：`MergeOutputVars` 产出 `["itemA","itemB","itemC"]` 紧凑形态为断言前提；若实际带空格，去空白断言仍过，精确子串断言可能需放宽（已同时保留下标序断言兜底）。
- **detail 无 owner 过滤**（现状）：starter 直读子实例状态与 subFlows 列表依赖此；若日后加 owner 校验，子状态轮询需换子审批人会话（README §3 已注为已知假设破裂点，非缺陷）。
- **基线核对**：报告引 2181 绿/5 skip（B-T3 中途 2148）；主代理全量重跑确认 + 前端 481 + `has-pending-model-changes` clean（唯一迁移 `WfsSubFlow`）。

## 提交

`test(wfs-subflow): F-T2 gstack QA harness(7剧本+seed+e2e脚本,只写不跑)` — 已 commit 即 push。
