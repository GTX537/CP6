# 3D Space Free Viewer Navigation Implementation Plan

> 历史计划归档：本文件恢复自 2026-08-29 提交 `36d3140d`，下述操作步骤与勾选状态保留当时语境，不是本轮执行授权。2026-09-16 恢复交付的实际范围与验证结果见 [项目状态](../../project-memory/PROJECT_STATE.md) 中的“恢复 3D Viewer 自由相机”条目；本轮不执行原计划中的数据库、容器、部署或浏览器验收步骤。

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让 3D Space Viewer 支持左键旋转、右键平移、鼠标指针缩放、双击聚焦和手动立即接管相机。

**Architecture:** 继续以 `CameraController` 作为唯一三维相机控制边界，使用 Three.js `OrbitControls` 的原生 `zoomToCursor`、鼠标和触控映射，不增加逐滚轮场景射线检测。`FloorViewer` 只增加交互提示、辅助技术说明和右键菜单抑制；Runtime API、数据库和 Published 场景合同不改变。

**Tech Stack:** Vue 3、TypeScript、Three.js 0.185、OrbitControls、Vitest、Vite、Docker/Nginx 本地验收环境。

---

## File map

- Modify `cp6.web/src/space-viewer/navigate/CameraController.ts`: 自由视角控制配置和手动接管飞行动画。
- Modify `cp6.web/src/space-viewer/navigate/CameraController.spec.ts`: 真实控制合同的失败/通过测试。
- Modify `cp6.web/src/views/space/viewer/FloorViewer.vue`: 可见操作提示、ARIA 说明和上下文菜单处理。
- Create `cp6.web/src/views/space/viewer/FloorViewer.navigation.spec.ts`: Viewer 页面交互合同源级回归测试。
- Modify `docs/project-memory/PROJECT_STATE.md`: 当前本地自由视角状态和验证证据。
- Modify `docs/project-memory/05-Completed.md`: 已完成任务记录。
- Modify `docs/project-memory/06-Todo.md`: 合并边界和剩余正式验收项。
- Modify `docs/project-memory/CHANGELOG-AI.md`: AI 变更日志。

### Task 1: CameraController 自由视角合同

**Files:**
- Modify: `cp6.web/src/space-viewer/navigate/CameraController.spec.ts`
- Modify: `cp6.web/src/space-viewer/navigate/CameraController.ts`

- [ ] **Step 1: 在现有测试文件中加入可观察的 OrbitControls 测试替身**

把 Vitest 导入扩展为 `beforeEach` 和 `vi`，并在导入 `CameraController` 前加入以下 mock。测试替身只实现产品代码实际使用的属性和事件：

```ts
import { MOUSE, PerspectiveCamera, TOUCH, Vector3 } from 'three'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { WebGLRenderer } from 'three'

const orbitState = vi.hoisted(() => ({ instances: [] as unknown[] }))

vi.mock('three/examples/jsm/controls/OrbitControls', async () => {
  const { Vector3 } = await import('three')

  class FakeOrbitControls {
    target = new Vector3()
    enableDamping = false
    dampingFactor = 0
    enablePan = false
    screenSpacePanning = false
    zoomToCursor = false
    minDistance = 0
    maxDistance = Infinity
    maxPolarAngle = Math.PI
    mouseButtons = { LEFT: -1, MIDDLE: -1, RIGHT: -1 }
    touches = { ONE: -1, TWO: -1 }
    private listeners = new Map<string, Set<() => void>>()

    constructor() {
      orbitState.instances.push(this)
    }

    addEventListener(type: string, listener: () => void): void {
      const listeners = this.listeners.get(type) ?? new Set<() => void>()
      listeners.add(listener)
      this.listeners.set(type, listeners)
    }

    removeEventListener(type: string, listener: () => void): void {
      this.listeners.get(type)?.delete(listener)
    }

    emit(type: string): void {
      this.listeners.get(type)?.forEach((listener) => listener())
    }

    update(): void {}
    dispose(): void {}
  }

  return { OrbitControls: FakeOrbitControls }
})

import { CameraController, easeInOutCubic, easeLinear } from './CameraController'

type TestOrbitControls = {
  target: Vector3
  enablePan: boolean
  screenSpacePanning: boolean
  zoomToCursor: boolean
  mouseButtons: { LEFT: number; MIDDLE: number; RIGHT: number }
  touches: { ONE: number; TWO: number }
  emit(type: string): void
}

function createController() {
  const camera = new PerspectiveCamera(45, 4 / 3, 0.1, 2000)
  camera.position.set(0, 50, 80)
  const renderer = {
    domElement: { clientWidth: 800, clientHeight: 600, width: 800, height: 600 },
  } as unknown as WebGLRenderer
  const requestRender = vi.fn()
  const controller = new CameraController(camera, renderer, requestRender)
  const controls = orbitState.instances.at(-1) as TestOrbitControls
  return { camera, controller, controls, requestRender }
}

beforeEach(() => {
  orbitState.instances.length = 0
})
```

- [ ] **Step 2: 写入自由导航配置失败测试**

```ts
describe('CameraController free navigation', () => {
  it('maps desktop and touch input to rotate, pan, and cursor-directed zoom', () => {
    const { controls } = createController()

    expect(controls.enablePan).toBe(true)
    expect(controls.screenSpacePanning).toBe(true)
    expect(controls.zoomToCursor).toBe(true)
    expect(controls.mouseButtons).toEqual({
      LEFT: MOUSE.ROTATE,
      MIDDLE: MOUSE.DOLLY,
      RIGHT: MOUSE.PAN,
    })
    expect(controls.touches).toEqual({
      ONE: TOUCH.ROTATE,
      TWO: TOUCH.DOLLY_PAN,
    })
  })
})
```

- [ ] **Step 3: 运行测试并确认 RED**

Run:

```powershell
npm exec vitest run src/space-viewer/navigate/CameraController.spec.ts --pool=threads --maxWorkers=1
```

Expected: FAIL；`enablePan`、`screenSpacePanning` 或 `zoomToCursor` 至少一项不满足新合同，证明测试捕获当前固定中心行为。

- [ ] **Step 4: 写入手动接管失败测试**

在同一 `describe` 中加入：

```ts
it('cancels an active fly animation when manual navigation starts', () => {
  const { camera, controller, controls } = createController()
  const destination = new Vector3(60, 60, 60)
  const target = new Vector3(20, 0, 20)

  controller.flyTo(destination, target, 1000, easeLinear)
  controller.update(100)
  const manuallyOwnedPosition = camera.position.clone()
  const manuallyOwnedTarget = controls.target.clone()

  controls.emit('start')
  controller.update(500)

  expect(camera.position.toArray()).toEqual(manuallyOwnedPosition.toArray())
  expect(controls.target.toArray()).toEqual(manuallyOwnedTarget.toArray())
})
```

- [ ] **Step 5: 再次运行测试并确认 RED**

Run:

```powershell
npm exec vitest run src/space-viewer/navigate/CameraController.spec.ts --pool=threads --maxWorkers=1
```

Expected: 新测试 FAIL；`update(500)` 仍继续插值到飞行动画目标。

- [ ] **Step 6: 在 CameraController 写入最小实现**

把 Three.js 导入扩展为 `MOUSE` 和 `TOUCH`，在构造函数中完成明确配置：

```ts
import {
  Box3,
  MOUSE,
  PerspectiveCamera,
  OrthographicCamera,
  Sphere,
  TOUCH,
  Vector3,
} from 'three'
```

在类中加入稳定事件处理器：

```ts
private readonly _cancelFlyOnManualStart = (): void => {
  this._fly = null
}
```

在现有 `maxPolarAngle` 配置之后加入：

```ts
this._controls.enablePan = true
this._controls.screenSpacePanning = true
this._controls.zoomToCursor = true
this._controls.mouseButtons.LEFT = MOUSE.ROTATE
this._controls.mouseButtons.MIDDLE = MOUSE.DOLLY
this._controls.mouseButtons.RIGHT = MOUSE.PAN
this._controls.touches.ONE = TOUCH.ROTATE
this._controls.touches.TWO = TOUCH.DOLLY_PAN

this._controls.addEventListener('start', this._cancelFlyOnManualStart)
```

在 `dispose()` 中先解除自定义监听：

```ts
dispose(): void {
  this._controls.removeEventListener('start', this._cancelFlyOnManualStart)
  this._controls.dispose()
}
```

- [ ] **Step 7: 运行聚焦测试并确认 GREEN**

Run:

```powershell
npm exec vitest run src/space-viewer/navigate/CameraController.spec.ts --pool=threads --maxWorkers=1
```

Expected: 全部 CameraController 测试 PASS，输出无 error/warning。

- [ ] **Step 8: 提交相机控制纵切**

```powershell
git add -- cp6.web/src/space-viewer/navigate/CameraController.ts cp6.web/src/space-viewer/navigate/CameraController.spec.ts
git diff --cached --check
git commit -m "fix(space): enable free viewer navigation"
```

### Task 2: Viewer 操作提示和右键平移入口

**Files:**
- Create: `cp6.web/src/views/space/viewer/FloorViewer.navigation.spec.ts`
- Modify: `cp6.web/src/views/space/viewer/FloorViewer.vue`

- [ ] **Step 1: 写入页面合同失败测试**

```ts
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

const source = readFileSync(
  fileURLToPath(new URL('./FloorViewer.vue', import.meta.url)),
  'utf8',
)

describe('FloorViewer navigation affordance', () => {
  it('documents free navigation and reserves right drag for panning', () => {
    expect(source).toContain(
      'aria-describedby="viewer-navigation-help viewer-keyboard-help"',
    )
    expect(source).toContain('@contextmenu.prevent')
    expect(source).toContain('id="viewer-navigation-help"')
    expect(source).toContain('左键旋转 · 右键平移 · 滚轮指向缩放 · 双击聚焦')
  })
})
```

- [ ] **Step 2: 运行测试并确认 RED**

Run:

```powershell
npm exec vitest run src/views/space/viewer/FloorViewer.navigation.spec.ts --pool=threads --maxWorkers=1
```

Expected: FAIL；当前模板没有导航提示、双 ARIA 描述或上下文菜单抑制。

- [ ] **Step 3: 写入最小模板实现**

把 Canvas 的描述和事件扩展为：

```vue
aria-describedby="viewer-navigation-help viewer-keyboard-help"
@contextmenu.prevent
```

在 Canvas 后加入：

```vue
<p id="viewer-navigation-help" class="viewer-navigation-help">
  {{ t('左键旋转 · 右键平移 · 滚轮指向缩放 · 双击聚焦') }}
</p>
```

把屏幕阅读器说明改为：

```vue
{{ t('鼠标操作：左键旋转，右键平移，滚轮指向缩放，双击聚焦。键盘快捷键：1 俯视，2 等轴，3 正视，Home 复位，O 整层概览，F 聚焦选中，P 切换投影。') }}
```

在 scoped style 中加入：

```css
.viewer-navigation-help {
  position: absolute;
  left: 50%;
  bottom: 16px;
  z-index: 9;
  transform: translateX(-50%);
  margin: 0;
  padding: 6px 10px;
  border: 1px solid rgba(79, 195, 247, 0.2);
  border-radius: 5px;
  color: #b3e5fc;
  background: rgba(10, 15, 29, 0.78);
  font-size: 11px;
  pointer-events: none;
  white-space: nowrap;
}
```

- [ ] **Step 4: 运行页面合同测试并确认 GREEN**

Run:

```powershell
npm exec vitest run src/views/space/viewer/FloorViewer.navigation.spec.ts --pool=threads --maxWorkers=1
```

Expected: 1 test PASS。

- [ ] **Step 5: 提交 Viewer 交互提示纵切**

```powershell
git add -- cp6.web/src/views/space/viewer/FloorViewer.vue cp6.web/src/views/space/viewer/FloorViewer.navigation.spec.ts
git diff --cached --check
git commit -m "feat(space): explain free viewer controls"
```

### Task 3: 前端回归门禁

**Files:**
- Verify only; no source changes expected.

- [ ] **Step 1: 运行两组聚焦测试**

```powershell
npm exec vitest run src/space-viewer/navigate/CameraController.spec.ts src/views/space/viewer/FloorViewer.navigation.spec.ts --pool=threads --maxWorkers=1
```

Expected: 两个测试文件全部 PASS。

- [ ] **Step 2: 运行 Space Viewer 测试集**

```powershell
npm exec vitest run src/space-viewer --pool=threads --maxWorkers=1
```

Expected: 全部 PASS，无新增 warning/error。

- [ ] **Step 3: 运行 Vue 类型检查**

```powershell
npm run type-check
```

Expected: exit code 0，无 TypeScript/Vue 错误。

- [ ] **Step 4: 运行生产构建**

```powershell
npm run build-only
```

Expected: exit code 0，生成 `cp6.web/dist`；仅允许既有 chunk-size 提示。

### Task 4: 本地组合部署和真实浏览器验收

**Files:**
- Local runtime only; no tracked source changes.

- [ ] **Step 1: 从上一轮 CP6DB 修复分支创建临时 detached 验收 worktree**

```powershell
git worktree add --detach D:\CP6-worktrees\accept-space-viewer-free-camera codex/fix-space-validation-warehouse-context-20260829
```

Expected: 临时 worktree 指向上一轮已验证的 DB/发布/URL 修复，不修改其任务分支。

- [ ] **Step 2: 只把本任务两个产品提交合入临时 worktree**

```powershell
$cameraCommit = git log --format=%H --grep '^fix(space): enable free viewer navigation$' -1
$viewerHintCommit = git log --format=%H --grep '^feat(space): explain free viewer controls$' -1
if (-not $cameraCommit -or -not $viewerHintCommit) { throw 'Required product commits are missing.' }
git -C D:\CP6-worktrees\accept-space-viewer-free-camera cherry-pick $cameraCommit $viewerHintCommit
```

Expected: cherry-pick clean；临时 detached HEAD 同时包含上一轮 Viewer 数据修复和本轮自由视角。

- [ ] **Step 3: 安装依赖并构建组合前端**

```powershell
npm ci
npm run build-only
```

Working directory: `D:\CP6-worktrees\accept-space-viewer-free-camera\cp6.web`

Expected: production build PASS。

- [ ] **Step 4: 热更新本地 Web 容器**

```powershell
docker cp D:\CP6-worktrees\accept-space-viewer-free-camera\cp6.web\dist\. cp6-business-web-22b3a9ce:/usr/share/nginx/html/
```

Expected: `http://127.0.0.1:18080/login` 返回 200，新 `index-*.js` 被 Nginx 提供。

- [ ] **Step 5: 用浏览器执行交互验收**

打开：

```text
http://127.0.0.1:18080/space/viewer/ff70bb90-996b-4c43-b633-05b80a893507?v=free-camera
```

逐项验证：

- 左上货架指针缩放后保持在鼠标附近并明显放大。
- 右键拖动改变观察中心，随后左键围绕新中心旋转。
- 双击可拾取库位后平滑聚焦。
- 预设飞行中拖动可立即接管且不回弹。
- Home 恢复默认；F1/F2 切换后仍可自由操作。
- 操作提示可见；控制台无新增错误；场景和 Runtime 请求均为成功响应。

- [ ] **Step 6: 保存最终截图并移除临时 worktree**

截图保存到：

```text
C:\Users\tt\AppData\Local\Temp\cp6-space-viewer-free-camera.png
```

确认临时 worktree 路径为 `D:\CP6-worktrees\accept-space-viewer-free-camera` 后执行：

```powershell
git worktree remove D:\CP6-worktrees\accept-space-viewer-free-camera
```

Expected: 本地 Web 容器继续提供已构建静态文件，临时源码 worktree 被安全清理。

### Task 5: 项目状态、审查和任务提交

**Files:**
- Modify: `docs/project-memory/PROJECT_STATE.md`
- Modify: `docs/project-memory/05-Completed.md`
- Modify: `docs/project-memory/06-Todo.md`
- Modify: `docs/project-memory/CHANGELOG-AI.md`

- [ ] **Step 1: 同步项目状态**

在四个项目记忆文件顶部追加 2026-08-29 记录，包含：

- 采用方案 A 和完整鼠标/触控合同。
- 自动化测试、类型检查、生产构建和 10,000 库位浏览器证据。
- 本任务仅完成本地验证，尚未推送、合并或部署生产。
- 与上一轮 CP6DB 修复的临时组合只用于本地验收，不改变两个独立分支的集成边界。

- [ ] **Step 2: 检查完整任务 diff**

```powershell
git diff --check origin/main...HEAD
git diff --stat origin/main...HEAD
git status --short --branch
```

Expected: 无 whitespace 错误、无机器专属配置、无临时构建文件、仅包含设计/计划、自由视角代码测试和项目状态文档。

- [ ] **Step 3: 提交项目状态文档**

```powershell
git add -- docs/project-memory/PROJECT_STATE.md docs/project-memory/05-Completed.md docs/project-memory/06-Todo.md docs/project-memory/CHANGELOG-AI.md
git diff --cached --check
git commit -m "docs(space): record free viewer navigation"
```

- [ ] **Step 4: 最终审查**

```powershell
git log --oneline --decorate origin/main..HEAD
git diff --check origin/main...HEAD
git status --short --branch
```

Expected: 任务分支干净、提交可审计；不执行 push、merge 或生产部署。
