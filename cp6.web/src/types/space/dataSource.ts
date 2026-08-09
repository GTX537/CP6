export type SpaceDataSourceKind = 'Real' | 'Simulated' | 'Unavailable'

/** Mandatory trust metadata returned with Space runtime data. */
export interface SpaceDataSource {
  kind: SpaceDataSourceKind
  dataSourceId: string
  observedAtUtc: string
  isSimulated: boolean
  isAvailable: boolean
}

export interface SourcedItems<T> {
  items: T[]
  source: SpaceDataSource
}

export function dataSourceLabel(source: SpaceDataSource): string {
  switch (source.kind) {
    case 'Real': return 'REAL'
    case 'Simulated': return 'SIMULATED'
    case 'Unavailable': return 'UNAVAILABLE'
  }
}

export function isUsableDataSource(source: SpaceDataSource): boolean {
  return source.kind !== 'Unavailable' && source.isAvailable
}
