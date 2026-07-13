# Task T7 报告：服务目录加载失败露显式重试

Status: DONE
Commit: 0dd80b6（已 push feat/wfs-cleanup-tickets）

## 核实证据（先核实现状再动手）
- `NodePropertyPanel.vue` 现状与 brief 描述一致：script 段 catalog 加载逻辑在 81-99 行（行号未漂移），用 `watch(isServiceTask, immediate)` + `catalogLoaded` 标记；失败时 `catalogLoaded=false` 允许下次重试，但仅在 isServiceTask false→true 跳变时重触发——停在 serviceTask 节点则下拉永久空白。缺陷确认属实。
- template 服务任务段「服务类型」下拉 `</el-form-item>` 位于 371 行（brief 锚点准确），其后紧接数据回写块。
- 所需依赖均已就位：`t`(useI18n)、`designerApi`、`ServiceCatalog` 类型、`watch` 皆已 import，无需新增 import。
- i18n seed `I18nOaServiceTaskScreenSeed.cs` 25 个 svc.* 键在位，`reloadCatalog` 全库无重复（grep 仅命中 seed/component/plan 三处，无既有键冲突）。该 seed 已经 E-T2 接入 Program.cs i18n 链，无需改 Program.cs。

## 实现
1. 抽 `loadCatalog()` 函数：进入时 `catalogFailed=false`，成功置 `catalogLoaded=true`，失败置 `catalogFailed=true`（新增失败态 ref）。
2. 保留 `watch(isServiceTask, immediate)` 首拉，仅在未成功加载过时调 `loadCatalog`。
3. template 服务类型下拉后插入 `el-alert`(v-if="catalogFailed")，内嵌 `el-button link` 调 `loadCatalog`，文案 `oa.designer.svc.reloadCatalog`——用户主动重拉入口，不再依赖节点跳变。
4. i18n 键 `oa.designer.svc.reloadCatalog` 五语齐全（重新加载服务目录/重新載入服務目錄/Reload service catalog/サービスカタログを再読み込み/서비스 카탈로그 다시 불러오기），加在重试键组，doc 注释计数 25→26 同步更新。

零硬编码色（el-alert type="warning" 走 Element Plus token）；范围仅 T7，未碰 NodePropertyPanel 的 T8 相关部分。

## TDD 红绿
本组件无独立 vitest（brief Step 4 明确「组件改动无独立 vitest，靠 type-check/build 兜」）。改动为条件渲染 + 函数抽取，无纯逻辑单元可脱离 Element Plus 挂载测试；按 brief 以 type-check + build + 全量回归兜底，未强行引入脆弱的组件挂载测试。

## 验证结果
- 前端 type-check：`NODE_OPTIONS=--max-old-space-size=8192 npm run type-check` → 0 错。
- 前端 vitest：`npm run test` → 390 passed（59 文件），无回归。
- 前端 build：成功（9.05s，仅既有 chunk-size 警告）。
- 后端全量（因改 seed）：`dotnet test CP6.Tests` → 1835 passed / 5 skipped（SQLite 既知限制），达标 ≥1835。
- diff scope：仅 `I18nOaServiceTaskScreenSeed.cs` + `NodePropertyPanel.vue` 两文件，零跨模块污染。

## 疑虑
- 无独立 vitest 覆盖新增的失败重试 UI（brief 授权此取舍）；如需强化可后续加组件挂载测试 mock `designerApi.getServiceCatalog` reject 断言 alert 出现。
- reloadCatalog 键放在「重试键组」而非 brief 建议的 errServiceConfig 附近（语义更贴 svc.* 分组），功能等价、无副作用。
