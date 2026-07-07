# 波4 Task 5 Report: 前端 v-permission 接线（4 页按钮）

## Status
DONE — commit `54f4080` on `feat/space-wave4-crosscutting`（单 commit）。

## Implemented
四页管理/发布按钮贴 `v-permission="'<menuKey>:<action>'"`，键与后端 Task 4（`CP6.Tests/Space/SpacePermissionAttributeTests.cs` L27-31）逐字一致。指令已在 main.ts 全局注册（`app.directive('permission', permission)`）：store loaded 且缺键→removeChild，未加载 fail-open。本 Task 未触碰指令本体。

## 贴点清单
| 页面 | 按钮 | 权限键 | 不贴 |
|---|---|---|---|
| SpaceSiteView | 新建 / 编辑 / 削除 | space-site:add / edit / delete | 「楼层」跳转 |
| SpaceFloorView | 新建 / 编辑 / 削除 | space-floor:add / edit / delete | 「編集画面」跳转 |
| SpaceCodeRuleView | 新建 / 编辑 / 削除 | space-code-rule:add / edit / delete | 预览（只读） |
| SpacePublishView | 生成编码 / 发布 / 停用 / 存量采纳 | space-code-rule:generate / space-publish:publish / space-publish:deactivate / space-publish:adopt | 预检重跑等只读手势 |

CpListPage 行内按钮在 `#col-_action` slot 内——v-permission 直接贴 el-button，指令 mounted 时移除元素（fixed:'right' 双渲染的两份皆被移除）。publish 页 deactivate 键贴在 `v-if="row.status===1"` 的按钮上（两指令共存）。

## 测试 stub 方案
- **既有先例**：4 个 spec 此前均无 pinia、无 usePermissionStore mock 先例（模板此前无 v-permission）。故采最小自建 stub。
- **store mock**：每 spec 顶部 `vi.hoisted` 造 `permHas` 持有器 + `vi.mock('@/stores/permission', () => ({ usePermissionStore: () => ({ loaded: true, has: (k) => permHas.fn(k) }) }))`。默认 `has()=>true`（全授权，既有断言零涟漪）；`beforeEach` 复位 `permHas.fn=()=>true`；新断言内翻转 `permHas.fn=(k)=>k!=='<被测键>'`。此设计避免污染同文件其它测试（尤其 SpacePublishView 6 个既有测试依赖发布按钮存在）。
- **指令注册**：mount 时 `global.directives: { permission }`（import `@/directives/permission`）——最小方案，无需装全套 plugin；store 已 mock 成纯函数，`usePermissionStore()` 不需活动 pinia。SpaceCodeRuleView 的 mountView 用 `directives` 置于 opts 展开之前，保证预览测试（传自定义 global）仍保留指令。
- **断言**：缺 `:delete`/`:publish` 键 → 该按钮从 DOM 移除（filter length===0 / find undefined）；同页排他键按钮（编辑 / 采纳）仍在。site/floor/code-rule 页 el-button 未解析为真组件（无 ElementPlus）→ 查 `'el-button'` 标签；publish 页装了 ElementPlus → 查真实 `'button'`。

## Files changed (8)
- cp6.web/src/views/space/master/SpaceSiteView.vue
- cp6.web/src/views/space/master/SpaceFloorView.vue
- cp6.web/src/views/space/lifecycle/SpaceCodeRuleView.vue
- cp6.web/src/views/space/lifecycle/SpacePublishView.vue
- cp6.web/src/views/space/master/__tests__/SpaceSiteView.spec.ts
- cp6.web/src/views/space/master/__tests__/SpaceFloorView.spec.ts
- cp6.web/src/views/space/lifecycle/__tests__/SpaceCodeRuleView.spec.ts
- cp6.web/src/views/space/lifecycle/__tests__/SpacePublishView.spec.ts

## Verification
- type-check（`node --max-old-space-size=8192 vue-tsc --build`）：0 error。
- vitest full：57 files / **368 passed**（364 基线 + 4 新断言）。
- vite build：✓ built（仅既有 chunk>500kB 提示）。

## Self-review
- 键逐字核对后端 Task 4 权限清单（SpacePermissionAttributeTests L27-31），完全一致。
- SpaceFloorView 删除按钮 fixed 列双渲染——两份均被指令移除，断言 length===0 稳健。
- fail-open 语义（loaded=false 保留）由指令保证；admin 全授权下 UI 无变化。
- 未贴：所有跳转/预览/只读手势，符合 brief「页面级菜单权限已管」。
- 仅 8 个目标文件变更；未提交 dist / picture / shots 等无关文件。
