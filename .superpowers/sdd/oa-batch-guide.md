# OA 模块迁移批次指南(feat/ui-migrate-oa)

适用于 Milestone C 首模块 OA 的每个迁移批次。实现者在派发消息中收到本批页面清单,其余规则全在本文件。

## 样板与契约

- **样板页(已迁移已审查,严格模仿)**: `cp6.web/src/views/wms/OutboundOrderListView.vue` — CpPageShell(:count + @total-change 接线) > CpListPage(columns/searchFields/fetch 包装/toolbar slot/col-<prop> slot/map 列映射/kind:'date')。
- 组件契约以各组件文件头注释为准: `src/components/templates/CpListPage.vue`(ListColumn: width/minWidth/overflowTooltip/fixed/kind[text|num|mono|tag|date]/map;map 纯函数,tone 仅 kind:'tag' 生效)、CpFilterBar(FilterField, labels)、CpFormDialog(fields/rules/submit/labels/requiredMessage)、CpDetailPanel、CpTag(Tone 类型)、CpEmpty、CpStatusStrip。
- 页面形态分类: 查询列表页→CpPageShell+CpListPage;页内编辑弹窗→CpFormDialog;详情区→CpDetailPanel;非表格特殊页(仪表盘/监控图表类)→只做 token 化与基础件替换,不强套模板。

## 每页纪律(Task 11 标准原样适用)

1. **迁移前盘点**(写进批次报告): API 调用/列定义/搜索字段/批量操作/行操作/权限指令 v-permission/i18n 词条——**一项不许丢**。
2. i18n: 所有原 t() 词条保留;CpFilterBar 按钮用 `:filter-labels`(search→common搜索词条(按原页grep确认,OA多用t键不同于wms.common.*——以原页现有键为准,不臆造)、reset→同上,与样板一致);不臆造 Sys_Langs 词条。
3. 码值列用 `kind:'tag'` + `map`(label 走 t() computed,tone 用共享 Tone 类型);原页无 tag 视觉的映射列用无 kind 的 map(仅换文案)。日期列用 `kind:'date'`(=slice(0,10);若原页格式不同则用 col slot 保原样)。
4. scoped style 目标归零,只许残留纯布局;**禁止硬编码色值/阴影/圆角**(图表系列色 §2.5 豁免,行尾加 `/* cp-chart-color */`)。
5. 模板表达不了的形态: col-<prop>/toolbar/expand slot 是逃生舱;slot 也不够时**保留旧机制保功能**,并在 `docs/superpowers/plans/2026-07-04-ui-restyle.md` §模板缺口 追加新条目(编号接续)。
6. 不改后端/API/路由/localStorage.menus/i18n 机制;不改模板组件本体(发现模板 bug → 报告并停,别绕)。

## 批次验证(每批必做)

1. `npm run type-check` 0 error;`npm run test` 全绿(现有 294+)。
2. 真栈走查本批每一页: dev server 5173(需 `VITE_API_TARGET=http://localhost:9991`,先验证 `POST http://localhost:5173/api/auth/login` 200,失败则重启);gstack browse 技能,admin/123456;每页: 打开、列表加载、查询/重置、翻页(有数据时)、行/头部按钮可点(不必真提交业务单据)、console 无新错误;截图存 `.superpowers/sdd/shots/wms-<页名>.png`。无菜单入口的页面用路由直达;无数据的页面验证空态渲染即可,并在报告注明。
3. 本批一个 commit: `refactor(ui): OA 迁移批次<N>——<页1>/<页2>/...` + trailer `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`。

## 报告

写满盘点表+每页迁移摘要+验证证据到派发消息给定的报告文件。回复(≤12 行): Status / commit SHA / 测试与走查一行 / 新增模板缺口数 / concerns。
