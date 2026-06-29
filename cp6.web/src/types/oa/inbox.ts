export interface PendingItem {
  taskId: string
  instanceId: string
  tokenId?: string
  flowKey: string
  flowName?: string
  nodeId: string
  nodeName?: string
  starterId: string
  starterName: string
  bizType?: string
  bizId?: string
  isRead: boolean
  sentAt: string
  stageIndex?: number
  stageRound?: number
  stageName?: string
  stageCode?: string
  canSendBackPrevStage?: boolean
}

export interface CcItem {
  ccId: string
  instanceId: string
  flowKey: string
  flowName?: string
  atNodeId?: string
  starterId: string
  starterName: string
  isRead: boolean
  createDate: string
}

export interface RunningItem {
  instanceId: string
  flowKey: string
  flowName?: string
  currentNode: string
  status: number
  currentHandlers: string[]
  createDate: string
}

export interface DoneItem {
  instanceId: string
  flowKey: string
  flowName?: string
  starterId: string
  starterName: string
  formToStatus: number
  doneAt: string
  instanceStatus: number
}

export interface TrendPoint {
  date: string
  count: number
}

export interface InboxStats {
  pendingCount: number
  runningCount: number
  doneThisMonth: number
  rejectedBackToMe: number
  trend: TrendPoint[]
  recentPending: PendingItem[]
}

export interface TimelineRow {
  stepSeq: number
  tokenId?: string
  nodeId: string
  nodeName?: string
  expectedHandlerId: string
  expectedHandlerName: string
  actualHandlerId?: string
  actualHandlerName?: string
  onBehalfOfId?: string
  onBehalfOfName?: string
  status: number
  comment?: string
  sentAt: string
  handledAt?: string
  stageIndex?: number | null
  stageRound?: number | null
}

export interface SnapshotRow {
  stepSeq: number
  nodeId: string
  dataJson: string
}

export interface CcRow {
  recipientId: string
  recipientName: string
  atNodeId?: string
  isRead: boolean
}

export interface ForecastStep {
  nodeId: string
  nodeName?: string
  type: string
  approvers: string[]
  resolved: boolean
  note?: string
}

export interface InboxDetail {
  instance: any
  flowName?: string
  formKey?: string
  formSchemaJson?: string
  currentDataJson: string
  timeline: TimelineRow[]
  snapshots: SnapshotRow[]
  forecast: ForecastStep[]
  cc: CcRow[]
}

export interface BatchResultItem {
  taskId: string
  ok: boolean
  error?: string
}

export interface FlowAdminItem {
  flowKey: string
  flowName: string
  formKey: string
  version: number
  enable: boolean
}
