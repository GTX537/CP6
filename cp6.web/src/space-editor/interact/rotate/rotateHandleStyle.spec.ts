import { describe, expect, it, vi } from 'vitest'
import {
  ACTIVE_ROTATE_HANDLE_STYLE,
  INACTIVE_ROTATE_HANDLE_STYLE,
  applyRotateHandleStyle,
} from './rotateHandleStyle'
import Konva from 'konva'
import { RotateTool } from '../tools/RotateTool'

const canvasContext = {
  clearRect: vi.fn(),
  fillRect: vi.fn(),
  fillStyle: '',
  getImageData: vi.fn(() => ({ data: new Uint8ClampedArray(400) })),
}
vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(canvasContext as never)

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
