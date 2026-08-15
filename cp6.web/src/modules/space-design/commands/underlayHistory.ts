import type { ISpaceUnderlayHistoryDto } from '../../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'
import type { UnderlayHistoryEntry } from './editorBatchCommands'

const labels = {
  UnderlaySet: '更新底图',
  UnderlayCalibrate: '标定底图',
} as const

export function buildUnderlayHistoryEntry(
  history: ISpaceUnderlayHistoryDto,
  label?: string,
): UnderlayHistoryEntry {
  if (
    history.schemaVersion !== 1
    || !history.originalCommandBatchId?.trim()
    || !history.historySha256?.match(/^[0-9a-f]{64}$/)
    || !(history.operationType in labels)
  ) {
    throw new Error('Underlay write is missing sealed reversible history')
  }
  const operationType = history.operationType as keyof typeof labels
  return {
    label: label ?? labels[operationType],
    underlayCompensation: {
      originalCommandBatchId: history.originalCommandBatchId,
      historySha256: history.historySha256,
      operationType,
    },
  }
}
