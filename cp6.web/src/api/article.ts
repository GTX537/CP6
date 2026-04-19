import http from './http'

// 文章相关 API，对应后端 ArticleController
export const articleApi = {
  // 分页查询
  getList(params: { page: number; pageSize: number; keyword?: string }) {
    return http.get('/article', { params })
  },
  // 根据 ID 查询
  getById(id: string) {
    return http.get(`/article/${id}`)
  },
  // 新增
  add(data: any) {
    return http.post('/article', data)
  },
  // 修改
  update(data: any) {
    return http.put('/article', data)
  },
  // 删除
  del(ids: string[]) {
    return http.delete('/article', { data: ids })
  }
}
