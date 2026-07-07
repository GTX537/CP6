# WFS ServiceTask 终审修复报告

分支：`feat/wfs-service-task-finish`　工作目录：`C:\CP6`

## 【必修】kind 切换残留 connectorName → timer 节点静默不可见 webApi 外呼

**文件**：`cp6.web/src/views/oa/designer/NodePropertyPanel.vue`

**实现**：在既有两个 watch（props.node→local 同步 watch、local→emit deep watch）之后新增第三个
watch，监听 `() => local.value.serviceKind`（对 primitive 取值，非 deep，仅在值真变时触发）：

- 新 kind `!== 'webApi'`（timer / dataWriteback）→ 清 `serviceConnectorName` 和 `servicePath`；
- 新 kind `=== 'webApi'` → 清 `serviceActionName`（卫生对称，webApi 无“到点动作”）；
- **timer 的 `serviceActionName` 保留**——那是面板可见的合法“到点动作”，不在清理之列。

清理靠 mutate `local.value.*` 完成，随即触发上方 local deep watch 正常 emit 干净 patch。

**根因链闭合**：cloneNode/emit 全量展开使旧 kind 的字段在切 kind 后残留且 UI 不可见；
运行期 `CP6.Core/Services/Wf/ServiceTaskActionRef.cs:54-73` 的 Snapshot 优先级规则
（timer + ConnectorName → actionKind='webApi'，seed 场景4 依赖之，是合法引擎特性）会据残留字段
到点真调用户以为已删的连接器。前端切 kind 时主动清残留即从源头切断该链。已加注释指向该 .cs
行号并明确“切勿删除本清理”，防后人回退。

**watch 守卫如何防误清**：复用文件既有的 `syncing` 守卫模式。节点切换 / 初始 clone 时，
props.node 同步 watch 先置 `syncing.value = true` 再整体替换 `local.value`（serviceKind 引用随之变化，
会触发新 watch），二者在同一 Vue 调度队列中于 `await nextTick()` 复位 syncing 之前 flush，
故新 watch 首行 `if (syncing.value) return` 命中、跳过清理——只有用户在面板上真实切换下拉时
（syncing 为 false）才执行清理。额外再加 `local.value.type !== 'serviceTask'` 兜底。

## 【顺手修】validateClient timer 校验镜像不对称

**文件**：`cp6.web/src/views/oa/designer/designerModel.ts`

timer 分支原仅查 `serviceDelayValue != null`，漏 `serviceDelayMode`（面板 radio 无默认值，
用户易只填值不点模式，前端放行后被后端 E-WF-016 打回）。改为
`n.serviceDelayValue != null && n.serviceDelayMode != null` 双字段镜像。

**测试**：`cp6.web/src/views/oa/designer/designerModel.serviceTask.spec.ts` 新增用例
“timer with delayValue but no delayMode” → 断言 `errs` 含 `oa.designer.errServiceConfig`。

## 验证

- `cd C:\CP6\cp6.web; npm run test -- designerModel` → **Test Files 2 passed / Tests 13 passed**（含新用例）。
- `$env:NODE_OPTIONS='--max-old-space-size=8192'; npm run type-check`（vue-tsc --build）→ **0 错误**。
