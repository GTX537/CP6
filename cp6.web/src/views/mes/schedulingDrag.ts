import type { SchedInterval } from '@/api/mes/scheduling'
// The server contract is offset-qualified UTC instants at microsecond precision.
export function shiftSchedulingInterval(value: SchedInterval, milliseconds: number): SchedInterval {
  const shift = (instant: string) => {
    const micros = (/\.(\d+)(?:Z|[+-]\d{2}:\d{2})$/.exec(instant)?.[1] ?? '').padEnd(6, '0').slice(3)
    return new Date(Date.parse(instant) + milliseconds).toISOString().replace('Z', micros + 'Z')
  }
  return { start: shift(value.start), end: shift(value.end) }
}
