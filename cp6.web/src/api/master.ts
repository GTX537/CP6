import http from './http'
import type {
  MasterBase,
  MasterStaff,
  MasterGenericCode,
  ApiResult,
} from '@/types/estimateCalc'

// 主数据 API
// 对应后端 CP6.WebApi/Controllers/MasterDataController.cs
export const masterApi = {
  /** 拠点列表 */
  getBases() {
    return http.get<any, ApiResult<MasterBase[]>>('/master/bases')
  },

  /** 担当者（可按拠点过滤） */
  getStaffs(baseCd?: string) {
    return http.get<any, ApiResult<MasterStaff[]>>('/master/staffs', {
      params: baseCd ? { baseCd } : undefined,
    })
  },

  /** 通用码（按 GroupCode 过滤） */
  getGenericCodes(groupCode: string) {
    return http.get<any, ApiResult<MasterGenericCode[]>>('/master/generic-codes', {
      params: { groupCode },
    })
  },
}
