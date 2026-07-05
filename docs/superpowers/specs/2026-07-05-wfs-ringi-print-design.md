# WFS 稟議書打印视图 设计

> 生成于 2026-07-05（brainstorming 已确认，WFS 深化四期 Spec ②）。日企场景闭环件：审批单+传签履历的纸质/PDF 归档。
> 落码位：`cp6.web/src/views/oa/inbox/`（纯前端）+ i18n seed。

---

## §0 范围与决策

**In**：FormDetail「打印」入口 + 稟議書专用打印视图（表头/表单字段/传签履历/印章式签名栏）+ `@media print` 排版 + 五语 i18n + QA。
**Out（→ §5 YAGNI）**：服务端 PDF 生成（QuestPDF 等，有强归档合规要求时再上）；批量打印/合并打印（父子流程各打各的）；自定义打印模板。

| # | 决策 | 依据 |
|---|------|------|
| D1 | **浏览器打印路线**：print stylesheet + 专用视图，用户浏览器打印/另存 PDF；零新依赖 | 用户选项确认 |
| D2 | 只打**已发生**：Forecast 预计段不打印（纸面只承载事实） | 归档语义 |
| D3 | 子流程单各打各的；父单履历中 subFlow 节点行显示子单号（文本引用） | 合并打印 YAGNI |

---

## §1 现状锚点

- `FormDetail.vue`（`views/oa/inbox/`）：已聚合全部数据源——表单字段（FormSchema+FlowData 快照）、传签履历时间线（`Wf_FlowFormTo`，含 Forecast 预计段）、实例头（单号/FlowCode/标题/发起人/日期）。打印视图零新端点。
- 三期移动端波（inbox-ux X-C）会动 FormDetail 布局——本 spec 的打印视图独立组件挂载，互不干扰；执行排其后。
- `FunctionId/FlowCode`（umbrella §2.7）＝稟議書表头的人面编号。

---

## §2 设计

### §2.1 入口与结构

FormDetail 工具栏加「打印」按钮 → 新标签打开**独立路由页** `/oa/inbox/print/:instanceId`（组件 `RingiPrintView.vue`；定案走路由不走对话框 iframe——浏览器打印对独立页面最稳，`@media print` 不与应用壳纠缠）→ 页面渲染预览，用户点浏览器打印/另存 PDF（不自动触发 `window.print()`，先让用户核对）。权限=FormDetail 同口径（能看详情即能打印）。

### §2.2 稟議書排版（A4 纵向）

1. **表头**：稟議書标题（流程名）/ 单号 + FlowCode / 起案者 / 起案日 / 状态（決裁済・却下・進行中）。
2. **表单字段表格**：FormSchema 字段序，label+值双列表格（长文本字段整行）。
3. **传签履历表格**：关卡 / 審査者 / 判定（承認・却下・転送・スキップ）/ 日付 / 意見——只打已发生行（D2）；subFlow 节点行备注列显示子单号（D3）。
4. **印章式签名栏**：按流程关卡横排「枠」（印鑑枠格式：关卡名+氏名+日付），已决关卡填充、未决留白——日企稟議書的视觉惯例。

### §2.3 打印样式

- `@media print`：隐藏应用壳（侧栏/顶栏/按钮）、A4 纵向 `@page { size: A4; margin: 15mm }`、黑白友好（不依赖底色传达语义）、履历表格跨页 `thead` 重复（`display: table-header-group`）、签名栏 `break-inside: avoid`。
- 屏幕态即打印预览（同一排版加纸张阴影）。
- 全部文案 i18n 五语（估 ~15 键：判定词/表头标签/签名栏），日文词条按稟議書惯例用语（決裁/承認/却下/起案者）。

---

## §3 测试与 QA

- vitest：打印视图渲染（已决/在途/驳回三态数据）、Forecast 段不出现、subFlow 行子单号、字段表格 label 映射。
- QA harness（gstack）：真浏览器打印预览走查（三态各一单）+ 移动端不受影响回归。
- 基线：前端全绿；后端零改动（i18n seed 除外）。

---

## §4 分期（供 writing-plans 细化）

- **P-A** RingiPrintView 组件 + 路由 + 排版 + 打印样式。
- **P-B** i18n seed + QA harness + DoD。

依赖：排三期 inbox-ux X-C（移动端改 FormDetail）之后。

---

## §5 YAGNI / 留后

服务端 PDF/批量打印/自定义模板/电子印章图片（署名欄は氏名文字で足りる——图片印鑑需印章管理基建，另立需求）。

---

*生成于 2026-07-05。纯前端增量；E 波（i18n+QA）紧跟；零跨模块污染。*
