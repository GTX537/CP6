import http from '../http'
import type { DeptTreeNode, DeptForm } from '@/types/sys/dept'

// 后端 DeptController 返回 {code,message,data} 信封；http 拦截器已返回 body，业务错误自动 toast。
export const deptApi = {
  async tree(): Promise<DeptTreeNode[]> {
    const res: any = await http.get('/pub/dept/tree')
    return res.data ?? []
  },
  create(data: DeptForm) {
    return http.post('/pub/dept', data)
  },
  update(id: string, data: DeptForm) {
    return http.put(`/pub/dept/${id}`, data)
  },
  remove(id: string) {
    return http.delete(`/pub/dept/${id}`)
  },
  move(id: string, newParentId: string | null) {
    return http.post(`/pub/dept/${id}/move`, { newParentId })
  },
  setLeader(id: string, leaderId: string | null) {
    return http.put(`/pub/dept/${id}/leader`, { leaderId })
  },
}
