import { describe, expect, it } from 'vitest'
import { screenDragDeltaToWorld } from './elementCanvasDrag'

describe('element canvas drag', () => {
  it('converts the screen delta into integer world millimetres', () => {
    expect(screenDragDeltaToWorld({ x: 12.5, y: -7.5 }, 0.05)).toEqual({
      x: 250,
      y: 150,
    })
  })

  it('preserves the Y-flipped canvas coordinate convention', () => {
    expect(screenDragDeltaToWorld({ x: -20, y: 40 }, 0.1)).toEqual({
      x: -200,
      y: -400,
    })
  })

  it('rejects a non-finite delta or invalid zoom', () => {
    expect(() => screenDragDeltaToWorld({ x: 1, y: 2 }, 0)).toThrow(
      'Canvas drag delta is invalid',
    )
    expect(() => screenDragDeltaToWorld({ x: Number.NaN, y: 2 }, 0.1)).toThrow(
      'Canvas drag delta is invalid',
    )
  })
})
