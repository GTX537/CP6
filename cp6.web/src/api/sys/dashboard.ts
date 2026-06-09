import http from '../http'

export const dashboardApi = {
  getSummary() {
    return http.get('/dashboard')
  }
}
