# Task 7 报告：波5 生命周期页错误呈现统一(前端)

分支 `feat/space-wave5`。执行日 2026-07-12。TDD 全绿：新增测试先落红→实现→377 全通过，type-check 0 错。

## 交付物

| 文件 | 变更 |
|---|---|
| `cp6.web/src/i18n/tOr.ts` | 新建。`tOr(i18n, key, fallback?)` 纯函数（`te?t:(fallback??key)`）+ `useTOr()` 组合式包装。 |
| `cp6.web/src/i18n/__tests__/tOr.spec.ts` | 新建。三例：已注册→译文 / 未注册+fallback→fallback / 未注册无fallback→key 本身。 |
| `cp6.web/src/views/space/lifecycle/SpacePublishView.vue` | ①重复码 `<el-collapse>` 明细（仅 `duplicateGroups.length>0`，逐组标题 `dupGroupTitle{n,c}` + 冲突库位 id 列表）②规则错误旁 `<router-link :to="'/space/code-rule'">`（`goCodeRule`）③`onGenerate` 生码前 `pcSeq` 快照，响应处理前 `seq!==pcSeq` 即 return ④watcher else 分支清空 precheck 时 `pcSeq++`。 |
| `cp6.web/src/views/space/lifecycle/SpaceEventsView.vue` | `showError` 经 `useTOr()` 译出 `row.lastError`（E-SPACE 码词条化，非码原样）。 |
| `cp6.web/src/views/space/lifecycle/__tests__/SpacePublishView.spec.ts` | mountView 加 `RouterLinkStub`；新增 5 例（collapse 明细渲染/无重复不渲染/去规则页链接 to 断言/无错误不渲染/生码seq守卫丢弃过期提示）。 |
| `docs/seeds/space-i18n-seed-2.sql` | 新增 `space.publish.goCodeRule` + `space.publish.dupGroupTitle`（五语齐全），头注/尾注计数 129→131、publish 45→47（MERGE 幂等，部署时重跑生效）。 |

## 关键实现说明

- **DTO 真相**：后端 `CodePrecheckResp.DuplicateGroups = List<List<Guid>>`（前端 `string[][]`），每组是**冲突 LocationId 列表**，不含「重复码」本身——故 collapse 逐组渲染库位 id 列表，组标题用 `dupGroupTitle{n,c}` 表达「第 n 组、c 个库位」。
- **tOr 双形态**：纯函数便于脱离组件单测（注入 `i18n.global` composer）；`useTOr()` 供组件 setup 用。
- **生码 seq 守卫**：复用既有 `pcSeq`（runPrecheck 内 `++pcSeq`）。生码在途切楼层/库区 → watcher 触发新 precheck bump pcSeq → 过期生码回填时 `seq!==pcSeq` 丢弃成功提示与重检。watcher else 分支 `pcSeq++` 堵住「清空后在途 precheck 回填复活」。
- **RouterLink 测试**：三生命周期视图既有约定是 `vi.mock('vue-router', useRouter)`（不注册 RouterLink 全局组件），故 spec 的 mountView 追加 `stubs:{RouterLink:RouterLinkStub}`，以 `findComponent(RouterLinkStub).props('to')` 断言路由目标。

## 验证

- `npm run test`：**377 passed / 58 files**（基线 369 → +8 新测试；tOr 3 + PublishView 5）。
- `NODE_OPTIONS=--max-old-space-size=8192 npm run type-check`：**exit 0，零错误**。

## 疑虑 / 待确认

1. **i18n 键仅入 seed 未跑 DB**：新键已五语补进 `space-i18n-seed-2.sql`（MERGE 幂等），需在部署/上线时对目标库重跑该 seed，键才在运行时可见（测试用本地 i18nPlugin，不依赖 DB）。
2. **precheckErrors 文本未过 tOr**：brief 第 5 项仅指定 EventsView.lastError 词条化；PublishView 的规则错误列表维持原样透出（未扩大范围）。若产品希望规则错误码亦本地化，可后续用 tOr 包一层（低风险）。
