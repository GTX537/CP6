# Task T8 报告：定时器到点动作补 webApi 连接器/路径变体 UI（spec §5.3 缺口）

**Status: DONE** — commit `d7135b7`（分支 feat/wfs-cleanup-tickets，已 push）

## 核实证据（行号漂移已核）
- brief 写 timer 分支 `:442-469`、清理 watch `:56-68`。实读 `NodePropertyPanel.vue`：清理 watch 确在 `:56-68`（T7 未动此段）；timer 分支实际漂到 `:459-487`（T7 在其上方加了 catalogFailed 重试 alert，推低 ~17 行）。改动照实际行号定位，与 T7 的 loadCatalog/catalogFailed 段共存零回退。
- 当前 timer 分支确实只有单个「到点动作」下拉（`serviceActionName`，dataWriteback 语义），无连接器/路径入口 —— 缺口属实。
- 旧清理 watch `kind !== 'webApi'` 分支会清空 `serviceConnectorName/servicePath` —— 若 timer 直接加连接器字段会被立即清掉，属实。
- 后端 `ServiceTaskActionRef.Snapshot`（`:59-73`）确认优先级：timer + ConnectorName → `webApi`，先于 ActionName 判定。故「选回写/无时必须清 ConnectorName」是防到点静默外呼的硬要求。

## 改动（3 文件，diff 干净无跨模块污染）
1. `CP6.WebApi/Seed/I18nOaServiceTaskScreenSeed.cs`：加 `timerActionKind` + `.none/.write/.api` 四键，五语齐全；doc-comment 计数 26→30 同步。
2. `cp6.web/src/views/oa/designer/NodePropertyPanel.vue`：
   - 重写 serviceKind 清理 watch：`dataWriteback` 清连接器/路径、`webApi` 清到点动作、`timer` 不清（三字段均可能合法）。
   - 新增 `timerActionKind` computed：getter 从已填字段派生（连接器优先→api，与 Snapshot 一致）；setter 互斥清非选中变体（write/none 必清 ConnectorName）。
   - timer 模板：加「到点动作类型」下拉 + 按类型渲染 回写动作下拉 / 连接器+路径。零硬编码色。
3. 新增 `cp6.web/src/views/oa/designer/NodePropertyPanel.timer.spec.ts`：7 用例（mount 组件，i18n+ElementPlus plugin，mock designerApi）。

## 红→绿
- 红：先写 7 用例跑 `vitest run <spec>` → **6 failed | 1 passed**（timerActionKind computed 不存在=getter undefined、watch 误清、模板无 api 变体）。
- 绿：实现后同 spec **7 passed**。

## 测试摘要
- 前端 vitest：**397 passed / 60 files**（基线 390 + 新 7），零失败。
- type-check（vue-tsc，8G 堆）：0 错。build：成功（仅既有 chunk-size 提示）。
- 后端全量 `dotnet test`：**Passed! 1835 / Skipped 5 / Failed 0**（基线 ≥1835 达标；5 skip 为 SQLite 既知限制）。

## 疑虑
- 无阻断。边角：若历史脏数据一个 timer 节点同时存 connector+action，getter 归 'api'（连接器优先，与 Snapshot 判定一致），action 残留在 local 直到用户切走 —— 行为与运行期 Snapshot 一致，不引入新静默差异。
- 未跑 `ef migrations has-pending-model-changes`：本改动仅新增 Sys_Lang 运行期种子数据（SeedLangs 插入），无实体/DbSet/模型配置变更，无 pending 迁移风险。

---

## 修复节（审查 Critical：票面 Step 2 设计缺陷）— commit `787b95b`

**缺陷（照抄票面处方引入）**：`timerActionKind` 纯 computed——getter 从字段推导、setter 只清字段不写状态。新建 timer 从 'none' 选 'api' 后 getter 因 connector 仍空立即弹回 'none'，connector/path 子表单永不渲染（'write' 同理鸡生蛋），且原无条件显示的到点动作下拉被 v-if 门住 → dataWriteback timer 配置路径也断。

**修复（按审查处方）**：
- `timerActionKindState` 独立 backing ref：初始化用 `deriveTimerActionKind()`（connector→'api'，action→'write'，否则 'none'，优先级与 Snapshot 一致）。
- 重推导时机：①props→local 同步且节点 id 变；②同节点回声（emit→父写回→deep watch）仅在派生 ≠'none' 时覆盖——保护用户刚选中、尚未填字段的 pending 选择不被弹回；③serviceKind 切「到」timer 时（带残留字段切来直接落对应变体）。
- 用户选择经 computed setter 直接驻留 ref；互斥清理保留在 setter（选 write/none 必清 serviceConnectorName，防到点误外呼）。子表单 v-if 读 ref。

**红→绿证据**：新 3 回归用例先对纯 computed 版（stash 本修复后跑 d7135b7 代码）= **3 failed | 7 passed**（失败点正是驻留断言 `toBe('api')` 得 'none'）；修复后 **10 passed**。

**测试**：前端 vitest **400 passed / 60 files**（397+3）；type-check 0 错；build 过。本轮未碰后端文件（seed 无改动），后端基线沿用上节 1835 绿。

**插曲**：修复中途 C 盘再次 100% 满（WU Download 缓存复胀，同 7/12 事故模式）——已停 wuauserv 清缓存复位，释放 2.4GB 后继续。扩容仍是治本建议。
