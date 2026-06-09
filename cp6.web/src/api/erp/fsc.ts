import http from '../http'
import type {
  ApiResult, FscChecklistQueryDto, FscChecklistItemDto,
  FscIssueRequestDto, FscIssueResultDto, FscFormat,
} from '@/types/erp/fsc'

// MSBBPA100 — FSC 製品化チェックシート API
export const fscApi = {
  /** 多条件検索 */
  search(query: FscChecklistQueryDto) {
    return http.get<any, ApiResult<{ rows: FscChecklistItemDto[]; total: number }>>(
      '/fsc-checklists/list',
      { params: query, paramsSerializer: { indexes: null } },
    )
  },

  /** 出力フォーマット一覧 */
  getFormats() {
    return http.get<any, ApiResult<FscFormat[]>>('/fsc-checklists/formats')
  },

  /** チェックシート発行 */
  issue(req: FscIssueRequestDto) {
    return http.post<any, ApiResult<FscIssueResultDto>>('/fsc-checklists/issue', req)
  },

  /** 発行済 Excel ダウンロード（ブラウザで直接開く） */
  downloadUrl(fscManagementNo: string): string {
    return `/api/fsc-checklists/download/${encodeURIComponent(fscManagementNo)}`
  },
}
