import http from '../http'

/** 年历某日例外行（后端 WorkCalendarDay 投影）。isWorkday=true 补班 / false 假日。 */
export interface WorkCalendarDay {
  date: string        // ISO yyyy-MM-ddT00:00:00
  isWorkday: boolean
  note?: string | null
}

/** GET /oa/work-calendar?year= 的响应体。isEmpty 驱动前端空态导入引导。 */
export interface WorkCalendarYear {
  year: number
  isEmpty: boolean
  items: WorkCalendarDay[]
}

const unwrap = (res: any) => res?.data ?? res

export const workCalendarApi = {
  /** 列某年例外 + 空态标志（一次往返）。 */
  list: async (year: number): Promise<WorkCalendarYear> =>
    unwrap(await http.get('/oa/work-calendar', { params: { year } })),

  /** 反转某日（补班/假日/备注 upsert）。date=yyyy-MM-dd。 */
  toggle: (date: string, isWorkday: boolean, note?: string | null) =>
    http.post('/oa/work-calendar/toggle', { date, isWorkday, note }),

  /** 回归默认态（删例外行）。date=yyyy-MM-dd。 */
  clear: (date: string) => http.delete(`/oa/work-calendar/${date}`),

  /** 空态导入日本法定假日到当前租户（幂等）。返回本次新增行数。 */
  importJp: async (): Promise<{ inserted: number }> =>
    unwrap(await http.post('/oa/work-calendar/import-jp')),
}
