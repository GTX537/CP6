// A2 工艺路线完善 · 主数据前端类型 —— 对应后端 CP6.Entity.DomainModels.Mes.WorkCenter / ProcessCostRate

export interface ApiResp<T> {
  code: number
  message: string
  data: T
}

/** 工作中心主数据（A2 · spec §3.2）。费率与产能挂载点；产能字段=CRP 地基。 */
export interface WorkCenter {
  id?: string
  /** 工作中心CD（业务键，唯一） */
  wgCd: string
  /** 工作中心名称 */
  wgName?: string | null
  /** 日可用产能（h/日） */
  dailyCapacityHours?: number | null
  /** 启用 */
  enable: boolean
}

/** 工序费率（A2 · spec §3.3）。工作中心×生效区间 的 工/费双率（元/h）。 */
export interface ProcessCostRate {
  id?: string
  /** 工作中心CD → WorkCenter.WgCd */
  wgCd: string
  /** 人工费率（元/h） */
  laborRate: number
  /** 制造费率（元/h） */
  overheadRate: number
  /** 生效日 */
  validFrom: string
  /** 失效日（null = 长期） */
  validTo?: string | null
}
