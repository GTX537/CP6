# Space Editor Free Viewport Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the 2D Space Editor cursor-anchored zoom, all-mode panning, fit/reset controls, responsive resizing, and conflict-free navigation without changing scene data or CP6DB.

**Architecture:** Put all coordinate, bounds, fit, clamp, resize, and preview-transform math in a pure `viewport.ts` module. `SceneStage` owns canonical and preview viewport state and performs a two-phase layer transform followed by one committed redraw; a focused `ViewportController` owns wheel/pointer gesture lifecycles and is exposed through `InteractionManager` to the Vue toolbar.

**Tech Stack:** Vue 3 `<script setup>`, Pinia, Element Plus, TypeScript 6, Konva 10, Vitest 4, Vue Test Utils, ResizeObserver, Pointer Events, Docker Compose development environment, Chrome browser acceptance.

---

## File Map

- Create `cp6.web/src/space-editor/viewport.ts`: pure viewport state, scene bounds, fit, zoom, pan, resize, and temporary Konva-layer transform math.
- Create `cp6.web/src/space-editor/viewport.spec.ts`: unit tests for anchor invariance, relative zoom limits, rotated racks, malformed geometry, fit padding, resize, and finite fallbacks.
- Modify `cp6.web/src/space-editor/SceneStage.ts`: own canonical/preview/initial viewports, cache the current scene, preview all layers, commit one redraw, preserve Transformer helpers, and observe container resizing.
- Create `cp6.web/src/space-editor/SceneStage.viewport.spec.ts`: focused stage tests for two-phase preview/commit, scene retention, Transformer survival, fit/reset, resize, and cleanup.
- Create `cp6.web/src/space-editor/interact/ViewportController.ts`: unify wheel, `Space` + left-button, middle-button, and Drag-mode background panning with pointer capture and click suppression.
- Create `cp6.web/src/space-editor/interact/ViewportController.spec.ts`: gesture matrix, debounce, cancellation, target routing, and listener-cleanup tests.
- Modify `cp6.web/src/space-editor/interact/InteractionManager.ts`: create/destroy the viewport controller, expose keyboard/toolbar methods, and refresh Transformer after committed view changes.
- Create `cp6.web/src/space-editor/interact/InteractionManager.viewport.spec.ts`: integration tests that navigation suppresses editor tools and refreshes selection helpers without changing the active tool.
- Modify `cp6.web/src/views/space/editor/FloorEditor.vue`: render five viewport controls, synchronize status, forward Space lifecycle, and show navigation cursors.
- Modify `cp6.web/src/views/space/editor/FloorEditor.feedback.spec.ts`: extend the existing isolated Vue harness with toolbar, keyboard, accessible-name, percentage, and dirty-state coverage.
- Modify `docs/project-memory/PROJECT_STATE.md`: record the verified implementation and its frontend-only data boundary.
- Modify `docs/project-memory/05-Completed.md`: record the completed editor viewport capability.
- Modify `docs/project-memory/06-Todo.md`: close this bug while retaining the independent seven-layer performance follow-up.
- Modify `docs/project-memory/CHANGELOG-AI.md`: add the implementation and verification summary.

## Execution Preconditions

- Work only in `D:\CP6\.claude\worktrees\space-editor-free-viewport-20260831` on branch `codex/space-editor-free-viewport-20260831`.
- The branch starts from `origin/main@1f60d1c08c2ee6a1a32cedf035d05d83993712ed`; approved design commit is `fb749247`.
- Preserve the unrelated dirty root worktree `D:\CP6`; do not clean, stash, overwrite, or commit its changes.
- Follow strict RED → GREEN → REFACTOR order. Do not write production code for a task before its focused failing test has run.
- Do not change API, DTO, migration, database, release workflow, or production deployment files.
- Docker is used only after code review and merge verification. Never run `docker compose down -v`, `docker volume prune`, snapshot import, SQL merge, or any CP6DB data mutation command.

### Task 1: Pure Viewport Math and Scene Bounds

**Files:**
- Create: `cp6.web/src/space-editor/viewport.spec.ts`
- Create: `cp6.web/src/space-editor/viewport.ts`

- [ ] **Step 1: Write the failing pure-function tests**

Create `viewport.spec.ts` with these focused cases:

```ts
import { describe, expect, it } from 'vitest'
import type { EditorScene } from '@/types/space/scene'
import {
  clampRelativeZoom,
  collectSceneBounds,
  createDefaultViewport,
  fitBounds,
  panViewport,
  resizeViewport,
  toCoordinateView,
  viewportLayerTransform,
  zoomAround,
  zoomPercent,
} from './viewport'
import { screenToWorld } from './coords'

const scene = (over: Partial<EditorScene> = {}): EditorScene => ({
  source: {
    kind: 'Real', dataSourceId: 'TEST', observedAtUtc: '2026-08-31T00:00:00Z',
    isSimulated: false, isAvailable: true,
  },
  floor: {
    id: 'floor-1', siteId: 'site-1', level: 1, floorCode: 'F1', floorName: 'Floor 1',
    height: 6000, underlayOffsetX: 0, underlayOffsetY: 0, originX: 0, originY: 0,
  },
  zones: [], aisles: [], racks: [], locations: [], markers: [],
  ...over,
})

describe('viewport math', () => {
  it('keeps the world point under the cursor fixed while zooming', () => {
    const before = { panX: 100, panY: 200, zoom: 0.05, canvasWidth: 1200, canvasHeight: 800 }
    const anchor = { x: 420, y: 360 }
    const worldBefore = screenToWorld(anchor, toCoordinateView(before))

    const after = zoomAround(before, 0.1, anchor, 0.05)
    const worldAfter = screenToWorld(anchor, toCoordinateView(after))

    expect(worldAfter.x).toBeCloseTo(worldBefore.x)
    expect(worldAfter.y).toBeCloseTo(worldBefore.y)
  })

  it('clamps relative zoom to 10%-800%', () => {
    expect(clampRelativeZoom(0.001, 0.05)).toBe(0.005)
    expect(clampRelativeZoom(1, 0.05)).toBe(0.4)
    expect(zoomPercent({ ...createDefaultViewport(800, 600), zoom: 0.2 }, 0.05)).toBe(400)
  })

  it('pans in screen pixels with the editor Y-axis convention', () => {
    const view = { panX: 0, panY: 0, zoom: 0.05, canvasWidth: 800, canvasHeight: 600 }
    expect(panViewport(view, 50, 25)).toMatchObject({ panX: -1000, panY: 500 })
  })

  it('includes versioned polygons, markers, and a rotated rack in scene bounds', () => {
    const bounds = collectSceneBounds(scene({
      zones: [{
        id: 'z', floorId: 'floor-1', zoneCode: 'Z', zoneName: 'Zone', zoneType: 1,
        polygon: JSON.stringify({ schemaVersion: 1, points: [[0, 0], [2000, 0], [2000, 1000]] }),
      }],
      racks: [{
        id: 'r', zoneId: 'z', floorId: 'floor-1', rackCode: 'R',
        x: 5000, y: 5000, z: 0, rotationZ: 90,
        cols: 2, levels: 1, depthCount: 1, cellW: 1000, cellH: 1000, cellD: 500,
      }],
      markers: [{ id: 'm', floorId: 'floor-1', x: -500, y: 9000, z: 0, markerType: 1, text: 'M' }],
    }))

    expect(bounds).toEqual({ minX: -500, minY: 0, maxX: 5000, maxY: 9000 })
  })

  it('fits all bounds inside a 48px safe margin', () => {
    const view = fitBounds({ minX: 0, minY: 0, maxX: 10000, maxY: 5000 }, 1000, 600, 48)
    const topLeft = {
      x: (0 - view.panX) * view.zoom,
      y: view.canvasHeight - (5000 - view.panY) * view.zoom,
    }
    const bottomRight = {
      x: (10000 - view.panX) * view.zoom,
      y: view.canvasHeight - (0 - view.panY) * view.zoom,
    }
    expect(topLeft.x).toBeGreaterThanOrEqual(48)
    expect(topLeft.y).toBeGreaterThanOrEqual(48)
    expect(bottomRight.x).toBeLessThanOrEqual(952)
    expect(bottomRight.y).toBeLessThanOrEqual(552)
  })

  it('ignores malformed geometry and returns a finite default for an empty scene', () => {
    expect(collectSceneBounds(scene({
      zones: [{
        id: 'bad', floorId: 'floor-1', zoneCode: 'BAD', zoneName: 'Bad', zoneType: 1,
        polygon: '{',
      }],
    }))).toBeNull()
    expect(Object.values(createDefaultViewport(0, 0)).every(Number.isFinite)).toBe(true)
  })

  it('preserves the world point at canvas center across resize', () => {
    const before = { panX: 100, panY: 200, zoom: 0.05, canvasWidth: 800, canvasHeight: 600 }
    const world = screenToWorld({ x: 400, y: 300 }, toCoordinateView(before))
    const after = resizeViewport(before, 1200, 900)
    expect(screenToWorld({ x: 600, y: 450 }, toCoordinateView(after))).toEqual(world)
  })

  it('derives one temporary affine transform from canonical to preview view', () => {
    const from = { panX: 0, panY: 0, zoom: 1, canvasWidth: 800, canvasHeight: 600 }
    const to = { panX: -20, panY: 10, zoom: 2, canvasWidth: 800, canvasHeight: 600 }
    expect(viewportLayerTransform(from, to)).toEqual({ scale: 2, x: 40, y: -580 })
  })
})
```

- [ ] **Step 2: Run the focused test and verify RED**

Run:

```powershell
Set-Location cp6.web
npm test -- src/space-editor/viewport.spec.ts
```

Expected: FAIL because `./viewport` does not exist.

- [ ] **Step 3: Implement the pure viewport module**

Create `viewport.ts` with this public contract and implementation:

```ts
import type { EditorScene } from '@/types/space/scene'
import { parseEditorPolygon } from './polygon'
import type { ViewState, XY } from './coords'

export interface ViewportState {
  panX: number
  panY: number
  zoom: number
  canvasWidth: number
  canvasHeight: number
}

export interface WorldBounds {
  minX: number
  minY: number
  maxX: number
  maxY: number
}

export interface LayerTransform {
  scale: number
  x: number
  y: number
}

export const VIEWPORT_PADDING_PX = 48
export const MIN_RELATIVE_ZOOM = 0.1
export const MAX_RELATIVE_ZOOM = 8
export const DEFAULT_ZOOM = 0.05

const positiveSize = (value: number): number => Number.isFinite(value) && value > 0 ? value : 1
const finite = (value: number, fallback: number): number => Number.isFinite(value) ? value : fallback

export function createDefaultViewport(width: number, height: number): ViewportState {
  const canvasWidth = positiveSize(width)
  const canvasHeight = positiveSize(height)
  return {
    panX: -canvasWidth / (2 * DEFAULT_ZOOM),
    panY: -canvasHeight / (2 * DEFAULT_ZOOM),
    zoom: DEFAULT_ZOOM,
    canvasWidth,
    canvasHeight,
  }
}

export function toCoordinateView(view: ViewportState): ViewState {
  return { panX: view.panX, panY: view.panY, zoom: view.zoom, height: view.canvasHeight }
}

export function clampRelativeZoom(target: number, initialZoom: number): number {
  const base = Number.isFinite(initialZoom) && initialZoom > 0 ? initialZoom : DEFAULT_ZOOM
  return Math.min(base * MAX_RELATIVE_ZOOM, Math.max(base * MIN_RELATIVE_ZOOM, finite(target, base)))
}

export function zoomPercent(view: ViewportState, initialZoom: number): number {
  const base = Number.isFinite(initialZoom) && initialZoom > 0 ? initialZoom : DEFAULT_ZOOM
  return Math.round((view.zoom / base) * 100)
}

export function zoomAround(
  view: ViewportState,
  targetZoom: number,
  anchor: XY,
  initialZoom: number,
): ViewportState {
  const base = Number.isFinite(initialZoom) && initialZoom > 0 ? initialZoom : DEFAULT_ZOOM
  const currentZoom = Number.isFinite(view.zoom) && view.zoom > 0 ? view.zoom : base
  const zoom = clampRelativeZoom(targetZoom, base)
  const point = Number.isFinite(anchor.x) && Number.isFinite(anchor.y)
    ? anchor
    : { x: view.canvasWidth / 2, y: view.canvasHeight / 2 }
  const worldX = point.x / currentZoom + finite(view.panX, 0)
  const worldY = (view.canvasHeight - point.y) / currentZoom + finite(view.panY, 0)
  return {
    ...view,
    zoom,
    panX: worldX - point.x / zoom,
    panY: worldY - (view.canvasHeight - point.y) / zoom,
  }
}

export function panViewport(view: ViewportState, dx: number, dy: number): ViewportState {
  if (!Number.isFinite(dx) || !Number.isFinite(dy)) return { ...view }
  const zoom = Number.isFinite(view.zoom) && view.zoom > 0 ? view.zoom : DEFAULT_ZOOM
  return { ...view, zoom, panX: finite(view.panX, 0) - dx / zoom, panY: finite(view.panY, 0) + dy / zoom }
}

export function resizeViewport(view: ViewportState, width: number, height: number): ViewportState {
  const canvasWidth = positiveSize(width)
  const canvasHeight = positiveSize(height)
  const zoom = Number.isFinite(view.zoom) && view.zoom > 0 ? view.zoom : DEFAULT_ZOOM
  const centerWorldX = positiveSize(view.canvasWidth) / (2 * zoom) + finite(view.panX, 0)
  const centerWorldY = positiveSize(view.canvasHeight) / (2 * zoom) + finite(view.panY, 0)
  return {
    ...view,
    zoom,
    canvasWidth,
    canvasHeight,
    panX: centerWorldX - canvasWidth / (2 * view.zoom),
    panY: centerWorldY - canvasHeight / (2 * view.zoom),
  }
}

export function viewportLayerTransform(from: ViewportState, to: ViewportState): LayerTransform {
  if (![from.panX, from.panY, from.zoom, from.canvasHeight, to.panX, to.panY, to.zoom, to.canvasHeight].every(Number.isFinite)
    || from.zoom <= 0 || to.zoom <= 0) return { scale: 1, x: 0, y: 0 }
  const scale = to.zoom / from.zoom
  return {
    scale,
    x: (from.panX - to.panX) * to.zoom,
    y: to.canvasHeight - scale * from.canvasHeight + (to.panY - from.panY) * to.zoom,
  }
}

function addPoint(xs: number[], ys: number[], x: number, y: number): void {
  if (!Number.isFinite(x) || !Number.isFinite(y)) return
  xs.push(x)
  ys.push(y)
}

export function collectSceneBounds(scene: EditorScene): WorldBounds | null {
  const xs: number[] = []
  const ys: number[] = []
  for (const item of [...scene.zones, ...scene.aisles]) {
    for (const [x, y] of parseEditorPolygon(item.polygon)) addPoint(xs, ys, x, y)
  }
  for (const rack of scene.racks) {
    if (![rack.x, rack.y, rack.rotationZ, rack.cols, rack.cellW, rack.depthCount, rack.cellD].every(Number.isFinite)) continue
    const width = rack.cols * rack.cellW
    const depth = rack.depthCount * rack.cellD
    if (width <= 0 || depth <= 0) continue
    const radians = rack.rotationZ * Math.PI / 180
    const cos = Math.cos(radians)
    const sin = Math.sin(radians)
    for (const [localX, localY] of [[0, 0], [width, 0], [width, depth], [0, depth]] as const) {
      addPoint(xs, ys, rack.x + localX * cos - localY * sin, rack.y + localX * sin + localY * cos)
    }
  }
  for (const marker of scene.markers) addPoint(xs, ys, marker.x, marker.y)
  if (xs.length === 0) return null
  return { minX: Math.min(...xs), minY: Math.min(...ys), maxX: Math.max(...xs), maxY: Math.max(...ys) }
}

export function fitBounds(
  bounds: WorldBounds | null,
  width: number,
  height: number,
  padding = VIEWPORT_PADDING_PX,
): ViewportState {
  if (!bounds
    || ![bounds.minX, bounds.minY, bounds.maxX, bounds.maxY].every(Number.isFinite)
    || bounds.maxX < bounds.minX
    || bounds.maxY < bounds.minY) return createDefaultViewport(width, height)
  const canvasWidth = positiveSize(width)
  const canvasHeight = positiveSize(height)
  const safePadding = Math.max(0, Math.min(finite(padding, VIEWPORT_PADDING_PX), canvasWidth / 2, canvasHeight / 2))
  const worldWidth = Math.max(bounds.maxX - bounds.minX, 1)
  const worldHeight = Math.max(bounds.maxY - bounds.minY, 1)
  const zoom = Math.max(Number.EPSILON, Math.min(
    (canvasWidth - safePadding * 2) / worldWidth,
    (canvasHeight - safePadding * 2) / worldHeight,
  ))
  const centerX = (bounds.minX + bounds.maxX) / 2
  const centerY = (bounds.minY + bounds.maxY) / 2
  return {
    panX: centerX - canvasWidth / (2 * zoom),
    panY: centerY - canvasHeight / (2 * zoom),
    zoom,
    canvasWidth,
    canvasHeight,
  }
}
```

- [ ] **Step 4: Run the focused tests and verify GREEN**

Run the same command as Step 2.

Expected: `viewport.spec.ts` PASS with all eight cases green and no non-finite output.

- [ ] **Step 5: Commit the pure viewport unit**

```powershell
Set-Location ..
git add -- cp6.web/src/space-editor/viewport.ts cp6.web/src/space-editor/viewport.spec.ts
git diff --cached --check
git commit -m "feat(space): define editor viewport math"
```

### Task 2: SceneStage Two-Phase Viewport and Resize

**Files:**
- Create: `cp6.web/src/space-editor/SceneStage.viewport.spec.ts`
- Modify: `cp6.web/src/space-editor/SceneStage.ts:1-135,220-274`

- [ ] **Step 1: Write failing SceneStage viewport tests**

Create a prototype-based harness in `SceneStage.viewport.spec.ts` so tests do not depend on browser WebGL or a real canvas:

```ts
import { describe, expect, it, vi } from 'vitest'
import { SceneStage } from './SceneStage'
import type { EditorScene } from '@/types/space/scene'

function layer() {
  const find = vi.fn((): Array<{ destroy(): void }> => [])
  return {
    position: vi.fn(), scale: vi.fn(), batchDraw: vi.fn(),
    destroyChildren: vi.fn(), find,
  }
}

function harness() {
  const layers = {
    underlay: layer(), grid: layer(), zone: layer(), aisle: layer(),
    rack: layer(), marker: layer(), ghost: layer(),
  }
  const stage = Object.create(SceneStage.prototype) as SceneStage
  const scene = {
    zones: [], aisles: [], racks: [], markers: [], locations: [],
  } as unknown as EditorScene
  Object.assign(stage, {
    layers,
    viewport: { panX: 0, panY: 0, zoom: 0.05, canvasWidth: 800, canvasHeight: 600 },
    initialViewport: { panX: 0, panY: 0, zoom: 0.05, canvasWidth: 800, canvasHeight: 600 },
    currentScene: scene,
    previewViewport: null,
    stage: { fire: vi.fn(), width: vi.fn(), height: vi.fn(), size: vi.fn(), destroy: vi.fn() },
  })
  return { stage, layers, scene }
}

describe('SceneStage viewport lifecycle', () => {
  it('previews every visible layer without rebuilding the scene', () => {
    const { stage, layers } = harness()
    const render = vi.spyOn(stage, 'render')
    stage.previewPan(40, 25)
    for (const item of Object.values(layers)) {
      expect(item.position).toHaveBeenCalled()
      expect(item.scale).toHaveBeenCalled()
    }
    expect(render).not.toHaveBeenCalled()
  })

  it('commits many previews with one canonical redraw and clears transforms', () => {
    const { stage, layers, scene } = harness()
    const internal = stage as unknown as { renderCurrentScene(): void }
    const redraw = vi.spyOn(internal, 'renderCurrentScene')
    stage.previewPan(20, 10)
    stage.previewPan(20, 10)
    stage.commitViewport()
    expect(redraw).toHaveBeenCalledTimes(1)
    expect((stage as unknown as { currentScene: EditorScene }).currentScene).toBe(scene)
    for (const item of Object.values(layers)) {
      expect(item.position).toHaveBeenLastCalledWith({ x: 0, y: 0 })
      expect(item.scale).toHaveBeenLastCalledWith({ x: 1, y: 1 })
    }
  })

  it('keeps helper nodes while replacing rack scene nodes', () => {
    const { stage, layers } = harness()
    const rackNode = { destroy: vi.fn() }
    layers.rack.find.mockReturnValue([rackNode])
    ;(stage as unknown as { renderCurrentScene(): void }).renderCurrentScene()
    expect(rackNode.destroy).toHaveBeenCalledOnce()
    expect(layers.rack.destroyChildren).not.toHaveBeenCalled()
  })

  it('fits and resets without losing the current scene', () => {
    const { stage, scene } = harness()
    stage.fitAll()
    stage.resetView()
    expect((stage as unknown as { currentScene: EditorScene }).currentScene).toBe(scene)
    expect(stage.getViewportStatus().percent).toBe(100)
  })

  it('preserves a finite viewport on resize and disconnects observation on destroy', () => {
    const { stage } = harness()
    const disconnect = vi.fn()
    Object.assign(stage as object, { resizeObserver: { disconnect } })
    ;(stage as unknown as { resize(width: number, height: number): void }).resize(1200, 900)
    expect(Object.values(stage.getViewportSnapshot()).every(Number.isFinite)).toBe(true)
    stage.destroy()
    expect(disconnect).toHaveBeenCalledOnce()
  })
})
```

- [ ] **Step 2: Run the stage test and verify RED**

```powershell
Set-Location cp6.web
npm test -- src/space-editor/SceneStage.viewport.spec.ts
```

Expected: FAIL because preview, commit, fit/reset, and viewport status methods do not exist.

- [ ] **Step 3: Replace mutable `view` ownership with canonical viewport ownership**

In `SceneStage.ts`, remove the existing `view: ViewState` field and its constructor assignment, import the pure functions, and add these fields/getters:

```ts
import {
  clampRelativeZoom,
  collectSceneBounds,
  createDefaultViewport,
  fitBounds,
  panViewport,
  resizeViewport,
  toCoordinateView,
  viewportLayerTransform,
  zoomAround,
  zoomPercent,
  type ViewportState,
} from './viewport'

private viewport: ViewportState
private initialViewport: ViewportState
private initialBounds: ReturnType<typeof collectSceneBounds> = null
private viewportInitialized = false
private previewViewport: ViewportState | null = null
private currentScene: EditorScene | null = null
private resizeObserver: ResizeObserver | null = null

get view(): ViewState {
  return toCoordinateView(this.previewViewport ?? this.viewport)
}
```

Initialize both viewports from the actual container size, remove `bindZoomPan()`, and install a guarded observer:

```ts
const initial = createDefaultViewport(w, h)
this.viewport = initial
this.initialViewport = { ...initial }

if (typeof ResizeObserver !== 'undefined') {
  this.resizeObserver = new ResizeObserver(entries => {
    const entry = entries[0]
    if (!entry) return
    const width = entry.contentRect.width
    const height = entry.contentRect.height
    if (width > 0 && height > 0) this.resize(width, height)
  })
  this.resizeObserver.observe(container)
}
```

- [ ] **Step 4: Split first render from viewport-preserving redraw**

Make public `render(scene)` cache the scene and initialize fit exactly once:

```ts
render(scene: EditorScene): void {
  this.currentScene = scene
  if (!this.viewportInitialized) {
    this.initialBounds = collectSceneBounds(scene)
    const fitted = fitBounds(this.initialBounds, this.viewport.canvasWidth, this.viewport.canvasHeight)
    this.viewport = fitted
    this.initialViewport = { ...fitted }
    this.viewportInitialized = true
  }
  this.renderCurrentScene()
  this.emitViewportChange(false)
}
```

Move the existing draw body to `renderCurrentScene()`. Replace `this.layers.rack.destroyChildren()` with rack-only destruction so the Transformer survives:

```ts
private renderCurrentScene(): void {
  const scene = this.currentScene
  if (!scene) return
  this.layers.zone.destroyChildren()
  this.layers.aisle.destroyChildren()
  for (const node of this.layers.rack.find('.rack')) node.destroy()
  this.layers.marker.destroyChildren()
  for (const zone of scene.zones) this.renderZone(zone)
  for (const aisle of scene.aisles) this.renderAisle(aisle)
  for (const rack of scene.racks) this.renderRack(rack)
  for (const marker of scene.markers) this.renderMarker(marker)
  for (const layer of Object.values(this.layers)) layer.batchDraw()
}
```

- [ ] **Step 5: Add preview, commit, toolbar, fit/reset, and resize methods**

Replace the existing ineffective `zoom`, `pan`, and wheel binding with:

```ts
getViewportSnapshot(): ViewportState {
  return { ...(this.previewViewport ?? this.viewport) }
}

getViewportStatus(): { percent: number; canZoomIn: boolean; canZoomOut: boolean } {
  const shown = this.previewViewport ?? this.viewport
  const percent = zoomPercent(shown, this.initialViewport.zoom)
  return { percent, canZoomIn: percent < 800, canZoomOut: percent > 10 }
}

previewZoomAt(factor: number, anchor: XY): void {
  const shown = this.previewViewport ?? this.viewport
  this.preview(zoomAround(shown, shown.zoom * factor, anchor, this.initialViewport.zoom))
}

previewPan(dx: number, dy: number): void {
  this.preview(panViewport(this.previewViewport ?? this.viewport, dx, dy))
}

commitViewport(): void {
  if (this.previewViewport) this.viewport = this.previewViewport
  this.previewViewport = null
  this.resetLayerTransforms()
  this.layers.ghost.destroyChildren()
  this.renderCurrentScene()
  this.emitViewportChange(false)
}

cancelViewportPreview(): void {
  this.previewViewport = null
  this.resetLayerTransforms()
  for (const layer of Object.values(this.layers)) layer.batchDraw()
  this.emitViewportChange(false)
}

zoomStep(direction: 1 | -1): void {
  const anchor = { x: this.viewport.canvasWidth / 2, y: this.viewport.canvasHeight / 2 }
  this.previewZoomAt(direction > 0 ? 1.1 : 0.9, anchor)
  this.commitViewport()
}

fitAll(): void {
  if (!this.currentScene) return
  const fitted = fitBounds(collectSceneBounds(this.currentScene), this.viewport.canvasWidth, this.viewport.canvasHeight)
  const center = { x: fitted.canvasWidth / 2, y: fitted.canvasHeight / 2 }
  this.previewViewport = zoomAround(
    fitted,
    clampRelativeZoom(fitted.zoom, this.initialViewport.zoom),
    center,
    this.initialViewport.zoom,
  )
  this.commitViewport()
}

resetView(): void {
  this.previewViewport = { ...this.initialViewport }
  this.commitViewport()
}

private preview(next: ViewportState): void {
  this.previewViewport = next
  const transform = viewportLayerTransform(this.viewport, next)
  for (const layer of Object.values(this.layers)) {
    layer.position({ x: transform.x, y: transform.y })
    layer.scale({ x: transform.scale, y: transform.scale })
    layer.batchDraw()
  }
  this.emitViewportChange(true)
}

private resize(width: number, height: number): void {
  if (this.previewViewport) this.commitViewport()
  this.stage.size({ width, height })
  const resized = resizeViewport(this.viewport, width, height)
  const nextInitial = fitBounds(this.initialBounds, width, height)
  const center = { x: resized.canvasWidth / 2, y: resized.canvasHeight / 2 }
  this.initialViewport = nextInitial
  this.viewport = zoomAround(
    resized,
    clampRelativeZoom(resized.zoom, nextInitial.zoom),
    center,
    nextInitial.zoom,
  )
  this.renderCurrentScene()
  this.emitViewportChange(false)
}

private resetLayerTransforms(): void {
  for (const layer of Object.values(this.layers)) {
    layer.position({ x: 0, y: 0 })
    layer.scale({ x: 1, y: 1 })
  }
}

private emitViewportChange(preview: boolean): void {
  this.stage.fire('viewportchange', { preview, ...this.getViewportStatus() }, false)
}
```

Update `destroy()` to disconnect the observer and remove the obsolete `bindZoomPan()` method:

```ts
destroy(): void {
  this.resizeObserver?.disconnect()
  this.resizeObserver = null
  this.stage.destroy()
}
```

- [ ] **Step 6: Run stage, geometry, coordinate, and viewport tests**

```powershell
npm test -- src/space-editor/viewport.spec.ts src/space-editor/SceneStage.viewport.spec.ts src/space-editor/SceneStage.geometry.spec.ts src/space-editor/coords.spec.ts
```

Expected: all four files PASS; repeated preview does not call scene redraw and one commit calls it exactly once.

- [ ] **Step 7: Commit the stage integration**

```powershell
Set-Location ..
git add -- cp6.web/src/space-editor/SceneStage.ts cp6.web/src/space-editor/SceneStage.viewport.spec.ts
git diff --cached --check
git commit -m "feat(space): add two-phase editor viewport"
```

### Task 3: Unified Pointer and Wheel Navigation

**Files:**
- Create: `cp6.web/src/space-editor/interact/ViewportController.spec.ts`
- Create: `cp6.web/src/space-editor/interact/ViewportController.ts`
- Create: `cp6.web/src/space-editor/interact/InteractionManager.viewport.spec.ts`
- Modify: `cp6.web/src/space-editor/interact/InteractionManager.ts:61-200`

- [ ] **Step 1: Write failing controller gesture tests**

Create `ViewportController.spec.ts` using a real JSDOM element and a small host mock:

```ts
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ViewportController } from './ViewportController'

function pointer(type: string, values: Record<string, number> = {}): Event {
  const event = new Event(type, { bubbles: true, cancelable: true })
  Object.assign(event, { button: 0, pointerId: 1, clientX: 100, clientY: 100, ...values })
  return event
}

function harness() {
  const element = document.createElement('div')
  document.body.appendChild(element)
  Object.defineProperty(element, 'getBoundingClientRect', {
    value: () => ({ left: 0, top: 0, width: 800, height: 600, right: 800, bottom: 600, x: 0, y: 0, toJSON: () => ({}) }),
  })
  Object.assign(element, { setPointerCapture: vi.fn(), releasePointerCapture: vi.fn() })
  let tool = 'select'
  let background = true
  const host = {
    previewZoomAt: vi.fn(), previewPan: vi.fn(), commitViewport: vi.fn(),
    cancelViewportPreview: vi.fn(), zoomStep: vi.fn(), fitAll: vi.fn(), resetView: vi.fn(),
  }
  const controller = new ViewportController(element, host as never, {
    getActiveTool: () => tool,
    isBackground: () => background,
    onNavigationChange: vi.fn(),
  })
  return {
    element, host, controller,
    setTool(value: string) { tool = value },
    setBackground(value: boolean) { background = value },
  }
}

describe('ViewportController', () => {
  beforeEach(() => vi.useFakeTimers())
  afterEach(() => { vi.useRealTimers(); document.body.innerHTML = '' })

  it('previews cursor-anchored wheel zoom and commits once after 120ms', () => {
    const { element, host, controller } = harness()
    element.dispatchEvent(new WheelEvent('wheel', { deltaY: -120, clientX: 420, clientY: 360, bubbles: true, cancelable: true }))
    element.dispatchEvent(new WheelEvent('wheel', { deltaY: -60, clientX: 420, clientY: 360, bubbles: true, cancelable: true }))
    expect(host.previewZoomAt).toHaveBeenCalledTimes(2)
    expect(host.previewZoomAt.mock.calls[0]?.[1]).toEqual({ x: 420, y: 360 })
    vi.advanceTimersByTime(119)
    expect(host.commitViewport).not.toHaveBeenCalled()
    vi.advanceTimersByTime(1)
    expect(host.commitViewport).toHaveBeenCalledOnce()
    controller.destroy()
  })

  it.each(['select', 'drag', 'rotate', 'marker', 'zone'])('pans with Space+left in %s mode', tool => {
    const { element, host, controller, setTool } = harness()
    setTool(tool)
    controller.setSpaceHeld(true)
    element.dispatchEvent(pointer('pointerdown'))
    element.dispatchEvent(pointer('pointermove', { clientX: 140, clientY: 125 }))
    element.dispatchEvent(pointer('pointerup', { clientX: 140, clientY: 125 }))
    expect(host.previewPan).toHaveBeenCalledWith(40, 25)
    expect(host.commitViewport).toHaveBeenCalledOnce()
    controller.destroy()
  })

  it.each(['select', 'drag', 'rotate', 'marker', 'zone'])('pans with middle button in %s mode', tool => {
    const { element, host, controller, setTool } = harness()
    setTool(tool)
    element.dispatchEvent(pointer('pointerdown', { button: 1 }))
    element.dispatchEvent(pointer('pointermove', { button: 1, clientX: 130, clientY: 115 }))
    element.dispatchEvent(pointer('pointerup', { button: 1, clientX: 130, clientY: 115 }))
    expect(host.previewPan).toHaveBeenCalledWith(30, 15)
    controller.destroy()
  })

  it('pans a Drag-mode background but leaves rack drag to DragTool', () => {
    const first = harness()
    first.setTool('drag')
    first.element.dispatchEvent(pointer('pointerdown'))
    first.element.dispatchEvent(pointer('pointermove', { clientX: 110, clientY: 100 }))
    first.element.dispatchEvent(pointer('pointerup', { clientX: 110, clientY: 100 }))
    expect(first.host.previewPan).toHaveBeenCalled()
    first.controller.destroy()

    const second = harness()
    second.setTool('drag')
    second.setBackground(false)
    second.element.dispatchEvent(pointer('pointerdown'))
    second.element.dispatchEvent(pointer('pointermove', { clientX: 130, clientY: 120 }))
    second.element.dispatchEvent(pointer('pointerup', { clientX: 130, clientY: 120 }))
    expect(second.host.previewPan).not.toHaveBeenCalled()
    second.controller.destroy()
  })

  it.each(['pointercancel', 'lostpointercapture'])('commits the last safe preview on %s', eventType => {
    const { element, host, controller } = harness()
    controller.setSpaceHeld(true)
    element.dispatchEvent(pointer('pointerdown'))
    element.dispatchEvent(pointer('pointermove', { clientX: 120, clientY: 110 }))
    element.dispatchEvent(pointer(eventType, { clientX: 120, clientY: 110 }))
    expect(host.commitViewport).toHaveBeenCalledOnce()
    controller.destroy()
  })

  it('cancels an uncommitted wheel preview and removes its timer on destroy', () => {
    const { element, host, controller } = harness()
    element.dispatchEvent(new WheelEvent('wheel', { deltaY: -120, bubbles: true, cancelable: true }))
    controller.destroy()
    vi.runAllTimers()
    expect(host.cancelViewportPreview).toHaveBeenCalledOnce()
    expect(host.commitViewport).not.toHaveBeenCalled()
  })
})
```

- [ ] **Step 2: Run the controller test and verify RED**

```powershell
Set-Location cp6.web
npm test -- src/space-editor/interact/ViewportController.spec.ts
```

Expected: FAIL because `ViewportController` does not exist.

- [ ] **Step 3: Implement the controller with capture, threshold, debounce, and cleanup**

Create `ViewportController.ts` with this public surface and event lifecycle:

```ts
import type { XY } from '../coords'

export interface ViewportHost {
  previewZoomAt(factor: number, anchor: XY): void
  previewPan(dx: number, dy: number): void
  commitViewport(): void
  cancelViewportPreview(): void
  zoomStep(direction: 1 | -1): void
  fitAll(): void
  resetView(): void
}

export interface ViewportControllerOptions {
  getActiveTool(): string
  isBackground(point: XY): boolean
  onNavigationChange(active: boolean): void
}

const PAN_THRESHOLD_PX = 4
const WHEEL_COMMIT_MS = 120

export class ViewportController {
  private spaceHeld = false
  private enabled = true
  private pointerId: number | null = null
  private lastPoint: XY | null = null
  private pendingDragBackground = false
  private panning = false
  private moved = false
  private suppressClick = false
  private wheelTimer: ReturnType<typeof setTimeout> | null = null

  constructor(
    private readonly element: HTMLElement,
    private readonly host: ViewportHost,
    private readonly options: ViewportControllerOptions,
  ) {
    element.addEventListener('wheel', this.onWheel, { passive: false })
    element.addEventListener('pointerdown', this.onPointerDown, true)
    element.addEventListener('pointermove', this.onPointerMove, true)
    element.addEventListener('pointerup', this.onPointerUp, true)
    element.addEventListener('pointercancel', this.onPointerCancel, true)
    element.addEventListener('lostpointercapture', this.onPointerCancel, true)
    element.addEventListener('click', this.onClick, true)
    window.addEventListener('blur', this.onBlur)
  }

  setSpaceHeld(held: boolean): void { this.spaceHeld = held }
  setEnabled(enabled: boolean): void {
    this.enabled = enabled
    if (!enabled) {
      this.flushWheel(true)
      this.finishPan(true)
    }
  }
  zoomIn(): void { this.flushWheel(true); this.host.zoomStep(1) }
  zoomOut(): void { this.flushWheel(true); this.host.zoomStep(-1) }
  fitAll(): void { this.flushWheel(true); this.host.fitAll() }
  resetView(): void { this.flushWheel(true); this.host.resetView() }

  destroy(): void {
    this.flushWheel(false)
    this.finishPan(false)
    this.element.removeEventListener('wheel', this.onWheel)
    this.element.removeEventListener('pointerdown', this.onPointerDown, true)
    this.element.removeEventListener('pointermove', this.onPointerMove, true)
    this.element.removeEventListener('pointerup', this.onPointerUp, true)
    this.element.removeEventListener('pointercancel', this.onPointerCancel, true)
    this.element.removeEventListener('lostpointercapture', this.onPointerCancel, true)
    this.element.removeEventListener('click', this.onClick, true)
    window.removeEventListener('blur', this.onBlur)
  }

  private point(event: MouseEvent): XY {
    const rect = this.element.getBoundingClientRect()
    return { x: event.clientX - rect.left, y: event.clientY - rect.top }
  }

  private onWheel = (event: WheelEvent): void => {
    if (!this.enabled || event.deltaY === 0) return
    event.preventDefault()
    this.host.previewZoomAt(Math.exp(-event.deltaY * 0.0015), this.point(event))
    if (this.wheelTimer) clearTimeout(this.wheelTimer)
    this.wheelTimer = setTimeout(() => {
      this.wheelTimer = null
      this.host.commitViewport()
    }, WHEEL_COMMIT_MS)
  }

  private onPointerDown = (event: PointerEvent): void => {
    if (!this.enabled) return
    const point = this.point(event)
    const forcedPan = event.button === 1 || (event.button === 0 && this.spaceHeld)
    const dragBackground = event.button === 0
      && this.options.getActiveTool() === 'drag'
      && this.options.isBackground(point)
    if (!forcedPan && !dragBackground) return
    this.flushWheel(true)
    this.pointerId = event.pointerId
    this.lastPoint = point
    this.pendingDragBackground = dragBackground && !forcedPan
    this.panning = forcedPan
    this.moved = false
    this.element.setPointerCapture?.(event.pointerId)
    if (forcedPan) {
      this.options.onNavigationChange(true)
      event.preventDefault()
      event.stopImmediatePropagation()
    }
  }

  private onPointerMove = (event: PointerEvent): void => {
    if (event.pointerId !== this.pointerId || !this.lastPoint) return
    const point = this.point(event)
    const dx = point.x - this.lastPoint.x
    const dy = point.y - this.lastPoint.y
    if (this.pendingDragBackground && !this.panning && Math.abs(dx) + Math.abs(dy) >= PAN_THRESHOLD_PX) {
      this.panning = true
      this.pendingDragBackground = false
      this.options.onNavigationChange(true)
    }
    if (!this.panning) return
    if (dx !== 0 || dy !== 0) {
      this.host.previewPan(dx, dy)
      this.lastPoint = point
      this.moved = true
    }
    event.preventDefault()
    event.stopImmediatePropagation()
  }

  private onPointerUp = (event: PointerEvent): void => {
    if (event.pointerId !== this.pointerId) return
    if (this.panning) {
      event.preventDefault()
      event.stopImmediatePropagation()
    }
    this.finishPan(true)
  }

  private onPointerCancel = (event: PointerEvent): void => {
    if (event.pointerId === this.pointerId) this.finishPan(true)
  }
  private onBlur = (): void => {
    this.spaceHeld = false
    this.flushWheel(true)
    this.finishPan(true)
  }
  private onClick = (event: MouseEvent): void => {
    if (!this.suppressClick) return
    this.suppressClick = false
    event.preventDefault()
    event.stopImmediatePropagation()
  }

  private finishPan(commit: boolean): void {
    const pointerId = this.pointerId
    const wasPanning = this.panning
    const moved = this.moved
    this.pointerId = null
    this.lastPoint = null
    this.pendingDragBackground = false
    this.panning = false
    this.moved = false
    if (pointerId !== null) {
      try { this.element.releasePointerCapture?.(pointerId) } catch { /* capture already lost */ }
    }
    if (wasPanning && moved) {
      if (commit) this.host.commitViewport()
      else this.host.cancelViewportPreview()
      this.suppressClick = true
    }
    if (wasPanning) this.options.onNavigationChange(false)
  }

  private flushWheel(commit: boolean): void {
    if (!this.wheelTimer) return
    clearTimeout(this.wheelTimer)
    this.wheelTimer = null
    if (commit) this.host.commitViewport()
    else this.host.cancelViewportPreview()
  }
}
```

- [ ] **Step 4: Write the failing InteractionManager integration tests**

Create `InteractionManager.viewport.spec.ts` with a real `Konva.Stage` mock boundary. Assert these exact behaviors:

```ts
it('exposes toolbar and Space lifecycle through one viewport controller', () => {
  manager.setSpaceHeld(true)
  manager.zoomIn()
  manager.zoomOut()
  manager.fitAll()
  manager.resetView()
  expect(controller.setSpaceHeld).toHaveBeenCalledWith(true)
  expect(controller.zoomIn).toHaveBeenCalledOnce()
  expect(controller.zoomOut).toHaveBeenCalledOnce()
  expect(controller.fitAll).toHaveBeenCalledOnce()
  expect(controller.resetView).toHaveBeenCalledOnce()
})

it('refreshes the transformer after a committed viewportchange without switching tool', () => {
  manager.switchTool('rotate')
  stageHandlers['viewportchange.im']!({ preview: false })
  expect(manager.activeTool).toBe('rotate')
  expect(transformer.nodes).toHaveBeenCalled()
})

it('destroys controller and viewport listener exactly once', () => {
  manager.destroy()
  expect(controller.destroy).toHaveBeenCalledOnce()
  expect(konvaStage.off).toHaveBeenCalledWith('viewportchange.im')
})
```

- [ ] **Step 5: Integrate the controller into InteractionManager**

Add a private controller, optional navigation callback, and target lookup:

```ts
import { ViewportController } from './ViewportController'

private readonly viewportController: ViewportController
private onNavigationChange: (active: boolean) => void = () => {}

this.viewportController = new ViewportController(stage.stage.container(), stage, {
  getActiveTool: () => this._activeTool,
  isBackground: point => {
    const target = stage.stage.getIntersection(point)
    return target === null || (!findRackGroup(target) && !isTransformerNode(target))
  },
  onNavigationChange: active => this.onNavigationChange(active),
})
```

Expose only these delegating methods:

```ts
setSpaceHeld(held: boolean): void { this.viewportController.setSpaceHeld(held) }
setNavigationStateHandler(handler: (active: boolean) => void): void { this.onNavigationChange = handler }
zoomIn(): void { this.viewportController.zoomIn() }
zoomOut(): void { this.viewportController.zoomOut() }
fitAll(): void { this.viewportController.fitAll() }
resetView(): void { this.viewportController.resetView() }
```

Extend `setEnabled()` with `this.viewportController.setEnabled(enabled)`. Add:

```ts
konvaStage.on('viewportchange.im', (event: { preview?: boolean }) => {
  if (event.preview !== true) this.refreshTransformer()
})
```

In `destroy()`, call `this.viewportController.destroy()` and remove `viewportchange.im` before destroying the Transformer.

- [ ] **Step 6: Run navigation and editor-tool regressions**

```powershell
npm test -- src/space-editor/interact/ViewportController.spec.ts src/space-editor/interact/InteractionManager.viewport.spec.ts src/space-editor/interact/rotate/rotateGeometry.spec.ts src/space-editor/interact/tools/zoneGeom.spec.ts src/space-editor/interact/select/lassoHit.spec.ts
```

Expected: all files PASS; Drag-mode rack routing remains with `DragTool`, all five tools support forced navigation, and no command test regresses.

- [ ] **Step 7: Commit unified navigation**

```powershell
Set-Location ..
git add -- cp6.web/src/space-editor/interact/ViewportController.ts cp6.web/src/space-editor/interact/ViewportController.spec.ts cp6.web/src/space-editor/interact/InteractionManager.ts cp6.web/src/space-editor/interact/InteractionManager.viewport.spec.ts
git diff --cached --check
git commit -m "feat(space): unify editor viewport gestures"
```

### Task 4: Accessible Toolbar, Percentage, and Keyboard State

**Files:**
- Modify: `cp6.web/src/views/space/editor/FloorEditor.feedback.spec.ts:12-75,140-375`
- Modify: `cp6.web/src/views/space/editor/FloorEditor.vue:1-45,140-236,590-722,792-838`

- [ ] **Step 1: Extend the component harness and write failing tests**

First widen the hoisted mock types so viewport events can carry status and the new controller methods are observable:

```ts
handlers: Record<string, (event?: { percent?: number; canZoomIn?: boolean; canZoomOut?: boolean }) => void>
zoomIn: ReturnType<typeof vi.fn>
zoomOut: ReturnType<typeof vi.fn>
fitAll: ReturnType<typeof vi.fn>
resetView: ReturnType<typeof vi.fn>
setSpaceHeld: ReturnType<typeof vi.fn>
setNavigationStateHandler: ReturnType<typeof vi.fn>
navigationHandler: (active: boolean) => void
```

Add these methods and status to the existing SceneStage/InteractionManager mocks:

```ts
on: vi.fn((event: string, handler: (payload?: { percent?: number; canZoomIn?: boolean; canZoomOut?: boolean }) => void) => {
  this.handlers[event] = handler
})
getViewportStatus = vi.fn(() => ({ percent: 100, canZoomIn: true, canZoomOut: true }))
zoomIn = vi.fn()
zoomOut = vi.fn()
fitAll = vi.fn()
resetView = vi.fn()
setSpaceHeld = vi.fn()
setNavigationStateHandler = vi.fn((handler: (active: boolean) => void) => { this.navigationHandler = handler })
navigationHandler: (active: boolean) => void = () => {}
```

Add these component cases:

```ts
it('renders five accessible viewport controls and delegates each action', async () => {
  const { wrapper } = await mountEditor('zh-CN')
  expect(wrapper.get('[data-test="zoom-percent"]').text()).toBe('100%')
  await wrapper.get('[data-test="zoom-out"]').trigger('click')
  await wrapper.get('[data-test="zoom-in"]').trigger('click')
  await wrapper.get('[data-test="fit-all"]').trigger('click')
  await wrapper.get('[data-test="reset-view"]').trigger('click')
  const interaction = interactionInstances[0]!
  expect(interaction.zoomOut).toHaveBeenCalledOnce()
  expect(interaction.zoomIn).toHaveBeenCalledOnce()
  expect(interaction.fitAll).toHaveBeenCalledOnce()
  expect(interaction.resetView).toHaveBeenCalledOnce()
  expect(wrapper.get('[data-test="zoom-out"]').attributes('aria-label')).toBe('缩小视图')
  expect(wrapper.get('[data-test="fit-all"]').attributes('aria-label')).toBe('适配全部内容')
})

it('updates percentage and limit states from viewportchange', async () => {
  const { wrapper } = await mountEditor('zh-CN')
  sceneStageInstances[0]!.handlers['viewportchange.toolbar']!({ percent: 800, canZoomIn: false, canZoomOut: true })
  await flushPromises()
  expect(wrapper.get('[data-test="zoom-percent"]').text()).toBe('800%')
  expect(wrapper.get('[data-test="zoom-in"]').attributes('disabled')).toBeDefined()
})

it('shows grabbing cursor only while viewport navigation is active', async () => {
  const { wrapper } = await mountEditor('zh-CN')
  interactionInstances[0]!.navigationHandler(true)
  await flushPromises()
  expect(wrapper.get('[data-test="editor-canvas"]').classes()).toContain('viewport-panning')
  interactionInstances[0]!.navigationHandler(false)
  await flushPromises()
  expect(wrapper.get('[data-test="editor-canvas"]').classes()).not.toContain('viewport-panning')
})

it('forwards Space outside editable controls but not while typing', async () => {
  const { wrapper } = await mountEditor('zh-CN')
  document.dispatchEvent(new KeyboardEvent('keydown', { code: 'Space', key: ' ', bubbles: true }))
  document.dispatchEvent(new KeyboardEvent('keyup', { code: 'Space', key: ' ', bubbles: true }))
  expect(interactionInstances[0]!.setSpaceHeld).toHaveBeenNthCalledWith(1, true)
  expect(interactionInstances[0]!.setSpaceHeld).toHaveBeenNthCalledWith(2, false)

  const input = wrapper.get('input[type="file"]').element
  input.dispatchEvent(new KeyboardEvent('keydown', { code: 'Space', key: ' ', bubbles: true }))
  expect(interactionInstances[0]!.setSpaceHeld).toHaveBeenCalledTimes(2)
})

it('does not dirty the scene when viewport controls are used', async () => {
  const { wrapper, store } = await mountEditor('zh-CN')
  const before = { upsert: store.dirty.upsert.size, del: store.dirty.del.size }
  await wrapper.get('[data-test="zoom-in"]').trigger('click')
  await wrapper.get('[data-test="fit-all"]').trigger('click')
  expect({ upsert: store.dirty.upsert.size, del: store.dirty.del.size }).toEqual(before)
})
```

- [ ] **Step 2: Run the component test and verify RED**

```powershell
Set-Location cp6.web
npm test -- src/views/space/editor/FloorEditor.feedback.spec.ts
```

Expected: FAIL because viewport controls, Space forwarding, and status synchronization are absent.

- [ ] **Step 3: Add reactive viewport/navigation state and lifecycle wiring**

In `FloorEditor.vue` add:

```ts
const viewportStatus = ref({ percent: 100, canZoomIn: true, canZoomOut: true })
const viewportPanning = ref(false)
const spacePanReady = ref(false)

function syncViewportStatus(event?: { percent?: number; canZoomIn?: boolean; canZoomOut?: boolean }): void {
  viewportStatus.value = event && typeof event.percent === 'number'
    ? {
        percent: event.percent,
        canZoomIn: event.canZoomIn !== false,
        canZoomOut: event.canZoomOut !== false,
      }
    : stageRef?.getViewportStatus() ?? viewportStatus.value
}
```

Immediately after creating `InteractionManager`:

```ts
imRef.value.setNavigationStateHandler(active => { viewportPanning.value = active })
stageRef.stage.on('viewportchange.toolbar', syncViewportStatus)
syncViewportStatus()
```

Before destroying the stage:

```ts
stageRef?.stage.off('viewportchange.toolbar')
```

Add this helper before `onKeydown`:

```ts
function isEditableTarget(target: EventTarget | null): boolean {
  const element = target instanceof HTMLElement ? target : null
  return element !== null && (
    element.tagName === 'INPUT'
    || element.tagName === 'TEXTAREA'
    || element.tagName === 'SELECT'
    || element.isContentEditable
  )
}
```

Replace the existing `tag === 'INPUT' ...` editable-target block in `onKeydown` with this block immediately after Ctrl tracking:

```ts
if (isEditableTarget(e.target)) return
if (e.code === 'Space') {
  e.preventDefault()
  spacePanReady.value = true
  im?.setSpaceHeld(true)
  return
}
```

At the start of `onKeyup`:

```ts
if (e.code === 'Space') {
  spacePanReady.value = false
  imRef.value?.setSpaceHeld(false)
}
```

- [ ] **Step 4: Render the approved five-control toolbar**

Insert this group before the flexible spacer:

```vue
<div class="viewport-controls" role="group" :aria-label="t('视图控制')">
  <el-button
    data-test="zoom-out"
    size="small"
    :disabled="!viewportStatus.canZoomOut"
    :aria-label="t('缩小视图')"
    :title="t('缩小视图')"
    @click="imRef?.zoomOut()"
  >−</el-button>
  <span data-test="zoom-percent" class="zoom-percent" aria-live="polite">
    {{ viewportStatus.percent }}%
  </span>
  <el-button
    data-test="zoom-in"
    size="small"
    :disabled="!viewportStatus.canZoomIn"
    :aria-label="t('放大视图')"
    :title="t('放大视图')"
    @click="imRef?.zoomIn()"
  >+</el-button>
  <el-button data-test="fit-all" size="small" :aria-label="t('适配全部内容')" @click="imRef?.fitAll()">
    {{ t('适配全部') }}
  </el-button>
  <el-button data-test="reset-view" size="small" :aria-label="t('复位视图')" @click="imRef?.resetView()">
    {{ t('复位视图') }}
  </el-button>
</div>
```

Extend the canvas class binding:

```vue
{
  'placement-mode': placementMode || connectorPlacementMode,
  'viewport-pan-ready': spacePanReady,
  'viewport-panning': viewportPanning,
}
```

Add styles:

```css
.viewport-controls { display: inline-flex; align-items: center; gap: 4px; }
.zoom-percent { min-width: 52px; text-align: center; font-variant-numeric: tabular-nums; }
.canvas-container.viewport-pan-ready { cursor: grab; }
.canvas-container.viewport-panning { cursor: grabbing; }
```

- [ ] **Step 5: Run component and viewport integration tests**

```powershell
npm test -- src/views/space/editor/FloorEditor.feedback.spec.ts src/space-editor/interact/ViewportController.spec.ts src/space-editor/interact/InteractionManager.viewport.spec.ts src/space-editor/SceneStage.viewport.spec.ts
```

Expected: all files PASS; controls are accessible, percentage synchronizes, Space is ignored in editable elements, and dirty sets remain unchanged.

- [ ] **Step 6: Commit the Vue integration**

```powershell
Set-Location ..
git add -- cp6.web/src/views/space/editor/FloorEditor.vue cp6.web/src/views/space/editor/FloorEditor.feedback.spec.ts
git diff --cached --check
git commit -m "feat(space): expose editor viewport controls"
```

### Task 5: Regression Gates, Review, and Project Memory

**Files:**
- Modify: `docs/project-memory/PROJECT_STATE.md`
- Modify: `docs/project-memory/05-Completed.md`
- Modify: `docs/project-memory/06-Todo.md`
- Modify: `docs/project-memory/CHANGELOG-AI.md`

- [ ] **Step 1: Run all focused Space Editor tests**

```powershell
Set-Location cp6.web
npm test -- src/space-editor/viewport.spec.ts src/space-editor/SceneStage.viewport.spec.ts src/space-editor/SceneStage.geometry.spec.ts src/space-editor/coords.spec.ts src/space-editor/interact/ViewportController.spec.ts src/space-editor/interact/InteractionManager.viewport.spec.ts src/space-editor/interact/tools/zoneGeom.spec.ts src/space-editor/interact/rotate/rotateGeometry.spec.ts src/space-editor/interact/select/lassoHit.spec.ts src/views/space/editor/FloorEditor.feedback.spec.ts
```

Expected: every focused file PASS with zero failed tests and no new Vue/Konva runtime warning attributable to viewport behavior.

- [ ] **Step 2: Run complete frontend gates**

```powershell
npm test
npm run type-check
npm run build-only
```

Expected: full Vitest suite PASS, Vue/TypeScript checking exits 0, and the production Vite build exits 0.

- [ ] **Step 3: Review the complete branch diff**

```powershell
Set-Location ..
git status --short --branch
git diff origin/main...HEAD --check
git diff --stat origin/main...HEAD
git diff origin/main...HEAD -- cp6.web/src/space-editor cp6.web/src/views/space/editor docs/superpowers
```

Expected: only approved viewport code/tests/design/plan are present; no credentials, machine configuration, generated `dist`, database file, API change, migration, or unrelated root-worktree content appears.

- [ ] **Step 4: Record the verified implementation without claiming production deployment**

Prepend these sections after the titles of the four project-memory files.

`PROJECT_STATE.md`:

```markdown
## Space Editor 自由视口实现完成（2026-08-31）

- 二维空间编辑器已支持指针锚定滚轮缩放、Space + 左键/中键全工具平移，以及拖拽模式空白平移与货架移动分流；工具栏提供缩小、百分比、放大、适配全部和复位视图。
- 采用两阶段 Konva 图层预览与单次提交重绘；初始适配定义为 100%，范围 10%～800%，ResizeObserver 保持世界中心，提交后刷新 Transformer 与命中状态。
- 视口只存在于前端页面生命周期，不进入场景 DTO、命令栈、保存请求或数据库；本任务没有 API、迁移和 CP6DB 数据变更，也没有把开发 Docker 验收描述为生产发布。
```

`05-Completed.md`:

```markdown
## 2026-08-31 Space Editor 自由视口

- 关闭空间编辑器只能围绕固定中心查看、无法检视角落的问题；所有五种工具均可临时导航，拖拽模式能区分空白画布和货架对象。
- 增加可访问视图工具栏、48px 全内容适配、复位、10%～800% 限制、旋转货架边界、错误几何回退和响应式尺寸处理。
- 纯数学、Stage、手势、InteractionManager、Vue 组件及完整前端门禁均通过；视口导航不产生场景脏状态或数据库写入。
```

`06-Todo.md` under `## P1：Space Studio V1 GA 后边界与运营增强`:

```markdown
- Space Editor 自由视口缺陷已关闭；既有七层 Konva 性能告警仍是独立性能评估项，只有建立场景规模基准并证明收益后才允许重构图层结构。
```

`CHANGELOG-AI.md`:

```markdown
## 2026-08-31：Space Editor 自由视口

- Added pure viewport/bounds math, cursor-anchored 10%–800% zoom, all-mode pan gestures, fit/reset controls and responsive center-preserving resize.
- Added a two-phase Konva preview/commit pipeline and unified pointer controller; Drag-mode background navigation no longer conflicts with rack movement, editing tools or Transformer state.
- Added focused and full frontend verification. Viewport state remains frontend-only and does not enter scene persistence, command history, API contracts, migrations or CP6DB.
```

- [ ] **Step 5: Validate and commit project memory**

```powershell
git diff --check
git add -- docs/project-memory/PROJECT_STATE.md docs/project-memory/05-Completed.md docs/project-memory/06-Todo.md docs/project-memory/CHANGELOG-AI.md
git diff --cached --check
git commit -m "docs(space): record free editor viewport"
```

### Task 6: Pull Request, Exact-Main Docker Candidate, and Public Acceptance

**Files:**
- Verify only: `.github/workflows/client-contract.yml`
- Use only: root development `docker-compose.yml`, `CP6.WebApi/Dockerfile`, `cp6.web/Dockerfile`
- Do not modify: Docker, deployment, database, migration, or tunnel files

- [ ] **Step 1: Push the task branch and create the auditable PR**

```powershell
git status --short --branch
git push -u origin codex/space-editor-free-viewport-20260831
gh pr create --base main --head codex/space-editor-free-viewport-20260831 --title "feat(space): add free editor viewport" --body "Implements the approved 2D Space Editor free-viewport design with TDD coverage. Frontend-only: no API, migration, scene persistence, or CP6DB changes."
```

Expected: push succeeds, one PR URL is returned, and the PR diff contains only this task.

- [ ] **Step 2: Wait for required PR checks and review the remote diff**

```powershell
gh pr checks --watch
gh pr diff
```

Expected: every required check reports `pass`; remote diff matches the locally reviewed branch and contains no release/deployment or database changes.

- [ ] **Step 3: Merge without rewriting history and verify remote main**

```powershell
gh pr merge --merge --delete-branch=false
git fetch origin main
$remoteMain = git rev-parse origin/main
git merge-base --is-ancestor HEAD origin/main
if ($LASTEXITCODE -ne 0) { throw 'origin/main does not contain the verified task branch' }
$remoteMain
```

Expected: the PR is merged, ancestor verification exits 0, and `$remoteMain` is the exact Docker source identity.

- [ ] **Step 4: Perform read-only Docker/CP6DB preflight**

From the clean task worktree, record current service and volume identities without changing them:

```powershell
docker version --format '{{.Server.Version}}'
docker compose --project-name cp6 --env-file 'D:\CP6\.env' --project-directory 'D:\CP6\.claude\worktrees\space-editor-free-viewport-20260831' -f docker-compose.yml ps
docker inspect cp6-db --format '{{.Id}} {{range .Mounts}}{{.Name}} {{.Destination}}{{end}}'
docker inspect cp6-cloudflared --format '{{.State.Status}} {{range .NetworkSettings.Networks}}{{.NetworkID}}{{end}}'
```

Expected: Docker is ready; `cp6-db`, `cp6-api`, `cp6-web`, and the existing public connector are running; the database volume identity is recorded. Stop if the connector serving `cp6.uk` cannot be identified safely.

- [ ] **Step 5: Build API/Web from exact remote main and recreate only application containers**

Create a clean detached worktree from the verified remote commit:

```powershell
$deployWorktree = 'D:\CP6\.claude\worktrees\space-editor-free-viewport-main-20260831'
git worktree add --detach $deployWorktree $remoteMain
docker compose --project-name cp6 --env-file 'D:\CP6\.env' --project-directory $deployWorktree -f "$deployWorktree\docker-compose.yml" build --build-arg RELEASE_VERSION=0.0.0-dev --build-arg GIT_SHA=$remoteMain cp6-api cp6-web
docker compose --project-name cp6 --env-file 'D:\CP6\.env' --project-directory $deployWorktree -f "$deployWorktree\docker-compose.yml" up -d --no-deps cp6-api cp6-web
```

Expected: API/Web images are built from the same exact `origin/main` SHA; only `cp6-api` and `cp6-web` are recreated. Do not run `down`, `down -v`, `db-init`, snapshot import, SQL, or any infrastructure service command.

- [ ] **Step 6: Verify service health and database-volume preservation**

```powershell
docker compose --project-name cp6 --env-file 'D:\CP6\.env' --project-directory $deployWorktree -f "$deployWorktree\docker-compose.yml" ps
Invoke-WebRequest -UseBasicParsing -Uri 'http://127.0.0.1:9991/health/ready' -TimeoutSec 15
Invoke-WebRequest -UseBasicParsing -Uri 'http://127.0.0.1:8080/release.json' -TimeoutSec 15
Invoke-WebRequest -UseBasicParsing -Uri 'https://api.cp6.uk/health/ready' -TimeoutSec 15
Invoke-WebRequest -UseBasicParsing -Uri 'https://cp6.uk/release.json' -TimeoutSec 15
docker inspect cp6-db --format '{{.Id}} {{range .Mounts}}{{.Name}} {{.Destination}}{{end}}'
```

Expected: local/public endpoints return success, release identity is the merged main SHA, and the `cp6-db` container/volume identity matches Step 4.

- [ ] **Step 7: Run Chrome acceptance without saving or issuing write requests**

Open the real editor route for floor `e0b4fcfd-80ee-4c82-95cd-350519b902f9` through `https://cp6.uk`. Record console errors, page errors, and POST/PUT/PATCH/DELETE requests from the moment the editor finishes loading.

Verify in this order:

1. Confirm the initial layout is fully visible at `100%` and all five viewport controls have accessible names.
2. Place the pointer over the upper-left rack, zoom in with the wheel, and confirm the rack remains under the pointer while the screenshot hash changes.
3. Use `Space` + left drag and middle drag in each of 选择、拖拽、旋转、打点、新建库区; confirm the canvas moves and the selected tool does not change.
4. In 拖拽 mode, drag blank canvas to pan, then drag one rack and undo the rack command before continuing.
5. Navigate to all four layout corners, then click 适配全部 and confirm the complete layout returns inside the viewport.
6. Reach 10% and 800% limits and confirm the respective toolbar button becomes disabled; click 复位视图 and confirm `100%`.
7. Resize the browser window and confirm the same world center stays visible and the Transformer remains aligned with a selected rack.
8. Do not click 保存. Confirm the captured write-request list, console-error list, and page-error list are empty.

Expected: every visual operation is observable, all four corners are inspectable, no tool action is triggered by navigation, and CP6DB receives no scene write.

- [ ] **Step 8: Hand off the exact acceptance environment to the user**

Report:

- `https://cp6.uk/space/editor/e0b4fcfd-80ee-4c82-95cd-350519b902f9`
- merged `origin/main` SHA and PR URL
- focused/full test, type-check, build, Docker health, local/public release identity, and browser acceptance results
- the unchanged `cp6-db` volume identity
- confirmation that no save, SQL, snapshot import, database merge, destructive Docker command, or production deployment occurred

Keep the environment running for the user's manual acceptance. Do not remove worktrees, branches, images, containers, volumes, or evidence unless the user separately requests cleanup.
