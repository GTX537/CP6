import type { ISpaceExcelCadApplyDto } from '../../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'
import type { ExcelCadHistoryEntry } from './editorBatchCommands'

export function buildExcelCadApplyHistoryEntry(
  response: ISpaceExcelCadApplyDto,
): ExcelCadHistoryEntry {
  const result = response.result
  if (
    response.jobStatus !== 'Succeeded'
    || !response.matchJobId
    || !response.applyJobId
    || result?.schemaVersion !== 2
    || !result.historySha256?.match(/^[0-9a-f]{64}$/)
    || !Number.isInteger(result.historyCommandCount)
    || (result.historyCommandCount ?? 0) < 1
  ) {
    throw new Error('Excel/CAD Apply is missing sealed reversible history')
  }
  return {
    label: `合入 Excel–CAD 匹配（${result.historyCommandCount} 项）`,
    excelCadCompensation: {
      matchJobId: response.matchJobId,
      applyJobId: response.applyJobId,
      historySha256: result.historySha256,
      historyCommandCount: result.historyCommandCount!,
    },
  }
}
