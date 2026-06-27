// Pure functions for D7 reverse-modeling bind pairing (ch01 §8.2)
import type { UnplacedLocationDto } from '@/types/space/scene'

export type { UnplacedLocationDto }

// ── Slot key ──────────────────────────────────────────────────────────────────

export interface SlotKey {
  col: number
  level: number
  depth: number
}

// ── Pair row (one per rack slot) ──────────────────────────────────────────────

export interface PairRow {
  col: number
  level: number
  depth: number
  /** null = slot unmatched (has-geometry-no-code → yellow) */
  locationId: string | null
  locationCode: string | null
}

// ── Mismatch summary ──────────────────────────────────────────────────────────

export type MismatchType = 'exact' | 'slotNoCode' | 'codeNoSlot'

export interface MismatchSummary {
  type: MismatchType
  slotCount: number
  codeCount: number
  /** Absolute difference between slot count and code count */
  diff: number
}

// ── Pure functions ────────────────────────────────────────────────────────────

/**
 * Enumerate all slots of a rack in col→level→depth order.
 * Mirrors the genRack loop so autoPair aligns with generation order.
 */
export function enumerateSlots(cols: number, levels: number, depthCount: number): SlotKey[] {
  const slots: SlotKey[] = []
  for (let c = 1; c <= cols; c++) {
    for (let l = 1; l <= levels; l++) {
      for (let d = 1; d <= depthCount; d++) {
        slots.push({ col: c, level: l, depth: d })
      }
    }
  }
  return slots
}

/**
 * Auto-pair rack slots with unplaced codes by sequential position.
 *
 * - slots[i] ← codes[i]  (exact match)
 * - slots beyond codes list  → locationId=null  (has-geometry-no-code, yellow)
 * - codes beyond slots list  → orphans (has-code-no-geometry, red), tracked
 *   separately via computeOrphans()
 */
export function autoPair(slots: SlotKey[], codes: UnplacedLocationDto[]): PairRow[] {
  return slots.map((s, i) => {
    const code = i < codes.length ? codes[i]! : null
    return {
      col: s.col,
      level: s.level,
      depth: s.depth,
      locationId: code?.id ?? null,
      locationCode: code?.locationCode ?? null,
    }
  })
}

/**
 * Compute mismatch summary between slot count and available code count.
 */
export function computeMismatch(slotCount: number, codeCount: number): MismatchSummary {
  if (slotCount === codeCount) {
    return { type: 'exact', slotCount, codeCount, diff: 0 }
  }
  if (slotCount > codeCount) {
    return { type: 'slotNoCode', slotCount, codeCount, diff: slotCount - codeCount }
  }
  return { type: 'codeNoSlot', slotCount, codeCount, diff: codeCount - slotCount }
}

/**
 * Return codes that are not assigned to any slot in the current pairs
 * (has-code-no-geometry, shown in red).
 */
export function computeOrphans(
  allCodes: UnplacedLocationDto[],
  pairs: PairRow[],
): UnplacedLocationDto[] {
  const assigned = new Set(
    pairs.map(p => p.locationId).filter((id): id is string => id !== null),
  )
  return allCodes.filter(c => !assigned.has(c.id))
}
