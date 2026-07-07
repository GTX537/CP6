# OA 迁移批次 4 报告 —— Designer 流程设计器族（8 文件）

分支 `feat/ui-migrate-oa`，基线 HEAD=456cde2（批次 1~3 共 15 文件）。

## 页面形态判定

设计器族属批次指南 §页面形态分类中的「非表格特殊页」（vue-flow 画布 + 自绘节点 + 属性面板）。
处置：**只做 token 化与基础件替换（el-tag→CpTag 无损普查），不强套 CpListPage/CpFormDialog 模板**。
API 调用、schema 模型、vue-flow 渲染管线、拖拽/撤销/连线机制、i18n 词条一律零改动。

## 每文件盘点 + 迁移摘要

| 文件 | 保留机制（未动） | 本批改动 |
|---|---|---|
| DesignerView.vue | designerApi.list/load/save/clone、validateClient、克隆弹窗、所有 t() 词条 | scoped 4 处中性色/线条 token 化（bg/card/line/muted）；无 tag/empty |
| DesignerCanvas.vue | VueFlow、useVueFlow、拖拽落点 project()、undo/redo、autoLayout、搜索、快捷键 | scoped chrome 全量 token 化；palette-dot 由 `:style="item.color"` 改为 `:class="dot-<type>"` + scoped token 类，图例与节点边框同源同色（designerModel.ts 未动，item.color 停用无害） |
| NodePropertyPanel.vue | userApi/roleApi 检索、串簽档位增删排序、Group 成员、collapse、所有 t() | `el-tag type="info"`（节点类型只读徽章）→ `CpTag tone="muted"`（保留灰色语义）；scoped 6 处色值 + stage-card 圆角 token 化 |
| EdgePropertyPanel.vue | 条件路径、知會人員 remote 检索、所有 t() | scoped panel-title/label 3 处 token 化；无 tag/empty |
| nodes/StartNode.vue | Handle、data.name 兜底文案 | 节点色语义 token 化（见下表） |
| nodes/ApprovalNode.vue | Handle、approverStrategy 副标 | 节点色语义 token 化 + node-strategy 副标色 |
| nodes/GatewayNode.vue | 菱形 rotate、汇聚/分叉标签 | 节点色语义 token 化（无圆角） |
| nodes/EndNode.vue | Handle、data.name 兜底文案 | 节点色语义 token 化 |

## 语义色映射表（原色值 → token → 语义理由）

节点类型色承载**流程语义**，四类节点保持四种可区分色调（ok/info/warn/muted 互不合并）：

| 原色值 | token | 语义家族 | 用途 / 理由 |
|---|---|---|---|
| `#67c23a` (el success 绿) | `--cp-ok` / `--cp-ok-bg` | ok | Start「填單/发起」节点 = 起点·放行绿 |
| `#409eff` (el primary 蓝) | `--cp-info` / `--cp-info-bg` | info | Approval 审批节点 = 进行中·蓝；节点副标 node-strategy 同色 |
| `#e6a23c` (el warning 橙) | `--cp-warn` / `--cp-warn-bg` | warn | Gateway 并行分叉/汇聚 = 分支·注意橙（两类同型同色，符合原设计） |
| `#909399` (el info 灰) | `--cp-muted` / `--cp-line-soft` | muted | End 结束节点 = 终点·中性灰；节点类型只读徽章 CpTag 亦取 muted 保灰 |
| `#409eff`（stage-index-label 强调数字） | `--cp-brand` | brand | 装饰性强调（档序号），非状态语义 → 归设计系统主强调色（teal） |
| 选中态 `0 0 0 2px #<node>80`（50% alpha 光环） | `color-mix(in srgb, var(--cp-<tone>) 50%, transparent)` | 各节点自色 | 保留「半透明光环」意图（非实心描边）；零硬编码地映射到对应语义 token |

中性 chrome 色（跨全部文件）：`#fff→--cp-card`、`#f5f7fa/#fafafa→--cp-bg / --cp-bg-th`、
`#e4e7ed/#dcdfe6→--cp-line`、`#303133→--cp-ink`、`#606266→--cp-text`、`#333→--cp-text`、
`#909399→--cp-muted`、`#f0f9ff→--cp-bg-hover`、`rgba(0,0,0,.1) 阴影→--cp-shadow-1`、
圆角 `4px→--cp-r-sm`、`20px→--cp-r-xl`、`6px→--cp-r-sm`。

图表系列色豁免（`/* cp-chart-color */`）：**无**。所有语义色均落到 token，无需图形学豁免。

## 验证证据

- `npm run type-check`：**0 error**（NODE_OPTIONS=8192，4096 会 OOM）。
- `npm run test`：**316 passed / 46 files**，与基线持平，无回归。
- 残留硬编码扫描（8 文件 #hex / rgba / box-shadow / border-radius 字面量，排除 var()/color-mix）：**0 命中**。
- 真栈走查（gstack browse，admin/123456，/oa/designer）：
  - 画布渲染：`.vue-flow` 存在、2 节点（start+end）、5 palette 项。
  - palette-dot 计算色 = 节点边框计算色 = 精确 token 值：ok `rgb(34,181,115)` / info `rgb(78,128,238)` / warn `rgb(240,148,10)` / muted `rgb(140,163,171)`。
  - 点节点 → 右侧「ノードのプロパティ」面板出现；节点类型徽章渲染为 `<span class="cp-tag t-muted">start</span>`。
  - 顶栏 検証/保存/コピーとして保存 按钮可点（保存为品牌 teal）；选中 start 节点显示半透明绿色光环（color-mix 生效）。
  - console：无 error；仅存量 `[intlify] Ignore object flatten` warning（既有 i18n 键结构告警，与本批样式改动无关）。
  - 截图：`.superpowers/sdd/shots/oa-designer-batch4.png`（选中 start 节点 + 属性面板全景）。
  - EdgePropertyPanel：初始 schema 无连线，结构与 NodePropertyPanel 同源、type-check 通过；连线面板逃生舱未触发数据态，空态不阻塞。

## 新增模板缺口

**0 条**。逃生舱/模板本体均未触碰，`docs/.../2026-07-04-ui-restyle.md §模板缺口` 台账仍停在 #23。

## Concerns

- 节点选中光环采用 `color-mix(in srgb, …)` 保留原 50% alpha 意图；现代 Chromium 全支持，本工具走 gstack browse 亦无碍。若未来目标浏览器需回退到更老内核，可降级为实心 `var(--cp-<tone>)` 描边。
- `designerModel.ts` 的 NODE_PALETTE.color 字段仍保留 element 十六进制（该文件不在本批 8 文件范围、且带单测）；DesignerCanvas 已改为 type→token 类不再消费它，图例与节点颜色现由 token 单一来源驱动，字段停用无副作用。可在后续批次顺手清理该冗余字段。
