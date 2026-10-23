export const slottingVersion = 'SLOTTING-OWNER/1'
export interface SlottingQuery { warehouseCd?: string; status?: number; page: number; pageSize: number }
export interface SlottingObservation {
  version: typeof slottingVersion; knowledge: 'UNKNOWN'; statisticsMethod: 'LEGACY_ROW_COUNT'
  id: string; slottingPlanNo: string; warehouseCd: string; analysisDays: number; legacyStatus: number
  analyzedAt: string | null; createDate: string | null
  snapshotReadable: boolean; snapshotState: 'AUTHORIZED_LEGACY_SNAPSHOT' | 'SOURCE_DISCLOSURE_UNAVAILABLE'
  txnSampleCount: number | null; recommendationCount: number | null; recommendationsJson: string | null
  jsonState: 'JSON_ARRAY' | 'LEGACY_PARSE_ERROR' | 'NOT_RECORDED' | 'NOT_DISCLOSED'
  approverCdReadable: boolean; remarksReadable: boolean; approverCd: string | null; remarks: string | null
}
export interface SlottingPage {
  version: typeof slottingVersion; coverage: 'AUTHORIZED_LEGACY_OBSERVATION'; writesAvailable: false
  writeBlocker: 'SLOTTING_OWNER_ADOPTION_UNPROVEN'; total: number; page: number; pageSize: number; items: SlottingObservation[]
}
