# Space Editor Tool Feedback Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make every Space Editor toolbar action visibly understandable without changing the existing scene data, command stack, API, or database behavior.

**Architecture:** Add a pure tool-feedback model that maps editor mode and single-rack selection to translated guidance and cursor classes. Render that model in `FloorEditor.vue`, keep action-specific feedback in the page, and isolate Konva Transformer presentation in a small rotation-style module used by `RotateTool`.

**Tech Stack:** Vue 3 `<script setup>`, Pinia, Element Plus, TypeScript 6, Konva 10, Vitest 4, Vue Test Utils, Playwright/Chrome for final local acceptance.

---

## File Map

- Create `cp6.web/src/views/space/editor/toolFeedback.ts`: pure mapping from `ToolType` and single-rack selection to title/message keys and cursor class.
- Create `cp6.web/src/views/space/editor/toolFeedback.spec.ts`: unit coverage for every tool mode and safe fallback.
- Create `cp6.web/src/views/space/editor/FloorEditor.feedback.spec.ts`: component-level feedback, reverse-model prerequisite, and export-result coverage.
- Modify `cp6.web/src/views/space/editor/FloorEditor.vue`: render the persistent hint, expose pressed state, apply cursor classes, keep reverse modeling clickable, and report export success.
- Create `cp6.web/src/space-editor/interact/rotate/rotateHandleStyle.ts`: active/inactive Transformer presentation constants and application helper.
- Create `cp6.web/src/space-editor/interact/rotate/rotateHandleStyle.spec.ts`: focused style application and restoration coverage.
- Modify `cp6.web/src/space-editor/interact/tools/RotateTool.ts`: apply and restore the approved high-visibility handle style.
- Modify `docs/project-memory/PROJECT_STATE.md`: record the verified local editor-feedback result.
- Modify `docs/project-memory/05-Completed.md`: record the completed capability after user acceptance.
- Modify `docs/project-memory/06-Todo.md`: retain only the boundary for later direct-manipulation redesign, not this closed bug.
- Modify `docs/project-memory/CHANGELOG-AI.md`: add the implementation and verification summary.

## Execution Preconditions

- Work only in `D:\CP6\.claude\worktrees\space-editor-tool-feedback-20260830` on branch `codex/space-editor-tool-feedback-20260830`.
- The branch starts from `origin/main@47263a498caadcb545092ca617e3d86633e9bea5`; the approved design commit is `1b612d53`.
- Do not edit or clean the dirty root worktree.
- Do not operate Docker. Browser acceptance uses the already-running local API on `5177`, local CP6DB, and the composite acceptance frontend on `18080`.
- The composite acceptance branch already contains the polygon-compatibility and viewport-navigation fixes. Keep those independent commits out of this task branch; integrate this task into the composite branch only after all task-branch gates pass.

### Task 1: Pure Tool Feedback Model

**Files:**
- Create: `cp6.web/src/views/space/editor/toolFeedback.spec.ts`
- Create: `cp6.web/src/views/space/editor/toolFeedback.ts`

- [ ] **Step 1: Write the failing feedback-model tests**

```ts
import { describe, expect, it } from 'vitest'
import { getEditorToolFeedback } from './toolFeedback'

describe('getEditorToolFeedback', () => {
  it.each([
    ['select', '选择模式', '单击选择货架；拖动空白区域可框选', 'tool-cursor-select'],
    ['drag', '拖拽模式', '拖动画布可平移视角；拖动货架可移动货架', 'tool-cursor-drag'],
    ['marker', '打点模式', '单击画布添加标注点，可使用撤销取消', 'tool-cursor-crosshair'],
    ['zone', '新建库区', '在画布上拖出矩形范围，然后填写库区信息', 'tool-cursor-crosshair'],
  ] as const)('maps %s to persistent guidance', (tool, titleKey, messageKey, cursorClass) => {
    expect(getEditorToolFeedback(tool, false)).toEqual({ titleKey, messageKey, cursorClass })
  })

  it('asks for one rack before rotation', () => {
    expect(getEditorToolFeedback('rotate', false)).toEqual({
      titleKey: '旋转模式',
      messageKey: '先单击一个货架，再拖动高亮圆形手柄',
      cursorClass: 'tool-cursor-crosshair',
    })
  })

  it('explains snapping after one rack is selected for rotation', () => {
    expect(getEditorToolFeedback('rotate', true)).toEqual({
      titleKey: '旋转模式',
      messageKey: '拖动高亮圆形手柄旋转；按住 Ctrl 可关闭 15° 吸附',
      cursorClass: 'tool-cursor-crosshair',
    })
  })

  it('falls back safely to select guidance for an unknown runtime value', () => {
    expect(getEditorToolFeedback('unexpected' as never, false))
      .toEqual(getEditorToolFeedback('select', false))
  })
})
```

- [ ] **Step 2: Run the focused test and verify RED**

Run:

```powershell
Set-Location cp6.web
npm test -- src/views/space/editor/toolFeedback.spec.ts
```

Expected: FAIL because `./toolFeedback` does not exist.

- [ ] **Step 3: Implement the minimal pure mapping**

```ts
import type { ToolType } from '@/space-editor/interact/InteractionManager'

export interface EditorToolFeedback {
  titleKey: string
  messageKey: string
  cursorClass: 'tool-cursor-select' | 'tool-cursor-drag' | 'tool-cursor-crosshair'
}

const SELECT_FEEDBACK: EditorToolFeedback = {
  titleKey: '选择模式',
  messageKey: '单击选择货架；拖动空白区域可框选',
  cursorClass: 'tool-cursor-select',
}

export function getEditorToolFeedback(
  tool: ToolType,
  hasSelectedRack: boolean,
): EditorToolFeedback {
  switch (tool) {
    case 'drag':
      return {
        titleKey: '拖拽模式',
        messageKey: '拖动画布可平移视角；拖动货架可移动货架',
        cursorClass: 'tool-cursor-drag',
      }
    case 'rotate':
      return {
        titleKey: '旋转模式',
        messageKey: hasSelectedRack
          ? '拖动高亮圆形手柄旋转；按住 Ctrl 可关闭 15° 吸附'
          : '先单击一个货架，再拖动高亮圆形手柄',
        cursorClass: 'tool-cursor-crosshair',
      }
    case 'marker':
      return {
        titleKey: '打点模式',
        messageKey: '单击画布添加标注点，可使用撤销取消',
        cursorClass: 'tool-cursor-crosshair',
      }
    case 'zone':
      return {
        titleKey: '新建库区',
        messageKey: '在画布上拖出矩形范围，然后填写库区信息',
        cursorClass: 'tool-cursor-crosshair',
      }
    case 'select':
    default:
      return SELECT_FEEDBACK
  }
}
```

- [ ] **Step 4: Run the focused test and verify GREEN**

Run the same command as Step 2.

Expected: 1 file PASS; all six assertions/cases pass with no warnings.

- [ ] **Step 5: Commit the model and tests**

```powershell
git add -- cp6.web/src/views/space/editor/toolFeedback.ts cp6.web/src/views/space/editor/toolFeedback.spec.ts
git diff --cached --check
git commit -m "feat(space): define editor tool guidance"
```

### Task 2: Persistent Hint, Button Semantics, and Action Feedback

**Files:**
- Create: `cp6.web/src/views/space/editor/FloorEditor.feedback.spec.ts`
- Modify: `cp6.web/src/views/space/editor/FloorEditor.vue`

- [ ] **Step 1: Write failing component tests with isolated stage/API dependencies**

Create `FloorEditor.feedback.spec.ts` with the following complete harness and cases:

```ts
// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import ElementPlus, { ElMessage } from 'element-plus'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'
import { sceneApi } from '@/api/space/scene'
import { useSpaceEditorStore } from '@/stores/spaceEditor'
import FloorEditor from './FloorEditor.vue'
import type { EditorScene, RackVO } from '@/types/space/scene'

const { switchTool, setZoneRectHandler } = vi.hoisted(() => ({
  switchTool: vi.fn(),
  setZoneRectHandler: vi.fn(),
}))

vi.mock('vue-router', () => ({
  useRoute: () => ({ params: { floorId: 'floor-1' } }),
  useRouter: () => ({ push: vi.fn() }),
}))

vi.mock('@/api/space/scene', () => ({
  sceneApi: {
    get: vi.fn(),
    exportScene: vi.fn(),
    importScene: vi.fn(),
  },
}))

vi.mock('@/space-editor/SceneStage', () => ({
  SceneStage: class {
    stage = {
      on: vi.fn(), off: vi.fn(), getPointerPosition: vi.fn(() => null),
    }
    render = vi.fn()
    destroy = vi.fn()
    applyRackStyles = vi.fn()
    hideGhost = vi.fn()
  },
}))

vi.mock('@/space-editor/interact/InteractionManager', () => ({
  InteractionManager: class {
    switchTool = switchTool
    setZoneRectHandler = setZoneRectHandler
    destroy = vi.fn()
    refreshTransformer = vi.fn()
    setEnabled = vi.fn()
    setCtrlHeld = vi.fn()
  },
}))

const rack: RackVO = {
  id: 'rack-1', zoneId: 'zone-1', floorId: 'floor-1', rackCode: 'R-001',
  x: 0, y: 0, z: 0, rotationZ: 0,
  cols: 2, levels: 2, depthCount: 1, cellW: 1000, cellH: 1000, cellD: 1000,
}

const scene: EditorScene = {
  source: {
    kind: 'Real', dataSourceId: 'LOCAL_CP6DB', observedAtUtc: '2026-08-30T00:00:00Z',
    isSimulated: false, isAvailable: true,
  },
  floor: { id: 'floor-1', siteId: 'site-1' } as EditorScene['floor'],
  zones: [], aisles: [], racks: [rack], locations: [], markers: [],
}

const i18n = createI18n({
  legacy: false,
  locale: 'zh-CN',
  missingWarn: false,
  fallbackWarn: false,
  messages: {},
})

function mountEditor() {
  return mount(FloorEditor, {
    global: {
      plugins: [i18n, ElementPlus],
      stubs: {
        TemplatePanel: true,
        ConnectorPanel: true,
        PropertiesPanel: true,
        BindCodesDialog: true,
      },
    },
  })
}

describe('FloorEditor tool feedback', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    setActivePinia(createPinia())
    vi.mocked(sceneApi.get).mockResolvedValue({ data: structuredClone(scene) } as never)
    vi.mocked(sceneApi.exportScene).mockResolvedValue({ data: { floorId: 'floor-1' } } as never)
    vi.stubGlobal('URL', {
      ...URL,
      createObjectURL: vi.fn(() => 'blob:scene'),
      revokeObjectURL: vi.fn(),
    })
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})
  })

  it('keeps current tool guidance visible and updates rotation prerequisites', async () => {
    const wrapper = mountEditor()
    await flushPromises()

    const rotate = wrapper.get('[data-tool="rotate"]')
    await rotate.trigger('click')
    expect(switchTool).toHaveBeenCalledWith('rotate')
    expect(rotate.attributes('aria-pressed')).toBe('true')
    expect(wrapper.get('[data-test="tool-hint"]').text())
      .toContain('先单击一个货架，再拖动高亮圆形手柄')
    expect(wrapper.get('[data-test="editor-canvas"]').classes())
      .toContain('tool-cursor-crosshair')

    useSpaceEditorStore().setSelection(['rack-1'])
    await nextTick()
    expect(wrapper.get('[data-test="tool-hint"]').text())
      .toContain('按住 Ctrl 可关闭 15° 吸附')
  })

  it('keeps reverse modeling clickable and explains its prerequisite', async () => {
    const warning = vi.spyOn(ElMessage, 'warning').mockImplementation(() => undefined as never)
    const wrapper = mountEditor()
    await flushPromises()

    const reverse = wrapper.get('[data-test="reverse-model"]')
    expect(reverse.attributes('disabled')).toBeUndefined()
    await reverse.trigger('click')
    expect(warning).toHaveBeenCalledWith('请先在画布上选中一个货架')
  })

  it('reports a successful scene export after creating the download', async () => {
    const success = vi.spyOn(ElMessage, 'success').mockImplementation(() => undefined as never)
    const wrapper = mountEditor()
    await flushPromises()

    await wrapper.get('[data-test="export-scene"]').trigger('click')
    await flushPromises()

    expect(sceneApi.exportScene).toHaveBeenCalledWith('floor-1')
    expect(HTMLAnchorElement.prototype.click).toHaveBeenCalled()
    expect(success).toHaveBeenCalledWith('导出成功')
  })
})
```

- [ ] **Step 2: Run the component test and verify RED**

Run:

```powershell
Set-Location cp6.web
npm test -- src/views/space/editor/FloorEditor.feedback.spec.ts
```

Expected: FAIL because the toolbar lacks `data-tool`, the hint and canvas test IDs do not exist, reverse modeling is disabled, and export has no success message.

- [ ] **Step 3: Add the computed feedback and action result**

In `FloorEditor.vue`:

```ts
import { getEditorToolFeedback } from './toolFeedback'

const toolFeedback = computed(() => getEditorToolFeedback(
  activeTool.value,
  selectedRack.value !== null,
))
```

Append the success message immediately after the anchor download is triggered and cleaned up:

```ts
document.body.removeChild(a)
URL.revokeObjectURL(url)
ElMessage.success(t('导出成功'))
```

- [ ] **Step 4: Render persistent guidance and accessible pressed state**

For each of the five tool buttons, add the matching `data-tool` and pressed state. Example for rotate:

```vue
<el-button
  data-tool="rotate"
  :type="activeTool === 'rotate' ? 'primary' : 'default'"
  :aria-pressed="activeTool === 'rotate'"
  :title="t('旋转 (R)')"
  @click="switchTool('rotate')"
>
  {{ t('旋转') }}
</el-button>
```

Use `select`, `drag`, `marker`, and `zone` as the other `data-tool` values and bind their respective `aria-pressed` expressions.

Replace the direct canvas/aside pair with a canvas shell and persistent status:

```vue
<div class="canvas-shell">
  <div
    ref="canvasRef"
    data-test="editor-canvas"
    :class="[
      'canvas-container',
      toolFeedback.cursorClass,
      { 'placement-mode': placementMode || connectorPlacementMode },
    ]"
  />
  <div data-test="tool-hint" class="tool-hint" role="status" aria-live="polite">
    <strong>{{ t(toolFeedback.titleKey) }}</strong>
    <span>{{ t(toolFeedback.messageKey) }}</span>
  </div>
</div>
```

Keep the side panel as the next sibling. Add these styles:

```css
.canvas-shell {
  position: relative;
  flex: 1;
  min-width: 0;
  overflow: hidden;
}
.canvas-container {
  width: 100%;
  height: 100%;
  overflow: hidden;
  background: #eaeaea;
}
.tool-cursor-select { cursor: default; }
.tool-cursor-drag { cursor: grab; }
.tool-cursor-drag:active { cursor: grabbing; }
.tool-cursor-crosshair { cursor: crosshair; }
.tool-hint {
  position: absolute;
  top: 12px;
  left: 12px;
  z-index: 5;
  display: flex;
  align-items: center;
  gap: 10px;
  max-width: min(680px, calc(100% - 24px));
  padding: 8px 12px;
  border: 1px solid rgba(11, 112, 120, 0.35);
  border-radius: 8px;
  background: rgba(9, 39, 45, 0.88);
  color: #fff;
  font-size: 12px;
  box-shadow: 0 5px 15px rgba(0, 0, 0, 0.14);
  pointer-events: none;
}
.tool-hint strong { white-space: nowrap; }
.tool-hint span { line-height: 1.35; }
```

- [ ] **Step 5: Make reverse modeling explain the missing selection**

Change the button to:

```vue
<el-button
  data-test="reverse-model"
  size="small"
  :aria-disabled="selectedRack === null"
  :title="selectedRack ? t('为所选货架绑定采纳态库位码') : t('请先选中一个货架')"
  @click="openBindDialog"
>
  {{ t('反向建模') }}
</el-button>
```

Add `data-test="export-scene"` to the existing export button. Do not change `openBindDialog`; its existing warning becomes reachable once the template no longer disables the button.

- [ ] **Step 6: Run the component and feedback-model tests and verify GREEN**

Run:

```powershell
npm test -- src/views/space/editor/toolFeedback.spec.ts src/views/space/editor/FloorEditor.feedback.spec.ts
```

Expected: both files PASS with no console errors or warnings.

- [ ] **Step 7: Commit the page integration**

```powershell
git add -- cp6.web/src/views/space/editor/FloorEditor.vue cp6.web/src/views/space/editor/FloorEditor.feedback.spec.ts
git diff --cached --check
git commit -m "fix(space): expose editor tool feedback"
```

### Task 3: High-Visibility Rotation Handle

**Files:**
- Create: `cp6.web/src/space-editor/interact/rotate/rotateHandleStyle.spec.ts`
- Create: `cp6.web/src/space-editor/interact/rotate/rotateHandleStyle.ts`
- Modify: `cp6.web/src/space-editor/interact/tools/RotateTool.ts`

- [ ] **Step 1: Write the failing style application test**

```ts
import { describe, expect, it, vi } from 'vitest'
import {
  ACTIVE_ROTATE_HANDLE_STYLE,
  INACTIVE_ROTATE_HANDLE_STYLE,
  setRotateHandleVisibility,
} from './rotateHandleStyle'

describe('setRotateHandleVisibility', () => {
  it('applies an 18px round, high-contrast active handle', () => {
    const transformer = { setAttrs: vi.fn() }
    setRotateHandleVisibility(transformer as never, true)

    expect(ACTIVE_ROTATE_HANDLE_STYLE.anchorSize).toBeGreaterThanOrEqual(18)
    expect(transformer.setAttrs).toHaveBeenCalledWith(ACTIVE_ROTATE_HANDLE_STYLE)
  })

  it('restores the neutral transformer style when rotation exits', () => {
    const transformer = { setAttrs: vi.fn() }
    setRotateHandleVisibility(transformer as never, false)
    expect(transformer.setAttrs).toHaveBeenCalledWith(INACTIVE_ROTATE_HANDLE_STYLE)
  })
})
```

- [ ] **Step 2: Run the focused test and verify RED**

Run:

```powershell
Set-Location cp6.web
npm test -- src/space-editor/interact/rotate/rotateHandleStyle.spec.ts
```

Expected: FAIL because `./rotateHandleStyle` does not exist.

- [ ] **Step 3: Implement the active and inactive styles**

```ts
import type Konva from 'konva'

export const ACTIVE_ROTATE_HANDLE_STYLE = Object.freeze({
  anchorSize: 18,
  anchorCornerRadius: 99,
  anchorFill: '#10bfc8',
  anchorStroke: '#ffffff',
  anchorStrokeWidth: 3,
  anchorShadowColor: '#075f65',
  anchorShadowBlur: 3,
  anchorShadowOpacity: 0.9,
  borderStroke: '#087d84',
  borderStrokeWidth: 2,
  rotateAnchorOffset: 42,
})

export const INACTIVE_ROTATE_HANDLE_STYLE = Object.freeze({
  anchorSize: 10,
  anchorCornerRadius: 0,
  anchorFill: '#ffffff',
  anchorStroke: '#0099ff',
  anchorStrokeWidth: 1,
  anchorShadowColor: 'transparent',
  anchorShadowBlur: 0,
  anchorShadowOpacity: 0,
  borderStroke: '#0099ff',
  borderStrokeWidth: 1.5,
  rotateAnchorOffset: 50,
})

export function setRotateHandleVisibility(
  transformer: Konva.Transformer,
  active: boolean,
): void {
  transformer.setAttrs(active ? ACTIVE_ROTATE_HANDLE_STYLE : INACTIVE_ROTATE_HANDLE_STYLE)
}
```

- [ ] **Step 4: Apply and restore the style in RotateTool**

Add:

```ts
import { setRotateHandleVisibility } from '../rotate/rotateHandleStyle'
```

At the start of `onActivate()`:

```ts
setRotateHandleVisibility(this.ctx.transformer, true)
```

In `onDeactivate()`, after disabling rotation and before clearing nodes:

```ts
setRotateHandleVisibility(this.ctx.transformer, false)
```

- [ ] **Step 5: Run focused rotation tests and verify GREEN**

Run:

```powershell
npm test -- src/space-editor/interact/rotate/rotateHandleStyle.spec.ts src/space-editor/interact/rotate/rotateGeometry.spec.ts
```

Expected: both files PASS; the existing geometry/snap behavior remains unchanged.

- [ ] **Step 6: Commit the rotation presentation**

```powershell
git add -- cp6.web/src/space-editor/interact/rotate/rotateHandleStyle.ts cp6.web/src/space-editor/interact/rotate/rotateHandleStyle.spec.ts cp6.web/src/space-editor/interact/tools/RotateTool.ts
git diff --cached --check
git commit -m "fix(space): clarify editor rotation handle"
```

### Task 4: Repository Gates and Composite Local Acceptance

**Files:**
- Verify: all task files above
- Integrate into: `D:\CP6\.claude\worktrees\space-local-acceptance-20260830`

- [ ] **Step 1: Run focused tests on the task branch**

```powershell
Set-Location cp6.web
npm test -- src/views/space/editor/toolFeedback.spec.ts src/views/space/editor/FloorEditor.feedback.spec.ts src/space-editor/interact/rotate/rotateHandleStyle.spec.ts src/space-editor/interact/rotate/rotateGeometry.spec.ts
```

Expected: all focused files PASS with zero failed tests.

- [ ] **Step 2: Run full frontend gates**

```powershell
npm test
npm run type-check
npm run build-only
```

Expected: full Vitest suite PASS, TypeScript/Vue type-check exits 0, production Vite build exits 0, and none emit a new error attributable to this task.

- [ ] **Step 3: Review the complete task diff**

```powershell
Set-Location ..
git status --short --branch
git diff origin/main...HEAD --check
git diff --stat origin/main...HEAD
git diff origin/main...HEAD -- cp6.web/src/views/space/editor cp6.web/src/space-editor/interact
```

Expected: only the approved design/plan, focused frontend code, and tests appear; no machine-specific configuration, credentials, generated artifacts, or unrelated fixes appear.

- [ ] **Step 4: Integrate task commits into the composite acceptance branch**

In `D:\CP6\.claude\worktrees\space-local-acceptance-20260830`, first verify the worktree is clean. Cherry-pick only the implementation commits from Tasks 1–3 in order; do not cherry-pick by an unreviewed broad range.

```powershell
git status --short --branch
$guidanceCommit = git -C 'D:\CP6\.claude\worktrees\space-editor-tool-feedback-20260830' log -1 --format=%H --grep='^feat(space): define editor tool guidance$'
$feedbackCommit = git -C 'D:\CP6\.claude\worktrees\space-editor-tool-feedback-20260830' log -1 --format=%H --grep='^fix(space): expose editor tool feedback$'
$rotationCommit = git -C 'D:\CP6\.claude\worktrees\space-editor-tool-feedback-20260830' log -1 --format=%H --grep='^fix(space): clarify editor rotation handle$'
git cherry-pick $guidanceCommit $feedbackCommit $rotationCommit
```

Before cherry-picking, print the three variables and require each to contain exactly one 40-character commit hash. Expected: clean cherry-picks and Vite on `18080` hot-reloads the composite acceptance frontend.

- [ ] **Step 5: Run the real local Chrome acceptance without saving test edits**

Open:

```text
http://127.0.0.1:18080/space/editor/e0b4fcfd-80ee-4c82-95cd-350519b902f9
```

Verify in this order:

1. Click each of 选择、拖拽、旋转、打点、新建库区; each button becomes active and the persistent hint updates.
2. In 拖拽 mode, pan the empty canvas and move one rack; use 撤销 to restore the rack before any save.
3. In 旋转 mode, click one rack, confirm the hint changes, drag the enlarged circular handle, and use 撤销 to restore it.
4. In 打点 mode, add one marker; confirm 撤销/重做 enables, then undo so no marker remains.
5. In 新建库区 mode, drag a rectangle; confirm the dialog opens, then cancel it.
6. With no selected rack, click 反向建模 and confirm the prerequisite message. Select one rack and confirm the existing dialog can open, then close it.
7. Click 导入 and cancel the system chooser. Click 导出 and confirm both the JSON download and “导出成功” message.
8. Do not click 保存 after temporary edits. Confirm the browser console has no new errors.

Expected: every action has visible feedback; all temporary scene changes are undone or canceled; CP6DB remains unchanged.

- [ ] **Step 6: Report the acceptance checkpoint to the user**

Provide the exact local URL, the passed gate summary, and the statement that no Docker operation or database write occurred. Ask the user to perform their own acceptance before `main` integration.

### Task 5: User-Accepted Project Memory and Main Integration

**Files:**
- Modify: `docs/project-memory/PROJECT_STATE.md`
- Modify: `docs/project-memory/05-Completed.md`
- Modify: `docs/project-memory/06-Todo.md`
- Modify: `docs/project-memory/CHANGELOG-AI.md`

This task begins only after the user explicitly accepts the behavior on `18080`.

- [ ] **Step 1: Add the verified state entry**

Prepend this section after the title in `PROJECT_STATE.md`:

```markdown
## Space Editor 工具反馈增强完成（2026-08-30）

- 空间编辑器五类工具均提供持续的当前模式/下一步提示和匹配光标；旋转工具使用高对比度大手柄，并保留既有单货架、15° 吸附和命令栈语义。
- 未选货架时“反向建模”可点击并解释前置条件；导出成功提供页面内反馈，保存、导入和失败路径保持既有行为。
- 聚焦测试、Web 全量测试、类型检查、生产构建和本地 Chrome/CP6DB 验收通过；没有数据库/API/DTO 变更，没有 Docker 操作或生产部署。
```

- [ ] **Step 2: Add the completed capability entry**

Prepend this section after the title in `05-Completed.md`:

```markdown
## 2026-08-30 Space Editor 工具反馈增强

- 关闭“除拖拽外其他按钮无反应”的可发现性问题：工具切换、持续提示、专用光标、旋转手柄、反向建模前置条件和导出结果均有明确反馈。
- 既有选择、拖拽、旋转、打点、新建库区、撤销/重做和保存语义保持不变；本地真实场景浏览器验收中临时编辑全部撤销或取消。
```

- [ ] **Step 3: Record the remaining redesign boundary**

Under `## P1：Space Studio V1 GA 后边界与运营增强` in `06-Todo.md`, add:

```markdown
- Space Editor 工具反馈增强已关闭；“选择后直接移动/旋转”的统一直接操作模式不属于本次缺陷修复。只有收到新的产品需求并重新评估框选/移动手势冲突后，才另立设计任务。
```

- [ ] **Step 4: Add the AI-readable change entry**

Prepend this section after the introductory quote in `CHANGELOG-AI.md`:

```markdown
## 2026-08-30：Space Editor 工具反馈增强

- Added persistent per-tool guidance, accessible pressed state and mode-specific cursors without changing editor command or persistence semantics.
- Enlarged and high-contrast styled the Konva rotation handle while preserving single-rack rotation, 15° snapping and undo/redo.
- Made reverse-model prerequisites reachable and added export success feedback; focused/full Web gates, type-check, build and local Chrome acceptance passed without Docker or database mutation.
```

- [ ] **Step 5: Validate and commit project memory**

```powershell
git diff --check
git add -- docs/project-memory/PROJECT_STATE.md docs/project-memory/05-Completed.md docs/project-memory/06-Todo.md docs/project-memory/CHANGELOG-AI.md
git diff --cached --check
git commit -m "docs(space): record editor tool feedback"
```

- [ ] **Step 6: Merge only after all required gates remain green**

Review `origin/main...HEAD`, merge the verified task branch into the main integration worktree using a non-destructive merge, rerun the focused tests plus type-check, and push only after confirming remote `main` contains the merge. Do not force-push, rewrite history, delete branches, or deploy production.

Expected: `main` contains the approved feature and project-memory commits; the composite local acceptance branch remains an auditable convenience branch rather than a second release authority.
