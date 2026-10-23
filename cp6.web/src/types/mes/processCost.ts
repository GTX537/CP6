// MES-MASTER-OWNER/1: exact decimal(21,8) strings and opaque PostgreSQL versions. No Number conversion.

export interface ApiResp<T> {
  code: number
  message: string
  data: T
}

/** 工作中心主数据（A2 · spec §3.2）。费率与产能挂载点；产能字段=CRP 地基。 */
export interface WorkCenter {
  id?: string
  rowVersion?: string
  /** 工作中心CD（业务键，唯一） */
  wgCd: string
  /** 工作中心名称 */
  wgName?: string | null
  /** 日可用产能（h/日） */
  dailyCapacityHours?: string | null
  /** 启用 */
  enable: boolean
}

/** 工序费率（A2 · spec §3.3）。工作中心×生效区间 的 工/费双率（元/h）。 */
export interface ProcessCostRate {
  id?: string
  rowVersion?: string
  /** 工作中心CD → WorkCenter.WgCd */
  wgCd: string
  /** 人工费率（元/h） */
  laborRate?: string | null
  laborRateWritable?: boolean
  /** 制造费率（元/h） */
  overheadRate?: string | null
  overheadRateWritable?: boolean
  /** 生效日 */
  validFrom: string
  /** 失效日（null = 长期） */
  validTo?: string | null
}
