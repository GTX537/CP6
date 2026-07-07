# Task D-T1: designerModel — NODE_PALETTE 3 入口 + Service*/IsError round-trip + validateClient(vitest 可测核心)

（摘自 docs/superpowers/plans/2026-06-29-wfs-service-task.md；spec 章节 §5.1；D 波纪律：视图全用 `t()` 运行时键，免重生 keys.generated；TS 类型加在 designerModel；每 Task 跑 `npm run test`(vitest) + `npm run type-check`）

**Files:**
- Modify: `cp6.web/src/views/oa/designer/designerModel.ts`
- Test: `cp6.web/src/views/oa/designer/__tests__/designerModel.serviceTask.spec.ts`(或既有 designerModel spec 同目录)

- [ ] **Step 1: 写失败 vitest**

```ts
import { describe, it, expect } from 'vitest'
import { schemaToGraph, graphToSchema, validateClient, NODE_PALETTE } from '../designerModel'

describe('serviceTask round-trip', () => {
  it('palette has 3 serviceTask entries', () => {
    const st = NODE_PALETTE.filter(p => p.type === 'serviceTask')
    expect(st.map(p => (p as any).kind).sort()).toEqual(['dataWriteback','timer','webApi'])
  })
  it('schemaToGraph/graphToSchema preserves Service* fields', () => {
    const schema = { nodes:[{ id:'s', type:'serviceTask', serviceKind:'webApi', serviceMode:'async',
      serviceConnectorName:'erpEcho', servicePath:'/o', serviceParamsJson:'{}', serviceMaxRetries:3 }],
      edges:[{ from:'s', to:'e', isError:true }] }
    const back = graphToSchema(schemaToGraph(schema as any))
    expect(back.nodes[0].serviceKind).toBe('webApi')
    expect(back.nodes[0].serviceConnectorName).toBe('erpEcho')
    expect(back.edges[0].isError).toBe(true)
  })
  it('validateClient flags incomplete serviceTask', () => {
    const schema = { nodes:[{ id:'s', type:'serviceTask', serviceKind:'webApi' /* 缺 connector/path */ }], edges:[] }
    const errs = validateClient(schema as any)
    expect(errs.some(e => e.includes('errServiceConfig') || e.includes('服务'))).toBe(true)
  })
})
```

- [ ] **Step 2: 跑验证 FAIL** — `cd cp6.web && npm run test -- designerModel.serviceTask`。
- [ ] **Step 3: 实现** — `designerModel.ts`:
  - NODE_PALETTE 加 spec §5.1 三入口(`{type:'serviceTask', kind:'dataWriteback'|'webApi'|'timer', label, color}`)。
  - TS `SchemaNode` 加可选 `serviceKind?/serviceMode?/serviceActionName?/serviceConnectorName?/servicePath?/serviceParamsJson?/serviceDelayMode?/serviceDelayValue?/serviceMaxRetries?/serviceRetryBackoffSec?`;`SchemaEdge` 加 `isError?`。
  - `schemaToGraph`/`graphToSchema`:把这些字段读/写到节点 data / edge data(round-trip);调色板落点时按 kind 预置 `serviceKind`。
  - `validateClient`:serviceTask 缺必填(webApi 缺 connector/path、dataWriteback 缺 action、timer 缺 delay)→ push `errServiceConfig` 文案(镜像后端 E-WF-016)。
- [ ] **Step 4: PASS + `npm run type-check`**。
- [ ] **Step 5: commit** — `git commit -m "feat(wfs-service-task): D-T1 designerModel 调色板+Service*/IsError round-trip+validateClient"`

## 注意（OA UI 迁移 2026-07-05 后补充）
- designerModel.ts 刚经历 UI token 化（OA 批次4）：NODE_PALETTE 的 color 字段已裁定为死字段被消除、只留 label 被消费（DesignerCanvas 不读 color）。新增三入口时**跟随现状**：若现 NODE_PALETTE 项无 color 字段就不要加回，调色板色彩由组件层 token 决定。落地前先读现文件确认结构。
- type-check 用堆 8192：`$env:NODE_OPTIONS='--max-old-space-size=8192'`（4096 会 OOM）。

## 落码纪律
- 工作目录 `C:\CP6`，分支 `feat/wfs-service-task-finish`。本地 commit 不 push。
- 零 Space 污染。TDD 节奏。视图文案全 `t()` 运行时键。
