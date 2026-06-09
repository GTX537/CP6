import http from '../http'
import type {
  MasterBase,
  MasterStaff,
  MasterGenericCode,
  ApiResult,
} from '@/types/erp/estimateCalc'

/** Master Popup レスポンス枠（MSBBPACOM 共通） */
export interface MasterLookupResult<T> {
  rows: T[]
  total: number
}

/** 得意先ルックアップ行 */
export interface CustomerLookupItem {
  customerCd: string
  customerName?: string | null
  usageCount: number
}

/** 製品マスタルックアップ行 */
export interface ProductLookupItem {
  productCd: string
  setProductName?: string | null
  customerCd?: string | null
  customerName?: string | null
  customerItemName1?: string | null
  customerItemName2?: string | null
  status: number
  modifyDate?: string | null
}

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

  /** 得意先ルックアップ（Master Popup 共通） */
  lookupCustomer(params: { keyword?: string; page?: number; pageSize?: number }) {
    return http.get<any, ApiResult<MasterLookupResult<CustomerLookupItem>>>(
      '/master/lookup/customer',
      { params }
    )
  },

  /** 製品マスタルックアップ（セット品 / 既存製品 共通） */
  lookupProduct(params: {
    keyword?: string
    customerCd?: string
    onlyApproved?: boolean
    page?: number
    pageSize?: number
  }) {
    return http.get<any, ApiResult<MasterLookupResult<ProductLookupItem>>>(
      '/master/lookup/product',
      { params }
    )
  },
}
