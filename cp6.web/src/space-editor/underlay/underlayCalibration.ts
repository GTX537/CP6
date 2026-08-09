export interface UnderlayPixelPoint {
  x: number
  y: number
}

export interface UnderlayWorldPoint {
  x: number
  y: number
}

export interface UnderlayCalibrationSample {
  pixel: UnderlayPixelPoint
  world: UnderlayWorldPoint
}

export interface UnderlayCalibrationInput {
  pixelWidth: number
  pixelHeight: number
  point1: UnderlayCalibrationSample
  point2: UnderlayCalibrationSample
  validationPoint: UnderlayCalibrationSample
}

export interface UnderlayCalibrationPreview {
  millimetersPerPixel: number
  offsetX: number
  offsetY: number
  rotationZ: number
  validationErrorMillimeters: number
}

export function calculateUnderlayCalibration(
  input: UnderlayCalibrationInput,
): UnderlayCalibrationPreview {
  requirePositive(input.pixelWidth, 'pixelWidth')
  requirePositive(input.pixelHeight, 'pixelHeight')
  for (const [name, sample] of [
    ['point1', input.point1],
    ['point2', input.point2],
    ['validationPoint', input.validationPoint],
  ] as const) {
    requirePoint(sample.pixel, input.pixelWidth, input.pixelHeight, name)
    requireFinite(sample.world.x, `${name}.world.x`)
    requireFinite(sample.world.y, `${name}.world.y`)
  }

  const pixelDx = input.point2.pixel.x - input.point1.pixel.x
  const pixelDy = input.point1.pixel.y - input.point2.pixel.y
  const pixelDistance = Math.hypot(pixelDx, pixelDy)
  if (pixelDistance < 10) {
    throw new Error('Calibration points must be at least 10 pixels apart')
  }
  const worldDx = input.point2.world.x - input.point1.world.x
  const worldDy = input.point2.world.y - input.point1.world.y
  const worldDistance = Math.hypot(worldDx, worldDy)
  if (worldDistance < 1) {
    throw new Error('Calibration world points must be distinct')
  }

  const validationDistance = perpendicularDistance(
    input.point1.pixel,
    input.point2.pixel,
    input.validationPoint.pixel,
  )
  if (validationDistance < Math.max(5, pixelDistance * 0.01)) {
    throw new Error('Validation point must be separated from the control line')
  }

  const millimetersPerPixel = round(worldDistance / pixelDistance, 8)
  const rotationRadians =
    Math.atan2(worldDy, worldDx) - Math.atan2(pixelDy, pixelDx)
  const rotationZ = round(normalizeDegrees(rotationRadians * 180 / Math.PI), 4)
  const radians = rotationZ * Math.PI / 180
  const cosine = Math.cos(radians)
  const sine = Math.sin(radians)
  const localX = input.point1.pixel.x
  const localY = input.pixelHeight - input.point1.pixel.y
  const offsetX = Math.round(
    input.point1.world.x -
      millimetersPerPixel * (cosine * localX - sine * localY),
  )
  const offsetY = Math.round(
    input.point1.world.y -
      millimetersPerPixel * (sine * localX + cosine * localY),
  )
  const predicted = transform(
    input.validationPoint.pixel,
    input.pixelHeight,
    millimetersPerPixel,
    rotationZ,
    offsetX,
    offsetY,
  )

  return {
    millimetersPerPixel,
    offsetX,
    offsetY,
    rotationZ,
    validationErrorMillimeters: round(
      Math.hypot(
        predicted.x - input.validationPoint.world.x,
        predicted.y - input.validationPoint.world.y,
      ),
      4,
    ),
  }
}

function transform(
  pixel: UnderlayPixelPoint,
  pixelHeight: number,
  scale: number,
  rotationZ: number,
  offsetX: number,
  offsetY: number,
): UnderlayWorldPoint {
  const radians = rotationZ * Math.PI / 180
  const cosine = Math.cos(radians)
  const sine = Math.sin(radians)
  const localX = pixel.x
  const localY = pixelHeight - pixel.y
  return {
    x: offsetX + scale * (cosine * localX - sine * localY),
    y: offsetY + scale * (sine * localX + cosine * localY),
  }
}

function perpendicularDistance(
  start: UnderlayPixelPoint,
  end: UnderlayPixelPoint,
  point: UnderlayPixelPoint,
): number {
  const dx = end.x - start.x
  const dy = end.y - start.y
  return Math.abs(
    dy * point.x -
      dx * point.y +
      end.x * start.y -
      end.y * start.x,
  ) / Math.hypot(dx, dy)
}

function requirePoint(
  point: UnderlayPixelPoint,
  width: number,
  height: number,
  field: string,
): void {
  requireFinite(point.x, `${field}.x`)
  requireFinite(point.y, `${field}.y`)
  if (point.x < 0 || point.x > width || point.y < 0 || point.y > height) {
    throw new Error(`${field} is outside the underlay raster`)
  }
}

function requirePositive(value: number, field: string): void {
  requireFinite(value, field)
  if (value <= 0) throw new Error(`${field} must be positive`)
}

function requireFinite(value: number, field: string): void {
  if (!Number.isFinite(value)) throw new Error(`${field} must be finite`)
}

function normalizeDegrees(value: number): number {
  const normalized = value % 360
  return normalized < 0 ? normalized + 360 : normalized
}

function round(value: number, decimals: number): number {
  const factor = 10 ** decimals
  return Math.round((value + Number.EPSILON) * factor) / factor
}
