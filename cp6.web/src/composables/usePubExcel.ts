import http from '@/api/http'

// 带 token 的 blob 下载（直链会丢 Authorization）
async function downloadBlob(url: string, fileName: string, params?: any) {
  const blob: any = await http.get(url, { params, responseType: 'blob' })
  const objUrl = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = objUrl
  a.download = fileName
  a.click()
  URL.revokeObjectURL(objUrl)
}

/**
 * PUB 章07 导入导出组合式。
 * 业务页面：const { exportExcel, downloadTemplate } = usePubExcel()
 */
export function usePubExcel() {
  /** 导出：按当前查询条件请求下载 */
  function exportExcel(url: string, params: any, fileName = '导出.xlsx') {
    return downloadBlob(url, fileName, params)
  }
  /** 下载导入模板 */
  function downloadTemplate(url: string, fileName = '导入模板.xlsx') {
    return downloadBlob(url, fileName)
  }
  return { exportExcel, downloadTemplate }
}
