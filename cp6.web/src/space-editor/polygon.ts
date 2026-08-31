export type EditorPoint = [number, number]

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

export function parseEditorPolygon(value: string | undefined): EditorPoint[] {
  if (!value) return []

  let parsed: unknown
  try {
    parsed = JSON.parse(value)
  } catch {
    return []
  }

  const points = Array.isArray(parsed)
    ? parsed
    : isRecord(parsed) && parsed.schemaVersion === 1 && Array.isArray(parsed.points)
      ? parsed.points
      : []

  if (!points.every(point => (
    Array.isArray(point)
    && point.length >= 2
    && typeof point[0] === 'number'
    && Number.isFinite(point[0])
    && typeof point[1] === 'number'
    && Number.isFinite(point[1])
  ))) return []

  return points.map(point => [point[0] as number, point[1] as number])
}
