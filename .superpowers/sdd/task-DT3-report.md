# Task D-T3 报告：NodePropertyPanel 服务任务段 + EdgePropertyPanel 错误边 + 服务目录拉取

**状态：完成。** type-check + build 双绿；零硬编码色值；零 Space 污染。

## 改动文件清单

| 文件 | 改动 |
|---|---|
| `cp6.web/src/api/oa/designer.ts` | 新增 `ServiceCatalogItem`/`ServiceCatalog` 类型 + `designerApi.getServiceCatalog()`（GET `/oa/designer/service-catalog`，剥 Ok2 包壳） |
| `cp6.web/src/views/oa/designer/NodePropertyPanel.vue` | `isServiceTask` 计算属性（`isApproval` 旁）；`catalog` ref + `onMounted` 拉取；`serviceTask` 专属 el-collapse 段（按 `serviceKind` 三分支）；`collapseActive` 加 `'service'` |
| `cp6.web/src/views/oa/designer/EdgePropertyPanel.vue` | 失败边 `el-checkbox` 绑 `local.isError` + 说明 hint（token 化样式） |

`git diff --stat`：3 files，+173 −2。

## 验证输出

- `npm run type-check`（`vue-tsc --build`）：**通过**，零错误。
- `npm run build`：**通过**，`✓ built in 7.67s`。stderr 里的 `NativeCommandError` 仅是 PowerShell 包裹 vite 的 chunk-size 警告（>500kB 提示），非编译错误。
- `NODE_OPTIONS='--max-old-space-size=8192'` 已设。

## delayMode 值与后端对齐核对依据

`ServiceTaskNodeHandler.ComputeDueUtc`（`CP6.Core/Services/Wf/NodeHandlers/ServiceTaskNodeHandler.cs:159-175`）的 `switch (node.ServiceDelayMode)` 分支精确字符串：

```csharp
case "duration":  ...   // line 161
case "untilDate": ...    // line 164
case "untilExpr": ...    // line 167
default:          nowUtc // 非法/缺失降级立即
```

→ radio 三选项值逐字采用 **`duration` / `untilDate` / `untilExpr`**（前端 `el-radio value` 与之一致）。

其余枚举字符串同样对后端常量核实（`CP6.Core/Services/Wf/WfStatus.cs`）：
- `ServiceKind`：`dataWriteback` / `webApi` / `timer`（lines 59-61）。
- `ServiceMode`：`sync` / `async`（lines 67-68）。

## 新引入 i18n 键完整清单（E-T2 按此 seed）

契约面已定、本任务**复用**（非新增）：`oa.designer.svc.title`、`oa.designer.svc.kind.dataWriteback`、`oa.designer.svc.kind.webApi`、`oa.designer.svc.kind.timer`。

本任务**新增 21 键**（全 `oa.designer.svc.*`，E-T2 五语 seed）：

| 键 | 用途 | 建议中文文案 |
|---|---|---|
| `oa.designer.svc.kind` | 服务类型 select 标签 | 服务类型 |
| `oa.designer.svc.mode` | 执行模式 select 标签 | 执行模式 |
| `oa.designer.svc.mode.sync` | 模式选项 | 同步 |
| `oa.designer.svc.mode.async` | 模式选项 | 异步 |
| `oa.designer.svc.action` | dataWriteback 动作下拉标签 | 回写动作 |
| `oa.designer.svc.connector` | webApi 连接器下拉标签 | 连接器 |
| `oa.designer.svc.path` | webApi 路径标签 | 接口路径 |
| `oa.designer.svc.pathHint` | 路径 placeholder | 如 /orders/{id}/writeback |
| `oa.designer.svc.params` | 参数模板标签 | 参数模板 (JSON) |
| `oa.designer.svc.paramsHint` | 参数 placeholder | JSON；支持 $.var / $wf.* 取值 |
| `oa.designer.svc.delayMode` | timer 延时模式 radio 标签 | 延时模式 |
| `oa.designer.svc.delayMode.duration` | 延时模式选项 | 相对时长 |
| `oa.designer.svc.delayMode.untilDate` | 延时模式选项 | 指定日期 |
| `oa.designer.svc.delayMode.untilExpr` | 延时模式选项 | 表达式 |
| `oa.designer.svc.delayValue` | timer 延时值标签 | 延时值 |
| `oa.designer.svc.delayValueHint` | 延时值 placeholder | 如 3d / PT2H / 2026-07-01 |
| `oa.designer.svc.timerAction` | timer 可选到点动作下拉标签 | 到点动作（可选） |
| `oa.designer.svc.maxRetries` | 最大重试次数标签 | 最大重试次数 |
| `oa.designer.svc.backoff` | 退避基数标签 | 退避基数（秒） |
| `oa.designer.svc.errorEdge` | 边面板失败边复选文案 | 失败边（IsError） |
| `oa.designer.svc.errorEdgeHint` | 失败边说明 | 服务任务失败耗尽时沿此边流转；未勾则挂起 |

> 运行时裸键（控制台 fallback=key 本身）为预期，E-T2 seed 后消失。

## 自查发现

1. **剥包壳约定**：`http` 响应拦截器返回 `response.data`（Ok2 信封 `{code,message,data}`）。既有 `designerApi` 兄弟函数返回原始 promise、由调用方 `res.data ?? res` 剥壳（`DesignerView.vue:56`）。为满足 brief 的 `Promise<{actions,connectors}>` 签名，`getServiceCatalog` 内联同一 `res.data ?? res` 约定并返回类型化 data，兜底 `?? []`——语义等价、更内聚。
2. **serviceDelayValue 用 `el-input`（非 `el-input-number`）**：字段是 `string?`（承载 "3d"/"PT2H"/日期串），已按 D-T1 类型 + 后端 `string? ServiceDelayValue` 处理。`serviceMaxRetries`/`serviceRetryBackoffSec` 是 number → `el-input-number`。
3. **字段贯通已核实**：`patchNode`/`patchEdge`（`DesignerView.vue:115/127`）做 `{...n, ...patch}` 合并；两面板 `local` 经 `cloneNode`/`cloneEdge` 的 `...n`/`...e` 扩展保留全部 service 字段与 `isError`，emit 携带 → 合并回 schema → `graphToSchema` 已读 `isError`。链路完整。
4. **onMounted 拉取加 `isServiceTask` 守卫**：面板按 `v-if` 每次选中重挂载，仅 serviceTask 节点触发 catalog GET，避免审批节点无谓请求。
5. **catalog 兜底**：`getServiceCatalog` 对 `actions`/`connectors` 缺失兜 `[]`，下拉空目录时不炸。当前后端实况 actions=[sampleWriteback]、connectors=[erpEcho]，各一项，正常。
6. **视觉纪律**：新样式仅 `edge-error-hint`，用 `--cp-text-muted`（DesignerCanvas 等既有 token，已 grep 核实存在）。面板结构照既有 el-collapse/el-form-item/el-select/el-radio-group 惯例，未引入新组件形态。

## Fix Round 1

**缺陷（中等）：** 服务目录用 `onMounted` + `isServiceTask` 守卫拉取。父组件 `DesignerView.vue` 的 `<NodePropertyPanel v-if=... >` 无 `:key`，node↔node 切换时 Vue 复用组件实例不重挂载；若首挂时选中的是非 serviceTask 节点，此后切到 serviceTask 节点 catalog 永远不拉，动作/连接器下拉为空。

**修法（仅改 NodePropertyPanel.vue）：** `onMounted` 换成 `watch(isServiceTask, ..., { immediate: true })` + `catalogLoaded` 已加载标记；拉取失败时回滚标记以允许重试。移除不再使用的 `onMounted` import。

```ts
const catalogLoaded = ref(false)
watch(
  isServiceTask,
  async (v) => {
    if (!v || catalogLoaded.value) return
    catalogLoaded.value = true
    try {
      catalog.value = await designerApi.getServiceCatalog()
    } catch {
      catalogLoaded.value = false // 允许下次重试
    }
  },
  { immediate: true },
)
```

**验证：**
- `npm run type-check` → 0 错（vue-tsc --build 干净退出）
- `npm run build` → 通过（2583 modules transformed，仅 chunk >500kB 体积警告，无错误）

未动 DesignerView.vue。
