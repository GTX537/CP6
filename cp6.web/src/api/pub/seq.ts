import http from '../http'

// 采番规则 CRUD（VolTable 通用接口）；后端返回 {rows,total}/实体，无信封。
export const seqApi = {
  getList(params: { page: number; pageSize: number; keyword?: string }) {
    return http.get('/pub/seq', { params })
  },
  add(data: any) {
    return http.post('/pub/seq', data)
  },
  update(data: any) {
    return http.put('/pub/seq', data)
  },
  del(ids: string[]) {
    return http.delete('/pub/seq', { data: ids })
  },
  async preview(bizKey: string): Promise<string> {
    const res: any = await http.get(`/pub/seq/preview/${bizKey}`)
    return res.sample ?? ''
  },
}
