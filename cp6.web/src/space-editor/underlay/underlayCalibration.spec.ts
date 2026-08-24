import { describe, expect, it } from 'vitest'
import {
  calculateUnderlayCalibration,
  deriveSecondCalibrationWorldPoint,
} from './underlayCalibration'

describe('calculateUnderlayCalibration', () => {
  it('calculates scale and origin in the millimeter Y-up frame', () => {
    const result = calculateUnderlayCalibration({
      pixelWidth: 1000,
      pixelHeight: 500,
      point1: { pixel: { x: 0, y: 500 }, world: { x: 1000, y: 2000 } },
      point2: { pixel: { x: 100, y: 500 }, world: { x: 2000, y: 2000 } },
      validationPoint: {
        pixel: { x: 0, y: 400 },
        world: { x: 1000, y: 3000 },
      },
    })

    expect(result).toEqual({
      millimetersPerPixel: 10,
      offsetX: 1000,
      offsetY: 2000,
      rotationZ: 0,
      validationErrorMillimeters: 0,
      errorThresholdMillimeters: 50,
    })
  })

  it('calculates a positive 90 degree world rotation', () => {
    const result = calculateUnderlayCalibration({
      pixelWidth: 1000,
      pixelHeight: 500,
      point1: { pixel: { x: 0, y: 500 }, world: { x: 1000, y: 2000 } },
      point2: { pixel: { x: 100, y: 500 }, world: { x: 1000, y: 3000 } },
      validationPoint: {
        pixel: { x: 0, y: 400 },
        world: { x: 0, y: 2000 },
      },
    })

    expect(result.rotationZ).toBe(90)
    expect(result.validationErrorMillimeters).toBe(0)
  })

  it('reports third point error before the server applies its threshold', () => {
    const result = calculateUnderlayCalibration({
      pixelWidth: 1000,
      pixelHeight: 500,
      point1: { pixel: { x: 0, y: 500 }, world: { x: 1000, y: 2000 } },
      point2: { pixel: { x: 100, y: 500 }, world: { x: 2000, y: 2000 } },
      validationPoint: {
        pixel: { x: 0, y: 400 },
        world: { x: 1100, y: 3000 },
      },
    })

    expect(result.validationErrorMillimeters).toBe(100)
    expect(result.errorThresholdMillimeters).toBe(50)
  })

  it('uses the frozen relative tolerance for long control distances', () => {
    const result = calculateUnderlayCalibration({
      pixelWidth: 1000,
      pixelHeight: 500,
      point1: { pixel: { x: 0, y: 500 }, world: { x: 0, y: 0 } },
      point2: { pixel: { x: 100, y: 500 }, world: { x: 100_000, y: 0 } },
      validationPoint: {
        pixel: { x: 0, y: 400 },
        world: { x: 0, y: 100_000 },
      },
    })

    expect(result.errorThresholdMillimeters).toBe(200)
  })

  it('rejects a validation point on the control line', () => {
    expect(() =>
      calculateUnderlayCalibration({
        pixelWidth: 1000,
        pixelHeight: 500,
        point1: { pixel: { x: 0, y: 500 }, world: { x: 1000, y: 2000 } },
        point2: { pixel: { x: 100, y: 500 }, world: { x: 2000, y: 2000 } },
        validationPoint: {
          pixel: { x: 50, y: 500 },
          world: { x: 1500, y: 2000 },
        },
      }),
    ).toThrow('Validation point')
  })
})

describe('deriveSecondCalibrationWorldPoint', () => {
  it('uses the selected first point as the world origin and applies rotation', () => {
    expect(deriveSecondCalibrationWorldPoint({
      point1Pixel: { x: 0, y: 500 },
      point2Pixel: { x: 100, y: 500 },
      originWorld: { x: 1000, y: 2000 },
      distanceMillimeters: 10_000,
      rotationZ: 90,
    })).toEqual({ x: 1000, y: 12_000 })
  })

  it('rotates relative to the selected pixel direction', () => {
    expect(deriveSecondCalibrationWorldPoint({
      point1Pixel: { x: 0, y: 500 },
      point2Pixel: { x: 0, y: 400 },
      originWorld: { x: 1000, y: 2000 },
      distanceMillimeters: 500,
      rotationZ: 90,
    })).toEqual({ x: 500, y: 2000 })
  })

  it('rejects invalid real distance and coincident pixel points', () => {
    expect(() => deriveSecondCalibrationWorldPoint({
      point1Pixel: { x: 0, y: 0 },
      point2Pixel: { x: 20, y: 0 },
      originWorld: { x: 0, y: 0 },
      distanceMillimeters: 0,
      rotationZ: 0,
    })).toThrow('distanceMillimeters')

    expect(() => deriveSecondCalibrationWorldPoint({
      point1Pixel: { x: 0, y: 0 },
      point2Pixel: { x: 5, y: 0 },
      originWorld: { x: 0, y: 0 },
      distanceMillimeters: 1000,
      rotationZ: 0,
    })).toThrow('at least 10 pixels')
  })
})
