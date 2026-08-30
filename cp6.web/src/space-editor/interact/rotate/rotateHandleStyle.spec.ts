import { describe, expect, it, vi } from 'vitest'
import Konva from 'konva'
import {
  ACTIVE_ROTATE_HANDLE_STYLE,
  INACTIVE_ROTATE_HANDLE_STYLE,
  setRotateHandleVisibility,
} from './rotateHandleStyle'

describe('rotate handle style', () => {
  it('applies the high-visibility active style', () => {
    const transformer = { setAttrs: vi.fn() } as unknown as Konva.Transformer

    setRotateHandleVisibility(transformer, true)

    expect(ACTIVE_ROTATE_HANDLE_STYLE.anchorSize).toBeGreaterThanOrEqual(18)
    expect(transformer.setAttrs).toHaveBeenCalledWith(ACTIVE_ROTATE_HANDLE_STYLE)
  })

  it('restores the inactive style', () => {
    const transformer = { setAttrs: vi.fn() } as unknown as Konva.Transformer

    setRotateHandleVisibility(transformer, false)

    expect(transformer.setAttrs).toHaveBeenCalledWith(INACTIVE_ROTATE_HANDLE_STYLE)
  })
})
