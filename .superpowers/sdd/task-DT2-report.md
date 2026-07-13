# Task D-T2 报告：InclusiveGatewayNode + 画布接线 + 属性面板「分支驳回策略」段

## Status: 完成（TDD 红→绿，前端 420 全绿 / type-check 0 / build 过）

## 交付物
| 文件 | 动作 | 内容 |
|---|---|---|
| `cp6.web/src/views/oa/designer/nodes/InclusiveGatewayNode.vue` | Create | BPMN 惯例 菱形+内嵌空心圆节点（区别 GatewayNode 实心菱形）；`t()` 文案；全 `var(--cp-warn)` token 零硬编码色 |
| `cp6.web/src/views/oa/designer/DesignerCanvas.vue` | Modify | import + `#node-inclusiveSplit`/`#node-inclusiveJoin` 两模板 + `.dot-inclusiveSplit/.dot-inclusiveJoin`（空心 dot，border token） |
| `cp6.web/src/views/oa/designer/NodePropertyPanel.vue` | Modify | script `isSplitGateway`+`branchReject` computed；「基本參數」nodeType 后加分支驳回 el-select 段；`.gw-hint` 样式 |
| `cp6.web/src/views/oa/designer/nodes/InclusiveGatewayNode.spec.ts` | Create（测试） | 4 用例：split/join 文案键、空心圆记号、选中态类 |
| `cp6.web/src/views/oa/designer/NodePropertyPanel.branchReject.spec.ts` | Create（测试） | 7 用例：split 型渲染/非 split 不渲染、默认 cascade 不落 schema、prune 写入、cascade 回清 |

**未碰** `EdgePropertyPanel.vue`（spec §7.3：inclusive 出边复用既有条件边编辑）。

## 红绿证据
- **基线**：前端 vitest 409 全绿（会话起点亲跑确认）。
- **红**：新增 2 spec（11 用例）跑 → 7 failed（InclusiveGatewayNode.vue 缺失 + panel 无 isSplitGateway/branchReject）。
- **绿**：实现后新 spec 11/11 通过；全量 `npx vitest run` → 63 files / 420 tests 全绿（409+11）。
- `npm run type-check`（vue-tsc --build）→ 0 错误。
- `npm run build` → 通过（唯一告警为既存 chunk>500kB 提示，与本任务无关）。

## 契约要点
- `branchReject` computed：默认 `cascade` 不落 schema（`onBranchReject=undefined`），选 `prune` 才写字段 → 旧流程零污染，与后端 `null=cascade` 语义一致。
- `isSplitGateway`：仅 `parallelSplit`/`inclusiveSplit` 露段。
- palette dot：inclusive 用空心（`background:transparent; border:2px solid var(--cp-warn)`）与 parallel 实心区分，呼应节点空心圆身份。

## E-T1 需种的 i18n 键清单（本任务只引用，五语 seed 归 E-T1）
本任务 template 内 `t()` 新引用键（六键，五语 = ja/zh-CN/zh-TW/en/ko）：
1. `oa.designer.gw.inclusiveSplit`（节点：包容分叉）
2. `oa.designer.gw.inclusiveJoin`（节点：包容汇聚）
3. `oa.designer.gw.branchReject`（属性面板 label：分支驳回策略）
4. `oa.designer.gw.branchReject.cascade`（选项：连坐/级联驳回）
5. `oa.designer.gw.branchReject.prune`（选项：剪枝）
6. `oa.designer.gw.branchRejectHint`（下拉下方 hint 说明）

> 注：D-T1 在 validateClient 引用的 `oa.designer.errInclusiveDefault|errInclusivePair|errBranchReject` 及 `E-WF-019|020|021` 亦属 E-T1 共 12 键五语 seed 范畴（见共享契约）。本任务新引用的是上列前 6 键（gw.* 家族）。

## 疑虑 / 备注
- InclusiveGatewayNode.spec 中 `<Handle>` 用 `stubs:{Handle:true}` 隔离 VueFlow provider（无 store context 会 inject 失败）；Vue Flow 真实渲染 smoke 按计划留 QA harness。
- 提交刻意剔除工作树中 CT2/CT3/DT2 .md 的 LF→CRLF 行尾 churn（非本任务改动），仅提交 D-T2 前端 5 文件 + 本报告。
