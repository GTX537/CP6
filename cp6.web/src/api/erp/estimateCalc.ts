import http from '../http'
import type {
  EstimateCalcDto,
  EstimateCalcQuery,
  EstimateCalcListItem,
  EstimateCalcResult,
  ApiResult,
} from '@/types/erp/estimateCalc'

// MSBBPA010 見積計算書 API
// 对应后端 CP6.WebApi/Controllers/EstimateCalcController.cs
export const estimateCalcApi = {
  /** 分页查询 —— 后端返回 { rows, total } */
  getList(params: EstimateCalcQuery) {
    return http.get<any, ApiResult<{ total: number; rows: EstimateCalcListItem[] }>>(
      '/estimate-calcs',
      { params }
    )
  },

  /** 按見積計算書No取明细 */
  getByNo(qtnCalcNo: string, includeDeleted = false) {
    return http.get<any, ApiResult<EstimateCalcDto>>(
      `/estimate-calcs/${encodeURIComponent(qtnCalcNo)}`,
      { params: { includeDeleted } }
    )
  },

  /** 新建 */
  create(data: EstimateCalcDto) {
    return http.post<any, ApiResult<EstimateCalcDto>>('/estimate-calcs', data)
  },

  /** 更新（带乐观锁） */
  update(qtnCalcNo: string, data: EstimateCalcDto) {
    return http.put<any, ApiResult<EstimateCalcDto>>(
      `/estimate-calcs/${encodeURIComponent(qtnCalcNo)}`,
      data
    )
  },

  /** 软删除 */
  remove(qtnCalcNo: string, rowVersion?: string) {
    return http.delete<any, ApiResult<null>>(
      `/estimate-calcs/${encodeURIComponent(qtnCalcNo)}`,
      { data: { rowVersion } }
    )
  },

  /** 流用（复制） */
  copy(qtnCalcNo: string) {
    return http.post<any, ApiResult<EstimateCalcDto>>(
      `/estimate-calcs/${encodeURIComponent(qtnCalcNo)}/copy`
    )
  },

  /** 計算（Phase 3 模拟引擎） */
  calculate(data: EstimateCalcDto) {
    return http.post<any, ApiResult<EstimateCalcResult>>('/estimate-calcs/calculate', data)
  },
}
