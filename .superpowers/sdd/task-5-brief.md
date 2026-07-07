### Task 5: 前端 v-permission 接线（4 页按钮）

**Files:** SpaceSiteView/SpaceFloorView/SpaceCodeRuleView/SpacePublishView（+spec 各 1 断言）

规格：按钮贴 `v-permission="'<menuKey>:<action>'"`，键与 Task 4 映射表逐字一致——site 页新建/编辑/削除（add/edit/delete）；floor 页同（编辑器跳转按钮**不贴**——页面级菜单权限已管）；code-rule 页新建/编辑/削除/生码相关（预览只读不贴）；publish 页生成编码（space-code-rule:generate）/发布（space-publish:publish）/停用（deactivate）/采纳（adopt）。指令 fail-open（store 未加载保留元素）——admin 全授权下 UI 无变化。
测试：每页 spec 加 1 断言——mock permission store `loaded=true` 且缺某键时对应按钮从 DOM 移除（v-permission mounted 移除元素；照 directives/permission.ts 行为；store mock 用 pinia testing 或直接 stub usePermissionStore——看既有 spec 有无先例，无则最小 stub）。

- [ ] Step 1: TDD → 实现 → type-check/vitest/build 三件套 → Commit `feat(space): 管理与发布页按钮接入 v-permission`

---

