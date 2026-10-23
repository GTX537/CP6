/** Quantities and reference arithmetic remain exact decimal strings. Only chart geometry is approximate. */
export function observationText(value: string | null | undefined): string {
  if (value == null) return '—'
  const [integer, fraction] = value.split('.')
  return integer!.replace(/\B(?=(\d{3})+(?!\d))/g, ',') + (fraction == null ? '' : `.${fraction}`)
}
export function observationPercent(part?: string, whole?: string): number | null {
  if (part == null || whole == null) return null
  const [pi = '0', pf = ''] = part.split('.'), [wi = '0', wf = ''] = whole.split('.')
  const scale = Math.max(pf.length, wf.length)
  const p = BigInt(pi + pf.padEnd(scale, '0')), w = BigInt(wi + wf.padEnd(scale, '0'))
  return w > 0n ? Number(p * 10000n / w) / 100 : 0
}
export function observationToday() {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}
export const inventoryObservationVersion = 'INVENTORY-OBSERVATION-OWNER/1'
