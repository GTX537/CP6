## Task T9: 错误边（IsError）画布视觉区分（danger 虚线，Design System token）

> **票9。** 缺陷：错误边（`FlowEdge.IsError`）在 Vue Flow 画布上与普通边**零视觉区分**——`schemaToGraph`（`designerModel.ts:84-90`）把 `isError` 只塞进 `edge.data`，不设 `style/class`，渲染成默认灰边。用户在画布上无从辨认哪条是失败边。修法=在 `schemaToGraph` 建边时，`isError===true` 的边加 danger 色虚线 `style`（用 `var(--cp-danger)`，token 已定义于 `tokens.css:14` `#E5484D`；**禁硬编码色**）。属性面板切换复选后经 `graphToSchema→父→schemaToGraph` 重建，样式随之刷新。

**Files:**
- Modify: `cp6.web/src/views/oa/designer/designerModel.ts:84-90`（`schemaToGraph` 建边加条件 style）
- Test: `cp6.web/src/views/oa/designer/designerModel.serviceTask.spec.ts`（新增：isError 边带 danger style）

- [ ] **Step 1: 写失败 vitest** — `designerModel.serviceTask.spec.ts` 追加：

```typescript
import { schemaToGraph } from './designerModel'

describe('error edge visual', () => {
  it('isError edge gets danger dashed style; normal edge does not', () => {
    const g = schemaToGraph({
      nodes: [
        { id: 'svc', type: 'serviceTask' } as any,
        { id: 'end', type: 'end' } as any,
        { id: 'h', type: 'approval' } as any,
      ],
      edges: [
        { from: 'svc', to: 'end' },              // 普通边
        { from: 'svc', to: 'h', isError: true }, // 失败边
      ],
    } as any)

    const normal = g.edges.find(e => e.target === 'end')!
    const err = g.edges.find(e => e.target === 'h')!

    // 普通边无自定义 stroke；失败边用 danger token 虚线
    expect((err.style as any)?.stroke).toBe('var(--cp-danger)')
    expect((err.style as any)?.strokeDasharray).toBeTruthy()
    expect((normal as any).style?.stroke).toBeUndefined()
    // data.isError round-trip 不受影响
    expect((err.data as any)?.isError).toBe(true)
  })
})
```

- [ ] **Step 2: 跑验证 FAIL** — `cd cp6.web && npx vitest run src/views/oa/designer/designerModel.serviceTask.spec.ts`。

- [ ] **Step 3: 实现** — `designerModel.ts:84-90` 的 `edges` map 替换为：

```typescript
  const edges: VFEdge[] = (schema.edges ?? []).map(e => ({
    id: `${e.from}__${e.to}`,
    source: e.from,
    target: e.to,
    data: { condition: e.condition, ccUsers: e.ccUsers, isError: e.isError },
    label: e.condition || undefined,
    // 票9：失败边（IsError）用 danger 虚线视觉区分。颜色走 Design System token（禁硬编码色）。
    ...(e.isError === true
      ? { style: { stroke: 'var(--cp-danger)', strokeWidth: 2, strokeDasharray: '6 4' }, class: 'edge-error', animated: false }
      : {}),
  }))
```

  > `graphToSchema`（`:113-118`）只读 `data.isError`，不读 `style`——round-trip 无损，样式纯呈现层。

- [ ] **Step 4: 跑验证 PASS + type-check**
```bash
cd cp6.web
npx vitest run src/views/oa/designer/designerModel.serviceTask.spec.ts
NODE_OPTIONS=--max-old-space-size=8192 npm run type-check
```

- [ ] **Step 5: commit**
```bash
git add -A && git commit -m "fix(wfs-service-task): T9 错误边画布视觉区分（danger 虚线，Design System token）"
```

---

## Global Constraints（每个 Task 都遵守）

- **测试基线不回归：**
  - 后端：`dotnet test CP6.Tests/CP6.Tests.csproj` 全绿——基线 **1509 测试**（5 skip = SQLite 既知限制）。`--filter Wf` 既有 Wf 测试字节等价（除本计划显式改动的测试断言外）。
  - 前端：`npm run test`（vitest run）**320 全绿** + `npm run type-check` 通过。**type-check 须大堆**（vue-tsc 内存密集）：
    - Bash 工具：`NODE_OPTIONS=--max-old-space-size=8192 npm run type-check`
    - PowerShell：`$env:NODE_OPTIONS='--max-old-space-size=8192'; npm run type-check`
- **EF 迁移 clean：**`dotnet ef migrations has-pending-model-changes --project CP6.Core/CP6.Core.csproj --startup-project CP6.WebApi/CP6.WebApi.csproj --context CP6Context` 报无 pending（本计划**不新增迁移**——无实体/DbSet 改动）。
- **零跨模块污染：**只碰 `CP6.Core/Services/Wf/**`、`CP6.WebApi/{Program.cs,Middleware,Seed}`、`cp6.web/src/views/oa/designer/**`、`cp6.web/src/utils/signalr.ts`、对应 `CP6.Tests/Wf/**`。**绝不碰** `views/space/**`、`Services/*Space*`、任何 Space 迁移/DbSet。每 Task 完成 `git show --stat` 复核 diff。
- **零硬编码色：**前端一切颜色走 Design System token（`var(--cp-danger)` 等，见 `cp6.web/src/styles/tokens.css`），禁十六进制字面量。
- **i18n 五语齐全：**任何新增文案键必须五语齐全 `ZhCN/ZhTW/En/Ja/Ko`，加进 `I18nOaServiceTaskScreenSeed.cs`，运行期 SeedLangs 幂等去重。
- **TDD 节奏：**先写失败测试→跑验证 FAIL→最小实现→跑验证 PASS→本地 commit（**不 push**）。提交信息风格：`fix(wfs-service-task): <中文描述>`。
- **独立性：**11 个 Task 互不依赖，可任意顺序 / 并行执行。建议顺序见文末「执行顺序」。

