import http from '@/api/http'

export interface SubjectExportManifest {
  version: string
  tenantId: string
  subjectId: string
  state: 'PARTIAL'
  complete: false
  directoryCoverage: string
  consistency: string
  sources: Array<{
    owner: string; source: string; status: 'INCLUDED' | 'EXCLUDED_WITH_REASON' | 'UNKNOWN_SCOPE'
    reason: string; selector: string; recordCount: number | null
    readStartedAtUtc: string | null; readCompletedAtUtc: string | null; versionBasis: string
    fields: Array<{ name: string; status: string; reason: string }>
  }>
}

// 多租户合规 #5（块③）GDPR 双粒度导出 / 被遗忘权擦除 API。
// 导出走 responseType:'blob' 取附件流 → 触发浏览器下载；擦除须 confirm=true 二次确认。
// http 拦截器返回 response.data，故 blob 调用运行期即得到 Blob 本体。

/** 触发浏览器下载一个 Blob。 */
function triggerDownload(blob: Blob, filename: string) {
  const url = window.URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = filename
  document.body.appendChild(a)
  a.click()
  document.body.removeChild(a)
  window.URL.revokeObjectURL(url)
}

const today = () => new Date().toISOString().slice(0, 10).replace(/-/g, '')

export const gdprApi = {
  async exportTenant(tenantId: string): Promise<void> {
    const blob = (await http.get(`/platform/gdpr/export/tenant/${tenantId}`, {
      responseType: 'blob'
    })) as unknown as Blob
    triggerDownload(blob, `tenant-${tenantId}-${today()}.json`)
  },
  async exportSubject(userId: string): Promise<SubjectExportManifest> {
    const blob = (await http.get(`/platform/gdpr/export/subject/${userId}`, {
      responseType: 'blob'
    })) as unknown as Blob
    const { manifest } = JSON.parse(await blob.text()) as { manifest: SubjectExportManifest }
    triggerDownload(blob, `subject-${userId}-${today()}.json`)
    return manifest
  },
  eraseSubject(userId: string): Promise<unknown> {
    return http.delete(`/platform/gdpr/erase/subject/${userId}`, {
      params: { confirm: true }
    }) as unknown as Promise<unknown>
  },
  eraseTenant(tenantId: string, mode: 'anonymize' | 'purge'): Promise<unknown> {
    return http.delete(`/platform/gdpr/erase/tenant/${tenantId}`, {
      params: { mode, confirm: true }
    }) as unknown as Promise<unknown>
  }
}
