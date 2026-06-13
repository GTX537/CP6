import http from '../http'

// 附件接口。后端 {code,message,data} 信封；http 拦截器已返回 body。
export const attachmentApi = {
  async list(bizType: string, bizId: string): Promise<any[]> {
    const res: any = await http.get('/pub/attachment/list', { params: { bizType, bizId } })
    return res.data ?? []
  },

  async upload(file: File, bizType: string, bizId?: string, draftToken?: string): Promise<any> {
    const fd = new FormData()
    fd.append('file', file)
    fd.append('bizType', bizType)
    if (bizId) fd.append('bizId', bizId)
    if (draftToken) fd.append('draftToken', draftToken)
    return http.post('/pub/attachment/upload', fd)
  },

  remove(id: string) {
    return http.delete(`/pub/attachment/${id}`)
  },

  rebind(draftToken: string, bizId: string) {
    return http.post('/pub/attachment/rebind', { draftToken, bizId })
  },

  // 带 token 的 blob 下载（直链会丢 Authorization 头）
  async download(id: string, fileName: string) {
    const blob: any = await http.get(`/pub/attachment/${id}/download`, { responseType: 'blob' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = fileName
    a.click()
    URL.revokeObjectURL(url)
  },

  // 预览：返回 blob 对象 URL（图片/PDF 内联），调用方用完应 revoke
  async previewObjectUrl(id: string): Promise<string> {
    const blob: any = await http.get(`/pub/attachment/${id}/preview`, { responseType: 'blob' })
    return URL.createObjectURL(blob)
  },
}
