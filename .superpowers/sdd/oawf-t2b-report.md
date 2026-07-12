# M-OA/WF T2 追补：双栈收编 执行报告

生成于 2026-07-12。任务：用户裁决=收编，补 Sys_Menu 行使旧设计器 `/wf/form-designer`、`/wf/flow-designer` 可达。

## 实现内容

1. **`CP6.WebApi/Seed/OawfMenuSeed.cs`**：Rows 追加两行
   - `(741, "フォームデザイナー(旧)", "/wf/form-designer", "Edit", 740, 741, null)`
   - `(742, "フローデザイナー(旧)", "/wf/flow-designer", "Edit", 740, 742, null)`
   - MenuKey 均留 null（权限维持锚 738 `oa-designer:edit`/`oa-designer:form-save`，若 741/742 也赋非空键会与 738 撞 `Sys_Menus.MenuKey IS NOT NULL` 过滤唯一索引）；类头注释「双栈收编裁决」段落说明理由及回填后果（派生 `wf-form-designer`/`wf-flow-designer`，无 RoleAction 引用，无害，与 MES 非锚定行同型）。
   - RoleMenu 授 admin（RoleId=1），沿用既有循环逻辑自动处理。
   - 防御矫正块（`r.Key != null` 过滤）天然跳过 741/742，不会误赋值。

2. **段位查证**：全仓 grep `CP6.WebApi/Seed/*.cs` + `Program.cs` + `Migrations/*.cs`（MenuId 字面量），确认 741/742 无占用——OA 段止于 740，PLAN 段 730–732 不重叠；迁移文件名中出现的 `741`/`742` 子串（如 `20260523064741_...`、`20260616143741_...`）系时间戳误命中，非 MenuId。取号照 `ErpMenuSeed` 五孤儿收编先例（216–220）就近连续取号。

3. **前端可达性核对**：`cp6.web/src/router/index.ts:46-47` viewModules 现有映射：
   ```
   '/wf/form-designer': () => import('@/views/wf/designer/FormDesigner.vue'),
   '/wf/flow-designer': () => import('@/views/wf/designer/FlowDesigner.vue'),
   ```
   与新增两行 RoutePath 逐字一致，`addDynamicRoutes` 匹配条件满足，收编后前端可达。

4. **`CP6.Tests/OawfMenuSeedTests.cs`**：追加 2 用例
   - `EnsureSeeded_CollectsOrphanDesignerRoutes_741And742_WithNullMenuKeyAndRoutePathMatchingViewModules`：断言 741/742 存在、RoutePath 精确、MenuKey null、ParentId=740、Enable=true、RoleMenu 授 admin。
   - `EnsureSeeded_IsIdempotent_WithOrphanCollectionRows_NoDuplicatesOnSecondRun`：断言二次调用不重复、MenuKey 仍 null、未与 7 锚定键共键（`oa-designer` 仍恰一行持有于 738）。
   - 同步订正既有 `EnsureSeeded_IsIdempotent_NoDuplicateRowsOrRoleMenus` 硬编码行数 8→10（Rows 总数从 8 增至 10）。

5. **文档同步**：
   - `docs/seeds/oawf-key-menu-anchor.md` 追加「T2 追补：双栈孤儿路由收编」节（表格+段位查证+前端核对+RoleMenu+唯一索引安全说明），并删除旧「待用户裁决」措辞。
   - `docs/seeds/oawf-menu-seed.sql` 追加 741/742 对照 INSERT 语句块（幂等 WHERE NOT EXISTS 写法，与既有 UPDATE 块同风格）。
   - `docs/seeds/oawf-permission-keys.md`（真相源）§六头号裁决点标题追记「已裁决=收编」，裁决段落正文追记落地说明+文件指向，§七计数收口同步措辞（menu-key 仍 7，孤儿路由标注已收编而非待裁决）。

## 验证

- `dotnet test --filter OawfMenuSeedTests`：8/8 passed（6 原有 + 2 新增）。
- 全量 `dotnet test`：**1771 passed**, 5 skipped（既有 skip，非本任务引入），0 failed。基线 1769 → 1771（+2 恰为新增测试数），无回归。

## Self-review checklist

- [x] 段位查证写了（grep Seed 目录+Program.cs+Migrations，741/742 无占用，含迁移文件名误命中排除说明）
- [x] RoutePath 与 viewModules 逐字对照写了（router/index.ts:46-47 引用原文对照）
- [x] MenuKey null 注释在（OawfMenuSeed.cs 类头「双栈收编裁决」段落 + Rows 内联注释 + 文档表格「裁决」列）
- [x] 真相源裁决追记了（oawf-permission-keys.md §六标题+正文+§七计数收口三处）

## Commit

`ac733ee` — `feat(oawf): M-OA/WF T2 追补——双栈孤儿路由收编(用户裁决2026-07-12)`，已 push 至 `feat/m-oawf-crosscutting`。

## Concerns

- 无功能性 concern。唯一需留意点：741/742 MenuKey 为 null，属「挂菜单树但不承载权限」类行，与 738 共享 `oa-designer:*` 权限语义——若未来审计要求区分新旧栈权限面，需重新拍板（当前用户裁决明确「不删旧栈端点、权限面不变」，故此为预期设计而非缺陷）。
