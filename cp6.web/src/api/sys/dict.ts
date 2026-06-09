import http from '../http'

// 字典类型 API
export const dictTypeApi = {
  getList(params: { page: number; pageSize: number; keyword?: string }) {
    return http.get('/dict/types', { params })
  },
  add(data: any) {
    return http.post('/dict/types', data)
  },
  update(data: any) {
    return http.put('/dict/types', data)
  },
  del(ids: number[]) {
    return http.delete('/dict/types', { data: ids })
  }
}

// 字典数据 API
export const dictDataApi = {
  getList(params: { typeCode: string; page: number; pageSize: number; keyword?: string }) {
    return http.get('/dict/data', { params })
  },
  add(data: any) {
    return http.post('/dict/data', data)
  },
  update(data: any) {
    return http.put('/dict/data', data)
  },
  del(ids: number[]) {
    return http.delete('/dict/data', { data: ids })
  }
}

// 获取字典选项（给下拉框用）
export function getDictOptions(typeCode: string) {
  return http.get(`/dict/options/${typeCode}`)
}
