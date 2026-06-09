import http from '../http'
import type { ApiResult, PlanAchievementQuery, PlanAchievementSummary } from '@/types/mes/planAchievement'

export const planAchievementApi = {
  summary(query: PlanAchievementQuery) {
    return http.post<any, ApiResult<PlanAchievementSummary>>('/mes/plan-achievement/summary', query)
  },

  exportCsv(query: PlanAchievementQuery) {
    return http.post<Blob>('/mes/plan-achievement/export-csv', query, { responseType: 'blob' })
  },
}
