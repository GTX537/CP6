import { describe, expect, it, vi } from 'vitest'
import {
  ACTIVE_ROTATE_HANDLE_STYLE,
  INACTIVE_ROTATE_HANDLE_STYLE,
  applyRotateHandleStyle,
} from './rotateHandleStyle'
import Konva from 'konva'
import { RotateTool } from '../tools/RotateTool'
import { InteractionManager } from '../InteractionManager'

const canvasContext = {
  clearRect: vi.fn(),
  fillRect: vi.fn(),
  fillStyle: '',
  getImageData: vi.fn(() => ({ data: new Uint8ClampedArray(400) })),
}
vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(canvasContext as never)

function createInteractionHarness(initialSelection: string[] = []) {
  const rackNodes = new Map([
    ['rack-1', new Konva.Group({ id: 'rack-1', name: 'rack' })],
    ['rack-2', new Konva.Group({ id: 'rack-2', name: 'rack' })],
  ])
  let selectionIds = [...initialSelection]
  const store = {
    get selectionIds() { return selectionIds },
    scene: {
      racks: [{ id: 'rack-1' }, { id: 'rack-2' }],
      aisles: [],
    },
    setSelection: vi.fn((ids: string[]) => { selectionIds = [...ids] }),
    clearSelection: vi.fn(() => { selectionIds = [] }),
  }
  const rackLayer = { add: vi.fn(), batchDraw: vi.fn() }
  const stage = {
    stage: { on: vi.fn(), off: vi.fn() },
    layers: {
      rack: rackLayer,
      ghost: { batchDraw: vi.fn() },
    },
    view: { zoom: 1 },
    getRackNode: vi.fn((id: string) => rackNodes.get(id) ?? null),
  }
  const afterCommand = vi.fn()
  const manager = new InteractionManager(stage as never, store as never, afterCommand)
  const nodesSpy = vi.spyOn(manager.transformer, 'nodes')

  return { manager, nodesSpy, rackNodes, store, afterCommand }
}

function lastTransformerAssignment(nodesSpy: ReturnType<typeof vi.spyOn>): Konva.Node[] | undefined {
  return nodesSpy.mock.calls.filter((args: unknown[]) => args.length === 1).at(-1)?.[0] as Konva.Node[] | undefined
}

describe('rotate handle style', () => {
  it('applies the high-visibility active style', () => {
    const transformer = new Konva.Transformer()

    applyRotateHandleStyle(transformer, true)

    expect(ACTIVE_ROTATE_HANDLE_STYLE.anchorSize).toBeGreaterThanOrEqual(18)
    expect(transformer.anchorSize()).toBe(18)
    expect(transformer.anchorCornerRadius()).toBe(99)
    expect(transformer.anchorFill()).toBe('#10bfc8')
    expect(transformer.anchorStroke()).toBe('#ffffff')
    expect(transformer.anchorStrokeWidth()).toBe(3)
    expect(transformer.borderStroke()).toBe('#087d84')
    expect(transformer.borderStrokeWidth()).toBe(2)
    expect(transformer.rotateAnchorOffset()).toBe(42)
    const rotater = transformer.findOne('.rotater')
    expect(rotater).toBeTruthy()
    expect(rotater?.getAttr('shadowColor')).toBe('#075f65')
    expect(rotater?.getAttr('shadowBlur')).toBe(3)
    expect(rotater?.getAttr('shadowOpacity')).toBe(0.9)
  })

  it('restores the inactive style', () => {
    const transformer = new Konva.Transformer()

    applyRotateHandleStyle(transformer, true)
    applyRotateHandleStyle(transformer, false)

    expect(transformer.anchorSize()).toBe(INACTIVE_ROTATE_HANDLE_STYLE.anchorSize)
    expect(transformer.anchorFill()).toBe(INACTIVE_ROTATE_HANDLE_STYLE.anchorFill)
    expect(transformer.borderStrokeWidth()).toBe(INACTIVE_ROTATE_HANDLE_STYLE.borderStrokeWidth)
    expect(transformer.rotateAnchorOffset()).toBe(INACTIVE_ROTATE_HANDLE_STYLE.rotateAnchorOffset)
    const rotater = transformer.findOne('.rotater')
    expect(rotater?.getAttr('shadowColor')).toBe('transparent')
    expect(rotater?.getAttr('shadowBlur')).toBe(0)
    expect(rotater?.getAttr('shadowOpacity')).toBe(0)
  })
})

describe('RotateTool handle lifecycle', () => {
  it('applies active then inactive style around rotation mode', () => {
    const calls: string[] = []
    const transformer = {
      setAttrs: vi.fn(() => calls.push('setAttrs')),
      rotateEnabled: vi.fn((value?: boolean) => {
        if (value !== undefined) calls.push(`rotateEnabled:${value}`)
        return value ?? false
      }),
      resizeEnabled: vi.fn(),
      enabledAnchors: vi.fn(),
      on: vi.fn(),
      off: vi.fn(),
      nodes: vi.fn(),
      findOne: vi.fn(() => undefined),
    } as unknown as Konva.Transformer
    const ctx = {
      transformer,
      stage: { layers: { rack: { batchDraw: vi.fn() }, ghost: { batchDraw: vi.fn() } }, getRackNode: vi.fn() },
      store: { selectionIds: [], scene: null },
      snap: {},
      ctrlHeld: () => false,
      afterCommand: vi.fn(),
    } as never
    const tool = new RotateTool(ctx)

    tool.onActivate()
    tool.onDeactivate()

    expect(transformer.setAttrs).toHaveBeenCalledTimes(2)
    expect(calls.indexOf('rotateEnabled:true')).toBeGreaterThan(calls.indexOf('setAttrs'))
    expect(calls.indexOf('rotateEnabled:false')).toBeLessThan(calls.lastIndexOf('setAttrs'))
  })
})

describe('RotateTool transformer selection', () => {
  it('does not attach a single-rack rotate handle when activated with multiple racks selected', () => {
    const { manager, nodesSpy, afterCommand } = createInteractionHarness(['rack-1', 'rack-2'])

    nodesSpy.mockClear()
    manager.switchTool('rotate')
    manager.transformer.fire('transformstart')
    manager.transformer.fire('transformend')

    expect(lastTransformerAssignment(nodesSpy)).toEqual([])
    expect(afterCommand).not.toHaveBeenCalled()
  })

  it('keeps the rotate handle detached when select-all refreshes a multi-selection', () => {
    const { manager, nodesSpy } = createInteractionHarness()
    manager.switchTool('rotate')
    nodesSpy.mockClear()

    manager.selectAll()

    expect(lastTransformerAssignment(nodesSpy)).toEqual([])
  })

  it('keeps the rotate handle detached after a general refresh of a multi-selection', () => {
    const { manager, nodesSpy, store } = createInteractionHarness()
    manager.switchTool('rotate')
    store.setSelection(['rack-1', 'rack-2'])
    nodesSpy.mockClear()

    manager.refreshTransformer()

    expect(lastTransformerAssignment(nodesSpy)).toEqual([])
  })

  it('still attaches the rotate handle for exactly one existing rack', () => {
    const { manager, nodesSpy, rackNodes, store } = createInteractionHarness()
    manager.switchTool('rotate')
    store.setSelection(['rack-1'])
    nodesSpy.mockClear()

    manager.refreshTransformer()

    expect(lastTransformerAssignment(nodesSpy)).toEqual([rackNodes.get('rack-1')])
  })

  it.each([
    { selectionIds: [] },
    { selectionIds: ['missing-rack'] },
  ])(
    'keeps the rotate handle detached when the refreshed selection is $selectionIds',
    ({ selectionIds }) => {
      const { manager, nodesSpy, store } = createInteractionHarness()
      manager.switchTool('rotate')
      store.setSelection(selectionIds)
      nodesSpy.mockClear()

      manager.refreshTransformer()

      expect(lastTransformerAssignment(nodesSpy)).toEqual([])
    },
  )
})
